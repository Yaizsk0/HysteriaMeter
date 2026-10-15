using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Overlay;

/// <summary>Posición, tamaño, zoom y contenido de una ventana del overlay, guardados entre sesiones.</summary>
public sealed record OverlayLayout(
    double Left,
    double Top,
    double Width,
    double Zoom,
    double Height = 0,
    MeterTab Tab = MeterTab.Damage,
    bool ShowHealing = true)
{
    private const string FileName = "ventanas.json";

    // Versión anterior: una sola ventana en overlay.json.
    private const string LegacyFileName = "overlay.json";

    /// <summary>Ventanas guardadas; la primera es la principal.</summary>
    public static IReadOnlyList<OverlayLayout> LoadAll() =>
        UserStore.Load<List<OverlayLayout>>(FileName)
        ?? (UserStore.Load<OverlayLayout>(LegacyFileName) is { } legacy ? [legacy] : []);

    public static void SaveAll(IEnumerable<OverlayLayout> layouts) => UserStore.Save(FileName, layouts.ToList());
}
