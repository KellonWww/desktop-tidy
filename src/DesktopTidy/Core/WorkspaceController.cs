using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DesktopTidy.UI;
using DesktopTidy.Windows;

namespace DesktopTidy.Core
{
    internal sealed class WorkspaceController : IDisposable
    {
        private NotesStore notes;
        internal NotesStore Notes { get { return notes ?? (notes = new NotesStore()); } set { notes = value; } }
        internal string WebProfileDirectory { get; set; }
        internal bool EdgeNotesEnabled { get; set; }
        internal Action ToggleEdgeNotes;
        internal int EdgeNotesDelay = 600;
        internal string EdgeNotesScreen = "all";
        internal bool EdgeNotesPaused;
        internal string EdgeNotesDockSide = "Left";
        internal string EdgeNotesDockScreen = "";
        internal string EdgeNotesDockStatus = "已停靠左侧 · 拖动顶部可换边";
        internal Action<int, string> ConfigureEdgeNotes;
        internal Action PauseEdgeNotes;
        private readonly LayoutStore store;
        private readonly Font font;
        private readonly Action<string> notify;
        private readonly ShellIconCache icons = new ShellIconCache();
        private readonly List<ClassificationBox> boxes = new List<ClassificationBox>();
        private List<ClassificationBox> dragOrder;
        private string searchText = string.Empty;
        private bool nextSortDescending = true;
        internal bool NextSortDescending { get { return nextSortDescending; } }
        internal bool AllGroupsCollapsed { get { return layout.Groups.Count > 0 && layout.Groups.TrueForAll(delegate(GroupState group) { return group.Collapsed; }); } }
        internal string SearchText { get { return searchText; } }
        internal bool SearchActive { get { return searchText.Length != 0; } }
        internal int ItemCount { get { return items.Count; } }
        internal int SearchMatchCount { get { return items.FindAll(MatchesSearch).Count; } }
        private LayoutState layout;
        private WorkspaceForm container;
        private ClassificationBox searchBox;
        private Form categoryEditor;
        private bool refreshing;
        internal Action RefreshRequested;
        internal bool IsRefreshing { get { return refreshing; } }
        internal string OrderDescription { get { return layout.OrderMode == CategoryOrderMode.CountDescending ? "数量降序（一次）" : layout.OrderMode == CategoryOrderMode.CountAscending ? "数量升序（一次）" : "自定义排列"; } }
        internal string LastRefreshDescription { get { return layout.LastRefreshUtcTicks == 0 ? "尚未刷新" : "更新 " + new DateTime(layout.LastRefreshUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm:ss"); } }
        private readonly DesktopIconVisibility desktopVisibility = new DesktopIconVisibility();
        internal int NoteFontSize { get { return layout.NoteFontSize; } }
        internal int OpacityPercent { get { return layout.OpacityPercent; } }
        internal bool HideEmptyGroups { get { return layout.HideEmptyGroups; } }
        internal bool HideIconsOnStartup { get { return layout.HideIconsOnStartup; } }
        internal bool NeedsFirstRunGuide { get { return !layout.FirstRunCompleted; } }
        private List<WorkspaceItem> items = new List<WorkspaceItem>();
        private bool disposed, showing, startupHidingApplied, arranging;
        private DesktopReadResult snapshot;
        internal bool Visible { get { return showing && boxes.Count > 0; } }
        internal WorkspaceController(Font font, Action<string> notify, string directory)
        {
            this.font = font; this.notify = notify; store = new LayoutStore(directory); layout = store.Load();
            EnsureGroups(); nextSortDescending = layout.OrderMode != CategoryOrderMode.CountDescending;
            itemChanges.Tick += ApplyItemChanges;
        }
        private void EnsureGroups()
        {
            layout.Groups.RemoveAll(delegate(GroupState group) { return group.Category == Category.Installers; });
            foreach (var assignment in layout.Assignments)
                if (assignment.Category == Category.Installers) assignment.Category = Category.Shortcuts;
            foreach (var group in layout.Groups)
                if (group.Category == Category.Shortcuts && group.Title == "程序与快捷方式 · 待分类") group.Title = null;
            int columns = 3, width = 280, height = 260;
            foreach (Category category in Enum.GetValues(typeof(Category)))
            {
                if (Classification.IsCustom(category) || category == Category.Installers || layout.DeletedCategories.Contains(category)) continue;
                bool found = layout.Groups.Exists(delegate(GroupState group) { return group.Category == category; });
                int index = layout.Groups.Count;
                if (!found) layout.Groups.Add(new GroupState { Category = category, X = 12 + index % columns * (width + 12), Y = 12 + index / columns * (height + 12), Width = width, Height = height });
            }
        }
        internal void Show(DesktopReadResult result)
        {
            if (disposed) return;
            if (result == null || !result.Success) { Notify("请先完成桌面扫描，再显示分类窗口。"); return; }
            if (!EnsureFirstRunGuide()) return;
            int skipped;
            if (!UpdateSnapshot(result, out skipped)) return;
            if (boxes.Exists(delegate(ClassificationBox box) { return box.IsDisposed; })) CloseBoxes();
            if (boxes.Count == 0)
            {
                foreach (var group in layout.Groups)
                {
                    if (container == null)
                    {
                        container = new WorkspaceForm(this, font);
                        if (!container.PrepareDesktop()) { desktopVisibility.Restore(); CloseBoxes(); Notify("桌面层不可用，工作区未显示；请从托盘重试。"); return; }
                    }
                    var box = new ClassificationBox(this, group, icons, font); boxes.Add(box); container.Canvas.Controls.Add(box);
                    box.SetItems(items.FindAll(delegate(WorkspaceItem item) { return item.Category == group.Category; })); box.Display();
                }
            }
            else { RefreshItems(); foreach (var box in boxes) box.Display(); }
            ReflowGroups(false);
            container.Sync();
            if (!container.ShowDesktop()) { showing = false; desktopVisibility.Restore(); Notify("无法挂载桌面层，工作区未显示；不会回退为遮挡应用的浮窗。"); return; }
            showing = true;
            bool hideOnThisShow = !startupHidingApplied && layout.HideIconsOnStartup; startupHidingApplied = true;
            icons.Load(items, delegate
            {
                // Posting uses a private HWND, never a disposed Form.BeginInvoke target.
                if (IconReady != null) IconReady();
            });
            Notify(string.Format("{0} · {1} 个图标，跳过 {2} 项；双击打开，原文件不移动。", LastRefreshDescription, items.Count, skipped));
            if (hideOnThisShow) HideDesktopIcons();
        }
        internal bool CompleteFirstRunChoice(bool hideNativeIcons)
        {
            bool previousCompleted = layout.FirstRunCompleted, previousHide = layout.HideIconsOnStartup;
            layout.FirstRunCompleted = true; layout.HideIconsOnStartup = hideNativeIcons;
            if (Save()) return true;
            layout.FirstRunCompleted = previousCompleted; layout.HideIconsOnStartup = previousHide; return false;
        }
        private bool EnsureFirstRunGuide()
        {
            if (!NeedsFirstRunGuide) return true;
            using (var dialog = new WorkspaceDialog { Text = "第一次使用桌面整理", Font = font, Icon = AppBrand.Icon,
                ClientSize = new Size(480, 240), FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen,
                AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi })
            {
                var explanation = new Label { Left = 20, Top = 18, Width = 440, Height = 84,
                    Text = "分类与拖动只改变显示；图标右键可操作真实文件。\n双击打开，右键可重命名、删除或修改快捷方式属性。\n可从托盘显示/隐藏工作区；隐藏或退出会恢复原生图标的原状态。" };
                var hide = new CheckBox { Left = 20, Top = 108, Width = 440, Height = 48, Checked = false,
                    Text = "工作区显示成功后隐藏原生桌面图标（默认保留）", AccessibleName = "是否隐藏原生桌面图标，默认不隐藏" };
                var enter = new RoundedButton { Height = 32, Text = "进入工作区", Left = 230, Top = 184, Width = 110, DialogResult = DialogResult.OK };
                var cancel = new RoundedButton { Height = 32, Secondary = true, Text = "暂不进入", Left = 350, Top = 184, Width = 110, DialogResult = DialogResult.Cancel };
                dialog.Controls.AddRange(new Control[] { explanation, hide, enter, cancel }); dialog.AcceptButton = enter; dialog.CancelButton = cancel;
                dialog.ApplyTheme();
                if (dialog.ShowDialog() != DialogResult.OK) return false;
                return CompleteFirstRunChoice(hide.Checked);
            }
        }
        internal bool UpdateSnapshot(DesktopReadResult result, out int skipped)
        {
            skipped = 0;
            if (result == null || !result.Success || result.Icons == null) return false;
            var next = Classification.Build(result.Icons, layout, out skipped);
            if (!ReferenceEquals(snapshot, result))
            {
                if (!store.BackupDesktop(result)) { Notify(store.Error); return false; }
                long previous = layout.LastRefreshUtcTicks; layout.LastRefreshUtcTicks = DateTime.UtcNow.Ticks;
                if (!Save()) { layout.LastRefreshUtcTicks = previous; return false; }
                snapshot = result;
            }
            items = next; return true;
        }
        internal List<GroupState> WebGroups { get { return layout.Groups; } }
        internal List<WorkspaceItem> WebItems { get { return items; } }
        internal void DrawWebIcon(Graphics graphics, WorkspaceItem item, Rectangle bounds) { icons.Draw(graphics, item, bounds); }
        internal void ResizeWebCategory(Category category, int width, int height)
        {
            GroupState state = layout.Groups.Find(delegate(GroupState value) { return value.Category == category; });
            if (state == null) return;
            int nextWidth = Math.Max(220, Math.Min(2000, width));
            int nextHeight = Math.Max(140, Math.Min(2000, height));
            if (state.Width == nextWidth && state.Height == nextHeight) return;
            if (!Checkpoint()) return;
            int previousWidth = state.Width, previousHeight = state.Height;
            state.Width = nextWidth; state.Height = nextHeight;
            if (!Save()) { state.Width = previousWidth; state.Height = previousHeight; return; }
            ClassificationBox box = boxes.Find(delegate(ClassificationBox value) { return value.State.Category == category; });
            if (box != null) box.ApplyState();
            Notify("分类框尺寸已保存并自动对齐；可撤销。");
        }
        internal void ToggleWebCategory(Category category)
        {
            var box = boxes.Find(delegate(ClassificationBox value) { return value.State.Category == category; });
            if (box != null) Toggle(box);
        }
        internal void ReorderWebCategory(Category category, string before)
        {
            int targetId;
            if (!int.TryParse(before, out targetId) || category == (Category)targetId) return;
            var source = boxes.Find(delegate(ClassificationBox value) { return value.State.Category == category; });
            var target = boxes.Find(delegate(ClassificationBox value) { return value.State.Category == (Category)targetId; });
            if (source == null || target == null || !Checkpoint()) return;
            var original = new List<ClassificationBox>(boxes); CategoryOrderMode previous = layout.OrderMode;
            boxes.Remove(source); boxes.Insert(boxes.IndexOf(target), source);
            layout.Groups.Clear(); foreach (var box in boxes) layout.Groups.Add(box.State);
            layout.OrderMode = CategoryOrderMode.Custom;
            if (!Save())
            {
                boxes.Clear(); boxes.AddRange(original); layout.Groups.Clear();
                foreach (var box in boxes) layout.Groups.Add(box.State); layout.OrderMode = previous;
            }
            nextSortDescending = true; ReflowGroups(false, true); if (container != null) container.SyncCommands();
        }
        internal Action IconReady;
        internal void InvalidateIcons() { if (container != null) container.RefreshWebIcons(); if (searchBox != null && !searchBox.IsDisposed) searchBox.Invalidate(); foreach (var box in boxes) if (!box.IsDisposed) box.Invalidate(); }
        internal void RefreshTheme() { if (searchBox != null && !searchBox.IsDisposed) searchBox.RefreshTheme(); if (container != null) container.RefreshTheme(); foreach (var box in boxes) if (!box.IsDisposed) box.RefreshTheme(); }
        internal void SetThemeOverride(bool? dark)
        {
            if (SystemInformation.HighContrast) { Notify("系统高对比度已启用，当前由 Windows 接管颜色设置。"); return; }
            if (!Theme.SetOverride(dark)) { Notify("主题设置保存失败，请检查当前 Windows 用户的注册表权限。"); return; }
            RefreshTheme();
            Notify(dark.HasValue ? dark.Value ? "已切换深色主题。" : "已切换浅色主题。" : "主题已改为跟随 Windows 应用模式。");
        }
        private void RefreshItems()
        {
            foreach (var item in items)
            {
                item.Category = Classification.ResolveCategory(item.AutomaticCategory, layout);
                foreach (var assignment in layout.Assignments)
                    if (string.Equals(assignment.Identity, item.Identity, StringComparison.OrdinalIgnoreCase)) { item.Category = Classification.ResolveCategory(assignment.Category, layout); break; }
            }
            RefreshSearchResults();
        }
        private bool MatchesSearch(WorkspaceItem item)
        {
            return !SearchActive || (item.Name ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private void RefreshSearchResults()
        {
            foreach (var box in boxes)
                box.SetItems(items.FindAll(delegate(WorkspaceItem item) { return item.Category == box.State.Category; }));
            if (container != null) { container.UpdateSearchSummary(); ReflowGroups(false); }
        }
        internal bool ShouldDisplay(ClassificationBox box)
        {
            return box.IsSearchResults ? SearchActive : !SearchActive && (!layout.HideEmptyGroups || items.Exists(delegate(WorkspaceItem item) { return item.Category == box.State.Category; }));
        }
        private void UpdateSearchView()
        {
            if (container == null) return;
            foreach (var box in boxes)
            { bool visible = ShouldDisplay(box); box.Tag = visible ? null : "layout-hidden"; if (visible) box.Display(); else box.Hide(); }
            if (SearchActive)
            {
                if (searchBox == null)
                {
                    searchBox = new ClassificationBox(this, new GroupState { Category = Category.Other, SortByName = true }, icons, font)
                    { IsSearchResults = true, AllowDrop = false, ContextMenuStrip = null, AccessibleName = "搜索结果，双击打开图标" };
                    container.Canvas.Controls.Add(searchBox);
                }
                searchBox.Tag = null;
                searchBox.State.X = searchBox.State.Y = 12;
                searchBox.State.Width = Math.Max(220, container.CanvasViewWidth - 24);
                searchBox.State.Height = Math.Max(140, container.CanvasViewHeight - 24);
                searchBox.AccessibleDescription = string.Format("匹配 {0} 项；双击打开，Esc或标题×返回全部分类；搜索不保存为布局", SearchMatchCount);
                searchBox.SetItems(items.FindAll(MatchesSearch)); searchBox.Display();
            }
            else if (searchBox != null) { searchBox.Tag = "layout-hidden"; searchBox.Hide(); }
            container.SetEmptyNotice(!SearchActive && !boxes.Exists(ShouldDisplay) ? "没有可显示的分类\n工作区设置 → 分类管理 → 取消隐藏空分类，或刷新桌面图标" : null);
            container.RefreshScroll();
        }
        internal void ClearSearch() { if (container != null) container.ClearSearch(); else SetSearch(string.Empty); }
        private bool itemOperation;
        private readonly Dictionary<string, FileSystemWatcher> itemWatchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> changedItemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer itemChanges = new Timer { Interval = 500 };
        private void WatchItemChanges(WorkspaceItem item)
        {
            string directory = Path.GetDirectoryName(item.Path);
            if (itemWatchers.ContainsKey(directory)) return;
            FileSystemWatcher watcher = null;
            try
            {
                watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                watcher.Changed += delegate(object sender, FileSystemEventArgs e) { QueueItemChange(e.FullPath); };
                watcher.Created += delegate(object sender, FileSystemEventArgs e) { QueueItemChange(e.FullPath); };
                watcher.Deleted += delegate(object sender, FileSystemEventArgs e) { QueueItemChange(e.FullPath); };
                watcher.Renamed += delegate(object sender, RenamedEventArgs e) { QueueItemChange(e.OldFullPath); QueueItemChange(e.FullPath); };
                watcher.Error += delegate { QueueItemChange(string.Empty); };
                watcher.EnableRaisingEvents = true; itemWatchers.Add(directory, watcher);
            }
            catch (Exception error)
            {
                if (watcher != null) watcher.Dispose();
                if (!ExpectedItemError(error)) throw;
                Notify("文件变化监测不可用，修改属性后请点击刷新。");
            }
        }
        private void QueueItemChange(string path)
        {
            var target = container;
            if (disposed || target == null || target.IsDisposed || !target.IsHandleCreated) return;
            try
            {
                target.BeginInvoke((Action)delegate
                {
                    if (disposed) return;
                    changedItemPaths.Add(path); itemChanges.Stop(); itemChanges.Start();
                });
            }
            catch (InvalidOperationException) { /* Window was destroyed between checking and posting. */ }
        }
        private void ApplyItemChanges(object sender, EventArgs e)
        {
            if (disposed) { itemChanges.Stop(); return; }
            if (itemOperation || refreshing) return;
            itemChanges.Stop();
            foreach (var item in items)
                if (changedItemPaths.Contains(item.Path) || changedItemPaths.Contains(string.Empty))
                    icons.Refresh(item, delegate { if (IconReady != null) IconReady(); });
            changedItemPaths.Clear(); InvalidateIcons(); RequestRefresh();
        }
        private bool CurrentFileItem(WorkspaceItem item)
        {
            if (disposed || item == null || !items.Contains(item)) return false;
            if (File.Exists(item.Path) || Directory.Exists(item.Path)) return true;
            Notify("文件已不存在，请刷新工作区。"); RequestRefresh(); return false;
        }
        internal void ShowItemMenu(WorkspaceItem item, Point screenPosition)
        {
            if (itemOperation || !CurrentFileItem(item)) return;
            itemOperation = true;
            try
            {
                WatchItemChanges(item);
                ShellItemMenu.Show(item.Path, screenPosition, delegate { RenameItem(item); },
                    delegate { DeleteItem(item); }, delegate { RefreshChangedItem(item); });
            }
            catch (Exception error)
            {
                if (!ExpectedItemError(error)) throw;
                Notify("系统菜单操作未完成：" + error.Message);
            }
            finally { itemOperation = false; }
        }
        private static bool ExpectedItemError(Exception error)
        {
            return error is System.Runtime.InteropServices.COMException || error is IOException ||
                error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception ||
                error is ArgumentException || error is NotSupportedException || error is System.Security.SecurityException;
        }
        private void RefreshChangedItem(WorkspaceItem item)
        {
            if (disposed) return;
            icons.Refresh(item, delegate { if (IconReady != null) IconReady(); });
            InvalidateIcons(); RequestRefresh();
        }
        private void DeleteItem(WorkspaceItem item)
        {
            if (!CurrentFileItem(item)) return;
            string message = "将删除真实桌面项目：" + item.Name + "\n优先移入回收站；无法回收时，系统会另行确认。\n删除快捷方式不会卸载目标程序。\n工作区 Ctrl+Z 不能恢复文件，请到回收站还原。";
            if (!WorkspaceDialog.Confirm("删除桌面项目", message, "删除", font)) return;
            try
            {
                using (var dialogOwner = new WorkspaceDialog { Opacity = 0, ClientSize = new Size(1, 1) })
                {
                    dialogOwner.Show();
                    if (!ShellItemMenu.Recycle(item.Path, dialogOwner.Handle))
                    { Notify("删除已取消或未完成，图标保持不变。"); RequestRefresh(); return; }
                }
                if (File.Exists(item.Path) || Directory.Exists(item.Path))
                { Notify("文件未删除，图标保持不变。"); return; }
                items.Remove(item); RefreshItems(); RequestRefresh();
                Notify("已删除桌面项目；如已进入回收站，可在回收站还原。工作区撤销不恢复文件。");
            }
            catch (Exception error)
            {
                if (!ExpectedItemError(error)) throw;
                Notify("删除未完成：" + error.Message); RequestRefresh();
            }
        }
        private void RenameItem(WorkspaceItem item)
        {
            if (!CurrentFileItem(item)) return;
            string original = Path.GetFileName(item.Path);
            using (var dialog = new WorkspaceDialog { Text = "重命名桌面项目", Font = font, ClientSize = new Size(500, 304) })
            {
                dialog.AddHeading("重命名项目", "修改桌面上的真实文件名，请保留需要的扩展名。");
                var label = new Label { Text = "文件名称", Left = 24, Top = 102, Width = 452, Height = 22 };
                var field = new WorkspaceTextField(original, 255, "真实文件名，包含扩展名") { Left = 22, Top = 128, Width = 456, TabIndex = 0 };
                var input = field.Editor;
                var hint = new Label { Tag = "muted", Text = "修改真实文件名（包含扩展名）；工作区 Ctrl+Z 不撤销此操作。", Left = 24, Top = 184, Width = 452, Height = 48 };
                var confirm = new RoundedButton { Text = "重命名", Left = 380, Top = 254, Width = 96, Height = 36, TabIndex = 2 };
                var cancel = new RoundedButton { Text = "取消", Secondary = true, Left = 272, Top = 254, Width = 96, Height = 36, TabIndex = 1, DialogResult = DialogResult.Cancel };
                confirm.Click += delegate
                {
                    if (!CurrentFileItem(item)) { dialog.Close(); return; }
                    string name = input.Text;
                    if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                        name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
                    { field.Invalid = true; hint.Text = "请输入合法文件名，不能包含路径、非法字符或以点、空格结尾。"; return; }
                    if (name == original) { dialog.Close(); return; }
                    string destination = Path.Combine(Path.GetDirectoryName(item.Path), name);
                    if (!string.Equals(item.Path, destination, StringComparison.OrdinalIgnoreCase) && (File.Exists(destination) || Directory.Exists(destination)))
                    { field.Invalid = true; hint.Text = "同名项目已存在，请换个名称；不会覆盖原文件。"; return; }
                    if (!item.IsDirectory && !string.Equals(Path.GetExtension(original), Path.GetExtension(name), StringComparison.OrdinalIgnoreCase) &&
                        !WorkspaceDialog.Confirm("更改文件扩展名", "更改扩展名可能导致文件或快捷方式无法正常打开。是否继续？", "仍然更改", font)) return;
                    try
                    {
                        if (!ShellItemMenu.Rename(item.Path, name, dialog.Handle)) { field.Invalid = true; hint.Text = "重命名已取消或未完成。"; return; }
                        if ((!File.Exists(destination) && !Directory.Exists(destination)) ||
                            (!string.Equals(item.Path, destination, StringComparison.OrdinalIgnoreCase) && (File.Exists(item.Path) || Directory.Exists(item.Path))))
                        { field.Invalid = true; hint.Text = "系统未完成重命名，请刷新后重试。"; RequestRefresh(); return; }
                        string previousIdentity = item.Identity;
                        layout.Assignments.RemoveAll(delegate(Assignment assignment) { return string.Equals(assignment.Identity, destination, StringComparison.OrdinalIgnoreCase) && !string.Equals(assignment.Identity, previousIdentity, StringComparison.OrdinalIgnoreCase); });
                        foreach (var assignment in layout.Assignments)
                            if (string.Equals(assignment.Identity, previousIdentity, StringComparison.OrdinalIgnoreCase)) assignment.Identity = destination;
                        item.Identity = item.Path = destination; item.Name = name;
                        bool saved = Save(); RefreshItems(); RefreshChangedItem(item);
                        Notify(saved ? "真实文件已重命名，手动分类已保留；工作区撤销不撤销文件重命名。" : "文件已重命名，但分类保存失败；请检查目录权限后重试保存。");
                        dialog.Close();
                    }
                    catch (Exception error)
                    {
                        if (!ExpectedItemError(error)) throw;
                        field.Invalid = true; hint.Text = "重命名未完成：" + error.Message; RequestRefresh();
                    }
                };
                dialog.Controls.AddRange(new Control[] { label, field, hint, cancel, confirm });
                dialog.AcceptButton = confirm; dialog.CancelButton = cancel; dialog.ApplyTheme();
                dialog.Shown += delegate { input.Focus(); input.Select(0, item.IsDirectory ? original.Length : Math.Max(1, original.Length - Path.GetExtension(original).Length)); };
                dialog.ShowDialog();
            }
        }
        internal void RequestRefresh()
        {
            if (refreshing) return;
            if (RefreshRequested != null) RefreshRequested();
            else Notify("刷新入口不可用，请重新打开工作区。");
        }
        internal void SetRefreshing(bool value)
        {
            refreshing = value;
            if (container != null) container.SyncRefresh();
            if (value) Notify("正在刷新桌面图标，保留手动分类和排列…");
        }
        internal void SetSearch(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (searchText == text) return;
            CancelGroupMove(); searchText = text;
            if (container != null) container.ResetScroll(); RefreshSearchResults();
        }
        internal void FocusSearch() { if (container != null) container.FocusSearch(); }
        internal void ToggleCountSort() { SortGroupsByCount(nextSortDescending); }
        internal void SortGroupsByCount(bool descending)
        {
            if (SearchActive) ClearSearch();
            if (container == null || boxes.Count == 0 || !Checkpoint()) return;
            var counts = new Dictionary<Category, int>();
            foreach (Category category in Enum.GetValues(typeof(Category))) counts[category] = 0;
            foreach (var item in items) counts[item.Category]++;
            var original = new List<ClassificationBox>(boxes);
            CategoryOrderMode previousMode = layout.OrderMode;
            boxes.Sort(delegate(ClassificationBox left, ClassificationBox right)
            {
                int compare = descending ? counts[right.State.Category].CompareTo(counts[left.State.Category]) : counts[left.State.Category].CompareTo(counts[right.State.Category]);
                return compare != 0 ? compare : original.IndexOf(left).CompareTo(original.IndexOf(right));
            });
            layout.Groups.Clear(); foreach (var box in boxes) layout.Groups.Add(box.State);
            ReflowGroups(false, true);
            layout.OrderMode = descending ? CategoryOrderMode.CountDescending : CategoryOrderMode.CountAscending;
            if (!Save())
            {
                layout.OrderMode = previousMode;
                boxes.Clear(); boxes.AddRange(original);
                layout.Groups.Clear(); foreach (var box in boxes) layout.Groups.Add(box.State);
                ReflowGroups(false, true); return;
            }
            nextSortDescending = !descending; container.SyncCommands();
            Notify(descending ? "已按全部图标数量从多到少排列（仅本次）；刷新不自动重排，拖动转为自定义；可撤销。" : "已按全部图标数量从少到多排列（仅本次）；刷新不自动重排，拖动转为自定义；可撤销。");
        }
        internal bool Checkpoint()
        {
            foreach (var box in boxes) box.CaptureState();
            if (store.Checkpoint(layout)) return true;
            Notify(store.Error); return false;
        }
        internal bool Save()
        {
            if (store.Save(layout)) return true;
            Notify(store.Error); return false;
        }
        internal void Toggle(ClassificationBox box)
        {
            if (SearchActive) ClearSearch();
            if (!Checkpoint()) return;
            bool previous = box.State.Collapsed; box.State.Collapsed = !previous; box.ApplyState(); ReflowGroups(false, true);
            if (!Save()) { box.State.Collapsed = previous; box.ApplyState(); ReflowGroups(false, true); return; }
            Notify(box.State.Collapsed ? "分类框已折叠；可撤销。" : "分类框已展开；可撤销。");
            if (container != null) container.SyncCommands();
        }
        internal void Assign(WorkspaceItem item, Category category)
        {
            if (!items.Contains(item) || item.Category == category || !layout.Groups.Exists(delegate(GroupState group) { return group.Category == category; }) || !Checkpoint()) return;
            var previous = new List<Assignment>(layout.Assignments);
            layout.Assignments.RemoveAll(delegate(Assignment assignment) { return string.Equals(assignment.Identity, item.Identity, StringComparison.OrdinalIgnoreCase); });
            layout.Assignments.Add(new Assignment { Identity = item.Identity, Category = category });
            if (!Save()) { layout.Assignments = previous; return; }
            RefreshItems();
            Notify("已归入“" + CategoryTitle(category) + "”；手动归属优先，刷新不覆盖；可撤销，原文件不移动。");
        }
        internal void ResetClassification()
        {
            if (!Checkpoint()) return;
            var previous = layout.Assignments; layout.Assignments = new List<Assignment>();
            if (!Save()) { layout.Assignments = previous; return; }
            RefreshItems(); Notify("已清除手动归属并恢复自动分类；可撤销。");
        }
        internal void ResetLayout()
        {
            if (!Checkpoint()) return;
            var previous = layout.Groups; CategoryOrderMode previousMode = layout.OrderMode;
            int index = 0;
            layout.Groups = previous.ConvertAll(delegate(GroupState group)
            {
                int slot = index++;
                return new GroupState { Category = group.Category, Title = group.Title, X = 12 + slot % 3 * 292, Y = 12 + slot / 3 * 272,
                    Width = 280, Height = 260, SortByName = group.SortByName };
            });
            layout.OrderMode = CategoryOrderMode.Custom;
            if (!Save()) { layout.Groups = previous; layout.OrderMode = previousMode; return; }
            nextSortDescending = true; RebuildBoxes(); Notify("分类框已恢复默认大小与位置；自定义分类和手动归属保留，可撤销。");
        }
        internal void Undo()
        {
            LayoutState previous = store.ReadUndo();
            if (previous == null) { Notify(store.Error ?? "暂无可撤销的修改。"); return; }
            // Scan metadata and acknowledged onboarding are not undoable layout actions.
            previous.LastRefreshUtcTicks = layout.LastRefreshUtcTicks; previous.FirstRunCompleted = layout.FirstRunCompleted;
            // Save the target first: failed persistence must not consume the current workspace.
            if (!store.Save(previous)) { Notify(store.Error); return; }
            layout = previous; EnsureGroups(); nextSortDescending = layout.OrderMode != CategoryOrderMode.CountDescending; RebuildBoxes(); Notify("已恢复上一次分类框布局和分类归属。");
        }
        private void RebuildBoxes()
        {
            bool visible = Visible; CloseBoxes(); RefreshItems();
            if (!visible) return;
            container = new WorkspaceForm(this, font);
            if (!container.PrepareDesktop())
            { desktopVisibility.Restore(); CloseBoxes(); Notify("桌面层不可用，工作区未显示；请从托盘重试。"); return; }
            foreach (var group in layout.Groups)
            {
                var box = new ClassificationBox(this, group, icons, font);
                boxes.Add(box); container.Canvas.Controls.Add(box);
                box.SetItems(items.FindAll(delegate(WorkspaceItem item) { return item.Category == group.Category; })); box.Display();
            }
            ReflowGroups(false);
            container.Sync();
            if (!container.ShowDesktop())
            { showing = false; desktopVisibility.Restore(); Notify("无法挂载桌面层，工作区未显示；不会回退为遮挡应用的浮窗。"); return; }
            showing = true;
        }
        internal void ArrangeItems(ClassificationBox target)
        {
            if (!Checkpoint()) return;
            var previous = boxes.ConvertAll(delegate(ClassificationBox box) { return box.State.SortByName; });
            foreach (var box in boxes) if (target == null || ReferenceEquals(target, box)) box.State.SortByName = true;
            if (!Save()) { for (int i = 0; i < boxes.Count; i++) boxes[i].State.SortByName = previous[i]; return; }
            RefreshItems(); Notify("项目已按名称紧凑排版；原文件与分类归属不变。");
        }
        internal void ArrangeGroups()
        {
            if (container == null || !Checkpoint()) return;
            ReflowGroups(false, true); if (!Save()) return; Notify("分类框已自适应排列，保持当前大小与折叠状态；可撤销。");
        }
        internal void ToggleGroupsCollapsed() { SetGroupsCollapsed(!AllGroupsCollapsed); }
        internal void CollapseGroups() { SetGroupsCollapsed(true); }
        private void SetGroupsCollapsed(bool collapsed)
        {
            if (container != null && SearchActive) container.ClearSearch();
            if (container == null || !Checkpoint()) return;
            var previous = new bool[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
            { previous[i] = boxes[i].State.Collapsed; boxes[i].State.Collapsed = collapsed; boxes[i].ApplyState(); }
            ReflowGroups(false, true);
            if (!Save())
            {
                for (int i = 0; i < boxes.Count; i++) boxes[i].State.Collapsed = previous[i];
                ReflowGroups(false, true); container.SyncCommands(); return;
            }
            container.SyncCommands();
            Notify(collapsed ? "全部分类框已折叠；再次点击可全部展开，可撤销。" : "全部分类框已展开并自动排列；可撤销。");
        }
        internal void ReflowGroups(bool persist) { ReflowGroups(persist, false); }
        internal void ReflowGroups(bool persist, bool animate)
        {
            UpdateSearchView();
            if (!SearchActive) LayoutGroups(persist, animate, null, true);
        }
        internal void PreviewGroupMove(ClassificationBox dragged, Point pointer)
        {
            int source = boxes.IndexOf(dragged);
            if (source < 0 || container == null) return;
            if (dragOrder == null) dragOrder = new List<ClassificationBox>(boxes);
            int destination = source;
            if (!dragged.LayoutBounds.Contains(pointer))
            {
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (i == source || !ShouldDisplay(boxes[i])) continue;
                    Rectangle hot = boxes[i].LayoutBounds;
                    // Stable slot geometry and a small inset prevent edge/gap jitter.
                    hot.Inflate(-Math.Min(18, hot.Width / 8), -Math.Min(8, hot.Height / 8));
                    if (hot.Contains(pointer)) { destination = i; break; }
                }
            }
            if (destination != source)
            {
                boxes.RemoveAt(source); boxes.Insert(destination, dragged);
                layout.Groups.Clear(); foreach (var box in boxes) layout.Groups.Add(box.State);
                LayoutGroups(false, true, dragged, false);
            }
            container.SetDropSlot(dragged.LayoutBounds);
        }
        internal void CompleteGroupMove()
        {
            if (container == null) return;
            container.SetDropSlot(Rectangle.Empty);
            var original = dragOrder == null ? new List<ClassificationBox>(boxes) : dragOrder;
            CategoryOrderMode previousMode = layout.OrderMode; layout.OrderMode = CategoryOrderMode.Custom;
            LayoutGroups(false, true, null, false);
            if (!Save())
            {
                boxes.Clear(); boxes.AddRange(original); layout.Groups.Clear(); foreach (var box in boxes) layout.Groups.Add(box.State);
                layout.OrderMode = previousMode; LayoutGroups(false, true, null, false);
            }
            else { nextSortDescending = true; Notify("已保存自定义排列；刷新不覆盖手动顺序，可撤销。"); }
            dragOrder = null; container.SyncCommands();
        }
        internal void CancelGroupMove()
        {
            if (container == null || dragOrder == null) return;
            boxes.Clear(); boxes.AddRange(dragOrder); dragOrder = null;
            layout.Groups.Clear(); foreach (var box in boxes) layout.Groups.Add(box.State);
            container.SetDropSlot(Rectangle.Empty);
            LayoutGroups(false, true, null, false);
        }
        private void LayoutGroups(bool persist, bool animate, ClassificationBox dragged, bool resetScroll)
        {
            if (container == null || arranging || boxes.Count == 0) return;
            var flow = boxes.FindAll(ShouldDisplay);
            if (flow.Count == 0) { container.RefreshScroll(); if (persist) Save(); return; }
            arranging = true; container.Canvas.SuspendLayout();
            try
            {
                if (resetScroll) container.ResetScroll();
                int available = Math.Max(220, container.CanvasViewWidth - 24);
                var targets = new Point[flow.Count];
                for (int pass = 0; pass < 2; pass++)
                {
                    int x = 12, y = 12, rowHeight = 0;
                    for (int i = 0; i < flow.Count; i++)
                    {
                        Rectangle bounds = flow[i].LayoutBounds;
                        if (x > 12 && x + bounds.Width > available + 12)
                        { x = 12; y += rowHeight + 12; rowHeight = 0; }
                        targets[i] = new Point(x, y);
                        x += bounds.Width + 12; rowHeight = Math.Max(rowHeight, bounds.Height);
                    }
                    if (y <= 4000) break;
                    int limit = Math.Max(140, (4000 - 12) / flow.Count - 12);
                    foreach (var box in flow)
                        if (!box.State.Collapsed) box.State.Height = Math.Min(box.State.Height, limit);
                }
                for (int i = 0; i < flow.Count; i++)
                {
                    var box = flow[i]; box.State.X = targets[i].X; box.State.Y = targets[i].Y;
                    if (!ReferenceEquals(box, dragged)) box.ApplyState(animate);
                }
                container.RefreshScroll();
                if (persist) Save();
            }
            finally { container.Canvas.ResumeLayout(); arranging = false; }
        }
        private static int CurrentDpi()
        { using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(96, Math.Min(960, (int)Math.Round(graphics.DpiX))); }
        internal static Rectangle FitWorkspaceBounds(Rectangle saved, Rectangle work)
        {
            int minWidth = Math.Min(640, Math.Max(1, work.Width)), minHeight = Math.Min(460, Math.Max(1, work.Height));
            saved.Width = Math.Min(Math.Max(minWidth, saved.Width), Math.Max(1, work.Width));
            saved.Height = Math.Min(Math.Max(minHeight, saved.Height), Math.Max(1, work.Height));
            saved.X = Math.Max(work.Left, Math.Min(saved.X, work.Right - saved.Width));
            saved.Y = Math.Max(work.Top, Math.Min(saved.Y, work.Bottom - saved.Height)); return saved;
        }
        internal Rectangle RestoreWorkspaceBounds(Rectangle fallback)
        {
            if (layout.WorkspaceWidth == 0) return FitWorkspaceBounds(fallback, Screen.FromRectangle(fallback).WorkingArea);
            var saved = new Rectangle(layout.WorkspaceX, layout.WorkspaceY, layout.WorkspaceWidth, layout.WorkspaceHeight);
            Screen target = null;
            foreach (var screen in Screen.AllScreens) if (string.Equals(screen.DeviceName, layout.WorkspaceScreen, StringComparison.Ordinal)) { target = screen; break; }
            if (target == null) target = Screen.FromRectangle(saved);
            Rectangle work = target.WorkingArea;
            if (!string.IsNullOrEmpty(layout.WorkspaceScreen))
            {
                double scale = layout.WorkspaceDpi == 0 ? 1.0 : CurrentDpi() / (double)layout.WorkspaceDpi;
                saved = new Rectangle(work.Left + (int)Math.Round((saved.X - layout.WorkspaceWorkX) * scale),
                    work.Top + (int)Math.Round((saved.Y - layout.WorkspaceWorkY) * scale),
                    (int)Math.Round(saved.Width * scale), (int)Math.Round(saved.Height * scale));
            }
            return FitWorkspaceBounds(saved, work);
        }
        internal bool RememberWorkspaceBounds(Rectangle bounds)
        {
            Rectangle previous = new Rectangle(layout.WorkspaceX, layout.WorkspaceY, layout.WorkspaceWidth, layout.WorkspaceHeight);
            string previousScreen = layout.WorkspaceScreen; int previousDpi = layout.WorkspaceDpi, previousWorkX = layout.WorkspaceWorkX, previousWorkY = layout.WorkspaceWorkY;
            var screen = Screen.FromRectangle(bounds); var work = screen.WorkingArea;
            layout.WorkspaceX = bounds.X; layout.WorkspaceY = bounds.Y; layout.WorkspaceWidth = bounds.Width; layout.WorkspaceHeight = bounds.Height;
            layout.WorkspaceScreen = screen.DeviceName; layout.WorkspaceDpi = CurrentDpi(); layout.WorkspaceWorkX = work.X; layout.WorkspaceWorkY = work.Y;
            if (Save()) return true;
            layout.WorkspaceX = previous.X; layout.WorkspaceY = previous.Y; layout.WorkspaceWidth = previous.Width; layout.WorkspaceHeight = previous.Height;
            layout.WorkspaceScreen = previousScreen; layout.WorkspaceDpi = previousDpi; layout.WorkspaceWorkX = previousWorkX; layout.WorkspaceWorkY = previousWorkY; return false;
        }
        internal string CategoryTitle(Category category)
        {
            var group = layout.Groups.Find(delegate(GroupState value) { return value.Category == category; });
            return group != null && !string.IsNullOrWhiteSpace(group.Title) ? group.Title : Classification.Title(category);
        }
        internal void ToggleEmptyGroups()
        {
            if (!Checkpoint()) return;
            layout.HideEmptyGroups = !layout.HideEmptyGroups;
            if (!Save()) { layout.HideEmptyGroups = !layout.HideEmptyGroups; return; }
            ReflowGroups(false, true); Notify(layout.HideEmptyGroups ? "已隐藏空分类；设置中可重新显示。" : "已显示空分类。");
        }
        private string categoryEditError;
        private bool ValidateCategoryName(string name, Category? exclude)
        {
            categoryEditError = null;
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40 || name.IndexOfAny(new[] { (char)10, (char)13, (char)0 }) >= 0)
                categoryEditError = "名称须为1至40字，不能包含换行。";
            else if (layout.Groups.Exists(delegate(GroupState group) { return (!exclude.HasValue || group.Category != exclude.Value) && string.Equals(CategoryTitle(group.Category), name.Trim(), StringComparison.CurrentCultureIgnoreCase); }))
                categoryEditError = "该名称已存在，请换一个名称。";
            if (categoryEditError == null) return true;
            Notify(categoryEditError); return false;
        }
        private void EditCategoryName(string title, string initial, Func<string, bool> apply)
        {
            if (categoryEditor != null && !categoryEditor.IsDisposed) { categoryEditor.Activate(); return; }
            var dialog = new WorkspaceDialog { Text = title, Font = font, Icon = AppBrand.Icon, ClientSize = new Size(440, 300),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen, AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi };
            dialog.AddHeading(title, "给这一组桌面图标，一个好找的名字。");
            var label = new Label { Text = "分类名称", Left = 24, Top = 102, Width = 280, Height = 22 };
            var limit = new Label { Tag = "muted", Text = "最多 40 字", TextAlign = ContentAlignment.TopRight, Left = 312, Top = 102, Width = 104, Height = 22 };
            var field = new WorkspaceTextField(initial, 40, "分类名称，最多40字") { Left = 22, Top = 128, Width = 396, TabIndex = 0 };
            var input = field.Editor;
            var hint = new Label { Tag = "muted", Text = "不搬动或删除原文件；手动归属优先", Left = 24, Top = 182, Width = 392, Height = 46 };
            var confirm = new RoundedButton { Height = 36, Text = "保存", Left = 320, Top = 250, Width = 96, TabIndex = 2 };
            var cancel = new RoundedButton { Height = 36, Secondary = true, Text = "取消", Left = 212, Top = 250, Width = 96, TabIndex = 1 };
            confirm.Click += delegate { if (disposed) { dialog.Close(); return; } if (apply(input.Text.Trim())) dialog.Close(); else { field.Invalid = true; hint.Text = categoryEditError ?? "保存未完成，请检查工作区提示。"; } };
            cancel.Click += delegate { dialog.Close(); }; dialog.AcceptButton = confirm; dialog.CancelButton = cancel;
            dialog.Controls.AddRange(new Control[] { label, limit, field, hint, cancel, confirm }); categoryEditor = dialog;
            dialog.FormClosed += delegate { if (ReferenceEquals(categoryEditor, dialog)) categoryEditor = null; dialog.Dispose(); };
            dialog.ApplyTheme(); dialog.Show(); input.Focus(); input.SelectAll();
        }
        internal void CreateCategory() { EditCategoryName("新增分类", string.Empty, TryCreateCategory); }
        internal bool TryCreateCategory(string name)
        {
            if (!ValidateCategoryName(name, null)) return false;
            Category category = Category.Custom1;
            while (category <= Category.Custom16 && layout.Groups.Exists(delegate(GroupState group) { return group.Category == category; })) category++;
            if (category > Category.Custom16) { categoryEditError = "最多支持16个自定义分类。"; Notify(categoryEditError); return false; }
            if (SearchActive) ClearSearch();
            if (!Checkpoint()) return false;
            var state = new GroupState { Category = category, Title = name.Trim(), X = 12, Y = 12 };
            bool hideEmpty = layout.HideEmptyGroups; layout.HideEmptyGroups = false; layout.Groups.Add(state);
            if (!Save()) { layout.Groups.Remove(state); layout.HideEmptyGroups = hideEmpty; return false; }
            if (container != null)
            {
                var box = new ClassificationBox(this, state, icons, font); boxes.Add(box); container.Canvas.Controls.Add(box); box.Display();
                ReflowGroups(false, true); container.SyncCommands();
            }
            Notify("分类已新增；已显示空分类，拖入图标即可归类；可撤销。"); return true;
        }
        internal void RenameCategory(ClassificationBox box) { RenameCategory(box.State.Category); }
        internal void RenameCategory(Category category)
        { EditCategoryName("重命名分类", CategoryTitle(category), delegate(string name) { return TryRenameCategory(category, name); }); }
        internal bool TryRenameCategory(Category category, string name)
        {
            if (!ValidateCategoryName(name, category)) return false;
            var group = layout.Groups.Find(delegate(GroupState value) { return value.Category == category; });
            if (group == null || !Checkpoint()) return false;
            string previous = group.Title; group.Title = name.Trim();
            if (!Save()) { group.Title = previous; return false; }
            foreach (var card in boxes) { card.Text = "桌面分类 · " + CategoryTitle(card.State.Category); card.AccessibleName = card.Text; card.Invalidate(); }
            Notify("分类名称已保存；类型规则不变，手动归属优先；可撤销。"); return true;
        }
        internal void DeleteCategory(ClassificationBox box) { DeleteCategory(box.State.Category); }
        internal void DeleteCategory(Category category)
        {
            if (!layout.Groups.Exists(delegate(GroupState group) { return group.Category == category; })) return;
            if (WorkspaceDialog.Confirm("删除分类", "分类内的图标将自动归入剩余分类。\n默认分类删除后，刷新或重启不会重新创建。\n原文件不会删除或移动，此操作可撤销。", "删除分类", font)) TryDeleteCategory(category);
        }
        internal bool TryDeleteCategory(Category category)
        {
            var state = layout.Groups.Find(delegate(GroupState group) { return group.Category == category; });
            if (state == null) return false;
            if (layout.Groups.Count <= 1) { Notify("至少保留一个分类，避免图标无处显示。"); return false; }
            if (!Checkpoint()) return false;
            int index = layout.Groups.IndexOf(state); var previous = new List<Assignment>(layout.Assignments);
            var previousDeleted = new List<Category>(layout.DeletedCategories);
            if (!Classification.IsCustom(category) && !layout.DeletedCategories.Contains(category)) layout.DeletedCategories.Add(category);
            layout.Groups.Remove(state); layout.Assignments.RemoveAll(delegate(Assignment item) { return item.Category == category; });
            if (!Save()) { layout.Groups.Insert(index, state); layout.Assignments = previous; layout.DeletedCategories = previousDeleted; return false; }
            var box = boxes.Find(delegate(ClassificationBox value) { return value.State.Category == category; });
            if (box != null) { boxes.Remove(box); box.Dispose(); }
            RefreshItems(); ReflowGroups(false, true); if (container != null) container.SyncCommands();
            Notify("分类已删除，图标已归入剩余分类；原文件未删除，可撤销。"); return true;
        }
        internal List<Category> Categories { get { return layout.Groups.ConvertAll(delegate(GroupState group) { return group.Category; }); } }
        internal void CommitNoteFontSize(int value)
        {
            if (value < 12 || value > 32 || value == layout.NoteFontSize) return;
            if (!Checkpoint()) return;
            int previous = layout.NoteFontSize; layout.NoteFontSize = value;
            if (!Save()) layout.NoteFontSize = previous;
            if (container != null) container.Sync();
            if (layout.NoteFontSize == value) Notify("便签字号已设为 " + value + " px；可撤销。");
        }
        internal void CommitOpacity(int value)
        {
            value = LayoutStore.NormalizeOpacity(value);
            if (value == layout.OpacityPercent) return;
            if (!Checkpoint()) { if (container != null) container.Sync(); return; }
            int previous = layout.OpacityPercent; layout.OpacityPercent = value;
            if (!Save()) layout.OpacityPercent = previous;
            if (container != null) container.Sync();
            if (layout.OpacityPercent == value) Notify("工作区不透明度已设为" + value + "%；可撤销。");
        }
        internal void SetStartupHiding(bool value)
        {
            if (!Checkpoint()) { if (container != null) container.Sync(); return; }
            bool previous = layout.HideIconsOnStartup; layout.HideIconsOnStartup = value;
            if (!Save()) { layout.HideIconsOnStartup = previous; if (container != null) container.Sync(); return; }
            if (container != null) container.Sync();
            Notify("启动隐藏设置已保存；仅工作区显示成功后隐藏。");
        }
        private void HideDesktopIcons()
        {
            string error; if (!desktopVisibility.Hide(out error)) Notify(error);
            if (container != null) container.SyncDesktopIcons();
            if (error == null) Notify(desktopVisibility.OwnsChange ? "桌面图标已暂时隐藏；隐藏或退出工作区将恢复。" : "桌面图标原本已隐藏，保持原状态。");
        }
        internal bool? DesktopIconsVisible { get { return desktopVisibility.Visible; } }
        internal void ToggleDesktopIcons()
        {
            if (!Visible) return;
            string error;
            if (!desktopVisibility.Toggle(out error)) Notify(error);
            else Notify(desktopVisibility.Visible == true ? "原生桌面图标已显示；退出程序时保持显示。" : "原生桌面图标已隐藏；退出程序时自动显示。");
            if (container != null) container.SyncDesktopIcons();
        }
        private void CloseBoxes()
        {
            dragOrder = null; if (searchBox != null) { searchBox.Dispose(); searchBox = null; } foreach (var box in boxes) box.Dispose(); boxes.Clear();
            if (container != null) { container.Dispose(); container = null; } showing = false;
        }
        internal void RequestHide()
        {
            if (!Visible) return;
            const string message = "从系统托盘右键 → 显示分类窗口，可重新打开。" + "\n托盘图标被折叠时，点击任务栏 ↑ 查找桌面整理。" + "\n桌面图标恢复原状态；原文件不会移动。";
            if (WorkspaceDialog.Confirm("收起工作区", message, "收起", font)) Hide();
        }
        internal void Hide()
        {
            if (container == null && boxes.Count == 0 && !desktopVisibility.OwnsChange) return;
            if (container != null && showing) container.PersistBounds();
            foreach (var box in boxes) box.CaptureState(); Save();
            bool restored = desktopVisibility.Restore(); if (container != null) container.HideDesktop(); showing = false;
            Notify(restored ? "工作区已隐藏，桌面图标恢复原状态；可从托盘显示。" : "工作区已隐藏，原生图标恢复未确认；恢复守护仍保留，请从桌面右键→查看核对原状态。");
        }
        internal void Notify(string text)
        {
            if (container != null && !container.IsDisposed) container.SetFeedback(text);
            // Routine workspace actions stay local; hidden-workspace alerts retain the tray recovery path.
            if (Visible) return;
            notify(text);
        }
        public void Dispose() { Dispose(true); }
        internal void DisposeForExit() { Dispose(false); }
        private void Dispose(bool persistBounds)
        {
            if (disposed) return; disposed = true;
            try
            {
                // Exit must not synchronously flush layout.json on the UI thread. Bounds
                // are already persisted after each completed move/resize gesture.
                if (persistBounds && container != null && showing) container.PersistBounds();
                if (categoryEditor != null) categoryEditor.Close();
            }
            finally
            {
                // Restoration must not depend on successfully persisting the layout.
                itemChanges.Dispose();
                foreach (var watcher in itemWatchers.Values) watcher.Dispose();
                itemWatchers.Clear(); changedItemPaths.Clear();
                desktopVisibility.Dispose(); CloseBoxes(); icons.Dispose(); items.Clear();
            }
        }
    }
}
