using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopTidy.Windows
{
    // Explorer's desktop hierarchy is private: fail closed instead of showing a floating fallback.
    internal static class DesktopAttachment
    {
        private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        internal static bool IsAttached(Form form)
        {
            if (!form.IsHandleCreated || !IsWindow(form.Handle)) return false;
            IntPtr parent = GetParent(form.Handle), shell = GetShellWindow();
            if (parent == IntPtr.Zero || shell == IntPtr.Zero || !IsWindow(parent)) return false;
            uint process, shellProcess;
            GetWindowThreadProcessId(parent, out process); GetWindowThreadProcessId(shell, out shellProcess);
            var name = new StringBuilder(64); GetClassName(parent, name, name.Capacity);
            return process == shellProcess && name.ToString() == "SHELLDLL_DefView";
        }
        internal static bool Position(Form form, Rectangle screenBounds)
        {
            if (!IsAttached(form)) return false;
            Point client = screenBounds.Location;
            return ScreenToClient(GetParent(form.Handle), ref client) &&
                SetWindowPos(form.Handle, IntPtr.Zero, client.X, client.Y, screenBounds.Width, screenBounds.Height, 0x10 | 0x20);
        }
        internal static bool RestoreChild(Control child, Control parent)
        {
            // Embedded Forms may be parked by WinForms during their top-level HWND recreation.
            if (GetParent(child.Handle) != parent.Handle) SetParent(child.Handle, parent.Handle);
            return GetParent(child.Handle) == parent.Handle && SetWindowPos(child.Handle, IntPtr.Zero,
                child.Left, child.Top, child.Width, child.Height, 0x10 | 0x20);
        }
        internal static IntPtr FindHost()
        {
            IntPtr shell = GetShellWindow(); uint shellProcess;
            if (shell == IntPtr.Zero) return IntPtr.Zero;
            GetWindowThreadProcessId(shell, out shellProcess);
            IntPtr host = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                uint process; GetWindowThreadProcessId(window, out process);
                if (process != shellProcess) return true;
                var name = new StringBuilder(64); GetClassName(window, name, name.Capacity);
                if (name.ToString() != "Progman" && name.ToString() != "WorkerW") return true;
                IntPtr view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view == IntPtr.Zero) return true;
                host = view; return false;
            }, IntPtr.Zero);
            return host;
        }
        internal static bool Attach(Form form, Point screenLocation)
        {
            if (IsAttached(form)) return Position(form, new Rectangle(screenLocation, form.Size));
            IntPtr host = FindHost();
            if (host == IntPtr.Zero) return false;
            IntPtr handle = form.Handle, style = GetWindowLongPtr(handle, -16), originalParent = GetParent(handle);
            if (originalParent == host) return Position(form, new Rectangle(screenLocation, form.Size));
            // Only our own HWND is modified; no Explorer styles or icon positions are touched.
            long childStyle = (style.ToInt64() & ~0x80000000L) | 0x40000000L;
            SetWindowLongPtr(handle, -16, new IntPtr(childStyle));
            SetParent(handle, host);
            if (GetParent(handle) != host)
            { SetParent(handle, originalParent); SetWindowLongPtr(handle, -16, style); return false; }
            Point client = screenLocation;
            if (!ScreenToClient(host, ref client) || !SetWindowPos(handle, IntPtr.Zero, client.X, client.Y, form.Width, form.Height, 0x10 | 0x20))
            { SetParent(handle, originalParent); SetWindowLongPtr(handle, -16, style); return false; }
            return true;
        }
    }
}
