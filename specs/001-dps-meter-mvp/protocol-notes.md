# Notas del protocolo (cliente global)

Primera captura: 2026-10-14, ~2 min de combate contra monstruos de campo, Wi-Fi. Conexión del juego: puerto de servidor 13328. Resultado: 380 KB del servidor → 16.824 frames (17.666 con el contenido de los bundles), **0 errores** de framing.

Segunda captura: 2026-10-14, dungeon en party de 5 (7 min, 3,7 MB, Wi-Fi). Server → cliente: 68.422 frames, 691 bundles, 0 errores de descompresión, 3 desyncs del framer. Referencia del juego: Combat Analysis del último boss (HP 9.860.000), 02:25, 2,04M de daño del jugador local.

## Validación contra el juego

- **Escala:** el valor de `0x3804` es HP real. Daño directo (`0x3804`) + ticks de DoT (`0x3805` type `0x0A`) cierran exacto 1.631 de 1.632 intervalos entre actualizaciones de HP del boss (`0x8D00`); el que no cierra es el overkill del golpe final.
- **Jugador local:** 1.875.033 directo + 165.288 DoT = **2.040.321**, contra 2,04M (14,02K DPS en 145,5 s) de Combat Analysis.
- Combat Analysis no cuenta el daño de las invocaciones.
- **Orden de `0x8D00` y `0x3804`** (replay del 2026-10-18): el `0x8D00` llega después de los golpes que refleja, en el mismo bundle y con el mismo timestamp. En el boss de la dungeon (Divine Auldor), 1.630 de las 1.631 lecturas cierran exacto con los golpes anteriores desde la lectura previa; si cada lectura incluyera el golpe siguiente, cerrarían 5. El spawn `0x3641` de ese boss trae 9.860.000 de 9.860.000 (lo que muestra el juego) y el primer `0x8D00` (9.857.918) llega después del primer golpe (2.082). El `0x8D00` de 0 llega después del golpe mortal: los golpes desde la última lectura positiva la pasan por 296, y entre la lectura de 0 y el `0x8D04` llegan 10.130 más. Por eso el overkill se mide desde la última lectura positiva y la de 0 no cuenta. `TargetVerification` del boss: vida inicial 9.860.000, contado 9.870.426, overkill 10.426, diferencia 0 (cierra). Cierran las 6 verificaciones de la dungeon.
- **Add de la dungeon (npc 2300215, #34157, pelea de las 21:48:05):** no cerraba (vida inicial 20, contado 104.736, overkill 1.588, diferencia +103.128) porque la vida de su spawn se leía de los dos últimos varints del cuerpo, que eran 20 y 91 (ver "Layout de `0x3641`"). Se descartó que fuera una vida vieja del servidor (de una pelea anterior): el spawn llegó 52 ms antes del primer golpe y antes no hubo ningún `0x8D00`, spawn ni muerte de esa entidad. En la posición fija trae 103.148 de 435.000: el primer `0x8D00` (99.572) más el golpe anterior (3.576) da 103.148, y la verificación cierra con diferencia 0.
- **Vida inicial tomada de antes de la pelea:** si la inactividad corta una pelea, la siguiente contra el mismo objetivo arranca con la última lectura de la anterior, y esa lectura sirve. En la captura de login (15 casos, de 0,1 a 6,5 s entre lecturas) y en la de comparación (6 casos, hasta 9,1 s), la primera lectura de la pelea es esa menos los golpes del medio, con resto 0. En Destroyer Tarka, 9,7 s después la vida bajó 14.670 con 3.423 de daño (× 4,286: la misma escala 30/7, sin regenerar). La lectura de 0 no sirve: en la de comparación, una entidad del npc 2920091 reaparece 899 s después de su 0.
- **Mini boss (Destroyer Tarka): la vida baja 30/7 del daño.** En 100 de los 102 intervalos entre lecturas del boss, la vida perdida es el daño decodificado × 4,2857 (± 0,01), y en la pelea del kill los únicos `0x3804` sobre el boss son los del local (648.468, lo mismo que Combat Analysis). No había party (ningún `0x921B`). De los otros dos intervalos, uno es el golpe final (1.954 de daño contra 2.689 de vida) y en el otro (13:39:57) un `0x3804` de 31.500 con fuente NPC **sube** la vida 135.000 (= 31.500 × 30/7): es una curación del boss que hoy se lee como daño (el motor no la cuenta porque la skill, 1910071, no es de clase). Sin confirmar de dónde sale la escala; en la dungeon es 1. `TargetVerification` del kill no cierra: vida inicial 2.626.900 (el último `0x8D00` de los 3 s anteriores contra el boss, que quedaron en otra pelea por inactividad), contado 648.468, overkill 2.044, diferencia −1.980.476. La de esos 3 s tampoco: 2.700.000 del spawn → 2.626.900 con 17.054 contados (× 4,286).
- **El resto del mini boss tampoco cierra:** de las 119 verificaciones de esa captura cierra una sola (npc 2920096, 3 → 0); las 116 que no son de Tarka ni esa son monstruos de campo e invocaciones. En los monstruos de campo (npc 2340151–2340158, 91), la vida que pierden entre el spawn y su primera lectura es el daño decodificado × 4,50 (de 4,5 a 4,511 en los 80 que reciben golpes antes de esa lectura; por ejemplo 540.000 → 531.644 con 1.856 de daño), otra escala sin explicar; en el total de cada pelea la razón va de 3,1 a 4,7, sin investigar. Las invocaciones sin dueño de clase 16 (npc 2920113–2920185, 25) no son objetivos: unos 100 ms después de aparecer se pegan a sí mismas dos veces con la skill 16990004 (su vida máxima y 100.000), lo contado es una skill de un jugador sobre ellas (16770001, ~500 por golpe) y la vida se la bajan los monstruos (NPC contra NPC con skills de prefijo 1, que no cuentan).
- **Comparación del 2026-10-17 (investigada el 2026-10-19):** el faltante de los bosses 2300311, 2300312 y 2300371 (−3,4 a −4,8 %) es entero el daño de invocaciones sin dueño que el motor toma por NPC: npc 2920655, 2920739 y 2920735, con spawn `0x3641` de tipo `0x5F` sin `ownerName` (el id del dueño viene en la cola, que el decoder no lee: ver "Dueño de la invocación") y skills de prefijo 15 (15280142/15280143, 15390022, 15200022/15200023). Son 1.277 golpes y 2.579.846 de daño sobre los bosses, que dejan la diferencia en 0 exacto en las 8 verificaciones que no cerraban. Intervalo por intervalo, 1.251 de 1.251 intervalos con faltante son exactamente el daño de invocación del intervalo, y los 9.270 que cierran no tienen ninguno. No es una escala: sin invocaciones, la razón vida/daño es 1,00. `TryAdoptSummon` ve de 2 a 4 candidatos de clase 15 (el de la conexión, los ids de conexiones anteriores y una entidad que el motor trata como jugador solo por sus skills: ver el punto 5 de "Pendiente") y exige uno solo, así que no se adoptan. En la dungeon, con un solo jugador de clase 15, sí se adoptan. La verificación cuenta (sin sumarlo al DPS) el golpe de NPC a NPC con skill de clase 11–18 y fuente distinta del objetivo; con eso cierran 36 de 52 en vez de 17, sin romper ni agregar ninguna en las seis capturas (dungeon 6/6, login 30/32 y mini boss 1/119 sin cambios; Tarka sigue en 648.468). Las 19 que pasan a cerrar quedan con diferencia 0. Un golpe de invocación puede crear la cuenta y adelantar su vida inicial: la de 2300307 #44462 pasa de 296.966 a 304.000 y cierra igual, y en el mini boss cambia la de 11 cuentas, que no cierran ni antes ni después. El filtro de skill hace falta: sin él aparecen 25 verificaciones de 2920091 y 24 no cierran. Además deja afuera la curación del mini boss leída como daño (skill 1910071, de prefijo 1); que la fuente sea distinta del objetivo deja afuera las invocaciones que se pegan a sí mismas al aparecer (16990004). Quedan 16 abiertas por otras causas: 14 entidades de 2920091 y una de 2920650 que pasan de unos 8,2 M (13.948 la de 2920650) a 0 al morir sin ningún golpe (muerte sin daño), y un monstruo 2300306 (#39344, diferencia +59.795) cuya vida vuelve de 206.205 a 266.000 a las 06:49:48 sin daño. Las entidades 2920091 tienen vida propia (22 de 24 intervalos cierran con su daño) y no comparten vida con el boss. La continuación (`comparacion-continuacion-20261017-0402`) no tiene daño.
- **Otras capturas (replay del 2026-10-18, sin investigar):** en la de login cierran 30 de 32; no cierran dos del npc 2600072 (diferencias −5.401 y −1.186).

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
| `0x3641` | Aparición de NPC o invocación: entidad, nombre del dueño opcional, ID de NPC, posición, HP actual y máximo; en las invocaciones de un bloque, el id del dueño en la cola | Alta; el dueño de la cola acierta 100 de 100 con dueño conocido (ver "Dueño de la invocación") | `NpcSpawnDecoder` (no lee el dueño de la cola) |
| `0x3645` | Aparición de otro jugador (1,5–2 KB) con su nombre | Alta para el nombre | `PlayerSpawnDecoder` |
| `0x8D04` | Muerte: víctima, skill, quien mató, nombre y legión (en dungeons llega con killer 0 y sin nombre) | Alta | `KillPacketDecoder` |
| `0x3802` | Lanzamiento de skill: fuente, skill u32, seq, mode, objetivo y, con `mode 0x02`, la posición del objetivo (ver "Layout de `0x3802`") | Alta para fuente y skill; media para objetivo y posición | — |
| `0x8D00` | Actualización de recurso: `varint entidad \| 02 01 00 \| u32 HP` para HP (otros recursos como `02 02 00` se ignoran) | Alta | `HealthDecoder` |
| `0x364A` | Solo del jugador local (¿stats o buffs?): el primer varint es su entidad. 0 falsos positivos en 3 capturas | Alta | `LocalPlayerDecoder` |
| `0x3633` | Datos del jugador local al entrar a una zona (~2,3 KB): entidad y nombre | Alta | `LocalPlayerInfoDecoder` |
| `0x3620` | Una vez por conexión: entidad local e ID de personaje persistente, más un bloque zlib | Media | — |
| `0x921B` | HP/MP de un miembro de la party (~1/s por miembro, desde el primer segundo de la conexión); solo compañeros, nunca randoms ni el local | Alta (dungeon en party: los 4; mini boss con 12 ajenos: 0) | `PartyMemberDecoder` |
| `0x9200` | Lista completa de la party (entidad, GUID, ID de personaje, nombre, HP, legión); llegó al irse miembros, falta ver si llega al armarla | Media | — |
| `0x3806` | Estado de entidades: del jugador local y de NPCs, nunca de otros jugadores | Media | — |
| `0x371A`–`0x371C` | Movimiento: entidad y posición x, y, z (ver "Movimiento") | Media | — |
| `0x371D` | Movimiento (el 77 % de los mensajes de movimiento) | Sin decodificar | — |
| `0x3600` | Reloj: u64 LE en ms, uno cada 50 ms (ver "Movimiento") | Media | — |

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
varint entity | u8 tipo | u8 | u8 hasOwnerName | [varint len + UTF-8 ownerName] | u32 npcId | u8 flags | u8 | pos 3×f32 | f32
| [12 bytes si flags & 0x08] | 3 bytes | varint HP | varint HPmax | cola
```

- Monstruos: `hasOwnerName = 0`. Invocaciones de jugadores (tipos `0x5F`, `0x1F`, `0x1C`): a veces traen el nombre del dueño; el motor lo resuelve contra los nombres conocidos.
- Hay invocaciones sin nombre de dueño (por ejemplo el espíritu del Hechicero, skill 15280143 Bittercold Wind: 605k en Kromede, todos críticos). Las de un bloque traen el id de entidad del dueño en la cola (ver "Dueño de la invocación"), pero el decoder todavía no lo lee. Hoy, si le pegan a un monstruo con una skill de jugador (prefijo de clase 11–18) y hay un solo jugador conocido de esa clase, el motor le asigna la invocación; si no hay exactamente uno, queda como NPC.
- HP y HPmax van en posición fija, 21 bytes después del `npcId`: dos u8, la posición, un f32 y tres bytes sin identificar (`9a 38 01` en el fixture), con 12 bytes más antes de los tres si el primer u8 tiene el bit `0x08` (ver abajo). El último de los tres es `0x01` en 2.891 de los 2.893 spawns de las seis capturas de referencia; los otros dos, un `0x2C` (npc 2920021, 0 de 435.000) y un `0x2F` (npc 2920626, 0 de 19.211), traen `00`. Después de la vida puede venir más, de 0 a unos 190 bytes. Antes se leían como los dos últimos varints del cuerpo, que solo sirve sin nada después (el fixture de 119.700/119.700): en la dungeon y el mini boss, los 76 cuerpos en los que los dos últimos varints pasaban por vida daban basura (20/91, 0/168.274.232, …).
- En la posición fija, 22 spawns de la dungeon y del mini boss cierran exacto con su primera lectura (vida del spawn = `0x8D00` + golpes anteriores), entre ellos el add y el boss de la dungeon; los que tienen lectura y no cierran son monstruos del mini boss (con otra escala, ver "Validación contra el juego") o invocaciones (algunas se pegan a sí mismas al aparecer). En la de comparación del 2026-10-17 traen vida los 2.207 spawns y cierran exacto 114 de los 115 que tienen lectura; en la de login traen vida los 111 y cierran exacto los 19 que tienen lectura. En ninguna de las seis la primera lectura (antes de que el id vuelva a aparecer) queda por encima de la vida del spawn.
- Un spawn puede traer la vida incompleta: el add 2300215 de la dungeon aparece con 103.148 de 435.000.
- **Bloque de 12 bytes:** con el bit `0x08` en el u8 que sigue al `npcId` vienen 12 bytes más, sin identificar, antes de los tres bytes y la vida. Ese u8 vale `0x00`, `0x40` (no agrega nada) u `0x08`, y no depende del tipo: en las seis capturas hay 11 spawns con `0x08`, 4 del tipo `0x85` (npc 2910001: 4.655 de 4.655), 6 del `0x0D` (npc 2600091, en la de login: 20.000.000 de 20.000.000, la misma vida que sus 17 spawns del tipo `0x0F`) y 1 del `0x5D` (npc 2920115: 23.983 de 23.983). En la posición de siempre daban basura y 6 de los 11 pasaban la validación (HP ≤ HPmax: 23/234.659.361, 8.706/1.481.659, 43/1.679.750, …). Ninguna de esas entidades tiene una lectura positiva en las capturas, así que la vida nueva no está validada contra un `0x8D00`: solo es consistente (vida llena, la misma en las 4 apariciones del 2910001).
- Sin la vida del spawn, el motor toma el máximo visto en `0x8D00` como HPmax. El overkill del golpe final sale del HP restante.

#### Dueño de la invocación (investigado el 2026-10-19)

```text
cola de un bloque:  ... | 80 75 d5 2a bb 03 00 00 | varint e2 | u8 k1 | u8 02 | 3×f32 | 07 02 06 | u32 dueño | ...
```

- **Regla:** si la cola tiene una sola aparición de la marca `80 75 d5 2a bb 03 00 00` y después sigue exactamente esa estructura, el u32 que sigue a `07 02 06` es el id de entidad del dueño. Hay que anclarse en la estructura y no en un offset: la marca cae en el byte 68 o 69 de la cola y el dueño en el 95, 96 o 97, según el largo de `e2` (2 o 3 bytes). Fuera de esa forma no se usa (ver "Refutado").
- **Confirmado** (recalculado de los volcados de las seis capturas y vuelto a calcular con un parser independiente, que coincide en todo lo que midieron los dos):
  - 395 spawns tienen esa forma: en la comparación 148 `0x5F` y 18 `0x1F`; en la dungeon 40 `0x5F`; en la de login 1 `0x5F`; en el mini boss 124 `0x5F` y 64 `0x1F`.
  - Contra el dueño conocido (nombre resuelto, único jugador de la clase o adopción del motor) acierta 100 de 100, de 17 dueños distintos: 82 `0x5F` (comparación 40, dungeon 40, login 1, mini boss 1) y 18 `0x1F` (npc 2920626, comparación). Solo 17 de los 100 discriminan (dos o más jugadores de la clase activos en la misma conexión), y aciertan los 17; los demás tenían un solo candidato. Fuera del conteo queda la entidad 56247, un id reusado en el que el nombre da 4069 y la adopción del motor 1628 (ver el punto 5 de "Pendiente"): el u32 da 4069, como el nombre.
  - La clase del dueño (prefijo de sus skills) es la de la invocación en 395 de 395. En 394 el dueño ya se había visto en la misma conexión; en el otro, el primer spawn del mini boss, el spawn llega 153 ms antes que la primera aparición del dueño.
  - Buscando el id del dueño conocido en todo el cuerpo, en cada offset, como u32, u16, varint y u64, aparece solo en ese lugar.
  - El respaldo por tiempo de abajo, que no lee la cola, nunca lo contradice (0 errores en los 395).
  - En la comparación, los 108 spawns `0x5F` sin nombre de npc 2920655, 2920739 y 2920735 tienen una sola marca. Los 106 con `07 02 06` dan un solo dueño por conexión y sin contradicciones: conexión 1 → 12962 (17 spawns), conexión 21 → 12426 (42) y conexión 46 → 6381 (47); los otros 2 traen `07 02 01` (ver "No concluyente"). Con esa asignación, sus golpes `0x3804` con monto > 0 son 640 (1.747.358) de 12426, 167 (425.621) de 12962 y 569 (1.444.756) de 6381. Este caso no discrimina: en cada conexión hay un solo jugador de clase 15 (ver el punto 5 de "Pendiente").
- **Respaldo por tiempo, confirmado:** el dueño manda por la misma conexión un `0x3804` con `flags 0x00` entre 39 y 112 ms antes del `0x3641` (con dueño conocido, de 47,8 a 105,0 ms en 101 de 101; llegan en pasos de unos 50 ms, casi todos a 40–50 o 90–100 ms). Regla: de los jugadores conocidos sin `0x3641` propio que mandaron ese `0x3804` en la ventana, quedarse con los que usaron una skill del mismo grupo (`skillId / 10.000`) que alguna skill de clase de la invocación, y adoptar solo si queda uno. Acierta 101 de 101 con dueño conocido; contra el u32 de la cola, en los 395: 367 aciertos, 20 con dos o más candidatos (siempre incluyen al dueño), 8 sin candidato y 0 errores. Los 28 que no decide son de clase 16, en el mini boss. Sirve para cuando la cola no tiene la forma.
- **Refutado** (no volver a probar):
  - El mismo campo en otras formas no es el dueño. En la forma de dos bloques (`0x1F` npc 2920342 y 2920345, con `01 <pos> 07 02 06` en el segundo bloque) acierta 0 de 329: apunta a bosses (198) o a la entidad 35952 (131), que el motor trata como jugador solo por sus skills. En `0x1C` (`06 <u32>` sin `07 02`) apunta al boss: 0 de 132, y el dueño conocido no aparece en ninguna parte de su cuerpo. Por eso hace falta la guarda de forma.
  - `e2` no es el dueño: con `k1 = 0x01` es la propia invocación; con `k1` mayor (138 spawns, todos del mini boss) vale lo mismo que el u32.
  - Ningún otro offset ni codificación del cuerpo trae el dueño (los dos u8 tras el tipo, el f32 que sigue a la posición, los 3 bytes, el bloque de 12 y toda la cola, como varint, u16, u32 o u64).
  - Campos chicos: en estas invocaciones, los u8 que siguen al tipo y al `npcId` y el bloque de 12 son constantes; el f32 y los 3 bytes cambian en cada spawn; los u32 de los bytes 0 y 20 de la cola y la vida se comparten entre dueños y cambian dentro de un mismo dueño.
  - La cadena con prefijo de largo de la cola de `0x5F`, `0x1F` y `0x1C` no es el nombre del dueño (0 de 82 con nombre) ni resuelve a un jugador, y su largo no separa dueños. En la comparación coincide con un texto de legión del `0x8D04` en 148 de 150 `0x5F` (INTERPRETADO: un nombre de legión). Solo en un `0x2F` (entidad 30205), con otra estructura, es igual al nombre del dueño.
  - Ningún campo del `0x3802` ni del `0x3804` del dueño (instance, count, unk, value, seq) se repite en el cuerpo del spawn. El `0x3802` tampoco trae el vínculo (ver "Layout de `0x3802`").
  - Sin filtrar por grupo de skill, el `0x3804` de 39–112 ms con una skill de la clase da, contra el u32, 332 aciertos, 60 con dos o más candidatos, 2 sin candidato y 1 error.
  - Tomar el evento más cercano en vez de exigir candidato único da errores: 4 con el `0x3804` y 10 con el `0x3802` en la comparación, y 1 en el mini boss. El emparejamiento tiene que ser uno a uno: en el error del mini boss, el evento a 0 ms del otro jugador es el de su propia invocación, que aparece 100,8 ms después.
  - El `0x3802` del dueño como ancla temporal: llega entre 134 y 998 ms antes del spawn, sin una ventana estable. Con 0–1.000 ms, en el mini boss deja 6 sin candidato y 91 con dos o más.
  - Posición: las invocaciones sin nombre de clase 15 aparecen sobre el objetivo, no junto al dueño (el dueño es el jugador más cercano en 1 de 26 en la dungeon; el candidato de la conexión, en 8 de 106 en la comparación). Las que traen nombre sí aparecen junto al dueño (en los 15 casos discriminantes de la comparación es el jugador más cercano, a una mediana de 246 unidades contra 1.490 del otro candidato), pero esas ya traen el nombre. Que el spawn coincida con la posición de un `0x3802` (ver su layout) tampoco discrimina: en la dungeon coinciden dos o más lanzadores en 20 de 23.
  - El último dígito de la skill del golpe (15280142 frente a 15280143) cambia dentro de una misma invocación: 104 de 106 usan dos o más en la comparación, y 26 de 26 en la dungeon, con un solo dueño.
  - El jugador de la clase en la misma conexión, sin leer la cola: en la comparación alcanza si no se cuenta a la entidad 63831 (ver el punto 5 de "Pendiente"), pero en el mini boss, con varios jugadores de la misma clase, no decide ninguna.
- **No concluyente:**
  - `07 02 01` en lugar de `07 02 06`: 3 casos. Dos `0x5F` de la comparación (43137 y 64475) dan 12426, coherente con su conexión, pero no tienen dueño conocido y no tienen golpes después de su `0x3641`; el `0x2F` 30205 da 2055 y su dueño por nombre es 9829 (0 de 1). No se usa.
  - Id de personaje del dueño o índice de party: los volcados no traen `0x3620` ni el índice del `0x921B`, así que no se pudo medir.
  - La skill base redondeada a decenas (15280140 frente a 15280240) distingue variantes de una misma clase (en el mini boss, cada familia la usa uno solo de los dos jugadores de clase 15) y no falla con dueño conocido, pero decide sola muy pocos casos. En las clases 14, 15 y 17, la skill base del dueño y el npc de la invocación van uno a uno dentro de cada captura (por ejemplo 15280140 → 2920655); en la clase 16 (mini boss) no.
  - La clase 16 del mini boss (138 spawns con `k1` mayor que 1) no tiene ningún caso con dueño conocido: la respaldan la clase, `e2` igual al u32 y el respaldo por tiempo.
  - `0x17` npc 2920347 (711 spawns sin nombre de dos bloques en la comparación): el u32 apunta siempre a un `0x1F` npc 2920346 sin nombre (INTERPRETADO: el creador inmediato, una cadena de invocaciones). Sus 47 golpes (251.926) quedan como NPC; sin investigar.
  - INTERPRETADO: que el u32 sea el invocador (y, en las otras formas, el NPC que crea la invocación); qué significan `k1` y el byte que sigue a `07 02`; y que la marca sea fija: es igual en las seis capturas, de tres días distintos, pero un parche del juego podría cambiarla.

### Layout de `0x3802` (investigado el 2026-10-19)

```text
varint source | u8 b1 | u32 skillId | u8 seq | u8 mode | varint target | f32 f1
| [f32 f2, f32 f3, f32 f4 si mode & 0x02] | varint unk | u8 b_n | varint v_last
```

- **Fuente y skill: confianza alta.** El layout consume el cuerpo exacto en 66.778 de los 66.992 casts de las seis capturas. Los otros 214 son de `mode 0x00` con `b1` = `0x08` o `0x0C` y les sobran de 2 a 4 bytes, pero su skill es una que la misma fuente usó en `0x3804` en los 214 (INTERPRETADO: `b1` sería un byte de flags que agrega campos). Cruce independiente: la clase de la skill coincide con la de esa fuente según sus `0x3804` en 47.448 de 47.627 casts de jugadores. El u32 justo después del varint (sin `b1`) nunca es una skill de clase.
- `unk` coincide con el `unk` del `0x3804` de la misma fuente y skill (el valor propio de cada personaje, ver "Layout de `0x3804`") en 33.508 de 33.519 casts de la comparación.
- **Objetivo y posición: confianza media.** `target` es una entidad ya conocida en 37.109 de 37.522 casts de la comparación; en 18.630 es la propia fuente. `mode` solo vale `0x00` o `0x02`. Con `mode 0x02`, `f2`–`f4` es la posición del **objetivo**, no la de quien lanza: (f2, f3) cae a menos de 0,01 de la última posición `0x371A`–`0x371C` del objetivo (hasta 1 s antes, con objetivo distinto de la fuente) en 776 de 1.464 casts en la comparación, 109 de 191 en la dungeon, 735 de 1.746 en el mini boss y 34 de 121 en la de login, contra 17 de 7.318, 7 de 1.454, 44 de 4.625 y 0 de 189 con la posición de la fuente. Solo se puede medir en el 8 % de los casts `mode 0x02`.
- INTERPRETADO: `f1` parece un ángulo de orientación; `seq`, `b_n` y `v_last` no tienen significado conocido.
- **No trae el vínculo entre dueño e invocación.** Las invocaciones lanzan con su propio id como fuente: 1.296 de 1.296 casts de las 101 invocaciones con dueño conocido, todos con skill de su clase. En esos casts el dueño no aparece en `target`, `unk` ni `v_last`, ni en el cuerpo como varint, u32 o u16, y el cast del dueño con una skill del mismo grupo tampoco trae el id de la invocación.

### Movimiento (investigado el 2026-10-19)

```text
0x371A, 0x371B, 0x371C  varint entity | u8 b0 | [u8 si b0 != 0x04] | f32 x | f32 y | f32 z | ...
0x3600                  u64 LE: reloj en ms, sube 50 por mensaje
0x371D                  sin decodificar
```

- **`0x371A`–`0x371C`, confianza media.** La posición vale por continuidad: entre posiciones seguidas de la misma entidad en la misma conexión (al menos 0,2 s entre sí), la velocidad da p50 385 y p99 1.742 unidades/s en la comparación (1 salto de más de 5.000 en 8.366), p99 1.565 en la dungeon (0 en 1.961) y 1.663 en el mini boss (1 en 3.690). Con la cabecera de otro largo el p99 pasa a 203.389 o 444.807. El primer movimiento de una entidad, hasta 3 s después de su `0x3641`, coincide bit a bit con la posición del spawn en 28 de 112 (comparación), 24 de 42 (dungeon) y 71 de 85 (mini boss). Lo que no se entiende es la cabecera: los cuerpos miden 18 bytes después del varint con `b0` `0x01`, `0x04` o `0x05` por igual, así que con `0x04` no es una cabecera más corta sino un campo corrido.
- Los ids son por conexión: no se pueden comparar posiciones de conexiones distintas.
- **`0x3600`** no es una entidad ni un movimiento: sus 8 bytes son un u64 LE que sube 50 por mensaje (p10 49, p50 50, p90 51, nunca baja), llega cada 50 ms y su diferencia con la hora de la captura es constante (en la comparación, entre −1.160 y −1.133 ms). INTERPRETADO: el reloj del servidor.
- **`0x371D`** es el 77 % de los mensajes de movimiento (35.649 de 46.203 en la comparación) y no está decodificado, así que la última posición conocida sale de uno de cada 4,4 mensajes.

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
5. Dueño de invocaciones sin nombre (investigado el 2026-10-19): el `0x3641` de un bloque trae el id del dueño en la cola, y el respaldo por tiempo (el `0x3804` con `flags 0x00` del dueño 39–112 ms antes) no lo contradice nunca (ver "Dueño de la invocación"). El `0x3802` y la posición no sirven. Mientras no se implemente, la verificación cuenta el daño de esas invocaciones, pero no entra al DPS de nadie. Falta:
   - Implementarlo en el motor (T020 de la 002): leer el dueño de la cola solo con la guarda de forma; exigir que sea un jugador conocido o resolverlo como canónico, porque en otras formas el mismo campo apunta a bosses o a otras invocaciones; aceptar un dueño que todavía no se vio (el primer spawn del mini boss llega 153 ms antes que su dueño); usar el respaldo por tiempo cuando la cola no tiene la forma; y reemplazar el dueño cuando un id reusado trae un `0x3641` nuevo. Hoy el motor arrastra el anterior: en la comparación, la invocación 56247 queda con 1628 (clase 14, de la conexión 1) en vez de 4069 (13 golpes, 31.715), y la 42670 con 12962 en vez de 12426 (7.194).
   - Confirmarlo con una pelea grabada con dos jugadores de la misma clase en la misma conexión, donde uno solo invoque sin nombre de dueño (por ejemplo dos de clase 15): ese caso no está en ninguna captura. Los 17 casos discriminantes con dueño conocido son de invocaciones con nombre, y en la comparación cada conexión tiene un solo jugador de clase 15. Los 2 a 4 candidatos que ve `TryAdoptSummon` ahí son el jugador de clase 15 de la conexión más los de las conexiones anteriores (12962 en la 1, 12426 en la 21 y 6381 en la 46; INTERPRETADO: el mismo personaje, con un id nuevo en cada conexión) y la entidad 63831, que el motor trata como jugador solo por sus skills: 4 golpes con 15280142 y 15280143, que solo usan las invocaciones, sin `0x3641`, sin `0x3802` y sin posición (INTERPRETADO: es una invocación).
   - Resolver lo no concluyente: `07 02 01`, la cadena `0x17` → `0x1F` npc 2920346 (47 golpes, 251.926) y la clase 16 del mini boss, que no tiene dueño conocido.
6. Nombres de NPC: la tabla ID → nombre está en `data/npcs.json` (vacía en el repo) y `hysteria-extract` saca los nombres de los archivos del juego a la PC de cada uno.
