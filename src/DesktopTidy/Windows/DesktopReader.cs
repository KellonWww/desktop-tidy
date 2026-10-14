using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace DesktopTidy.Windows
{
    [DataContract]
    internal sealed class DesktopIcon
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string ParsingName { get; set; }
        [DataMember] public int X { get; set; }
        [DataMember] public int Y { get; set; }
    }
    [DataContract]
    internal sealed class DesktopReadResult
    {
        [DataMember] public bool Success { get; set; }
        [DataMember] public string Error { get; set; }
        [DataMember] public int ItemCount { get; set; }
        [DataMember] public int Skipped { get; set; }
        [DataMember] public List<DesktopIcon> Icons { get; set; }
    }

    // 只读 PoC：有意不声明 SelectAndPositionItems，不提供任何布局写入口。
    internal static class DesktopReader
    {
        private static readonly Guid ShellWindowsClass = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid TopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid ShellBrowserId = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid PersistFolderId = new Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F");
        internal static DesktopReadResult Read()
        {
            var result = new DesktopReadResult { Icons = new List<DesktopIcon>() };
            var owned = new List<object>();
            IntPtr root = IntPtr.Zero;
            try
            {
                object windowsObject = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellWindowsClass, true));
                owned.Add(windowsObject);
                var windows = (IShellWindows)windowsObject;
                object location = 0, locationRoot = null, desktop;
                int hwnd;
                Check(windows.FindWindowSW(ref location, ref locationRoot, 8, out hwnd, 1, out desktop));
                if (desktop == null) throw new InvalidOperationException("未找到 Explorer 桌面 Shell 视图。");
                owned.Add(desktop);
                var provider = (IComServiceProvider)desktop;
                Guid service = TopLevelBrowser, browserId = ShellBrowserId;
                object browserObject;
                Check(provider.QueryService(ref service, ref browserId, out browserObject));
                owned.Add(browserObject);
                object viewObject;
                Check(((IShellBrowser)browserObject).QueryActiveShellView(out viewObject));
                owned.Add(viewObject);
                var view = (IFolderView)viewObject;
                Guid folderId = PersistFolderId;
                object folderObject;
                Check(view.GetFolder(ref folderId, out folderObject));
                owned.Add(folderObject);
                Check(((IPersistFolder2)folderObject).GetCurFolder(out root));
                int count;
                Check(view.ItemCount(2, out count)); // SVGIO_ALLVIEW
                result.ItemCount = count;
                if (count < 0 || count > 3000) throw new InvalidOperationException("桌面项数量超出只读原型的 3000 项上限。");
                for (int i = 0; i < count; i++)
                {
                    IntPtr relative = IntPtr.Zero, absolute = IntPtr.Zero;
                    try
                    {
                        Check(view.Item(i, out relative));
                        if (relative == IntPtr.Zero) throw new InvalidOperationException("Shell 返回空 PIDL。");
                        Point point;
                        Check(view.GetItemPosition(relative, out point));
                        absolute = ILCombine(root, relative);
                        if (absolute == IntPtr.Zero) throw new OutOfMemoryException("PIDL 合并失败。");
                        string identity = GetName(absolute, 0x80028000); // SIGDN_DESKTOPABSOLUTEPARSING
                        if (string.IsNullOrEmpty(identity)) throw new InvalidOperationException("Shell 项没有可验证的名称。");
                        result.Icons.Add(new DesktopIcon { Name = GetName(absolute, 0), ParsingName = identity, X = point.X, Y = point.Y });
                    }
                    catch (COMException) { result.Skipped++; }
                    catch (InvalidOperationException) { result.Skipped++; }
                    finally
                    {
                        if (absolute != IntPtr.Zero) Marshal.FreeCoTaskMem(absolute);
                        if (relative != IntPtr.Zero) Marshal.FreeCoTaskMem(relative);
                    }
                }
                int finalCount;
                Check(view.ItemCount(2, out finalCount));
                var identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var icon in result.Icons) identities.Add(icon.ParsingName);
                if (finalCount != count || identities.Count != result.Icons.Count)
                    throw new InvalidOperationException("读取期间桌面项发生变化，请重试。");
                result.Success = result.Skipped == 0;
                if (!result.Success) result.Error = "部分项读取失败；此结果不能作为完整布局备份。";
            }
            catch (Exception ex) { result.Success = false; result.Error = ex.GetType().Name + ": " + ex.Message; }
            finally
            {
                if (root != IntPtr.Zero) Marshal.FreeCoTaskMem(root);
                // 每个自有 RCW 仅释放一次；类型转换不当成额外所有权，不用 FinalReleaseComObject。
                for (int i = owned.Count - 1; i >= 0; i--)
                {
                    object value = owned[i];
                    bool duplicate = false;
                    for (int j = i + 1; j < owned.Count; j++) if (ReferenceEquals(value, owned[j])) duplicate = true;
                    if (!duplicate && value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
                }
            }
            return result;
        }
        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        private static string GetName(IntPtr pidl, uint kind)
        {
            IntPtr text;
            Check(SHGetNameFromIDList(pidl, kind, out text));
            try { return Marshal.PtrToStringUni(text); }
            finally { if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text); }
        }
        [DllImport("shell32.dll")] private static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
        [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, uint kind, out IntPtr text);
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }

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
        [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView
        {
            [PreserveSig] int GetCurrentViewMode(out uint mode);
            [PreserveSig] int SetCurrentViewMode(uint mode); // 仅为维持 vtable 顺序，不调用。
            [PreserveSig] int GetFolder(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object folder);
            [PreserveSig] int Item(int index, out IntPtr pidl);
            [PreserveSig] int ItemCount(uint flags, out int count);
            [PreserveSig] int Items(uint flags, ref Guid interfaceId, out IntPtr items);
            [PreserveSig] int GetSelectionMarkedItem(out int index);
            [PreserveSig] int GetFocusedItem(out int index);
            [PreserveSig] int GetItemPosition(IntPtr pidl, out Point point);
        }
        [ComImport, Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFolder2
        {
            [PreserveSig] int GetClassID(out Guid id);
            [PreserveSig] int Initialize(IntPtr pidl);
            [PreserveSig] int GetCurFolder(out IntPtr pidl);
        }
    }
}
