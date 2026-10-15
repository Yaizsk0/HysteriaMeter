namespace HysteriaMeter.Combat;

/// <summary>
/// Tiempo activo y tiempo muerto de un jugador, a partir de su daño por segundo (<see cref="ActorSnapshot.Timeline"/>).
/// Cuenta como muerto cada hueco sin daño de más de <see cref="Tolerance"/> segundos, desde el primer golpe hasta el último:
/// un par de segundos sin pegar es normal (animaciones, esquivar); más que eso es tiempo perdido.
/// </summary>
public sealed record FightActivity(int Seconds, int DeadSeconds, int LongestGap, int Gaps)
{
    /// <summary>Segundos seguidos sin daño que no cuentan como tiempo muerto.</summary>
    public const int Tolerance = 2;

    public static FightActivity Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Fracción del tiempo con daño (0 a 1); 1 si no hubo pelea.</summary>
    public double Uptime => Seconds == 0 ? 1 : 1 - ((double)DeadSeconds / Seconds);

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

        int dead = 0, longest = 0, gaps = 0, run = 0;
        for (var i = first; i <= last; i++)
        {
            if (damageBySecond[i] > 0)
            {
                Close(run);
                run = 0;
            }
            else
            {
                run++;
            }
        }

        return new FightActivity(last - first + 1, dead, longest, gaps);

        void Close(int length)
        {
            if (length > Tolerance)
            {
                dead += length;
                gaps++;
                longest = Math.Max(longest, length);
            }
        }
    }
}
