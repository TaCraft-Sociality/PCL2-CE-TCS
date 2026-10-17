using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL;

/// <summary>多人游戏：一条服务器记录。</summary>
public class McServerEntry
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Name = "";
    public string Address = "";
    public string Note = "";
    public string Instance = "";

    public string DisplayAddress
    {
        get
        {
            var (host, port) = ModServerList.SplitAddress(Address);
            return port == 25565 ? host : host + ":" + port;
        }
    }
}

/// <summary>多人游戏：服务器列表存储、默认实例解析、启动并进服。</summary>
public static class ModServerList
{
    private const string FileName = "servers.json";
    public static readonly List<McServerEntry> Items = new();
    private static bool _loaded;

    public static string Tr(string zhCn, string zhTw, string en) => ModGameOptions.Tr(zhCn, zhTw, en, en);

    public static string StoragePath
    {
        get
        {
            var root = Path.GetDirectoryName(ModGameOptions.StoragePath);
            if (string.IsNullOrEmpty(root)) root = ModBase.exePath;
            return Path.Combine(root, FileName);
        }
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(StoragePath)) return;
            if (JsonNode.Parse(ModBase.ReadFile(StoragePath)) is not JsonObject json) return;
            if (json["servers"] is not JsonArray array) return;
            foreach (var node in array)
            {
                if (node is not JsonObject item) continue;
                Items.Add(new McServerEntry
                {
                    Id = item["id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    Name = item["name"]?.ToString() ?? "",
                    Address = item["address"]?.ToString() ?? "",
                    Note = item["note"]?.ToString() ?? "",
                    Instance = item["instance"]?.ToString() ?? ""
                });
            }
            ModBase.Log("[Multiplayer] 已读取 " + Items.Count + " 台服务器");
        }
        catch (Exception ex) { ModBase.Log(ex, "[Multiplayer] 读取服务器列表失败"); }
    }

    public static void Save()
    {
        try
        {
            var array = new JsonArray();
            foreach (var server in Items)
                array.Add(new JsonObject
                {
                    ["id"] = server.Id, ["name"] = server.Name, ["address"] = server.Address,
                    ["note"] = server.Note, ["instance"] = server.Instance
                });
            var root = new JsonObject { ["formatVersion"] = 1, ["servers"] = array };
            ModBase.WriteFile(StoragePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ModBase.Log("[Multiplayer] 已保存 " + Items.Count + " 台服务器");
        }
        catch (Exception ex) { ModBase.Log(ex, "[Multiplayer] 保存服务器列表失败"); }
    }

    public static McServerEntry Add(string name, string address, string instance)
    {
        EnsureLoaded();
        var entry = new McServerEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? (address ?? "").Trim() : name.Trim(),
            Address = NormalizeAddress(address),
            Instance = instance ?? ""
        };
        Items.Add(entry);
        Save();
        return entry;
    }

    public static void Remove(McServerEntry entry) { Items.Remove(entry); Save(); }

    public static void SetInstance(McServerEntry entry, string instance) { entry.Instance = instance ?? ""; Save(); }

    public static string NormalizeAddress(string address)
    {
        var text = (address ?? "").Trim();
        if (text.Contains("://")) text = text.AfterFirst("://");
        text = text.TrimEnd('/');
        if (text.Length == 0) return "";
        var parts = SplitAddress(text);
        return parts.Port == 25565 ? parts.Host : parts.Host + ":" + parts.Port;
    }

    public static (string Host, int Port) SplitAddress(string address)
    {
        var text = (address ?? "").Trim();
        if (text.Contains("://")) text = text.AfterFirst("://");
        if (text.Length == 0) return ("", 25565);
        var index = text.LastIndexOf(':');
        if (index > 0 && index < text.Length - 1 && int.TryParse(text.Substring(index + 1), out var port))
            return (text.Substring(0, index), port);
        return (text, 25565);
    }

    /// <summary>取该服务器要用的实例：有默认就用，否则弹窗让用户选并按分类显示，选完记住。</summary>
    public static McInstance ResolveInstance(McServerEntry entry, bool askIfMissing = true)
    {
        var candidates = CollectInstances();
        if (candidates.Count == 0)
        {
            ModMain.MyMsgBox(Tr("还没有任何游戏实例，请先去下载或导入一个实例。", "還沒有任何遊戲實例，請先去下載或匯入一個實例。",
                "No instance available yet. Download or import one first."), Tr("多人游戏", "多人遊戲", "Multiplayer"));
            return null;
        }
        var current = candidates.FirstOrDefault(i => string.Equals(i.Name, entry.Instance, StringComparison.OrdinalIgnoreCase));
        if (current != null) return current;
        if (!askIfMissing) return null;

        McInstance selected = null;
        ModBase.RunInUiWait(() =>
        {
            var controls = new List<IMyRadio>();
            foreach (var group in GroupInstances(candidates))
            {
                foreach (var instance in group.Value)
                {
                    controls.Add(new MyRadioBox
                    {
                        Text = "[" + group.Key + "] " + instance.Name,
                    });
                }
            }
            var index = ModMain.MyMsgBoxSelect(controls,
                Tr("选择要进入该服务器的实例", "選擇要進入該伺服器的實例", "Choose an instance for this server"));
            if (index != null && index.Value >= 0 && index.Value < controls.Count)
                selected = candidates[index.Value];
        });
        if (selected == null) return null;
        SetInstance(entry, selected.Name);
        return selected;
    }

    public static List<McInstance> CollectInstances()
    {
        var result = new List<McInstance>();
        if (ModInstanceList.mcInstanceList == null) return result;
        foreach (var list in ModInstanceList.mcInstanceList.Values)
            foreach (var instance in list)
                if (instance != null) result.Add(instance);
        return result;
    }

    /// <summary>按实例分类分组（原版 / Forge / Fabric ...），与启动页同一口径。</summary>
    public static List<KeyValuePair<string, List<McInstance>>> GroupInstances(List<McInstance> instances)
    {
        var groups = new List<KeyValuePair<string, List<McInstance>>>();
        if (ModInstanceList.mcInstanceList == null) return groups;
        foreach (var pair in ModInstanceList.mcInstanceList)
        {
            var list = pair.Value.Where(i => i != null && instances.Contains(i)).ToList();
            if (list.Count == 0) continue;
            groups.Add(new KeyValuePair<string, List<McInstance>>(pair.Key.ToString(), list));
        }
        return groups;
    }

    /// <summary>启动该服务器：自动选实例 → 启动游戏 → 启动后直接进服。</summary>
    public static bool Launch(McServerEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Address)) return false;
        var instance = ResolveInstance(entry);
        if (instance == null) return false;
        var parts = SplitAddress(entry.Address);
        ModBase.Log("[Multiplayer] 启动服务器 " + entry.Name + "（" + entry.DisplayAddress + "），实例=" + instance.Name);
        ModLaunch.McLaunchStart(new ModLaunch.McLaunchOptions
        {
            instance = instance,
            ServerIp = parts.Host + ":" + parts.Port
        });
        return true;
    }
}
