using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;
using DesktopTidy.Core;
using DesktopTidy.UI;
using DesktopTidy.Windows;

internal static class WorkspaceTests
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static StreamWriter log;
    private static void Check(bool value, string name)
    {
        log.WriteLine((value ? "PASS " : "FAIL ") + name); log.Flush();
        if (!value) throw new InvalidOperationException(name); checks++;
    }
    private static List<ClassificationBox> Boxes(WorkspaceController controller) { return (List<ClassificationBox>)typeof(WorkspaceController).GetField("boxes", Private).GetValue(controller); }
    private static List<WorkspaceItem> Items(WorkspaceController controller) { return (List<WorkspaceItem>)typeof(WorkspaceController).GetField("items", Private).GetValue(controller); }
    private static void Event(ClassificationBox box, string name, object args) { typeof(ClassificationBox).GetMethod(name, Private).Invoke(box, new object[] { args }); }
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static void Screenshot(Form form, string path) { using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height)); image.Save(path); } }
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        string directory = Path.GetFullPath(args[0]);
        if (Directory.Exists(directory) || File.Exists(directory)) { Console.Error.WriteLine("测试输出目录必须不存在，避免覆盖用户文件。"); return 2; }
        Directory.CreateDirectory(directory);
        using (log = new StreamWriter(Path.Combine(directory, "results.txt")))
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Check(Classification.Classify("Report.PDF", false) == Category.Documents, "case-insensitive document");
                Check(Classification.Classify("photo.webp", false) == Category.Images, "image");
                Check(Classification.Classify("movie.mkv", false) == Category.Videos, "video");
                Check(Classification.Classify("app.msix", false) == Category.Shortcuts, "installer goes to programs");
                Check(Classification.Classify("backup.7z", false) == Category.Archives, "archive");
                Check(Classification.Classify("setup.exe", false) == Category.Shortcuts, "exe not guessed installer");
                Check(Classification.Classify("folder.pdf", true) == Category.Folders, "folder precedence");
                Check(Classification.Classify("README", false) == Category.Other, "extensionless fallback");
                var readerBefore = DesktopReader.Read();
                Check(readerBefore.Success, "read-only real desktop scan before");
                using (var stream = File.Create(Path.Combine(directory, "desktop-before.json"))) new DataContractJsonSerializer(typeof(DesktopReadResult)).WriteObject(stream, readerBefore);
                string fixtures = Path.Combine(directory, "fixtures"); Directory.CreateDirectory(fixtures);
                string[] names = { "项目说明.txt", "会议记录.pdf", "假期照片.jpg", "演示视频.mp4", "工具安装.msi", "资料备份.zip", "快速入口.url", "临时记录.data" };
                var result = new DesktopReadResult { Success = true, Icons = new List<DesktopIcon>() };
                foreach (string name in names) { string path = Path.Combine(fixtures, name); File.WriteAllText(path, "fixture only"); result.Icons.Add(new DesktopIcon { Name = name, ParsingName = path }); }
                string folder = Path.Combine(fixtures, "工作文件夹"); Directory.CreateDirectory(folder); result.Icons.Add(new DesktopIcon { Name = "工作文件夹", ParsingName = folder });
                for (int i = 0; i < 25; i++) { string path = Path.Combine(fixtures, "示例文档-" + i.ToString("00") + ".txt"); File.WriteAllText(path, "fixture only"); result.Icons.Add(new DesktopIcon { Name = Path.GetFileName(path), ParsingName = path }); }
                string hidden = Path.Combine(fixtures, "隐藏文件.txt"); File.WriteAllText(hidden, "fixture only"); File.SetAttributes(hidden, FileAttributes.Hidden);
                string readOnly = Path.Combine(fixtures, "只读文件.txt"); File.WriteAllText(readOnly, "fixture only"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
                result.Icons.Add(new DesktopIcon { Name = "hidden", ParsingName = hidden }); result.Icons.Add(new DesktopIcon { Name = "readonly", ParsingName = readOnly });
                result.Icons.Add(new DesktopIcon { Name = "virtual", ParsingName = "::{00000000-0000-0000-0000-000000000000}" });
                result.Icons.Add(new DesktopIcon { Name = "network", ParsingName = @"\\server\share\file.txt" });
                int skipped; var built = Classification.Build(result.Icons, new LayoutState(), out skipped);
                Check(built.Count == 34 && skipped == 4, "safe filtering: hidden/readonly/virtual/network");
                string config = Path.Combine(directory, "config"); var store = new LayoutStore(config);
                Check(store.Load().Schema == 2, "missing config fallback");
                Check(store.Save(new LayoutState()), "atomic initial save");
                var legacy = new LayoutState { Schema = 1, Groups = new List<GroupState> { new GroupState { Category = Category.Images, X = -1920, Y = 100, Width = 280, Height = 260 } }, Assignments = new List<Assignment> { new Assignment { Identity = "legacy.txt", Category = Category.Images } } };
                using (var stream = File.Create(Path.Combine(config, "layout.json"))) new DataContractJsonSerializer(typeof(LayoutState)).WriteObject(stream, legacy);
                var migrated = store.Load();
                Check(migrated.Schema == 2 && migrated.OpacityPercent == 100 && migrated.Groups[0].X == 304 && migrated.Assignments[0].Category == Category.Images, "schema1 migration preserves assignment and resets local coordinates");
                Check(store.Save(migrated), "migrated layout can be persisted");
                legacy.Groups = null;
                using (var stream = File.Create(Path.Combine(config, "layout.json"))) new DataContractJsonSerializer(typeof(LayoutState)).WriteObject(stream, legacy);
                Check(store.Load().Groups.Count == 0 && store.Error != null, "malformed legacy groups rejected safely");
                Check(!store.Save(new LayoutState { OpacityPercent = 24 }), "invalid opacity rejected");
                Check(!store.Save(new LayoutState { Groups = new List<GroupState> { new GroupState { X = int.MaxValue } } }), "invalid local coordinates rejected");

                File.WriteAllText(Path.Combine(config, "layout.json"), "corrupt"); Check(store.Load().Groups.Count == 0 && store.Error != null, "corrupt config fallback");
                Check(store.Save(new LayoutState()), "recover corrupt config");
                Check(!store.Save(new LayoutState { Schema = 99 }), "reject unknown config schema");
                string lockedPath = Path.Combine(directory, "not-directory"); File.WriteAllText(lockedPath, "fixture only");
                Check(!new LayoutStore(lockedPath).Checkpoint(new LayoutState()), "failed backup refuses write");
                var messages = new List<string>();
                using (var font = new Font("Microsoft YaHei UI", 11F))
                using (var heading = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold))
                using (var controller = new WorkspaceController(font, delegate(string text) { messages.Add(text); }, config))
                {
                    Check(controller.CompleteFirstRunChoice(false), "first-run fixture choice persisted");
                    controller.Show(result); Pump(1200);
                    Check(controller.Visible && Boxes(controller).Count == 16, "sixteen lightweight category boxes");
                    Check(Items(controller).Count == 34, "all eligible items exactly once");
                    int attached = Boxes(controller).FindAll(delegate(ClassificationBox box) { return box.Attached; }).Count;
                    log.WriteLine("Attached " + attached + "/16; private MiB " + NativeMethods.PrivateMiB().ToString("F2"));
                    foreach (var box in Boxes(controller)) Screenshot(box, Path.Combine(directory, "box-" + box.State.Category + ".png"));
                    Check(File.Exists(Path.Combine(config, "desktop-snapshot.json")), "native coordinate snapshot before show");
                    WorkspaceItem item = Items(controller).Find(delegate(WorkspaceItem value) { return value.Name == "项目说明.txt"; });
                    var target = Boxes(controller).Find(delegate(ClassificationBox box) { return box.State.Category == Category.Images; });
                    var transfer = new DataObject(); transfer.SetData("DesktopTidy.Item", false, new ItemTransfer { Owner = controller, Item = item });
                    var drag = new DragEventArgs(transfer, 1, 0, 0, DragDropEffects.Link, DragDropEffects.None);
                    Event(target, "OnDragEnter", drag); Check(drag.Effect == DragDropEffects.Link, "accept private same-workspace drag");
                    Event(target, "OnDragDrop", drag); Check(item.Category == Category.Images, "cross-box reclassification");
                    Check(File.ReadAllText(item.Path) == "fixture only", "reclassification does not touch original file");
                    var external = new DataObject(DataFormats.FileDrop, new string[] { item.Path });
                    var reject = new DragEventArgs(external, 1, 0, 0, DragDropEffects.Move, DragDropEffects.Move);
                    Event(target, "OnDragEnter", reject); Check(reject.Effect == DragDropEffects.None, "reject external file move");
                    controller.Undo(); Check(item.Category == Category.Documents, "undo manual classification");
                    var boxToMove = Boxes(controller)[0]; Category firstCategory = boxToMove.State.Category;
                    var reorderTarget = Boxes(controller)[1].LayoutBounds;
                    controller.PreviewGroupMove(boxToMove, new Point(reorderTarget.Left + reorderTarget.Width / 2, reorderTarget.Top + reorderTarget.Height / 2));
                    controller.CompleteGroupMove();
                    Check(Boxes(controller)[1].State.Category == firstCategory, "title drag persists custom order");
                    controller.Undo(); Check(Boxes(controller)[0].State.Category == firstCategory, "undo group reorder");
                    var boxToResize = Boxes(controller)[0]; Category resizedCategory = boxToResize.State.Category; int oldWidth = boxToResize.Width;
                    Event(boxToResize, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, boxToResize.Width - 9, boxToResize.Height - 9, 0));
                    Event(boxToResize, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, boxToResize.Width + 17, boxToResize.Height + 11, 0));
                    Event(boxToResize, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    Check(boxToResize.State.Width == oldWidth + 26, "resize persists dimensions");
                    var webSized = Boxes(controller)[1]; Category webSizedCategory = webSized.State.Category;
                    int webWidth = webSized.State.Width, webHeight = webSized.State.Height;
                    controller.ResizeWebCategory(webSizedCategory, 347, 381);
                    Check(webSized.State.Width == 347 && webSized.State.Height == 381, "web resize persists snapped category dimensions");
                    controller.Undo(); webSized = Boxes(controller).Find(delegate(ClassificationBox box) { return box.State.Category == webSizedCategory; });
                    boxToResize = Boxes(controller).Find(delegate(ClassificationBox box) { return box.State.Category == resizedCategory; });
                    Check(webSized.State.Width == webWidth && webSized.State.Height == webHeight, "undo web category resize");
                    controller.Toggle(boxToResize); Check(boxToResize.State.Collapsed && boxToResize.Height < 100, "collapse");
                    controller.Toggle(boxToResize); Check(!boxToResize.State.Collapsed && boxToResize.Width == oldWidth + 26, "expand preserves geometry");
                    controller.ArrangeGroups();
                    var workspace = Boxes(controller)[0].TopLevelControl as WorkspaceForm;
                    Check(workspace != null && Boxes(controller).TrueForAll(delegate(ClassificationBox b) { return !b.TopLevel && ReferenceEquals(b.TopLevelControl, workspace); }), "all categories belong to one workspace");
                    Check(Boxes(controller).TrueForAll(delegate(ClassificationBox b) { return !b.State.Collapsed && b.State.X >= 0 && b.State.Y >= 0; }), "grid arrangement within local canvas");
                    controller.ArrangeItems(Boxes(controller)[0]); Check(Boxes(controller)[0].State.SortByName, "per-category arrange persists sorting");
                    controller.Undo(); Check(!Boxes(controller)[0].State.SortByName, "undo per-category arrange");
                    controller.ArrangeItems(null); Check(Boxes(controller).TrueForAll(delegate(ClassificationBox b) { return b.State.SortByName; }), "global arrange items");
                    controller.SetStartupHiding(true); Check(controller.HideIconsOnStartup && store.Load().HideIconsOnStartup, "startup option persisted without hiding during setting");
                    controller.SetStartupHiding(false); Check(!controller.HideIconsOnStartup, "startup hiding can be disabled");
                    controller.CommitOpacity(73); Check(controller.OpacityPercent == 73, "opacity saved at one-percent precision");
                    controller.Undo(); Check(controller.OpacityPercent == 100, "opacity undo");
                    controller.CommitOpacity(999); Check(controller.OpacityPercent == 100, "opacity upper clamp");
                    controller.CommitOpacity(-1); Check(controller.OpacityPercent == 25, "opacity lower clamp");
                    controller.CommitOpacity(100);
                    controller.Undo(); controller.CommitOpacity(100);
                    workspace = Boxes(controller)[0].TopLevelControl as WorkspaceForm;
                    Screenshot(workspace, Path.Combine(directory, "workspace-container.png"));
                    int retainedCardWidth = Boxes(controller)[0].Width;
                    workspace.Size = workspace.MinimumSize; controller.ArrangeGroups();
                    Check(Boxes(controller)[0].Width == retainedCardWidth && retainedCardWidth >= 220, "minimum workspace preserves usable card width with scroll");
                    Screenshot(workspace, Path.Combine(directory, "workspace-minimum.png"));
                    Check(DesktopIconVisibility.RunGuardian(new string[] { "--restore-desktop-icons" }) == 2, "guardian rejects invalid arguments without desktop changes");
                    controller.ResetLayout();
                    // Preserve the resize fixture expected by the existing restart regression.
                    Boxes(controller)[0].State.Width = oldWidth + 26; Boxes(controller)[0].ApplyState();
                    controller.Assign(item, Category.Images); controller.Hide();
                    using (var restored = new WorkspaceController(font, delegate(string text) { }, config))
                    {
                        restored.Show(result); Pump(100); Check(Items(restored).Find(delegate(WorkspaceItem value) { return value.Identity == item.Identity; }).Category == Category.Images, "restart restores manual assignment");
                        Check(Boxes(restored)[0].State.Width == oldWidth + 26, "restart restores width"); restored.ResetLayout();
                        Check(Boxes(restored)[0].State.Width == oldWidth, "restore default box layout"); restored.Undo(); Check(Boxes(restored)[0].State.Width == oldWidth + 26, "undo reset layout"); restored.Hide();
                    }
                    using (var form = new MainForm(false, delegate { }, font, heading, true))
                    {
                        form.Show(); form.SetTheme(Theme.Palette(false)); Screenshot(form, Path.Combine(directory, "manager-light.png"));
                        form.SetResult(result); form.SetTheme(Theme.Palette(true)); Screenshot(form, Path.Combine(directory, "manager-dark.png"));
                        form.Size = form.MinimumSize; Screenshot(form, Path.Combine(directory, "manager-minimum.png"));
                        var largeResult = new DesktopReadResult { Success = true, Icons = new List<DesktopIcon>() };
                        for (int i = 0; i < 500; i++) largeResult.Icons.Add(new DesktopIcon { Name = "模拟项目-" + i.ToString("000"), ParsingName = "synthetic-" + i });
                        form.SetResult(largeResult); Pump(50);
                        var accessibility = form.AccessibilityObject; bool headingAccessible = false, countAccessible = false;
                        for (int i = 0; i < accessibility.GetChildCount(); i++)
                        {
                            var child = accessibility.GetChild(i);
                            if (child == null || child.Role != AccessibleRole.StaticText) continue;
                            if (child.Name == "桌面概览") headingAccessible = child.Bounds.Width > 0 && ReferenceEquals(child.Parent, accessibility);
                            if (child.Name == "桌面文件  /  500 项") countAccessible = true;
                        }
                        Check(headingAccessible && countAccessible, "shared painted text exposes accessible heading and current count");
                        Check(accessibility.GetChild(-1) == null && accessibility.GetChild(accessibility.GetChildCount()) == null, "accessible child bounds validated");
                        var desktopList = (DesktopListControl)typeof(MainForm).GetField("list", Private).GetValue(form);
                        var retained = (List<DesktopIcon>)typeof(DesktopListControl).GetField("items", Private).GetValue(desktopList);
                        var scroll = (SurfaceScrollBar)typeof(DesktopListControl).GetField("scroll", Private).GetValue(desktopList);
                        Check(retained.Count == 500, "manager retains all 500 items, not first 100");
                        Check(scroll.Maximum == 499 && scroll.Visible, "500 item scroll range available");
                        scroll.Value = int.MaxValue;
                        Check(scroll.Value + scroll.LargeChange == 500 && retained[499].Name == "模拟项目-499", "scroll reaches last item after 100");
                        Screenshot(form, Path.Combine(directory, "manager-500-last-page.png"));
                        form.SetResult(result); Check(retained.Count == result.Icons.Count && scroll.Value == 0, "rescan replaces data and resets scroll");
                        var limitResult = new DesktopReadResult { Success = true, Icons = new List<DesktopIcon>() };
                        for (int i = 0; i < 3001; i++) limitResult.Icons.Add(new DesktopIcon { Name = "limit-" + i });
                        form.SetResult(limitResult); Check(retained.Count == 3000, "manager list retains bounded 3000 item maximum");
                        form.SetResult(new DesktopReadResult { Success = true, Icons = new List<DesktopIcon>() });
                        Check(retained.Count == 0 && scroll.Value == 0 && !scroll.Visible, "empty rescan clears list and scrollbar");
                        form.Close(); Check(form.IsDisposed && desktopList.IsDisposed, "manager close disposes form and list");
                        Check(font.Height > 0 && heading.Height > 0, "manager disposal preserves shared font ownership");
                    }
                    controller.Show(result); Pump(300); controller.Hide(); Pump(300);
                    uint gdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0), user = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                    double memory = NativeMethods.PrivateMiB();
                    for (int i = 0; i < 50; i++) { controller.Show(result); Pump(15); controller.Hide(); Pump(15); }
                    Pump(300); log.WriteLine(string.Format("50 box cycles: private {0:F2}->{1:F2} MiB; GDI {2}->{3}; USER {4}->{5}", memory, NativeMethods.PrivateMiB(), gdi, GetGuiResources(Process.GetCurrentProcess().Handle, 0), user, GetGuiResources(Process.GetCurrentProcess().Handle, 1)));
                    Check(GetGuiResources(Process.GetCurrentProcess().Handle, 0) <= gdi + 1, "50 show/hide cycles GDI stable");
                    Check(GetGuiResources(Process.GetCurrentProcess().Handle, 1) <= user + 1, "50 show/hide cycles USER stable");
                    var cache = (ShellIconCache)typeof(WorkspaceController).GetField("icons", Private).GetValue(controller); Check(cache.Count <= 64, "bounded icon cache");
                    using (var iconReady = new ManualResetEvent(false))
                    {
                        cache.Refresh(Items(controller)[0], delegate { iconReady.Set(); });
                        Check(iconReady.WaitOne(3000), "changed desktop icon cache refresh completes");
                    }
                    var revisions = (Dictionary<string, int>)typeof(ShellIconCache).GetField("revisions", Private).GetValue(cache);
                    Check(revisions.Count == 1, "changed desktop icon invalidates its cached shell image");
                    var varied = new List<WorkspaceItem>();
                    for (int i = 0; i < 100; i++) varied.Add(new WorkspaceItem { Path = Path.Combine(fixtures, "item.ext" + i), Identity = i.ToString(), Name = "synthetic" });
                    cache.Load(varied, delegate { }); Pump(800);
                    var requested = (HashSet<string>)typeof(ShellIconCache).GetField("requested", Private).GetValue(cache);
                    Check(requested.Count == 64 && cache.Count <= 64, "100 additional extensions capped at 64 cache keys");
                    log.WriteLine("Last status: " + messages[messages.Count - 1]);
                }
                using (var emptyFont = new Font("Microsoft YaHei UI", 11F))
                using (var empty = new WorkspaceController(emptyFont, delegate(string text) { }, Path.Combine(directory, "empty-config")))
                {
                    Check(empty.CompleteFirstRunChoice(false), "empty first-run fixture choice persisted");
                    empty.Show(new DesktopReadResult { Success = true, Icons = new List<DesktopIcon>() });
                    Check(empty.Visible && Boxes(empty).Count == 16 && Items(empty).Count == 0, "empty desktop retains workspace and sidebar");
                    empty.Hide(); Check(!empty.Visible, "empty workspace can be hidden");
                }
                var readerAfter = DesktopReader.Read(); Check(readerAfter.Success, "read-only real desktop scan after");
                using (var stream = File.Create(Path.Combine(directory, "desktop-after.json"))) new DataContractJsonSerializer(typeof(DesktopReadResult)).WriteObject(stream, readerAfter);
                var before = new Dictionary<string, DesktopIcon>(); foreach (var icon in readerBefore.Icons) before[icon.ParsingName] = icon;
                int changed = 0; foreach (var icon in readerAfter.Icons) { DesktopIcon old; if (before.TryGetValue(icon.ParsingName, out old) && (old.X != icon.X || old.Y != icon.Y)) changed++; }
                Check(changed == 0 && readerBefore.Icons.Count == readerAfter.Icons.Count, "real desktop items and coordinates unchanged");
                foreach (string name in names) Check(File.ReadAllText(Path.Combine(fixtures, name)) == "fixture only", "original content intact: " + name);
                log.WriteLine("PASS total=" + checks); return 0;
            }
            catch (Exception ex) { log.WriteLine(ex); log.Flush(); return 1; }
        }
    }
}
