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

    /// <summary>Nombres que puso el usuario desde el detalle (doble clic en una skill); pisan a los del archivo.</summary>
    public IReadOnlyDictionary<uint, string> Custom { get; set; } = new Dictionary<uint, string>();

    public string Of(uint skillId) =>
        Custom.TryGetValue(skillId, out var custom) ? custom : names.TryGetValue(skillId, out var name) ? name : $"Skill {skillId}";

    /// <summary>Una copia propia, para no tocar la tabla compartida (por ejemplo <see cref="Empty"/>) al poner nombres.</summary>
    public SkillNames Copy() => new(names) { Custom = Custom };

    /// <summary>Pone, cambia o (con texto vacío) saca el nombre que el usuario le dio a una skill.</summary>
    public static IReadOnlyDictionary<uint, string> With(IReadOnlyDictionary<uint, string>? custom, uint skillId, string? name)
    {
        var result = new Dictionary<uint, string>(custom ?? new Dictionary<uint, string>());
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length > MaxLength)
        {
            clean = clean[..MaxLength];
        }

        if (clean.Length == 0)
        {
            result.Remove(skillId);
        }
        else
        {
            result[skillId] = clean;
        }

        return result;
    }

    public const int MaxLength = 32;

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
