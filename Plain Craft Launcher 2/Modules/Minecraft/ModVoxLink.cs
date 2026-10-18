using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PCL;

/// <summary>VoxLink 联机模组的安装与版本匹配。</summary>
public static class ModVoxLink
{
    private const string RepoApi = "https://api.github.com/repos/AUGUHDAR/VoxLink/releases/latest";
    public const string ProjectUrl = "https://github.com/AUGUHDAR/VoxLink";
    public const string HomeUrl = "https://p2p.wuhui.icu/";

    public static string ModsDir(McInstance instance) => Path.Combine(instance.PathIndie, "mods");

    public static bool IsInstalled(McInstance instance)
    {
        try
        {
            var dir = ModsDir(instance);
            if (!Directory.Exists(dir)) return false;
            return Directory.GetFiles(dir, "*.jar").Any(f => Path.GetFileName(f).ToLowerInvariant().Contains("voxlink"));
        }
        catch { return false; }
    }

    /// <summary>从实例的版本 json 读 MC 版本与加载器。</summary>
    public static (string version, string loader) DetectInstance(McInstance instance)
    {
        var version = "";
        var loader = "";
        try
        {
            var jsonPath = Path.Combine(instance.PathInstance, instance.Name + ".json");
            if (File.Exists(jsonPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                foreach (var key in new[] { "clientVersion", "inheritsFrom", "id" })
                {
                    if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString() ?? "";
                        var m = Regex.Match(s, @"\d+\.\d+(?:\.\d+)?");
                        if (m.Success) { version = m.Value; break; }
                    }
                }
                if (root.TryGetProperty("mainClass", out var mc) && mc.ValueKind == JsonValueKind.String)
                {
                    var s = (mc.GetString() ?? "").ToLowerInvariant();
                    if (s.Contains("neoforge")) loader = "neoforge";
                    else if (s.Contains("fabric")) loader = "fabric";
                    else if (s.Contains("quilt")) loader = "quilt";
                    else if (s.Contains("forge") || s.Contains("fml")) loader = "forge";
                }
            }
        }
        catch { }
        if (string.IsNullOrWhiteSpace(version))
        {
            var m = Regex.Match(instance.Name, @"\d+\.\d+(?:\.\d+)?");
            if (m.Success) version = m.Value;
        }
        if (string.IsNullOrWhiteSpace(loader))
        {
            var n = instance.Name.ToLowerInvariant();
            if (n.Contains("neoforge")) loader = "neoforge";
            else if (n.Contains("fabric")) loader = "fabric";
            else if (n.Contains("quilt")) loader = "quilt";
            else if (n.Contains("forge")) loader = "forge";
        }
        return (version, loader);
    }

    /// <summary>按实例版本 + 加载器挑资产并下载到 mods。</summary>
    public static async Task<(bool ok, string message)> InstallAsync(McInstance instance, Action<string> log = null)
    {
        try
        {
            var detected = DetectInstance(instance);
            var version = detected.version;
            var loader = detected.loader;
            if (string.IsNullOrWhiteSpace(version))
                return (false, "无法识别该实例的 Minecraft 版本");
            log?.Invoke("实例：" + instance.Name + " → MC " + version + (string.IsNullOrWhiteSpace(loader) ? "（加载器未知）" : " / " + loader));
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.Add("User-Agent", "PCL-TCS");
            var json = await http.GetStringAsync(RepoApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var assets = new List<(string name, string url)>();
            if (doc.RootElement.TryGetProperty("assets", out var arr))
            {
                foreach (var a in arr.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    var url = a.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && name.ToLowerInvariant().Contains("voxlink"))
                        assets.Add((name, url));
                }
            }
            log?.Invoke("发布资产 " + assets.Count + " 个 jar");
            if (assets.Count == 0) return (false, "VoxLink 发布里没有 jar 资产");

            bool HasToken(string file, string token)
            {
                if (string.IsNullOrWhiteSpace(token)) return false;
                var parts = Regex.Split(file.ToLowerInvariant(), @"[^a-z0-9\.]+");
                return parts.Any(p => p == token.ToLowerInvariant());
            }

            var byVersion = assets.Where(a => HasToken(a.name, version)).ToList();
            if (byVersion.Count == 0)
                return (false, "VoxLink 没有 " + version + " 对应的版本（该实例暂时无法用模组联机）");
            (string name, string url)? picked = null;
            if (!string.IsNullOrWhiteSpace(loader))
            {
                var byLoader = byVersion.Where(a => HasToken(a.name, loader)).ToList();
                if (byLoader.Count > 0) picked = byLoader[0];
            }
            picked ??= byVersion[0];
            var chosen = picked.Value;
            if (Directory.Exists(ModsDir(instance)) &&
                Directory.GetFiles(ModsDir(instance), "*.jar").Any(f => Path.GetFileName(f) == chosen.name))
            {
                return (true, "已经装好了：" + chosen.name);
            }
            Directory.CreateDirectory(ModsDir(instance));
            var dest = Path.Combine(ModsDir(instance), chosen.name);
            log?.Invoke("下载 " + chosen.name);
            var bytes = await http.GetByteArrayAsync(chosen.url).ConfigureAwait(false);
            await File.WriteAllBytesAsync(dest, bytes).ConfigureAwait(false);
            log?.Invoke("已安装：" + dest);
            return (true, "已安装 " + chosen.name);
        }
        catch (Exception ex)
        {
            return (false, "安装失败：" + ex.Message);
        }
    }
}
