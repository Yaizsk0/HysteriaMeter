using System.Globalization;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Consejos al terminar una pelea, con reglas locales sobre lo que el meter ve (RT-08, sin LLM):
/// tiempo muerto, muertes, golpes por la espalda y distancia con el mejor de la party.
/// </summary>
public static class FightAdvice
{
    /// <summary>Máximo de consejos que se muestran, para que se lean.</summary>
    public const int Max = 4;

    /// <summary>Por debajo de esta fracción de tiempo pegando, se avisa del tiempo muerto.</summary>
    public const double UptimeGoal = 0.9;

    public static IReadOnlyList<string> For(EncounterSnapshot snapshot, ActorSnapshot actor, Func<string, string>? displayName = null)
    {
        var name = displayName ?? (n => n);
        var culture = CultureInfo.CurrentCulture;
        var tips = new List<string>();
        var activity = FightActivity.Of(actor);

        if (activity.Seconds >= 10 && activity.Uptime < UptimeGoal && activity.DeadSeconds >= 5)
        {
            var at = TimeSpan.FromSeconds(activity.LongestGapAt).ToString(@"m\:ss", CultureInfo.InvariantCulture);
            tips.Add($"Pasaste {activity.DeadSeconds} s sin pegar ({activity.Uptime.ToString("P0", culture)} del tiempo activo). El hueco más largo fue de {activity.LongestGap} s, a los {at}.");

            // "Ese" es el tiempo muerto de recién y nada más: el mismo daño en la pelea sin esos segundos. No es el DPS
            // activo, que además descuenta lo que no pegó antes de su primer golpe o después del último (entrar tarde,
            // morir y no volver) y no se le puede atribuir a este tiempo muerto. En la sesión y la run el tiempo muerto
            // incluye los ratos entre peleas y puede pasar a la duración, que no los cuenta: ahí no se da.
            var withoutDeadTime = snapshot.Duration.TotalSeconds - activity.DeadSeconds;
            var hypothetical = withoutDeadTime >= 1 ? actor.Damage / withoutDeadTime : 0;
            if (hypothetical > actor.Dps * 1.15 && actor.Dps > 0)
            {
                tips.Add($"Sin ese tiempo muerto tu DPS habría sido {ActorRowViewModel.Compact(hypothetical)} en vez de {ActorRowViewModel.Compact(actor.Dps)}.");
            }
        }

        if (actor.Deaths > 0)
        {
            var times = actor.Deaths == 1 ? "1 vez" : $"{actor.Deaths} veces";
            var killer = (snapshot.Deaths ?? []).LastOrDefault(d => d.ActorId == actor.Id)?.KillingBlow?.SourceName;
            tips.Add(killer is null ? $"Moriste {times}." : $"Moriste {times}; la última, por un golpe de {killer}.");
        }

        if (actor.Counts is { Hits: >= 30 } counts && counts.Rate(counts.Back) < 0.1 && counts.Rate(counts.Front) > 0.5)
        {
            tips.Add($"Solo {counts.Rate(counts.Back).ToString("P0", culture)} de tus golpes al boss fueron por la espalda; si tu clase puede, ubicate detrás.");
        }

        var top = snapshot.Actors.Where(a => a.Id != actor.Id).MaxBy(a => a.Dps);
        if (top is not null && actor.Dps > 0 && top.Dps > actor.Dps * 1.1)
        {
            tips.Add($"{name(top.Name)} hizo {((top.Dps / actor.Dps) - 1).ToString("P0", culture)} más DPS que vos ({ActorRowViewModel.Compact(top.Dps)}).");
        }

        return tips.Count > Max ? tips[..Max] : tips;
    }
}
