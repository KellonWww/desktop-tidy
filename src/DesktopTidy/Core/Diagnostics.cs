using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace DesktopTidy.Core
{
    internal static class Diagnostics
    {
        private static readonly object Sync = new object();
        internal static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTidy"); } }
        internal static void Log(Exception error)
        {
            if (error == null) return;
            try
            {
                lock (Sync)
                {
                    string file = Path.Combine(DataDirectory, "Prototype", "error.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    if (File.Exists(file) && new FileInfo(file).Length > 262144)
                    {
                        string previous = file + ".1"; if (File.Exists(previous)) File.Delete(previous); File.Move(file, previous);
                    }
                    File.AppendAllText(file, DateTime.UtcNow.ToString("o") + " " + error + Environment.NewLine);
                }
            }
            catch (Exception failure) { Trace.WriteLine(failure); Trace.WriteLine(error); }
        }
        internal static void Export(string output)
        {
            // Export an explicit allowlist, not raw logs: paths, exception messages and notes stay local.
            var text = new StringBuilder();
            text.AppendLine("Desktop Tidy diagnostics v1 (no notes, paths or raw logs)");
            text.AppendLine("UTC: " + DateTime.UtcNow.ToString("o"));
            text.AppendLine("Build: " + typeof(Diagnostics).Assembly.GetName().Version);
            text.AppendLine("OS: " + Environment.OSVersion.Version);
            text.AppendLine("CLR: " + Environment.Version + "; 64-bit: " + Environment.Is64BitProcess);
            using (var process = Process.GetCurrentProcess())
            {
                text.AppendLine("Process uptime seconds: " + (DateTime.Now - process.StartTime).TotalSeconds.ToString("F0"));
                text.AppendLine("Host private MiB: " + (process.PrivateMemorySize64 / 1048576.0).ToString("F1"));
                text.AppendLine("Host CPU seconds: " + process.TotalProcessorTime.TotalSeconds.ToString("F2"));
                text.AppendLine("Host handles: " + process.HandleCount);
            }
            text.AppendLine("Memory and CPU above exclude WebView2 child processes.");
            try { text.AppendLine("WebView2: " + CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch (Exception error) { text.AppendLine("WebView2 unavailable: " + error.GetType().Name); }
            foreach (var screen in Screen.AllScreens)
                text.AppendLine("Display: " + screen.Bounds.Width + "x" + screen.Bounds.Height + "; primary: " + screen.Primary);
            byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
    }
}
