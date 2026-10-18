using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HysteriaMeter.App.History;

/// <summary>
/// Objetivo, intento y nota en una sola línea, como la fila del mock del historial (los hijos van en ese orden). Si no
/// entran, se recorta el objetivo, como el span con ellipsis del mock, y el intento se ve siempre. Para que el objetivo no
/// desaparezca en una ventana angosta, conserva hasta <see cref="TargetMinWidth"/> antes de que la nota ocupe lo suyo; si a
/// la nota le queda menos de <see cref="NoteMinWidth"/>, no se muestra (va completa en el tooltip de la fila) y ese lugar
/// vuelve al objetivo. Los hijos se apoyan abajo: con la letra más chica del intento y la nota, los tres quedan casi sobre
/// la misma línea de base.
/// </summary>
public sealed class RowTitlePanel : Panel
{
    /// <summary>Lo que conserva el objetivo antes de dejarle lugar a la nota.</summary>
    public const double TargetMinWidth = 96;

    /// <summary>Menos que esto, la nota sería solo "…": no se muestra.</summary>
    public const double NoteMinWidth = 24;

    /// <summary>La nota no entró: quedó con ancho 0 al final y se corta en el borde (ver <see cref="GetLayoutClip"/>).</summary>
    private bool _noteHidden;

    protected override Size MeasureOverride(Size availableSize)
    {
        var free = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(free);
        }

        if (InternalChildren.Count != 3)
        {
            return Line(InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize));
        }

        var (target, attempt, note) = (InternalChildren[0], InternalChildren[1], InternalChildren[2]);
        var natural = note.DesiredSize.Width;
        var widths = Split(availableSize.Width, target.DesiredSize.Width, attempt.DesiredSize.Width, natural);
        target.Measure(new Size(widths.Target, availableSize.Height));
        attempt.Measure(new Size(widths.Attempt, availableSize.Height));
        note.Measure(new Size(widths.Note, availableSize.Height));
        _noteHidden = widths.Note == 0 && natural > 0;
        return Line([target.DesiredSize, attempt.DesiredSize, note.DesiredSize]);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(x, Math.Max(0, finalSize.Height - size.Height), size.Width, size.Height));
            x += size.Width;
        }

        return finalSize;
    }

    /// <summary>
    /// Sin nota, el panel se corta en su borde derecho: la nota de ancho 0 no deja un "…" suelto. Con nota no se corta,
    /// así la última letra en cursiva (que sobresale un poco de su ancho) se ve entera.
    /// </summary>
    protected override Geometry? GetLayoutClip(Size layoutSlotSize) =>
        _noteHidden ? new RectangleGeometry(new Rect(0, -RenderSize.Height, RenderSize.Width, RenderSize.Height * 3)) : base.GetLayoutClip(layoutSlotSize);

    /// <summary>
    /// Cuánto ancho le toca a cada parte en <paramref name="available"/>, con el ancho que pide cada una: primero el
    /// intento, después el objetivo hasta <see cref="TargetMinWidth"/>, después la nota (si le quedan al menos
    /// <see cref="NoteMinWidth"/>) y el resto otra vez al objetivo.
    /// </summary>
    public static (double Target, double Attempt, double Note) Split(double available, double target, double attempt, double note)
    {
        if (double.IsPositiveInfinity(available))
        {
            return (target, attempt, note);
        }

        var shownAttempt = Math.Min(attempt, Math.Max(0, available));
        var rest = Math.Max(0, available - shownAttempt);
        var kept = Math.Min(target, Math.Min(TargetMinWidth, rest));
        var shownNote = Math.Min(note, rest - kept);
        if (shownNote < NoteMinWidth && shownNote < note)
        {
            shownNote = 0;
        }

        return (Math.Min(target, rest - shownNote), shownAttempt, shownNote);
    }

    private static Size Line(IEnumerable<Size> sizes)
    {
        var (width, height) = (0.0, 0.0);
        foreach (var size in sizes)
        {
            width += size.Width;
            height = Math.Max(height, size.Height);
        }

        return new Size(width, height);
    }
}
