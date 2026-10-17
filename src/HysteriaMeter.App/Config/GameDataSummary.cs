using System.IO;
using HysteriaMeter.App.ViewModels;

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

    /// <summary>Las mismas entradas que cargan SkillNames y la bienvenida, incluidos IDs repetidos.</summary>
    private static int CountSkills(string path)
    {
        try
        {
            return SkillNames.CountIn(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un archivo bloqueado no impide abrir la configuración.
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
