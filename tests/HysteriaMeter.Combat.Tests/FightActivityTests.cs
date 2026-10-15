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
}
