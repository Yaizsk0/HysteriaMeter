using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.FightCard;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.App.Welcome;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;
using DrawingColor = System.Drawing.Color;

namespace HysteriaMeter.App.Tests;

/// <summary>Bienvenida del primer arranque, tarjeta de fin de pelea y menú de la bandeja: la lógica, sin ventanas.</summary>
public sealed class InicioTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Welcome_is_pending_only_for_new_preferences()
    {
        // Mismas opciones que UserStore: un preferencias.json de antes de la bienvenida es de alguien que ya usaba el meter.
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        Assert.True(JsonSerializer.Deserialize<MeterPreferences>("{}", options)!.WelcomeDone);
        Assert.True(new MeterPreferences().WelcomeDone);

        var pending = JsonSerializer.Serialize(new MeterPreferences { WelcomeDone = false }, options);
        Assert.False(JsonSerializer.Deserialize<MeterPreferences>(pending, options)!.WelcomeDone);
    }

    [Fact]
    public void News_latest_id_skips_old_news_for_new_users()
    {
        var news = NewsFeed.Parse("""{ "news": [ { "id": 35, "text": "Vieja" }, { "id": 37, "text": "Nueva" } ] }""");

        Assert.Equal(37, NewsFeed.LatestId(news));
        Assert.Equal(0, NewsFeed.LatestId([]));
        Assert.Null(NewsFeed.NoticeFor(news, NewsFeed.LatestId(news)));
    }

    [Fact]
    public void Capture_status_knows_when_traffic_arrived()
    {
        var status = new CaptureStatus(DateTime.UtcNow);
        Assert.False(status.HasTraffic);
        status.MarkTraffic(DateTime.UtcNow);
        Assert.True(status.HasTraffic);
    }

    [Fact]
    public void Npcap_step_is_ready_or_links_to_the_download()
    {
        var ready = WelcomeViewModel.NpcapStep(installed: true);
        Assert.Equal(("✓", "Listo", true, false), (ready.Mark, ready.State, ready.IsReady, ready.HasAction));

        var missing = WelcomeViewModel.NpcapStep(installed: false);
        Assert.Equal(("1", "Revisar", false), (missing.Mark, missing.State, missing.IsReady));
        Assert.Equal(new SetupAction("Descargar Npcap", WelcomeViewModel.NpcapDownloadUrl, IsFolder: false), missing.Action);
    }

    [Fact]
    public void Npcap_step_warns_about_admin_only_but_stays_ready()
    {
        // Instalado con "solo administradores" y el meter sin elevar: no es un error, pero avisa del pedido de permiso.
        var adminOnly = WelcomeViewModel.NpcapStep(installed: true, adminOnly: true);
        Assert.Equal(("✓", "Npcap instalado", "Listo", true, false), (adminOnly.Mark, adminOnly.Title, adminOnly.State, adminOnly.IsReady, adminOnly.HasAction));
        Assert.Equal(WelcomeViewModel.NpcapAdminOnlyText, adminOnly.Hint);
        Assert.Contains("Restrict Npcap driver's access to Administrators only", adminOnly.Hint, StringComparison.Ordinal);
        Assert.NotEqual(adminOnly.Hint, WelcomeViewModel.NpcapStep(installed: true).Hint);

        // Sin Npcap instalado, lo que falta es Npcap.
        Assert.Equal(WelcomeViewModel.NpcapStep(installed: false), WelcomeViewModel.NpcapStep(installed: false, adminOnly: true));

        // El overlay la muestra con la captura ya cortada, que no se reabre sola: el pedido vuelve al abrir otra vez el meter.
        Assert.Contains("Restrict Npcap driver's access to Administrators only", WelcomeViewModel.NpcapAdminOnlyReopenText, StringComparison.Ordinal);
        Assert.Contains("Volvé a abrir el meter y aceptá ese pedido", WelcomeViewModel.NpcapAdminOnlyReopenText, StringComparison.Ordinal);
        Assert.DoesNotContain("Volvé a abrir", WelcomeViewModel.NpcapAdminOnlyText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, null, false, true)]
    [InlineData(1, null, true, false)]
    [InlineData(null, 1, false, true)] // antes de Npcap 0.93 el valor estaba en Services\npcap
    [InlineData(0, 1, false, false)] // si está en Parameters, manda ese
    [InlineData(0, null, false, false)]
    [InlineData(null, null, false, false)]
    [InlineData("1", null, false, false)] // no es un REG_DWORD
    public void Npcap_admin_only_needs_the_dword_checked_and_no_elevation(object? parameters, object? legacy, bool elevated, bool expected) =>
        Assert.Equal(expected, NpcapAccess.AdminOnlyWithoutElevation(parameters, legacy, elevated));

    [Fact]
    public void Device_step_waits_for_game_traffic_and_shows_failures()
    {
        var automatic = new CaptureSetup(NpcapInstalled: true, Device: null);
        var status = new CaptureStatus(DateTime.UtcNow);

        var waiting = WelcomeViewModel.DeviceStep(automatic, status);
        Assert.Equal(("2", "Esperando tráfico", false), (waiting.Mark, waiting.State, waiting.IsReady));
        Assert.StartsWith("Automática", waiting.Hint, StringComparison.Ordinal);

        status.MarkTraffic(DateTime.UtcNow);
        Assert.True(WelcomeViewModel.DeviceStep(automatic, status).IsReady);
        Assert.StartsWith("Ethernet", WelcomeViewModel.DeviceStep(new CaptureSetup(true, "Ethernet"), status).Hint, StringComparison.Ordinal);

        var failed = new CaptureStatus(DateTime.UtcNow);
        failed.Fail("No se encontraron placas de red.");
        var broken = WelcomeViewModel.DeviceStep(automatic, failed);
        Assert.Equal(("No se encontraron placas de red.", "Revisar", false), (broken.Hint, broken.State, broken.IsReady));

        // Repitiendo un .pcap no hay placa: el paso está listo con el archivo.
        var replay = WelcomeViewModel.DeviceStep(new CaptureSetup(true, null, "pelea.pcap"), failed);
        Assert.True(replay.IsReady);
        Assert.Contains("pelea.pcap", replay.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_step_names_the_device_and_where_it_came_from()
    {
        var status = new CaptureStatus(DateTime.UtcNow);

        // La elegida en Configuración → Captura: la descripción de Npcap, no el \Device\NPF_{GUID}.
        var chosen = new CaptureSetup(true, @"\Device\NPF_{X}", DeviceLabel: "Intel Ethernet", DeviceOrigin: CaptureDeviceOrigin.Preferences);
        Assert.Equal("Intel Ethernet · elegida en la configuración", WelcomeViewModel.DeviceStep(chosen, status).Hint);

        // La de appsettings.json no se eligió en la configuración.
        var configured = WelcomeViewModel.DeviceStep(new CaptureSetup(true, "Intel", DeviceLabel: "Intel Ethernet", DeviceOrigin: CaptureDeviceOrigin.AppSettings), status);
        Assert.Equal("Intel Ethernet · fijada en appsettings.json", configured.Hint);
        Assert.DoesNotContain("elegida en la configuración", configured.Hint, StringComparison.Ordinal);

        // Sin descripción de Npcap queda el nombre, también con tráfico.
        status.MarkTraffic(DateTime.UtcNow);
        var unnamed = new CaptureSetup(true, @"\Device\NPF_{X}", DeviceOrigin: CaptureDeviceOrigin.Preferences);
        Assert.Equal(@"\Device\NPF_{X} · elegida en la configuración", WelcomeViewModel.DeviceStep(unnamed, status).Hint);
    }

    [Fact]
    public void Capture_setup_asks_npcap_only_when_there_is_a_device_to_name()
    {
        var listed = 0;
        IReadOnlyList<(string Name, string Description)> Devices()
        {
            listed++;
            return [(@"\Device\NPF_{X}", "Intel Ethernet")];
        }

        var chosen = CaptureSetup.From(new CaptureSettings { Device = @"\Device\NPF_{X}" }, @"\Device\NPF_{X}", Devices, () => false);
        Assert.Equal(("Intel Ethernet", CaptureDeviceOrigin.Preferences), (chosen.DeviceLabel, chosen.DeviceOrigin));
        Assert.Equal(1, listed);

        // Automática o repitiendo un .pcap no hay placa que nombrar: no se listan las placas.
        Assert.Equal(CaptureDeviceOrigin.Automatic, CaptureSetup.From(new CaptureSettings(), string.Empty, Devices, () => false).DeviceOrigin);
        var replay = CaptureSetup.From(new CaptureSettings { Mode = CaptureMode.Replay, ReplayPath = @"C:\pcap\pelea.pcap", Device = "Intel" }, string.Empty, Devices, () => false);
        Assert.Equal("pelea.pcap", replay.ReplayFile);
        Assert.Equal(1, listed);
    }

    [Fact]
    public void Capture_setup_asks_for_admin_only_only_in_live_mode()
    {
        var asked = 0;
        bool AdminOnly()
        {
            asked++;
            return true;
        }

        static IReadOnlyList<(string Name, string Description)> NoDevices() => [];

        Assert.True(CaptureSetup.From(new CaptureSettings(), string.Empty, NoDevices, AdminOnly).NpcapAdminOnly);
        Assert.Equal(1, asked);
        Assert.False(CaptureSetup.From(new CaptureSettings(), string.Empty, NoDevices, () => false).NpcapAdminOnly);

        // Repitiendo un .pcap no se abre ninguna placa: no hay pedido de permiso que avisar.
        var replay = CaptureSetup.From(new CaptureSettings { Mode = CaptureMode.Replay, ReplayPath = @"C:\pcap\pelea.pcap" }, string.Empty, NoDevices, AdminOnly);
        Assert.False(replay.NpcapAdminOnly);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Welcome_first_step_uses_the_admin_only_flag()
    {
        var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { WelcomeDone = false }, _ => { });
        var data = new GameDataSummary(@"C:\no-existe", Exists: false, HasSkillFile: false, 0, 0, 0);

        var adminOnly = new WelcomeViewModel(session, new CaptureSetup(true, null, NpcapAdminOnly: true), data).Steps[0];
        Assert.Equal((WelcomeViewModel.NpcapAdminOnlyText, true), (adminOnly.Hint, adminOnly.IsReady));
        Assert.Equal(WelcomeViewModel.NpcapStep(installed: true), new WelcomeViewModel(session, new CaptureSetup(true, null), data).Steps[0]);
    }

    [Fact]
    public void Game_data_step_counts_the_files_in_the_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"hysteria-juego-{Guid.NewGuid():N}");
        try
        {
            var empty = WelcomeViewModel.GameDataStep(GameDataSummary.Read(folder));
            Assert.Equal(("Revisar", false), (empty.State, empty.IsReady));
            Assert.Equal(new SetupAction("Abrir carpeta", folder, IsFolder: true), empty.Action);

            Directory.CreateDirectory(Path.Combine(folder, "iconos"));
            Directory.CreateDirectory(Path.Combine(folder, "clases"));
            File.WriteAllText(Path.Combine(folder, "skills.json"), """
                {
                  // como lo deja el extractor: solo cuentan los IDs numéricos con nombre
                  "skills": { "15280000": "Bittercold Wind", "15290000": "Frost Arrow", "x": "no", "16000000": "", },
                }
                """);
            // Solo cuentan los <id>.png que carga la app: fondo.png no es de ninguna skill ni de ninguna clase.
            foreach (var file in new[] { @"iconos\15280000.png", @"iconos\15290000.png", @"iconos\fondo.png", @"clases\1.png", @"clases\fondo.png", @"clases\leeme.txt" })
            {
                File.WriteAllBytes(Path.Combine(folder, file), [0]);
            }

            var data = GameDataSummary.Read(folder);
            Assert.Equal(new GameDataSummary(folder, Exists: true, HasSkillFile: true, Skills: 2, Icons: 2, Emblems: 1), data);
            var step = WelcomeViewModel.GameDataStep(data);
            Assert.Equal(("✓", "Listo", false), (step.Mark, step.State, step.HasAction));
            Assert.EndsWith(": 2 skills, 2 íconos, 1 emblema", step.Hint, StringComparison.Ordinal);

            // Un archivo roto no rompe la bienvenida: hay archivo pero ninguna skill.
            File.WriteAllText(Path.Combine(folder, "skills.json"), "{ roto");
            Assert.Equal(0, GameDataSummary.Read(folder).Skills);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void Game_data_path_hides_the_user_folder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(@"%LOCALAPPDATA%\HysteriaMeter\Juego", WelcomeViewModel.DisplayPath(Path.Combine(local, "HysteriaMeter", "Juego")));
        Assert.Equal(@"D:\Meter\datos\Juego", WelcomeViewModel.DisplayPath(@"D:\Meter\datos\Juego"));
    }

    [Fact]
    public void Welcome_tips_use_the_configured_hotkey()
    {
        Assert.Contains("Ctrl+Alt+P:", Assert.Single(WelcomeViewModel.TipsFor(new MeterPreferences { HotkeyClickThrough = "Ctrl+Alt+P" })), StringComparison.Ordinal);
        Assert.EndsWith("El engranaje abre la configuración.", Assert.Single(WelcomeViewModel.TipsFor(new MeterPreferences { HotkeyClickThrough = string.Empty })), StringComparison.Ordinal);
    }

    [Fact]
    public void Welcome_refreshes_with_traffic_and_start_marks_it_done()
    {
        MeterPreferences? saved = null;
        var status = new CaptureStatus(DateTime.UtcNow);
        var session = new MeterSession(
            new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { WelcomeDone = false }, p => saved = p, status: status);
        var welcome = new WelcomeViewModel(session, new CaptureSetup(true, null), new GameDataSummary(@"C:\no-existe", Exists: false, HasSkillFile: false, 0, 0, 0));
        var closed = 0;
        welcome.CloseRequested += () => closed++;
        var changes = new List<string?>();
        welcome.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.Equal([true, false, false], welcome.Steps.Select(s => s.IsReady));
        welcome.Refresh();
        Assert.Empty(changes); // nada cambió: la lista no se reemplaza y la ventana no repinta

        status.MarkTraffic(DateTime.UtcNow);
        welcome.Refresh();
        Assert.Equal(new string?[] { nameof(WelcomeViewModel.Steps) }, changes);
        Assert.True(welcome.Steps[1].IsReady);

        // "Más tarde" cierra sin marcarla; "Empezar a capturar" la da por vista y guarda.
        welcome.LaterCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.Null(saved);
        welcome.StartCommand.Execute(null);
        Assert.Equal(2, closed);
        Assert.True(saved!.WelcomeDone);
        Assert.True(session.Preferences.WelcomeDone);
    }

    [Fact]
    public void Fight_card_shows_only_on_boss_kills_with_your_damage()
    {
        var me = Player(1, "Yo", local: true, dps: 300);
        Assert.True(FightCardViewModel.CanShow(Kill(T0, me)));
        Assert.False(FightCardViewModel.CanShow(Kill(T0, me) with { TargetKilled = false }));
        Assert.False(FightCardViewModel.CanShow(Kill(T0, Player(2, "Otro", local: false, dps: 300))));
        Assert.False(FightCardViewModel.CanShow(Kill(T0, Player(1, "Yo", local: true, dps: 0))));
    }

    [Fact]
    public void Fight_card_rank_uses_the_same_metric_as_your_dps()
    {
        ActorSnapshot[] actors =
        [
            Player(1, "Yo", local: true, dps: 300, bossDps: 290),
            Player(2, "A", local: false, dps: 500, bossDps: 200),
            Player(3, "B", local: false, dps: 400, bossDps: 350),
            Player(4, "Curandero", local: false, dps: 0, bossDps: 0),
        ];

        Assert.Equal((3, 3), FightCardViewModel.RankOf(actors, a => a.Dps));
        Assert.Equal((2, 3), FightCardViewModel.RankOf(actors, a => a.MainTargetDps));

        var card = new FightCardViewModel(Kill(T0, actors), Session(), bossOnly: true);
        Assert.Equal("2 / 3", card.Rank);
        Assert.Equal(Numbers.Format(290), card.Dps);
        Assert.Equal("BOSS DERROTADO · 4:12", card.Eyebrow);
        Assert.Equal("Tarka", card.Title);
        Assert.Equal(MeterViewModel.ExportFolder, Path.GetDirectoryName(card.CapturePath));
        Assert.EndsWith(" captura.png", card.CapturePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Fight_card_top_skill_adds_up_the_variants()
    {
        var names = new SkillNames(new Dictionary<uint, string> { [15280000] = "Bittercold Wind", [15290000] = "Frost Arrow" });
        var actor = Player(1, "Yo", local: true, dps: 300) with
        {
            Skills =
            [
                new SkillSnapshot(15290000, 400, 4, 0, 100, 0.40),
                new SkillSnapshot(15280143, 350, 4, 0, 100, 0.35),
                new SkillSnapshot(15280144, 250, 4, 0, 100, 0.25),
            ],
        };

        var share = 60.0.ToString("0.0", CultureInfo.CurrentCulture);
        Assert.Equal($"Tu skill top: Bittercold Wind ({share}%)", FightCardViewModel.TopSkillText(actor, names));
        Assert.Null(FightCardViewModel.TopSkillText(actor with { Skills = [] }, names));
    }

    [Fact]
    public void Fight_card_compares_with_your_previous_attempt()
    {
        var first = FightCardViewModel.ComparisonText(300, previous: null, best: null);
        Assert.Null(first.Text);
        Assert.True(first.Better);
        Assert.Equal(
            ($"+20% vs tu intento anterior · ¡récord! (antes {Numbers.Format(250)})", true),
            FightCardViewModel.ComparisonText(300, previous: 250, best: 250));
        Assert.Equal(
            ($"-25% vs tu intento anterior · mejor intento: {Numbers.Format(400)}", false),
            FightCardViewModel.ComparisonText(300, previous: 400, best: 400));
        Assert.Equal(
            ($"Igual que tu intento anterior · mejor intento: {Numbers.Format(500)}", true),
            FightCardViewModel.ComparisonText(300, previous: 300, best: 500));
    }

    [Fact]
    public async Task Fight_card_reads_percentile_and_comparison_from_the_history()
    {
        var database = Path.Combine(Path.GetTempPath(), $"hysteria-tarjeta-{Guid.NewGuid():N}.db");
        try
        {
            var history = new EncounterRepository(database);
            await history.InitializeAsync();
            await history.SaveAsync(Kill(T0, Player(1, "Yo", local: true, dps: 100)));
            await history.SaveAsync(Kill(T0.AddMinutes(5), Player(1, "Otro personaje", local: true, dps: 900)));
            await history.SaveAsync(Kill(T0.AddMinutes(10), Player(1, "Yo", local: true, dps: 200)));
            await history.SaveAsync(Kill(T0.AddMinutes(12), Player(1, "Yo", local: true, dps: 999)) with { TargetName = "Otro boss" });

            var session = Session(history);
            var card = new FightCardViewModel(Kill(T0.AddMinutes(20), Player(1, "Yo", local: true, dps: 150)), session, bossOnly: false);
            await card.LoadAsync();

            // Solo cuentan los intentos de este personaje contra este boss: 100, 200 y la pelea actual (150).
            Assert.Equal($"-25% vs tu intento anterior · mejor intento: {Numbers.Format(200)}", card.Comparison);
            Assert.False(card.IsImprovement);
            Assert.Equal(67, card.Percentile);
            Assert.Equal("Entre tus 3 intentos contra Tarka", card.PercentileHint);

            // Primer intento contra un boss: no hay con qué comparar ni percentil.
            var first = new FightCardViewModel(Kill(T0.AddMinutes(30), Player(1, "Yo", local: true, dps: 150)) with { TargetName = "Boss nuevo" }, session, bossOnly: false);
            await first.LoadAsync();
            Assert.False(first.HasComparison);
            Assert.False(first.HasPercentile);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { database, database + "-wal", database + "-shm" })
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Fight_card_closes_on_reset_until_detached()
    {
        var session = Session();
        var card = new FightCardViewModel(Kill(T0, Player(1, "Yo", local: true, dps: 300)), session, bossOnly: false);
        var closed = 0;
        card.CloseRequested += () => closed++;
        string? saveTo = null;
        card.SaveRequested += path => saveTo = path;

        Assert.False(card.IsStale()); // el motor no tiene otra pelea
        session.Reset();
        Assert.Equal(1, closed);
        card.SaveCaptureCommand.Execute(null);
        Assert.Equal(card.CapturePath, saveTo);

        card.Detach();
        session.Reset();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Fight_card_goes_below_the_overlay_or_above_when_it_does_not_fit()
    {
        var screen = new Rect(0, 0, 1920, 1040);
        var card = new Size(380, 150);

        Assert.Equal(new Point(100, 306), FightCardWindow.Placement(new Rect(100, 100, 356, 200), card, screen));
        Assert.Equal(new Point(100, 644), FightCardWindow.Placement(new Rect(100, 800, 356, 200), card, screen));
        Assert.Equal(new Point(1540, 306), FightCardWindow.Placement(new Rect(1700, 100, 200, 200), card, screen));

        // No entra ni arriba ni abajo: queda pegada al borde de la pantalla, sin salirse.
        Assert.Equal(new Point(0, 0), FightCardWindow.Placement(new Rect(0, 0, 300, 500), new Size(380, 600), new Rect(0, 0, 800, 700)));
    }

    [Fact]
    public void Tray_menu_texts_and_colors()
    {
        Assert.Equal("Ocultar overlay", TrayIcon.VisibilityText(anyVisible: true));
        Assert.Equal("Mostrar overlay", TrayIcon.VisibilityText(anyVisible: false));

        // WinForms no mezcla alfa: el color translúcido del tema se pasa a opaco sobre el fondo del menú.
        Assert.Equal(DrawingColor.FromArgb(128, 128, 128), TrayIcon.Blend(DrawingColor.FromArgb(128, 255, 255, 255), DrawingColor.Black));
        Assert.Equal(DrawingColor.FromArgb(0xE8, 0xB0, 0x4A), TrayIcon.Blend(DrawingColor.FromArgb(0xE8, 0xB0, 0x4A), DrawingColor.Black));
    }

    private static MeterSession Session(EncounterRepository? history = null) =>
        new(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { }, history: history);

    private static ActorSnapshot Player(ulong id, string name, bool local, double dps, double bossDps = 0) =>
        new(id, name, null, local, (long)(dps * 252), dps, 0, 10, 1, 100, 0, [new SkillSnapshot(100, (long)(dps * 252), 10, 1, 100, 1)], MainTargetDps: bossDps);

    /// <summary>Un kill de boss de 4:12 contra "Tarka".</summary>
    private static EncounterSnapshot Kill(DateTimeOffset start, params ActorSnapshot[] actors) =>
        new(Guid.NewGuid(), start, start.AddSeconds(252), "Tarka", TargetKilled: true, actors.Sum(a => a.Damage), actors, TargetIsBoss: true);
}

/// <summary>
/// Las ventanas necesitan la App (sus estilos y recursos) y WPF admite una sola por proceso: esta colección no corre en
/// paralelo con nada, xUnit la deja para cuando terminaron las demás (WindowSmokeTests, que también la crea, incluida).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InicioWindowsGroup
{
    public const string Name = "Ventanas de inicio";
}

/// <summary>Carga la bienvenida y la tarjeta con sus plantillas expandidas, como WindowSmokeTests.</summary>
[Collection(InicioWindowsGroup.Name)]
public sealed class InicioWindowTests
{
    [Fact]
    public void Welcome_and_fight_card_windows_load()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();

            var status = new CaptureStatus(DateTime.UtcNow);
            status.Fail("No se encontraron placas de red.");
            var session = new MeterSession(
                new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { WelcomeDone = false }, _ => { }, status: status);

            // Con todo por revisar (con links) y con todo listo.
            Layout(new WelcomeWindow(new WelcomeViewModel(session, new CaptureSetup(false, null), new GameDataSummary(@"C:\no-existe", Exists: false, HasSkillFile: false, 0, 0, 0))));
            Layout(new WelcomeWindow(new WelcomeViewModel(session, new CaptureSetup(true, null, "pelea.pcap"), new GameDataSummary(@"C:\Juego", Exists: true, HasSkillFile: true, 280, 280, 9))));

            ActorSnapshot me = new(1, "Yo", null, true, 3000, 300, 1, 3, 1, 1500, 0, [new SkillSnapshot(100, 3000, 3, 1, 1500, 1)]);
            var kill = new EncounterSnapshot(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(10), "Tarka", true, 3000, [me], TargetIsBoss: true);
            var full = new FightCardViewModel(kill, session, bossOnly: false)
            {
                Percentile = 80,
                Comparison = "+8% vs tu intento anterior",
                IsImprovement = false,
                SaveMessage = "Imagen guardada",
            };
            var card = new FightCardWindow(full);
            card.PlaceNear(new Rect(100, 100, 356, 240), new Rect(0, 0, 1920, 1040));
            Layout(card);
            Layout(new FightCardWindow(new FightCardViewModel(kill, session, bossOnly: true), clickThrough: true));
        });
    }

    private static void Layout(Window window)
    {
        if (window.Content is FrameworkElement content)
        {
            content.Measure(new Size(800, 600));
            content.Arrange(new Rect(0, 0, 800, 600));
            content.UpdateLayout();
        }

        window.Close();
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
