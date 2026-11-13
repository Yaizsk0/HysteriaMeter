using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.App.ViewModels;
using Microsoft.Extensions.Hosting;

namespace HysteriaMeter.App.Icons;

/// <summary>
/// Al abrir el meter, baja de MetaBot.GG los íconos y emblemas que falten en <c>Juego</c> (<see cref="IconDownloader"/>).
/// Una vuelta por arranque; con "Bajar íconos de MetaBot.GG" apagado no se conecta. No manda nada: son GET anónimos a
/// las imágenes públicas del sitio.
/// </summary>
public sealed class IconService(MeterSession session) : BackgroundService
{
    /// <summary>Unos segundos para que primero arranquen la captura y las ventanas.</summary>
    private static readonly TimeSpan FirstRun = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRun, stoppingToken).ConfigureAwait(false);
            if (!session.Preferences.DownloadIcons)
            {
                return;
            }

            var manifest = IconManifest.Load(Path.Combine(AppContext.BaseDirectory, "data", "iconos-metabot.json"));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HysteriaMeter", Update.UpdateState.RunningVersion?.ToString() ?? "0"));
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/Yaizsk0/HysteriaMeter)"));
            var downloader = new IconDownloader(http, IconDownloader.WebpToPng);
            IconState.Current.Running = true;
            var result = await downloader.RunAsync(manifest, AppPaths.In("Juego"), DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
            IconState.Current.Last = result;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Se cerró el meter: lo que falte se baja la próxima vez.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disco lleno o carpeta sin permiso: el meter sigue con lo que tenga. Ningún error de acá puede terminar el
            // servicio: el host se pararía y con él la captura.
        }
        finally
        {
            IconState.Current.Running = false;
        }
    }
}

/// <summary>Cómo fue la descarga de íconos de este arranque, para Configuración → Datos del juego.</summary>
public sealed partial class IconState : ObservableObject
{
    public static IconState Current { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool Running { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial IconResult? Last { get; set; }

    /// <summary>Vacío si todavía no corrió (o está apagado).</summary>
    public string StatusText => Describe(Running, Last);

    public static string Describe(bool running, IconResult? last) => (running, last) switch
    {
        (true, _) => "Bajando íconos de MetaBot.GG…",
        (_, { Outcome: IconOutcome.CodecMissing }) =>
            "Windows no puede abrir las imágenes WebP: instalá «Extensión de imagen WebP» desde Microsoft Store y volvé a abrir el meter.",
        (_, { Outcome: IconOutcome.Offline } r) =>
            $"No se pudo bajar de MetaBot.GG: sin conexión o el sitio no respondió ({Images(r.Total)} en disco). Se reintenta al volver a abrir el meter.",
        (_, { Outcome: IconOutcome.Blocked } r) =>
            $"MetaBot.GG rechazó la descarga ({Images(r.Total)} en disco). Se reintenta al volver a abrir el meter.",
        (_, { Outcome: IconOutcome.Done } r) => $"Se bajaron {Images(r.Downloaded)} de MetaBot.GG ({r.Total} en disco).{Missing(r)}",
        (_, { Outcome: IconOutcome.UpToDate } r) => $"Íconos al día ({Images(r.Total)} en disco).{Missing(r)}",
        _ => string.Empty,
    };

    /// <summary>Íconos de skills y emblemas de clase juntos.</summary>
    private static string Images(int count) => count == 1 ? "1 imagen" : $"{count} imágenes";

    private static string Missing(IconResult result) => result.NotFound switch
    {
        0 => string.Empty,
        1 => " MetaBot no tiene 1: se vuelve a probar en una semana.",
        var n => $" MetaBot no tiene {n}: se vuelven a probar en una semana.",
    };
}
