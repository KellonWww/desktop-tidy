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
        private const double MotionDurationMilliseconds = 180.0;
        private readonly NotesMotionClock frames;
        private bool animating;
        private Point motionFrom, motionTo;
        private readonly Stopwatch elapsed = new Stopwatch();
        private Point dragStart;
        private Rectangle dragBounds;
        private bool dragMoved;
        internal bool Dragging { get; private set; }
        internal event Action<Point> DragPreview;
        internal event Action<Point, bool> DragCompleted;
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
            frames = new NotesMotionClock(this, MotionFrame);
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
            frames.Stop(); animating = false;
            bool wasVisible = Visible;
            Point previousLocation = Location;
            collapsing = false; target = bounds;
            inset = side == NotesEdge.Left ? new Point(36, 0) : side == NotesEdge.Right ? new Point(-36, 0) :
                side == NotesEdge.Top ? new Point(0, 36) : new Point(0, -36);
            if (Size != bounds.Size) Size = bounds.Size;
            if (!wasVisible)
            {
                Theme.Read().Apply(this);
                Location = ReduceMotion ? bounds.Location : new Point(target.X + inset.X, target.Y + inset.Y);
                Show();
            }
            else Location = previousLocation;
            StartMotion(target.Location);
            if (!initialized) { initialized = true; web.Initialize(); }
            if (web.Ready) { web.RestoreBrowserSurface(); web.Publish(); web.PublishNotes(true); }
            if (activate) Activate();
        }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        internal void BeginDrag()
        {
            if (!Visible || collapsing || Dragging || (Control.MouseButtons & MouseButtons.Left) == 0) return;
            StartDrag(Cursor.Position);
        }
        internal void StartDrag(Point point)
        {
            frames.Stop(); animating = false; dragStart = point; dragBounds = Bounds;
            dragMoved = false; Dragging = true; frames.Start();
        }
        private void DragTick(object sender, EventArgs e)
        {
            ContinueDrag(Cursor.Position, (Control.MouseButtons & MouseButtons.Left) != 0, (GetAsyncKeyState(0x1B) & 0x8000) != 0);
        }
        internal void ContinueDrag(Point point, bool leftDown, bool cancel)
        {
            if (!Dragging) return;
            if (cancel || !leftDown)
            {
                frames.Stop(); Dragging = false;
                if (cancel || !dragMoved) Bounds = dragBounds;
                if (DragCompleted != null) DragCompleted(point, cancel || !dragMoved);
                return;
            }
            if (Math.Abs(point.X - dragStart.X) + Math.Abs(point.Y - dragStart.Y) < 6 && !dragMoved) return;
            dragMoved = true;
            Point next = new Point(dragBounds.X + point.X - dragStart.X, dragBounds.Y + point.Y - dragStart.Y);
            if (Location == next) return;
            Location = next;
            if (DragPreview != null) DragPreview(point);
        }
        private void MotionFrame()
        {
            if (Dragging) DragTick(this, EventArgs.Empty);
            else if (animating) Animate();
        }
        private void StartMotion(Point destination)
        {
            motionFrom = Location; motionTo = destination;
            if (ReduceMotion || motionFrom == motionTo)
            {
                Location = destination;
                if (collapsing) Hide();
                return;
            }
            elapsed.Restart(); animating = true; frames.Start();
        }
        private void Animate()
        {
            double progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / MotionDurationMilliseconds);
            double eased = 1 - Math.Pow(1 - progress, 3);
            Point next = new Point(motionFrom.X + (int)Math.Round((motionTo.X - motionFrom.X) * eased),
                motionFrom.Y + (int)Math.Round((motionTo.Y - motionFrom.Y) * eased));
            if (Location != next) Location = next;
            if (progress < 1) return;
            frames.Stop(); animating = false;
            if (collapsing) Hide(); else Location = target.Location;
        }
        internal void Collapse()
        {
            if (!Visible || collapsing || IsDisposed || Dragging) return;
            frames.Stop(); animating = false; collapsing = true;
            if (Collapsed != null) Collapsed();
            StartMotion(new Point(target.X + inset.X, target.Y + inset.Y));
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
            if (value) { disposing = true; frames.Dispose(); }
            base.Dispose(value);
        }
    }
    // A WinForms WM_TIMER is low-priority and can miss frames while WebView2 is busy.
    // Coalesce worker ticks to one UI callback; never queue a backlog or touch HWNDs off-thread.
    internal sealed class NotesMotionClock : IDisposable
    {
        private const int FrameIntervalMilliseconds = 8;
        private readonly Control host;
        private readonly Action frame;
        private System.Threading.Thread worker;
        private System.Threading.ManualResetEvent stopped;
        private int generation, pending;
        private bool highResolution;
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
        internal NotesMotionClock(Control host, Action frame) { this.host = host; this.frame = frame; }
        internal void Start()
        {
            Stop();
            highResolution = timeBeginPeriod(1) == 0;
            if (!highResolution) Debug.WriteLine("Notes motion: high-resolution timer unavailable; using default resolution.");
            int version = generation;
            stopped = new System.Threading.ManualResetEvent(false);
            var signal = stopped;
            worker = new System.Threading.Thread(delegate()
            {
                while (!signal.WaitOne(FrameIntervalMilliseconds)) QueueFrame(version);
            }) { IsBackground = true, Name = "Notes motion frames" };
            worker.Start();
        }
        private void QueueFrame(int version)
        {
            if (version != System.Threading.Volatile.Read(ref generation) ||
                System.Threading.Interlocked.CompareExchange(ref pending, version, 0) != 0) return;
            try
            {
                host.BeginInvoke((Action)delegate
                {
                    System.Threading.Interlocked.CompareExchange(ref pending, 0, version);
                    if (version == generation && !host.IsDisposed) frame();
                });
            }
            catch (InvalidOperationException error)
            {
                System.Threading.Interlocked.CompareExchange(ref pending, 0, version);
                Debug.WriteLine("Notes motion callback canceled: " + error.Message);
            }
        }
        internal void Stop()
        {
            System.Threading.Interlocked.Increment(ref generation);
            if (worker != null)
            {
                stopped.Set(); worker.Join(); worker = null;
                stopped.Dispose(); stopped = null;
            }
            System.Threading.Interlocked.Exchange(ref pending, 0);
            if (highResolution)
            {
                highResolution = false;
                if (timeEndPeriod(1) != 0) Debug.WriteLine("Notes motion: timer resolution release failed.");
            }
        }
        public void Dispose() { Stop(); }
    }

    internal sealed class NotesDockHandle : Form
    {
        private readonly NotesDockButton button;
        private readonly ToolTip tip = new ToolTip();
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { var value = base.CreateParams; value.ExStyle |= 0x80 | 0x08000000; return value; } }
        internal NotesDockHandle(Action open)
        {
            Text = "便签停靠位置"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            TopMost = true; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None; MinimumSize = new Size(1, 1);
            button = new NotesDockButton { Dock = DockStyle.Fill, Cursor = Cursors.Hand, TabStop = false };
            button.Click += delegate { open(); }; Controls.Add(button);
        }
        protected override void CreateHandle()
        {
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { base.CreateHandle(); } finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        internal void Place(Rectangle bounds, NotesEdge side, string screen, bool paused)
        {
            Bounds = bounds;
            Theme theme = Theme.Read(); BackColor = theme.Surface;
            button.SetAppearance(theme, side, paused);
            using (var outline = NotesDockButton.Outline(ClientSize, side))
            {
                Region previous = Region; Region = new Region(outline);
                if (previous != null) previous.Dispose();
            }
            button.AccessibleName = "打开停靠在" + screen + " · " + NotesEdgeGeometry.Name(side) + "的便签";
            tip.SetToolTip(button, button.AccessibleName + (paused ? "（已暂停悬停呼出，点击仍可打开）" : "；悬停或点击打开"));
            if (!Visible) Show();
        }
        protected override void Dispose(bool disposing)
        { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    // Owner-drawn so the dock landmark never inherits the system button border or focus rectangle.
    internal sealed class NotesDockButton : Button
    {
        private Theme theme = Theme.Palette(false);
        private NotesEdge side;
        private bool hovered, pressed, paused;
        internal NotesDockButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        }
        internal void SetAppearance(Theme value, NotesEdge edge, bool isPaused)
        { theme = value; side = edge; paused = isPaused; Invalidate(); }
        internal static System.Drawing.Drawing2D.GraphicsPath Outline(Size size, NotesEdge edge)
        {
            float scale = Math.Max(1, Math.Min(size.Width, size.Height)) / 28f;
            float radius = Math.Min(8 * scale, Math.Min(size.Width, size.Height) / 2f);
            var rect = new RectangleF(.5f, .5f, Math.Max(1, size.Width - 1), Math.Max(1, size.Height - 1));
            // Extend the screen-facing corners outside the clip: attached edge is square, exposed edge rounded.
            if (edge == NotesEdge.Left) { rect.X -= radius; rect.Width += radius; }
            if (edge == NotesEdge.Right) rect.Width += radius;
            if (edge == NotesEdge.Top) { rect.Y -= radius; rect.Height += radius; }
            if (edge == NotesEdge.Bottom) rect.Height += radius;
            float diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            bool active = hovered || pressed;
            Color surface = active ? theme.AccentSoft : theme.Surface;
            Color ink = active ? theme.Accent : theme.Text;
            g.Clear(surface);
            using (var outline = Outline(ClientSize, side))
            using (var stroke = new Pen(active ? theme.AccentLine : theme.Stroke))
                g.DrawPath(stroke, outline);
            bool vertical = side == NotesEdge.Left || side == NotesEdge.Right;
            float scale = Math.Min(Width, Height) / 28f;
            using (var font = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
                if (vertical)
                {
                    TextRenderer.DrawText(g, "便", font, Rectangle.Round(new RectangleF(0, Height / 2f - 23 * scale, Width, 17 * scale)), ink, flags);
                    TextRenderer.DrawText(g, "签", font, Rectangle.Round(new RectangleF(0, Height / 2f - 6 * scale, Width, 17 * scale)), ink, flags);
                }
                else TextRenderer.DrawText(g, "便签", font, new Rectangle(0, 0, Width - (int)(18 * scale), Height), ink, flags);
            }
            float x = vertical ? Width / 2f : Width / 2f + 23 * scale;
            float y = vertical ? Height / 2f + 21 * scale : Height / 2f;
            float d = 2.5f * scale;
            PointF[] chevron = side == NotesEdge.Left ? new[] { new PointF(x-d,y-d), new PointF(x,y), new PointF(x-d,y+d) } :
                side == NotesEdge.Right ? new[] { new PointF(x+d,y-d), new PointF(x,y), new PointF(x+d,y+d) } :
                side == NotesEdge.Top ? new[] { new PointF(x-d,y-d), new PointF(x,y), new PointF(x+d,y-d) } :
                new[] { new PointF(x-d,y+d), new PointF(x,y), new PointF(x+d,y+d) };
            using (var pen = new Pen(paused ? theme.Muted : theme.Accent, 1.4f * scale))
            { pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawLines(pen, chevron); }
        }
    }

}
