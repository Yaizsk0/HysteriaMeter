namespace HysteriaMeter.Combat;

/// <summary>Foto inmutable de un encuentro, lista para la UI o para persistir.</summary>
public sealed record EncounterSnapshot(
    Guid Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? TargetName,
    bool TargetKilled,
    long TotalDamage,
    IReadOnlyList<ActorSnapshot> Actors,
    TimeSpan? MainTargetDuration = null,
    // Vida del objetivo principal, si la conocemos (spawn 0x3641 y actualizaciones 0x8D00).
    Health? TargetHealth = null,
    // Muertes de jugadores durante la pelea, con los últimos golpes recibidos.
    IReadOnlyList<DeathRecap>? Deaths = null,
    // ID de plantilla del objetivo principal (para ponerle nombre en data/npcs.json) y si la tabla lo marca como boss.
    uint? TargetNpcId = null,
    bool TargetIsBoss = false,
    // Momentos marcados por el jugador con el atajo, en segundos desde el inicio.
    IReadOnlyList<int>? Marks = null,
    // Entrada a la zona donde empezó la pelea: las peleas con la misma entrada son una "run" de dungeon.
    DateTimeOffset? ZoneEnteredAt = null,
    // Segundos hasta el enrage del objetivo principal, si data/npcs.json lo trae.
    int? TargetEnrageSeconds = null,
    // Cuándo la vida del objetivo principal bajó del 75, 50 y 25 % (fases de la pelea).
    IReadOnlyList<HealthPhase>? Phases = null,
    // Si el daño contado contra cada objetivo cierra con la vida que perdió según el servidor, de mayor a menor daño
    // contado. Solo la pelea la lleva: la sesión, la run, una pelea sin objetivos verificables y las guardadas antes de
    // que existiera quedan en null.
    IReadOnlyList<TargetVerification>? Verifications = null)
{
    /// <summary>Duración mínima de 1 s para que un único golpe no dé un DPS absurdo.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Math.Max((End - Start).Ticks, TimeSpan.TicksPerSecond));
}

/// <summary>La vida del objetivo bajó de <paramref name="Percent"/> % a los <paramref name="Second"/> segundos de pelea.</summary>
public sealed record HealthPhase(int Percent, int Second);

/// <summary>
/// Auto-verificación de un objetivo: si el daño contado en la pelea cierra con la vida que perdió según el servidor
/// (spawn <c>0x3641</c> y <c>0x8D00</c>, sin el descuento de <see cref="EntityRegistry.ApplyDamage"/>), con 1 % de tolerancia.
/// Cuenta el daño de todos los jugadores, también los que el filtro deja fuera del ranking: a la vida le bajan igual.
/// <para>
/// El <c>0x8D00</c> llega después de los golpes que refleja, en el mismo bundle (dungeon de la 001: 1.630 de 1.631
/// lecturas del boss cierran exacto con los golpes anteriores; contando el golpe siguiente, 5), así que la lectura se
/// compara con el daño contado hasta ella. Sin muerte, el daño posterior a la última lectura no se puede verificar y
/// queda afuera. La lectura de 0 no cuenta: llega después del golpe mortal, junto con golpes que ya estaban en vuelo, y
/// la muerte la marca el <c>0x8D04</c>. Con muerte, la vida final es 0 y lo que pasó de la última lectura positiva es
/// overkill: un golpe final que no alcanza para matar queda como daño que falta.
/// </para>
/// <para>
/// La vida inicial es la del servidor justo antes del primer golpe contado, aunque sea una lectura de antes de la pelea
/// (la de una pelea anterior contra el mismo objetivo, cortada por inactividad): en las capturas, la primera lectura de
/// la pelea siempre es esa menos los golpes del medio. Si al primer golpe no se conoce (el spawn no la trajo, el id
/// reapareció sin ella o la última es el 0 de su muerte), la cuenta arranca en la primera lectura de la pelea, que ya
/// refleja ese golpe. Sin ninguna lectura después de esa ni muerte, o sin vida perdida, no hay verificación.
/// </para>
/// </summary>
/// <param name="TargetId">Entidad del objetivo.</param>
/// <param name="NpcId">Plantilla del objetivo, si llegó en su aparición.</param>
/// <param name="StartHp">Vida del servidor al empezar la cuenta.</param>
/// <param name="EndHp">Última vida positiva que informó el servidor en la pelea, o 0 si murió.</param>
/// <param name="MaxHp">Vida máxima: la del spawn o, sin ella, la mayor que informó el servidor.</param>
/// <param name="Counted">Daño contado hasta la última lectura positiva o, si murió, hasta la muerte.</param>
/// <param name="Overkill">Si murió, el daño posterior a la última lectura positiva que pasó de esa vida; si no, 0.</param>
/// <param name="Difference">(Counted − Overkill) − (StartHp − EndHp): positivo es daño de más, negativo es daño que falta.</param>
/// <param name="Closes">La diferencia no pasa del 1 % de la vida perdida.</param>
public sealed record TargetVerification(
    ulong TargetId, uint? NpcId, long StartHp, long EndHp, long MaxHp, long Counted, long Overkill, long Difference, bool Closes);

public sealed record ActorSnapshot(
    ulong Id,
    string Name,
    int? ClassId,
    bool IsLocalPlayer,
    long Damage,
    double Dps,
    double Share,
    int Hits,
    int Crits,
    long MaxHit,
    long Healing,
    IReadOnlyList<SkillSnapshot> Skills,
    // Golpes por modificador contra el objetivo principal, como Combat Analysis.
    HitCounts? Counts = null,
    long MainTargetDamage = 0,
    double MainTargetDps = 0,
    long PeriodicDamage = 0,
    // DPS sin tiempo muerto (FightActivity.ActiveDps): el daño de los segundos activos de FightActivity.Of sobre ese
    // tiempo, sin los huecos de más de FightActivity.Tolerance s sin golpes directos ni los ticks que caen en ellos o
    // después del último golpe (el DoT no cuenta como actividad). Pegando de punta a punta sin tiempo muerto, es el DPS.
    // 0 si todo fue DoT.
    // Las peleas ya guardadas conservan el cálculo anterior (huecos topeados en 3 s, DoT incluido): no se recalculan.
    double ActiveDps = 0,
    long Overkill = 0,
    // Daño por segundo desde el inicio del encuentro, para el gráfico de DPS en el tiempo.
    IReadOnlyList<long>? Timeline = null,
    long DamageTaken = 0,
    int HitsTaken = 0,
    int Deaths = 0,
    // Item level y power (CP) del jugador, si llegaron en su aparición.
    int? ItemLevel = null,
    long? CombatPower = null,
    // Como Timeline, sin los ticks periódicos (DoT): marca cuándo volvió a pegar después de morir. null si no hubo DoT
    // (sería igual a Timeline) o en las peleas guardadas antes de que existiera: en los dos casos, usar Timeline.
    IReadOnlyList<long>? DirectTimeline = null,
    // DamageTaken y HitsTaken separados por la skill del monstruo que pegó, de mayor a menor daño: suman lo mismo que
    // ellos. null si no recibió daño o en las peleas guardadas antes de que existiera (ahí DamageTaken puede no ser 0).
    IReadOnlyList<TakenSkillSnapshot>? DamageTakenBySkill = null)
{
    public double CritRate => Hits == 0 ? 0 : (double)Crits / Hits;
}

/// <summary>Daño y golpes que un jugador recibió de la skill <paramref name="SkillId"/> de un monstruo.</summary>
public sealed record TakenSkillSnapshot(uint SkillId, long Damage, int Hits);

public sealed record SkillSnapshot(uint SkillId, long Damage, int Hits, int Crits, long MaxHit, double Share, HitCounts? Counts = null, long PeriodicDamage = 0)
{
    public long DirectDamage => Damage - PeriodicDamage;
}

/// <summary>
/// Golpes por modificador, como las columnas de Combat Analysis (CRI, PERF, DOUB, FRNT, BACK, MULT).
/// Un modificador fuera de <see cref="HysteriaMeter.Protocol.Events.ModifierCoverage"/> se muestra como "sin identificar".
/// </summary>
public sealed record HitCounts(int Hits, int Critical, int Perfect, int DoubleHit, int Front, int Back, int MultiHit)
{
    public static HitCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public double Rate(int count) => Hits == 0 ? 0 : (double)count / Hits;
}
