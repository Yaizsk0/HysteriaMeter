using System.IO;
using System.Text.Json;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Nombres de skills por ID, desde <c>data/skills.json</c>. El juego solo manda el ID, así que el mapa se
/// completa a mano comparando con Combat Analysis; lo que falta se muestra como "Skill 12345".
/// </summary>
public sealed class SkillNames(IReadOnlyDictionary<uint, string> names)
{
    public static SkillNames Empty { get; } = new(new Dictionary<uint, string>());

    public string Of(uint skillId) => names.TryGetValue(skillId, out var name) ? name : $"Skill {skillId}";

    public static SkillNames Load(string path)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var names = new Dictionary<uint, string>();
            if (document.RootElement.TryGetProperty("skills", out var skills))
            {
                foreach (var entry in skills.EnumerateObject())
                {
                    if (uint.TryParse(entry.Name, out var id) && entry.Value.GetString() is { Length: > 0 } name)
                    {
                        names[id] = name;
                    }
                }
            }

            return new SkillNames(names);
        }
        catch (JsonException)
        {
            // Un skills.json mal escrito no debe impedir abrir el meter: se ven los IDs.
            return Empty;
        }
    }
}
