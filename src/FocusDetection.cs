using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
namespace Gamepad
{
    internal sealed class FocusInfo
    {
        internal IntPtr Window;
        internal string Id;
        internal bool Editable, Password;
        internal Rectangle Bounds;
    }
    internal sealed class FocusDetection : IDisposable
    {
        readonly object lifetime = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ManualResetEvent stopListener = new ManualResetEvent(false);
        readonly Thread worker;
        volatile bool quit;
        FocusInfo latest;
        int dirty;
        public FocusDetection()
        {
            worker = new Thread(Run) { Name = "Editable focus", IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            var listener = new Thread(Listen) { Name = "UIA focus notifications", IsBackground = true };
            listener.SetApartmentState(ApartmentState.MTA);
            listener.Start();
        }
        public bool Dirty
        {
            get {
                return Interlocked.Exchange(ref dirty, 0) != 0;
            }
        }
        public FocusInfo Take()
        {
            return Interlocked.Exchange(ref latest, null);
        }
        public void Request()
        {
            lock (lifetime)
            {
                if (!quit)
                    wake.Set();
            }
        }
        void Changed(object sender, AutomationFocusChangedEventArgs args)
        {
            Interlocked.Exchange(ref dirty, 1);
        }
        // Registering a global UIA listener can take longer than the entire pad-click window.
        // The sampling worker must be able to inspect the first click immediately.
        void Listen()
        {
            bool registered = false;
            try
            {
                Automation.AddAutomationFocusChangedEventHandler(Changed);
                registered = true;
            }
            catch (COMException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                stopListener.WaitOne();
                if (registered)
                    try
                    {
                        Automation.RemoveAutomationFocusChangedEventHandler(Changed);
                    }
                    catch (Exception)
                    {
                    }
                lock (lifetime) stopListener.Dispose();
            }
        }
        void Run()
        {
            try
            {
                while (!quit)
                {
                    wake.WaitOne();
                    if (quit)
                        break;
                    try
                    {
                        Interlocked.Exchange(ref latest, Inspect());
                    }
                    catch (Exception)
                    {
                        Interlocked.Exchange(ref latest, new FocusInfo());
                    }
                }
            }
            finally
            {
                lock (lifetime) wake.Dispose();
            }
        }
        internal static FocusInfo Inspect()
        {
            var hwnd = Win.GetForegroundWindow();
            var e = AutomationElement.FocusedElement;
            var r = new FocusInfo { Window = hwnd };
            if (e == null || hwnd == IntPtr.Zero)
                return r;
            int pid;
            Win.GetWindowThreadProcessId(hwnd, out pid);
            if (pid != e.Current.ProcessId)
            {
                // Allow hosted child windows only when their native ancestor belongs to this foreground window.
                var parent = e;
                bool related = false;
                for (int i = 0; i < 10 && parent != null; i++, parent = TreeWalker.RawViewWalker.GetParent(parent))
                {
                    int handle = parent.Current.NativeWindowHandle;
                    if (handle != 0 && Win.GetAncestor(new IntPtr(handle), 2) == hwnd)
                    {
                        related = true;
                        break;
                    }
                }
                if (!related)
                    return r;
            }
            var runtime = e.GetRuntimeId();
            if (runtime == null || runtime.Length == 0)
                return r;
            r.Id = e.Current.ProcessId + "/" + String.Join(".", runtime);
            if (!e.Current.IsEnabled || !e.Current.HasKeyboardFocus)
                return r;
            var type = e.Current.ControlType;
            r.Password = e.Current.IsPassword;
            object pattern;
            if (e.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                r.Editable = !((ValuePattern)pattern).Current.IsReadOnly &&
                             (type == ControlType.Edit || type == ControlType.Document ||
                              type == ControlType.ComboBox || type == ControlType.Custom || r.Password);
            else if (r.Password)
                r.Editable = true;
            else if (e.TryGetCurrentPattern(TextPattern.Pattern, out pattern) &&
                     (type == ControlType.Document || type == ControlType.Edit || type == ControlType.Custom))
            {
                object ro =
                    ((TextPattern)pattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                r.Editable = ro is bool && !(bool)ro;
            }
            else
                r.Editable = false;
            if (r.Editable)
            {
                var b = e.Current.BoundingRectangle;
                if (!b.IsEmpty && b.Width > 0 && b.Height > 0 && b.Width < 30000 && b.Height < 30000)
                    r.Bounds =
                        Rectangle.Round(new RectangleF((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height));
            }
            return r;
        }
        public void Dispose()
        {
            lock (lifetime)
            {
                if (quit)
                    return;
                quit = true;
                stopListener.Set();
                wake.Set();
            }
        }
    }
    internal sealed partial class Host
    {
        FocusDetection focus;
        long eligibleUntil, requestAfter, sampleAt, badSince = -1, lastGood;
        string focusedId;
        bool activatedByFocus;
        partial void OnInteraction()
        {
            if (!Config.AutoKeyboard || keyboard.Visible || game || paused)
                return;
            long now = clock.ElapsedMilliseconds;
            eligibleUntil = now + 930;
            requestAfter = now + 85;
            sampleAt = requestAfter;
            if (focus != null)
                focus.Take();
        }
        internal void PhysicalActivity()
        {
            eligibleUntil = 0;
        }
        partial void OnKeyboardClosed()
        {
            eligibleUntil = 0;
            focusedId = null;
            activatedByFocus = false;
            badSince = -1;
            lastGood = clock.ElapsedMilliseconds;
            if (focus != null)
                focus.Take();
        }
        partial void CheckFocus(long now)
        {
            if (game || paused || systemBlocked)
                return;
            bool watch = keyboard.Visible,
                 eligible = eligibleUntil > now && Config.AutoKeyboard && selected != null;
            if (!watch && !eligible)
                return;
            if (focus == null)
                focus = new FocusDetection();
            var sample = focus.Take();
            if (sample != null && now >= requestAfter && sample.Window == Win.GetForegroundWindow())
            {
                if (!watch && eligible && sample.Editable)
                {
                    OpenKeyboard(sample.Bounds, sample.Password ? "English" : Config.StartLayout);
                    focusedId = sample.Id;
                    activatedByFocus = true;
                    eligibleUntil = 0;
                    lastGood = now;
                    badSince = -1;
                }
                else if (watch && sample.Id != null)
                {
                    if (focusedId == null)
                    {
                        focusedId = sample.Id;
                        lastGood = now;
                    }
                    if (sample.Id == focusedId && (!activatedByFocus || sample.Editable))
                    {
                        badSince = -1;
                        lastGood = now;
                    }
                    else if (badSince < 0)
                        badSince = now;
                    else if (now - badSince >= 125)
                        CloseKeyboard();
                }
            }
            if (watch && focusedId != null && now - lastGood > 1700)
                CloseKeyboard();
            if (now >= requestAfter && (now >= sampleAt || (focus.Dirty && now >= sampleAt - 70)))
            {
                focus.Request();
                sampleAt = now + 150;
            }
        }
    }
}