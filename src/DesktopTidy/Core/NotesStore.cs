using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;

namespace DesktopTidy.Core
{
    [DataContract]
    internal sealed class WorkspaceNote
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public bool IsTodo { get; set; }
        [DataMember] public bool Completed { get; set; }
        [DataMember] public bool IsPinned { get; set; }
        [DataMember] public string Updated { get; set; }
    }

    internal sealed class NotesStore
    {
        internal event Action Changed;
        internal const int MaxNotes = 200;
        internal const int MaxText = 2000;
        private readonly string path;
        private readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<WorkspaceNote>));
        private List<WorkspaceNote> notes = new List<WorkspaceNote>();
        internal IEnumerable<WorkspaceNote> Notes { get { return notes.AsReadOnly(); } }
        internal string LoadError { get; private set; }

        internal NotesStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTidy", "notes.json")) { }
        internal NotesStore(string path)
        {
            this.path = path;
            try
            {
                if (!File.Exists(path)) return;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException("便签文件过大");
                    var loaded = (List<WorkspaceNote>)serializer.ReadObject(stream);
                    Validate(loaded); notes = loaded;
                }
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                LoadError = "便签读取失败，原文件已保留，请检查 " + path + "：" + error.Message;
            }
        }
        private static bool Expected(Exception error)
        {
            return error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is SecurityException ||
                error is SerializationException || error is ArgumentException || error is InvalidOperationException;
        }
        private static void Validate(List<WorkspaceNote> values)
        {
            if (values == null || values.Count > MaxNotes) throw new InvalidDataException("便签数量无效（最多 200 条）");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in values)
            {
                Guid id;
                if (note == null || !Guid.TryParseExact(note.Id, "N", out id) || !ids.Add(note.Id) ||
                    note.Title == null || note.Title.Length > 80 || note.Text == null || note.Text.Length > MaxText ||
                    (string.IsNullOrWhiteSpace(note.Title) && string.IsNullOrWhiteSpace(note.Text)) ||
                    (!note.IsTodo && note.Completed) || note.Updated == null)
                    throw new InvalidDataException("便签内容无效");
            }
        }
        internal WorkspaceNote Find(string id) { return notes.Find(note => note.Id == id); }
        internal bool Change(string command, string id, string title, string text, bool todo, out string error)
        { return Change(command, id, title, text, todo, null, out error); }
        internal bool Change(string command, string id, string title, string text, bool todo, string revision, out string error)
        {
            error = LoadError;
            if (error != null) return false;
            var next = new List<WorkspaceNote>(notes);
            var existing = Find(id);
            if (command != "saveNote" && command != "deleteNote" && command != "toggleTodo" && command != "togglePin")
            { error = "不支持的便签操作"; return false; }
            if ((id.Length > 0 || command != "saveNote") && existing == null)
            { error = "便签不存在，请刷新后重试"; return false; }
            if (existing != null && revision != null && existing.Updated != revision)
            { error = "便签已在另一处更新，请重新打开编辑；当前草稿仍保留。"; return false; }
            if (command == "deleteNote") next.Remove(existing);
            else
            {
                if (command == "toggleTodo" && !existing.IsTodo) { error = "文本便签不能标记完成"; return false; }
                var changed = command == "toggleTodo" || command == "togglePin"
                    ? new WorkspaceNote {
                        Id = id, Title = existing.Title, Text = existing.Text, IsTodo = existing.IsTodo,
                        Completed = command == "toggleTodo" ? !existing.Completed : existing.Completed,
                        IsPinned = command == "togglePin" ? !existing.IsPinned : existing.IsPinned }
                    : new WorkspaceNote {
                        Id = existing == null ? Guid.NewGuid().ToString("N") : id, Title = title.Trim(), Text = text.Trim(),
                        IsTodo = todo, IsPinned = existing != null && existing.IsPinned,
                        Completed = todo && existing != null && existing.Completed };
                // Revisions must keep advancing even within one clock tick or after a clock rollback.
                DateTime updated = DateTime.UtcNow, previousUpdate;
                if (existing != null && DateTime.TryParseExact(existing.Updated, "o", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out previousUpdate) && updated <= previousUpdate)
                {
                    if (previousUpdate == DateTime.MaxValue) { error = "便签更新时间无效，请先备份并检查数据文件。"; return false; }
                    updated = previousUpdate.AddTicks(1);
                }
                changed.Updated = updated.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                if (existing == null) next.Add(changed); else next[next.IndexOf(existing)] = changed;
            }
            string temporary = path + ".tmp";
            try
            {
                Validate(next); Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { serializer.WriteObject(stream, next); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
                notes = next;
            }
            catch (Exception failure)
            {
                if (!Expected(failure)) throw;
                error = "便签保存失败：" + failure.Message; return false;
            }
            if (Changed != null) Changed();
            return true;
        }
    }
}
