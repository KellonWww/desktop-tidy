using System;
using System.IO;
using System.Text;

namespace DesktopTidy.Core
{
    internal static class AtomicFile
    {
        internal static void Write(string target, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static string Read(string target, int maximum)
        {
            using (var stream = File.OpenRead(target))
            {
                if (stream.Length > maximum) throw new InvalidDataException("数据文件超过大小限制");
                using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
            }
        }
    }
}
