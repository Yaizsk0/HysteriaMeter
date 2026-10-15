using System.Text.Json;
using System.Text.Json.Serialization;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Perfil de configuración para pasar a otra PC o compartir con la legión (como los perfiles de Details!).
/// El webhook de Discord no se exporta: es una URL privada del canal.
/// </summary>
public static class ProfileFile
{
    public const string FileName = "perfil-hysteria.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string ToJson(MeterPreferences preferences) =>
        JsonSerializer.Serialize(preferences with { DiscordWebhook = string.Empty, DiscordOnKill = false }, Json);

    /// <summary>Las preferencias del archivo, conservando el webhook propio; <c>null</c> si el archivo no es un perfil válido.</summary>
    public static MeterPreferences? FromJson(string json, MeterPreferences current)
    {
        try
        {
            return JsonSerializer.Deserialize<MeterPreferences>(json, Json) is { } imported
                ? imported with { DiscordWebhook = current.DiscordWebhook, DiscordOnKill = current.DiscordOnKill }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
