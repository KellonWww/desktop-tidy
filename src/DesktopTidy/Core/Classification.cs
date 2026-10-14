using System;
using System.Collections.Generic;
using System.IO;
using DesktopTidy.Windows;

namespace DesktopTidy.Core
{
    internal enum Category { Documents, Images, Videos, Installers, Archives, Shortcuts, Folders, Other, Custom1, Custom2, Custom3, Custom4, Custom5, Custom6, Custom7, Custom8, Custom9, Custom10, Custom11, Custom12, Custom13, Custom14, Custom15, Custom16 }
    internal sealed class WorkspaceItem
    {
        internal string Identity, Name, Path;
        internal bool IsDirectory;
        internal Category Category;
    }
    internal static class Classification
    {
        internal static string Title(Category category)
        {
            string[] titles = { "文档", "图片", "视频", "安装包", "压缩包", "程序与快捷方式", "文件夹", "其他" };
            int index = (int)category; return index >= 0 && index < titles.Length ? titles[index] : "自定义分类";
        }
        private static bool Has(string values, string extension) { return values.IndexOf("|" + extension + "|", StringComparison.OrdinalIgnoreCase) >= 0; }
        internal static Category Classify(string path, bool directory)
        {
            if (directory) return Category.Folders;
            string extension = System.IO.Path.GetExtension(path);
            if (Has("|.doc|.docx|.xls|.xlsx|.ppt|.pptx|.pdf|.txt|.md|.csv|.rtf|.odt|", extension)) return Category.Documents;
            if (Has("|.png|.jpg|.jpeg|.gif|.bmp|.webp|.svg|.heic|.tif|.tiff|", extension)) return Category.Images;
            if (Has("|.mp4|.mkv|.mov|.avi|.wmv|.webm|.m4v|", extension)) return Category.Videos;
            if (Has("|.msi|.msix|.appx|.msixbundle|", extension)) return Category.Installers;
            if (Has("|.zip|.rar|.7z|.tar|.gz|.bz2|.xz|", extension)) return Category.Archives;
            if (Has("|.lnk|.url|.exe|.com|", extension)) return Category.Shortcuts;
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
                    Category category = Classify(path, (attributes & FileAttributes.Directory) != 0);
                    Category assigned;
                    if (assignments.TryGetValue(path, out assigned)) category = assigned;
                    result.Add(new WorkspaceItem { Identity = path, Path = path, Name = icon.Name, IsDirectory = (attributes & FileAttributes.Directory) != 0, Category = category });
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
