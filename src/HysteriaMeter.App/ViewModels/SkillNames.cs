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

    /// <summary>
    /// El nombre exacto o, si no está, el de la skill base: en las skills de clase (11xxxxxx a 18xxxxxx) los últimos
    /// cuatro dígitos son nivel y variante (Bittercold Wind 15280143 → 15280000).
    /// </summary>
    public string Of(uint skillId) =>
        Custom.TryGetValue(skillId, out var custom) ? custom
        : names.TryGetValue(skillId, out var name) ? name
        : BaseOf(skillId) is { } baseId && (Custom.TryGetValue(baseId, out name) || names.TryGetValue(baseId, out name)) ? name
        : $"Skill {skillId}";

    private static uint? BaseOf(uint skillId) =>
        skillId / 1_000_000 is >= 11 and <= 18 && skillId % 10_000 != 0 ? skillId / 10_000 * 10_000 : null;

    /// <summary>Una copia propia, para no tocar la tabla compartida (por ejemplo <see cref="Empty"/>) al poner nombres.</summary>
    public SkillNames Copy() => new(names) { Custom = Custom, IconFolder = IconFolder };

    /// <summary>La misma tabla con íconos desde esa carpeta (si existe).</summary>
    public SkillNames WithIcons(string folder) => new(names) { Custom = Custom, IconFolder = Directory.Exists(folder) ? folder : null };

    /// <summary>Carpeta con los íconos (<c>&lt;id&gt;.png</c>), por ejemplo <c>Juego\iconos</c>; <c>null</c> si no hay.</summary>
    public string? IconFolder { get; init; }

    private readonly Dictionary<uint, string?> _icons = [];

    /// <summary>Ruta del ícono de la skill o, si no está, el de su skill base; <c>null</c> si no hay ninguno.</summary>
    public string? IconOf(uint skillId)
    {
        if (IconFolder is null)
        {
            return null;
        }

        if (!_icons.TryGetValue(skillId, out var icon))
        {
            icon = new[] { skillId, BaseOf(skillId) ?? skillId }
                .Select(id => Path.Combine(IconFolder, $"{id}.png"))
                .FirstOrDefault(File.Exists);
            _icons[skillId] = icon;
        }

        return icon;
    }

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

    /// <summary>
    /// Une varios archivos: los posteriores pisan a los anteriores. Así lo extraído del juego
    /// (<c>%LOCALAPPDATA%\HysteriaMeter\Juego\skills.json</c>) se completa con lo cargado a mano en <c>data/</c>.
    /// </summary>
    public static SkillNames Load(params string[] paths)
    {
        var names = new Dictionary<uint, string>();
        foreach (var path in paths)
        {
            foreach (var (id, name) in Read(path))
            {
                names[id] = name;
            }
        }

        return names.Count == 0 ? Empty : new SkillNames(names);
    }

    private static Dictionary<uint, string> Read(string path)
    {
        var names = new Dictionary<uint, string>();
        if (!File.Exists(path))
        {
            return names;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
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
        }
        catch (JsonException)
        {
            // Un skills.json mal escrito no debe impedir abrir el meter: se ven los IDs.
            names.Clear();
        }

        return names;
    }
}
