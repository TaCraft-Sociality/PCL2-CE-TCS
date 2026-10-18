using System.Windows.Input;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

/// <summary>
///     「游戏选项」页面左侧栏：同步开关与同步操作。
/// </summary>
public partial class PageGameOptionsLeft
{
    private bool _suppress;

    public PageGameOptionsLeft()
    {
        InitializeComponent();
        ModGameOptions.EnsureLoaded();
        ApplyTexts();
        Refresh();
    }

    /// <summary>这些文案随功能一起维护，不写进语言文件（与选项目录同一套 Tr 口径）。</summary>
    private void ApplyTexts()
    {
        LabSectionShare.Text = ModGameOptions.Tr("分享规则", "分享規則", "Share Rules", "Share Rules");
        ItemExportRules.Title = ModGameOptions.Tr("导出规则", "匯出規則", "Export Rules", "Export Rules");
        ItemImportRules.Title = ModGameOptions.Tr("导入规则", "匯入規則", "Import Rules", "Import Rules");
    }

    /// <summary>刷新开关状态与底部说明。</summary>
    public void Refresh()
    {
        _suppress = true;
        try
        {
            CheckSyncLaunch.SetChecked(ModGameOptions.SyncOnLaunch, false);
            CheckSyncInstall.SetChecked(ModGameOptions.SyncAfterInstall, false);
        }
        finally
        {
            _suppress = false;
        }

        LabStatus.Text = ModGameOptions.Count == 0
            ? Lang.Text("GameOptions.Left.Hint")
            : Lang.Text("GameOptions.Left.HintCount", ModGameOptions.Count);
    }

    private void CheckSync_Change(object sender, bool user)
    {
        if (_suppress)
            return;
        ModGameOptions.SyncOnLaunch = CheckSyncLaunch.Checked == true;
        ModGameOptions.SyncAfterInstall = CheckSyncInstall.Checked == true;
        ModGameOptions.Save();
        Refresh();
        ModMain.frmGameOptions?.OnSyncChanged();
    }

    private void ItemSyncCurrent_Click(object sender, MouseButtonEventArgs e)
    {
        var instance = ModInstanceList.McMcInstanceSelected;
        if (instance is null)
        {
            HintService.Hint(Lang.Text("GameOptions.Tip.NoInstance"), HintType.Error);
            return;
        }

        if (ModGameOptions.Count == 0)
        {
            HintService.Hint(Lang.Text("GameOptions.Tip.Nothing"), HintType.Info);
            return;
        }

        var result = ModGameOptions.ApplyToInstance(instance);
        ReportResults([result]);
    }

    private void ItemSyncAll_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModGameOptions.Count == 0)
        {
            HintService.Hint(Lang.Text("GameOptions.Tip.Nothing"), HintType.Info);
            return;
        }

        var results = ModGameOptions.ApplyToAllInstances();
        ReportResults(results);
    }

    private void ItemAddCustom_Click(object sender, MouseButtonEventArgs e)
    {
        var input = ModMain.MyMsgBoxInput(Lang.Text("GameOptions.Left.Action.AddCustom"),
            Lang.Text("GameOptions.Tip.AddCustomHint"), "");
        if (string.IsNullOrWhiteSpace(input))
            return;
        var index = input.IndexOf(':');
        if (index <= 0 || index >= input.Length - 1)
        {
            HintService.Hint(Lang.Text("GameOptions.Tip.BadFormat"), HintType.Error);
            return;
        }

        var key = input[..index].Trim();
        var value = input[(index + 1)..].Trim();
        ModGameOptions.CustomRaw[key] = value;
        ModGameOptions.Save();
        ModMain.frmGameOptions?.RefreshAll();
        HintService.Hint(Lang.Text("GameOptions.Tip.Added", $"{key}={value}"), HintType.Success);
    }

    private void ItemReset_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModGameOptions.Count == 0)
        {
            HintService.Hint(Lang.Text("GameOptions.Tip.Nothing"), HintType.Info);
            return;
        }

        if (ModMain.MyMsgBox(Lang.Text("GameOptions.Tip.ResetConfirm"), Lang.Text("GameOptions.Left.Action.Reset"),
                Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"), "") != 1)
            return;
        ModGameOptions.Reset();
        Refresh();
        ModMain.frmGameOptions?.RefreshAll();
        HintService.Hint(Lang.Text("GameOptions.Tip.Reset"), HintType.Success);
    }

    /// <summary>导出规则：把当前自定义项存成一个 json，可发给其他玩家导入。</summary>
    private void ItemExportRules_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModGameOptions.Count == 0)
        {
            HintService.Hint(ModGameOptions.Tr("还没有任何自定义项可以导出", "還沒有任何自訂項可以匯出",
                "No customized options to export yet", "No customised options to export yet"), HintType.Info);
            return;
        }

        var savePath = SystemDialogs.SelectSaveFile(
            ModGameOptions.Tr("导出游戏选项规则", "匯出遊戲選項規則", "Export game option rules",
                "Export game option rules"),
            $"PCL-TCS 游戏选项规则 {DateTime.Now:yyyyMMdd-HHmm}.json",
            "游戏选项规则 (*.json)|*.json|所有文件 (*.*)|*.*",
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        if (string.IsNullOrWhiteSpace(savePath))
            return;

        var result = ModGameOptions.ExportRules(savePath);
        if (result.Error is not null)
        {
            HintService.Hint(ModGameOptions.Tr("导出失败：", "匯出失敗：", "Export failed: ", "Export failed: ")
                             + result.Error.Message, HintType.Error);
            return;
        }

        HintService.Hint(ModGameOptions.Tr($"已导出 {result.Count} 项规则，把文件发给别人就能直接导入",
            $"已匯出 {result.Count} 項規則，把檔案發給別人就能直接匯入",
            $"Exported {result.Count} rules — send the file to others and they can import it",
            $"Exported {result.Count} rules — send the file to others and they can import it"), HintType.Success);
        ModBase.OpenExplorer(savePath);
    }

    /// <summary>导入规则：读别人发来的 json，同名项覆盖后立即套用到当前实例。</summary>
    private void ItemImportRules_Click(object sender, MouseButtonEventArgs e)
    {
        var path = SystemDialogs.SelectFile("游戏选项规则 (*.json)|*.json|所有文件 (*.*)|*.*",
            ModGameOptions.Tr("选择要导入的规则文件", "選擇要匯入的規則檔", "Select a rules file to import",
                "Select a rules file to import"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        var peek = ModGameOptions.PeekRules(path);
        if (peek.Error is not null)
        {
            HintService.Hint(ModGameOptions.Tr("无法导入：", "無法匯入：", "Cannot import: ", "Cannot import: ")
                             + peek.Error.Message, HintType.Error);
            return;
        }

        var from = string.IsNullOrWhiteSpace(peek.ExportVersion)
            ? ""
            : ModGameOptions.Tr($"（来自 {peek.ExportVersion}", $"（來自 {peek.ExportVersion}",
                $" (exported from {peek.ExportVersion}", $" (exported from {peek.ExportVersion}")
              + (string.IsNullOrWhiteSpace(peek.ExportedAt) ? "）" : $"，{peek.ExportedAt}）");
        if (ModMain.MyMsgBox(
                ModGameOptions.Tr($"将导入 {peek.Count} 项规则{from}，同名的自定义项会被覆盖，其余保持不变。是否继续？",
                    $"將匯入 {peek.Count} 項規則{from}，同名的自訂項會被覆蓋，其餘保持不變。是否繼續？",
                    $"Import {peek.Count} rules{from}? Existing items with the same key will be overwritten.",
                    $"Import {peek.Count} rules{from}? Existing items with the same key will be overwritten."),
                ModGameOptions.Tr("导入规则", "匯入規則", "Import rules", "Import rules"),
                Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"), "") != 1)
            return;

        var result = ModGameOptions.ImportRules(path);
        if (result.Error is not null)
        {
            HintService.Hint(ModGameOptions.Tr("导入失败：", "匯入失敗：", "Import failed: ", "Import failed: ")
                             + result.Error.Message, HintType.Error);
            return;
        }

        Refresh();
        ModMain.frmGameOptions?.RefreshAll();
        var applied = ModGameOptions.ApplyToInstance(ModInstanceList.McMcInstanceSelected);
        var tail = applied.Error is null && applied.Applied.Count > 0
            ? ModGameOptions.Tr($"，并已套用到当前实例 {applied.Applied.Count} 项",
                $"，並已套用到目前實例 {applied.Applied.Count} 項",
                $" and applied {applied.Applied.Count} of them to the current instance",
                $" and applied {applied.Applied.Count} of them to the current instance")
            : "";
        HintService.Hint(ModGameOptions.Tr($"已导入 {result.Count} 项，当前共 {ModGameOptions.Count} 项{tail}",
            $"已匯入 {result.Count} 項，目前共 {ModGameOptions.Count} 項{tail}",
            $"Imported {result.Count} rules, {ModGameOptions.Count} in total{tail}",
            $"Imported {result.Count} rules, {ModGameOptions.Count} in total{tail}"), HintType.Success);
    }

    private static void ReportResults(List<GameOptionApplyResult> results)
    {
        var applied = results.Where(r => r.Error is null).ToList();
        var failed = results.Where(r => r.Error is not null).ToList();
        var hasSkipped = applied.Any(r => r.Skipped.Count > 0) || failed.Count > 0;
        var text = Lang.Text("GameOptions.Tip.Synced",
            applied.Count == 0 ? "0" : string.Join("、", applied.Select(r => r.Summary)));
        HintService.Hint(text, hasSkipped ? HintType.Info : HintType.Success);
    }
}
