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
    private DateTimeOffset _now;
    private string? _localName;

    public ulong? LocalPlayerId { get; private set; }

    public void Apply(CombatEvent e)
    {
        _now = e.Timestamp > _now ? e.Timestamp : _now;
        switch (e)
        {
            case LocalPlayerEvent local:
                LocalPlayerId = local.EntityId;
                _localName = local.Name ?? _localName;

                // Al cambiar de zona el local recibe otra entidad y el nombre llega segundos después:
                // mientras tanto se usa el que ya conocíamos.
                if (_localName is { } name && Profile(local.EntityId).Name != name)
                {
                    SetName(local.EntityId, name);
                }

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

                if (spawn.NpcId is { } npcId)
                {
                    Update(spawn.EntityId, p => p with { NpcId = npcId });
                }

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

    public EntityProfile Profile(ulong id) => _profiles.GetValueOrDefault(id, EntityProfile.Empty);

    /// <summary>Un NPC/monstruo: no suma al ranking de DPS.</summary>
    public bool IsNpc(ulong canonicalId) => Profile(canonicalId).NpcId is not null;

    public string DisplayName(ulong canonicalId) =>
        Profile(canonicalId).Name ?? (canonicalId == LocalPlayerId ? "Yo" : $"#{canonicalId}");

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

    private void SetName(ulong id, string name)
    {
        Update(id, p => p with { Name = name });
        _idsByName[name] = id;
    }

    private void Update(ulong id, Func<EntityProfile, EntityProfile> change) =>
        _profiles[id] = change(Profile(id));
}

public sealed record EntityProfile(string? Name, int? ClassId, uint? NpcId)
{
    public static EntityProfile Empty { get; } = new(null, null, null);
}
