using System.Globalization;
using System.Windows.Media;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.Converters;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

/// <summary>Configuración contra el mock (paso 7): orden de las tarjetas de TEMA y chips de FUENTE en su propia fuente.</summary>
public sealed class ConfigVisualTests
{
    [Fact]
    public void Theme_cards_follow_the_mock_order_without_moving_the_fallback_theme()
    {
        Assert.Equal(["Abismo", "Dorado", "Mínimo", "Carmesí"], ThemeCard.Ordered(MeterThemes.All).Select(t => t.Name));

        // El primero de MeterThemes.All sigue siendo el de reemplazo de un nombre desconocido.
        Assert.Equal(MeterThemes.Default, MeterThemes.All[0].Name);
        Assert.Equal(MeterThemes.Default, MeterThemes.Of("no existe").Name);
    }

    [Fact]
    public void A_theme_outside_the_order_goes_last()
    {
        var extra = new MeterTheme("Nuevo", "De prueba", new Dictionary<string, Color>());

        var ordered = ThemeCard.Ordered([extra, .. MeterThemes.All]).Select(t => t.Name).ToList();

        Assert.Equal("Nuevo", ordered[^1]);
        Assert.Equal(MeterThemes.All.Count + 1, ordered.Count);
    }

    [Theory]
    [InlineData("Barlow Condensed", "#Barlow")]
    [InlineData("IBM Plex Sans", "#IBM Plex Sans")]
    [InlineData("Segoe UI", "Segoe UI")]
    public void Font_chip_is_written_in_its_own_font(string name, string source)
    {
        // Registra el esquema pack:// sin crear la App (las fuentes embebidas lo usan).
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var family = (FontFamily)new FontPreviewConverter().Convert(name, typeof(FontFamily), null!, CultureInfo.InvariantCulture);

        Assert.EndsWith(source, family.Source, StringComparison.Ordinal);
        Assert.Equal(MeterViewModel.FontOf(name).Source, family.Source);
    }
}
