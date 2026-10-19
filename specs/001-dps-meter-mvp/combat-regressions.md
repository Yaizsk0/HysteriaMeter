# Regresiones de continuidad y atribución de combate

## Pausas de daño saliente

Una pelea podía cerrarse a los ocho segundos sin daño saliente aunque los NPC siguieran atacando al grupo. Al volver a atacar se abría otro encuentro, dividiendo el daño y cambiando la ventana del DPS.

El motor mantiene ahora un reloj monótono de actividad de combate separado del reloj del daño saliente. Los golpes recibidos válidos de un NPC que no es el objetivo principal actualizan el primero, después de comprobar el alcance de jugadores (ver "Solo los golpes de otro NPC"). Curación, vida y presencia en party no evitan el cierre. Un golpe recibido después del vencimiento tampoco revive ni abre un encuentro. Las fórmulas del DPS y las ventanas del objetivo conservan su comportamiento.

`IncomingActivityTests` cubre continuidad con y sin Tick, daño al local y a la party, vencimiento, eventos fuera del alcance, señales sin daño, eventos fuera de orden, muerte del objetivo y separación desactivada. Los casos nuevos reprodujeron nueve fallos antes del cambio; los quince pasan con el arreglo.

El replay local del mismo PCAP pasa de quince a trece encuentros: los tres tramos de una pelea de jefe permanecen unidos. Su daño total se conserva. El catálogo de NPC está vacío en esta comprobación; la corrección no depende de identificar al jefe por una tabla externa.

### Solo los golpes de otro NPC (2026-10-19)

Con cualquier golpe recibido, el replay del mini boss unía los primeros 3 s contra Destroyer Tarka con la pelea del kill: el local quedaba con 665.522 de daño al objetivo y Combat Analysis muestra 648.468, en 00:27, solo la pelea del kill. En esa pausa de 9,7 s del daño hecho (13:39:30,4 → 13:39:40,1), el único que pega es el propio boss: tres golpes al local. En las dos pausas de la pelea contra 2300371 de la comparación (8,5 y 9,1 s), el boss no le pega a nadie; solo pegan sus entidades (npc 2920343 y 2920347), y su vida no cambia (las tres cuentas cierran con diferencia 0).

Ninguna medida separa los dos casos sin ajustarse a ellos. Un umbral de inactividad tendría medio segundo de margen (9,1 contra 9,7 s). Con la actividad hecha más recibida, el hueco más largo es casi igual (5,9 contra 5,95 s). La vida de los dos bosses sigue sin reiniciarse. Por decisión del usuario, solo los golpes de un NPC que no es el objetivo principal sostienen la pelea; los del objetivo principal siguen contando como daño recibido.

- Es un ajuste a dos casos y no una regla medida: si en la pausa de Tarka le hubiera pegado al local un monstruo de campo, la pelea se habría unido igual, contra Combat Analysis.
- Para la pelea en party, la referencia es otro medidor y no Combat Analysis: sus cifras redondeadas coinciden con los tramos unidos. Hace falta una pelea de jefe en party con pausas de fase, grabada con Combat Analysis (ver "Pendiente" en `references.md` de la 002).
- Con esto, Tarka vuelve a 648.468 y los tests de Combat Analysis pasan con `HYSTERIA_CAPTURES`. La comparación queda igual que con la regla anterior: 13 encuentros y 49 verificaciones, de las que 8 son de los bosses (una sola de 2300371 en vez de tres). La dungeon y la de login no cambian. En el mini boss, las verificaciones pasan de 93 a 94 porque Tarka vuelve a tener dos peleas.
- `IncomingActivityTests` usa un add como fuente del daño recibido y cubre que los golpes del objetivo principal no sostienen la pelea (los tres casos nuevos fallan sin el cambio).

## Invocaciones sin dueño explícito

La inferencia por clase incluía identificadores históricos del mismo personaje y entidades sin identidad de jugador cuya clase se dedujo de un golpe. Eso podía convertir un único hechicero en varios candidatos y descartar el daño de sus invocaciones contra NPC.

La corrección exige evidencia de jugador además de la clase. Cuando varios identificadores comparten exactamente el mismo nombre, solo se resuelve el grupo si hay una única identidad local o de party vigente. Dos jugadores distintos de la misma clase siguen siendo ambiguos, incluso cuando el filtro de pantalla muestra solamente al local. Los dueños explícitos conservan prioridad.

La comparación por habilidades separa la ventana de tiempo del daño perdido. Las habilidades directas coinciden al sumar los tramos; el faltante se concentra en Bittercold Wind, Fire Wall y Cold Storm, incluidos daños periódicos.

## Alcance de la validación

Las pruebas de regresión usan eventos sintéticos identificados como tales. Los PCAP, las capturas de pantalla, los nombres y las tablas individuales de jugadores permanecen fuera del repositorio.

La captura local terminó antes que la pelea completa. El replay comprueba los eventos disponibles; la conciliación del total mostrado por el otro medidor usa sus cifras redondeadas y no sustituye una comparación nueva de extremo a extremo. No se modifican retrospectivamente los encuentros ya guardados.

## Verificación de las dos correcciones

- 356 pruebas pasan: App 196, Capture 6, Combat 84, Protocol 47 y Storage 23.
- `SummonAttributionTests`: cuatro fallos reproducidos antes de producción; los once casos pasan después.
- Replay de 64.387 eventos: recupera 3.290.535 de daño de invocaciones en la sesión. En el fragmento de la última pelea recupera exactamente 481.705; los otros cuatro actores mantienen idéntico daño.
- La revisión independiente no encontró regresiones concretas.

Se detectó además un caso distinto de presencia: algunos primeros golpes de un pull preceden a la actualización de party, después de que venciera su ventana de diez segundos. En el replay quedan 41.686 puntos de invocaciones en esas ventanas; ni siquiera asignarles dueño basta porque el filtro de actores también los excluye. Requiere validar la vigencia de party, separado de estas dos correcciones.
