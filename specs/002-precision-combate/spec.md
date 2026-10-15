# Feature Specification: Precisión del combate

**Feature Branch**: `002-precision-combate`

**Created**: 2026-10-15

**Status**: Draft

**Input**: Features aprobadas por Mati en la lista de [features](../features.md): MT-01, MT-02, MT-03, MT-04, MT-05, MT-06, MT-07, MT-10, MT-11 y PT-01 (ver [roadmap](../roadmap.md)). Parte de la base que deja el PR #1 (protocolo real, notas en `specs/001-dps-meter-mvp/protocol-notes.md`).

## Contexto

Con el PR #1 el daño total del jugador local ya coincide con Combat Analysis del juego (2.040.321 contra 2,04M en el boss Divine Auldor). Lo que falta para que los números sean completos y confiables es el tipo de golpe (crítico y demás), la curación, el daño recibido, el daño de todas las invocaciones, el ~8 % que todavía le falta al jugador local en algunas peleas y los nombres de toda la party. Esta feature cierra esos huecos; no agrega pantallas nuevas (eso es la 004), solo hace que los datos que ya muestra el overlay sean correctos y estén completos.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tipos de golpe como en Combat Analysis (Priority: P1)

Al terminar una pelea veo, para mí y para cada jugador de la party, cuántos golpes fueron críticos, perfectos, dobles, frontales, por la espalda y multi-golpe, con los mismos números que muestra Combat Analysis para mi personaje.

**Why this priority**: es el dato que más se pide a un meter y lo necesitan el desglose por skill (005) y la puntuación de rotación (006).

**Independent Test**: replay de una pelea grabada con su captura de pantalla de Combat Analysis; comparar los porcentajes CRI, PERF, DOUB, FRNT, BACK y MULT del jugador local.

**Acceptance Scenarios**:

1. **Given** una pelea grabada con su Combat Analysis, **When** la reproduzco, **Then** el % de críticos del jugador local difiere en menos de 1 punto porcentual del que muestra el juego.
2. **Given** la misma pelea, **When** miro PERF, DOUB, FRNT, BACK y MULT, **Then** cada uno difiere en menos de 1 punto porcentual, o el modificador figura como "sin identificar" si todavía no se encontró su bit.
3. **Given** un golpe multi-golpe, **When** se registra, **Then** cuenta como un golpe con su daño total, igual que el juego.

---

### User Story 2 - Daño completo del jugador local (Priority: P1)

El daño total de mi personaje coincide con Combat Analysis en todas las peleas, no solo en algunas.

**Why this priority**: hoy falta ~8 % en algunas peleas; si el número propio no es exacto, nada de lo que se construya encima es confiable.

**Independent Test**: 10 peleas grabadas con Combat Analysis (solo y en party); el total propio difiere en menos de 1 % en todas.

**Acceptance Scenarios**:

1. **Given** las 10 peleas de referencia, **When** las reproduzco, **Then** el total propio difiere menos de 1 % en cada una (SC-001 de la 001, ahora en todas las peleas).
2. **Given** un golpe que pasa la vida restante del objetivo, **When** se registra, **Then** el exceso se cuenta aparte como overkill y no infla el total contra ese objetivo.
3. **Given** una pelea con DoTs, **When** miro el desglose, **Then** el daño periódico de cada skill aparece separado del directo.

---

### User Story 3 - Toda la party con nombre (Priority: P1)

Cada jugador de la party aparece con su nombre real (y su clase cuando el protocolo la trae), aunque el nombre no llegue en los paquetes de muerte y aunque el jugador ya estuviera en la zona cuando arrancó la captura.

**Why this priority**: sin nombres el ranking de la party (US2 de la 001) muestra IDs, y la comparación entre jugadores no sirve.

**Independent Test**: replay de la dungeon en party de 5; las 5 filas tienen nombre.

**Acceptance Scenarios**:

1. **Given** una captura que empieza con la party ya formada, **When** los jugadores pegan, **Then** cada fila muestra el nombre real en algún momento de la pelea.
2. **Given** un jugador cuyo nombre todavía no llegó, **When** pega, **Then** aparece con un identificador provisional y se renombra solo cuando llega el nombre (ya pedido en la 001, se mantiene).
3. **Given** el jugador local, **When** arranca el meter con el juego ya logueado, **Then** se identifica como local y muestra su nombre.

---

### User Story 4 - Invocaciones atribuidas al dueño (Priority: P2)

El daño de todas las invocaciones se suma a su dueño, incluidas las que hoy aparecen sin nombre ni ID de dueño. Puedo verlo sumado o separado.

**Why this priority**: algunas clases dependen de invocaciones; sin esto su DPS sale bajo y el ranking es injusto.

**Independent Test**: replay de una pelea con invocadores; ninguna fila de la tabla es una invocación suelta y el daño de invocación figura en el desglose del dueño.

**Acceptance Scenarios**:

1. **Given** una invocación sin nombre de dueño en el spawn, **When** pega, **Then** su daño se atribuye al dueño correcto.
2. **Given** la opción "invocaciones separadas", **When** está activa, **Then** el daño de invocaciones aparece como una línea propia dentro del jugador.
3. **Given** que Combat Analysis no cuenta invocaciones, **When** comparo contra el juego, **Then** la comparación usa el total sin invocaciones.

---

### User Story 5 - Curación y daño recibido (Priority: P2)

Veo cuánto curó cada jugador (HPS) y cuánto daño recibió cada uno (DTPS), con desglose por skill.

**Why this priority**: es la base de las vistas de healer y tank (007) y de la pantalla de muertes (005).

**Independent Test**: replay de la dungeon; la curación del jugador local coincide con la que muestra el juego (si el juego la muestra) o con la suma de subidas de HP observadas.

**Acceptance Scenarios**:

1. **Given** una pelea con un healer en la party, **When** termina, **Then** veo su curación total y por skill.
2. **Given** un jugador que recibe golpes del boss, **When** termina la pelea, **Then** veo su daño recibido total y por skill del boss.
3. **Given** una curación sobre un objetivo con la vida llena, **When** se registra, **Then** se cuenta como sobrecuración si el protocolo permite saber la vida del objetivo; si no, se informa solo la curación bruta.

---

### User Story 6 - Métricas de tiempo y auto-verificación (Priority: P3)

Además del DPS real veo el DPS activo (descontando el tiempo sin objetivo o muerto), y el meter me avisa si el daño total contra un boss no cierra con la vida que perdió.

**Why this priority**: es barato con los datos que ya hay y protege contra regresiones después de un parche.

**Independent Test**: replay de la dungeon; la auto-verificación da "cierra" para el boss Divine Auldor y un caso sintético con un golpe faltante da "no cierra".

**Acceptance Scenarios**:

1. **Given** un jugador que estuvo muerto 20 s de una pelea de 2 min, **When** miro su DPS activo, **Then** se calcula sobre 100 s.
2. **Given** un boss con HP conocido, **When** termina la pelea, **Then** el encuentro guarda si la suma del daño cierra con la vida perdida (tolerancia 1 %), y el diagnóstico muestra la diferencia.

### Edge Cases

- Un modificador de golpe cuyo bit no se encuentra: el meter lo muestra como "sin identificar" en vez de 0 %.
- Un golpe que es crítico y por la espalda a la vez: cuenta en los dos.
- El jugador local cambia de personaje sin cerrar el juego: el meter detecta el nuevo local y su nombre.
- Invocación cuyo dueño se desconecta antes de que llegue su daño: el daño queda en el dueño ya resuelto.
- Curación de un jugador fuera de la party (otro grupo cerca): no aparece en el ranking de la party.
- Parche que cambia el layout de `0x3804`: los modificadores pasan a "sin identificar" y el total sigue funcionando (constitución V).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema MUST clasificar cada golpe con los modificadores que distingue Combat Analysis (crítico, perfecto, doble, frontal, por la espalda, multi-golpe) a partir de los flags de `0x3804`, sin leer nada fuera del tráfico (constitución I).
- **FR-002**: El sistema MUST distinguir "modificador en 0 %" de "modificador sin identificar".
- **FR-003**: El sistema MUST atribuir al jugador local todo su daño, de modo que el total coincida con Combat Analysis con menos de 1 % de error en las peleas de referencia (incluye investigar `0x3806`).
- **FR-004**: El sistema MUST registrar el overkill de cada golpe por separado.
- **FR-005**: El sistema MUST separar daño directo y periódico (DoT) por skill.
- **FR-006**: El sistema MUST identificar al jugador local sin depender de que la captura empiece en el login.
- **FR-007**: El sistema MUST obtener el nombre de cada jugador de la party por al menos una fuente además de `0x3645` (por ejemplo, el nombre de dueño de sus invocaciones, paquetes de party o legión).
- **FR-008**: El sistema MUST atribuir el daño de toda invocación a su dueño y permitir verlo sumado o separado.
- **FR-009**: El sistema MUST calcular la curación por jugador y por skill (`0x3805` tipos `0x09` y `0x0B`, y el paquete de curación directa si existe), validada contra el juego.
- **FR-010**: El sistema MUST calcular el daño recibido por jugador y por skill de origen.
- **FR-011**: El sistema MUST calcular el DPS activo además del DPS real.
- **FR-012**: El sistema MUST verificar, para cada objetivo con HP conocido, si el daño registrado cierra con la vida perdida, y guardar el resultado en el encuentro.
- **FR-013**: Todo layout nuevo o corregido MUST documentarse en `protocol-notes.md` y tener un test con bytes reales (constitución IV).

### Key Entities

- **Golpe**: origen, objetivo, skill, monto, overkill, modificadores (con estado "sin identificar" por modificador), directo o periódico.
- **Curación**: origen, objetivo, skill, monto bruto, sobrecuración opcional.
- **Jugador**: ID canónico, nombre, clase, si es local, fuente del nombre.
- **Invocación**: ID, dueño resuelto, cómo se resolvió (spawn, nombre, heurística).
- **Verificación de objetivo**: HP inicial, HP final, daño registrado, diferencia, cierra o no.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: En 10 peleas de referencia, el total propio difiere menos de 1 % de Combat Analysis en todas.
- **SC-002**: En las mismas peleas, el % de críticos propio difiere menos de 1 punto porcentual; los demás modificadores identificados, también.
- **SC-003**: En la captura de la dungeon en party de 5, las 5 filas tienen nombre real al terminar el primer boss.
- **SC-004**: Ninguna invocación aparece como fila propia en el ranking de las capturas de referencia.
- **SC-005**: La auto-verificación da "cierra" en todos los bosses de las capturas de referencia.

## Assumptions

- Mati sigue grabando capturas con Combat Analysis abierto al final de cada pelea (vía Remote Control en su PC); las capturas quedan fuera del repo.
- Todo se deriva de capturas propias; no se usan datos de Abyss DPS Meter (cerrado y sin licencia).
- Si la clase de un jugador no viene en el protocolo, la clase queda vacía; esta feature no la infiere por skills (eso es PT-05, en la 007).
- Fuera de alcance: buffs y debuffs (007), pantallas nuevas (004 y 005), nombres de skills y NPCs (005).
