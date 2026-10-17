using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una muerte del jugador elegido, para la pestaña Muertes del detalle.</summary>
public sealed record DeathItem(string When, string Summary, string LastHits)
{
    /// <summary>"1:52": el reloj de la pelea, como en el mock Muertes.</summary>
    public string Clock { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public bool HasKillingBlow { get; init; }

    /// <summary>Quién lo mató, para la frase "Lo mató {Killer} con {SkillName} por {Amount}{FlagsText}".</summary>
    public string Killer { get; init; } = string.Empty;

    public string SkillName { get; init; } = string.Empty;

    public string Amount { get; init; } = string.Empty;

    /// <summary>" (crítico)" o vacío.</summary>
    public string FlagsText { get; init; } = string.Empty;

    /// <summary>Golpes recibidos (y curaciones, si están en el registro) en los segundos previos, del más viejo al golpe final.</summary>
    public IReadOnlyList<DeathRecapRow> Rows { get; init; } = [];
}

/// <summary>Pestaña Muertes: la lista de muertes del jugador, cuál se ve y su recap.</summary>
public sealed partial class ActorDetailViewModel
{
    public ObservableCollection<DeathItem> Deaths { get; } = [];

    [ObservableProperty]
    public partial bool HasDeaths { get; set; }

    // Pelea, jugador y cantidad con que se armó la lista: una muerte no cambia una vez registrada.
    private (Guid Fight, ulong Actor, int Count) _deathsKey;

    /// <summary>Muerte que se ve: la última por defecto; ‹ › (o las flechas) recorren las demás.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDeath), nameof(DeathPosition))]
    [NotifyCanExecuteChangedFor(nameof(PreviousDeathCommand), nameof(NextDeathCommand))]
    public partial int SelectedDeathIndex { get; set; } = -1;

    public DeathItem? SelectedDeath => SelectedDeathIndex >= 0 && SelectedDeathIndex < Deaths.Count ? Deaths[SelectedDeathIndex] : null;

    /// <summary>"MUERTE 2 DE 3 · 1:52".</summary>
    public string DeathPosition => SelectedDeath is { } death ? $"MUERTE {SelectedDeathIndex + 1} DE {Deaths.Count} · {death.Clock}" : string.Empty;

    /// <summary>"ÚLTIMOS 10 SEGUNDOS": la ventana real con que el motor guarda los golpes previos.</summary>
    public static string RecapTitle { get; } = $"ÚLTIMOS {DeathRecap.Window.TotalSeconds:0} SEGUNDOS";

    /// <summary>La línea del registro donde murió (o el último golpe antes), para marcarla en el log de la pestaña.</summary>
    [ObservableProperty]
    public partial LogLine? DeathLogLine { get; set; }

    [RelayCommand(CanExecute = nameof(CanPreviousDeath))]
    private void PreviousDeath() => SelectedDeathIndex--;

    private bool CanPreviousDeath() => SelectedDeathIndex > 0;

    [RelayCommand(CanExecute = nameof(CanNextDeath))]
    private void NextDeath() => SelectedDeathIndex++;

    private bool CanNextDeath() => SelectedDeathIndex >= 0 && SelectedDeathIndex < Deaths.Count - 1;

    partial void OnSelectedDeathIndexChanged(int value) => UpdateDeathLogLine();

    private void UpdateDeathLogLine()
    {
        if (SelectedDeath is not { } death || Log is not { } log)
        {
            DeathLogLine = null;
            return;
        }

        DeathLogLine = log.Lines.LastOrDefault(l => l.IsDeath && l.SourceId == ActorId && l.Timestamp == death.Timestamp)
            ?? log.Lines.LastOrDefault(l => l.Timestamp <= death.Timestamp);
    }

    private void UpdateDeaths(EncounterSnapshot snapshot, ulong actorId)
    {
        var deaths = (snapshot.Deaths ?? []).Where(d => d.ActorId == actorId).ToList();
        HasDeaths = deaths.Count > 0;
        var key = (snapshot.Id, actorId, deaths.Count);
        if (key == _deathsKey)
        {
            return;
        }

        // Si se estaba viendo la última, sigue en la última (la nueva); si no, se queda en la que estaba.
        var sameList = _deathsKey.Fight == snapshot.Id && _deathsKey.Actor == actorId;
        var followLast = !sameList || SelectedDeathIndex < 0 || SelectedDeathIndex >= Deaths.Count - 1;
        var keep = SelectedDeathIndex;
        _deathsKey = key;

        // Las curaciones del recap salen del registro (el motor solo guarda los golpes): se lee una vez por muerte nueva.
        var log = deaths.Count > 0 ? FightLog() : [];
        Deaths.Clear();
        foreach (var death in deaths)
        {
            Deaths.Add(Describe(death, snapshot.Start, names, log));
        }

        // Pasar por -1 avisa a la vista aunque el índice quede igual (la lista es otra).
        SelectedDeathIndex = -1;
        SelectedDeathIndex = followLast ? Deaths.Count - 1 : Math.Min(keep, Deaths.Count - 1);
    }

    /// <summary>"01:23 · Lo mató Tarka con Skill 502 por 9.0k (crítico)", el resumen de los golpes previos y el recap.</summary>
    /// <param name="log">Registro de la pelea, para sumar al recap las curaciones que recibió; sin él, solo los golpes.</param>
    public static DeathItem Describe(DeathRecap death, DateTimeOffset start, SkillNames names, IReadOnlyList<CombatLogEntry>? log = null)
    {
        var offset = death.Timestamp - start;
        var when = $"{(int)offset.TotalMinutes:00}:{offset.Seconds:00}";
        var clock = DungeonSummaryViewModel.Length(offset < TimeSpan.Zero ? TimeSpan.Zero : offset);
        var rows = RecapRows(death, names, log);
        if (death.KillingBlow is not { } blow)
        {
            return new DeathItem(when, "Murió (sin golpes registrados antes)", string.Empty) { Clock = clock, Timestamp = death.Timestamp, Rows = rows };
        }

        var flags = LogViewModel.Flags(blow.Modifiers);
        var flagsText = flags.Length > 0 ? $" ({flags})" : string.Empty;
        var skill = names.Of(blow.SkillId);
        var amount = ActorRowViewModel.Compact(blow.Amount);
        var summary = $"Lo mató {blow.SourceName} con {skill} por {amount}{flagsText}";
        var total = death.LastHits.Sum(h => h.Amount);
        var lastHits = $"{death.LastHits.Count} golpes por {ActorRowViewModel.Compact(total)} en los {DeathRecap.Window.TotalSeconds:0} s anteriores";
        return new DeathItem(when, summary, lastHits)
        {
            Clock = clock,
            Timestamp = death.Timestamp,
            HasKillingBlow = true,
            Killer = blow.SourceName,
            SkillName = skill,
            Amount = amount,
            FlagsText = flagsText,
            Rows = rows,
        };
    }

    /// <summary>
    /// Recap de una muerte: los golpes que recibió en la ventana previa y, si están en el registro, las curaciones que le
    /// llegaron en esa misma ventana, del más viejo al golpe final ("-4,9 s · Golpe de Kromede · crítico · −61k").
    /// </summary>
    public static IReadOnlyList<DeathRecapRow> RecapRows(DeathRecap death, SkillNames names, IReadOnlyList<CombatLogEntry>? log = null)
    {
        var culture = CultureInfo.CurrentCulture;
        var since = death.Timestamp - DeathRecap.Window;
        var blow = death.KillingBlow;
        var heals = (log ?? []).Where(e => e.Kind == LogKind.Heal && e.TargetId == death.ActorId && e.Timestamp >= since && e.Timestamp <= death.Timestamp);
        return
        [
            .. death.LastHits.Concat(heals)
                .OrderBy(e => e.Timestamp)
                .ThenBy(e => ReferenceEquals(e, blow))
                .Select(e =>
                {
                    var heal = e.Kind == LogKind.Heal;
                    var flags = heal ? string.Empty : LogViewModel.Flags(e.Modifiers);
                    var what = $"{names.Of(e.SkillId)} de {e.SourceName}{(flags.Length > 0 ? $" · {flags}" : string.Empty)}";
                    var time = $"{(e.Timestamp - death.Timestamp).TotalSeconds.ToString("0.0", culture)} s";
                    var amount = (heal ? "+" : "−") + ActorRowViewModel.Compact(e.Amount);
                    return new DeathRecapRow(time, what, amount, heal, ReferenceEquals(e, blow));
                }),
        ];
    }
}
