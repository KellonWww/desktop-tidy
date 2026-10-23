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
        private const string Preferences = @"Software\DesktopTidy\Preferences";
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
        {
            this.owner = owner; this.notify = notify;
            owner.EdgeNotesEnabled = ReadEnabled(); owner.ToggleEdgeNotes = Toggle;
            watch.Tick += Tick;
            if (Enabled) watch.Start();
        }
        private bool ReadEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Preferences))
                    return key == null || Convert.ToInt32(key.GetValue("EdgeNotesEnabled", 1)) != 0;
            }
            catch (Exception error)
            {
                if (!SettingsError(error)) throw;
                Debug.WriteLine(error); notify("便签贴边设置读取失败，本次暂不开启；可从托盘手动打开。"); return false;
            }
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
                using (var key = Registry.CurrentUser.CreateSubKey(Preferences))
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
            notify(enabled ? "已开启左侧呼出便签：鼠标贴住屏幕左边停留片刻即可。" : "已关闭鼠标贴边呼出，仍可从托盘打开便签。");
        }
        private void Tick(object sender, EventArgs e)
        {
            if (disposed) return;
            Point pointer = Cursor.Position;
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
            Screen current = Screen.FromPoint(pointer);
            NotesEdge edge = NotesEdgeGeometry.Detect(current.Bounds, pointer);
            string key = edge == NotesEdge.None ? null : current.DeviceName + ":" + edge;
            if (!trigger.ShouldReveal(key, clock.ElapsedMilliseconds)) return;
            if ((panel != null && panel.Failed) || Fullscreen(current)) { trigger.Suppress(); return; }
            Reveal(current, edge, false);
        }
        internal void ShowFromTray()
        {
            if (disposed) return;
            if (panel != null && panel.Failed) { panel.Dispose(); panel = null; }
            Reveal(Screen.FromPoint(Cursor.Position), NotesEdge.Left, true);
        }
        private void Reveal(Screen screen, NotesEdge edge, bool activate)
        {
            if (panel == null || panel.IsDisposed)
            {
                panel = new NotesEdgeForm(owner);
                panel.Collapsed += delegate { trigger.Suppress(); };
            }
            monitor = screen.DeviceName; workingArea = screen.WorkingArea;
            float scale;
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = graphics.DpiX / 96f;
            trigger.ShouldHide(true, false, clock.ElapsedMilliseconds);
            panel.Reveal(NotesEdgeGeometry.PanelBounds(workingArea, edge, scale), edge, activate);
            watch.Start();
        }
        internal void RefreshTheme() { if (panel != null) panel.RefreshTheme(); }
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
            owner.ToggleEdgeNotes = null;
            if (panel != null) { panel.Dispose(); panel = null; }
        }
    }
}
