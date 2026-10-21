using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using HysteriaMeter.App.ViewModels;
using Microsoft.Extensions.Hosting;

namespace HysteriaMeter.App.Update;

/// <summary>
/// Busca versiones nuevas del zip portable en los releases de GitHub: a los 2 minutos de abrir el meter y después cada
/// 6 horas. Si hay una, la baja y la deja lista; se instala la próxima vez que se abre. No manda nada: es un GET anónimo
/// a la API pública de GitHub y la descarga del zip. Con "Actualizar solo" apagado no se conecta.
/// </summary>
public sealed class UpdateService(MeterSession session) : BackgroundService
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Tope de cada vuelta (lista de releases, .sha256 y zip juntos).</summary>
    private static readonly TimeSpan CheckLimit = TimeSpan.FromMinutes(15);

    // Sin Timeout propio: el zip pesa decenas de MB y con una conexión lenta se cortaría a la mitad. Cada vuelta lleva el
    // suyo (CheckLimit).
    private static readonly HttpClient Http = CreateClient();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var updater = UpdateState.CreateUpdater();
        UpdateState.Current.Staged = updater.Staged();
        try
        {
            await Task.Delay(FirstCheck, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                // El .exe viejo ya cerró hace rato: sus .old se pueden borrar.
                updater.CleanupLeftovers();
                if (session.Preferences.AutoUpdate && UpdateState.RunningVersion is { } running)
                {
                    await CheckAsync(updater, running, stoppingToken).ConfigureAwait(false);
                }

                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Se cerró el meter.
        }
    }

    private static async Task CheckAsync(Updater updater, ReleaseVersion running, CancellationToken stoppingToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        limit.CancelAfter(CheckLimit);
        try
        {
            var json = await Http.GetStringAsync(ReleaseFeed.ReleasesApi, limit.Token).ConfigureAwait(false);
            if (ReleaseFeed.Newest(json, running) is { } offer && await updater.StageAsync(Http, offer, limit.Token).ConfigureAwait(false))
            {
                UpdateState.Current.Staged = updater.Staged();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
            || (ex is OperationCanceledException && !stoppingToken.IsCancellationRequested))
        {
            // Sin conexión, GitHub caído o una descarga rota: se vuelve a intentar en la próxima vuelta.
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub pide un User-Agent; va solo el nombre y la versión.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HysteriaMeter", UpdateState.RunningVersion?.ToString() ?? "0"));
        return http;
    }
}
