using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using DesktopTidy.Core;

namespace DesktopTidy.UI
{
    // A real top-level window shares the store and editor, but never the edge auto-hide policy.
    internal sealed class NoteWindowForm : Form
    {
        private readonly WorkspaceController owner;
        private readonly WebWorkspace web;
        private readonly Label notice = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "正在打开便签…" };
        private bool allowClose, closePending, dockAfterClose;
        internal string NoteId { get; private set; }
        internal event Action CloseCanceled;
        private string BoundsFile { get { return Path.Combine(owner.LayoutDirectory, "NoteWindows", NoteId + ".json"); } }
        internal NoteWindowForm(WorkspaceController owner, string id)
        {
            this.owner = owner; NoteId = id;
            Text = "便签 · 桌面整理"; Icon = AppBrand.Icon;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(380, 340); Size = new Size(650, 680);
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
            ReadBounds(); KeepVisible();
            Controls.Add(notice); web = new WebWorkspace(owner, this) { Visible = false }; Controls.Add(web);
            Shown += delegate { web.PrepareBrowserSurface(); web.Initialize(); };
            owner.Notes.Changed += RefreshTitle;
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            RefreshTitle();
        }
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        protected override void CreateHandle()
        {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { base.CreateHandle(); } finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }
        // Only clean/approved closes reach the OS; saving is asynchronous inside WebView2.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && web.Ready)
            {
                e.Cancel = true;
                if (!closePending) { closePending = true; web.NoteWindowEvent("noteWindowClosing"); }
            }
            else SaveBounds();
            base.OnFormClosing(e);
        }
        internal void ConfirmClose(bool dirty, bool dock)
        {
            dockAfterClose = dock;
            if (!dirty) { FinishClose(); return; }
            DialogResult choice = MessageBox.Show(this, "便签有未保存的修改，是否保存后关闭？\n选择“否”将放弃本次修改。", "关闭便签", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes) web.NoteWindowEvent("noteWindowSaveAndClose");
            else if (choice == DialogResult.No) web.NoteWindowEvent("noteWindowDiscardAndClose");
            else CancelClose();
        }
        internal void CancelClose()
        {
            closePending = false; dockAfterClose = false;
            if (CloseCanceled != null) CloseCanceled();
        }
        internal void FinishClose()
        {
            allowClose = true; Close();
            if (dockAfterClose && owner.ShowEdgeNotes != null) owner.ShowEdgeNotes();
        }
        internal void ToggleTopMost()
        {
            TopMost = !TopMost;
            web.NoteWindowEvent(TopMost ? "noteWindowTopMostOn" : "noteWindowTopMostOff");
        }
        internal void WebReady()
        {
            notice.Visible = false; web.Visible = true; web.BringToFront();
            web.NoteWindowEvent(TopMost ? "noteWindowTopMostOn" : "noteWindowTopMostOff");
        }
        internal void WebFailed(string message) { notice.Text = message; notice.Visible = true; notice.BringToFront(); CancelClose(); }
        private void RefreshTitle()
        {
            var note = owner.Notes.Find(NoteId);
            Text = (note == null ? "便签已删除" : string.IsNullOrWhiteSpace(note.Title) ? "无标题便签" : note.Title) + " · 桌面整理";
        }
        // Display callbacks may arrive off-thread; move orphaned windows onto a connected screen.
        private void DisplayChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)KeepVisible); }
            catch (InvalidOperationException) { /* The window may close between the check and dispatch. */ }
        }
        private void KeepVisible()
        {
            if (IsDisposed) return;
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Rectangle work = Screen.FromRectangle(bounds).WorkingArea;
            int width = Math.Min(Math.Max(380, bounds.Width), work.Width), height = Math.Min(Math.Max(340, bounds.Height), work.Height);
            MinimumSize = new Size(Math.Min(380, work.Width), Math.Min(340, work.Height));
            var visibleBounds = new Rectangle(Math.Max(work.Left, Math.Min(bounds.Left, work.Right - width)), Math.Max(work.Top, Math.Min(bounds.Top, work.Bottom - height)), width, height);
            if (bounds == visibleBounds) return;
            FormWindowState previousState = WindowState;
            if (previousState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            Bounds = visibleBounds;
            // Monitor changes must not silently unmaximize or restore a minimized note.
            if (previousState != FormWindowState.Normal) WindowState = previousState;
        }
        private void ReadBounds()
        {
            try
            {
                if (!File.Exists(BoundsFile)) return;
                var saved = new JavaScriptSerializer().Deserialize<WindowPlacement>(AtomicFile.Read(BoundsFile, 4096));
                if (saved == null || saved.Width < 1 || saved.Height < 1 || saved.Width > 32768 || saved.Height > 32768 || Math.Abs((long)saved.X) > 100000 || Math.Abs((long)saved.Y) > 100000)
                    throw new InvalidDataException("便签窗口位置无效");
                Bounds = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height); TopMost = saved.TopMost;
                if (saved.Maximized) Shown += delegate { WindowState = FormWindowState.Maximized; };
            }
            catch (Exception error) { Program.Report(error); owner.Notify("便签窗口位置读取失败，使用默认位置。"); }
        }
        private void SaveBounds()
        {
            try
            {
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                AtomicFile.Write(BoundsFile, new JavaScriptSerializer().Serialize(new WindowPlacement { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height, TopMost = TopMost, Maximized = WindowState == FormWindowState.Maximized }));
            }
            catch (Exception error) { Program.Report(error); owner.Notify("便签窗口位置保存失败，便签内容不受影响。"); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { owner.Notes.Changed -= RefreshTitle; SystemEvents.DisplaySettingsChanged -= DisplayChanged; }
            base.Dispose(disposing);
        }
        // Public properties are required by the existing JSON serializer.
        internal sealed class WindowPlacement
        {
            public int X { get; set; } public int Y { get; set; }
            public int Width { get; set; } public int Height { get; set; }
            public bool TopMost { get; set; } public bool Maximized { get; set; }
        }
    }
}
