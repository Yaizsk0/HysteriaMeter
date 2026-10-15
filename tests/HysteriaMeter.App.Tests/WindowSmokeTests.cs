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
public sealed class WindowSmokeTests
{
    [Fact]
    public void Overlay_and_detail_windows_load_with_rows()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();

            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { });
            var viewModel = new MeterViewModel(session);
            var healing = new MeterViewModel(session, MeterTab.Healing, showHealing: false);
            var row = new ActorRowViewModel();
            row.Update(1, Actor(), MeterTab.Damage, top: 3000, total: 3000, TimeSpan.FromSeconds(10));
            viewModel.Actors.Add(row);
            viewModel.Detail.Select(row);
            viewModel.History.Add(new HistoryItem("x", "Boss", "15/10 21:42", "02:47", "3.0k", Killed: true, "rotación nueva", "mi DPS 1.2k", 3, IsRecord: true));
            viewModel.IsHistoryOpen = true;
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

            // Modo demo y compacto: la pelea inventada llena las filas y el overlay chico carga igual.
            viewModel.ShowDemoCommand.Execute(null);
            Assert.Equal(6, viewModel.Actors.Count);
            Assert.True(viewModel.IsViewingHistory);
            viewModel.IsCompact = true;
            Layout(new OverlayWindow(viewModel));

            // Pestaña de daño recibido, detalle con muertes y registro.
            viewModel.IsTakenTab = true;
            Assert.Equal("DTPS", viewModel.RateUnit);
            var tharos = viewModel.Actors.Single(a => a.Name == "Tharos");
            Assert.EndsWith("✝", tharos.DisplayName, StringComparison.Ordinal);
            viewModel.ShowDetailCommand.Execute(tharos);
            Assert.True(viewModel.Detail.HasDeaths);
            Layout(new DetailWindow(viewModel.Detail));
            var demo = DemoEncounter.Create(DateTimeOffset.UnixEpoch.AddHours(1));
            Layout(new HysteriaMeter.App.Registro.LogWindow(new LogViewModel(() => demo.Deaths![0].LastHits, SkillNames.Empty, tharos.Id, "Tharos")));
        });
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
