namespace HysteriaMeter.App.ViewModels;

/// <summary>Pestañas del detalle (mock Detalle). "Objetivos" no está: el motor no guarda el daño por objetivo.</summary>
public enum DetailTab
{
    Skills,
    Deaths,
    Log,
}

/// <summary>Si una diferencia es mejora (verde), empeora (rojo) o no cuenta (tenue).</summary>
public enum DeltaTone
{
    Neutral,
    Better,
    Worse,
}

/// <summary>Texto de una diferencia o de la línea chica de un KPI, con su color.</summary>
public sealed record Delta(string Text, DeltaTone Tone = DeltaTone.Neutral)
{
    public static Delta None { get; } = new(string.Empty);

    public bool HasText => Text.Length > 0;
}

/// <summary>Una barra del gráfico "DPS en el tiempo": alto 0..1 respecto de la más alta y si cae en un momento marcado.</summary>
public sealed record ChartBar(double Height, bool IsMarked, string Tip);

/// <summary>
/// Una línea del recap de una muerte: cuánto antes pasó ("-7,8 s"), qué fue ("Golpe de Kromede · crítico") y el monto
/// ("−42k" o "+38k" si fue una curación). La vida en cada momento no se registra, así que el recap no la muestra.
/// </summary>
public sealed record DeathRecapRow(string Time, string What, string Amount, bool IsHeal, bool IsKillingBlow);

/// <summary>Una skill en la comparación: daño de cada uno, la barra de diferencia y los golpes.</summary>
/// <param name="Gain">Largo de la barra verde hacia la derecha (0..1 de media columna).</param>
/// <param name="Loss">Largo de la barra roja hacia la izquierda (0..1 de media columna).</param>
public sealed record CompareSkillRow(string Name, string? Icon, string Mine, string Theirs, double Gain, double Loss, string Hits, DeltaTone Tone, string Tip)
{
    public bool HasIcon => Icon is not null;
}

/// <summary>Las dos curvas de "DPS acumulado" sobre el mismo eje de segundos y las muertes de cada uno.</summary>
public sealed record CompareChartData(IReadOnlyList<double> Mine, IReadOnlyList<double> Theirs, IReadOnlyList<int> MineDeaths, IReadOnlyList<int> TheirDeaths)
{
    public static CompareChartData Empty { get; } = new([], [], [], []);
}
