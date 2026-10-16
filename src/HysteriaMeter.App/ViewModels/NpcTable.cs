using System.IO;
using System.Text.Json;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Lee <c>data/npcs.json</c>: <c>"npcId": { "name": "...", "dungeon": "...", "boss": true, "enrage": 480 }</c>, o solo
/// <c>"npcId": "Nombre"</c>. Un archivo mal escrito no impide abrir el meter: se ve el ID.
/// </summary>
public static class NpcTable
{
    /// <summary>Une varios archivos: los posteriores pisan a los anteriores (lo extraído del juego, después <c>data/</c>).</summary>
    public static NpcCatalog Load(params string[] paths)
    {
        var npcs = new Dictionary<uint, NpcInfo>();
        foreach (var path in paths.Where(File.Exists))
        {
            foreach (var (id, npc) in Entries(File.ReadAllText(path)))
            {
                npcs[id] = npc;
            }
        }

        return npcs.Count == 0 ? NpcCatalog.Empty : new NpcCatalog(npcs);
    }

    public static NpcCatalog Parse(string json) => Entries(json) is { Count: > 0 } npcs ? new NpcCatalog(npcs) : NpcCatalog.Empty;

    private static Dictionary<uint, NpcInfo> Entries(string json)
    {
        var npcs = new Dictionary<uint, NpcInfo>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("npcs", out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in entries.EnumerateObject())
                {
                    if (uint.TryParse(entry.Name, out var id) && Read(entry.Value) is { } npc)
                    {
                        npcs[id] = npc;
                    }
                }
            }

        }
        catch (JsonException)
        {
            npcs.Clear();
        }

        return npcs;
    }

    private static NpcInfo? Read(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when value.GetString() is { Length: > 0 } name => new NpcInfo(name),
        JsonValueKind.Object when Text(value, "name") is { } name =>
            new NpcInfo(
                name,
                Text(value, "dungeon"),
                value.TryGetProperty("boss", out var boss) && boss.ValueKind == JsonValueKind.True,
                value.TryGetProperty("enrage", out var enrage) && enrage.ValueKind == JsonValueKind.Number && enrage.TryGetInt32(out var seconds) && seconds > 0 ? seconds : null),
        _ => null,
    };

    private static string? Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { Length: > 0 } s ? s.Trim() : null;
}
