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

    /// <summary>
    /// DPS activo (<see cref="ActorSnapshot.ActiveDps"/>), con la misma actividad que <see cref="Of"/>
    /// (<paramref name="direct"/> ?? <paramref name="timeline"/>): el daño de los segundos activos (del primer golpe directo
    /// al último, sin los huecos muertos; DoT incluido) sobre el tiempo activo. Un tick que cae en un hueco o después del
    /// último golpe no suma tiempo ni daño: con el jugador muerto o quieto, no es el jugador actuando. El tiempo se mide
    /// como el DPS, que del segundo 0 al 9 cuenta 9 s: los segundos activos menos uno, mínimo 1 s. Así, pegando de punta a
    /// punta sin tiempo muerto da lo mismo que el DPS. 0 si no hubo golpes directos (todo DoT): no hay tiempo que medir.
    /// </summary>
    /// <param name="maxSeconds">
    /// Tope del tiempo activo: el tiempo de combate del DPS. En una pelea no cambia nada; en la sesión y la run, cuyas
    /// series van desde su propio inicio, cada rato entre peleas es un hueco muerto pero deja su segundo "menos uno" de más.
    /// </param>
    public static double ActiveDps(IReadOnlyList<long>? direct, IReadOnlyList<long>? timeline, double maxSeconds = double.PositiveInfinity)
    {
        var (first, last, gaps) = Scan(direct ?? timeline);
        if (first < 0 || timeline is null)
        {
            return 0;
        }

        long damage = 0;
        for (var i = first; i <= last && i < timeline.Count; i++)
        {
            damage += timeline[i];
        }

        foreach (var (start, length) in gaps)
        {
            for (var i = start; i < start + length && i < timeline.Count; i++)
            {
                damage -= timeline[i];
            }
        }

        var seconds = Math.Min(Math.Max(last - first - gaps.Sum(g => g.Length), 1), maxSeconds);
        return damage / seconds;
    }

    public static FightActivity From(IReadOnlyList<long>? damageBySecond)
    {
        var (first, last, gaps) = Scan(damageBySecond);
        if (first < 0)
        {
            return Empty;
        }

        // Si hay empate, el primero de los más largos (MaxBy se queda con el primero).
        var longest = gaps.Count == 0 ? default : gaps.MaxBy(g => g.Length);
        return new FightActivity(last - first + 1, gaps.Sum(g => g.Length), longest.Length, gaps.Count, longest.Start);
    }

    /// <summary>
    /// Primer y último segundo con daño y los huecos muertos entre ellos (más de <see cref="Tolerance"/> segundos seguidos
    /// sin daño); First = -1 si no hubo daño.
    /// </summary>
    private static (int First, int Last, List<(int Start, int Length)> Gaps) Scan(IReadOnlyList<long>? damageBySecond)
    {
        var gaps = new List<(int Start, int Length)>();
        int first = -1, last = -1, run = 0;
        for (var i = 0; damageBySecond is not null && i < damageBySecond.Count; i++)
        {
            if (damageBySecond[i] <= 0)
            {
                run += first < 0 ? 0 : 1;
                continue;
            }

            if (run > Tolerance)
            {
                gaps.Add((i - run, run));
            }

            first = first < 0 ? i : first;
            last = i;
            run = 0;
        }

        return (first, last, gaps);
    }
}
