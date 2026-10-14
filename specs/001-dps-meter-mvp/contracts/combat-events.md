# Contrato: eventos de combate (`HysteriaMeter.Protocol.Events`)

Producidos por los decoders, consumidos por `CombatEngine.Apply`. IDs crudos del servidor; la canonicalización ocurre en `Combat`.

| Evento | Campos | Efecto en Combat |
|---|---|---|
| `LocalPlayerEvent` | EntityId, Name? | Marca al jugador local |
| `EntitySpawnEvent` | EntityId, OwnerId?, NpcId? | Registra dueño (invocaciones / IDs transitorios) y si es NPC |
| `EntityInfoEvent` | EntityId, Name, ClassId? | Nombre y clase |
| `DamageEvent` | SourceId, TargetId, SkillId, Amount, Modifiers | Suma al actor canónico si no es NPC; abre encuentro |
| `HealEvent` | SourceId, TargetId, SkillId, Amount | Suma curación durante un encuentro |
| `DeathEvent` | EntityId | Si es el objetivo principal NPC, cierra como kill |

`HitModifiers` (flags): Critical, BackAttack, Parried, Blocked, Evaded, DamageOverTime.

Todos llevan `Timestamp` = timestamp de captura del segmento que completó el frame.
