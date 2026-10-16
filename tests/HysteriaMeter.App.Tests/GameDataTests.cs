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
    public void Class_skill_variants_take_the_name_of_their_base_skill()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var file = Path.Combine(folder, "skills.json");
        File.WriteAllText(file, """{ "skills": { "15280000": "Bittercold Wind", "15060453": "Hellfire (3/3)" } }""");

        var names = SkillNames.Load(file);

        Assert.Equal("Bittercold Wind", names.Of(15280143));
        Assert.Equal("Hellfire (3/3)", names.Of(15060453));
        Assert.Equal("Skill 15060451", names.Of(15060451));
        Assert.Equal("Skill 3001016", names.Of(3001016));
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
