# Notas del protocolo (cliente global)

Primera captura: 2026-10-14, ~2 min de combate contra monstruos de campo, Wi-Fi. Conexión del juego: puerto de servidor 13328. Resultado: 380 KB del servidor → 16.824 frames (17.666 con el contenido de los bundles), **0 errores** de framing.

Segunda captura: 2026-10-14, dungeon en party de 5 (7 min, 3,7 MB, Wi-Fi). Server → cliente: 68.422 frames, 691 bundles, 0 errores de descompresión, 3 desyncs del framer. Referencia del juego: Combat Analysis del último boss (HP 9.860.000), 02:25, 2,04M de daño del jugador local.

## Validación contra el juego

- **Escala:** el valor de `0x3804` es HP real. Daño directo (`0x3804`) + ticks de DoT (`0x3805` type `0x0A`) cierran exacto 1.631 de 1.632 intervalos entre actualizaciones de HP del boss (`0x8D00`); el que no cierra es el overkill del golpe final.
- **Jugador local:** 1.875.033 directo + 165.288 DoT = **2.040.321**, contra 2,04M (14,02K DPS en 145,5 s) de Combat Analysis.
- Combat Analysis no cuenta el daño de las invocaciones.

## Framing (confirmado)

```text
[varint L][opcode u16 LE][cuerpo]      bytes después del prefijo = L - 4
```

- Sin cifrado en servidor → cliente.
- Cliente → servidor: bloques de 12 bytes de aspecto aleatorio; **no se decodifica** (parece cifrado). Por eso el pipeline solo decodifica la dirección servidor → cliente (`FlowDirection.IsFromServer`).
- Bundle: opcode `0xFFFF`, cuerpo `[u32 LE largo descomprimido][bloque LZ4]`; adentro hay frames con el mismo formato.
- Con Wi-Fi, los paquetes entrantes llegan con etiqueta VLAN 802.1Q (PacketDotNet la maneja).

## Opcodes identificados

| Opcode | Qué es | Confianza | Decoder |
|---|---|---|---|
| `0xFFFF` | Bundle LZ4 | Alta (82/82 descomprimen) | `GameStreamDecoder` |
| `0x3804` | Resultado de efecto sobre un objetivo; con `flags & 0x04` trae daño | Alta en el layout (913/913 parsean); ver "Pendiente" para la escala | `DamagePacketDecoder` |
| `0x3805` | Tick de efecto periódico: type `0x0A` = DoT, `0x0B` = curación a otro, `0x09` = curación propia | Alta para DoT; curación sin validar | `PeriodicEffectDecoder` |
| `0x3641` | Aparición de NPC o invocación: entidad, nombre del dueño opcional, ID de NPC, posición, HP actual y máximo | Alta | `NpcSpawnDecoder` |
| `0x3645` | Aparición de otro jugador (1,5–2 KB) con su nombre | Alta para el nombre | `PlayerSpawnDecoder` |
| `0x8D04` | Muerte: víctima, skill, quien mató, nombre y legión (en dungeons llega con killer 0 y sin nombre) | Alta | `KillPacketDecoder` |
| `0x3802` | Lanzamiento de skill (fuente, skill u32, seq, objetivo, posición) | Media | — |
| `0x8D00` | Actualización de recurso: `varint entidad \| 02 01 00 \| u32 HP` para HP | Alta | — |
| `0x3806`, `0x364A` | Solo del jugador local (y algunas invocaciones); sin daño. ¿Estado? | Baja | — |
| `0x371A`–`0x371D`, `0x3600` | Movimiento / posición de entidades | Media | — |

### Layout de `0x3804`

```text
varint target | u8 flags | u8 flags2 | varint source | u32 skillId | u8 seq | u8 type
| [3 bytes si flags & 0x02] | u32 instance | u32 count | varint ? | varint value | ...
```

- `flags & 0x04`: lleva daño. `flags = 0x00`: aplicación/tick de buff sin valor.
- `flags & 0x20`: agrega al final una lista corta (p. ej. `03 33 33 33`); ¿multi-golpe?
- `flags2 & 0x04`: agrega 1–2 bytes al final.
- Bit de crítico: **sin identificar**.

### Layout de `0x3805`

```text
varint target | u8 type | varint source | varint tickSeq | u32 instance | varint amount | u32 skillId
```

- `instance` y `skillId` coinciden con los del `0x3804` que aplicó el efecto.
- `type = 0x00`: otro formato, sin monto (se ignora).

### Layout de `0x3641`

```text
varint entity | u8 tipo | u8 | u8 hasOwnerName | [varint len + UTF-8 ownerName] | u32 npcId | u8 | u8 | pos 3×f32 | ... | varint HP | varint HPmax
```

- Monstruos: `hasOwnerName = 0`. Invocaciones de jugadores (tipos `0x5F`, `0x1C`): a veces traen el nombre del dueño; el motor lo resuelve contra los nombres conocidos.
- Hay invocaciones sin nombre de dueño y sin ID de dueño en el spawn: su daño todavía no se atribuye.

### Layout de `0x3645` (parcial)

```text
varint entity | u8 | varint ? | u8 | varint len + UTF-8 name | u32 ? (¿clase o nivel?) | ... posición, legión
```

- El jugador local no aparece: su `0x3645` llega al loguear o al entrar a la zona.

## Pendiente

1. ~~Escala del daño~~: resuelta con la dungeon (ver "Validación contra el juego"); el factor de la primera captura era un error de análisis.
2. Identificar el jugador local. Heurística que funcionó en la dungeon: es la única entidad sin paquetes de movimiento (`0x371B`–`0x371D`) y la que recibe casi todos los `0x8D00`. El nombre propio sale del dueño de sus invocaciones o de una captura desde el login.
3. Crítico, golpes por espalda, validar curaciones contra el juego, dueño de invocaciones sin nombre.
4. Nombres de NPC: el ID de NPC está, falta una tabla ID → nombre.
