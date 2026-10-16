using System.IO;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

public class GameDataTests
{
    [Fact]
    public void Hand_written_skill_names_override_the_extracted_ones()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var extracted = Path.Combine(folder, "juego.json");
        var manual = Path.Combine(folder, "data.json");
        File.WriteAllText(extracted, """{ "skills": { "15280000": "Viento helado", "15010000": "Disparo" } }""");
        File.WriteAllText(manual, """{ "skills": { "15010000": "Flame Scattershot" } }""");

        var names = SkillNames.Load(extracted, manual, Path.Combine(folder, "no-existe.json"));

        Assert.Equal(("Viento helado", "Flame Scattershot"), (names.Of(15280000), names.Of(15010000)));
    }

    [Fact]
    public void Npc_files_are_merged_and_a_broken_one_is_ignored()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var extracted = Path.Combine(folder, "juego.json");
        var broken = Path.Combine(folder, "roto.json");
        File.WriteAllText(extracted, """{ "npcs": { "2300371": { "name": "Ambición de Kromede", "boss": true } } }""");
        File.WriteAllText(broken, "{ esto no es json");

        var npcs = NpcTable.Load(extracted, broken);

        Assert.Equal("Ambición de Kromede", npcs.Of(2300371)?.Name);
    }
}
