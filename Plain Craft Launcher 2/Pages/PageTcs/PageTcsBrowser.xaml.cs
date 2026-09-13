using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace PCL;

/// <summary>
///     TCS 定制页面：使用 WebView2 在启动器内嵌显示网页（社区、皮肤站等）。
/// </summary>
public partial class PageTcsBrowser
{
    private readonly string _displayName;
    private readonly string _homeUrl;

    private bool _initStarted;
    private string _lastUrl = "";
    private string? _pendingUrl;
    private bool _ready;
    private string? _injectedCss;
    private bool _injectionRegistered;

    /// <summary>
    ///     TCS 定制：注入到页面里的 CSS（用于让第三方页面贴合启动器观感，例如联机页的陶瓦界面）。
    ///     赋值后会在下一次加载时生效，并对后续每次导航都生效。
    /// </summary>
    public string? InjectedCss
    {
        get => _injectedCss;
        set
        {
            _injectedCss = value;
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!_ready || Browser.CoreWebView2 is null || _injectionRegistered) return;
            _injectionRegistered = true;
            _ = Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildInjectionScript(value));
        }
    }

    /// <summary>TCS 定制：自检用，在页面里执行一段脚本并返回结果。</summary>
    public async Task<string> RunScriptAsync(string js)
    {
        if (Browser.CoreWebView2 is null) return "<no-core>";
        try
        {
            return await Browser.CoreWebView2.ExecuteScriptAsync(js) ?? "";
        }
        catch (Exception ex)
        {
            return "<err:" + ex.Message + ">";
        }
    }

    /// <summary>生成「插入 &lt;style&gt;」的脚本。</summary>
    private static string BuildInjectionScript(string css)
    {
        var escaped = css.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${");
        return "(function(){try{var s=document.getElementById('tcs-injected-style');" +
               "if(!s){s=document.createElement('style');s.id='tcs-injected-style';(document.head||document.documentElement).appendChild(s);}" +
               $"s.textContent=`{escaped}`;}}catch(e){{}}}})();";
    }

    /// <summary>把当前 InjectedCss 应用到已经加载完成的页面上。</summary>
    private void ApplyInjectedCss()
    {
        if (Browser.CoreWebView2 is null || string.IsNullOrWhiteSpace(_injectedCss)) return;
        _ = Browser.CoreWebView2.ExecuteScriptAsync(BuildInjectionScript(_injectedCss));
    }

    /// <summary>
    ///     创建一个内嵌浏览器页面。
    /// </summary>
    /// <param name="homeUrl">站点首页地址。</param>
    /// <param name="displayName">站点名称，用于界面提示。</param>
    /// <param name="showToolbar">是否显示后退 / 前进 / 刷新 / 地址栏（联机页传 false，让内容满屏，看起来是启动器原生页面）。</param>
    public PageTcsBrowser(string homeUrl, string displayName, bool showToolbar = true)
    {
        _homeUrl = homeUrl;
        _displayName = displayName;
        InitializeComponent();
        Browser.DefaultBackgroundColor = System.Drawing.Color.White;
        LabLoading.Text = $"正在加载 {displayName} …";
        LabLoadingUrl.Text = homeUrl;
        LabUrl.Text = homeUrl;
        if (!showToolbar)
        {
            PanToolbar.Visibility = Visibility.Collapsed;
            PanRoot.Margin = new Thickness(14, 10, 14, 12);
        }

        PageEnter += OnPageEnter;
        Loaded += (_, _) => EnsureBrowser();
        HookMessages();
    }

    /// <summary>
    ///     当前显示的网址。
    /// </summary>
    public string CurrentUrl => Browser.Source?.ToString() ?? (_lastUrl.Length > 0 ? _lastUrl : _homeUrl);

    /// <summary>
    ///     内置浏览器内核是否已就绪。
    /// </summary>
    public bool IsReady => _ready;

    /// <summary>
    ///     最近一次页面导航是否成功（未开始导航时为 null）。
    /// </summary>
    public bool? LastNavigationSuccess { get; private set; }

    /// <summary>
    ///     最近一次导航失败的错误类型。
    /// </summary>
    public string? LastNavigationError { get; private set; }

    /// <summary>
    ///     浏览器可用区域的尺寸，用于自检。
    /// </summary>
    public string GetDebugInfo() =>
        $"ready={_ready}, navSuccess={LastNavigationSuccess?.ToString() ?? "null"}, " +
        $"navError={LastNavigationError ?? "none"}, " +
        $"url={CurrentUrl}, host={PanBrowserHost.ActualWidth:0}x{PanBrowserHost.ActualHeight:0}, " +
        $"webview={Browser.ActualWidth:0}x{Browser.ActualHeight:0}";

    /// <summary>
    ///     TCS 定制：供联机页在陶瓦启动后跳转到它自带的本地界面。
    /// </summary>
    /// <param name="url">目标地址（http://127.0.0.1:端口/）。</param>
    public void NavigateTo(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (_ready)
            Navigate(url);
        else
            _pendingUrl = url;
        if (!_initStarted)
            EnsureBrowser();
    }

    #region 初始化

    // 消息弹窗与右下角浮动按钮的遮挡处理（WebView2 是独立 HWND，会盖住 WPF 内容）
    private void HookMessages()
    {
        var main = ModMain.frmMain;
        if (main is null)
            return;
        main.PanMsgBackground.IsVisibleChanged += (_, _) => RefreshLayout();
        foreach (var button in new FrameworkElement[]
                 {
                     main.BtnExtraBack, main.BtnExtraDownload, main.BtnExtraLog, main.BtnExtraShutdown,
                     main.BtnExtraMusic, main.BtnExtraApril, main.BtnExtraUpdateRestart
                 })
            button.IsVisibleChanged += (_, _) => RefreshLayout();
        RefreshLayout();
    }

    private void OnPageEnter()
    {
        RefreshLayout();
        EnsureBrowser();
    }

    private void RefreshLayout()
    {
        var main = ModMain.frmMain;
        if (main is null)
            return;
        // 弹窗显示时隐藏浏览器，避免浏览器盖住弹窗
        var overlayVisible = main.PanMsgBackground.Visibility == Visibility.Visible;
        PanBrowserHost.Visibility = overlayVisible ? Visibility.Collapsed : Visibility.Visible;
        // 右下角浮动按钮显示时为它让出位置，避免按钮被浏览器覆盖
        var buttonsVisible = main.BtnExtraBack.Visibility == Visibility.Visible ||
                             main.BtnExtraDownload.Visibility == Visibility.Visible ||
                             main.BtnExtraLog.Visibility == Visibility.Visible ||
                             main.BtnExtraShutdown.Visibility == Visibility.Visible ||
                             main.BtnExtraMusic.Visibility == Visibility.Visible ||
                             main.BtnExtraApril.Visibility == Visibility.Visible ||
                             main.BtnExtraUpdateRestart.Visibility == Visibility.Visible;
        PanRoot.Margin = new Thickness(25, 16, buttonsVisible ? 56 : 25, 18);
    }

    /// <summary>
    ///     初始化 WebView2（首次进入页面时执行）。
    /// </summary>
    public async void EnsureBrowser()
    {
        if (_initStarted || !IsLoaded)
            return;
        _initStarted = true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, GetUserDataFolder());
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                // 网页中的 target=_blank 链接也交给内置浏览器处理
                e.Handled = true;
                if (!string.IsNullOrEmpty(e.Uri))
                    Navigate(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                _lastUrl = e.Uri;
                LabUrl.Text = e.Uri;
                PanError.Visibility = Visibility.Collapsed;
                PanLoading.Visibility = Visibility.Visible;
            };
            core.SourceChanged += (_, _) =>
            {
                _lastUrl = Browser.Source?.ToString() ?? _lastUrl;
                LabUrl.Text = _lastUrl;
            };
            core.NavigationCompleted += (_, e) => OnNavigationCompleted(e);
            core.ProcessFailed += (_, e) =>
                ShowError($"浏览器内核进程异常退出（{e.ProcessFailedKind}），请重试或使用系统浏览器打开。");
            _ready = true;
            var url = _pendingUrl ?? _homeUrl;
            _pendingUrl = null;
            Navigate(url);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"初始化内置浏览器失败（{_displayName}）", ModBase.LogLevel.Debug);
            ShowError("内置浏览器初始化失败，可能未安装 WebView2 运行时。\r\n" +
                      "可前往 https://go.microsoft.com/fwlink/p/?LinkId=2124703 安装「Microsoft Edge WebView2 Runtime」后点击重试，" +
                      "或直接在系统浏览器中访问本站。");
        }
    }

    private static string GetUserDataFolder()
    {
        try
        {
            var folder = Path.Combine(ModBase.exePath, "PCL", "WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            var folder = Path.Combine(Path.GetTempPath(), "TCS-Launcher-WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private void OnNavigationCompleted(CoreWebView2NavigationCompletedEventArgs e)
    {
        LastNavigationSuccess = e.IsSuccess;
        LastNavigationError = e.IsSuccess ? null : e.WebErrorStatus.ToString();
        PanLoading.Visibility = Visibility.Collapsed;
        UpdateNavState();
        if (e.IsSuccess)
        {
            PanError.Visibility = Visibility.Collapsed;
            ApplyInjectedCss();
        }
        else
            ShowError($"页面加载失败：{e.WebErrorStatus}\r\n" +
                      "请检查网络连接后点击重试，或使用系统浏览器打开。\r\n" +
                      "提示：若正在使用网络代理 / 游戏加速器，部分服务器站点会拦截代理流量，可尝试关闭后重试。");
    }

    private void UpdateNavState()
    {
        try
        {
            var core = Browser.CoreWebView2;
            if (core is null)
                return;
            BtnBack.IsEnabled = core.CanGoBack;
            BtnForward.IsEnabled = core.CanGoForward;
        }
        catch
        {
            // 忽略内核未就绪时的异常
        }
    }

    private void ShowError(string message)
    {
        LabError.Text = message;
        PanError.Visibility = Visibility.Visible;
        PanLoading.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region 浏览操作

    /// <summary>
    ///     在当前内置浏览器中打开指定网址。
    /// </summary>
    public void Navigate(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (!_ready)
        {
            _pendingUrl = url;
            EnsureBrowser();
            return;
        }

        try
        {
            PanError.Visibility = Visibility.Collapsed;
            PanLoading.Visibility = Visibility.Visible;
            LabUrl.Text = url;
            Browser.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "内置浏览器跳转失败", ModBase.LogLevel.Debug);
            ShowError("页面跳转失败，请重试或使用系统浏览器打开。");
        }
    }

    /// <summary>
    ///     回到站点首页。
    /// </summary>
    public void GoHome() => Navigate(_homeUrl);

    /// <summary>
    ///     刷新当前页面。
    /// </summary>
    public void Refresh()
    {
        try
        {
            if (_ready)
                Browser.CoreWebView2.Reload();
            else
                Navigate(_homeUrl);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    ///     后退。
    /// </summary>
    public void GoBack()
    {
        try
        {
            if (_ready && Browser.CoreWebView2.CanGoBack)
                Browser.CoreWebView2.GoBack();
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    ///     前进。
    /// </summary>
    public void GoForward()
    {
        try
        {
            if (_ready && Browser.CoreWebView2.CanGoForward)
                Browser.CoreWebView2.GoForward();
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    ///     使用系统默认浏览器打开当前页面。
    /// </summary>
    public void OpenExternal()
    {
        try
        {
            ModBase.OpenWebsite(CurrentUrl);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "打开系统浏览器失败", ModBase.LogLevel.Debug);
        }
    }

    #endregion

    #region 工具栏事件

    private void BtnBack_Click(object sender, EventArgs e) => GoBack();

    private void BtnForward_Click(object sender, EventArgs e) => GoForward();

    private void BtnRefresh_Click(object sender, EventArgs e) => Refresh();

    private void BtnHome_Click(object sender, EventArgs e) => GoHome();

    private void BtnExternal_Click(object sender, EventArgs e) => OpenExternal();

    private void BtnErrorExternal_Click(object sender, MouseButtonEventArgs e) => OpenExternal();

    private void BtnRetry_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_ready)
        {
            _initStarted = false;
            EnsureBrowser();
            return;
        }

        Navigate(_lastUrl.Length > 0 ? _lastUrl : _homeUrl);
    }

    #endregion
}
