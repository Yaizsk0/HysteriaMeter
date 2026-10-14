# Notas del protocolo (cliente global)

Primera captura: 2026-10-14, ~2 min de combate contra monstruos de campo, Wi-Fi. Conexión del juego: puerto de servidor 13328. Resultado: 380 KB del servidor → 16.824 frames (17.666 con el contenido de los bundles), **0 errores** de framing.

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
| `0x3641` | Aparición de NPC: entidad, ID de NPC, posición, HP actual y máximo | Media | `NpcSpawnDecoder` |
| `0x8D04` | Muerte: víctima, skill, quien mató, nombre y legión | Alta | `KillPacketDecoder` |
| `0x3802` | Lanzamiento de skill (fuente, skill u32, seq, objetivo, posición) | Media | — |
| `0x8D00` | Actualización de recurso: `varint entidad, u8 tipo, …, u32 valor`; tipo 2 en NPCs = HP actual | Alta | — |
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

## Pendiente

1. **Escala del daño.** `value` es proporcional a la caída de HP del objetivo en `0x8D00`, pero con un factor constante distinto por monstruo (≈4,29 en uno, ≈3,92 en otro). Hay que comparar con Combat Analysis (Ctrl+X) para saber cuál de los dos números muestra el juego.
2. Identificar el jugador local (hoy el nombre llega recién con el primer kill vía `0x8D04`).
3. Crítico, golpes por espalda, curación, dueño de invocaciones.
4. Nombres de NPC: el ID de NPC está, falta una tabla ID → nombre.
