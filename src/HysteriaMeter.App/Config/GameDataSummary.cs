using System.IO;
using System.Text.Json;

namespace HysteriaMeter.App.Config;

/// <summary>
/// Qué hay en la carpeta <c>Juego</c> (lo que deja <c>hysteria-extract</c>), para Configuración → Datos del juego:
/// skills con nombre en <c>skills.json</c>, íconos en <c>iconos\</c> y emblemas de clase en <c>clases\</c>. Solo cuenta
/// archivos en disco; los mismos que lee App al arrancar.
/// </summary>
public sealed record GameDataSummary(string Folder, bool Exists, int Skills, int Icons, int Emblems)
{
    public static GameDataSummary Read(string folder) => new(
        folder,
        Directory.Exists(folder),
        CountSkills(Path.Combine(folder, "skills.json")),
        CountImages(Path.Combine(folder, "iconos")),
        CountImages(Path.Combine(folder, "clases")));

    /// <summary>Entradas de <c>{"skills": {"id": "nombre"}}</c> con ID numérico y nombre, como las toma SkillNames.</summary>
    private static int CountSkills(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Object
                ? skills.EnumerateObject().Count(e => uint.TryParse(e.Name, out _) && e.Value.ValueKind == JsonValueKind.String && e.Value.GetString() is { Length: > 0 })
                : 0;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Mal escrito o bloqueado: el meter tampoco lo usa, así que no cuenta.
            return 0;
        }
    }

    /// <summary>Los <c>&lt;id&gt;.png</c> de una carpeta (el nombre es el ID de la skill o de la clase).</summary>
    private static int CountImages(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.png").Count(f => uint.TryParse(Path.GetFileNameWithoutExtension(f), out _))
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
