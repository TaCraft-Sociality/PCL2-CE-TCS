using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

/// <summary>多人游戏 → 服务器：列表、每服默认实例（分组折叠选择）、一键启动并进服。</summary>
public partial class PageMultiplayerServers
{
    private List<InstanceGroupItem> _allInstances = new();
    private readonly HashSet<string> _expanded = new();
    private string _openPickerFor = "";

    public PageMultiplayerServers()
    {
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        ModServerList.EnsureLoaded();
        PanList.Children.Clear();
        LabCount.Text = "共 " + ModServerList.Items.Count + " 台服务器";
        LabEmpty.Visibility = ModServerList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var server in ModServerList.Items.ToList()) PanList.Children.Add(BuildRow(server));
    }

    private string InstanceLabel(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "未设置默认实例";
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }

    private FrameworkElement BuildRow(McServerEntry server)
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = server.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        info.Children.Add(new TextBlock
        {
            Text = server.Address + "  ·  默认实例：" + InstanceLabel(server.Instance),
            Opacity = 0.6,
            FontSize = 11.5,
            Margin = new Thickness(0, 3, 0, 0)
        });
        if (!string.IsNullOrWhiteSpace(server.Note))
            info.Children.Add(new TextBlock { Text = server.Note, Opacity = 0.5, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(info, 0);
        head.Children.Add(info);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var launch = new MyButton { Text = "进入服务器", Height = 28, MinWidth = 96, ColorType = MyButton.ColorState.Highlight };
        launch.Click += async (_, _) => await LaunchAsync(server);
        var pick = new MyButton { Text = "默认实例", Height = 28, MinWidth = 84, Margin = new Thickness(8, 0, 0, 0) };
        pick.Click += (_, _) =>
        {
            _openPickerFor = _openPickerFor == server.Id ? "" : server.Id;
            Refresh();
            if (_openPickerFor == server.Id) _ = EnsurePickerAsync();
        };
        var del = new MyButton { Text = "删除", Height = 28, MinWidth = 64, Margin = new Thickness(8, 0, 0, 0) };
        del.Click += (_, _) =>
        {
            if (ModMain.MyMsgBox("删除服务器「" + server.Name + "」？", "删除服务器", "删除", "取消") != 1) return;
            ModServerList.Remove(server);
            Refresh();
        };
        buttons.Children.Add(launch);
        buttons.Children.Add(pick);
        buttons.Children.Add(del);
        Grid.SetColumn(buttons, 1);
        head.Children.Add(buttons);
        panel.Children.Add(head);

        var pickerBorder = new Border
        {
            Name = "Pick_" + server.Id,
            Visibility = _openPickerFor == server.Id ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            MaxHeight = 320
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var pickerPanel = new StackPanel { Name = "PanPick_" + server.Id };
        scroll.Content = pickerPanel;
        pickerBorder.Child = scroll;
        panel.Children.Add(pickerBorder);
        if (_openPickerFor == server.Id) FillPicker(pickerPanel, server);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x08, 0, 0, 0)),
            Child = panel
        };
    }

    private async System.Threading.Tasks.Task EnsurePickerAsync()
    {
        _allInstances = await ModInstancePicker.LoadAsync();
        foreach (var child in PanList.Children.OfType<Border>())
        {
            if (child.Child is not StackPanel sp) continue;
            foreach (var element in sp.Children.OfType<Border>())
            {
                if (element.Name == null || !element.Name.StartsWith("Pick_")) continue;
                var id = element.Name.Substring(5);
                var server = ModServerList.Items.FirstOrDefault(s => s.Id == id);
                if (server == null) continue;
                if (element.Child is ScrollViewer sv && sv.Content is StackPanel pan) FillPicker(pan, server);
            }
        }
    }

    private void FillPicker(StackPanel panel, McServerEntry server)
    {
        ModInstancePicker.Build(panel, _allInstances, _expanded, null, inst =>
        {
            server.Instance = inst.PathInstance;
            ModServerList.Save();
            _openPickerFor = "";
            Refresh();
            HintService.Hint("「" + server.Name + "」默认实例已设为 " + InstanceLabel(inst.PathInstance), HintType.Success);
        });
    }

    private async System.Threading.Tasks.Task LaunchAsync(McServerEntry server)
    {
        if (string.IsNullOrWhiteSpace(server.Instance))
        {
            _openPickerFor = server.Id;
            Refresh();
            await EnsurePickerAsync();
            HintService.Hint("请先选择要进入该服务器的实例", HintType.Info);
            return;
        }
        try
        {
            var instance = new McInstance(server.Instance);
            ModLaunch.McLaunchStart(new ModLaunch.McLaunchOptions { instance = instance, ServerIp = server.Address });
        }
        catch (Exception ex)
        {
            HintService.Hint("启动失败：" + ex.Message, HintType.Error);
        }
    }

    private void BtnAdd_Click(object sender, MouseButtonEventArgs e)
    {
        var name = ModMain.MyMsgBoxInput("添加服务器", "服务器名称（如 朋友的世界）", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var address = ModMain.MyMsgBoxInput("添加服务器", "服务器地址（play.example.com 或 1.2.3.4:25566）", "");
        ModServerList.Add(name.Trim(), ModServerList.NormalizeAddress(address), "");
        Refresh();
    }
}
