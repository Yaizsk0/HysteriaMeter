using System.IO;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Capture.Sources;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Grabación retroactiva ("Guardar lo último"): los últimos 10 min del tráfico van a un .pcap a pedido o, si se prendió,
/// solo cuando una pelea no cierra. Sin Npcap: la captura en vivo se reemplaza por una función que anota el archivo.
/// </summary>
public sealed class RecentRecordingTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 30, 22, 15, 0, DateTimeKind.Local);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hysteria-grabaciones-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _requested = [];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Without_live_capture_there_is_nothing_to_save()
    {
        // En replay, o con la captura todavía sin arrancar, el pipeline no conecta ningún buffer.
        var recorder = new CaptureRecorder(_folder);

        Assert.False(recorder.CanSaveRecent);
        Assert.Null(recorder.SaveRecent(Now));
    }

    [Fact]
    public void Manual_save_goes_to_an_ultimo_file_in_the_recordings_folder()
    {
        var recorder = Connected();

        var saved = recorder.SaveRecent(Now);

        Assert.NotNull(saved);
        Assert.Equal(Path.Combine(_folder, "ultimo-20261030-221500.pcap"), saved.Path);
        Assert.Equal([saved.Path], _requested);
        Assert.Same(saved, recorder.LastSaved);
    }

    [Fact]
    public void Fight_where_every_target_closes_is_not_saved()
    {
        var recorder = Connected();

        Assert.Null(recorder.SaveIfMismatch(Fight(Verification(closes: true), Verification(closes: true)), Now));
        Assert.Null(recorder.SaveIfMismatch(Fight(), Now));
        Assert.Empty(_requested);
    }

    [Fact]
    public void Fight_where_any_target_does_not_close_is_saved_as_auto()
    {
        // Como el "Cierran 1 de 2 objetivos" de Diagnóstico: cuenta cualquier objetivo, no solo el principal (un add
        // que perdió vida sin golpes contados, como el Necromancer Duanka).
        var recorder = Connected();

        var saved = recorder.SaveIfMismatch(Fight(Verification(closes: true), Verification(closes: false)), Now);

        Assert.NotNull(saved);
        Assert.Equal(Path.Combine(_folder, "auto-20261030-221500.pcap"), saved.Path);
    }

    [Fact]
    public void Automatic_save_waits_the_whole_window_after_the_previous_one()
    {
        // Con 10 min de buffer, guardar antes repetiría casi todo; las peleas del medio caen en el siguiente archivo.
        var recorder = Connected();
        var mismatch = Fight(Verification(closes: false));

        Assert.NotNull(recorder.SaveIfMismatch(mismatch, Now));
        Assert.Null(recorder.SaveIfMismatch(mismatch, Now.AddMinutes(9)));
        Assert.NotNull(recorder.SaveIfMismatch(mismatch, Now.AddMinutes(10)));
        Assert.Equal(2, _requested.Count);
    }

    [Fact]
    public void Automatic_save_without_traffic_does_not_start_the_wait()
    {
        var empty = true;
        var recorder = new CaptureRecorder(_folder) { RecentSource = path => empty ? null : Write(path) };
        var mismatch = Fight(Verification(closes: false));

        Assert.Null(recorder.SaveIfMismatch(mismatch, Now));
        empty = false;

        Assert.NotNull(recorder.SaveIfMismatch(mismatch, Now.AddMinutes(1)));
    }

    [Fact]
    public void Automatic_save_is_skipped_while_REC_is_recording()
    {
        // La pelea ya queda en el pelea-*.pcap de ● REC.
        var recorder = Connected();
        recorder.Start(Now);

        Assert.Null(recorder.SaveIfMismatch(Fight(Verification(closes: false)), Now));
        Assert.Empty(_requested);
    }

    [Fact]
    public void Automatic_saves_keep_the_newest_twenty_and_never_touch_the_others()
    {
        Directory.CreateDirectory(_folder);
        for (var i = 0; i < CaptureRecorder.AutomaticKeep + 2; i++)
        {
            File.WriteAllBytes(Path.Combine(_folder, $"auto-20261029-{i:D2}0000.pcap"), []);
        }

        File.WriteAllBytes(Path.Combine(_folder, "pelea-20261001-100000.pcap"), []);
        File.WriteAllBytes(Path.Combine(_folder, "ultimo-20261001-100000.pcap"), []);
        var recorder = Connected();

        var saved = recorder.SaveIfMismatch(Fight(Verification(closes: false)), Now);

        var autos = Directory.GetFiles(_folder, "auto-*.pcap").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(CaptureRecorder.AutomaticKeep, autos.Count);
        Assert.Equal("auto-20261029-030000.pcap", autos[0]);
        Assert.Contains(Path.GetFileName(saved!.Path), autos);
        Assert.True(File.Exists(Path.Combine(_folder, "pelea-20261001-100000.pcap")));
        Assert.True(File.Exists(Path.Combine(_folder, "ultimo-20261001-100000.pcap")));
    }

    [Fact]
    public void Saved_text_says_how_much_the_file_really_covers()
    {
        // Recién abierto el meter el buffer tiene menos de 10 min: el aviso dice lo que hay.
        Assert.Equal("Últimos 7 min guardados en ultimo-1.pcap", CaptureRecorder.SavedText(new SavedPackets(@"C:\g\ultimo-1.pcap", 10, TimeSpan.FromMinutes(7.4))));
        Assert.Equal("Últimos 40 s guardados en ultimo-1.pcap", CaptureRecorder.SavedText(new SavedPackets(@"C:\g\ultimo-1.pcap", 10, TimeSpan.FromSeconds(40))));
    }

    [Fact]
    public async Task Session_saves_on_its_own_only_with_the_option_on()
    {
        // Viene apagado: los que usan el release no juntan .pcap sin saberlo. Prendido, se recuerda.
        Assert.False(new MeterPreferences().SaveRecentOnMismatch);
        var session = new MeterSession(new CombatEngine(), Connected(), SkillNames.Empty, new MeterPreferences(), _ => { });
        var mismatch = Fight(Verification(closes: false));

        await session.SaveRecentIfMismatchAsync(mismatch, Now);
        Assert.Empty(_requested);

        session.Update(session.Preferences with { SaveRecentOnMismatch = true });
        await session.SaveRecentIfMismatchAsync(mismatch, Now);
        Assert.Single(_requested);
    }

    [Fact]
    public async Task Session_survives_a_disk_error_while_saving_on_its_own()
    {
        // Corre en el async void del fin de pelea: una excepción sin atrapar cerraría el meter.
        var recorder = new CaptureRecorder(_folder) { RecentSource = _ => throw new IOException("disco lleno") };
        var session = new MeterSession(new CombatEngine(), recorder, SkillNames.Empty, new MeterPreferences { SaveRecentOnMismatch = true }, _ => { });

        await session.SaveRecentIfMismatchAsync(Fight(Verification(closes: false)), Now);

        Assert.Null(recorder.LastSaved);
    }

    [Fact]
    public void Imported_profile_does_not_turn_on_automatic_saving()
    {
        // Como la actualización automática, es de cada PC: un perfil de la legión no empieza a juntar .pcap en otro disco.
        var shared = ProfileFile.ToJson(new MeterPreferences { SaveRecentOnMismatch = true });

        Assert.False(ProfileFile.FromJson(shared, new MeterPreferences())!.SaveRecentOnMismatch);
        Assert.True(ProfileFile.FromJson(ProfileFile.ToJson(new MeterPreferences()), new MeterPreferences { SaveRecentOnMismatch = true })!.SaveRecentOnMismatch);
    }

    [Fact]
    public void Overlay_notice_names_the_file_or_says_why_nothing_was_saved()
    {
        RunOnSta(() =>
        {
            var vm = new MeterViewModel(new MeterSession(new CombatEngine(), new CaptureRecorder(_folder), SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { }));

            // En replay no hay buffer: el comando contesta sin escribir nada.
            vm.SaveRecentCommand.Execute(null);
            Assert.True(vm.HasSavedNotice);
            Assert.False(vm.HasSavedNoticeFile);
            Assert.Equal("No hay captura en vivo para guardar", vm.SavedNoticeText);

            var path = Path.Combine(_folder, "ultimo-20261030-221500.pcap");
            vm.ShowSavedRecording(new SavedPackets(path, 10, TimeSpan.FromMinutes(7)), problem: null);
            Assert.True(vm.HasSavedNoticeFile);
            Assert.Equal("Últimos 7 min guardados en ultimo-20261030-221500.pcap", vm.SavedNoticeText);
            Assert.Equal(path, vm.SavedNoticePath);
        });
    }

    private static void RunOnSta(Action action)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private CaptureRecorder Connected() => new(_folder) { RecentSource = Write };

    /// <summary>Lo que haría la captura en vivo: crea el archivo y dice qué guardó.</summary>
    private SavedPackets Write(string path)
    {
        _requested.Add(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return new SavedPackets(path, 100, TimeSpan.FromMinutes(10));
    }

    private static TargetVerification Verification(bool closes) =>
        new(1, 2100001, 1_000_000, 0, 1_000_000, closes ? 1_000_000 : 300_000, 0, closes ? 0 : -700_000, closes);

    private static EncounterSnapshot Fight(params TargetVerification[] verifications) =>
        new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(30), "Monstruo", true, 1_000_000, [],
            TargetNpcId: 2100001, Verifications: verifications.Length > 0 ? verifications : null);
}
