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

            // 9g. 游戏选项页面（跨实例同步游戏内设置）
            main.PageChange(FormMain.PageType.GameOptions);
            Write($"[游戏选项] 已请求切换，等待生效：{await WaitForPage(FormMain.PageType.GameOptions, 20000)}");
            Write($"[游戏选项] pageCurrent={main.pageCurrent.page}, 右页={(ModMain.frmGameOptions is null ? "未创建" : "已创建")}, " +
                  $"左栏={(ModMain.frmGameOptionsLeft is null ? "未创建" : "已创建")}, " +
                  $"预览版本={ModGameOptions.PreviewVersion}(code={ModGameOptions.VersionCodeOf(ModGameOptions.PreviewVersion)}), " +
                  $"自定义项={ModGameOptions.Count}, 启动时同步={ModGameOptions.SyncOnLaunch}, 导入后套用={ModGameOptions.SyncAfterInstall}, " +
                  $"存储={ModGameOptions.StoragePath}");
            await Task.Delay(2500);
            if (ModMain.frmGameOptions is not null)
                Write($"[游戏选项] 右页尺寸={ModMain.frmGameOptions.ActualWidth:0}x{ModMain.frmGameOptions.ActualHeight:0}, " +
                      $"左栏尺寸={ModMain.frmGameOptionsLeft?.ActualWidth:0}x{ModMain.frmGameOptionsLeft?.ActualHeight:0}");
            var catalog = new List<string>();
            foreach (var group in ModGameOptions.Groups)
                catalog.Add($"{group.Id}={ModGameOptions.GetOptions(group.Id).Count}");
            Write($"[游戏选项目录] 分组数={ModGameOptions.Groups.Length}, 选项数={ModGameOptions.Options.Count}, " +
                  $"分布={string.Join(",", catalog)}");

            // 9h. 数值换算与版本兼容规则（纯逻辑，不依赖界面）
            var fovDef = ModGameOptions.GetDef("fov");
            var sensitivityDef = ModGameOptions.GetDef("mouseSensitivity");
            var capeDef = ModGameOptions.GetDef("modelPart_cape");
            var cloudDef = ModGameOptions.GetDef("renderClouds");
            var simulationDef = ModGameOptions.GetDef("simulationDistance");
            var graphicsDef = ModGameOptions.GetDef("graphicsMode");
            Write($"[换算] fov110@26.2={ModGameOptions.ToRaw(fovDef, "110", 260200)}; " +
                  $"mouse100@26.2={ModGameOptions.ToRaw(sensitivityDef, "100", 260200)}; " +
                  $"mouse100@1.12.2={ModGameOptions.ToRaw(sensitivityDef, "100", 11202)}; " +
                  $"fov回读110={ModGameOptions.ToCanonical(fovDef, "110", 260200)}; " +
                  $"mouse回读100={ModGameOptions.ToCanonical(sensitivityDef, "0.5", 260200)}");
            Write($"[兼容] cape键名@1.12.2={ModGameOptions.GetKeyFor(capeDef, 11202)}, cape键名@26.2={ModGameOptions.GetKeyFor(capeDef, 260200)}; " +
                  $"云@1.12.2={ModGameOptions.ToRaw(cloudDef, "2", 11202)}, 云@26.2={ModGameOptions.ToRaw(cloudDef, "2", 260200)}; " +
                  $"图形@1.12.2可选={ModGameOptions.GetDef("graphicsMode")?.GetChoices(11202)?.Length}, 图形@26.2可选={graphicsDef?.GetChoices(260200)?.Length}; " +
                  $"模拟距离@1.12.2支持={ModGameOptions.IsAvailable(simulationDef, 11202)}, 模拟距离@26.2支持={ModGameOptions.IsAvailable(simulationDef, 260200)}; " +
                  $"版本序数 1.12.2={ModGameOptions.VersionCodeOf("1.12.2")}, 1.21.11={ModGameOptions.VersionCodeOf("1.21.11")}, " +
                  $"26.2={ModGameOptions.VersionCodeOf("26.2")}, 26.4快照={ModGameOptions.VersionCodeOf("26.4-snapshot-1")}");

            // 9h2. 极值专用文案（最大帧率到顶应显示「无限制」）与滑动条范围（回归「最大值拖不到」）
            var fpsDef = ModGameOptions.GetDef("maxFps");
            Write($"[极值文案] 260@26.2={ModGameOptions.Describe(fpsDef, "260", 260200)}; " +
                  $"260@1.21.11={ModGameOptions.Describe(fpsDef, "260", 12111)}; " +
                  $"260@1.20.1={ModGameOptions.Describe(fpsDef, "260", 12001)}; " +
                  $"120@26.2={ModGameOptions.Describe(fpsDef, "120", 260200)}");
            var ranges = new List<string>();
            foreach (var key in new[] { "maxFps", "fov", "mouseSensitivity", "entityDistanceScaling", "renderDistance", "chatHeightUnfocused", "menuBackgroundBlurriness" })
            foreach (var entry in PageGameOptionsRight.SliderRangeDebug)
                if (entry.StartsWith(key + "=", StringComparison.Ordinal))
                    ranges.Add(entry);
            Write($"[滑动条范围] 记录={PageGameOptionsRight.SliderRangeDebug.Count} 条; 关键项: {string.Join(" | ", ranges.Distinct())}");

            // 9h3. fov 的两种表示法（26.x 存 0~1 归一值，旧版存度数）＋ 垂直同步开关
            var fovDef2 = ModGameOptions.GetDef("fov");
            var vsyncDef = ModGameOptions.GetDef("enableVsync");
            var modernFile = new Dictionary<string, string> { ["fov"] = "0.75", ["enableVsync"] = "false" };
            var legacyFile = new Dictionary<string, string> { ["fov"] = "70" };
            Write($"[fov] 110@26.2(新写法)={ModGameOptions.ToRaw(fovDef2, "110", 260200, true)}; " +
                  $"70@26.2(新写法)={ModGameOptions.ToRaw(fovDef2, "70", 260200, true)}; " +
                  $"30@26.2(新写法)={ModGameOptions.ToRaw(fovDef2, "30", 260200, true)}; " +
                  $"110@1.12.2(旧写法)={ModGameOptions.ToRaw(fovDef2, "110", 11202, false)}; " +
                  $"回读0.5@26.2={ModGameOptions.ToCanonical(fovDef2, "0.5", 260200, true)}; " +
                  $"回读70@1.12.2={ModGameOptions.ToCanonical(fovDef2, "70", 11202, false)}; " +
                  $"文件判据0.75={ModGameOptions.ResolveModernFormat(fovDef2, 0, modernFile, "fov")}, " +
                  $"文件判据70={ModGameOptions.ResolveModernFormat(fovDef2, 0, legacyFile, "fov")}");
            Write($"[垂直同步] 目录含enableVsync={(vsyncDef is not null)}; " +
                  $"true->{ModGameOptions.ToRaw(vsyncDef, "true", 260200)}; false->{ModGameOptions.ToRaw(vsyncDef, "false", 260200)}; " +
                  $"回读true={ModGameOptions.ToCanonical(vsyncDef, "true", 260200)}; " +
                  $"26.2支持={ModGameOptions.IsAvailable(vsyncDef, 260200)}");

            // 9h4. 回归：左栏复选框的内部变换不能被页面进入动画换成 TranslateTransform（否则点击必崩）
            if (ModMain.frmGameOptionsLeft is not null)
            {
                var leftBox = ModMain.frmGameOptionsLeft.CheckSyncLaunch;
                var shapeTransform = leftBox.ShapeCheck.RenderTransform;
                var borderTransform = leftBox.ShapeBorder.RenderTransform;
                Write($"[左栏复选框] 变换类型: ShapeCheck={(shapeTransform?.GetType().Name ?? "null")}, " +
                      $"ShapeBorder={(borderTransform?.GetType().Name ?? "null")}, " +
                      $"ScaleX={(shapeTransform is ScaleTransform sc ? sc.ScaleX : -1d):0.##}");
            }
            else
            {
                Write("[左栏复选框] 左栏未创建，跳过");
            }

            // 9j. 回归：整合包自带「精简 options.txt」（缺键）或首次启动（无文件）时都必须真的写入
            try
            {
                // 用正式档（%APPDATA%\PCLCE\game-options.json）里的真实自定义项做样本
                ModGameOptions.Overrides.Clear();
                var releaseOptions = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCLCE", "game-options.json");
                if (File.Exists(releaseOptions))
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                                 ModBase.ReadFile(releaseOptions),
                                 "\"([A-Za-z_][A-Za-z0-9_]*)\"\\s*:\\s*\"([^\"]*)\"\\s*(?=[,}])"))
                    {
                        var k = m.Groups[1].Value;
                        if (k is "syncOnLaunch" or "syncAfterInstall" or "previewVersion")
                            continue;
                        ModGameOptions.Overrides[k] = m.Groups[2].Value;
                    }

                var sampleCount = ModGameOptions.Overrides.Count;
                var emptyPlan = ModGameOptions.BuildPlan(260300, new Dictionary<string, string>()).Count;
                var curatedPlan = ModGameOptions.BuildPlan(260300, new Dictionary<string, string> { ["lang"] = "zh_cn",
                    ["key_forward"] = "key.keyboard.w" }).Count;
                Write($"[回归] 样本={sampleCount} 项；空文件计划={emptyPlan} 项；精简文件计划={curatedPlan} 项");

                var folder = Path.Combine(Path.GetTempPath(), "tcs-options-packcase");
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "options.txt"),
                    "lang:zh_cn" + Environment.NewLine + "key_forward:key.keyboard.w" + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
                var packCase = ModGameOptions.ApplyToFolder(folder, "精简整合包", false);
                var packBack = ModGameOptions.ReadOptionsFile(Path.Combine(folder, "options.txt"));
                Write($"[回归] 精简 options.txt 套用：写入 {packCase.Applied.Count} 项，跳过 {packCase.Skipped.Count} 项；" +
                      $"回读 {packBack.Count} 键：maxFps={packBack.GetValueOrDefault("maxFps")}, " +
                      $"fov={packBack.GetValueOrDefault("fov")}, enableVsync={packBack.GetValueOrDefault("enableVsync")}, " +
                      $"原有键保留 lang={packBack.GetValueOrDefault("lang")}, key_forward={packBack.GetValueOrDefault("key_forward")}");
                Directory.Delete(folder, true);
                ModGameOptions.Overrides.Clear();
            }
            catch (Exception ex2)
            {
                Write($"[回归] 失败: {ex2.GetType().Name}: {ex2.Message}");
            }

            // 9k. 回归：规则的导出 / 导入（含同名覆盖与错误文件校验）
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "tcs-rules-selftest");
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                var rulesPath = Path.Combine(folder, "rules.json");

                ModGameOptions.Overrides.Clear();
                ModGameOptions.CustomRaw.Clear();
                var emptyPath = Path.Combine(folder, "empty.json");
                var emptyExport = ModGameOptions.ExportRules(emptyPath);
                Write($"[规则] 无自定义项时导出被拒={emptyExport.Error is not null}，未创建文件={!File.Exists(emptyPath)}");

                ModGameOptions.Overrides["maxFps"] = "260";
                ModGameOptions.Overrides["fov"] = "110";
                ModGameOptions.Overrides["enableVsync"] = "false";
                ModGameOptions.CustomRaw["key_forward"] = "key.keyboard.w";
                var exported = ModGameOptions.ExportRules(rulesPath);
                var jsonText = ModBase.ReadFile(rulesPath);
                Write($"[规则] 导出 {exported.Count} 项（成功={exported.Error is null}）；文件含类型标记={jsonText.Contains(ModGameOptions.RulesType)}；" +
                      $"含 overrides={jsonText.Contains("\"overrides\"")} 含 customRaw={jsonText.Contains("\"customRaw\"")}");

                ModGameOptions.Overrides.Clear();
                ModGameOptions.CustomRaw.Clear();
                var peek = ModGameOptions.PeekRules(rulesPath);
                Write($"[规则] 预读：{peek.Count} 项，来自版本={peek.ExportVersion}，成功={peek.Error is null}");
                var imported = ModGameOptions.ImportRules(rulesPath);
                Write($"[规则] 导入 {imported.Count} 项 → 现在共 {ModGameOptions.Count} 项；" +
                      $"maxFps={ModGameOptions.Overrides.GetValueOrDefault("maxFps")} fov={ModGameOptions.Overrides.GetValueOrDefault("fov")} " +
                      $"enableVsync={ModGameOptions.Overrides.GetValueOrDefault("enableVsync")} key_forward={ModGameOptions.CustomRaw.GetValueOrDefault("key_forward")}");

                ModGameOptions.Overrides["guiScale"] = "3";
                ModGameOptions.Overrides["maxFps"] = "60";
                var again = ModGameOptions.ImportRules(rulesPath);
                Write($"[规则] 重复导入覆盖：写 {again.Count} 项，maxFps={ModGameOptions.Overrides.GetValueOrDefault("maxFps")}（应为 260），" +
                      $"原有 guiScale 保留={ModGameOptions.Overrides.GetValueOrDefault("guiScale")}（应为 3）");

                var badPath = Path.Combine(folder, "bad.json");
                ModBase.WriteFile(badPath, "{ this is not json ");
                var wrongPath = Path.Combine(folder, "wrong.json");
                ModBase.WriteFile(wrongPath, "{\"type\":\"SomethingElse\",\"overrides\":{\"maxFps\":\"60\"}}");
                Write($"[规则] 坏 JSON 被拒={ModGameOptions.PeekRules(badPath).Error is not null}；" +
                      $"类型不符被拒={ModGameOptions.PeekRules(wrongPath).Error is not null}；" +
                      $"文件不存在被拒={ModGameOptions.PeekRules(Path.Combine(folder, "nope.json")).Error is not null}");

                ModGameOptions.Overrides.Clear();
                ModGameOptions.CustomRaw.Clear();
                Directory.Delete(folder, true);
            }
            catch (Exception ex3)
            {
                Write($"[规则] 失败: {ex3.GetType().Name}: {ex3.Message}");
            }

            // 9l. 「功能隐藏」支持主界面「联机」与「游戏选项」
            try
            {
                var hideConf = Config.Preference.Hide;
                var oldLink = hideConf.PageLink;
                var oldGameOptions = hideConf.PageGameOptions;
                hideConf.PageLink = true;
                hideConf.PageGameOptions = true;
                PageSetupUI.HiddenRefresh();
                Write($"[功能隐藏] 隐藏后：联机={ModMain.frmMain.BtnTitleSelect7.Visibility}, 游戏选项={ModMain.frmMain.BtnTitleSelect8.Visibility}");
                hideConf.PageLink = oldLink;
                hideConf.PageGameOptions = oldGameOptions;
                PageSetupUI.HiddenRefresh();
                Write($"[功能隐藏] 还原后：联机={ModMain.frmMain.BtnTitleSelect7.Visibility}, 游戏选项={ModMain.frmMain.BtnTitleSelect8.Visibility}");
            }
            catch (Exception ex4)
            {
                Write($"[功能隐藏] 失败: {ex4.GetType().Name}: {ex4.Message}");
            }
            // 9m. 多人游戏：服务器列表存储 / 地址规范化 / 启动入口
            try
            {
                ModServerList.EnsureLoaded();
                var beforeCount = ModServerList.Items.Count;
                var noPort = ModServerList.NormalizeAddress("mc.example.com");
                var withScheme = ModServerList.NormalizeAddress("http://1.2.3.4:25566/");
                var split = ModServerList.SplitAddress("play.test.cn:25580");
                var entry = ModServerList.Add("__selftest__", "127.0.0.1:25599", "");
                var savedOk = File.Exists(ModServerList.StoragePath);
                var hasEntry = ModServerList.Items.Contains(entry);
                ModServerList.Remove(entry);
                Write($"[多人游戏] 条目={beforeCount}→{ModServerList.Items.Count}; 默认端口规范化={noPort}; 带协议={withScheme}; 拆分={split.Host}/{split.Port}; 落盘={savedOk}; 加入成功={hasEntry}; 存储={ModServerList.StoragePath}");
            }
            catch (Exception ex6)
            {
                Write($"[多人游戏] 失败: {ex6.GetType().Name}: {ex6.Message}");
            }
            // 9m2. 多人游戏页装配（真正切到该页，XAML 若有问题会在这里炸）
            try
            {
                main.PageChange(FormMain.PageType.Link);
                await Task.Delay(1200);
                var left = ModMain.frmMultiplayerLeft;
                Write($"[多人游戏页] 左栏={ModMain.frmMultiplayerLeft is not null}; 宿主={ModMain.frmMultiplayerHost is not null}; 子页={ModMain.frmMultiplayerHost?.Current}; 服务器条目={ModServerList.Items.Count}");
                main.PageChange(FormMain.PageType.Launch);
                await Task.Delay(600);
            }
            catch (Exception ex7)
            {
                Write($"[多人游戏页] 左栏={ModMain.frmMultiplayerLeft is not null}; 宿主={ModMain.frmMultiplayerHost is not null}; 子页={ModMain.frmMultiplayerHost?.Current}; 服务器条目={ModServerList.Items.Count}");
            }
            // 9i. 极值文案与真实写入验证
            try
            {
                var tempFolder = Path.Combine(Path.GetTempPath(), "tcs-options-selftest");
                if (Directory.Exists(tempFolder))
                    Directory.Delete(tempFolder, true);
                Directory.CreateDirectory(tempFolder);
                var tempOptions = Path.Combine(tempFolder, "options.txt");
                File.WriteAllText(tempOptions,
                    string.Join(Environment.NewLine, "fov:0.5", "maxFps:120", "enableVsync:false",
                        "mouseSensitivity:0.5", "lang:en_us", "modelPart_cape:true", "chatOpacity:1.0",
                        "gamma:0.0") + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
                ModGameOptions.Overrides["fov"] = "110";
                ModGameOptions.Overrides["mouseSensitivity"] = "100";
                ModGameOptions.Overrides["simulationDistance"] = "12";
                ModGameOptions.Overrides["enableVsync"] = "true";
                ModGameOptions.CustomRaw["maxFps"] = "260";
                var writeResult = ModGameOptions.ApplyToFolder(tempFolder, "selftest", true);
                var readBack = ModGameOptions.ReadOptionsFile(tempOptions);
                Write($"[真实写入] 写入={writeResult.Applied.Count}({string.Join(",", writeResult.Applied)}), " +
                      $"跳过={writeResult.Skipped.Count}({string.Join(",", writeResult.Skipped)}); " +
                      $"回读 fov={readBack.GetValueOrDefault("fov")}, enableVsync={readBack.GetValueOrDefault("enableVsync")}, " +
                      $"maxFps={readBack.GetValueOrDefault("maxFps")}, mouseSensitivity={readBack.GetValueOrDefault("mouseSensitivity")}, " +
                      $"其它键保留: lang={readBack.GetValueOrDefault("lang")}, chatOpacity={readBack.GetValueOrDefault("chatOpacity")}, " +
                      $"gamma={readBack.GetValueOrDefault("gamma")}; 行数={readBack.Count}");
                ModGameOptions.Overrides.Clear();
                ModGameOptions.CustomRaw.Clear();
                Directory.Delete(tempFolder, true);
                Write("[真实写入] 已完成并清理临时目录");
            }
            catch (Exception ex)
            {
                Write("[真实写入] 失败：" + ex);
            }

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
