using System;
using System.Drawing;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Gamepad
{
    internal sealed class PopupTheme
    {
        internal readonly Color Surface, Inset, Text, Secondary, Border, Accent, AccentText;
        internal readonly bool Dark;

        PopupTheme(Color surface, Color inset, Color text, Color secondary, Color border, Color accent,
                   Color accentText, bool dark)
        {
            Surface = surface;
            Inset = inset;
            Text = text;
            Secondary = secondary;
            Border = border;
            Accent = accent;
            AccentText = accentText;
            Dark = dark;
        }

        static bool UsesDarkMode()
        {
            const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            try
            {
                return Equals(Registry.GetValue(key, "AppsUseLightTheme", 1), 0);
            }
            catch (SecurityException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static PopupTheme Current()
        {
            if (SystemInformation.HighContrast)
                return new PopupTheme(SystemColors.Window, SystemColors.Window, SystemColors.WindowText,
                                      SystemColors.WindowText, SystemColors.WindowText, SystemColors.Highlight,
                                      SystemColors.HighlightText, false);
            if (UsesDarkMode())
                return new PopupTheme(Color.FromArgb(35, 39, 46), Color.FromArgb(28, 31, 38),
                                      Color.FromArgb(244, 247, 252), Color.FromArgb(195, 206, 222),
                                      Color.FromArgb(83, 99, 125), Color.FromArgb(144, 202, 249),
                                      Color.FromArgb(12, 25, 42), true);

            return new PopupTheme(Color.FromArgb(248, 250, 253), Color.FromArgb(234, 240, 247),
                                  Color.FromArgb(24, 36, 53), Color.FromArgb(55, 72, 96),
                                  Color.FromArgb(186, 201, 222), Color.FromArgb(0, 95, 184), Color.White, false);
        }
    }
}