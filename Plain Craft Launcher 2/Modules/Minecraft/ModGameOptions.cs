using System.IO;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     游戏选项（options.txt）的控件类型。
/// </summary>
public enum GameOptionKind
{
    Slider,
    Toggle,
    Choice
}

/// <summary>
///     单个游戏选项的定义。
///     启动器内部一律使用「规范值」存储（百分比、度数等），写入时按目标版本的格式换算成游戏原始值。
/// </summary>
public class GameOptionDef
{
    public GameOptionDef(string key, string group, GameOptionKind kind, int min = 0, int max = 100,
        double factor = 1d, string unit = "", int minCode = 0, int maxCode = int.MaxValue, string legacyKey = null)
    {
        Key = key;
        Group = group;
        Kind = kind;
        Min = min;
        Max = max;
        Factor = factor;
        Unit = unit;
        MinCode = minCode;
        MaxCode = maxCode;
        LegacyKey = legacyKey;
    }

    /// <summary>options.txt 中的键名。</summary>
    public string Key { get; }

    /// <summary>所属分组 ID。</summary>
    public string Group { get; }

    public GameOptionKind Kind { get; }

    /// <summary>规范值下限（滑动条用）。</summary>
    public int Min { get; }

    /// <summary>规范值上限（滑动条用）。</summary>
    public int Max { get; }

    /// <summary>规范值 × Factor = 游戏原始值。</summary>
    public double Factor { get; }

    /// <summary>规范值 → 原始值时的偏移：raw = (canonical - Offset) × Factor。</summary>
    public double Offset { get; set; }

    /// <summary>新版表示法下的乘数（不填则与 Factor 相同）。</summary>
    public double ModernFactor { get; set; }

    /// <summary>新版表示法下的偏移。</summary>
    public double ModernOffset { get; set; }

    /// <summary>
    ///     该选项在版本间换过表示法（如 1.21+ 的 fov 由度数改成 0~1 归一值）：
    ///     为真时优先按目标 options.txt 里已有的值判断用哪种表示法。
    /// </summary>
    public bool AutoFormatFromFile { get; set; }

    /// <summary>是否使用新版表示法。</summary>
    public bool UseModernFormat(int versionCode, bool? fileIsFractional = null)
    {
        if (ModernFactor <= 0d)
            return false;
        if (fileIsFractional.HasValue)
            return fileIsFractional.Value;
        return versionCode <= 0 || ModernFromCode <= 0 || versionCode >= ModernFromCode;
    }

    /// <summary>取实际使用的乘数与偏移。</summary>
    public (double factor, double offset) GetScale(bool modern)
        => modern && ModernFactor > 0d ? (ModernFactor, ModernOffset) : (Factor, Offset);

    /// <summary>展示单位，如 ° / % / 格。</summary>
    public string Unit { get; }

    /// <summary>支持该选项的最低版本（版本序数）。</summary>
    public int MinCode { get; }

    /// <summary>支持该选项的最高版本（版本序数）。</summary>
    public int MaxCode { get; }

    /// <summary>旧版本中的键名（如 cape → modelPart_cape）。</summary>
    public string LegacyKey { get; }

    /// <summary>切换到新版键名 / 数值的版本序数。</summary>
    public int ModernFromCode { get; set; }

    /// <summary>新版菜单名称。</summary>
    public string[] Choices { get; set; }

    /// <summary>新版菜单对应的原始值。</summary>
    public string[] ChoiceRaw { get; set; }

    /// <summary>旧版菜单名称。</summary>
    public string[] OldChoices { get; set; }

    /// <summary>旧版菜单对应的原始值。</summary>
    public string[] OldChoiceRaw { get; set; }

    /// <summary>该选项的补充说明（显示在选项行里）。</summary>
    public string Note { get; set; }

    /// <summary>按版本取键名。</summary>
    public string GetKey(int versionCode)
        => LegacyKey is not null && ModernFromCode > 0 && versionCode > 0 && versionCode < ModernFromCode
            ? LegacyKey
            : Key;

    /// <summary>按版本取菜单名称，非选择类返回 null。</summary>
    public string[] GetChoices(int versionCode)
        => Choices is null ? null : UseOld(versionCode) && OldChoices is not null ? OldChoices : Choices;

    /// <summary>按版本取菜单原始值。</summary>
    public string[] GetChoiceRaw(int versionCode)
        => ChoiceRaw is null ? null : UseOld(versionCode) && OldChoiceRaw is not null ? OldChoiceRaw : ChoiceRaw;

    private bool UseOld(int versionCode)
        => ModernFromCode > 0 && versionCode > 0 && versionCode < ModernFromCode;
}

/// <summary>
///     选项分组。
/// </summary>
public class GameOptionGroup
{
    public string Hint;
    public string Id;
    public string Name;
}

/// <summary>
///     一次同步的结果。
/// </summary>
public class GameOptionApplyResult
{
    public readonly List<string> Applied = [];
    public Exception Error;
    public string InstanceName = "";
    public string OptionsPath = "";
    public readonly List<string> Skipped = [];
    public string VersionName = "";

    public string Summary => $"{InstanceName}({VersionName}) 写入 {Applied.Count} 项"
                             + (Skipped.Count > 0 ? $"，跳过 {Skipped.Count} 项" : "");
}

/// <summary>
///     一次规则导出或导入的结果。
/// </summary>
public class GameOptionRulesResult
{
    /// <summary>导出的项数（导入时为实际写入的项数）。</summary>
    public int Count;

    /// <summary>失败原因，成功时为 null。</summary>
    public Exception Error;

    /// <summary>导出方在文件里记录的导出时间。</summary>
    public string ExportedAt;

    /// <summary>导入的文件里记录的导出方预览版本（仅用于提示）。</summary>
    public string ExportVersion;

    /// <summary>导入的文件里记录的启动器名称。</summary>
    public string Launcher;

    /// <summary>规则文件路径。</summary>
    public string Path = "";
}

/// <summary>
///     「游戏选项」功能的核心：保存用户调好的游戏内设置，并套用到任意整合包实例的 options.txt。
///     只写入用户明确改动过的键，其余设置保持游戏内原样。
/// </summary>
public static class ModGameOptions
{
    private const string FileName = "game-options.json";

    /// <summary>选项的本地化名称（键 → 名称）。</summary>
    public static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, GameOptionGroup> GroupsById = new(StringComparer.Ordinal);

    /// <summary>用户明确改动过的键：键名 → 规范值（滑动条为整数、开关为 true/false、菜单为下标）。</summary>
    public static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal);

    /// <summary>手动添加的键：键名 → 原始值（原样写入）。</summary>
    public static readonly Dictionary<string, string> CustomRaw = new(StringComparer.Ordinal);

    private static bool _loaded;

    /// <summary>启动时同步（每次启动任意实例都套用）。</summary>
    public static bool SyncOnLaunch = true;

    /// <summary>导入整合包后自动套用。</summary>
    public static bool SyncAfterInstall = true;

    /// <summary>兼容性预览版本。</summary>
    public static string PreviewVersion = "26.2";

    /// <summary>设置文件位置。</summary>
    public static string StoragePath
    {
        get
        {
            var root = Paths.SharedData;
            if (string.IsNullOrEmpty(root))
                root = ModBase.exePath;
            return Path.Combine(root, FileName);
        }
    }

    public static int Count => Overrides.Count + CustomRaw.Count;

    #region 分组与选项目录

    public static readonly GameOptionGroup[] Groups =
    [
        new()
        {
            Id = "skin",
            Name = Tr("皮肤自定义", "面板自訂", "Skin Customization", "Skin Customisation")
        },
        new()
        {
            Id = "video",
            Name = Tr("视频设置", "視訊設定", "Video Settings", "Video Settings")
        },
        new()
        {
            Id = "language",
            Name = Tr("语言", "語言", "Language", "Language")
        },
        new()
        {
            Id = "resourcepack",
            Name = Tr("资源包", "資源包", "Resource Packs", "Resource Packs"),
            Hint = Tr(
                "资源包列表由实例内的 resourcepacks 文件夹与游戏内选择决定，启动器不改动，避免覆盖整合包自带的资源包顺序。",
                "資源包清單由實例內的 resourcepacks 資料夾與遊戲內選擇決定，啟動器不會更動，避免覆蓋整合包自帶的資源包順序。",
                "Resource pack selection is managed by the instance's resourcepacks folder and the in-game menu; the launcher leaves it untouched so modpack-provided packs keep their order.",
                "Resource pack selection is managed by the instance's resourcepacks folder and the in-game menu; the launcher leaves it untouched so modpack-provided packs keep their order.")
        },
        new()
        {
            Id = "telemetry",
            Name = Tr("遥测数据", "遙測資料", "Telemetry Data", "Telemetry Data"),
            Hint = Tr("只同步启动器写入的开关；更细的遥测项（如发送世界加载时间）请在游戏内「Esc → 选项 → 遥测数据」中设置。",
                "只同步啟動器寫入的開關；更細的遙測項（如傳送世界載入時間）請在遊戲內「Esc → 選項 → 遙測資料」中設定。",
                "Only the switches written by the launcher are synced; finer telemetry items are configured in-game via Esc → Options → Telemetry Data.",
                "Only the switches written by the launcher are synced; finer telemetry items are configured in-game via Esc → Options → Telemetry Data.")
        },
        new()
        {
            Id = "sound",
            Name = Tr("音乐和声音", "音樂與音效", "Music & Sounds", "Music & Sounds")
        },
        new()
        {
            Id = "controls",
            Name = Tr("控制", "控制", "Controls", "Controls")
        },
        new()
        {
            Id = "chat",
            Name = Tr("聊天设置", "聊天設定", "Chat Settings", "Chat Settings")
        },
        new()
        {
            Id = "accessibility",
            Name = Tr("辅助功能", "協助工具", "Accessibility", "Accessibility")
        },
        new()
        {
            Id = "credits",
            Name = Tr("鸣谢和著作权", "鳴謝與著作權", "Credits & Attribution", "Credits & Attribution"),
            Hint = Tr("该页面仅用于查看游戏版权信息，没有可同步的设置。",
                "該頁面僅用於檢視遊戲版權資訊，沒有可同步的設定。",
                "This screen only shows the game's copyright information; there is nothing to sync.",
                "This screen only shows the game's copyright information; there is nothing to sync.")
        }
    ];

    /// <summary>选项目录。规范值含义：滑动条为整数、开关为 true/false、菜单为下标。</summary>
    public static readonly List<GameOptionDef> Options = BuildCatalog();

    private static List<GameOptionDef> BuildCatalog()
    {
        var list = new List<GameOptionDef>();
        foreach (var group in Groups)
            GroupsById[group.Id] = group;

        void Add(GameOptionDef def, string zh, string tw, string en)
        {
            list.Add(def);
            Named[def.Key] = Tr(zh, tw, en, en);
        }

        void Slider(string key, string group, string zh, string tw, string en, int min, int max, double factor,
            string unit, int minCode = 0, int maxCode = int.MaxValue, string note = null)
            => Add(new GameOptionDef(key, group, GameOptionKind.Slider, min, max, factor, unit, minCode, maxCode)
            {
                Note = note
            }, zh, tw, en);

        void Toggle(string key, string group, string zh, string tw, string en, int minCode = 0,
            int maxCode = int.MaxValue, string note = null, string legacyKey = null)
            => Add(new GameOptionDef(key, group, GameOptionKind.Toggle, minCode: minCode, maxCode: maxCode,
                legacyKey: legacyKey)
            {
                Note = note
            }, zh, tw, en);

        void Choice(string key, string group, string zh, string tw, string en, string[] choices, string[] raws,
            int minCode = 0, int maxCode = int.MaxValue, string note = null, int modernFromCode = 0,
            string[] oldChoices = null, string[] oldRaws = null)
            => Add(new GameOptionDef(key, group, GameOptionKind.Choice, minCode: minCode, maxCode: maxCode)
            {
                Choices = choices,
                ChoiceRaw = raws,
                Note = note,
                ModernFromCode = modernFromCode,
                OldChoices = oldChoices,
                OldChoiceRaw = oldRaws
            }, zh, tw, en);

        #region 皮肤自定义

        string[] SkinOnOff = [Tr("开启", "開啟", "On", "On"), Tr("关闭", "關閉", "Off", "Off")];
        string[] SkinOnOffRaw = ["true", "false"];
        SkinPart("modelPart_cape", "cape", "披风", "披風", "Cape");
        SkinPart("modelPart_jacket", "jacket", "外套", "外套", "Jacket");
        SkinPart("modelPart_left_sleeve", "leftSleeve", "左袖", "左袖", "Left Sleeve");
        SkinPart("modelPart_right_sleeve", "rightSleeve", "右袖", "右袖", "Right Sleeve");
        SkinPart("modelPart_left_pants_leg", "leftPants", "左裤腿", "左褲管", "Left Pants Leg");
        SkinPart("modelPart_right_pants_leg", "rightPants", "右裤腿", "右褲管", "Right Pants Leg");
        SkinPart("modelPart_hat", "hat", "帽子", "帽子", "Hat");
        Choice("mainHand", "skin", "惯用手", "慣用手", "Main Hand",
            [Tr("右手", "右手", "Right", "Right"), Tr("左手", "左手", "Left", "Left")], ["right", "left"]);

        void SkinPart(string key, string legacy, string zh, string tw, string en)
            => Toggle(key, "skin", zh, tw, en, note: Tr($"旧版本（1.12 及以下）键名为 {legacy}",
                $"舊版本（1.12 及以下）鍵名為 {legacy}", $"Legacy key on 1.12 and older: {legacy}",
                $"Legacy key on 1.12 and older: {legacy}"), legacyKey: legacy);

        #endregion

        #region 视频设置（按游戏内「Esc → 选项 → 视频设置」的顺序）

        Choice("graphicsMode", "video", "图形", "圖形", "Graphics",
            [Tr("流畅", "流暢", "Fast", "Fast"), Tr("高品质", "高品質", "Fancy", "Fancy"),
                Tr("极佳", "極佳", "Fabulous", "Fabulous")], ["0", "1", "2"],
            note: Tr("「极佳」需要 1.16 及以上；1.21.2 起由「图形预设」取代", "「極佳」需要 1.16 以上；1.21.2 起由「圖形預設」取代",
                "Fabulous needs 1.16+; replaced by the graphics preset in 1.21.2+",
                "Fabulous needs 1.16+; replaced by the graphics preset in 1.21.2+"), modernFromCode: VersionCodeOf("1.16"),
            maxCode: VersionCodeOf("1.21.1"),
            oldChoices: [Tr("流畅", "流暢", "Fast", "Fast"), Tr("高品质", "高品質", "Fancy", "Fancy")],
            oldRaws: ["0", "1"]);
        Choice("graphicsPreset", "video", "图形预设", "圖形預設", "Graphics Preset",
            [Tr("流畅", "流暢", "Fast", "Fast"), Tr("高品质", "高品質", "Fancy", "Fancy"),
                Tr("极佳", "極佳", "Fabulous", "Fabulous"), Tr("自定义", "自訂", "Custom", "Custom")],
            ["fast", "fancy", "fabulous", "custom"], VersionCodeOf("1.21.2"),
            note: Tr("游戏内单独改过画面项后会显示为「自定义」", "遊戲內單獨改過畫面項後會顯示為「自訂」",
                "Shows as Custom once individual graphics options were changed",
                "Shows as Custom once individual graphics options were changed"));
        Slider("renderDistance", "video", "渲染距离", "顯示距離", "Render Distance", 2, 32, 1d, " 格");
        Slider("simulationDistance", "video", "模拟距离", "模擬距離", "Simulation Distance", 5, 32, 1d, " 格",
            VersionCodeOf("1.18"));
        Slider("maxFps", "video", "最大帧率", "最大幀率", "Max Framerate", 10, 260, 1d, " FPS",
            note: Tr("拉到最大即「无限制」；1.21.1 及之前拉到最大是「垂直同步」", "拉到最大即「無限制」；1.21.1 及之前拉到最大是「垂直同步」",
                "Maximum means Unlimited; on 1.21.1 and older the maximum meant VSync",
                "Maximum means Unlimited; on 1.21.1 and older the maximum meant VSync"));
        Toggle("enableVsync", "video", "垂直同步", "垂直同步", "VSync", VersionCodeOf("1.21.2"),
            note: Tr("1.21.2 起与最大帧率分开的独立开关", "1.21.2 起與最大幀率分開的獨立開關",
                "Separate switch since 1.21.2", "Separate switch since 1.21.2"));
        Toggle("ao", "video", "平滑光照", "柔和光源", "Smooth Lighting");
        Choice("renderClouds", "video", "云", "雲", "Clouds",
            [Tr("关闭", "關閉", "Off", "Off"), Tr("流畅", "流暢", "Fast", "Fast"),
                Tr("高品质", "高品質", "Fancy", "Fancy")], ["false", "fast", "true"],
            note: Tr("「流畅」云需要 1.15 及以上", "「流暢」雲需要 1.15 以上", "Fast clouds need 1.15+",
                "Fast clouds need 1.15+"), modernFromCode: VersionCodeOf("1.15"),
            oldChoices: [Tr("关闭", "關閉", "Off", "Off"), Tr("开启", "開啟", "On", "On")],
            oldRaws: ["false", "true"]);
        Toggle("entityShadows", "video", "实体阴影", "實體陰影", "Entity Shadows");
        Slider("entityDistanceScaling", "video", "实体渲染距离", "實體顯示距離", "Entity Distance", 50, 500, 0.01d, "%",
            VersionCodeOf("1.17"));
        Toggle("bobView", "video", "视角摇晃", "視角晃動", "View Bobbing");
        Slider("gamma", "video", "亮度", "亮度", "Brightness", 0, 100, 0.01d, "%");
        Choice("guiScale", "video", "界面尺寸", "介面大小", "GUI Scale",
            [Tr("自动", "自動", "Auto", "Auto"), "1", "2", "3", "4"], ["0", "1", "2", "3", "4"],
            note: Tr("受窗口分辨率限制，过大的值可能无效", "受視窗解析度限制，過大的值可能無效",
                "Limited by window resolution", "Limited by window resolution"));
        Choice("particles", "video", "粒子效果", "粒子效果", "Particles",
            [Tr("全部", "全部", "All", "All"), Tr("减少", "減少", "Decreased", "Decreased"),
                Tr("最少", "最少", "Minimal", "Minimal")], ["0", "1", "2"]);
        Add(new GameOptionDef("fov", "video", GameOptionKind.Slider, 30, 110, 1d, "°")
        {
            // 游戏内范围恒为 30~110 度；1.21 起 options.txt 里存的是 0~1 归一值（0.5=70°默认、1.0=110°）
            ModernFromCode = VersionCodeOf("1.21"),
            ModernFactor = 1d / 80d,
            ModernOffset = 30d,
            AutoFormatFromFile = true,
            Note = Tr("游戏内范围为 30~110°，启动器会自动换算新旧两种写法",
                "遊戲內範圍為 30~110°，啟動器會自動換算新舊兩種寫法",
                "In-game range is 30-110 degrees; the launcher converts between both storage formats",
                "In-game range is 30-110 degrees; the launcher converts between both storage formats")
        }, "视场角", "視野角", "FOV");
        Slider("fovEffectScale", "video", "视场角效果", "視野角效果", "FOV Effects", 0, 100, 0.01d, "%",
            VersionCodeOf("1.16"));
        Slider("screenEffectScale", "video", "屏幕扭曲效果", "畫面扭曲效果", "Distortion Effects", 0, 100, 0.01d, "%",
            VersionCodeOf("1.16"));
        Slider("darknessEffectScale", "video", "黑暗效果", "黑暗效果", "Darkness Pulsing", 0, 100, 0.01d, "%",
            VersionCodeOf("1.19"));
        Toggle("pauseOnLostFocus", "video", "失去焦点时暂停", "失去焦點時暫停", "Pause on Lost Focus");
        Toggle("improvedTransparency", "video", "高品质透明", "高品質透明", "Improved Transparency",
            VersionCodeOf("1.21.2"));
        Toggle("vignette", "video", "屏幕暗角", "畫面暗角", "Vignette", VersionCodeOf("1.21.2"));
        Toggle("hideLightningFlashes", "video", "隐藏闪电闪烁", "隱藏閃電閃爍", "Hide Lightning Flashes",
            VersionCodeOf("1.20.5"));
        Slider("biomeBlendRadius", "video", "生物群系混色半径", "生態域混色半徑", "Biome Blend", 0, 7, 1d, "",
            note: Tr("0~7，越大过渡越柔和", "0~7，越大過渡越柔和", "0-7, higher blends more",
                "0-7, higher blends more"));
        Slider("mipmapLevels", "video", "多级渐远纹理", "多層遠近紋理", "Mipmap Levels", 0, 4, 1d, "");
        Slider("glintStrength", "video", "附魔光效强度", "附魔光效強度", "Glint Strength", 0, 100, 0.01d, "%",
            VersionCodeOf("1.21.2"));
        Slider("glintSpeed", "video", "附魔光效速度", "附魔光效速度", "Glint Speed", 0, 100, 0.01d, "%",
            VersionCodeOf("1.21.2"));

        #endregion

        #region 语言

        Choice("lang", "language", "游戏语言", "遊戲語言", "Game Language",
            [Tr("简体中文", "簡體中文", "简体中文", "简体中文"), Tr("繁體中文", "繁體中文", "繁體中文", "繁體中文"),
                "English (US)", "English (UK)", Tr("日语", "日語", "日本語", "日本語")],
            ["zh_cn", "zh_tw", "en_us", "en_gb", "ja_jp"],
            note: Tr("与启动器「自动设置游戏语言」同时开启时，以此处为准", "與啟動器「自動設定遊戲語言」同時開啟時，以此處為準",
                "Takes precedence over the launcher's auto language switch when both are set",
                "Takes precedence over the launcher's auto language switch when both are set"));

        #endregion

        #region 遥测数据

        Toggle("telemetryOptInExtra", "telemetry", "发送附加遥测数据", "傳送附加遙測資料", "Send Additional Telemetry",
            VersionCodeOf("1.18"),
            note: Tr("关闭后只发送最小化的匿名数据", "關閉後只傳送最小化的匿名資料",
                "When off, only minimal anonymous data is sent",
                "When off, only minimal anonymous data is sent"));

        #endregion

        #region 音乐和声音

        Slider("soundCategory_master", "sound", "主音量", "主音量", "Master Volume", 0, 100, 0.01d, "%");
        Slider("soundCategory_music", "sound", "音乐", "音樂", "Music", 0, 100, 0.01d, "%");
        Slider("soundCategory_record", "sound", "音符盒 / 唱片机", "音階盒 / 唱片機", "Note Blocks / Jukebox",
            0, 100, 0.01d, "%");
        Slider("soundCategory_weather", "sound", "天气", "天氣", "Weather", 0, 100, 0.01d, "%");
        Slider("soundCategory_block", "sound", "方块", "方塊", "Blocks", 0, 100, 0.01d, "%");
        Slider("soundCategory_hostile", "sound", "敌对生物", "敵對生物", "Hostile Creatures", 0, 100, 0.01d, "%");
        Slider("soundCategory_neutral", "sound", "友好生物", "友好生物", "Friendly Creatures", 0, 100, 0.01d, "%");
        Slider("soundCategory_player", "sound", "玩家", "玩家", "Players", 0, 100, 0.01d, "%");
        Slider("soundCategory_ambient", "sound", "环境", "環境", "Ambient / Environment", 0, 100, 0.01d, "%");
        Slider("soundCategory_voice", "sound", "语音 / 鹦鹉", "語音 / 鸚鵡", "Voice / Parrots", 0, 100, 0.01d, "%",
            VersionCodeOf("1.11"));
        Slider("soundCategory_ui", "sound", "界面", "介面", "Interface", 0, 100, 0.01d, "%", VersionCodeOf("1.16"));
        Slider("soundCategory_village", "sound", "村民", "村民", "Villagers", 0, 100, 0.01d, "%", VersionCodeOf("1.16"),
            VersionCodeOf("1.21.1"),
            Tr("1.21.2 起并入「界面」音量", "1.21.2 起併入「介面」音量", "Merged into Interface volume in 1.21.2+",
                "Merged into Interface volume in 1.21.2+"));
        Toggle("directionalAudio", "sound", "定向音频", "定向音訊", "Directional Audio", VersionCodeOf("1.21"));
        Toggle("showSubtitles", "sound", "隐藏式字幕", "隱藏式字幕", "Show Subtitles");

        #endregion

        #region 控制

        Slider("mouseSensitivity", "controls", "鼠标灵敏度", "滑鼠靈敏度", "Mouse Sensitivity", 0, 200, 0.005d, "%");
        Toggle("invertYMouse", "controls", "鼠标反转", "滑鼠反轉", "Invert Mouse");
        Toggle("invertXMouse", "controls", "鼠标水平反转", "滑鼠水平反轉", "Invert Mouse X", VersionCodeOf("1.21.2"));
        Toggle("autoJump", "controls", "自动跳跃", "自動跳躍", "Auto-Jump", VersionCodeOf("1.10"));
        Toggle("toggleCrouch", "controls", "潜行切换", "潛行切換", "Toggle Crouch", VersionCodeOf("1.15"));
        Toggle("toggleSprint", "controls", "疾跑切换", "疾走切換", "Toggle Sprint", VersionCodeOf("1.15"));
        Toggle("toggleAttack", "controls", "攻击切换", "攻擊切換", "Toggle Attack", VersionCodeOf("1.21.2"));
        Toggle("toggleUse", "controls", "使用切换", "使用切換", "Toggle Use", VersionCodeOf("1.21.2"));
        Toggle("rawMouseInput", "controls", "原始鼠标输入", "原始滑鼠輸入", "Raw Mouse Input", VersionCodeOf("1.21.2"));
        Toggle("discrete_mouse_scroll", "controls", "离散滚轮滚动", "離散滾輪捲動", "Discrete Mouse Scroll",
            VersionCodeOf("1.21.2"));
        Choice("attackIndicator", "controls", "攻击指示器", "攻擊指示器", "Attack Indicator",
            [Tr("关闭", "關閉", "Off", "Off"), Tr("准星", "準星", "Crosshair", "Crosshair"),
                Tr("快捷栏", "快捷欄", "Hotbar", "Hotbar")], ["0", "1", "2"]);

        #endregion

        #region 聊天设置

        Choice("chatVisibility", "chat", "聊天框显示", "聊天框顯示", "Chat",
            [Tr("全部显示", "全部顯示", "Shown", "Shown"), Tr("仅指令", "僅指令", "Commands Only", "Commands Only"),
                Tr("隐藏", "隱藏", "Hidden", "Hidden")], ["0", "1", "2"]);
        Slider("chatOpacity", "chat", "聊天框不透明度", "聊天框不透明度", "Chat Opacity", 0, 100, 0.01d, "%");
        Slider("textBackgroundOpacity", "chat", "聊天框背景不透明度", "聊天框背景不透明度", "Text Background Opacity",
            0, 100, 0.01d, "%", VersionCodeOf("1.19.3"), int.MaxValue,
            Tr("1.19.3 起新增，与「聊天框不透明度」共存", "1.19.3 起新增，與「聊天框不透明度」共存",
                "Added in 1.19.3, works alongside chat opacity", "Added in 1.19.3, works alongside chat opacity"));
        Toggle("backgroundForChatOnly", "chat", "聊天背景仅聊天时显示", "聊天背景僅聊天時顯示", "Chat Background Only",
            VersionCodeOf("1.19.3"));
        Slider("chatScale", "chat", "聊天框大小", "聊天框大小", "Chat Size", 0, 100, 0.01d, "%");
        Slider("chatWidth", "chat", "聊天框宽度", "聊天框寬度", "Chat Width", 0, 100, 0.01d, "%");
        Slider("chatHeightFocused", "chat", "聊天框高度（展开）", "聊天框高度（展開）", "Chat Height (Focused)",
            0, 100, 0.01d, "%");
        Slider("chatHeightUnfocused", "chat", "聊天框高度（收起）", "聊天框高度（收起）", "Chat Height (Unfocused)",
            0, 100, 0.01d, "%");
        Slider("chatLineSpacing", "chat", "聊天行距", "聊天行距", "Chat Line Spacing", 0, 100, 0.01d, "%");
        Slider("chatDelay", "chat", "聊天延迟", "聊天延遲", "Chat Delay", 0, 100, 0.01d, "%", VersionCodeOf("1.16"),
            note: Tr("0~100%：越大越晚发送", "0~100%：越大越晚發送", "0-100%: higher sends later",
                "0-100%: higher sends later"));
        Toggle("chatColors", "chat", "聊天颜色", "聊天顏色", "Chat Colors");
        Toggle("chatLinks", "chat", "聊天链接", "聊天連結", "Chat Links");
        Toggle("chatLinksPrompt", "chat", "链接确认提示", "連結確認提示", "Chat Links Prompt");
        Toggle("autoSuggestions", "chat", "命令自动补全", "指令自動補全", "Command Suggestions");
        Toggle("hideServerAddress", "chat", "隐藏服务器地址", "隱藏伺服器位址", "Hide Server Address",
            VersionCodeOf("1.16"));
        Toggle("hideMatchedNames", "chat", "隐藏匹配的昵称", "隱藏匹配的暱稱", "Hide Matched Names",
            VersionCodeOf("1.16.4"));
        Toggle("onlyShowSecureChat", "chat", "仅显示安全的聊天", "僅顯示安全的聊天", "Only Show Secure Chat",
            VersionCodeOf("1.19.1"));
        Toggle("reducedDebugInfo", "chat", "减少调试信息", "減少除錯資訊", "Reduced Debug Info");

        #endregion

        #region 辅助功能

        Choice("narrator", "accessibility", "复述功能", "朗讀功能", "Narrator",
            [Tr("关闭", "關閉", "Off", "Off"), Tr("仅聊天", "僅聊天", "Chat", "Chat"), Tr("系统", "系統", "System", "System"),
                Tr("全部", "全部", "All", "All")], ["0", "1", "2", "3"], VersionCodeOf("1.12"));
        Slider("damageTiltStrength", "accessibility", "受伤视角抖动", "受傷視角晃動", "Damage Tilt", 0, 100, 0.01d, "%",
            VersionCodeOf("1.19.4"));
        Slider("menuBackgroundBlurriness", "accessibility", "菜单背景模糊", "選單背景模糊", "Menu Background Blurriness",
            0, 10, 1d, "", VersionCodeOf("1.21"),
            note: Tr("0~10（游戏内范围）", "0~10（遊戲內範圍）", "0-10 (in-game range)", "0-10 (in-game range)"));
        Toggle("narratorHotkey", "accessibility", "复述功能快捷键", "朗讀功能快捷鍵", "Narrator Hotkey",
            VersionCodeOf("1.12"));
        Toggle("highContrast", "accessibility", "高对比度", "高對比度", "High Contrast", VersionCodeOf("1.19.4"));

        #endregion

        return list;
    }

    /// <summary>取某个分组下的所有选项。</summary>
    public static List<GameOptionDef> GetOptions(string groupId)
        => Options.Where(o => o.Group == groupId).ToList();

    /// <summary>取某个分组的定义。</summary>
    public static GameOptionGroup GetGroup(string groupId)
        => GroupsById.TryGetValue(groupId, out var group) ? group : null;

    /// <summary>取选项名称。</summary>
    public static string GetName(GameOptionDef def)
        => Named.TryGetValue(def.Key, out var name) ? name : def.Key;

    /// <summary>取选项定义。</summary>
    public static GameOptionDef GetDef(string key)
        => Options.FirstOrDefault(o => o.Key == key);

    #endregion

    #region 文案

    /// <summary>按当前启动器语言取文案（游戏选项名称随目录一起维护，不写入语言文件）。</summary>
    public static string Tr(string zhCn, string zhTw, string enUs, string enGb)
    {
        var code = LocalizationService.CurrentLanguage is null
            ? "zh-CN"
            : LocalizationService.CurrentLanguage.Code ?? "zh-CN";
        return code.ToLowerInvariant() switch
        {
            "zh-tw" => zhTw,
            "en-us" => enUs,
            "en-gb" => enGb,
            _ => zhCn
        };
    }

    #endregion

    #region 版本

    /// <summary>
    ///     把版本名转换为可比较的序数：1.12.2 → 11202，1.21.11 → 12111，26.2 → 260200。
    ///     无法解析时返回 0（表示未知，不做版本过滤）。
    /// </summary>
    public static int VersionCodeOf(string versionName)
    {
        if (string.IsNullOrEmpty(versionName))
            return 0;
        var basePart = versionName.BeforeFirst("-").Trim();
        var numbers = new List<int>();
        foreach (var piece in basePart.Split('.'))
        {
            var digits = new string(piece.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, out var value))
                break;
            numbers.Add(value);
        }

        if (numbers.Count == 0)
            return 0;
        var major = numbers[0];
        var minor = numbers.Count > 1 ? numbers[1] : 0;
        var patch = numbers.Count > 2 ? numbers[2] : 0;
        return major * 10000 + minor * 100 + patch;
    }

    /// <summary>取实例的真实版本序数，失败返回 0。</summary>
    public static int GetInstanceVersionCode(McInstance instance)
    {
        try
        {
            return instance?.Info is null ? 0 : VersionCodeOf(instance.Info.VanillaName);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>取实例的版本显示名。</summary>
    public static string GetInstanceVersionName(McInstance instance)
    {
        try
        {
            var name = instance?.Info?.VanillaName;
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>兼容性预览可选版本（内置常见版本 + 本机已安装实例的版本）。</summary>
    public static List<string> GetPreviewVersions()
    {
        var result = new List<string> { "26.2", "26.4-snapshot-1", "26.3", "1.21.11", "1.20.1", "1.16.5", "1.12.2" };
        try
        {
            if (ModInstanceList.mcInstanceList is not null)
                foreach (var list in ModInstanceList.mcInstanceList.Values)
                foreach (var instance in list)
                {
                    var name = GetInstanceVersionName(instance);
                    if (name == "?" || result.Contains(name))
                        continue;
                    result.Insert(0, name);
                }
        }
        catch
        {
            // 实例列表不可用时忽略
        }

        if (string.IsNullOrEmpty(PreviewVersion))
            PreviewVersion = result[0];
        if (!result.Contains(PreviewVersion))
            result.Insert(0, PreviewVersion);
        return result;
    }

    /// <summary>按版本序数判断选项是否可用；若目标 options.txt 里已有该键，则视为该版本支持。</summary>
    public static bool IsAvailable(GameOptionDef def, int versionCode, Dictionary<string, string> existing = null)
    {
        if (existing is { Count: > 0 } &&
            (existing.ContainsKey(def.Key) ||
             def.LegacyKey is not null && existing.ContainsKey(def.LegacyKey)))
            return true;
        return versionCode <= 0 || versionCode >= def.MinCode && versionCode <= def.MaxCode;
    }

    /// <summary>取选项在某个版本下实际使用的键名。</summary>
    public static string GetKeyFor(GameOptionDef def, int versionCode)
        => def.GetKey(versionCode);

    #endregion

    #region 值的换算与展示

    /// <summary>规范值 → 游戏原始值。</summary>
    public static string ToRaw(GameOptionDef def, string canonical, int versionCode, bool? modernFormat = null)
    {
        switch (def.Kind)
        {
            case GameOptionKind.Toggle:
                return canonical == "true" ? "true" : "false";
            case GameOptionKind.Choice:
            {
                var raws = def.GetChoiceRaw(versionCode);
                if (raws is null || raws.Length == 0)
                    return canonical;
                var index = Math.Clamp(ParseInt(canonical, 0), 0, raws.Length - 1);
                return raws[index];
            }
            default:
            {
                var (factor, offset) = def.GetScale(def.UseModernFormat(versionCode, modernFormat));
                var value = (ParseInt(canonical, def.Min) - offset) * factor;
                return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>游戏原始值 → 规范值，无法解析返回 null。</summary>
    public static string ToCanonical(GameOptionDef def, string raw, int versionCode, bool? modernFormat = null)
    {
        if (raw is null)
            return null;
        raw = raw.Trim();
        switch (def.Kind)
        {
            case GameOptionKind.Toggle:
                return raw.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            case GameOptionKind.Choice:
            {
                var raws = def.GetChoiceRaw(versionCode) ?? [];
                for (var i = 0; i < raws.Length; i++)
                    if (string.Equals(raws[i], raw, StringComparison.OrdinalIgnoreCase))
                        return i.ToString();
                return null;
            }
            default:
            {
                if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value))
                    return null;
                var (factor, offset) = def.GetScale(def.UseModernFormat(versionCode, modernFormat));
                var canonical = factor == 0d ? value : value / factor + offset;
                return Math.Clamp((int)Math.Round(canonical), def.Min, def.Max).ToString();
            }
        }
    }

    /// <summary>
    ///     判断 options.txt 里某个键的现有值属于哪种表示法：
    ///     true＝新版（0~1 小数，如 1.21+ 的 fov），false＝旧版（整数度数），无法判断返回 null。
    /// </summary>
    public static bool? DetectFractionalFormat(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return null;
        if (value <= 1.0001d)
            return true;
        return value > 1.5d ? false : null;
    }

    /// <summary>按目标文件里已有的值（优先）或版本序数，决定该选项用新版还是旧版表示法。</summary>
    public static bool ResolveModernFormat(GameOptionDef def, int versionCode, Dictionary<string, string> existing,
        string realKey)
    {
        bool? fromFile = null;
        if (def.AutoFormatFromFile && realKey is not null && existing is not null &&
            existing.TryGetValue(realKey, out var raw))
            fromFile = DetectFractionalFormat(raw);
        return def.UseModernFormat(versionCode, fromFile);
    }

    /// <summary>规范值的展示文本。</summary>
    public static string Describe(GameOptionDef def, string canonical, int versionCode)
        => GetSpecialLabel(def, canonical, versionCode) ?? DescribeRaw(def, canonical, versionCode);

    /// <summary>
    ///     拉到极值时游戏内显示的专用文案（如「最大帧率」到顶为无限制）
    ///     非特殊项返回 null。
    /// </summary>
    public static string GetSpecialLabel(GameOptionDef def, string canonical, int versionCode)
    {
        if (def.Key != "maxFps" || !int.TryParse(canonical, out var value) || value < def.Max)
            return null;
        // 1.21.2 起垂直同步改为独立选项，帧率上限的顶格即为无限制
        return versionCode == 0 || versionCode >= VersionCodeOf("1.21.2")
            ? Tr("无限制", "無限制", "Unlimited", "Unlimited")
            : Tr("垂直同步", "垂直同步", "VSync", "VSync");
    }

    private static string DescribeRaw(GameOptionDef def, string canonical, int versionCode)
    {
        switch (def.Kind)
        {
            case GameOptionKind.Toggle:
                return canonical == "true"
                    ? Tr("开启", "開啟", "On", "On")
                    : Tr("关闭", "關閉", "Off", "Off");
            case GameOptionKind.Choice:
            {
                var choices = def.GetChoices(versionCode) ?? [];
                var index = ParseInt(canonical, 0);
                return index >= 0 && index < choices.Length ? choices[index] : canonical;
            }
            default:
                return canonical + def.Unit;
        }
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text, out var value) ? value : fallback;

    #endregion

    #region 读写选项文件

    /// <summary>读取 options.txt 的全部键值（不受 ini 缓存影响）。</summary>
    public static Dictionary<string, string> ReadOptionsFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path))
                return result;
            foreach (var line in ModBase.ReadFile(path).Split('\n'))
            {
                var text = line.TrimEnd('\r');
                var index = text.IndexOf(':');
                if (index <= 0)
                    continue;
                result[text[..index]] = text[(index + 1)..];
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[GameOptions] 读取 options.txt 失败：" + path, ModBase.LogLevel.Hint);
        }

        return result;
    }

    /// <summary>
    ///     计算要写入的键值对。
    ///     文件里已有该键 → 用文件里的键名（游戏自己写出的键名最可靠，能兼容改过名的旧版本）；
    ///     文件里没有该键 → 退回版本区间判断（**不能直接跳过**：整合包自带的 options.txt 常常是精简的，
    ///     缺很多键，若此时跳过就会表现为「进游戏没有自动同步」——2026-10-16 真机踩到）；
    ///     版本区间也说不行，才跳过。
    /// </summary>
    public static List<(string key, string raw)> BuildPlan(int versionCode, Dictionary<string, string> existing)
    {
        var plan = new List<(string, string)>();
        var hasFile = existing is { Count: > 0 };
        foreach (var (key, canonical) in Overrides)
        {
            var def = GetDef(key);
            if (def is null)
                continue;
            var realKey = GetKeyFor(def, versionCode);
            if (hasFile && existing.ContainsKey(def.Key))
                realKey = def.Key;
            else if (hasFile && def.LegacyKey is not null && existing.ContainsKey(def.LegacyKey))
                realKey = def.LegacyKey;
            else if (!IsAvailable(def, versionCode, existing))
                continue; // 版本不支持

            plan.Add((realKey, ToRaw(def, canonical, versionCode, ResolveModernFormat(def, versionCode, existing, realKey))));
        }

        foreach (var (key, raw) in CustomRaw)
        {
            if (plan.Any(item => item.Item1 == key))
                continue; // 手动键与自定义项重名时以自定义项为准
            plan.Add((key, raw));
        }

        return plan;
    }

    /// <summary>
    ///     把当前设置套用到某个文件夹（整合包导入时使用）。
    ///     requireExisting 为真时，仅在目标已存在 options.txt 时才写入，避免凭空造出一份设置文件。
    /// </summary>
    public static GameOptionApplyResult ApplyToFolder(string folder, string displayName, bool requireExisting = true)
    {
        var result = new GameOptionApplyResult { InstanceName = displayName };
        try
        {
            if (string.IsNullOrEmpty(folder))
                return result;
            var optionsPath = Path.Combine(folder, "options.txt");
            result.OptionsPath = optionsPath;
            result.VersionName = Tr("整合包", "整合包", "Modpack", "Modpack");
            if (!File.Exists(optionsPath))
            {
                ModBase.Log($"[GameOptions] 整合包 {displayName} 未自带 options.txt，跳过导入时套用（首次启动时会自动套用）");
                return result;
            }

            ModBase.IniClearCache(optionsPath);
            var existing = ReadOptionsFile(optionsPath);
            var plan = BuildPlan(0, existing);
            ModBase.Log($"[GameOptions] 整合包 {displayName} 目标={optionsPath}（已读 {existing.Count} 键，计划 {plan.Count} 项）",
                ModBase.LogLevel.Debug);
            foreach (var (key, raw) in plan)
            {
                ModBase.WriteIni(optionsPath, key, raw);
                result.Applied.Add($"{key}={raw}");
            }

            foreach (var key in Overrides.Keys.Concat(CustomRaw.Keys))
            {
                var def = GetDef(key);
                if (def is null)
                {
                    if (!result.Applied.Any(item => item.StartsWith(key + "=", StringComparison.Ordinal)))
                        result.Skipped.Add(key);
                    continue;
                }

                if (!result.Applied.Any(item =>
                        item.StartsWith(def.Key + "=", StringComparison.Ordinal) ||
                        def.LegacyKey is not null &&
                        item.StartsWith(def.LegacyKey + "=", StringComparison.Ordinal)))
                    result.Skipped.Add(GetName(def));
            }

            ModBase.Log($"[GameOptions] 导入整合包 {displayName} 后已套用游戏选项：写入 {result.Applied.Count} 项"
                        + (result.Skipped.Count > 0 ? $"，跳过 {result.Skipped.Count} 项" : ""));
        }
        catch (Exception ex)
        {
            result.Error = ex;
            ModBase.Log(ex, "[GameOptions] 导入整合包后套用游戏选项失败", ModBase.LogLevel.Hint);
        }

        return result;
    }

    #endregion

    #region 同步

    /// <summary>把当前设置套用到某个实例的 options.txt。</summary>
    public static GameOptionApplyResult ApplyToInstance(McInstance instance, bool createIfMissing = true)
    {
        var result = new GameOptionApplyResult
        {
            InstanceName = instance?.Name ?? "?",
            VersionName = GetInstanceVersionName(instance)
        };
        try
        {
            if (instance is null)
                return result;
            var optionsPath = Path.Combine(instance.PathIndie, "options.txt");
            result.OptionsPath = optionsPath;
            ModBase.IniClearCache(optionsPath);
            var existing = ReadOptionsFile(optionsPath);
            if (existing.Count == 0 && !File.Exists(optionsPath) && !createIfMissing)
                return result;
            var versionCode = GetInstanceVersionCode(instance);
            var plan = BuildPlan(versionCode, existing);
            ModBase.Log($"[GameOptions] 同步目标={optionsPath}（存在={File.Exists(optionsPath)}，已读 {existing.Count} 键，" +
                        $"版本序数={versionCode}，计划 {plan.Count} 项）", ModBase.LogLevel.Debug);
            foreach (var (key, raw) in plan)
            {
                ModBase.WriteIni(optionsPath, key, raw);
                result.Applied.Add($"{key}={raw}");
            }

            foreach (var key in Overrides.Keys.Concat(CustomRaw.Keys))
            {
                var def = GetDef(key);
                var realKey = def is null ? key : GetKeyFor(def, versionCode);
                if (!result.Applied.Any(item => item.StartsWith(realKey + "=", StringComparison.Ordinal)))
                    result.Skipped.Add(def is null ? key : GetName(def));
            }

            ModBase.Log($"[GameOptions] 已同步到 {result.InstanceName}（{result.VersionName}）：写入 {result.Applied.Count} 项"
                        + (result.Skipped.Count > 0 ? $"，跳过 {result.Skipped.Count} 项：{string.Join("、", result.Skipped)}" : ""));
        }
        catch (Exception ex)
        {
            result.Error = ex;
            ModBase.Log(ex, "[GameOptions] 同步游戏选项失败", ModBase.LogLevel.Hint);
        }

        return result;
    }

    /// <summary>套用到当前选中的实例。</summary>
    public static GameOptionApplyResult ApplyToSelectedInstance()
        => ApplyToInstance(ModInstanceList.McMcInstanceSelected);

    /// <summary>套用到全部已加载的实例。</summary>
    public static List<GameOptionApplyResult> ApplyToAllInstances()
    {
        var results = new List<GameOptionApplyResult>();
        try
        {
            foreach (var list in ModInstanceList.mcInstanceList.Values)
            foreach (var instance in list)
                results.Add(ApplyToInstance(instance, false));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[GameOptions] 批量同步游戏选项失败", ModBase.LogLevel.Hint);
        }

        return results;
    }

    #endregion

    #region 规则的导出与导入

    /// <summary>规则文件的类型标记（用于校验导入的文件是不是本启动器导出的规则）。</summary>
    public const string RulesType = "PCL-TCS-GameOptions";

    /// <summary>规则文件当前格式版本。</summary>
    public const int RulesFormatVersion = 1;

    /// <summary>把当前自定义项导出为规则文件，可发给其他玩家导入。</summary>
    public static GameOptionRulesResult ExportRules(string path)
    {
        var result = new GameOptionRulesResult { Path = path, Count = Count };
        try
        {
            if (Count == 0)
            {
                result.Error = new InvalidOperationException(Tr("还没有任何自定义项可以导出",
                    "還沒有任何自訂項可以匯出", "No customized options to export yet", "No customised options to export yet"));
                return result;
            }

            var json = new JsonObject
            {
                ["type"] = RulesType,
                ["formatVersion"] = RulesFormatVersion,
                ["exportedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["launcher"] = "PCL TCS",
                ["previewVersion"] = PreviewVersion
            };
            var overrides = new JsonObject();
            foreach (var (key, value) in Overrides)
                overrides[key] = value;
            var custom = new JsonObject();
            foreach (var (key, value) in CustomRaw)
                custom[key] = value;
            json["overrides"] = overrides;
            json["customRaw"] = custom;
            ModBase.WriteFile(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ModBase.Log($"[GameOptions] 已导出规则 {result.Count} 项 → {path}");
        }
        catch (Exception ex)
        {
            result.Error = ex;
            ModBase.Log(ex, "[GameOptions] 导出规则失败", ModBase.LogLevel.Hint);
        }

        return result;
    }

    /// <summary>只读取规则文件的信息（不改动当前设置），用于导入前的确认提示。</summary>
    public static GameOptionRulesResult PeekRules(string path)
    {
        var result = new GameOptionRulesResult { Path = path };
        try
        {
            var json = ParseRules(path, result);
            if (json is null)
                return result;
            result.Count = CountRules(json);
            result.ExportVersion = json["previewVersion"]?.ToString();
            result.ExportedAt = json["exportedAt"]?.ToString();
            result.Launcher = json["launcher"]?.ToString();
            ModBase.Log($"[GameOptions] 规则文件 {path}：{result.Count} 项，来自 {result.ExportVersion}（{result.ExportedAt}）",
                ModBase.LogLevel.Debug);
        }
        catch (Exception ex)
        {
            result.Error = ex;
        }

        return result;
    }

    /// <summary>从规则文件导入自定义项：同名键覆盖，其余保持原样。</summary>
    public static GameOptionRulesResult ImportRules(string path)
    {
        var result = new GameOptionRulesResult { Path = path };
        try
        {
            var json = ParseRules(path, result);
            if (json is null)
                return result;
            var imported = 0;
            if (json["overrides"] is JsonObject overrides)
                foreach (var (key, value) in overrides)
                    if (value is not null)
                    {
                        Overrides[key] = value.ToString();
                        imported++;
                    }

            if (json["customRaw"] is JsonObject custom)
                foreach (var (key, value) in custom)
                    if (value is not null)
                    {
                        CustomRaw[key] = value.ToString();
                        imported++;
                    }

            result.Count = imported;
            result.ExportVersion = json["previewVersion"]?.ToString();
            result.ExportedAt = json["exportedAt"]?.ToString();
            result.Launcher = json["launcher"]?.ToString();
            Save();
            ModBase.Log($"[GameOptions] 已导入规则 {imported} 项（{path}），当前共 {Count} 项");
        }
        catch (Exception ex)
        {
            result.Error = ex;
            ModBase.Log(ex, "[GameOptions] 导入规则失败", ModBase.LogLevel.Hint);
        }

        return result;
    }

    /// <summary>解析并校验规则文件，失败时写入 result.Error 并返回 null。</summary>
    private static JsonObject ParseRules(string path, GameOptionRulesResult result)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            result.Error = new FileNotFoundException(Tr("规则文件不存在", "規則檔不存在",
                "The rules file does not exist", "The rules file does not exist"), path);
            return null;
        }

        if (JsonNode.Parse(ModBase.ReadFile(path)) is not JsonObject json)
        {
            result.Error = new InvalidDataException(Tr("规则文件不是有效的 JSON", "規則檔不是有效的 JSON",
                "The rules file is not valid JSON", "The rules file is not valid JSON"));
            return null;
        }

        if (json["type"]?.ToString() != RulesType)
        {
            result.Error = new InvalidDataException(Tr("这不是 PCL-TCS 的游戏选项规则文件",
                "這不是 PCL-TCS 的遊戲選項規則檔", "This is not a PCL-TCS game option rules file",
                "This is not a PCL-TCS game option rules file"));
            return null;
        }

        return json;
    }

    private static int CountRules(JsonObject json)
    {
        var count = 0;
        if (json["overrides"] is JsonObject overrides)
            count += overrides.Count;
        if (json["customRaw"] is JsonObject custom)
            count += custom.Count;
        return count;
    }

    #endregion

    #region 持久化

    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            var path = StoragePath;
            if (!File.Exists(path))
                return;
            if (JsonNode.Parse(ModBase.ReadFile(path)) is not JsonObject json)
                return;
            SyncOnLaunch = ReadBool(json, "syncOnLaunch", SyncOnLaunch);
            SyncAfterInstall = ReadBool(json, "syncAfterInstall", SyncAfterInstall);
            if (json["previewVersion"]?.ToString() is { Length: > 0 } preview)
                PreviewVersion = preview;
            if (json["overrides"] is JsonObject overrides)
                foreach (var (key, value) in overrides)
                    if (value is not null)
                        Overrides[key] = value.ToString();
            if (json["customRaw"] is JsonObject custom)
                foreach (var (key, value) in custom)
                    if (value is not null)
                        CustomRaw[key] = value.ToString();
            ModBase.Log($"[GameOptions] 已载入游戏选项设置：自定义 {Count} 项，启动时同步={SyncOnLaunch}，" +
                        $"导入后套用={SyncAfterInstall}，预览版本={PreviewVersion}");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[GameOptions] 载入游戏选项设置失败", ModBase.LogLevel.Hint);
        }
    }

    public static void Save()
    {
        try
        {
            var json = new JsonObject
            {
                ["syncOnLaunch"] = SyncOnLaunch,
                ["syncAfterInstall"] = SyncAfterInstall,
                ["previewVersion"] = PreviewVersion
            };
            var overrides = new JsonObject();
            foreach (var (key, value) in Overrides)
                overrides[key] = value;
            var custom = new JsonObject();
            foreach (var (key, value) in CustomRaw)
                custom[key] = value;
            json["overrides"] = overrides;
            json["customRaw"] = custom;
            ModBase.WriteFile(StoragePath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[GameOptions] 保存游戏选项设置失败", ModBase.LogLevel.Hint);
        }
    }

    private static bool ReadBool(JsonObject json, string key, bool fallback)
    {
        var node = json[key];
        if (node is null)
            return fallback;
        try
        {
            return node.GetValue<bool>();
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>清空全部自定义项。</summary>
    public static void Reset()
    {
        Overrides.Clear();
        CustomRaw.Clear();
        Save();
    }

    #endregion
}
