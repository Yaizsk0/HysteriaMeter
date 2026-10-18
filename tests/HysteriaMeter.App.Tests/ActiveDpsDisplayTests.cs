using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// DPS activo con el criterio de ACTIVO y TIEMPO MUERTO: 0 quiere decir que no hubo golpes directos que medir (todo
/// DoT), así que se muestra "–" en vez de un 0. El consejo "Sin ese tiempo muerto…" solo sigue al que lo nombra y su
/// número descuenta ese tiempo muerto y nada más.
/// </summary>
public sealed class ActiveDpsDisplayTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 21, 0, 0, TimeSpan.Zero);

    private static ActorSnapshot Actor(ulong id, string name, double activeDps, bool local = false) =>
        new(id, name, null, local, 50_000, 500, 0.5, 10, 0, 1_000, 0, [], ActiveDps: activeDps);

    private static EncounterSnapshot Fight(params ActorSnapshot[] actors) => Fight(100, actors);

    private static EncounterSnapshot Fight(int seconds, params ActorSnapshot[] actors) =>
        new(Guid.NewGuid(), Start, Start.AddSeconds(seconds), "Kromede", false, actors.Sum(a => a.Damage), actors);

    private static ActorDetailViewModel DetailOf(EncounterSnapshot fight, ulong id)
    {
        var detail = new ActorDetailViewModel(SkillNames.Empty);
        detail.Select(RowOf(fight, id));
        detail.Update(fight);
        return detail;
    }

    private static ActorRowViewModel RowOf(EncounterSnapshot fight, ulong id)
    {
        var row = new ActorRowViewModel();
        row.Update(1, fight.Actors.Single(a => a.Id == id), MeterTab.Damage, 1, 1, fight.Duration);
        return row;
    }

    [Fact]
    public void The_detail_shows_a_dash_without_active_dps()
    {
        var fight = Fight(Actor(1, "Solo DoT", activeDps: 0), Actor(2, "Directo", activeDps: 650));

        Assert.Equal("–", DetailOf(fight, 1).ActiveDps);
        Assert.NotEqual("–", DetailOf(fight, 2).ActiveDps);
    }

    [Fact]
    public void The_overlay_tooltip_shows_a_dash_without_active_dps()
    {
        var fight = Fight(Actor(1, "Solo DoT", activeDps: 0), Actor(2, "Directo", activeDps: 650));

        Assert.Contains("DPS activo: – ·", RowOf(fight, 1).Breakdown, StringComparison.Ordinal);
        Assert.DoesNotContain("DPS activo: – ·", RowOf(fight, 2).Breakdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Comparing_against_a_dash_marks_nobody_as_better()
    {
        var row = ActorDetailViewModel.Compare(Actor(1, "Solo DoT", activeDps: 0), Actor(2, "Directo", activeDps: 650))
            .Single(r => r.Label == "DPS activo");

        Assert.Equal("–", row.Mine);
        Assert.NotEqual("–", row.Theirs);
        Assert.False(row.MineBetter);
        Assert.False(row.TheirsBetter);
    }

    private static bool IsLostDpsTip(string tip) => tip.StartsWith("Sin ese tiempo muerto", StringComparison.Ordinal);

    [Fact]
    public void The_lost_dps_tip_only_follows_the_dead_time_tip()
    {
        // Pegó sin huecos del 0 al 59 y después no volvió (murió y quedó muerto) en una pelea de 119 s: dentro de su
        // actividad no hay tiempo muerto (ACTIVO 100 %, TIEMPO MUERTO 0 s), así que no hay "ese tiempo muerto" del que
        // hablar, aunque el DPS activo (sobre sus 59 s) sea el doble del DPS de la pelea.
        long[] steady = [.. Enumerable.Repeat(1_000L, 60)];
        var me = Actor(1, "Vos", activeDps: 60_000 / 59d, local: true) with { Damage = 60_000, Dps = 60_000 / 119d, Timeline = steady };

        Assert.Equal(0, FightActivity.Of(me).DeadSeconds);
        Assert.DoesNotContain(FightAdvice.For(Fight(119, me), me), IsLostDpsTip);

        // Con 20 s sin pegar en el medio de una pelea de 119 s, los dos consejos van juntos y hablan del mismo tiempo
        // muerto: el mismo daño en 119 − 20 = 99 s.
        long[] gap = [.. Enumerable.Range(0, 120).Select(s => s is >= 50 and < 70 ? 0L : 1_000L)];
        var dead = me with { Damage = 100_000, Dps = 100_000 / 119d, ActiveDps = 100_000 / 99d, Timeline = gap };
        var tips = FightAdvice.For(Fight(119, dead), dead);

        Assert.StartsWith("Pasaste 20 s sin pegar", tips[0], StringComparison.Ordinal);
        Assert.Equal(
            $"Sin ese tiempo muerto tu DPS habría sido {Numbers.Format(100_000 / 99d)} en vez de {Numbers.Format(100_000 / 119d)}.",
            tips[1]);
    }

    [Fact]
    public void The_lost_dps_tip_only_counts_the_named_dead_time()
    {
        // Pelea de 119 s (un aliado pega de punta a punta). Vos pegás 1.000 por segundo del 0 al 39, no pegás del 40 al
        // 49, pegás del 50 al 59 y morís sin volver. Dentro de tu actividad hay 10 s muertos de 60 (83 %): sale el primer
        // consejo. Sin esos 10 s tu DPS sería 50.000 / 109 ≈ 459 contra 420, 9 % más: no llega al 15 % y no se da. El
        // DPS activo (50.000 / 49 ≈ 1,0k) es otra cosa: también descuenta los 60 s en que estuviste muerto sin volver,
        // que no son "ese" tiempo muerto.
        long[] timeline = [.. Enumerable.Range(0, 60).Select(s => s is >= 40 and < 50 ? 0L : 1_000L)];
        var me = Actor(1, "Vos", activeDps: 50_000 / 49d, local: true) with { Damage = 50_000, Dps = 50_000 / 119d, Timeline = timeline };
        var tips = FightAdvice.For(Fight(119, me), me);

        Assert.Equal((60, 10), (FightActivity.Of(me).Seconds, FightActivity.Of(me).DeadSeconds));
        Assert.StartsWith("Pasaste 10 s sin pegar", tips[0], StringComparison.Ordinal);
        Assert.DoesNotContain(tips, IsLostDpsTip);
    }

    [Fact]
    public void The_lost_dps_tip_is_not_given_when_the_dead_time_does_not_fit_in_the_duration()
    {
        // Sesión de dos peleas de 5 s con 90 s entre ellas: la serie de la sesión cuenta ese rato como tiempo muerto, pero
        // la duración (el tiempo de combate) no lo incluye. "Sin ese tiempo muerto" no tiene sentido: no se da.
        long[] timeline = [.. Enumerable.Range(0, 106).Select(s => s is <= 5 or >= 100 ? 1_000L : 0L)];
        var me = Actor(1, "Vos", activeDps: 1_200, local: true) with { Damage = 12_000, Dps = 1_200, Timeline = timeline };
        var tips = FightAdvice.For(Fight(10, me), me);

        Assert.True(FightActivity.Of(me).DeadSeconds > 10);
        Assert.DoesNotContain(tips, IsLostDpsTip);
    }
}
