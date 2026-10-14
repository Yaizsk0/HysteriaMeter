# Data Model

## Capa Capture

| Tipo | Campos | Notas |
|---|---|---|
| `TcpSegment` | Timestamp, Source, Destination, Sequence, Syn, Fin, Rst, Payload | Un segmento por trama capturada |
| `FlowKey` | Source, Destination | Una dirección de una conexión; `Reverse` da la otra |
| `ReassemblyResult` | Chunks, GapDetected | Bytes contiguos nuevos; `GapDetected` obliga a resincronizar |

## Capa Protocol

| Tipo | Campos | Notas |
|---|---|---|
| `RawFrame` | Opcode (ushort), Body | Salida del framer |
| `PacketKind` | Unknown, Bundle, LocalPlayer, EntitySpawn, EntityInfo, Damage, Heal, Death | Claves de `opcodes.json` |
| `CombatEvent` y derivados | ver [contracts/combat-events.md](./contracts/combat-events.md) | IDs crudos del servidor |
| `DecoderStats` | ByOpcode, UnknownFrames, Errors | Diagnóstico |

## Capa Combat

| Tipo | Campos | Notas |
|---|---|---|
| `EntityProfile` | Name?, ClassId?, NpcId? | NpcId ≠ null ⇒ NPC (no rankea) |
| `EntityRegistry` | LocalPlayerId, owners (id→dueño), profiles | `Canonical(id)` sigue la cadena de dueños (máx. 4) |
| `EncounterSnapshot` | Id, Start, End, TargetName?, TargetKilled, TotalDamage, Actors | Inmutable; Duration ≥ 1 s |
| `ActorSnapshot` | Id, Name, ClassId?, IsLocalPlayer, Damage, Dps, Share, Hits, Crits, MaxHit, Healing, Skills | Ordenados por daño |
| `SkillSnapshot` | SkillId, Damage, Hits, Crits, MaxHit, Share | Share relativo al actor |

### Ciclo de vida del encuentro

```text
(sin encuentro) --primer daño de un no-NPC--> En curso
En curso --daño--> En curso (LastActivity = t)
En curso --t - LastActivity > IdleTimeout (daño nuevo o Tick)--> Terminado → EncounterFinished
En curso --muere el objetivo principal (NPC)--> Terminado (TargetKilled = true)
En curso --Reset manual--> Terminado
```

Objetivo principal = el que más daño recibió en el encuentro.

## Capa Storage (SQLite)

```text
encounters(id PK, started_at, ended_at, target_name, target_killed, total_damage)
encounter_actors(encounter_id FK, actor_id, name, class_id, is_local, damage, dps,
                 hits, crits, max_hit, healing, skills_json, PK(encounter_id, actor_id))
```
