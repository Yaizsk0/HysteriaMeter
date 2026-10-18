using System.Globalization;

namespace HysteriaMeter.Combat;

/// <summary>
/// Cómo se llama el objetivo de una pelea en el overlay, el historial y el modo dungeon, y cuándo dos peleas son contra
/// el mismo objetivo. Los monstruos no traen nombre en el paquete: sale de data/npcs.json o, si la plantilla no está ahí,
/// queda "NPC 2400123". Sin nombre ni plantilla (peleas guardadas antes de registrar la plantilla) es un "Encuentro" sin
/// nada con qué compararlo.
/// </summary>
public static class FightTarget
{
    /// <summary>Rótulo de una pelea sin nombre ni plantilla de NPC.</summary>
    public const string Unknown = "Encuentro";

    private const string NpcPrefix = "NPC ";

    /// <summary>"NPC 2400123": la plantilla del objetivo cuando data/npcs.json no le pone nombre.</summary>
    public static string NpcLabel(uint npcId) => NpcPrefix + npcId.ToString(CultureInfo.InvariantCulture);

    /// <summary>El nombre de verdad: las versiones viejas guardaban "#entidad" cuando no había nombre.</summary>
    public static string? Name(string? name) => name is { Length: > 0 } text && text[0] != '#' ? text : null;

    /// <summary>
    /// Lo que se muestra del objetivo: el nombre o, sin nombre, la plantilla ("NPC 2400123"). <c>null</c> si no hay
    /// ninguno de los dos. Para saber si dos peleas son contra el mismo objetivo va <see cref="Identity(string?, uint?)"/>.
    /// </summary>
    public static string? NameOrNpc(string? name, uint? npcId) => Name(name) ?? (npcId is { } npc ? NpcLabel(npc) : null);

    /// <summary>El rótulo o, sin nombre ni plantilla, "Encuentro".</summary>
    public static string Label(string? name, uint? npcId) => NameOrNpc(name, npcId) ?? Unknown;

    public static string? NameOrNpc(EncounterSnapshot snapshot) => NameOrNpc(snapshot.TargetName, snapshot.TargetNpcId);

    public static string Label(EncounterSnapshot snapshot) => Label(snapshot.TargetName, snapshot.TargetNpcId);

    /// <summary>
    /// Con qué se comparan los intentos: la plantilla primero ("npc:2400123") y, sin plantilla, el nombre
    /// ("nombre:Kromede"). Así, ponerle nombre a una plantilla en data/npcs.json no separa las peleas nuevas de las que
    /// quedaron como "NPC 2400123", y dos plantillas con el mismo nombre son dos objetivos. <c>null</c> sin ninguno de los
    /// dos: esa pelea no se junta con ninguna otra. Es la misma regla que el SQL del historial (EncounterRepository).
    /// </summary>
    public static string? Identity(string? name, uint? npcId) =>
        npcId is { } npc ? "npc:" + npc.ToString(CultureInfo.InvariantCulture)
        : Name(name) is { } text ? "nombre:" + text
        : null;

    public static string? Identity(EncounterSnapshot snapshot) => Identity(snapshot.TargetName, snapshot.TargetNpcId);
}
