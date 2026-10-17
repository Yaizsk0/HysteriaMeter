# Continuación del rediseño de interfaz

Diseño: [decisiones.md](decisiones.md), recuperado de la sesión de Claude del 16/10/2026. La base visual quedó integrada en #70.

## Plan y estado

- [X] Recuperar las cinco pistas y sus revisiones sin perder cambios locales.
- [X] Historial: integrar ventana, corregir notas/progreso/visibilidad y verificar con SQLite y capturas.
- [ ] Detalle: integrar skills, muertes, log y comparación; revisar regresiones y capturas.
- [X] Configuración: terminar navegación, selector de placa y preferencias; verificar opciones existentes.
- [ ] Inicio: terminar bienvenida, tarjeta de kill y bandeja; verificar capturas y datos reales.
- [ ] Overlay: completar XAML y conexiones del modelo, estados, avisos y pastilla.
- [ ] Integración: un PR por mejora, README y novedades, compilación y tests; integrar solo con CI verde.

## Coordinación

Las pistas parten de a43bf1b, equivalente al squash bcf67fc (#70). Se recuperan solamente sus cambios posteriores.
Las compilaciones y pruebas locales se ejecutan de a una. No se ejecuta la app ni se inicia captura de red: la revisión visual usa el arnés fuera de pantalla.
MeterViewModel, OverlayWindow, preferencias y el arnés son archivos compartidos: conservar el comportamiento combinado al resolver conflictos.

## Decisiones de integración

- El progreso por boss toma todos los intentos del personaje; los chips eligen las filas visibles, sin cambiar el denominador de kills.
- Antes de cambiar una pelea se guarda la nota editada; las ventanas auxiliares se cierran al minimizar o bloquear el overlay y se ocultan con él.
- Las ramas originales de Claude se conservan como recuperación; las correcciones y PR se preparan en un worktree de integración.
