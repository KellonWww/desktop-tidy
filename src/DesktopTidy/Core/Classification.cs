using System;
using System.Collections.Generic;
using System.IO;
using DesktopTidy.Windows;

namespace DesktopTidy.Core
{
    internal enum Category { Documents, Images, Videos, Installers, Archives, Shortcuts, Folders, Other, Custom1, Custom2, Custom3, Custom4, Custom5, Custom6, Custom7, Custom8, Custom9, Custom10, Custom11, Custom12, Custom13, Custom14, Custom15, Custom16, Entertainment, Chat, Design, Remote, Downloads, Security, Tools, Browsers, Reading }
    internal sealed class WorkspaceItem
    {
        internal string Identity, Name, Path;
        internal bool IsDirectory;
        internal Category Category, AutomaticCategory;
        internal string Section;
    }
    internal static class Classification
    {
        internal static string Title(Category category)
        {
            string[] titles = { "文档", "图片", "视频", "程序", "压缩包", "程序", "文件夹", "其他" };
            if (category >= Category.Entertainment && category <= Category.Reading)
                return new[] { "娱乐", "聊天", "设计", "远程", "下载", "安全", "工具", "浏览器", "文档/阅读" }[(int)category - (int)Category.Entertainment];
            int index = (int)category; return index >= 0 && index < titles.Length ? titles[index] : "自定义分类";
        }
        internal static bool IsCustom(Category category) { return category >= Category.Custom1 && category <= Category.Custom16; }
        internal static Category ResolveCategory(Category category, LayoutState layout)
        {
            if (category == Category.Installers) category = Category.Shortcuts;
            if (layout.Groups.Count == 0 && layout.DeletedCategories.Count == 0) return category;
            if (!layout.DeletedCategories.Contains(category) && layout.Groups.Exists(delegate(GroupState group) { return group.Category == category; })) return category;
            bool application = category == Category.Shortcuts || category >= Category.Entertainment;
            Category[] fallbacks = application ? new[] { Category.Shortcuts, Category.Other } : new[] { Category.Other, Category.Shortcuts };
            foreach (Category fallback in fallbacks)
                if (!layout.DeletedCategories.Contains(fallback) && layout.Groups.Exists(delegate(GroupState group) { return group.Category == fallback; })) return fallback;
            return layout.Groups.Count > 0 ? layout.Groups[0].Category : Category.Other;
        }
        private static bool Has(string values, string extension) { return values.IndexOf("|" + extension + "|", StringComparison.OrdinalIgnoreCase) >= 0; }
        internal static Category Classify(string path, bool directory) { return Classify(path, directory, null); }
        internal static Category Classify(string path, bool directory, string displayName)
        {
            string section; return Classify(path, directory, displayName, out section);
        }
        private static Category Classify(string path, bool directory, string displayName, out string section)
        {
            section = string.Empty;
            if (directory) return Category.Folders;
            string extension = System.IO.Path.GetExtension(path);
            if (Has("|.doc|.docx|.xls|.xlsx|.ppt|.pptx|.pdf|.txt|.md|.csv|.rtf|.odt|", extension)) return Category.Documents;
            if (Has("|.png|.jpg|.jpeg|.gif|.bmp|.webp|.svg|.heic|.tif|.tiff|", extension)) return Category.Images;
            if (Has("|.mp4|.mkv|.mov|.avi|.wmv|.webm|.m4v|", extension)) return Category.Videos;
            if (Has("|.msi|.msix|.appx|.msixbundle|", extension)) return Category.Shortcuts;
            if (Has("|.zip|.rar|.7z|.tar|.gz|.bz2|.xz|", extension)) return Category.Archives;
            if (Has("|.lnk|.url|.exe|.com|", extension))
            {
                var match = ApplicationClassifier.Identify(path, displayName);
                if (match != null) section = match.Section;
                return match == null ? Category.Shortcuts : match.Category;
            }
            return Category.Other;
        }
        internal static List<WorkspaceItem> Build(IList<DesktopIcon> icons, LayoutState layout, out int skipped)
        {
            skipped = 0;
            var result = new List<WorkspaceItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assignments = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in layout.Assignments) assignments[assignment.Identity] = assignment.Category;
            foreach (var icon in icons)
            {
                try
                {
                    string path = icon.ParsingName;
                    if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path) || path.StartsWith("\\", StringComparison.Ordinal)) { skipped++; continue; }
                    path = System.IO.Path.GetFullPath(path);
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0) { skipped++; continue; }
                    if (!seen.Add(path)) continue;
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    string section;
                    Category automatic = Classify(path, directory, icon.Name, out section);
                    Category category = automatic;
                    Category assigned;
                    if (assignments.TryGetValue(path, out assigned)) category = assigned;
                    category = ResolveCategory(category, layout);
                    result.Add(new WorkspaceItem { Identity = path, Path = path, Name = icon.Name, IsDirectory = directory, Category = category, AutomaticCategory = automatic, Section = section });
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
                catch (ArgumentException) { skipped++; }
                catch (System.Security.SecurityException) { skipped++; }
            }
            result.Sort(delegate(WorkspaceItem left, WorkspaceItem right) { return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name); });
            return result;
        }
    }
}
