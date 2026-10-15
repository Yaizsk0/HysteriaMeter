using System.IO;
using System.Windows;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HysteriaMeter.App;

public partial class App : Application
{
    private const string PreferencesFile = "preferencias.json";

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.Configure<CaptureSettings>(builder.Configuration.GetSection("Capture"));
        builder.Services.Configure<MeterSettings>(builder.Configuration.GetSection("Meter"));

        builder.Services.AddSingleton(sp => new CombatEngine(new CombatEngineOptions
        {
            IdleTimeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<MeterSettings>>().Value.IdleTimeoutSeconds),
        }));
        builder.Services.AddSingleton(sp => new EncounterRepository(
            AppPaths.Database(sp.GetRequiredService<IOptions<MeterSettings>>().Value.DatabasePath)));
        builder.Services.AddSingleton<CaptureRecorder>();
        builder.Services.AddSingleton<CaptureStatus>();
        builder.Services.AddHostedService<MeterPipeline>();
        builder.Services.AddSingleton(sp => new MeterSession(
            sp.GetRequiredService<CombatEngine>(),
            sp.GetRequiredService<CaptureRecorder>(),
            SkillNames.Load(Path.Combine(AppContext.BaseDirectory, "data", "skills.json")),
            UserStore.Load<MeterPreferences>(PreferencesFile) ?? new MeterPreferences(),
            preferences => UserStore.Save(PreferencesFile, preferences),
            sp.GetRequiredService<EncounterRepository>(),
            sp.GetRequiredService<CaptureStatus>()));
        builder.Services.AddSingleton<OverlayWindows>();

        _host = builder.Build();
        await _host.StartAsync().ConfigureAwait(true);

        MainWindow = _host.Services.GetRequiredService<OverlayWindows>().OpenSaved();

        // Limpieza del historial según lo elegido en configuración; si la base falla, se intenta en la próxima sesión.
        try
        {
            await _host.Services.GetRequiredService<MeterSession>().CleanupHistoryAsync(DateTimeOffset.UtcNow).ConfigureAwait(true);
        }
        catch (System.Data.Common.DbException)
        {
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
