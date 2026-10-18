// El historial en su ancho mínimo, con la fila que tiene nota: si objetivo, intento y nota no entran, se recorta el
// objetivo como el mock y el intento sigue a la vista.

using HysteriaMeter.App.History;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    private static readonly State[] HistorialAnchoStates =
    [
        new("06b-historial-ancho-minimo", h =>
        {
            var window = new HistoryWindow(h.HistoryWindowOpen());
            window.Width = window.MinWidth;
            return window;
        }),
    ];
}
