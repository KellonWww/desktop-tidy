using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DesktopTidy.Windows
{
    internal sealed class DesktopIconVisibility : IDisposable
    {
        private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        private IntPtr hidden;
        private uint shellProcess;
        private bool originalVisible = true;
        private EventWaitHandle stop;
        private Process guardian;
        internal bool OwnsChange { get { return hidden != IntPtr.Zero; } }
        internal bool? Visible { get { uint process; IntPtr target = FindIcons(out process); return target == IntPtr.Zero ? (bool?)null : IsWindowVisible(target); } }
        internal bool Toggle(out string error)
        {
            bool? visible = Visible;
            if (!visible.HasValue) { error = "未找到 Explorer 桌面图标，未修改系统。"; return false; }
            return Change(!visible.Value, out error);
        }
        private static string ClassName(IntPtr window) { var name = new StringBuilder(64); GetClassName(window, name, name.Capacity); return name.ToString(); }
        private static bool Valid(IntPtr window, uint process)
        {
            uint actual, current; GetWindowThreadProcessId(window, out actual); GetWindowThreadProcessId(GetShellWindow(), out current);
            return window != IntPtr.Zero && process != 0 && actual == process && current == process && ClassName(window) == "SysListView32" && ClassName(GetParent(window)) == "SHELLDLL_DefView";
        }
        private static IntPtr FindIcons(out uint process)
        {
            GetWindowThreadProcessId(GetShellWindow(), out process); uint expected = process; IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                uint actual; GetWindowThreadProcessId(window, out actual); if (actual != expected) return true;
                IntPtr view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero) result = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
                return result == IntPtr.Zero;
            }, IntPtr.Zero);
            return Valid(result, process) ? result : IntPtr.Zero;
        }
        internal bool Hide(out string error) { return Change(false, out error); }
        private bool Change(bool visible, out string error)
        {
            error = null;
            if (hidden != IntPtr.Zero)
            {
                if (Valid(hidden, shellProcess))
                {
                    if (visible && originalVisible)
                    {
                        if (Restore()) return true;
                        error = "恢复未确认；恢复守护仍保留。"; return false;
                    }
                    if (SetVisible(hidden, shellProcess, visible)) return true;
                    error = "桌面图标显隐未确认；退出时仍会请求恢复。"; return false;
                }
                ReleaseGuardian(); hidden = IntPtr.Zero;
            }
            uint process; IntPtr target = FindIcons(out process);
            if (target == IntPtr.Zero) { error = "未找到 Explorer 桌面图标，未修改系统。"; return false; }
            if (visible && IsWindowVisible(target)) return true;
            originalVisible = IsWindowVisible(target);
            string token = @"Local\DesktopTidy.IconGuard." + Guid.NewGuid().ToString("N");
            try
            {
                using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".ready"))
                {
                    stop = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".stop");
                    var start = new ProcessStartInfo(Application.ExecutablePath, "--restore-desktop-icons " + Process.GetCurrentProcess().Id + " " + target.ToInt64().ToString(CultureInfo.InvariantCulture) + " " + process + " " + token + " " + "1");
                    start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
                    guardian = Process.Start(start);
                    if (!ready.WaitOne(2500) || guardian == null || guardian.HasExited) throw new InvalidOperationException("恢复守护进程未就绪");
                }
                hidden = target; shellProcess = process;
                if (!SetVisible(target, process, visible)) { Restore(); error = "图标显隐失败，已请求恢复。"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException || ex is System.IO.IOException)) throw;
                Restore(); error = "未修改桌面图标：" + ex.Message; return false;
            }
        }
        private static bool SetVisible(IntPtr window, uint process, bool visible)
        {
            if (!Valid(window, process)) return false;
            if (IsWindowVisible(window) == visible) return true;
            if (!ShowWindowAsync(window, visible ? 5 : 0)) return false;
            for (int i = 0; i < 25; i++) { if (!Valid(window, process)) return false; if (IsWindowVisible(window) == visible) return true; Thread.Sleep(20); }
            return false;
        }
        internal bool Restore()
        {
            if (hidden != IntPtr.Zero && Valid(hidden, shellProcess) && !SetVisible(hidden, shellProcess, originalVisible)) return false;
            // A hidden original state is restored when folding the workspace, but the
            // guardian stays armed so terminating the application still shows the icons.
            if (hidden != IntPtr.Zero && !originalVisible && Valid(hidden, shellProcess)) return true;
            hidden = IntPtr.Zero; ReleaseGuardian(); return true;
        }
        private static bool ShowIconsForExit(IntPtr window, uint process)
        {
            // Explorer may be rebuilding its desktop during exit. Reacquire the HWND
            // and confirm visibility before cancelling the recovery guardian.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (!Valid(window, process)) window = FindIcons(out process);
                if (window != IntPtr.Zero && SetVisible(window, process, true)) return true;
                if (attempt < 9) Thread.Sleep(100);
            }
            return false;
        }
        internal bool RestoreForExit()
        {
            if (hidden == IntPtr.Zero) return true;
            if (!ShowIconsForExit(hidden, shellProcess)) return false;
            hidden = IntPtr.Zero; ReleaseGuardian(); return true;
        }
        private void ReleaseGuardian()
        {
            if (stop != null) { stop.Set(); stop.Dispose(); stop = null; }
            if (guardian != null) { guardian.Dispose(); guardian = null; }
        }
        internal static int RunGuardian(string[] args)
        {
            int parent; long handle; uint process;
            if (args.Length != 6 || !int.TryParse(args[1], out parent) || !long.TryParse(args[2], out handle) || !uint.TryParse(args[3], out process) || !args[4].StartsWith(@"Local\DesktopTidy.IconGuard.", StringComparison.Ordinal)) return 2;
            if (args[5] != "0" && args[5] != "1") return 2;
            IntPtr window = new IntPtr(handle); if (!Valid(window, process)) return 2;
            try
            {
                using (var parentProcess = Process.GetProcessById(parent))
                using (var ready = EventWaitHandle.OpenExisting(args[4] + ".ready"))
                using (var cancel = EventWaitHandle.OpenExisting(args[4] + ".stop"))
                {
                    ready.Set();
                    while (!parentProcess.HasExited) if (cancel.WaitOne(500)) return 0;
                    return ShowIconsForExit(window, process) ? 0 : 1;
                }
            }
            catch (ArgumentException) { return ShowIconsForExit(window, process) ? 0 : 1; }
            catch (WaitHandleCannotBeOpenedException) { return 2; }
        }
        public void Dispose()
        {
            if (hidden == IntPtr.Zero) { ReleaseGuardian(); return; }
            if (guardian != null && !guardian.HasExited)
            {
                // Let the already-running recovery process restore icons after this process exits.
                hidden = IntPtr.Zero;
                if (stop != null) { stop.Dispose(); stop = null; }
                guardian.Dispose(); guardian = null;
                return;
            }
            RestoreForExit();
        }
    }
}
