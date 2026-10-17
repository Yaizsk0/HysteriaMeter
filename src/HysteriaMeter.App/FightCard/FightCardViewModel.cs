using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.FightCard;

/// <summary>
/// Tarjeta de fin de pelea (mock Mini): al derrotar un boss, tu DPS, tu puesto, tu percentil contra ese boss y la
/// diferencia con tu intento anterior. Todo sale de la pelea y del historial; lo que no existe no se muestra.
/// </summary>
public sealed partial class FightCardViewModel : ObservableObject
{
    private readonly EncounterSnapshot _snapshot;
    private readonly MeterSession _session;
    private readonly ActorSnapshot _local;

    /// <param name="bossOnly">"Solo boss" de la ventana en una pelea de boss: TU DPS y PUESTO cuentan solo el objetivo principal.</param>
    public FightCardViewModel(EncounterSnapshot snapshot, MeterSession session, bool bossOnly)
    {
        _snapshot = snapshot;
        _session = session;
        _local = snapshot.Actors.First(a => a.IsLocalPlayer);
        Eyebrow = $"BOSS DERROTADO · {Length(snapshot.Duration)}";
        Title = MeterViewModel.TitleOf(snapshot);
        Func<ActorSnapshot, double> metric = bossOnly ? a => a.MainTargetDps : a => a.Dps;
        Dps = ActorRowViewModel.Compact(metric(_local));
        var (rank, players) = RankOf(snapshot.Actors, metric);
        Rank = $"{rank} / {players}";
        TopSkill = TopSkillText(_local, session.SkillNames);
        CapturePath = Path.Combine(MeterViewModel.ExportFolder, $"{Path.GetFileNameWithoutExtension(MeterViewModel.KillImagePath(snapshot))} captura.png");
        session.ResetRequested += OnReset;
    }

    /// <summary>La tarjeta necesita tu fila: sin jugador local (o sin daño propio) no hay nada tuyo que mostrar.</summary>
    public static bool CanShow(EncounterSnapshot snapshot) =>
        MeterViewModel.IsBossKill(snapshot) && snapshot.Actors.Any(a => a.IsLocalPlayer && a.Dps > 0);

    /// <summary>La ventana se cierra (✕, Esc, Reiniciar, o al abrir el detalle).</summary>
    public event Action? CloseRequested;

    /// <summary>"Ver detalle": el overlay abre el detalle de tu fila.</summary>
    public event Action? DetailRequested;

    /// <summary>"Guardar captura": el overlay guarda su imagen en esta ruta.</summary>
    public event Action<string>? SaveRequested;

    public Guid FightId => _snapshot.Id;

    /// <summary>"BOSS DERROTADO · 4:12".</summary>
    public string Eyebrow { get; }

    public string Title { get; }

    public string Dps { get; }

    /// <summary>"2 / 6": tu puesto entre los que hicieron daño, con la misma métrica que TU DPS.</summary>
    public string Rank { get; }

    /// <summary>"Tu skill top: Bittercold Wind (21,4%)", sumando las variantes de una skill (mismo nombre base).</summary>
    public string? TopSkill { get; }

    public bool HasTopSkill => TopSkill is not null;

    /// <summary>Exportes\2026-10-16 21-03 Tarka captura.png: no pisa la imagen automática de "imagen al derrotar un boss".</summary>
    public string CapturePath { get; }

    /// <summary>Percentil de tu DPS entre tus intentos contra este boss (con este personaje); <c>null</c> con menos de 2.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPercentile), nameof(PercentileText), nameof(PercentileBrush))]
    public partial int? Percentile { get; set; }

    public bool HasPercentile => Percentile is not null;

    public string PercentileText => Percentile?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Mismos colores que el historial (HistoryItem.TierBrush).</summary>
    public Brush PercentileBrush => HistoryItem.TierBrush(Percentile ?? 0);

    [ObservableProperty]
    public partial string PercentileHint { get; set; } = string.Empty;

    /// <summary>"+8% vs tu intento anterior · mejor intento: 402k"; <c>null</c> si es tu primer intento contra este boss.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComparison))]
    public partial string? Comparison { get; set; }

    public bool HasComparison => Comparison is not null;

    /// <summary>Mejor o igual que el intento anterior (verde); si no, rojo.</summary>
    [ObservableProperty]
    public partial bool IsImprovement { get; set; } = true;

    /// <summary>Resultado de "Guardar captura" ("Imagen guardada: …" o el error).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveMessage))]
    public partial string SaveMessage { get; set; } = string.Empty;

    public bool HasSaveMessage => SaveMessage.Length > 0;

    /// <summary>
    /// Lee el historial: intento anterior, mejor intento y los DPS de los intentos contra este boss para el percentil.
    /// Compara con el DPS de la pelea entera, que es lo que guarda el historial (como el aviso de récord).
    /// </summary>
    public async Task LoadAsync()
    {
        if (_session.History is not { } history || _snapshot.TargetName is not { Length: > 0 } target)
        {
            return;
        }

        var id = _snapshot.Id.ToString();
        var dps = _local.Dps;
        try
        {
            var previous = await history.PreviousLocalDpsAsync(target, id, _local.Name).ConfigureAwait(true);
            var best = await history.BestLocalDpsAsync(target, id, _local.Name).ConfigureAwait(true);
            (Comparison, IsImprovement) = ComparisonText(dps, previous, best);

            // Las últimas 200 peleas, como el historial: sin la actual (puede estar guardada o no) y con su DPS a mano.
            var recent = await history.ListRecentAsync(200).ConfigureAwait(true);
            IReadOnlyCollection<double> attempts =
            [
                .. recent.Where(r => r.TargetName == target && r.LocalName == _local.Name && r.Id != id && r.LocalDps is > 0).Select(r => r.LocalDps!.Value),
                dps,
            ];
            PercentileHint = $"Entre tus {attempts.Count} intentos contra {target}";
            Percentile = attempts.Count >= 2 ? HistoryItem.PercentileOf(dps, attempts) : null;
        }
        catch (DbException)
        {
            // La base no responde: la tarjeta queda con lo de la pelea (DPS, puesto y skill top).
        }
    }

    /// <summary>Empezó otra pelea: la tarjeta de la anterior ya no corresponde.</summary>
    public bool IsStale() => _session.Engine.CurrentSnapshot() is { } current && current.Id != _snapshot.Id;

    /// <summary>Suelta la sesión (la ventana lo llama al cerrarse).</summary>
    public void Detach() => _session.ResetRequested -= OnReset;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private void ShowDetail() => DetailRequested?.Invoke();

    [RelayCommand]
    private void SaveCapture() => SaveRequested?.Invoke(CapturePath);

    private void OnReset() => CloseRequested?.Invoke();

    /// <summary>Tu puesto (1 = el que más pegó) y cuántos hicieron daño, con <paramref name="metric"/>.</summary>
    public static (int Rank, int Players) RankOf(IReadOnlyList<ActorSnapshot> actors, Func<ActorSnapshot, double> metric)
    {
        var local = actors.First(a => a.IsLocalPlayer);
        var mine = metric(local);
        return (1 + actors.Count(a => !a.IsLocalPlayer && metric(a) > mine), actors.Count(a => a.IsLocalPlayer || metric(a) > 0));
    }

    /// <summary>La skill que más daño hizo, juntando las variantes con el mismo nombre; <c>null</c> sin skills.</summary>
    public static string? TopSkillText(ActorSnapshot actor, SkillNames names)
    {
        var top = actor.Skills
            .GroupBy(s => names.Of(s.SkillId), StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Share: g.Sum(s => s.Share)))
            .OrderByDescending(g => g.Share)
            .FirstOrDefault();
        return top.Name is null ? null : $"Tu skill top: {top.Name} ({(top.Share * 100).ToString("0.0", CultureInfo.CurrentCulture)}%)";
    }

    /// <summary>
    /// "+8% vs tu intento anterior · mejor intento: 402k", o "· ¡récord! (antes 402k)" si superó el mejor. <c>null</c>
    /// sin intento anterior. El color: verde si no empeoró.
    /// </summary>
    public static (string? Text, bool Better) ComparisonText(double dps, double? previous, double? best)
    {
        if (previous is not { } before || before <= 0)
        {
            return (null, true);
        }

        var percent = (int)Math.Round((dps - before) / before * 100, MidpointRounding.AwayFromZero);
        var text = percent == 0
            ? "Igual que tu intento anterior"
            : $"{percent.ToString("+0;-0", CultureInfo.InvariantCulture)}% vs tu intento anterior";
        if (best is { } record && record > 0)
        {
            text += dps > record
                ? $" · ¡récord! (antes {ActorRowViewModel.Compact(record)})"
                : $" · mejor intento: {ActorRowViewModel.Compact(record)}";
        }

        return (text, percent >= 0);
    }

    private static string Length(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
}
