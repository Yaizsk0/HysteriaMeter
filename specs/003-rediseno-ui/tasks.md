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
