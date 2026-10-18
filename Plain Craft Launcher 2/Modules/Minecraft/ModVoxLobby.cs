using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCL;

/// <summary>VoxLink 公共大厅里的一条房间。</summary>
public class VoxRoom
{
    public string Code = "";
    public string Name = "";
    public string GameVersion = "";
    public string Loader = "";
    public int Current;
    public int Max;
    public bool HasPassword;
    public string Region = "";
}

/// <summary>VoxLink 中继的公共大厅接口（与官网同源）。</summary>
public static class ModVoxLobby
{
    public const string Endpoint = "https://p2p.wuhui.icu/?route=/room/list";

    /// <summary>拉取公共大厅房间列表。</summary>
    public static async Task<(bool ok, string message, List<VoxRoom> rooms, int total)> FetchAsync(int page = 1, int size = 20)
    {
        var list = new List<VoxRoom>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 PCL-TCS");
            var url = Endpoint + "&page=" + page + "&size=" + size + "&clientType=web";
            var json = await http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var okEl) || !okEl.GetBoolean())
                return (false, "中继返回失败", list, 0);
            if (!root.TryGetProperty("data", out var data)) return (false, "返回格式不符", list, 0);
            var total = data.TryGetProperty("total", out var tEl) ? tEl.GetInt32() : 0;
            if (data.TryGetProperty("rooms", out var rooms) && rooms.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rooms.EnumerateArray())
                {
                    list.Add(new VoxRoom
                    {
                        Code = Str(r, "code"),
                        Name = Str(r, "name"),
                        GameVersion = Str(r, "gameVersion"),
                        Loader = Str(r, "loader"),
                        Current = Int(r, "currentPlayers"),
                        Max = Int(r, "maxPlayers"),
                        HasPassword = r.TryGetProperty("hasPassword", out var hp) && hp.ValueKind == JsonValueKind.True,
                        Region = Str(r, "region")
                    });
                }
            }
            return (true, "共 " + total + " 个房间", list, total);
        }
        catch (Exception ex)
        {
            return (false, "拉取大厅失败：" + ex.Message, list, 0);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
