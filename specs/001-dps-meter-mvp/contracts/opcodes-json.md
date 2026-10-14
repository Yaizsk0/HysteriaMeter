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
- El opcode se lee de los primeros `ProtocolOptions.OpcodeSize` bytes del frame (por defecto 2, big-endian como aparece en un hex dump).

## Formato de frame asumido

```text
[varint largo][opcode][cuerpo]
largo = len(opcode + cuerpo)  (o + prefijo si ProtocolOptions.LengthIncludesPrefix)
```

## Formato de bundle asumido

```text
cuerpo = [varint largo descomprimido][bloque LZ4]
descomprimido = frames concatenados con el mismo formato
```
