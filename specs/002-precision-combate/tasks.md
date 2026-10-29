---
description: "Tareas de la feature 002: precisión del combate"
---

# Tasks: Precisión del combate

**Input**: Design documents from `/specs/002-precision-combate/`

**Prerequisites**: plan.md, spec.md; PR #1 (protocolo real) mergeado

**Tests**: pedidos por la constitución (principio IV): cada hallazgo de protocolo trae un test con bytes reales.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: se puede hacer en paralelo (archivos distintos, sin dependencias)
- **[Story]**: historia de usuario (US1…US6)

## Phase 1: Setup

- [ ] T001 Definir el set de capturas de referencia (10 peleas: muñeco, campo, dungeon en party, con healer, con invocador), cada una con captura de Combat Analysis, y registrarlas en `specs/002-precision-combate/references.md` (sin los `.pcap`)
- [X] T002 [P] Test de replay de punta a punta que compara el total propio de cada captura contra Combat Analysis en `tests/HysteriaMeter.Protocol.Tests/ReferenceFightsTests.cs` (se saltea si la captura no está en la PC). Corre con `HYSTERIA_CAPTURES` apuntando a la carpeta de las capturas; sin la variable o sin el `.pcap` se saltea (la CI no hace replay). Compara el `MainTargetDamage` del jugador local en el encuentro que termina con la muerte del boss (por npcId): mini boss exacto (648.468 y los golpes por modificador) y dungeon a menos de 1 % (2.040.321 contra 2.040.000)

## Phase 2: Foundational

- [X] T003 [P] `HitModifiers` con `Perfect`, `Double`, `Front`, `MultiHit` y `ModifierCoverage` en `src/HysteriaMeter.Protocol/Events/CombatEvents.cs`
- [X] T004 [P] Contadores por modificador y estado "sin identificar" en `ActorSnapshot` y `SkillSnapshot` en `src/HysteriaMeter.Combat/Snapshots.cs`
- [X] T005 Migración de SQLite con las columnas nuevas en `src/HysteriaMeter.Storage/EncounterRepository.cs` + test

**Checkpoint**: modelo listo para recibir los datos nuevos sin cambiar resultados actuales.

## Phase 3: User Story 1 - Tipos de golpe (P1)

- [X] T006 [US1] Research: correlacionar flags de `0x3804` con CRI/PERF/DOUB/FRNT/BACK/MULT y documentarlo en `protocol-notes.md`
- [X] T007 [US1] Decodificar los modificadores en `src/HysteriaMeter.Protocol/Decoding/DamagePacketDecoder.cs` + test con bytes reales
- [X] T008 [US1] Contar multi-golpe como un solo golpe con su daño total en `CombatEngine` + test
- [X] T009 [US1] Mostrar crit % y "sin identificar" en las filas del overlay en `src/HysteriaMeter.App/ViewModels/ActorRowViewModel.cs`

## Phase 4: User Story 2 - Daño completo del local (P1)

- [X] T010 [US2] Research: explicar el ~8 % faltante (`0x3806`, `0x364A`) y documentarlo (resuelto: era el DoT `0x3805`, no `0x3806`)
- [X] T011 [US2] Decoder para la fuente faltante + entrada en `data/opcodes.json` + test
- [X] T012 [P] [US2] Overkill por golpe usando el HP conocido del objetivo en `CombatEngine` + test
- [X] T013 [P] [US2] Daño directo y periódico separados por skill en `CombatEngine` y `SkillSnapshot` + test
- [ ] T014 [US2] Validar SC-001 en las 10 peleas y registrar resultados en `specs/002-precision-combate/validation.md`

## Phase 5: User Story 3 - Party con nombre (P1)

- [X] T015 [US3] `LocalPlayerDecoder` (heurística de movimiento + `0x8D00`, o paquete explícito si aparece) en `src/HysteriaMeter.Protocol/Decoding/` + test
- [X] T016 [US3] Research: paquete de party o legión con nombres
- [ ] T017 [US3] Fuentes secundarias de nombre (dueño de invocación, party) y prioridad entre fuentes en `src/HysteriaMeter.Combat/EntityRegistry.cs` + test
- [X] T018 [US3] Detectar cambio de personaje local sin reiniciar el meter + test

## Phase 6: User Story 4 - Invocaciones (P2)

- [X] T019 [US4] Research: resolver el dueño de invocaciones sin nombre (cast `0x3802` + tiempo + posición) (resuelto: el dueño viene en el propio `0x3641`, como u32 después de `07 02 06` en la cola de un bloque, con una sola marca `80 75 d5 2a bb 03 00 00`. Acierta 100 de 100 con dueño conocido (17 discriminantes) y asigna 106 de los 108 spawns sin nombre de la comparación, sin contradicciones. El `0x3802` y la posición no lo traen; el `0x3804` con `flags 0x00` del dueño 39–112 ms antes sirve de respaldo, con 0 errores contra el u32. Falta confirmarlo con dos jugadores de la misma clase en una conexión, que no está en ninguna captura. Detalle en "Dueño de la invocación" y en el punto 5 de "Pendiente" de `protocol-notes.md` de la 001)
- [X] T020 [US4] Resolución de dueño en `EntityRegistry` + test con la captura de la dungeon (`NpcSpawnDecoder` lee el id del dueño de la cola del `0x3641`, con un fixture real de la dungeon; el motor lo usa antes que el nombre y la clase, reemplaza el dueño de un id reusado y no cuenta como jugador a una invocación que pega antes de su spawn. Detalle en el punto 5 de "Pendiente" de `protocol-notes.md` de la 001)
- [X] T021 [US4] Opción "invocaciones separadas" y total sin invocaciones para comparar con Combat Analysis (Combat marca las skills de las invocaciones con `SkillSnapshot.FromSummon` y suma `ActorSnapshot.SummonDamage`; con la opción, el detalle las junta en una fila "Invocaciones" con el total sin ellas. Que Combat Analysis no las cuente está sin verificar: falta comparar una pelea con invocaciones. Detalle en "Detalle" de `decisiones.md` de la 003)

## Phase 7: User Story 5 - Curación y daño recibido (P2)

- [ ] T022 [US5] Research: validar `0x3805` `0x09`/`0x0B` y encontrar la curación directa con un healer en party
- [ ] T023 [P] [US5] `HealEvent` desde los decoders validados + test con bytes reales (parcial: los ticks `0x09` y `0x0B` de `0x3805` ya tienen fixture real de la captura de comparación del 2026-10-17. El `0x09` cierra con el layout del decoder; el `0x0B` trae un varint más (B) entre el monto (A) y la skill, y el decoder lee mal la skill. El monto que emite hoy tampoco sirve como curación: en la captura A baja exactamente B de un `0x0B` al siguiente (2.803/2.825 pares; en el fixture, de 560 en 560 desde 7851 hasta 571) y el primer `0x0B` cumple A = monto del `0x09` − B (431/431), así que el `0x09` parece el total del HoT y B lo curado por tick. Falta T022: confirmarlo contra el juego y recién ahí cambiar el decoder y los tests, que hoy fijan la lectura actual)
- [X] T024 [P] [US5] Daño recibido por jugador y por skill de origen en `CombatEngine` + test (`ActorSnapshot.DamageTakenBySkill`, guardado en `damage_taken_json`; sin UI. El daño que llega sin pelea abierta se sigue descartando: falta decidir si abre la pelea)
- [ ] T025 [US5] Sobrecuración cuando se conoce la vida del objetivo + test

## Phase 8: User Story 6 - DPS activo y verificación (P3)

- [X] T026 [P] [US6] DPS activo (sin tiempo muerto ni sin objetivo) en `CombatEngine` + test
- [X] T027 [P] [US6] `TargetVerification` por objetivo con HP conocido, guardada en el encuentro + test sintético que no cierra (sin UI: cada pelea guarda en `EncounterSnapshot.Verifications` y `verification_json`, por NPC golpeado, si el daño de todos los jugadores cierra con 1 % de tolerancia con la vida que perdió según el servidor; sin vida en el spawn, la cuenta arranca en la primera lectura. Cierran las 6 de la dungeon y el mini boss no cierra por la escala 30/7 sin explicar entre el daño y la vida: ver "Validación contra el juego" en `protocol-notes.md` de la 001)
- [X] T028 [US6] Mostrar el resultado de la verificación en el diagnóstico (bloque VERIFICACIÓN en Configuración → Diagnóstico con una línea por objetivo, "Verificación: cierra / no cierra" del objetivo principal en el tooltip de la vida y "- Verificación:" sin nombres en el reporte de un problema)

## Phase 9: Polish

- [X] T029 Actualizar `protocol-notes.md` con todos los layouts nuevos y su confianza (convención de confianza y el porqué en cada fila de "Opcodes identificados"; filas nuevas de `0x3656`, `0x920E`/`0x921A`, `0x8A05`, `0x5611` y `0x8D57`/`0x8A03`; `0x3806` pasa a baja y `0x9200` a sin decodificar; layouts nuevos de `0x8D04`, `0x8D00` y `0x921B`; reglas del framing y de cada decoder; tabla de las capturas citadas; layouts viejos de `0x3804`, `0x3805` y `0x3645` corregidos según el código. Sin tocar código; en `data/opcodes.json` solo se corrigió el comentario de movimiento (`0x3600` no es movimiento; `0x371D` sin decodificar). Queda afuera: medir las altas "sin conteo" y alinear con el doc los comentarios de `KillPacketDecoder` (u16 "¿nivel y clase?"), `PlayerSpawnDecoder` ("¿clase o nivel?"), `PeriodicEffectDecoder` (layout sin el varint del `0x0B`), `NpcSpawnDecoder` (solo tipo `0x5F`) y `ReferenceFightsTests` ("dos jugadores de su clase en la party"))
- [ ] T030 Re-correr la validación completa (SC-001 a SC-005) y cerrar `validation.md`

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phases 3–8.
- US1, US2 y US3 (P1) primero; se pueden investigar en paralelo porque usan las mismas capturas.
- T020 depende de T017 (nombres) para resolver dueños por nombre.
- T025 depende de T012 (HP conocido del objetivo).
- Las tareas de research (T006, T010, T016, T019, T022) necesitan capturas nuevas en la PC de Mati.

## Corrección de víctimas de invocaciones (2026-10-17)

- [X] T031 [US4] Separar víctima NPC física y atribución de daño al dueño; conservar aliases sin clasificación NPC y registrar cadáver/reaparición por ID físico.
- [X] T032 [US4] Regresiones de dueño por ID/nombre/clase en encuentro/sesión/run, recaps/log y muerte real posterior; documentar evidencia y límites en `summon-deaths.md`.