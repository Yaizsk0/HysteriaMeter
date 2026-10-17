# Decisiones del rediseño (las tomó el orquestador; no volver a preguntarlas)

Mocks: `scratchpad/redesign/project/*.dc.html`. Mapeo por pantalla: `scratchpad/mapa/<Pantalla>.json` (elementos, fuente de datos, comportamientos a preservar, pasos). Tokens: `scratchpad/mapa/tokens.md`. Arnés de capturas: `scratchpad/mapa/arnes.md`.

## Reglas generales
- **No ejecutar la app** (`dotnet run` ni el .exe): el usuario juega en esta PC. Para ver la UI se usa el arnés de capturas (ventanas fuera de pantalla, sin captura de red).
- **Nunca inventar datos.** Si el mock muestra algo sin fuente real ("no existe" en el mapeo y no se puede derivar sin I/O nuevo del juego), el hueco se omite o se colapsa (DataTrigger / Visibility) y se anota en el informe final. Nada de valores falsos.
- **Principio I:** captura pasiva. Nada que envíe paquetes ni toque el proceso o la memoria del juego.
- Comentarios, textos de UI y mensajes en español. Build con `-warnaserror` (analizadores activos: CA1859, etc.).
- Fuentes originales del mock, embebidas con licencia OFL: **Barlow Condensed** en `NumberFont`/`TitleFont` e **IBM Plex Sans** como `BodyFont` por defecto. Esta corrección reemplaza la sustitución inicial por Barlow Semi Condensed/Inter. Ambas opciones anteriores se conservan para preferencias existentes.
- Los pesos de Barlow Condensed se resuelven en WPF con `#Barlow` dentro de su propia carpeta de recursos; así no se mezcla con la familia anterior. IBM Plex Sans tiene su carpeta propia. No requieren instalación global ni conexión. WPF no tiene letter-spacing: se omite.
- Colores siempre desde `Themes/Theme.xaml` (claves del PR de tema). Dentro de `Style`/`ControlTemplate`, `DynamicResource` para lo que cambia con el tema. Nada de hex sueltos salvo colores de clase/datos.
- Los tamaños en px del mock son referencia: el overlay es redimensionable y tiene zoom (Ctrl+rueda); layout proporcional, sin anchos fijos que rompan al achicar.
- Mantener TODAS las funciones existentes accesibles. Si el mock no muestra un botón/acción que existe, va a un menú "⋯" o a un tooltip, nunca se elimina.
- Tests nuevos en un archivo nuevo por pista (no tocar el final de WindowSmokeTests.cs salvo para arreglar tests que el cambio rompe), para que las pistas paralelas no choquen.
- README y data/novedades.json los agrega el orquestador al abrir el PR: no tocarlos.

## Paleta y tema
- El tema por defecto sigue llamándose "Dorado" y toma el aspecto de los mocks (#0C0E13 + acento #E8B04A). Carmesí/Abismo/Mínimo conservan su acento actual; las claves derivadas del acento se recalculan en MeterThemes.Apply.
- Se cambian los valores de las claves compartidas (Gold, Text*, Panel, Row, Rule...): recolorea todas las ventanas a la vez.
- **Opacidad:** se mantiene la semántica actual (Window.Opacity: toda la ventana, "opaco al pasar el mouse", "atenuar sin combate"). La etiqueta pasa a "Opacidad".
- **Sombra del panel del overlay: NO** (rompe imanes, captura de clics y la imagen exportada). Las ventanas grandes sí pueden llevar sombra si no complica.
- Se eliminan las esquinas doradas (estilo Corner) y el degradé del encabezado.
- Percentiles con los colores nuevos (#9D9D9D #4FD15C #4A90E8 #B867F0 #F0A04A #E8B04A), mismos umbrales.

## Overlay en vivo (Main, Estados, Mini pastilla, avisos, errores)
- Encabezado: rombo + HYSTERIA, segmentado DPS / CURA / RECIB. (pestaña activa: fondo acento; en CURA fondo #7BD88F con texto OnHealing; en RECIB. fondo Danger #F08A7E con texto oscuro), reloj, pausa, configuración y un botón "⋯" con menú (estilo menú de bandeja del mock Avisos: fondo MenuBg, borde Stroke, radio 8) que contiene: Historial, Reiniciar, Copiar resumen, Nueva ventana, Segmento PELEA/RUN/SESIÓN (sub-opciones con check), Bloquear clics, Minimizar, Cerrar. Se mantienen en el encabezado el punto de estado de captura y "● REC" (chicos, a la izquierda del reloj).
- Bloque objetivo: nombre (Barlow 17 SemiBold), "Intento N" (solo contra bosses según BossRules, desde el 2º intento; la cuenta sale de la base si es barato con una consulta nueva en Storage, con test; si no, se omite), % de vida con un decimal (GoldBright), barra 5 px con degradé Health, línea "Party X/s" y "Enrage en m:ss" (si hay enrage). Vida absoluta y "muere en m:ss" van al tooltip del %.
- Filas: radio 5, fondo Row, barra del color de clase con alfa ~0x55, puesto (Barlow 13 tenue), insignia circular 20 px: emblema PNG si existe (PR #68), si no círculo con la inicial de la clase en su color, sin clase la inicial del jugador; nombre (`NumberFont`, Barlow Condensed SemiBold 16 con fila de 30 px, TextBright; local en acento con contorno RowBorderLocal 1 px), flecha ▲/▼ (Success/Danger), DPS (Barlow Bold 17, tabular), % con un decimal ("31,5 %"). Se quita el sufijo " (vos)": lo marca el contorno + color (ajustar tests).
- Encabezado de columnas: se oculta cuando solo se ven DPS y %; si hay columnas opcionales (CRIT, MÁX, total, etc.) se muestra.
- Pie: "Total X · N jugadores" a la izquierda y "Clic en una fila: detalle" a la derecha (se mantiene clic simple = detalle; doble clic = compacto, como hoy). Crít./Máx. del local pasan al tooltip del total.
- Defaults para instalaciones nuevas como el mock: texto IBM Plex Sans, Sparklines=false y ColorByClass=true, entre otros. No se reemplazan los valores guardados ni se fuerza una migración de fuente o de columnas al usuario.
- Compacto: como hoy (oculta pestañas, filas 22).
- Tarjeta de espera ("Listo para la próxima pelea"): solo cuando el overlay está vacío de verdad (arranque o después de Reiniciar). Entre peleas siguen visibles las filas de la última pelea, como hoy. Sin nombre de zona (no existe): muestra "Última: <objetivo> · <duración>" si hay.
- Vista RUN: título "Run · entrada HH:mm" (no hay nombre de zona); chips por tramo (Trash agrupa tramos consecutivos; kill dorado/verde; wipe rojo con nombre del boss); tiempo = hasta el último golpe (como RunTitle).
- Pestaña CURA: sin "overheal" (no hay dato): se omite.
- Densidades: 22 / 30 / 36 (migrar el viejo 24 → 22 si hace falta, sin romper preferencias). Como el mock Estados: radio 4 / 5 / 5, separación entre filas 2 / 3 / 3 y DPS a 13 / 17 / 18. El tooltip de la fila es una grilla etiqueta-valor (etiquetas en `TextDim`) con la clase al lado del nombre; "Críticos" se omite cuando no hay dato. En modo compacto, el tooltip de la franja dice cómo salir y dónde cambiar la pestaña.
- Pastilla minimizada (Mini): rediseño con `MiniPanel` (fondo al 88 %, distinto del aviso `PanelRaised`), borde AccentLine, radio pill: insignia de clase, tu DPS (Barlow), tu puesto; botón chico para volver (el mismo de hoy).
- Avisos dentro del overlay: superpuestos arriba de las filas (no empujan el alto), PanelRaised + borde AccentStrong, radio 8: "Boss detectado" (al empezar una pelea con TargetIsBoss), alerta de enrage mientras IsEnrageDanger (AlertBg, borde DangerStrong, texto OnDanger) que reemplaza el RecordMessage "No llegan al enrage…", "Captura guardada" (SuccessLine) con link "Abrir" a la carpeta (Exportes, como hoy). Récord y "Momento marcado" adoptan el mismo estilo de aviso. Si se abre la tarjeta de fin de pelea, el aviso de récord o de "vs tu intento anterior" no se muestra (la tarjeta ya lo dice); el sonido se mantiene. Se ocultan solos (N s) y no se ven en la imagen del kill. Solo en la ventana principal.
- Errores de captura (Inicio): tarjetas "No se encontró Npcap" (DangerBorder, link a npcap.com), "Capturando, pero no llega nada de AION 2" (título en Warning, borde WarningLine al 45 %, botón "Elegir placa de red" → abre Configuración en Captura) y "El juego se actualizó…" (texto suavizado: "Puede que el juego se haya actualizado"), mostradas en el área de filas cuando corresponda (Npcap/sin tráfico: con el overlay vacío; parche: como aviso).

## Fin de pelea (Mini, tarjeta)
- Ventana propia pequeña `FightCardWindow` (sin bordes, topmost, no activa, Owner = overlay), debajo del overlay (o de la pastilla si está minimizado). Solo en kills de boss (BossRules.IsBossKill). Se cierra a los 12 s, con Esc, con clic en ✕ discreto, al empezar otra pelea o al abrir el detalle.
- Contenido: "BOSS DERROTADO · m:ss" (Eyebrow), nombre, KPIs: TU DPS (Dps del local; si BossOnly, MainTargetDps; en ese caso los tooltips de la comparación y del percentil aclaran que comparan el DPS de la pelea entera, el que guarda el historial), PUESTO, percentil (HistoryItem.PercentileOf con TierBrush; con <2 intentos se oculta), diferencia vs intento anterior (si existe; si no, se oculta), skill top por nombre base (SkillNames), botones "Ver detalle" (primario) y "Guardar captura" (fantasma, mismo PNG/carpeta que hoy, nombre sin pisar ImageOnKill).
- Solo la ventana principal. Respeta click-through: si el overlay está bloqueado, la tarjeta también deja pasar clics.

## Historial (ventana propia)
- `HistoryWindow` (≈1040×700, redimensionable, sin bordes con cabecera arrastrable, estilo Dungeon). Reemplaza el panel de historial del overlay (se borra ese bloque de OverlayWindow.xaml). Abre con el mismo comando/atajo de hoy. Queda abierta al abrir una pelea (la fila seleccionada se marca con AccentFaint).
- Chips: Todas / Solo kills / Solo bosses / Esta semana (excluyentes como el mock). "Solo bosses" usa BossRules (hace falta guardar o derivar si la pelea fue de boss: preferir derivar de columnas existentes; si falta, agregar columna con migración y test). "Esta semana" = últimos 7 días.
- Runs agrupadas por ZoneEnteredAt con título "Run · entrada dd/MM HH:mm", resumen "k/n bosses · w wipes" (n = bosses distintos intentados) y link "Resumen de la run" (abre DungeonWindow).
- Fila: insignia ✓ kill / ✕ wipe / · trash, objetivo, duración, tu DPS, delta vs intento anterior contra ese boss (solo kills), "intento N" (conteo histórico actual), MUERTES (suma de encounter_actors.deaths de la pelea: consulta nueva con test). ★ récord, fecha/hora y nota van en la fila de forma discreta o tooltip; no se pierden.
- Panel lateral "TU PROGRESO": del boss de la fila seleccionada (o del filtro si deja uno); sin selección, estado vacío con texto. Gráfico con color por percentil real. KILLS k/n = kills/peleas contra ese objetivo con tu DPS.
- Contador "N peleas · M runs" de lo filtrado. Estados vacíos con texto.

## Objetivos sin nombre, intentos y wipes (datos reales)
- data/npcs.json casi no trae nombres: el objetivo se identifica por su nombre o, sin nombre, por su plantilla de NPC con el mismo rótulo del overlay, "NPC 2400123" (`FightTarget.Key`/`Label` en Combat). Sin nombre ni plantilla (peleas guardadas antes de registrarla) es "Encuentro" y no se compara con nada. Los nombres "#entidad" de versiones viejas cuentan como sin nombre.
- Storage guarda la plantilla en `encounters.target_npc_id` (migración aditiva, NULL en las peleas viejas). El intento N y el récord del historial cuentan por nombre o, si las dos peleas no tienen nombre, por plantilla; sin ninguno de los dos, intento 0 y sin récord.
- Historial: la fila dice el rótulo del overlay ("NPC 2400123") y en el tooltip cómo ponerle nombre (agregar el ID a data/npcs.json). Intento, percentil, récord, delta, kill más rápida y TU PROGRESO agrupan por ese rótulo. El trash dice "Trash" (el objetivo real va al tooltip) y no lleva intento, percentil, récord ni delta; tampoco entra en la serie de TU PROGRESO, que tiene su propio estado vacío para una fila de trash. Una pelea vieja sin nombre ni plantilla que no murió queda "Encuentro": no se puede probar que fue trash. Objetivo, intento y nota van en la misma línea como el mock; si no entra, se recorta la nota (completa en el tooltip).
- BossRules: un wipe (pelea sin kill) puede ser de boss si tiene nombre **o plantilla**; sin ninguno de los dos es trash (no se puede probar que fue un wipe).
- Modo dungeon: los intentos se agrupan por plantilla (o nombre); nunca se juntan peleas sin clave: cada una es su propio boss si fue kill ("Boss N" por su lugar en la run). Un kill cierra el boss: la misma plantilla después es otro boss ("NPC 2400123 (2)"). Wipes = intentos reales sin kill (antes era "intentos − 1", que inventaba wipes al juntar kills distintos). El DPS y la duración salen del intento que decide (el kill, o el último).
- Ventana del modo dungeon, como el mock: el segmento de un kill dice "Nombre 3:58" (la duración de su tarjeta), el de un wipe "Nombre ✕" con el token OnWipe y el trash "Trash". La nota al pie de cada boss sale solo de lo medido: cada wipe con el tiempo al que cayó y, si se midió, la fase más baja ("vida bajo el 50%", de las fases 75/50/25); sin wipes, "Kill limpio" (nadie murió) o "Kill al primer intento", y el mejor DPS. La "fase N", el % exacto de vida y el "por enrage" del mock no se guardan y no se muestran. "Exportar resumen" guarda en Exportes el CSV de la run (una fila por jugador y boss, y la run completa); ese CSV y "Copiar para Discord" respetan "Ocultar los nombres de los demás". Un clic en una celda de la matriz abre en el overlay el intento que da ese DPS (el kill, o el último) y la ventana queda abierta, como el historial.

## Configuración (ventana propia)
- `ConfigWindow` (≈1040×700) con nav a la izquierda: Apariencia (como el mock), Columnas, Alertas y sonidos, Atajos, Captura, Datos del juego, Historial, Diagnóstico. Todos los controles actuales se reparten ahí con el estilo del mock (toggles 36×20, chips, slider). Se borra el panel de configuración del overlay.
- Opacidad con la semántica actual. Máximo de jugadores: slider 4–24 + opción "Todos" (0). Alto de fila: 22–40 en pasos de 2; compacto sigue forzando 22.
- Atajo nuevo "Cambiar pestaña" (rota DPS→CURA→RECIB. en la ventana principal), por defecto vacío.
- Captura: selector de placa de red (LiveCaptureSource.ListDevices) guardado en preferencias, se aplica al reiniciar el meter (decirlo en la UI); "Automática" por defecto.
- Datos del juego: carpeta %LOCALAPPDATA%\HysteriaMeter\Juego con conteos (skills con nombre, íconos, emblemas) y botón "Abrir carpeta".
- Tema: 4 tarjetas con vista previa de las paletas reales de MeterThemes, en el orden del mock: Abismo, Dorado, Mínimo, Carmesí (`ThemeCard.Order`; `MeterThemes.All` no cambia porque su primer tema es el de reemplazo).
- Copiar imagen / Ver barras de prueba / Abrir pelea: ya no necesitan cerrar nada; la ventana queda abierta.

## Detalle (Detalle, Muertes, Comparar)
- DetailWindow rediseñada como el mock: cabecera con avatar (emblema PNG o inicial de clase en su color), nombre, clase, perfil (formato actual con NumberStyle, no romper tests), "Comparar con" (sin preselección), cerrar.
- KPIs del mock; los stats actuales que el mock no muestra (DPS activo, duración, overkill, recibido, muertes, activo, tiempo muerto) van en el tooltip de cada KPI o en una línea secundaria: no se pierden. PERCENTIL solo para el jugador local, con ≥2 intentos (consulta en Storage si hace falta, con test); si no, oculto.
- Comparar: en "Comparar con" van primero tus intentos guardados y después los jugadores (como el mock); el " · DPS" de cada opción se mantiene porque ayuda a elegir. Si perdiste en la skill elegida y moriste, la frase agrega ", coincide con la muerte de m:ss a m:ss" (de la muerte al siguiente golpe propio o al final; se omite si el tramo dura menos de 3 s). En Muertes, el log de la derecha muestra el mismo "Solo {jugador}" de la pestaña Log (lo comparten), para que el filtro se vea y se pueda sacar.
- Pestañas: Skills, Muertes, Log. "Objetivos" NO (no hay datos): se omite. Tabla de skills con íconos (PR #67), conserva TOTAL/ESPALDA/PROM. (en tooltip de fila si no entran). Nota "invocación": se omite.
- Gráfico: barras doradas = Marks (momentos marcados) si existen.
- Muertes: navegación ‹ › (y flechas), última muerte por defecto, ventana del recap = DeathRecap.Window real (no hardcodear 8 s), barra de vida solo si hay dato (local), curaciones del recap solo si están en el log; formato "{skill} de {origen} · {modificadores}".
- Log integrado en la pestaña Log del jugador elegido (filtro y búsqueda), tiempo con décimas como hoy, etiquetas en español; LogWindow se mantiene y se restilea con la paleta.
- Comparar: estado propio del cuerpo del detalle al elegir una opción en "Comparar con"; "Sin comparar" para salir. Columna "GOLPES" (SkillSnapshot.Hits), compara daño total y DPS por skill; eje X en tiempo absoluto; marcadores de todas las muertes; delta verde = mejora, rojo = empeora. Las stats actuales de comparación pasan a una franja secundaria.

## Inicio (bienvenida)
- `WelcomeWindow` propia y centrada en el primer arranque (bandera nueva WelcomeDone en preferencias, no reutilizar LastSeenNews). Checklist real: Npcap (detectado o no), placa de red (la elegida o "automática", "esperando tráfico" hasta el primer paquete si se puede saber sin I/O nuevo; si no, "automática"), datos del juego (conteos reales; "Listo" si hay skills.json, "Revisar" con link a la carpeta si no).
- "Empezar a capturar" cierra y marca WelcomeDone (la captura ya corre); "Más tarde" cierra sin marcar.
- Los consejos de NewsFeed.WelcomeLines pasan a una línea de ayuda en la bienvenida.

## Bandeja y novedades (Avisos)
- Menú de bandeja: ToolStripProfessionalRenderer propio con los colores del mock (MenuBg, borde Stroke, hover AccentSoft, "Salir" en Danger). Texto de 13 px lógicos (9,75 pt), con la fuente elegida; IBM Plex Sans por defecto. Las fuentes embebidas se registran solo en memoria del proceso para que GDI las pueda medir y dibujar; se liberan al cambiar de fuente o disponer el menú. Se mantienen todas las opciones actuales. Se muestra el atajo configurado al lado de "Ocultar overlay".
- Tarjeta de novedades: WindowBg con borde AccentLine, "NOVEDADES · vX.Y.Z" (AppVersion corta), lista de NewsFeed, botón "Entendido".
  Flota sobre el cuerpo del overlay (debajo del encabezado) sin empujar las filas; su alto lo limita la ventana, la lista se
  desplaza y "Entendido" queda siempre a la vista. En modo compacto o con menos de 280 px de cuerpo va apretada (sin "Qué
  cambió", texto 12, botón de 32). Esc la cierra igual que "Entendido", después de cerrar un panel abierto.
- Renombrar skill (si existe el flujo): diálogo con estilo del mock y "Aplicar a todas las variantes" marcado por defecto (se aplica al ID base).


## Corrección de fidelidad visual

La revisión posterior de los HTML originales corrige las sustituciones tipográficas y diferencias de geometría, estados y avisos. Alcance, diferencias intencionales y evidencia local: [fidelity.md](fidelity.md). Los tamaños del mock se comprueban con las fuentes originales; una suite funcional aprobada por sí sola no certifica equivalencia visual completa.
