using System;
using System.IO;
using System.Text;
using System.Threading;

namespace DesktopTidy.Core
{
    internal static class AtomicFile
    {
        // Commit a flushed snapshot without exposing partial content or discarding the last good file.
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
                // Readers and virus scanners may briefly deny replacement on Windows. Retry only
                // transient sharing/replacement errors, never delete the last good file as a fallback.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                        break;
                    }
                    catch (IOException error)
                    {
                        int code = error.HResult & 0xffff;
                        if (attempt >= 3 || (code != 32 && code != 33 && code != 1175 && code != 1176 && code != 1224)) throw;
                        Thread.Sleep(25 << attempt);
                    }
                }
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
