using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using HysteriaMeter.App.Config;
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
