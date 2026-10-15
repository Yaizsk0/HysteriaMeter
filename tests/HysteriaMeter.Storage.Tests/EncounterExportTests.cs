using HysteriaMeter.Combat;

namespace HysteriaMeter.Storage.Tests;

public sealed class EncounterExportTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 21, 42, 0, TimeSpan.Zero);

    private static EncounterSnapshot Fight() => new(
        Guid.NewGuid(),
        Start,
        Start.AddSeconds(100),
        "Destroyer: Tarka",
        TargetKilled: true,
        TotalDamage: 3000,
        [
            new ActorSnapshot(2, "Beto;el", null, false, 1000, 10, 1d / 3, 5, 1, 400, 0, []),
            new ActorSnapshot(1, "Mati", null, true, 2000, 20, 2d / 3, 8, 4, 900, 500, []),
        ]);

    [Fact]
    public void Csv_has_one_row_per_player_by_damage()
    {
        var lines = EncounterExport.ToCsv(Fight()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("objetivo;inicio;duracion_s;", lines[0], StringComparison.Ordinal);
        Assert.Contains(";Mati;si;2000;20;", lines[1], StringComparison.Ordinal);
        Assert.Contains(";\"Beto;el\";no;1000;", lines[2], StringComparison.Ordinal);
        Assert.Contains(";500;5;", lines[1], StringComparison.Ordinal); // curación y HPS
    }

    [Fact]
    public void Anonymize_keeps_me_and_numbers_the_rest()
    {
        var anonymous = EncounterExport.Anonymize(Fight());

        Assert.Equal(["Yo", "Jugador 1"], anonymous.Actors.Select(a => a.Name));
        Assert.DoesNotContain("Beto", EncounterExport.ToJson(anonymous), StringComparison.Ordinal);
    }

    [Fact]
    public void File_name_is_safe_for_windows()
    {
        Assert.Equal("Destroyer_ Tarka 2026-10-15 21-42", EncounterExport.FileName(Fight(), Start));
    }

    [Fact]
    public void Json_round_trips_a_shared_fight()
    {
        var fight = Fight();

        var read = EncounterExport.FromJson(EncounterExport.ToJson(fight));

        Assert.NotNull(read);
        Assert.Equal(fight.TargetName, read.TargetName);
        Assert.Equal(fight.TotalDamage, read.TotalDamage);
        Assert.Equal(["Beto;el", "Mati"], read.Actors.Select(a => a.Name));
        Assert.True(read.Actors[1].IsLocalPlayer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    [InlineData("{\"Start\":\"2026-10-15T21:42:00+00:00\",\"End\":\"2026-10-15T21:00:00+00:00\",\"Actors\":[{\"Name\":\"Mati\"}]}")]
    [InlineData("{\"Start\":\"2026-10-15T21:42:00+00:00\",\"End\":\"2026-10-15T21:43:00+00:00\",\"Actors\":[]}")]
    public void Invalid_json_is_not_a_fight(string json) => Assert.Null(EncounterExport.FromJson(json));
}
