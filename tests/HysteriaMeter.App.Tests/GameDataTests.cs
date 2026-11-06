using System.IO;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

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
    public void Published_skill_names_cover_the_skills_compared_with_Combat_Analysis()
    {
        var names = SkillNames.Load(FindRepoFile("skills.json"));

        // Los IDs como llegaron en la pelea comparada: las variantes de clase toman el nombre de la base.
        Assert.Equal("Punishing Strike", names.Of(12060240));
        Assert.Equal("Decisive Strike", names.Of(12020040));
        Assert.Equal("Desperate Strike", names.Of(12030040));
        Assert.Equal("Theostone: Ulgorn's Will", names.Of(3000019));
        Assert.Equal("Skill 1900911", names.Of(1900911));
    }

    [Fact]
    public void Skill_icons_fall_back_to_the_base_skill()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(folder, "15280000.png"), [1]);

        var names = SkillNames.Empty.WithIcons(folder);

        Assert.Equal(Path.Combine(folder, "15280000.png"), names.IconOf(15280143));
        Assert.Null(names.IconOf(15010000));
        Assert.Null(SkillNames.Empty.WithIcons(Path.Combine(folder, "no-existe")).IconOf(15280000));
    }

    [Fact]
    public void Class_emblems_come_from_the_folder_when_present()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(folder, "15.png"), [1]);
        var previous = PlayerClasses.EmblemFolder;
        try
        {
            PlayerClasses.EmblemFolder = folder;

            Assert.Equal(Path.Combine(folder, "15.png"), PlayerClasses.EmblemOf(15));
            Assert.Null(PlayerClasses.EmblemOf(11));
            Assert.Null(PlayerClasses.EmblemOf(null));
        }
        finally
        {
            PlayerClasses.EmblemFolder = previous;
        }
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

    [Fact]
    public void Published_npc_names_cover_the_reference_fights()
    {
        var npcs = NpcTable.Load(FindRepoFile("npcs.json"));

        // Las dos peleas de referencia de la 002, con el nombre que muestra Combat Analysis y sin enrage medido.
        Assert.Equal(new NpcInfo("Divine Auldor", "Urugugu Canyon", IsBoss: true), npcs.Of(2300218));
        Assert.Equal(new NpcInfo("Destroyer Tarka", IsBoss: true), npcs.Of(2101106));
        Assert.Null(npcs.Of(2400123));
    }

    [Fact]
    public void Published_npc_list_names_the_mobs_of_real_fights()
    {
        var npcs = NpcTable.Load(FindRepoFile("npcs-taengu.json"));

        // Un archivo generado que no se pueda leer queda vacío sin avisar: el conteo es lo que lo detecta.
        Assert.True(npcs.Count > 6000, $"{npcs.Count} NPCs");
        // Monstruos de peleas reales que quedaban como "NPC 2100664": trash, invocaciones y objetos, no solo bosses.
        Assert.Equal(new NpcInfo("Hellfire Spirit"), npcs.Of(2100664));
        Assert.Equal(new NpcInfo("Summoned Duduka Worker"), npcs.Of(2300900));
        Assert.Equal(new NpcInfo("Dark Spirit's Sealed Apparition"), npcs.Of(2600205));
        // Los bosses vienen marcados (pausa de fase, tarjeta del kill), sin dungeon ni enrage: eso es de MetaBot.GG.
        Assert.Equal(new NpcInfo("Ferocious Horn Nuakum", IsBoss: true), npcs.Of(2300910));
    }

    [Fact]
    public void Installed_npc_lists_go_from_the_published_one_to_the_hand_written_one()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        var extracted = Path.Combine(folder, "juego.json");
        File.WriteAllText(Path.Combine(folder, "data", "npcs-taengu.json"), """{ "npcs": { "1": "Publicada", "2": "Publicada", "3": { "name": "Publicada", "boss": true } } }""");
        File.WriteAllText(extracted, """{ "npcs": { "2": "Juego", "3": "Juego" } }""");
        File.WriteAllText(Path.Combine(folder, "data", "npcs.json"), """{ "npcs": { "3": { "name": "A mano", "boss": true, "enrage": 480 } } }""");

        var npcs = NpcTable.LoadInstalled(folder, extracted);

        Assert.Equal(new NpcInfo("Publicada"), npcs.Of(1));
        Assert.Equal(new NpcInfo("Juego"), npcs.Of(2));
        Assert.Equal(new NpcInfo("A mano", IsBoss: true, EnrageSeconds: 480), npcs.Of(3));
        // Sin la copia del juego (lo normal) quedan la publicada y la del repo.
        Assert.Equal("Publicada", NpcTable.LoadInstalled(folder, Path.Combine(folder, "no-existe.json")).Of(2)?.Name);
    }

    /// <summary>Sube desde la carpeta de salida hasta la raíz del repo y devuelve <c>data/&lt;archivo&gt;</c>.</summary>
    private static string FindRepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", name);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "HysteriaMeter.slnx")))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"No se encontró data/{name} en la raíz del repo.");
    }
}
