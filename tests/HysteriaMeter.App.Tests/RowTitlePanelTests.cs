using HysteriaMeter.App.History;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Fila del historial: objetivo, intento y nota en una línea. Como el mock, si no entran se recorta el objetivo y el
/// intento se ve siempre; el objetivo conserva un mínimo antes de darle lugar a la nota.
/// </summary>
public sealed class RowTitlePanelTests
{
    [Fact]
    public void When_everything_fits_each_part_gets_what_it_asks()
    {
        Assert.Equal((135d, 50d, 95d), RowTitlePanel.Split(300, target: 135, attempt: 50, note: 95));
    }

    [Fact]
    public void The_target_is_trimmed_first_and_the_attempt_and_note_stay_like_the_mock()
    {
        // Ventana de 1040: lugar para 265. La nota entra entera y el objetivo se recorta a lo que queda.
        Assert.Equal((120d, 50d, 95d), RowTitlePanel.Split(265, target: 135, attempt: 50, note: 95));
    }

    [Fact]
    public void In_a_narrow_window_the_target_keeps_its_minimum_and_the_note_gives_way()
    {
        // Ancho mínimo: con 165 no entran el mínimo del objetivo y una nota legible; la nota se esconde (está en el
        // tooltip) y el objetivo usa ese lugar.
        Assert.Equal((115d, 50d, 0d), RowTitlePanel.Split(165, target: 135, attempt: 50, note: 95));

        // Con algo más de lugar, la nota toma lo que sobra después del mínimo del objetivo y se recorta sola.
        Assert.Equal((RowTitlePanel.TargetMinWidth, 50d, 40d), RowTitlePanel.Split(50 + RowTitlePanel.TargetMinWidth + 40, target: 135, attempt: 50, note: 95));
    }

    [Fact]
    public void A_short_target_leaves_its_unused_minimum_to_the_note()
    {
        // "Trash" pide 40: no se le reserva más de lo que pide, y la nota usa el resto.
        Assert.Equal((40d, 0d, 95d), RowTitlePanel.Split(150, target: 40, attempt: 0, note: 95));
    }

    [Fact]
    public void Without_a_width_limit_nothing_is_trimmed()
    {
        Assert.Equal((135d, 50d, 95d), RowTitlePanel.Split(double.PositiveInfinity, target: 135, attempt: 50, note: 95));
    }
}
