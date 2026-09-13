#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.UI.Theme;

namespace PCL;

/// <summary>
///     TCS 定制版自检（仅 Debug 构建）。
///     设置环境变量 PCL_TCS_SELFTEST=1 后启动，会自动检查主题配色、左侧导航栏与内置浏览器页面，
///     结果写入 PCL\tcs-selftest.txt，便于无人值守验证。
/// </summary>
public static class ModTcsSelfTest
{
    private static readonly List<string> Lines = [];

    public static bool IsEnabled => Environment.GetEnvironmentVariable("PCL_TCS_SELFTEST") == "1";

    private static void Write(string text)
    {
        Lines.Add($"[{DateTime.Now:HH:mm:ss.fff}] {text}");
        ModBase.Log("[TCS SelfTest] " + text);
        try
        {
            var folder = Path.Combine(ModBase.exePath, "PCL");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "tcs-selftest.txt"), Lines);
        }
        catch
        {
            // 忽略写入失败
        }
    }

    private static string BrushHex(object? brush) => brush is SolidColorBrush solid
        ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
        : brush?.ToString() ?? "null";

    // 跳过首次启动的确认弹窗，便于无人值守自检
    public static void PrepareEnvironment()
    {
        try
        {
            States.System.LauncherEula = true;
            Config.System.TelemetryConfig.SetValue(false, forceNewValue: true);
        }
        catch (Exception ex)
        {
            Write("PrepareEnvironment 失败：" + ex.Message);
        }
    }

    public static async void RunAsync()
    {
        try
        {
            Write("================ TCS 自检开始 ================");
            Write($"[隐藏配置] Launch={Config.Preference.Hide.SetupLaunch}, Java={Config.Preference.Hide.SetupJava}, " +
                  $"GameManage={Config.Preference.Hide.SetupGameManage}, GameLink={Config.Preference.Hide.SetupGameLink}, " +
                  $"Ui={Config.Preference.Hide.SetupUi}, Lang={Config.Preference.Hide.SetupLauncherLanguage}, " +
                  $"Misc={Config.Preference.Hide.SetupLauncherMisc}, About={Config.Preference.Hide.SetupAbout}, " +
                  $"Log={Config.Preference.Hide.SetupLog}");
            // 1. 主题配色
            var res = System.Windows.Application.Current.Resources;
            Write($"主题={ThemeService.CurrentTheme}, 深色模式={ThemeService.IsDarkMode}");
            Write($"ColorBrush2={BrushHex(res["ColorBrush2"])}, ColorBrush3={BrushHex(res["ColorBrush3"])}, ColorBrush4={BrushHex(res["ColorBrush4"])}");
            // 2. 左侧导航栏
            var main = ModMain.frmMain;
            Write($"窗口={main.ActualWidth:0}x{main.ActualHeight:0}, 导航栏={main.PanNav.ActualWidth:0}x{main.PanNav.ActualHeight:0}");
            var navButtons = new List<string>();
            foreach (var child in main.PanTitleSelect.Children)
                if (child is MyRadioButton radio)
                    navButtons.Add($"{radio.Name}(Tag={radio.Tag},Text={radio.Text},W={radio.ActualWidth:0},H={radio.ActualHeight:0}," +
                                   $"ColorType={radio.ColorType},前景={BrushHex(radio.LabText.Foreground)},背景={BrushHex(radio.Background)})");
            Write($"导航按钮({navButtons.Count}): " + string.Join(" | ", navButtons));
            Write($"当前页面={main.pageCurrent.page}");
            Write($"标题栏：窗口标题=\"{main.Title}\", 文字标识={main.LabTitleLogo.Text}(" +
                  $"{main.LabTitleLogo.Visibility}), 字重={main.LabTitleLogo.FontWeight}, 字号={main.LabTitleLogo.FontSize}, " +
                  $"PCL标识={main.ShapeTitleLogo.Visibility}, CE角标={main.CELogo.Visibility}");
            Write($"配置 WindowTitleType={Config.Preference.WindowTitleType}({(int)Config.Preference.WindowTitleType}), UiLogoText=\"{main.LabTitleLogo.Text}\"");

            // 布局边界检查：用于回归检测"界面右侧突出"
            async Task CheckLayout(string tag)
            {
                await Task.Delay(500);
                var formWidth = main.PanForm.ActualWidth;
                var pos = main.PanMain.TranslatePoint(new Point(0, 0), main.PanForm);
                var right = pos.X + main.PanMain.ActualWidth;
                Write($"[布局 {tag}] PanForm={formWidth:0}x{main.PanForm.ActualHeight:0}, 导航栏宽={main.PanNav.ActualWidth:0}, " +
                      $"PanMain 左={pos.X:0} 宽={main.PanMain.ActualWidth:0} 右边缘={right:0} 右溢出={right - formWidth:0.0}px, " +
                      $"下边缘={pos.Y + main.PanMain.ActualHeight:0}, 左页={main.PanMainLeft.ActualWidth:0}, 右页={main.PanMainRight.ActualWidth:0}");
            }

            await CheckLayout("启动页");
            main.PageChange(FormMain.PageType.Download);
            await WaitForPage(FormMain.PageType.Download, 20000);
            await CheckLayout("下载页");
            ModSetup.UiLogoType(1);
            ModSetup.UiLogoText(Config.Preference.WindowTitleCustomText);
            Write($"[手动 UiLogoType(1)] 文字={main.LabTitleLogo.Text}({main.LabTitleLogo.Visibility}), PCL标识={main.ShapeTitleLogo.Visibility}, CE角标={main.CELogo.Visibility}");

            // 直接构造新页面，以便捕获被 PageChangeActual 吞掉的异常
            try
            {
                var probe = new PageTcsBrowser("https://bbs.tcscraft.com", "TCS 社区");
                Write("PageTcsBrowser 构造成功：" + probe.GetType().Name);
            }
            catch (Exception ex)
            {
                Write("PageTcsBrowser 构造失败：" + ex);
            }

            try
            {
                var probeLeft = new PageTcsLeft("TCS 社区", null);
                Write("PageTcsLeft 构造成功：" + probeLeft.GetType().Name);
            }
            catch (Exception ex)
            {
                Write("PageTcsLeft 构造失败：" + ex);
            }

            // 监听导航按钮选中事件，抓出自动跳转的来源
            foreach (var child in main.PanTitleSelect.Children)
                if (child is MyRadioButton radio)
                    radio.Check += (sender, _) =>
                    {
                        var stack = string.Join("\r\n  ", Environment.StackTrace.Split('\n').Take(9));
                        Write($"[导航事件] {((MyRadioButton)sender).Name} Tag={((MyRadioButton)sender).Tag} 被选中\r\n  {stack}");
                    };

            await Task.Delay(1200);

            // 3. 社区页面
            main.PageChange(FormMain.PageType.Community);
            Write($"[社区] 已请求切换，等待生效：{await WaitForPage(FormMain.PageType.Community, 20000)}");
            await SamplePage("社区", 5000);
            Write($"[社区] pageCurrent={main.pageCurrent.page}, pageLast={main.pageLast.page}, " +
                  $"左栏={ModMain.frmTcsCommunityLeft?.ActualWidth:0}x{ModMain.frmTcsCommunityLeft?.ActualHeight:0}, " +
                  $"frmTcsCommunity: {ModMain.frmTcsCommunity?.GetDebugInfo()}");

            // 3b. 用可达站点验证内置浏览器是否真的能加载并渲染网页
            ModMain.frmTcsCommunity?.Navigate("https://example.com");
            await Task.Delay(8000);
            Write($"[连通性测试 example.com] {ModMain.frmTcsCommunity?.GetDebugInfo()}");

            // 4. 皮肤站页面
            main.PageChange(FormMain.PageType.Skin);
            Write($"[皮肤站] 已请求切换，等待生效：{await WaitForPage(FormMain.PageType.Skin, 20000)}");
            await SamplePage("皮肤站", 5000);
            Write($"[皮肤站] pageCurrent={main.pageCurrent.page}, pageLast={main.pageLast.page}, " +
                  $"左栏={ModMain.frmTcsSkinLeft?.ActualWidth:0}x{ModMain.frmTcsSkinLeft?.ActualHeight:0}, " +
                  $"frmTcsSkin: {ModMain.frmTcsSkin?.GetDebugInfo()}");

            // 4b. B站官方页面
            main.PageChange(FormMain.PageType.Bili);
            Write($"[B站] 已请求切换，等待生效：{await WaitForPage(FormMain.PageType.Bili, 20000)}");
            await Task.Delay(9000);
            Write($"[B站] pageCurrent={main.pageCurrent.page}, " +
                  $"左栏={ModMain.frmTcsBiliLeft?.ActualWidth:0}x{ModMain.frmTcsBiliLeft?.ActualHeight:0}, " +
                  $"frmTcsBili: {ModMain.frmTcsBili?.GetDebugInfo()}");

            // 6. 工具页（联机入口已移除，用于回归检测联机报错弹窗）
            main.PageChange(FormMain.PageType.Tools);
            Write($"[工具页] 已请求切换，等待生效：{await WaitForPage(FormMain.PageType.Tools, 20000)}");
            await Task.Delay(6000);
            Write($"[工具页] pageCurrent={main.pageCurrent.page}, 子页面={ModMain.frmToolsLeft?.pageID}, " +
                  $"左栏控件数={ModMain.frmToolsLeft?.PanItem.Children.Count}, " +
                  $"联机页面实例={(ModMain.frmToolsGameLink is null ? "未创建" : "已创建")}");

            // 7. 设置页（软件信息 / 软件更新 / 反馈 已移除）
            main.PageChange(FormMain.PageType.Setup);
            Write($"[设置页] 已请求切换，等待生效：{await WaitForPage(FormMain.PageType.Setup, 20000)}");
            await Task.Delay(3000);
            var setupItems = new List<string>();
            if (ModMain.frmSetupLeft is not null)
                foreach (var child in ModMain.frmSetupLeft.PanItem.Children)
                    if (child is MyListItem listItem)
                        setupItems.Add(listItem.Name);
            Write($"[设置页] pageCurrent={main.pageCurrent.page}, " +
                  $"SubPageID={(int)(ModMain.frmSetupLeft?.pageID ?? FormMain.PageSubType.Default)}, " +
                  $"Items={setupItems.Count}: {string.Join(",", setupItems)}");

            // 9. 启动页「PCL TCS 提示」文案
            var hintTitle = PCL.Core.App.Localization.Lang.Text("Launch.Right.CommunityHint.Title");
            var hintMsg = PCL.Core.App.Localization.Lang.Text("Launch.Right.CommunityHint.Message");
            var hintHide = PCL.Core.App.Localization.Lang.Text("Launch.Right.CommunityHint.HidePrompt");
            var labText = ModMain.frmLaunchRight?.LabHint1.Text ?? "";
            Write($"[提示文案] Title={hintTitle}; MsgHasPCLTCS={hintMsg.Contains("PCL TCS")}; " +
                  $"MsgHasPCLCE={hintMsg.Contains("PCL CE")}; UiHasPCLTCS={labText.Contains("PCL TCS")}; " +
                  $"UiNoOldPCLCE={!labText.Contains("PCL CE（PCL 社区版）")}; LabHint1Len={labText.Length}; " +
                  $"HidePromptIsThreeLetters={hintHide.Contains("三个字母")}");

            // 9b. 可选配色主题逐个取样（蓝版为 天空蓝/龙猫蓝/死机蓝，橙版为 TCS 橙/死机蓝/龙猫蓝），最后恢复默认配色
            var themeReport = new List<string>();
            foreach (var theme in ThemeService.SelectableThemes)
            {
                ThemeService.CurrentTheme = theme;
                await Task.Delay(450);
                var args = ThemeService.GetCurrentThemeArgs();
                themeReport.Add($"{theme}(idx={ThemeService.ThemeToIndex(theme)},Hue={args.Hue},Light={args.LightAdjust:0.00}," +
                                $"Chroma={args.ChromaAdjust:0.00},Brush2={BrushHex(res["ColorBrush2"])},Brush3={BrushHex(res["ColorBrush3"])},Brush4={BrushHex(res["ColorBrush4"])})");
            }
            Write($"[配色取样] " + string.Join(" | ", themeReport));
            ThemeService.CurrentTheme = Config.Preference.Theme.LightColor;
            await Task.Delay(450);
            Write($"[配色恢复] Theme={ThemeService.CurrentTheme}, 下拉框项数={ThemeService.SelectableThemes.Length}, " +
                  $"Brush3={BrushHex(res["ColorBrush3"])}, 文字表={(ModMain.frmSetupUI is null ? "null" : string.Join("/", ModMain.frmSetupUI.ThemeColors))}");

            // 9c. 个性化页提示文案（推荐官方快照版）—— 先真正进入个性化页，才能取到界面控件文本
            main.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupUI);
            await Task.Delay(1200);
            var blueHint = PCL.Core.App.Localization.Lang.Text("Setup.Ui.Basic.BlueHint");
            var snapLabel = PCL.Core.App.Localization.Lang.Text("Setup.Ui.Basic.GetSnapshot");
            Write($"[个性化提示] HintLen={blueHint.Length}; RecommendsSnapshot={blueHint.Contains("官方快照版")}; " +
                  $"HasOrangeWord={blueHint.Contains("橙色")}; SnapshotBtnLabel={snapLabel}; " +
                  $"UIButtonText={(ModMain.frmSetupUI is null ? "null" : ModMain.frmSetupUI.BtnLauncherDonate.Text)}");

            // 9d. 版本说明弹窗文案（PCL TCS 版本说明，仅升级后展示一次）
            var noticeTitle = PCL.Core.App.Localization.Lang.Text("Update.CommunityNotice.Title");
            var noticeBody = PCL.Core.App.Localization.Lang.Text("Update.CommunityNotice.Body");
            Write($"[版本说明] Title={noticeTitle}; BodyLen={noticeBody.Length}; " +
                  $"Version={ModBase.versionBaseName}/{ModBase.versionCode}; " +
                  $"HasPCLTCS={noticeBody.Contains("PCL TCS")}; HasThemeLine={noticeBody.Contains("死机蓝")}; " +
                  $"HasLobbyLine={noticeBody.Contains("联机功能：已移除")}; " +
                  $"HasOldBlueThemeClaim={noticeBody.Contains("仅部分固定蓝色系主题")}");

            // 9e. 设置 → 关于 → 软件信息（PCL TCS 介绍 + 页底上游开源链接）
            main.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupAbout);
            await Task.Delay(1500);
            var tcsIntro = PCL.Core.App.Localization.Lang.Text("Setup.About.Tcs.Intro");
            var ceBtnLabel = PCL.Core.App.Localization.Lang.Text("Setup.About.Tcs.CeSource.Button");
            Write($"[软件信息] 页面实例={(ModMain.frmSetupAbout is null ? "未创建" : "已创建")}; " +
                  $"IntroLen={tcsIntro.Length}; IntroHasPCLTCS={tcsIntro.Contains("PCL TCS")}; " +
                  $"IntroHasThemes={tcsIntro.Contains("死机蓝")}; IntroHasLobbyRemoved={tcsIntro.Contains("移除了联机功能")}; " +
                  $"CeButtonText={(ModMain.frmSetupAbout is null ? "null" : ModMain.frmSetupAbout.BtnCeSource.Text)}; " +
                  $"CeLabelHasOpenSource={ceBtnLabel.Contains("开源")}");

            // 9f. 百宝箱 / 赞助提示文案已改为 PCL TCS
            var launchCountMsg = PCL.Core.App.Localization.Lang.Text("Tools.Test.LaunchCount.Message", 298);
            var shortcutName = PCL.Core.App.Localization.Lang.Text("Tools.Test.Shortcut.FileName", "");
            var cleanTip = PCL.Core.App.Localization.Lang.Text("Tools.Test.Clean.ToolTip");
            var donateMsg = PCL.Core.App.Localization.Lang.Text("Minecraft.Launch.Donate.Message", 298);
            var serviceName = PCL.Core.App.Localization.Lang.Text("Update.Service.PclCe");
            Write($"[百宝箱文案] LaunchCountHasPCLTCS={launchCountMsg.Contains("PCL TCS")}; " +
                  $"LaunchCountHasPCLCE={launchCountMsg.Contains("PCL CE")}; ShortcutFileName={shortcutName}; " +
                  $"DonateHasPCLTCS={donateMsg.Contains("PCL TCS")}; DonateHasPCLCE={donateMsg.Contains("PCL CE")}; " +
                  $"CleanTipHasPCLTCS={cleanTip.Contains("PCL TCS")}; ServiceName={serviceName}");

            // 10. 返回启动页
            main.PageChange(FormMain.PageType.Launch);
            Write($"[返回] 已请求切换，等待生效：{await WaitForPage(FormMain.PageType.Launch, 20000)}, 启动页左栏={main.PanMainLeft.ActualWidth:0}");
            Write("================ TCS 自检结束 ================");
        }
        catch (Exception ex)
        {
            Write("自检异常：" + ex);
        }
    }

    // 轮询等待页面切换完成（避免 UI 线程延迟导致的时序错乱）
    private static async Task<bool> WaitForPage(FormMain.PageType page, int timeoutMs)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(200);
            if (ModMain.frmMain?.pageCurrent.page == page)
                return true;
        }

        return false;
    }

    // 采样当前页面，用于定位非预期的自动跳转
    private static async Task SamplePage(string tag, int durationMs)
    {
        var samples = new List<string>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < durationMs)
        {
            await Task.Delay(500);
            samples.Add($"{stopwatch.ElapsedMilliseconds}ms={ModMain.frmMain?.pageCurrent.page}");
        }

        Write($"[{tag}] 页面采样：" + string.Join(", ", samples));
    }
}
#endif
