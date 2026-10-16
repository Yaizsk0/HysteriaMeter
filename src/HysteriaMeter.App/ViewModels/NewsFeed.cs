using System.IO;
using System.Text.Json;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una novedad del meter; el número crece con cada una (se usa el del PR).</summary>
public sealed record NewsItem(int Id, string Text);

/// <summary>Lo que se muestra al abrir el meter: la bienvenida la primera vez o las novedades desde la última vez.</summary>
public sealed record StartupNotice(string Title, IReadOnlyList<string> Lines, int LatestId);

/// <summary>
/// Novedades de <c>data/novedades.json</c>, como el "qué cambió" de Ember: al actualizar se ven una vez y listo.
/// </summary>
public static class NewsFeed
{
    public const string WelcomeTitle = "BIENVENIDO A HYSTERIA METER";

    public static IReadOnlyList<string> WelcomeLines { get; } =
    [
        "Necesitás Npcap instalado (npcap.com, modo compatible con WinPcap). El meter solo lee el tráfico: no toca el juego.",
        "Entrá al juego y pegale a algo: el punto del encabezado se pone verde en combate.",
        "Clic en una fila: detalle por skill. Doble clic en el encabezado: modo compacto.",
        "Ctrl+Shift+L deja pasar los clics al juego; el engranaje abre la configuración.",
    ];

    public static IReadOnlyList<NewsItem> Current { get; private set; } = [];

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
    /// La primera vez (<paramref name="lastSeen"/> 0) da la bienvenida; después, las novedades más nuevas que la última
    /// vista (hasta 6). <c>null</c> si no hay nada que mostrar.
    /// </summary>
    public static StartupNotice? NoticeFor(IReadOnlyList<NewsItem> items, int lastSeen)
    {
        var latest = items.Count > 0 ? items.Max(i => i.Id) : 0;
        if (lastSeen <= 0)
        {
            return new StartupNotice(WelcomeTitle, WelcomeLines, Math.Max(latest, 1));
        }

        var unseen = items.Where(i => i.Id > lastSeen).OrderByDescending(i => i.Id).Take(6).Select(i => i.Text).ToList();
        return unseen.Count == 0 ? null : new StartupNotice("NOVEDADES", unseen, latest);
    }
}
