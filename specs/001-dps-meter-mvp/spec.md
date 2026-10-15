# Feature Specification: DPS meter de escritorio para AION 2 (MVP)

**Feature Branch**: `001-dps-meter-mvp`

**Created**: 2026-10-14

**Status**: In progress (esqueleto implementado; falta el protocolo real)

**Input**: User description: "Tener mi propio DPS meter de escritorio para AION 2, inspirado en Abyss DPS Meter, con la tecnología más adecuada, para poder hacerle las mejoras que considere necesarias."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ver mi DPS en vivo (Priority: P1)

Mientras peleo, una ventana chica encima del juego muestra mi daño por segundo, el daño total, el tiempo de la pelea y el porcentaje de críticos, actualizándose varias veces por segundo.

**Why this priority**: es el valor central del meter; sin esto no hay producto.

**Independent Test**: reproducir una captura `.pcap` de una pelea en modo Replay y comparar el total contra la ventana oficial Combat Analysis (Ctrl+X) de esa misma pelea.

**Acceptance Scenarios**:

1. **Given** el juego abierto y el meter corriendo, **When** golpeo a un monstruo, **Then** en menos de 1 s aparece mi fila con DPS y daño total.
2. **Given** una pelea terminada, **When** comparo con Combat Analysis, **Then** el daño total difiere menos de 1 %.
3. **Given** ningún golpe durante 8 s, **When** vuelvo a pegar, **Then** empieza un encuentro nuevo y el anterior queda guardado.

---

### User Story 2 - Ranking de la party (Priority: P2)

Veo a todos los jugadores que pegan al mismo objetivo, ordenados por daño, con su DPS, porcentaje del total y nombre; mi fila se distingue.

**Why this priority**: es lo que agrega valor sobre el meter oficial, que solo muestra al propio personaje.

**Independent Test**: replay de una captura de party y verificar el orden y los porcentajes.

**Acceptance Scenarios**:

1. **Given** una party de 4 peleando a un boss, **When** miro el overlay, **Then** veo 4 filas ordenadas por daño y los porcentajes suman 100 %.
2. **Given** un jugador con invocación, **When** la invocación pega, **Then** el daño se suma al dueño.
3. **Given** que el nombre de un jugador todavía no llegó, **When** pega, **Then** aparece con un identificador provisional y se renombra cuando llega el nombre.

---

### User Story 3 - Overlay que no molesta (Priority: P2)

Puedo mover el overlay, bloquearlo para que los clicks lo atraviesen hacia el juego y desbloquearlo con un atajo de teclado.

**Why this priority**: sin esto el overlay estorba al jugar.

**Independent Test**: con el overlay bloqueado, hacer click sobre él y verificar que el click llega a la ventana de abajo; Ctrl+Shift+L lo desbloquea.

**Acceptance Scenarios**:

1. **Given** el overlay desbloqueado, **When** arrastro el encabezado, **Then** la ventana se mueve.
2. **Given** el overlay bloqueado, **When** hago click encima, **Then** el click llega al juego.
3. **Given** el overlay bloqueado, **When** presiono Ctrl+Shift+L, **Then** se desbloquea.

---

### User Story 4 - Historial de peleas (Priority: P3)

Cada encuentro terminado queda guardado localmente para revisarlo y compararlo después.

**Why this priority**: útil para mejorar la rotación, pero no bloquea el uso en vivo.

**Independent Test**: replay de una captura con 3 encuentros y verificar 3 registros en la base local.

**Acceptance Scenarios**:

1. **Given** un encuentro que termina por inactividad o por muerte del boss, **When** se cierra, **Then** queda guardado con duración, objetivo, jugadores y desglose por skill.

---

### User Story 5 - Herramienta de descubrimiento del protocolo (Priority: P1, habilitadora)

Como desarrollador, grabo una sesión de juego a `.pcap` y la inspecciono: qué opcodes aparecen, cuántas veces, y hex dumps de ejemplo, para identificar los paquetes de daño, entidades y nombres.

**Why this priority**: sin conocer los opcodes reales el meter no decodifica nada; es prerrequisito de US1.

**Independent Test**: `hysteria-rec dump captura.pcap` lista el histograma de opcodes y ejemplos en hexa.

**Acceptance Scenarios**:

1. **Given** el juego abierto, **When** corro `hysteria-rec record pelea.pcap --process <exe>`, **Then** se graba solo el tráfico del juego.
2. **Given** una captura, **When** corro `dump --opcode 0xNNNN`, **Then** veo hex dumps de ese opcode, incluyendo los que vienen dentro de bundles LZ4.

### Edge Cases

- Parche del juego que renumera opcodes → el meter sigue capturando, cuenta frames desconocidos y se arregla editando `opcodes.json`.
- Pérdida de paquetes o captura que empieza a mitad de una conexión → el reensamblador saltea el hueco y el framer se resincroniza.
- Cambio de zona/dungeon que reasigna IDs de entidades → la canonicalización evita filas duplicadas.
- Npcap no instalado o sin permisos → mensaje claro en el log, la app no se cuelga.
- VPN o reductores de ping (ExitLag, GearUp) → elegir la placa correcta en la configuración.
- Varias conexiones del juego a la vez (login, mundo, chat) → cada una tiene su propio estado de decodificación.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema MUST capturar el tráfico del juego de forma pasiva, sin enviar ni modificar paquetes ni tocar el proceso del juego.
- **FR-002**: El sistema MUST identificar la conexión del juego por nombre de proceso o, alternativamente, por puertos configurados.
- **FR-003**: El sistema MUST reensamblar los streams TCP (orden, retransmisiones, wraparound, huecos).
- **FR-004**: El sistema MUST separar frames con prefijo de largo varint y expandir bundles comprimidos con LZ4.
- **FR-005**: El sistema MUST mapear opcodes a tipos de paquete desde un archivo de configuración editable sin recompilar.
- **FR-006**: El sistema MUST calcular por jugador: daño total, DPS, % del total, golpes, % de críticos, golpe máximo, curación y desglose por skill.
- **FR-007**: El sistema MUST atribuir el daño de invocaciones y entidades transitorias al jugador dueño.
- **FR-008**: El sistema MUST cerrar un encuentro tras un tiempo configurable sin daño (por defecto 8 s) o cuando muere el objetivo principal.
- **FR-009**: El sistema MUST mostrar un overlay siempre visible, movible, con modo click-through y atajo global para alternarlo.
- **FR-010**: El sistema MUST guardar cada encuentro terminado en una base local.
- **FR-011**: El sistema MUST poder reproducir un `.pcap` en lugar de capturar en vivo, a velocidad real o máxima.
- **FR-012**: El sistema MUST exponer contadores de diagnóstico (opcodes vistos, desconocidos, errores de decodificación, desincronizaciones, segmentos descartados).
- **FR-013**: La herramienta `hysteria-rec` MUST listar placas, grabar a `.pcap` y volcar histogramas y hex dumps por opcode.
- **FR-014**: El layout real de los paquetes de daño, entidades, nombres, curación y muerte MUST confirmarse con capturas propias. Framing, daño, aparición de NPC y muerte confirmados el 2026-10-14 (ver `protocol-notes.md`); falta la escala del daño, curación y jugador local.
- **FR-015**: El sistema MUST identificar el proceso del juego por nombre. Confirmado: `AION2`.

### Key Entities

- **Segmento TCP**: dirección (origen/destino), número de secuencia, flags, payload y timestamp.
- **Frame**: opcode + cuerpo, extraído del stream reensamblado.
- **Evento de combate**: daño, curación, muerte, aparición de entidad, info de entidad (nombre/clase), jugador local.
- **Entidad**: ID crudo del servidor, ID canónico (dueño), nombre, clase, si es NPC.
- **Encuentro**: inicio, fin, objetivo principal, si murió, actores con sus estadísticas y desglose por skill.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: El daño total propio coincide con Combat Analysis del juego con un error menor al 1 % en 10 peleas de prueba.
- **SC-002**: El overlay refleja un golpe en menos de 500 ms desde que llega el paquete.
- **SC-003**: Con 6 jugadores peleando, el meter usa menos de 3 % de CPU y menos de 150 MB de RAM.
- **SC-004**: Tras un parche que solo renumera opcodes, el meter vuelve a funcionar editando únicamente `opcodes.json`.
- **SC-005**: Ninguna captura de prueba hace que el meter se cierre por un paquete malformado.

## Assumptions

- Uso personal en Windows 10/11 con Npcap instalado en modo compatible WinPcap.
- El stream de combate no está cifrado y usa framing varint + bundles LZ4, según lo publicado por los meters de la comunidad (A2Tools, Aion2DPSMeter, aion2t). Se verifica con capturas propias.
- Fuera de alcance del MVP: subir logs a la web, leaderboards, rDPS (atribución de buffs), timers de bosses, multilenguaje.
- El jugador acepta el riesgo residual frente a la Operation Policy de NCSOFT; el principio I de la constitución lo minimiza.
