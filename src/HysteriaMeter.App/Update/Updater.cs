using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace HysteriaMeter.App.Update;

/// <summary>
/// Baja, deja lista e instala una versión nueva del zip portable. Trabaja en <paramref name="stagingFolder"/>
/// (<c>datos\actualizacion</c>): <c>nuevo\</c> tiene el zip descomprimido, <c>lista.txt</c> su versión (se escribe al
/// final, cuando ya está todo) y <c>viejos.txt</c> los archivos reemplazados que falta borrar.
/// </summary>
public sealed class Updater(string programFolder, string stagingFolder)
{
    /// <summary>Tope del zip: el de verdad pesa decenas de MB.</summary>
    public const long MaxZipBytes = 300L * 1024 * 1024;

    public const string ExeName = "HysteriaMeter.exe";

    private const string NewFolder = "nuevo";
    private const string ReadyFile = "lista.txt";
    private const string LeftoversFile = "viejos.txt";
    private const string DownloadFile = "descarga.zip";
    private const string OldSuffix = ".old";

    /// <summary>La versión bajada y lista para instalar al abrir el meter, o <c>null</c>.</summary>
    public ReleaseVersion? Staged()
    {
        try
        {
            var ready = Path.Combine(stagingFolder, ReadyFile);
            return File.Exists(ready) && Directory.Exists(Path.Combine(stagingFolder, NewFolder))
                ? ReleaseVersion.Parse(File.ReadAllText(ready))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Baja el zip de <paramref name="offer"/>, lo compara con su .sha256 y lo descomprime en <c>nuevo\</c>. Devuelve
    /// <c>true</c> si quedó lista; <c>false</c> si ya estaba esa versión (o una más nueva), si el zip es demasiado grande,
    /// si el hash no coincide o si le falta el .exe. Los errores de red o de disco salen como excepción.
    /// </summary>
    public async Task<bool> StageAsync(HttpClient http, UpdateOffer offer, CancellationToken cancellationToken)
    {
        if ((Staged() is { } staged && !offer.Version.IsNewerThan(staged)) || offer.ZipSize > MaxZipBytes)
        {
            return false;
        }

        DiscardStaged();
        Directory.CreateDirectory(stagingFolder);
        var expected = ParseChecksum(await http.GetStringAsync(offer.Checksum, cancellationToken).ConfigureAwait(false));
        if (expected is null)
        {
            return false;
        }

        var download = Path.Combine(stagingFolder, DownloadFile);
        try
        {
            using (var response = await http.GetAsync(offer.Zip, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxZipBytes)
                {
                    return false;
                }

                var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (source.ConfigureAwait(false))
                {
                    var file = new FileStream(download, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await using (file.ConfigureAwait(false))
                    {
                        await CopyLimitedAsync(source, file, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (!string.Equals(await HashAsync(download, cancellationToken).ConfigureAwait(false), expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var target = Path.Combine(stagingFolder, NewFolder);
            ZipFile.ExtractToDirectory(download, target);
            if (!File.Exists(Path.Combine(target, ExeName)))
            {
                DiscardStaged();
                return false;
            }

            // Al final: si se corta antes, no queda nada a medio bajar marcado como listo.
            await File.WriteAllTextAsync(Path.Combine(stagingFolder, ReadyFile), offer.Version.ToString(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            TryDelete(download);
        }
    }

    /// <summary>
    /// Instala la versión lista si es más nueva que <paramref name="current"/>. Cada archivo del programa que cambia pasa
    /// a ".old" (Windows deja renombrar el .exe que está corriendo) y el nuevo ocupa su lugar. No toca la carpeta
    /// <c>datos</c> ni <c>portable.txt</c>. Si algo falla, deshace lo hecho y la deja para el próximo arranque. Devuelve
    /// <c>true</c> si la instaló: hay que abrir el .exe de nuevo para usarla.
    /// </summary>
    public bool ApplyStaged(ReleaseVersion current)
    {
        if (Staged() is not { } staged)
        {
            return false;
        }

        if (!staged.IsNewerThan(current))
        {
            DiscardStaged();
            return false;
        }

        var source = Path.Combine(stagingFolder, NewFolder);
        var done = new List<Replacement>();
        try
        {
            // La lista entera antes de mover: en FAT o exFAT (un pendrive), enumerar mientras se sacan archivos puede
            // saltear alguno sin error y dejar una instalación mezclada.
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.GetFullPath(Path.Combine(programFolder, relative));
                if (IsUserData(relative) || !IsInside(target))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? old = null;
                if (File.Exists(target))
                {
                    old = target + OldSuffix;
                    // Un .old de otra vez que no se pudo borrar: si sigue en uso, esto falla y se deshace todo.
                    File.Delete(old);
                    File.Move(target, old);
                }

                var replacement = new Replacement(file, target, old);
                done.Add(replacement);
                File.Move(file, target);
                replacement.Moved = true;
            }
        }
        catch (Exception)
        {
            // Cualquier error, no solo los de disco: corre al abrir el meter y una instalación a medias lo dejaría sin
            // arrancar. Se deshace y se reintenta en el próximo arranque.
            Undo(done);
            return false;
        }

        AddLeftovers(done.Where(r => r.Old is not null).Select(r => Path.GetRelativePath(programFolder, r.Old!)));
        DiscardStaged();
        return true;
    }

    /// <summary>
    /// Borra los ".old" que dejó la última instalación. Los que siguen en uso (el .exe viejo, si todavía no cerró)
    /// quedan anotados para la próxima vez.
    /// </summary>
    public void CleanupLeftovers()
    {
        var list = Path.Combine(stagingFolder, LeftoversFile);
        string[] lines;
        try
        {
            if (!File.Exists(list))
            {
                return;
            }

            lines = File.ReadAllLines(list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var remaining = new List<string>();
        foreach (var line in lines.Where(l => l.Length > 0))
        {
            var path = Path.GetFullPath(Path.Combine(programFolder, line));
            if (!IsInside(path) || !path.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                remaining.Add(line);
            }
        }

        try
        {
            if (remaining.Count == 0)
            {
                File.Delete(list);
            }
            else
            {
                File.WriteAllLines(list, remaining);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Se vuelve a intentar en la próxima limpieza.
        }
    }

    /// <summary>El hash del archivo .sha256 ("&lt;hash&gt;  &lt;archivo&gt;", como sha256sum), o <c>null</c> si no tiene uno.</summary>
    public static string? ParseChecksum(string text)
    {
        var first = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(char.IsAsciiHexDigit) ? first : null;
    }

    /// <summary>Lo del usuario no viene en el zip y nunca se pisa: la carpeta datos y la marca del modo portable.</summary>
    private static bool IsUserData(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return string.Equals(first, "datos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relative, AppPaths.PortableMarker, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInside(string path) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(programFolder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>De atrás para adelante: el nuevo vuelve a <c>nuevo\</c> (para reintentar entero) y el viejo a su lugar.</summary>
    private static void Undo(List<Replacement> done)
    {
        for (var i = done.Count - 1; i >= 0; i--)
        {
            var replacement = done[i];
            try
            {
                if (replacement.Moved)
                {
                    File.Move(replacement.Target, replacement.Source);
                }

                if (replacement.Old is { } old)
                {
                    File.Move(old, replacement.Target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Se sigue con los demás: lo que no se pudo devolver queda como .old o en nuevo\.
            }
        }
    }

    private void AddLeftovers(IEnumerable<string> relativePaths)
    {
        var list = Path.Combine(stagingFolder, LeftoversFile);
        try
        {
            var previous = File.Exists(list) ? File.ReadAllLines(list) : [];
            File.WriteAllLines(list, previous.Concat(relativePaths).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin la lista, los .old quedan en la carpeta: no molestan.
        }
    }

    /// <summary>Saca lo bajado a medias o ya instalado. <c>viejos.txt</c> queda: todavía hay que borrar esos archivos.</summary>
    private void DiscardStaged()
    {
        try
        {
            var folder = Path.Combine(stagingFolder, NewFolder);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin lista.txt no se instala: lo que quede se borra en el próximo intento.
        }

        TryDelete(Path.Combine(stagingFolder, ReadyFile));
        TryDelete(Path.Combine(stagingFolder, DownloadFile));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Se vuelve a intentar la próxima vez.
        }
    }

    private static async Task CopyLimitedAsync(Stream source, Stream target, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxZipBytes)
            {
                throw new InvalidDataException("El zip de la actualización es demasiado grande.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        var file = File.OpenRead(path);
        await using (file.ConfigureAwait(false))
        {
            return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        }
    }

    private sealed class Replacement(string source, string target, string? old)
    {
        public string Source { get; } = source;

        public string Target { get; } = target;

        public string? Old { get; } = old;

        public bool Moved { get; set; }
    }
}
