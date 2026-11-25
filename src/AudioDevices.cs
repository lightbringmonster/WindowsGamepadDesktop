using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Gamepad
{
    internal enum AudioFlow
    {
        Output,
        Input
    }

    internal sealed class AudioEndpoint
    {
        internal readonly string Id, Name;
        internal readonly bool IsDefault;

        internal AudioEndpoint(string id, string name, bool isDefault)
        {
            Id = id;
            Name = name;
            IsDefault = isDefault;
        }
    }

    internal interface IAudioDevices
    {
        AudioEndpoint[] List(AudioFlow flow);
        void SetDefault(AudioFlow flow, string id);
    }

    internal sealed class WindowsAudioDevices : IAudioDevices
    {
        const uint Active = 1;
        const int NotFound = unchecked((int)0x80070490);
        const int ConsoleRole = 0, MultimediaRole = 1;
        static readonly PropertyKey FriendlyName = new PropertyKey {
            Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14
        };

        public AudioEndpoint[] List(AudioFlow flow)
        {
            var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
            IDeviceCollection collection = null;
            try
            {
                string current = DefaultId(enumerator, flow, ConsoleRole);
                enumerator.EnumAudioEndpoints(flow, Active, out collection);
                uint count;
                collection.GetCount(out count);
                var devices = new List<AudioEndpoint>();
                for (uint i = 0; i < count; i++)
                {
                    IDevice device;
                    collection.Item(i, out device);
                    try
                    {
                        string id;
                        device.GetId(out id);
                        devices.Add(new AudioEndpoint(id, DeviceName(device), id == current));
                    }
                    finally
                    {
                        Release(device);
                    }
                }
                return devices.ToArray();
            }
            finally
            {
                Release(collection);
                Release(enumerator);
            }
        }

        public void SetDefault(AudioFlow flow, string id)
        {
            var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
            object policyInstance = null;
            IDevice device = null;
            try
            {
                enumerator.GetDevice(id, out device);
                uint state;
                device.GetState(out state);
                if (state != Active)
                    throw new InvalidOperationException("设备已断开");

                string previous = DefaultId(enumerator, flow, ConsoleRole);
                policyInstance = new AudioPolicy();
                var policy = policyInstance as IAudioPolicy;
                if (policy == null)
                    throw new InvalidOperationException("请在 Windows 声音设置中调整");
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, ConsoleRole));
                int result = policy.SetDefaultEndpoint(id, MultimediaRole);
                if (result < 0 && previous != null && previous != id)
                    policy.SetDefaultEndpoint(previous, ConsoleRole);
                Marshal.ThrowExceptionForHR(result);

                if (DefaultId(enumerator, flow, ConsoleRole) != id || DefaultId(enumerator, flow, MultimediaRole) != id)
                    throw new InvalidOperationException("Windows 未确认设备切换");
            }
            finally
            {
                Release(device);
                Release(policyInstance);
                Release(enumerator);
            }
        }

        static string DefaultId(IDeviceEnumerator enumerator, AudioFlow flow, int role)
        {
            IDevice device;
            int result = enumerator.GetDefaultAudioEndpoint(flow, role, out device);
            if (result == NotFound)
                return null;
            Marshal.ThrowExceptionForHR(result);
            try
            {
                string id;
                device.GetId(out id);
                return id;
            }
            finally
            {
                Release(device);
            }
        }

        static string DeviceName(IDevice device)
        {
            IPropertyStore store;
            device.OpenPropertyStore(0, out store);
            var value = new PropertyValue();
            try
            {
                var key = FriendlyName;
                store.GetValue(ref key, out value);
                return value.Type == 31 && value.Pointer != IntPtr.Zero
                           ? Marshal.PtrToStringUni(value.Pointer)
                           : "未命名设备";
            }
            finally
            {
                PropVariantClear(ref value);
                Release(store);
            }
        }

        static void Release(object instance)
        {
            if (instance != null)
                Marshal.ReleaseComObject(instance);
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropertyValue value);

        [StructLayout(LayoutKind.Sequential)]
        struct PropertyKey
        {
            internal Guid Format;
            internal uint Id;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PropertyValue
        {
            [FieldOffset(0)] internal ushort Type;
            [FieldOffset(8)] internal IntPtr Pointer;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class DeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDeviceEnumerator
        {
            void EnumAudioEndpoints(AudioFlow flow, uint stateMask, out IDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(AudioFlow flow, int role, out IDevice device);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDeviceCollection
        {
            void GetCount(out uint count);
            void Item(uint index, out IDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDevice
        {
            void Activate();
            void OpenPropertyStore(uint mode, out IPropertyStore store);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out uint state);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropertyValue value);
        }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        class AudioPolicy { }

        // Windows' audio policy COM interface is not an SDK contract. The preceding slots are never called.
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioPolicy
        {
            void GetMixFormat();
            void GetDeviceFormat();
            void ResetDeviceFormat();
            void SetDeviceFormat();
            void GetProcessingPeriod();
            void SetProcessingPeriod();
            void GetShareMode();
            void SetShareMode();
            void GetPropertyValue();
            void SetPropertyValue();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        }
    }
}
