using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HysteriaMeter.App.Overlay;

/// <summary>Preferencias del usuario en JSON en la carpeta de datos (<see cref="AppPaths"/>), entre sesiones.</summary>
internal static class UserStore
{
    private static readonly string Folder = AppPaths.DataFolder;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static T? Load<T>(string fileName)
        where T : class
    {
        var path = Path.Combine(Folder, fileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un archivo roto no debe impedir que arranque el overlay: se vuelve a los valores por defecto.
            return null;
        }
    }

    public static void Save<T>(string fileName, T value)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, fileName), JsonSerializer.Serialize(value, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No poder guardar preferencias no es motivo para fallar.
        }
    }
}
