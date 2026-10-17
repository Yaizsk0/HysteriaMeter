# Muertes de invocaciones atribuidas al dueño

Fecha: 2026-10-17. Corrección acotada al conteo de víctimas; no cambia opcodes, decoders, fórmulas de DPS ni captura pasiva.

## Evidencia

Inspección offline de una captura local del 2026-10-14, con el Recorder ya compilado: 52 mensajes `0x8D04` con 52 IDs de víctima distintos. Dieciocho víctimas tenían un spawn de NPC con dueño. No hubo IDs de víctima repetidos en ese conjunto, por lo que esa evidencia no apunta a retransmisiones duplicadas.

Ejemplo sin nombres personales: a las 21:48:35.670 aparece la entidad 22627, tipo `0x5F`, NPC 2920650 y un dueño identificado. A las 21:48:43.818 su HP llega a cero; a las 21:48:43.869 llega `0x8D04` para la entidad 22627, con skill y killer en cero. Hay otros casos de invocaciones con skill no cero: filtrar únicamente mensajes de skill cero tampoco separaría jugador e invocación.

El decoder emite correctamente la muerte de la entidad física. CombatEngine convertía esa víctima al dueño con Canonical antes de contar las muertes, igual que al atribuir daño. Así, la desaparición o muerte de un NPC con dueño terminaba sumando una muerte al jugador. El registro de cadáveres también usaba el ID canónico y omitía el cuerpo de la invocación.

## Corrección y regresiones

Se descarta el conteo de muerte de jugador si la víctima física es un NPC conocido. El daño sigue atribuido al dueño y se conserva la semántica de aliases de combate que no tienen clasificación NPC. EntityRegistry marca el cadáver por ID físico, coherente con el spawn que lo elimina al reaparecer.

Las pruebas sintéticas cubren dueño por ID, por nombre y por adopción de clase; verifican encuentro, sesión, run, daño conservado, ausencia de recap/log falsos y una muerte real del jugador posterior. También cubren el alias sin NPC y el ciclo muerte/reaparición del cuerpo de la invocación. No se agregó deduplicación temporal ni inferencia de resurrección.

## Límites

El caso reportado del 2026-10-17 tiene contadores inflados guardados, pero no hay PCAP ni eventos crudos de esa pelea para reconstruir cada muerte. La captura anterior prueba el mecanismo de atribución incorrecta; no permite afirmar cuántas muertes del caso actual provienen de él ni descartar causas adicionales. La corrección afecta al procesamiento nuevo y no reescribe el historial guardado.
