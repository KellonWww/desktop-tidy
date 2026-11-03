using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;
using DesktopTidy.Windows;
using DesktopTidy.Core;

namespace DesktopTidy
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Report(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { Report(e.ExceptionObject as Exception); };
            try
            {
                if (args.Length > 0 && args[0] == "--restore-desktop-icons") return DesktopIconVisibility.RunGuardian(args);
                if (args.Length == 2 && args[0] == "--probe-shell")
                {
                    var result = DesktopReader.Read();
                    string output = Path.GetFullPath(args[1]);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        new DataContractJsonSerializer(typeof(DesktopReadResult)).WriteObject(stream, result);
                    return result.Success ? 0 : 2;
                }
                bool tray = false, diagnostic = false;
                foreach (string arg in args)
                {
                    if (arg == "--tray") tray = true;
                    else if (arg == "--diagnostic-control") diagnostic = true;
                    else throw new ArgumentException("参数只支持 --tray、--diagnostic-control 或 --probe-shell <新报告路径>。");
                }
                bool created;
                using (var mutex = new Mutex(true, "Local\\DesktopTidy.Prototype.C6E185EF", out created))
                {
                    if (!created) return 3;
                    try
                    {
                        if (new RecoveryStore(Diagnostics.DataDirectory).RecoverInterrupted())
                            MessageBox.Show("检测到上次恢复未完成，已回滚到恢复前数据。", "桌面整理");
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        using (var context = new TrayApplicationContext(tray, diagnostic)) Application.Run(context);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception ex) { Report(ex); return 1; }
        }

        internal static void Report(Exception error) { Diagnostics.Log(error); }
    }
}
