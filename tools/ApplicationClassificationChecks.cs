using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DesktopTidy.Core;
using DesktopTidy.UI;
using DesktopTidy.Windows;

// Isolated smoke checks: fixtures only; never scan, hide or rearrange the live desktop.
internal static class ApplicationClassificationChecks
{
    private static int count;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); count++; }
    private static void Shortcut(string path, string target)
    {
        object shell = null, link = null;
        try
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type);
            link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        finally
        {
            if (link != null) Marshal.ReleaseComObject(link);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1 || Directory.Exists(args[0])) return 2;
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        try
        {
            string[] apps = { "QQ音乐", "网易云音乐", "豆瓣FM", "酷我音乐", "优酷", "爱奇艺", "腾讯视频", "芒果TV", "Steam", "Epic", "4399小游戏", "微博", "知乎", "百度贴吧", "虎扑社区" };
            foreach (string app in apps) Check(Classification.Classify(app + ".lnk", false) == Category.Entertainment, "entertainment " + app);
            Category[] categories = { Category.Chat, Category.Design, Category.Remote, Category.Downloads, Category.Security, Category.Tools, Category.Browsers, Category.Reading };
            string[] names = { "微信", "Photoshop", "ToDesk", "迅雷", "火绒安全", "Visual Studio Code", "Google Chrome", "Adobe Acrobat" };
            for (int i = 0; i < names.Length; i++) Check(Classification.Classify(names[i] + ".exe", false) == categories[i], "category " + names[i]);
            Check(Classification.Classify("QQ音乐 - 快捷方式.lnk", false) == Category.Entertainment, "shortcut suffix");
            Check(Classification.Classify("chrome (64-bit).lnk", false) == Category.Browsers, "architecture suffix");
            Check(Classification.Classify("photo.jpg", false, "Steam") == Category.Images, "files never guessed applications");
            Check(Classification.Classify("Steam", true) == Category.Folders, "directory precedence");
            Check(Classification.Classify("unknown.exe", false) == Category.Shortcuts, "unknown program fallback");
            Check(Classification.Classify("Steam备份工具.lnk", false) == Category.Shortcuts, "avoid fuzzy substring");
            Check(Classification.Classify("setup.exe", false) == Category.Shortcuts, "legacy exe behavior");
            foreach (string extension in new[] { ".msi", ".msix", ".appx", ".msixbundle" })
                Check(Classification.Classify("setup" + extension, false) == Category.Shortcuts, "installer merged into programs " + extension);
            Check(ApplicationClassifier.MatchName("QQMusic").Section == "音乐", "music section");
            Check(ApplicationClassifier.MatchName("QQLive").Section == "影视", "video section");
            Check(ApplicationClassifier.MatchName("EpicGamesLauncher").Section == "游戏", "game section");
            Check(ApplicationClassifier.MatchName("知乎").Section == "社交", "social section");
            Check(ApplicationClassifier.MatchUrl("https://v.qq.com/x/cover/demo").Category == Category.Entertainment, "video URL");
            Check(ApplicationClassifier.MatchUrl("https://v.qq.com.evil.example/") == null, "reject deceptive host");
            Check(ApplicationClassifier.MatchUrl("https://evil.example/?next=https://v.qq.com") == null, "ignore URL query");
            Check(ApplicationClassifier.MatchUrl("file:///C:/Steam.exe") == null, "ignore non-web scheme");
            string web = Path.Combine(root, "随意名称.url"); File.WriteAllText(web, "[InternetShortcut]\r\nURL=https://music.163.com/\r\n");
            Check(Classification.Classify(web, false) == Category.Entertainment, "URL content not filename");
            string deceptive = Path.Combine(root, "Steam.url"); File.WriteAllText(deceptive, "[InternetShortcut]\r\nURL=https://unknown.example/\r\n");
            Check(Classification.Classify(deceptive, false) == Category.Shortcuts, "unknown URL not guessed from name");
            string renamed = Path.Combine(root, "随意快捷方式.lnk"); Shortcut(renamed, Path.Combine(root, "Steam.exe"));
            byte[] before = File.ReadAllBytes(renamed);
            Check(Classification.Classify(renamed, false) == Category.Entertainment, "renamed shortcut target");
            Check(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(renamed)), "shortcut identification read-only");
            string broken = Path.Combine(root, "坏链接.lnk"); File.WriteAllText(broken, "invalid shortcut");
            Check(Classification.Classify(broken, false) == Category.Shortcuts, "broken shortcut fallback");
            string unc = Path.Combine(root, "网络链接.lnk"); Shortcut(unc, @"\\server\share\Steam.exe");
            Check(Classification.Classify(unc, false) == Category.Shortcuts, "UNC target not accessed");
            Check((int)Category.Custom1 == 8 && (int)Category.Custom16 == 23, "old category IDs unchanged");
            Check(!Classification.IsCustom(Category.Chat) && Classification.IsCustom(Category.Custom16), "built-in vs custom");
            var layout = new LayoutState();
            foreach (Category category in Enum.GetValues(typeof(Category))) layout.Groups.Add(new GroupState { Category = category });
            layout.Assignments.Add(new Assignment { Identity = renamed, Category = Category.Chat });
            var store = new LayoutStore(Path.Combine(root, "config"));
            Check(store.Save(layout), "all 33 groups persisted");
            var loaded = store.Load(); Check(loaded.Groups.Count == 33 && loaded.Assignments[0].Category == Category.Chat, "new built-in assignment persisted");
            var snapshot = new List<DesktopIcon> { new DesktopIcon { Name = "随意名称", ParsingName = renamed }, new DesktopIcon { Name = "音乐", ParsingName = web } };
            int skipped; var built = Classification.Build(snapshot, loaded, out skipped);
            Check(built[0].Category == Category.Chat && built[0].AutomaticCategory == Category.Entertainment, "manual assignment wins");
            Check(built[1].Section == "音乐" && skipped == 0, "automatic section from URL");
            loaded.Assignments.Clear(); built = Classification.Build(snapshot, loaded, out skipped);
            Check(built[0].Category == Category.Entertainment, "manual reset restores automatic classification");
            using (var font = new Font("Microsoft YaHei UI", 9F))
            using (var controller = new WorkspaceController(font, delegate { }, Path.Combine(root, "controller")))
            using (var icons = new ShellIconCache())
            using (var host = new System.Windows.Forms.Form())
            using (var box = new ClassificationBox(controller, new GroupState { Category = Category.Entertainment, Width = 560, Height = 600 }, icons, font))
            {
                Check(controller.Categories.Count == 16, "16 built-in categories initialized");
                Check(!controller.Categories.Contains(Category.Installers), "installer category removed");
                Check(controller.CategoryTitle(Category.Shortcuts) == "程序", "program category default title");
                Check(controller.TryDeleteCategory(Category.Chat), "built-in category can be deleted");
                Check(!controller.Categories.Contains(Category.Chat), "deleted built-in absent");
                controller.Undo();
                Check(controller.Categories.Contains(Category.Chat), "undo restores deleted built-in");
                var read = new DesktopReadResult { Success = true, Icons = snapshot };
                Check(controller.UpdateSnapshot(read, out skipped), "isolated snapshot accepted");
                var values = (List<WorkspaceItem>)typeof(WorkspaceController).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                var assignedItem = values.Find(delegate(WorkspaceItem item) { return item.Identity == renamed; });
                controller.Assign(assignedItem, Category.Chat);
                Check(assignedItem.Category == Category.Chat, "controller assignment retained after refresh");
                Check(controller.UpdateSnapshot(read, out skipped), "same snapshot refresh");
                values = (List<WorkspaceItem>)typeof(WorkspaceController).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                Check(values.Find(delegate(WorkspaceItem item) { return item.Identity == renamed; }).Category == Category.Chat, "rescan does not overwrite manual assignment");
                controller.ResetClassification();
                Check(values.Find(delegate(WorkspaceItem item) { return item.Identity == renamed; }).Category == Category.Entertainment, "controller reset retains automatic category");
                controller.Undo();
                Check(values.Find(delegate(WorkspaceItem item) { return item.Identity == renamed; }).Category == Category.Chat, "undo restores manual category");
                Check(controller.TryCreateCategory("我的应用"), "custom category creation retained");
                Check(controller.Categories.Contains(Category.Custom1), "first custom slot unchanged");
                Check(controller.TryDeleteCategory(Category.Custom1), "custom category deletion retained");
                var examples = new List<WorkspaceItem>();
                foreach (string app in apps)
                {
                    var match = ApplicationClassifier.MatchName(app);
                    examples.Add(new WorkspaceItem { Name = app, Path = Path.Combine(root, app + ".lnk"), Category = match.Category, AutomaticCategory = match.Category, Section = match.Section });
                }
                host.Controls.Add(box); box.SetItems(examples); host.CreateControl(); box.Show();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var headers = (Dictionary<int, string>)typeof(ClassificationBox).GetField("sectionHeaders", flags).GetValue(box);
                Check(headers.Count == 4, "four section headers");
                var hit = typeof(ClassificationBox).GetMethod("Hit", flags);
                var cell = typeof(ClassificationBox).GetMethod("Cell", flags);
                for (int i = 0; i < apps.Length; i++)
                {
                    var rectangle = (Rectangle)cell.Invoke(box, new object[] { i });
                    if (rectangle.Bottom < box.Height - 12) Check((int)hit.Invoke(box, new object[] { new Point(rectangle.X + 20, rectangle.Y + 20) }) == i, "section cell hit " + i);
                }
                using (var image = new Bitmap(box.Width, box.Height)) { box.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height)); image.Save(Path.Combine(root, "分类预览-20261014.png")); }
                var scroll = (SurfaceScrollBar)typeof(ClassificationBox).GetField("scroll", flags).GetValue(box);
                box.Height = 260; Check(scroll.Visible || scroll.Maximum > 0, "group scroll enabled");
                scroll.Value = scroll.Maximum;
                var last = (Rectangle)cell.Invoke(box, new object[] { apps.Length - 1 });
                Check(last.Bottom <= box.Height - 12, "last icon reachable by scrolling");
                box.Width = 280; Check(headers.Count == 4, "sections survive resize");
                box.IsSearchResults = true; box.SetItems(examples); Check(headers.Count == 0, "search flat grid preserved");
                Check(controller.TryDeleteCategory(Category.Entertainment), "populated entertainment category deleted");
                Check(values.Find(delegate(WorkspaceItem item) { return item.Identity == renamed; }).Category == Category.Chat, "other manual assignment preserved");
                controller.ResetClassification();
                Check(values.Find(delegate(WorkspaceItem item) { return item.Identity == renamed; }).Category == Category.Shortcuts, "deleted application category falls back to programs");
                Check(controller.TryDeleteCategory(Category.Shortcuts), "programs category can be deleted");
                Check(values.TrueForAll(delegate(WorkspaceItem item) { return item.Category == Category.Other; }), "deleted programs falls back to other");
                Check(controller.UpdateSnapshot(read, out skipped), "refresh after deletion");
                Check(!controller.Categories.Contains(Category.Entertainment) && !controller.Categories.Contains(Category.Shortcuts), "refresh does not recreate categories");
                using (var restarted = new WorkspaceController(font, delegate { }, Path.Combine(root, "controller")))
                {
                    Check(!restarted.Categories.Contains(Category.Entertainment) && !restarted.Categories.Contains(Category.Shortcuts), "restart preserves deleted categories");
                    Check(restarted.UpdateSnapshot(read, out skipped), "restarted snapshot accepted");
                    var restartedItems = (List<WorkspaceItem>)typeof(WorkspaceController).GetField("items", flags).GetValue(restarted);
                    Check(restartedItems.Count == 2 && restartedItems.TrueForAll(delegate(WorkspaceItem item) { return restarted.Categories.Contains(item.Category); }), "items remain visible after restart");
                }
                Check(controller.TryDeleteCategory(Category.Other), "other category can be deleted");
                foreach (Category category in new List<Category>(controller.Categories))
                    if (controller.Categories.Count > 1) Check(controller.TryDeleteCategory(category), "delete remaining category " + category);
                Check(!controller.TryDeleteCategory(controller.Categories[0]), "last category cannot be deleted");
                Check(controller.UpdateSnapshot(read, out skipped), "single category refresh");
                values = (List<WorkspaceItem>)typeof(WorkspaceController).GetField("items", flags).GetValue(controller);
                Check(values.Count == 2 && values.TrueForAll(delegate(WorkspaceItem item) { return controller.Categories.Contains(item.Category); }), "no icons lost with one remaining category");
            }
            string legacyDirectory = Path.Combine(root, "legacy-layout");
            var legacyStore = new LayoutStore(legacyDirectory);
            var legacy = new LayoutState();
            legacy.Groups.Add(new GroupState { Category = Category.Installers, Title = "安装包" });
            legacy.Groups.Add(new GroupState { Category = Category.Shortcuts, Title = "程序与快捷方式 · 待分类" });
            legacy.Groups.Add(new GroupState { Category = Category.Custom1, Title = "工作" });
            legacy.Assignments.Add(new Assignment { Identity = renamed, Category = Category.Installers });
            Check(legacyStore.Save(legacy), "legacy installer fixture persisted");
            string legacyFile = Path.Combine(legacyDirectory, "layout.json");
            string json = File.ReadAllText(legacyFile).Replace("\"DeletedCategories\":[],", "");
            File.WriteAllText(legacyFile, json);
            Check(legacyStore.Load().DeletedCategories.Count == 0, "old layout without deletion field supported");
            using (var font = new Font("Microsoft YaHei UI", 9F))
            using (var migrated = new WorkspaceController(font, delegate { }, legacyDirectory))
            {
                Check(!migrated.Categories.Contains(Category.Installers), "legacy installer group migrated away");
                Check(migrated.CategoryTitle(Category.Shortcuts) == "程序", "legacy default program name migrated");
                Check(migrated.CategoryTitle(Category.Custom1) == "工作", "legacy custom title retained");
                Check(migrated.UpdateSnapshot(new DesktopReadResult { Success = true, Icons = snapshot }, out skipped), "legacy snapshot accepted");
                var migratedItems = (List<WorkspaceItem>)typeof(WorkspaceController).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(migrated);
                Check(migratedItems.Find(delegate(WorkspaceItem item) { return item.Identity == renamed; }).Category == Category.Shortcuts, "legacy installer assignment moved to programs");
            }
            var invalid = new LayoutState(); invalid.DeletedCategories.Add(Category.Custom1);
            Check(!legacyStore.Save(invalid), "custom category deletion record rejected");
            invalid.DeletedCategories.Clear(); invalid.DeletedCategories.Add(Category.Chat); invalid.DeletedCategories.Add(Category.Chat);
            Check(!legacyStore.Save(invalid), "duplicate deleted category rejected");
            invalid.DeletedCategories.RemoveAt(1); invalid.Groups.Add(new GroupState { Category = Category.Chat });
            Check(!legacyStore.Save(invalid), "deleted category cannot remain a group");
            Console.WriteLine("TOTAL " + count); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
