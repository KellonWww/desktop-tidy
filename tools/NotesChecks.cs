using System;
using System.IO;
using System.Drawing;
using System.Linq;
using DesktopTidy.Core;

internal static class NotesChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    private static void CheckEdges()
    {
        var screen = new Rectangle(-1920, -200, 1920, 1080);
        Check(NotesEdgeGeometry.Detect(screen, new Point(-1920, 300)) == NotesEdge.Left, "negative monitor left edge");
        Check(NotesEdgeGeometry.Detect(screen, new Point(-1, 300)) == NotesEdge.Right, "right edge detection");
        Check(NotesEdgeGeometry.Detect(screen, new Point(-900, -200)) == NotesEdge.Top, "top edge detection");
        Check(NotesEdgeGeometry.Detect(screen, new Point(-900, 879)) == NotesEdge.Bottom, "bottom edge detection");
        Check(NotesEdgeGeometry.Detect(screen, new Point(-1920, -200)) == NotesEdge.None, "corner guard protects system gestures");
        Check(NotesEdgeGeometry.Detect(screen, new Point(0, 300)) == NotesEdge.None, "outside monitor ignored");
        Check(NotesEdgeGeometry.Detect(screen, new Point(-1917, 300)) == NotesEdge.None, "only three pixel trigger band");
        foreach (NotesEdge side in new[] { NotesEdge.Left, NotesEdge.Top, NotesEdge.Right, NotesEdge.Bottom })
        {
            var work = new Rectangle(-1920, -200, 1920, 1032);
            Check(work.Contains(NotesEdgeGeometry.PanelBounds(work, side, 1.5f)), "panel respects working area: " + side);
            Rectangle panel = NotesEdgeGeometry.PanelBounds(work, side, 1f);
            Rectangle handle = NotesEdgeGeometry.HandleBounds(work, side, 1.5f);
            Check(work.Contains(handle), "dock handle remains in work area: " + side);
            Point edgePoint = side == NotesEdge.Left ? new Point(work.Left, work.Top + work.Height / 2) :
                side == NotesEdge.Right ? new Point(work.Right - 1, work.Top + work.Height / 2) :
                side == NotesEdge.Top ? new Point(work.Left + work.Width / 2, work.Top) : new Point(work.Left + work.Width / 2, work.Bottom - 1);
            Check(NotesEdgeGeometry.Nearest(work, edgePoint) == side, "drag chooses nearest edge: " + side);
            Check(side == NotesEdge.Left ? panel.Left == work.Left + 12 : side == NotesEdge.Right ? panel.Right == work.Right - 12 :
                side == NotesEdge.Top ? panel.Top == work.Top + 12 : panel.Bottom == work.Bottom - 12, "panel actually anchored to chosen side: " + side);
            Check(side == NotesEdge.Left ? handle.Left == work.Left : side == NotesEdge.Right ? handle.Right == work.Right :
                side == NotesEdge.Top ? handle.Top == work.Top : handle.Bottom == work.Bottom, "visible handle marks chosen side: " + side);
            var small = new Rectangle(0, 0, 320, 240);
            Check(small.Contains(NotesEdgeGeometry.HandleBounds(small, side, 2f)), "handle fits small high-DPI screen: " + side);
            Check(small.Contains(NotesEdgeGeometry.PanelBounds(small, side, 2f)), "panel fits small high-DPI area: " + side);
        }
        var timing = new NotesEdgeTrigger();
        Check(!timing.ShouldReveal("left", 0) && !timing.ShouldReveal("left", 299) && timing.ShouldReveal("left", 300), "stable hover delay");
        timing.Suppress();
        Check(!timing.ShouldReveal("left", 1000) && !timing.ShouldReveal("right", 2000), "collapse suppresses repeat reveals");
        timing.ShouldReveal(null, 2100);
        Check(!timing.ShouldReveal("right", 2200) && timing.ShouldReveal("right", 2500), "leaving edge rearms reveal");
        timing.ShouldReveal("left", 3000); timing.ShouldReveal("right", 3100);
        Check(!timing.ShouldReveal("right", 3399) && timing.ShouldReveal("right", 3400), "changing edge restarts hover");
        Check(!timing.ShouldHide(false, false, 4000) && !timing.ShouldHide(false, false, 4599) && timing.ShouldHide(false, false, 4600), "leave delay before collapse");
        Check(!timing.ShouldHide(false, true, 5000) && !timing.ShouldHide(false, false, 7000), "editing protects panel and resets leave timer");
        Check(!timing.ShouldHide(true, false, 8000) && !timing.ShouldHide(false, false, 9000), "pointer reentry resets leave timer");
    }
    private static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "DesktopTidy-NotesChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "notes.json"), error;
            var store = new NotesStore(path);
            Check(!store.Notes.Any() && store.LoadError == null, "empty store");
            Check(store.Change("saveNote", "", " 常用文本 ", "第一行\n第二行 <script> & 😀", false, out error), "create Unicode multiline text");
            var note = store.Notes.Single(); string id = note.Id;
            Check(note.Title == "常用文本" && !note.IsTodo && !note.Completed, "trim title and plain-text defaults");
            Check(!store.Change("toggleTodo", id, "", "", false, out error), "text cannot be completed");
            Check(store.Change("saveNote", id, "待办", "写完这个功能", true, out error), "convert text to todo");
            Check(store.Change("toggleTodo", id, "", "", false, out error) && store.Find(id).Completed, "complete todo");
            Check(new NotesStore(path).Find(id).Completed, "completion survives restart");
            Check(store.Change("saveNote", id, "更新标题", "更新内容", true, out error) && store.Find(id).Completed, "edit completed todo preserves status");
            Check(store.Change("toggleTodo", id, "", "", false, out error) && !store.Find(id).Completed, "reopen todo");
            Check(store.Change("toggleTodo", id, "", "", false, out error) && store.Change("saveNote", id, "文本", "内容", false, out error) && !store.Find(id).Completed, "conversion back to text clears completion");
            string revision = store.Find(id).Updated;
            Check(store.Change("saveNote", id, "文本", "内容", false, revision, out error), "current revision can save");
            string current = File.ReadAllText(path);
            foreach (string command in new[] { "saveNote", "toggleTodo", "deleteNote" })
                Check(!store.Change(command, id, "旧草稿", "旧内容", false, revision, out error) && File.ReadAllText(path) == current,
                    "stale revision rejected without disk changes: " + command);
            DateTime future = DateTime.UtcNow.AddMinutes(1);
            store.Find(id).Updated = future.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            Check(store.Change("saveNote", id, "文本", "内容", false, store.Find(id).Updated, out error) &&
                DateTime.Parse(store.Find(id).Updated, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) > future,
                "revision advances despite clock rollback or same clock tick");
            string before = File.ReadAllText(path);
            Check(!store.Change("saveNote", "", " ", "\n", false, out error), "reject empty notes");
            Check(!store.Change("saveNote", id, new string('x', 81), "ok", false, out error), "reject long title");
            Check(!store.Change("saveNote", id, "ok", new string('x', 2001), false, out error), "reject long text");
            Check(!store.Change("saveNote", "unknown", "title", "text", false, out error), "reject stale ID");
            Check(!store.Change("unknown", id, "", "", false, out error), "reject unknown commands");
            Check(File.ReadAllText(path) == before && store.Find(id).Text == "内容", "validation failures preserve disk and memory");
            Check(store.Change("saveNote", "", "", new string('x', 2000), false, out error), "accept text-only maximum length");
            Check(File.Exists(path + ".bak"), "atomic replace keeps previous backup");
            Check(store.Change("deleteNote", id, "", "", false, out error) && new NotesStore(path).Find(id) == null, "delete persists across restart");
            File.WriteAllText(path, "{invalid json");
            var broken = new NotesStore(path);
            Check(broken.LoadError != null && !broken.Change("saveNote", "", "no", "overwrite", false, out error) && File.ReadAllText(path) == "{invalid json", "corrupt store is protected from overwrite");
            var capacity = new NotesStore(Path.Combine(folder, "capacity.json"));
            for (int i = 0; i < NotesStore.MaxNotes; i++)
                if (!capacity.Change("saveNote", "", "Note " + i, "content", false, out error)) throw new InvalidOperationException(error);
            Check(!capacity.Change("saveNote", "", "overflow", "content", false, out error) && capacity.Notes.Count() == NotesStore.MaxNotes, "enforce 200-note limit without losing data");
            string blocked = Path.Combine(folder, "not-a-directory"); File.WriteAllText(blocked, "blocked");
            var unwritable = new NotesStore(Path.Combine(blocked, "notes.json"));
            Check(!unwritable.Change("saveNote", "", "title", "text", false, out error) && !unwritable.Notes.Any() && error != null, "write failure does not mutate memory");
            CheckEdges();
            Console.WriteLine(checks + " notes/edge checks passed"); return 0;
        }
        finally { Directory.Delete(folder, true); }
    }
}
