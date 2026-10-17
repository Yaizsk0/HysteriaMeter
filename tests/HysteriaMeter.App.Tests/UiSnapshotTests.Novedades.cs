using System.Windows;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    /// <summary>
    /// Novedades en ventanas de alto fijo (como la app, que no crece con el contenido): seis novedades de prueba del
    /// largo de las reales, el caso que en una ventana de 455 × 275 compacta tapaba las filas y dejaba afuera el botón.
    /// </summary>
    private static readonly State[] NewsStates =
    [
        new("51-novedades-compacto-455x275", h => WithLongNews(() => FixedSize(Live(h.Demo(compact: true)), 455, 275))),
        new("52-novedades-380x300", h => WithLongNews(() => FixedSize(Live(h.Demo()), 380, 300))),
        new("53-novedades-380x560", h => WithLongNews(() => FixedSize(Live(h.Demo()), 380, 560))),
    ];

    /// <summary>Overlay del tamaño que eligió el usuario: sin SizeToContent, lo que no entra se desplaza.</summary>
    private static OverlayWindow FixedSize(MeterViewModel viewModel, double width, double height)
    {
        var window = Overlay(viewModel, width);
        window.SizeToContent = SizeToContent.Manual;
        window.Height = height;
        return window;
    }

    /// <summary>Arma la ventana con las seis novedades de OverlayNewsTests cargadas y comprueba que el VM las tomó.</summary>
    private static Window WithLongNews(Func<Window> build) => OverlayNewsTests.WithNews(() =>
    {
        var window = build();
        return window.DataContext is MeterViewModel { HasNotice: true }
            ? window
            : throw new InvalidOperationException("El estado no tomó las novedades de prueba");
    });
}
