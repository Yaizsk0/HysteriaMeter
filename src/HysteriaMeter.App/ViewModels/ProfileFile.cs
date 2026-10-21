using System.Text.Json;
using System.Text.Json.Serialization;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Perfil de configuración para pasar a otra PC o compartir con la legión (como los perfiles de Details!).
/// El webhook de Discord no se exporta: es una URL privada del canal. La placa de red y la escala de la interfaz
/// tampoco: son de cada PC (la escala depende del monitor).
/// </summary>
public static class ProfileFile
{
    public const string FileName = "perfil-hysteria.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string ToJson(MeterPreferences preferences) =>
        JsonSerializer.Serialize(preferences with { DiscordWebhook = string.Empty, DiscordOnKill = false, CaptureDevice = string.Empty, UiScale = 1.0 }, Json);

    /// <summary>
    /// Las preferencias del archivo, conservando el webhook, la placa, la escala, la actualización automática y el estado
    /// de bienvenida/novedades propios; null si el archivo no es un perfil válido.
    /// </summary>
    public static MeterPreferences? FromJson(string json, MeterPreferences current)
    {
        try
        {
            return JsonSerializer.Deserialize<MeterPreferences>(json, Json) is { } imported
                ? imported with
                {
                    CaptureDevice = current.CaptureDevice,
                    DiscordWebhook = current.DiscordWebhook,
                    DiscordOnKill = current.DiscordOnKill,
                    WelcomeDone = current.WelcomeDone,
                    LastSeenNews = current.LastSeenNews,
                    UiScale = current.UiScale,
                    AutoUpdate = current.AutoUpdate,
                    // Un perfil viejo no vuelve a disparar el paso a la vista del rediseño en el próximo arranque.
                    RedesignViewApplied = current.RedesignViewApplied,
                }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
