using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using PCL.Core.App.Localization;
using PCL.Core.Logging;

namespace PCL;

/// <summary>
///     陶瓦联机（Terracotta）集成。
///     TCS 定制：与 HMCL 相同的接入方式 —— 下载官方组件，启动
///     <c>terracotta.exe --hmcl &lt;状态文件&gt;</c>，由陶瓦自己把 JSON 状态写进该文件，启动器轮询读取。
///     房间的创建 / 加入在陶瓦自己的窗口里完成，启动器只负责拉起、显示状态与房间号。
/// </summary>
public static class ModTerracotta
{
    // ---- 上游信息（与 HMCL 的 terracotta.json 一致）----
    private const string UpstreamVersion = "0.4.2";
    private const string Classifier = "windows-x86_64";
    private const string ExeFileName = "terracotta-0.4.2-windows-x86_64.exe";
    private const string VcRuntimeFileName = "VCRUNTIME140.DLL";

    /// <summary>整包（tar.gz）的 SHA-512，用于下载后校验。</summary>
    private const string PackageSha512 =
        "6a98f524d4f00373696517306af8aa50d01d55ce4eadb27e9e4bc2f882707a0b5f20d5d4c33371d1459dcf5bf144ffed9beb414202d9ccf32b11dbbfcf19d650";

    /// <summary>包内各文件的 SHA-512（HMCL 同款校验）。</summary>
    private static readonly Dictionary<string, string> FileSha512 = new(StringComparer.OrdinalIgnoreCase)
    {
        [VcRuntimeFileName] =
            "3d4b24061f72c0e957c7b04a0c4098c94c8f1afb4a7e159850b9939c7210d73398be6f27b5ab85073b4e8c999816e7804fef0f6115c39cd061f4aaeb4dcda8cf",
        [ExeFileName] =
            "6e98d1f2380ed22fb5a2dd4aafce6c773e9cf69100c8bb8e49e7d6983756bdb9a31f80e06bcfbe5a2742144fe806d3d687dec54d8f09d87c659341f99dd9fd80"
    };

    /// <summary>下载地址：优先国内镜像，最后回退 GitHub。</summary>
    private static readonly string[] DownloadUrls =
    {
        $"https://gitee.com/burningtnt/Terracotta/releases/download/v{UpstreamVersion}/terracotta-{UpstreamVersion}-{Classifier}-pkg.tar.gz",
        $"https://cnb.cool/HMCL-Terracotta/Terracotta/-/releases/download/v{UpstreamVersion}/terracotta-{UpstreamVersion}-{Classifier}-pkg.tar.gz",
        $"https://alist.8mi.tech/d/mirror/HMCL-Terracotta/Auto/v{UpstreamVersion}/terracotta-{UpstreamVersion}-{Classifier}-pkg.tar.gz",
        $"https://github.com/burningtnt/Terracotta/releases/download/v{UpstreamVersion}/terracotta-{UpstreamVersion}-{Classifier}-pkg.tar.gz"
    };

    public const string ProjectUrl = "https://github.com/burningtnt/Terracotta";

    // ---- 运行时状态 ----
    public enum LinkState
    {
        NotReady,      // 尚未启动
        Downloading,   // 正在下载 / 解包
        Launching,     // 已拉起进程，等状态
        Running,       // 陶瓦界面已就绪（UiUrl 可用）
        Waiting,       // 等待创建 / 加入房间
        Failed,        // 出错（含 exception / fatal）
        Stopped        // 已停止
    }

    public static LinkState State { get; private set; } = LinkState.NotReady;
    public static string Detail { get; private set; } = string.Empty;   // 状态原文 / 错误信息
    public static string Room { get; private set; } = string.Empty;     // 房间号（若有）
    public static bool IsRunning => _process is { HasExited: false };

    /// <summary>陶瓦启动后其自带 Web 界面的地址（从状态文件里的 port 解析），未就绪时为空。</summary>
    public static string UiUrl { get; private set; } = string.Empty;

    /// <summary>
    ///     准备一个本地提示页（file:// 地址），在陶瓦尚未启动时显示给用户。
    /// </summary>
    public static string PrepareHintPage()
    {
        try
        {
            var path = Path.Combine(ModBase.pathTemp, "Terracotta", "hint.html");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string html = """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
<title>陶瓦联机</title>
<style>
 body{margin:0;font-family:'Microsoft YaHei UI',sans-serif;background:#fafafa;color:#333}
 .wrap{max-width:640px;margin:56px auto;padding:0 24px}
 h1{font-size:22px;margin:0 0 6px}
 .sub{opacity:.6;font-size:13px;margin-bottom:26px}
 ol{line-height:1.9;font-size:14.5px;padding-left:22px}
 .tip{margin-top:26px;padding:14px 16px;background:#fff;border:1px solid #eee;border-radius:8px;font-size:13px;opacity:.8}
</style></head><body><div class="wrap">
<h1>陶瓦联机（Terracotta）</h1>
<div class="sub">与 HMCL 相同的联机方案：点左侧「启动联机」后，这里会显示陶瓦的房间界面。</div>
<ol>
 <li>点左侧栏的 <b>启动联机</b>（首次会下载并校验陶瓦组件，约 20 MB）</li>
 <li>在陶瓦界面里点 <b>创建房间</b>，把房间号发给朋友（房主）</li>
 <li>朋友启动本启动器后，在陶瓦界面输入房间号 <b>加入房间</b></li>
 <li>房主在游戏里对局域网开放世界，陶瓦会自动扫描到端口；朋友在多人游戏里连入即可</li>
</ol>
<div class="tip">联机由陶瓦（Terracotta）提供，不依赖 PCL 官方联机服务。房主与朋友需要各自打开本页启动陶瓦。</div>
</div></body></html>
""";
            File.WriteAllText(path, html, System.Text.Encoding.UTF8);
            return new Uri(path).AbsoluteUri;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 生成提示页失败");
            return "about:blank";
        }
    }

    private static Process? _process;
    private static string _stateFile = string.Empty;
    private static CancellationTokenSource? _watchCts;

    /// <summary>界面端口就绪时触发（供页面立即跳转到陶瓦界面）。</summary>
    public static event Action? UiReady;

    // 陶瓦会在 stdout 打印形如 "port = 62287" 或 "127.0.0.1:58381" 的端口信息（主/次要模式都会打印）
    private static readonly System.Text.RegularExpressions.Regex PortRegex =
        new(@"(?:port\s*=\s*|127\.0\.0\.1:)(\d{2,5})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>把端口记成界面地址（重复设置同一端口不会重复触发事件）。</summary>
    private static void SetUiPort(string port)
    {
        if (string.IsNullOrWhiteSpace(port)) return;
        var url = $"http://127.0.0.1:{port}/";
        if (string.Equals(url, UiUrl, StringComparison.Ordinal)) return;
        UiUrl = url;
        State = LinkState.Running;
        ModBase.Log($"[Terracotta] 界面就绪：{url}");
        try { UiReady?.Invoke(); } catch (Exception ex) { ModBase.Log($"[Terracotta] UiReady 事件异常：{ex.Message}"); }
    }

    /// <summary>组件安装目录（放在启动器的 PCL 目录下，便于整体拷贝）。</summary>
    public static string InstallDir => Path.Combine(ModBase.pathTemp, "Terracotta");

    public static string ExecutablePath => Path.Combine(InstallDir, ExeFileName);

    /// <summary>组件是否已就绪（存在且大小正常）。</summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                return File.Exists(ExecutablePath) && new FileInfo(ExecutablePath).Length > 1024 * 1024;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>界面状态文案（TCS 页面与 PageTcsLeft 一致，直接内嵌中文）。</summary>
    public static string StateText => State switch
    {
        LinkState.NotReady => "未启动",
        LinkState.Downloading => "正在下载 / 解包陶瓦组件…",
        LinkState.Launching => "正在启动陶瓦…",
        LinkState.Running => "陶瓦已就绪，在右侧创建或加入房间",
        LinkState.Waiting => "等待操作（请在右侧陶瓦界面创建或加入房间）",
        LinkState.Failed => "出错了",
        LinkState.Stopped => "已停止",
        _ => State.ToString()
    };

    /// <summary>确保组件存在：缺失则下载 + 校验 + 解包。</summary>
    public static async Task<bool> EnsureInstalledAsync()
    {
        if (IsInstalled) return true;
        State = LinkState.Downloading;
        Detail = string.Empty;
        try
        {
            Directory.CreateDirectory(InstallDir);
            var pkg = Path.Combine(Path.GetTempPath(), $"terracotta-{UpstreamVersion}-{Classifier}-pkg.tar.gz");
            if (!await DownloadAsync(pkg).ConfigureAwait(false))
            {
                State = LinkState.Failed;
                return false;
            }

            ModBase.Log($"[Terracotta] 下载完成，校验 SHA-512：{pkg}");
            if (!string.Equals(Sha512File(pkg), PackageSha512, StringComparison.OrdinalIgnoreCase))
            {
                Detail = "package sha512 mismatch";
                ModBase.Log("[Terracotta] 整包 SHA-512 校验失败，已删除下载文件", ModBase.LogLevel.Hint);
                try { File.Delete(pkg); } catch { }
                State = LinkState.Failed;
                return false;
            }

            ExtractTarGz(pkg, InstallDir);
            try { File.Delete(pkg); } catch { }

            foreach (var (name, expect) in FileSha512)
            {
                var path = Path.Combine(InstallDir, name);
                if (!File.Exists(path))
                {
                    Detail = $"missing {name}";
                    State = LinkState.Failed;
                    return false;
                }

                var actual = Sha512File(path);
                if (!string.Equals(actual, expect, StringComparison.OrdinalIgnoreCase))
                {
                    Detail = $"sha512 mismatch: {name}";
                    ModBase.Log($"[Terracotta] {name} 校验失败", ModBase.LogLevel.Hint);
                    State = LinkState.Failed;
                    return false;
                }
            }

            ModBase.Log($"[Terracotta] 组件就绪：{ExecutablePath}");
            State = LinkState.NotReady;
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 安装失败");
            Detail = ex.Message;
            State = LinkState.Failed;
            return false;
        }
    }

    private static async Task<bool> DownloadAsync(string dest)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("User-Agent", "PCL-TCS");
        foreach (var url in DownloadUrls)
        {
            try
            {
                ModBase.Log($"[Terracotta] 尝试下载：{url}");
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(dest);
                await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                ModBase.Log($"[Terracotta] 下载失败（{url}）：{ex.Message}");
            }
        }

        Detail = "all mirrors failed";
        return false;
    }

    private static string Sha512File(string path)
    {
        using var sha = SHA512.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    /// <summary>解包 tar.gz（.NET 的 GZipStream + TarReader，无需外部工具）。</summary>
    private static void ExtractTarGz(string pkg, string dest)
    {
        Directory.CreateDirectory(dest);
        using var fs = File.OpenRead(pkg);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new System.Formats.Tar.TarReader(gz, leaveOpen: false);
        while (tar.GetNextEntry() is { } entry)
        {
            var name = Path.GetFileName(entry.Name);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var target = Path.Combine(dest, name);
            switch (entry.EntryType)
            {
                case System.Formats.Tar.TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;
                case System.Formats.Tar.TarEntryType.RegularFile:
                case System.Formats.Tar.TarEntryType.V7RegularFile:
                {
                    entry.ExtractToFile(target, overwrite: true);
                    ModBase.Log($"[Terracotta] 解出 {name}（{entry.Length} 字节）");
                    break;
                }
            }
        }
    }

    /// <summary>启动陶瓦联机（若组件缺失会先下载）。</summary>
    public static async Task<bool> StartAsync()
    {
        if (IsRunning)
        {
            State = LinkState.Waiting;
            return true;
        }

        if (!await EnsureInstalledAsync().ConfigureAwait(false)) return false;

        try
        {
            _stateFile = Path.Combine(Path.GetTempPath(), $"terracotta-state-{Environment.ProcessId}.json");
            try { File.Delete(_stateFile); } catch { }

            State = LinkState.Launching;
            Detail = string.Empty;
            Room = string.Empty;

            var psi = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                WorkingDirectory = InstallDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                // 必须接住它的 stdout/stderr：陶瓦是 Rust 程序，向无效句柄写日志会直接 panic 退出
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("--hmcl");
            psi.ArgumentList.Add(_stateFile);

            _process = Process.Start(psi);
            if (_process is null)
            {
                State = LinkState.Failed;
                Detail = "process start returned null";
                return false;
            }

            _process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                ModBase.Log($"[Terracotta/out] {e.Data}");
                var m = PortRegex.Match(e.Data);
                if (m.Success) SetUiPort(m.Groups[1].Value);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) ModBase.Log($"[Terracotta/err] {e.Data}");
            };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => ModBase.Log($"[Terracotta] 进程退出（PID={_process?.Id}，UiUrl={(string.IsNullOrWhiteSpace(UiUrl) ? "无" : UiUrl)}）");

            ModBase.Log($"[Terracotta] 已启动 PID={_process.Id}，状态文件={_stateFile}");
            _watchCts = new CancellationTokenSource();
            _ = WatchStateAsync(_watchCts.Token);
            State = LinkState.Waiting;
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 启动失败");
            Detail = ex.Message;
            State = LinkState.Failed;
            return false;
        }
    }

    /// <summary>停止陶瓦联机。</summary>
    public static void Stop()
    {
        try { _watchCts?.Cancel(); } catch { }
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                ModBase.Log("[Terracotta] 已结束进程");
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Terracotta] 结束进程失败");
        }
        finally
        {
            _process = null;
            Room = string.Empty;
            UiUrl = string.Empty;
            State = LinkState.Stopped;
            Detail = string.Empty;
        }
    }

    /// <summary>轮询状态文件，把 kind 映射成本地状态。</summary>
    private static async Task WatchStateAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, token).ConfigureAwait(false);
                if (_process is null || _process.HasExited)
                {
                    if (!string.IsNullOrWhiteSpace(UiUrl))
                    {
                        // 已从 stdout 拿到端口：多为「附加到已在运行的陶瓦实例」，保持可用
                        State = LinkState.Running;
                        Detail = "已附加到正在运行的陶瓦实例";
                        ModBase.Log($"[Terracotta] 子进程已退出，但界面可用（{UiUrl}），按已在运行处理");
                    }
                    else if (State is not (LinkState.Stopped or LinkState.Failed))
                    {
                        State = LinkState.Stopped;
                        Detail = "陶瓦进程已退出（可能是已有实例占用单实例锁，请先关闭其它陶瓦窗口后重试）";
                    }

                    return;
                }

                if (!File.Exists(_stateFile)) continue;
                string text;
                try
                {
                    text = await File.ReadAllTextAsync(_stateFile, token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    continue; // 陶瓦正在写，下一轮再读
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                // 陶瓦在 --hmcl 模式下把自带 Web 界面的端口写进状态文件，形如 {"port":57726}
                var port = ParseString(text, "port");
                if (!string.IsNullOrWhiteSpace(port))
                {
                    var url = $"http://127.0.0.1:{port}/";
                    if (!string.Equals(url, UiUrl, StringComparison.Ordinal))
                    {
                        UiUrl = url;
                        State = LinkState.Running;
                        ModBase.Log($"[Terracotta] 界面已就绪：{url}");
                    }

                    continue;
                }

                var kind = ParseKind(text);
                var room = ParseString(text, "room");
                if (!string.IsNullOrEmpty(room)) Room = room;
                var message = ParseString(text, "message");
                if (!string.IsNullOrEmpty(message)) Detail = message;

                var mapped = kind switch
                {
                    "waiting" => LinkState.Waiting,
                    "host-scanning" or "host-starting" or "host-ok" => LinkState.Running,
                    "guest-connecting" or "guest-starting" or "guest-ok" => LinkState.Running,
                    "exception" or "fatal" => LinkState.Failed,
                    _ => State
                };

                if (mapped != State)
                {
                    ModBase.Log($"[Terracotta] 状态：{State} -> {mapped}（kind={kind}, room={Room}）");
                    State = mapped;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                ModBase.Log($"[Terracotta] 轮询异常：{ex.Message}");
            }
        }
    }

    private static string ParseKind(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return string.Empty;
            if (doc.RootElement.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
                return k.GetString() ?? string.Empty;
            if (doc.RootElement.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String)
                return s.GetString() ?? string.Empty;
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var nested = ParseKind(prop.Value.GetRawText());
                    if (!string.IsNullOrEmpty(nested)) return nested;
                }
        }
        catch
        {
            // 非法 JSON：忽略
        }

        return string.Empty;
    }

    private static string ParseString(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return string.Empty;
            if (doc.RootElement.TryGetProperty(name, out var v))
                return v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? string.Empty,
                    JsonValueKind.Number => v.ToString(),
                    _ => string.Empty
                };
        }
        catch
        {
            // 忽略
        }

        return string.Empty;
    }
}
