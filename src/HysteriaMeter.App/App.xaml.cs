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

        PlayerClasses.Load(Path.Combine(AppContext.BaseDirectory, "data", "classes.json"));
        PlayerClasses.EmblemFolder = AppPaths.In("Juego", "clases");
        if (Environment.ProcessPath is { } exe)
        {
            try
            {
                Autostart.Refresh(exe);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Sin permiso para el registro: el inicio con Windows queda como estaba.
            }
        }

        NewsFeed.Load(Path.Combine(AppContext.BaseDirectory, "data", "novedades.json"));
        builder.Services.AddSingleton(sp => new CombatEngine(new CombatEngineOptions
        {
            IdleTimeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<MeterSettings>>().Value.IdleTimeoutSeconds),
        })
        {
            Entities = { Npcs = NpcTable.Load(AppPaths.In("Juego", "npcs.json"), Path.Combine(AppContext.BaseDirectory, "data", "npcs.json")) },
        });
        builder.Services.AddSingleton(sp => new EncounterRepository(
            AppPaths.Database(sp.GetRequiredService<IOptions<MeterSettings>>().Value.DatabasePath)));
        builder.Services.AddSingleton<CaptureRecorder>();
        builder.Services.AddSingleton<CaptureStatus>();
        builder.Services.AddSingleton<HysteriaMeter.Protocol.Decoding.DecoderStats>();
        builder.Services.AddHostedService<MeterPipeline>();
        builder.Services.AddSingleton(sp => new MeterSession(
            sp.GetRequiredService<CombatEngine>(),
            sp.GetRequiredService<CaptureRecorder>(),
            SkillNames.Load(AppPaths.In("Juego", "skills.json"), Path.Combine(AppContext.BaseDirectory, "data", "skills.json")).WithIcons(AppPaths.In("Juego", "iconos")),
            // Quien ya tenía preferencias guardadas no es nuevo: ve las novedades en vez de la bienvenida.
            UserStore.Load<MeterPreferences>(PreferencesFile) is { } saved ? saved with { LastSeenNews = Math.Max(saved.LastSeenNews, 1) } : new MeterPreferences(),
            preferences => UserStore.Save(PreferencesFile, preferences),
            sp.GetRequiredService<EncounterRepository>(),
            sp.GetRequiredService<CaptureStatus>(),
            sp.GetRequiredService<HysteriaMeter.Protocol.Decoding.DecoderStats>())
        {
            PlaySound = sound => (sound == AlertSound.Warning ? System.Media.SystemSounds.Exclamation : System.Media.SystemSounds.Asterisk).Play(),
        });
        builder.Services.AddSingleton<OverlayWindows>();
        builder.Services.AddSingleton<TrayIcon>();

        _host = builder.Build();
        await _host.StartAsync().ConfigureAwait(true);

        // Tema visual: se aplica antes de abrir las ventanas; al cambiarlo, se actualizan los colores que se pueden.
        var session = _host.Services.GetRequiredService<MeterSession>();
        var themeDefaults = MeterThemes.Snapshot(Resources);
        MeterThemes.Apply(Resources, session.Preferences.Theme, themeDefaults);
        session.PreferencesChanged += names =>
        {
            if (names.Contains(nameof(MeterViewModel.SelectedTheme)))
            {
                Dispatcher.InvokeAsync(() => MeterThemes.Apply(Resources, session.Preferences.Theme, themeDefaults));
            }
        };

        MainWindow = _host.Services.GetRequiredService<OverlayWindows>().OpenSaved();
        // El host lo desecha al salir (saca el icono de la bandeja).
        _host.Services.GetRequiredService<TrayIcon>();

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
