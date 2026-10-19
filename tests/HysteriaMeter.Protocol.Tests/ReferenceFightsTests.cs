using System.Globalization;
using HysteriaMeter.Capture;
using HysteriaMeter.Capture.Reassembly;
using HysteriaMeter.Capture.Sources;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Decoding;
using HysteriaMeter.Protocol.Opcodes;
using Xunit.Abstractions;

namespace HysteriaMeter.Protocol.Tests;

/// <summary>
/// Replay de punta a punta de las peleas de referencia (<c>specs/002-precision-combate/references.md</c>) contra
/// Combat Analysis del juego, que es la verdad. Las capturas no están en el repo (tienen datos de otros jugadores):
/// con <c>HYSTERIA_CAPTURES</c> apuntando a la carpeta que las tiene se corren; sin la variable o sin el .pcap se
/// saltean, así un <c>dotnet test</c> normal y la CI no hacen replay.
/// </summary>
public class ReferenceFightsTests(ITestOutputHelper output)
{
    // Objetivos principales por ID de plantilla (npcId del spawn 0x3641), tal como salen en el replay.
    private const uint DestroyerTarka = 2_101_106; // vida máx 2.700.000
    private const uint DivineAuldor = 2_300_218; // vida máx 9.860.000, la misma que muestra el juego

    // Combat Analysis cuenta solo el objetivo elegido: se compara MainTargetDamage, no Damage (en la dungeon Damage
    // incluye los adds).
    [ReferenceCaptureFact("miniboss-20261015-103328.pcap")]
    public async Task Mini_boss_matches_combat_analysis_exactly()
    {
        var local = LocalPlayer(BossKill(await ReplayAsync("miniboss-20261015-103328.pcap"), DestroyerTarka));

        // Referencia 2: 648.468, 92 golpes, 51 CRI, 5 PERF, 5 DOUB, 62 FRNT, 25 BACK, 23 MULT.
        Assert.Equal(648_468, local.MainTargetDamage);
        Assert.Equal(new HitCounts(Hits: 92, Critical: 51, Perfect: 5, DoubleHit: 5, Front: 62, Back: 25, MultiHit: 23), local.Counts);
    }

    [ReferenceCaptureFact("dungeon-20261014-184438.pcap")]
    public async Task Dungeon_boss_is_within_one_percent_of_combat_analysis()
    {
        var local = LocalPlayer(BossKill(await ReplayAsync("dungeon-20261014-184438.pcap"), DivineAuldor));

        // Referencia 1: el juego muestra 2.040.000 (redondeado); SC-001 pide menos de 1 % de diferencia.
        const long Expected = 2_040_000;
        var error = Math.Abs(local.MainTargetDamage - Expected) / (double)Expected;
        Assert.True(error < 0.01, $"daño local contra el objetivo: {local.MainTargetDamage:N0}; Combat Analysis: {Expected:N0} (diferencia {error:P2})");
    }

    // Auto-verificación (US6 de la 002): el daño contado contra el boss cierra con la vida que perdió según el servidor.
    [ReferenceCaptureFact("dungeon-20261014-184438.pcap")]
    public async Task Dungeon_boss_verification_closes()
    {
        var v = Verification(BossKill(await ReplayAsync("dungeon-20261014-184438.pcap"), DivineAuldor), DivineAuldor);
        Assert.True(v.Closes, Describe(v));
    }

    // Todos los objetivos verificables de la dungeon, no solo el boss. El add de las 21:48:05 no cerraba porque la vida
    // de su spawn se leía del final del paquete (20 de 91 en vez de 103.148 de 435.000).
    [ReferenceCaptureFact("dungeon-20261014-184438.pcap")]
    public async Task Dungeon_verifications_all_close()
    {
        var verifications = (await ReplayAsync("dungeon-20261014-184438.pcap")).SelectMany(e => e.Verifications ?? []).ToList();
        var open = verifications.Where(v => !v.Closes).Select(Describe).ToList();
        Assert.True(verifications.Count > 0 && open.Count == 0, $"de {verifications.Count} verificaciones no cierran {open.Count}: {string.Join("; ", open)}");
    }

    // El mini boss no cierra: su vida baja 30/7 del daño decodificado, una escala sin explicar (ver "Validación contra el
    // juego" en protocol-notes.md de la 001). Si esto cambia, hay que revisar la escala.
    [ReferenceCaptureFact("miniboss-20261015-103328.pcap")]
    public async Task Mini_boss_verification_does_not_close_by_the_unexplained_health_scale()
    {
        var encounter = BossKill(await ReplayAsync("miniboss-20261015-103328.pcap"), DestroyerTarka);
        var v = Verification(encounter, DestroyerTarka);

        Assert.False(v.Closes, Describe(v));
        // Todo lo contado es del local: no hay otro jugador pegándole.
        Assert.Equal(648_468, v.Counted);
        Assert.Equal(LocalPlayer(encounter).MainTargetDamage, v.Counted);
    }

    // En la comparación del 2026-10-17 a los bosses les faltaba entre 3,4 y 4,8 % del daño: era entero el de invocaciones
    // sin dueño (npc 2920655, 2920739 y 2920735) que no se adoptan porque el motor ve más de un jugador conocido de su
    // clase (los ids de conexiones anteriores y una entidad que trata como jugador solo por sus skills). La verificación
    // las cuenta y las 8 cuentas de los bosses cierran exacto. Son 8 y no 10 porque la pelea contra 2300371 de las
    // 06:55:23 es una sola: sus entidades (npc 2920343 y 2920347) la sostienen en dos pausas del daño hecho de 8,5 y
    // 9,1 s (ver combat-regressions.md de la 001).
    [ReferenceCaptureFact("comparacion-20261017-033114.pcap")]
    public async Task Comparison_boss_verifications_close_counting_ownerless_summons()
    {
        uint[] bosses = [2_300_311, 2_300_312, 2_300_371];
        var verifications = (await ReplayAsync("comparacion-20261017-033114.pcap"))
            .SelectMany(e => e.Verifications ?? [])
            .Where(v => v.NpcId is { } npc && bosses.Contains(npc))
            .ToList();
        var open = verifications.Where(v => v.Difference != 0).Select(Describe).ToList();
        Assert.True(verifications.Count == 8 && open.Count == 0,
            $"verificaciones de los bosses: {verifications.Count} (se esperan 8); con diferencia: {open.Count}: {string.Join("; ", open)}");
    }

    // Las que siguen sin cerrar en la comparación tienen causas que la verificación no contempla: muertes sin daño (las
    // entidades 2920091 pasan a 0 sin ningún golpe) y un reinicio de vida (2300306 #39344 vuelve a la máxima a las
    // 06:49:48 sin daño). 2920650 #45279, que también moría sin daño, trae su dueño en la cola del spawn y ya no se
    // verifica. Hay 49 y no 52 porque las entidades de 2300371 sostienen esa pelea en dos pausas del daño hecho (ver
    // combat-regressions.md de la 001). Si este conteo cambia, hay que revisar la causa.
    [ReferenceCaptureFact("comparacion-20261017-033114.pcap")]
    public async Task Comparison_verifications_left_open_have_known_causes()
    {
        var verifications = (await ReplayAsync("comparacion-20261017-033114.pcap")).SelectMany(e => e.Verifications ?? []).ToList();
        var open = verifications.Where(v => !v.Closes).ToList();
        var byNpc = string.Join(", ", open
            .GroupBy(v => v.NpcId)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key?.ToString(CultureInfo.InvariantCulture) ?? "?"}: {g.Count()}"));
        Assert.True(verifications.Count == 49 && verifications.Count - open.Count == 34 && byNpc == "2300306: 1, 2920091: 14",
            $"de {verifications.Count} verificaciones cierran {verifications.Count - open.Count}; las que no, por npc: {byNpc} ({string.Join("; ", open.Select(Describe))})");
    }

    // Sin capturas: si la elección del encuentro o del jugador local falla, el mensaje no puede llevar nombres de
    // jugadores (la salida de dotnet test termina pegada en PRs e issues).
    [Fact]
    public void Selection_failures_do_not_print_player_names()
    {
        const string Name = "NombreDePrueba";
        var start = DateTimeOffset.UnixEpoch;
        ActorSnapshot Actor(ulong id, bool local) => new(id, Name, null, local, 0, 0, 0, 0, 0, 0, 0, []);
        EncounterSnapshot Encounter(params ActorSnapshot[] actors) => new(Guid.Empty, start, start, null, true, 0, actors, TargetNpcId: 1);

        Assert.DoesNotContain(Name, Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LocalPlayer(Encounter(Actor(1, false), Actor(2, false)))).Message);
        Assert.DoesNotContain(Name, Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LocalPlayer(Encounter(Actor(1, true), Actor(2, true)))).Message);
        Assert.DoesNotContain(Name, Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => BossKill([Encounter(Actor(1, true))], npcId: 2)).Message);
        Assert.DoesNotContain(Name, Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verification(Encounter(Actor(1, true)), npcId: 1)).Message);
    }

    /// <summary>La verificación del objetivo <paramref name="npcId"/>, que tiene que ser una sola. El mensaje lleva solo ids.</summary>
    private static TargetVerification Verification(EncounterSnapshot encounter, uint npcId)
    {
        var verified = encounter.Verifications ?? [];
        var checks = verified.Where(v => v.NpcId == npcId).ToList();
        Assert.True(checks.Count == 1,
            $"verificaciones del npc {npcId}: {checks.Count} (npcs verificados: {string.Join(", ", verified.Select(v => v.NpcId))})");
        return checks[0];
    }

    /// <summary>Los números de la cuenta (vida inicial y final, contado, overkill y diferencia), para ver qué falta. Sin nombres.</summary>
    private static string Describe(TargetVerification v) => string.Create(CultureInfo.InvariantCulture,
        $"npc {v.NpcId?.ToString(CultureInfo.InvariantCulture) ?? "?"} #{v.TargetId}: vida inicial {v.StartHp}, final {v.EndHp}, máx {v.MaxHp}, contado {v.Counted}, overkill {v.Overkill}, diferencia {v.Difference} · {(v.Closes ? "cierra" : "no cierra")}");

    /// <summary>
    /// La pelea de referencia es el encuentro que termina con la muerte del objetivo <paramref name="npcId"/>. Las
    /// capturas traen más peleas (trash, y en la del mini boss una anterior contra otro objetivo con más daño local), así
    /// que "la de más daño" no sirve; y en la del mini boss los primeros 3 s contra el mismo objetivo quedan en un encuentro
    /// aparte, cortado por inactividad, mientras que la referencia (00:27) es el que termina con la muerte. No depende de
    /// nombres de jugadores ni de <c>data/npcs.json</c>, que el replay no carga (sin él no hay <c>TargetName</c> ni
    /// <c>TargetIsBoss</c>).
    /// </summary>
    /// <remarks>
    /// Cuenta en vez de usar <c>Assert.Single</c>: al fallar, xUnit vuelca la colección con el <c>ToString</c> de cada
    /// elemento, y el de <see cref="ActorSnapshot"/> trae el nombre del jugador. Los mensajes llevan solo ids.
    /// </remarks>
    private static EncounterSnapshot BossKill(IReadOnlyList<EncounterSnapshot> encounters, uint npcId)
    {
        var kills = encounters.Where(e => e.TargetNpcId == npcId && e.TargetKilled).ToList();
        Assert.True(kills.Count == 1,
            $"encuentros que terminan con la muerte del npc {npcId}: {kills.Count} de {encounters.Count} (npcs muertos: {string.Join(", ", encounters.Where(e => e.TargetKilled).Select(e => e.TargetNpcId))})");
        return kills[0];
    }

    private static ActorSnapshot LocalPlayer(EncounterSnapshot encounter)
    {
        var locals = encounter.Actors.Where(a => a.IsLocalPlayer).ToList();
        Assert.True(locals.Count == 1,
            $"jugadores locales en el encuentro: {locals.Count} de {encounter.Actors.Count} (ids {string.Join(", ", encounter.Actors.Select(a => a.Id))})");
        return locals[0];
    }

    /// <summary>
    /// El pipeline completo sobre la captura, igual que <c>hysteria-rec replay</c>: reensamblado TCP, un decoder por
    /// conexión del servidor, el motor de combate y un Tick al final para cerrar la última pelea.
    /// </summary>
    private async Task<IReadOnlyList<EncounterSnapshot>> ReplayAsync(string file)
    {
        var table = OpcodeTable.Load(Path.Combine(AppContext.BaseDirectory, "data", "opcodes.json"));
        var engine = new CombatEngine();
        var encounters = new List<EncounterSnapshot>();
        engine.EncounterFinished += encounters.Add;

        var flows = new TcpFlowTracker();
        var streams = new Dictionary<FlowKey, GameStreamDecoder>();
        var stats = new DecoderStats();
        DateTimeOffset last = default;

        await using var source = new PcapFileSource(ReferenceCaptureFactAttribute.PathOf(file));
        await foreach (var segment in source.ReadAllAsync(CancellationToken.None))
        {
            last = segment.Timestamp;
            var result = flows.Push(segment);
            if (!segment.Flow.IsFromServer())
            {
                continue;
            }

            if (!streams.TryGetValue(segment.Flow, out var stream))
            {
                streams[segment.Flow] = stream = new GameStreamDecoder(table, PacketDecoders.All, stats: stats);
            }

            if (result.GapDetected)
            {
                stream.Resync();
            }

            foreach (var chunk in result.Chunks)
            {
                foreach (var e in stream.Push(chunk.Span, segment.Timestamp))
                {
                    engine.Apply(e);
                }
            }
        }

        engine.Tick(last + TimeSpan.FromMinutes(1));

        // Para diagnosticar un fallo: solo ids y números, nunca nombres de jugadores.
        output.WriteLine($"Errores de decodificación: {stats.Errors}");
        foreach (var encounter in encounters)
        {
            var local = encounter.Actors.FirstOrDefault(a => a.IsLocalPlayer);
            var c = local?.Counts ?? HitCounts.Empty;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[{encounter.Start:HH:mm:ss}] npc {encounter.TargetNpcId?.ToString(CultureInfo.InvariantCulture) ?? "?"} · vida máx {encounter.TargetHealth?.MaxHp ?? 0} · {encounter.Duration.TotalSeconds:0.0}s · {(encounter.TargetKilled ? "muerto" : "sin terminar")}"
                + $" · local {(local is null ? "-" : $"#{local.Id} objetivo {local.MainTargetDamage} total {local.Damage}")}"
                + $" · golpes {c.Hits} CRI {c.Critical} PERF {c.Perfect} DOUB {c.DoubleHit} FRNT {c.Front} BACK {c.Back} MULT {c.MultiHit}"));
            foreach (var v in encounter.Verifications ?? [])
            {
                output.WriteLine($"    verificación {Describe(v)}");
            }
        }

        return encounters;
    }
}

/// <summary>
/// <see cref="FactAttribute"/> que se saltea si falta <c>HYSTERIA_CAPTURES</c> (carpeta de las capturas) o el .pcap
/// adentro. xUnit 2 no tiene skip dinámico: el motivo se decide al descubrir el test.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReferenceCaptureFactAttribute : FactAttribute
{
    public const string Variable = "HYSTERIA_CAPTURES";

    public ReferenceCaptureFactAttribute(string file)
    {
        File = file;
        var folder = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(folder))
        {
            Skip = $"Sin {Variable}: apuntarla a la carpeta de las capturas de referencia para correr el replay.";
        }
        else if (!System.IO.File.Exists(PathOf(file)))
        {
            // Una ruta relativa se resuelve contra la carpeta del testhost (bin/...), no contra la del dotnet test: se
            // muestra la resuelta para que un skip por eso no pase por una validación hecha.
            Skip = $"No está {file} en {Path.GetFullPath(folder)} ({Variable} va con ruta absoluta).";
        }
    }

    /// <summary>Nombre del .pcap dentro de <see cref="Variable"/>.</summary>
    public string File { get; }

    public static string PathOf(string file) => Path.Combine(Environment.GetEnvironmentVariable(Variable) ?? string.Empty, file);
}
