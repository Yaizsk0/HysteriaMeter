using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una fila del desglose por skill, con las columnas del mock de detalle.</summary>
public sealed partial class SkillRowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Ruta del ícono de la skill; <c>null</c> si no hay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial string? Icon { get; set; }

    public bool HasIcon => Icon is not null;

    /// <summary>ID de la skill, para renombrarla.</summary>
    public uint SkillId { get; private set; }

    /// <summary>
    /// "sin nombre" si el juego solo mandó el ID, "invocación" si la pegó una invocación del jugador y la parte del daño
    /// que viene de ticks de DoT, si tiene. En la fila de las invocaciones juntas, el total del jugador sin ellas.
    /// </summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    private string _extraNote = string.Empty;

    /// <summary>Fila de "Invocaciones separadas": junta las skills de las invocaciones y no se renombra.</summary>
    public bool IsSummonGroup { get; private set; }

    /// <summary>Tooltip de la fila: PROM., ESPALDA (su columna se oculta con la ventana angosta) y cómo renombrarla.</summary>
    [ObservableProperty]
    public partial string Tip { get; set; } = string.Empty;

    // Al renombrarla cambia el nombre y, con él, la nota "sin nombre".
    partial void OnNameChanged(string value)
    {
        if (!IsSummonGroup)
        {
            Note = NoteText(value, _extraNote);
        }
    }

    /// <summary>"sin nombre · invocación · DoT 62%": las partes que correspondan, separadas por " · ".</summary>
    public static string NoteText(string name, string extraNote) =>
        string.Join(" · ", new[] { name.StartsWith("Skill ", StringComparison.Ordinal) ? "sin nombre" : null, extraNote.Length > 0 ? extraNote : null }.OfType<string>());

    [ObservableProperty]
    public partial string Damage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Share { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Hits { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Critical { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Perfect { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DoubleHit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Front { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Back { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MultiHit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaxHit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Average { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Dps { get; set; } = string.Empty;

    /// <summary>Largo de la barra respecto de la skill que más pegó (0..1).</summary>
    [ObservableProperty]
    public partial double Bar { get; set; }

    public void Update(string name, SkillSnapshot skill, long top, TimeSpan duration, string? icon = null)
    {
        var culture = CultureInfo.CurrentCulture;
        var counts = skill.Counts ?? HitCounts.Empty;
        SkillId = skill.SkillId;
        IsSummonGroup = false;
        var dotNote = skill.PeriodicDamage == 0 ? null : $"DoT {((double)skill.PeriodicDamage / skill.Damage).ToString("P0", culture)}";
        _extraNote = string.Join(" · ", new[] { skill.FromSummon ? "invocación" : null, dotNote }.OfType<string>());
        Name = name;
        Note = NoteText(name, _extraNote);
        Icon = icon;
        Damage = ActorRowViewModel.Compact(skill.Damage);
        Share = skill.Share.ToString("P1", culture);
        Hits = skill.Hits.ToString("N0", culture);
        Critical = Rate(skill.Hits, skill.Crits);
        SetModifiers(counts);
        MaxHit = skill.Hits == 0 ? "–" : ActorRowViewModel.Compact(skill.MaxHit);
        Average = skill.Hits == 0 ? "–" : ActorRowViewModel.Compact((double)skill.DirectDamage / skill.Hits);
        Dps = ActorRowViewModel.Compact(skill.Damage / duration.TotalSeconds);
        Bar = top == 0 ? 0 : (double)skill.Damage / top;
        Tip = TipText(Back, Average) + Environment.NewLine + "Doble clic para ponerle nombre";
    }

    /// <summary>
    /// La fila "Invocaciones" de "Invocaciones separadas": <paramref name="group"/> suma las skills de las invocaciones y
    /// <paramref name="withoutSummons"/> es el total del jugador sin ellas.
    /// </summary>
    public void UpdateSummons(SkillSnapshot group, long top, TimeSpan duration, string withoutSummons)
    {
        Update("Invocaciones", group, top, duration);
        IsSummonGroup = true;
        Note = withoutSummons;
        Tip = TipText(Back, Average) + Environment.NewLine + "Las skills de sus invocaciones juntas";
    }

    /// <summary>
    /// PERF., DOBLE, FRENTE, ESPALDA y MULTI: el % de golpes con ese modificador, como las columnas de Combat Analysis.
    /// Van sobre los golpes con modificadores leídos (<see cref="HitCounts.Hits"/>), no sobre GOLPES.
    /// </summary>
    public void SetModifiers(HitCounts counts)
    {
        Perfect = Rate(counts.Hits, counts.Perfect);
        DoubleHit = Rate(counts.Hits, counts.DoubleHit);
        Front = Rate(counts.Hits, counts.Front);
        Back = Rate(counts.Hits, counts.Back);
        MultiHit = Rate(counts.Hits, counts.MultiHit);
    }

    /// <summary>"Por la espalda 25% · promedio 9.0k por golpe directo (sin ticks de DoT)".</summary>
    public static string TipText(string back, string average) =>
        $"Por la espalda {back} · promedio {average} por golpe directo (sin ticks de DoT)";

    internal static string Rate(int hits, int count) =>
        hits == 0 ? "–" : ((double)count / hits).ToString("P0", CultureInfo.CurrentCulture);
}
