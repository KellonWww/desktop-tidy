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
                Background = dark ? Color.FromArgb(20, 33, 38) : Color.FromArgb(240, 236, 223),
                Surface = dark ? Color.FromArgb(27, 43, 49) : Color.FromArgb(248, 245, 235),
                Header = dark ? Color.FromArgb(37, 55, 62) : Color.FromArgb(228, 223, 208),
                Stroke = dark ? Color.FromArgb(53, 73, 79) : Color.FromArgb(202, 196, 181),
                Text = dark ? Color.FromArgb(237, 242, 232) : Color.FromArgb(41, 45, 40),
                Muted = dark ? Color.FromArgb(176, 192, 195) : Color.FromArgb(91, 98, 89),
                Accent = dark ? Color.FromArgb(213, 245, 120) : Color.FromArgb(40, 74, 179),
                AccentText = dark ? Color.FromArgb(25, 37, 25) : Color.FromArgb(249, 250, 252),
                Hover = dark ? Color.FromArgb(224, 250, 156) : Color.FromArgb(52, 91, 199),
                Pressed = dark ? Color.FromArgb(190, 219, 104) : Color.FromArgb(32, 62, 153),
                AccentSoft = dark ? Color.FromArgb(51, 67, 41) : Color.FromArgb(221, 226, 242),
                AccentLine = dark ? Color.FromArgb(147, 178, 82) : Color.FromArgb(107, 129, 195),
                StrongStroke = dark ? Color.FromArgb(97, 119, 125) : Color.FromArgb(148, 142, 126)
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
