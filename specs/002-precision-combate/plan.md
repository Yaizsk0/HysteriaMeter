# Implementation Plan: Precisión del combate

**Branch**: `002-precision-combate` | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-precision-combate/spec.md`

## Summary

Completar y validar los datos de combate sobre la base del PR #1: decodificar los flags de modificador de `0x3804`, la curación, el daño recibido, el daño faltante del jugador local (`0x3806`) y el dueño de todas las invocaciones; identificar al jugador local y los nombres de la party; agregar overkill, DoT separado, DPS activo y auto-verificación contra la vida del boss. Casi todo el trabajo es de análisis de capturas (Protocol) y de agregación (Combat); la App solo muestra los campos nuevos en el overlay existente.

## Technical Context

**Language/Version**: C# `latest` sobre .NET 10 (LTS)

**Primary Dependencies**: las de la 001; ninguna nueva

**Storage**: SQLite local; columnas nuevas en el encuentro (verificación, overkill, curación, daño recibido)

**Testing**: xUnit; tests de decoders con bytes reales extraídos de las capturas de referencia (`RealCapture.cs`); tests de Combat como modelo puro; test de replay de punta a punta contra los totales de Combat Analysis

**Target Platform**: Windows 10/11 x64 con Npcap; librerías no-UI también en Linux

**Project Type**: desktop-app (WPF) + librerías + herramienta CLI

**Performance Goals**: sin cambios respecto a la 001 (overlay cada 250 ms, < 500 ms golpe → overlay)

**Constraints**: solo lectura pasiva (constitución I); capturas fuera del repo

**Scale/Scope**: parties de hasta 6; capturas de referencia de 2 a 10 minutos

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principio | Cumple | Cómo |
|---|---|---|
| I. Solo lectura pasiva | ✅ | Todo sale de paquetes servidor → cliente ya capturados. El jugador local se identifica por tráfico, no por el proceso. |
| II. Capas en un sentido | ✅ | Los flags se traducen a `HitModifiers` en Protocol; overkill, DPS activo y verificación se calculan en Combat; App solo presenta. |
| III. Protocolo en configuración | ✅ | Opcodes nuevos (`0x3806`, curación directa, party) van a `data/opcodes.json`. Los bits de flags quedan en el decoder, documentados en `protocol-notes.md`. |
| IV. Replay primero | ✅ | Cada hallazgo se valida con replay contra Combat Analysis; tests con bytes reales. |
| V. Robustez | ✅ | Modificadores no identificados son un estado explícito, no un 0 %; un layout roto no tumba el total. |
| VI. Simplicidad | ✅ | Sin proyectos ni dependencias nuevas. |

## Phase 0: Research

Preguntas a resolver con capturas, en este orden. Cada una termina en una entrada de `protocol-notes.md` y un test.

1. **Bits de modificador en `0x3804`**: grabar peleas cortas contra un muñeco con Combat Analysis al final y correlacionar `flags`, `flags2`, el byte `type` y la lista final (`flags & 0x20`) con CRI, PERF, DOUB, FRNT, BACK y MULT. Hipótesis de partida: `flags & 0x20` = multi-golpe.
2. **El ~8 % faltante (`0x3806`)**: comparar, pelea por pelea, la diferencia contra Combat Analysis con la suma de valores candidatos en `0x3806` y `0x364A`.
3. **Jugador local**: formalizar la heurística que funcionó (única entidad sin movimiento `0x371B`–`0x371D` que recibe casi todos los `0x8D00`) y buscar un paquete explícito en una captura desde el login.
4. **Nombres de la party**: buscar paquetes de party o legión con nombres; usar como fuente secundaria el nombre de dueño de invocaciones (`0x3641`).
5. **Invocaciones sin dueño**: correlacionar el spawn de la invocación con el `0x3802` (cast) del jugador que la invocó, por tiempo y posición.
6. **Curación**: validar `0x3805` tipos `0x09`/`0x0B` y buscar la curación directa (no periódica) con un healer en party.
7. **Daño recibido**: confirmar que `0x3804` con objetivo jugador y origen NPC cubre todo el daño del boss.

## Phase 1: Design

- `HitModifiers` gana `Perfect`, `Double`, `Front` y `MultiHit`, y un `ModifierCoverage` (qué bits están identificados) para distinguir 0 % de "sin identificar".
- `DamageEvent` gana `IsPeriodic` (hoy se infiere de `DamageOverTime`) y Combat calcula `Overkill` con el HP conocido del objetivo (`0x8D00`, `0x3641`).
- `EntityRegistry` guarda la fuente del nombre y la resolución de dueño de cada invocación.
- `ActorSnapshot` gana `DamageTaken`, `Healing` por skill, `ActiveDps`, `SummonDamage` y conteos por modificador; `EncounterSnapshot` gana una lista de `TargetVerification`.
- Storage: migración que agrega esas columnas sin perder el historial existente.

## Project Structure

### Documentation (this feature)

```text
specs/002-precision-combate/
├── spec.md
├── plan.md
└── tasks.md
```

`research.md` y `data-model.md` se agregan al cerrar la Phase 0, con lo que salga de las capturas.

### Source Code (archivos que se tocan)

```text
src/HysteriaMeter.Protocol/
├── Events/CombatEvents.cs          # HitModifiers, ModifierCoverage, IsPeriodic
└── Decoding/
    ├── DamagePacketDecoder.cs      # flags → modificadores
    ├── PeriodicEffectDecoder.cs    # curación validada
    ├── LocalPlayerDecoder.cs       # nuevo
    └── (decoder de 0x3806 y de party, según lo que salga de la research)
src/HysteriaMeter.Combat/
├── CombatEngine.cs                 # overkill, daño recibido, DPS activo, verificación
├── EntityRegistry.cs               # fuentes de nombre, dueño de invocaciones
└── Snapshots.cs
src/HysteriaMeter.Storage/EncounterRepository.cs
src/HysteriaMeter.App/ViewModels/   # columnas nuevas en las filas existentes
data/opcodes.json
tests/                              # un test con bytes reales por hallazgo
```

**Structure Decision**: se mantiene la estructura de la 001; no hay proyectos nuevos.

## Complexity Tracking

Sin violaciones que justificar.
