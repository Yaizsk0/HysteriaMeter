using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Carga las ventanas con sus plantillas expandidas: un recurso que falta o un binding mal escrito en XAML
/// compila igual y recién explota al abrir la app, así que esto lo atrapa en el CI.
/// </summary>
public sealed partial class WindowSmokeTests
{
    [Fact]
    public void Overlay_and_detail_windows_load_with_rows()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            // La regresión de Owner muestra ventanas fuera de pantalla; cerrar la última no debe apagar el arnés.
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { });
            var viewModel = new MeterViewModel(session);
            var healing = new MeterViewModel(session, MeterTab.Healing, showHealing: false);
            var row = new ActorRowViewModel();
            row.Update(1, Actor(), MeterTab.Damage, top: 3000, total: 3000, TimeSpan.FromSeconds(10));
            viewModel.Actors.Add(row);
            viewModel.Detail.Select(row);
            viewModel.IsViewingHistory = true;
            viewModel.Detail.Update(new EncounterSnapshot(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(10), "Boss", false, 3000, [Actor()]));

            Layout(new OverlayWindow(viewModel));
            Layout(new OverlayWindow(healing));

            // Las preferencias generales llegan a todas las ventanas; la vista es de cada una.
            viewModel.OverlayOpacity = 0.8;
            Assert.Equal(0.8, healing.OverlayOpacity);
            Assert.True(viewModel.ShowHealingColumn);
            Assert.False(healing.ShowHealingColumn);
            Layout(new DetailWindow(viewModel.Detail));
            Assert.Equal(2, viewModel.Detail.Skills.Count);

            // Modo dungeon: con un boss y también sin ninguno (solo trash).
            var start = DateTimeOffset.UnixEpoch;
            var kill = new EncounterSnapshot(Guid.NewGuid(), start, start.AddSeconds(90), "Kromede", true, 3000, [Actor()], TargetIsBoss: true);
            Layout(new Dungeon.DungeonWindow(new DungeonSummaryViewModel(DungeonSummary.From([kill], start))));
            Layout(new Dungeon.DungeonWindow(new DungeonSummaryViewModel(DungeonSummary.From([], start))));
            viewModel.Detail.Nickname = "El Tanque";
            viewModel.Detail.SaveNicknameCommand.Execute(null);
            Assert.Equal("El Tanque", session.Preferences.Nicknames["Tester"]);

            // Modo demo y compacto: la pelea inventada llena las filas y el overlay chico carga igual.
            viewModel.ShowDemoCommand.Execute(null);
            Assert.Equal(6, viewModel.Actors.Count);
            Assert.True(viewModel.IsViewingHistory);
            viewModel.IsCompact = true;
            Layout(new OverlayWindow(viewModel));

            // Esc: primero cierra el panel abierto y recién después vuelve a la pelea en vivo.
            viewModel.IsSettingsOpen = true;
            viewModel.EscapeCommand.Execute(null);
            Assert.False(viewModel.IsSettingsOpen);
            Assert.True(viewModel.IsViewingHistory);
            viewModel.EscapeCommand.Execute(null);
            Assert.False(viewModel.IsViewingHistory);
            ConfigWindowLoads(viewModel);

            // Fijar posición vale para todas las ventanas y saca la agarradera.
            viewModel.LockPosition = true;
            Assert.False(healing.CanMoveWindow);
            Layout(new OverlayWindow(viewModel));
            viewModel.LockPosition = false;

            // Columnas CRIT y MÁX: se llenan con la pelea en pantalla.
            viewModel.ShowCritColumn = true;
            viewModel.ShowMaxHitColumn = true;
            viewModel.ShowDemoCommand.Execute(null);
            Assert.NotEqual("–", viewModel.Actors[0].Crit);
            Assert.NotEqual("–", viewModel.Actors[0].MaxHitText);
            Layout(new OverlayWindow(viewModel));
            viewModel.ShowCritColumn = false;
            viewModel.ShowMaxHitColumn = false;

            // Sin base de historial, las flechas de peleas no hacen nada (y no rompen).
            viewModel.OlderFightCommand.Execute(null);
            viewModel.NewerFightCommand.Execute(null);
            viewModel.BackToLiveCommand.Execute(null);
            viewModel.IsSessionView = true;
            viewModel.BossOnly = true;
            Assert.False(viewModel.IsFightView);
            viewModel.ShowDemoCommand.Execute(null);

            // Pestaña de daño recibido, detalle con muertes y registro.
            viewModel.IsTakenTab = true;
            Assert.Equal("DTPS", viewModel.RateUnit);
            var tharos = viewModel.Actors.Single(a => a.Name == "Tharos");
            Assert.EndsWith("✝", tharos.DisplayName, StringComparison.Ordinal);
            viewModel.ShowDetailCommand.Execute(tharos);
            Assert.True(viewModel.Detail.HasDeaths);
            viewModel.Detail.CompareWith = viewModel.Detail.Others[0];
            Assert.True(viewModel.Detail.HasComparison);
            Layout(new DetailWindow(viewModel.Detail));
            var demo = DemoEncounter.Create(DateTimeOffset.UnixEpoch.AddHours(1));
            Layout(new HysteriaMeter.App.Registro.LogWindow(new LogViewModel(() => demo.Deaths![0].LastHits, SkillNames.Empty, tharos.Id, "Tharos")));

            // Atajos configurables: se guardan normalizados; una letra sola o uno repetido no.
            viewModel.HotkeyMark = "alt+ctrl+k";
            Assert.Equal("Ctrl+Alt+K", session.Preferences.HotkeyMark);
            viewModel.HotkeyMark = "K";
            Assert.Equal("Ctrl+Alt+K", session.Preferences.HotkeyMark);
            Assert.Contains("no sirve como atajo", viewModel.HotkeyMessage, StringComparison.Ordinal);
            viewModel.HotkeyMark = "Ctrl+Shift+L";
            Assert.Equal("Ctrl+Alt+K", session.Preferences.HotkeyMark);
            Assert.Contains("ya es otro atajo", viewModel.HotkeyMessage, StringComparison.Ordinal);
            viewModel.HotkeyMark = string.Empty;
            Assert.Equal(string.Empty, session.Preferences.HotkeyMark);
            viewModel.Sounds = true;
            Assert.True(session.Preferences.Sounds);
            viewModel.MarkMomentCommand.Execute(null);
            Assert.True(viewModel.HasRecordMessage);

            // Abrir una pelea compartida (JSON de otro Hysteria Meter) la fija en el overlay; un archivo que no es pelea no.
            var shared = Path.Combine(Path.GetTempPath(), $"hysteria-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(shared, HysteriaMeter.Storage.EncounterExport.ToJson(demo));
                viewModel.BackToLiveCommand.Execute(null);
                Assert.True(viewModel.OpenSharedFile(shared));
                Assert.StartsWith("ARCHIVO · ", viewModel.HistoryLabel, StringComparison.Ordinal);
                Assert.True(viewModel.IsViewingHistory);
                File.WriteAllText(shared, "no es una pelea");
                Assert.False(viewModel.OpenSharedFile(shared));
                Assert.Contains("no es una pelea exportada", viewModel.ExportMessage, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(shared);
            }
        });
    }

    [Fact]
    public void Rank_trend_marks_who_moved_for_a_few_seconds()
    {
        var trends = new RankTrends();
        var fight = Guid.NewGuid();
        var t0 = DateTimeOffset.UnixEpoch;
        trends.Begin(fight, 0);
        Assert.Equal(string.Empty, trends.Update(1, 2, t0));
        Assert.Equal(string.Empty, trends.Update(2, 1, t0));
        Assert.Equal("▲", trends.Update(1, 1, t0.AddSeconds(1)));
        Assert.Equal("▼", trends.Update(2, 2, t0.AddSeconds(1)));
        Assert.Equal("▲", trends.Update(1, 1, t0.AddSeconds(2)));
        Assert.Equal(string.Empty, trends.Update(1, 1, t0.AddSeconds(1) + RankTrends.Duration));

        // Otra pelea u otra pestaña: nadie "sube" por el solo hecho de reordenarse.
        trends.Begin(fight, 1);
        Assert.Equal(string.Empty, trends.Update(2, 1, t0.AddSeconds(10)));
    }

    [Fact]
    public void Boss_only_applies_when_the_main_target_took_most_of_the_damage()
    {
        static ActorSnapshot Hitter(long damage, long onMain) =>
            new(1, "Tests", null, true, damage, damage, 1, 1, 0, damage, 0, [], MainTargetDamage: onMain);
        static EncounterSnapshot Fight(bool boss, params ActorSnapshot[] actors) =>
            new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(60), "X", false, 0, actors, TargetIsBoss: boss);

        Assert.True(MeterViewModel.LooksLikeBossFight(Fight(false, Hitter(1000, 900))));
        Assert.False(MeterViewModel.LooksLikeBossFight(Fight(false, Hitter(1000, 300))));
        Assert.True(MeterViewModel.LooksLikeBossFight(Fight(true, Hitter(1000, 300))));
    }

    [Fact]
    public void Version_shows_the_short_commit()
    {
        Assert.Equal("v0.15.0 · a352982", AppVersion.Format("0.15.0+a352982e972d3725ee7c7428b76c584d8d57f3c"));
        Assert.Equal("v0.15.0", AppVersion.Format("0.15.0"));
        Assert.Equal("versión desconocida", AppVersion.Format(null));
        Assert.StartsWith("v0.15.0 · ", AppVersion.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Opacity_is_full_on_hover_and_faded_without_combat()
    {
        var quiet = MeterViewModel.FadeAfter + TimeSpan.FromSeconds(1);
        Assert.Equal(0.6, MeterViewModel.ComputeOpacity(0.6, hovered: false, opaqueOnHover: true, fadeWhenIdle: false, quiet));
        Assert.Equal(1, MeterViewModel.ComputeOpacity(0.6, hovered: true, opaqueOnHover: true, fadeWhenIdle: true, quiet));
        Assert.Equal(0.6, MeterViewModel.ComputeOpacity(0.6, hovered: true, opaqueOnHover: false, fadeWhenIdle: false, quiet));
        Assert.Equal(MeterViewModel.FadedOpacity, MeterViewModel.ComputeOpacity(0.6, hovered: false, opaqueOnHover: true, fadeWhenIdle: true, quiet));
        Assert.Equal(0.6, MeterViewModel.ComputeOpacity(0.6, hovered: false, opaqueOnHover: true, fadeWhenIdle: true, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Capture_status_warns_when_the_game_is_silent()
    {
        var start = new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);
        var status = new CaptureStatus(start);

        Assert.Equal(CaptureState.Starting, status.Current(start.AddSeconds(5)));
        Assert.Equal(CaptureState.NoTraffic, status.Current(start.AddSeconds(20)));

        status.MarkTraffic(start.AddSeconds(21));
        Assert.Equal(CaptureState.Capturing, status.Current(start.AddSeconds(30)));
        Assert.Equal(CaptureState.NoTraffic, status.Current(start.AddSeconds(40)));

        status.Fail("Npcap no está instalado");
        Assert.Equal(CaptureState.Failed, status.Current(start.AddSeconds(41)));
        Assert.Contains("Npcap", status.Describe(start.AddSeconds(41)), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_notice_only_when_the_previous_best_is_beaten()
    {
        Assert.Null(MeterViewModel.RecordText("Tarka", 1000, previousBest: null));
        Assert.Null(MeterViewModel.RecordText("Tarka", 1000, previousBest: 1200));
        var text = MeterViewModel.RecordText("Tarka", 1500, 1200);
        Assert.NotNull(text);
        Assert.StartsWith("¡Récord en Tarka! ", text, StringComparison.Ordinal);
        Assert.Contains(" DPS (antes ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Saved_attempt_option_uses_the_local_row_of_that_fight()
    {
        Assert.Null(CompareOption.Attempt(CompareOption.PreviousAttemptId, "Tu intento anterior", null));
        var saved = DemoEncounter.Create(DateTimeOffset.UtcNow);
        var option = CompareOption.Attempt(CompareOption.PreviousAttemptId, "Tu intento anterior", saved);
        Assert.NotNull(option);
        Assert.StartsWith("Tu intento anterior · ", option.Name, StringComparison.Ordinal);
        Assert.True(option.Saved!.IsLocalPlayer);
    }

    [Fact]
    public void Recent_dps_counts_the_last_seconds_until_the_end_of_the_fight()
    {
        Assert.Equal(0, ActorRowViewModel.RecentDps(null, TimeSpan.FromSeconds(30)));
        long[] timeline = [.. Enumerable.Repeat(100L, 20)];
        Assert.Equal(100, ActorRowViewModel.RecentDps(timeline, TimeSpan.FromSeconds(20)));

        // Dejó de pegar en el segundo 20 y la pelea va por el 25: los últimos 10 s tienen 5 sin daño.
        Assert.Equal(50, ActorRowViewModel.RecentDps(timeline, TimeSpan.FromSeconds(25)));
        Assert.Equal(100, ActorRowViewModel.RecentDps([500, 500], TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Kill_image_goes_to_exports_with_the_fight_name()
    {
        var path = MeterViewModel.KillImagePath(DemoEncounter.Create(DateTimeOffset.UtcNow));
        Assert.Equal(MeterViewModel.ExportFolder, Path.GetDirectoryName(path));
        Assert.EndsWith(".png", path, StringComparison.Ordinal);
    }

    [Fact]
    public void Boss_kill_needs_a_boss_or_a_long_fight()
    {
        var start = DateTimeOffset.UtcNow;
        var trash = new EncounterSnapshot(Guid.NewGuid(), start, start.AddSeconds(8), "Trash", true, 100, []);
        Assert.False(MeterViewModel.IsBossKill(trash));
        Assert.True(MeterViewModel.IsBossKill(trash with { TargetIsBoss = true }));
        Assert.True(MeterViewModel.IsBossKill(trash with { End = start.AddSeconds(45) }));
        Assert.False(MeterViewModel.IsBossKill(trash with { End = start.AddSeconds(45), TargetKilled = false }));
    }

    [Fact]
    public void History_compares_and_filters_per_character()
    {
        HistoryItem Item(string id, string character, double dps) =>
            new(id, "Tarka", "16/10 21:00", "01:00", "1.0M", true, LocalDps: dps, Character: character, Kind: RunSegmentKind.Kill);

        var items = HistoryItem.WithCharacterTags(HistoryItem.WithComparisons([Item("a", "Tests", 3000), Item("b", "Alt", 1000), Item("c", "Alt", 1200)]));
        Assert.Equal(100, items.Single(i => i.Id == "c").Percentile);
        Assert.Null(items.Single(i => i.Id == "a").Percentile);
        Assert.Equal("· Alt", items.Single(i => i.Id == "b").CharacterTag);
        Assert.True(items[0].Matches(null, onlyKills: false, HistoryItem.AllCharacters));
        Assert.False(items[0].Matches(null, onlyKills: false, "Alt"));
        Assert.Empty(HistoryItem.WithCharacterTags([Item("x", "Tests", 1)]).Single().CharacterTag);
    }

    [Fact]
    public void Snap_sticks_to_screen_edges_and_other_windows()
    {
        var screen = new Rect(0, 0, 1920, 1040);

        // Cerca de la esquina superior izquierda: se pega.
        Assert.Equal(new Point(0, 0), WindowSnap.Snap(new Rect(9, 12, 300, 200), screen, []));

        // Lejos de todo: no se mueve.
        Assert.Equal(new Point(500, 400), WindowSnap.Snap(new Rect(500, 400, 300, 200), screen, []));

        // Al lado de otra ventana: se pega a su borde derecho y se alinea arriba con ella.
        var other = new Rect(100, 100, 300, 200);
        Assert.Equal(new Point(400, 100), WindowSnap.Snap(new Rect(408, 106, 300, 200), screen, [other]));

        // Una ventana lejos en el otro eje no atrae.
        Assert.Equal(new Point(408, 700), WindowSnap.Snap(new Rect(408, 700, 300, 200), screen, [other]));
    }

    [Fact]
    public void Phases_text_lists_when_the_boss_crossed_each_phase()
    {
        Assert.Empty(ActorDetailViewModel.PhasesText(null));
        Assert.Equal(" · boss 75% 0:42 · 50% 1:30", ActorDetailViewModel.PhasesText([new HealthPhase(75, 42), new HealthPhase(50, 90)]));
    }

    [Fact]
    public void Autostart_command_quotes_the_path()
    {
        Assert.Equal("\"C:\\Program Files\\Hysteria Meter\\HysteriaMeter.exe\"", Autostart.CommandFor(@"C:\Program Files\Hysteria Meter\HysteriaMeter.exe"));
    }

    [Fact]
    public void Phase_comparison_marks_the_faster_attempt()
    {
        Assert.Empty(ActorDetailViewModel.ComparePhases(null, [new HealthPhase(75, 40)]));
        var rows = ActorDetailViewModel.ComparePhases(
            [new HealthPhase(75, 40), new HealthPhase(50, 80)],
            [new HealthPhase(75, 45), new HealthPhase(50, 70), new HealthPhase(25, 100)]);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new CompareRow("Boss al 75%", "0:40", "0:45", true, false), rows[0]);
        Assert.Equal(new CompareRow("Boss al 50%", "1:20", "1:10", false, true), rows[1]);
    }

    [Fact]
    public void Vs_previous_attempt_shows_the_change_in_percent()
    {
        Assert.Null(MeterViewModel.VsPreviousText(1000, previous: null));
        Assert.Null(MeterViewModel.VsPreviousText(1000, previous: 1005));
        Assert.StartsWith("+8% vs tu intento anterior", MeterViewModel.VsPreviousText(1080, 1000), StringComparison.Ordinal);
        Assert.StartsWith("-20% vs tu intento anterior", MeterViewModel.VsPreviousText(800, 1000), StringComparison.Ordinal);
    }

    [Fact]
    public void History_search_matches_target_or_note()
    {
        var item = new HistoryItem("x", "Destroyer Tarka", "15/10 21:42", "02:47", "3.0k", Killed: false, Note: "Piedra nueva");

        Assert.True(item.Matches("tarka", onlyKills: false));
        Assert.True(item.Matches(" piedra ", onlyKills: false));
        Assert.False(item.Matches("otro", onlyKills: false));
        Assert.False(item.Matches(string.Empty, onlyKills: true));
        Assert.Equal(string.Empty, item.AttemptText);
        Assert.Equal("· intento 2", (item with { Attempt = 2 }).AttemptText);
    }

    [Fact]
    public void Time_to_kill_uses_the_party_dps_on_the_target()
    {
        Assert.Equal(TimeSpan.FromSeconds(42), MeterViewModel.EstimateTimeToKill(420_000, 10_000));
        Assert.Equal(TimeSpan.FromSeconds(43), MeterViewModel.EstimateTimeToKill(420_001, 10_000));
        Assert.Null(MeterViewModel.EstimateTimeToKill(420_000, 0));
        Assert.Null(MeterViewModel.EstimateTimeToKill(0, 10_000));
        Assert.Null(MeterViewModel.EstimateTimeToKill(100_000_000, 10));
    }

    [Fact]
    public void Numbers_follow_the_chosen_style()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal("2.04M", Numbers.Format(2_040_321, NumberStyle.Compact, culture));
        Assert.Equal("2.0M", Numbers.Format(2_040_321, NumberStyle.Brief, culture));
        Assert.Equal("2,040,321", Numbers.Format(2_040_321, NumberStyle.Full, culture));
        Assert.Equal("12k", Numbers.Format(11_900, NumberStyle.Brief, culture));
        Assert.Equal("950", Numbers.Format(950, NumberStyle.Compact, culture));
    }

    [Fact]
    public void Sparkline_keeps_the_last_30_seconds()
    {
        Assert.Null(ActorRowViewModel.Sparkline(null));
        Assert.Null(ActorRowViewModel.Sparkline([0, 0, 0, 0]));

        RunOnSta(() =>
        {
            var points = ActorRowViewModel.Sparkline([.. Enumerable.Range(0, 50).Select(i => (long)i * 100)]);
            Assert.NotNull(points);
            Assert.Equal(ActorRowViewModel.SparkSeconds, points.Count);
            Assert.Equal(0, points[^1].Y); // el último segundo es el pico: arriba de todo
        });
    }

    [Fact]
    public void Only_discord_webhook_urls_are_accepted()
    {
        Assert.True(DiscordWebhook.IsValidUrl("https://discord.com/api/webhooks/123/abc"));
        Assert.True(DiscordWebhook.IsValidUrl(" https://discordapp.com/api/webhooks/123/abc "));
        Assert.False(DiscordWebhook.IsValidUrl("http://discord.com/api/webhooks/123/abc"));
        Assert.False(DiscordWebhook.IsValidUrl("https://evil.example/api/webhooks/123/abc"));
        Assert.False(DiscordWebhook.IsValidUrl("https://discord.com/channels/1/2"));
        Assert.False(DiscordWebhook.IsValidUrl(string.Empty));
    }

    [Fact]
    public void Sharing_hides_names_only_when_chosen()
    {
        var snapshot = DemoEncounter.Create(DateTimeOffset.UnixEpoch.AddHours(1));
        var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { });
        Assert.Same(snapshot, session.ForSharing(snapshot));

        session.Update(session.Preferences with { AnonymizeExports = true });
        Assert.Contains(session.ForSharing(snapshot).Actors, a => a.Name == "Yo");
        Assert.DoesNotContain(session.ForSharing(snapshot).Actors, a => a.Name == "Kaelis");
    }

    [Fact]
    public void Log_filters_by_player_and_text()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        CombatLogEntry[] entries =
        [
            new(t0, LogKind.Damage, 1, "Mati", 9, "Tarka", 10, 1500, HysteriaMeter.Protocol.Events.HitModifiers.Critical | HysteriaMeter.Protocol.Events.HitModifiers.BackAttack),
            new(t0.AddSeconds(1.25), LogKind.Heal, 2, "Ana", 1, "Mati", 20, 300),
            new(t0.AddSeconds(2), LogKind.Damage, 9, "Tarka", 2, "Ana", 30, 800),
            new(t0.AddSeconds(3), LogKind.Death, 2, "Ana", 2, "Ana", 0, 0),
        ];

        Assert.Equal(4, LogViewModel.Filter(entries, SkillNames.Empty, null, null).Count);
        Assert.Equal(2, LogViewModel.Filter(entries, SkillNames.Empty, null, actorId: 1).Count);
        var crit = Assert.Single(LogViewModel.Filter(entries, SkillNames.Empty, "espalda", null));
        Assert.Equal(("00:00.0", "Mati", "Tarka", "Skill 10", "crítico, espalda"), (crit.Time, crit.Source, crit.Target, crit.Skill, crit.Flags));
        Assert.Equal("00:01.2", LogViewModel.Filter(entries, SkillNames.Empty, "Ana", null)[0].Time);
        Assert.Equal("murió", LogViewModel.Filter(entries, SkillNames.Empty, null, actorId: 2)[^1].Skill);
    }

    [Fact]
    public void Death_is_described_with_the_killing_blow()
    {
        var demo = DemoEncounter.Create(DateTimeOffset.UnixEpoch.AddHours(1));
        var item = ActorDetailViewModel.Describe(demo.Deaths![0], demo.Start, SkillNames.Empty);

        Assert.Equal("01:11", item.When);
        Assert.StartsWith("Lo mató Muñeco de prueba con Skill 9102 por ", item.Summary, StringComparison.Ordinal);
        Assert.EndsWith("(crítico)", item.Summary, StringComparison.Ordinal);
        Assert.StartsWith("2 golpes por ", item.LastHits, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ActorRowViewModel.DeathMark(0));
        Assert.Equal(" ✝3", ActorRowViewModel.DeathMark(3));
    }

    [Fact]
    public void Data_folder_is_next_to_the_exe_in_portable_mode()
    {
        Assert.Equal(System.IO.Path.Combine("C:/Juegos/Hysteria", "datos"), AppPaths.Resolve("C:/Juegos/Hysteria", portable: true, "C:/Users/X/AppData/Local"));
        Assert.Equal(System.IO.Path.Combine("C:/Users/X/AppData/Local", "HysteriaMeter"), AppPaths.Resolve("C:/Juegos/Hysteria", portable: false, "C:/Users/X/AppData/Local"));
    }

    [Fact]
    public void Boss_only_counts_damage_to_the_main_target()
    {
        var actor = Actor() with { Damage = 3000, MainTargetDamage = 2000, DamageTaken = 700, Healing = 50 };

        Assert.Equal(3000, MeterTab.Damage.Metric(actor));
        Assert.Equal(2000, MeterTab.Damage.Metric(actor, bossOnly: true));
        Assert.Equal(700, MeterTab.Taken.Metric(actor, bossOnly: true));
        Assert.Equal(50, MeterTab.Healing.Metric(actor));
    }

    [Fact]
    public void Health_check_compares_counted_damage_with_lost_health()
    {
        Assert.Null(MeterViewModel.HealthCheck(null, 100));
        Assert.Null(MeterViewModel.HealthCheck(new Health(1000, 1000), 100));
        var text = MeterViewModel.HealthCheck(new Health(600, 1000), 400);
        Assert.NotNull(text);
        Assert.Contains("100", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Patch_is_suspected_only_with_traffic_and_nothing_known()
    {
        Assert.False(MeterViewModel.PatchSuspected(frames: 500, unknown: 500, errors: 0));
        Assert.True(MeterViewModel.PatchSuspected(frames: 5_000, unknown: 5_000, errors: 0));
        Assert.False(MeterViewModel.PatchSuspected(frames: 5_000, unknown: 4_000, errors: 3));
        Assert.True(MeterViewModel.PatchSuspected(frames: 5_000, unknown: 4_800, errors: 60));
    }

    [Fact]
    public void History_compares_my_dps_with_the_best_try_on_the_same_target()
    {
        HistoryItem Item(string target, double? dps) => new("x", target, "15/10 21:00", "01:00", "1m", false, LocalDps: dps, Kind: RunSegmentKind.Wipe);
        var items = HistoryItem.WithComparisons([Item("Tarka", 800), Item("Tarka", 1000), Item("Solo", 500), Item("Tarka", null)]);
        Assert.Contains("vs mejor", items[0].VsBest);
        Assert.Contains("20", items[0].VsBest);
        Assert.Equal("· mejor intento", items[1].VsBest);
        Assert.Equal(string.Empty, items[2].VsBest);
        Assert.Equal(string.Empty, items[3].VsBest);
        Assert.Equal(("P50", "P100"), (items[0].PercentileText, items[1].PercentileText));
        Assert.False(items[2].HasPercentile);
        Assert.Equal(25, HistoryItem.PercentileOf(10, [10, 20, 30, 40]));
        Assert.NotEqual(HistoryItem.TierBrush(100), HistoryItem.TierBrush(96));
    }

    [Fact]
    public void Practice_counts_down_and_ends_at_the_limit()
    {
        Assert.Equal(TimeSpan.FromSeconds(31), MeterViewModel.PracticeRemaining(TimeSpan.FromSeconds(29.5), 60));
        Assert.Null(MeterViewModel.PracticeRemaining(TimeSpan.FromSeconds(60), 60));
    }

    [Fact]
    public void Dead_time_is_described_with_the_longest_gap()
    {
        var (uptime, dead, hint) = ActorDetailViewModel.DescribeActivity(new FightActivity(100, 12, 6, 3));
        Assert.Contains("88", uptime);
        Assert.Equal("12 s", dead);
        Assert.Contains("6 s", hint);
        Assert.Equal("–", ActorDetailViewModel.DescribeActivity(FightActivity.Empty).Uptime);
    }

    [Fact]
    public void Nicknames_change_only_the_shown_name()
    {
        var names = Nicknames.With(null, "Tester", "  El Tanque  ");
        Assert.Equal("El Tanque", names["Tester"]);
        var snapshot = new EncounterSnapshot(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(10), "Boss", false, 3000, [Actor()]);
        Assert.Equal("El Tanque", Nicknames.Apply(snapshot, names).Actors[0].Name);
        Assert.Same(snapshot, Nicknames.Apply(snapshot, new Dictionary<string, string>()));
        Assert.Empty(Nicknames.With(names, "Tester", ""));
        Assert.Equal(Nicknames.MaxLength, Nicknames.With(null, "Tester", new string('x', 40))["Tester"].Length);
    }

    [Fact]
    public void Profile_round_trips_without_the_webhook()
    {
        var mine = new MeterPreferences { DiscordWebhook = "https://discord.com/api/webhooks/1/abc", RefreshMs = 500, IdleSeconds = 15 };
        var json = ProfileFile.ToJson(mine);
        Assert.DoesNotContain("webhooks", json, StringComparison.Ordinal);

        var other = new MeterPreferences { DiscordWebhook = "https://discord.com/api/webhooks/2/xyz" };
        var imported = ProfileFile.FromJson(json, other);
        Assert.NotNull(imported);
        Assert.Equal((500, 15, other.DiscordWebhook), (imported.RefreshMs, imported.IdleSeconds, imported.DiscordWebhook));
        Assert.Null(ProfileFile.FromJson("no es json", other));
    }

    [Fact]
    public void Advice_points_at_dead_time_deaths_and_the_gap_to_the_top()
    {
        var me = Actor() with { Dps = 1000, ActiveDps = 1400, Deaths = 1, Timeline = [100, 100, 0, 0, 0, 0, 0, 0, 100, 100, 100, 100, 100, 100] };
        var top = Actor() with { Id = 8, Name = "Rival", IsLocalPlayer = false, Dps = 1500 };
        var snapshot = new EncounterSnapshot(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(14), "Boss", false, 3000, [me, top]);

        var tips = FightAdvice.For(snapshot, me);
        Assert.Contains(tips, t => t.Contains("6 s sin pegar", StringComparison.Ordinal) && t.Contains("0:02", StringComparison.Ordinal));
        Assert.Contains(tips, t => t.StartsWith("Moriste 1 vez", StringComparison.Ordinal));
        Assert.Contains(tips, t => t.StartsWith("Rival hizo", StringComparison.Ordinal));
        Assert.True(tips.Count <= FightAdvice.Max);
    }

    [Fact]
    public void Comparison_marks_who_is_better_in_each_row()
    {
        var me = Actor() with { Dps = 1000, Deaths = 0 };
        var other = Actor() with { Id = 8, Name = "Rival", Dps = 1500, Deaths = 2 };
        var rows = ActorDetailViewModel.Compare(me, other);
        var dps = rows.Single(r => r.Label == "DPS");
        Assert.True(dps.TheirsBetter);
        Assert.False(dps.MineBetter);
        var deaths = rows.Single(r => r.Label == "Muertes");
        Assert.True(deaths.MineBetter);
    }

    [Fact]
    public void Progress_shows_only_for_a_single_boss_oldest_first()
    {
        HistoryItem Item(string target, double? dps) => new("x", target, "15/10 21:00", "01:00", "1m", false, LocalDps: dps);
        var progress = HistoryItem.Progress([Item("Tarka", 1200), Item("Tarka", null), Item("Tarka", 900), Item("Tarka", 1000)]);
        Assert.NotNull(progress);
        Assert.Equal("Tarka", progress.Value.Target);
        Assert.Equal([1000d, 900d, 1200d], progress.Value.Dps);
        Assert.Null(HistoryItem.Progress([Item("Tarka", 1200), Item("Solo", 900)]));
        Assert.Null(HistoryItem.Progress([Item("Tarka", 1200)]));
        Assert.StartsWith("Tarka · 3 intentos", MeterViewModel.ProgressSummary("Tarka", progress.Value.Dps), StringComparison.Ordinal);
        Assert.EndsWith("· kill más rápida 2:25", MeterViewModel.ProgressSummary("Tarka", progress.Value.Dps, TimeSpan.FromSeconds(145)), StringComparison.Ordinal);
    }

    [Fact]
    public void Fastest_kill_is_tagged_per_boss()
    {
        HistoryItem Kill(string id, double seconds, bool killed = true) =>
            new(id, "Tarka", "16/10 21:00", "02:00", "1.0M", killed, Seconds: seconds, Kind: killed ? RunSegmentKind.Kill : RunSegmentKind.Wipe);

        var items = HistoryItem.WithComparisons([Kill("a", 150), Kill("b", 140), Kill("c", 90, killed: false)]);
        Assert.Equal("· ⏱ kill más rápida", items.Single(i => i.Id == "b").FastestTag);
        Assert.Empty(items.Single(i => i.Id == "a").FastestTag);
        Assert.Equal(TimeSpan.FromSeconds(140), HistoryItem.FastestKill(items));
        Assert.Null(HistoryItem.FastestKill([Kill("x", 90, killed: false)]));
    }

    [Fact]
    public void Npc_table_reads_full_and_short_entries()
    {
        var table = NpcTable.Parse("""
            {
              // comentario
              "npcs": {
                "12345": { "name": " Destroyer Tarka ", "dungeon": "Fortaleza", "boss": true, "enrage": 480 },
                "777": "Esqueleto",
                "abc": "no es un ID",
                "888": { "dungeon": "sin nombre" },
              }
            }
            """);

        Assert.Equal(2, table.Count);
        Assert.Equal(new NpcInfo("Destroyer Tarka", "Fortaleza", IsBoss: true, EnrageSeconds: 480), table.Of(12345));
        Assert.Equal(new NpcInfo("Esqueleto"), table.Of(777));
        Assert.Equal(0, NpcTable.Parse("{ roto").Count);
        Assert.Equal(0, NpcTable.Parse("[]").Count);

        var fight = new EncounterSnapshot(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(5), null, false, 0, []);
        Assert.Equal("Encuentro", MeterViewModel.TitleOf(fight));
        Assert.Equal("NPC 12345", MeterViewModel.TitleOf(fight with { TargetNpcId = 12345 }));
        Assert.Equal("Tarka", MeterViewModel.TitleOf(fight with { TargetName = "Tarka", TargetNpcId = 12345 }));
    }

    [Fact]
    public void Enrage_warns_when_the_boss_dies_after_it()
    {
        Assert.Equal((" · enrage en 1:20", false), MeterViewModel.Enrage(480, TimeSpan.FromSeconds(400), TimeSpan.FromSeconds(60)));
        Assert.Equal((" · enrage en 1:20", true), MeterViewModel.Enrage(480, TimeSpan.FromSeconds(400), TimeSpan.FromSeconds(90)));
        Assert.Equal((" · enrage en 1:20", false), MeterViewModel.Enrage(480, TimeSpan.FromSeconds(400), null));
        Assert.Equal((" · ENRAGE", true), MeterViewModel.Enrage(480, TimeSpan.FromSeconds(481), null));
    }

    [Fact]
    public void Hotkeys_parse_and_print_the_same()
    {
        Assert.Equal("Ctrl+Shift+L", HotkeyGesture.Parse("ctrl + shift + l")?.ToString());
        Assert.Equal("Alt+5", HotkeyGesture.Parse("Alt+5")?.ToString());
        Assert.Equal("F9", HotkeyGesture.Parse("F9")?.ToString());
        Assert.Equal("Shift+F2", HotkeyGesture.Parse("Shift+F2")?.ToString());
        Assert.Null(HotkeyGesture.Parse("L"));
        Assert.Null(HotkeyGesture.Parse("Shift+L"));
        Assert.Null(HotkeyGesture.Parse("Ctrl+"));
        Assert.Null(HotkeyGesture.Parse("Win+L"));
        Assert.Null(HotkeyGesture.Parse("Ctrl+NoExiste"));
        Assert.Null(HotkeyGesture.Parse("Ctrl+65"));
        Assert.Null(HotkeyGesture.From(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.LeftShift));
        Assert.Equal(" · marcas 0:05, 1:02", ActorDetailViewModel.MarksText([62, 5]));
        Assert.Equal(string.Empty, ActorDetailViewModel.MarksText(null));
    }

    [Fact]
    public void Demo_encounter_is_consistent()
    {
        var demo = DemoEncounter.Create(DateTimeOffset.UnixEpoch.AddHours(1));

        Assert.Equal(demo.TotalDamage, demo.Actors.Sum(a => a.Damage));
        Assert.Single(demo.Actors, a => a.IsLocalPlayer);
        Assert.All(demo.Actors, a => Assert.Equal(95, a.Timeline!.Count));
    }

    [Fact]
    public void Rolling_dps_averages_the_last_seconds()
    {
        Assert.Equal([2d, 2, 4], ActorDetailViewModel.RollingDps([10, 0, 10], 5));
        Assert.Equal([5d, 5, 5], ActorDetailViewModel.RollingDps([10, 0, 10], 2));
    }

    [Fact]
    public void Summary_lists_players_by_dps_for_the_game_chat()
    {
        var start = DateTimeOffset.UnixEpoch;
        var snapshot = new EncounterSnapshot(Guid.NewGuid(), start, start.AddSeconds(100), "Tarka", true, 3000,
        [
            Actor() with { Name = "Beto", Damage = 1000 },
            Actor() with { Name = "Ana", Damage = 2000 },
            Actor() with { Name = "Curandera", Damage = 0, Healing = 500 },
        ]);

        var summary = MeterViewModel.BuildSummary(snapshot, MeterTab.Damage);

        Assert.Equal("Tarka 01:40 DPS · 1. Ana 20 (67%) · 2. Beto 10 (33%)", summary);
    }

    [Fact]
    public void Gear_and_class_show_next_to_the_name()
    {
        Assert.Equal(string.Empty, ActorRowViewModel.GearText(null, null));
        Assert.StartsWith("2130 · ", ActorRowViewModel.GearText(2130, 93_100), StringComparison.Ordinal);
        Assert.Equal("Templario", PlayerClasses.Of(12)?.Name);
        Assert.Null(PlayerClasses.Of(3));
        Assert.StartsWith("Templario · iLvl 2130 · power ", ActorDetailViewModel.ProfileText(Actor() with { ClassId = 12, ItemLevel = 2130, CombatPower = 93_100 }), StringComparison.Ordinal);
    }

    [Fact]
    public void Class_table_merges_json_over_defaults()
    {
        var table = PlayerClasses.Parse("""{ "classes": { "12": { "name": "Paladín", "icon": "cruz" }, "x": {}, "15": { "icon": "nada" } } }""");

        Assert.Equal("Paladín", table[12].Name);
        Assert.Same(PlayerClasses.Icons["cruz"], table[12].Icon);
        Assert.Equal(PlayerClasses.Defaults[15], table[15]);
        Assert.Equal(PlayerClasses.Defaults.Count, PlayerClasses.Parse("no es json").Count);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x11, 0x22, 0x33), PlayerClasses.Parse("""{ "classes": { "12": { "color": "#112233" } } }""")[12].Color);
        Assert.Null(PlayerClasses.ParseColor("rojo"));
    }

    [Fact]
    public void Startup_notice_lists_unseen_news()
    {
        var news = NewsFeed.Parse("""{ "news": [ { "id": 35, "text": "Vieja" }, { "id": 37, "text": "Nueva" }, { "id": "x" } ] }""");

        Assert.Equal([37, 35], news.Select(n => n.Id));
        // La bienvenida ya no sale por este aviso (es su propia ventana): con 0 vistas son todas novedades.
        Assert.Equal(["Nueva", "Vieja"], NewsFeed.NoticeFor(news, 0)!.Lines);
        Assert.Equal(["Nueva"], NewsFeed.NoticeFor(news, 35)!.Lines);
        Assert.Equal(37, NewsFeed.NoticeFor(news, 1)!.LatestId);
        Assert.Null(NewsFeed.NoticeFor(news, 37));
        Assert.Empty(NewsFeed.Parse("roto"));
    }

    [Fact]
    public void Limited_rows_always_keep_the_local_player()
    {
        var rows = Enumerable.Range(1, 6).Select(i => Actor() with { Id = (ulong)i, Name = $"J{i}", IsLocalPlayer = i == 6 }).ToList();

        Assert.Equal([1UL, 2, 6], MeterViewModel.LimitRows(rows, 3).Select(a => a.Id));
        Assert.Equal(6, MeterViewModel.LimitRows(rows, 0).Count);
        Assert.Equal([1UL, 2, 3, 4, 5], MeterViewModel.LimitRows([.. rows.Take(5)], 5).Select(a => a.Id));
    }

    [Fact]
    public void Themes_replace_and_restore_the_palette()
    {
        RunOnSta(() =>
        {
            var gold = System.Windows.Media.Colors.Gold;
            var resources = new ResourceDictionary
            {
                ["Gold"] = new System.Windows.Media.SolidColorBrush(gold),
                ["Panel"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black),
                // Derivada del acento: el tema le cambia el color y le deja el alfa.
                ["GoldDim"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x47, gold.R, gold.G, gold.B)),
            };
            var defaults = MeterThemes.Snapshot(resources);
            System.Windows.Media.Color ColorOf(string key) => ((System.Windows.Media.SolidColorBrush)resources[key]).Color;

            MeterThemes.Apply(resources, "Abismo", defaults);
            var abyss = MeterThemes.Of("Abismo").Colors["Gold"];
            Assert.Equal(abyss, ColorOf("Gold"));
            Assert.Equal(MeterThemes.Of("Abismo").Colors["Panel"], ColorOf("Panel"));
            Assert.Equal(System.Windows.Media.Color.FromArgb(0x47, abyss.R, abyss.G, abyss.B), ColorOf("GoldDim"));

            MeterThemes.Apply(resources, "no existe", defaults);
            Assert.Equal(gold, ColorOf("Gold"));
            Assert.Equal(System.Windows.Media.Colors.Black, ColorOf("Panel"));
            Assert.Equal(System.Windows.Media.Color.FromArgb(0x47, gold.R, gold.G, gold.B), ColorOf("GoldDim"));

            // Cada tema alternativo define fondos, acento y textos (15 colores); las derivadas del acento y los neutros no.
            string[] themed =
            [
                "WindowBg", "Panel", "MiniPanel", "PanelRaised", "PanelSolid", "Surface", "CardBg", "InputBg", "MenuBg",
                "Gold", "GoldBright", "Text", "TextBright", "TextMuted", "TextDim",
            ];
            Assert.All(MeterThemes.All.Skip(1), t => Assert.Equal(themed.Order(), t.Colors.Keys.Order()));
        });
    }

    [Fact]
    public void Minimized_pill_shows_my_dps_and_rank()
    {
        var me = new ActorRowViewModel();
        me.Update(2, Actor(), MeterTab.Damage, 6000, 9000, TimeSpan.FromSeconds(10));
        var other = new ActorRowViewModel();
        other.Update(1, Actor() with { IsLocalPlayer = false, Name = "Otro" }, MeterTab.Damage, 6000, 9000, TimeSpan.FromSeconds(10));

        Assert.Equal($"{me.PerSecond} · #2 de 3", MeterViewModel.MinimizedSummary([other, me], "900", 3));
        Assert.Equal("900 party", MeterViewModel.MinimizedSummary([other], "900", 3));
        Assert.Equal("HYSTERIA", MeterViewModel.MinimizedSummary([], "0", 0));
    }

    [Fact]
    public void Skill_names_set_by_the_user_win_over_the_file()
    {
        var names = new SkillNames(new Dictionary<uint, string> { [100] = "Del archivo" }).Copy();
        names.Custom = SkillNames.With(null, 100, " Mía ");
        Assert.Equal("Mía", names.Of(100));
        Assert.Equal("Skill 7", names.Of(7));

        names.Custom = SkillNames.With(names.Custom, 100, "");
        Assert.Equal("Del archivo", names.Of(100));
        Assert.Empty(SkillNames.Empty.Custom);

        var json = System.Text.Json.JsonSerializer.Serialize(new MeterPreferences { SkillAliases = SkillNames.With(null, 12010350, "Golpe") });
        Assert.Equal("Golpe", System.Text.Json.JsonSerializer.Deserialize<MeterPreferences>(json)!.SkillAliases[12010350]);
    }

    [Fact]
    public void Bug_report_has_the_diagnostics_but_no_webhook()
    {
        var preferences = new MeterPreferences { DiscordWebhook = "https://discord.com/api/webhooks/1/secreto" };
        var report = BugReport.Build("v0.15.0 · abc1234", "Windows 11", "En combate", "frames 10\nconocidos 9", preferences);

        Assert.Contains("v0.15.0 · abc1234", report);
        Assert.Contains("frames 10 conocidos 9", report);
        Assert.DoesNotContain("secreto", report);
        Assert.StartsWith(BugReport.IssuesUrl + "?title=", BugReport.IssueUrl(report));
        Assert.True(BugReport.IssueUrl(new string('x', 10_000)).Length < 3200);
    }

    [Fact]
    public void Rows_take_the_class_color_when_asked()
    {
        var row = new ActorRowViewModel();
        var templario = Actor() with { ClassId = 12, IsLocalPlayer = false };

        row.Update(1, templario, MeterTab.Damage, 3000, 3000, TimeSpan.FromSeconds(10), colorByClass: true);
        Assert.Equal(PlayerColors.ForClass(PlayerClasses.Defaults[12].Color), row.Accent);

        row.Update(1, templario, MeterTab.Damage, 3000, 3000, TimeSpan.FromSeconds(10));
        Assert.Equal(PlayerColors.For(templario.Name), row.Accent);
    }

    private static ActorSnapshot Actor() => new(
        Id: 7, Name: "Tester", ClassId: null, IsLocalPlayer: true, Damage: 3000, Dps: 300, Share: 1, Hits: 3, Crits: 1, MaxHit: 1500, Healing: 0,
        Timeline: [1000, 0, 500, 1500, 0, 0],
        Skills:
        [
            new SkillSnapshot(100, 2000, 2, 1, 1500, 0.667),
            new SkillSnapshot(200, 1000, 1, 0, 1000, 0.333, PeriodicDamage: 400),
        ]);

    /// <summary>Mide y acomoda sin mostrar la ventana: alcanza para expandir plantillas y resolver recursos.</summary>
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
