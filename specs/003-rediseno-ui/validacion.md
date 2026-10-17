# Validación del rediseño

Validación local del 17/10/2026 en Windows, .NET 10. Las cinco pistas están integradas en `feature/rediseno-overlay`.

- Compilación Release con advertencias como errores: 0 errores, 0 advertencias.
- Suite completa: 316 pruebas aprobadas (App 187, Capture 6, Combat 53, Protocol 47, Storage 23).
- Arnés WPF: 57 capturas generadas fuera de pantalla, con estados de las cinco pistas, errores, tamaños mínimos y columnas desplazadas. Revisión visual de las pantallas principales y sus estados límite.
- Revisión independiente de las pistas y de sus cruces: sin hallazgos P1/P2 pendientes.
- Se verificaron regresiones de notas del historial, progreso con filtros, registro de la pelea elegida, tiempos de muerte, snapshot final desde pausa, exportación sin decoraciones temporales y migración de preferencias.

Las compilaciones y pruebas se ejecutaron de a una. No se ejecutó la aplicación ni se inició captura de red. No hay cambios en las capas Capture/Protocol ni en las herramientas de extracción/registro.

Publicación en cinco PR, cada uno con su número real en `data/novedades.json`:

- [#71 Historial](https://github.com/Yaizsk0/HysteriaMeter/pull/71).
- [#72 Configuración](https://github.com/Yaizsk0/HysteriaMeter/pull/72).
- [#73 Inicio](https://github.com/Yaizsk0/HysteriaMeter/pull/73).
- [#74 Detalle](https://github.com/Yaizsk0/HysteriaMeter/pull/74).
- [#75 Overlay e integración](https://github.com/Yaizsk0/HysteriaMeter/pull/75).

El procedimiento de merge comprueba que el HEAD coincida con el validado y que todos sus checks hayan terminado con éxito. Tras cada squash, la siguiente rama se reubica sobre `main`; se comprueba que el código coincida con la versión validada localmente.
