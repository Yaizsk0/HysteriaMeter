# Hysteria Meter

DPS meter de escritorio para **AION 2**. Captura pasivamente el tráfico del juego con Npcap, decodifica los paquetes de combate y muestra un overlay con el DPS de la party en vivo.

> **Estado**: decodifica daño real del cliente global (framing, bundles LZ4, daño, aparición de NPC y muertes con nombres). Pendiente: confirmar la escala del daño contra Combat Analysis, críticos, curación y jugador local. Ver [notas del protocolo](specs/001-dps-meter-mvp/protocol-notes.md).

## Principio rector

Solo lectura pasiva: no envía ni modifica paquetes, no lee ni escribe la memoria del juego, no automatiza nada. Ver la [constitución](.specify/memory/constitution.md).

## Arquitectura

```text
Npcap ─► Capture ─► TcpFlowTracker ─► GameStreamDecoder ─► CombatEngine ─► Overlay WPF
          (Live/Replay)  (reensamblado)   (varint + LZ4 +       (entidades,      └► SQLite
                                           opcodes.json)         encuentros)
```

| Proyecto | Responsabilidad |
|---|---|
| `src/HysteriaMeter.Capture` | Npcap, filtro por proceso/puerto, reensamblado TCP, replay de `.pcap` |
| `src/HysteriaMeter.Protocol` | Framing varint, bundles LZ4, `opcodes.json`, decoders → eventos |
| `src/HysteriaMeter.Combat` | Modelo puro: canonicalización de entidades, encuentros, DPS |
| `src/HysteriaMeter.Storage` | Historial en SQLite |
| `src/HysteriaMeter.App` | Overlay WPF (click-through, Ctrl+Shift+L) |
| `tools/HysteriaMeter.Recorder` | `hysteria-rec`: grabar y analizar capturas |

## Uso rápido

Requisitos: Windows 10/11, [Npcap](https://npcap.com/) en modo compatible WinPcap, .NET 10 SDK.

```powershell
dotnet build
dotnet test
dotnet run --project src/HysteriaMeter.App -c Release
```

El overlay arranca en vivo y toma solo el tráfico del proceso `AION2`. Para probarlo sin jugar, reproducí una grabación a velocidad real (o más rápido con `ReplaySpeed`):

```powershell
dotnet run --project src/HysteriaMeter.App -c Release -- --Capture:Mode=Replay --Capture:ReplayPath=C:\ruta\a\captura.pcap --Capture:ReplaySpeed=4
```

Como en Details!, el menú ⋯ → Nueva ventana abre otro overlay: cada uno elige su pestaña (DPS, CURA o RECIB.) y si muestra el HPS junto al daño, y todas se recuerdan con su posición. Cerrar la primera cierra la app. Al soltar una ventana cerca de un borde de la pantalla o de otra ventana del meter, se pega a ese borde (imanes; se apagan en configuración).

En la bandeja del sistema aparece un rombo dorado: clic para mostrar u ocultar el overlay (sirve aunque esté en modo atravesable) y clic derecho para reiniciar la pelea, abrir la carpeta de datos o salir.

Doble clic en el encabezado alterna el modo compacto (solo filas y reloj). El overlay se vuelve opaco al pasar el mouse y, si lo activás, se atenúa cuando no hay combate. El punto del encabezado indica la captura: verde en combate, ámbar sin tráfico del juego, rojo si se cortó. En configuración, "Ver barras de prueba" muestra una pelea inventada para acomodar todo sin combatir.

En el primer arranque aparece una bienvenida con el estado real de Npcap, la captura y los datos locales del juego. Al derrotar un boss, una tarjeta debajo del overlay muestra tu resultado y permite abrir el detalle o guardar la captura de esa pelea, incluso si el overlay estaba pausado. Se cierra a los 12 segundos, con Esc o al empezar otra pelea. La bandeja mantiene los accesos a Historial, Configuración y la carpeta de datos, y muestra los atajos configurados; las novedades se presentan dentro del overlay.

Configuración se abre en una ventana propia con ocho secciones: Apariencia, Columnas, Alertas y sonidos, Atajos, Captura, Datos del juego, Historial y Diagnóstico. Los sliders ajustan filas, cantidad de jugadores (o Todos) y opacidad. Captura permite elegir la placa de red; el cambio se aplica al reiniciar el meter. Datos del juego muestra los archivos locales disponibles. El atajo opcional "Cambiar pestaña" rota DPS, CURA y RECIB. en la ventana principal. La configuración y el historial pueden quedar abiertos al copiar imágenes, mostrar barras de prueba o abrir peleas.

El historial (menú ⋯ → Historial) se puede buscar por boss o por nota y filtrar por derrotados. Cada pelea muestra tu DPS, el número de intento contra ese boss, tu percentil entre tus intentos con el color de Warcraft Logs (P100 dorado = tu mejor, naranja, violeta, azul, verde, gris) y una ★ si es tu récord; al batir un récord aparece un aviso dorado, y si no, al terminar la pelea el aviso dice cuánto mejoraste o empeoraste contra tu intento anterior ("+8% vs tu intento anterior"). Al abrir una pelea guardada se le puede escribir una nota o borrarla. Si la búsqueda deja un solo boss, arriba aparece la línea de tu DPS intento por intento, con el último, el mejor y la kill más rápida; esa pelea lleva la marca "⏱ kill más rápida". Con más de un personaje, récords, intentos, percentiles y el aviso "vs tu intento anterior" se cuentan por personaje, cada pelea dice con cuál fue y arriba aparece un selector para ver uno solo. En configuración se elige cuánto guardar: los bosses derrotados y las peleas con nota no se borran nunca.

Con la vida del boss a la vista, el overlay estima en cuánto muere al ritmo actual de la party ("muere en 0:42"). Cada fila tiene detrás una línea tenue con el DPS de los últimos 30 segundos. En configuración se elige el formato de los números (2.04M, 2.0M o 2.040.321) y cada cuánto se refresca el overlay.

En configuración, EXPORTAR Y COMPARTIR copia el overlay como imagen (para pegar en Discord) y guarda la pelea en CSV o JSON en `%LOCALAPPDATA%\HysteriaMeter\Exportes`. Con "Guardar una imagen al derrotar un boss", cada boss derrotado (marcado como boss en `data/npcs.json` o que aguantó al menos 30 s) deja ahí un .png del overlay con su nombre y la hora (no se guarda si el overlay está en pausa o minimizado). Con "Ocultar los nombres de los demás", el resumen y los archivos dicen "Yo", "Jugador 1", "Jugador 2". Si pegás la URL de un webhook de Discord, el resumen se puede mandar al canal a mano o solo al derrotar un boss. Es lo único que sale de tu PC, y solo si lo configurás.

"Abrir pelea…" carga un JSON guardado así (tuyo o el que te pasó otro de la legión) y lo muestra en el overlay como una pelea del historial, con detalle, skills y gráfico. También se puede arrastrar el archivo al overlay.

La pestaña RECIBIDO muestra el daño que le hicieron los monstruos a cada uno (todavía sin validar contra Combat Analysis). Quien murió lleva una ✝ junto al nombre; en el detalle del jugador se ve qué lo mató y cuánto recibió en los 10 segundos anteriores. El botón "Registro" del detalle abre el golpe a golpe de la pelea, con búsqueda por jugador, skill o modificador.

Cada ventana elige entre PELEA (la actual), RUN (todas las peleas desde que entraste a la dungeon, trash y bosses juntos, con el tiempo de la run en el título: "Run 18:20 · 9 peleas") y SESIÓN (todas las peleas desde el último reinicio, con el DPS sobre el tiempo en combate), y puede contar solo el daño al boss. En configuración se elige tras cuántos segundos sin daño termina una pelea, y si el overlay vuelve solo al vivo cuando arranca otra mientras mirás el historial.

Los datos (preferencias, historial, grabaciones y exportes) van a `%LOCALAPPDATA%\HysteriaMeter`. Con un archivo `portable.txt` junto a `HysteriaMeter.exe`, van a la carpeta `datos` al lado del programa. El historial de versiones anteriores, que estaba junto al .exe, se copia solo la primera vez. En configuración, "Iniciar con Windows" abre el meter al iniciar sesión (una entrada en el registro de tu usuario, sin administrador); si movés la carpeta del programa, la ruta se corrige sola al abrirlo.

En configuración, DIAGNÓSTICO muestra el estado de la captura y cuántos mensajes del juego se leyeron, cuántos se reconocen, los errores, las resincronizaciones por huecos en la conexión, las desincronizaciones (largos de mensaje inválidos que obligaron a descartar lo pendiente) y los paquetes que el meter descartó porque no daba abasto (no son pérdidas de la placa ni de Npcap). Si llega tráfico pero no se reconoce nada, el punto se pone ámbar con un aviso de que el juego pudo haberse actualizado. El tooltip de la vida del boss compara el daño contado con la vida que perdió (auto-verificación). "Reportar un problema" copia la versión y el diagnóstico (sin nombres de personajes ni el webhook) y abre un issue nuevo en GitHub para contar qué pasó.

El detalle del jugador muestra cuánto del tiempo estuvo pegando (ACTIVO) y el TIEMPO MUERTO: cada hueco de más de 2 segundos sin golpes directos (los ticks de DoT no cuentan), desde el primer golpe hasta el último. En el historial, cada pelea contra un boss repetido dice cuánto le faltó a tu DPS para tu mejor intento ("−12% vs mejor"). Para practicar contra el muñeco, PRUEBA DE DPS en configuración cuenta 60, 180 o 300 segundos desde el primer golpe y congela el resultado; Ctrl+Shift+R reinicia para otro intento.

Los atajos globales se cambian en configuración, en ATAJOS: clic en la caja y apretá la combinación nueva (Borrar lo saca). Por defecto: Ctrl+Shift+L bloquea los clics, Ctrl+Shift+R reinicia, Ctrl+Shift+H oculta el overlay, Ctrl+Shift+N lo minimiza a la pastilla, Ctrl+Shift+P lo pausa y Ctrl+Shift+M marca el momento de la pelea ("acá tiré el burst"). Las marcas se ven en el gráfico del detalle y van en el JSON exportado. Junto a ellas, el detalle muestra las fases del boss: cuándo su vida bajó del 75, 50 y 25 % ("boss 75% 0:43 · 50% 1:13 · 25% 1:45"), para comparar intentos de progresión. Si el meter empezó a capturar con el boss ya por debajo de una fase, esa fase no se inventa. Si otro programa ya usa una combinación, el meter avisa. Con el overlay enfocado, Esc cierra la configuración o el historial y, si no hay ninguno abierto, vuelve de una pelea guardada a la actual; en el detalle y en el registro, Esc cierra la ventana. "Fijar posición y tamaño" (configuración) evita mover o agrandar el overlay sin querer, pero deja los botones y las filas andando, a diferencia del bloqueo de clics. También hay columnas opcionales CRIT (porcentaje de críticos) y MÁX (golpe más alto) para cada jugador, como en Ember. Alt+← muestra la pelea anterior sin abrir el historial y Alt+→ avanza hasta volver a la actual; mirando una pelea guardada también están las flechas ‹ › al lado de "Volver al vivo". En vivo, un ▲ verde o ▼ rojo junto al puesto marca durante unos segundos a quien acaba de subir o bajar. "Solo el daño al boss" viene prendido de entrada, así los números coinciden con Abyss: contra un boss (marcado en data/npcs.json, o el objetivo que se llevó al menos el 60 % del daño) cuenta solo el daño al boss, y en un pull de trash cuenta todo.

En el historial, las peleas de una misma entrada a dungeon quedan agrupadas bajo un encabezado ("ENTRADA 21:30 · 4 peleas · 2 bosses · 24:10"), con el tiempo desde que entraste hasta la última pelea. El meter detecta la entrada porque al cambiar de zona el juego abre otra conexión. Las marcas del atajo también se guardan en el historial.

En tu propio detalle aparecen CONSEJOS de la pelea, sacados de reglas simples sobre lo que el meter ve: cuánto tiempo pasaste sin pegar y cuándo fue el hueco más largo, las muertes, los golpes por la espalda y cuánto más DPS hizo el mejor de la party. "Comparar con" pone lado a lado a dos jugadores de la misma pelea (DPS, daño, crítico, espalda, golpe máximo, tiempo activo, recibido y muertes), con el mejor de cada fila en dorado. En tu propio detalle la lista también trae "Tu intento anterior" y "Tu mejor intento" contra ese boss, sacados del historial, para ver en qué mejoraste o empeoraste; ahí también se comparan los tiempos a los que el boss llegó al 75, 50 y 25 %.

En el detalle de un jugador se le puede poner un apodo (Enter para guardar, vacío para sacarlo): cambia solo cómo se ve en el meter. En EXPORTAR Y COMPARTIR, "Exportar perfil" guarda tu configuración en `Exportes\perfil-hysteria.json` para llevarla a otra PC o pasársela a la legión, y "Cargar perfil" la lee de ahí; el webhook de Discord y la placa de red se conservan en esta PC y nunca van en el perfil.

A la izquierda de cada nombre va el símbolo de su clase, deducido de las skills que usa, y a la derecha su item level y power ("2130 · 93.1k"), leídos de cuando el jugador aparece cerca (los tuyos, al entrar a la zona). Se ocultan en configuración con "Item level y power al lado del nombre". Si alguna clase sale con el nombre equivocado, se corrige en `data/classes.json`.

Como en Details!, en configuración se puede pintar cada fila con el color de su clase ("Colores por clase", colores en `data/classes.json`), sacar las columnas TOTAL y % para un overlay más angosto, sumar la columna "10 s" (el DPS de los últimos 10 segundos, para ver quién está pegando ahora, como el Last10DPS de ACT) y dejar "Solo mi fila": tu DPS con tu puesto real en la party.

El botón ❚❚ del encabezado pausa el overlay: congela lo que ves (por ejemplo, para leer el resultado con calma) mientras la pelea sigue contando por detrás. La primera vez que abrís el meter aparece una bienvenida con lo básico, y después de cada actualización, la lista de novedades (`data/novedades.json`), una sola vez.

En Apariencia se elige el alto de cada fila (22 a 40 px, de dos en dos) y cuántos jugadores mostrar; si quedás fuera del corte, tu fila ocupa el último lugar con tu puesto real.

En TEMA hay cuatro estilos, como los de Ember: Dorado (el de siempre), Mínimo (gris, sin dorados), Abismo (violeta) y Carmesí.

El menú ⋯ → Minimizar convierte el overlay a una pastilla con tu DPS y tu puesto ("12.3k · #2 de 6"), como Ember; se arrastra igual y el botón ↗ lo vuelve a abrir.

Clic en una fila del overlay abre el desglose por skill de ese jugador (daño, %, golpes, crítico, espalda, máximo, promedio y DPS). El paquete de daño solo trae el ID de la skill: doble clic en una skill del detalle para ponerle nombre (queda guardado en tus preferencias), o cargalos en `data/skills.json` para toda la legión.

Los monstruos tampoco traen nombre: si una pelea sale como "NPC 2001234", agregá ese ID a `data/npcs.json` con su nombre, la dungeon y si es boss (`"2001234": { "name": "Destroyer Tarka", "dungeon": "...", "boss": true }`) y la próxima vez se ve con nombre, también en el historial y en el progreso por boss.

Si además le ponés `"enrage": 480` (segundos de pelea hasta el enrage), la vida del boss en el overlay dice "enrage en 1:20" y se pone roja cuando, al DPS actual, el boss muere después del enrage. Con "Sonido en las alertas" (configuración) suena un aviso de Windows en ese caso, al batir un récord y al terminar la prueba de DPS.

Para grabar una pelea desde el overlay: engranaje → GRABACIÓN → "Grabar lo capturado (.pcap)". Mientras graba aparece `● REC` arriba; los archivos quedan en `%LOCALAPPDATA%\HysteriaMeter\Grabaciones` ("Abrir carpeta"). Esas grabaciones tienen nombres de personajes: no se suben al repo.
Invocaciones sin dueño: si una invocación que el juego manda sin dueño usa una skill de clase (por ejemplo Bittercold Wind del Hechicero) y en la pelea hay un solo jugador de esa clase, su daño se le suma a ese jugador, como hace Abyss.,
Nombres e íconos desde el juego instalado: `tools/HysteriaMeter.Extractor` (`hysteria-extract`) lee los archivos de AION 2 en disco, con el juego cerrado, y deja lo extraído en `%LOCALAPPDATA%\HysteriaMeter\Juego`. Los archivos del juego están cifrados: la clave la ponés vos con `--clave 0x...` o la variable `AION2_AES_KEY`. Sin clave, `hysteria-extract info --juego "D:\SteamLibrary\steamapps\common\AION2"` solo muestra cuántos contenedores hay. El meter usa `Juego\skills.json` y `Juego
pcs.json` si existen, y lo que cargues a mano en `data/` les gana. Nada extraído va al repo (constitución, principio VII).

Las skills de clase (IDs 11xxxxxx a 18xxxxxx) traen nivel y variante en los últimos cuatro dígitos: si no hay nombre para el ID exacto se usa el de la skill base (15280143 → 15280000), así alcanza con nombrar la base.

Íconos de skills: si existe `%LOCALAPPDATA%\HysteriaMeter\Juego\iconos\<id>.png`, el detalle lo muestra a la izquierda del nombre (también con el de la skill base). Los íconos son locales y no se suben al repo. Igual con los emblemas de clase: `Juego\clases\<número de clase>.png` (11 a 18) reemplaza al rombo con el símbolo dibujado en el overlay.

Modo dungeon: en el historial, cada run (peleas con la misma entrada a la zona) tiene un botón RESUMEN ›. Abre la run entera: entrada y última pelea, una línea de tiempo con trash, kills y wipes, una tarjeta por boss con sus intentos y el DPS y las muertes de cada jugador, y una tabla de muertes y DPS por jugador y boss. Un boss es el que está marcado en `npcs.json`, o un objetivo que se llevó la mayor parte del daño y aguantó al menos 30 s; el resto es trash. "Copiar para Discord" copia el resumen como texto.

Rediseño, paso 1: la paleta nueva (fondo casi negro #0C0E13, acento ámbar #E8B04A, filetes y filas en blanco translúcido), números y títulos en Barlow Semi Condensed y la fuente elegida en configuración también en el detalle, el registro y el modo dungeon. Los temas Mínimo, Abismo y Carmesí siguen con su acento, que ahora tiñe también los bordes y resaltados. Para revisar la interfaz sin el juego abierto hay un arnés de capturas: `HYSTERIA_SNAPSHOTS=<carpeta> dotnet test tests/HysteriaMeter.App.Tests --filter "FullyQualifiedName~UiSnapshotTests"` guarda un PNG por ventana y estado con datos de prueba.

Rediseño, paso 2: el historial abre en su propia ventana, con peleas agrupadas por entrada a dungeon, filtros de kills, bosses y últimos siete días, y una columna de progreso por boss. Podés dejarlo abierto mientras mirás una pelea en el overlay; las notas se guardan al cambiar de pelea y los filtros no ocultan intentos del cálculo de progreso.

Rediseño, paso 5: el detalle separa skills, muertes y registro, y permite comparar jugadores o intentos guardados con curvas de DPS sobre el mismo eje de tiempo. Las marcas de muerte quedan en el segundo real; el registro abierto aparte conserva la pelea elegida y se pueden renombrar una skill o todas sus variantes.

Rediseño, paso 6: el overlay incorpora pestañas DPS/CURA/RECIB., menú ⋯, filas con insignias de clase, HP con un decimal e intento histórico contra bosses. RUN muestra los tramos reales de la entrada. Las tarjetas explican los problemas de captura y abren Configuración en Captura. Los avisos de boss, enrage, récord y captura guardada se superponen a las filas y quedan fuera del PNG. El modo compacto y la pastilla conservan sus acciones; a 260 px el encabezado se adapta sin reducir la letra y las columnas opcionales comparten desplazamiento horizontal con sus títulos. Las preferencias antiguas de 24 px migran una vez a 22; luego se puede elegir y guardar 24 normalmente.

Rediseño, paso 7: más fiel a los mocks. Números y títulos van en Barlow Condensed y el cuerpo, por defecto, en IBM Plex Sans, las dos embebidas; Inter y Barlow Semi Condensed siguen como opción y la fuente que ya elegiste no se toca. El encabezado lleva el reloj, la pausa y la configuración a la derecha y, en ventanas angostas, pasa las pestañas a una segunda línea. En modo compacto, el tooltip del encabezado explica cómo salir y ⋯ › Pestaña cambia entre DPS, CURA y RECIBIDO. Las novedades flotan sobre las filas con alto limitado, se desplazan y se cierran con "Entendido" o Esc. Las densidades de 22, 30 y 36 px ajustan radio, separación y tamaño del DPS, y el tooltip de cada fila es una grilla de etiqueta y valor. La pastilla muestra tendencia y reloj. Siguen los mocks: los avisos, la tarjeta de fin de pelea (que ya no repite el récord en el overlay), el resumen del dungeon, la configuración (los temas en el orden del mock y cada fuente escrita en sí misma) y Comparar (primero tus intentos, y el tramo en que estuviste muerto en la explicación). Las novedades de los PRs #67 y #68 tenían barras invertidas sin escapar y por eso el meter no mostraba ninguna; quedó corregido y un test lee el archivo real.

Historial y modo dungeon con datos reales. Como data/npcs.json casi no trae nombres, cada pelea guarda la plantilla del monstruo y un boss sin nombre se muestra como "NPC 2400123" en el overlay, el historial y el dungeon. Los intentos se cuentan por plantilla (o por nombre si no la hay), así que ponerle nombre después en data/npcs.json no los corta, y el "Intento N" del overlay, la tarjeta de fin de pelea, el récord, "Comparar con" y el percentil dan el mismo número que el historial. Las comparaciones son entre intentos de boss: un pull corto que se reseteó ya no queda como récord. El modo dungeon agrupa los bosses por plantilla, cuenta solo los wipes reales (antes inventaba uno por cada kill de más), marca cada tramo con "Nombre 3:58" o "Nombre ✕", escribe en cada boss solo lo que se midió (a qué tiempo y fase cayó cada wipe, o si nadie murió) y suma "Exportar resumen" (CSV de la run); un clic en una celda abre ese intento. Las peleas guardadas antes no tienen plantilla: sin nombre quedan como "Encuentro" y no se comparan, y las que tenían nombre siguen como una serie aparte de las nuevas.

Detalles menores del rediseño, paso 7. La alerta de enrage tiene una sola capa de fondo, como el mock, y deja ver apenas la fila de abajo. En las filas del overlay, si el nombre no entra se recorta solo el nombre: la cruz de muertes y la flecha ▲/▼ se ven siempre, como la ✕ de los tramos del dungeon, también con el overlay en pausa. Con el overlay minimizado, Esc vuelve a la pelea actual sin cerrar las novedades, que quedan para cuando lo agrandes. La tarjeta de novedades muestra la barra de desplazamiento solo si queda texto afuera. En Comparar, el tramo en que estuviste muerto termina en tu siguiente golpe directo: un DoT que sigue pegando ya no lo corta (en las peleas guardadas antes no se puede separar el DoT y se usa la serie completa, como hasta ahora). Los tests ya no arrancan el meter de verdad, ni el arnés de capturas ni el historial corrido solo: no abren la base ni tocan las ventanas, las preferencias o el inicio con Windows.

El ACTIVO y el TIEMPO MUERTO del detalle cuentan solo tus golpes directos: un DoT que sigue pegando mientras estás muerto o quieto ya no tapa ese hueco. Muerto de 0:30 a 0:45 con un veneno corriendo, antes daba 92 % activo y ahora 79 %. La fila "Activo" de Comparar y el consejo "Pasaste N s sin pegar" usan la misma cuenta. En las peleas guardadas antes no se puede separar el DoT y se usa el daño completo, como hasta ahora.

Si cambiás el tema con una pelea guardada abierta en el overlay, la franja de arriba (la fecha, las flechas ‹ ›, "Volver al vivo" y la caja de la nota) toma los colores nuevos en el momento; antes se quedaba con los del tema anterior hasta volver a abrir el meter. La caja de la nota tiene el fondo de las demás cajas de texto y "Borrar pelea" el rojo de las otras acciones destructivas.

El DPS ACTIVO usa el mismo tiempo activo que ACTIVO y TIEMPO MUERTO: los ticks de DoT ya no cuentan como actividad, ni suman daño cuando caen con el jugador muerto o quieto. Sin tiempo muerto, el DPS activo es igual al DPS. Muerto 20 s en una pelea de 2 min, el daño se divide por 100 s. Si todo tu daño fue DoT se muestra "–". El consejo "Sin ese tiempo muerto…" ahora sale solo después de "Pasaste N s sin pegar" y calcula sobre esos segundos. Las peleas guardadas antes conservan el valor que tenían.

DIAGNÓSTICO y "Reportar un problema" suman dos contadores: las desincronizaciones del lector de mensajes y los paquetes del juego que el meter tiró porque no daba abasto. Ese segundo contador antes valía siempre 0, también en el Recorder, aunque se tiraran paquetes. El reporte que se hace desde el overlay (captura detenida o aviso de parche) ahora lleva el diagnóstico aunque no se haya abierto Configuración.

En la bienvenida, el paso de la placa de red muestra el nombre de la placa ("Intel(R) Ethernet Connection…") en vez del `\Device\NPF_{GUID}`, y dice de dónde salió: "elegida en la configuración" si es la que guardaste y sigue conectada, "fijada en appsettings.json" si viene de `Capture:Device`. Si la guardada no está, nombra la que de verdad usa la captura.

La vida de los monstruos se lee bien desde que aparecen. Antes se tomaba de los últimos bytes del paquete de aparición, que en casi todos los monstruos de la dungeon y del mini boss son otros datos. Así salía basura (un add de 435.000 de vida aparecía con 20 de 91) o no salía nada. Eso contaba overkill falso en los primeros golpes y podía dejar a ese objetivo sin las marcas de fase del 75, el 50 y el 25 %. Ahora la vida se lee en su posición fija, y en las capturas de referencia coincide con la primera actualización de vida del servidor.

El meter comprueba solo si leyó todo el daño. En Configuración → Diagnóstico, el bloque Verificación compara, para cada monstruo de la pelea, el daño que contó (de todos los jugadores, también los que el filtro deja afuera, y de las invocaciones aunque el meter no sepa de quién son, como pasa con dos jugadores de la misma clase en la party) con la vida que perdió según el servidor, con 1 % de tolerancia, y dice si cierra o cuánto falta o sobra. El objetivo principal también lo muestra en el tooltip de su vida, y el reporte de un problema lo incluye sin nombres. En la dungeon de referencia cierran todos, y en la pelea de comparación cierran los tres bosses; en algunos monstruos del mundo abierto la vida baja más que el daño, por una escala que todavía no se explica, así que ahí va a decir que no cierra.

Si Npcap se instaló con la opción "Restrict Npcap driver's access to Administrators only", Windows pide permiso (UAC) cada vez que el meter abre la captura, y la bienvenida ya lo avisa en el paso de Npcap. Si la captura se corta antes de que llegue tráfico, el overlay explica que falta ese permiso. Da tres salidas: volver a abrir el meter y aceptar el pedido, abrirlo como administrador o reinstalar Npcap sin esa opción. Igual muestra el error real y el botón para reportar un problema, por si el corte fue por otra cosa. Para saberlo, el meter solo lee del registro la opción con la que se instaló Npcap y no cambia nada del sistema.

El meter lee todas las actualizaciones de vida que manda el servidor, también las que traen varios valores juntos, como la del jugador al revivir. Antes se perdían y el overkill de los golpes sobre un jugador se medía contra una vida vieja, por ejemplo la de su muerte, y salía de más. El daño, el DPS y la verificación de los monstruos no cambian. Las peleas ya guardadas conservan el overkill con el que se guardaron.

El menú "⋯" del overlay se abre sin problemas. Desde el rediseño, abrirlo cerraba el meter por un error de estilo en los separadores del menú. Ahora se ve como en el diseño: los ítems con el estilo del overlay y los separadores como una línea fina.

Cuatro skills que el detalle mostraba como "Skill 12060240" y parecidos ya tienen nombre: Punishing Strike, Decisive Strike y Desperate Strike del Templario, y la teostone Ulgorn's Will. Salen de comparar una pelea con la vista detallada de Combat Analysis, donde coincidían el daño, los golpes y los críticos, y van en `data/skills.json`, que completa la lista de MetaBot.GG.

La tabla de skills del detalle suma las columnas PERF., DOBLE, FRENTE, ESPALDA y MULTI, como la vista detallada de Combat Analysis: el % de golpes perfectos, dobles, de frente, por la espalda y múltiples de cada skill, y el de todas juntas en TOTAL. La ventana arranca un poco más ancha para que entren; si la achicás por debajo de 1040 se ocultan y ESPALDA vuelve al tooltip de la fila.

## Spec Kit

El proyecto se desarrolla con [Spec Kit](https://github.com/github/spec-kit). Con Claude Code: `/speckit-specify` para una feature nueva, luego `/speckit-plan`, `/speckit-tasks` y `/speckit-implement`. La feature actual es [`specs/001-dps-meter-mvp`](specs/001-dps-meter-mvp/spec.md).
