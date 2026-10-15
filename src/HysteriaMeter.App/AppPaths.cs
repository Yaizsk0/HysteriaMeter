using System.IO;

namespace HysteriaMeter.App;

/// <summary>
/// Dónde guarda la app sus datos (preferencias, historial, grabaciones, exportes). Por defecto en
/// %LOCALAPPDATA%\HysteriaMeter; en modo portable (un archivo <c>portable.txt</c> junto al .exe), en la
/// carpeta <c>datos</c> al lado del programa, para llevarlo en un pendrive.
/// </summary>
public static class AppPaths
{
    public const string PortableMarker = "portable.txt";

    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    public static string DataFolder { get; } = Resolve(AppContext.BaseDirectory, IsPortable,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string In(params string[] parts) => Path.Combine([DataFolder, .. parts]);

    public static string Resolve(string baseDirectory, bool portable, string localAppData) =>
        portable ? Path.Combine(baseDirectory, "datos") : Path.Combine(localAppData, "HysteriaMeter");

    /// <summary>
    /// Ruta del historial. Las versiones anteriores lo guardaban junto al .exe (y se perdía al limpiar la
    /// compilación): si existe ahí y todavía no en la carpeta de datos, se copia una vez.
    /// </summary>
    public static string Database(string configured)
    {
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        var target = In(configured);
        var legacy = Path.Combine(AppContext.BaseDirectory, configured);
        if (!File.Exists(target) && File.Exists(legacy) && !string.Equals(Path.GetFullPath(target), Path.GetFullPath(legacy), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                {
                    if (File.Exists(legacy + suffix))
                    {
                        File.Copy(legacy + suffix, target + suffix);
                    }
                }
            }
            catch (IOException)
            {
                // Si no se puede copiar, se sigue usando el viejo.
                return legacy;
            }
            catch (UnauthorizedAccessException)
            {
                return legacy;
            }
        }

        Directory.CreateDirectory(DataFolder);
        return target;
    }
}
