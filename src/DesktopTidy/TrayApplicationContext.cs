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
        private readonly ContextMenuStrip menu;
        private readonly Font sharedFont = new Font("Microsoft YaHei UI", 11F);
        private readonly Font headingFont = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
        private WorkspaceController workspace;
        private MainForm form;
        private bool scanning, exitRequested, disposed;
        private DesktopReadResult pending, lastResult;
        private readonly object resultLock = new object();
        internal TrayApplicationContext(bool keepTray, bool diagnostic)
        {
            this.keepTray = keepTray; this.diagnostic = diagnostic;
            messages = new MessageWindow("DesktopTidy.Prototype." + NativeMethods.GetCurrentProcessId(), Receive);
            menu = new ContextMenuStrip();
            menu.Items.Add("打开管理窗口", null, delegate { ShowWindow(); });
            menu.Items.Add("显示工作区", null, delegate { ShowWorkspace(); });
            menu.Items.Add("隐藏工作区", null, delegate { if (workspace != null) workspace.RequestHide(); });
            var toggleGroups = menu.Items.Add("折叠全部分类框", null, delegate { if (workspace != null) workspace.ToggleGroupsCollapsed(); });
            menu.Opening += delegate { toggleGroups.Enabled = workspace != null && workspace.Visible; toggleGroups.Text = workspace != null && workspace.AllGroupsCollapsed ? "展开全部分类框" : "折叠全部分类框"; };
            var opacityMenu = new ToolStripMenuItem("工作区不透明度（最低25%）");
            for (int i = 1; i <= 4; i++)
            {
                int value = i * 25;
                var choice = new ToolStripMenuItem(value + "%");
                choice.Click += delegate { GetWorkspace().CommitOpacity(value); };
                choice.Tag = value; opacityMenu.DropDownItems.Add(choice);
            }
            opacityMenu.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem choice in opacityMenu.DropDownItems)
                    choice.Checked = (int)choice.Tag == GetWorkspace().OpacityPercent;
            };
            menu.Items.Add(opacityMenu);
            menu.Items.Add("退出", null, delegate { RequestExit(); });
            tray = new NotifyIcon { Icon = AppBrand.Icon, Text = "桌面整理 · 分类框", ContextMenuStrip = menu, Visible = keepTray };
            tray.DoubleClick += delegate { ShowWindow(); };
            if (!keepTray) ShowWindow();
            if (GetWorkspace().HideIconsOnStartup) { startupWorkspace = true; Scan(); }
        }
        private void Notify(string text)
        {
            if (form != null && !form.IsDisposed) form.SetWorkspaceStatus(text);
            else if (tray.Visible) { tray.BalloonTipTitle = "桌面整理"; tray.BalloonTipText = text; tray.ShowBalloonTip(2500); }
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
            if (lastResult == null || !lastResult.Success) { ShowWindow(); Notify("请点击“扫描桌面”，扫描完成后显示工作区。"); return; }
            GetWorkspace().Show(lastResult); if (workspace.Visible) tray.Visible = true;
        }
        private void Receive(int message, int command)
        {
            if (message == NativeMethods.CommandMessage && diagnostic)
            {
                if (command == 1) ShowWindow();
                else if (command == 2 && form != null) form.Close();
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
                if (form != null && !form.IsDisposed && result != null) form.SetResult(result);
                if (workspace != null && result != null && !result.Success) workspace.Notify("刷新失败，保留原有结果：" + result.Error);
                if (result != null && result.Success && !exitRequested && (startupWorkspace || GetWorkspace().NeedsFirstRunGuide)) { startupWorkspace = false; ShowWorkspace(); }
                else if (workspace != null && workspace.Visible && result != null && result.Success && !exitRequested) workspace.Show(result);
                if (exitRequested) ExitThread();
            }
            else if (message == NativeMethods.IconsMessage) { if (workspace != null) workspace.InvalidateIcons(); }
            else if (message == NativeMethods.SettingChange || message == NativeMethods.ThemeChanged)
            { if (form != null) form.RefreshTheme(); if (workspace != null) workspace.RefreshTheme(); }
        }
        private void ShowWindow()
        {
            if (exitRequested) return;
            if (form == null)
            {
                var window = new MainForm(keepTray, Scan, sharedFont, headingFont, diagnostic); form = window;
                window.ShowWorkspace = ShowWorkspace;
                window.UndoWorkspace = delegate { GetWorkspace().Undo(); };
                window.ResetWorkspace = delegate { GetWorkspace().ResetLayout(); };
                window.FormClosed += delegate
                {
                    if (ReferenceEquals(form, window)) form = null;
                    if (!exitRequested && !keepTray && (workspace == null || !workspace.Visible)) RequestExit();
                };
                if (lastResult != null) window.SetResult(lastResult); window.SetBusy(scanning); window.Show();
            }
            else { form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); }
        }
        private void Scan()
        {
            if (scanning || exitRequested) return;
            scanning = true; if (workspace != null) workspace.SetRefreshing(true); if (form != null) form.SetBusy(true);
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
                if (workspace != null) workspace.Dispose();
                if (form != null) form.Close();
            }
            finally { if (!scanning) ExitThread(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                lock (resultLock) { disposed = true; }
                if (workspace != null) { workspace.Dispose(); workspace = null; }
                if (form != null) { form.Dispose(); form = null; }
                tray.Visible = false; tray.Dispose(); menu.Dispose(); messages.Dispose(); sharedFont.Dispose(); headingFont.Dispose(); pending = lastResult = null;
            }
            base.Dispose(disposing);
        }
    }
}
