using System.Text.Json;

namespace HysteriaMeter.App.Update;

/// <summary>Una versión publicada para bajar: el zip portable, su tamaño según GitHub y su .sha256.</summary>
public sealed record UpdateOffer(ReleaseVersion Version, Uri Zip, long ZipSize, Uri Checksum);

/// <summary>
/// Lee la lista de releases de GitHub (<see cref="ReleasesApi"/>) y elige la versión a la que conviene pasar.
/// </summary>
public static class ReleaseFeed
{
    public const string Repository = "Yaizsk0/HysteriaMeter";

    /// <summary>Los assets se bajan solo de los releases de este repo, aunque el JSON diga otra cosa.</summary>
    private const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";

    public static Uri ReleasesApi { get; } = new($"https://api.github.com/repos/{Repository}/releases?per_page=20");

    /// <summary>El nombre que le pone <c>packaging/portable.ps1</c>: "HysteriaMeter-0.16.0-alpha.1-portable-win-x64.zip".</summary>
    public static string ZipName(ReleaseVersion version) => $"HysteriaMeter-{version}-portable-win-x64.zip";

    /// <summary>
    /// La versión más nueva que <paramref name="current"/> con el zip y el .sha256 publicados; <c>null</c> si no hay o si
    /// el JSON no se entiende. Los borradores no cuentan. Quien usa una versión estable no pasa solo a una de prueba;
    /// desde una de prueba se sigue hasta la estable.
    /// </summary>
    public static UpdateOffer? Newest(string json, ReleaseVersion current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            UpdateOffer? best = null;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (Offer(release, current) is { } offer && (best is null || offer.Version.IsNewerThan(best.Version)))
                {
                    best = offer;
                }
            }

            return best;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UpdateOffer? Offer(JsonElement release, ReleaseVersion current)
    {
        if (release.ValueKind != JsonValueKind.Object || IsTrue(release, "draft")
            || ReleaseVersion.Parse(Text(release, "tag_name")) is not { } version || !version.IsNewerThan(current)
            || ((version.IsPrerelease || IsTrue(release, "prerelease")) && !current.IsPrerelease)
            || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var zipName = ZipName(version);
        Uri? zip = null;
        Uri? checksum = null;
        long size = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || DownloadUrl(asset) is not { } url)
            {
                continue;
            }

            var name = Text(asset, "name");
            if (name == zipName)
            {
                zip = url;
                size = asset.TryGetProperty("size", out var bytes) && bytes.TryGetInt64(out var value) ? value : 0;
            }
            else if (name == zipName + ".sha256")
            {
                checksum = url;
            }
        }

        return zip is not null && checksum is not null ? new UpdateOffer(version, zip, size, checksum) : null;
    }

    private static Uri? DownloadUrl(JsonElement asset) =>
        Text(asset, "browser_download_url") is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.AbsoluteUri.StartsWith(DownloadPrefix, StringComparison.Ordinal)
            ? uri
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
