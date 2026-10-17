# Validación del rediseño

Validación local del 17/10/2026 en Windows, .NET 10. Las cinco pistas están integradas en `feature/rediseno-overlay`.

- Compilación Release con advertencias como errores: 0 errores, 0 advertencias.
- Suite completa: 316 pruebas aprobadas (App 187, Capture 6, Combat 53, Protocol 47, Storage 23).
- Arnés WPF: 57 capturas generadas fuera de pantalla, con estados de las cinco pistas, errores, tamaños mínimos y columnas desplazadas. Revisión visual de las pantallas principales y sus estados límite.
- Revisión independiente de las pistas y de sus cruces: sin hallazgos P1/P2 pendientes.
- Se verificaron regresiones de notas del historial, progreso con filtros, registro de la pelea elegida, tiempos de muerte, snapshot final desde pausa, exportación sin decoraciones temporales y migración de preferencias.

Las compilaciones y pruebas se ejecutaron de a una. No se ejecutó la aplicación ni se inició captura de red. No hay cambios en las capas Capture/Protocol ni en las herramientas de extracción/registro.

La publicación y el CI remoto están pendientes. Las ramas locales separan Historial, Configuración, Inicio, Detalle y Overlay; cada PR debe incorporar su número real en `data/novedades.json` antes de integrarse.
