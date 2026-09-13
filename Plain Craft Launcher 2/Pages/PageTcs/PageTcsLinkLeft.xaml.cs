using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace PCL;

/// <summary>
///     TCS 定制：陶瓦联机页面的左侧栏。
///     启动后会把右侧的浏览器切到陶瓦自带的本地界面（http://127.0.0.1:端口/）。
/// </summary>
public partial class PageTcsLinkLeft
{
    public PageTcsLinkLeft()
    {
        InitializeComponent();
    }

    private async void Item_Click(object sender, MouseButtonEventArgs e)
    {
        switch ((sender as MyListItem)?.Tag?.ToString())
        {
            case "start":
                await TriggerStartAsync();
                break;
            case "stop":
                ModTerracotta.Stop();
                ModMain.frmTcsLink?.NavigateTo(ModTerracotta.PrepareHintPage());
                break;
            case "reload":
                ModMain.frmTcsLink?.NavigateTo(
                    string.IsNullOrWhiteSpace(ModTerracotta.UiUrl) ? ModTerracotta.PrepareHintPage() : ModTerracotta.UiUrl);
                break;
            case "dir":
                try
                {
                    Directory.CreateDirectory(ModTerracotta.InstallDir);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = ModTerracotta.InstallDir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "[Terracotta] 打开组件目录失败");
                }

                break;
            case "project":
                ModBase.OpenWebsite(ModTerracotta.ProjectUrl);
                break;
        }
    }

    /// <summary>
    ///     TCS 定制：让陶瓦界面贴合启动器观感的 CSS。
    ///     ① 「我想当房主 / 我想当房客」两张卡片改为左右并排；
    ///     ② 背景与启动器背景一致（浅色），并把文字改成深色，保证浅底可读。
    /// </summary>
    public static string BuildSkinCss()
    {
        var bg = GetBackgroundHex();
        return $$"""
                 /* TCS 定制：陶瓦界面贴合启动器（浅色卡片风） */
                 html, body { background: {{bg}} !important; color: #2B2B2B !important; }
                 body { padding: 12px !important; justify-content: flex-start !important; }
                 h1 { font-size: 2.4rem !important; margin-bottom: 4px !important; -webkit-text-fill-color: #2B2B2B !important; }
                 .subtitle, .tile-description, .result-description, .loading-text, .input-hint {
                     color: #5A5A5A !important;
                 }
                 .tile-title, .result-title, .input-title, .loading-text { color: #2B2B2B !important; }
                 .role-selection {
                     display: flex !important;
                     flex-direction: row !important;
                     flex-wrap: wrap !important;
                     justify-content: center !important;
                     align-items: stretch !important;
                     gap: 22px !important;
                 }
                 .role-selection .tile {
                     width: auto !important;
                     height: auto !important;
                     min-height: 250px !important;
                     flex: 1 1 320px !important;
                     max-width: 380px !important;
                     border: 1px solid rgba(0, 0, 0, 0.1) !important;
                     box-shadow: 0 6px 18px rgba(0, 0, 0, 0.08) !important;
                 }
                 .loading-view, .result-view, .input-view, .community-link {
                     background: rgba(0, 0, 0, 0.035) !important;
                     border: 1px solid rgba(0, 0, 0, 0.06) !important;
                 }
                 .input-field {
                     color: #2B2B2B !important;
                     background: rgba(0, 0, 0, 0.04) !important;
                     border: 1px solid rgba(0, 0, 0, 0.15) !important;
                 }
                 .invite-code { background: rgba(0, 0, 0, 0.05) !important; }
                 """;
    }

    /// <summary>读取启动器当前背景色（浅色主题下基本就是白/浅灰），失败时回退纯白。</summary>
    private static string GetBackgroundHex()
    {
        foreach (var key in new[] { "ColorBrushBackground", "ColorBrushBg0", "ColorBrushWhite" })
        {
            try
            {
                if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush b &&
                    b.Color.A > 200)
                    return $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
            }
            catch
            {
                // 忽略，尝试下一个
            }
        }

        return "#FFFFFF";
    }

    /// <summary>启动陶瓦，并把右侧浏览器切到它的本地界面（供左侧栏项与自检调用）。</summary>
    public async Task TriggerStartAsync()
    {
        var browser = ModMain.frmTcsLink;
        browser?.NavigateTo(ModTerracotta.PrepareHintPage());

        // 陶瓦一报出界面端口就立刻跳转（比轮询更及时；事件来自后台线程，必须切回 UI 线程）
        void OnReady() => ModBase.RunInUi(() => browser?.NavigateTo(ModTerracotta.UiUrl));
        ModTerracotta.UiReady -= OnReady;
        ModTerracotta.UiReady += OnReady;
        try
        {
            var ok = await ModTerracotta.StartAsync();
            if (!ok)
            {
                ModBase.Log($"[Terracotta] 启动失败：{ModTerracotta.Detail}");
                return;
            }

            for (var i = 0; i < 24 && string.IsNullOrWhiteSpace(ModTerracotta.UiUrl); i++)
                await Task.Delay(500);

            if (!string.IsNullOrWhiteSpace(ModTerracotta.UiUrl))
                browser?.NavigateTo(ModTerracotta.UiUrl);
            else
                ModBase.Log("[Terracotta] 12 秒内未拿到界面端口：请确认没有其它陶瓦窗口在运行，或点「打开组件目录」查看组件");
        }
        finally
        {
            ModTerracotta.UiReady -= OnReady;
        }
    }
}
