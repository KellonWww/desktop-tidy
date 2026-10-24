// Temporary stores only. --desktop-startup also mounts its own test workspace HWND
// through the production desktop path; it never scans or modifies user icons.
using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class NotesEdgeNativeChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);

    private static object Get(object target, string name)
    {
        var field = target.GetType().GetField(name, Members);
        return field != null ? field.GetValue(target) : target.GetType().GetProperty(name, Members).GetValue(target, null);
    }
    private static void Set(object target, string name, object value)
    { target.GetType().GetProperty(name, Members).SetValue(target, value, null); }
    private static object Call(object target, string name, params object[] args)
    { return target.GetType().GetMethod(name, Members).Invoke(target, args); }
    private static object Make(Type type, params object[] args)
    { return Activator.CreateInstance(type, Members, null, args, null); }
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static void Until(Func<bool> condition, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > 20000) throw new TimeoutException(message);
            Pump(20);
        }
    }
    private static string Script(object panel, string source)
    {
        object core = Get(Get(Get(panel, "web"), "browser"), "CoreWebView2");
        var task = (Task<string>)Call(core, "ExecuteScriptAsync", source);
        Until(() => task.IsCompleted, "script timeout");
        return task.GetAwaiter().GetResult();
    }
    private static void InspectSurface(Form form, string stage)
    {
        Control web = (Control)Get(form, "web"), browser = (Control)Get(web, "browser");
        Console.WriteLine("SURFACE " + stage + " form=" + form.Visible + " web=" + web.Visible + " bounds=" + web.Bounds + " browser=" + browser.Visible + " handle=" + browser.IsHandleCreated + " bounds=" + browser.Bounds);
        Console.WriteLine("SURFACE native parent=" + GetParent(browser.Handle) + " expected=" + web.Handle + " native visible=" + IsWindowVisible(browser.Handle));
        Console.WriteLine("SURFACE top control=" + form.GetChildAtPoint(new Point(30, 30)).GetType().Name);
        Console.WriteLine("SURFACE DPI host=" + GetWindowDpiAwarenessContext(web.Handle) + " browser=" + GetWindowDpiAwarenessContext(browser.Handle));
        Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(web.Handle), GetWindowDpiAwarenessContext(browser.Handle)), "browser and drawer share DPI context: " + stage);
        Check(GetParent(browser.Handle) == web.Handle && IsWindowVisible(browser.Handle), "browser is visibly hosted: " + stage);
        form.Activate(); Pump(600);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents", "evidence", "便签功能");
        Directory.CreateDirectory(folder);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
            bitmap.Save(Path.Combine(folder, "native-surface-" + stage + ".png"));
        }
    }
    private static int Count(object store)
    { int count = 0; foreach (object item in (IEnumerable)Get(store, "Notes")) { if (item != null) count++; } return count; }
    private static void CheckDocking(Assembly assembly, object owner, Type edge)
    {
        string key = @"Software\DesktopTidy-Tests\Dock-" + Guid.NewGuid().ToString("N");
        object controller = null;
        try
        {
            controller = Make(assembly.GetType("DesktopTidy.UI.NotesEdgeController", true), owner, (Action<string>)delegate(string message) { Console.WriteLine("DOCK " + message); }, key);
            Call(controller, "ShowFromTray");
            Form drawer = (Form)Get(controller, "panel"); Until(() => (bool)Get(drawer, "Ready"), "dock drawer initialization timeout");
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            foreach (string side in new[] { "Left", "Top", "Right", "Bottom" })
            {
                Point point = side == "Left" ? new Point(work.Left + 1, work.Top + work.Height / 2) :
                    side == "Right" ? new Point(work.Right - 2, work.Top + work.Height / 2) :
                    side == "Top" ? new Point(work.Left + work.Width / 2, work.Top + 1) : new Point(work.Left + work.Width / 2, work.Bottom - 2);
                Point start = new Point(work.Left + work.Width / 2, work.Top + work.Height / 2);
                Call(drawer, "StartDrag", start); Call(drawer, "ContinueDrag", point, true, false);
                Check((bool)Get(drawer, "Dragging") && ((string)Get(owner, "EdgeNotesDockStatus")).Contains("松手吸附"), "native drag previews destination: " + side);
                Call(drawer, "ContinueDrag", point, false, false);
                Check(!(bool)Get(drawer, "Dragging") && (string)Get(owner, "EdgeNotesDockSide") == side, "native release docks to selected side: " + side);
                Until(() => !(bool)Get(drawer, "animating"), "dock animation completion timeout");
                Check(work.Contains(drawer.Bounds), "docked native drawer stays in work area: " + side);
                Until(() => Script(drawer, "document.getElementById('note-dock-location').textContent").Contains("已吸附"), "dock status publication timeout");
                using (var saved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key))
                    Check(Convert.ToString(saved.GetValue("EdgeNotesDock")).StartsWith(side + "|"), "native dock persisted: " + side);
                Call(drawer, "Collapse"); Until(() => !drawer.Visible, "dock collapse timeout");
                Call(controller, "Tick", null, EventArgs.Empty);
                Form handle = (Form)Get(controller, "dockHandle");
                Check(handle != null && handle.Visible && work.Contains(handle.Bounds), "collapsed native handle visible: " + side);
                Check(side == "Left" ? handle.Left == work.Left : side == "Right" ? handle.Right == work.Right : side == "Top" ? handle.Top == work.Top : handle.Bottom == work.Bottom,
                    "native landmark marks correct screen edge: " + side);
                ((Button)handle.Controls[0]).PerformClick();
                Check(drawer.Visible && !handle.Visible, "handle click reopens docked drawer: " + side);
            }
            ((IDisposable)controller).Dispose(); controller = null;
            controller = Make(assembly.GetType("DesktopTidy.UI.NotesEdgeController", true), owner, (Action<string>)delegate(string message) { Console.WriteLine("DOCK " + message); }, key);
            Check((string)Get(owner, "EdgeNotesDockSide") == "Bottom", "dock direction restored after controller restart");
            Call(controller, "ShowFromTray"); drawer = (Form)Get(controller, "panel");
            Until(() => (bool)Get(drawer, "Ready"), "restored dock drawer initialization timeout");
            Call(drawer, "StartDrag", new Point(work.Left + work.Width / 2, work.Top + work.Height / 2));
            Call(drawer, "ContinueDrag", new Point(work.Left, work.Top + 100), true, false);
            Call(drawer, "ContinueDrag", new Point(work.Left, work.Top + 100), false, true);
            Check((string)Get(owner, "EdgeNotesDockSide") == "Bottom" && !((string)Get(owner, "EdgeNotesDockStatus")).Contains("松手"), "Escape cancels drag without changing saved dock");
            Call(controller, "Pause"); Call(drawer, "Collapse"); Until(() => !drawer.Visible, "paused dock collapse timeout");
            Call(controller, "Tick", null, EventArgs.Empty);
            Form pausedHandle = (Form)Get(controller, "dockHandle");
            Check(pausedHandle.Visible, "paused hover retains visible dock landmark");
            ((Button)pausedHandle.Controls[0]).PerformClick(); Check(drawer.Visible, "paused dock still opens by click");
            Call(controller, "Toggle"); Until(() => !drawer.Visible, "disabled dock collapse timeout");
            Check(!pausedHandle.Visible, "disabling edge feature hides landmark");
            Call(controller, "ShowFromTray"); Check(drawer.Visible, "disabled docking preserves manual tray opening");
        }
        finally
        {
            if (controller != null) ((IDisposable)controller).Dispose();
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false);
        }
    }


    [STAThread] private static int Main(string[] args)
    {
        string folder = Path.Combine(Path.GetTempPath(), "DesktopTidy-EdgeChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        IDisposable owner = null;
        Form panel = null, second = null, main = null;
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopTidy.exe"));
            object store = Make(assembly.GetType("DesktopTidy.Core.NotesStore", true), Path.Combine(folder, "notes.json"));
            using (var font = new Font("Microsoft YaHei UI", 9F))
            {
                owner = (IDisposable)Make(assembly.GetType("DesktopTidy.Core.WorkspaceController", true), font, new Action<string>(Console.WriteLine), folder);
                Set(owner, "Notes", store);
                Set(owner, "WebProfileDirectory", Path.Combine(folder, "WebView2"));
                int groups = ((ICollection)Get(owner, "WebGroups")).Count;
                if (args.Length == 1 && args[0] == "--desktop-startup")
                {
                    main = (Form)Make(assembly.GetType("DesktopTidy.UI.WorkspaceForm", true), owner, font);
                    Check((bool)Call(main, "ShowDesktop"), "production desktop startup mounts only test HWND");
                    Until(() => (bool)Get(Get(main, "web"), "Ready"), "desktop main WebView timeout");
                    Console.WriteLine("DESKTOP main DPI=" + GetWindowDpiAwarenessContext(main.Handle));
                }
                Type edge = assembly.GetType("DesktopTidy.Core.NotesEdge", true);
                Type form = assembly.GetType("DesktopTidy.UI.NotesEdgeForm", true);
                IntPtr edgeCreationContext = SetThreadDpiAwarenessContext(new IntPtr(-2));
                try { panel = (Form)Make(form, owner); IntPtr systemDrawer = panel.Handle; }
                finally { SetThreadDpiAwarenessContext(edgeCreationContext); }
                Set(panel, "ReduceMotion", false);
                // Reproduce the real startup race: lazy WebView HWND creation can run
                // in the desktop workspace's per-monitor callback, not the drawer's system context.
                IntPtr drawerHandle = panel.Handle;
                Control earlyWeb = (Control)Get(panel, "web");
                Console.WriteLine("EARLY hostCreated=" + earlyWeb.IsHandleCreated + " browserCreated=" + ((Control)Get(earlyWeb, "browser")).IsHandleCreated);
                IntPtr originalDpi = SetThreadDpiAwarenessContext(new IntPtr(-3));
                try { IntPtr earlyBrowser = ((Control)Get(earlyWeb, "browser")).Handle; }
                finally { SetThreadDpiAwarenessContext(originalDpi); }
                Call(panel, "Reveal", new Rectangle(100, 100, 560, 700), Enum.Parse(edge, "Left"), false);
                Pump(50); Call(panel, "Collapse"); Until(() => !panel.Visible, "loading collapse timeout");
                Until(() => (bool)Get(panel, "Ready") || (bool)Get(panel, "Failed"), "WebView load timeout");
                Call(panel, "Reveal", new Rectangle(100, 100, 560, 700), Enum.Parse(edge, "Left"), false);
                if ((bool)Get(panel, "Failed")) Console.WriteLine("LOAD FAILURE " + ((Label)Get(panel, "notice")).Text);
                Check((bool)Get(panel, "Ready"), "native WebView2 ready");
                Check(Script(panel, "document.body.classList.contains('edge-notes') && !document.getElementById('notes-view').hidden") == "true", "native notes-only route");
                InspectSurface(panel, "initial");
                Script(panel, "document.querySelector('[data-note-filter=done]').click()");
                Call(panel, "Collapse"); Until(() => !panel.Visible, "initial collapse timeout");
                if (main == null)
                {
                    main = (Form)Make(assembly.GetType("DesktopTidy.UI.WorkspaceForm", true), owner, font);
                    main.Bounds = new Rectangle(600, 100, 800, 700);
                    object manualWeb = Make(assembly.GetType("DesktopTidy.UI.WebWorkspace", true), owner, main);
                    main.GetType().GetField("web", Members).SetValue(main, manualWeb);
                    main.Controls.Add((Control)manualWeb); main.Show(); Call(manualWeb, "Initialize");
                }
                object mainWeb = Get(main, "web");
                Until(() => (bool)Get(mainWeb, "Ready"), "main workspace WebView timeout");
                Script(main, "document.getElementById('notes-nav').click(); document.getElementById('new-note').click(); document.getElementById('note-title').value='main text'; document.getElementById('note-text').value='shared text'; document.getElementById('note-save').click()");
                Until(() => Count(store) == 1, "main text save timeout");
                Script(main, "document.getElementById('new-note').click(); document.getElementById('note-title').value='main TODO'; document.getElementById('note-todo').checked=true; document.getElementById('note-save').click()");
                Until(() => Count(store) == 2, "main TODO save timeout");
                Call(panel, "Reveal", new Rectangle(100, 100, 440, 700), Enum.Parse(edge, "Left"), false);
                Until(() => Script(panel, "document.querySelectorAll('.note-card').length") == "2", "main-to-edge sync timeout");
                InspectSurface(panel, "reopened");
                Call(Get(panel, "web"), "RecreateHandle"); Pump(300);
                InspectSurface(panel, "recreated");
                Call(panel, "RecreateHandle"); Pump(900);
                InspectSurface(panel, "form-recreated");
                Control hostedWeb = (Control)Get(panel, "web"), hostedBrowser = (Control)Get(hostedWeb, "browser");
                IntPtr parkingHandle;
                IntPtr parkingDpi = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(hostedBrowser.Handle));
                // A WinForms Panel itself inherits its thread's cached parking window
                // DPI. Use a native hidden host so the recovery fixture really matches.
                try { parkingHandle = CreateWindowEx(0, "STATIC", "Notes test parking", 0x80000000,
                    0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); }
                finally { SetThreadDpiAwarenessContext(parkingDpi); }
                try
                {
                    if (parkingHandle == IntPtr.Zero || !AreDpiAwarenessContextsEqual(
                        GetWindowDpiAwarenessContext(parkingHandle), GetWindowDpiAwarenessContext(hostedBrowser.Handle)))
                        throw new InvalidOperationException("Native parking fixture DPI mismatch");
                    SetParent(hostedBrowser.Handle, parkingHandle);
                    Check(hostedBrowser.Visible && GetParent(hostedBrowser.Handle) != hostedWeb.Handle && !IsWindowVisible(hostedBrowser.Handle), "reproduces hidden parked WebView despite logical visibility");
                    Pump(1000);
                    Check(GetParent(hostedBrowser.Handle) == hostedWeb.Handle && IsWindowVisible(hostedBrowser.Handle), "visible drawer self-recovers parked WebView");
                    Call(panel, "Collapse"); Until(() => !panel.Visible, "parking collapse timeout");
                    SetParent(hostedBrowser.Handle, parkingHandle);
                    Call(panel, "Reveal", new Rectangle(100, 100, 440, 700), Enum.Parse(edge, "Left"), false);
                    Check(GetParent(hostedBrowser.Handle) == hostedWeb.Handle && IsWindowVisible(hostedBrowser.Handle), "reopening restores parked browser before showing content");
                    InspectSurface(panel, "recovered");
                }
                finally { DestroyWindow(parkingHandle); }
                Check(GetParent(hostedBrowser.Handle) == hostedWeb.Handle && IsWindowVisible(hostedBrowser.Handle), "browser HWND remains attached and visible after host handle recreation");
                Check(Script(panel, "[...document.querySelectorAll('.note-card')].every(c => c.getBoundingClientRect().height > 0 && c.getBoundingClientRect().width > 0 && c.getBoundingClientRect().top < innerHeight && c.getBoundingClientRect().bottom > 0)") == "true", "main text and TODO actually visible in left drawer");
                Check(Script(panel, "document.querySelector('[data-note-filter=all]').getAttribute('aria-pressed')") == "\"true\"", "reopen clears stale completed-only filter");
                panel.Dispose(); panel = (Form)Make(form, owner); Set(panel, "ReduceMotion", false);
                Call(panel, "Reveal", new Rectangle(100, 100, 440, 700), Enum.Parse(edge, "Left"), false);
                Until(() => (bool)Get(panel, "Ready"), "new drawer WebView timeout");
                Until(() => Script(panel, "document.querySelectorAll('.note-card').length") == "2", "new drawer initial snapshot timeout");
                Check(true, "fresh drawer reads main text and TODO on first open");
                Script(main, "document.querySelector('[data-note-action=delete]').click(); document.getElementById('note-delete-confirm').click()");
                Until(() => Count(store) == 1, "main delete timeout");
                Script(main, "document.querySelector('[data-note-action=delete]').click(); document.getElementById('note-delete-confirm').click()");
                Until(() => Count(store) == 0, "main second delete timeout");
                Until(() => Script(panel, "document.querySelectorAll('.note-card').length") == "0", "main delete sync timeout");
                Script(panel, "document.getElementById('new-note').click()");
                Until(() => (bool)Get(panel, "InteractionActive"), "editor guard not set");
                Check(true, "native editor prevents auto-collapse");
                Script(panel, "document.getElementById('note-title').value='native TODO'; document.getElementById('note-text').value='temporary test'; document.getElementById('note-todo').checked=true; document.getElementById('note-save').click()");
                Until(() => Count(store) == 1 && !(bool)Get(panel, "InteractionActive"), "native save timeout");
                Check(File.Exists(Path.Combine(folder, "notes.json")), "native bridge saves to injected temporary store");
                Script(panel, "document.querySelector('[data-note-action=pin]').click()");
                Until(() => Script(panel, "Boolean(document.querySelector('.note-card.pinned'))") == "true", "native pin timeout");
                Check(true, "native pin saves through the bridge");
                Script(panel, "document.querySelector('.note-check input').click()");
                Until(() => Script(panel, "Boolean(document.querySelector('.note-card.completed'))") == "true", "native toggle timeout");
                Check(true, "native TODO completion");
                second = main;
                Until(() => Script(second, "document.querySelectorAll('.note-card.completed').length") == "1", "edge-to-main completion timeout");
                Check(true, "main workspace receives drawer completion immediately");
                Script(panel, "document.querySelector('[data-note-action=edit]').click(); document.getElementById('note-text').value='my draft'");
                Script(second, "document.querySelector('[data-note-action=edit]').click(); document.getElementById('note-text').value='new from second surface'; document.getElementById('note-save').click()");
                Until(() => Script(second, "document.getElementById('note-editor').open") == "false", "main surface save timeout");
                Pump(500);
                Script(panel, "document.getElementById('note-save').click()");
                Until(() => Script(panel, "document.getElementById('note-error').textContent.includes('另一处更新')") == "true", "revision guard timeout");
                Check(Script(panel, "document.getElementById('note-text').value") == "\"my draft\"", "native stale edit rejected while retaining draft");
                Call(panel, "Collapse"); Until(() => !panel.Visible, "collapse animation timeout"); Check(!panel.Visible && second.Visible, "native collapse hides only edge window");
                foreach (string side in new[] { "Left", "Top", "Right", "Bottom" })
                {
                    Call(panel, "Reveal", new Rectangle(100, 100, 560, 700), Enum.Parse(edge, side), false);
                    Check(panel.Visible && Script(panel, "document.getElementById('note-text').value") == "\"my draft\"", "native reveal preserves draft: " + side);
                    Call(panel, "Collapse"); Until(() => !panel.Visible, "collapse animation timeout");
                }
                Check(((ICollection)Get(owner, "WebGroups")).Count == groups && !(bool)Get(owner, "Visible"), "classification groups and visibility untouched");
                object fresh = Make(assembly.GetType("DesktopTidy.Core.NotesStore", true), Path.Combine(folder, "notes.json"));
                Check(Count(fresh) == 1, "native saved notes survive store reload");
                foreach (object savedNote in (IEnumerable)Get(fresh, "Notes"))
                    Check((bool)Get(savedNote, "IsPinned"), "pin survives editing, completion and store reload");
                second.Dispose(); second = null; main = null; panel.Dispose(); panel = null;
                CheckDocking(assembly, owner, edge);
                owner.Dispose(); owner = null;
            }
            Console.WriteLine(checks + " native edge checks passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (main != null) main.Dispose(); if (second != null) second.Dispose(); if (panel != null) panel.Dispose(); if (owner != null) owner.Dispose();
            // Chromium may still be releasing profile files after control disposal.
            for (int attempt = 0; attempt < 20 && Directory.Exists(folder); attempt++)
            {
                try { Directory.Delete(folder, true); }
                catch (IOException) { Thread.Sleep(200); }
                catch (UnauthorizedAccessException) { Thread.Sleep(200); }
            }
        }
    }
}