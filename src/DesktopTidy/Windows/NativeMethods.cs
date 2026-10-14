using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopTidy.Windows
{
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessMemoryCounters
        {
            internal uint Size, PageFaultCount;
            internal UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
        internal static double PrivateMiB()
        {
            var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf(typeof(ProcessMemoryCounters)) };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return counters.PrivateUsage.ToUInt64() / 1048576.0;
        }
        internal const int CommandMessage = 0x8000 + 44;
        internal const int ResultMessage = 0x8000 + 45;
        internal const int IconsMessage = 0x8000 + 46;
        internal const int SettingChange = 0x001A;
        internal const int ThemeChanged = 0x031A;
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    internal sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action<int, int> received;
        internal MessageWindow(string title, Action<int, int> received)
        {
            this.received = received;
            CreateHandle(new CreateParams { Caption = title, Style = unchecked((int)0x80000000) });
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.CommandMessage || message.Msg == NativeMethods.ResultMessage ||
                message.Msg == NativeMethods.SettingChange || message.Msg == NativeMethods.ThemeChanged || message.Msg == NativeMethods.IconsMessage)
                received(message.Msg, message.WParam.ToInt32());
            base.WndProc(ref message);
        }
        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }
}
