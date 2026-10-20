using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Lo que suma la ventana de configuración (Config/): el máximo de jugadores con "Todos", el atajo para cambiar de
/// pestaña, la placa de red, los datos del juego y la vista previa de los temas. Sin App: la ventana en sí la carga
/// WindowSmokeTests.
/// </summary>
public sealed class ConfigTests
{
    [Fact]
    public void Max_players_keeps_all_and_remembers_the_last_limit()
    {
        RunOnSta(() =>
        {
            var (session, vm) = Meter(new MeterPreferences { MaxRows = 0 });
            Assert.True(vm.ShowAllRows);

            // Con "Todos" el slider no escribe: su mínimo (4) no pisa el 0 guardado.
            vm.RowLimit = (int)MeterViewModel.MinRowLimit;
            Assert.Equal(0, session.Preferences.MaxRows);

            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.ShowAllRows = false;
            Assert.Equal(8, session.Preferences.MaxRows);
            Assert.Contains(nameof(MeterViewModel.RowLimit), changed);

            vm.RowLimit = 12;
            vm.ShowAllRows = true;
            Assert.Equal(0, session.Preferences.MaxRows);
            vm.ShowAllRows = false;
            Assert.Equal(12, session.Preferences.MaxRows);

            // Fuera del rango del slider se acota.
            vm.RowLimit = 99;
            Assert.Equal(MeterViewModel.MaxRowLimit, session.Preferences.MaxRows);

            // Un límite viejo (las opciones eran 0, 5, 8 y 12) se ve tal cual.
            Assert.Equal(5, Meter(new MeterPreferences { MaxRows = 5 }).ViewModel.RowLimit);
        });
    }

    [Fact]
    public void Tab_hotkey_starts_empty_and_follows_the_hotkey_rules()
    {
        RunOnSta(() =>
        {
            var (session, vm) = Meter(new MeterPreferences());
            Assert.Equal(string.Empty, vm.HotkeyTab);

            vm.HotkeyTab = "shift+ctrl+t";
            Assert.Equal("Ctrl+Shift+T", session.Preferences.HotkeyTab);

            // Repetido con otro atajo: no se guarda, en ninguna de las dos direcciones.
            vm.HotkeyTab = session.Preferences.HotkeyReset;
            Assert.Equal("Ctrl+Shift+T", session.Preferences.HotkeyTab);
            Assert.Contains("ya es otro atajo", vm.HotkeyMessage, StringComparison.Ordinal);
            vm.HotkeyMark = "Ctrl+Shift+T";
            Assert.Equal("Ctrl+Shift+M", session.Preferences.HotkeyMark);

            vm.HotkeyTab = string.Empty;
            Assert.Equal(string.Empty, session.Preferences.HotkeyTab);
        });
    }

    [Fact]
    public void Cycle_tab_goes_dps_healing_taken_and_back()
    {
        RunOnSta(() =>
        {
            var vm = Meter(new MeterPreferences()).ViewModel;
            Assert.Equal(MeterTab.Damage, vm.Tab);
            vm.CycleTabCommand.Execute(null);
            Assert.Equal(MeterTab.Healing, vm.Tab);
            vm.CycleTabCommand.Execute(null);
            Assert.Equal(MeterTab.Taken, vm.Tab);
            vm.CycleTabCommand.Execute(null);
            Assert.Equal(MeterTab.Damage, vm.Tab);
        });
    }

    [Fact]
    public void Capture_devices_list_automatic_first_and_keep_a_missing_choice()
    {
        (string Name, string Description)[] devices = [(@"\Device\NPF_{A}", "Intel Ethernet"), (@"\Device\NPF_{B}", string.Empty)];

        var options = CaptureDevices.Options(devices, string.Empty);
        Assert.Equal(["Automática", "Intel Ethernet", @"\Device\NPF_{B}"], options.Select(o => o.Label));
        Assert.Equal(string.Empty, options[0].Name);

        var missing = CaptureDevices.Options(devices, @"\Device\NPF_{C}");
        Assert.Equal(@"\Device\NPF_{C}", missing[^1].Name);
        Assert.EndsWith("(no está conectada)", missing[^1].Label, StringComparison.Ordinal);

        // La elegida le gana a appsettings mientras siga conectada; si no, queda la de appsettings (vacía = automática).
        Assert.Equal(@"\Device\NPF_{B}", CaptureDevices.Resolve(@"\Device\NPF_{B}", "Intel", devices));
        Assert.Equal("Intel", CaptureDevices.Resolve(@"\Device\NPF_{C}", "Intel", devices));
        Assert.Null(CaptureDevices.Resolve(@"\Device\NPF_{C}", null, devices));
    }

    [Fact]
    public void Capture_device_description_names_it_and_says_where_it_came_from()
    {
        (string Name, string Description)[] devices = [(@"\Device\NPF_{A}", "Intel Ethernet"), (@"\Device\NPF_{B}", string.Empty)];

        // La elegida que quedó aplicada, con su descripción de Npcap; sin descripción no hay etiqueta (va el nombre).
        Assert.Equal(("Intel Ethernet", CaptureDeviceOrigin.Preferences), CaptureDevices.Describe(@"\Device\NPF_{A}", @"\Device\NPF_{A}", devices));
        Assert.Equal(((string?)null, CaptureDeviceOrigin.Preferences), CaptureDevices.Describe(@"\Device\NPF_{B}", @"\Device\NPF_{B}", devices));

        // La de appsettings se busca como la busca la captura: parte del nombre o de la descripción, sin mayúsculas.
        Assert.Equal(("Intel Ethernet", CaptureDeviceOrigin.AppSettings), CaptureDevices.Describe(string.Empty, "intel", devices));
        Assert.Equal(("Intel Ethernet", CaptureDeviceOrigin.AppSettings), CaptureDevices.Describe(string.Empty, "NPF_{A}", devices));
        Assert.Equal(((string?)null, CaptureDeviceOrigin.AppSettings), CaptureDevices.Describe(string.Empty, "Realtek", devices));
        Assert.Equal(((string?)null, CaptureDeviceOrigin.Automatic), CaptureDevices.Describe(string.Empty, null, devices));

        // La guardada ya no está: la captura siguió con la de appsettings o la automática, y eso es lo que se dice.
        const string Missing = @"\Device\NPF_{C}";
        Assert.Equal(
            ("Intel Ethernet", CaptureDeviceOrigin.AppSettings),
            CaptureDevices.Describe(CaptureDevices.AppliedChoice(Missing, CaptureMode.Live, "Intel"), CaptureDevices.Resolve(Missing, "Intel", devices), devices));
        Assert.Equal(
            ((string?)null, CaptureDeviceOrigin.Automatic),
            CaptureDevices.Describe(CaptureDevices.AppliedChoice(Missing, CaptureMode.Live, null), CaptureDevices.Resolve(Missing, null, devices), devices));
    }

    [Fact]
    public void Choosing_another_capture_device_asks_for_a_restart()
    {
        RunOnSta(() =>
        {
            var previous = CaptureDevices.Source;
            CaptureDevices.Source = () => [(@"\Device\NPF_{A}", "Intel Ethernet")];
            try
            {
                var (session, vm) = Meter(new MeterPreferences());
                Assert.Equal(CaptureDevices.AutomaticLabel, vm.SelectedCaptureDevice.Label);
                Assert.False(vm.CaptureRestartPending);

                vm.SelectedCaptureDevice = vm.CaptureDeviceOptions[1];
                Assert.Equal(@"\Device\NPF_{A}", session.Preferences.CaptureDevice);
                Assert.True(vm.CaptureRestartPending);

                vm.SelectedCaptureDevice = vm.CaptureDeviceOptions[0];
                Assert.Equal(string.Empty, session.Preferences.CaptureDevice);
                Assert.False(vm.CaptureRestartPending);
            }
            finally
            {
                CaptureDevices.Source = previous;
            }
        });
    }

    [Fact]
    public void Profile_does_not_carry_the_capture_device()
    {
        var json = ProfileFile.ToJson(new MeterPreferences { CaptureDevice = @"\Device\NPF_{A}" });
        Assert.DoesNotContain("NPF_", json, StringComparison.Ordinal);

        var imported = ProfileFile.FromJson(json, new MeterPreferences { CaptureDevice = @"\Device\NPF_{B}" });
        Assert.NotNull(imported);
        Assert.Equal(@"\Device\NPF_{B}", imported.CaptureDevice);
    }

    [Fact]
    public void Applied_capture_choice_tracks_the_device_actually_used()
    {
        Assert.Equal("", CaptureDevices.AppliedChoice("missing", CaptureMode.Live, "configured"));
        Assert.Equal("", CaptureDevices.AppliedChoice("missing", CaptureMode.Live, null));
        Assert.Equal("connected", CaptureDevices.AppliedChoice("connected", CaptureMode.Live, "connected"));
        Assert.Equal("saved", CaptureDevices.AppliedChoice("saved", CaptureMode.Replay, null));
    }

    [Fact]
    public void Config_placement_fits_the_owner_monitor_and_keeps_offscreen_harness_offscreen()
    {
        var placement = ConfigWindow.Placement(new Rect(1900, 100, 356, 400), new Rect(1600, 0, 1092, 614), new Size(1040, 700), new Size(880, 480));
        Assert.Equal(new Rect(1626, 0, 1040, 614), placement);
        var offscreen = ConfigWindow.Placement(new Rect(-10000, -10000, 356, 400), new Rect(0, 0, 1920, 1080), new Size(1040, 700), new Size(880, 480));
        Assert.True(offscreen.Right < 0);
        Assert.True(offscreen.Bottom < 0);
    }

    [Fact]
    public void Game_data_counts_named_skills_icons_and_emblems()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"hysteria-juego-{Guid.NewGuid():N}");
        try
        {
            var missing = GameDataSummary.Read(folder);
            Assert.Equal((false, 0, 0, 0), (missing.Exists, missing.Skills, missing.Icons, missing.Emblems));

            Directory.CreateDirectory(Path.Combine(folder, "iconos"));
            Directory.CreateDirectory(Path.Combine(folder, "clases"));
            File.WriteAllText(Path.Combine(folder, "skills.json"), """
                // como lo deja hysteria-extract
                { "skills": { "100": "Golpe", "200": "", "abc": "Sin ID", "300": "Corte", } }
                """);
            foreach (var name in new[] { "100.png", "300.png", "fondo.png", "100.txt" })
            {
                File.WriteAllText(Path.Combine(folder, "iconos", name), string.Empty);
            }

            File.WriteAllText(Path.Combine(folder, "clases", "11.png"), string.Empty);

            var summary = GameDataSummary.Read(folder);
            Assert.Equal((true, 2, 2, 1), (summary.Exists, summary.Skills, summary.Icons, summary.Emblems));

            // Un skills.json roto no cuenta (el meter tampoco lo usa).
            File.WriteAllText(Path.Combine(folder, "skills.json"), "{ no es json");
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
    public void Theme_card_preview_uses_the_palette()
    {
        RunOnSta(() =>
        {
            var gold = Color.FromRgb(0xE8, 0xB0, 0x4A);
            var card = ThemeCard.From(MeterThemes.All[0], new Dictionary<string, Color> { ["WindowBg"] = Color.FromRgb(0x10, 0x12, 0x18), ["Gold"] = gold });
            Assert.Equal(Color.FromRgb(0x10, 0x12, 0x18), ((SolidColorBrush)card.Ground).Color);
            Assert.Equal(Color.FromArgb(0x99, gold.R, gold.G, gold.B), ((SolidColorBrush)card.Bar).Color);
            Assert.True(card.Ground.IsFrozen);

            var empty = ThemeCard.From(MeterThemes.All[0], new Dictionary<string, Color>());
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)empty.Bar).Color);
        });
    }

    [Fact]
    public void Auxiliary_windows_remain_open_when_copying_demoing_or_opening_a_fight()
    {
        RunOnSta(() =>
        {
            var vm = Meter(new MeterPreferences()).ViewModel;
            vm.IsHistoryOpen = true;
            vm.ToggleSettingsCommand.Execute(null);
            Assert.True(vm.IsHistoryOpen);
            vm.CopyImageCommand.Execute(null);
            Assert.True(vm.IsSettingsOpen && vm.IsHistoryOpen);
            vm.ShowDemoCommand.Execute(null);
            Assert.True(vm.IsSettingsOpen && vm.IsHistoryOpen);

            var file = Path.Combine(Path.GetTempPath(), $"hysteria-config-integration-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(file, HysteriaMeter.Storage.EncounterExport.ToJson(DemoEncounter.Create(DateTimeOffset.UnixEpoch)));
                Assert.True(vm.OpenSharedFile(file));
                Assert.True(vm.IsSettingsOpen && vm.IsHistoryOpen);
            }
            finally
            {
                File.Delete(file);
                vm.Stop();
            }

            vm.IsMinimized = true;
            Assert.False(vm.IsSettingsOpen || vm.IsHistoryOpen);
        });
    }

    [Fact]
    public void Factory_view_resets_the_rows_and_compact_mode_and_keeps_the_rest()
    {
        RunOnSta(() =>
        {
            // Columnas prendidas a mano después del paso a la vista del rediseño: el link vuelve a la de fábrica.
            var old = new MeterPreferences
            {
                Sparklines = true,
                ShowGear = true,
                ColorByClass = false,
                ShowTotalColumn = true,
                ShowShareColumn = false,
                ShowRecentColumn = true,
                ShowCritColumn = true,
                ShowMaxHitColumn = true,
                OnlyMe = true,
                Theme = "Abismo",
                TextFont = "Segoe UI",
                Opacity = 0.7,
                RowHeight = 36,
                MaxRows = 8,
                HotkeyReset = "Ctrl+Shift+F9",
                CaptureDevice = @"\Device\NPF_{prueba}",
                LastSeenNews = 100,
                UiScale = 1.25,
            };
            var (session, vm) = Meter(old);
            vm.IsCompact = true;
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.ResetViewCommand.Execute(null);

            // Filas y columnas con los valores de una instalación nueva; lo demás queda como estaba.
            var fresh = new MeterPreferences();
            var expected = old with
            {
                Sparklines = fresh.Sparklines,
                ShowGear = fresh.ShowGear,
                ColorByClass = fresh.ColorByClass,
                ShowTotalColumn = fresh.ShowTotalColumn,
                ShowShareColumn = fresh.ShowShareColumn,
                ShowRecentColumn = fresh.ShowRecentColumn,
                ShowCritColumn = fresh.ShowCritColumn,
                ShowMaxHitColumn = fresh.ShowMaxHitColumn,
                OnlyMe = fresh.OnlyMe,
            };
            Assert.Equal(expected, session.Preferences);
            Assert.False(session.Preferences.Sparklines || session.Preferences.ShowGear || session.Preferences.ShowTotalColumn);
            Assert.True(session.Preferences.ShowShareColumn && session.Preferences.ColorByClass);
            Assert.Equal("Abismo", session.Preferences.Theme);
            Assert.Equal(36, session.Preferences.RowHeight);

            // Esta ventana sale del compacto y, con solo DPS y %, sin encabezado de columnas.
            Assert.False(vm.IsCompact);
            Assert.False(vm.ShowColumnHeaders);
            Assert.Contains(nameof(MeterViewModel.ShowSparklines), changed);
            Assert.Contains(nameof(MeterViewModel.ShowCritColumn), changed);
            Assert.Contains(nameof(MeterViewModel.IsCompact), changed);
        });
    }

    [Fact]
    public void Saved_view_from_before_the_redesign_moves_to_the_mock_once()
    {
        // Lo que tenía guardado un usuario de antes del rediseño: compacto, ancho, columnas y línea de DPS.
        var old = new MeterPreferences
        {
            BossOnlyDefaulted = true,
            Sparklines = true,
            ShowGear = true,
            ShowTotalColumn = true,
            ShowCritColumn = true,
            ShowMaxHitColumn = true,
            Theme = "Abismo",
            Opacity = 0.7,
            RowHeight = 36,
        };
        var window = new OverlayLayout(-682, 492, 586, 1.1, Height: 279, Tab: MeterTab.Healing, ShowHealing: true, Compact: true, BossOnly: true);

        var (preferences, layouts) = OverlayWindows.Migrate(old, [window]);

        Assert.Equal(old.WithFactoryView() with { RedesignViewApplied = true }, preferences);
        Assert.False(preferences.Sparklines || preferences.ShowGear || preferences.ShowTotalColumn || preferences.ShowCritColumn || preferences.ShowMaxHitColumn);
        Assert.Equal(("Abismo", 0.7, 36), (preferences.Theme, preferences.Opacity, preferences.RowHeight));

        // La ventana sale del compacto con el ancho y el alto del mock, en el mismo lugar y con lo que mostraba.
        var migrated = Assert.Single(layouts);
        Assert.Equal(window with { Compact = false, Width = OverlayLayout.FactoryWidth, Height = 0 }, migrated);
        Assert.Equal(380, OverlayLayout.FactoryWidth);

        // Una sola vez: lo que se vuelva a prender después se respeta.
        var later = preferences with { ShowCritColumn = true };
        var compact = migrated with { Compact = true, Width = 500 };
        var again = OverlayWindows.Migrate(later, [compact]);
        Assert.Same(later, again.Preferences);
        Assert.Equal(compact, Assert.Single(again.Layouts));
    }

    [Fact]
    public void Migration_folds_boss_only_and_leaves_a_new_install_as_it_is()
    {
        // Muy vieja: tampoco tenía "solo boss" prendido en sus ventanas.
        var fresh = new MeterPreferences();
        var (oldest, layouts) = OverlayWindows.Migrate(fresh, [new OverlayLayout(0, 0, 380, 1, BossOnly: false)]);
        Assert.True(oldest.BossOnlyDefaulted && oldest.RedesignViewApplied);
        Assert.True(Assert.Single(layouts).BossOnly);

        // Una instalación nueva ya tiene la vista de fábrica: solo quedan las marcas.
        Assert.Equal(fresh with { BossOnlyDefaulted = true, RedesignViewApplied = true }, oldest);
        Assert.Empty(OverlayWindows.Migrate(new MeterPreferences(), []).Layouts);
    }

    [Fact]
    public void Imported_profile_does_not_repeat_the_redesign_view()
    {
        var json = ProfileFile.ToJson(new MeterPreferences { ShowCritColumn = true });
        var imported = ProfileFile.FromJson(json, new MeterPreferences { RedesignViewApplied = true });
        Assert.NotNull(imported);
        Assert.True(imported.RedesignViewApplied);
        Assert.True(imported.ShowCritColumn);
    }

    private static (MeterSession Session, MeterViewModel ViewModel) Meter(MeterPreferences preferences)
    {
        var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, preferences, _ => { });
        return (session, new MeterViewModel(session));
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
