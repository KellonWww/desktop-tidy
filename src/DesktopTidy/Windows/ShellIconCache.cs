using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DesktopTidy.Core;

namespace DesktopTidy.Windows
{
    internal sealed class ShellIconCache : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfo
        {
            internal IntPtr Icon;
            internal int Index;
            internal uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out FileInfo info, uint size, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private readonly object gate = new object();
        private readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> revisions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool disposed;
        private static string Key(WorkspaceItem item)
        {
            if (item.IsDirectory) return "folder";
            string extension = Path.GetExtension(item.Path);
            return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ? item.Path : "ext:" + extension;
        }
        private sealed class IconRequest
        {
            internal string Key;
            internal string Path;
            internal int Revision;
        }
        private int Revision(string key)
        {
            int value;
            return revisions.TryGetValue(key, out value) ? value : 0;
        }
        internal void Load(IList<WorkspaceItem> items, Action ready)
        {
            var pending = new List<IconRequest>();
            lock (gate)
            {
                if (disposed) return;
                foreach (var item in items)
                {
                    string key = Key(item);
                    if (requested.Count == 64) break;
                    if (requested.Add(key)) pending.Add(new IconRequest { Key = key, Path = item.Path, Revision = Revision(key) });
                }
            }
            if (pending.Count == 0) return;
            var thread = new Thread(delegate()
            {
                foreach (var request in pending)
                {
                    lock (gate) { if (disposed) return; }
                    uint flags = 0x100;
                    string path = request.Path; uint attributes = 0x80;
                    if (request.Key == "folder") { path = "folder"; attributes = 0x10; flags |= 0x10; }
                    else if (request.Key.StartsWith("ext:", StringComparison.Ordinal)) { path = "item" + request.Key.Substring(4); flags |= 0x10; }
                    Icon copy = Extract(path, attributes, flags);
                    lock (gate)
                    {
                        if (disposed) { if (copy != null) copy.Dispose(); return; }
                        if (request.Revision != Revision(request.Key)) { if (copy != null) copy.Dispose(); continue; }
                        Icon previous;
                        if (copy != null && icons.TryGetValue(request.Key, out previous)) previous.Dispose();
                        if (copy != null) icons[request.Key] = copy;
                    }
                }
                ready();
            });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        internal void Refresh(WorkspaceItem item, Action ready)
        {
            if (item == null) return;
            string key = Key(item);
            lock (gate)
            {
                if (disposed) return;
                revisions[key] = Revision(key) + 1;
                requested.Remove(key);
                Icon previous;
                if (icons.TryGetValue(key, out previous)) { icons.Remove(key); previous.Dispose(); }
            }
            Load(new[] { item }, ready);
        }
        private static Icon Extract(string path, uint attributes, uint flags)
        {
            FileInfo info;
            SHGetFileInfo(path, attributes, out info, (uint)Marshal.SizeOf(typeof(FileInfo)), flags);
            if (info.Icon == IntPtr.Zero) return null;
            try { using (var borrowed = Icon.FromHandle(info.Icon)) return (Icon)borrowed.Clone(); }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
            finally { DestroyIcon(info.Icon); }
        }
        internal void Draw(Graphics graphics, WorkspaceItem item, Rectangle bounds)
        {
            string key = Key(item);
            lock (gate) { Icon icon; graphics.DrawIcon(!disposed && icons.TryGetValue(key, out icon) ? icon : SystemIcons.Application, bounds); }
        }
        internal int Count { get { lock (gate) return icons.Count; } }
        public void Dispose() { lock (gate) { disposed = true; foreach (var icon in icons.Values) icon.Dispose(); icons.Clear(); requested.Clear(); revisions.Clear(); } }
    }
}
