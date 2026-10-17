---
description: "Tareas del MVP de Hysteria Meter"
---

# Tasks: DPS meter de escritorio para AION 2 (MVP)

**Input**: Design documents from `/specs/001-dps-meter-mvp/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: pedidos por la constitución (principio IV).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: se puede hacer en paralelo (archivos distintos, sin dependencias)
- **[Story]**: historia de usuario (US1…US5)

## Phase 1: Setup

- [X] T001 Crear la solución `HysteriaMeter.slnx` con proyectos por capa en `src/`, `tools/`, `tests/`
- [X] T002 Configurar `Directory.Build.props` (.NET 10, nullable, warnings as errors, analizadores) y `Directory.Packages.props`
- [X] T003 [P] Inicializar Spec Kit y escribir `.specify/memory/constitution.md`
- [X] T004 [P] Agregar CI en `.github/workflows/ci.yml` (build + test en `windows-latest`)

## Phase 2: Foundational

- [X] T005 [P] `VarInt` y `PacketReader` en `src/HysteriaMeter.Protocol/Framing/`
- [X] T006 [P] `PacketFramer` y `ProtocolOptions` en `src/HysteriaMeter.Protocol/Framing/`
- [X] T007 [P] `BundleDecoder` (LZ4) en `src/HysteriaMeter.Protocol/Framing/BundleDecoder.cs`
- [X] T008 [P] `OpcodeTable` + `data/opcodes.json` (vacío) en `src/HysteriaMeter.Protocol/Opcodes/`
- [X] T009 [P] Eventos de combate en `src/HysteriaMeter.Protocol/Events/CombatEvents.cs`
- [X] T010 `GameStreamDecoder` + `DecoderStats` en `src/HysteriaMeter.Protocol/Decoding/GameStreamDecoder.cs`
- [X] T011 [P] `TcpStreamReassembler` + `TcpFlowTracker` en `src/HysteriaMeter.Capture/Reassembly/`
- [X] T012 [P] `PcapFileSource`, `LiveCaptureSource`, `SegmentParser` en `src/HysteriaMeter.Capture/Sources/`
- [X] T013 [P] Filtros `ProcessConnectionFilter` y `ServerPortFilter` en `src/HysteriaMeter.Capture/Filtering/`
- [X] T014 [P] Tests de framing, bundles, opcodes y reensamblado en `tests/HysteriaMeter.Protocol.Tests/` y `tests/HysteriaMeter.Capture.Tests/`

**Checkpoint**: bytes → frames → eventos funcionando con datos sintéticos.

## Phase 3: User Story 5 - Descubrimiento del protocolo (P1, habilitadora)

- [X] T015 [US5] `hysteria-rec devices | record | dump` en `tools/HysteriaMeter.Recorder/Program.cs`
- [X] T016 [US5] Confirmar el nombre del ejecutable del juego (`AION2`) y actualizar `appsettings.json` (FR-015)
- [ ] T017 [US5] Grabar capturas de referencia (pelea solo ✓, party ✓, cambio de zona) y guardarlas fuera del repo
- [X] T018 [US5] Confirmar framing real (opcode u16 LE, largo = bytes + 4, bundle 0xFFFF con u32 + LZ4) y ajustar `ProtocolOptions` (ver `protocol-notes.md`)
- [X] T019 [US5] Identificar opcodes de Bundle, Damage, EntitySpawn y Death y cargarlos en `data/opcodes.json`
- [ ] T019b [US5] Identificar LocalPlayer, EntityInfo (nombres fuera de kills ✓ `0x3645`) y Heal (tick `0x3805` ✓, falta el directo y validar)

## Phase 4: User Story 1 - Mi DPS en vivo (P1) 🎯 MVP

- [X] T020 [US1] `CombatEngine` y snapshots en `src/HysteriaMeter.Combat/`
- [X] T021 [US1] `MeterPipeline` (BackgroundService) en `src/HysteriaMeter.App/Pipeline/MeterPipeline.cs`
- [X] T022 [US1] Overlay básico con ranking en `src/HysteriaMeter.App/Overlay/`
- [X] T023 [US1] Reemplazar el layout provisorio de `DamagePacketDecoder` por el real + test con bytes reales
- [ ] T023b [US1] Confirmar la escala del daño de `0x3804` contra Combat Analysis (✓ exacto con DoT `0x3805`) e identificar el bit de crítico
- [ ] T024 [P] [US1] `LocalPlayerDecoder` + test en `src/HysteriaMeter.Protocol/Decoding/`
- [ ] T025 [US1] Validar contra Combat Analysis en 10 peleas (SC-001) y registrar resultados en `specs/001-dps-meter-mvp/validation.md`

**Checkpoint**: MVP usable para el jugador local.

## Phase 5: User Story 2 - Ranking de la party (P2)

- [X] T026 [US2] Canonicalización de entidades y atribución de invocaciones en `src/HysteriaMeter.Combat/EntityRegistry.cs`
- [X] T027 [P] [US2] `NpcSpawnDecoder` (NpcId) + test con bytes reales
- [ ] T027b [US2] Dueño de invocaciones (✓ por nombre en `0x3641`; faltan las que no traen dueño)
- [ ] T028 [P] [US2] `EntityInfoDecoder` (nombre, clase) + test
- [ ] T029 [US2] Remapear IDs huérfanos al cambiar de zona/pull (edge case) + test en `tests/HysteriaMeter.Combat.Tests/`

## Phase 6: User Story 3 - Overlay que no molesta (P2)

- [X] T030 [US3] Click-through + hotkey Ctrl+Shift+L en `src/HysteriaMeter.App/Interop/OverlayInterop.cs`
- [ ] T031 [US3] Recordar posición y tamaño del overlay entre sesiones
- [ ] T032 [P] [US3] Ventana de detalle por jugador (desglose por skill)

## Phase 7: User Story 4 - Historial (P3)

- [X] T033 [US4] `EncounterRepository` (SQLite) + test en `src/HysteriaMeter.Storage/`
- [X] T034a [P] [US4] `KillPacketDecoder` (muerte + nombre del que mata) + test
- [ ] T034 [P] [US4] `HealDecoder` + test
- [X] T035 [US4] Vista de historial con los últimos encuentros

## Phase 8: Polish

- [ ] T036 [P] Mostrar `DecoderStats` y segmentos descartados en una ventana de diagnóstico
- [ ] T037 [P] Mensaje claro si Npcap no está instalado o no hay permisos
- [X] T040 [P] Elegir automáticamente la placa con tráfico del juego en `LiveCaptureSource`
- [X] T041 [P] `hysteria-rec replay` para correr el pipeline completo sobre un `.pcap`
- [ ] T038 Medir CPU/RAM con party de 6 (SC-003)
- [ ] T039 Decidir licencia (GPL-3.0 si se usan datos de A2Tools) y agregar `LICENSE`

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phase 3 (US5) → Phase 4 (US1) → Phases 5–7 en paralelo → Phase 8.
- T023–T024 dependen de T018–T019 (protocolo real).
- T027–T029 dependen de T019.
