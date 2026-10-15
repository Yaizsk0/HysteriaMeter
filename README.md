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

Clic en una fila del overlay abre el desglose por skill de ese jugador (daño, %, golpes, crítico, espalda, máximo, promedio y DPS). El paquete de daño solo trae el ID de la skill: los nombres se cargan a mano en `data/skills.json`.

Para grabar una pelea desde el overlay: engranaje → GRABACIÓN → "Grabar lo capturado (.pcap)". Mientras graba aparece `● REC` arriba; los archivos quedan en `%LOCALAPPDATA%\HysteriaMeter\Grabaciones` ("Abrir carpeta"). Esas grabaciones tienen nombres de personajes: no se suben al repo.

## Spec Kit

El proyecto se desarrolla con [Spec Kit](https://github.com/github/spec-kit). Con Claude Code: `/speckit-specify` para una feature nueva, luego `/speckit-plan`, `/speckit-tasks` y `/speckit-implement`. La feature actual es [`specs/001-dps-meter-mvp`](specs/001-dps-meter-mvp/spec.md).
