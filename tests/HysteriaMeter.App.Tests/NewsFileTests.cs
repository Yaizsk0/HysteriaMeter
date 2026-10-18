using System.IO;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// El <c>data/novedades.json</c> que se publica se tiene que poder leer: un escape inválido ("Juego\iconos")
/// hacía que NewsFeed descartara el archivo entero y el meter no mostrara ninguna novedad.
/// </summary>
public sealed class NewsFileTests
{
    [Fact]
    public void Published_news_file_parses_every_entry()
    {
        var path = FindNewsFile();
        var json = File.ReadAllText(path);

        var items = NewsFeed.Parse(json);

        // Una entrada por cada "id" del archivo: si el JSON está roto, Parse devuelve una lista vacía.
        var declared = json.Split("\"id\"").Length - 1;
        Assert.NotEmpty(items);
        Assert.Equal(declared, items.Count);
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Paths_with_backslashes_keep_their_backslash()
    {
        var items = NewsFeed.Parse(File.ReadAllText(FindNewsFile()));

        Assert.Contains(items, i => i.Text.Contains(@"Juego\iconos", StringComparison.Ordinal));
    }

    /// <summary>Sube desde la carpeta de salida hasta la raíz del repo, donde está <c>data/novedades.json</c>.</summary>
    private static string FindNewsFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", "novedades.json");
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "HysteriaMeter.slnx")))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("No se encontró data/novedades.json en la raíz del repo.");
    }
}
