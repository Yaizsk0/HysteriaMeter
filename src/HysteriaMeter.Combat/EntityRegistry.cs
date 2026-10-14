using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

/// <summary>
/// Canonicaliza IDs: un jugador puede pegar con IDs transitorios (entidad de combate, invocaciones)
/// que se reasignan a su ID "dueño" estable. Además guarda nombres y clase cuando llegan.
/// </summary>
public sealed class EntityRegistry
{
    private const int MaxOwnerDepth = 4;

    private readonly Dictionary<ulong, ulong> _owners = [];
    private readonly Dictionary<ulong, EntityProfile> _profiles = [];

    public ulong? LocalPlayerId { get; private set; }

    public void Apply(CombatEvent e)
    {
        switch (e)
        {
            case LocalPlayerEvent local:
                LocalPlayerId = local.EntityId;
                if (local.Name is { } name)
                {
                    Update(local.EntityId, p => p with { Name = name });
                }

                break;
            case EntitySpawnEvent spawn:
                if (spawn.OwnerId is { } owner && owner != spawn.EntityId)
                {
                    _owners[spawn.EntityId] = owner;
                }

                if (spawn.NpcId is { } npcId)
                {
                    Update(spawn.EntityId, p => p with { NpcId = npcId });
                }

                break;
            case EntityInfoEvent info:
                Update(info.EntityId, p => p with { Name = info.Name, ClassId = info.ClassId ?? p.ClassId });
                break;
        }
    }

    public ulong Canonical(ulong id)
    {
        for (var depth = 0; depth < MaxOwnerDepth && _owners.TryGetValue(id, out var owner); depth++)
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

    private void Update(ulong id, Func<EntityProfile, EntityProfile> change) =>
        _profiles[id] = change(Profile(id));
}

public sealed record EntityProfile(string? Name, int? ClassId, uint? NpcId)
{
    public static EntityProfile Empty { get; } = new(null, null, null);
}
