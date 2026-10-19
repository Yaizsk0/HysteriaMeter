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
| `0x3804` | Resultado de efecto sobre un objetivo; con `flags & 0x04` trae daño | Alta en el layout (913/913 parsean) y en la escala (ver "Validación contra el juego") | `DamagePacketDecoder` |
| `0x3805` | Tick de efecto periódico: type `0x0A` = DoT, `0x0B` = curación a otro, `0x09` = curación propia | Alta para DoT; curación sin validar (hay fixture real de `0x09` y `0x0B`; el `0x0B` trae un varint más antes del `skillId` y el monto que se lee hoy no parece lo curado por tick, ver el layout) | `PeriodicEffectDecoder` |
| `0x3641` | Aparición de NPC o invocación: entidad, nombre del dueño opcional, ID de NPC, posición, HP actual y máximo | Alta | `NpcSpawnDecoder` |
| `0x3645` | Aparición de otro jugador (1,5–2 KB) con su nombre | Alta para el nombre | `PlayerSpawnDecoder` |
| `0x8D04` | Muerte: víctima, skill, quien mató, nombre y legión (en dungeons llega con killer 0 y sin nombre) | Alta | `KillPacketDecoder` |
| `0x3802` | Lanzamiento de skill (fuente, skill u32, seq, objetivo, posición) | Media | — |
| `0x8D00` | Actualización de recurso: `varint entidad \| 02 01 00 \| u32 HP` para HP (otros recursos como `02 02 00` se ignoran) | Alta | `HealthDecoder` |
| `0x364A` | Solo del jugador local (¿stats o buffs?): el primer varint es su entidad. 0 falsos positivos en 3 capturas | Alta | `LocalPlayerDecoder` |
| `0x3633` | Datos del jugador local al entrar a una zona (~2,3 KB): entidad y nombre | Alta | `LocalPlayerInfoDecoder` |
| `0x3620` | Una vez por conexión: entidad local e ID de personaje persistente, más un bloque zlib | Media | — |
| `0x921B` | HP/MP de un miembro de la party (~1/s por miembro, desde el primer segundo de la conexión); solo compañeros, nunca randoms ni el local | Alta (dungeon en party: los 4; mini boss con 12 ajenos: 0) | `PartyMemberDecoder` |
| `0x9200` | Lista completa de la party (entidad, GUID, ID de personaje, nombre, HP, legión); llegó al irse miembros, falta ver si llega al armarla | Media | — |
| `0x3806` | Estado de entidades: del jugador local y de NPCs, nunca de otros jugadores | Media | — |
| `0x371A`–`0x371D`, `0x3600` | Movimiento / posición de entidades | Media | — |

### Layout de `0x3804`

```text
varint target | u8 flags | u8 flags2 | varint source | u32 skillId | u8 seq | u8 type
| [3 bytes si flags & 0x02] | u32 instance | u32 count | varint ? | varint value | ...
```

- `flags & 0x04`: lleva daño. `flags = 0x00`: aplicación/tick de buff sin valor. Cada frame con daño es **un golpe** para Combat Analysis; los ticks de `0x3805` suman daño pero no golpes.
- `value` es el daño exacto del juego.

Modificadores, validados contra el detalle de Combat Analysis de Destroyer Tarka (2026-10-15: 92 golpes, 648.468 de daño; las 19 skills cierran exacto en golpes, daño y cada columna):

| Columna del juego | Dónde está | Total en la pelea |
|---|---|---|
| CRI | `type = 0x03` (normal `0x02`) | 51 |
| MULT | `flags & 0x20`; la cola trae `[n]` y n varints iguales que **no** suman daño | 23 |
| FRNT / BACK | 3.er byte del bloque extra: `0x02` frente, `0x01` espalda; sin bloque extra, ninguno | 62 / 25 (+5 sin lado) |
| PERF | 1.er byte del bloque extra `& 0x04` | 5 |
| DOUB | 1.er byte del bloque extra `& 0x08` (el valor sale ~2x) | 5 |

- `flags & 0x10` con 1.er byte del extra `0x80`: aparece en todas las skills activas; las pasivas/procs no lo traen.
- `flags2 & 0x04`: agrega al final un varint (~1.100 por golpe en Punishing Strike). ¿Robo de vida? Sin confirmar.
- `varint unknown` es un valor propio de cada personaje (11660 para el de las capturas, otros valores para cada jugador de la party) y 10000 o 0 para NPCs e invocaciones. No marca al jugador local.
- Duración del juego: del primer al último golpe de la pelea (27,65 s ⇒ 23.453 DPS).

### Layout de `0x3805`

```text
varint target | u8 type | varint source | varint tickSeq | u32 instance | varint amount | u32 skillId
```

- `instance` y `skillId` coinciden con los del `0x3804` que aplicó el efecto.
- `type = 0x00`: otro formato, sin monto (se ignora).
- `type = 0x0B` trae un varint más entre `amount` y `skillId` (captura de comparación del 2026-10-17: así cierran los 3.309 ticks y las skills coinciden con las de `0x09` y `0x0A`; con el layout de arriba sobran 1 o 2 bytes). El decoder todavía no lo lee, así que la skill de esos ticks sale mal.
- En esa captura, `amount` no parece lo curado por tick, ni en el `0x09` ni en el `0x0B`. Con A = `amount` y B = el varint extra del `0x0B`: de un `0x0B` al siguiente del mismo efecto (objetivo, instancia, fuente y skill), A baja exactamente B (2.803 de 2.825 pares), y el primer `0x0B` después de un `0x09` cumple A = `amount` del `0x09` − B (431 de 431). En el efecto del fixture, el `0x09` trae 8411 y los 14 `0x0B` siguientes, cada 2 s, traen A = 7851, 7291, …, 571 con B = 560. Se ve como si el `0x09` trajera el total del HoT, A lo que queda y B lo curado en el tick, pero falta confirmarlo contra el juego (T022). Mientras tanto, el monto que emite hoy el decoder para `0x09` y `0x0B` no sirve como curación: ese efecto suma 67.365 (8411 + 58.954) contra 7.840 de B.
- `type = 0x09` no es solo la curación propia: en esa captura 388 tienen fuente igual al objetivo y 353 distinta (el del fixture también).

### Layout de `0x3641`

```text
varint entity | u8 tipo | u8 | u8 hasOwnerName | [varint len + UTF-8 ownerName] | u32 npcId | u8 | u8 | pos 3×f32 | ... | varint HP | varint HPmax
```

- Monstruos: `hasOwnerName = 0`. Invocaciones de jugadores (tipos `0x5F`, `0x1C`): a veces traen el nombre del dueño; el motor lo resuelve contra los nombres conocidos.
- Hay invocaciones sin nombre de dueño y sin ID de dueño en el spawn (por ejemplo el espíritu del Hechicero, skill 15280143 Bittercold Wind: 605k en Kromede, todos críticos). Si le pegan a un monstruo con una skill de jugador (prefijo de clase 11–18) y hay un solo jugador conocido de esa clase, el motor le asigna la invocación. Con dos jugadores de la misma clase sigue sin atribuirse: falta encontrar el dueño real en el protocolo (grabar una pelea con un Hechicero).
- HP y HPmax son los dos últimos varints del cuerpo (validado: 119.700/119.700). Lo que hay entre la posición y el HP no es un solo varint (`9a 38 01` antes del HP), por eso se leen desde el final. El motor toma el máximo visto en `0x8D00` como HPmax y calcula el overkill del golpe final con el HP restante.

### Layout de `0x3645` (parcial)

```text
varint entity | u8 | u16 ? | u8 | 07 | varint len + UTF-8 name | u32 ? (¿clase o nivel?) | ... posición, legión
```

- El campo tras la entidad es un u16 fijo: leído como varint, más de la mitad de los nombres del mini boss salían como `\x07`.

- El jugador local no aparece: su nombre llega por `0x3633`.

### Jugador local (captura de login, 2026-10-15)

```text
0x364A  varint entity | u8 count | (u16 id, u32 valor)...   ← primer bundle de cada conexión, cada 7–18 s después
0x3633  varint entity | 4 bytes | u8 | varint len + UTF-8 name | u16 | ...   ← 6–10 s después de abrir la conexión
0x3620  u16 0 | varint entity | u32 0 | u32 ID de personaje | u16 0 | u16 | bloque zlib (78 9C)
```

- **Cada cambio de zona, login o relog abre una conexión TCP nueva** y el local recibe otra entidad (por ejemplo 850 → 1879, 1244 → 14338). `EntityRegistry` toma la última entidad de `0x364A` como local y le pasa el nombre que ya conocía hasta que llega el `0x3633`.
- `0x5611` (lista de personajes) y `0x8D57`/`0x8A03`/`0x8A05` (nombre, legión y stats en JSON) traen el nombre sin entidad.
- Si la captura arranca con el jugador quieto, `0x364A` puede tardar ~3 s (hasta 103 s en un caso). Respaldo posible: emisor de `0x3806` que no sea un NPC de `0x3641`.

### Item level, power y clase (2026-10-15)

Validado contra la lista de la legión (`0x8A05`, que trae nombre, item level y power de cada miembro) en las tres grabaciones.

```text
0x3645  ... u32 personaje | 00 00 | u16 servidor | u8 n | n × (u16 tag, i32) | u32 nivel | u32 0 | u32 item level ...
        ... ?? f4 cb 1f | u32 | u32 | [u8 extra] | u64 power | 00        ← última aparición del marcador
0x3633  ... nombre | u16 servidor | u32 | u8 | u32 nivel | u32 item level | u32 item level
0x3656  u64 power actual | u64 ?                                         ← power del local, varias veces al cargar; vale el último
```

- Las secciones de `0x3645` tienen largo variable: `PlayerGear` las busca por su forma y descarta valores fuera de rango (item level < 10.000, power < 10 M). Algunas apariciones no traen el item level.
- El power ronda 45–50 por punto de item level, útil para detectar falsos positivos.
- **Clase**: el juego no la manda en un campo claro, pero los IDs de skill de cada clase empiezan con su número (`skillId / 1.000.000` entre 11 y 18; por ejemplo 12010350). Los ítems y pociones (1 a 3 millones) no cuentan. El nombre de cada número se asume en el orden clásico de AION y se corrige en `data/classes.json`.

## Pendiente

1. ~~Escala del daño~~: resuelta con la dungeon (ver "Validación contra el juego"); el factor de la primera captura era un error de análisis.
2. ~~Identificar el jugador local~~: resuelto con `0x364A` y `0x3633` (ver "Jugador local").
3. ~~Crítico y golpes por espalda~~: validados contra Destroyer Tarka (ver "Modificadores").
4. Validar las curaciones contra el juego (tipos `0x09`/`0x0B` de `0x3805` y la curación directa): hace falta una pelea grabada con un healer en la party.
5. Dueño de invocaciones sin nombre cuando dos jugadores comparten clase (ver "Layout de `0x3641`").
6. Nombres de NPC: la tabla ID → nombre está en `data/npcs.json` (vacía en el repo) y `hysteria-extract` saca los nombres de los archivos del juego a la PC de cada uno.
