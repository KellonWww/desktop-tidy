using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using DesktopTidy.Core;
using DesktopTidy.UI;
using DesktopTidy.Windows;

namespace DesktopTidy
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private bool startupWorkspace;
        private readonly bool keepTray, diagnostic;
        private readonly MessageWindow messages;
        private readonly NotifyIcon tray;
        private readonly WorkspaceMenu menu;
        private readonly Font sharedFont = new Font("Microsoft YaHei UI", 11F);
        private readonly Font menuFont = new Font("Microsoft YaHei UI", 9F);
        private WorkspaceController workspace;
        private readonly NotesEdgeController edgeNotes;
        private bool scanning, exitRequested, disposed;
        private DesktopReadResult pending, lastResult;
        private readonly object resultLock = new object();
        internal TrayApplicationContext(bool keepTray, bool diagnostic)
        {
            this.keepTray = keepTray; this.diagnostic = diagnostic;
            messages = new MessageWindow("DesktopTidy.Prototype." + NativeMethods.GetCurrentProcessId(), Receive);
            menu = new WorkspaceMenu { Font = menuFont };
            menu.Items.Add("显示分类窗口", null, delegate { ShowWorkspace(); });
            menu.Items.Add("刷新桌面图标", null, delegate { Scan(); });
            menu.Items.Add("打开边缘便签", null, delegate { if (edgeNotes != null) edgeNotes.ShowFromTray(); });
            var edgeToggle = new ToolStripMenuItem("鼠标贴边呼出便签");
            edgeToggle.Click += delegate { if (edgeNotes != null) edgeNotes.Toggle(); };
            menu.Items.Add(edgeToggle);
            menu.Opening += delegate { edgeToggle.Checked = edgeNotes != null && edgeNotes.Enabled; };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("隐藏工作区", null, delegate { if (workspace != null) workspace.RequestHide(); });
            var toggleGroups = menu.Items.Add("折叠全部分类框", null, delegate { if (workspace != null) workspace.ToggleGroupsCollapsed(); });
            menu.Opening += delegate { toggleGroups.Enabled = workspace != null && workspace.Visible; toggleGroups.Text = workspace != null && workspace.AllGroupsCollapsed ? "展开全部分类框" : "折叠全部分类框"; };
            var opacityMenu = WorkspaceMenu.CreateOpacityMenu(
                delegate { return GetWorkspace().OpacityPercent; },
                delegate(int value) { GetWorkspace().CommitOpacity(value); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { RequestExit(); });
            tray = new NotifyIcon { Icon = AppBrand.Icon, Text = "桌面整理 · 分类工作区", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowWorkspace(); };
            edgeNotes = new NotesEdgeController(GetWorkspace(), Notify);
            if (!keepTray) { startupWorkspace = true; Scan(); }
            if (GetWorkspace().HideIconsOnStartup && !scanning) { startupWorkspace = true; Scan(); }
        }
        private void Notify(string text)
        {
            if (tray.Visible) { tray.BalloonTipTitle = "桌面整理"; tray.BalloonTipText = text; tray.ShowBalloonTip(2500); }
        }
        private WorkspaceController GetWorkspace()
        {
            if (workspace == null)
            {
                workspace = new WorkspaceController(sharedFont, Notify, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTidy", "Workspace"));
                workspace.RefreshRequested = Scan;
                workspace.IconReady = delegate { lock (resultLock) { if (!disposed) NativeMethods.PostMessage(messages.Handle, NativeMethods.IconsMessage, IntPtr.Zero, IntPtr.Zero); } };
            }
            return workspace;
        }
        private void ShowWorkspace()
        {
            if (exitRequested) return;
            if (lastResult == null || !lastResult.Success)
            {
                startupWorkspace = true;
                if (!scanning) Scan();
                else Notify("正在扫描桌面，完成后会显示分类窗口。");
                return;
            }
            GetWorkspace().Show(lastResult);
        }
        private void Receive(int message, int command)
        {
            if (message == NativeMethods.CommandMessage && diagnostic)
            {
                if (command == 1) ShowWorkspace();
                else if (command == 2 && workspace != null) workspace.Hide();
                else if (command == 3) RequestExit();
                else if (command == 4) Scan();
                else if (command == 5) ShowWorkspace();
                else if (command == 6 && workspace != null) workspace.Hide();
            }
            else if (message == NativeMethods.ResultMessage)
            {
                DesktopReadResult result; lock (resultLock) { result = pending; pending = null; }
                scanning = false;
                if (workspace != null) workspace.SetRefreshing(false);
                if (result != null && result.Success) lastResult = result;
                if (workspace != null && result != null && !result.Success) workspace.Notify("刷新失败，保留原有结果：" + result.Error);
                if (result != null && !result.Success && startupWorkspace) { startupWorkspace = false; Notify("桌面扫描失败：" + result.Error); }
                if (result != null && result.Success && !exitRequested && (startupWorkspace || GetWorkspace().NeedsFirstRunGuide)) { startupWorkspace = false; ShowWorkspace(); }
                else if (workspace != null && workspace.Visible && result != null && result.Success && !exitRequested) workspace.Show(result);
                if (exitRequested) ExitThread();
            }
            else if (message == NativeMethods.IconsMessage) { if (workspace != null) workspace.InvalidateIcons(); }
            else if (message == NativeMethods.SettingChange || message == NativeMethods.ThemeChanged)
            { if (workspace != null) workspace.RefreshTheme(); if (edgeNotes != null) edgeNotes.RefreshTheme(); }
        }
        private void Scan()
        {
            if (scanning || exitRequested) return;
            scanning = true; if (workspace != null) workspace.SetRefreshing(true);
            var thread = new Thread(delegate()
            {
                DesktopReadResult result = DesktopReader.Read();
                lock (resultLock) { pending = result; if (!disposed) NativeMethods.PostMessage(messages.Handle, NativeMethods.ResultMessage, IntPtr.Zero, IntPtr.Zero); }
            });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        private void RequestExit()
        {
            if (exitRequested) return; exitRequested = true; tray.Visible = false;
            try
            {
                if (edgeNotes != null) edgeNotes.Dispose();
                if (workspace != null) { workspace.DisposeForExit(); workspace = null; }
            }
            finally { if (!scanning) ExitThread(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                lock (resultLock) { disposed = true; }
                if (edgeNotes != null) edgeNotes.Dispose();
                if (workspace != null) { workspace.Dispose(); workspace = null; }
                tray.Visible = false; tray.Dispose(); menu.Dispose(); messages.Dispose(); sharedFont.Dispose(); menuFont.Dispose(); pending = lastResult = null;
            }
            base.Dispose(disposing);
        }
    }
}
