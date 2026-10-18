using System.Globalization;
using System.Windows.Data;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Converters;

/// <summary>La familia de una fuente elegible por su nombre (<see cref="MeterViewModel.FontOf"/>), para escribir cada chip en la suya.</summary>
public sealed class FontPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        MeterViewModel.FontOf(value as string ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
