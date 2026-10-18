// Historial y modo dungeon con datos como los reales: data/npcs.json casi no trae nombres, así que las peleas se
// guardan sin nombre de objetivo y con su plantilla de NPC ("NPC 2400123", como el overlay). Hay trash, un wipe real
// (sin kill, con las fases de vida que se vieron), kills de tres bosses distintos y una run vieja de antes de guardar
// la plantilla (ni nombre ni plantilla). Las peleas son la demo (DemoEncounter) con otra hora, plantilla y resultado;
// el trash es la demo acortada a 12 s, lo que dura un pull que muere rápido. Van por el camino real: SaveAsync,
// ListRecentAsync, HistoryItem.From, ToggleHistory y, para el dungeon, OpenRunSummary con lo que carga LoadAsync.
//
// Cada estado usa su propia base (no la compartida del arnés): estas peleas no cambian los estados 06 y 07 del historial.

using System.IO;
using System.Windows;
using HysteriaMeter.App.Dungeon;
using HysteriaMeter.App.History;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    /// <summary>Plantillas sin nombre en data/npcs.json: tres bosses y el trash de la run.</summary>
    private const uint FirstBoss = 2400123;
    private const uint SecondBoss = 2400456;
    private const uint ThirdBoss = 2400789;
    private const uint TrashNpc = 2400901;

    /// <summary>Bases de estos estados; el test las borra al final con la compartida.</summary>
    private static readonly List<string> SeparateDatabases = [];

    private static readonly State[] DatosRealesStates =
    [
        // Sin fila elegida: filas "NPC …" con su intento, "Trash" sin comparaciones y la run vieja como "Encuentro".
        new("07f-historial-sin-nombres", _ => new HistoryWindow(RealDataHistory())),

        // La kill del primer boss: TU PROGRESO agrupa por plantilla (el wipe y la kill de NPC 2400123).
        new("07g-historial-sin-nombres-intento", _ =>
        {
            var vm = RealDataHistory();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(vm.HistoryRuns.SelectMany(r => r.Fights).First(i => i.NpcId == FirstBoss && i.IsKill)));
            return new HistoryWindow(vm);
        }),

        // Una fila de trash elegida: el progreso explica que el trash no tiene intentos con qué compararse.
        new("07h-historial-trash-elegido", _ =>
        {
            var vm = RealDataHistory();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(vm.HistoryRuns.SelectMany(r => r.Fights).First(i => i.NpcId == TrashNpc)));
            return new HistoryWindow(vm);
        }),

        // La run de hoy: tres bosses sin nombre, un wipe real del primero y trash.
        new("32-dungeon-sin-nombres", _ => RunSummary(RealDataHistory(), run => run.Fights.Any(f => f.NpcId == FirstBoss))),

        // La run vieja, sin plantilla: cada kill es su boss ("Boss 1", "Boss 2") y la pelea sin kill no se cuenta como wipe.
        new("33-dungeon-peleas-viejas", _ => RunSummary(RealDataHistory(), run => run.Fights.All(f => f.NpcId is null))),
    ];

    /// <summary>El resumen del modo dungeon de la run elegida, como lo abre "Resumen de la run" en el historial.</summary>
    private static DungeonWindow RunSummary(MeterViewModel vm, Func<HistoryRun, bool> which)
    {
        DungeonSummaryViewModel? summary = null;
        vm.RunSummaryRequested += s => summary = s;
        Pump(vm.OpenRunSummaryCommand.ExecuteAsync(vm.HistoryRuns.First(r => r.CanSummarize && which(r)).Fights[0]));
        return new DungeonWindow(summary ?? throw new InvalidOperationException("El historial no abrió el resumen de la run."));
    }

    /// <summary>Historial abierto, con el reloj fijo, sobre una base propia con las peleas de <see cref="RealDataFights"/>.</summary>
    private static MeterViewModel RealDataHistory()
    {
        var database = Path.Combine(Path.GetTempPath(), $"hysteria-snapshots-datos-{Guid.NewGuid():N}.db");
        SeparateDatabases.Add(database);
        var repository = new EncounterRepository(database);
        Pump(repository.InitializeAsync());
        foreach (var fight in RealDataFights())
        {
            Pump(repository.SaveAsync(fight));
        }

        var status = new CaptureStatus(DateTime.UtcNow);
        status.MarkTraffic(DateTime.UtcNow);
        var vm = new MeterViewModel(new MeterSession(
            new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { },
            history: repository, status: status))
        {
            HistoryClock = () => HistoryNow,
        };
        Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
        Assert.True(vm.IsHistoryOpen);
        return vm;
    }

    /// <summary>
    /// Run de hoy: trash, wipe y kill del primer boss, kill del segundo, otro trash y kill del tercero. Run de hace tres
    /// días, guardada antes de registrar la plantilla: una pelea sin kill y dos kills, sin nombre ni plantilla.
    /// </summary>
    private static EncounterSnapshot[] RealDataFights()
    {
        var today = Base.AddHours(-2);
        var old = Base.AddDays(-3);

        EncounterSnapshot Fight(DateTimeOffset end, DateTimeOffset entered, uint? npc, bool killed) =>
            DemoEncounter.Create(end) with
            {
                Id = Guid.NewGuid(), ZoneEnteredAt = entered, TargetName = null, TargetNpcId = npc, TargetKilled = killed,
            };

        EncounterSnapshot Trash(DateTimeOffset end, DateTimeOffset entered) =>
            Fight(end, entered, TrashNpc, killed: true) with { Start = end.AddSeconds(-12), MainTargetDuration = TimeSpan.FromSeconds(12) };

        return
        [
            Trash(today.AddMinutes(3), today),
            // El wipe: la vida bajó del 75 % y del 50 % antes de que cayera la party.
            Fight(today.AddMinutes(8), today, FirstBoss, killed: false) with { Phases = [new HealthPhase(75, 31), new HealthPhase(50, 74)] },
            Fight(today.AddMinutes(13), today, FirstBoss, killed: true),
            Fight(today.AddMinutes(19), today, SecondBoss, killed: true),
            Trash(today.AddMinutes(22), today),
            Fight(today.AddMinutes(27), today, ThirdBoss, killed: true),

            Fight(old.AddMinutes(6), old, null, killed: false),
            Fight(old.AddMinutes(11), old, null, killed: true),
            Fight(old.AddMinutes(16), old, null, killed: true),
        ];
    }
}
