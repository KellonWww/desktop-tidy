using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesktopTidy.UI
{
    // 即时矢量绘制，不缓存位图；路径、画笔和画刷均在当次绘制释放。
    internal static class SurfaceDrawing
    {
        internal static GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void Clip(Control control, int radius)
        {
            if (control.Width < 1 || control.Height < 1) return;
            Region previous = control.Region;
            using (var path = Rounded(new Rectangle(0, 0, control.Width, control.Height), radius)) control.Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
        internal static void Fill(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            if (bounds.Width < 1 || bounds.Height < 1) return;
            using (var path = Rounded(bounds, radius))
            using (var brush = new SolidBrush(color)) graphics.FillPath(brush, path);
        }
        internal static void Outline(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            if (bounds.Width < 1 || bounds.Height < 1) return;
            using (var path = Rounded(bounds, radius))
            using (var pen = new Pen(color)) graphics.DrawPath(pen, path);
        }
    }

    // Transparent track, alpha-blended thumb; the workspace can use either orientation.
    internal sealed class SurfaceScrollBar : Control
    {
        private int current, maximum, largeChange = 1, dragOffset;
        private bool dragging, hovered;
        private Theme theme = Theme.Read();
        internal bool Horizontal { get; set; }
        internal bool Translucent { get; set; }
        internal int SmallChange = 3;
        internal event EventHandler ValueChanged;
        internal int Value
        {
            get { return current; }
            set
            {
                int next = Math.Max(0, Math.Min(Math.Max(0, maximum - largeChange + 1), value));
                if (current == next) return;
                current = next; Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        internal int Maximum { get { return maximum; } set { maximum = Math.Max(0, value); Value = current; Invalidate(); } }
        internal int LargeChange { get { return largeChange; } set { largeChange = Math.Max(1, value); Value = current; Invalidate(); } }
        private int TrackInset { get { return Math.Min(6, Math.Max(0, (Horizontal ? Width : Height) / 4)); } }
        private int TrackLength { get { return Math.Max(0, (Horizontal ? Width : Height) - TrackInset * 2); } }
        private int Coordinate(Point point) { return Horizontal ? point.X : point.Y; }
        internal SurfaceScrollBar()
        {
            Width = 14; Translucent = true; TabStop = false; AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "桌面列表滚动条";
            SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }
        internal void SetTheme(Theme value) { theme = value; BackColor = Translucent ? Color.Transparent : theme.Surface; Invalidate(); }
        private Rectangle Track
        {
            get
            {
                int cross = Horizontal ? Height : Width;
                int thickness = Math.Min(Math.Max(0, cross - 4), SystemInformation.HighContrast ? 6 : dragging ? 8 : hovered || Focused ? 6 : 4);
                return Horizontal ? new Rectangle(TrackInset, (Height - thickness) / 2, TrackLength, thickness)
                    : new Rectangle((Width - thickness) / 2, TrackInset, thickness, TrackLength);
            }
        }
        private Rectangle Thumb
        {
            get
            {
                Rectangle track = Track;
                int length = Math.Min(TrackLength, Math.Max(28, Math.Max(Font.Height + 8,
                    (int)((long)TrackLength * largeChange / Math.Max(1L, (long)maximum + 1)))));
                int range = Math.Max(1, maximum - largeChange + 1);
                int start = TrackInset + (int)((long)(TrackLength - length) * current / range);
                return Horizontal ? new Rectangle(start, track.Y, length, track.Height)
                    : new Rectangle(track.X, start, track.Width, length);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (!Translucent) e.Graphics.Clear(theme.Surface);
            Rectangle thumb = Thumb; if (thumb.Width <= 0 || thumb.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool contrast = SystemInformation.HighContrast;
            if (hovered || dragging || Focused)
                SurfaceDrawing.Fill(e.Graphics, Track, 3, Color.FromArgb(contrast ? 255 : 22, contrast ? SystemColors.ControlDark : theme.Muted));
            int alpha = contrast ? 255 : dragging ? 220 : hovered || Focused ? 180 : 100;
            Color ink = contrast ? SystemColors.WindowText : theme.Muted;
            SurfaceDrawing.Fill(e.Graphics, thumb, Math.Min(thumb.Width, thumb.Height) / 2, Color.FromArgb(alpha, ink));
            if (Focused && ShowFocusCues) SurfaceDrawing.Outline(e.Graphics, thumb, 3, theme.Text);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            if (TabStop) Focus();
            var thumb = Thumb; int position = Coordinate(e.Location), start = Horizontal ? thumb.Left : thumb.Top, end = Horizontal ? thumb.Right : thumb.Bottom;
            if (position >= start && position < end) { dragging = true; dragOffset = position - start; Capture = true; }
            else Value += position < start ? -largeChange : largeChange;
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Value = (int)Math.Round((double)(Coordinate(e.Location) - TrackInset - dragOffset) * Math.Max(0, maximum - largeChange + 1) / Math.Max(1, TrackLength - (Horizontal ? Thumb.Width : Thumb.Height)));
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; Invalidate(); } }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Consume even at the scroll boundary; the enclosing workspace must not move.
            var wheel = e as HandledMouseEventArgs;
            if (wheel != null) wheel.Handled = true;
            base.OnMouseWheel(e);
            Value -= e.Delta / 120 * SmallChange;
        }
        protected override bool IsInputKey(Keys keyData)
        { return (keyData & Keys.KeyCode) == Keys.Left || (keyData & Keys.KeyCode) == Keys.Right || (keyData & Keys.KeyCode) == Keys.Up || (keyData & Keys.KeyCode) == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) Value -= SmallChange;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) Value += SmallChange;
            else if (e.KeyCode == Keys.PageUp) Value -= LargeChange;
            else if (e.KeyCode == Keys.PageDown) Value += LargeChange;
            else if (e.KeyCode == Keys.Home) Value = 0;
            else if (e.KeyCode == Keys.End) Value = Maximum;
            else return;
            e.Handled = e.SuppressKeyPress = true;
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ScrollAccessibility(this); }
        private sealed class ScrollAccessibility : ControlAccessibleObject
        {
            private readonly SurfaceScrollBar owner;
            internal ScrollAccessibility(SurfaceScrollBar owner) : base(owner) { this.owner = owner; }
            public override string Value { get { return owner.Value.ToString(); } set { int position; if (int.TryParse(value, out position)) owner.Value = position; } }
        }
    }

    // Native scrollbars cannot alpha-blend; scroll a canvas inside a clipped viewport.
    internal sealed class WorkspaceViewport : Panel
    {
        private readonly Panel canvas;
        private readonly Panel clip = new Panel();
        private readonly SurfaceScrollBar vertical = new SurfaceScrollBar { Translucent = true, TabStop = true, SmallChange = 48 };
        private readonly SurfaceScrollBar horizontal = new SurfaceScrollBar { Translucent = true, Horizontal = true, TabStop = true, SmallChange = 48 };
        private bool updating;
        internal int ViewWidth { get { return Math.Max(1, ClientSize.Width - 28); } }
        internal int ViewHeight { get { return Math.Max(1, ClientSize.Height - 16); } }
        internal WorkspaceViewport(Panel content)
        {
            canvas = content; clip.Controls.Add(canvas); Controls.Add(clip); Controls.Add(vertical); Controls.Add(horizontal);
            vertical.AccessibleName = "工作区垂直滚动条"; horizontal.AccessibleName = "工作区水平滚动条";
            vertical.ValueChanged += delegate { PositionCanvas(); };
            horizontal.ValueChanged += delegate { PositionCanvas(); };
            canvas.ControlAdded += delegate(object sender, ControlEventArgs e)
            { e.Control.LocationChanged += ContentChanged; e.Control.SizeChanged += ContentChanged; e.Control.Enter += ContentFocused; UpdateContent(); };
            canvas.ControlRemoved += delegate(object sender, ControlEventArgs e)
            { e.Control.LocationChanged -= ContentChanged; e.Control.SizeChanged -= ContentChanged; e.Control.Enter -= ContentFocused; UpdateContent(); };
            canvas.MouseWheel += Wheel; clip.MouseWheel += Wheel;
            Resize += delegate { UpdateContent(); };
        }
        private void ContentChanged(object sender, EventArgs e) { UpdateContent(); }
        private void ContentFocused(object sender, EventArgs e)
        {
            var control = sender as Control;
            if (control == null) return;
            if (control.Top < vertical.Value) vertical.Value = control.Top;
            else if (control.Bottom > vertical.Value + clip.Height) vertical.Value = control.Bottom - clip.Height;
        }
        private void Wheel(object sender, MouseEventArgs e)
        {
            // Canvas and clipping panel share this handler; process each native wheel once.
            var wheel = e as HandledMouseEventArgs;
            if (wheel != null) wheel.Handled = true;
            SurfaceScrollBar bar = (ModifierKeys & Keys.Shift) != 0 ? horizontal : vertical;
            bar.Value -= e.Delta / 120 * bar.SmallChange;
        }
        internal void SetTheme(Theme theme) { SetTheme(theme, theme.Background); }
        internal void SetTheme(Theme theme, Color background)
        { BackColor = clip.BackColor = canvas.BackColor = background; vertical.SetTheme(theme); horizontal.SetTheme(theme); }
        internal void ScrollTo(Point point) { horizontal.Value = point.X; vertical.Value = point.Y; PositionCanvas(); }
        internal void UpdateContent()
        {
            if (updating || IsDisposed) return; updating = true;
            try
            {
                int width = ViewWidth, height = ViewHeight;
                int extentWidth = width, extentHeight = height;
                foreach (Control card in canvas.Controls)
                { if (object.Equals(card.Tag, "layout-hidden")) continue; extentWidth = Math.Max(extentWidth, card.Right + 12); extentHeight = Math.Max(extentHeight, card.Bottom + 12); }
                clip.SetBounds(0, 0, width, height);
                vertical.SetBounds(width + 2, 12, 14, Math.Max(1, height - 24));
                horizontal.SetBounds(12, height, Math.Max(1, width - 24), 14);
                canvas.Size = new Size(extentWidth, extentHeight);
                vertical.Maximum = extentHeight - 1; vertical.LargeChange = height;
                horizontal.Maximum = extentWidth - 1; horizontal.LargeChange = width;
                vertical.Visible = extentHeight > height; horizontal.Visible = extentWidth > width;
                PositionCanvas();
            }
            finally { updating = false; }
        }
        private void PositionCanvas() { canvas.Location = new Point(-horizontal.Value, -vertical.Value); }
    }

    internal sealed class RoundedButton : Button
    {
        internal bool Secondary { get; set; }
        internal bool Quiet { get; set; }
        internal string ToolGlyph { get; set; }
        internal bool MenuIndicator { get; set; }
        private bool selected;
        internal bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        private bool hovered, pressed;
        private Theme theme = Theme.Read();
        internal RoundedButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
        }
        internal void SetTheme(Theme value) { theme = value; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            // 此按钮位于表单绘制的工作台内，背景为 Surface，而非表单底色。
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? theme.Header : pressed ? theme.Pressed : hovered ? theme.Hover : theme.Accent;
            if (Secondary && Enabled) fill = pressed ? theme.Header : hovered ? theme.AccentSoft : BackColor;
            if (Secondary && Quiet && Enabled) fill = pressed ? theme.Header : hovered ? theme.AccentSoft : BackColor;
            if (Selected && Enabled) fill = theme.Accent;
            Color ink = !Enabled ? theme.Muted : Selected || !Secondary ? theme.AccentText : hovered && !SystemInformation.HighContrast ? theme.Accent : theme.Text;
            if (Secondary && Enabled && (hovered || pressed) && SystemInformation.HighContrast) { fill = theme.Accent; ink = theme.AccentText; }
            var bounds = new Rectangle(2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
            int radius = 10;
            SurfaceDrawing.Fill(e.Graphics, bounds, radius, fill);
            if (Secondary && !Quiet) SurfaceDrawing.Outline(e.Graphics, bounds, radius, hovered && Enabled ? theme.AccentLine : theme.StrongStroke);
            Rectangle textBounds = bounds;
            if (Quiet) { textBounds.X += 12; textBounds.Width -= 24; }
            if (ToolGlyph != null)
            {
                DrawToolGlyph(e.Graphics, ToolGlyph, ToolGlyph == "clear" ? Width / 2 - 8 : 12, Height / 2 - 8, Enabled ? ink : theme.Muted);
                textBounds.X = 40; textBounds.Width = Math.Max(1, Width - 64);
            }
            if (MenuIndicator)
                using (var pen = new Pen(Enabled ? theme.Muted : theme.Stroke, 1.4F))
                    e.Graphics.DrawLines(pen, new Point[] { new Point(Width - 21, Height / 2 - 2), new Point(Width - 17, Height / 2 + 2), new Point(Width - 13, Height / 2 - 2) });
            if (ToolGlyph != "clear") TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ink, (Quiet ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) SurfaceDrawing.Outline(e.Graphics, new Rectangle(5, 5, Math.Max(1, Width - 11), Math.Max(1, Height - 11)), radius, ink);
        }
        // Small line icons share a 16px grid; no glyph-font or bitmap dependency.
        private static void DrawToolGlyph(Graphics graphics, string glyph, int x, int y, Color color)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(x, y);
            using (var pen = new Pen(color, 1.4F))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (glyph)
                {
                    case "sort": case "sort-up":
                        graphics.DrawLine(pen, 1, 3, 9, 3); graphics.DrawLine(pen, 1, 8, 7, 8); graphics.DrawLine(pen, 1, 13, 5, 13);
                        graphics.DrawLine(pen, 12, 2, 12, 13);
                        graphics.DrawLines(pen, glyph == "sort-up" ? new Point[] { new Point(9, 5), new Point(12, 2), new Point(15, 5) } : new Point[] { new Point(9, 10), new Point(12, 13), new Point(15, 10) }); break;
                    case "fold": case "expand":
                        graphics.DrawRectangle(pen, 2, 2, 12, 12); graphics.DrawLine(pen, 5, 8, 11, 8);
                        if (glyph == "expand") graphics.DrawLine(pen, 8, 5, 8, 11); break;
                    case "refresh":
                        graphics.DrawArc(pen, 2, 2, 12, 12, 45, 285);
                        graphics.DrawLines(pen, new Point[] { new Point(10, 1), new Point(14, 3), new Point(12, 7) }); break;
                    case "arrange":
                        graphics.DrawRectangle(pen, 2, 2, 4, 4); graphics.DrawRectangle(pen, 10, 2, 4, 4);
                        graphics.DrawRectangle(pen, 2, 10, 4, 4); graphics.DrawRectangle(pen, 10, 10, 4, 4); break;
                    case "settings":
                        graphics.DrawLine(pen, 1, 5, 15, 5); graphics.DrawLine(pen, 1, 11, 15, 11);
                        graphics.DrawEllipse(pen, 4, 3, 4, 4); graphics.DrawEllipse(pen, 9, 9, 4, 4); break;
                    case "hide":
                        graphics.DrawRectangle(pen, 2, 2, 12, 12); graphics.DrawLine(pen, 5, 5, 5, 11);
                        graphics.DrawLines(pen, new Point[] { new Point(10, 5), new Point(7, 8), new Point(10, 11) }); break;
                    case "clear":
                        graphics.DrawLine(pen, 5, 5, 11, 11); graphics.DrawLine(pen, 11, 5, 5, 11); break;
                }
            }
            graphics.Restore(state);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressed = false; Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); pressed = false; Invalidate(); }
    }
}
