namespace HysteriaMeter.Combat;

/// <summary>Lo que sabemos de un NPC por su ID de plantilla (el <c>npcId</c> del spawn 0x3641).</summary>
/// <param name="EnrageSeconds">Segundos de pelea hasta el enrage del boss, si se conoce.</param>
public sealed record NpcInfo(string Name, string? Dungeon = null, bool IsBoss = false, int? EnrageSeconds = null);

/// <summary>
/// Tabla <c>npcId → nombre, dungeon, es boss</c>. El juego no manda el nombre de los monstruos, así que se arma a mano
/// (<c>data/npcs.json</c>) mirando el ID que muestra el meter en una pelea sin nombre.
/// </summary>
public sealed class NpcCatalog(IReadOnlyDictionary<uint, NpcInfo> npcs)
{
    public static NpcCatalog Empty { get; } = new(new Dictionary<uint, NpcInfo>());

    public int Count => npcs.Count;

    public NpcInfo? Of(uint npcId) => npcs.GetValueOrDefault(npcId);
}
