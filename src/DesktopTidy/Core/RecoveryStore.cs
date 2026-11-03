using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DesktopTidy.Core
{
    [DataContract] internal sealed class RecoveryBundle
    {
        [DataMember] public int Version = 1;
        [DataMember] public string CreatedUtc = DateTime.UtcNow.ToString("o");
        [DataMember] public Dictionary<string, string> Files = new Dictionary<string, string>();
    }
    internal sealed class RecoveryStore
    {
        private readonly string directory;
        private static readonly string[] Names = { "notes.json", "drafts-workspace.json", "drafts-edge.json", "Workspace/layout.json" };
        private const int Maximum = 16 * 1024 * 1024;
        internal RecoveryStore(string directory) { this.directory = directory; }
        private string Target(string name) { return Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar)); }
        private string Journal { get { return Path.Combine(directory, "restore-journal.json"); } }
        private static string Serialize(RecoveryBundle bundle)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(RecoveryBundle)).WriteObject(stream, bundle);
                if (stream.Length > Maximum) throw new InvalidDataException("备份超过 16 MiB");
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        private static RecoveryBundle Parse(string content, bool validate)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                var bundle = (RecoveryBundle)new DataContractJsonSerializer(typeof(RecoveryBundle)).ReadObject(stream);
                if (bundle == null || bundle.Version != 1 || bundle.Files == null || bundle.Files.Count != Names.Length) throw new InvalidDataException("备份版本或文件清单不受支持");
                foreach (string name in Names)
                {
                    string value;
                    if (!bundle.Files.TryGetValue(name, out value)) throw new InvalidDataException("备份缺少文件记录");
                    if (value != null && validate) ValidateFile(name, value);
                }
                return bundle;
            }
        }
        private static void ValidateFile(string name, string content)
        {
            if (Encoding.UTF8.GetByteCount(content) > DraftStore.Maximum) throw new InvalidDataException("备份单文件过大");
            if (name == "notes.json") { NotesStore.Parse(content); return; }
            if (name.StartsWith("drafts-", StringComparison.Ordinal)) { DraftStore.Validate(content); return; }
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                LayoutStore.Validate((LayoutState)new DataContractJsonSerializer(typeof(LayoutState)).ReadObject(stream));
        }
        private RecoveryBundle Capture()
        {
            var bundle = new RecoveryBundle();
            foreach (string name in Names) bundle.Files.Add(name, File.Exists(Target(name)) ? AtomicFile.Read(Target(name), DraftStore.Maximum) : null);
            return bundle;
        }
        internal void Export(string output)
        {
            var bundle = Capture();
            foreach (var entry in bundle.Files) if (entry.Value != null) ValidateFile(entry.Key, entry.Value);
            byte[] bytes = Encoding.UTF8.GetBytes(Serialize(bundle));
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        private void Apply(RecoveryBundle bundle)
        {
            foreach (string name in Names)
            {
                string value = bundle.Files[name], target = Target(name);
                if (value == null) { if (File.Exists(target)) File.Delete(target); }
                else AtomicFile.Write(target, value);
            }
        }
        // The journal is flushed before touching live data. Startup rolls back interrupted restores.
        internal bool RecoverInterrupted()
        {
            if (!File.Exists(Journal)) return false;
            Apply(Parse(AtomicFile.Read(Journal, Maximum), false)); File.Delete(Journal); return true;
        }
        internal string Restore(string input)
        {
            if (File.Exists(Journal)) throw new IOException("有未完成的恢复，请先重启应用");
            var incoming = Parse(AtomicFile.Read(input, Maximum), true);
            var previous = Capture(); string rollback = Serialize(previous);
            string history = Path.Combine(directory, "Recovery"); Directory.CreateDirectory(history);
            string safety = Path.Combine(history, "before-restore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
            AtomicFile.Write(safety, rollback); AtomicFile.Write(Journal, rollback);
            try { Apply(incoming); File.Delete(Journal); }
            catch
            {
                // If rollback itself fails the journal stays on disk for the next startup.
                Apply(previous); File.Delete(Journal); throw;
            }
            return safety;
        }
    }
}
