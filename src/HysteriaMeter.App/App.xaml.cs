using System.IO;
using System.Windows;
using HysteriaMeter.App.Config;
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

        // Placa de red: la elegida en Configuración → Captura le gana a appsettings (Capture:Device) mientras siga
        // conectada. La captura la lee una sola vez, al arrancar: cambiarla pide reiniciar el meter.
        CaptureDevices.Source = CaptureDevices.FromNpcap;
        builder.Services.AddOptions<CaptureSettings>().PostConfigure<MeterSession>((settings, session) =>
        {
            var saved = session.Preferences.CaptureDevice;
            if (settings.Mode == CaptureMode.Live && saved.Length > 0)
            {
                settings.Device = CaptureDevices.Resolve(saved, settings.Device, CaptureDevices.FromNpcap());
            }

            CaptureDevices.Applied = CaptureDevices.AppliedChoice(saved, settings.Mode, settings.Device);
        });

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

        // Tema visual y fuente del texto: se aplican antes de abrir las ventanas. Al cambiarlos (o al importar un perfil,
        // que avisa "cambió todo"), los ven las referencias DynamicResource (BodyFont, estilos de Theme.xaml) y las
        // ventanas que se abren después; el jugador local toma el acento nuevo en la próxima actualización de las filas.
        var session = _host.Services.GetRequiredService<MeterSession>();
        MeterThemes.Defaults = MeterThemes.Snapshot(Resources);
        ApplyTheme(session.Preferences.Theme);
        Resources["BodyFont"] = MeterViewModel.FontOf(session.Preferences.TextFont);
        session.PreferencesChanged += names =>
        {
            if (MeterSession.Touches(names, nameof(MeterViewModel.SelectedTheme)))
            {
                Dispatcher.InvokeAsync(() => ApplyTheme(session.Preferences.Theme));
            }

            if (MeterSession.Touches(names, nameof(MeterViewModel.FontName)))
            {
                Dispatcher.InvokeAsync(() => Resources["BodyFont"] = MeterViewModel.FontOf(session.Preferences.TextFont));
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

    private void ApplyTheme(string? name)
    {
        var palette = MeterThemes.Apply(Resources, name, MeterThemes.Defaults);
        if (palette.TryGetValue("Gold", out var accent))
        {
            PlayerColors.UseAccent(accent);
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
