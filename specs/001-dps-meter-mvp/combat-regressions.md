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

### Pausas de fase de un boss (2026-10-29)

La regla anterior volvió a partir peleas de boss. En la base del usuario hay tres casos con una pausa de 8,3 a 8,6 s contra los 8 s de inactividad, todos en Ferocious Horn Den: Ferocious Horn Nuakum (npc 2300910) el 2026-10-19 (8,6 s) y el 2026-10-29 (8,3 s), y Watchdog Kwapo (npc 2300908) el 2026-10-19 (8,4 s). La simulación de abajo encuentra dos más, con huecos más largos. En el del 2026-10-29, el primer tramo dura 119,8 s y su verificación termina con el boss en 2.322.588 de vida. El segundo tramo dura 9,3 s, arranca con 2.322.588 y se lleva el kill. Como la vida no cambia entre los dos tramos, es la misma pelea. En los últimos 8 s de esa pausa ningún NPC que no fuera el boss le pegó a alguien del filtro: con la regla de entonces, eso la habría sostenido. Sin captura no se sabe si el boss pegó.

No hay una regla medida que separe esto de Tarka: Destroyer Tarka también está marcado como boss y su vida tampoco se reinicia en la pausa de 9,7 s. Un umbral de unos 9 s, o mirar si el boss pega en la pausa (en la de Nuakum no se sabe), podrían separarlos: los datos no los descartan, pero serían ajustes a estos casos y no reglas medidas. Por decisión del usuario, una pausa de fase no corta la pelea de un boss vivo y sin resetear, aunque Tarka deje de coincidir con Combat Analysis:

- Si el objetivo principal es un boss del catálogo vivo y sin resetear, la pelea cierra tras `BossPauseTimeout` (30 s) sin actividad, en vez de la inactividad configurada. Si la configurada es mayor, vale esa. El catálogo es `data/npcs.json` más la copia del usuario (`Juego/npcs.json`). El del repo marca solo a Divine Auldor y Destroyer Tarka; Nuakum y los demás vienen de la copia del usuario. Sin ella, la regla solo vale para esos dos.
- Reset por vida: la vida que informa el servidor (`0x8D00`, o un spawn con vida) vuelve al máximo después de verse por debajo en esta pelea. La lectura llena antes de verlo herido no cuenta, porque en el pull puede llegar después del primer golpe. Si el boss vuelve a bajar, la pausa de boss vuelve a valer. Ultimate Berk vuelve con la misma entidad (30544) y la vida llena: el intento que arranca 22,2 s después del wipe queda aparte. El tramo que arranca 11,7 s después de otro intento no es un intento: es una cola de 7,1 s de un solo jugador antes del enrage. Queda unido si ese jugador le pegó al boss o a un add que ya estaba en la pelea; el tramo no tiene verificación que lo diga.
- Reset por reaparición: aparece otra entidad del mismo NPC sin herir (un spawn sin vida o con la vida llena). Atiel (npc 2343601 y 2320601) vuelve como otra entidad en cada intento, y de la vieja no llega ni la muerte ni una lectura llena. Sin esta regla, la pelea del wipe sigue abierta hasta el primer golpe del reintento (en tres reintentos del 2026-10-20 y 21, 24,1, 29,5 y 24,7 s después del wipe). Si ese golpe va a la entidad nueva, el reintento queda aparte igual, porque no es parte de la pelea (ver abajo). Se une si va a algo que ya era parte de ella, o si un add de ella le pega a la party en el hueco, y el daño recibido y las muertes del hueco quedan en la pelea del wipe. Con esta regla, la pelea cierra con la inactividad configurada. Una entidad del mismo NPC que llega herida es una copia y no corta la pausa. Tampoco la corta el spawn de otro NPC.
- Solo lo que ya es parte de la pelea la sigue. Pasada la inactividad configurada, solo cuentan los NPCs que recibieron daño del filtro en ella. Un jugador nunca: el "daño" a sí mismo o a un compañero (costos de vida) también queda registrado en la pelea viva, pero no la sigue ni la estira, y en la pausa se descarta como antes. Tampoco un monstruo sin spawn capturado (sin npcId, por ejemplo uno que apareció antes de empezar a capturar): para él vale la regla de antes. Pegarle a otro NPC (boss, add o trash) la cierra y abre otra, como la regla de antes. Que otro NPC le pegue a la party no la cierra ni la sostiene. En las pausas medidas, el grupo volvió a pegarle al boss: en los segundos tramos de Nuakum (8,6 y 8,3 s), Kwapo y Duanka, el boss es el único objetivo de la verificación. En el hueco de 13,1 s de Nuakum, el segundo tramo no tiene verificación, pero cruza el 25 % en el segundo 0 (INTERPRETADO: el grupo volvió al boss). Esto evita dos casos:
  - El 2026-10-29, Silver Blade Rotan (2300310, boss en la copia del usuario) quedó vivo 13,8 s antes del pull de Kromede's Desire. Si no se reseteó, ese pull se sumaba a su pelea, o lo hacían sus entidades (npc 2920343 y 2920347, que no están en el catálogo) al pegarle a la party. Sin captura no se sabe.
  - La copia del usuario marca boss a 973 de sus 1.006 NPCs, también a entidades de mecánica como las de Atiel (npc 2920445 y 2920446), que le pegan a la party en sus pausas sin que nadie les pegue.
- Lo que solo va a la verificación (lecturas de vida, muertes y golpes de jugadores fuera del filtro o de invocaciones sin dueño) sigue la misma regla. Pasada la inactividad configurada, el de un NPC que no es parte de la pelea no entra, y su cuenta queda congelada en la última lectura de antes, porque si la pelea sigue, lo que venga después no se vería entero.
  - La muerte del objetivo principal es la excepción: cierra la pelea con ese mismo evento y su cuenta registra la muerte, aunque llegue en la pausa (el kill lo hacen jugadores fuera del filtro, o una mecánica). El registro ya lo marcó muerto, y un boss muerto no sostiene la pausa: sin la excepción, la pelea se guardaba como kill con la verificación del boss sin la muerte.
  - La cuenta congelada no vuelve: si después el filtro le pega a ese NPC y pasa a ser parte de la pelea, su verificación queda en la lectura de antes, sin lo que siguió (incompleta, no inconsistente).
- En el peor caso vale la regla de antes: después de un reset, o con algo que no es parte de la pelea, la pelea corta con la inactividad configurada. La excepción es el golpe que recibe la party de algo que no es parte de la pelea: antes cerraba la pelea pasada esa inactividad, y ahora espera la pausa de boss. Ese daño recibido, y las muertes que cause, quedan en la pelea vieja hasta que el grupo le pegue a otra cosa. En las pausas de Atiel es lo correcto, porque sus entidades de mecánica son de su pelea; en un pull de otro boss, como el de Kromede's Desire, quedaría en la pelea anterior. Es una decisión de esta implementación, sin captura que la mida.
- Si en la vuelta de la pausa un mismo AoE le pega a la vez a un add nuevo y al boss, decide el orden de los golpes en el paquete: si llega primero el del add, la pelea se parte como antes. No aparece en las pausas medidas, donde el boss es el único objetivo del segundo tramo.
- Los NPCs que no están marcados como boss en el catálogo, o que no están en él, siguen con la inactividad configurada cuando son el objetivo principal; uno marcado boss (la copia del usuario marca también adds y entidades de mecánica) espera la pausa de boss. `ReferenceFightsTests` y el `replay` del Recorder arman el motor sin catálogo, así que no aplican la pausa de boss. Tarka sigue dando 648.468 ahí. INTERPRETADO: en el meter, con la lista de NPCs, sus dos tramos quedan juntos si el primer golpe después de la pausa fue a Tarka o a algo ya golpeado en esos 3 s; ningún test ni replay lo reproduce con el catálogo.
- Simulación sobre la base del usuario (1.204 peleas, solo lectura, sin captura). Quedan unidas:
  - las pausas de Nuakum de 8,6 y 8,3 s;
  - un hueco de Nuakum de 13,1 s con la vida continua;
  - la pausa de Kwapo de 8,4 s;
  - la de Duanka de 10,1 s, con la misma entidad.

  Los wipes de Nuakum que vuelven con la misma entidad y la vida llena quedan aparte. En los wipes cuya pelea no tiene verificación del boss, solo una captura puede decir si llegó la lectura llena.
- `BossPauseTests` cubre:
  - la pausa de 8,3 y de 29 s, con y sin Tick, y el cierre a los 30 s;
  - el reset por lectura y por spawn, la lectura llena del pull y el boss herido otra vez;
  - el boss que vuelve como otra entidad (con y sin vida en el spawn), su copia herida y el spawn de otro boss;
  - pasada la inactividad, pegarle a otro boss o a un add nuevo, el pull que hace otro boss, y un NPC fuera de la pelea (marcado boss o no) que le pega a la party;
  - la verificación de un NPC fuera de la pelea con un golpe (de un jugador fuera del filtro o de una invocación sin dueño), una lectura o su muerte en la pausa, y lo que le llega después, muerte incluida;
  - un boss y un add que ya son parte de la pelea, con un golpe hecho o recibido;
  - en la pausa, un golpe que no puede abrir una pelea (al cadáver de un NPC o a uno mismo) y el daño entre jugadores, que no la siguen;
  - la muerte del objetivo principal en la pausa, que su verificación registra;
  - el trash, el monstruo sin catálogo, el add como objetivo principal y una inactividad configurada mayor.

Queda abierto otro problema de la misma sesión, que no es de cortes: Necromancer Duanka (npc 2300909) perdió 6,6 M de vida en 49 s (de 7.623.702 a 998.657) sin un solo golpe registrado de nadie. En la run siguiente quedó un tramo de 2,8 s sin kill, y en la tercera, dos tramos (21,2 y 6,9 s) con 1,8 M perdidos en los 10 s del medio y 44.250 que faltan dentro del primero (la verificación no cierra). En las tres, el jugador local no aparece en ningún tramo de Duanka, y en las otras peleas de la run sí. El 2026-10-19 salió entero las dos veces (114 s, unos 9 M). INTERPRETADO: se pierden golpes antes del motor (decoder o captura), no es un corte; hace falta una grabación de esa pelea.

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
