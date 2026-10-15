using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Pelea inventada para probar el overlay sin combatir: tamaño, fuente, opacidad, columnas, colores.
/// Los nombres no son de jugadores reales.
/// </summary>
public static class DemoEncounter
{
    private static readonly (string Name, long Damage, long Healing, bool Local)[] Players =
    [
        ("Kaelis", 2_840_000, 0, false),
        ("Vos", 2_310_000, 48_000, true),
        ("Morwen", 1_960_000, 0, false),
        ("Tharos", 1_420_000, 0, false),
        ("Ilyra", 610_000, 1_870_000, false),
        ("Brann", 980_000, 120_000, false),
    ];

    public static EncounterSnapshot Create(DateTimeOffset end)
    {
        const int seconds = 95;
        var start = end.AddSeconds(-seconds);
        var total = Players.Sum(p => p.Damage);
        var actors = Players
            .Select((p, i) =>
            {
                var hits = (int)(p.Damage / 9_000);
                var crits = hits * (30 + (i * 7)) / 100;
                var counts = new HitCounts(hits, crits, hits / 12, hits / 9, hits / 3, hits / 4, hits / 15);
                var skills = new[]
                {
                    new SkillSnapshot(9_001, p.Damage * 45 / 100, hits / 3, crits / 3, p.Damage / 40, 0.45, counts),
                    new SkillSnapshot(9_002, p.Damage * 30 / 100, hits / 3, crits / 3, p.Damage / 60, 0.30, counts, p.Damage / 10),
                    new SkillSnapshot(9_003, p.Damage * 25 / 100, hits / 3, crits / 3, p.Damage / 80, 0.25, counts),
                };
                var timeline = Enumerable.Range(0, seconds)
                    .Select(s => (long)(p.Damage / seconds * (0.6 + (0.8 * Math.Abs(Math.Sin((s + (i * 11)) / 9.0))))))
                    .ToArray();
                return new ActorSnapshot(
                    (ulong)(1000 + i),
                    p.Name,
                    null,
                    p.Local,
                    p.Damage,
                    (double)p.Damage / seconds,
                    (double)p.Damage / total,
                    hits,
                    crits,
                    p.Damage / 40,
                    p.Healing,
                    skills,
                    counts,
                    p.Damage * 9 / 10,
                    p.Damage * 9 / 10.0 / seconds,
                    p.Damage / 10,
                    p.Damage / (seconds * 0.92),
                    p.Damage / 200,
                    timeline,
                    DamageTaken: 180_000 + (i * 95_000),
                    HitsTaken: 20 + (i * 6),
                    Deaths: p.Name == "Tharos" ? 1 : 0);
            })
            .ToArray();

        var boss = 9_999UL;
        var tharos = actors.First(a => a.Name == "Tharos");
        var deathAt = start.AddSeconds(71);
        var lastHits = new[]
        {
            new CombatLogEntry(deathAt.AddSeconds(-6), LogKind.Damage, boss, "Muñeco de prueba", tharos.Id, tharos.Name, 9_101, 41_000),
            new CombatLogEntry(deathAt.AddSeconds(-2), LogKind.Damage, boss, "Muñeco de prueba", tharos.Id, tharos.Name, 9_102, 88_000, Protocol.Events.HitModifiers.Critical),
        };

        return new EncounterSnapshot(
            Guid.Empty,
            start,
            end,
            "Muñeco de prueba",
            TargetKilled: false,
            total,
            actors,
            TimeSpan.FromSeconds(seconds),
            new Health(3_620_000, 13_740_000),
            [new DeathRecap(deathAt, tharos.Id, tharos.Name, lastHits)]);
    }
}
