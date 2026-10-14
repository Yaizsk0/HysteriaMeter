# Implementation Plan: DPS meter de escritorio para AION 2 (MVP)

**Branch**: `001-dps-meter-mvp` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-dps-meter-mvp/spec.md`

## Summary

Meter de escritorio que captura pasivamente el tráfico TCP de AION 2 con Npcap, lo reensambla, lo corta en frames varint (expandiendo bundles LZ4), traduce los opcodes configurados a eventos de combate y los agrega en un modelo puro que alimenta un overlay WPF y un historial SQLite. Una herramienta de consola (`hysteria-rec`) graba y analiza capturas para descubrir el protocolo. Ver [research.md](./research.md).

## Technical Context

**Language/Version**: C# `latest` sobre .NET 10 (LTS)

**Primary Dependencies**: SharpPcap 6.3 + PacketDotNet 1.4 (Npcap), K4os.Compression.LZ4 1.3, CommunityToolkit.Mvvm 8.4, Microsoft.Extensions.Hosting 10, Microsoft.Data.Sqlite 10 + Dapper 2.1

**Storage**: SQLite local (`hysteria-meter.db` junto al ejecutable)

**Testing**: xUnit; tests de protocolo con bytes (sintéticos por ahora, reales cuando existan capturas); tests de combate como modelo puro

**Target Platform**: Windows 10/11 x64 con Npcap; librerías no-UI también compilan y se testean en Linux

**Project Type**: desktop-app (WPF) + librerías + herramienta CLI

**Performance Goals**: refresco del overlay cada 250 ms; < 500 ms de latencia golpe → overlay

**Constraints**: solo lectura pasiva (constitución I); < 3 % CPU y < 150 MB RAM en party de 6

**Scale/Scope**: 1 usuario, 1 PC; parties de hasta 6 (y raids si el protocolo lo permite)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principio | Cumple | Cómo |
|---|---|---|
| I. Solo lectura pasiva | ✅ | Npcap solo en captura; nunca se usa `IInjectionDevice`. El filtro por proceso usa `GetExtendedTcpTable` (tabla TCP del SO), no abre el proceso. |
| II. Capas en un sentido | ✅ | `Capture` y `Protocol` no se referencian entre sí; `Combat → Protocol`; `Storage → Combat`; `App` compone. |
| III. Protocolo en configuración | ✅ | `data/opcodes.json` con lista de valores por tipo; `ProtocolOptions` para el framing. |
| IV. Replay primero | ✅ | `PcapFileSource` + `Capture:Mode=Replay`; `hysteria-rec dump`. |
| V. Robustez | ✅ | `DecoderStats`, resync en huecos, descarte con contador si la cola se llena. |
| VI. Simplicidad | ✅ | 5 proyectos con responsabilidades distintas; sin servicios externos. |

Re-check post diseño: sin violaciones.

## Project Structure

### Documentation (this feature)

```text
specs/001-dps-meter-mvp/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── opcodes-json.md
│   └── combat-events.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── HysteriaMeter.Capture/      # Npcap, filtros de conexión, reensamblado TCP, fuentes Live/Replay
│   ├── Sources/                # IPacketSource, LiveCaptureSource, PcapFileSource, SegmentParser
│   ├── Filtering/              # IConnectionFilter, ProcessConnectionFilter, ServerPortFilter
│   └── Reassembly/             # TcpStreamReassembler, TcpFlowTracker
├── HysteriaMeter.Protocol/     # bytes → eventos
│   ├── Framing/                # VarInt, PacketReader, PacketFramer, BundleDecoder, ProtocolOptions
│   ├── Opcodes/                # PacketKind, OpcodeTable
│   ├── Decoding/               # IPacketDecoder, decoders por tipo, GameStreamDecoder, DecoderStats
│   └── Events/                 # CombatEvent y derivados
├── HysteriaMeter.Combat/       # modelo puro: EntityRegistry, CombatEngine, snapshots
├── HysteriaMeter.Storage/      # EncounterRepository (SQLite)
└── HysteriaMeter.App/          # WPF: overlay, view models, MeterPipeline (BackgroundService)

tools/
└── HysteriaMeter.Recorder/     # hysteria-rec: devices | record | dump

tests/
├── HysteriaMeter.Capture.Tests/
├── HysteriaMeter.Protocol.Tests/
├── HysteriaMeter.Combat.Tests/
└── HysteriaMeter.Storage.Tests/

data/
└── opcodes.json
```

**Structure Decision**: solución multi-proyecto por capas (constitución II). `Capture` y `Protocol` son independientes para poder testear el protocolo con bytes sin Npcap y reutilizar la captura en `hysteria-rec`.

## Complexity Tracking

Sin violaciones que justificar.
