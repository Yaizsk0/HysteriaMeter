namespace HysteriaMeter.App.Overlay;

/// <summary>Posición, tamaño y zoom del overlay, guardados entre sesiones.</summary>
internal sealed record OverlayLayout(double Left, double Top, double Width, double Zoom, double Height = 0)
{
    private const string FileName = "overlay.json";

    public static OverlayLayout? Load() => UserStore.Load<OverlayLayout>(FileName);

    public static void Save(OverlayLayout layout) => UserStore.Save(FileName, layout);
}
