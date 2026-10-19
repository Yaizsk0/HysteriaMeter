using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// La verificación por objetivo (TargetVerification) en Configuración → Diagnóstico, en el tooltip de la vida del
/// objetivo y en el reporte de un problema.
/// </summary>
public sealed class VerificacionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 21, 0, 0, TimeSpan.Zero);

    // Nombres inventados: el reporte va a un issue público y no tiene que llevar ninguno de los dos.
    private const string BossName = "Jefe de prueba";
    private const string PlayerName = "Jugador de prueba";

    private const uint BossNpc = 500;

    [Fact]
    public void Health_tip_shows_the_verification_of_the_main_target()
    {
        // Números de menos de mil: el formato compacto no cambia con el estilo de números ni con la cultura.
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var closes = new TargetVerification(900, BossNpc, StartHp: 1_000, EndHp: 0, MaxHp: 1_000, Counted: 1_050, Overkill: 50, Difference: 0, Closes: true);
        var missing = new TargetVerification(900, BossNpc, 1_000, 200, 1_000, Counted: 600, Overkill: 0, Difference: -200, Closes: false);
        var extra = new TargetVerification(900, BossNpc, 1_000, 600, 1_000, Counted: 500, Overkill: 0, Difference: 100, Closes: false);
        var add = new TargetVerification(901, 777, 1_000, 200, 1_000, 600, 0, -200, false);

        Assert.Null(MeterViewModel.HealthCheck(Fight(BossNpc)));
        Assert.Equal("Verificación: cierra", MeterViewModel.HealthCheck(Fight(BossNpc, add, closes)));
        Assert.Equal("Verificación: no cierra, falta 200 (25.0 % de la vida perdida)", MeterViewModel.HealthCheck(Fight(BossNpc, missing)));
        Assert.Equal("Verificación: no cierra, sobra 100 (25.0 % de la vida perdida)", MeterViewModel.HealthCheck(Fight(BossNpc, extra)));

        // Sin objetivo principal con plantilla, o sin verificación de esa plantilla: no hay línea.
        Assert.Null(MeterViewModel.HealthCheck(Fight(null, closes)));
        Assert.Null(MeterViewModel.HealthCheck(Fight(BossNpc + 1, closes, add)));
    }

    [Fact]
    public void Diagnostics_count_the_targets_that_close()
    {
        var fight = Fight(BossNpc, Closing(900, BossNpc), Missing(901, 777), Closing(902, 778));

        Assert.Equal("Cierran 2 de 3 objetivos", MeterViewModel.VerificationSummary(fight, saved: false));
        Assert.Equal("Cierra 0 de 1 objetivo", MeterViewModel.VerificationSummary(Fight(BossNpc, Missing(901, 777)), saved: true));
    }

    [Fact]
    public void Diagnostics_list_each_target_in_order()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var fight = Fight(BossNpc, Closing(900, BossNpc), Missing(901, 777), Closing(902, 778), Missing(34_157, null));
        static string? Names(uint npc) => npc == 777 ? "Add de prueba" : null;

        var lines = MeterViewModel.VerificationLines(fight, Names).Split('\n');

        Assert.Equal(
        [
            // El principal sin nombre en la tabla toma el del objetivo de la pelea.
            $"{BossName} · cierra · vida 10,000 → 0 · contado 10,500 · overkill 500 · diferencia 0 (0.0 % de la vida perdida)",
            "Add de prueba · no cierra · vida 10,000 → 2,000 · contado 6,000 · diferencia -2,000 (-25.0 % de la vida perdida)",
            "NPC 778 · cierra · vida 10,000 → 0 · contado 10,500 · overkill 500 · diferencia 0 (0.0 % de la vida perdida)",
            // Sin plantilla, el id de la entidad.
            "entidad 34157 · no cierra · vida 10,000 → 2,000 · contado 6,000 · diferencia -2,000 (-25.0 % de la vida perdida)",
        ], lines);
    }

    [Fact]
    public void Diagnostics_show_extra_damage_with_a_plus_sign()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var extra = new TargetVerification(900, BossNpc, 1_000, 600, 1_000, Counted: 500, Overkill: 0, Difference: 100, Closes: false);

        Assert.EndsWith("diferencia +100 (+25.0 % de la vida perdida)", MeterViewModel.VerificationLines(Fight(BossNpc, extra), _ => null), StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_cut_the_list_of_targets()
    {
        var many = Enumerable.Range(0, MeterViewModel.VerificationLineLimit + 3).Select(i => Closing(1_000 + (ulong)i, 2_000 + (uint)i)).ToArray();

        var lines = MeterViewModel.VerificationLines(Fight(BossNpc, many), _ => null).Split('\n');

        Assert.Equal(MeterViewModel.VerificationLineLimit + 1, lines.Length);
        Assert.StartsWith("NPC 2009 ", lines[^2], StringComparison.Ordinal);
        Assert.Equal("y 3 más", lines[^1]);
        Assert.Equal("Cierran 13 de 13 objetivos", MeterViewModel.VerificationSummary(Fight(BossNpc, many), saved: false));
    }

    [Fact]
    public void Diagnostics_explain_why_there_is_no_verification()
    {
        var none = MeterViewModel.VerificationSummary(null, saved: false);
        var live = MeterViewModel.VerificationSummary(Fight(BossNpc), saved: false);
        var saved = MeterViewModel.VerificationSummary(Fight(BossNpc), saved: true);

        Assert.StartsWith("Sin pelea", none, StringComparison.Ordinal);
        Assert.Contains("vida del servidor conocida", live, StringComparison.Ordinal);
        // La base guarda null igual para una pelea de antes de la verificación y para una sin objetivos verificables.
        Assert.Contains("antes de que existiera", saved, StringComparison.Ordinal);
        Assert.Contains("vida del servidor conocida", saved, StringComparison.Ordinal);
        Assert.Equal(3, new[] { none, live, saved }.Distinct().Count());
        Assert.Equal(string.Empty, MeterViewModel.VerificationLines(null, _ => null));
        Assert.Equal(string.Empty, MeterViewModel.VerificationLines(Fight(BossNpc), _ => null));
    }

    [Fact]
    public void Problem_report_has_the_verification_without_names()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var preferences = new MeterPreferences();
        var closes = Fight(BossNpc, Closing(900, BossNpc), Missing(901, 777));
        var fails = Fight(BossNpc, Missing(900, BossNpc), Closing(901, 777));

        var closing = BugReport.Build("v0", "Windows 11", "En combate", "frames 10", preferences, closes);
        var failing = BugReport.Build("v0", "Windows 11", "En combate", "frames 10", preferences, fails);

        Assert.Contains("- Verificación: Cierra 1 de 2 objetivos · principal NPC 500: cierra, diferencia 0 (0.0 % de la vida perdida)" + Environment.NewLine, closing, StringComparison.Ordinal);
        Assert.Contains("- Verificación: Cierra 1 de 2 objetivos · principal NPC 500: no cierra, diferencia -2,000 (-25.0 % de la vida perdida)" + Environment.NewLine, failing, StringComparison.Ordinal);
        foreach (var report in new[] { closing, failing })
        {
            Assert.DoesNotContain(BossName, report, StringComparison.Ordinal);
            Assert.DoesNotContain(PlayerName, report, StringComparison.Ordinal);

            // Entre el diagnóstico y las preferencias, sin mover las otras líneas.
            var diagnostics = report.IndexOf("- Diagnóstico:", StringComparison.Ordinal);
            var verification = report.IndexOf("- Verificación:", StringComparison.Ordinal);
            var preferencesLine = report.IndexOf("- Preferencias:", StringComparison.Ordinal);
            Assert.True(diagnostics < verification && verification < preferencesLine);
        }
    }

    [Fact]
    public void Problem_report_without_verification_leaves_a_dash()
    {
        var preferences = new MeterPreferences();

        Assert.Contains("- Verificación: –" + Environment.NewLine, BugReport.Build("v0", "W", "s", "d", preferences, null), StringComparison.Ordinal);
        Assert.Contains("- Verificación: –" + Environment.NewLine, BugReport.Build("v0", "W", "s", "d", preferences, Fight(BossNpc)), StringComparison.Ordinal);

        // Sin verificación del principal, solo la cuenta.
        var other = BugReport.Build("v0", "W", "s", "d", preferences, Fight(BossNpc, Closing(901, 777)));
        Assert.Contains("- Verificación: Cierra 1 de 1 objetivo" + Environment.NewLine, other, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_verify_the_fight_also_in_the_run_view()
    {
        // La verificación es por pelea: en la vista RUN (que suma peleas y no la tiene) es la de la pelea en curso.
        RunOnSta(() =>
        {
            var session = Session();
            var engine = session.Engine;
            engine.Apply(new LocalPlayerEvent(Start, 1, "Prueba"));
            engine.Apply(new EntityInfoEvent(Start, 1, "Prueba", 11));
            engine.Apply(new EntitySpawnEvent(Start, 900, null, BossNpc, Hp: 10_000, MaxHp: 10_000));
            engine.Apply(new DamageEvent(Start, 1, 900, 11_010_000, 3_000, HitModifiers.None));
            engine.Apply(new HealthEvent(Start, 900, 7_000));

            var run = new MeterViewModel(session, runView: true);
            run.ToggleSettingsCommand.Execute(null);
            run.IsPaused = true;
            run.IsPaused = false;
            Assert.Equal("Cierra 1 de 1 objetivo", run.Verification);
            Assert.StartsWith("NPC 500 · cierra · ", run.VerificationDetail, StringComparison.Ordinal);
            Assert.Contains("- Verificación: Cierra 1 de 1 objetivo · principal NPC 500: cierra", run.BuildProblemReport(), StringComparison.Ordinal);

            // El tooltip de la vida no: el objetivo principal de la run puede no ser el de la pelea.
            Assert.Null(run.TargetHealthCheck);

            // En la vista de pelea, también el tooltip de la vida.
            var fight = new MeterViewModel(session);
            fight.IsPaused = true;
            fight.IsPaused = false;
            Assert.EndsWith(Environment.NewLine + "Verificación: cierra", fight.TargetHealthTip, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Shared_file_with_null_verifications_opens_without_failing()
    {
        // Un .json editado a mano puede traer "Verifications": [null]: System.Text.Json deja el null en la lista.
        RunOnSta(() =>
        {
            var vm = new MeterViewModel(Session());
            vm.ToggleSettingsCommand.Execute(null);
            var file = Path.Combine(Path.GetTempPath(), $"hysteria-verificacion-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(file, EncounterExport.ToJson(Fight(BossNpc, [null!, Closing(900, BossNpc)])));
                Assert.True(vm.OpenSharedFile(file));
                Assert.Equal("Cierra 1 de 1 objetivo", vm.Verification);
                Assert.StartsWith($"{BossName} · cierra · ", vm.VerificationDetail, StringComparison.Ordinal);
                Assert.Equal("Verificación: cierra", vm.TargetHealthCheck);
                Assert.Contains("- Verificación: Cierra 1 de 1 objetivo · principal NPC 500: cierra", vm.BuildProblemReport(), StringComparison.Ordinal);

                // Solo nulls: como una pelea guardada sin verificación.
                File.WriteAllText(file, EncounterExport.ToJson(Fight(BossNpc, [null!])));
                Assert.True(vm.OpenSharedFile(file));
                Assert.Contains("antes de que existiera", vm.Verification, StringComparison.Ordinal);
                Assert.Equal(string.Empty, vm.VerificationDetail);
                Assert.Null(vm.TargetHealthCheck);
                Assert.Contains("- Verificación: –" + Environment.NewLine, vm.BuildProblemReport(), StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(file);
                vm.Stop();
            }
        });
    }

    /// <summary>10.000 → 0 con 10.500 contados y 500 de overkill: cierra exacto.</summary>
    private static TargetVerification Closing(ulong id, uint? npc) =>
        new(id, npc, StartHp: 10_000, EndHp: 0, MaxHp: 10_000, Counted: 10_500, Overkill: 500, Difference: 0, Closes: true);

    /// <summary>10.000 → 2.000 con 6.000 contados: faltan 2.000, el 25 % de la vida perdida.</summary>
    private static TargetVerification Missing(ulong id, uint? npc) =>
        new(id, npc, StartHp: 10_000, EndHp: 2_000, MaxHp: 10_000, Counted: 6_000, Overkill: 0, Difference: -2_000, Closes: false);

    private static EncounterSnapshot Fight(uint? targetNpc, params TargetVerification[] verifications) => new(
        Guid.NewGuid(), Start, Start.AddSeconds(30), BossName, TargetKilled: false, TotalDamage: 10_000,
        [new ActorSnapshot(7, PlayerName, null, true, 10_000, 333, 1, 10, 2, 2_000, 0, [])],
        TargetNpcId: targetNpc,
        Verifications: verifications.Length > 0 ? verifications : null);

    private static MeterSession Session()
    {
        var status = new CaptureStatus(DateTime.UtcNow);
        status.MarkTraffic(DateTime.UtcNow);
        return new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { }, status: status);
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
