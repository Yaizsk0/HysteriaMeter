using System.IO;
using System.Windows;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.Themes;
using HysteriaMeter.App.Update;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.App.Welcome;
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

        // Reinicio desde la bandeja: espera a que cierre el meter anterior. Después, si hay una versión nueva bajada, se
        // instala y se abre esa, antes de levantar nada.
        var args = AppRestart.WaitForPrevious(e.Args);
        if (InstallStagedUpdate() && AppRestart.Start(args))
        {
            Shutdown();
            return;
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.Configure<CaptureSettings>(builder.Configuration.GetSection("Capture"));
        builder.Services.Configure<MeterSettings>(builder.Configuration.GetSection("Meter"));

        // Placa de red: la elegida en Configuración → Captura le gana a appsettings (Capture:Device) mientras siga
        // conectada. La captura la lee una sola vez, al arrancar: cambiarla pide reiniciar el meter.
        CaptureDevices.Source = CaptureDevices.FromNpcap;
        // Npcap en modo "solo administradores" sin elevar: lo miran el overlay (en el primer corte) y la bienvenida.
        NpcapAccess.Source = NpcapAccess.FromSystem;
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
            // Quien ya tenía preferencias guardadas no es nuevo: ve las novedades al actualizar. Quien llega sin
            // preferencias ve la bienvenida (su propia ventana) y no la lista de novedades de versiones que no usó.
            UserStore.Load<MeterPreferences>(PreferencesFile) is { } saved
                ? saved with { LastSeenNews = Math.Max(saved.LastSeenNews, 1) }
                : new MeterPreferences { WelcomeDone = false, LastSeenNews = NewsFeed.LatestId(NewsFeed.Current) },
            preferences => UserStore.Save(PreferencesFile, preferences),
            sp.GetRequiredService<EncounterRepository>(),
            sp.GetRequiredService<CaptureStatus>(),
            sp.GetRequiredService<HysteriaMeter.Protocol.Decoding.DecoderStats>())
        {
            PlaySound = sound => (sound == AlertSound.Warning ? System.Media.SystemSounds.Exclamation : System.Media.SystemSounds.Asterisk).Play(),
        });
        builder.Services.AddSingleton<OverlayWindows>();
        builder.Services.AddSingleton<TrayIcon>();
        if (UpdateState.IsAvailable)
        {
            builder.Services.AddHostedService<UpdateService>();
        }

        _host = builder.Build();
        await _host.StartAsync().ConfigureAwait(true);

        // Tema visual y fuente del texto: se aplican antes de abrir las ventanas. Al cambiarlos (o al importar un perfil,
        // que avisa "cambió todo"), los ven las referencias DynamicResource (BodyFont, estilos de Theme.xaml) y las
        // ventanas que se abren después; el jugador local toma el acento nuevo en la próxima actualización de las filas.
        var session = _host.Services.GetRequiredService<MeterSession>();
        MeterThemes.Defaults = MeterThemes.Snapshot(Resources);
        ApplyTheme(session.Preferences.Theme);
        Resources["BodyFont"] = MeterViewModel.FontOf(session.Preferences.TextFont);
        Resources[UiScale.ResourceKey] = UiScale.Normalize(session.Preferences.UiScale);
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

            // La escala de la interfaz: las ventanas abiertas y sus popups la siguen por el recurso (Themes/UiScale).
            if (MeterSession.Touches(names, nameof(MeterViewModel.UiScale)))
            {
                Dispatcher.InvokeAsync(() => Resources[UiScale.ResourceKey] = UiScale.Normalize(session.Preferences.UiScale));
            }
        };

        MainWindow = _host.Services.GetRequiredService<OverlayWindows>().OpenSaved();
        // El host lo desecha al salir (saca el icono de la bandeja).
        _host.Services.GetRequiredService<TrayIcon>();
        if (!session.Preferences.WelcomeDone)
        {
            ShowWelcome(session);
        }

        // Limpieza del historial según lo elegido en configuración; si la base falla, se intenta en la próxima sesión.
        try
        {
            await _host.Services.GetRequiredService<MeterSession>().CleanupHistoryAsync(DateTimeOffset.UtcNow).ConfigureAwait(true);
        }
        catch (System.Data.Common.DbException)
        {
        }
    }

    /// <summary>
    /// Instala la versión ya bajada si la hay y "Actualizar solo" sigue prendido (solo el zip portable de los releases).
    /// Devuelve <c>true</c> si la instaló: hay que abrir el .exe nuevo.
    /// </summary>
    private static bool InstallStagedUpdate() =>
        UpdateState.IsAvailable
        && UpdateState.RunningVersion is { } running
        && UserStore.Load<MeterPreferences>(PreferencesFile) is not { AutoUpdate: false }
        && UpdateState.CreateUpdater().ApplyStaged(running);

    /// <summary>
    /// Bienvenida del primer arranque, encima del overlay. La captura ya corre (MeterPipeline arrancó con el host): la
    /// ventana solo muestra cómo viene y no frena el resto del arranque.
    /// </summary>
    private void ShowWelcome(MeterSession session)
    {
        // Value ya pasó por el PostConfigure de la placa: Device es la que usa la captura y Applied, la elegida que quedó.
        var capture = _host!.Services.GetRequiredService<IOptions<CaptureSettings>>().Value;
        var setup = CaptureSetup.From(capture, CaptureDevices.Applied, CaptureDevices.Source, NpcapAccess.Source);
        var welcome = new WelcomeViewModel(session, setup, GameDataSummary.Read(AppPaths.In("Juego")));
        new WelcomeWindow(welcome) { Owner = MainWindow }.Show();
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
