using System.Windows;

namespace HysteriaMeter.App.Overlay;

/// <summary>
/// Imanes al mover el overlay (como Details!): si al soltarla la ventana quedó cerca de un borde de la pantalla
/// o de otra ventana del meter, se pega a ese borde. Sirve para alinear varias ventanas sin pelear con los píxeles.
/// </summary>
public static class WindowSnap
{
    /// <summary>Distancia, en píxeles de WPF, a la que un borde atrae a la ventana.</summary>
    public const double Distance = 14;

    /// <summary>
    /// Nueva esquina superior izquierda de <paramref name="window"/>. Los bordes de <paramref name="screen"/> atraen desde
    /// adentro; los de <paramref name="others"/>, pegando la ventana al lado (o alineada) con cada una.
    /// </summary>
    public static Point Snap(Rect window, Rect screen, IReadOnlyList<Rect> others, double distance = Distance)
    {
        // Candidatos para el borde izquierdo de la ventana, por cada borde que la atrae.
        var lefts = new List<double> { screen.Left, screen.Right - window.Width };
        var tops = new List<double> { screen.Top, screen.Bottom - window.Height };
        foreach (var other in others)
        {
            // Solo atraen las ventanas que se superponen en el otro eje: si no, el imán salta a una ventana lejana.
            if (Overlaps(window.Top, window.Bottom, other.Top, other.Bottom, distance))
            {
                lefts.AddRange([other.Right, other.Left - window.Width, other.Left, other.Right - window.Width]);
            }

            if (Overlaps(window.Left, window.Right, other.Left, other.Right, distance))
            {
                tops.AddRange([other.Bottom, other.Top - window.Height, other.Top, other.Bottom - window.Height]);
            }
        }

        return new Point(Closest(window.Left, lefts, distance), Closest(window.Top, tops, distance));
    }

    private static bool Overlaps(double start, double end, double otherStart, double otherEnd, double distance) =>
        start < otherEnd + distance && otherStart < end + distance;

    private static double Closest(double value, List<double> candidates, double distance)
    {
        var best = value;
        var bestGap = distance;
        foreach (var candidate in candidates)
        {
            var gap = Math.Abs(candidate - value);
            if (gap <= bestGap)
            {
                best = candidate;
                bestGap = gap;
            }
        }

        return best;
    }
}
