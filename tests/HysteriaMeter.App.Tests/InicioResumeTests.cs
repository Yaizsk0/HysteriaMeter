using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using HysteriaMeter.App.FightCard;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.App.Welcome;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

/// <summary>Regresiones encontradas en la revisión de Inicio, Mini y Bandeja.</summary>
public sealed class InicioResumeTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Card_detail_uses_its_fight_while_paused_in_another_view()
    {
        var vm = new MeterViewModel(Session(), runView: true);
        vm.ShowDemoCommand.Execute(null);
        vm.IsPaused = true;
        var opened = 0;
        vm.DetailRequested += () => opened++;
        var fight = Fight();

        Assert.True(vm.ShowFightDetail(fight));

        Assert.Equal(1, opened);
        Assert.Equal("Boss de la tarjeta", vm.Title);
        Assert.Equal("Yo", vm.Detail.Name);
        Assert.Equal("400", vm.Detail.Dps);
        Assert.True(vm.IsViewingHistory);
    }

    [Fact]
    public void Card_detail_finds_the_local_player_when_the_healing_tab_has_no_row()
    {
        var vm = new MeterViewModel(Session(), MeterTab.Healing, showHealing: false);

        Assert.True(vm.ShowFightDetail(Fight()));

        Assert.Empty(vm.Actors);
        Assert.Equal(1UL, vm.Detail.ActorId);
        Assert.Equal("Yo", vm.Detail.Name);
        Assert.Equal("400", vm.Detail.Dps);
        Assert.False(vm.ShowFightDetail(Fight() with { Actors = [] }));
    }

    [Fact]
    public void Card_image_renders_its_fight_in_full_and_restores_the_view_after_an_error()
    {
        var vm = new MeterViewModel(Session(), sessionView: true);
        vm.ShowDemoCommand.Execute(null);
        vm.IsMinimized = true;

        Assert.Throws<IOException>(() => vm.SaveFightImage(Fight(), () =>
        {
            Assert.Equal("Boss de la tarjeta", vm.Title);
            Assert.Equal("400", vm.Actors.Single().PerSecond);
            Assert.True(vm.IsFullView);
            Assert.False(vm.IsSettingsOpen || vm.IsHistoryOpen || vm.IsViewingHistory);
            throw new IOException("Fallo de prueba al dibujar");
        }));

        Assert.True(vm.IsMinimized);
        Assert.True(vm.IsViewingHistory);
    }

    [Fact]
    public void Card_image_returns_the_actual_result_without_overwriting_export_message()
    {
        var vm = new MeterViewModel(Session()) { ExportMessage = "Resultado anterior" };

        Assert.Equal("No se pudo dibujar", vm.SaveFightImage(Fight(), () => "No se pudo dibujar"));
        Assert.Equal("Resultado anterior", vm.ExportMessage);
    }

    [Fact]
    public void Card_image_refreshes_a_paused_snapshot_of_the_same_fight()
    {
        var session = Session();
        var vm = new MeterViewModel(session);
        var engine = session.Engine;
        engine.Apply(new LocalPlayerEvent(Start, 1, "Yo"));
        engine.Apply(new DamageEvent(Start, 1, 900, 100, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(Start.AddSeconds(5), 1, 900, 100, 1000, HitModifiers.None));
        vm.BossOnly = false; // Refresca el vivo antes de pausar: 2000 de daño en 5 s.
        Assert.Equal("400", Assert.Single(vm.Actors).PerSecond);
        vm.IsPaused = true;
        vm.IsMinimized = true;

        EncounterSnapshot? finished = null;
        engine.EncounterFinished += fight => finished = fight;
        engine.Apply(new DamageEvent(Start.AddSeconds(10), 1, 900, 100, 4000, HitModifiers.None));
        engine.Tick(Start.AddSeconds(20));
        Assert.NotNull(finished);
        Assert.Equal(6000, Assert.Single(finished.Actors).Damage);

        Assert.Equal("Captura guardada", vm.SaveFightImage(finished, () =>
        {
            Assert.Equal("600", Assert.Single(vm.Actors).PerSecond);
            Assert.False(vm.IsMinimized || vm.IsSettingsOpen || vm.IsHistoryOpen || vm.IsViewingHistory);
            return "Captura guardada";
        }));

        Assert.True(vm.IsPaused);
        Assert.True(vm.IsMinimized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Card_image_suppresses_pause_styling_and_restores_pause_even_after_render_failure(bool renderFails)
    {
        var session = Session();
        var vm = new MeterViewModel(session);
        var engine = session.Engine;
        engine.Apply(new LocalPlayerEvent(Start, 1, "Yo"));
        engine.Apply(new DamageEvent(Start, 1, 900, 100, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(Start.AddSeconds(5), 1, 900, 100, 1000, HitModifiers.None));
        vm.BossOnly = false;
        Assert.Equal("400", Assert.Single(vm.Actors).PerSecond);
        vm.IsPaused = true;
        vm.IsMinimized = true;
        engine.Apply(new DamageEvent(Start.AddSeconds(10), 1, 900, 100, 4000, HitModifiers.None));
        var final = engine.CurrentSnapshot()!;

        string Render()
        {
            Assert.False(vm.IsPauseBannerVisible);
            Assert.False(vm.IsPaused);
            Assert.False(vm.IsMinimized || vm.IsViewingHistory);
            Assert.Equal("600", Assert.Single(vm.Actors).PerSecond);
            if (renderFails)
            {
                throw new IOException("Fallo de render para verificar restauración de pausa");
            }

            return "Captura guardada";
        }

        if (renderFails)
        {
            Assert.Throws<IOException>(() => vm.SaveFightImage(final, Render));
        }
        else
        {
            Assert.Equal("Captura guardada", vm.SaveFightImage(final, Render));
        }

        Assert.True(vm.IsPaused);
        Assert.True(vm.IsMinimized);
        Assert.True(vm.IsViewingHistory);
        Assert.Equal("600", Assert.Single(vm.Actors).PerSecond);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Card_image_keeps_the_independent_auxiliary_windows_open(bool renderFails)
    {
        var vm = new MeterViewModel(Session()) { IsSettingsOpen = true, IsHistoryOpen = true };
        string Render()
        {
            Assert.True(vm.IsSettingsOpen && vm.IsHistoryOpen);
            Assert.True(vm.IsFullView);
            Assert.False(vm.IsViewingHistory);
            if (renderFails)
            {
                throw new IOException("Fallo de render para verificar restauración");
            }

            return "Imagen guardada";
        }

        if (renderFails)
        {
            Assert.Throws<IOException>(() => vm.SaveFightImage(Fight(), Render));
        }
        else
        {
            Assert.Equal("Imagen guardada", vm.SaveFightImage(Fight(), Render));
        }

        Assert.True(vm.IsSettingsOpen && vm.IsHistoryOpen);
        Assert.True(vm.IsViewingHistory);
    }

    [Fact]
    public void Config_and_welcome_count_the_same_names_as_the_meter()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"hysteria-game-data-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "skills.json");
            File.WriteAllText(file, """{"skills":{"15280000":"Primero","15280000":"Último","15290000":42}}""");
            Assert.Equal(1, HysteriaMeter.App.Config.GameDataSummary.Read(folder).Skills);
            Assert.Equal(1, GameDataSummary.Read(folder).Skills);
            Assert.Equal("Último", SkillNames.Load(file).Of(15280000));
        }
        finally
        {
            File.Delete(Path.Combine(folder, "skills.json"));
            Directory.Delete(folder);
        }
    }
    [Fact]
    public void Top_skill_groups_unnamed_variants_by_base_id()
    {
        var actor = Fight().Actors[0] with
        {
            Skills =
            [
                new SkillSnapshot(15280143, 350, 1, 0, 350, 0.35),
                new SkillSnapshot(15280144, 250, 1, 0, 250, 0.25),
                new SkillSnapshot(15290000, 400, 1, 0, 400, 0.40),
            ],
        };

        var share = 60.0.ToString("0.0", CultureInfo.CurrentCulture);
        Assert.Equal($"Tu skill top: Skill 15280000 ({share}%)", FightCardViewModel.TopSkillText(actor, SkillNames.Empty));
        var names = new SkillNames(new Dictionary<uint, string> { [15280143] = "Variante", [15280000] = "Base" });
        Assert.Equal($"Tu skill top: Base ({share}%)", FightCardViewModel.TopSkillText(actor, names));
    }

    [Fact]
    public void Importing_a_profile_preserves_the_installations_welcome_and_news_state()
    {
        var current = new MeterPreferences { WelcomeDone = true, LastSeenNews = 37 };
        var imported = ProfileFile.FromJson(ProfileFile.ToJson(new MeterPreferences { WelcomeDone = false, LastSeenNews = 1 }), current);

        Assert.NotNull(imported);
        Assert.True(imported.WelcomeDone);
        Assert.Equal(37, imported.LastSeenNews);
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("{\"skills\":42}", 0)]
    [InlineData("{\"skills\":{\"15280000\":\"Primero\",\"15280000\":\"Último\",\"15290000\":42}}", 1)]
    public void Welcome_count_matches_names_loaded_from_the_same_skill_file(string json, int count)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hysteria-skills-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            Assert.Equal(count, GameDataSummary.CountSkills(path));
            Assert.Equal(count == 1 ? "Último" : "Skill 15280000", SkillNames.Load(path).Of(15280000));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Current_encounter_id_and_card_expiry_follow_the_real_engine()
    {
        var session = Session();
        Assert.Null(session.Engine.CurrentEncounterId());
        using var card = new DetachedCard(new FightCardViewModel(Fight(), session, bossOnly: false));
        Assert.False(card.Value.IsStale());
        session.Engine.Apply(new LocalPlayerEvent(Start, 1, "Yo"));
        session.Engine.Apply(new DamageEvent(Start, 1, 900, 100, 400, HitModifiers.None));
        Assert.Equal(session.Engine.CurrentSnapshot()!.Id, session.Engine.CurrentEncounterId());
        Assert.True(card.Value.IsStale());
        session.Engine.Reset();
        Assert.Null(session.Engine.CurrentEncounterId());
    }

    [Fact]
    public void Welcome_help_is_a_single_line_with_the_configured_shortcut()
    {
        var line = Assert.Single(WelcomeViewModel.TipsFor(new MeterPreferences { HotkeyClickThrough = "Ctrl+Alt+P" }));
        Assert.Contains("Clic en una fila", line, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Alt+P", line, StringComparison.Ordinal);
    }

    private static MeterSession Session() =>
        new(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { });

    private static EncounterSnapshot Fight() =>
        new(Guid.NewGuid(), Start, Start.AddSeconds(10), "Boss de la tarjeta", true, 4000,
            [new ActorSnapshot(1, "Yo", null, true, 4000, 400, 0, 1, 0, 4000, 0, [], MainTargetDamage: 4000, MainTargetDps: 400)], TargetIsBoss: true);

    private sealed class DetachedCard(FightCardViewModel card) : IDisposable
    {
        public FightCardViewModel Value { get; } = card;

        public void Dispose() => Value.Detach();
    }
}

[Collection(InicioWindowsGroup.Name)]
public sealed class InicioResumeWindowTests
{
    [Fact]
    public void News_banner_and_all_bundled_barlow_weights_load()
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();
            var file = Path.Combine(Path.GetTempPath(), $"hysteria-news-{Guid.NewGuid():N}.json");
            var previous = JsonSerializer.Serialize(new { news = NewsFeed.Current.Select(n => new { id = n.Id, text = n.Text }) });
            try
            {
                File.WriteAllText(file, """{"news":[{"id":37,"text":"Una novedad real de prueba"}]}""");
                NewsFeed.Load(file);
                var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { });
                var vm = new MeterViewModel(session);
                Assert.True(vm.HasNotice);
                var overlay = new OverlayWindow(vm);
                var content = (FrameworkElement)overlay.Content;
                content.Measure(new Size(800, 600));
                content.Arrange(new Rect(0, 0, 800, 600));
                content.UpdateLayout();
                overlay.Close();
                vm.DismissNoticeCommand.Execute(null);
                Assert.False(vm.HasNotice);

                // Crear el menú no muestra un NotifyIcon ni abre placas de red.
                using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);
                Assert.Equal(
                    ["Ocultar overlay", "Reiniciar la pelea", "Historial", "Configuración", "Abrir la carpeta de datos", "Salir"],
                    menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Select(i => i.Text));

                foreach (var family in new[] { (FontFamily)app.FindResource("NumberFont"), (FontFamily)app.FindResource("TitleFont"), MeterViewModel.FontOf("Barlow Semi Condensed") })
                {
                    foreach (var weight in new[] { FontWeights.Normal, FontWeights.SemiBold, FontWeights.Bold })
                    {
                        var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                        Assert.True(typeface.TryGetGlyphTypeface(out var glyph));
                        Assert.Equal(weight, glyph.Weight);
                        Assert.Contains("Barlow", glyph.FamilyNames.Values.First(), StringComparison.Ordinal);
                    }
                }
            }
            finally
            {
                File.WriteAllText(file, previous);
                NewsFeed.Load(file);
                File.Delete(file);
            }
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
