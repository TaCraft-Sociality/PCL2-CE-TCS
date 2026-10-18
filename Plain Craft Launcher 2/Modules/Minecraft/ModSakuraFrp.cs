using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PCL;

public class SakuraNode
{
    public int Id;
    public string Host = "";
    public string Name = "";
    public bool Premium;
    public Dictionary<string, string> Extras = new();

    public string DisplayName
    {
        get
        {
            foreach (var key in new[] { "name", "node_name", "title", "host" })
                if (Extras.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            return "节点 " + Id;
        }
    }

    public string LoadText
    {
        get
        {
            foreach (var key in new[] { "load", "load_percent", "usage", "used", "bandwidth_usage", "traffic" })
                if (Extras.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            return "";
        }
    }

    public string Combo
    {
        get
        {
            var load = LoadText;
            if (load.Length > 0 && !load.EndsWith("%")) load += "%";
            return DisplayName + " · " + (Premium ? "专业" : "免费") + (load.Length > 0 ? " · " + load : "") + "   [节点 " + Id + "]";
        }
    }
}

public class SakuraTunnel
{
    public int Id;
    public string Name = "";
    public int NodeId;
    public string Type = "";
    public int Remote;
    public string LocalIp = "";
    public int LocalPort;
    public bool Online;
    public string NodeHost = "";
    public string PublicAddress => string.IsNullOrWhiteSpace(NodeHost) || Remote == 0 ? "" : NodeHost + ":" + Remote;
}

/// <summary>樱花穿透（natfrp）OpenAPI 客户端：访问密钥、隧道、节点。</summary>
public static class ModSakuraFrp
{
    private const string ApiBase = "https://api.natfrp.com/v4";
    private const string StoreName = "sakura-token.json";
    public const string SiteUrl = "https://www.natfrp.com/";
    public const string UserUrl = "https://www.natfrp.com/user/";
    public const string GuideUrl = "https://www.natfrp.com/tutorial/";

    public static string StorePath
    {
        get
        {
            var dir = Path.GetDirectoryName(ModServerList.StoragePath);
            return Path.Combine(string.IsNullOrWhiteSpace(dir) ? ModBase.exePath : dir, StoreName);
        }
    }

    public static string Token { get; private set; } = "";

    public static void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(StorePath));
            if (doc.RootElement.TryGetProperty("token", out var t)) Token = t.GetString() ?? "";
        }
        catch { }
    }

    public static void SaveToken(string token)
    {
        Token = (token ?? "").Trim();
        try
        {
            var obj = new JsonObject { ["token"] = Token };
            File.WriteAllText(StorePath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { ModBase.Log(ex, "[SakuraFrp] 保存访问密钥失败"); }
    }

    /// <summary>尝试从已安装的樱花客户端读取访问密钥。</summary>
    public static string DetectLocalToken()
    {
        try
        {
            var p = @"C:\ProgramData\SakuraFrpService\config.json";
            if (!File.Exists(p)) return "";
            using var doc = JsonDocument.Parse(File.ReadAllText(p));
            if (doc.RootElement.TryGetProperty("token", out var t)) return t.GetString() ?? "";
        }
        catch { }
        return "";
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.Add("User-Agent", "PCL-TCS");
        if (!string.IsNullOrWhiteSpace(Token)) http.DefaultRequestHeaders.Add("Authorization", "Bearer " + Token);
        return http;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string path)
    {
        var text = await http.GetStringAsync(ApiBase + path).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>读取隧道列表（附带节点 host）。</summary>
    public static async Task<(bool ok, string message, List<SakuraTunnel> tunnels)> FetchTunnelsAsync()
    {
        var list = new List<SakuraTunnel>();
        try
        {
            using var http = CreateClient();
            var nodes = new Dictionary<int, string>();
            try
            {
                var nodeEl = await GetJsonAsync(http, "/nodes").ConfigureAwait(false);
                if (nodeEl.ValueKind == JsonValueKind.Object)
                    foreach (var prop in nodeEl.EnumerateObject())
                    {
                        if (!int.TryParse(prop.Name, out var id)) continue;
                        var host = prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("host", out var h)
                            ? h.GetString() ?? "" : "";
                        nodes[id] = host;
                    }
            }
            catch { }
            var el = await GetJsonAsync(http, "/tunnels").ConfigureAwait(false);
            if (el.ValueKind != JsonValueKind.Array) return (false, "隧道返回格式异常", list);
            foreach (var t in el.EnumerateArray())
            {
                var item = new SakuraTunnel
                {
                    Id = IntOf(t, "id"),
                    Name = StrOf(t, "name"),
                    NodeId = IntOf(t, "node"),
                    Type = StrOf(t, "type"),
                    Remote = IntOf(t, "remote"),
                    LocalIp = StrOf(t, "local_ip"),
                    LocalPort = IntOf(t, "local_port"),
                    Online = t.TryGetProperty("online", out var o) && o.ValueKind == JsonValueKind.True
                };
                item.NodeHost = nodes.TryGetValue(item.NodeId, out var host) ? host : "";
                list.Add(item);
            }
            return (true, "读取到 " + list.Count + " 条隧道", list);
        }
        catch (Exception ex)
        {
            return (false, "读取失败：" + ex.Message, list);
        }
    }

    public static async Task<(bool ok, string message, List<SakuraNode> nodes)> FetchNodesAsync()
    {
        var list = new List<SakuraNode>();
        try
        {
            using var http = CreateClient();
            string rawNodes = await http.GetStringAsync(ApiBase + "/nodes").ConfigureAwait(false);
            try
            {
                var dumpDir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrWhiteSpace(dumpDir))
                    File.WriteAllText(Path.Combine(dumpDir, "sakura-nodes-raw.json"), rawNodes);
            }
            catch { }
            var el = System.Text.Json.JsonDocument.Parse(rawNodes).RootElement.Clone();
            if (el.ValueKind != JsonValueKind.Object) return (false, "节点返回格式异常", list);
            foreach (var prop in el.EnumerateObject())
            {
                if (!int.TryParse(prop.Name, out var id)) continue;
                var node = new SakuraNode { Id = id };
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    if (prop.Value.TryGetProperty("host", out var h)) node.Host = h.GetString() ?? "";
                    if (prop.Value.TryGetProperty("name", out var n)) node.Name = n.GetString() ?? "";
                    foreach (var f in prop.Value.EnumerateObject())
                    {
                        if (f.Value.ValueKind == JsonValueKind.String) node.Extras[f.Name] = f.Value.GetString() ?? "";
                        else if (f.Value.ValueKind == JsonValueKind.Number) node.Extras[f.Name] = f.Value.GetRawText();
                        else if (f.Value.ValueKind == JsonValueKind.True) node.Extras[f.Name] = "true";
                    }
                    if (node.Extras.TryGetValue("premium", out var prem) && prem == "true") node.Premium = true;
                    if (node.Extras.TryGetValue("vip", out var vip) && vip == "true") node.Premium = true;
                    if (node.Extras.TryGetValue("group", out var grp) && grp.ToLowerInvariant().Contains("vip")) node.Premium = true;
                    if (prop.Value.TryGetProperty("premium", out var pm) && pm.ValueKind == JsonValueKind.True) node.Premium = true;
                    if (prop.Value.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String
                        && (g.GetString() ?? "").ToLowerInvariant().Contains("vip")) node.Premium = true;
                }
                list.Add(node);
            }
            list = list.OrderBy(n => n.Id).ToList();
            return (true, "共 " + list.Count + " 个节点", list);
        }
        catch (Exception ex)
        {
            return (false, "读取节点失败：" + ex.Message, list);
        }
    }

    public static async Task<(bool ok, string message)> CreateTunnelAsync(string name, int nodeId, int localPort, int remotePort = 0, string localIp = "127.0.0.1")
    {
        try
        {
            using var http = CreateClient();
            var body = new
            {
                name = name,
                type = "tcp",
                node = nodeId,
                local_ip = string.IsNullOrWhiteSpace(localIp) ? "127.0.0.1" : localIp,
                local_port = localPort,
                remote = remotePort,
                extra = ""
            };
            var resp = await http.PostAsJsonAsync(ApiBase + "/tunnels", body).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (false, "创建失败（" + (int)resp.StatusCode + "）：" + Trim(text));
            return (true, "隧道已创建");
        }
        catch (Exception ex)
        {
            return (false, "创建失败：" + ex.Message);
        }
    }

    public static async Task<(bool ok, string message, string config)> GetFrpcConfigAsync(int tunnelId)
    {
        try
        {
            using var http = CreateClient();
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["query"] = tunnelId.ToString(),
                ["frpc"] = "0.59.0"
            });
            var resp = await http.PostAsync(ApiBase + "/tunnel/config", form).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? (true, "已生成 frpc 配置", text) : (false, "生成配置失败（" + (int)resp.StatusCode + "）", "");
        }
        catch (Exception ex)
        {
            return (false, "生成配置失败：" + ex.Message, "");
        }
    }

    // ===== 内置 frpc：在启动器内直接启动/停止隧道 =====
    private static readonly Dictionary<int, System.Diagnostics.Process> Running = new();
    public static string FrpcDir => Path.Combine(Path.GetTempPath(), "PCL_TCS", "frpc");
    public static string FrpcExe => Path.Combine(FrpcDir, "frpc.exe");

    public static bool IsTunnelRunning(int id) => Running.TryGetValue(id, out var p) && p != null && !p.HasExited;

    /// <summary>确保本地有上游 frpc.exe（首次自动下载，约 12MB）。</summary>
    public static async Task<(bool ok, string message)> EnsureFrpcAsync(Action<string> log = null)
    {
        try
        {
            if (File.Exists(FrpcExe)) return (true, "frpc 已就绪");
            Directory.CreateDirectory(FrpcDir);
            var url = "https://nya.globalslb.net/natfrp/client/frpc/0.51.0-sakura-14/frpc_windows_amd64.exe";
            log?.Invoke("下载樱花 frpc（0.51.0-sakura-14）");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "PCL-TCS");
            var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
            await File.WriteAllBytesAsync(FrpcExe, bytes).ConfigureAwait(false);
            log?.Invoke("frpc 就绪：" + FrpcExe);
            return (true, "frpc 已就绪");
        }
        catch (Exception ex) { return (false, "准备 frpc 失败：" + ex.Message); }
    }

    /// <summary>用樱花生成的配置在本地启动该隧道。</summary>
    public static async Task<(bool ok, string message)> StartTunnelAsync(int tunnelId, Action<string> log = null)
    {
        try
        {
            if (IsTunnelRunning(tunnelId)) return (true, "该隧道已在运行");
            if (string.IsNullOrWhiteSpace(Token)) return (false, "请先保存访问密钥");
            var frpc = await EnsureFrpcAsync(log).ConfigureAwait(false);
            if (!frpc.ok) return frpc;
            var fetched = await FetchTunnelsAsync().ConfigureAwait(false);
            var tunnel = fetched.tunnels?.FirstOrDefault(t => t.Id == tunnelId);
            if (tunnel == null) return (false, "账号里没有这条隧道，请点「刷新状态」");
            log?.Invoke("启动隧道 " + tunnel.Name + "（" + tunnel.Type + "）");
            var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FrpcExe,
                    Arguments = "-f " + Token + ":" + tunnelId,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = FrpcDir
                },
                EnableRaisingEvents = true
            };
            var succeeded = false;
            var publicAddr = "";
            proc.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                log?.Invoke("[frpc] " + e.Data);
                if (e.Data.Contains("隧道启动成功")) succeeded = true;
                var m = System.Text.RegularExpressions.Regex.Match(e.Data, @"(?:使用 >>)?([a-z0-9\-\.]+\.(?:natfrp\.cloud|nyatwork\.cn|frp-hub\.com|frp\.com|frp\.net|frp-egg\.com|frp-hen\.com|frp-van\.com))\:(\d{2,5})");
                if (m.Success && publicAddr.Length == 0) publicAddr = m.Groups[1].Value + ":" + m.Groups[2].Value;
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke("[frpc] " + e.Data);
            };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            Running[tunnelId] = proc;
            for (int i = 0; i < 40 && !succeeded && !proc.HasExited; i++) await Task.Delay(500).ConfigureAwait(false);
            if (proc.HasExited) return (false, "frpc 已退出，请看上方日志");
            if (!succeeded) log?.Invoke("没抓到「隧道启动成功」，但进程仍在运行");
            var addr = publicAddr.Length > 0 ? publicAddr : tunnel.PublicAddress;
            return (true, "隧道已启动" + (string.IsNullOrWhiteSpace(addr) ? "" : "：" + addr));
        }
        catch (Exception ex) { return (false, "启动失败：" + ex.Message); }
    }

    public static (bool ok, string message) StopTunnel(int tunnelId)
    {
        try
        {
            if (!Running.TryGetValue(tunnelId, out var proc) || proc == null) return (false, "该隧道未在启动器内运行");
            if (!proc.HasExited) proc.Kill(true);
            Running.Remove(tunnelId);
            return (true, "已停止");
        }
        catch (Exception ex) { return (false, "停止失败：" + ex.Message); }
    }

    public static void StopAll()
    {
        foreach (var key in Running.Keys.ToList()) StopTunnel(key);
    }
    private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 160 ? s.Substring(0, 160) : s);

    private static string StrOf(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int IntOf(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i) ? i : 0;
    }
}
