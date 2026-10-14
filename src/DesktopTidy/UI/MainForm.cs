using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopTidy.Windows;

namespace DesktopTidy.UI
{
    internal sealed class MainForm : Form
    {
        private readonly RoundedButton scan, showBoxes, undo, reset;
        private readonly DesktopListControl list;
        private readonly Label status, memory;
        private readonly TextBlock title, subtitle, guide, section, footer;
        private readonly TextBlock[] textBlocks;
        private readonly RoundedButton[] buttons;
        private readonly bool noActivate;
        private Theme theme = Theme.Read();
        private Rectangle workspace;
        private bool hasResult;
        internal Action ShowWorkspace, UndoWorkspace, ResetWorkspace;
        internal MainForm(bool keepTray, Action readDesktop, Font sharedFont, Font headingFont, bool noActivate)
        {
            SuspendLayout();
            this.noActivate = noActivate;
            Icon = AppBrand.Icon; Text = "桌面整理 · 分类框"; ImeMode = ImeMode.Disable;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            Font = sharedFont; ClientSize = new Size(960, 700); MinimumSize = new Size(820, 620);
            StartPosition = FormStartPosition.CenterScreen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            title = Block("桌面整理", ""); title.Font = headingFont;
            subtitle = Block("让桌面各归其位。双击打开，拖动分类，不搬动原文件。", "muted");
            section = Block("桌面文件", "surface"); status = Label("点击“扫描桌面”开始。", "surface-muted");
            guide = Block("侧栏支持排版、透明度与桌面图标显隐；默认保留系统图标。", "surface-muted");
            memory = Label("", "muted"); memory.TextAlign = ContentAlignment.MiddleRight;
            footer = Block(keepTray ? "托盘模式 · 右键托盘可隐藏工作区或退出。" : "显示工作区后关窗保留托盘；未显示时关窗退出。", "muted");
            scan = new RoundedButton { Tag = "toolbar", Secondary = true, Quiet = true, Text = "扫描桌面", TabIndex = 0 };
            scan.Click += delegate { readDesktop(); };
            showBoxes = new RoundedButton { Tag = "toolbar", Text = "显示工作区", Enabled = false, TabIndex = 1 };
            showBoxes.Click += delegate { if (ShowWorkspace != null) ShowWorkspace(); };
            undo = new RoundedButton { Tag = "toolbar", Secondary = true, Quiet = true, Text = "撤销修改", Enabled = false, TabIndex = 2 };
            undo.Click += delegate { if (UndoWorkspace != null) UndoWorkspace(); };
            reset = new RoundedButton { Tag = "toolbar", Secondary = true, Quiet = true, Text = "恢复框位置", Enabled = false, TabIndex = 3 };
            reset.Click += delegate { if (ResetWorkspace != null) ResetWorkspace(); };
            buttons = new RoundedButton[] { scan, showBoxes, undo, reset };
            textBlocks = new TextBlock[] { title, subtitle, section, guide, footer };
            list = new DesktopListControl { TabIndex = 4 };
            Controls.AddRange(new Control[] { status, memory, scan, showBoxes, undo, reset, list });
            AcceptButton = scan; Arrange(); ResumeLayout(false);
        }
        private static TextBlock Block(string text, string role) { return new TextBlock { Text = text, Role = role }; }
        private static Label Label(string text, string role) { return new Label { Text = text, Tag = role, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }; }
        protected override bool ShowWithoutActivation { get { return noActivate; } }
        private int Scale(int value) { return (int)Math.Round(value * Font.Height / 20F); }
        private void Arrange()
        {
            if (list == null) return;
            SuspendLayout();
            try
            {
                int m = Scale(32), width = ClientSize.Width - m * 2;
                title.SetBounds(m + Scale(60), Scale(26), width - Scale(60), Scale(48));
                subtitle.SetBounds(m, Scale(80), width, Scale(44));
                int gap = Scale(10), buttonWidth = Math.Min(Scale(174), (width - gap * 3) / 4), top = Scale(132);
                for (int i = 0; i < buttons.Length; i++) buttons[i].SetBounds(m + i * (buttonWidth + gap), top, buttonWidth, Scale(46));
                workspace = new Rectangle(m, Scale(202), width, Math.Max(Scale(200), ClientSize.Height - Scale(280)));
                section.SetBounds(m + Scale(22), workspace.Top + Scale(17), width - Scale(44), Scale(28));
                status.SetBounds(m + Scale(22), workspace.Top + Scale(49), width - Scale(44), Scale(30));
                list.SetBounds(m + Scale(16), workspace.Top + Scale(90), width - Scale(32), Math.Max(Scale(80), workspace.Height - Scale(142)));
                guide.SetBounds(m + Scale(22), workspace.Bottom - Scale(40), width - Scale(44), Scale(28));
                footer.SetBounds(m, ClientSize.Height - Scale(60), width - Scale(200), Scale(40));
                memory.SetBounds(ClientSize.Width - m - Scale(194), ClientSize.Height - Scale(60), Scale(194), Scale(40)); Invalidate();
            }
            finally { ResumeLayout(false); }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            AppBrand.Draw(e.Graphics, new Rectangle(Scale(32), Scale(32), Scale(40), Scale(40)));
            SurfaceDrawing.Fill(e.Graphics, workspace, Scale(28), theme.Surface);
            SurfaceDrawing.Outline(e.Graphics, new Rectangle(workspace.X, workspace.Y, workspace.Width - 1, workspace.Height - 1), Scale(28), theme.Stroke);
            foreach (var block in textBlocks)
                TextRenderer.DrawText(e.Graphics, block.Text, block.Font ?? Font, block.Bounds, block.Role.Contains("muted") ? theme.Muted : theme.Text,
                    TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); RefreshTheme(); RefreshMemory(); }
        internal void RefreshTheme() { SetTheme(Theme.Read()); }
        internal void SetTheme(Theme value) { theme = value; theme.Apply(this); Invalidate(true); }
        internal void SetBusy(bool busy)
        {
            scan.Enabled = !busy; scan.Text = busy ? "正在扫描…" : "扫描桌面";
            showBoxes.Enabled = undo.Enabled = reset.Enabled = !busy && hasResult;
            if (busy) status.Text = "正在读取桌面；不会修改文件和系统图标。";
        }
        internal void SetResult(DesktopReadResult result)
        {
            if (!result.Success && hasResult) { SetBusy(false); SetWorkspaceStatus("刷新失败，保留原有结果：" + result.Error); return; }
            hasResult = result.Success; SetBusy(false); list.SetItems(result.Icons);
            section.Text = "桌面文件  /  " + result.Icons.Count + " 项";
            status.Text = result.Success ? "扫描完成；首次使用会引导进入工作区，也可点击“显示工作区”。" : "读取未完成，请重新扫描。";
            guide.Text = result.Success ? "框内排版：点击标题排版 · 全局排版、透明度和图标显隐：工作区侧栏。" : "读取失败：" + result.Error;
            RefreshMemory(); Invalidate();
        }
        internal void SetWorkspaceStatus(string text) { status.Text = text; status.AccessibleDescription = text; RefreshMemory(); }
        // 静态文案共享绘制，避免每次重建主窗为五段文案分配原生控件。
        private sealed class TextBlock
        {
            internal string Text, Role;
            internal Font Font;
            internal Rectangle Bounds;
            internal void SetBounds(int x, int y, int width, int height) { Bounds = new Rectangle(x, y, width, height); }
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ManagerAccessibility(this); }
        private sealed class ManagerAccessibility : ControlAccessibleObject
        {
            private readonly MainForm owner;
            internal ManagerAccessibility(MainForm owner) : base(owner) { this.owner = owner; }
            public override int GetChildCount() { return Math.Max(0, base.GetChildCount()) + owner.textBlocks.Length; }
            public override AccessibleObject GetChild(int index)
            {
                int controls = Math.Max(0, base.GetChildCount());
                if (index < 0 || index >= controls + owner.textBlocks.Length) return null;
                if (index < controls) return base.GetChild(index);
                return new TextAccessibility(owner, owner.textBlocks[index - controls]);
            }
        }
        private sealed class TextAccessibility : AccessibleObject
        {
            private readonly MainForm owner;
            private readonly TextBlock block;
            internal TextAccessibility(MainForm owner, TextBlock block) { this.owner = owner; this.block = block; }
            public override string Name { get { return block.Text; } set { } }
            public override AccessibleRole Role { get { return AccessibleRole.StaticText; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override Rectangle Bounds { get { return owner.IsDisposed || !owner.IsHandleCreated ? Rectangle.Empty : owner.RectangleToScreen(block.Bounds); } }
            public override AccessibleStates State { get { return !owner.IsDisposed && owner.Visible ? AccessibleStates.ReadOnly : AccessibleStates.ReadOnly | AccessibleStates.Invisible; } }
        }
        private void RefreshMemory()
        {
            try { memory.Text = string.Format("应用内存  {0:F1} MiB", NativeMethods.PrivateMiB()); }
            catch (System.ComponentModel.Win32Exception) { memory.Text = "内存信息暂不可用"; }
        }
    }
}
