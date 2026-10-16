using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;

namespace HysteriaMeter.App.Converters;

/// <summary>Cursor de mover si la ventana se puede arrastrar; flecha común si está fijada.</summary>
public sealed class MoveCursorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Cursors.SizeAll : Cursors.Arrow;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
