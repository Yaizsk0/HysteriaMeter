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

Como en Details!, el botón + abre otra ventana del overlay: cada una elige su pestaña (DAÑO o CURACIÓN) y si muestra el HPS junto al daño, y todas se recuerdan con su posición. Cerrar la primera cierra la app. Al soltar una ventana cerca de un borde de la pantalla o de otra ventana del meter, se pega a ese borde (imanes; se apagan en configuración).

En la bandeja del sistema aparece un rombo dorado: clic para mostrar u ocultar el overlay (sirve aunque esté en modo atravesable) y clic derecho para reiniciar la pelea, abrir la carpeta de datos o salir.

Doble clic en el encabezado alterna el modo compacto (solo filas y reloj). El overlay se vuelve opaco al pasar el mouse y, si lo activás, se atenúa cuando no hay combate. El punto del encabezado indica la captura: verde en combate, ámbar sin tráfico del juego, rojo si se cortó. En configuración, "Ver barras de prueba" muestra una pelea inventada para acomodar todo sin combatir.

El historial (botón del reloj) se puede buscar por boss o por nota y filtrar por derrotados. Cada pelea muestra tu DPS, el número de intento contra ese boss, tu percentil entre tus intentos con el color de Warcraft Logs (P100 dorado = tu mejor, naranja, violeta, azul, verde, gris) y una ★ si es tu récord; al batir un récord aparece un aviso dorado, y si no, al terminar la pelea el aviso dice cuánto mejoraste o empeoraste contra tu intento anterior ("+8% vs tu intento anterior"). Al abrir una pelea guardada se le puede escribir una nota o borrarla. Si la búsqueda deja un solo boss, arriba aparece la línea de tu DPS intento por intento, con el último, el mejor y la kill más rápida; esa pelea lleva la marca "⏱ kill más rápida". Con más de un personaje, récords, intentos, percentiles y el aviso "vs tu intento anterior" se cuentan por personaje, cada pelea dice con cuál fue y arriba aparece un selector para ver uno solo. En configuración se elige cuánto guardar: los bosses derrotados y las peleas con nota no se borran nunca.

Con la vida del boss a la vista, el overlay estima en cuánto muere al ritmo actual de la party ("muere en 0:42"). Cada fila tiene detrás una línea tenue con el DPS de los últimos 30 segundos. En configuración se elige el formato de los números (2.04M, 2.0M o 2.040.321) y cada cuánto se refresca el overlay.

En configuración, EXPORTAR Y COMPARTIR copia el overlay como imagen (para pegar en Discord) y guarda la pelea en CSV o JSON en `%LOCALAPPDATA%\HysteriaMeter\Exportes`. Con "Guardar una imagen al derrotar un boss", cada boss derrotado (marcado como boss en `data/npcs.json` o que aguantó al menos 30 s) deja ahí un .png del overlay con su nombre y la hora (no se guarda si el overlay está en pausa, minimizado o con un panel abierto). Con "Ocultar los nombres de los demás", el resumen y los archivos dicen "Yo", "Jugador 1", "Jugador 2". Si pegás la URL de un webhook de Discord, el resumen se puede mandar al canal a mano o solo al derrotar un boss. Es lo único que sale de tu PC, y solo si lo configurás.

"Abrir pelea…" carga un JSON guardado así (tuyo o el que te pasó otro de la legión) y lo muestra en el overlay como una pelea del historial, con detalle, skills y gráfico. También se puede arrastrar el archivo al overlay.

La pestaña RECIBIDO muestra el daño que le hicieron los monstruos a cada uno (todavía sin validar contra Combat Analysis). Quien murió lleva una ✝ junto al nombre; en el detalle del jugador se ve qué lo mató y cuánto recibió en los 10 segundos anteriores. El botón "Registro" del detalle abre el golpe a golpe de la pelea, con búsqueda por jugador, skill o modificador.

Cada ventana elige entre PELEA (la actual), RUN (todas las peleas desde que entraste a la dungeon, trash y bosses juntos, con el tiempo de la run en el título: "Run 18:20 · 9 peleas") y SESIÓN (todas las peleas desde el último reinicio, con el DPS sobre el tiempo en combate), y puede contar solo el daño al boss. En configuración se elige tras cuántos segundos sin daño termina una pelea, y si el overlay vuelve solo al vivo cuando arranca otra mientras mirás el historial.

Los datos (preferencias, historial, grabaciones y exportes) van a `%LOCALAPPDATA%\HysteriaMeter`. Con un archivo `portable.txt` junto a `HysteriaMeter.exe`, van a la carpeta `datos` al lado del programa. El historial de versiones anteriores, que estaba junto al .exe, se copia solo la primera vez. En configuración, "Iniciar con Windows" abre el meter al iniciar sesión (una entrada en el registro de tu usuario, sin administrador); si movés la carpeta del programa, la ruta se corrige sola al abrirlo.

En configuración, DIAGNÓSTICO muestra el estado de la captura y cuántos mensajes del juego se leyeron, cuántos se reconocen, los errores y las resincronizaciones. Si llega tráfico pero no se reconoce nada, el punto se pone ámbar con un aviso de que el juego pudo haberse actualizado. El tooltip de la vida del boss compara el daño contado con la vida que perdió (auto-verificación). "Reportar un problema" copia la versión y el diagnóstico (sin nombres de personajes ni el webhook) y abre un issue nuevo en GitHub para contar qué pasó.

El detalle del jugador muestra cuánto del tiempo estuvo pegando (ACTIVO) y el TIEMPO MUERTO: cada hueco de más de 2 segundos sin daño, desde el primer golpe hasta el último. En el historial, cada pelea contra un boss repetido dice cuánto le faltó a tu DPS para tu mejor intento ("−12% vs mejor"). Para practicar contra el muñeco, PRUEBA DE DPS en configuración cuenta 60, 180 o 300 segundos desde el primer golpe y congela el resultado; Ctrl+Shift+R reinicia para otro intento.

Los atajos globales se cambian en configuración, en ATAJOS: clic en la caja y apretá la combinación nueva (Borrar lo saca). Por defecto: Ctrl+Shift+L bloquea los clics, Ctrl+Shift+R reinicia, Ctrl+Shift+H oculta el overlay, Ctrl+Shift+N lo minimiza a la pastilla, Ctrl+Shift+P lo pausa y Ctrl+Shift+M marca el momento de la pelea ("acá tiré el burst"). Las marcas se ven en el gráfico del detalle y van en el JSON exportado. Junto a ellas, el detalle muestra las fases del boss: cuándo su vida bajó del 75, 50 y 25 % ("boss 75% 0:43 · 50% 1:13 · 25% 1:45"), para comparar intentos de progresión. Si el meter empezó a capturar con el boss ya por debajo de una fase, esa fase no se inventa. Si otro programa ya usa una combinación, el meter avisa. Con el overlay enfocado, Esc cierra la configuración o el historial y, si no hay ninguno abierto, vuelve de una pelea guardada a la actual; en el detalle y en el registro, Esc cierra la ventana. "Fijar posición y tamaño" (configuración) evita mover o agrandar el overlay sin querer, pero deja los botones y las filas andando, a diferencia del bloqueo de clics. También hay columnas opcionales CRIT (porcentaje de críticos) y MÁX (golpe más alto) para cada jugador, como en Ember. Alt+← muestra la pelea anterior sin abrir el historial y Alt+→ avanza hasta volver a la actual; mirando una pelea guardada también están las flechas ‹ › al lado de "Volver al vivo". En vivo, un ▲ verde o ▼ rojo junto al puesto marca durante unos segundos a quien acaba de subir o bajar. "Solo el daño al boss" viene prendido de entrada, así los números coinciden con Abyss: contra un boss (marcado en data/npcs.json, o el objetivo que se llevó al menos el 60 % del daño) cuenta solo el daño al boss, y en un pull de trash cuenta todo.

En el historial, las peleas de una misma entrada a dungeon quedan agrupadas bajo un encabezado ("ENTRADA 21:30 · 4 peleas · 2 bosses · 24:10"), con el tiempo desde que entraste hasta la última pelea. El meter detecta la entrada porque al cambiar de zona el juego abre otra conexión. Las marcas del atajo también se guardan en el historial.

En tu propio detalle aparecen CONSEJOS de la pelea, sacados de reglas simples sobre lo que el meter ve: cuánto tiempo pasaste sin pegar y cuándo fue el hueco más largo, las muertes, los golpes por la espalda y cuánto más DPS hizo el mejor de la party. "Comparar con" pone lado a lado a dos jugadores de la misma pelea (DPS, daño, crítico, espalda, golpe máximo, tiempo activo, recibido y muertes), con el mejor de cada fila en dorado. En tu propio detalle la lista también trae "Tu intento anterior" y "Tu mejor intento" contra ese boss, sacados del historial, para ver en qué mejoraste o empeoraste; ahí también se comparan los tiempos a los que el boss llegó al 75, 50 y 25 %.

En el detalle de un jugador se le puede poner un apodo (Enter para guardar, vacío para sacarlo): cambia solo cómo se ve en el meter. En EXPORTAR Y COMPARTIR, "Exportar perfil" guarda tu configuración en `Exportes\perfil-hysteria.json` para llevarla a otra PC o pasársela a la legión, y "Cargar perfil" la lee de ahí; el webhook de Discord nunca va en el perfil.

A la izquierda de cada nombre va el símbolo de su clase, deducido de las skills que usa, y a la derecha su item level y power ("2130 · 93.1k"), leídos de cuando el jugador aparece cerca (los tuyos, al entrar a la zona). Se ocultan en configuración con "Item level y power al lado del nombre". Si alguna clase sale con el nombre equivocado, se corrige en `data/classes.json`.

Como en Details!, en configuración se puede pintar cada fila con el color de su clase ("Colores por clase", colores en `data/classes.json`), sacar las columnas TOTAL y % para un overlay más angosto, sumar la columna "10 s" (el DPS de los últimos 10 segundos, para ver quién está pegando ahora, como el Last10DPS de ACT) y dejar "Solo mi fila": tu DPS con tu puesto real en la party.

El botón ❚❚ del encabezado pausa el overlay: congela lo que ves (por ejemplo, para leer el resultado con calma) mientras la pelea sigue contando por detrás. La primera vez que abrís el meter aparece una bienvenida con lo básico, y después de cada actualización, la lista de novedades (`data/novedades.json`), una sola vez.

En FILAS se elige el alto de cada fila (24, 30 o 36 px) y cuántos jugadores mostrar; si quedás fuera del corte, tu fila ocupa el último lugar con tu puesto real.

En TEMA hay cuatro estilos, como los de Ember: Dorado (el de siempre), Mínimo (gris, sin dorados), Abismo (violeta) y Carmesí.

El botón — del encabezado minimiza el overlay a una pastilla con tu DPS y tu puesto ("12.3k · #2 de 6"), como Ember; se arrastra igual y el cuadradito lo vuelve a abrir.

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

## Spec Kit

El proyecto se desarrolla con [Spec Kit](https://github.com/github/spec-kit). Con Claude Code: `/speckit-specify` para una feature nueva, luego `/speckit-plan`, `/speckit-tasks` y `/speckit-implement`. La feature actual es [`specs/001-dps-meter-mvp`](specs/001-dps-meter-mvp/spec.md).
