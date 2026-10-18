using System.Runtime.ExceptionServices;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Decoding;

namespace HysteriaMeter.App.Tests;

/// <summary>Contadores de salud de la captura en Configuración → Diagnóstico y en el reporte de un problema.</summary>
public sealed class DiagnosticoTests
{
    // Distintos entre sí y de menos de mil: si se cruzan los argumentos el texto cambia, y no dependen del separador de
    // miles de la cultura.
    private const string Counters = "resincronizaciones 1 · desincronizaciones 3 · descartados 7";

    [Fact]
    public void Diagnostics_show_desyncs_and_dropped_segments()
    {
        var text = MeterViewModel.DescribeDecoder(frames: 900, unknown: 100, errors: 2, resyncs: 1, desyncs: 3, dropped: 7);

        Assert.Contains("errores 2 · " + Counters, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_show_the_session_counters()
    {
        RunOnSta(() =>
        {
            var vm = new MeterViewModel(Session());
            vm.ToggleSettingsCommand.Execute(null);

            // El Refresh() real (y UpdateStatus): OnIsPausedChanged(false) lo llama.
            vm.IsPaused = true;
            vm.IsPaused = false;

            Assert.Contains(Counters, vm.Diagnostics, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Problem_report_has_the_counters_without_opening_settings()
    {
        // El overlay también ofrece reportar (captura detenida, aviso de parche) sin pasar por Configuración.
        RunOnSta(() =>
        {
            var vm = new MeterViewModel(Session());
            vm.IsPaused = true;
            vm.IsPaused = false;
            Assert.False(vm.IsSettingsOpen);
            Assert.Equal(string.Empty, vm.Diagnostics);

            Assert.Contains("- Diagnóstico: Frames 0 · conocidos 0 · errores 2 · " + Counters, vm.BuildProblemReport(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Capture_status_keeps_the_last_dropped_count()
    {
        var status = new CaptureStatus(DateTime.UtcNow);
        Assert.Equal(0, status.Dropped);

        status.SetDropped(42);
        Assert.Equal(42, status.Dropped);
    }

    /// <summary>Una sesión con contadores conocidos, como los que publican el decoder y el tick del pipeline.</summary>
    private static MeterSession Session()
    {
        var status = new CaptureStatus(DateTime.UtcNow);
        status.MarkTraffic(DateTime.UtcNow);
        status.SetDropped(7);
        return new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { },
            status: status, decoder: new DecoderStats { Errors = 2, Resyncs = 1, Desyncs = 3 });
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
