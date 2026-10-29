using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

/// <summary>Acumuladores mutables de un encuentro en curso. Solo los toca el <see cref="CombatEngine"/>.</summary>
internal sealed class Encounter(DateTimeOffset start)
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTimeOffset Start { get; } = start;

    public DateTimeOffset LastActivity { get; set; } = start;

    /// <summary>
    /// Último daño aceptado, hecho o recibido de un NPC que no es el objetivo principal; solo determina el cierre por
    /// inactividad.
    /// </summary>
    public DateTimeOffset LastCombatActivity { get; private set; } = start;

    public Dictionary<ulong, ActorStats> Actors { get; } = [];

    /// <summary>Daño recibido por cada objetivo; el que más recibió es el "objetivo principal" (boss).</summary>
    public Dictionary<ulong, long> DamageTaken { get; } = [];

    public bool TargetKilled { get; set; }

    /// <summary>Registro de la pelea (daño, curación y muertes de lo que cuenta el filtro), con tope para peleas largas.</summary>
    public List<CombatLogEntry> Log { get; } = [];

    /// <summary>Más que esto, se descartan las líneas más viejas de a <see cref="LogTrim"/>.</summary>
    internal const int LogCapacity = 50_000;

    private const int LogTrim = 5_000;

    public List<DeathRecap> Deaths { get; } = [];

    /// <summary>Momentos marcados con el atajo, en segundos desde el inicio.</summary>
    public List<int> Marks { get; } = [];

    /// <summary>Vida (en %) de cada objetivo la primera vez que se la vio en esta pelea: solo cuentan las fases que cruzó acá.</summary>
    public Dictionary<ulong, double> FirstHealthPercent { get; } = [];

    /// <summary>Fases cruzadas por cada objetivo (75, 50 y 25 %), en orden.</summary>
    public Dictionary<ulong, List<HealthPhase>> Phases { get; } = [];

    /// <summary>Cuenta de cada NPC golpeado en esta pelea para <see cref="TargetVerification"/>. Solo la lleva la pelea.</summary>
    public Dictionary<ulong, VerificationCount> Verifications { get; } = [];

    /// <summary>Entrada a la zona donde empezó la pelea (ver <see cref="EntityRegistry.ZoneEnteredAt"/>).</summary>
    public DateTimeOffset? ZoneEnteredAt { get; init; }

    public void AddLog(CombatLogEntry entry)
    {
        if (Log.Count >= LogCapacity)
        {
            Log.RemoveRange(0, LogTrim);
        }

        Log.Add(entry);
    }

    public ulong? MainTarget => DamageTaken.Count == 0 ? null : DamageTaken.MaxBy(kv => kv.Value).Key;

    /// <summary>Primer y último golpe a cada objetivo: Combat Analysis mide la pelea así.</summary>
    public Dictionary<ulong, (DateTimeOffset First, DateTimeOffset Last)> TargetWindows { get; } = [];

    public void RegisterDamage(ulong target, long amount, DateTimeOffset at)
    {
        DamageTaken[target] = DamageTaken.GetValueOrDefault(target) + amount;
        TargetWindows[target] = TargetWindows.TryGetValue(target, out var window) ? (window.First, at) : (at, at);
        LastActivity = at;
        RegisterCombatActivity(at);
    }

    public void RegisterCombatActivity(DateTimeOffset at)
    {
        if (at > LastCombatActivity)
        {
            LastCombatActivity = at;
        }
    }

    public ActorStats Actor(ulong id)
    {
        if (!Actors.TryGetValue(id, out var stats))
        {
            stats = new ActorStats();
            Actors[id] = stats;
        }

        return stats;
    }
}

internal sealed class ActorStats
{
    public long Damage { get; private set; }

    public int Hits { get; private set; }

    public int Crits { get; private set; }

    public long MaxHit { get; private set; }

    public long Healing { get; set; }

    /// <summary>Daño recibido de monstruos y golpes recibidos.</summary>
    public long DamageTaken { get; set; }

    public int HitsTaken { get; set; }

    /// <summary>Daño y golpes recibidos por skill del monstruo que pegó: se suman junto con DamageTaken y HitsTaken.</summary>
    public Dictionary<uint, TakenSkillStats> TakenBySkill { get; } = [];

    public int Deaths { get; set; }

    /// <summary>Daño que pasó de la vida que le quedaba al objetivo. Se informa aparte; no se descuenta del daño.</summary>
    public long Overkill { get; set; }

    public long PeriodicDamage { get; private set; }

    /// <summary>Por skill y por si pegó el jugador o una invocación suya: la misma skill de los dos son dos filas.</summary>
    public Dictionary<(uint SkillId, bool FromSummon), SkillStats> Skills { get; } = [];

    /// <summary>Daño en cada segundo desde el inicio del encuentro (índice = segundo).</summary>
    public List<long> DamageBySecond { get; } = [];

    /// <summary>Como <see cref="DamageBySecond"/>, sin los ticks de DoT: termina en el último golpe directo.</summary>
    public List<long> DirectDamageBySecond { get; } = [];

    public void AddToTimeline(int second, long amount, bool periodic)
    {
        Add(DamageBySecond, second, amount);
        if (!periodic)
        {
            Add(DirectDamageBySecond, second, amount);
        }

        static void Add(List<long> timeline, int second, long amount)
        {
            while (timeline.Count <= second)
            {
                timeline.Add(0);
            }

            timeline[second] += amount;
        }
    }

    public Dictionary<ulong, long> DamageByTarget { get; } = [];

    /// <summary>Golpes por modificador separados por objetivo: Combat Analysis cuenta solo los del objetivo elegido.</summary>
    public Dictionary<ulong, HitCounter> CountsByTarget { get; } = [];

    public void AddHit(uint skillId, ulong targetId, long amount, HitModifiers modifiers, bool fromSummon = false)
    {
        DamageByTarget[targetId] = DamageByTarget.GetValueOrDefault(targetId) + amount;
        if (!CountsByTarget.TryGetValue(targetId, out var counts))
        {
            counts = new HitCounter();
            CountsByTarget[targetId] = counts;
        }

        counts.Add(modifiers);
        // Como Combat Analysis: los ticks de DoT suman daño pero no cuentan como golpe.
        var periodic = modifiers.HasFlag(HitModifiers.DamageOverTime);
        var hit = periodic ? 0 : 1;
        var critical = modifiers.HasFlag(HitModifiers.Critical);
        Damage += amount;
        PeriodicDamage += periodic ? amount : 0;
        Hits += hit;
        Crits += critical ? 1 : 0;
        MaxHit = periodic ? MaxHit : Math.Max(MaxHit, amount);

        if (!Skills.TryGetValue((skillId, fromSummon), out var skill))
        {
            skill = new SkillStats();
            Skills[(skillId, fromSummon)] = skill;
        }

        skill.Counts.Add(modifiers);
        skill.Damage += amount;
        skill.PeriodicDamage += periodic ? amount : 0;
        skill.Hits += hit;
        skill.Crits += critical ? 1 : 0;
        skill.MaxHit = periodic ? skill.MaxHit : Math.Max(skill.MaxHit, amount);
    }
}

internal sealed class SkillStats
{
    public long Damage { get; set; }

    public long PeriodicDamage { get; set; }

    public int Hits { get; set; }

    public int Crits { get; set; }

    public long MaxHit { get; set; }

    public HitCounter Counts { get; } = new();
}

internal sealed class TakenSkillStats
{
    public long Damage { get; set; }

    public int Hits { get; set; }
}

/// <summary>
/// Lo que pide <see cref="TargetVerification"/> de un objetivo: la vida del servidor al empezar la cuenta, el daño
/// contado desde ahí y la última lectura positiva de la pelea con el daño contado hasta ella. Con la muerte, la cuenta
/// se congela.
/// </summary>
internal sealed class VerificationCount
{
    /// <param name="serverHp">Vida del servidor antes del primer golpe; <c>null</c> si no se conoce todavía. Una de 0
    /// (la lectura de su muerte) cuenta como desconocida.</param>
    public VerificationCount(long? serverHp)
    {
        if (serverHp is { } hp && hp > 0)
        {
            Start(hp);
        }
    }

    /// <summary>Vida del servidor al empezar la cuenta; <c>null</c> hasta la primera lectura si el spawn no la trajo.</summary>
    public long? StartHp { get; private set; }

    public long Counted { get; private set; }

    /// <summary>Última vida positiva que informó el servidor; al empezar, la inicial.</summary>
    public long LastReading { get; private set; }

    public long CountedAtReading { get; private set; }

    /// <summary>Llegó al menos una lectura después de empezar la cuenta.</summary>
    public bool HasReading { get; private set; }

    public bool Killed { get; private set; }

    /// <summary>Sin vida inicial todavía, el golpe no se cuenta: la primera lectura ya lo refleja.</summary>
    public void AddHit(long amount)
    {
        if (StartHp is not null && !Killed)
        {
            Counted += amount;
        }
    }

    /// <summary>
    /// Un 0x8D00 del objetivo. Llega después de los golpes que refleja, así que se compara con lo contado hasta acá.
    /// Si la cuenta no había empezado, empieza con esta lectura. La de 0 se ignora: llega después del golpe mortal, que
    /// pasa de la última lectura positiva, y el overkill sale de esa (la muerte la marca el 0x8D04).
    /// </summary>
    public void Reading(long hp)
    {
        if (hp <= 0 || Killed)
        {
            return;
        }

        if (StartHp is null)
        {
            Start(hp);
            return;
        }

        LastReading = hp;
        CountedAtReading = Counted;
        HasReading = true;
    }

    public void Kill() => Killed = true;

    /// <summary>
    /// La verificación, o <c>null</c> sin vida inicial, sin lectura después de empezar ni muerte, o sin vida perdida.
    /// Sin muerte se compara la última lectura con lo contado hasta ella; con muerte, la vida final es 0 y lo contado
    /// después de la última lectura positiva que pasa de esa vida es overkill.
    /// </summary>
    public TargetVerification? Result(ulong targetId, uint? npcId, long? maxHp)
    {
        if (StartHp is not { } start || !(HasReading || Killed))
        {
            return null;
        }

        var (end, counted, overkill) = Killed
            ? (0L, Counted, Math.Max(0, Counted - CountedAtReading - LastReading))
            : (LastReading, CountedAtReading, 0L);
        var lost = start - end;
        if (lost <= 0)
        {
            return null;
        }

        var difference = counted - overkill - lost;
        return new TargetVerification(targetId, npcId, start, end, maxHp ?? start, counted, overkill, difference, Math.Abs(difference) * 100 <= lost);
    }

    private void Start(long hp)
    {
        StartHp = hp;
        LastReading = hp;
    }
}

/// <summary>Cuenta golpes por modificador, igual que las columnas de Combat Analysis. Los ticks de DoT no son golpes.</summary>
internal sealed class HitCounter
{
    private int _hits;
    private int _crits;
    private int _perfect;
    private int _double;
    private int _front;
    private int _back;
    private int _multi;

    public void Add(HitModifiers modifiers)
    {
        if (modifiers.HasFlag(HitModifiers.DamageOverTime))
        {
            return;
        }

        _hits++;
        _crits += modifiers.HasFlag(HitModifiers.Critical) ? 1 : 0;
        _perfect += modifiers.HasFlag(HitModifiers.Perfect) ? 1 : 0;
        _double += modifiers.HasFlag(HitModifiers.DoubleHit) ? 1 : 0;
        _front += modifiers.HasFlag(HitModifiers.FrontAttack) ? 1 : 0;
        _back += modifiers.HasFlag(HitModifiers.BackAttack) ? 1 : 0;
        _multi += modifiers.HasFlag(HitModifiers.MultiHit) ? 1 : 0;
    }

    public HitCounts Snapshot() => new(_hits, _crits, _perfect, _double, _front, _back, _multi);
}
