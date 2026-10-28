using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using DesktopTidy.Core;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    // Independent tool window: never attaches to Explorer, reserves screen space or changes desktop icons.
    internal sealed class NotesEdgeController : IDisposable
    {
        private readonly string preferences;
        private NotesDockHandle dockHandle;
        private string handleSignature;
        private NotesEdge dockSide = NotesEdge.Left;
        private int dockPosition = 5000;
        private readonly WorkspaceController owner;
        private readonly Action<string> notify;
        private readonly Timer watch = new Timer { Interval = 50 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly NotesEdgeTrigger trigger = new NotesEdgeTrigger();
        private NotesEdgeForm panel;
        private string monitor;
        private Rectangle workingArea;
        private bool disposed;
        internal bool Enabled { get { return owner.EdgeNotesEnabled; } }
        internal NotesEdgeController(WorkspaceController owner, Action<string> notify)
            : this(owner, notify, @"Software\DesktopTidy\Preferences") { }
        internal NotesEdgeController(WorkspaceController owner, Action<string> notify, string preferences)
        {
            this.owner = owner; this.notify = notify; this.preferences = preferences;
            owner.EdgeNotesEnabled = ReadEnabled(); owner.ToggleEdgeNotes = Toggle;
            ReadOptions(); trigger.HoverDelay = owner.EdgeNotesDelay;
            owner.EdgeNotesDockStatus = DockStatus(DockScreen());
            owner.ConfigureEdgeNotes = Configure; owner.PauseEdgeNotes = Pause;
            watch.Tick += Tick;
            if (Enabled) watch.Start();
        }
        private bool ReadEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(preferences))
                    return key == null || Convert.ToInt32(key.GetValue("EdgeNotesEnabled", 1)) != 0;
            }
            catch (Exception error)
            {
                if (!SettingsError(error)) throw;
                Debug.WriteLine(error); notify("便签贴边设置读取失败，本次暂不开启；可从托盘手动打开。"); return false;
            }
        }
        private void ReadOptions()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(preferences))
                {
                    if (key == null) return;
                    string[] dock = Convert.ToString(key.GetValue("EdgeNotesDock", "Left|")).Split('|');
                    NotesEdge savedSide;
                    if (dock.Length >= 2 && Enum.TryParse<NotesEdge>(dock[0], out savedSide) && savedSide != NotesEdge.None && Enum.IsDefined(typeof(NotesEdge), savedSide))
                    {
                        dockSide = savedSide; owner.EdgeNotesDockSide = dockSide.ToString(); owner.EdgeNotesDockScreen = dock[1];
                    }
                    dockPosition = Math.Max(0, Math.Min(10000, Convert.ToInt32(key.GetValue("EdgeNotesHandlePosition", 5000))));
                    string[] options = Convert.ToString(key.GetValue("EdgeNotesOptions", "600|all")).Split('|');
                    int delay = options.Length == 2 ? Convert.ToInt32(options[0]) : 600;
                    owner.EdgeNotesDelay = delay == 300 || delay == 600 || delay == 1000 ? delay : 600;
                    owner.EdgeNotesScreen = options.Length == 2 ? options[1] : "all";
                }
            }
            catch (Exception error)
            {
                if (!SettingsError(error)) throw;
                Debug.WriteLine(error); notify("便签触发偏好读取失败，本次使用默认值。");
            }
        }
        private void Configure(int delay, string screen)
        {
            if (disposed || (delay != 300 && delay != 600 && delay != 1000)) return;
            bool valid = screen == "all" || screen == "primary";
            foreach (var candidate in Screen.AllScreens) if (candidate.DeviceName == screen) valid = true;
            if (!valid) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(preferences))
                {
                    if (key == null) throw new IOException("无法打开个人设置");
                    // Persist both fields together so a failed write cannot leave a partial preference.
                    key.SetValue("EdgeNotesOptions", delay + "|" + screen, RegistryValueKind.String);
                }
            }
            catch (Exception error)
            {
                if (!SettingsError(error)) throw;
                Debug.WriteLine(error); notify("便签触发偏好保存失败，请重试。"); return;
            }
            owner.EdgeNotesDelay = delay; owner.EdgeNotesScreen = screen;
            trigger.HoverDelay = delay; trigger.Suppress(); handleSignature = null;
            if (panel != null && panel.Visible && !panel.Dragging) Reveal(DockScreen(), dockSide, false);
            notify("便签触发偏好已保存。");
        }
        private void Pause()
        {
            if (disposed) return;
            owner.EdgeNotesPaused = !owner.EdgeNotesPaused; trigger.Suppress();
            notify(owner.EdgeNotesPaused ? "已暂停贴边呼出，本次退出后恢复；仍可从托盘打开。" : "已恢复贴边呼出。");
        }
        private static bool SettingsError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is SecurityException ||
                error is FormatException || error is InvalidCastException || error is OverflowException;
        }
        internal void Toggle()
        {
            if (disposed) return;
            bool enabled = !Enabled;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(preferences))
                {
                    if (key == null) throw new IOException("无法打开个人设置");
                    key.SetValue("EdgeNotesEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception error)
            {
                if (!SettingsError(error)) throw;
                Debug.WriteLine(error); notify("便签贴边设置保存失败，未改变当前设置。"); return;
            }
            owner.EdgeNotesEnabled = enabled; trigger.Suppress();
            if (enabled) watch.Start();
            else { if (panel != null) panel.Collapse(); if (panel == null || !panel.Visible) watch.Stop(); }
            if (!enabled && dockHandle != null) dockHandle.Hide();
            notify(enabled ? "已开启便签贴边呼出：在边缘的便签把手处悬停或点击打开。" : "已关闭鼠标贴边呼出，仍可从托盘打开便签。");
        }
        private void Tick(object sender, EventArgs e)
        {
            if (disposed) return;
            if (panel != null) panel.SyncOpacity(owner.OpacityPercent);
            Point pointer = Cursor.Position;
            if (panel != null && panel.Dragging) { if (dockHandle != null) dockHandle.Hide(); return; }
            if (dockHandle != null && dockHandle.Dragging) return;
            UpdateHandle();
            if (panel != null && panel.Visible)
            {
                Screen screen = null;
                foreach (var candidate in Screen.AllScreens) if (candidate.DeviceName == monitor) { screen = candidate; break; }
                if (screen == null || screen.WorkingArea != workingArea) { panel.Collapse(); return; }
                Rectangle safe = panel.Bounds; safe.Inflate(20, 20);
                if (trigger.ShouldHide(safe.Contains(pointer), panel.InteractionActive || Control.MouseButtons != MouseButtons.None, clock.ElapsedMilliseconds)) panel.Collapse();
                return;
            }
            if (!Enabled) { watch.Stop(); return; }
            Screen current = DockScreen();
            bool allowed = !owner.EdgeNotesPaused && Allows(current) && dockHandle != null && dockHandle.Visible && dockHandle.Bounds.Contains(pointer);
            string key = allowed ? current.DeviceName + ":" + dockSide : null;
            if (!trigger.ShouldReveal(key, clock.ElapsedMilliseconds)) return;
            if ((panel != null && panel.Failed) || Fullscreen(current)) { trigger.Suppress(); return; }
            Reveal(current, dockSide, false);
        }
        private bool Allows(Screen screen)
        {
            return owner.EdgeNotesScreen == "all" || (owner.EdgeNotesScreen == "primary" && screen.Primary) || owner.EdgeNotesScreen == screen.DeviceName;
        }
        private Screen DockScreen()
        {
            foreach (var candidate in Screen.AllScreens)
                if (candidate.DeviceName == owner.EdgeNotesDockScreen && Allows(candidate)) return candidate;
            if (Allows(Screen.PrimaryScreen)) return Screen.PrimaryScreen;
            foreach (var candidate in Screen.AllScreens) if (Allows(candidate)) return candidate;
            return Screen.PrimaryScreen;
        }
        private static float Scale()
        { using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f; }
        private void UpdateHandle()
        {
            Screen screen = DockScreen();
            if (!Enabled || (panel != null && panel.Visible) || !Allows(screen) || Fullscreen(screen))
            { if (dockHandle != null) dockHandle.Hide(); return; }
            if (dockHandle == null) dockHandle = new NotesDockHandle(ShowFromTray, MoveHandle);
            double opacity = owner.OpacityPercent / 100.0;
            if (dockHandle.Opacity != opacity) dockHandle.Opacity = opacity;
            Rectangle bounds = NotesEdgeGeometry.HandleBounds(screen.WorkingArea, dockSide, Scale(), dockPosition);
            string signature = bounds + ":" + dockSide + ":" + screen.DeviceName + ":" + owner.EdgeNotesPaused + ":" + dockPosition;
            if (handleSignature != signature || !dockHandle.Visible)
            {
                dockHandle.Place(bounds, dockSide, screen.DeviceName, owner.EdgeNotesPaused); handleSignature = signature;
            }
            owner.EdgeNotesDockStatus = DockStatus(screen);
        }
        private void MoveHandle(int position)
        {
            if (disposed) return;
            dockPosition = Math.Max(0, Math.Min(10000, position)); handleSignature = null;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(preferences))
                {
                    if (key == null) throw new IOException("无法打开个人设置");
                    key.SetValue("EdgeNotesHandlePosition", dockPosition, RegistryValueKind.DWord);
                }
            }
            catch (Exception error)
            {
                if (!SettingsError(error)) throw;
                Debug.WriteLine(error); notify("便签把手位置保存失败，本次仍可继续使用。");
            }
            owner.EdgeNotesDockStatus = DockStatus(DockScreen());
        }
        private string DockStatus(Screen screen)
        { return "已吸附" + NotesEdgeGeometry.Name(dockSide) + "（" + screen.DeviceName + "）· 收起后留有便签把手"; }
        private void PreviewDock(Point point)
        {
            Screen screen = Screen.FromPoint(point);
            owner.EdgeNotesDockStatus = Allows(screen) ? "松手吸附" + NotesEdgeGeometry.Name(NotesEdgeGeometry.Nearest(screen.WorkingArea, point)) +
                "（" + screen.DeviceName + "）· Esc 取消" : "此屏幕不在呼出范围内 · 松手返回原位";
        }
        private void CompleteDock(Point point, bool canceled)
        {
            Screen screen = Screen.FromPoint(point);
            if (!canceled && Allows(screen))
            {
                NotesEdge side = NotesEdgeGeometry.Nearest(screen.WorkingArea, point);
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(preferences))
                    {
                        if (key == null) throw new IOException("无法打开个人设置");
                        key.SetValue("EdgeNotesDock", side + "|" + screen.DeviceName, RegistryValueKind.String);
                    }
                    dockSide = side; owner.EdgeNotesDockSide = side.ToString(); owner.EdgeNotesDockScreen = screen.DeviceName;
                }
                catch (Exception error)
                {
                    if (!SettingsError(error)) throw;
                    Debug.WriteLine(error); notify("停靠位置保存失败，已返回原位置，请重试。");
                }
            }
            else if (!canceled) notify("此屏幕不在便签呼出范围内，已返回原位置；可在贴边设置中修改范围。");
            trigger.Suppress(); handleSignature = null; Reveal(DockScreen(), dockSide, false);
        }
        internal void ShowFromTray()
        {
            if (disposed) return;
            if (panel != null && panel.Failed) { panel.Dispose(); panel = null; }
            Reveal(DockScreen(), dockSide, true);
        }
        private void Reveal(Screen screen, NotesEdge edge, bool activate)
        {
            if (panel == null || panel.IsDisposed)
            {
                panel = new NotesEdgeForm(owner);
                panel.Collapsed += delegate { trigger.Suppress(); };
                panel.DragPreview += PreviewDock; panel.DragCompleted += CompleteDock;
            }
            monitor = screen.DeviceName; workingArea = screen.WorkingArea;
            float scale = Scale();
            owner.EdgeNotesDockStatus = DockStatus(screen);
            if (dockHandle != null) dockHandle.Hide();
            trigger.ShouldHide(true, false, clock.ElapsedMilliseconds);
            panel.Reveal(NotesEdgeGeometry.PanelBounds(workingArea, edge, scale), edge, activate);
            watch.Start();
        }
        internal void RefreshTheme() { handleSignature = null; if (panel != null) panel.RefreshTheme(); }
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowBounds { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out WindowBounds rectangle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        private static bool Fullscreen(Screen screen)
        {
            IntPtr window = GetForegroundWindow(); if (window == IntPtr.Zero) return false;
            uint process; GetWindowThreadProcessId(window, out process);
            if (process == NativeMethods.GetCurrentProcessId()) return false;
            var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString() == "Shell_TrayWnd") return false;
            WindowBounds bounds;
            return GetWindowRect(window, out bounds) && bounds.Left <= screen.Bounds.Left && bounds.Top <= screen.Bounds.Top &&
                bounds.Right >= screen.Bounds.Right && bounds.Bottom >= screen.Bounds.Bottom;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; watch.Stop(); watch.Dispose(); clock.Stop();
            owner.ToggleEdgeNotes = null; owner.ConfigureEdgeNotes = null; owner.PauseEdgeNotes = null;
            if (panel != null) { panel.Dispose(); panel = null; }
            if (dockHandle != null) { dockHandle.Dispose(); dockHandle = null; }
        }
    }
}
