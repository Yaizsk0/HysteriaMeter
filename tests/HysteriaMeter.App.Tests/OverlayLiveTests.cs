using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

// Application y sus ResourceDictionary son globales: WPF no permite que dos hilos carguen recursos a la vez.
[CollectionDefinition("Overlay WPF", DisableParallelization = true)]
public sealed class OverlayWpfTestsGroup;

[Collection("Overlay WPF")]
public sealed class OverlayLiveTests
{
    [Fact]
    public void Capture_settings_request_switches_an_open_configuration_to_capture()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var vm = new MeterViewModel(session);
            vm.OpenSettingsCommand.Execute(ConfigSection.Atajos);
            var window = new ConfigWindow(vm, vm.SettingsSection);
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(window.Width, window.Height));
                content.Arrange(new Rect(0, 0, window.Width, window.Height));
                content.UpdateLayout();
                Assert.True(vm.IsSettingsOpen);
                Assert.True(Assert.IsType<RadioButton>(window.FindName("NavHotkeys")).IsChecked);

                vm.OpenCaptureSettingsCommand.Execute(null);
                content.UpdateLayout();

                Assert.True(vm.IsSettingsOpen);
                Assert.Equal(ConfigSection.Captura, vm.SettingsSection);
                Assert.True(Assert.IsType<RadioButton>(window.FindName("NavCapture")).IsChecked);
                Assert.False(Assert.IsType<RadioButton>(window.FindName("NavHotkeys")).IsChecked);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Legacy_density_migrates_once_and_the_next_slider_step_can_be_24()
    {
        RunOnSta(() =>
        {
            // El formato anterior no tenía versión de densidad. Es la misma deserialización de UserStore.
            var loaded = JsonSerializer.Deserialize<MeterPreferences>("""{"RowHeight":24,"RefreshMs":500}""")!;
            Assert.Equal(22, loaded.RowHeight);
            Assert.Equal(500, loaded.RefreshMs);
            string? saved = null;
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                loaded, preferences => saved = JsonSerializer.Serialize(preferences));
            var vm = new MeterViewModel(session);

            // La flecha del slider avanza dos píxeles; no debe volver a activar la migración.
            vm.RowHeight += 2;

            Assert.Equal(24, vm.RowHeight);
            Assert.NotNull(saved);
            var reopened = JsonSerializer.Deserialize<MeterPreferences>(saved)!;
            Assert.Equal(24, reopened.RowHeight);
            Assert.Equal(500, reopened.RefreshMs);
        });
    }

    [Fact]
    public void A_current_profile_keeps_a_selected_density_of_24()
    {
        var preferences = new MeterPreferences { RowHeight = 24 };
        var reopened = ProfileFile.FromJson(ProfileFile.ToJson(preferences), new MeterPreferences());
        Assert.NotNull(reopened);
        Assert.Equal(24, reopened.RowHeight);
        Assert.Equal(30, JsonSerializer.Deserialize<MeterPreferences>("{}")!.RowHeight);
    }

    [Fact]
    public void A_new_overlay_in_the_same_run_keeps_the_previous_fight_chips()
    {
        RunOnSta(() =>
        {
            var engine = new CombatEngine();
            var session = new MeterSession(engine, new CaptureRecorder(), SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var start = DateTimeOffset.UnixEpoch;
            engine.Entities.Npcs = new NpcCatalog(new Dictionary<uint, NpcInfo> { [123] = new("Tarka", null, true) });
            engine.Apply(new LocalPlayerEvent(start, 1, "Mati"));
            engine.Apply(new EntitySpawnEvent(start, 900, null, 123));
            engine.Apply(new DamageEvent(start, 1, 900, 10, 1000, HitModifiers.None));
            engine.Apply(new DamageEvent(start.AddSeconds(30), 1, 900, 10, 1000, HitModifiers.None));
            engine.Apply(new DeathEvent(start.AddSeconds(31), 900));
            var vm = new MeterViewModel(session, runView: true);
            vm.IsPaused = true;
            vm.IsPaused = false;
            Assert.Contains(vm.RunChips, chip => chip.Text == "Tarka ✓");
            vm.ResetCommand.Execute(null);
            Assert.Empty(vm.RunChips);
        });
    }

    [Fact]
    public void Run_chips_group_only_consecutive_trash_and_wipes()
    {
        var start = DateTimeOffset.UnixEpoch;
        RunFight Fight(int minute, string title, RunSegmentKind kind, bool current = false) =>
            new(start.AddMinutes(minute), start.AddMinutes(minute + 1), title, kind, start, current);
        var chips = MeterViewModel.BuildRunChips([
            Fight(0, "Trash A", RunSegmentKind.Trash), Fight(2, "Trash B", RunSegmentKind.Trash),
            Fight(4, "Tarka", RunSegmentKind.Wipe), Fight(6, "Tarka", RunSegmentKind.Wipe),
            Fight(8, "Kromede", RunSegmentKind.Wipe), Fight(10, "Tarka", RunSegmentKind.Kill),
            Fight(12, "Kromede", RunSegmentKind.Wipe, current: true),
        ]);
        Assert.Equal(["Trash 3:00", "Tarka ✕2", "Kromede ✕", "Tarka ✓", "Kromede · en curso"], chips.Select(c => c.Text));
        Assert.Equal(RunChipKind.Current, chips[^1].Kind);
    }

    [Theory]
    [InlineData(CaptureState.Failed, "falló una DLL", false, CaptureIssue.Npcap)]
    [InlineData(CaptureState.Failed, "Npcap no responde", true, CaptureIssue.Npcap)]
    [InlineData(CaptureState.Failed, "acceso denegado", true, CaptureIssue.Failed)]
    [InlineData(CaptureState.NoTraffic, null, true, CaptureIssue.NoTraffic)]
    [InlineData(CaptureState.Capturing, null, true, CaptureIssue.Patch)]
    public void Capture_cards_distinguish_missing_driver_silence_and_unknown_protocol(CaptureState state, string? error, bool installed, CaptureIssue expected) =>
        Assert.Equal(expected, MeterViewModel.IssueOf(state, error, patch: true, installed));

    [Fact]
    public void Empty_overlay_shows_waiting_and_capture_cards_and_keeps_the_menu_accessible()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var vm = new MeterViewModel(session);
            var window = new OverlayWindow(vm);
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            content.Measure(new Size(380, 360));
            content.Arrange(new Rect(0, 0, 380, 360));
            content.UpdateLayout();

            var waiting = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("WaitingCard"));
            var target = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("TargetBlock"));
            var menu = Assert.IsType<Button>(window.FindName("MoreButton"));
            Assert.Equal(Visibility.Visible, waiting.Visibility);
            Assert.Equal(Visibility.Collapsed, target.Visibility);
            Assert.NotNull(menu.ContextMenu);

            vm.Issue = CaptureIssue.Npcap;
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, waiting.Visibility);
            var npcap = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("NpcapCard"));
            Assert.Equal(Visibility.Visible, npcap.Visibility);

            vm.Issue = CaptureIssue.NoTraffic;
            window.UpdateLayout();
            var traffic = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("TrafficCard"));
            Assert.Equal(Visibility.Visible, traffic.Visibility);
            vm.OpenCaptureSettingsCommand.Execute(null);
            Assert.True(vm.IsSettingsOpen);
            Assert.Equal(ConfigSection.Captura, vm.SettingsSection);

            vm.IsSettingsOpen = false;
            vm.ShowDemoCommand.Execute(null);
            window.UpdateLayout();
            Assert.True(vm.HasFight);
            Assert.Equal(Visibility.Collapsed, traffic.Visibility);
            Assert.Equal(Visibility.Visible, target.Visibility);

            // Los avisos pueden tapar filas en pantalla, pero nunca deben quedar en el PNG compartido.
            vm.ShowsToasts = true;
            vm.HasBossNotice = false;
            content.UpdateLayout();
            // Sin Show(), el primer VisualBrush prepara la composición de WPF. La comparación usa dos renders ya preparados.
            _ = RenderPixels(window);
            var clean = RenderPixels(window);
            vm.BossNoticeName = "Boss detectado";
            vm.BossNoticeDetail = "Este aviso queda fuera de la imagen exportada";
            vm.HasBossNotice = true;
            content.UpdateLayout();
            Assert.Equal(clean, RenderPixels(window));

            vm.BackToLiveCommand.Execute(null);
            window.UpdateLayout();
            Assert.False(vm.HasFight);
            Assert.Empty(vm.Actors);
            Assert.Equal(Visibility.Visible, waiting.Visibility);
            window.Close();
        });
    }

    private static byte[] RenderPixels(OverlayWindow window)
    {
        var render = typeof(OverlayWindow).GetMethod("RenderOverlay", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var bitmap = Assert.IsType<RenderTargetBitmap>(render.Invoke(window, null));
        var pixels = new byte[bitmap.PixelHeight * bitmap.PixelWidth * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    [Fact]
    public void Optional_columns_remain_reachable_at_minimum_width()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                new MeterPreferences
                {
                    LastSeenNews = 1, ShowTotalColumn = true, ShowRecentColumn = true,
                    ShowCritColumn = true, ShowMaxHitColumn = true,
                }, _ => { });
            var vm = new MeterViewModel(session);
            vm.ShowDemoCommand.Execute(null);
            var window = new OverlayWindow(vm);
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            content.Measure(new Size(260, 360));
            content.Arrange(new Rect(0, 0, 260, 360));
            content.UpdateLayout();
            var scroller = Assert.IsType<ScrollViewer>(window.FindName("ActorScroller"));
            Assert.True(scroller.ScrollableWidth > 0);
            scroller.ScrollToRightEnd();
            content.UpdateLayout();
            Assert.Equal(scroller.ScrollableWidth, scroller.HorizontalOffset);
            window.Close();
        });
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
