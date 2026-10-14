using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    // 仅绘制可见行；不加载文件缩略图，不为每一项创建控件。
    internal sealed class DesktopListControl : Control
    {
        private readonly List<DesktopIcon> items = new List<DesktopIcon>();
        private readonly SurfaceScrollBar scroll = new SurfaceScrollBar();
        private Theme theme = Theme.Read();
        private bool scanned;
        private int hover = -1;
        private int RowHeight { get { return Font.Height + 22; } }
        private int HeaderHeight { get { return Font.Height + 20; } }
        internal DesktopListControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            scroll.ValueChanged += delegate { hover = -1; Invalidate(); };
            Controls.Add(scroll); TabStop = true;
            AccessibleName = "桌面项目列表";
            AccessibleDescription = "只读列表，可用滚轮、上下键和翻页键查看项目";
        }
        internal void SetTheme(Theme value) { theme = value; scroll.SetTheme(value); Invalidate(); }
        internal void SetItems(IList<DesktopIcon> source)
        {
            items.Clear(); scanned = true; hover = -1;
            // 限制的是可见行数量，不是数据数量；保留 Shell 扫描上限内的全部项目。
            for (int i = 0; i < Math.Min(source.Count, 3000); i++) items.Add(source[i]);
            scroll.Value = 0; UpdateScroll(); Invalidate();
        }
        private void UpdateScroll()
        {
            if (scroll == null) return;
            int rows = Math.Min(100, Math.Max(1, (ClientSize.Height - HeaderHeight) / RowHeight));
            int max = Math.Max(0, items.Count - rows);
            if (scroll.Value > max) scroll.Value = max;
            scroll.LargeChange = rows;
            scroll.Maximum = Math.Max(0, items.Count - 1);
            scroll.Visible = max > 0;
            scroll.SetBounds(Math.Max(0, ClientSize.Width - scroll.Width - 10), HeaderHeight + 6, scroll.Width, Math.Max(1, ClientSize.Height - HeaderHeight - 24));
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateScroll(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int width = ClientSize.Width - (scroll.Visible ? scroll.Width + 10 : 0);
            int coordinateWidth = Font.Height * 7, nameWidth = Math.Max(60, width - coordinateWidth);
            SurfaceDrawing.Fill(e.Graphics, new Rectangle(0, 0, width, HeaderHeight), Font.Height / 2, theme.Header);
            Draw(e.Graphics, "名称", 12, 0, nameWidth - 12, HeaderHeight, theme.Muted);
            Draw(e.Graphics, "桌面坐标", nameWidth, 0, coordinateWidth, HeaderHeight, theme.Muted);
            if (items.Count == 0) DrawEmpty(e.Graphics, width);
            int visible = Math.Min(100, Math.Max(0, (ClientSize.Height - HeaderHeight) / RowHeight));
            for (int i = 0; i < visible && i + scroll.Value < items.Count; i++)
            {
                var item = items[i + scroll.Value];
                int y = HeaderHeight + i * RowHeight;
                if (hover == i) SurfaceDrawing.Fill(e.Graphics, new Rectangle(0, y + 2, width, RowHeight - 4), Font.Height / 2, theme.Header);
                int iconSize = Font.Height - 4;
                SurfaceDrawing.Outline(e.Graphics, new Rectangle(20, y + (RowHeight - iconSize) / 2, iconSize - 3, iconSize), 3, theme.Muted);
                Draw(e.Graphics, item.Name, Font.Height + 24, y, nameWidth - Font.Height - 24, RowHeight, theme.Text);
                Draw(e.Graphics, item.X + ", " + item.Y, nameWidth, y, coordinateWidth, RowHeight, theme.Muted);
                using (var pen = new Pen(theme.Stroke)) e.Graphics.DrawLine(pen, 18, y + RowHeight - 1, width - 14, y + RowHeight - 1);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(1, 1, Math.Max(1, width - 3), Math.Max(1, Height - 3)), theme.Text, theme.Surface);
        }
        private void DrawEmpty(Graphics graphics, int width)
        {
            int unit = Font.Height, center = width / 2;
            int y = HeaderHeight + Math.Max(unit, (ClientSize.Height - HeaderHeight - unit * 6) / 2);
            var screen = new Rectangle(center - unit * 2, y, unit * 4, unit * 3);
            SurfaceDrawing.Fill(graphics, screen, unit / 2, theme.Header);
            SurfaceDrawing.Outline(graphics, screen, unit / 2, theme.Stroke);
            for (int i = 0; i < 3; i++)
            {
                SurfaceDrawing.Fill(graphics, new Rectangle(screen.X + unit / 2, y + unit / 2 + i * unit * 3 / 4, unit / 2, unit / 2), 3, theme.Muted);
                using (var pen = new Pen(theme.Stroke)) graphics.DrawLine(pen, screen.X + unit * 3 / 2, y + unit * 3 / 4 + i * unit * 3 / 4, screen.Right - unit / 2, y + unit * 3 / 4 + i * unit * 3 / 4);
            }
            DrawCentered(graphics, scanned ? "桌面暂时没有可读取的项目" : "你的桌面，从这里看清", width, y + unit * 3 + 12, unit * 2, theme.Text);
            DrawCentered(graphics, scanned ? "可以稍后重新扫描" : "点击上方「扫描桌面」，不会移动任何文件", width, y + unit * 5, unit * 2, theme.Muted);
        }
        private void DrawCentered(Graphics graphics, string text, int width, int y, int height, Color color)
        {
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(0, y, width, height), color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        private void Draw(Graphics graphics, string text, int x, int y, int width, int height, Color color)
        {
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(x + 8, y, Math.Max(1, width - 16), height), color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = e.Y < HeaderHeight ? -1 : (e.Y - HeaderHeight) / RowHeight;
            if (row + scroll.Value >= items.Count) row = -1;
            if (hover != row) { hover = row; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var wheel = e as HandledMouseEventArgs;
            if (wheel != null) wheel.Handled = true;
            base.OnMouseWheel(e);
            scroll.Value = Math.Max(0, Math.Min(Math.Max(0, items.Count - scroll.LargeChange), scroll.Value - e.Delta / 120 * 3));
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.PageDown ? scroll.LargeChange : e.KeyCode == Keys.PageUp ? -scroll.LargeChange : 0;
            if (step != 0) { scroll.Value = Math.Max(0, Math.Min(Math.Max(0, items.Count - scroll.LargeChange), scroll.Value + step)); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) items.Clear(); base.Dispose(disposing); }
    }
}
