using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    internal sealed class Theme
    {
        internal Color Background, Surface, Header, Stroke, Text, Muted, Accent, AccentText, Hover, Pressed;
        internal bool Dark;
        internal static Theme Palette(bool dark)
        {
            return new Theme
            {
                Dark = dark,
                Background = dark ? Color.FromArgb(25, 27, 25) : Color.FromArgb(239, 240, 236),
                Surface = dark ? Color.FromArgb(33, 36, 33) : Color.FromArgb(250, 251, 247),
                Header = dark ? Color.FromArgb(42, 46, 42) : Color.FromArgb(235, 238, 231),
                Stroke = dark ? Color.FromArgb(61, 67, 60) : Color.FromArgb(215, 221, 210),
                Text = dark ? Color.FromArgb(237, 240, 232) : Color.FromArgb(38, 43, 35),
                Muted = dark ? Color.FromArgb(170, 180, 162) : Color.FromArgb(99, 110, 91),
                Accent = dark ? Color.FromArgb(178, 198, 157) : Color.FromArgb(80, 103, 60),
                AccentText = dark ? Color.FromArgb(29, 42, 23) : Color.FromArgb(250, 251, 247),
                Hover = dark ? Color.FromArgb(196, 212, 180) : Color.FromArgb(95, 119, 74),
                Pressed = dark ? Color.FromArgb(154, 177, 129) : Color.FromArgb(64, 87, 47)
            };
        }
        internal static Theme Read()
        {
            if (SystemInformation.HighContrast) return new Theme
            {
                Background = SystemColors.Control, Surface = SystemColors.Window, Header = SystemColors.Control,
                Stroke = SystemColors.WindowText, Text = SystemColors.WindowText, Muted = SystemColors.WindowText,
                Accent = SystemColors.Highlight, AccentText = SystemColors.HighlightText,
                Hover = SystemColors.Highlight, Pressed = SystemColors.Highlight
            };
            bool dark = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { }
            return Palette(dark);
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
