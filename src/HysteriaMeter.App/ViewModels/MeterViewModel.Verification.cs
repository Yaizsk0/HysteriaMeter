using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// La verificación por objetivo (<see cref="TargetVerification"/>): en Configuración → Diagnóstico, en el tooltip de la
/// vida del objetivo y en el reporte de un problema (<see cref="BugReport.Verification"/>).
/// </summary>
public sealed partial class MeterViewModel
{
    /// <summary>Objetivos que lista Diagnóstico; el resto queda en "y N más" (una pelea con muchos adds).</summary>
    public const int VerificationLineLimit = 10;

    // La pelea que se verifica: la fijada, la en curso o la última terminada. En las vistas RUN y SESIÓN también, porque
    // esas suman peleas y no tienen verificación. La guarda cada refresco (en pausa queda la que se congeló).
    private EncounterSnapshot? _verified;

    // La pelea abierta del historial o de un archivo: si no trae verificación, puede ser de antes de que existiera.
    private EncounterSnapshot? _savedPinned;

    /// <summary>"Cierran 2 de 3 objetivos", o por qué no hay verificación. Se refresca con Configuración abierta.</summary>
    [ObservableProperty]
    public partial string Verification { get; set; } = string.Empty;

    /// <summary>Una línea por objetivo verificado (<see cref="VerificationLines"/>); vacío sin verificación.</summary>
    [ObservableProperty]
    public partial string VerificationDetail { get; set; } = string.Empty;

    private void UpdateVerification()
    {
        if (!IsSettingsOpen)
        {
            return;
        }

        Verification = VerificationSummary(_verified, saved: _verified is not null && ReferenceEquals(_verified, _savedPinned));
        VerificationDetail = VerificationLines(_verified, npcId => _engine.Entities.Npcs.Of(npcId)?.Name);
    }

    /// <summary>
    /// La verificación del objetivo principal: la primera de la lista con su plantilla (<see cref="EncounterSnapshot.TargetNpcId"/>).
    /// <c>null</c> si el principal no tiene plantilla o ninguna verificación es de ella.
    /// </summary>
    public static TargetVerification? MainVerification(EncounterSnapshot snapshot) =>
        snapshot.TargetNpcId is { } npc ? snapshot.Verifications?.FirstOrDefault(v => v.NpcId == npc) : null;

    /// <summary>
    /// La línea de verificación del tooltip de la vida: "Verificación: cierra" o "Verificación: no cierra, falta 2.0k
    /// (25,0 % de la vida perdida)" ("sobra" si el meter contó de más). <c>null</c> sin verificación del principal,
    /// también en las vistas RUN y SESIÓN: su objetivo principal puede no ser el de la pelea, y solo la pelea se verifica.
    /// </summary>
    public static string? HealthCheck(EncounterSnapshot snapshot)
    {
        if (MainVerification(snapshot) is not { } verification)
        {
            return null;
        }

        if (verification.Closes)
        {
            return "Verificación: cierra";
        }

        var culture = CultureInfo.CurrentCulture;
        var amount = ActorRowViewModel.Compact(Math.Abs(verification.Difference));
        var share = Math.Abs(ShareOfLost(verification)).ToString("P1", culture);
        return $"Verificación: no cierra, {(verification.Difference < 0 ? "falta" : "sobra")} {amount} ({share} de la vida perdida)";
    }

    /// <summary>
    /// El resumen de Diagnóstico: "Cierran 2 de 3 objetivos" o, sin verificación, por qué. Una pelea guardada
    /// (<paramref name="saved"/>: del historial o de un archivo) sin verificación puede ser de antes de que existiera o no
    /// haber tenido objetivos verificables: la base guarda lo mismo en los dos casos y no se distinguen.
    /// </summary>
    public static string VerificationSummary(EncounterSnapshot? fight, bool saved)
    {
        if (fight is null)
        {
            return "Sin pelea: la verificación es de la pelea que muestra el meter.";
        }

        if (fight.Verifications is not { Count: > 0 } verifications)
        {
            return saved
                ? "Pelea guardada sin verificación: es de antes de que existiera o no tuvo objetivos con la vida del servidor conocida."
                : "La pelea no tiene objetivos con la vida del servidor conocida: sin ella no hay con qué comparar el daño.";
        }

        return CountText(verifications);
    }

    /// <summary>
    /// Una línea por objetivo, en el orden de la verificación (de mayor a menor daño contado: el principal primero):
    /// "Kromede · no cierra · vida 10.000 → 2.000 · contado 6.000 · diferencia -2.000 (-25,0 % de la vida perdida)".
    /// El overkill va solo si lo hubo. Pasado <see cref="VerificationLineLimit"/>, "y N más". El nombre sale de
    /// <paramref name="nameOf"/> (data/npcs.json), o del objetivo de la pelea si es su plantilla, o "NPC 2400123"; sin
    /// plantilla, "entidad 34157". Vacío sin verificación.
    /// </summary>
    public static string VerificationLines(EncounterSnapshot? fight, Func<uint, string?> nameOf)
    {
        if (fight?.Verifications is not { Count: > 0 } verifications)
        {
            return string.Empty;
        }

        var culture = CultureInfo.CurrentCulture;
        string Name(TargetVerification v) => v.NpcId is { } npc
            ? nameOf(npc) ?? (npc == fight.TargetNpcId ? FightTarget.Name(fight.TargetName) : null) ?? FightTarget.NpcLabel(npc)
            : $"entidad {v.TargetId.ToString(CultureInfo.InvariantCulture)}";

        var lines = verifications.Take(VerificationLineLimit).Select(v =>
            $"{Name(v)} · {(v.Closes ? "cierra" : "no cierra")} · vida {v.StartHp.ToString("N0", culture)} → {v.EndHp.ToString("N0", culture)} · " +
            $"contado {v.Counted.ToString("N0", culture)}" +
            (v.Overkill > 0 ? $" · overkill {v.Overkill.ToString("N0", culture)}" : string.Empty) +
            $" · {DifferenceText(v, culture)}").ToList();
        if (verifications.Count > VerificationLineLimit)
        {
            lines.Add($"y {(verifications.Count - VerificationLineLimit).ToString(culture)} más");
        }

        return string.Join('\n', lines);
    }

    /// <summary>"Cierran 2 de 3 objetivos" ("Cierra 1 de 2 objetivos", "Cierra 0 de 1 objetivo").</summary>
    internal static string CountText(IReadOnlyCollection<TargetVerification> verifications)
    {
        var (closes, total) = (verifications.Count(v => v.Closes), verifications.Count);
        var culture = CultureInfo.CurrentCulture;
        return $"{(closes == 1 || total == 1 ? "Cierra" : "Cierran")} {closes.ToString(culture)} de {total.ToString(culture)} {(total == 1 ? "objetivo" : "objetivos")}";
    }

    /// <summary>"diferencia -2.000 (-25,0 % de la vida perdida)", con signo también si sobra ("+100 (+25,0 %...)").</summary>
    internal static string DifferenceText(TargetVerification verification, CultureInfo culture)
    {
        var sign = verification.Difference > 0 ? "+" : string.Empty;
        return $"diferencia {sign}{verification.Difference.ToString("N0", culture)} ({sign}{ShareOfLost(verification).ToString("P1", culture)} de la vida perdida)";
    }

    /// <summary>La diferencia sobre la vida perdida (la base de la tolerancia del 1 %); 0 si no perdió vida.</summary>
    private static double ShareOfLost(TargetVerification verification) =>
        verification.StartHp - verification.EndHp is var lost && lost > 0 ? (double)verification.Difference / lost : 0;
}
