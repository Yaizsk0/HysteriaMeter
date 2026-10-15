# Hysteria Meter: roadmap

Fecha: 2026-10-15. Sale de lo que Mati marcó en la página de features del proyecto: 103 sí y 1 no (OV-12, modo streamer). El detalle de cada ID está en [features.md](./features.md).

103 features no entran en una sola spec de Spec Kit, así que las agrupé en features chicas, cada una entregable por separado y en orden de dependencia. Cada una sería su propia carpeta `specs/NNN-.../` con spec, plan y tasks.

## Orden propuesto

| # | Feature de Spec Kit | IDs | Depende de |
|---|---|---|---|
| 001 | **MVP** (en curso: protocolo real en el PR #1, party, overlay básico, historial básico) | (ya especificada) | — |
| 002 | **Precisión del combate**: crítico y demás flags, curación, daño recibido, invocaciones, DoT, DPS activo, overkill, el ~8 % del local, auto-verificación, nombres de la party, jugador local | MT-01, MT-02, MT-03, MT-04, MT-05, MT-06, MT-07, MT-10, MT-11, PT-01 | 001 |
| 003 | **Robustez y herramientas de protocolo**: aviso de parche, opcodes versionados, diagnóstico, desyncs, regresión con capturas reales, diff de capturas, buscador de valores, marcadores, visor y plugin de Wireshark, privacidad local | RB-01, RB-02, RB-04, RB-06, RB-07, DV-01, DV-02, DV-03, DV-04, DV-05, PV-01, PV-03 | 001 (conviene hacerla en paralelo con 002, porque las herramientas DV aceleran 002) |
| 004 | **Overlay completo con estilo AION**: modos compacto/expandido, colores por clase, columnas, mi fila, objetivo vs encuentro, HP del boss y enrage, sparklines, opacidad, auto-ocultar, varios overlays, click a detalle, estado, atajos, reset, inactividad, temas, bandeja, formato de números, perfiles, detección de boss, kill/wipe, adds | OV-01…OV-11, OV-13, UX-02, UX-03, UX-04, UX-05, UX-07, UX-08, UX-09, AL-03, BS-01, BS-04, BS-05 | 002 (crit, clase, local); mocks del hilo de UI |
| 005 | **Detalle de pelea e historial**: desglose por skill, gráficos, timeline de casts, log de eventos, muertes, nombres de skills, comparar jugadores, lista y filtros de encuentros, runs de dungeon, récords, progreso, comparar peleas, varios personajes, notas, guardar y reprocesar `.pcap`, retención, tabla de NPCs, fases | DT-01…DT-07, HS-01…HS-11, BS-02, BS-03 | 002, 004 |
| 006 | **Analizador de rotaciones**: rotación, tiempo muerto, cooldowns, burst, rotación de referencia, opener, DoTs, recomendaciones, puntuación, comparar contra mejor pelea y contra otro jugador, modo muñeco, experimentos A/B, aviso de CD sin usar | RT-01…RT-13, AL-01 | 005 (historial) |
| 007 | **Buffs, roles y alertas**: uptime de buffs, rDPS, vista healer, vista tank, detección de rol, buff caído, timers del boss, sonidos | MT-08, MT-09, PT-02, PT-03, PT-05, AL-02, AL-04, AL-05 | 002 (paquetes de buffs), 006 |
| 008 | **Exportar y compartir**: resumen al portapapeles, imagen, CSV/JSON, webhook de Discord, archivo `.hmlog`, anonimizar, asistencia de la legión | EX-01…EX-05, PV-02, PT-04 | 005 |
| 009 | **Distribución**: instalador, auto-actualización, releases firmadas, portable, primer arranque guiado, idiomas, actualizar `opcodes.json` sin reinstalar | DS-01…DS-04, UX-01, UX-06, RB-03, RB-05 | 004 |
| 010 | **Sitio web de logs** (producto aparte) | EX-06 | 008 |

Cobertura: los 103 IDs aprobados aparecen exactamente una vez en la tabla.

## Por qué este orden

- **002 primero**: las tareas de protocolo pendientes (bit de crítico, jugador local, nombres de la party, curación, invocaciones) destraban casi todo lo demás. Sin crítico no hay desglose completo ni puntuación de rotación.
- **003 en paralelo**: el buscador de valores y el diff de capturas son justamente lo que hace falta para encontrar el bit de crítico y la curación; la suite de regresión protege el trabajo ante parches.
- **004 después de 002**: el overlay nuevo necesita clase, crítico y jugador local, y va a usar los mocks aprobados.
- **006 después de 005**: el analizador compara contra el historial, así que el historial tiene que existir antes.
- **010 al final**: es un servicio web con hosting y privacidad propios; conviene decidirlo con todo lo demás andando.

## Próximo paso

Escribir `specs/002-precision-combate/` con Spec Kit (spec, plan, tasks) en una rama nueva del repo, mientras el PR #1 sigue su curso.
