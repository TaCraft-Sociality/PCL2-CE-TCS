using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;

namespace PCL;

/// <summary>一个实例条目（带所属实例文件夹）。</summary>
public class InstanceGroupItem
{
    public string Folder = "";
    public string Name = "";
    public McInstance Instance;
}

/// <summary>按实例文件夹分组、可折叠的实例选择面板（就地展开，不用弹窗）。</summary>
public static class ModInstancePicker
{
    /// <summary>枚举实例：文件夹列表取自启动器自带的 ModFolder.mcFolderList。</summary>
    public static async System.Threading.Tasks.Task<List<InstanceGroupItem>> LoadAsync()
    {
        var result = new List<InstanceGroupItem>();
        try
        {
            if (ModFolder.mcFolderList.Count == 0)
            {
                try
                {
                    ModFolder.mcFolderListLoader.Start();
                    for (int i = 0; i < 60 && ModFolder.mcFolderList.Count == 0; i++)
                        await System.Threading.Tasks.Task.Delay(100);
                }
                catch { }
            }

            var roots = new List<string>();
            foreach (var folder in ModFolder.mcFolderList)
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.Location)) continue;
                var loc = folder.Location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (loc.Length == 0) continue;
                if (roots.Any(r => string.Equals(r, loc, StringComparison.OrdinalIgnoreCase))) continue;
                roots.Add(loc);
            }
            if (!string.IsNullOrWhiteSpace(ModFolder.mcFolderSelected))
            {
                var sel = ModFolder.mcFolderSelected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (sel.Length > 0 && !roots.Any(r => string.Equals(r, sel, StringComparison.OrdinalIgnoreCase)))
                    roots.Add(sel);
            }

            foreach (var root in roots)
            {
                var versionsDir = Path.Combine(root, "versions");
                if (!Directory.Exists(versionsDir)) continue;
                var folderName = Path.GetFileName(root);
                if (string.IsNullOrWhiteSpace(folderName)) folderName = root;
                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    try
                    {
                        var inst = new McInstance(dir);
                        if (result.Any(x => string.Equals(x.Instance.PathIndie, inst.PathIndie, StringComparison.OrdinalIgnoreCase))) continue;
                        result.Add(new InstanceGroupItem { Folder = folderName, Name = name, Instance = inst });
                    }
                    catch { }
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>把分组面板填进 StackPanel：文件夹可折叠（默认折叠），点实例回调。</summary>
    public static void Build(StackPanel panel, List<InstanceGroupItem> items, HashSet<string> expanded,
                             McInstance current, Action<McInstance> onPick)
    {
        if (panel == null) return;
        panel.Children.Clear();
        var groups = new List<string>();
        foreach (var item in items) if (!groups.Contains(item.Folder)) groups.Add(item.Folder);
        foreach (var folder in groups)
        {
            var isOpen = expanded.Contains(folder);
            var count = items.Count(x => x.Folder == folder);
            var header = new Border
            {
                Margin = new Thickness(0, 2, 0, 1),
                Padding = new Thickness(8, 7, 8, 7),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x0A, 0, 0, 0)),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = (isOpen ? "▾  " : "▸  ") + folder + "（" + count + "）",
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold
                }
            };
            var key = folder;
            header.MouseLeftButtonUp += (_, _) =>
            {
                if (!expanded.Add(key)) expanded.Remove(key);
                Build(panel, items, expanded, current, onPick);
            };
            panel.Children.Add(header);
            if (!isOpen) continue;
            foreach (var item in items.Where(x => x.Folder == folder))
            {
                var isCurrent = current != null && string.Equals(current.PathIndie, item.Instance.PathIndie, StringComparison.OrdinalIgnoreCase);
                var row = new Border
                {
                    Margin = new Thickness(14, 1, 0, 1),
                    Padding = new Thickness(8, 6, 8, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = isCurrent ? new SolidColorBrush(Color.FromArgb(0x18, 0x17, 0x61, 0xB4)) : Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = item.Name,
                        FontSize = 12.5,
                        FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal
                    }
                };
                var picked = item.Instance;
                row.MouseLeftButtonUp += (_, _) => onPick?.Invoke(picked);
                panel.Children.Add(row);
            }
        }
        if (items.Count == 0)
            panel.Children.Add(new TextBlock { Text = "没有找到实例", FontSize = 12, Opacity = 0.5, Margin = new Thickness(6, 6, 0, 6) });
    }
}
