// Integration checks use temporary notes, WebView profile and registry branch only.
using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
internal static class NotesWindowNativeChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    private static object Get(object target, string name)
    {
        var field = target.GetType().GetField(name, Flags);
        return field != null ? field.GetValue(target) : target.GetType().GetProperty(name, Flags).GetValue(target, null);
    }
    private static object Call(object target, string name, params object[] args)
    { return target.GetType().GetMethod(name, Flags).Invoke(target, args); }
    private static object Make(Type type, params object[] args)
    { return Activator.CreateInstance(type, Flags, null, args, null); }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS " + label); }
    private static void Until(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done()) { if (clock.ElapsedMilliseconds > 20000) throw new TimeoutException("Native acceptance timeout"); Application.DoEvents(); Thread.Sleep(10); }
    }
    private static string Script(Form form, string source)
    {
        var browser = Get(Get(form, "web"), "browser");
        var task = (Task<string>)Call(browser, "ExecuteScriptAsync", source);
        Until(() => task.IsCompleted); return task.GetAwaiter().GetResult();
    }
    // Exercise production window lifecycle and docking callbacks without touching installed app data.
    [STAThread] private static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "DesktopTidy-WindowChecks-" + Guid.NewGuid().ToString("N"));
        string key = @"Software\DesktopTidy-Tests\Window-" + Guid.NewGuid().ToString("N");
        const string id = "11111111111111111111111111111111";
        IDisposable owner = null, controller = null;
        Form window = null;
        Directory.CreateDirectory(folder);
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopTidy.exe"));
            // A transient reader must not make draft replacement lose the latest snapshot.
            string lockedFile = Path.Combine(folder, "replace-check.json"); File.WriteAllText(lockedFile, "before");
            var reader = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = Task.Run(() => { Thread.Sleep(80); reader.Dispose(); });
            assembly.GetType("DesktopTidy.Core.AtomicFile").GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { lockedFile, "after" });
            release.GetAwaiter().GetResult();
            Check(File.ReadAllText(lockedFile) == "after", "atomic replacement tolerates transient reader without deleting original");
            File.WriteAllText(Path.Combine(folder, "notes.json"), "[{\"Id\":\"" + id + "\",\"Title\":\"Acceptance\",\"Text\":\"original\",\"Updated\":\"v1\"}]");
            owner = (IDisposable)Make(assembly.GetType("DesktopTidy.Core.WorkspaceController"), SystemFonts.MessageBoxFont, new Action<string>(Console.WriteLine), folder);
            owner.GetType().GetProperty("Notes", Flags).SetValue(owner, Make(assembly.GetType("DesktopTidy.Core.NotesStore"), Path.Combine(folder, "notes.json")), null);
            owner.GetType().GetProperty("WebProfileDirectory", Flags).SetValue(owner, Path.Combine(folder, "WebView2"), null);
            Call(owner, "OpenNoteWindow", id);
            var windows = (IDictionary)Get(owner, "noteWindows"); window = (Form)windows[id];
            Until(() => (bool)Get(Get(window, "web"), "Ready"));
            Until(() => Script(window, "document.getElementById('note-editor').open") == "true");
            Check(window.FormBorderStyle == FormBorderStyle.Sizable && window.ShowInTaskbar, "native sizable taskbar window");
            Call(owner, "OpenNoteWindow", id); Check(windows.Count == 1, "same note reuses one native window");
            window.WindowState = FormWindowState.Maximized; Call(window, "KeepVisible");
            Check(window.WindowState == FormWindowState.Maximized, "monitor callback preserves maximized state");
            window.WindowState = FormWindowState.Minimized; Call(window, "KeepVisible");
            Check(window.WindowState == FormWindowState.Minimized, "monitor callback preserves minimized state");
            window.WindowState = FormWindowState.Normal;
            Call(window, "ToggleTopMost"); Check(window.TopMost, "native always-on-top enabled");
            Call(window, "ToggleTopMost"); Check(!window.TopMost, "native always-on-top disabled");
            Script(window, "document.getElementById('note-start-edit').click(); const input=document.getElementById('note-text');input.value='saved by native bridge';input.dispatchEvent(new Event('input',{bubbles:true}));input.focus();input.dispatchEvent(new KeyboardEvent('keydown',{key:'s',ctrlKey:true,bubbles:true,cancelable:true}))");
            Until(() => File.ReadAllText(Path.Combine(folder, "notes.json")).Contains("saved by native bridge"));
            Until(() => Script(window, "document.activeElement.id") == "\"note-text\"");
            Check(Script(window, "document.getElementById('note-editor').open") == "true", "real bridge Ctrl+S persists, stays open and restores focus");
            string draftFile = Path.Combine(folder, "drafts-note-" + id + ".json");
            Until(() => File.Exists(draftFile) && File.ReadAllText(draftFile).Contains("\"drafts\":\"{}\""));
            Check(Script(window, "!document.getElementById('note-draft-status').textContent.includes('失败')") == "true", "real bridge clears durable draft after successful save");
            window.Size = new Size(380, 340);
            Check(Script(window, "document.getElementById('note-save').getBoundingClientRect().bottom <= innerHeight") == "true", "native minimum size keeps Save reachable");
            window.Bounds = new Rectangle(140, 150, 600, 570); var savedBounds = window.Bounds;
            window.Close(); Until(() => window.IsDisposed);
            Check(windows.Count == 0, "clean close releases window instance");
            Call(owner, "OpenNoteWindow", id); window = (Form)windows[id];
            Until(() => (bool)Get(Get(window, "web"), "Ready"));
            Check(window.Bounds == savedBounds, "native reopen restores position and size");
            window.Close(); Until(() => window.IsDisposed);
            controller = (IDisposable)Make(assembly.GetType("DesktopTidy.UI.NotesEdgeController"), owner, new Action<string>(Console.WriteLine), key);
            Call(controller, "Tick", null, EventArgs.Empty);
            var handle = (Form)Get(controller, "dockHandle");
            Check(handle.Visible, "isolated collapsed dock handle visible");
            var work = Screen.PrimaryScreen.WorkingArea;
            foreach (string side in new[] { "Top", "Right", "Bottom", "Left" })
            {
                Point point = side == "Left" ? new Point(work.Left + 1, work.Top + work.Height / 3) : side == "Right" ? new Point(work.Right - 2, work.Top + work.Height / 3) : side == "Top" ? new Point(work.Left + work.Width / 3, work.Top + 1) : new Point(work.Left + work.Width / 3, work.Bottom - 2);
                var start = new Point(handle.Left + handle.Width / 2, handle.Top + handle.Height / 2);
                Call(handle, "StartDrag", start); Call(handle, "ContinueDrag", point, true);
                Check((bool)Get(handle, "Dragging") && handle.Location != start, "collapsed handle moves freely toward " + side);
                Call(handle, "ContinueDrag", point, false);
                Check((string)Get(owner, "EdgeNotesDockSide") == side && work.Contains(handle.Bounds), "collapsed handle docks to " + side);
                Check(Get(controller, "panel") == null && handle.Visible, "drop stays collapsed at " + side);
                using (var saved = Registry.CurrentUser.OpenSubKey(key)) Check(Convert.ToString(saved.GetValue("EdgeNotesDock")).StartsWith(side + "|"), "edge and offset persisted at " + side);
            }
            var before = handle.Bounds;
            Call(handle, "StartDrag", before.Location); Call(handle, "ContinueDrag", new Point(work.Left + work.Width / 2, work.Top + work.Height / 2), true);
            Call(handle, "FinishDrag", before.Location, true);
            Check(handle.Bounds == before && (string)Get(owner, "EdgeNotesDockSide") == "Left", "canceled drag restores original edge and position");
            Console.WriteLine(checks + " native acceptance checks passed; temporary data: " + folder);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (controller != null) controller.Dispose();
            if (window != null) window.Dispose();
            if (owner != null) owner.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(key, false);
            // Keep the isolated files for diagnosis; no recursive removal or real-data cleanup.
        }
    }
}
