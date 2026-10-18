using HysteriaMeter.Combat;

namespace HysteriaMeter.Storage.Tests;

/// <summary>"Exportar resumen" del modo dungeon: la run en CSV, con los mismos números que las tarjetas.</summary>
public class RunExportTests
{
    private static readonly DateTimeOffset Entered = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static ActorSnapshot Player(string name, long damage, double seconds, int deaths = 0, bool local = false) =>
        new(1, name, 15, local, damage, damage / seconds, 0, 10, 2, 100, 0, [],
            MainTargetDamage: damage, MainTargetDps: damage / seconds, Deaths: deaths);

    private static EncounterSnapshot Fight(int startSecond, int seconds, uint npc, bool killed, params ActorSnapshot[] actors) =>
        new(Guid.NewGuid(), Entered.AddSeconds(startSecond), Entered.AddSeconds(startSecond + seconds), null, killed,
            actors.Sum(a => a.Damage), actors, TargetNpcId: npc, ZoneEnteredAt: Entered);

    private static DungeonSummary Run() => DungeonSummary.From(
        [
            Fight(60, 100, 2400123, false, Player("Mati", 10_000, 100, deaths: 1, local: true), Player("Rubia; la maga", 8_000, 100)),
            Fight(300, 120, 2400123, true, Player("Mati", 24_000, 120, local: true), Player("Rubia; la maga", 36_000, 120)),
        ],
        Entered);

    [Fact]
    public void Writes_one_row_per_player_and_boss_and_the_whole_run_at_the_end()
    {
        var lines = EncounterExport.RunToCsv(Run()).TrimEnd().Split(Environment.NewLine);

        Assert.Equal("boss;entrada;intentos;wipes;derrotado;duracion_s;dps_party;jugador;local;dps;muertes", lines[0]);
        Assert.Equal("NPC 2400123;2026-10-16T21:00:00.0000000+00:00;2;1;si;120.0;500;\"Rubia; la maga\";no;300;0", lines[1]);
        Assert.Equal("NPC 2400123;2026-10-16T21:00:00.0000000+00:00;2;1;si;120.0;500;Mati;si;200;1", lines[2]);
        Assert.StartsWith("Run completa;2026-10-16T21:00:00.0000000+00:00;2;1;1/1;420.0;;", lines[3], StringComparison.Ordinal);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void Anonymizing_a_run_keeps_the_same_name_for_a_player_on_every_boss()
    {
        var run = EncounterExport.Anonymize(Run());

        Assert.Equal(["Jugador 1", "Yo"], run.Players.Select(p => p.Name));
        Assert.Equal(["Jugador 1", "Yo"], run.Bosses[0].Players.Select(p => p.Name));
    }

    [Fact]
    public void The_file_is_named_after_the_entry()
    {
        Assert.Equal("Run 2026-10-16 21-00", EncounterExport.RunFileName(Entered));
    }
}
