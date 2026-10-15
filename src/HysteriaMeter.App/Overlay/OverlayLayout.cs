using System.IO;
using System.Text.Json;

namespace HysteriaMeter.App.Overlay;

/// <summary>Posición, tamaño y zoom del overlay, guardados entre sesiones en %LOCALAPPDATA%\HysteriaMeter.</summary>
internal sealed record OverlayLayout(double Left, double Top, double Width, double Zoom, double Height = 0)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HysteriaMeter", "overlay.json");

    public static OverlayLayout? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<OverlayLayout>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un archivo roto no debe impedir que arranque el overlay: se vuelve al tamaño por defecto.
            return null;
        }
    }

    public static void Save(OverlayLayout layout)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(layout));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No poder guardar la posición no es motivo para fallar al cerrar.
        }
    }
}
