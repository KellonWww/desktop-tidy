using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopTidy.Windows
{
    // Shell objects and menus are owned by this STA call, never exposed to JavaScript.
    internal static class ShellItemMenu
    {
        private const uint FirstCommand = 1, LastCommand = 0x7fff;
        private const uint CanRename = 0x10, ReturnCommand = 0x100, RightButton = 2;
        private const uint AllowUndo = 0x40, WantNukeWarning = 0x4000, RecycleOnDelete = 0x80000;
        private static readonly Guid ShellItemId = typeof(IShellItem).GUID;
        private static readonly Guid ShellUiObject = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5");
        private static readonly Guid ContextMenuId = typeof(IContextMenu).GUID;

        internal static void Show(string path, Point position, Action rename, Action delete, Action changed)
        {
            IShellItem item = null; object menuObject = null; IntPtr popup = IntPtr.Zero;
            try
            {
                Guid itemId = ShellItemId;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out item);
                Guid handler = ShellUiObject, menuId = ContextMenuId;
                item.BindToHandler(IntPtr.Zero, ref handler, ref menuId, out menuObject);
                var menu = (IContextMenu)menuObject;
                popup = CreatePopupMenu();
                if (popup == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                Marshal.ThrowExceptionForHR(menu.QueryContextMenu(popup, 0, FirstCommand, LastCommand, CanRename));
                using (var window = new MenuWindow(menuObject, position))
                {
                    window.Show(); window.Activate();
                    uint command = TrackPopupMenuEx(popup, ReturnCommand | RightButton, position.X, position.Y, window.Handle, IntPtr.Zero);
                    if (command == 0) return;
                    uint offset = command - FirstCommand;
                    var verb = new StringBuilder(256);
                    int result = menu.GetCommandString(new UIntPtr(offset), 4, IntPtr.Zero, verb, (uint)verb.Capacity);
                    Marshal.ThrowExceptionForHR(result);
                    string name = verb.ToString();
                    if (string.IsNullOrEmpty(name)) throw new NotSupportedException("系统菜单未返回可验证的操作名称。");
                    window.Hide();
                    // Rename is a host action; delete always requires our explicit confirmation.
                    if (name.Equals("rename", StringComparison.OrdinalIgnoreCase)) { rename(); return; }
                    if (name.Equals("delete", StringComparison.OrdinalIgnoreCase)) { delete(); return; }
                    var invoke = new InvokeInfo { Size = Marshal.SizeOf(typeof(InvokeInfo)), Mask = 0x100, Window = window.Handle,
                        Verb = new IntPtr(offset), Show = 1 };
                    menu.InvokeCommand(ref invoke);
                    changed();
                }
            }
            finally
            {
                if (popup != IntPtr.Zero) DestroyMenu(popup);
                Release(menuObject); Release(item);
            }
        }

        internal static bool Recycle(string path, IntPtr owner)
        { return Operate(path, null, owner); }
        internal static bool Rename(string path, string name, IntPtr owner)
        { return Operate(path, name, owner); }
        private static bool Operate(string path, string name, IntPtr owner)
        {
            IShellItem item = null; IFileOperation operation = null;
            try
            {
                Guid itemId = ShellItemId;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out item);
                operation = (IFileOperation)new FileOperation();
                operation.SetOwnerWindow(owner);
                // Do not suppress Windows error, collision, elevation or permanent-deletion warnings.
                operation.SetOperationFlags(AllowUndo | WantNukeWarning | RecycleOnDelete);
                if (name == null) operation.DeleteItem(item, IntPtr.Zero);
                else operation.RenameItem(item, name, IntPtr.Zero);
                operation.PerformOperations();
                bool aborted; operation.GetAnyOperationsAborted(out aborted);
                return !aborted;
            }
            finally { Release(operation); Release(item); }
        }
        private static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        // A top-level owner keeps the menu active even though the workspace is a WorkerW child.
        private sealed class MenuWindow : Form
        {
            private readonly IContextMenu2 menu2;
            private readonly IContextMenu3 menu3;
            internal MenuWindow(object menu, Point position)
            {
                menu2 = menu as IContextMenu2; menu3 = menu as IContextMenu3;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; Opacity = 0;
                StartPosition = FormStartPosition.Manual; Bounds = new Rectangle(position, new Size(1, 1));
            }
            protected override void WndProc(ref Message message)
            {
                // Forward owner-drawn entries and dynamic submenus to installed Shell extensions.
                if (message.Msg == 0x117 || message.Msg == 0x2b || message.Msg == 0x2c || message.Msg == 0x120)
                {
                    IntPtr result;
                    if (menu3 != null && menu3.HandleMenuMsg2((uint)message.Msg, message.WParam, message.LParam, out result) >= 0)
                    { message.Result = result; return; }
                    if (message.Msg != 0x120 && menu2 != null && menu2.HandleMenuMsg((uint)message.Msg, message.WParam, message.LParam) >= 0)
                    { message.Result = IntPtr.Zero; return; }
                }
                base.WndProc(ref message);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr binding, ref Guid id, out IShellItem item);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct InvokeInfo
        {
            internal int Size;
            internal uint Mask;
            internal IntPtr Window, Verb;
            [MarshalAs(UnmanagedType.LPStr)] internal string Parameters;
            [MarshalAs(UnmanagedType.LPStr)] internal string Directory;
            internal int Show;
            internal uint HotKey;
            internal IntPtr Icon;
        }
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out object value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint displayName, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
        [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
            void InvokeCommand(ref InvokeInfo info);
            [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint size);
        }
        [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu2
        {
            [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
            void InvokeCommand(ref InvokeInfo info);
            [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint size);
            [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        }
        [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu3
        {
            [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
            void InvokeCommand(ref InvokeInfo info);
            [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint size);
            [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
            [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        }
        [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09")] private class FileOperation { }
        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOperationFlags(uint flags);
            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
            void SetProgressDialog(IntPtr dialog);
            void SetProperties(IntPtr properties);
            void SetOwnerWindow(IntPtr window);
            void ApplyPropertiesToItem(IShellItem item);
            void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
            void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
            void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
            void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
            void DeleteItem(IShellItem item, IntPtr sink);
            void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
            void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
            void PerformOperations();
            void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }
    }
}
