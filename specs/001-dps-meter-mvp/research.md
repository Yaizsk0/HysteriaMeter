# Research: DPS meter para AION 2

Fecha: 2026-10-14. Fuentes públicas; no se leyó código fuente de terceros, solo README y artículos.

## Decisión: captura pasiva de red con Npcap

- **Rationale**: todos los meters conocidos lo hacen así. Abyss DPS Meter ([abysslogs.com](https://abysslogs.com/)) "lee paquetes pasivamente desde la interfaz de red"; A2Tools ([a2tools.app](https://a2tools.app/), GPL-3.0) exige Npcap en modo WinPcap; Aion2DPSMeter ([domel1998](https://github.com/domel1998/Aion2DPSMeter), C#/WPF, GPL-3.0) y aion2t ([dev.to](https://dev.to/aionon/reverse-engineering-an-mmo-aion-2s-network-protocol-to-build-a-real-time-dps-meter-rust-tauri-3157), Rust/Tauri) también.
- **Alternativas descartadas**: lectura de memoria (riesgo alto con el anti-cheat y se rompe en cada parche); OCR de la ventana de combate (impreciso y solo muestra datos propios); log de combate en archivo (AION 2 no expone uno utilizable para la party).

## Lo que se sabe del protocolo (a confirmar)

- Stream de combate sin cifrar.
- Bundles comprimidos con LZ4.
- Framing con prefijo de largo varint; campos también varint.
- Opcodes que cambian con parches → `opcodes.json` con varios valores por tipo.
- Un registro `06 38` identifica al jugador local.
- Modelo de entidades complejo: ID dueño estable + IDs de combate transitorios que cambian por pull/zona; nombres que llegan tarde → canonicalización.

**Pendiente**: tamaño y endianness del opcode, si el largo incluye el prefijo, formato exacto del bundle y layout de cada paquete. Todo esto es configurable (`ProtocolOptions`) o está aislado en un decoder.

## Riesgo frente a NCSOFT

- No hay postura oficial sobre meters. La Operation Policy de AION 2 sanciona software que dé ventaja injusta o interactúe con el servicio de forma no prevista, y es dura con la manipulación de paquetes ([aion2guide.org](https://aion2guide.org/guides/aion-2-damage-meter/)).
- No hay reportes públicos de baneos por meters; las sanciones y demandas fueron contra macros/bots ([Inven Global](https://www.invenglobal.com/articles/20119/ncsoft-moves-forward-with-second-round-of-legal-action-against-aion-2-illegal-program-users)).
- **Conclusión**: riesgo bajo, no nulo. Mitigación: principio I de la constitución.

## Stack

| Elección | Alternativas | Por qué |
|---|---|---|
| .NET 10 + C# | Rust/Tauri, Java | Stack del autor; `Span<T>` para parseo sin allocations |
| SharpPcap + PacketDotNet | P/Invoke directo a wpcap | Wrapper maduro, lee y escribe `.pcap` |
| K4os.Compression.LZ4 | LZ4 nativo | 100 % managed, rápido |
| WPF | Avalonia, WinUI 3 | El juego y Npcap son solo Windows; WPF hace transparencia/click-through con poco código |
| SQLite + Dapper | EF Core, LiteDB | Liviano, sin migraciones pesadas para 2 tablas |

## Licencia

El conocimiento publicado del protocolo (opcodes, tablas de skills) está en proyectos GPL-3.0. Usarlo obliga a GPL-3.0 si se distribuye. Decisión pendiente del dueño del repo; por ahora el código es propio y no incluye datos de terceros.
