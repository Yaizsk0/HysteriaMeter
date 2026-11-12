using HysteriaMeter.Combat;

namespace HysteriaMeter.Combat.Tests;

/// <summary>
/// Las peleas guardadas antes de que la lista de NPCs nombrara su plantilla quedaron sin nombre ("NPC 2400123"): al
/// mostrarlas se usa el nombre que la lista trae hoy. Un nombre guardado no cambia y la base no se toca.
/// </summary>
public class SavedFightNameTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);

    private static readonly NpcCatalog Npcs = new(new Dictionary<uint, NpcInfo>
    {
        [2400123] = new("Kromede"),
        [2400124] = new("Nombre de hoy"),
    });

    [Fact]
    public void A_fight_saved_without_a_name_takes_the_name_the_list_has_today()
    {
        Assert.Equal("Kromede", Npcs.NameOf(null, 2400123));
        Assert.Equal("Kromede", Npcs.NameOf("", 2400123));
        Assert.Equal("Kromede", Npcs.NameOf("#900", 2400123)); // "#entidad" de versiones viejas cuenta como sin nombre
    }

    [Fact]
    public void A_saved_name_stays_and_an_unknown_template_has_none()
    {
        Assert.Equal("Nombre guardado", Npcs.NameOf("Nombre guardado", 2400124));
        Assert.Equal("Nombre guardado", Npcs.NameOf("Nombre guardado", null));
        Assert.Null(Npcs.NameOf(null, 7));
        Assert.Null(Npcs.NameOf(null, null));
        Assert.Null(NpcCatalog.Empty.NameOf(null, 2400123));
    }

    [Fact]
    public void A_saved_fight_is_named_without_changing_anything_else()
    {
        var saved = new EncounterSnapshot(Guid.NewGuid(), Start, Start.AddSeconds(30), null, true, 1000, [], TargetNpcId: 2400123);

        var named = Npcs.Resolve(saved);

        Assert.Equal(saved with { TargetName = "Kromede" }, named);
        Assert.Equal("Kromede", FightTarget.Label(named));
        // Los intentos se siguen juntando por la plantilla, con nombre o sin él.
        Assert.Equal(FightTarget.Identity(saved), FightTarget.Identity(named));

        var unknown = saved with { TargetNpcId = 7 };
        Assert.Same(unknown, Npcs.Resolve(unknown));
        var withName = saved with { TargetName = "Nombre guardado" };
        Assert.Same(withName, Npcs.Resolve(withName));
    }
}
