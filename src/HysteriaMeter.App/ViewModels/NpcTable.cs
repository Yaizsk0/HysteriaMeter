using System.IO;
using System.Text.Json;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Lee <c>data/npcs.json</c>: <c>"npcId": { "name": "...", "dungeon": "...", "boss": true }</c>, o solo
/// <c>"npcId": "Nombre"</c>. Un archivo mal escrito no impide abrir el meter: se ve el ID.
/// </summary>
public static class NpcTable
{
    public static NpcCatalog Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : NpcCatalog.Empty;

    public static NpcCatalog Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var npcs = new Dictionary<uint, NpcInfo>();
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

            return new NpcCatalog(npcs);
        }
        catch (JsonException)
        {
            return NpcCatalog.Empty;
        }
    }

    private static NpcInfo? Read(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when value.GetString() is { Length: > 0 } name => new NpcInfo(name),
        JsonValueKind.Object when Text(value, "name") is { } name =>
            new NpcInfo(name, Text(value, "dungeon"), value.TryGetProperty("boss", out var boss) && boss.ValueKind == JsonValueKind.True),
        _ => null,
    };

    private static string? Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { Length: > 0 } s ? s.Trim() : null;
}
