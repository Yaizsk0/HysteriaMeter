namespace HysteriaMeter.Combat;

/// <summary>Lo que sabemos de un NPC por su ID de plantilla (el <c>npcId</c> del spawn 0x3641).</summary>
/// <param name="EnrageSeconds">Segundos de pelea hasta el enrage del boss, si se conoce.</param>
public sealed record NpcInfo(string Name, string? Dungeon = null, bool IsBoss = false, int? EnrageSeconds = null);

/// <summary>
/// Tabla <c>npcId → nombre, dungeon, es boss</c>. El juego no manda el nombre de los monstruos: la base es la lista
/// publicada (<c>data/npcs-taengu.json</c>) y se completa a mano en <c>data/npcs.json</c> mirando el ID que muestra el
/// meter en una pelea sin nombre.
/// </summary>
public sealed class NpcCatalog(IReadOnlyDictionary<uint, NpcInfo> npcs)
{
    public static NpcCatalog Empty { get; } = new(new Dictionary<uint, NpcInfo>());

    public int Count => npcs.Count;

    public NpcInfo? Of(uint npcId) => npcs.GetValueOrDefault(npcId);

    /// <summary>
    /// El nombre de una pelea guardada: el que quedó guardado o, si se guardó sin nombre ("NPC 2400123", porque la lista
    /// todavía no tenía la plantilla), el que trae hoy. <c>null</c> si no hay ninguno de los dos.
    /// </summary>
    public string? NameOf(string? savedName, uint? npcId) => FightTarget.Name(savedName) ?? (npcId is { } npc ? Of(npc)?.Name : null);

    /// <summary>
    /// La pelea guardada con el nombre de <see cref="NameOf"/>, solo para mostrarla: la base no cambia y los intentos se
    /// siguen juntando por la plantilla. Es la misma instancia si ya tenía nombre o la lista no conoce la plantilla.
    /// </summary>
    public EncounterSnapshot Named(EncounterSnapshot snapshot) =>
        FightTarget.Name(snapshot.TargetName) is null && NameOf(null, snapshot.TargetNpcId) is { } name ? snapshot with { TargetName = name } : snapshot;
}
