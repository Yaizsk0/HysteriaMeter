using HysteriaMeter.Combat;

namespace HysteriaMeter.Combat.Tests;

/// <summary>
/// La marca de boss no se guarda con la pelea: en vivo sale de la lista de NPCs y una pelea guardada o compartida la
/// recupera de la misma lista, así el historial y el modo dungeon clasifican como el vivo.
/// </summary>
public class SavedFightBossTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);

    private static readonly NpcCatalog Npcs = new(new Dictionary<uint, NpcInfo>
    {
        [2300910] = new("Ferocious Horn Nuakum", IsBoss: true),
        [2300907] = new("Shining Mau Totem", IsBoss: true),
        [2100664] = new("Hellfire Spirit"),
    });

    [Fact]
    public void The_list_says_which_templates_are_bosses()
    {
        Assert.True(Npcs.IsBoss(2300910));
        Assert.False(Npcs.IsBoss(2100664));
        Assert.False(Npcs.IsBoss(7));
        Assert.False(Npcs.IsBoss(null));
        Assert.False(NpcCatalog.Empty.IsBoss(2300910));

        // Ordenados, para que el SQL del historial reciba siempre la misma lista.
        Assert.Equal([2300907u, 2300910u], Npcs.Bosses);
        Assert.Empty(NpcCatalog.Empty.Bosses);
    }

    [Fact]
    public void A_saved_fight_gets_back_the_boss_mark_of_the_list()
    {
        var saved = new EncounterSnapshot(Guid.NewGuid(), Start, Start.AddSeconds(15), "Ferocious Horn Nuakum", true, 1000, [], TargetNpcId: 2300910);

        var resolved = Npcs.Resolve(saved);

        Assert.Equal(saved with { TargetIsBoss = true }, resolved);
        Assert.True(BossRules.IsBossKill(resolved));
        Assert.False(BossRules.IsBossKill(saved));

        // Sin nombre guardado recupera las dos cosas a la vez.
        var unnamed = saved with { TargetName = null };
        Assert.Equal(saved with { TargetIsBoss = true }, Npcs.Resolve(unnamed));
    }

    [Fact]
    public void What_the_list_does_not_mark_stays_as_it_came()
    {
        var trash = new EncounterSnapshot(Guid.NewGuid(), Start, Start.AddSeconds(15), "Hellfire Spirit", true, 1000, [], TargetNpcId: 2100664);
        Assert.Same(trash, Npcs.Resolve(trash));

        // Un archivo compartido que ya traía la marca (la lista de quien lo exportó) la conserva aunque la de hoy no la tenga.
        var shared = trash with { TargetIsBoss = true };
        Assert.Same(shared, Npcs.Resolve(shared));
        Assert.Same(shared, NpcCatalog.Empty.Resolve(shared));
    }
}
