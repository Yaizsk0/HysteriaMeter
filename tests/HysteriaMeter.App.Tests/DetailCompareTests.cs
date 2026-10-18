using System.Globalization;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>Vista Comparar contra el mock (paso 7): la frase de la skill dice si coincide con una muerte.</summary>
public sealed class DetailCompareTests
{
    [Fact]
    public void Death_span_runs_from_the_death_to_the_next_own_hit()
    {
        // Pega del 0 al 111, muere en el 112 y vuelve a pegar en el 140.
        long[] timeline = [.. Enumerable.Range(0, 200).Select(i => i is >= 112 and < 140 ? 0L : 100L)];

        Assert.Equal(", coincide con la muerte de 1:52 a 2:20", ActorDetailViewModel.DeathClause([112], timeline, 200));
    }

    [Fact]
    public void Without_a_later_hit_the_span_ends_with_the_fight_and_the_longest_death_wins()
    {
        long[] timeline = [.. Enumerable.Range(0, 100).Select(i => i is (>= 20 and < 25) or >= 60 ? 0L : 100L)];

        Assert.Equal(", coincide con la muerte de 1:00 a 1:40", ActorDetailViewModel.DeathClause([20, 60], timeline, 100));
        Assert.Equal(string.Empty, ActorDetailViewModel.DeathClause([], timeline, 100));
    }

    [Fact]
    public void A_death_followed_by_a_hit_right_away_is_not_named()
    {
        // Murió en el 20 y pegó en el 21 (por ejemplo, un daño periódico): no hubo tramo muerto que explique nada.
        long[] timeline = [.. Enumerable.Repeat(100L, 100)];

        Assert.Equal(string.Empty, ActorDetailViewModel.DeathClause([20], timeline, 100));
    }

    [Fact]
    public void Insight_adds_the_death_only_when_damage_was_lost()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        long[] timeline = [.. Enumerable.Range(0, 200).Select(i => i is >= 112 and < 140 ? 0L : 100L)];
        var mine = Actor(1, [Skill(1, 600, 6)], timeline);
        var theirs = Actor(2, [Skill(1, 1_000, 10)], null);

        var (lead, skill, tail) = ActorDetailViewModel.Insight(mine, theirs, SkillNames.Empty, deathSeconds: [112], fightSeconds: 200);

        Assert.Equal(("Perdiste más en ", "Skill 1", ": 4 golpes menos (−400), coincide con la muerte de 1:52 a 2:20."), (lead, skill, tail));

        // Sin muertes, la frase de siempre; si sacó ventaja, la muerte no explica nada y no se nombra.
        Assert.Equal(": 4 golpes menos (−400).", ActorDetailViewModel.Insight(mine, theirs, SkillNames.Empty).Tail);
        Assert.Equal(": 4 golpes más (+400).", ActorDetailViewModel.Insight(theirs, mine, SkillNames.Empty, deathSeconds: [112], fightSeconds: 200).Tail);
    }

    private static SkillSnapshot Skill(uint id, long damage, int hits) => new(id, damage, hits, 0, damage / Math.Max(1, hits), 0);

    private static ActorSnapshot Actor(ulong id, SkillSnapshot[] skills, long[]? timeline) =>
        new(id, $"Jugador {id}", null, id == 1, skills.Sum(s => s.Damage), 100, 0.5, skills.Sum(s => s.Hits), 0, 0, 0, skills, Timeline: timeline);
}
