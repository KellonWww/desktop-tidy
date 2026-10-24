using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    internal sealed class Theme
    {
        internal Color Background, Surface, Header, Stroke, Text, Muted, Accent, AccentText, Hover, Pressed, AccentSoft, AccentLine, StrongStroke;
        internal bool Dark;
        internal bool FollowsSystem;
        internal bool HighContrast;
        internal static Theme Palette(bool dark)
        {
            return new Theme
            {
                Dark = dark,
                Background = dark ? Color.FromArgb(27, 29, 34) : Color.FromArgb(245, 246, 248),
                Surface = dark ? Color.FromArgb(36, 39, 46) : Color.FromArgb(255, 255, 255),
                Header = dark ? Color.FromArgb(48, 52, 61) : Color.FromArgb(238, 240, 244),
                Stroke = dark ? Color.FromArgb(59, 65, 77) : Color.FromArgb(220, 224, 231),
                Text = dark ? Color.FromArgb(231, 234, 240) : Color.FromArgb(37, 42, 52),
                Muted = dark ? Color.FromArgb(162, 170, 184) : Color.FromArgb(101, 109, 123),
                Accent = dark ? Color.FromArgb(155, 185, 255) : Color.FromArgb(54, 95, 211),
                AccentText = dark ? Color.FromArgb(23, 43, 83) : Color.FromArgb(255, 255, 255),
                Hover = dark ? Color.FromArgb(181, 203, 255) : Color.FromArgb(71, 112, 224),
                Pressed = dark ? Color.FromArgb(135, 166, 237) : Color.FromArgb(41, 79, 191),
                AccentSoft = dark ? Color.FromArgb(48, 62, 91) : Color.FromArgb(234, 240, 255),
                AccentLine = dark ? Color.FromArgb(126, 155, 218) : Color.FromArgb(139, 163, 229),
                StrongStroke = dark ? Color.FromArgb(104, 115, 132) : Color.FromArgb(146, 155, 170)
            };
        }
        internal static Theme Read()
        {
            if (SystemInformation.HighContrast) return new Theme
            {
                HighContrast = true,
                Background = SystemColors.Control, Surface = SystemColors.Window, Header = SystemColors.Control,
                Stroke = SystemColors.WindowText, Text = SystemColors.WindowText, Muted = SystemColors.WindowText,
                Accent = SystemColors.Highlight, AccentText = SystemColors.HighlightText,
                Hover = SystemColors.Highlight, Pressed = SystemColors.Highlight,
                AccentSoft = SystemColors.Highlight, AccentLine = SystemColors.Highlight, StrongStroke = SystemColors.WindowText
            };
            bool dark;
            if (TryReadOverride(out dark))
            {
                Theme selected = Palette(dark); selected.FollowsSystem = false; return selected;
            }
            dark = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { }
            Theme system = Palette(dark); system.FollowsSystem = true; return system;
        }
        private static bool TryReadOverride(out bool dark)
        {
            dark = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\DesktopTidy\Preferences"))
                {
                    string value = key == null ? null : key.GetValue("ThemeMode") as string;
                    if (value == "dark") { dark = true; return true; }
                    if (value == "light") { dark = false; return true; }
                }
            }
            catch { }
            return false;
        }
        internal static bool SetOverride(bool? dark)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\DesktopTidy\Preferences"))
                {
                    if (key == null) return false;
                    if (dark.HasValue) key.SetValue("ThemeMode", dark.Value ? "dark" : "light", RegistryValueKind.String);
                    else key.DeleteValue("ThemeMode", false);
                }
                return true;
            }
            catch { return false; }
        }
        internal void Apply(Form form)
        {
            form.BackColor = Background; form.ForeColor = Text;
            PaintControls(form);
            try { int value = Dark ? 1 : 0; NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)); int corners = 2; NativeMethods.DwmSetWindowAttribute(form.Handle, 33, ref corners, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        private void PaintControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is SurfaceScrollBar) continue;
                string role = child.Tag as string ?? "";
                child.ForeColor = role.Contains("muted") ? Muted : Text;
                child.BackColor = role.Contains("surface") || child is DesktopListControl || (child is RoundedButton && role != "toolbar") ? Surface : Background;
                var button = child as RoundedButton;
                if (button != null) button.SetTheme(this);
                var list = child as DesktopListControl;
                if (list != null) list.SetTheme(this);
                PaintControls(child);
            }
        }
    }
}
