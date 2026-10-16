using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

/// <summary>
/// Canonicaliza IDs: un jugador puede pegar con IDs transitorios (entidad de combate, invocaciones)
/// que se reasignan a su ID "dueño" estable. Además guarda nombres y clase cuando llegan.
/// </summary>
public sealed class EntityRegistry
{
    private const int MaxOwnerDepth = 4;

    /// <summary>Un miembro que deja de mandar HP/MP este tiempo se considera fuera de la party.</summary>
    private static readonly TimeSpan PartyMemberTimeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<ulong, ulong> _owners = [];
    private readonly Dictionary<ulong, EntityProfile> _profiles = [];
    private readonly Dictionary<string, ulong> _idsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, string> _pendingOwnerNames = [];
    private readonly Dictionary<ulong, DateTimeOffset> _partySeenAt = [];
    private readonly Dictionary<ulong, Health> _health = [];
    private readonly HashSet<ulong> _deadNpcs = [];
    private DateTimeOffset _now;
    private string? _localName;
    private (int? ItemLevel, long? CombatPower)? _localGear;

    public ulong? LocalPlayerId { get; private set; }

    /// <summary>Nombres de NPCs por ID de plantilla; el juego no los manda.</summary>
    public NpcCatalog Npcs { get; set; } = NpcCatalog.Empty;

    /// <summary>
    /// Cuándo entramos a la zona actual: cada cambio de zona abre otra conexión y el jugador local recibe otra entidad.
    /// Las peleas con la misma entrada son una misma "run" de dungeon.
    /// </summary>
    public DateTimeOffset? ZoneEnteredAt { get; private set; }

    public void Apply(CombatEvent e)
    {
        _now = e.Timestamp > _now ? e.Timestamp : _now;
        switch (e)
        {
            case LocalPlayerEvent local:
                if (LocalPlayerId != local.EntityId)
                {
                    ZoneEnteredAt = local.Timestamp;
                }

                LocalPlayerId = local.EntityId;
                _localName = local.Name ?? _localName;

                // El item level y el power del local llegan una vez por conexión: pasan a la entidad nueva.
                if (_localGear is { } localGear)
                {
                    Update(local.EntityId, p => p with { ItemLevel = p.ItemLevel ?? localGear.ItemLevel, CombatPower = p.CombatPower ?? localGear.CombatPower });
                }

                // Al cambiar de zona el local recibe otra entidad y el nombre llega segundos después:
                // mientras tanto se usa el que ya conocíamos.
                if (_localName is { } name && Profile(local.EntityId).Name != name)
                {
                    SetName(local.EntityId, name);
                }

                break;
            case HealthEvent health:
                // Sin el máximo del spawn (por ejemplo, si arrancamos a mitad de pelea) se usa el mayor visto.
                _health[health.EntityId] = _health.TryGetValue(health.EntityId, out var known)
                    ? new Health(health.Hp, Math.Max(known.MaxHp, health.Hp))
                    : new Health(health.Hp, health.Hp);
                break;
            case PartyMemberSeenEvent member:
                _partySeenAt[member.EntityId] = member.Timestamp;
                break;
            case EntitySpawnEvent spawn:
                if (spawn.OwnerId is { } owner && owner != spawn.EntityId)
                {
                    _owners[spawn.EntityId] = owner;
                }
                else if (spawn.OwnerName is { } ownerName)
                {
                    // El dueño puede aparecer después que la invocación: se resuelve en Canonical.
                    _pendingOwnerNames[spawn.EntityId] = ownerName;
                }

                _deadNpcs.Remove(spawn.EntityId);
                if (spawn.NpcId is { } npcId)
                {
                    Update(spawn.EntityId, p => p with { NpcId = npcId });
                }

                if (spawn is { Hp: { } hp, MaxHp: { } maxHp })
                {
                    _health[spawn.EntityId] = new Health(hp, maxHp);
                }

                break;
            case PlayerGearEvent gear:
                Update(gear.EntityId, p => p with { ItemLevel = gear.ItemLevel ?? p.ItemLevel, CombatPower = gear.CombatPower ?? p.CombatPower });
                if (gear.EntityId == LocalPlayerId)
                {
                    RememberLocalGear();
                }

                break;
            case LocalCombatPowerEvent power:
                _localGear = (_localGear?.ItemLevel, power.CombatPower);
                if (LocalPlayerId is { } localId)
                {
                    Update(localId, p => p with { CombatPower = power.CombatPower });
                }

                break;
            case DeathEvent death when IsNpc(Canonical(death.EntityId)):
                _deadNpcs.Add(Canonical(death.EntityId));
                break;
            case EntityInfoEvent info:
                SetName(info.EntityId, info.Name);
                if (info.ClassId is { } classId)
                {
                    Update(info.EntityId, p => p with { ClassId = classId });
                }

                break;
        }
    }

    /// <summary>Vida conocida de una entidad: la del último 0x8D00 menos el daño registrado desde entonces.</summary>
    public Health? HealthOf(ulong id) => _health.TryGetValue(id, out var health) ? health : null;

    /// <summary>
    /// Descuenta un golpe de la vida conocida y devuelve el overkill: la parte que pasó de la vida que le quedaba.
    /// Sin vida conocida no hay overkill.
    /// </summary>
    public long ApplyDamage(ulong id, long amount)
    {
        if (!_health.TryGetValue(id, out var health))
        {
            return 0;
        }

        var overkill = Math.Max(0, amount - Math.Max(health.Hp, 0));
        _health[id] = health with { Hp = Math.Max(health.Hp - amount, 0) };
        return overkill;
    }

    public bool IsPartyMember(ulong canonicalId) =>
        _partySeenAt.TryGetValue(canonicalId, out var seenAt) && _now - seenAt <= PartyMemberTimeout;

    public ulong Canonical(ulong id)
    {
        for (var depth = 0; depth < MaxOwnerDepth && TryGetOwner(id, out var owner); depth++)
        {
            id = owner;
        }

        return id;
    }

    /// <summary>
    /// Deduce la clase por las skills que usa: los IDs de skill de cada clase empiezan con su número
    /// (11 a 18; por ejemplo 12010350). Los IDs de ítems y pociones (1 a 3 millones) no cuentan.
    /// </summary>
    public void ObserveSkill(ulong canonicalId, uint skillId)
    {
        // La clase del spawn puede venir en otra numeración: manda la de las skills.
        if (ClassOfSkill(skillId) is { } classId && Profile(canonicalId) is { NpcId: null } profile && profile.ClassId != classId
            && profile.ClassId is null or < 11 or > 18)
        {
            Update(canonicalId, p => p with { ClassId = classId });
        }
    }

    public static int? ClassOfSkill(uint skillId) => (int)(skillId / 1_000_000) is var prefix and >= 11 and <= 18 ? prefix : null;

    public EntityProfile Profile(ulong id) => _profiles.GetValueOrDefault(id, EntityProfile.Empty);

    /// <summary>Un monstruo que ya murió (y no volvió a aparecer).</summary>
    public bool IsDeadNpc(ulong canonicalId) => _deadNpcs.Contains(canonicalId);

    /// <summary>
    /// Un jugador conocido: el local, uno con nombre o clase, o un compañero de party. Las entidades sin datos
    /// (por ejemplo monstruos que aparecieron antes de empezar a capturar) no cuentan como jugadores.
    /// </summary>
    public bool IsKnownPlayer(ulong canonicalId) =>
        canonicalId == LocalPlayerId
        || Profile(canonicalId) is { NpcId: null } profile && (profile.Name is not null || profile.ClassId is not null)
        || _partySeenAt.ContainsKey(canonicalId);

    /// <summary>Un NPC/monstruo: no suma al ranking de DPS.</summary>
    public bool IsNpc(ulong canonicalId) => Profile(canonicalId).NpcId is not null;

    public string DisplayName(ulong canonicalId) =>
        NameOf(canonicalId) ?? (canonicalId == LocalPlayerId ? "Yo" : $"#{canonicalId}");

    /// <summary>El nombre que mandó el juego o, para un NPC, el de la tabla; <c>null</c> si no se conoce.</summary>
    public string? NameOf(ulong canonicalId)
    {
        var profile = Profile(canonicalId);
        return profile.Name ?? (profile.NpcId is { } npcId ? Npcs.Of(npcId)?.Name : null);
    }

    /// <summary>
    /// Una invocación que aparece como monstruo y sin dueño (por ejemplo el espíritu del Hechicero, que pega con
    /// Bittercold Wind) pero usa una skill de jugador: si hay un solo jugador conocido de esa clase, es suya.
    /// La asignación queda guardada para los golpes siguientes.
    /// </summary>
    public bool TryAdoptSummon(ulong summonId, uint skillId, out ulong owner)
    {
        owner = 0;
        if (ClassOfSkill(skillId) is not { } classId)
        {
            return false;
        }

        var candidates = _profiles
            .Where(p => p.Value is { NpcId: null } profile && profile.ClassId == classId && IsKnownPlayer(p.Key))
            .Select(p => p.Key)
            .Take(2)
            .ToList();
        if (candidates.Count != 1 || candidates[0] == summonId)
        {
            return false;
        }

        owner = candidates[0];
        _owners[summonId] = owner;
        return true;
    }

    private bool TryGetOwner(ulong id, out ulong owner)
    {
        if (_owners.TryGetValue(id, out owner))
        {
            return true;
        }

        if (_pendingOwnerNames.TryGetValue(id, out var name) && _idsByName.TryGetValue(name, out owner) && owner != id)
        {
            _owners[id] = owner;
            _pendingOwnerNames.Remove(id);
            return true;
        }

        return false;
    }

    private void RememberLocalGear()
    {
        if (LocalPlayerId is { } id)
        {
            var profile = Profile(id);
            _localGear = (profile.ItemLevel, profile.CombatPower);
        }
    }

    private void SetName(ulong id, string name)
    {
        Update(id, p => p with { Name = name });
        _idsByName[name] = id;
    }

    private void Update(ulong id, Func<EntityProfile, EntityProfile> change) =>
        _profiles[id] = change(Profile(id));
}

public sealed record Health(long Hp, long MaxHp);

/// <param name="ItemLevel">Item level de un jugador, si llegó en su aparición.</param>
/// <param name="CombatPower">Power (CP) de un jugador.</param>
public sealed record EntityProfile(string? Name, int? ClassId, uint? NpcId, int? ItemLevel = null, long? CombatPower = null)
{
    public static EntityProfile Empty { get; } = new(null, null, null);
}
