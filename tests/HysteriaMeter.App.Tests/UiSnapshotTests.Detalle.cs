// Estados del arnés para el detalle de jugador (mocks Detalle, Muertes y Comparar; "Renombrar skill" del mock Avisos)
// y la ventana Registro. Ver el encabezado de UiSnapshotTests.cs para correrlo.

using System.Windows;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Registro;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

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
        new("24-detalle-renombrar", h =>
        {
            var detail = h.DetailOf("Kaelis");
            detail.StartRenameSkillCommand.Execute(detail.Skills[0]);
            return new DetailWindow(detail);
        }),

        // Registro aparte, con el mismo registro de la pelea guardada.
        new("25-registro", _ =>
        {
            var death = DemoEncounter.Create(SavedFightEnd).Deaths![0];
            return new LogWindow(new LogViewModel(() => SavedFightLog, SkillNames.Empty, death.ActorId, death.Name));
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
        var vm = h.HistoryOpen();
        var item = vm.History.First(i => i.Killed);
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
}
