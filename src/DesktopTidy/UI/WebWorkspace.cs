using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DesktopTidy.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DesktopTidy.UI
{
    // Only opaque item identities cross the bridge, never arbitrary shell commands.
    internal sealed class WebWorkspace : Panel
    {
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        private const string Origin = "https://workspace.desktop-tidy.invalid";
        private readonly NotesStore notes;
        private readonly WorkspaceController owner;
        private readonly WorkspaceForm host;
        private readonly NotesEdgeForm edgeHost;
        private readonly Form surface;
        private readonly WebView2 browser;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
        private readonly Timer updates = new Timer { Interval = 700 };
        private readonly Timer deadline = new Timer { Interval = 15000 };
        private readonly Dictionary<string, string> iconData = new Dictionary<string, string>();
        private readonly Dictionary<string, string> itemTokens = new Dictionary<string, string>();
        private readonly Dictionary<string, WorkspaceItem> tokenItems = new Dictionary<string, WorkspaceItem>();
        private string previous, feedback = "双击打开 · 拖动归类 · 原文件留在原处";
        private bool ready, stopped, executing;
        private int pendingCommands;
        internal bool Ready { get { return ready && !stopped; } }
        internal WebWorkspace(WorkspaceController owner, WorkspaceForm host)
            : this(owner, host, null) { }
        internal WebWorkspace(WorkspaceController owner, NotesEdgeForm host)
            : this(owner, null, host) { }
        private WebWorkspace(WorkspaceController owner, WorkspaceForm host, NotesEdgeForm edgeHost)
        {
            this.owner = owner; this.host = host; this.edgeHost = edgeHost;
            surface = (Form)host ?? edgeHost; notes = owner.Notes;
            if (edgeHost != null) feedback = "贴边呼出 · 移开收起 · 编辑时保持展开";
            Dock = DockStyle.Fill; Visible = true;
            browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Read().Background };
            Controls.Add(browser);
            notes.Changed += NotesChanged;
            updates.Tick += delegate { RestoreBrowserSurface(); Publish(); };
            deadline.Tick += delegate { Fail("网页工作区加载超时，已恢复原生界面。"); };
        }
        internal void PrepareBrowserSurface()
        {
            // A hidden host is not automatically created with its WebView. Create the
            // entire parent chain before async initialization can run in another HWND's
            // DPI context; otherwise WinForms parks a differently-aware browser HWND.
            IntPtr root = surface.Handle;
            IntPtr parent = Handle;
            IntPtr child = browser.Handle;
            if (GetParent(parent) != root || GetParent(child) != parent)
                Debug.WriteLine("Edge notes: browser HWND was not created in its host.");
        }
        internal void RestoreBrowserSurface()
        {
            // WinForms can park the native WebView HWND while rebuilding its host.
            // Logical Visible/Parent stay unchanged, so setting Visible alone cannot recover it.
            if (edgeHost == null || !Ready || !Visible || !IsHandleCreated ||
                browser.IsDisposed || !browser.IsHandleCreated) return;
            IntPtr child = browser.Handle, parent = Handle;
            bool parked = GetParent(child) != parent;
            int attachError = 0;
            if (parked) { SetParent(child, parent); attachError = Marshal.GetLastWin32Error(); }
            if (GetParent(child) != parent)
            { Fail("便签网页挂载失败（Windows 错误 " + attachError + "），请从托盘重新打开便签。"); return; }
            if (parked || !IsWindowVisible(child))
            {
                browser.Visible = true;
                if (!SetWindowPos(child, IntPtr.Zero, browser.Left, browser.Top,
                    browser.Width, browser.Height, 0x10 | 0x20 | 0x40))
                    Fail("便签网页显示失败，请从托盘重新打开便签。");
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (edgeHost != null) BeginInvoke((Action)delegate
            { if (!IsDisposed && !Disposing) RestoreBrowserSurface(); });
        }
        internal async void Initialize()
        {
            try
            {
                if (SystemInformation.HighContrast) { Fail("系统高对比模式使用原生界面。"); return; }
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");
                if (!File.Exists(Path.Combine(folder, "index.html"))) { Fail("网页资源缺失，已恢复原生界面。"); return; }
                deadline.Start();
                string profile = owner.WebProfileDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTidy", "WebView2");
                // Independent native hosts can have different DPI awareness. A separate
                // renderer profile avoids shared-process initialization failures in either
                // opening order; notes still come from the same owner.Notes store.
                if (edgeHost != null) profile = Path.Combine(profile, "NotesEdge");
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                if (stopped || IsDisposed) return;
                // Explorer callbacks can run in a different DPI context from this tool
                // window. Controller creation must use its actual parent HWND context.
                System.Threading.Tasks.Task initialization;
                IntPtr previousDpi = edgeHost == null ? IntPtr.Zero :
                    SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(browser.Handle));
                try { initialization = browser.EnsureCoreWebView2Async(environment); }
                finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
                await initialization;
                if (stopped || IsDisposed) return;
                var core = browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.SetVirtualHostNameToFolderMapping("workspace.desktop-tidy.invalid", folder, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                { if (!IsTrusted(e.Uri)) e.Cancel = true; };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                core.ProcessFailed += delegate { Fail("网页渲染进程异常，已恢复原生界面。"); };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                { if (!e.IsSuccess) Fail("网页工作区加载失败，已恢复原生界面。"); };
                core.WebMessageReceived += Receive;
                core.Navigate(Origin + "/index.html" + (edgeHost == null ? "" : "?notesEdge=1"));
            }
            catch (Exception error)
            { Debug.WriteLine(error); Fail("WebView2 不可用：" + error.Message + "，已恢复原生界面。"); }
        }
        private static bool IsTrusted(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.GetLeftPart(UriPartial.Authority) == Origin && uri.AbsolutePath == "/index.html";
        }
        private void Fail(string message)
        {
            if (stopped || IsDisposed || Disposing) return;
            stopped = true; ready = false;
            if (edgeHost != null) message = message.Replace("已恢复原生界面。", "请从托盘重新打开便签重试。");
            try { updates.Stop(); deadline.Stop(); Visible = false; }
            catch (Exception error) { Debug.WriteLine(error); }
            try { if (edgeHost != null) edgeHost.WebFailed(message); else host.WebFailed(message); }
            catch (Exception error) { Debug.WriteLine(error); }
            try { if (!browser.IsDisposed) browser.Dispose(); }
            catch (Exception error) { Debug.WriteLine(error); }
        }
        internal void Feedback(string value) { feedback = value; Publish(); }
        internal void RefreshIcons() { iconData.Clear(); previous = null; }
        internal void FocusSearch()
        { if (Ready) browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"focusSearch\"}"); }
        private string TokenFor(WorkspaceItem item)
        {
            string token;
            if (!itemTokens.TryGetValue(item.Identity, out token))
            { token = Guid.NewGuid().ToString("N"); itemTokens[item.Identity] = token; }
            tokenItems[token] = item; return token;
        }
        private string IconFor(WorkspaceItem item)
        {
            string value;
            if (iconData.TryGetValue(item.Identity, out value)) return value;
            using (var image = new Bitmap(40, 40))
            using (var graphics = Graphics.FromImage(image))
            using (var stream = new MemoryStream())
            {
                owner.DrawWebIcon(graphics, item, new Rectangle(0, 0, 40, 40));
                image.Save(stream, ImageFormat.Png);
                value = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
            }
            iconData[item.Identity] = value; return value;
        }
        private void NotesChanged() { PublishNotes(false); }
        internal void PublishNotes(bool opened)
        {
            if (!Ready || IsDisposed || !surface.Visible || browser.CoreWebView2 == null) return;
            try
            {
                browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {
                    type = opened ? "notesOpened" : "notesState", notes = notes.Notes, notesError = notes.LoadError }));
            }
            catch (Exception error) { Debug.WriteLine(error); }
        }
        internal void Publish()
        {
            if (!Ready || IsDisposed || executing || !surface.Visible || browser.CoreWebView2 == null) return;
            if (SystemInformation.HighContrast) { Fail("系统高对比模式已恢复原生界面。"); return; }
            try
            {
                var groups = new List<object>();
                foreach (var group in edgeHost == null ? owner.WebGroups : new List<GroupState>())
                {
                    var members = new List<object>();
                    foreach (var item in owner.WebItems)
                        if (item.Category == group.Category)
                            members.Add(new { id = TokenFor(item), name = item.Name, section = item.Section ?? "", icon = IconFor(item) });
                    groups.Add(new { id = (int)group.Category, title = owner.CategoryTitle(group.Category), collapsed = group.Collapsed, sortByName = group.SortByName, width = group.Width, height = group.Height, items = members });
                }
                Theme theme = Theme.Read();
                string payload = json.Serialize(new { type = "state", groups = groups, total = owner.ItemCount, search = owner.SearchText,
                    refreshing = owner.IsRefreshing, hideEmpty = owner.HideEmptyGroups, allCollapsed = owner.AllGroupsCollapsed,
                    nextDescending = owner.NextSortDescending, desktopVisible = owner.DesktopIconsVisible, opacity = owner.OpacityPercent,
                    hideOnStartup = owner.HideIconsOnStartup, dark = theme.Dark, followsSystemTheme = theme.FollowsSystem, highContrast = theme.HighContrast,
                    notes = notes.Notes, notesError = notes.LoadError, edgeNotesEnabled = owner.EdgeNotesEnabled, edgeNotesDelay = owner.EdgeNotesDelay,
                    edgeNotesDockSide = owner.EdgeNotesDockSide, edgeNotesDockStatus = owner.EdgeNotesDockStatus, edgeNotesScreen = owner.EdgeNotesScreen, edgeNotesPaused = owner.EdgeNotesPaused,
                    notesScreens = Array.ConvertAll(Screen.AllScreens, screen => new { id = screen.DeviceName, name = screen.DeviceName + (screen.Primary ? "（主屏）" : "") }), updated = owner.LastRefreshDescription, feedback = feedback });
                if (payload == previous) return;
                browser.CoreWebView2.PostWebMessageAsJson(payload); previous = payload;
            }
            catch (Exception error) { Debug.WriteLine(error); Fail("网页通信异常，已恢复原生界面。"); }
        }
        private static string ReadText(Dictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) ? value as string ?? string.Empty : string.Empty; }
        private static int ReadInt(Dictionary<string, object> data, string key)
        {
            object value; int result;
            if (!data.TryGetValue(key, out value) || value == null) return 0;
            return int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out result) ? result : 0;
        }
        private void Receive(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (stopped || !IsTrusted(e.Source)) return;
            try
            {
                string raw = e.WebMessageAsJson;
                if (raw.Length > 32768) return;
                var data = json.Deserialize<Dictionary<string, object>>(raw);
                if (data == null) return;
                string command = ReadText(data, "command");
                if (command == "ready")
                {
                    if (ready) return;
                    ready = true; deadline.Stop(); Visible = true; BringToFront();
                    if (edgeHost != null) edgeHost.WebReady(); else host.WebReady(); updates.Start(); Publish(); PublishNotes(edgeHost != null); return;
                }
                if (!Ready || executing || pendingCommands >= 64) return;
                pendingCommands++;
                // Modal WinForms dialogs must run after WebView2's COM callback returns.
                BeginInvoke((Action)delegate
                {
                    pendingCommands--;
                    if (stopped || IsDisposed || executing) return;
                    executing = true;
                    try { Dispatch(command, data); }
                    catch (Exception error)
                    {
                        Debug.WriteLine(error);
                        if (!IsDisposed)
                        {
                            Feedback("操作未完成，请重试。");
                            if (!string.IsNullOrEmpty(ReadText(data, "request")) && browser.CoreWebView2 != null)
                                browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new { type = "notesResult", request = ReadText(data, "request"), success = false, error = "操作未完成，请重试。", notes = notes.Notes }));
                        }
                    }
                    finally { executing = false; }
                    Publish();
                });
            }
            catch (Exception error)
            { Debug.WriteLine(error); if (!IsDisposed) { executing = false; Feedback("操作未完成，请重试。"); } }
        }
        private void Dispatch(string command, Dictionary<string, object> data)
        {
            if (command == "saveNote" || command == "deleteNote" || command == "toggleTodo" || command == "togglePin" || command == "copyDraft" || command == "copyNote")
            {
                string error = null;
                bool success;
                if (command == "copyDraft")
                {
                    string text = ReadText(data, "text");
                    success = text.Length > 0 && text.Length <= NotesStore.MaxText + 82;
                    if (!success) error = "草稿为空或过长。";
                    else
                    {
                        try { Clipboard.SetText(text); }
                        catch (System.Runtime.InteropServices.ExternalException) { success = false; error = "剪贴板正忙，请重试。"; }
                    }
                }
                else if (command == "copyNote")
                {
                    var note = notes.Find(ReadText(data, "id"));
                    success = note != null;
                    if (success)
                    {
                        try { Clipboard.SetText(string.IsNullOrEmpty(note.Text) ? note.Title : note.Text); }
                        catch (System.Runtime.InteropServices.ExternalException) { success = false; error = "剪贴板正忙，请重试。"; }
                    }
                    else error = "便签不存在，请刷新后重试。";
                }
                else success = notes.Change(command, ReadText(data, "id"), ReadText(data, "title"), ReadText(data, "text"), ReadText(data, "todo") == "true", ReadText(data, "revision"), out error);
                browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new { type = "notesResult", request = ReadText(data, "request"), success = success, error = error, notes = notes.Notes }));
                return;
            }
            if (edgeHost != null)
            {
                if (command == "move") edgeHost.BeginDrag();
                else if (command == "hide") edgeHost.Collapse();
                else if (command == "notesMotion") edgeHost.ReduceMotion = ReadText(data, "value") == "true" || !SystemInformation.IsMenuAnimationEnabled;
                else if (command == "notesInteraction") edgeHost.InteractionActive = ReadText(data, "value") == "true";
                else if (command == "setTheme") owner.SetThemeOverride(ReadText(data, "value") == "true");
                return;
            }
            if (command == "configureEdgeNotes")
            { if (owner.ConfigureEdgeNotes != null) owner.ConfigureEdgeNotes(ReadInt(data, "delay"), ReadText(data, "screen")); return; }
            if (command == "pauseEdgeNotes") { if (owner.PauseEdgeNotes != null) owner.PauseEdgeNotes(); return; }
            if (command == "toggleEdgeNotes") { if (owner.ToggleEdgeNotes != null) owner.ToggleEdgeNotes(); return; }
            if (command == "search") { owner.SetSearch(ReadText(data, "value")); return; }
            if (command == "refresh") { owner.RequestRefresh(); return; }
            if (command == "sort") { owner.ToggleCountSort(); return; }
            if (command == "collapseAll") { owner.ToggleGroupsCollapsed(); return; }
            if (command == "undo") { owner.Undo(); return; }
            if (command == "arrange") { owner.ArrangeGroups(); return; }
            if (command == "sortItems") { owner.ArrangeItems(null); return; }
            if (command == "create") { owner.CreateCategory(); return; }
            if (command == "resetClassification")
            {
                if (WorkspaceDialog.Confirm("重新自动分类", "这会清除手动归属；原文件不变，可撤销。", "重新分类", host.Font)) owner.ResetClassification();
                return;
            }
            if (command == "hideEmpty") { owner.ToggleEmptyGroups(); return; }
            if (command == "desktop") { owner.ToggleDesktopIcons(); return; }
            if (command == "hide") { owner.RequestHide(); return; }
            if (command == "startup") { owner.SetStartupHiding(ReadText(data, "value") == "true"); return; }
            if (command == "setTheme") { owner.SetThemeOverride(ReadText(data, "value") == "true"); return; }
            if (command == "systemTheme") { owner.SetThemeOverride(null); return; }
            if (command == "opacity") { int value; if (int.TryParse(ReadText(data, "value"), out value)) owner.CommitOpacity(value); return; }
            if (command == "native") { Fail("已切换原生工作区；下次打开重新尝试网页界面。"); return; }
            if (command == "move" || command == "resize") { host.BeginWebGesture(command == "resize"); return; }
            WorkspaceItem item;
            if (!tokenItems.TryGetValue(ReadText(data, "item"), out item) || !owner.WebItems.Contains(item)) item = null;
            if (command == "itemMenu")
            {
                if (item != null)
                {
                    int x = Math.Max(0, Math.Min(browser.ClientSize.Width - 1, ReadInt(data, "x")));
                    int y = Math.Max(0, Math.Min(browser.ClientSize.Height - 1, ReadInt(data, "y")));
                    owner.ShowItemMenu(item, browser.PointToScreen(new Point(x, y)));
                }
                return;
            }
            if (command == "open")
            {
                if (item == null) return;
                try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { owner.Notify("无法打开该项目，请确认文件仍存在或文件关联可用。"); }
                catch (InvalidOperationException) { owner.Notify("无法打开该项目，请确认文件仍存在。"); }
                return;
            }
            int categoryId;
            if (!int.TryParse(ReadText(data, "category"), out categoryId)) return;
            Category category = (Category)categoryId;
            if (!owner.Categories.Contains(category)) return;
            if (command == "resizeCategory") owner.ResizeWebCategory(category, ReadInt(data, "width"), ReadInt(data, "height"));
            else if (command == "assign" && item != null) owner.Assign(item, category);
            else if (command == "rename") owner.RenameCategory(category);
            else if (command == "delete") owner.DeleteCategory(category);
            else if (command == "collapse") owner.ToggleWebCategory(category);
            else if (command == "reorder") owner.ReorderWebCategory(category, ReadText(data, "before"));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { notes.Changed -= NotesChanged; stopped = true; updates.Dispose(); deadline.Dispose(); iconData.Clear(); itemTokens.Clear(); tokenItems.Clear(); }
            base.Dispose(disposing);
        }
    }
}
