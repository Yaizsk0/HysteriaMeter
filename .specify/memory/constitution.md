<!--
Sync Impact Report
- Versión: (plantilla) → 1.0.0
- Principios agregados: I–VI (todos nuevos)
- Secciones agregadas: Restricciones técnicas, Flujo de desarrollo, Gobierno
- Plantillas revisadas: plan-template.md ✅ (Constitution Check usa estos principios), spec-template.md ✅, tasks-template.md ✅
- TODOs pendientes: ninguno
-->

# Hysteria Meter Constitution

## Core Principles

### I. Solo lectura pasiva (NO NEGOCIABLE)

Hysteria Meter observa copias del tráfico de red y nada más.

- NUNCA envía, inyecta, reenvía ni modifica paquetes. No se usa `IInjectionDevice` ni equivalentes.
- NUNCA abre, lee ni escribe la memoria del proceso del juego, ni carga DLLs en él.
- NUNCA modifica archivos del juego ni automatiza acciones (macros, inputs).
- Identificar la conexión del juego se hace con información pública del sistema operativo (tabla TCP, como `netstat`), no inspeccionando el proceso.

**Por qué**: es lo que mantiene el riesgo frente al anti-cheat y la Operation Policy de NCSOFT en el mismo nivel que los meters de la comunidad. Cualquier cambio que roce este principio se rechaza.

### II. Capas con dependencias en un solo sentido

`Capture → Protocol → Combat → (Storage, App)`.

- **Capture** convierte tramas en bytes TCP ordenados. No sabe nada del juego.
- **Protocol** convierte bytes en eventos tipados (`CombatEvent`). No sabe de UI ni de agregados.
- **Combat** es un modelo puro, sin I/O: eventos entran, snapshots salen.
- **App** compone todo; ninguna capa inferior referencia a una superior.

### III. Datos del protocolo en configuración

Los opcodes cambian con los parches. Viven en `data/opcodes.json` (con varios valores por tipo para que convivan versión vieja y nueva), no en el código. Un parche del juego idealmente se arregla editando ese archivo; si hace falta tocar un decoder, el cambio queda acotado a `src/HysteriaMeter.Protocol/Decoding/`.

### IV. Replay primero y tests con bytes reales

- Toda funcionalidad de protocolo o combate debe poder ejecutarse sobre un `.pcap` grabado, sin el juego abierto.
- Cada decoder nuevo trae al menos un test con bytes copiados de una captura real (o, mientras no existan, sintéticos marcados como tales).
- `dotnet test` en verde es condición para mergear.

### V. Robustez ante lo desconocido

Un paquete que no se entiende no tumba el meter: se cuenta en `DecoderStats` y se sigue. Los huecos de TCP disparan una resincronización, no una excepción. Los contadores (opcodes desconocidos, errores, desincronizaciones, descartes) son visibles para diagnosticar.

### VI. Simplicidad

Se agrega una dependencia, una capa o una abstracción solo cuando el caso concreto lo pide. Sin servicios en la nube ni cuentas: todo local.

## Restricciones técnicas

- .NET 10 (LTS), C# `latest`, `Nullable` habilitado, `TreatWarningsAsErrors`, analizadores `latest-recommended`.
- Versiones de NuGet centralizadas en `Directory.Packages.props`.
- Plataforma de ejecución: Windows 10/11 x64 con Npcap (modo compatible WinPcap). Las librerías no-UI compilan y se testean también en Linux.
- UI: WPF + CommunityToolkit.Mvvm. Persistencia: SQLite local.
- Licencia: si se incorporan opcodes o tablas de proyectos GPL-3.0 (A2Tools y derivados), el proyecto pasa a GPL-3.0-or-later antes de distribuirse.

## Flujo de desarrollo

- Spec-driven con Spec Kit: `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`. Cada feature vive en `specs/NNN-nombre/`.
- Una rama por feature; PR con `dotnet build` y `dotnet test` en verde (CI en `windows-latest`).
- Las tareas completadas se marcan `[X]` en `tasks.md` dentro del mismo PR.

## Governance

Esta constitución tiene prioridad sobre cualquier otra práctica del repo. Cambiarla requiere un PR que actualice este archivo, suba la versión (MAJOR si se quita o redefine un principio, MINOR si se agrega uno, PATCH para aclaraciones) y revise las plantillas en `.specify/templates/`. Todo plan debe pasar el "Constitution Check" contra estos principios; las excepciones se justifican en la tabla "Complexity Tracking" del plan.

**Version**: 1.0.0 | **Ratified**: 2026-10-14 | **Last Amended**: 2026-10-14
