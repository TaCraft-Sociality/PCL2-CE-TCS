using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using static PCL.ModGameOptions;

namespace PCL;

/// <summary>
///     「游戏选项」页面右侧内容：按游戏内「Esc → 选项」的顺序列出可同步的项目，
///     用户调整过的项目会成为自定义项，启动/导入整合包时自动套用到实例的 options.txt。
/// </summary>
public partial class PageGameOptionsRight
{
    private readonly List<OptionRow> _rows = [];
    private readonly DispatcherTimer _saveTimer;

    /// <summary>自检用：记录每个滑动条实际设置的范围（用于回归「最大值拖不到」这类问题）。</summary>
    public static readonly List<string> SliderRangeDebug = [];

    /// <summary>参考实例（当前选中实例）的 options.txt 内容，用于展示「游戏内当前值」。</summary>
    private Dictionary<string, string> _reference = new(StringComparer.Ordinal);

    private bool _suppress;

    public PageGameOptionsRight()
    {
        InitializeComponent();
        EnsureLoaded();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
            RefreshSummary();
            RefreshSavedText();
        };
        BuildPreviewCombo();
        RefreshAll();
    }

    /// <summary>预览版本的版本序数。</summary>
    private int PreviewCode => VersionCodeOf(PreviewVersion);

    /// <summary>当前用于展示「游戏内当前值」的实例。</summary>
    private McInstance ReferenceInstance => ModInstanceList.McMcInstanceSelected;

    #region 构建

    private void BuildPreviewCombo()
    {
        _suppress = true;
        ComboPreview.Items.Clear();
        var versions = GetPreviewVersions();
        foreach (var version in versions)
            ComboPreview.Items.Add(new MyComboBoxItem { Content = version });
        var index = versions.IndexOf(PreviewVersion);
        ComboPreview.SelectedIndex = index < 0 ? 0 : index;
        _suppress = false;
    }

    /// <summary>重建全部内容。</summary>
    public void RefreshAll()
    {
        try
        {
            _reference = ReadOptionsFile(Path.Combine(ReferenceInstance?.PathIndie ?? "", "options.txt"));
            _rows.Clear();
            PanGroupsLeft.Children.Clear();
            PanGroupsRight.Children.Clear();
            var split = (Groups.Length + 1) / 2;
            for (var i = 0; i < Groups.Length; i++)
            {
                var panel = i < split ? PanGroupsLeft : PanGroupsRight;
                panel.Children.Add(BuildGroupBar(Groups[i]));
            }

            RefreshSummary();
            RefreshSavedText();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[GameOptions] 构建游戏选项页面失败", ModBase.LogLevel.Hint);
        }
    }

    /// <summary>构建一个分组的折叠栏。</summary>
    private MyCollapseBar BuildGroupBar(GameOptionGroup group)
    {
        var bar = new MyCollapseBar { Title = group.Name, IsCollapsed = true };
        var defs = GetOptions(group.Id);
        if (defs.Count == 0)
        {
            bar.ContentPanel.Children.Add(new TextBlock
            {
                Text = group.Hint ?? "",
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 12, 6)
            });
            return bar;
        }

        foreach (var def in defs)
        {
            var row = BuildOptionRow(def, true);
            _rows.Add(row);
            bar.ContentPanel.Children.Add(row.Root);
        }

        return bar;
    }

    private sealed class OptionRow
    {
        public MyCheckBox Check;
        public MyComboBox Combo;
        public GameOptionDef Def;
        public TextBlock FollowText;
        public FrameworkElement Root;
        public MySlider Slider;
        public TextBlock Value;
        public MyTextButton FollowButton;
    }

    /// <summary>构建一行选项。</summary>
    private OptionRow BuildOptionRow(GameOptionDef def, bool withBackground)
    {
        var row = new OptionRow { Def = def };
        var grid = new Grid { Margin = new Thickness(0, withBackground ? 5 : 6, 0, withBackground ? 5 : 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = GetName(def),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (!string.IsNullOrEmpty(def.Note))
            name.ToolTip = def.Note;
        grid.Children.Add(name);

        row.Value = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 0),
            MinWidth = 46,
            TextAlignment = TextAlignment.Right
        };
        row.Value.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3");
        Grid.SetColumn(row.Value, 2);
        grid.Children.Add(row.Value);

        row.FollowButton = new MyTextButton
        {
            Text = Lang.Text("GameOptions.Options.FollowGame"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 2, 6, 2),
            Visibility = Visibility.Collapsed
        };
        row.FollowButton.Click += (_, _) => ClearOverride(def);
        Grid.SetColumn(row.FollowButton, 3);
        grid.Children.Add(row.FollowButton);

        row.FollowText = new TextBlock
        {
            Text = Lang.Text("GameOptions.Options.FollowGame"),
            FontSize = 12,
            Opacity = 0.4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(row.FollowText, 3);
        grid.Children.Add(row.FollowText);

        switch (def.Kind)
        {
            case GameOptionKind.Toggle:
            {
                row.Check = new MyCheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                row.Check.Change += (_, _) => SetOverride(def, row.Check.Checked == true ? "true" : "false");
                Grid.SetColumn(row.Check, 1);
                grid.Children.Add(row.Check);
                break;
            }
            case GameOptionKind.Choice:
            {
                row.Combo = new MyComboBox
                {
                    Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MinWidth = 150
                };
                row.Combo.SelectionChanged += (_, _) =>
                {
                    if (_suppress || row.Combo.SelectedIndex < 0)
                        return;
                    SetOverride(def, row.Combo.SelectedIndex.ToString());
                };
                Grid.SetColumn(row.Combo, 1);
                grid.Children.Add(row.Combo);
                break;
            }
            default:
            {
                row.Slider = new MySlider
                {
                    // 滑动条本身只支持 0~MaxValue，规范值区间靠 def.Min 偏移，必须显式设置上限
                    MaxValue = def.Max - def.Min,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                SliderRangeDebug.Add($"{def.Key}={def.Min}~{def.Max}(maxValue={row.Slider.MaxValue})");
                row.Slider.getHintText = new Func<int, string>(value => SliderHint(def, value));
                row.Slider.Change += (_, _) =>
                {
                    if (_suppress)
                        return;
                    var canonical = SliderCanonical(def, row.Slider.Value).ToString();
                    SetOverride(def, canonical, true);
                    row.Value.Text = Describe(def, canonical, PreviewCode);
                };
                Grid.SetColumn(row.Slider, 1);
                grid.Children.Add(row.Slider);
                break;
            }
        }

        row.Root = grid;
        UpdateRow(row);
        return row;
    }

    #endregion

    #region 刷新

    /// <summary>刷新行的显示状态。</summary>
    private void UpdateRow(OptionRow row)
    {
        var def = row.Def;
        var code = PreviewCode;
        var realKey = GetKeyFor(def, code);
        var available = IsAvailable(def, code, _reference);
        var modern = ResolveModernFormat(def, code, _reference, realKey);
        var overridden = Overrides.TryGetValue(def.Key, out var canonical);
        if (!overridden)
            canonical = ToCanonical(def,
                _reference.TryGetValue(realKey, out var reference) ? reference : null, code, modern);
        canonical ??= GetDefault(row.Def);

        _suppress = true;
        try
        {
            if (row.Slider is not null && canonical is not null)
                row.Slider.Value = Math.Clamp(int.TryParse(canonical, out var value) ? value : def.Min, def.Min, def.Max)
                                   - def.Min;
            if (row.Check is not null && canonical is not null)
                row.Check.SetChecked(canonical == "true", false);
            if (row.Combo is not null)
            {
                row.Combo.Items.Clear();
                var choices = def.GetChoices(code) ?? [];
                foreach (var choice in choices)
                    row.Combo.Items.Add(new MyComboBoxItem { Content = choice });
                var index = canonical is not null && int.TryParse(canonical, out var raw) ? raw : 0;
                if (choices.Length > 0)
                    row.Combo.SelectedIndex = Math.Clamp(index, 0, choices.Length - 1);
            }
        }
        finally
        {
            _suppress = false;
        }

        FrameworkElement control = row.Slider is not null
            ? row.Slider
            : row.Check is not null
                ? row.Check
                : row.Combo;
        control.IsEnabled = available;
        row.Value.Text = available
            ? canonical is null
                ? "—"
                : Describe(def, canonical, code)
            : Lang.Text("GameOptions.Options.Unsupported");
        row.Value.Opacity = overridden ? 1d : 0.55d;
        row.FollowButton.Visibility = overridden ? Visibility.Visible : Visibility.Collapsed;
        row.FollowText.Visibility = overridden ? Visibility.Collapsed : Visibility.Visible;
        control.Opacity = available ? overridden ? 1d : 0.7d : 0.45d;
    }

    /// <summary>只刷新选项卡里的自定义项列表（避免重建正在操作的控件）。</summary>
    private void RefreshSummary()
    {
        PanOptionRows.Children.Clear();
        var keys = Overrides.Keys.Concat(CustomRaw.Keys).ToList();
        LabCustomCount.Text = Lang.Text("GameOptions.Options.Custom", keys.Count);
        if (keys.Count == 0)
        {
            PanOptionRows.Children.Add(new TextBlock
            {
                Text = Lang.Text("GameOptions.Options.Empty"),
                FontSize = 12,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var key in keys)
        {
            var def = GetDef(key);
            if (def is null)
            {
                PanOptionRows.Children.Add(BuildRawRow(key, CustomRaw[key]));
                continue;
            }

            var row = BuildOptionRow(def, false);
            PanOptionRows.Children.Add(row.Root);
        }
    }

    /// <summary>手动添加的键值行。</summary>
    private FrameworkElement BuildRawRow(string key, string raw)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = key,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        var value = new TextBlock
        {
            Text = raw,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 0)
        };
        value.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3");
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        var follow = new MyTextButton
        {
            Text = Lang.Text("GameOptions.Options.FollowGame"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 2, 6, 2)
        };
        follow.Click += (_, _) =>
        {
            CustomRaw.Remove(key);
            Save();
            RefreshAll();
        };
        Grid.SetColumn(follow, 3);
        grid.Children.Add(follow);
        return grid;
    }

    private void RefreshSavedText()
    {
        var sync = SyncOnLaunch
            ? Lang.Text("GameOptions.Status.SyncOn")
            : Lang.Text("GameOptions.Status.SyncOff");
        LabSaved.Text = Lang.Text("GameOptions.Status.Saved") + " · " + sync;
    }

    #endregion

    #region 操作

    private void SetOverride(GameOptionDef def, string canonical, bool delayed = false)
    {
        if (_suppress)
            return;
        Overrides[def.Key] = canonical;
        foreach (var row in _rows.Where(r => r.Def.Key == def.Key))
            UpdateRow(row);
        if (delayed)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        else
        {
            Save();
            RefreshSummary();
            RefreshSavedText();
        }
    }

    private void ClearOverride(GameOptionDef def)
    {
        Overrides.Remove(def.Key);
        Save();
        RefreshAll();
        HintService.Hint(Lang.Text("GameOptions.Tip.Followed", GetName(def)), HintType.Success);
    }

    private void ComboPreview_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ComboPreview.SelectedItem is not MyComboBoxItem item || item.Content is not string version)
            return;
        if (version == PreviewVersion)
            return;
        PreviewVersion = version;
        Save();
        RefreshAll();
        HintService.Hint(Lang.Text("GameOptions.Tip.Preview", version), HintType.Info);
    }

    /// <summary>由左侧栏的「同步」操作调用，用于刷新显示。</summary>
    public void OnSyncChanged()
    {
        RefreshAll();
    }

    #endregion

    #region 默认值

    /// <summary>滑动条气泡提示文本（顶格显示「无限制」这类专用文案）。</summary>
    private static string SliderHint(GameOptionDef def, int sliderValue)
        => GetSpecialLabel(def, (def.Min + sliderValue).ToString(), 0)
           ?? def.Min + sliderValue + def.Unit;

    /// <summary>滑动条位置 → 规范值；顶格附近吸附到最大值，保证无限制这类极值一定可达。</summary>
    private static int SliderCanonical(GameOptionDef def, int sliderValue)
    {
        var value = def.Min + sliderValue;
        if (sliderValue >= def.Max - def.Min - 1)
            value = def.Max;
        return Math.Clamp(value, def.Min, def.Max);
    }

    /// <summary>游戏内默认值（规范值），用于实例还没启动过时展示。</summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        ["modelPart_cape"] = "true", ["modelPart_jacket"] = "true", ["modelPart_left_sleeve"] = "true",
        ["modelPart_right_sleeve"] = "true", ["modelPart_left_pants_leg"] = "true",
        ["modelPart_right_pants_leg"] = "true", ["modelPart_hat"] = "true", ["mainHand"] = "0",
        ["graphicsMode"] = "1", ["renderDistance"] = "12", ["simulationDistance"] = "12", ["maxFps"] = "120",
        ["ao"] = "true", ["renderClouds"] = "2", ["entityShadows"] = "true", ["entityDistanceScaling"] = "100",
        ["bobView"] = "true", ["gamma"] = "50", ["guiScale"] = "0", ["particles"] = "0", ["fov"] = "70",
        ["fovEffectScale"] = "100", ["screenEffectScale"] = "100", ["darknessEffectScale"] = "100",
        ["pauseOnLostFocus"] = "true", ["lang"] = "0",
        ["soundCategory_master"] = "100", ["soundCategory_music"] = "100", ["soundCategory_record"] = "100",
        ["soundCategory_weather"] = "100", ["soundCategory_block"] = "100", ["soundCategory_hostile"] = "100",
        ["soundCategory_neutral"] = "100", ["soundCategory_player"] = "100", ["soundCategory_ambient"] = "100",
        ["soundCategory_voice"] = "100", ["soundCategory_village"] = "100", ["showSubtitles"] = "false",
        ["mouseSensitivity"] = "100", ["invertYMouse"] = "false", ["autoJump"] = "true",
        ["toggleCrouch"] = "false", ["toggleSprint"] = "false",
        ["chatVisibility"] = "0", ["chatOpacity"] = "100", ["textBackgroundOpacity"] = "100", ["chatScale"] = "100",
        ["chatWidth"] = "100", ["chatHeightFocused"] = "100", ["chatHeightUnfocused"] = "0",
        ["chatLineSpacing"] = "0", ["chatColors"] = "true", ["chatLinks"] = "true", ["chatLinksPrompt"] = "true",
        ["autoSuggestions"] = "true", ["reducedDebugInfo"] = "false",
        ["narrator"] = "0", ["damageTiltStrength"] = "100", ["menuBackgroundBlurriness"] = "5",
        ["narratorHotkey"] = "true", ["highContrast"] = "false",
        ["enableVsync"] = "false", ["graphicsPreset"] = "1", ["improvedTransparency"] = "false",
        ["vignette"] = "true", ["hideLightningFlashes"] = "false", ["biomeBlendRadius"] = "2",
        ["mipmapLevels"] = "4", ["glintStrength"] = "75", ["glintSpeed"] = "50",
        ["soundCategory_ui"] = "100", ["directionalAudio"] = "false",
        ["invertXMouse"] = "false", ["toggleAttack"] = "false", ["toggleUse"] = "false",
        ["rawMouseInput"] = "true", ["discrete_mouse_scroll"] = "false", ["attackIndicator"] = "1",
        ["backgroundForChatOnly"] = "true", ["chatDelay"] = "0", ["hideServerAddress"] = "false",
        ["hideMatchedNames"] = "true", ["onlyShowSecureChat"] = "false", ["telemetryOptInExtra"] = "false"
    };

    private static string GetDefault(GameOptionDef def)
        => Defaults.TryGetValue(def.Key, out var value)
            ? value
            : def.Kind switch
            {
                GameOptionKind.Toggle => "false",
                GameOptionKind.Choice => "0",
                _ => def.Min.ToString()
            };

    #endregion
}
