using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    internal sealed class WorkspaceMenu : ContextMenuStrip
    {
        internal WorkspaceMenu()
        {
            ShowImageMargin = false; ShowCheckMargin = true;
            Padding = new Padding(4); AutoClose = true;
            Opening += delegate { ApplyTheme(this, Theme.Read()); };
            Opened += delegate { ApplyRoundedCorners(this, EventArgs.Empty); };
        }
        internal static ToolStripMenuItem CreateOpacityMenu(Func<int> current, Action<int> commit)
        {
            var menu = new ToolStripMenuItem("不透明度（25%～100%）");
            var input = new NumericUpDown { Minimum = 25, Maximum = 100, Increment = 1, DecimalPlaces = 0,
                Width = 150, AccessibleName = "工作区不透明度百分比，步进1%" };
            var editor = new ToolStripControlHost(input);
            menu.DropDownItems.Add(editor);
            menu.DropDownItems.Add("应用不透明度", null, delegate { commit((int)input.Value); });
            menu.DropDownOpening += delegate
            {
                Theme theme = Theme.Read();
                input.BackColor = theme.Surface; input.ForeColor = theme.Text;
                input.Font = menu.Font; input.Value = current();
            };
            return menu;
        }
        private static void ApplyRoundedCorners(object sender, EventArgs e)
        {
            var strip = sender as ToolStrip;
            if (strip == null || strip.Width < 1 || strip.Height < 1) return;
            try
            {
                int preference = 2;
                if (NativeMethods.DwmSetWindowAttribute(strip.Handle, 33, ref preference, sizeof(int)) == 0) return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            SurfaceDrawing.Clip(strip, 12);
        }
        private static void ApplyTheme(ToolStrip strip, Theme theme)
        {
            strip.Renderer = new WorkspaceMenuRenderer(theme);
            strip.BackColor = theme.Surface; strip.ForeColor = theme.Text;
            var dropDown = strip as ToolStripDropDown;
            if (dropDown != null) { dropDown.Opened -= ApplyRoundedCorners; dropDown.Opened += ApplyRoundedCorners; }
            foreach (ToolStripItem item in strip.Items)
            {
                item.ForeColor = item.Enabled ? theme.Text : theme.Muted;
                item.Padding = item is ToolStripSeparator ? Padding.Empty : new Padding(5, 2, 5, 2);
                item.Margin = item is ToolStripSeparator ? new Padding(6, 1, 6, 1) : new Padding(2, 0, 2, 0);
                var menu = item as ToolStripMenuItem;
                if (menu != null && menu.HasDropDownItems) ApplyTheme(menu.DropDown, theme);
            }
        }
    }
    internal sealed class WorkspaceMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Theme theme;
        internal WorkspaceMenuRenderer(Theme theme) { this.theme = theme; RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        { e.Graphics.Clear(theme.Surface); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        { SurfaceDrawing.Outline(e.Graphics, new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 12, theme.Stroke); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
                SurfaceDrawing.Fill(e.Graphics, new Rectangle(5, 1, e.Item.Width - 10, e.Item.Height - 2), 8,
                    SystemInformation.HighContrast ? SystemColors.Highlight : theme.AccentSoft);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected && e.Item.Enabled && SystemInformation.HighContrast ? SystemColors.HighlightText : e.Item.Enabled ? e.Item.Selected ? theme.Accent : theme.Text : theme.Muted;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor, e.TextFormat);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        { e.ArrowColor = SystemInformation.HighContrast && e.Item.Selected ? SystemColors.HighlightText : theme.Muted; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            using (var pen = new Pen(SystemInformation.HighContrast && e.Item.Selected ? SystemColors.HighlightText : theme.Accent, 2))
                e.Graphics.DrawLines(pen, new[] { new Point(r.Left + 3, r.Top + 8), new Point(r.Left + 6, r.Top + 11), new Point(r.Left + 12, r.Top + 4) });
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        { using (var pen = new Pen(theme.Stroke)) e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2); }
    }
    // Keep the native editor for IME, selection and accessibility; draw only its surface.
    internal sealed class WorkspaceTextField : Panel
    {
        private Theme theme = Theme.Read();
        private bool hovered, invalid;
        internal TextBox Editor { get; private set; }
        internal bool Invalid { get { return invalid; } set { invalid = value; Invalidate(); } }
        internal WorkspaceTextField(string text, int maxLength, string accessibleName)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false; Height = 46;
            Editor = new TextBox { BorderStyle = BorderStyle.None, Text = text, MaxLength = maxLength,
                AccessibleName = accessibleName, TabIndex = 0 };
            Controls.Add(Editor);
            Editor.GotFocus += delegate { Invalidate(); };
            Editor.LostFocus += delegate { Invalidate(); };
            Editor.TextChanged += delegate { Invalid = false; };
            MouseEnter += delegate { hovered = true; Invalidate(); };
            MouseLeave += delegate { hovered = false; Invalidate(); };
            Editor.MouseEnter += delegate { hovered = true; Invalidate(); };
            Editor.MouseLeave += delegate { hovered = false; Invalidate(); };
            MouseDown += delegate { Editor.Focus(); };
            SetTheme(theme);
        }
        internal void SetTheme(Theme value)
        {
            theme = value; BackColor = theme.Surface;
            Editor.BackColor = theme.HighContrast ? SystemColors.Window : theme.Background;
            Editor.ForeColor = theme.Text; Invalidate();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Editor == null) return;
            int inset = Math.Max(12, (int)Math.Round(12 * DeviceDpi / 96F));
            Editor.SetBounds(inset, Math.Max(4, (ClientSize.Height - Editor.PreferredHeight) / 2),
                Math.Max(1, ClientSize.Width - inset * 2), Editor.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int scale = Math.Max(1, (int)Math.Round(DeviceDpi / 96F));
            Rectangle bounds = new Rectangle(scale * 2, scale * 2, Width - scale * 4 - 1, Height - scale * 4 - 1);
            Color border = invalid ? (theme.HighContrast ? theme.Text : theme.Dark ? Color.FromArgb(245, 161, 151) : Color.FromArgb(169, 57, 48))
                : ContainsFocus ? theme.Accent : hovered ? theme.StrongStroke : theme.Stroke;
            int radius = theme.HighContrast ? 1 : 10 * scale;
            if (ContainsFocus && !theme.HighContrast)
                SurfaceDrawing.Fill(e.Graphics, ClientRectangle, radius + 2 * scale, theme.AccentSoft);
            SurfaceDrawing.Fill(e.Graphics, bounds, radius, Editor.BackColor);
            SurfaceDrawing.Outline(e.Graphics, bounds, radius, border);
        }
    }
    internal sealed class WorkspaceDialog : Form
    {
        private Font headingFont;
        internal WorkspaceDialog()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppBrand.Icon;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
        }
        internal void AddHeading(string title, string caption)
        {
            if (headingFont == null) headingFont = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            Controls.Add(new Label { Text = title, Font = headingFont, Left = 24, Top = 22, Width = ClientSize.Width - 48, Height = 34 });
            Controls.Add(new Label { Text = caption, Tag = "muted", Left = 24, Top = 62, Width = ClientSize.Width - 48, Height = 26 });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && headingFont != null) { headingFont.Dispose(); headingFont = null; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int inset = Math.Max(1, (int)Math.Round(24 * DeviceDpi / 96F));
            int footer = ClientSize.Height - (int)Math.Round(68 * DeviceDpi / 96F);
            using (var pen = new Pen(Theme.Read().Stroke)) e.Graphics.DrawLine(pen, inset, footer, ClientSize.Width - inset, footer);
        }
        internal void ApplyTheme()
        {
            Theme theme = Theme.Read(); theme.Apply(this); BackColor = theme.Surface; ForeColor = theme.Text;
            PaintChildren(this, theme);
        }
        private static void PaintChildren(Control parent, Theme theme)
        {
            foreach (Control control in parent.Controls)
            {
                var field = control as WorkspaceTextField;
                if (field != null) { field.SetTheme(theme); continue; }
                control.BackColor = theme.Surface;
                control.ForeColor = (control.Tag as string) == "muted" ? theme.Muted : theme.Text;
                var button = control as RoundedButton;
                if (button != null) button.SetTheme(theme);
                PaintChildren(control, theme);
            }
        }
        internal static bool Confirm(string title, string message, string action, Font font)
        {
            using (var dialog = new WorkspaceDialog { Text = title, AccessibleName = title, Font = font, ClientSize = new Size(460, 250) })
            using (var headingFont = new Font(font.FontFamily, 14F, FontStyle.Bold))
            {
                var heading = new Label { Text = title, Font = headingFont, Left = 24, Top = 22, Width = 412, Height = 36 };
                var body = new Label { Text = message, Tag = "muted", Left = 24, Top = 68, Width = 412, Height = 100 };
                var cancel = new RoundedButton { Text = "取消", Secondary = true, Left = 232, Top = 200, Width = 96, Height = 36, DialogResult = DialogResult.Cancel };
                var accept = new RoundedButton { Text = action, Left = 340, Top = 200, Width = 96, Height = 36, DialogResult = DialogResult.OK };
                dialog.Controls.AddRange(new Control[] { heading, body, cancel, accept });
                // Enter defaults to cancel; destructive changes always require an explicit choice.
                dialog.AcceptButton = cancel; dialog.CancelButton = cancel;
                dialog.Shown += delegate { cancel.Focus(); };
                dialog.ApplyTheme();
                return dialog.ShowDialog() == DialogResult.OK;
            }
        }
    }
}
