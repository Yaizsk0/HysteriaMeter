using System.Globalization;

namespace HysteriaMeter.Combat;

/// <summary>
/// Cómo se llama el objetivo de una pelea en el overlay, el historial y el modo dungeon. Los monstruos no traen nombre
/// en el paquete: sale de data/npcs.json o, si la plantilla no está ahí, queda "NPC 2400123". Sin nombre ni plantilla
/// (peleas guardadas antes de registrar la plantilla) es un "Encuentro" sin nada con qué compararlo.
/// </summary>
public static class FightTarget
{
    /// <summary>Rótulo de una pelea sin nombre ni plantilla de NPC.</summary>
    public const string Unknown = "Encuentro";

    /// <summary>"NPC 2400123": la plantilla del objetivo cuando data/npcs.json no le pone nombre.</summary>
    public static string NpcLabel(uint npcId) => $"NPC {npcId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>El nombre de verdad: las versiones viejas guardaban "#entidad" cuando no había nombre.</summary>
    public static string? Name(string? name) => name is { Length: > 0 } text && text[0] != '#' ? text : null;

    /// <summary>
    /// Con qué se comparan los intentos: el nombre o, sin nombre, la plantilla ("NPC 2400123"). <c>null</c> si no hay
    /// ninguno de los dos: esa pelea no se junta con ninguna otra.
    /// </summary>
    public static string? Key(string? name, uint? npcId) => Name(name) ?? (npcId is { } npc ? NpcLabel(npc) : null);

    /// <summary>La clave o, sin clave, "Encuentro".</summary>
    public static string Label(string? name, uint? npcId) => Key(name, npcId) ?? Unknown;

    public static string? Key(EncounterSnapshot snapshot) => Key(snapshot.TargetName, snapshot.TargetNpcId);

    public static string Label(EncounterSnapshot snapshot) => Label(snapshot.TargetName, snapshot.TargetNpcId);
}
