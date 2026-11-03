using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DesktopTidy.Core;

namespace DesktopTidy.UI
{
    internal sealed class RecoveryForm : Form
    {
        private readonly NotesStore notes;
        private readonly ListBox history = new ListBox { Dock = DockStyle.Fill };
        private readonly ListBox entries = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Title" };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 62, AutoEllipsis = true };
        internal bool Restored { get; private set; }
        internal RecoveryForm(NotesStore notes)
        {
            this.notes = notes; Text = "数据备份与恢复"; Width = 760; Height = 530;
            MinimumSize = new System.Drawing.Size(640, 400); StartPosition = FormStartPosition.CenterScreen;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(8), AutoSize = false, WrapContents = true };
            Add(buttons, "导出完整备份", Export); Add(buttons, "恢复备份并退出", Restore);
            Add(buttons, "找回选中便签（另存）", RestoreNote); Add(buttons, "导出脱敏诊断", ExportDiagnostics);
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300 };
            split.Panel1.Controls.Add(history); split.Panel2.Controls.Add(entries);
            Controls.Add(split); Controls.Add(status); Controls.Add(buttons);
            status.Text = "左侧选择历史，右侧选择要找回的便签。恢复整份备份会覆盖便签、独立草稿和布局并退出；注册表偏好不包含在备份中。";
            history.SelectedIndexChanged += delegate { LoadHistory(); };
            if (Directory.Exists(notes.HistoryDirectory))
                foreach (string file in Directory.GetFiles(notes.HistoryDirectory, "*.json").OrderByDescending(Path.GetFileName)) history.Items.Add(new HistoryEntry(file));
            string previous = Path.Combine(notes.DataDirectory, "notes.json.bak");
            if (File.Exists(previous)) history.Items.Insert(0, new HistoryEntry(previous));
            if (history.Items.Count > 0) history.SelectedIndex = 0;
        }
        private sealed class HistoryEntry
        {
            internal readonly string Path;
            internal HistoryEntry(string path) { Path = path; }
            public override string ToString() { return System.IO.Path.GetFileName(Path); }
        }
        private static void Add(Control parent, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32 };
            button.Click += delegate
            {
                try { action(); }
                catch (Exception error) { Program.Report(error); MessageBox.Show(error.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            parent.Controls.Add(button);
        }
        private void LoadHistory()
        {
            entries.Items.Clear(); var selected = history.SelectedItem as HistoryEntry;
            if (selected == null) return;
            try { foreach (var note in NotesStore.Parse(AtomicFile.Read(selected.Path, DraftStore.Maximum))) entries.Items.Add(note); }
            catch (Exception error) { Program.Report(error); status.Text = "历史读取失败：" + error.Message; }
        }
        private void RestoreNote()
        {
            var note = entries.SelectedItem as WorkspaceNote; if (note == null) { status.Text = "请选中要找回的便签。"; return; }
            string error;
            if (!notes.Change("saveNote", "", note.Title, note.Text, note.IsTodo, out error)) throw new IOException(error);
            status.Text = "已另存为新便签，未覆盖现有内容；完成和置顶状态请重新设置。";
        }
        private void Export()
        {
            using (var dialog = new SaveFileDialog { Filter = "Desktop Tidy 备份 (*.json)|*.json", FileName = "DesktopTidy-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    new RecoveryStore(notes.DataDirectory).Export(dialog.FileName);
                    status.Text = "备份已导出（明文，含便签、草稿及路径，请妥善保管）。已有同名文件不会覆盖。";
                }
        }
        private void Restore()
        {
            using (var dialog = new OpenFileDialog { Filter = "Desktop Tidy 备份 (*.json)|*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this, "将覆盖当前便签、独立草稿及布局，然后退出。恢复前会自动保留安全副本。继续？", "确认恢复", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                string safety = new RecoveryStore(notes.DataDirectory).Restore(dialog.FileName);
                MessageBox.Show(this, "恢复完成，请重新启动应用。恢复前副本：" + safety, "桌面整理");
                Restored = true; Close();
            }
        }
        private void ExportDiagnostics()
        {
            using (var dialog = new SaveFileDialog { Filter = "诊断文本 (*.txt)|*.txt", FileName = "DesktopTidy-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { Diagnostics.Export(dialog.FileName); status.Text = "诊断已导出，不包含便签、路径或原始异常日志。"; }
        }
    }
}
