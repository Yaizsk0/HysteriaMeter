# Contrato: `data/opcodes.json`

```jsonc
{
  "opcodes": {
    "<PacketKind>": ["0x0638", "0x0639", 1592]
  }
}
```

- Claves: nombres de `PacketKind` sin distinguir mayúsculas (`Bundle`, `LocalPlayer`, `EntitySpawn`, `EntityInfo`, `Damage`, `Heal`, `Death`). Una clave desconocida es un error de carga.
- Valores: opcodes en hex (`"0x...."`) o decimal. Varios por tipo para que convivan versiones.
- Un mismo opcode no puede pertenecer a dos tipos.
- Se permiten comentarios `//` y comas finales.
- El opcode se lee de los primeros `ProtocolOptions.OpcodeSize` bytes del frame (2, little-endian: los bytes `04 38` son `0x3804`).

## Formato de frame (confirmado)

```text
[varint L][opcode u16 LE][cuerpo]
bytes después del prefijo = L - ProtocolOptions.LengthBias (4)
```

## Formato de bundle (confirmado, opcode 0xFFFF)

```text
cuerpo = [u32 LE largo descomprimido][bloque LZ4]
descomprimido = frames concatenados con el mismo formato
```
