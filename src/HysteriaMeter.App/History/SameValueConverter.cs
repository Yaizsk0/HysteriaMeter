using System.Globalization;
using System.Windows.Data;

namespace HysteriaMeter.App.History;

/// <summary>
/// <c>true</c> si los dos valores son iguales: marca la fila de la pelea que se está mirando (su Id contra
/// PinnedHistoryId) sin volver a armar la lista, así no se pierde el scroll al abrir otra pelea.
/// </summary>
public sealed class SameValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [var first, var second] && first is not null && Equals(first, second);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
