using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopTidy.Core;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    internal sealed class WorkspaceCanvas : Panel
    {
        private Rectangle dropSlot;
        private string emptyNotice;
        internal string EmptyNotice { get { return emptyNotice; } set { if (emptyNotice == value) return; emptyNotice = value; Invalidate(); } }
        private Theme theme = Theme.Read();
        internal Rectangle DropSlot { get { return dropSlot; } set { if (dropSlot == value) return; dropSlot = value; Invalidate(); } }
        internal WorkspaceCanvas()
        { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        internal void SetTheme(Theme value) { theme = value; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!string.IsNullOrEmpty(emptyNotice)) TextRenderer.DrawText(e.Graphics, emptyNotice, Font, new Rectangle(16, 20, Math.Max(1, Width - 32), 100), theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            if (dropSlot.IsEmpty) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            SurfaceDrawing.Fill(e.Graphics, dropSlot, 12, Color.FromArgb(SystemInformation.HighContrast ? 255 : 24, theme.Accent));
            Rectangle outline = dropSlot; outline.Inflate(-2, -2);
            using (var path = SurfaceDrawing.Rounded(outline, 12))
            using (var pen = new Pen(Color.FromArgb(SystemInformation.HighContrast ? 255 : 140, theme.Accent), 2))
            { pen.DashStyle = DashStyle.Dash; e.Graphics.DrawPath(pen, path); }
        }
    }

    internal sealed class WorkspaceForm : Form
    {
        private readonly WorkspaceController owner;
        private readonly Font titleFont, detailFont;
        private Theme sidebarTheme = Theme.Read();
        private readonly WorkspaceCanvas canvas = new WorkspaceCanvas { AutoScroll = false };
        private readonly WorkspaceViewport viewport;
        private readonly WorkspaceViewport sidebarViewport;
        private readonly FlowLayoutPanel sidebar = new FlowLayoutPanel { Width = 210, Padding = new Padding(20, 8, 16, 8), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false };
        private readonly TextBox search = new TextBox { Width = 130, MaxLength = 260, AccessibleName = "搜索图标名称" };
        private readonly RoundedButton countSortButton, collapseButton, refreshButton, clearSearchButton, organizeButton, settingsButton, desktopIconsButton;
        private readonly Label feedback = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(20, 9, 20, 8), AutoEllipsis = true, AccessibleName = "工作区操作反馈" };
        private ContextMenuStrip settingsMenu, organizeMenu;
        private readonly ToolTip sidebarTips = new ToolTip { ShowAlways = true };
        private readonly Label searchSummary = new Label { Width = 164, Height = 36, Margin = new Padding(0, 0, 0, 2), Tag = "muted", AutoEllipsis = true };
        private bool desktopRequested, recovering, moving, resizing;
        private Rectangle desktopBounds, gestureBounds;
        private Point gestureStart;
        private readonly Timer hostWatch = new Timer { Interval = 2000 };
        internal Panel Canvas { get { return canvas; } }
        internal int CanvasViewWidth { get { return viewport.ViewWidth; } }
        internal int CanvasViewHeight { get { return Math.Max(140, viewport.ViewHeight); } }
        internal void ResetScroll() { viewport.ScrollTo(Point.Empty); }
        internal void RefreshScroll() { viewport.UpdateContent(); }
        internal void SetDropSlot(Rectangle bounds) { canvas.DropSlot = bounds; }
        internal void SetEmptyNotice(string text) { canvas.EmptyNotice = text; }
        internal WorkspaceForm(WorkspaceController owner, Font font)
        {
            this.owner = owner; Font = font;
            titleFont = new Font(font.FontFamily, 14F, FontStyle.Bold);
            detailFont = new Font(font.FontFamily, 9F); search.Font = font; searchSummary.Font = detailFont; Icon = AppBrand.Icon; Text = "桌面整理 · 工作区";
            AccessibleName = Text; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = false;
            AutoScaleMode = AutoScaleMode.None; MinimumSize = new Size(640, 460);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(work.Left + 24, work.Top + 24, Math.Min(1180, work.Width - 48), Math.Min(780, work.Height - 48));
            desktopBounds = owner.RestoreWorkspaceBounds(Bounds);
            Rectangle restoredWork = Screen.FromRectangle(desktopBounds).WorkingArea;
            MinimumSize = new Size(Math.Min(640, restoredWork.Width), Math.Min(460, restoredWork.Height));
            Bounds = desktopBounds;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            viewport = new WorkspaceViewport(canvas) { Dock = DockStyle.Fill };
            sidebarViewport = new WorkspaceViewport(sidebar) { Dock = DockStyle.Left, Width = 226 };
            Controls.Add(viewport); Controls.Add(feedback); Controls.Add(sidebarViewport);
            viewport.SizeChanged += delegate { if (!resizing) owner.ReflowGroups(false); };
            var header = new Label { Text = "工作区", Font = titleFont, Width = 164, Height = 28, Margin = new Padding(0, 0, 0, 4), Cursor = Cursors.SizeAll, AccessibleName = "拖动工作区" };
            WireGesture(header, false); sidebar.Controls.Add(header);
            sidebar.Controls.Add(new Label { Text = "搜索  /  Ctrl+F", Tag = "muted", Font = detailFont, Width = 164, Height = 18, Margin = new Padding(0, 0, 0, 2) });
            search.Text = owner.SearchText;
            search.TextChanged += delegate { owner.SetSearch(search.Text); };
            search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { search.Clear(); e.Handled = true; e.SuppressKeyPress = true; } };
            var searchRow = new Panel { Width = 164, Height = Math.Max(32, search.PreferredHeight + 4), Margin = new Padding(0, 0, 0, 4) };
            search.BorderStyle = BorderStyle.None;
            search.Location = new Point(10, 7); search.Width = 116;
            searchRow.Height = Math.Max(36, search.PreferredHeight + 14);
            searchRow.Paint += delegate(object sender, PaintEventArgs e)
            {
                Theme palette = sidebarTheme;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                SurfaceDrawing.Outline(e.Graphics, new Rectangle(1, 1, searchRow.Width - 3, searchRow.Height - 3), 7, search.Focused ? palette.Accent : palette.Stroke);
            };
            search.GotFocus += delegate { searchRow.Invalidate(); };
            search.LostFocus += delegate { searchRow.Invalidate(); };
            clearSearchButton = new RoundedButton { Text = "×", AccessibleName = "清除搜索并显示全部图标", Width = 28, Height = searchRow.Height - 4, Left = 132, Top = 2, Secondary = true, Quiet = true, ToolGlyph = "clear" };
            sidebarTips.SetToolTip(clearSearchButton, "清除搜索 · Esc");
            clearSearchButton.Click += delegate { search.Clear(); search.Focus(); };
            searchRow.Controls.Add(search); searchRow.Controls.Add(clearSearchButton);
            sidebar.Controls.Add(searchRow); sidebar.Controls.Add(searchSummary);
            AddSection("整理");
            countSortButton = AddButton("数量↓", owner.ToggleCountSort); countSortButton.ToolGlyph = "sort";
            collapseButton = AddButton("折叠", owner.ToggleGroupsCollapsed); collapseButton.ToolGlyph = "fold";
            refreshButton = AddButton("刷新", owner.RequestRefresh); refreshButton.ToolGlyph = "refresh"; refreshButton.AccessibleName = "刷新桌面图标";
            organizeButton = AddButton("排列", ShowOrganize); organizeButton.ToolGlyph = "arrange"; organizeButton.MenuIndicator = true; organizeButton.AccessibleName = "排列与撤销";
            AddSection("工作区");
            desktopIconsButton = AddButton("隐藏图标", owner.ToggleDesktopIcons); desktopIconsButton.ToolGlyph = "hide";
            desktopIconsButton.AccessibleName = "显示或隐藏原生桌面图标";
            sidebarTips.SetToolTip(desktopIconsButton, "仅切换原生桌面图标；退出软件恢复操作前的显隐状态，不影响分类框与文件");
            settingsButton = AddButton("设置", ShowSettings); settingsButton.ToolGlyph = "settings"; settingsButton.MenuIndicator = true; settingsButton.AccessibleName = "工作区设置";
            sidebarTips.SetToolTip(countSortButton, "按全部图标数量排序，不按搜索匹配数；排序前清除搜索；一次性动作，可撤销");
            sidebarTips.SetToolTip(search, "仅按图标名称搜索；Esc或×返回全部分类，搜索不会保存为布局");
            sidebarTips.SetToolTip(organizeButton, "分类框排列、名称排列、撤销");
            sidebarTips.SetToolTip(settingsButton, "不透明度、启动设置、分类管理");
            var hideButton = AddButton("收起", owner.RequestHide); hideButton.ToolGlyph = "hide";
            hideButton.AccessibleName = "隐藏工作区，确认后从托盘恢复";
            sidebarTips.SetToolTip(hideButton, "隐藏工作区；从系统托盘恢复，不影响原文件");
            sidebarTips.SetToolTip(collapseButton, "折叠或展开全部分类框，不隐藏工作区");
            var grip = new Label { Text = "↘ 调整大小", Tag = "muted", Width = 164, Height = 22, Margin = new Padding(0), Cursor = Cursors.SizeNWSE, AccessibleName = "调整工作区大小" };
            WireGesture(grip, true); sidebar.Controls.Add(grip);
            UpdateSearchSummary();
            hostWatch.Tick += delegate
            {
                SyncDesktopIcons();
                if (desktopRequested && !moving && !resizing) RecoverVisibleBounds();
                if (!desktopRequested || recovering || DesktopAttachment.IsAttached(this)) return;
                recovering = true;
                try
                {
                    // Explorer parent destruction invalidates child HWNDs. Recreate only our window.
                    base.SetVisibleCore(false); RecreateHandle();
                    if (DesktopAttachment.Attach(this, desktopBounds.Location) && DesktopAttachment.Position(this, desktopBounds)) { Show(); RestoreCards(); }
                    else { owner.Hide(); owner.Notify("桌面宿主不可用，工作区已安全隐藏；Explorer恢复后从托盘重新显示。"); }
                }
                finally { recovering = false; }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; owner.RequestHide(); } };
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.F) { FocusSearch(); e.Handled = true; e.SuppressKeyPress = true; } else if (e.Control && e.KeyCode == Keys.Z && !search.Focused) { owner.Undo(); e.Handled = true; } };
            Sync(); SyncDesktopIcons(); RefreshTheme();
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                if (desktopRequested)
                {
                    IntPtr host = DesktopAttachment.FindHost();
                    if (host != IntPtr.Zero)
                    {
                        // Create in the desktop hierarchy directly: avoid cross-process SetParent DPI reset.
                        parameters.Parent = host;
                        parameters.Style = (parameters.Style & unchecked((int)~0x80000000U)) | 0x40000000;
                    }
                }
                return parameters;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!desktopRequested) return;
            DesktopAttachment.Attach(this, desktopBounds.Location);
            // WinForms can reset parent/style after OnHandleCreated during RecreateHandle.
            BeginInvoke((Action)delegate
            {
                if (IsDisposed || !desktopRequested) return;
                base.SetVisibleCore(false);
                if (DesktopAttachment.Attach(this, desktopBounds.Location) && DesktopAttachment.Position(this, desktopBounds)) { Show(); RestoreCards(); }
                else { owner.Hide(); owner.Notify("桌面挂载失效，工作区已安全隐藏。"); }
            });
        }
        protected override void SetVisibleCore(bool value)
        {
            // Never permit a re-created or failed-to-attach workspace to surface as a floating window.
            if (value && desktopRequested && !DesktopAttachment.IsAttached(this)) value = false;
            base.SetVisibleCore(value);
        }
        internal bool PrepareDesktop()
        {
            Rectangle work = Screen.FromRectangle(desktopBounds).WorkingArea;
            Rectangle next = WorkspaceController.FitWorkspaceBounds(desktopBounds, work);
            MinimumSize = new Size(Math.Min(640, work.Width), Math.Min(460, work.Height));
            desktopBounds = next;
            desktopRequested = true;
            if (!DesktopAttachment.Attach(this, desktopBounds.Location) || !DesktopAttachment.Position(this, desktopBounds))
            { HideDesktop(); return false; }
            return true;
        }
        internal bool ShowDesktop()
        {
            if (!PrepareDesktop()) return false;
            Show(); RestoreCards();
            if (!desktopRequested || !Visible) return false;
            hostWatch.Start(); return true;
        }
        private void RestoreCards()
        {
            // Embedded Forms do not automatically regain visibility after their parent is hidden.
            foreach (Control control in canvas.Controls)
            {
                var card = control as ClassificationBox;
                if (card == null || card.IsDisposed) continue;
                if (!owner.ShouldDisplay(card)) { card.Hide(); continue; }
                card.Display();
                if (!DesktopAttachment.RestoreChild(card, canvas))
                { owner.Hide(); owner.Notify("分类框窗口恢复失败，工作区已安全隐藏。"); return; }
            }
        }
        internal void HideDesktop()
        {
            desktopRequested = false; hostWatch.Stop(); Hide();
        }
        internal void PersistBounds() { owner.RememberWorkspaceBounds(desktopBounds); }
        private void RecoverVisibleBounds()
        {
            Rectangle work = Screen.FromRectangle(desktopBounds).WorkingArea;
            Rectangle next = WorkspaceController.FitWorkspaceBounds(desktopBounds, work);
            if (next == desktopBounds) return;
            MinimumSize = new Size(Math.Min(640, work.Width), Math.Min(460, work.Height));
            if (DesktopAttachment.Position(this, next)) { desktopBounds = next; owner.ReflowGroups(false); PersistBounds(); }
        }
        private void FinishGesture(Control control)
        {
            bool changed = resizing, touched = moving || resizing; moving = resizing = false;
            control.Capture = false;
            if (!touched) return;
            PersistBounds(); if (changed) owner.ReflowGroups(true, true);
        }
        private void WireGesture(Control control, bool resize)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                gestureStart = Cursor.Position; gestureBounds = desktopBounds;
                resizing = resize; moving = !resize; control.Capture = true;
            };
            control.MouseMove += delegate
            {
                if (!moving && !resizing) return;
                Point cursor = Cursor.Position;
                Rectangle next = gestureBounds;
                if (resizing)
                {
                    next.Width = Math.Max(MinimumSize.Width, gestureBounds.Width + cursor.X - gestureStart.X);
                    next.Height = Math.Max(MinimumSize.Height, gestureBounds.Height + cursor.Y - gestureStart.Y);
                }
                else next.Offset(cursor.X - gestureStart.X, cursor.Y - gestureStart.Y);
                Rectangle work = Screen.FromPoint(cursor).WorkingArea;
                next.Width = Math.Min(next.Width, Math.Max(MinimumSize.Width, work.Width));
                next.Height = Math.Min(next.Height, Math.Max(MinimumSize.Height, work.Height));
                next.X = Math.Max(work.Left, Math.Min(next.X, work.Right - Math.Min(next.Width, work.Width)));
                next.Y = Math.Max(work.Top, Math.Min(next.Y, work.Bottom - Math.Min(next.Height, work.Height)));
                if (DesktopAttachment.Position(this, next)) desktopBounds = next;
            };
            control.MouseUp += delegate { FinishGesture(control); };
            control.MouseCaptureChanged += delegate { if (!control.Capture) FinishGesture(control); };
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
            {
                desktopBounds.Size = Size;
                SurfaceDrawing.Clip(this, 16);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (settingsMenu != null) settingsMenu.Dispose(); if (organizeMenu != null) organizeMenu.Dispose(); sidebarTips.Dispose(); desktopRequested = false; hostWatch.Stop(); hostWatch.Dispose(); titleFont.Dispose(); detailFont.Dispose(); }
            base.Dispose(disposing);
        }
        internal void ClearSearch() { search.Clear(); }
        internal void FocusSearch() { sidebarViewport.ScrollTo(Point.Empty); search.Focus(); search.SelectAll(); }
        internal void UpdateSearchSummary()
        {
            searchSummary.Text = owner.SearchActive ? string.Format("{0} / {1} 项匹配\n{2}", owner.SearchMatchCount, owner.ItemCount, owner.LastRefreshDescription) : string.Format("{0} 个图标\n{1}", owner.ItemCount, owner.LastRefreshDescription);
            searchSummary.AccessibleName = owner.SearchActive ? "搜索结果" : "图标总数与排列状态";
            searchSummary.AccessibleDescription = searchSummary.Text;
            sidebarTips.SetToolTip(searchSummary, searchSummary.Text + "；" + owner.OrderDescription + "；数量排序始终基于全部图标");
            if (clearSearchButton != null) clearSearchButton.Enabled = owner.SearchActive;
        }
        internal void SetFeedback(string text)
        {
            feedback.Text = text; feedback.AccessibleDescription = text;
            sidebarTips.SetToolTip(feedback, text + "\n撤销上次修改：Ctrl+Z，或排列菜单");
        }
        internal void SyncRefresh()
        {
            refreshButton.Enabled = !owner.IsRefreshing;
            refreshButton.Text = owner.IsRefreshing ? "刷新中…" : "刷新";
            sidebarTips.SetToolTip(refreshButton, owner.LastRefreshDescription + "；刷新保留手动分类与排列");
            UpdateSearchSummary();
        }
        internal void SyncDesktopIcons()
        {
            bool? visible = owner.DesktopIconsVisible;
            desktopIconsButton.Text = visible == false ? "显示图标" : "隐藏图标";
            desktopIconsButton.Enabled = visible.HasValue;
        }
        private ContextMenuStrip CreateOrganizeMenu()
        {
            var menu = new WorkspaceMenu { Font = Font, AccessibleName = "工作区操作菜单" };
            menu.Items.Add(new ToolStripMenuItem("数量排序仅执行一次；拖动后为自定义") { Enabled = false });
            menu.Items.Add("紧凑排列", null, delegate { owner.ArrangeGroups(); });
            menu.Items.Add("名称排列", null, delegate { owner.ArrangeItems(null); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("撤销", null, delegate { owner.Undo(); }) { ShortcutKeyDisplayString = "Ctrl+Z" });
            return menu;
        }
        private void ShowOrganize()
        {
            if (organizeMenu != null) organizeMenu.Dispose();
            organizeMenu = CreateOrganizeMenu();
            organizeMenu.Show(organizeButton, new Point(0, organizeButton.Height));
        }
        private ContextMenuStrip CreateSettingsMenu()
        {
            var menu = new WorkspaceMenu { Font = Font, AccessibleName = "工作区操作菜单" };
            var transparency = new ToolStripMenuItem("不透明度");
            for (int i = 1; i <= 4; i++)
            {
                int value = i * 25;
                var choice = new ToolStripMenuItem(value + "%") { Checked = value == owner.OpacityPercent };
                choice.Click += delegate { owner.CommitOpacity(value); }; transparency.DropDownItems.Add(choice);
            }
            menu.Items.Add(transparency);
            var startup = new ToolStripMenuItem("启动时隐藏原生桌面图标") { Checked = owner.HideIconsOnStartup };
            startup.Click += delegate { owner.SetStartupHiding(!owner.HideIconsOnStartup); }; menu.Items.Add(startup);
            menu.Items.Add("桌面图标显隐", null, delegate { owner.ToggleDesktopIcons(); });
            menu.Items.Add(new ToolStripSeparator());
            var categories = new ToolStripMenuItem("分类管理");
            var empty = new ToolStripMenuItem("隐藏空分类") { Checked = owner.HideEmptyGroups };
            empty.Click += delegate { owner.ToggleEmptyGroups(); }; categories.DropDownItems.Add(empty);
            categories.DropDownItems.Add("新增个人分类…", null, delegate { owner.CreateCategory(); });
            categories.DropDownItems.Add(new ToolStripSeparator());
            foreach (Category value in owner.Categories)
            {
                Category category = value;
                var entry = new ToolStripMenuItem(owner.CategoryTitle(category));
                entry.DropDownItems.Add("重命名…", null, delegate { owner.RenameCategory(category); });
                if (category > Category.Other) entry.DropDownItems.Add("删除分类（不删文件）…", null, delegate { owner.DeleteCategory(category); });
                categories.DropDownItems.Add(entry);
            }
            menu.Items.Add(categories);
            menu.Items.Add(new ToolStripMenuItem("手动归属优先；重命名不改变类型规则") { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem("只整理显示，不搬动原文件") { Enabled = false });
            return menu;
        }
        private void ShowSettings()
        {
            if (settingsMenu != null) settingsMenu.Dispose();
            settingsMenu = CreateSettingsMenu();
            settingsMenu.Show(settingsButton, new Point(0, settingsButton.Height));
        }
        private void AddSection(string text)
        {
            sidebar.Controls.Add(new Label { Text = text, Tag = "muted", Font = detailFont, Width = 164, Height = 18, Margin = new Padding(0) });
        }
        private RoundedButton AddButton(string text, Action click)
        {
            var button = new RoundedButton { Text = text, AccessibleName = text, Width = 164, Height = 28, Margin = new Padding(0), Secondary = true, Quiet = true };
            button.Click += delegate { click(); }; sidebar.Controls.Add(button); return button;
        }
        internal void SyncCommands()
        {
            countSortButton.Text = owner.NextSortDescending ? "数量↓" : "数量↑";
            countSortButton.AccessibleName = "下次按全部图标数量" + (owner.NextSortDescending ? "从多到少排序" : "从少到多排序");
            countSortButton.ToolGlyph = owner.NextSortDescending ? "sort" : "sort-up";
            sidebarTips.SetToolTip(countSortButton, countSortButton.AccessibleName + "；排序前清除搜索；一次性动作，可撤销");
            collapseButton.Text = owner.AllGroupsCollapsed ? "展开" : "折叠";
            collapseButton.AccessibleName = collapseButton.Text + "全部分类框";
            collapseButton.ToolGlyph = owner.AllGroupsCollapsed ? "expand" : "fold"; UpdateSearchSummary();
        }
        internal void Sync()
        { SyncCommands(); SyncRefresh(); ApplyOpacity(owner.OpacityPercent); }
        internal void ApplyOpacity(int value)
        {
            double next = SystemInformation.HighContrast ? 1.0 : value / 100.0;
            if (Math.Abs(Opacity - next) < 0.001) return;
            bool wasVisible = desktopRequested && Visible;
            // WinForms rewrites native window styles on layered/non-layered transitions.
            // Hide first, then reapply the desktop parent before allowing visibility again.
            if (wasVisible) base.SetVisibleCore(false);
            Opacity = next;
            if (!wasVisible) return;
            if (DesktopAttachment.Attach(this, desktopBounds.Location) && DesktopAttachment.Position(this, desktopBounds)) { Show(); RestoreCards(); }
            else { owner.Hide(); owner.Notify("透明度切换后桌面挂载失败，工作区已安全隐藏。"); }
        }
        private static void ApplySidebarTheme(Control control, Theme theme)
        {
            control.BackColor = theme.Surface; control.ForeColor = (control.Tag as string) == "muted" ? theme.Muted : theme.Text;
            var button = control as RoundedButton; if (button != null) button.SetTheme(theme);
            foreach (Control child in control.Controls) ApplySidebarTheme(child, theme);
        }
        internal void RefreshTheme()
        {
            Theme theme = Theme.Read(); sidebarTheme = theme; BackColor = theme.Background; ForeColor = theme.Text;
            canvas.BackColor = theme.Background; viewport.SetTheme(theme); canvas.SetTheme(theme); sidebarViewport.SetTheme(theme, theme.Surface);
            ApplySidebarTheme(sidebar, theme);
            feedback.BackColor = theme.Surface; feedback.ForeColor = theme.Text;
            ApplyOpacity(owner.OpacityPercent); Invalidate();
        }
    }
}
