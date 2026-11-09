using System;
using System.Runtime.InteropServices;

namespace DesktopTidy.Windows
{
    // Acquire the live Explorer view on the calling STA; never cache it across Explorer restarts.
    internal static class DesktopShellView
    {
        private static readonly Guid ShellWindowsClass = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid TopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid ShellBrowserId = new Guid("000214E2-0000-0000-C000-000000000046");
        private const uint NoIcons = 0x1000; // FWF_NOICONS, not SW_HIDE: let Explorer retain background commands.

        // The caller owns the returned COM reference; discovery objects are released here.
        internal static object Open()
        {
            object windowsObject = null, desktop = null, browserObject = null;
            try
            {
                windowsObject = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellWindowsClass, true));
                object location = 0, locationRoot = null;
                int hwnd;
                Marshal.ThrowExceptionForHR(((IShellWindows)windowsObject).FindWindowSW(ref location, ref locationRoot, 8, out hwnd, 1, out desktop));
                if (desktop == null) throw new InvalidOperationException("未找到 Explorer 桌面 Shell 视图。");
                Guid service = TopLevelBrowser, browserId = ShellBrowserId;
                Marshal.ThrowExceptionForHR(((IComServiceProvider)desktop).QueryService(ref service, ref browserId, out browserObject));
                object view;
                Marshal.ThrowExceptionForHR(((IShellBrowser)browserObject).QueryActiveShellView(out view));
                return view;
            }
            finally { Release(browserObject); Release(desktop); Release(windowsObject); }
        }

        // Read Shell's icon flag rather than inferring its state from Explorer's internal HWNDs.
        internal static bool IconsVisible()
        {
            object view = Open();
            try
            {
                uint flags;
                Marshal.ThrowExceptionForHR(((IFolderView2)view).GetCurrentFolderFlags(out flags));
                return (flags & NoIcons) == 0;
            }
            finally { Release(view); }
        }

        // Mask only NOICONS, preserving Explorer's sorting, positioning and all other view flags.
        internal static bool SetIconsVisible(bool visible)
        {
            object view = Open();
            try
            {
                var folder = (IFolderView2)view;
                Marshal.ThrowExceptionForHR(folder.SetCurrentFolderFlags(NoIcons, visible ? 0 : NoIcons));
                uint flags;
                Marshal.ThrowExceptionForHR(folder.GetCurrentFolderFlags(out flags));
                return ((flags & NoIcons) == 0) == visible;
            }
            finally { Release(view); }
        }

        // Create the background menu in this process, not through Explorer's remote view.
        // The remote view may populate entries but return E_INVALIDARG from QueryContextMenu.
        internal static object BackgroundMenu(IntPtr owner, Guid menuId)
        {
            IShellFolder folder;
            SHGetDesktopFolder(out folder);
            try
            {
                object menu;
                Marshal.ThrowExceptionForHR(folder.CreateViewObject(owner, ref menuId, out menu));
                return menu;
            }
            finally { Release(folder); }
        }

        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHGetDesktopFolder(out IShellFolder folder);

        internal static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IShellWindows
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item([MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.IDispatch)] out object item);
            [PreserveSig] int NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object enumerator);
            [PreserveSig] int Register([MarshalAs(UnmanagedType.IDispatch)] object item, int hwnd, int windowClass, out int cookie);
            [PreserveSig] int RegisterPending(int threadId, ref object location, ref object locationRoot, int windowClass, out int cookie);
            [PreserveSig] int Revoke(int cookie);
            [PreserveSig] int OnNavigate(int cookie, ref object location);
            [PreserveSig] int OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
            [PreserveSig] int FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object locationRoot,
                int windowClass, out int hwnd, int options, [MarshalAs(UnmanagedType.IDispatch)] out object item);
        }
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IComServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        }
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            [PreserveSig] int GetWindow(out IntPtr hwnd);
            [PreserveSig] int ContextSensitiveHelp(int enter);
            [PreserveSig] int InsertMenusSB(IntPtr menu, IntPtr widths);
            [PreserveSig] int SetMenuSB(IntPtr menu, IntPtr reserved, IntPtr hwnd);
            [PreserveSig] int RemoveMenusSB(IntPtr menu);
            [PreserveSig] int SetStatusTextSB(IntPtr text);
            [PreserveSig] int EnableModelessSB(int enable);
            [PreserveSig] int TranslateAcceleratorSB(IntPtr message, ushort id);
            [PreserveSig] int BrowseObject(IntPtr pidl, uint flags);
            [PreserveSig] int GetViewStateStream(uint mode, out IntPtr stream);
            [PreserveSig] int GetControlWindow(uint id, out IntPtr hwnd);
            [PreserveSig] int SendControlMsg(uint id, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
            [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
        }
        // Prefix declarations preserve the SDK vtable order; unused slots are never invoked.
        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr owner, IntPtr context, IntPtr name, IntPtr eaten, out IntPtr item, IntPtr attributes);
            [PreserveSig] int EnumObjects(IntPtr owner, uint flags, out IntPtr items);
            [PreserveSig] int BindToObject(IntPtr item, IntPtr context, ref Guid id, out IntPtr value);
            [PreserveSig] int BindToStorage(IntPtr item, IntPtr context, ref Guid id, out IntPtr value);
            [PreserveSig] int CompareIDs(IntPtr parameter, IntPtr first, IntPtr second);
            [PreserveSig] int CreateViewObject(IntPtr owner, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out object value);
        }

        [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView2
        {
            [PreserveSig] int GetCurrentViewMode(out uint mode);
            [PreserveSig] int SetCurrentViewMode(uint mode);
            [PreserveSig] int GetFolder(ref Guid id, out IntPtr folder);
            [PreserveSig] int Item(int index, out IntPtr item);
            [PreserveSig] int ItemCount(uint flags, out int count);
            [PreserveSig] int Items(uint flags, ref Guid id, out IntPtr items);
            [PreserveSig] int GetSelectionMarkedItem(out int index);
            [PreserveSig] int GetFocusedItem(out int index);
            [PreserveSig] int GetItemPosition(IntPtr item, IntPtr point);
            [PreserveSig] int GetSpacing(IntPtr point);
            [PreserveSig] int GetDefaultSpacing(IntPtr point);
            [PreserveSig] int GetAutoArrange();
            [PreserveSig] int SelectItem(int index, uint flags);
            [PreserveSig] int SelectAndPositionItems(uint count, IntPtr items, IntPtr points, uint flags);
            [PreserveSig] int SetGroupBy(IntPtr key, int ascending);
            [PreserveSig] int GetGroupBy(IntPtr key, IntPtr ascending);
            [PreserveSig] int SetViewProperty(IntPtr item, IntPtr key, IntPtr value);
            [PreserveSig] int GetViewProperty(IntPtr item, IntPtr key, IntPtr value);
            [PreserveSig] int SetTileViewProperties(IntPtr item, IntPtr properties);
            [PreserveSig] int SetExtendedTileViewProperties(IntPtr item, IntPtr properties);
            [PreserveSig] int SetText(uint type, IntPtr text);
            [PreserveSig] int SetCurrentFolderFlags(uint mask, uint flags);
            [PreserveSig] int GetCurrentFolderFlags(out uint flags);
        }
    }
}
