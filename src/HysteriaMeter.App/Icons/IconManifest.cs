using System.IO;
using System.Text.Json;

namespace HysteriaMeter.App.Icons;

/// <summary>
/// Qué íconos baja el meter de MetaBot.GG (<c>data/iconos-metabot.json</c>): IDs de skills y el nombre de cada clase en
/// las URLs del sitio. Solo IDs: las imágenes son de NCSOFT y no viajan en el repo ni en el zip.
/// </summary>
public sealed record IconManifest(IReadOnlyList<uint> Skills, IReadOnlyDictionary<int, string> Classes)
{
    public static IconManifest Empty { get; } = new([], new Dictionary<int, string>());

    /// <summary>Página de la que salen los íconos de skills: es el link de la atribución.</summary>
    public const string SkillsPage = "https://metabot.gg/en/aion-2/skills";

    /// <summary>Página de la que salen los emblemas de clase.</summary>
    public const string ClassesPage = "https://metabot.gg/en/aion-2/classes";

    public static Uri SkillUrl(uint skillId) => new($"https://metabot.gg/web/aion2/skills/{skillId}.webp");

    public static Uri ClassUrl(string name) => new($"https://metabot.gg/web/aion2/classes/{Uri.EscapeDataString(name)}.webp");

    /// <summary>Lee el manifiesto; si falta o está roto, no hay nada que bajar (<see cref="Empty"/>).</summary>
    public static IconManifest Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static IconManifest Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var skills = new List<uint>();
            if (root.TryGetProperty("skills", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                skills.AddRange(list.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out _))
                    .Select(e => e.GetUInt32())
                    .Distinct());
            }

            var classes = new Dictionary<int, string>();
            if (root.TryGetProperty("classes", out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in map.EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out var id) && entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { Length: > 0 } name)
                    {
                        classes[id] = name;
                    }
                }
            }

            return new IconManifest(skills, classes);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
