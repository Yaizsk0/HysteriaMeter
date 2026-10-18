using System.IO;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Config;

/// <summary>
/// Qué hay en la carpeta <c>Juego</c> (lo que deja <c>hysteria-extract</c>), para Configuración → Datos del juego y el
/// paso "Datos del juego" de la bienvenida: las dos cuentan con este tipo y dan el mismo número. Skills con nombre en
/// <c>skills.json</c>, íconos en <c>iconos\</c> y emblemas de clase en <c>clases\</c>. Solo cuenta archivos en disco;
/// los mismos que lee App al arrancar.
/// </summary>
/// <param name="Folder">La carpeta real (<c>AppPaths.In("Juego")</c>; en modo portable, <c>datos\Juego</c> junto al .exe).</param>
/// <param name="Exists">Si existe la carpeta: sin ella, Configuración avisa que las skills se ven con su número.</param>
/// <param name="HasSkillFile">Si existe <c>skills.json</c>: es lo que decide "Listo" o "Revisar" en la bienvenida.</param>
public sealed record GameDataSummary(string Folder, bool Exists, bool HasSkillFile, int Skills, int Icons, int Emblems)
{
    /// <summary>Cuenta lo que hay en <paramref name="folder"/>. Una carpeta que falta o un archivo roto dan 0, no un error.</summary>
    public static GameDataSummary Read(string folder)
    {
        var skillFile = Path.Combine(folder, "skills.json");
        return new GameDataSummary(
            folder,
            Directory.Exists(folder),
            File.Exists(skillFile),
            CountSkills(skillFile),
            CountImages(Path.Combine(folder, "iconos")),
            CountImages(Path.Combine(folder, "clases")));
    }

    /// <summary>
    /// Las skills con nombre que carga la app de ese archivo: lo cuenta SkillNames con su propia lectura, así el número
    /// no se aparta de lo que se ve en el meter. Un archivo que no se puede leer da 0.
    /// </summary>
    public static int CountSkills(string path)
    {
        try
        {
            return SkillNames.CountIn(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un archivo bloqueado no impide abrir la configuración ni la bienvenida.
            return 0;
        }
    }

    /// <summary>
    /// Los <c>&lt;id&gt;.png</c> de una carpeta (el nombre es el ID de la skill o de la clase): son los que buscan
    /// SkillNames y PlayerClasses. Otro .png (un fondo, una copia) no se carga y no se cuenta.
    /// </summary>
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
