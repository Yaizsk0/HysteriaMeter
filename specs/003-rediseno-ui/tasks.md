# Tareas del rediseño

Alcance aprobado: [spec.md](spec.md) y [decisiones.md](decisiones.md). Coordinación y decisiones de integración: [plan.md](plan.md).

- [X] Recuperar las cinco pistas y revisiones de Claude, preservando commits y cambios locales.
- [X] Historial: ventana independiente, filtros/runs/progreso, persistencia de notas, recálculo al borrar, actualización al guardar y cierre con el overlay.
- [X] Historial: comprobar compilación, suite completa y capturas del arnés.
- [X] Configuración: integrar navegación, todas las opciones, selector de red y conteos locales; combinar ciclo de vida con Historial.
- [X] Configuración: comprobar compilación, suite completa y capturas de las ocho secciones y estados límite.
- [X] Detalle: integrar skills/muertes/log/comparación conservando datos y acciones.
- [X] Detalle: comprobar compilación, suite completa y capturas de sus estados.
- [X] Inicio: integrar bienvenida/tarjeta/bandeja/novedades con datos y acciones reales.
- [X] Inicio: comprobar compilación, suite completa y capturas de sus estados.
- [X] Overlay: completar cabecera/filas/objetivo/estados/avisos/pastilla y conexiones del modelo.
- [X] Overlay: comprobar compilación, suite completa y capturas al tamaño normal y mínimo.
- [X] Integración: actualizar README y validar la combinación de ventanas, preferencias, atajos y temas.
- [X] Publicación: agregar novedades con el número real de cada PR (#71–#75).
- [X] Integración: preparar los cinco PR y completar su revisión; comprobar CI verde sobre el último commit como condición de cada merge.
- [X] Paso 7, detalles menores: enrage con una sola capa de AlertBg, cruz y flecha siempre visibles con nombres largos (también en pausa), Esc en la pastilla, barra de novedades solo cuando hace falta, comentario de WarningLine y vuelta después de una muerte sin contar el DoT.
- [X] Tests sin el OnStartup de la App (arnés de capturas e HistoryWindowTests incluidos): no tocan la base, las ventanas, las preferencias ni el inicio con Windows del usuario.
- [X] ACTIVO y TIEMPO MUERTO sin los ticks de DoT (`FightActivity.Of` con la serie directa): KPI del detalle, fila "Activo" de Comparar y consejo de tiempo muerto.
- [X] Datos del juego: un solo `GameDataSummary` (el de Config) para Configuración y la bienvenida; íconos y emblemas cuentan solo los `<id>.png` que carga la app.
- [X] Overlay: la pelea guardada (etiqueta, ‹ ›, "Volver al vivo" y nota) se repinta al cambiar el tema y "Borrar pelea" usa ButtonDangerLink; OverlayWindow.xaml sin StaticResource de colores del tema.
- [X] DPS activo sin los ticks de DoT (`FightActivity.ActiveDps`, el mismo tiempo activo que ACTIVO y TIEMPO MUERTO, medido como el DPS y sin el daño de los huecos muertos ni el de después del último golpe): "–" si todo fue DoT, tooltip al criterio nuevo y el consejo del DPS perdido solo después del de tiempo muerto y sobre esos segundos; las peleas guardadas no se recalculan.
- [X] Bienvenida: la placa de red con la descripción de Npcap (`CaptureDevices.Describe`, la misma búsqueda que la captura) y de dónde salió: "elegida en la configuración" solo si es la guardada aplicada, "fijada en appsettings.json" si es `Capture:Device`; las placas se listan solo si hay una fija en vivo.
- [X] Overlay: el menú "⋯" ya no cierra la app al abrirlo (sin ItemContainerStyle de MenuItem, que WPF también aplica a los Separator) y los separadores toman su línea del diseño con MenuItem.SeparatorStyleKey.
- [X] Detalle: columnas PERF., DOBLE, FRENTE, ESPALDA y MULTI en la tabla de skills y en TOTAL, como Combat Analysis; ventana de 1120 y las cinco ocultas por debajo de 1040.
- [X] Overlay: en modo compacto la franja muestra el objetivo y su % de vida durante la pelea, recortado antes de las acciones.
- [X] Datos: `data/npcs.json` con los bosses de las peleas de referencia (2300218 Divine Auldor, Urugugu Canyon; 2101106 Destroyer Tarka), con el nombre de Combat Analysis, marcados como boss y sin enrage.
- [X] Configuración: escala de la interfaz (100–150 %) para todas las ventanas y sus popups, en vivo; el overlay la suma a su zoom y guarda su tamaño sin ella; el perfil exportado no la lleva.
- [X] Escala: los desplegables y el submenú del "⋯" ya no se escalan dos veces (heredan la de su ventana) y el overlay que crece cerca del borde se corre para no salirse del monitor.
- [X] Configuración → Apariencia: "Volver a la vista de fábrica" deja las filas como el diseño (DPS y %, colores por clase, sin línea de DPS, item level ni columnas extra, y no solo tu fila) y saca del compacto a esa ventana; no toca tema, fuente, opacidad, alto de fila, atajos ni captura.
- [X] Historial: la cabecera queda en una línea con el filtro de personaje y en el ancho mínimo; se achica la búsqueda en vez de bajar el filtro abajo del título.
- [X] Escala: el menú de la bandeja (WinForms, sin LayoutTransform) agranda su fuente, el relleno de los ítems, el separador y los márgenes y el radio del resaltado con la escala de la interfaz, cada vez que se abre.
- [X] Combat: el filtro de party cuenta a quien estuvo en la party en esta zona aunque no mande HP/MP hace más de 10 s; ya no se pierde su daño (la apertura, por ejemplo).
- [X] Overlay: lo guardado antes del rediseño pasa una sola vez a la vista del mock al abrir (filas y columnas de fábrica, ventanas sin compacto a 380 px); lo que se vuelva a prender después se respeta y un perfil importado no repite el paso.
