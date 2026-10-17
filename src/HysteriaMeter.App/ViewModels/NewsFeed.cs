using System.IO;
using System.Text.Json;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una novedad del meter; el número crece con cada una (se usa el del PR).</summary>
public sealed record NewsItem(int Id, string Text);

/// <summary>Lo que se muestra al abrir el meter después de actualizarlo: las novedades desde la última vez.</summary>
public sealed record StartupNotice(string Title, IReadOnlyList<string> Lines, int LatestId);

/// <summary>
/// Novedades de <c>data/novedades.json</c>, como el "qué cambió" de Ember: al actualizar se ven una vez y listo.
/// La bienvenida del primer arranque ya no pasa por acá: es su propia ventana (Welcome/WelcomeWindow).
/// </summary>
public static class NewsFeed
{
    public static IReadOnlyList<NewsItem> Current { get; private set; } = [];

    /// <summary>La novedad más nueva (0 si no hay): quien recién instala ya la cuenta como vista.</summary>
    public static int LatestId(IReadOnlyList<NewsItem> items) => items.Count > 0 ? items.Max(i => i.Id) : 0;

    public static void Load(string path)
    {
        if (File.Exists(path))
        {
            Current = Parse(File.ReadAllText(path));
        }
    }

    /// <summary><c>{ "news": [ { "id": 36, "text": "..." } ] }</c>; lo que esté mal se ignora.</summary>
    public static IReadOnlyList<NewsItem> Parse(string json)
    {
        var items = new List<NewsItem>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("news", out var news)
                && news.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in news.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object
                        && entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) && number > 0
                        && entry.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { Length: > 0 } line)
                    {
                        items.Add(new NewsItem(number, line.Trim()));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Un archivo roto no impide abrir el meter: simplemente no hay novedades.
        }

        return [.. items.OrderByDescending(i => i.Id)];
    }

    /// <summary>
    /// Las novedades más nuevas que la última vista (hasta 6). <c>null</c> si no hay nada que mostrar. Con
    /// <paramref name="lastSeen"/> 0 son todas: a quien recién instala, App.xaml.cs ya le marca la última como vista.
    /// </summary>
    public static StartupNotice? NoticeFor(IReadOnlyList<NewsItem> items, int lastSeen)
    {
        var latest = LatestId(items);
        var unseen = items.Where(i => i.Id > lastSeen).OrderByDescending(i => i.Id).Take(6).Select(i => i.Text).ToList();
        return unseen.Count == 0 ? null : new StartupNotice("NOVEDADES", unseen, latest);
    }
}
