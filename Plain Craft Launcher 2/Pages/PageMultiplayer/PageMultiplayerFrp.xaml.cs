using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

/// <summary>多人游戏 → 联机房间 → FRP 内网穿透（樱花 natfrp）。</summary>
public partial class PageMultiplayerFrp
{
    private List<SakuraTunnel> _tunnels = new();
    private List<SakuraNode> _nodes = new();
    private bool _loadedOnce;

public PageMultiplayerFrp()
{
    InitializeComponent();
            Loaded += (_, _) => { ModTcsTheme.ApplyIconTiles(this); foreach (var s in new[] { 1, 2, 4, 8, 20, 40 }) { var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(s) }; tm.Tick += (_, _) => { ModTcsTheme.ApplySurfaceColors(this); tm.Stop(); }; tm.Start(); } };
    ModSakuraFrp.Load();
    TextToken.Password = "";
    UpdateTokenState();
    Loaded += async (_, _) =>
    {
        if (_loadedOnce || string.IsNullOrWhiteSpace(ModSakuraFrp.Token)) return;
        _loadedOnce = true;
        await RefreshAsync(true);
    };
}

    private void UpdateTokenState()
    {
        LabTokenState.Text = string.IsNullOrWhiteSpace(ModSakuraFrp.Token)
            ? "尚未保存访问密钥。密钥只保存在本机文件里，界面不回显。"
            : "已保存访问密钥（界面不显示原文，可点右侧「清除已保存密钥」删除）。";
        BtnClearToken.IsEnabled = !string.IsNullOrWhiteSpace(ModSakuraFrp.Token);
    }

    private void BtnClearToken_Click(object sender, MouseButtonEventArgs e)
    {
        ModSakuraFrp.SaveToken("");
        TextToken.Password = "";
        _tunnels = new List<SakuraTunnel>();
        RenderTunnels();
        UpdateTokenState();
        HintService.Hint("已清除本机保存的访问密钥", HintType.Success);
    }
    private async System.Threading.Tasks.Task RefreshAsync(bool withNodes)
    {
        BtnRefresh.IsEnabled = false;
        BtnRead.IsEnabled = false;
        try
        {
            var result = await ModSakuraFrp.FetchTunnelsAsync();
            _tunnels = result.tunnels ?? new List<SakuraTunnel>();
            RenderTunnels();
            LabTunnelTip.Text = result.ok
                ? "▾ 自动恢复与连接说明：隧道开关由樱花启动器管理（这里只读取状态）；好友在游戏的「直接连接」里填公网地址即可。"
                : "▾ 读取失败：" + result.message;
            if (withNodes) await LoadNodesAsync(false);
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
            BtnRead.IsEnabled = true;
        }
    }

    private void RenderTunnels()
    {
        for (int i = PanTunnels.Children.Count - 1; i >= 0; i--)
        {
            var child = PanTunnels.Children[i];
            if (child is FrameworkElement fe && (fe.Name == "LabEmptyTunnel" || fe.Name == "LabEmptyTunnelTip")) continue;
            PanTunnels.Children.RemoveAt(i);
        }
        LabEmptyTunnel.Visibility = _tunnels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LabEmptyTunnelTip.Visibility = _tunnels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LabOnline.Text = _tunnels.Count(t => t.Online).ToString();
        LabRecover.Text = "0";
        foreach (var tunnel in _tunnels) PanTunnels.Children.Add(BuildTunnelCard(tunnel));
    }

    private FrameworkElement BuildTunnelCard(SakuraTunnel tunnel)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x08, 0, 0, 0))
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = (string.IsNullOrWhiteSpace(tunnel.Name) ? "未命名隧道" : tunnel.Name) + "  ·  " + tunnel.Type,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold
        });
        var addr = string.IsNullOrWhiteSpace(tunnel.PublicAddress) ? "（未分配公网地址）" : tunnel.PublicAddress;
        left.Children.Add(new TextBlock
        {
            Text = "本地 " + (string.IsNullOrWhiteSpace(tunnel.LocalIp) ? "127.0.0.1" : tunnel.LocalIp) + ":" + tunnel.LocalPort
                   + "   →   公网 " + addr + "   ·   节点 " + tunnel.NodeId,
            Opacity = 0.62,
            FontSize = 11.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        var state = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        state.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush((ModSakuraFrp.IsTunnelRunning(tunnel.Id) || tunnel.Online)
                ? Color.FromRgb(0x2F, 0xA8, 0x4A)
                : Color.FromRgb(0xC9, 0xCE, 0xD6)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        state.Children.Add(new TextBlock
        {
            Text = ModSakuraFrp.IsTunnelRunning(tunnel.Id) ? "已连接（本启动器内 frpc 运行中）" : (tunnel.Online ? "已连接（樱花侧在线）" : "未连接（点右侧「启动穿透」）"),
            FontSize = 11.5,
            Opacity = 0.7
        });
        left.Children.Add(state);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var runBtn = new MyButton
        {
            Text = ModSakuraFrp.IsTunnelRunning(tunnel.Id) ? "停止" : "启动穿透",
            Height = 28,
            MinWidth = 84,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            ColorType = MyButton.ColorState.Highlight
        };
        runBtn.Click += async (_, _) =>
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcl-tcs-frp-trace.txt"), DateTime.Now.ToString("HH:mm:ss") + " click id=" + tunnel.Id + Environment.NewLine); } catch { }
            if (ModSakuraFrp.IsTunnelRunning(tunnel.Id))
            {
                var stop = ModSakuraFrp.StopTunnel(tunnel.Id);
                LabTunnelTip.Text = stop.message;
                HintService.Hint(stop.message, stop.ok ? HintType.Success : HintType.Error);
            }
            else
            {
                runBtn.IsEnabled = false;
                LabTunnelTip.Text = "正在启动隧道 " + tunnel.Id + " …";
                var started = await ModSakuraFrp.StartTunnelAsync(tunnel.Id, line =>
                {
                    Dispatcher.BeginInvoke(new Action(() => { LabTunnelTip.Text = line; }));
                });
                LabTunnelTip.Text = started.message;
                HintService.Hint(started.message, started.ok ? HintType.Success : HintType.Error);
                runBtn.IsEnabled = true;
            }
            await RefreshAsync(false);
        };
        var copy = new MyButton { Text = "复制地址", Height = 28, MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right };
        copy.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(tunnel.PublicAddress)) { HintService.Hint("这条隧道还没有公网地址", HintType.Error); return; }
            try { Clipboard.SetText(tunnel.PublicAddress); HintService.Hint("已复制：" + tunnel.PublicAddress, HintType.Success); }
            catch (Exception ex) { HintService.Hint("复制失败：" + ex.Message, HintType.Error); }
        };
        right.Children.Add(copy);
        right.Children.Add(runBtn);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }

    private async System.Threading.Tasks.Task LoadNodesAsync(bool render)
    {
        var result = await ModSakuraFrp.FetchNodesAsync();
        _nodes = result.nodes ?? new List<SakuraNode>();
        LabNodeTip.Text = result.ok
            ? "共 " + _nodes.Count + " 个节点" + (CheckFreeOnly.IsChecked == true ? "（已过滤专业版）" : "") + "，点「新建隧道」会自动选第一个可用节点"
            : result.message;
        if (render) RenderNodes();
    }

    private void RenderNodes()
    {
        PanNodes.Children.Clear();
        var free = CheckFreeOnly.IsChecked == true;
        var shown = free ? _nodes.Where(n => !n.Premium).ToList() : _nodes;
        foreach (var node in shown.Take(60))
        {
            PanNodes.Children.Add(new TextBlock
            {
                Text = node.Combo + (string.IsNullOrWhiteSpace(node.Host) ? "" : "   " + node.Host),
                FontSize = 11.5,
                Opacity = 0.68,
                Margin = new Thickness(0, 1, 0, 1)
            });
        }
        if (shown.Count == 0) PanNodes.Children.Add(new TextBlock { Text = "没有可用节点", FontSize = 11.5, Opacity = 0.5 });
    }

    private void ItemNodes_Click(object sender, MouseButtonEventArgs e)
    {
        PanNodes.Visibility = PanNodes.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ItemNodes.Title = PanNodes.Visibility == Visibility.Visible ? "收起节点列表" : "展开节点列表（默认收起）";
        if (PanNodes.Visibility == Visibility.Visible) RenderNodes();
    }

    private void CheckFreeOnly_Click(object sender, RoutedEventArgs e) => RenderNodes();

    private async void BtnRead_Click(object sender, MouseButtonEventArgs e)
    {
        var token = (TextToken.Password ?? "").Trim();
        if (token.Length > 0) ModSakuraFrp.SaveToken(token);
        if (string.IsNullOrWhiteSpace(ModSakuraFrp.Token)) { HintService.Hint("请先粘贴访问密钥", HintType.Error); return; }
        TextToken.Password = "";
        UpdateTokenState();
        await RefreshAsync(true);
        HintService.Hint("已保存访问密钥，共读取到 " + _tunnels.Count + " 条隧道", HintType.Success);
    }

    private async void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        try { await RefreshAsync(false); }
        finally { BtnRefresh.IsEnabled = true; }
    }

    private async void BtnNodes_Click(object sender, MouseButtonEventArgs e)
    {
        BtnNodes.IsEnabled = false;
        try { await LoadNodesAsync(true); }
        finally { BtnNodes.IsEnabled = true; }
    }

    private async void BtnNew_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ModSakuraFrp.Token)) { HintService.Hint("请先填写并读取访问密钥", HintType.Error); return; }
        BorderNew.Visibility = BorderNew.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (BorderNew.Visibility != Visibility.Visible) return;
        if (_nodes.Count == 0) await LoadNodesAsync(false);
        FillNodeCombo();
        if (ComboNode.Items.Count > 0) ComboNode.SelectedIndex = 0;
        HintService.Hint("填写名称 / 本地端口后点「创建 TCP 隧道」", HintType.Info);
    }

    private void FillNodeCombo()
    {
        var freeOnly = CheckNewFreeOnly.IsChecked == true;
        var list = freeOnly ? _nodes.Where(n => !n.Premium).ToList() : _nodes.ToList();
        var keep = ComboNode.SelectedIndex;
        ComboNode.Items.Clear();
        foreach (var node in list) ComboNode.Items.Add(node.Combo);
        if (ComboNode.Items.Count > 0) ComboNode.SelectedIndex = Math.Min(Math.Max(keep, 0), ComboNode.Items.Count - 1);
        LabNewTip.Text = list.Count == 0 ? "没有可用节点（试试取消「仅显示免费节点」）" : "";
    }

    private void CheckNewFreeOnly_Click(object sender, RoutedEventArgs e) => FillNodeCombo();

    private async void BtnCreateTunnel_Click(object sender, MouseButtonEventArgs e)
    {
        var name = (TextNewName.Text ?? "").Trim();
        if (name.Length == 0) { HintService.Hint("请填写隧道名称", HintType.Error); return; }
        if (!int.TryParse((TextNewPort.Text ?? "").Trim(), out var port) || port <= 0 || port > 65535)
        {
            HintService.Hint("本地端口不对（游戏对局域网开放时会显示端口号）", HintType.Error);
            return;
        }
        var remote = 0;
        var remoteText = (TextNewRemote.Text ?? "").Trim();
        if (remoteText.Length > 0 && (!int.TryParse(remoteText, out remote) || remote < 0 || remote > 65535))
        {
            HintService.Hint("远程端口不合法（留空表示由樱花分配）", HintType.Error);
            return;
        }
        var freeOnly = CheckNewFreeOnly.IsChecked == true;
        var pool = freeOnly ? _nodes.Where(n => !n.Premium).ToList() : _nodes.ToList();
        if (pool.Count == 0) { HintService.Hint("没有可用节点", HintType.Error); return; }
        var index = ComboNode.SelectedIndex;
        if (index < 0 || index >= pool.Count) index = 0;
        var node = pool[index];
        BtnCreateTunnel.IsEnabled = false;
        var result = await ModSakuraFrp.CreateTunnelAsync(name, node.Id, port, remote);
        HintService.Hint(result.message, result.ok ? HintType.Success : HintType.Error);
        BtnCreateTunnel.IsEnabled = true;
        if (result.ok)
        {
            BorderNew.Visibility = Visibility.Collapsed;
            await RefreshAsync(false);
        }
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { HintService.Hint("打开失败：" + ex.Message, HintType.Error); }
    }

    private void BtnSite_Click(object sender, MouseButtonEventArgs e) => OpenUrl(ModSakuraFrp.SiteUrl);
    private void BtnGuide_Click(object sender, MouseButtonEventArgs e) => OpenUrl(ModSakuraFrp.GuideUrl);

    private void BtnCopySite_Click(object sender, MouseButtonEventArgs e)
    {
        try { Clipboard.SetText(ModSakuraFrp.SiteUrl); HintService.Hint("已复制：" + ModSakuraFrp.SiteUrl, HintType.Success); }
        catch (Exception ex) { HintService.Hint("复制失败：" + ex.Message, HintType.Error); }
    }

    private void BtnBack_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.frmMultiplayerHost?.SwitchTo(1);
    }
}
