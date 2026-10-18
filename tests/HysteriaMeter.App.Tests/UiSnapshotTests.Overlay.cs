using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    // Estados sintéticos declarados del rediseño; no se abre la captura ni el juego.
    private static readonly State[] OverlayStates =
    [
        new("40-overlay-boss-detectado", h =>
        {
            var vm = Live(h.Demo());
            vm.HasBossNotice = true;
            vm.BossNoticeName = vm.Title;
            vm.BossNoticeDetail = "Boss de prueba para revisar el aviso superpuesto";
            return Overlay(vm);
        }),
        new("41-overlay-enrage", h => Overlay(LiveEncounter(h))),
        new("42-overlay-captura-guardada", h =>
        {
            var vm = Live(h.Demo());
            vm.ShowSavedImage(@"C:\Prueba\Exportes\boss.png");
            return Overlay(vm);
        }),
        new("43-overlay-parche", h =>
        {
            var vm = Live(h.Demo());
            vm.Issue = CaptureIssue.Patch;
            vm.StatusKind = "warn";
            return Overlay(vm);
        }),
        new("44-overlay-sin-npcap", h =>
        {
            var status = new CaptureStatus();
            status.Fail("Npcap no está instalado [estado de prueba]");
            var vm = Refreshed(new MeterViewModel(h.Session(status: status)));
            Pump(vm.LiveQueries);
            return Overlay(vm);
        }),
        new("45-overlay-ancho-minimo", h => Overlay(Live(h.Demo()), width: 260)),
        new("46-overlay-columnas", h => Overlay(Live(h.Demo(preferences: new MeterPreferences
        {
            LastSeenNews = 1, ShowTotalColumn = true, ShowRecentColumn = true, ShowCritColumn = true,
            ShowMaxHitColumn = true, ShowGear = true,
        })), width: 650)),
        new("47-overlay-run", h =>
        {
            var vm = LiveEncounter(h);
            vm.IsRunChip = true;
            return Overlay(vm);
        }),
        new("48-overlay-minimo-columnas", h => Overlay(Live(h.Demo(preferences: new MeterPreferences
        {
            LastSeenNews = 1, ShowTotalColumn = true, ShowRecentColumn = true, ShowCritColumn = true,
            ShowMaxHitColumn = true, ShowGear = true,
        })), width: 260)),
        new("49-overlay-minimo-columnas-derecha", h =>
        {
            var window = Overlay(Live(h.Demo(preferences: new MeterPreferences
            {
                LastSeenNews = 1, ShowTotalColumn = true, ShowRecentColumn = true, ShowCritColumn = true,
                ShowMaxHitColumn = true, ShowGear = true,
            })), width: 260);
            window.Loaded += (_, _) => ((System.Windows.Controls.ScrollViewer)window.FindName("ActorScroller")).ScrollToRightEnd();
            return window;
        }),

        // Nombre largo con muerte y tendencia en el ancho mínimo, normal y compacto: se recorta solo el nombre y la cruz y la
        // flecha siguen a la vista, pegadas a continuación (como la ✕ de los tramos del dungeon). Brann, corto, la lleva pegada.
        new("45b-overlay-nombre-largo", h => Overlay(LongName(Live(h.Demo())), width: 260)),
        new("45c-overlay-nombre-largo-compacto", h => Overlay(LongName(Live(h.Demo(compact: true))), width: 260)),

        // En pausa la fila solo lleva puesto, nombre, cruz y DPS: la cruz también queda a la vista. El nombre es más largo
        // que el de 45b porque, sin insignia ni %, entra más.
        new("45d-overlay-nombre-largo-pausa", h =>
        {
            var vm = Live(h.Demo());
            vm.Actors.Single(a => a.Name == "Tharos").Name = "Nombre de prueba muy largo, más que la fila en pausa";
            vm.IsPaused = true;
            return Overlay(vm, width: 260);
        }),
    ];

    /// <summary>
    /// Tharos (el de la demo con una muerte) con un nombre de prueba que no entra y ▲; Brann con ▼. La flecha se fija a mano:
    /// en vivo la pone RankTrends al cambiar de puesto y dura 3 s. Va después de Live(), que no refresca.
    /// </summary>
    private static MeterViewModel LongName(MeterViewModel vm)
    {
        var tharos = vm.Actors.Single(a => a.Name == "Tharos");
        tharos.Name = "Nombre de prueba muy largo";
        tharos.Trend = "▲";
        vm.Actors.Single(a => a.Name == "Brann").Trend = "▼";
        return vm;
    }

    private static MeterViewModel LiveEncounter(Harness harness)
    {
        var session = harness.Session();
        var engine = session.Engine;
        engine.Entities.Npcs = new NpcCatalog(new Dictionary<uint, NpcInfo>
        {
            [500] = new("Boss de prueba", null, IsBoss: true, EnrageSeconds: 75),
        });
        var start = Base;
        engine.Apply(new LocalPlayerEvent(start, 1, "Prueba"));
        engine.Apply(new EntityInfoEvent(start, 1, "Prueba", 11));
        engine.Apply(new EntitySpawnEvent(start, 900, null, 500, Hp: 100_000, MaxHp: 100_000));
        var vm = new MeterViewModel(session);
        for (var second = 0; second <= 60; second++)
        {
            engine.Apply(new DamageEvent(start.AddSeconds(second), 1, 900, 11_010_000, 1_000, HitModifiers.Critical));
        }

        engine.Apply(new HealthEvent(start.AddSeconds(60), 900, 39_000));
        Refreshed(vm);
        Pump(vm.LiveQueries);
        vm.HasBossNotice = false;
        return vm;
    }
}
