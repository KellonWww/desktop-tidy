using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopTidy.Core;

namespace DesktopTidy.UI
{
    internal sealed class NotesEdgeForm : Form
    {
        private readonly WebWorkspace web;
        private readonly Label notice;
        private readonly Button close;
        private readonly Timer animation = new Timer { Interval = 16 };
        private readonly Stopwatch elapsed = new Stopwatch();
        private Rectangle target;
        private Point inset;
        private bool initialized, collapsing, disposing;
        internal bool InteractionActive { get; set; }
        internal bool ReduceMotion { get; set; }
        internal bool Failed { get; private set; }
        internal bool Ready { get { return web.Ready; } }
        internal event Action Collapsed;
        protected override bool ShowWithoutActivation { get { return true; } }

        internal NotesEdgeForm(WorkspaceController owner)
        {
            Text = "桌面整理 · 边缘便签"; AccessibleName = Text;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            Icon = AppBrand.Icon; MinimumSize = new Size(1, 1);
            ReduceMotion = !SystemInformation.IsMenuAnimationEnabled;
            notice = new Label { Dock = DockStyle.Fill, Text = "正在打开便签…", TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(30) };
            close = new Button { Dock = DockStyle.Bottom, Height = 42, Text = "收起便签", FlatStyle = FlatStyle.Flat };
            close.Click += delegate { Collapse(); };
            Controls.Add(notice); Controls.Add(close);
            web = new WebWorkspace(owner, this) { Visible = false };
            Controls.Add(web);
            web.PrepareBrowserSurface();
            animation.Tick += Animate;
        }
        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        protected override void CreateHandle()
        {
            // Keep the entire independent drawer in one per-monitor-v2 context,
            // including handle recreation after callbacks from the desktop host.
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { base.CreateHandle(); }
            finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x80; return value; }
        }
        internal void Reveal(Rectangle bounds, NotesEdge side, bool activate)
        {
            if (IsDisposed) return;
            animation.Stop(); collapsing = false; target = bounds;
            inset = side == NotesEdge.Left ? new Point(18, 0) : side == NotesEdge.Right ? new Point(-18, 0) :
                side == NotesEdge.Top ? new Point(0, 18) : new Point(0, -18);
            Bounds = bounds; Theme.Read().Apply(this);
            if (!Visible)
            {
                if (!ReduceMotion) Location = new Point(target.X + inset.X, target.Y + inset.Y);
                Show();
                if (!ReduceMotion) { elapsed.Restart(); animation.Start(); }
            }
            if (!initialized) { initialized = true; web.Initialize(); }
            if (web.Ready) { web.RestoreBrowserSurface(); web.Publish(); web.PublishNotes(true); }
            if (activate) Activate();
        }
        private void Animate(object sender, EventArgs e)
        {
            double progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / 180.0);
            double eased = 1 - Math.Pow(1 - progress, 3);
            double offset = collapsing ? eased : 1 - eased;
            Location = new Point(target.X + (int)Math.Round(inset.X * offset), target.Y + (int)Math.Round(inset.Y * offset));
            if (progress < 1) return;
            animation.Stop();
            if (collapsing) Hide(); else Bounds = target;
        }
        internal void Collapse()
        {
            if (!Visible || collapsing || IsDisposed) return;
            collapsing = true;
            if (Collapsed != null) Collapsed();
            if (ReduceMotion) { animation.Stop(); Hide(); return; }
            elapsed.Restart(); animation.Start();
        }
        internal void WebReady() { close.Visible = false; notice.Visible = false; web.BringToFront(); web.RestoreBrowserSurface(); }
        internal void WebFailed(string message)
        {
            Failed = true; close.Visible = true;
            notice.Text = "边缘便签暂不可用，原便签数据未更改。\n请从托盘重新打开重试；桌面分类不受影响。\n\n" + message;
            notice.Visible = true; notice.BringToFront();
            Debug.WriteLine("Edge notes: " + message);
        }
        internal void RefreshTheme() { if (!IsDisposed) { Theme.Read().Apply(this); if (Visible) web.Publish(); } }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
            using (var path = SurfaceDrawing.Rounded(ClientRectangle, 18))
            {
                var previous = Region; Region = new Region(path); if (previous != null) previous.Dispose();
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!disposing && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Collapse(); return; }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool value)
        {
            if (value) { disposing = true; animation.Stop(); animation.Dispose(); }
            base.Dispose(value);
        }
    }
}
