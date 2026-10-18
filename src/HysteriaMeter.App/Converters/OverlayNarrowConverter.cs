using System.Globalization;
using System.Windows.Data;

namespace HysteriaMeter.App.Converters;

/// <summary>Apila bloques del overlay cuando el ancho disponible no alcanza; no cambia el tamaño de la ventana.</summary>
public sealed class OverlayNarrowConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var threshold = parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : 350;
        // Antes del primer arrange usamos el layout angosto: evita superponer controles durante la medición inicial.
        return value is not double width || !double.IsFinite(width) || width <= 0 || width < threshold;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
