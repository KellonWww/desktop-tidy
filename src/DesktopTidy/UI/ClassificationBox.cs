using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopTidy.Core;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    internal sealed class ItemTransfer
    {
        internal WorkspaceController Owner;
        internal WorkspaceItem Item;
    }
    internal sealed class ClassificationBox : Form
    {
        private readonly WorkspaceController owner;
        private readonly ShellIconCache icons;
        private readonly SurfaceScrollBar scroll;
        private readonly ContextMenuStrip menu;
        private readonly List<WorkspaceItem> items = new List<WorkspaceItem>();
        private Theme theme = Theme.Read();
        private int selected = -1, hover = -1;
        private Point mouseStart, locationStart;
        private Size sizeStart;
        private bool moving, resizing, modifying, committing;
        private readonly Timer motion = new Timer { Interval = 16 };
        private readonly Stopwatch motionClock = new Stopwatch();
        private Point motionStart, motionTarget;
        internal readonly GroupState State;
        internal bool IsSearchResults { get; set; }
        internal bool Attached { get { return false; } }
        private bool DisplayCollapsed { get { return State.Collapsed; } }
        private int HeaderHeight { get { return Font.Height * 2 + 14; } }
        private int CellHeight { get { return Math.Max(90, Font.Height * 4 + 12); } }
        private int Columns { get { return Math.Max(1, (ClientSize.Width - 30) / Math.Max(88, Font.Height * 4)); } }
        private int CellWidth { get { return Math.Max(1, (ClientSize.Width - 30) / Columns); } }
        internal ClassificationBox(WorkspaceController owner, GroupState state, ShellIconCache icons, Font font)
        {
            this.owner = owner; State = state; this.icons = icons;
            Font = font; Text = "桌面分类 · " + owner.CategoryTitle(state.Category);
            TopLevel = false; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            AllowDrop = true; AccessibleName = Text; KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            scroll = new SurfaceScrollBar { Font = font, AccessibleName = "分类框滚动条" };
            scroll.ValueChanged += delegate { Invalidate(); }; Controls.Add(scroll);
            menu = new WorkspaceMenu { Font = Font, AccessibleName = "工作区操作菜单" };
            menu.Items.Add("双击打开 · 拖到其他分类框", null, delegate { }).Enabled = false;
            var toggle = menu.Items.Add("折叠分类框", null, delegate { owner.Toggle(this); });
            menu.Opening += delegate { toggle.Text = State.Collapsed ? "展开分类框" : "折叠分类框"; };
            menu.Items.Add("重命名分类", null, delegate { owner.RenameCategory(this); });
            if (state.Category > Category.Other) menu.Items.Add("删除此自定义分类（不删文件）", null, delegate { owner.DeleteCategory(this); });
            menu.Items.Add("重新按类型分类（清除手动归属）", null, delegate { owner.ResetClassification(); });
            menu.Items.Add("撤销上一次修改", null, delegate { owner.Undo(); });
            var collapseAll = menu.Items.Add("折叠全部分类框", null, delegate { owner.ToggleGroupsCollapsed(); });
            menu.Opening += delegate { collapseAll.Text = owner.AllGroupsCollapsed ? "展开全部分类框" : "折叠全部分类框"; };
            menu.Items.Add("隐藏工作区", null, delegate { owner.RequestHide(); });
            ContextMenuStrip = menu;
            motion.Tick += delegate { AdvanceMotion(); };
            RefreshTheme(); ApplyState();
        }
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x80; return value; } }
        internal void Display() { Show(); }
        internal void ApplyState() { ApplyState(false); }
        internal void ApplyState(bool animate)
        {
            int height = DisplayCollapsed ? HeaderHeight + 2 : State.Height;
            Size = new Size(State.Width, height);
            MoveTo(new Point(Math.Max(0, State.X), Math.Max(0, State.Y)), animate);
            LayoutScroll(); Invalidate();
        }
        private void MoveTo(Point target, bool animate)
        {
            if (!animate || !SystemInformation.IsMenuAnimationEnabled || SystemInformation.HighContrast)
            { motion.Stop(); motionClock.Stop(); Location = target; return; }
            if (motion.Enabled && target == motionTarget) return;
            motion.Stop();
            if (Location == target) return;
            motionStart = Location; motionTarget = target; motionClock.Restart(); motion.Start();
        }
        private void AdvanceMotion()
        {
            double progress = Math.Min(1.0, motionClock.Elapsed.TotalMilliseconds / 180.0);
            double eased = 1.0 - Math.Pow(1.0 - progress, 3);
            Location = new Point(motionStart.X + (int)Math.Round((motionTarget.X - motionStart.X) * eased),
                motionStart.Y + (int)Math.Round((motionTarget.Y - motionStart.Y) * eased));
            if (progress >= 1.0) { motion.Stop(); motionClock.Stop(); Location = motionTarget; }
        }
        internal bool LayoutAnimating { get { return motion.Enabled; } }
        internal Rectangle LayoutBounds { get { return new Rectangle(State.X, State.Y, State.Width, DisplayCollapsed ? HeaderHeight + 2 : State.Height); } }
        internal void CaptureState()
        {
            if (IsSearchResults) return;
            Point offset = Parent is ScrollableControl ? ((ScrollableControl)Parent).AutoScrollPosition : Point.Empty;
            Point position = moving && modifying ? new Point(State.X, State.Y) : motion.Enabled ? motionTarget : Location;
            State.X = Math.Max(0, Math.Min(4000, position.X - offset.X)); State.Y = Math.Max(0, Math.Min(4000, position.Y - offset.Y));
            State.Width = Width; if (!State.Collapsed) State.Height = Height;
        }
        internal void SetItems(IEnumerable<WorkspaceItem> values)
        {
            items.Clear(); items.AddRange(values);
            if (State.SortByName) items.Sort(delegate(WorkspaceItem a, WorkspaceItem b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); }); selected = hover = -1; scroll.Value = 0; ApplyState();
        }
        internal void RefreshTheme() { theme = Theme.Read(); BackColor = theme.Surface; ForeColor = theme.Text; scroll.SetTheme(theme); Invalidate(); }
        private void LayoutScroll()
        {
            if (scroll == null) return;
            int visibleRows = Math.Max(1, (ClientSize.Height - HeaderHeight - 12) / CellHeight);
            int rows = (items.Count + Columns - 1) / Columns;
            scroll.SetBounds(Width - 24, HeaderHeight + 6, 14, Math.Max(1, Height - HeaderHeight - 30));
            scroll.Maximum = Math.Max(0, rows - 1); scroll.LargeChange = visibleRows;
            scroll.Visible = !DisplayCollapsed && rows > visibleRows;
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
            {
                SurfaceDrawing.Clip(this, 12);
            }
            LayoutScroll();
        }
        private Rectangle Cell(int index)
        {
            return new Rectangle(12 + index % Columns * CellWidth, HeaderHeight + 6 + (index / Columns - scroll.Value) * CellHeight, CellWidth - 4, CellHeight - 4);
        }
        private int Hit(Point point)
        {
            if (DisplayCollapsed || point.Y < HeaderHeight + 6 || point.Y >= Height - 12 || point.X < 12 || point.X >= Width - 18) return -1;
            int column = (point.X - 12) / CellWidth;
            int index = (scroll.Value + (point.Y - HeaderHeight - 6) / CellHeight) * Columns + column;
            return column < Columns && index < items.Count ? index : -1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            SurfaceDrawing.Outline(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 12, theme.Stroke);
            TextRenderer.DrawText(e.Graphics, IsSearchResults ? "搜索结果" : owner.CategoryTitle(State.Category), Font, new Rectangle(18, 7, Width - 160, HeaderHeight - 12), theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, items.Count.ToString(), Font, new Rectangle(Width - 86, 7, 40, HeaderHeight - 12), theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            TextRenderer.DrawText(e.Graphics, IsSearchResults ? "×" : DisplayCollapsed ? "+" : "−", Font, new Rectangle(Width - 40, 7, 28, HeaderHeight - 12), theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            if (!IsSearchResults) TextRenderer.DrawText(e.Graphics, "排版", Font, new Rectangle(Width - 142, 7, 50, HeaderHeight - 12), theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            if (moving && modifying) SurfaceDrawing.Outline(e.Graphics, new Rectangle(2, 2, Width - 5, Height - 5), 12, theme.Accent);
            if (DisplayCollapsed) return;
            if (IsSearchResults && items.Count == 0)
            {
                TextRenderer.DrawText(e.Graphics, "没有找到匹配图标\n换个关键词，或按 Esc 清除搜索", Font,
                    new Rectangle(16, HeaderHeight + 24, Width - 32, Math.Max(60, Height - HeaderHeight - 48)), theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            using (var pen = new Pen(theme.Stroke)) e.Graphics.DrawLine(pen, 16, HeaderHeight, Width - 16, HeaderHeight);
            var clip = e.Graphics.Save(); e.Graphics.SetClip(new Rectangle(10, HeaderHeight + 2, Width - 29, Math.Max(1, Height - HeaderHeight - 14)));
            int start = scroll.Value * Columns, end = Math.Min(items.Count, start + ((Height - HeaderHeight) / CellHeight + 1) * Columns);
            for (int i = start; i < end; i++)
            {
                Rectangle cell = Cell(i);
                if (i == selected || i == hover) SurfaceDrawing.Fill(e.Graphics, cell, 10, theme.Header);
                icons.Draw(e.Graphics, items[i], new Rectangle(cell.X + (cell.Width - 32) / 2, cell.Y + 7, 32, 32));
                TextRenderer.DrawText(e.Graphics, items[i].Name, Font, new Rectangle(cell.X + 2, cell.Y + 43, cell.Width - 4, cell.Height - 45), theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            e.Graphics.Restore(clip);
            if (!IsSearchResults) using (var pen = new Pen(theme.Muted)) { e.Graphics.DrawLine(pen, Width - 14, Height - 7, Width - 7, Height - 14); e.Graphics.DrawLine(pen, Width - 10, Height - 7, Width - 7, Height - 10); }
        }
        private bool HeaderAction(Point point)
        {
            return point.Y >= 0 && point.Y < HeaderHeight &&
                (point.X > Width - 42 || (!IsSearchResults && point.X >= Width - 144 && point.X < Width - 90));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            if (IsSearchResults)
            {
                if (HeaderAction(e.Location)) { owner.ClearSearch(); return; }
                selected = Hit(e.Location); Invalidate(); return;
            }
            if (HeaderAction(e.Location) && e.X < Width - 90) { owner.ArrangeItems(this); return; }
            if (HeaderAction(e.Location)) { owner.Toggle(this); return; }
            motion.Stop(); motionClock.Stop();
            mouseStart = PointToScreen(e.Location); locationStart = Location; sizeStart = Size;
            moving = e.Y < HeaderHeight; resizing = !State.Collapsed && e.X > Width - 24 && e.Y > Height - 24;
            selected = Hit(e.Location); if (moving || resizing) Capture = true;
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); Point screen = PointToScreen(e.Location);
            int dx = screen.X - mouseStart.X, dy = screen.Y - mouseStart.Y;
            if ((moving || resizing) && e.Button == MouseButtons.Left)
            {
                if (!modifying && Math.Abs(dx) + Math.Abs(dy) < 4) return;
                if (!modifying) { if (!owner.Checkpoint()) { moving = resizing = false; Capture = false; return; } modifying = true; }
                if (moving)
                {
                    Location = new Point(Math.Max(0, Math.Min(4000, locationStart.X + dx)), Math.Max(0, Math.Min(4000, locationStart.Y + dy)));
                    BringToFront(); owner.PreviewGroupMove(this, Parent.PointToClient(screen)); Invalidate();
                }
                else Size = new Size(Math.Max(220, Math.Min(2000, sizeStart.Width + dx)), Math.Max(140, Math.Min(2000, sizeStart.Height + dy)));
            }
            else if (!IsSearchResults && selected >= 0 && e.Button == MouseButtons.Left && Math.Abs(dx) + Math.Abs(dy) > 8)
            {
                WorkspaceItem item = items[selected]; selected = -1;
                var data = new DataObject(); data.SetData("DesktopTidy.Item", false, new ItemTransfer { Owner = owner, Item = item });
                DoDragDrop(data, DragDropEffects.Link); Invalidate();
            }
            else
            {
                int next = Hit(e.Location); if (next != hover) { hover = next; Invalidate(); }
                Cursor = HeaderAction(e.Location) || IsSearchResults ? Cursors.Default : e.Y < HeaderHeight ? Cursors.SizeAll : e.X > Width - 24 && e.Y > Height - 24 && !State.Collapsed ? Cursors.SizeNWSE : Cursors.Default;
            }
        }
        private void EndChange()
        {
            if (committing) return; committing = true;
            bool save = modifying, wasMoving = moving; moving = resizing = modifying = false; Capture = false;
            if (save)
            {
                if (wasMoving) owner.CompleteGroupMove();
                else { CaptureState(); owner.ReflowGroups(true, true); }
            }
            Invalidate();
            committing = false;
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); EndChange(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) EndChange(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Cursor = Cursors.Default; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Consume even at the scroll boundary; the enclosing workspace must not move.
            var wheel = e as HandledMouseEventArgs;
            if (wheel != null) wheel.Handled = true;
            base.OnMouseWheel(e);
            scroll.Value -= e.Delta / 120;
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e); int index = Hit(e.Location); if (e.Button == MouseButtons.Left && index >= 0) Open(items[index]);
        }
        private void Open(WorkspaceItem item)
        {
            try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
            catch (Exception ex) { if (!(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is System.IO.IOException || ex is UnauthorizedAccessException)) throw; owner.Notify("无法打开：" + ex.Message); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.F) { owner.FocusSearch(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Z) { owner.Undo(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && selected >= 0) { Open(items[selected]); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape)
            {
                if (IsSearchResults) { owner.ClearSearch(); e.Handled = true; return; }
                bool cancel = moving && modifying, cancelResize = resizing && modifying;
                moving = resizing = modifying = false; Capture = false; selected = -1;
                if (cancel) owner.CancelGroupMove();
                else if (cancelResize) ApplyState();
                Invalidate(); e.Handled = true;
            }
        }
        protected override void OnDragEnter(DragEventArgs e)
        {
            if (IsSearchResults) { e.Effect = DragDropEffects.None; return; }
            var transfer = e.Data.GetData("DesktopTidy.Item", false) as ItemTransfer;
            e.Effect = transfer != null && ReferenceEquals(transfer.Owner, owner) ? DragDropEffects.Link : DragDropEffects.None;
        }
        protected override void OnDragDrop(DragEventArgs e)
        {
            if (IsSearchResults) return;
            var transfer = e.Data.GetData("DesktopTidy.Item", false) as ItemTransfer;
            if (transfer != null && ReferenceEquals(transfer.Owner, owner)) owner.Assign(transfer.Item, State.Category);
        }
        protected override void Dispose(bool disposing) { if (disposing) { motion.Stop(); motion.Dispose(); motionClock.Stop(); if (menu != null) menu.Dispose(); } base.Dispose(disposing); }
    }
}
