using System.IO;
using System.Text.Json;

namespace HysteriaMeter.App.Welcome;

/// <summary>
/// Qué hay en la carpeta <c>Juego</c> (lo que deja el extractor): skills con nombre, íconos y emblemas de clase.
/// Solo cuenta archivos propios del meter; no lee nada del juego.
/// </summary>
/// <param name="Folder">La carpeta real (<c>AppPaths.In("Juego")</c>; en modo portable, <c>datos\Juego</c> junto al .exe).</param>
/// <param name="HasSkillFile">Si existe <c>skills.json</c>: es lo que decide "Listo" o "Revisar".</param>
public sealed record GameDataSummary(string Folder, bool HasSkillFile, int Skills, int Icons, int Emblems)
{
    /// <summary>Cuenta lo que hay en <paramref name="folder"/>. Una carpeta que falta o un archivo roto dan 0, no un error.</summary>
    public static GameDataSummary Read(string folder)
    {
        var skillFile = Path.Combine(folder, "skills.json");
        return new GameDataSummary(
            folder,
            File.Exists(skillFile),
            CountSkills(skillFile),
            CountPngs(Path.Combine(folder, "iconos")),
            CountPngs(Path.Combine(folder, "clases")));
    }

    /// <summary>Las mismas reglas que SkillNames.Load: <c>{"skills": {"id": "nombre"}}</c>, con comentarios y comas finales.</summary>
    public static int CountSkills(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("skills", out var skills)
                && skills.ValueKind == JsonValueKind.Object
                ? skills.EnumerateObject().Count(e => uint.TryParse(e.Name, out _) && e.Value.ValueKind == JsonValueKind.String && e.Value.GetString() is { Length: > 0 })
                : 0;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static int CountPngs(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.png").Count() : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
