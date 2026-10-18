namespace HysteriaMeter.Combat;

/// <summary>
/// Tiempo activo y tiempo muerto de un jugador, a partir de su daño por segundo (<see cref="Of"/> usa la serie sin DoT).
/// Cuenta como muerto cada hueco sin daño de más de <see cref="Tolerance"/> segundos, desde el primer golpe hasta el último:
/// un par de segundos sin pegar es normal (animaciones, esquivar); más que eso es tiempo perdido.
/// </summary>
/// <param name="LongestGapAt">Segundo de la pelea (desde el inicio del encuentro) en que empezó el hueco más largo.</param>
public sealed record FightActivity(int Seconds, int DeadSeconds, int LongestGap, int Gaps, int LongestGapAt = 0)
{
    /// <summary>Segundos seguidos sin daño que no cuentan como tiempo muerto.</summary>
    public const int Tolerance = 2;

    public static FightActivity Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Fracción del tiempo con daño (0 a 1); 1 si no hubo pelea.</summary>
    public double Uptime => Seconds == 0 ? 1 : 1 - ((double)DeadSeconds / Seconds);

    /// <summary>
    /// La actividad del jugador: sus golpes directos, sin los ticks de DoT (<see cref="ActorSnapshot.DirectTimeline"/>).
    /// Un veneno que sigue pegando mientras está muerto o quieto no es el jugador actuando. Sin DoT, o en las peleas
    /// guardadas antes de la serie directa, usa el <see cref="ActorSnapshot.Timeline"/>, que en ese caso es lo mismo.
    /// </summary>
    public static FightActivity Of(ActorSnapshot actor) => From(actor.DirectTimeline ?? actor.Timeline);

    public static FightActivity From(IReadOnlyList<long>? damageBySecond)
    {
        if (damageBySecond is null || damageBySecond.Count == 0)
        {
            return Empty;
        }

        var first = -1;
        var last = -1;
        for (var i = 0; i < damageBySecond.Count; i++)
        {
            if (damageBySecond[i] > 0)
            {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        if (first < 0)
        {
            return Empty;
        }

        int dead = 0, longest = 0, longestAt = 0, gaps = 0, run = 0;
        for (var i = first; i <= last; i++)
        {
            if (damageBySecond[i] > 0)
            {
                Close(run, i - run);
                run = 0;
            }
            else
            {
                run++;
            }
        }

        return new FightActivity(last - first + 1, dead, longest, gaps, longestAt);

        void Close(int length, int start)
        {
            if (length > Tolerance)
            {
                dead += length;
                gaps++;
                if (length > longest)
                {
                    longest = length;
                    longestAt = start;
                }
            }
        }
    }
}
