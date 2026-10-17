using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HysteriaMeter.App.Converters;

/// <summary>
/// Recorte redondeado para un elemento: el CornerRadius de un Border no recorta a sus hijos, así que la barra de una
/// fila pintaría las esquinas. Valores: ActualWidth, ActualHeight y el radio (CornerRadius, se usa la esquina superior
/// izquierda, o double).
/// </summary>
public sealed class RoundedClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [double width, double height, var corner] || width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var radius = corner switch
        {
            CornerRadius r => r.TopLeft,
            double d => d,
            _ => 0,
        };

        var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        clip.Freeze();
        return clip;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
