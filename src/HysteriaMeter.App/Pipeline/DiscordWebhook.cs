using System.Net.Http;
using System.Net.Http.Json;

namespace HysteriaMeter.App.Pipeline;

/// <summary>
/// Manda el resumen de una pelea a un canal de Discord (por ejemplo, el de la legión). Es lo único que sale de la PC,
/// y solo si el usuario pegó la URL del webhook en configuración.
/// </summary>
public static class DiscordWebhook
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Solo URLs de webhooks de Discord, para no mandar el resumen a cualquier lado por un error de tipeo.</summary>
    public static bool IsValidUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com")
        && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);

    /// <summary>Devuelve <c>null</c> si salió bien, o el motivo del error para mostrarlo.</summary>
    public static async Task<string?> SendAsync(string url, string message, CancellationToken cancellationToken = default)
    {
        if (!IsValidUrl(url))
        {
            return "La URL no es un webhook de Discord";
        }

        try
        {
            // Discord corta los mensajes de más de 2000 caracteres.
            var content = message.Length > 1900 ? message[..1900] + "…" : message;
            using var response = await Http.PostAsJsonAsync(new Uri(url.Trim()), new { content, username = "Hysteria Meter" }, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"Discord respondió {(int)response.StatusCode}";
        }
        catch (HttpRequestException ex)
        {
            return $"No se pudo conectar: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "Discord no respondió a tiempo";
        }
    }
}
