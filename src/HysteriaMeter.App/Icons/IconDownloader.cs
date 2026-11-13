using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace HysteriaMeter.App.Icons;

/// <summary>Cómo terminó una vuelta de <see cref="IconDownloader.RunAsync"/>.</summary>
public enum IconOutcome
{
    /// <summary>No se bajó nada: ya estaban todos o los que faltan no existen en MetaBot (se vuelven a probar en una semana).</summary>
    UpToDate,

    /// <summary>Se bajó al menos uno de los que faltaban.</summary>
    Done,

    /// <summary>Sin conexión, o MetaBot respondió con un error del servidor o un límite: se sigue al volver a abrir el meter.</summary>
    Offline,

    /// <summary>
    /// MetaBot rechazó varios pedidos seguidos sin dar ninguna imagen (403, una página en vez del WebP): parece un bloqueo
    /// y no íconos que faltan. Se corta sin anotarlos como faltantes y se reintenta al volver a abrir el meter.
    /// </summary>
    Blocked,

    /// <summary>Windows no tiene el códec WebP (la "Extensión de imagen WebP" de Microsoft Store).</summary>
    CodecMissing,
}

/// <param name="Downloaded">Íconos y emblemas nuevos en esta vuelta.</param>
/// <param name="NotFound">Los que MetaBot no tiene (404, 403 o una imagen que no se abre): no se piden de nuevo por una semana.</param>
/// <param name="Total">Cuántos hay en disco al terminar, de los del manifiesto.</param>
public sealed record IconResult(IconOutcome Outcome, int Downloaded, int NotFound, int Total);

/// <summary>
/// Baja de MetaBot.GG los íconos de skills y los emblemas de clase que faltan en la carpeta <c>Juego</c> de esta PC,
/// uno por vez y con una pausa entre pedidos (los términos de MetaBot piden un ritmo razonable). Convierte cada WebP
/// a <c>&lt;id&gt;.png</c>, que es lo que leen <see cref="ViewModels.SkillNames"/> y <see cref="ViewModels.PlayerClasses"/>.
/// No pisa archivos que ya están (los puestos a mano ganan) y deja un <c>FUENTE.txt</c> con la atribución.
/// </summary>
/// <param name="toPng">Convierte los bytes WebP en PNG; los tests pasan uno propio para no depender del códec.</param>
public sealed class IconDownloader(HttpClient http, Func<byte[], byte[]> toPng)
{
    /// <summary>Un 404 no se vuelve a pedir hasta que pase este tiempo: MetaBot puede sumar el ícono después.</summary>
    public static readonly TimeSpan RetryNotFoundAfter = TimeSpan.FromDays(7);

    /// <summary>Tantos rechazos seguidos sin ninguna imagen en el medio se toman como un bloqueo (<see cref="IconOutcome.Blocked"/>).</summary>
    public const int BlockedAfter = 5;

    /// <summary>
    /// WebP de 1x1 (sin pérdida): si Windows no lo abre, falta el códec y no se pide nada. Así un error al abrir un ícono
    /// bajado es siempre una imagen rota y no el códec.
    /// </summary>
    public static byte[] Probe() => Convert.FromBase64String("UklGRh4AAABXRUJQVlA4TBEAAAAvAAAAAAdQiirUo/+BiOh/AAA=");

    /// <summary>Pausa entre un pedido y el siguiente.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Se llama después de guardar cada ícono, para que el meter lo muestre sin esperar al final.</summary>
    public Action? Saved { get; init; } = ViewModels.SkillNames.IconsChanged;

    public const string SkillCredit = "Íconos de skills de AION 2 vía MetaBot.GG, " + IconManifest.SkillsPage
        + " (atribución según sus términos). Los bajó Hysteria Meter en esta PC; son de NCSOFT: no los subas ni los publiques.";

    public const string ClassCredit = "Emblemas de clase de AION 2 vía MetaBot.GG, " + IconManifest.ClassesPage
        + " (atribución según sus términos). Los bajó Hysteria Meter en esta PC; son de NCSOFT: no los subas ni los publiques.";

    public async Task<IconResult> RunAsync(IconManifest manifest, string gameFolder, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var skillFolder = Path.Combine(gameFolder, "iconos");
        var classFolder = Path.Combine(gameFolder, "clases");
        var statePath = Path.Combine(gameFolder, "metabot.json");
        var notFound = LoadState(statePath);
        var jobs = manifest.Skills
            .Select(id => new Job($"skill:{id}", Path.Combine(skillFolder, $"{id}.png"), IconManifest.SkillUrl(id), skillFolder))
            .Concat(manifest.Classes.Select(c => new Job($"clase:{c.Key}", Path.Combine(classFolder, $"{c.Key}.png"), IconManifest.ClassUrl(c.Value), classFolder)))
            .ToList();
        var pending = jobs
            .Where(j => !File.Exists(j.Target) && !(notFound.TryGetValue(j.Key, out var when) && now - when < RetryNotFoundAfter))
            .ToList();

        if (pending.Count > 0 && !CanDecode())
        {
            return new IconResult(IconOutcome.CodecMissing, 0, 0, jobs.Count(j => File.Exists(j.Target)));
        }

        var outcome = IconOutcome.UpToDate;
        var downloaded = 0;
        var missing = 0;
        var streak = new List<string>();
        void Reject(string key)
        {
            notFound[key] = now;
            missing++;
            streak.Add(key);
        }

        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                if (i > 0)
                {
                    await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
                }

                if (streak.Count >= BlockedAfter)
                {
                    break;
                }

                var job = pending[i];
                using var response = await http.GetAsync(job.Url, cancellationToken).ConfigureAwait(false);
                // MetaBot sirve las imágenes desde un almacenamiento que contesta 403 a lo que no existe.
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden)
                {
                    Reject(job.Key);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var webp = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (!IsWebp(webp))
                {
                    // Un 200 con otra cosa (una página de error o de desafío): como si no existiera.
                    Reject(job.Key);
                    continue;
                }

                byte[] png;
                try
                {
                    png = toPng(webp);
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
                {
                    // El códec ya se probó con Probe: es una imagen rota o una variante que Windows no abre. Se vuelve a
                    // probar en una semana.
                    Reject(job.Key);
                    continue;
                }

                Directory.CreateDirectory(job.Folder);
                var temporary = job.Target + ".tmp";
                await File.WriteAllBytesAsync(temporary, png, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, job.Target, overwrite: true);
                notFound.Remove(job.Key);
                streak.Clear();
                downloaded++;
                outcome = IconOutcome.Done;
                WriteCredit(job.Folder, job.Folder == skillFolder ? SkillCredit : ClassCredit);
                Saved?.Invoke();
            }

            if (streak.Count >= BlockedAfter)
            {
                // Varios rechazos seguidos (también los últimos): no son íconos que faltan sino un bloqueo. No se anotan.
                foreach (var key in streak)
                {
                    notFound.Remove(key);
                }

                missing -= streak.Count;
                outcome = IconOutcome.Blocked;
            }
        }
        catch (HttpRequestException)
        {
            outcome = IconOutcome.Offline;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // El HttpClient venció su tiempo de espera: MetaBot no respondió.
            outcome = IconOutcome.Offline;
        }
        finally
        {
            SaveState(statePath, notFound);
        }

        return new IconResult(outcome, downloaded, missing, jobs.Count(j => File.Exists(j.Target)));
    }

    /// <summary>WebP → PNG con el decodificador de Windows (WIC). Sin el códec WebP instalado lanza <see cref="NotSupportedException"/>.</summary>
    public static byte[] WebpToPng(byte[] data)
    {
        using var input = new MemoryStream(data);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private bool CanDecode()
    {
        try
        {
            toPng(Probe());
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>La firma de un WebP: "RIFF", el tamaño y "WEBP".</summary>
    public static bool IsWebp(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8);

    private static void WriteCredit(string folder, string text)
    {
        var path = Path.Combine(folder, "FUENTE.txt");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, text + Environment.NewLine);
        }
    }

    /// <summary><c>Juego\metabot.json</c>: los que MetaBot no tenía y cuándo se pidieron.</summary>
    private static Dictionary<string, DateTimeOffset> LoadState(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(path))?.NotFound ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static void SaveState(string path, Dictionary<string, DateTimeOffset> notFound)
    {
        try
        {
            if (notFound.Count == 0)
            {
                File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new State { NotFound = notFound }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin poder guardar, la próxima vez se vuelven a pedir: no rompe nada.
        }
    }

    private sealed record Job(string Key, string Target, Uri Url, string Folder);

    private sealed class State
    {
        public Dictionary<string, DateTimeOffset> NotFound { get; set; } = [];
    }
}
