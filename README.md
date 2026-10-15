# Hysteria Meter

DPS meter de escritorio para **AION 2**. Captura pasivamente el tráfico del juego con Npcap, decodifica los paquetes de combate y muestra un overlay con el DPS de la party en vivo.

> **Estado**: decodifica daño real del cliente global (framing, bundles LZ4, daño, aparición de NPC y muertes con nombres). Pendiente: confirmar la escala del daño contra Combat Analysis, críticos, curación y jugador local. Ver [notas del protocolo](specs/001-dps-meter-mvp/protocol-notes.md).

## Principio rector

Solo lectura pasiva: no envía ni modifica paquetes, no lee ni escribe la memoria del juego, no automatiza nada. Ver la [constitución](.specify/memory/constitution.md).

## Arquitectura

```text
Npcap ─► Capture ─► TcpFlowTracker ─► GameStreamDecoder ─► CombatEngine ─► Overlay WPF
          (Live/Replay)  (reensamblado)   (varint + LZ4 +       (entidades,      └► SQLite
                                           opcodes.json)         encuentros)
```

| Proyecto | Responsabilidad |
|---|---|
| `src/HysteriaMeter.Capture` | Npcap, filtro por proceso/puerto, reensamblado TCP, replay de `.pcap` |
| `src/HysteriaMeter.Protocol` | Framing varint, bundles LZ4, `opcodes.json`, decoders → eventos |
| `src/HysteriaMeter.Combat` | Modelo puro: canonicalización de entidades, encuentros, DPS |
| `src/HysteriaMeter.Storage` | Historial en SQLite |
| `src/HysteriaMeter.App` | Overlay WPF (click-through, Ctrl+Shift+L) |
| `tools/HysteriaMeter.Recorder` | `hysteria-rec`: grabar y analizar capturas |

## Uso rápido

Requisitos: Windows 10/11, [Npcap](https://npcap.com/) en modo compatible WinPcap, .NET 10 SDK.

```powershell
dotnet build
dotnet test
dotnet run --project src/HysteriaMeter.App -c Release
```

El overlay arranca en vivo y toma solo el tráfico del proceso `AION2`. Para probarlo sin jugar, reproducí una grabación a velocidad real (o más rápido con `ReplaySpeed`):

```powershell
dotnet run --project src/HysteriaMeter.App -c Release -- --Capture:Mode=Replay --Capture:ReplayPath=C:\ruta\a\captura.pcap --Capture:ReplaySpeed=4
```

Como en Details!, el botón + abre otra ventana del overlay: cada una elige su pestaña (DAÑO o CURACIÓN) y si muestra el HPS junto al daño, y todas se recuerdan con su posición. Cerrar la primera cierra la app.

Doble clic en el encabezado alterna el modo compacto (solo filas y reloj). El overlay se vuelve opaco al pasar el mouse y, si lo activás, se atenúa cuando no hay combate. El punto del encabezado indica la captura: verde en combate, ámbar sin tráfico del juego, rojo si se cortó. En configuración, "Ver barras de prueba" muestra una pelea inventada para acomodar todo sin combatir.

El historial (botón del reloj) se puede buscar por boss o por nota y filtrar por derrotados. Cada pelea muestra tu DPS, el número de intento contra ese boss y una ★ si es tu récord; al batir un récord aparece un aviso dorado. Al abrir una pelea guardada se le puede escribir una nota o borrarla. En configuración se elige cuánto guardar: los bosses derrotados y las peleas con nota no se borran nunca.

Con la vida del boss a la vista, el overlay estima en cuánto muere al ritmo actual de la party ("muere en 0:42"). Cada fila tiene detrás una línea tenue con el DPS de los últimos 30 segundos. En configuración se elige el formato de los números (2.04M, 2.0M o 2.040.321) y cada cuánto se refresca el overlay.

En configuración, EXPORTAR Y COMPARTIR copia el overlay como imagen (para pegar en Discord) y guarda la pelea en CSV o JSON en `%LOCALAPPDATA%\HysteriaMeter\Exportes`. Con "Ocultar los nombres de los demás", el resumen y los archivos dicen "Yo", "Jugador 1", "Jugador 2". Si pegás la URL de un webhook de Discord, el resumen se puede mandar al canal a mano o solo al derrotar un boss. Es lo único que sale de tu PC, y solo si lo configurás.

La pestaña RECIBIDO muestra el daño que le hicieron los monstruos a cada uno (todavía sin validar contra Combat Analysis). Quien murió lleva una ✝ junto al nombre; en el detalle del jugador se ve qué lo mató y cuánto recibió en los 10 segundos anteriores. El botón "Registro" del detalle abre el golpe a golpe de la pelea, con búsqueda por jugador, skill o modificador.

Clic en una fila del overlay abre el desglose por skill de ese jugador (daño, %, golpes, crítico, espalda, máximo, promedio y DPS). El paquete de daño solo trae el ID de la skill: los nombres se cargan a mano en `data/skills.json`.

Para grabar una pelea desde el overlay: engranaje → GRABACIÓN → "Grabar lo capturado (.pcap)". Mientras graba aparece `● REC` arriba; los archivos quedan en `%LOCALAPPDATA%\HysteriaMeter\Grabaciones` ("Abrir carpeta"). Esas grabaciones tienen nombres de personajes: no se suben al repo.

## Spec Kit

El proyecto se desarrolla con [Spec Kit](https://github.com/github/spec-kit). Con Claude Code: `/speckit-specify` para una feature nueva, luego `/speckit-plan`, `/speckit-tasks` y `/speckit-implement`. La feature actual es [`specs/001-dps-meter-mvp`](specs/001-dps-meter-mvp/spec.md).
