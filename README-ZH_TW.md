**繁體中文** | [简体中文](README.md) | [English](README-EN.md)

<div align="center">

# PCL TCS（藍版）

[社群版下載](https://github.com/PCL-Community/PCL-CE/releases/latest) |
[上游儲存庫](https://github.com/Meloong-Git/PCL)

[提交問題](https://github.com/PCL-Community/PCL-CE/issues/new/choose) |
[貢獻指南](https://github.com/PCL-Community/PCL-CE/wiki/开发指南)

</div>

> 本目錄為 **PCL TCS（TCS 客戶端啟動器）藍版原始碼**：由 TCS 伺服器團隊基於 PCL CE（PCL Community Edition）二次開發。
> 相對上游的主要客製：藍色圖示、左側縱向導覽、內建「社群 / 皮膚站 / B站」瀏覽器入口、
> 基於陶瓦（Terracotta）的「連線」頁面（與 HMCL 同款方案，不依賴 PCL 官方連線服務）、
> 首次啟動即可用離線 / 第三方驗證登入，以及 TCS 口徑的文案。
> 上游 PCL CE 與 PCL 的版權歸其各自作者所有。

PCL CE 是基於 PCL 開源程式碼二次開發的社群版本，包括了主線暫未製作的功能和改進！

社群版的版本號與主線並非嚴格對應關係，也請不要向官方倉庫回饋社群版問題。

歡迎大家來用用看！

## 💻 支援平台

| 作業系統 | 支援情況 | 環境需求 |
|---|---|---|
| Windows 10 1809 (17763) 或更高 | ✅ 完整支援 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 - Windows 10 1809 (17763) | ⚠️ 理論可執行，酌情提供社群支援 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 或更低版本 | ❌ 不支援 | / |
| macOS / Linux / 其他作業系統 | ⚠️ 僅跨平台開發支援（交叉編譯） | [.NET 10 SDK](https://get.dot.net/10) |

**✅ 完整支援**：盡可能提供一切相關支援，但必須確保啟動器為最新版本。

**⚠️ 理論可執行，酌情提供社群支援**：PCL CE 應該可以在這些平台上執行，但不保證功能完全可用。你可能需要升級到完整支援的系統版本以獲得進一步社群技術支援。

**❌ 不支援**：PCL CE 在這些平台的可用性較低，甚至根本打不開。請升級作業系統以使用 PCL CE。

**⚠️ 僅跨平台開發支援（交叉編譯）**：PCL CE 的原始碼可以在 macOS 與 Linux 平台編譯，但無法直接執行。作為開發者，你可以在這些平台上進行開發，然後將編譯產物轉移到 Windows 系統測試。

**註**：    
社群僅對最新版本的啟動器提供支援。    
取決於部分問題的特殊性（如系統不完整），有時你仍然必須升級作業系統以繼續獲得支援。    
PCL CE 始終建議使用最新版本的作業系統以獲得最佳體驗。    
你仍然可以嘗試在不受支援的系統上使用 PCL CE，但可能會遇到很多額外問題。

## 🔒 授權條款

- `Plain Craft Launcher 2/` 使用 [自訂授權條款](https://github.com/PCL-Community/PCL-CE/blob/dev/Plain%20Craft%20Launcher%202/LICENCE)
- `其餘所有目錄` 使用 [Apache License 2.0](https://github.com/PCL-Community/PCL-CE/blob/dev/LICENSE)

## ❤️ 貢獻者

上游貢獻者列表：[PCL-Community/PCL-CE 貢獻者](https://github.com/PCL-Community/PCL-CE/graphs/contributors)
