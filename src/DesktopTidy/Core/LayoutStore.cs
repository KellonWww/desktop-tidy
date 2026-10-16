using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DesktopTidy.Core
{
    [DataContract] internal sealed class Assignment
    {
        [DataMember] public string Identity;
        [DataMember] public Category Category;
    }
    [DataContract] internal sealed class GroupState
    {
        [DataMember] public Category Category;
        [DataMember] public int X;
        [DataMember] public int Y;
        [DataMember] public int Width = 280;
        [DataMember] public int Height = 260;
        [DataMember] public bool Collapsed;
        [DataMember] public bool SortByName;
        [DataMember] public string Title;
    }
    internal enum CategoryOrderMode { Custom, CountDescending, CountAscending }
    [DataContract] internal sealed class LayoutState
    {
        [DataMember] public int Schema = 2;
        [DataMember] public int OpacityPercent = 100;
        [DataMember] public bool HideIconsOnStartup;
        [DataMember] public bool FirstRunCompleted;
        [DataMember] public bool HideEmptyGroups;
        [DataMember] public long LastRefreshUtcTicks;
        [DataMember] public CategoryOrderMode OrderMode;
        [DataMember] public int WorkspaceX, WorkspaceY, WorkspaceWidth, WorkspaceHeight;
        [DataMember] public string WorkspaceScreen;
        [DataMember] public int WorkspaceDpi, WorkspaceWorkX, WorkspaceWorkY;
        [DataMember] public List<GroupState> Groups = new List<GroupState>();
        [DataMember] public List<Assignment> Assignments = new List<Assignment>();
        [DataMember] public List<Category> DeletedCategories = new List<Category>();
    }
    internal sealed class LayoutStore
    {
        private readonly string directory;
        private readonly DataContractJsonSerializer layoutSerializer = new DataContractJsonSerializer(typeof(LayoutState));
        private readonly DataContractJsonSerializer desktopSerializer = new DataContractJsonSerializer(typeof(DesktopTidy.Windows.DesktopReadResult));
        internal string Error { get; private set; }
        internal LayoutStore(string directory) { this.directory = directory; }
        internal LayoutState Load() { return Read("layout.json") ?? new LayoutState(); }
        private LayoutState Read(string name)
        {
            try
            {
                string path = Path.Combine(directory, name);
                if (!File.Exists(path)) return null;
                if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("布局文件过大");
                using (var stream = File.OpenRead(path))
                {
                    var state = (LayoutState)layoutSerializer.ReadObject(stream);
                    if (state != null && state.DeletedCategories == null) state.DeletedCategories = new List<Category>();
                    if (state != null && state.Schema == 1)
                    {
                        if (state.Groups == null) throw new InvalidDataException("布局分组缺失");
                        state.Schema = 2; state.OpacityPercent = 100; state.HideIconsOnStartup = false;
                        foreach (var group in state.Groups)
                        {
                            if (group == null) throw new InvalidDataException("布局分组为空");
                            int index = (int)group.Category; group.X = 12 + index % 3 * 292; group.Y = 12 + index / 3 * 272;
                        }
                    }
                    if (state != null && state.OpacityPercent >= 0 && state.OpacityPercent <= 100)
                        state.OpacityPercent = NormalizeOpacity(state.OpacityPercent);
                    if (state != null && state.Groups != null && state.Assignments != null)
                        state.Assignments.RemoveAll(delegate(Assignment item) { return item != null && Classification.IsCustom(item.Category) && !state.Groups.Exists(delegate(GroupState group) { return group != null && group.Category == item.Category; }); });
                    Validate(state); return state;
                }
            }
            catch (Exception ex) { if (!Expected(ex)) throw; Error = "布局读取失败：" + ex.Message; return null; }
        }
        private static bool Expected(Exception ex)
        {
            return ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException || ex is ArgumentException || ex is System.Security.SecurityException;
        }
        internal static int NormalizeOpacity(int value)
        {
            return Math.Max(25, Math.Min(100, value));
        }
        private static void Validate(LayoutState state)
        {
            if (state == null || state.Schema != 2 || state.OpacityPercent < 25 || state.OpacityPercent > 100 || state.Groups == null || state.Assignments == null || state.DeletedCategories == null || state.DeletedCategories.Count > Enum.GetValues(typeof(Category)).Length || state.Groups.Count > Enum.GetValues(typeof(Category)).Length || state.Assignments.Count > 3000) throw new InvalidDataException("布局格式不受支持");
            if (state.WorkspaceWidth != 0 || state.WorkspaceHeight != 0)
                if (state.WorkspaceWidth < 320 || state.WorkspaceWidth > 16384 || state.WorkspaceHeight < 200 || state.WorkspaceHeight > 16384 || Math.Abs((long)state.WorkspaceX) > 100000 || Math.Abs((long)state.WorkspaceY) > 100000)
                    throw new InvalidDataException("工作区窗口边界无效");
            if (state.LastRefreshUtcTicks < 0 || state.LastRefreshUtcTicks > DateTime.MaxValue.Ticks) throw new InvalidDataException("刷新时间无效");
            if (state.WorkspaceDpi < 0 || state.WorkspaceDpi > 960 || (state.WorkspaceScreen != null && state.WorkspaceScreen.Length > 128) || Math.Abs((long)state.WorkspaceWorkX) > 100000 || Math.Abs((long)state.WorkspaceWorkY) > 100000) throw new InvalidDataException("工作区屏幕参数无效");
            if (!Enum.IsDefined(typeof(CategoryOrderMode), state.OrderMode)) throw new InvalidDataException("排列模式无效");
            var deleted = new HashSet<Category>();
            foreach (Category category in state.DeletedCategories)
                if (!Enum.IsDefined(typeof(Category), category) || Classification.IsCustom(category) || !deleted.Add(category)) throw new InvalidDataException("已删除分类记录无效");
            var groups = new HashSet<Category>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in state.Groups)
                if (group == null || !Enum.IsDefined(typeof(Category), group.Category) || !groups.Add(group.Category) || deleted.Contains(group.Category) || group.X < 0 || group.X > 4000 || group.Y < 0 || group.Y > 4000 || group.Width < 220 || group.Width > 2000 || group.Height < 140 || group.Height > 2000 || (group.Title != null && (group.Title.Length > 40 || group.Title.IndexOfAny(new[] { (char)10, (char)13, (char)0 }) >= 0))) throw new InvalidDataException("分类框参数无效");
            foreach (var item in state.Assignments)
                if (item == null || string.IsNullOrEmpty(item.Identity) || item.Identity.Length > 32768 || !Enum.IsDefined(typeof(Category), item.Category) || !paths.Add(item.Identity) || deleted.Contains(item.Category) || (Classification.IsCustom(item.Category) && !groups.Contains(item.Category))) throw new InvalidDataException("分类归属无效");
        }
        private bool Write(string name, LayoutState state)
        {
            string temp = Path.Combine(directory, name + ".tmp");
            try
            {
                Validate(state); Directory.CreateDirectory(directory);
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { layoutSerializer.WriteObject(stream, state); if (stream.Length > 1048576) throw new InvalidDataException("布局文件超过 1 MiB 限制"); stream.Flush(true); }
                string target = Path.Combine(directory, name);
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                Error = null; return true;
            }
            catch (Exception ex) { if (!Expected(ex)) throw; Error = "布局保存失败：" + ex.Message; return false; }
        }
        internal bool BackupDesktop(DesktopTidy.Windows.DesktopReadResult result)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string target = Path.Combine(directory, "desktop-snapshot.json"), temp = target + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { desktopSerializer.WriteObject(stream, result); stream.Flush(true); }
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                Error = null; return true;
            }
            catch (Exception ex) { if (!Expected(ex)) throw; Error = "桌面快照保存失败：" + ex.Message; return false; }
        }
        internal bool Save(LayoutState state) { return Write("layout.json", state); }
        internal bool Checkpoint(LayoutState state) { return Write("undo.json", state); }
        internal LayoutState ReadUndo() { return Read("undo.json"); }
    }
}
