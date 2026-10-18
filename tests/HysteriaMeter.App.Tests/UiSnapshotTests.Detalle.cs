// Estados del arnés para el detalle de jugador (mocks Detalle, Muertes y Comparar; "Renombrar skill" del mock Avisos)
// y la ventana Registro. Ver el encabezado de UiSnapshotTests.cs para correrlo.

using System.Windows.Controls.Primitives;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Registro;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    /// <summary>
    /// El detalle no tiene overlay en el arnés: DetailRequested no tiene suscriptor y la ventana la crea cada estado.
    /// Lo que la demo no trae y queda fuera: marcas (barras doradas del gráfico), emblemas de clase (ClassId nulo),
    /// íconos de skill (las skills 9001–9003 no están en los datos del juego) y variantes al renombrar (sin skill base).
    /// </summary>
    private static readonly State[] DetalleStates =
    [
        new("20-detalle-skills", h => new DetailWindow(h.DetailOf("Kaelis"))),

        // Vos con los otros dos intentos guardados: diferencias contra el anterior y PERCENTIL.
        new("20b-detalle-skills-local", h => new DetailWindow(SavedFightDetail(h, "Vos"))),

        // La muerte de Tharos, con el registro de la pelea: sus dos últimos golpes y la muerte, como los anota el motor.
        new("21-detalle-muertes", h =>
        {
            var detail = SavedFightDetail(h, "Tharos");
            detail.IsDeathsTab = true;
            return new DetailWindow(detail);
        }),
        new("22-detalle-log", h =>
        {
            var detail = SavedFightDetail(h, "Tharos");
            detail.IsLogTab = true;
            return new DetailWindow(detail);
        }),
        new("23-detalle-comparar", h =>
        {
            var detail = h.DetailOf("Vos");
            detail.CompareWith = detail.Others.First(o => o.Name == "Kaelis");
            return new DetailWindow(detail);
        }),

        // Contra tu mejor intento guardado: la misma demo, así que las diferencias dan cero (no hay otros números).
        new("23b-detalle-comparar-intento", h =>
        {
            var detail = SavedFightDetail(h, "Vos");
            detail.CompareWith = detail.Others.First(o => o.Saved is not null);
            return new DetailWindow(detail);
        }),
        // Tharos murió a la 1:11: el círculo y "☠ 1:11" sobre su curva, y la conclusión en tercera persona.
        new("23c-detalle-comparar-muerte", h =>
        {
            var detail = h.DetailOf("Tharos");
            detail.CompareWith = detail.Others.First(o => o.Name == "Kaelis");
            return new DetailWindow(detail);
        }),

        // Un DoT que sigue pegando con el jugador muerto no es la vuelta: la conclusión nombra la muerte de 0:30 a 0:45.
        // Con el Timeline entero (DoT incluido) el tramo terminaba en el 0:31 y no se nombraba.
        new("23d-detalle-comparar-muerte-dot", h =>
        {
            var detail = DotAfterDeath(h);
            detail.CompareWith = detail.Others.First(o => o.Name == "Compañero");
            Assert.EndsWith(", coincide con la muerte de 0:30 a 0:45.", detail.InsightTail);
            return new DetailWindow(detail);
        }),
        new("24-detalle-renombrar", h =>
        {
            var detail = h.DetailOf("Kaelis");
            detail.StartRenameSkillCommand.Execute(detail.Skills[0]);
            return new DetailWindow(detail);
        }),

        // Menú "⋯" de la cabecera: el apodo y el registro en otra ventana (es parte de la ventana, no un Popup).
        new("24b-detalle-menu", h =>
        {
            var window = new DetailWindow(h.DetailOf("Kaelis"));
            ((ToggleButton)window.FindName("MoreButton")).IsChecked = true;
            return window;
        }),

        // Registro aparte, con el mismo registro de la pelea guardada.
        new("25-registro", h =>
        {
            var detail = SavedFightDetail(h, "Tharos");
            LogViewModel? log = null;
            detail.LogRequested += value => log = value;
            detail.OpenLogCommand.Execute(null);
            Assert.NotNull(log);
            Assert.Contains(log.Lines, line => line.IsDeath && line.Time == "01:11.0");
            return new LogWindow(log);
        }),
    ];

    /// <summary>La pelea guardada que se abre del historial (la última de Harness.Create, la del kill).</summary>
    private static readonly DateTimeOffset SavedFightEnd = Base.AddMinutes(8);

    /// <summary>
    /// Lo que el motor tendría en el registro de esa pelea: los golpes que recibió Tharos antes de morir (los de su
    /// DeathRecap) y la línea de la muerte. Sin otros golpes: la demo no los trae.
    /// </summary>
    private static IReadOnlyList<CombatLogEntry> SavedFightLog
    {
        get
        {
            var death = DemoEncounter.Create(SavedFightEnd).Deaths![0];
            return [.. death.LastHits, new CombatLogEntry(death.Timestamp, LogKind.Death, death.ActorId, death.Name, death.ActorId, death.Name, 0, 0)];
        }
    }

    /// <summary>
    /// Detalle de un jugador en la pelea guardada abierta desde el historial, con el registro de esa pelea y, para vos,
    /// los otros intentos ya cargados de la base.
    /// </summary>
    private static ActorDetailViewModel SavedFightDetail(Harness h, string name)
    {
        var vm = h.HistoryWindowOpen();
        var item = vm.HistoryRuns.SelectMany(r => r.Fights).First(i => i.IsKill);
        Pump(vm.OpenEncounterCommand.ExecuteAsync(item));
        vm.ShowDetailCommand.Execute(vm.Actors.Single(a => a.Name == name));
        var detail = vm.Detail;
        detail.LogSource = () => SavedFightLog;

        // Los intentos y el percentil vienen de SQLite en otro hilo y vuelven por el Dispatcher.
        for (var i = 0; i < 300 && name == "Vos" && !(detail.Others.Any(o => o.Saved is not null) && detail.HasPercentile); i++)
        {
            Thread.Sleep(10);
            Pump();
        }

        return detail;
    }

    /// <summary>
    /// Pelea en vivo pasada por el motor, como la reciben los eventos: vos ("Prueba") y "Compañero" le pegan al boss una
    /// vez por segundo durante 70 s. Morís en el 0:30 con un DoT puesto que sigue pegando hasta el 0:38, y volvés a pegar
    /// directo en el 0:45. Alcance "todos" para que Compañero cuente sin armar un grupo.
    /// </summary>
    private static ActorDetailViewModel DotAfterDeath(Harness h)
    {
        var session = h.Session(new MeterPreferences { LastSeenNews = 1, Scope = ActorScope.Everyone });
        var engine = session.Engine;
        engine.Entities.Npcs = new NpcCatalog(new Dictionary<uint, NpcInfo> { [500] = new("Boss de prueba", null, IsBoss: true) });
        var start = Base;
        engine.Apply(new LocalPlayerEvent(start, 1, "Prueba"));
        engine.Apply(new EntityInfoEvent(start, 1, "Prueba", 11));
        engine.Apply(new EntityInfoEvent(start, 2, "Compañero", null));
        engine.Apply(new EntitySpawnEvent(start, 900, null, 500, Hp: 10_000_000, MaxHp: 10_000_000));
        for (var second = 0; second <= 70; second++)
        {
            var at = start.AddSeconds(second);
            engine.Apply(new DamageEvent(at, 2, 900, 11_010_000, 9_000, HitModifiers.None));
            if (second is < 30 or >= 45)
            {
                engine.Apply(new DamageEvent(at, 1, 900, 11_010_000, 8_000, HitModifiers.None));
            }

            if (second is > 20 and <= 38)
            {
                engine.Apply(new DamageEvent(at, 1, 900, 11_020_000, 1_000, HitModifiers.DamageOverTime));
            }

            if (second == 30)
            {
                engine.Apply(new DeathEvent(at, 1));
            }
        }

        var vm = Refreshed(new MeterViewModel(session));
        Pump(vm.LiveQueries);
        vm.ShowDetailCommand.Execute(vm.Actors.Single(a => a.Name == "Prueba"));
        return vm.Detail;
    }
}
