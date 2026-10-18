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
    private const uint DestroyerTarka = 2_101_106; // vida máx 2.684.259
    private const uint DivineAuldor = 2_300_218; // vida máx 9.857.918 (protocol-notes.md de la 001 la anota como 9.860.000)

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
    }

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
