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

    /// <summary>Las plantillas marcadas como boss, ordenadas: el SQL del historial las recibe para clasificar como el vivo.</summary>
    public IReadOnlyList<uint> Bosses { get; } = [.. npcs.Where(npc => npc.Value.IsBoss).Select(npc => npc.Key).Order()];

    public NpcInfo? Of(uint npcId) => npcs.GetValueOrDefault(npcId);

    /// <summary>La plantilla está marcada como boss. Es lo que el motor le pone a la pelea en vivo (<see cref="EncounterSnapshot.TargetIsBoss"/>).</summary>
    public bool IsBoss(uint? npcId) => npcId is { } npc && Of(npc) is { IsBoss: true };

    /// <summary>
    /// El nombre de una pelea guardada: el que quedó guardado o, si se guardó sin nombre ("NPC 2400123", porque la lista
    /// todavía no tenía la plantilla), el que trae hoy. <c>null</c> si no hay ninguno de los dos.
    /// </summary>
    public string? NameOf(string? savedName, uint? npcId) => FightTarget.Name(savedName) ?? (npcId is { } npc ? Of(npc)?.Name : null);

    /// <summary>
    /// La pelea guardada o compartida con lo que la lista dice hoy de su objetivo, solo para mostrarla: el nombre de
    /// <see cref="NameOf"/> si se guardó sin nombre, y la marca de boss (<see cref="IsBoss"/>), que no se guarda. Así el
    /// detalle, "Comparar con" y el modo dungeon la clasifican como el vivo. La base no cambia y los intentos se siguen
    /// juntando por la plantilla. Una marca que ya traía (un archivo compartido) se queda. Es la misma instancia si no
    /// cambia nada.
    /// </summary>
    public EncounterSnapshot Resolve(EncounterSnapshot snapshot)
    {
        var name = FightTarget.Name(snapshot.TargetName) is null ? NameOf(null, snapshot.TargetNpcId) : null;
        var boss = !snapshot.TargetIsBoss && IsBoss(snapshot.TargetNpcId);
        return name is null && !boss
            ? snapshot
            : snapshot with { TargetName = name ?? snapshot.TargetName, TargetIsBoss = snapshot.TargetIsBoss || boss };
    }
}
