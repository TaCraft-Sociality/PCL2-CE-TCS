using System;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PCL;

/// <summary>多人游戏 → 联机房间 → 陶瓦联机：工具状态 + 当前连接 + 一个「打开陶瓦界面」按钮。</summary>
public partial class PageMultiplayerTerracotta
{
    private readonly StringBuilder _log = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(900) };

    public PageMultiplayerTerracotta()
    {
        InitializeComponent();
            Loaded += (_, _) => ModTcsTheme.ApplyIconTiles(this); // TCS：陶瓦页保持白底，只处理图标块
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void AddLog(string text)
    {
        _log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
        if (_log.Length > 6000) _log.Remove(0, _log.Length - 6000);
        var count = _log.ToString().Split('\n').Length - 1;
        TextLog.Text = _log.ToString();
        LabLogTitle.Text = "陶瓦引擎日志（" + count + " 条）";
        TextLog.ScrollToEnd();
    }

    private void Refresh()
    {
        var installed = ModTerracotta.IsInstalled;
        LabToolState.Text = installed ? "已安装" : "尚未安装或需要修复";
        BtnInstall.Text = installed ? "校验并修复" : "下载陶瓦工具";
        LabVersion.Text = "0.4.2";
        LabConnState.Text = ModTerracotta.StateText;
        DotState.Fill = new SolidColorBrush(ModTerracotta.IsRunning
            ? Color.FromRgb(0x2F, 0xA8, 0x4A)
            : Color.FromRgb(0xC9, 0xCE, 0xD6));
        var detail = ModTerracotta.Detail;
        if (!string.IsNullOrWhiteSpace(ModTerracotta.Room))
            detail = "房间号：" + ModTerracotta.Room + (string.IsNullOrWhiteSpace(detail) ? "" : "\n" + detail);
        LabDetail.Text = string.IsNullOrWhiteSpace(detail) ? "（暂无）" : detail;
        BtnOpen.IsEnabled = installed;
    }

    private void ItemDetail_Click(object sender, MouseButtonEventArgs e)
    {
        LabDetail.Visibility = LabDetail.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void BtnInstall_Click(object sender, MouseButtonEventArgs e)
    {
        AddLog("开始下载 / 校验陶瓦工具");
        BtnInstall.IsEnabled = false;
        try
        {
            var ok = await ModTerracotta.EnsureInstalledAsync();
            AddLog(ok ? "陶瓦工具已就绪" : "陶瓦工具下载或校验失败");
        }
        catch (Exception ex) { AddLog("下载失败：" + ex.Message); }
        BtnInstall.IsEnabled = true;
        Refresh();
    }

    private async void BtnOpen_Click(object sender, MouseButtonEventArgs e)
    {
        AddLog("启动陶瓦并打开官方界面");
        if (!await ModTerracotta.EnsureInstalledAsync()) { AddLog("陶瓦工具未就绪"); return; }
        if (!await ModTerracotta.StartAsync()) { AddLog("陶瓦启动失败"); return; }
        AddLog("已启动，正在打开陶瓦界面");
        ModMain.frmMultiplayerHost?.SwitchTo(3);
        Refresh();
    }

    private void BtnBack_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.frmMultiplayerHost?.SwitchTo(1);
    }
}
