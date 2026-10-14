using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Security.Cryptography;

namespace DesktopTidy.MemoryProbe
{
    [DataContract] internal sealed class Phase
    {
        [DataMember] public string Name;
        [DataMember] public int Round, Samples;
        [DataMember] public long MaxSampleGapMilliseconds;
        [DataMember] public long MaxPrivateBytes, MaxWorkingSetBytes, FirstPrivateBytes, LastPrivateBytes;
        [DataMember] public uint FirstGdi, LastGdi, MaxGdi, FirstUser, LastUser, MaxUser;
        [DataMember] public double CpuPercent;
    }
    [DataContract] internal sealed class Summary
    {
        [DataMember] public string StartedAt, Os, Clr, Scope, TargetSha256;
        [DataMember] public uint SystemDpi;
        [DataMember] public int WarmupSeconds, PhaseSeconds, Cycles, Rounds;
        [DataMember] public bool AllTargetsExited;
        [DataMember] public List<Phase> Phases = new List<Phase>();
    }
    internal static class Program
    {
        private const int CommandMessage = 0x8000 + 44;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--check-demand-exit") return CheckDemandExit(Path.GetFullPath(args[1]));
                if (args.Length != 6) throw new ArgumentException("用法：MemoryProbe.exe <DesktopTidy.exe> <新输出目录> <每阶段秒数> <预热秒数> <窗口循环次数> <轮数>");
                string exe = Path.GetFullPath(args[0]), directory = Path.GetFullPath(args[1]);
                int seconds = Number(args[2], 1, 3600), warmup = Number(args[3], 0, 600), cycles = Number(args[4], 1, 100), rounds = Number(args[5], 1, 10);
                if (!File.Exists(exe)) throw new FileNotFoundException("未找到原型 exe。", exe);
                if (Directory.Exists(directory)) throw new IOException("输出目录已存在，不覆盖测量证据。");
                Directory.CreateDirectory(directory);
                var summary = new Summary { StartedAt = DateTimeOffset.Now.ToString("o"), Os = Environment.OSVersion.ToString(), Clr = Environment.Version.ToString(),
                    Scope = "原型测量；当前桌面项数量、DPI 与硬件需另记；无组件，非完整产品验收。系统 Working Set 汇总可能包含共享页，GPU 尚未采集。",
                    WarmupSeconds = warmup, PhaseSeconds = seconds, Cycles = cycles, Rounds = rounds, AllTargetsExited = true, SystemDpi = GetDpiForSystem(), TargetSha256 = Hash(exe) };
                using (var csv = new StreamWriter(Path.Combine(directory, "samples.csv")))
                {
                    csv.WriteLine("utc,round,phase,private_bytes,working_set_bytes,gdi,user,explorer_private,dwm_private,explorer_working_set,dwm_working_set");
                    for (int round = 1; round <= rounds; round++)
                    {
                        using (var process = Process.Start(new ProcessStartInfo(exe, "--tray --diagnostic-control") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) }))
                        {
                            IntPtr window = IntPtr.Zero;
                            try
                            {
                                var ready = Stopwatch.StartNew();
                                while (window == IntPtr.Zero && ready.ElapsedMilliseconds < 10000)
                                {
                                    if (process.HasExited) throw new InvalidOperationException("原型提前退出，可能已有实例运行。退出码：" + process.ExitCode);
                                    window = FindWindow(null, "DesktopTidy.Prototype." + process.Id);
                                    Thread.Sleep(50);
                                }
                                if (window == IntPtr.Zero) throw new TimeoutException("诊断消息窗口未就绪。");
                                Console.WriteLine("第 " + round + " 轮：预热 " + warmup + " 秒。");
                                Thread.Sleep(warmup * 1000);
                                summary.Phases.Add(Sample(process, csv, round, "tray", seconds));
                                Command(window, 1); Thread.Sleep(2000);
                                summary.Phases.Add(Sample(process, csv, round, "main-open", seconds));
                                Command(window, 2); Thread.Sleep(2000);
                                summary.Phases.Add(Sample(process, csv, round, "main-closed-baseline", seconds));
                                for (int i = 0; i < cycles; i++) { Command(window, 1); Thread.Sleep(100); Command(window, 2); Thread.Sleep(100); }
                                Thread.Sleep(2000);
                                summary.Phases.Add(Sample(process, csv, round, "after-window-cycles", seconds));
                                Command(window, 4);
                                summary.Phases.Add(Sample(process, csv, round, "read-only-scan", seconds));
                            }
                            finally
                            {
                                if (!process.HasExited && window != IntPtr.Zero)
                                {
                                    try { Command(window, 3); }
                                    catch (TimeoutException) { summary.AllTargetsExited = false; }
                                }
                                if (!process.WaitForExit(10000))
                                {
                                    summary.AllTargetsExited = false;
                                    // 诊断子进程只读，无文件写任务；仅终止本工具创建的目标。
                                    process.Kill(); process.WaitForExit(5000);
                                }
                            }
                        }
                    }
                }
                using (var file = File.Create(Path.Combine(directory, "summary.json"))) new DataContractJsonSerializer(typeof(Summary)).WriteObject(file, summary);
                foreach (var phase in summary.Phases)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "round={0} {1}: max private={2:F2} MiB, WS={3:F2} MiB, GDI={4}->{5}, USER={6}->{7}, CPU={8:F3}%",
                        phase.Round, phase.Name, phase.MaxPrivateBytes / 1048576.0, phase.MaxWorkingSetBytes / 1048576.0, phase.FirstGdi, phase.LastGdi, phase.FirstUser, phase.LastUser, phase.CpuPercent));
                return summary.AllTargetsExited ? 0 : 2;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static int CheckDemandExit(string exe)
        {
            using (var process = Process.Start(new ProcessStartInfo(exe, "--diagnostic-control") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) }))
            {
                try
                {
                    var clock = Stopwatch.StartNew();
                    IntPtr window = IntPtr.Zero;
                    while (window == IntPtr.Zero && clock.ElapsedMilliseconds < 10000)
                    {
                        if (process.HasExited) throw new InvalidOperationException("按需原型提前退出，已有实例运行或启动失败。");
                        window = FindWindow(null, "DesktopTidy.Prototype." + process.Id);
                        Thread.Sleep(50);
                    }
                    if (window == IntPtr.Zero) throw new TimeoutException("未找到按需原型消息窗口。");
                    Thread.Sleep(500);
                    Command(window, 2);
                    if (!process.WaitForExit(10000)) throw new TimeoutException("关闭主窗后未按需退出。");
                    if (process.ExitCode != 0) throw new InvalidOperationException("退出码不为零：" + process.ExitCode);
                    Console.WriteLine("PASS: on-demand window close exited its process (code 0).");
                    return 0;
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
            }
        }
        private static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var file = File.OpenRead(path)) return BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        private static int Number(string text, int min, int max)
        {
            int value = int.Parse(text, CultureInfo.InvariantCulture);
            if (value < min || value > max) throw new ArgumentOutOfRangeException("参数", "超出允许范围。");
            return value;
        }
        private static void Command(IntPtr hwnd, int command)
        {
            IntPtr result;
            if (SendMessageTimeout(hwnd, CommandMessage, new IntPtr(command), IntPtr.Zero, 2, 10000, out result) == IntPtr.Zero)
                throw new TimeoutException("原型命令未响应：" + command);
        }
        private static Phase Sample(Process process, StreamWriter csv, int round, string name, int seconds)
        {
            var phase = new Phase { Round = round, Name = name };
            var clock = Stopwatch.StartNew();
            process.Refresh();
            double initialCpu = process.TotalProcessorTime.TotalMilliseconds;
            long previousSample = 0, nextSample = 100;
            while (clock.ElapsedMilliseconds < seconds * 1000L)
            {
                if (process.HasExited) throw new InvalidOperationException("采样时原型退出。");
                long now = clock.ElapsedMilliseconds;
                if (phase.Samples > 0) phase.MaxSampleGapMilliseconds = Math.Max(phase.MaxSampleGapMilliseconds, now - previousSample);
                previousSample = now;
                process.Refresh();
                long privateBytes = process.PrivateMemorySize64, workingSet = process.WorkingSet64;
                uint gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1);
                if (phase.Samples == 0) { phase.FirstPrivateBytes = privateBytes; phase.FirstGdi = gdi; phase.FirstUser = user; }
                phase.Samples++; phase.LastPrivateBytes = privateBytes; phase.LastGdi = gdi; phase.LastUser = user;
                phase.MaxPrivateBytes = Math.Max(phase.MaxPrivateBytes, privateBytes); phase.MaxWorkingSetBytes = Math.Max(phase.MaxWorkingSetBytes, workingSet);
                phase.MaxGdi = Math.Max(phase.MaxGdi, gdi); phase.MaxUser = Math.Max(phase.MaxUser, user);
                long[] explorer = SystemMemory("explorer"), dwm = SystemMemory("dwm");
                csv.WriteLine(string.Join(",", new[] { DateTimeOffset.UtcNow.ToString("o"), round.ToString(CultureInfo.InvariantCulture), name,
                    privateBytes.ToString(CultureInfo.InvariantCulture), workingSet.ToString(CultureInfo.InvariantCulture), gdi.ToString(CultureInfo.InvariantCulture), user.ToString(CultureInfo.InvariantCulture),
                    explorer[0].ToString(CultureInfo.InvariantCulture), dwm[0].ToString(CultureInfo.InvariantCulture), explorer[1].ToString(CultureInfo.InvariantCulture), dwm[1].ToString(CultureInfo.InvariantCulture) }));
                Thread.Sleep((int)Math.Max(0, nextSample - clock.ElapsedMilliseconds));
                nextSample += 100;
            }
            process.Refresh();
            phase.CpuPercent = (process.TotalProcessorTime.TotalMilliseconds - initialCpu) / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100.0;
            csv.Flush();
            return phase;
        }
        private static long[] SystemMemory(string name)
        {
            long privateBytes = 0, workingSet = 0;
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try { process.Refresh(); privateBytes += process.PrivateMemorySize64; workingSet += process.WorkingSet64; }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            return new[] { privateBytes, workingSet };
        }
    }
}
