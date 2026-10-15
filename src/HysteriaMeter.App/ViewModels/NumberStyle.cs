using System.Globalization;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Cómo se escriben los números del overlay, el detalle y el resumen.</summary>
public enum NumberStyle
{
    /// <summary>2.04M · 11.9k</summary>
    Compact,

    /// <summary>2.0M · 12k: más corto, para overlays angostos.</summary>
    Brief,

    /// <summary>2.040.321 con separador de miles.</summary>
    Full,
}

public static class Numbers
{
    /// <summary>Estilo activo. Lo cambia la configuración; toda la app lee el mismo.</summary>
    public static NumberStyle Style { get; set; } = NumberStyle.Compact;

    public static string Format(double value) => Format(value, Style, CultureInfo.CurrentCulture);

    public static string Format(double value, NumberStyle style, CultureInfo culture) => style switch
    {
        NumberStyle.Full => Math.Round(value).ToString("N0", culture),
        NumberStyle.Brief => value switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.0", culture) + "B",
            >= 1_000_000 => (value / 1_000_000).ToString("0.0", culture) + "M",
            >= 1_000 => (value / 1_000).ToString("0", culture) + "k",
            _ => value.ToString("N0", culture),
        },
        _ => value switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.00", culture) + "B",
            >= 1_000_000 => (value / 1_000_000).ToString("0.00", culture) + "M",
            >= 1_000 => (value / 1_000).ToString("0.0", culture) + "k",
            _ => value.ToString("N0", culture),
        },
    };

    /// <summary>Ejemplo para el selector de configuración.</summary>
    public static string Example(NumberStyle style) => Format(2_040_321, style, CultureInfo.CurrentCulture);
}
