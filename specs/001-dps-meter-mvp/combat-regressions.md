# Regresiones de continuidad y atribución de combate

## Pausas de daño saliente

Una pelea podía cerrarse a los ocho segundos sin daño saliente aunque los NPC siguieran atacando al grupo. Al volver a atacar se abría otro encuentro, dividiendo el daño y cambiando la ventana del DPS.

El motor mantiene ahora un reloj monótono de actividad de combate separado del reloj del daño saliente. Los golpes recibidos válidos actualizan el primero, después de comprobar el alcance de jugadores. Curación, vida y presencia en party no evitan el cierre. Un golpe recibido después del vencimiento tampoco revive ni abre un encuentro. Las fórmulas del DPS y las ventanas del objetivo conservan su comportamiento.

`IncomingActivityTests` cubre continuidad con y sin Tick, daño al local y a la party, vencimiento, eventos fuera del alcance, señales sin daño, eventos fuera de orden, muerte del objetivo y separación desactivada. Los casos nuevos reprodujeron nueve fallos antes del cambio; los quince pasan con el arreglo.

El replay local del mismo PCAP pasa de quince a trece encuentros: los tres tramos de una pelea de jefe permanecen unidos. Su daño total se conserva. El catálogo de NPC está vacío en esta comprobación; la corrección no depende de identificar al jefe por una tabla externa.

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
