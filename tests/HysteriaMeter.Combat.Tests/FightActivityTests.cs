namespace HysteriaMeter.Combat.Tests;

public class FightActivityTests
{
    [Fact]
    public void Gaps_longer_than_the_tolerance_count_as_dead_time()
    {
        // Antes del primer golpe no cuenta; 2 s sin pegar se toleran; 4 s y 3 s son tiempo muerto.
        long[] timeline = [0, 0, 100, 0, 0, 100, 0, 0, 0, 0, 100, 100, 0, 0, 0, 100, 0, 0];
        var activity = FightActivity.From(timeline);
        Assert.Equal(new FightActivity(Seconds: 14, DeadSeconds: 7, LongestGap: 4, Gaps: 2, LongestGapAt: 6), activity);
        Assert.Equal(0.5, activity.Uptime, 3);
    }

    [Fact]
    public void Without_damage_there_is_no_activity()
    {
        Assert.Equal(FightActivity.Empty, FightActivity.From([0, 0, 0]));
        Assert.Equal(FightActivity.Empty, FightActivity.From(null));
        Assert.Equal(1, FightActivity.Empty.Uptime);
    }

    [Fact]
    public void A_players_activity_leaves_out_the_dot_ticks()
    {
        // El DoT pega todos los segundos; los golpes directos tienen un hueco de 4 s.
        var actor = new ActorSnapshot(1, "Yo", null, true, 600, 100, 1, 2, 0, 100, 0, [],
            Timeline: [100, 100, 100, 100, 100, 100], DirectTimeline: [100, 0, 0, 0, 0, 100]);

        Assert.Equal(new FightActivity(Seconds: 6, DeadSeconds: 4, LongestGap: 4, Gaps: 1, LongestGapAt: 1), FightActivity.Of(actor));

        // Sin serie directa (sin DoT o una pelea guardada antes), el Timeline.
        Assert.Equal(FightActivity.From(actor.Timeline), FightActivity.Of(actor with { DirectTimeline = null }));
    }

    [Fact]
    public void Active_dps_counts_the_damage_and_time_of_the_active_seconds()
    {
        // Golpes directos en 1, 2, 3 y 8; el DoT pega del 1 al 9. El hueco del 4 al 7 (4 s) es tiempo muerto: sus ticks no
        // cuentan, ni el del 9, después del último golpe. Daño activo: 150 · 3 + 150 = 600; tiempo, del 1 al 8 son 7 s menos
        // los 4 muertos = 3 s.
        long[] direct = [0, 100, 100, 100, 0, 0, 0, 0, 100];
        long[] timeline = [0, 150, 150, 150, 50, 50, 50, 50, 150, 50];

        Assert.Equal(600d / 3, FightActivity.ActiveDps(direct, timeline), 3);
        Assert.Equal(600d / 2, FightActivity.ActiveDps(direct, timeline, maxSeconds: 2), 3); // el tope del tiempo de combate

        // Sin serie directa (sin DoT), el Timeline: del 1 al 9 sin huecos, 8 s.
        Assert.Equal(850d / 8, FightActivity.ActiveDps(null, timeline), 3);

        // Sin golpes directos (todo DoT), 0; un solo golpe, su segundo sobre 1 s.
        Assert.Equal(0d, FightActivity.ActiveDps([], timeline));
        Assert.Equal(0d, FightActivity.ActiveDps(null, null));
        Assert.Equal(150d, FightActivity.ActiveDps([0, 100], timeline), 3);
    }
}
