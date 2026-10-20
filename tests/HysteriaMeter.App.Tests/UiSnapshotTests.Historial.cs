// Estados de la ventana del historial (mock "Historial"), por el camino real: las peleas se guardan en la base
// temporal del arnés, vuelven por ListRecentAsync y HistoryItem.From, y ToggleHistory arma la lista, las runs y el
// progreso. Todas son la pelea de demo (DemoEncounter) con otra hora, otra entrada a la zona, kill o sin nombre de
// objetivo: no se inventan números, así que todas tienen el mismo DPS (percentil 100, barras iguales, delta +0 %).

using System.Windows;
using HysteriaMeter.App.History;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    /// <summary>"Ahora" del historial: una hora después de las peleas de Base. "Esta semana" y "hoy" no dependen del día de la corrida.</summary>
    private static readonly DateTimeOffset HistoryNow = Base.AddHours(1);

    private static readonly State[] HistorialStates =
    [
        // Sin fila elegida: el trash ("Encuentro" sin kill) no cuenta como otro objetivo y el progreso grafica el único boss.
        new("06-historial-abierto", h => new HistoryWindow(h.HistoryWindowOpen())),

        // Clic en la kill de la run: la fila queda marcada, el overlay la muestra y la columna grafica ese boss.
        new("07-historial-pelea-abierta", h =>
        {
            var vm = h.HistoryWindowOpen();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(RunKill(vm)));
            return new HistoryWindow(vm);
        }),

        // "Solo bosses" deja un solo objetivo: el progreso sale del filtro, sin fila elegida.
        new("07a-historial-solo-bosses", h =>
        {
            var vm = h.HistoryWindowOpen();
            vm.FilterBosses = true;
            return new HistoryWindow(vm);
        }),

        // "Esta semana": la run de hace 10 días queda afuera.
        new("07b-historial-esta-semana", h =>
        {
            var vm = h.HistoryWindowOpen();
            vm.FilterThisWeek = true;
            return new HistoryWindow(vm);
        }),

        new("07c-historial-sin-resultados", h =>
        {
            var vm = h.HistoryWindowOpen();
            vm.HistorySearch = "[búsqueda de prueba]";
            return new HistoryWindow(vm);
        }),

        // Con peleas de más de un personaje aparece el filtro de personaje: tiene que entrar en la cabecera sin bajar de línea.
        new("07j-historial-personajes", h => new HistoryWindow(h.HistoryWithCharacters())),
        new("07k-historial-personajes-ancho-minimo", h =>
        {
            var window = new HistoryWindow(h.HistoryWithCharacters());
            window.Width = window.MinWidth;
            return window;
        }),

        // Sin base de historial: nada guardado todavía.
        new("07d-historial-vacio", h => new HistoryWindow(h.HistoryWindowOpen(withHistory: false))),

        // El overlay con la pelea abierta desde el historial (lo que mostraba el estado 07 de antes).
        new("07e-historial-overlay-pelea-abierta", h =>
        {
            var vm = h.HistoryWindowOpen();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(RunKill(vm)));
            return Overlay(vm);
        }),
    ];

    /// <summary>La kill de la run con entrada a la zona, la que muestra el resumen del modo dungeon.</summary>
    private static HistoryItem RunKill(MeterViewModel vm) => vm.HistoryRuns.First(r => r.CanSummarize).Fights.First(f => f.IsKill);

    private sealed partial class Harness
    {
        private bool _historySeeded;

        /// <summary>
        /// Ventana del historial abierta con el reloj fijo. Sin <paramref name="withHistory"/>, una sesión sin base
        /// (la lista vacía de una instalación nueva).
        /// </summary>
        public MeterViewModel HistoryWindowOpen(bool withHistory = true)
        {
            MeterViewModel vm;
            if (withHistory)
            {
                SeedHistory();
                vm = new MeterViewModel(Session());
            }
            else
            {
                var status = new CaptureStatus(DateTime.UtcNow);
                status.MarkTraffic(DateTime.UtcNow);
                vm = new MeterViewModel(new MeterSession(
                    new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { }, status: status));
            }

            vm.HistoryClock = () => HistoryNow;
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            Assert.True(vm.IsHistoryOpen);
            return vm;
        }

        /// <summary>
        /// Historial con el filtro de personaje a la vista (en "Todos"): las peleas del arnés son de un solo personaje, así
        /// que el otro se agrega a la lista como lo haría UpdateHistoryCharacters.
        /// </summary>
        public MeterViewModel HistoryWithCharacters()
        {
            var vm = HistoryWindowOpen();
            vm.HistoryCharacters.Add("[personaje de prueba]");
            vm.HasSeveralCharacters = true;
            return vm;
        }

        /// <summary>
        /// Además de las tres peleas sueltas de <see cref="Create"/> (sin entrada a la zona): una run de hoy con trash
        /// (sin nombre de objetivo), dos wipes y la kill, y una run de hace 10 días con un wipe y la kill. El primer
        /// wipe de la run lleva una nota que se ve de prueba, guardada como la guarda SaveNote.
        /// </summary>
        private void SeedHistory()
        {
            if (_historySeeded)
            {
                return;
            }

            _historySeeded = true;
            var today = Base.AddHours(-2);
            var old = Base.AddDays(-10);
            EncounterSnapshot[] fights =
            [
                DemoEncounter.Create(today.AddMinutes(4)) with { Id = Guid.NewGuid(), ZoneEnteredAt = today, TargetName = null },
                DemoEncounter.Create(today.AddMinutes(9)) with { Id = Guid.NewGuid(), ZoneEnteredAt = today },
                DemoEncounter.Create(today.AddMinutes(14)) with { Id = Guid.NewGuid(), ZoneEnteredAt = today },
                DemoEncounter.Create(today.AddMinutes(19)) with { Id = Guid.NewGuid(), ZoneEnteredAt = today, TargetKilled = true },
                DemoEncounter.Create(old.AddMinutes(6)) with { Id = Guid.NewGuid(), ZoneEnteredAt = old },
                DemoEncounter.Create(old.AddMinutes(11)) with { Id = Guid.NewGuid(), ZoneEnteredAt = old, TargetKilled = true },
            ];
            foreach (var fight in fights)
            {
                Pump(_history.SaveAsync(fight));
            }

            Pump(_history.SetNoteAsync(fights[1].Id.ToString(), "[nota de prueba]"));
        }
    }
}
