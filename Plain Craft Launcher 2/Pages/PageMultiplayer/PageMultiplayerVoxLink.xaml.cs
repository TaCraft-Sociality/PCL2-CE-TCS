using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

/// <summary>多人游戏 → 联机房间 → VoxLink。</summary>
public partial class PageMultiplayerVoxLink
{
    private readonly StringBuilder _log = new();
    private readonly List<McInstance> _instances = new();
    private readonly List<string> _labels = new();
    private readonly List<string> _folders = new();
    private readonly HashSet<string> _expanded = new();
    private List<VoxRoom> _rooms = new();
    private McInstance _selected;
    private bool _lobbyLoaded;

    private static bool TcsDark()
    {
        if (System.Windows.Application.Current.TryFindResource("ColorBrush1") is SolidColorBrush fg)
        {
            var c = fg.Color;
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128;
        }
        return false;
    }
    private static Brush TcsCard() => new SolidColorBrush(TcsDark() ? Color.FromRgb(0x2B, 0x2C, 0x30) : Colors.White);
    private static Brush TcsText1() => new SolidColorBrush(TcsDark() ? Colors.White : Color.FromRgb(0x1F, 0x1F, 0x1F));
    private static Brush TcsText2() => new SolidColorBrush(TcsDark() ? Color.FromRgb(0xB0, 0xB6, 0xBF) : Color.FromRgb(0x66, 0x66, 0x66));
    private static Brush TcsText3() => new SolidColorBrush(TcsDark() ? Color.FromRgb(0x8A, 0x90, 0x99) : Color.FromRgb(0x99, 0x99, 0x99));
    public PageMultiplayerVoxLink()
    {
        InitializeComponent();
            Loaded += (_, _) => { ModTcsTheme.ApplyIconTiles(this); };
            // TCS：切换主题后重建大厅卡片，使卡片配色跟随主题
            PCL.Core.UI.Theme.ThemeService.ColorModeChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { try { ModTcsTheme.ApplyIconTiles(this); BtnRefreshLobby_Click(null, null); } catch { } }));
        LoadInstances();
        UpdateState();
        Loaded += async (_, _) =>
        {
            if (_lobbyLoaded) return;
            _lobbyLoaded = true;
            await RefreshLobbyAsync();
        };
    }

    private async void LoadInstances()
    {
        _instances.Clear();
        _labels.Clear();
        _folders.Clear();
        var items = await ModInstancePicker.LoadAsync();
        foreach (var item in items)
        {
            _instances.Add(item.Instance);
            _labels.Add(item.Name);
            _folders.Add(item.Folder);
        }
        AddLog("发现实例 " + _instances.Count + " 个（" + _folders.Distinct().Count() + " 个实例文件夹）");
        if (_instances.Count > 0)
        {
            _selected = _instances[0];
            LabInstance.Text = _labels[0];
        }
        else
        {
            AddLog("没有可用实例，请先在启动页创建实例");
        }
        BuildPicker();
    }

    private McInstance Selected() => _selected;

    private void BorderInstance_Click(object sender, MouseButtonEventArgs e)
    {
        BuildPicker();
        BorderPicker.Visibility = BorderPicker.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BuildPicker()
    {
        if (PanInstancePicker == null) return;
        PanInstancePicker.Children.Clear();
        var groups = new List<string>();
        foreach (var folder in _folders) if (!groups.Contains(folder)) groups.Add(folder);
        foreach (var folder in groups)
        {
            var isOpen = _expanded.Contains(folder);
            var count = _folders.Count(f => f == folder);
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
            var folderKey = folder;
            header.MouseLeftButtonDown += (_, _) =>
            {
                if (!_expanded.Add(folderKey)) _expanded.Remove(folderKey);
                BuildPicker();
            };
            PanInstancePicker.Children.Add(header);
            if (!isOpen) continue;
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_folders[i] != folder) continue;
                var inst = _instances[i];
                var label = _labels[i];
                var isCurrent = ReferenceEquals(inst, _selected);
                var row = new Border
                {
                    Margin = new Thickness(14, 1, 0, 1),
                    Padding = new Thickness(8, 6, 8, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = isCurrent ? new SolidColorBrush(Color.FromArgb(0x18, 0x17, 0x61, 0xB4)) : Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = label,
                        FontSize = 12.5,
                        FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal
                    }
                };
                var target = inst;
                var targetLabel = label;
                row.MouseLeftButtonDown += (_, _) =>
                {
                    _selected = target;
                    LabInstance.Text = targetLabel;
                    BorderPicker.Visibility = Visibility.Collapsed;
                    UpdateState();
                };
                PanInstancePicker.Children.Add(row);
            }
        }
    }

    private void AddLog(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => AddLog(text)));
            return;
        }
        _log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
        if (_log.Length > 8000) _log.Remove(0, _log.Length - 8000);
        TextLog.Text = _log.ToString();
        TextLog.ScrollToEnd();
    }

    private void UpdateState()
    {
        var inst = Selected();
        if (inst == null) { LabModState.Text = "请选择实例"; return; }
        var installed = ModVoxLink.IsInstalled(inst);
        LabModState.Text = installed ? "已安装 VoxLink 模组" : "尚未安装模组";
        BtnInstall.Text = installed ? "校验并重装模组" : "安装 VoxLink 模组";
    }

    private async void BtnInstall_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = Selected();
        if (inst == null) { HintService.Hint("请先选择游戏实例", HintType.Error); return; }
        BtnInstall.IsEnabled = false;
        var result = await ModVoxLink.InstallAsync(inst, AddLog);
        AddLog(result.message);
        HintService.Hint(result.message, result.ok ? HintType.Success : HintType.Error);
        BtnInstall.IsEnabled = true;
        UpdateState();
    }

    private async void BtnCreate_Click(object sender, MouseButtonEventArgs e)
    {
        var inst = Selected();
        if (inst == null) { HintService.Hint("请先选择游戏实例", HintType.Error); return; }
        var result = await ModVoxLink.InstallAsync(inst, AddLog);
        if (!result.ok) { AddLog(result.message); HintService.Hint(result.message, HintType.Error); return; }
        AddLog("启动游戏：" + inst.Name);
        ModLaunch.McLaunchStart(new ModLaunch.McLaunchOptions { instance = inst });
        HintService.Hint("进入世界 → 对局域网开放，模组会给 6 位房间码", HintType.Success);
    }

    private async void BtnJoin_Click(object sender, MouseButtonEventArgs e)
    {
        var code = (TextCode.Text ?? "").Trim();
        if (code.Length == 0) { HintService.Hint("请填写好友的 6 位房间码", HintType.Error); return; }
        await JoinByCodeAsync(code);
    }

    private async System.Threading.Tasks.Task JoinByCodeAsync(string code)
    {
        var inst = Selected();
        if (inst == null) { HintService.Hint("请先选择游戏实例", HintType.Error); return; }
        var result = await ModVoxLink.InstallAsync(inst, AddLog);
        if (!result.ok) { AddLog(result.message); HintService.Hint(result.message, HintType.Error); return; }
        TextCode.Text = code;
        AddLog("启动游戏准备加入：房间码 " + code);
        ModLaunch.McLaunchStart(new ModLaunch.McLaunchOptions { instance = inst });
        HintService.Hint("游戏启动后在模组界面填入房间码 " + code, HintType.Success);
    }

    private async void BtnRefreshLobby_Click(object sender, MouseButtonEventArgs e) => await RefreshLobbyAsync();

    private async System.Threading.Tasks.Task RefreshLobbyAsync()
    {
        BtnRefreshLobby.IsEnabled = false;
        LabLobbyTip.Text = "正在从 VoxLink 中继拉取公共大厅…";
        try
        {
            var result = await ModVoxLobby.FetchAsync(1, 20);
            if (!result.ok)
            {
                _rooms = new List<VoxRoom>();
                PanLobby.Children.Clear();
                LabLobbyTip.Text = result.message;
                return;
            }
            _rooms = result.rooms ?? new List<VoxRoom>();
            LabLobbyTip.Text = result.message + "（与官网 p2p.wuhui.icu 同源，实时刷新）";
            AddLog("大厅：" + result.message);
            RenderLobby();
        }
        finally
        {
            BtnRefreshLobby.IsEnabled = true;
        }
    }

    private void TextSearch_TextChanged(object sender, TextChangedEventArgs e) => RenderLobby();

    private void RenderLobby()
    {
        PanLobby.Children.Clear();
        var keyword = (TextSearch.Text ?? "").Trim();
        var shown = string.IsNullOrWhiteSpace(keyword)
            ? _rooms
            : _rooms.Where(r => (r.Name ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                             || (r.Code ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (shown.Count == 0)
        {
            PanLobby.Children.Add(new TextBlock
            {
                Text = _rooms.Count == 0 ? "当前大厅没有房间" : "没有匹配「" + keyword + "」的房间",
                Opacity = 0.5,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 4)
            });
            return;
        }
        var wrap = new WrapPanel();
        foreach (var room in shown) wrap.Children.Add(BuildRoomCard(room));
        PanLobby.Children.Add(wrap);
    }

    private FrameworkElement BuildRoomCard(VoxRoom room)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(room.Name) ? "未命名房间" : room.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var meta = (string.IsNullOrWhiteSpace(room.GameVersion) ? "未知版本" : room.GameVersion)
                   + " · " + (string.IsNullOrWhiteSpace(room.Loader) ? "未知" : room.Loader)
                   + (string.IsNullOrWhiteSpace(room.Region) ? "" : " · " + room.Region);
        panel.Children.Add(new TextBlock { Text = meta, Opacity = 0.6, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "房间码 " + room.Code + (room.HasPassword ? "（有密码）" : ""),
            Opacity = 0.5,
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0)
        });
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = room.Current + "/" + room.Max + " 人",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        var join = new MyButton
        {
            Text = "加入 →",
            Height = 28,
            MinWidth = 78,
            HorizontalAlignment = HorizontalAlignment.Right,
            ColorType = MyButton.ColorState.Highlight
        };
        join.Click += async (_, _) => await JoinByCodeAsync(room.Code);
        row.Children.Add(join);
        panel.Children.Add(row);
        return new Border
        {
            Width = 232,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(14, 12, 14, 10),
            CornerRadius = new CornerRadius(10),
            Background = TcsCard(),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }

    private void BtnLinks_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.MyMsgBox(
            "官网：https://p2p.wuhui.icu/\n" +
            "GitHub：https://github.com/AUGUHDAR/VoxLink\n" +
            "Gitee：https://gitee.com/AUGUHDAR/VoxLink\n" +
            "MC百科：https://www.mcmod.cn/class/28295.html\n" +
            "Modrinth：https://modrinth.com/mod/voxlink\n" +
            "CurseForge：https://www.curseforge.com/minecraft/mc-mods/voxlink\n" +
            "Discord：https://discord.gg/XaAFxvzPDS",
            "VoxLink · 相关链接", "关闭");
    }

    private void BtnHome_Click(object sender, MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ModVoxLink.HomeUrl) { UseShellExecute = true }); }
        catch (Exception ex) { AddLog("打开官网失败：" + ex.Message); }
    }

    private void BtnBack_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.frmMultiplayerHost?.SwitchTo(1);
    }
}
