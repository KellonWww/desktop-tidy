using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopTidy.Core
{
    internal sealed class ApplicationMatch
    {
        internal readonly Category Category;
        internal readonly string Section;
        internal ApplicationMatch(Category category, string section) { Category = category; Section = section; }
    }
    // Local, deterministic rules. Never run a program or save a shortcut to identify it.
    internal static class ApplicationClassifier
    {
        private sealed class Rule
        {
            internal Category Category;
            internal string Section, Aliases, Hosts;
        }
        private static readonly Rule[] Rules = {
            RuleFor(Category.Entertainment, "音乐", "QQ音乐|QQMusic|网易云音乐|CloudMusic|豆瓣FM|酷我音乐|Kuwo|酷狗音乐|KuGou|Spotify|汽水音乐", "music.163.com|y.qq.com|douban.fm|kuwo.cn|kugou.com|open.spotify.com"),
            RuleFor(Category.Entertainment, "影视", "优酷|Youku|爱奇艺|iQIYI|QiyiClient|腾讯视频|QQLive|芒果TV|MangoTV|哔哩哔哩|Bilibili|VLC|VLC media player|PotPlayer|PotPlayerMini64|PotPlayerMini|Netflix", "youku.com|iqiyi.com|v.qq.com|mgtv.com|bilibili.com|netflix.com"),
            RuleFor(Category.Entertainment, "游戏", "Steam|Epic|Epic Games Launcher|EpicGamesLauncher|4399小游戏|WeGame|Battle.net|战网|GOG Galaxy|Ubisoft Connect|Uplay|EA", "store.steampowered.com|epicgames.com|4399.com"),
            RuleFor(Category.Entertainment, "社交", "微博|Weibo|知乎|百度贴吧|虎扑社区|虎扑", "weibo.com|weibo.cn|zhihu.com|tieba.baidu.com|hupu.com"),
            RuleFor(Category.Chat, "", "微信|WeChat|Weixin|QQ|腾讯QQ|TIM|钉钉|DingTalk|飞书|Feishu|Lark|企业微信|WXWork|Telegram|Telegram Desktop|Discord|Slack|Microsoft Teams|MSTeams|Teams|Skype|WhatsApp", "web.whatsapp.com|web.telegram.org|discord.com|slack.com"),
            RuleFor(Category.Design, "", "Photoshop|Adobe Photoshop|Illustrator|Adobe Illustrator|Figma|Sketch|Blender|AutoCAD|Adobe Premiere Pro|Premiere|Adobe After Effects|AfterFX|剪映|JianyingPro|CapCut|Canva", "figma.com|canva.com"),
            RuleFor(Category.Remote, "", "向日葵|SunloginClient|ToDesk|AnyDesk|TeamViewer|RustDesk|远程桌面连接|mstsc", ""),
            RuleFor(Category.Downloads, "", "迅雷|Thunder|qBittorrent|BitTorrent|uTorrent|Internet Download Manager|IDMan|Free Download Manager|fdm|Motrix|AriaNg", ""),
            RuleFor(Category.Security, "", "Windows Security|Windows安全中心|火绒安全|HuoRong|HipsMain|360安全卫士|360Tray|360杀毒|360sd|腾讯电脑管家|QQPCTray|Malwarebytes|Bitdefender|卡巴斯基|Kaspersky|Avast|ESET", ""),
            RuleFor(Category.Browsers, "", "Google Chrome|Chrome|Microsoft Edge|msedge|Mozilla Firefox|Firefox|Opera|Brave|Vivaldi|Safari|360安全浏览器|360se|360极速浏览器|360chrome|QQ浏览器|QQBrowser", ""),
            RuleFor(Category.Reading, "", "Adobe Acrobat|Acrobat|AcroRd32|Adobe Acrobat Reader|Foxit Reader|福昕阅读器|SumatraPDF|Calibre|Kindle|微信读书|Microsoft Word|WINWORD|Microsoft Excel|EXCEL|Microsoft PowerPoint|POWERPNT|WPS Office|WPS|ET|WPP|Notion|Obsidian|Typora", "weread.qq.com|notion.so"),
            RuleFor(Category.Tools, "", "Visual Studio Code|Code|VSCode|Visual Studio|devenv|IntelliJ IDEA|idea64|PyCharm|pycharm64|WebStorm|webstorm64|Git Bash|Git GUI|Windows Terminal|wt|PowerToys|Everything|Notepad++|notepad++|7Zip|7zFM|WinRAR|Bandizip|Snipaste|ShareX|Postman|Docker Desktop|DockerDesktop|DBeaver|Navicat|DBX|Codex|Claude", "")
        };
        private static readonly Dictionary<string, ApplicationMatch> Names = BuildNames();
        private static Dictionary<string, ApplicationMatch> BuildNames()
        {
            var names = new Dictionary<string, ApplicationMatch>(StringComparer.Ordinal);
            foreach (var rule in Rules)
                foreach (string alias in rule.Aliases.Split(new[] { (char)124 }))
                    names[Normalize(alias)] = new ApplicationMatch(rule.Category, rule.Section);
            return names;
        }
        private static Rule RuleFor(Category category, string section, string aliases, string hosts)
        { return new Rule { Category = category, Section = section, Aliases = aliases, Hosts = hosts }; }
        private static string Normalize(string value)
        {
            value = Regex.Replace(value ?? string.Empty, @"(?:\s*[-_]?\s*(?:快捷方式|shortcut|\((?:x64|x86|64-bit|32-bit)\)))+$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var text = new StringBuilder();
            foreach (char c in value) if (char.IsLetterOrDigit(c)) text.Append(char.ToLowerInvariant(c));
            return text.ToString();
        }
        internal static ApplicationMatch MatchName(string name)
        {
            string value = Normalize(name);
            if (value.Length == 0) return null;
            ApplicationMatch match;
            return Names.TryGetValue(value, out match) ? match : null;
        }
        internal static ApplicationMatch MatchUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
            foreach (var rule in Rules)
                foreach (string host in rule.Hosts.Split(new[] { (char)124 }))
                    if (host.Length > 0 && (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
                        return new ApplicationMatch(rule.Category, rule.Section);
            return null;
        }
        internal static ApplicationMatch Identify(string path, string displayName)
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) return ReadUrl(path);
            var named = MatchName(displayName) ?? MatchName(Path.GetFileNameWithoutExtension(path));
            if (named != null) return named;
            string target = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? ReadShortcut(path) : path;
            if (!IsLocal(target)) return null;
            var targetMatch = MatchName(Path.GetFileNameWithoutExtension(target));
            if (targetMatch != null) return targetMatch;
            if (!Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                if (!File.Exists(target) || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) return null;
                var version = FileVersionInfo.GetVersionInfo(target);
                return MatchName(version.ProductName) ?? MatchName(version.FileDescription);
            }
            catch (Exception ex) { if (!Expected(ex)) throw; return null; }
        }
        private static bool IsLocal(string path)
        { return !string.IsNullOrEmpty(path) && path.Length >= 3 && char.IsLetter(path[0]) && path[1] == (char)58 && (path[2] == (char)92 || path[2] == (char)47); }
        private static bool Expected(Exception ex)
        { return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Security.SecurityException || ex is COMException || ex is TargetInvocationException || ex is System.ComponentModel.Win32Exception; }
        private static ApplicationMatch ReadUrl(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
                bool section = false;
                foreach (string line in File.ReadAllLines(path))
                {
                    string text = line.Trim();
                    if (text.StartsWith("[", StringComparison.Ordinal)) { section = text.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase); continue; }
                    if (section && text.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return MatchUrl(text.Substring(4).Trim());
                }
            }
            catch (Exception ex) { if (!Expected(ex)) throw; }
            return null;
        }
        private static string ReadShortcut(string path)
        {
            object shell = null, shortcut = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 1048576) return null;
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return Convert.ToString(shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null));
            }
            catch (Exception ex) { if (!Expected(ex)) throw; return null; }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }
    }
}
