using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace DesktopTidy.Core
{
    internal static class DraftStore
    {
        internal const int Maximum = 2 * 1024 * 1024;
        internal static string FileName(bool edge) { return "drafts-" + (edge ? "edge" : "workspace") + ".json"; }
        internal static void Validate(string content)
        {
            if (content == null || content.Length > Maximum) throw new InvalidDataException("草稿过大");
            var json = new JavaScriptSerializer { MaxJsonLength = Maximum };
            var bundle = json.Deserialize<Dictionary<string, string>>(content);
            if (bundle == null || bundle.Count > 2) throw new InvalidDataException("草稿格式无效");
            foreach (var entry in bundle) ValidateValue(entry.Key, entry.Value);
        }
        private static void ValidateValue(string name, string content)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = Maximum };
            var value = json.Deserialize<Dictionary<string, object>>(content);
            if (value == null) throw new InvalidDataException("草稿为空");
            if (name == "quick")
            {
                Text(value, "text", 2000); Boolean(value, "todo"); return;
            }
            if (name != "drafts" || value.Count > 201) throw new InvalidDataException("草稿数量或类型无效");
            foreach (var entry in value)
            {
                Guid id;
                if (entry.Key != "new" && !Guid.TryParseExact(entry.Key, "N", out id)) throw new InvalidDataException("草稿标识无效");
                var draft = entry.Value as Dictionary<string, object>;
                if (draft == null) throw new InvalidDataException("编辑草稿无效");
                Text(draft, "title", 80); Text(draft, "text", 2000); Text(draft, "revision", 128); Boolean(draft, "todo");
            }
        }
        private static void Text(Dictionary<string, object> data, string key, int limit)
        {
            object value;
            if (!data.TryGetValue(key, out value) || !(value is string) || ((string)value).Length > limit) throw new InvalidDataException("草稿字段无效：" + key);
        }
        private static void Boolean(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || !(value is bool)) throw new InvalidDataException("草稿选项无效");
        }
        internal static string Bootstrap(string directory, bool edge)
        {
            string file = Path.Combine(directory, FileName(edge));
            if (!File.Exists(file)) return "";
            string content = AtomicFile.Read(file, Maximum); Validate(content);
            var json = new JavaScriptSerializer { MaxJsonLength = Maximum };
            // The durable mirror is authoritative after an explicit backup restore.
            return "(() => { const data = " + content + "; for (const name of ['drafts','quick']) { const key = " +
                json.Serialize("desktop-tidy.notes." + (edge ? "edge" : "workspace") + ".") +
                " + name; if (data[name] !== undefined) localStorage.setItem(key, data[name]); else localStorage.removeItem(key); } })();";
        }
        internal static void Save(string directory, bool edge, string name, string value)
        {
            ValidateValue(name, value);
            string file = Path.Combine(directory, FileName(edge));
            var json = new JavaScriptSerializer { MaxJsonLength = Maximum };
            var bundle = new Dictionary<string, string>();
            if (File.Exists(file))
            {
                string content = AtomicFile.Read(file, Maximum); Validate(content);
                bundle = json.Deserialize<Dictionary<string, string>>(content);
            }
            bundle[name] = value;
            string next = json.Serialize(bundle); Validate(next); AtomicFile.Write(file, next);
        }
    }
}
