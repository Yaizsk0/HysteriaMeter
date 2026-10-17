// Estados del arnés para la bienvenida (mock Inicio) y la tarjeta de fin de pelea (mock Mini). Ver UiSnapshotTests.cs.

using System.IO;
using HysteriaMeter.App.FightCard;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.App.Welcome;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    /// <summary>
    /// El mensaje real de LiveCaptureSource cuando no hay placas (sin Npcap): MeterPipeline lo pasa a CaptureStatus.Fail.
    /// </summary>
    private const string NoDevicesMessage = "No se encontraron placas de red. ¿Está instalado Npcap (modo compatible WinPcap)?";

    /// <summary>
    /// Bienvenida y tarjeta de fin de pelea. Reemplazan al viejo "14-bienvenida" (el banner del overlay, que ya no existe).
    /// Los datos del juego salen de archivos de prueba temporales, contados por GameDataSummary.Read como en la app.
    /// No dependen de la instalación del usuario.
    /// </summary>
    private static readonly State[] InicioStates =
    [
        // Primer uso: Npcap presente y la captura ya vio tráfico del juego (los tres pasos como en el mock).
        new("14-bienvenida", h => Welcome(h.Session(new MeterPreferences { WelcomeDone = false }), new CaptureSetup(NpcapInstalled: true, Device: null))),

        // Primer uso antes de abrir el juego: la placa sigue en "Esperando tráfico" hasta el primer paquete.
        new("14-bienvenida-esperando-trafico", h => Welcome(
            h.Session(new MeterPreferences { WelcomeDone = false }, new CaptureStatus(DateTime.UtcNow)),
            new CaptureSetup(NpcapInstalled: true, Device: null))),

        // Sin Npcap: no está el driver y la captura falló con el mensaje real de LiveCaptureSource.
        new("14-bienvenida-sin-npcap", h =>
        {
            var failed = new CaptureStatus(DateTime.UtcNow);
            failed.Fail(NoDevicesMessage);
            return Welcome(h.Session(new MeterPreferences { WelcomeDone = false }, failed), new CaptureSetup(NpcapInstalled: false, Device: null));
        }),

        // Fin de pelea con intentos anteriores: la demo como kill de boss, después de las tres guardadas en el
        // historial del arnés. Son la misma demo, así que la comparación da "igual" y el percentil, 100.
        new("17-fin-de-pelea", h =>
        {
            var kill = DemoEncounter.Create(Base.AddMinutes(14)) with { Id = Guid.NewGuid(), TargetIsBoss = true, TargetKilled = true };
            var card = new FightCardViewModel(kill, h.Session(), bossOnly: false);
            Pump(card.LoadAsync());
            Assert.True(card.HasComparison && card.HasPercentile);
            return new FightCardWindow(card);
        }),

        // Primer intento contra el boss: sin peleas anteriores no hay comparación ni percentil (las dos columnas).
        // Una sesión sin historial da lo mismo que un historial sin intentos contra ese objetivo.
        new("18-fin-de-pelea-primer-intento", _ =>
        {
            var kill = DemoEncounter.Create(Base.AddMinutes(14)) with { Id = Guid.NewGuid(), TargetIsBoss = true, TargetKilled = true };
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var card = new FightCardViewModel(kill, session, bossOnly: false);
            Pump(card.LoadAsync());
            Assert.False(card.HasComparison || card.HasPercentile);
            return new FightCardWindow(card);
        }),
    ];

    private static WelcomeWindow Welcome(MeterSession session, CaptureSetup capture)
    {
        // Ruta estable para que el texto de los PNG sea reproducible. Solo se borra la carpeta que crea este estado.
        var folder = Path.Combine(Path.GetTempPath(), "hysteria-ui-inicio-fixture", "Juego");
        if (Directory.Exists(folder))
        {
            throw new IOException($"La carpeta de prueba ya existe: {folder}");
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "iconos"));
            Directory.CreateDirectory(Path.Combine(folder, "clases"));
            File.WriteAllText(Path.Combine(folder, "skills.json"), """{"skills":{"15280000":"Bittercold Wind","15290000":"Frost Arrow"}}""");
            File.WriteAllBytes(Path.Combine(folder, "iconos", "15280000.png"), [0]);
            File.WriteAllBytes(Path.Combine(folder, "iconos", "15290000.png"), [0]);
            File.WriteAllBytes(Path.Combine(folder, "clases", "1.png"), [0]);
            return new WelcomeWindow(new WelcomeViewModel(session, capture, GameDataSummary.Read(folder)));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
