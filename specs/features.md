# Hysteria Meter: features, mejoras y tareas posibles

Fecha: 2026-10-14 · Estado: Mati eligió el 2026-10-15 (103 sí, 1 no: OV-12). Roadmap en [roadmap.md](./roadmap.md).

## Cómo usar esta lista

- Cada ítem tiene un ID (`OV-01`, `RT-03`...) para poder referirlo en el hilo o en una spec de Spec Kit.
- Marcá `[x]` lo que querés y cambiá a `[-]` lo que descartás. Lo que quede `[ ]` sigue sin decidir.
- **Esfuerzo**: S (horas), M (días), L (semanas).
- **Datos**: ✅ ya los tenemos decodificados (PR #1) · 🔶 falta decodificar algo del protocolo · 🔷 no depende del protocolo.
- Todo respeta el principio I de la constitución: solo lectura pasiva. Lo que lo violaría está al final, en "Descartadas".

---

## 1. Overlay en vivo (OV)

- [x] **OV-01 Modo compacto y modo expandido**: una barra mínima (mi DPS y tiempo) y la tabla completa de la party; se alterna con un atajo. *S · 🔷*
- [x] **OV-02 Barras de color por clase** en cada fila, estilo ranking de AION. *S · 🔶 (clase en `0x3645`)*
- [x] **OV-03 Columnas configurables**: DPS, total, %, golpes, crit %, golpe máximo, HPS, muertes; elegir y ordenar. *M · ✅/🔶 (crit, heal)*
- [x] **OV-04 Mi fila resaltada y fijada** aunque no esté en el top N. *S · 🔶 (identificar jugador local)*
- [x] **OV-05 Vista "objetivo actual" vs "todo el encuentro"**: DPS contra el boss solo, o contra todo lo que se pegó. *M · ✅*
- [x] **OV-06 Barra de HP del boss** con % restante, tiempo estimado para matarlo (TTK) y DPS de la party necesario para el enrage. *M · ✅ (`0x8D00`, `0x3641`)*
- [x] **OV-07 Sparkline de DPS** de los últimos 30 s en cada fila. *S · ✅*
- [x] **OV-08 Opacidad, escala y tamaño de fuente** configurables; posición recordada por monitor. *S · 🔷*
- [x] **OV-09 Ocultar solo** cuando el juego no está en foco o no hay combate por X segundos. *S · 🔷*
- [x] **OV-10 Varios overlays a la vez**: uno de daño, uno de curación, uno de daño recibido. *M · 🔶*
- [x] **OV-11 Click en una fila** abre el detalle de ese jugador sin cortar la pelea. *S · ✅*
- [-] **OV-12 Modo streamer**: overlay como fuente de navegador para OBS (página local) y nombres ocultables. *M · 🔷*
- [x] **OV-13 Indicador de estado**: capturando / sin tráfico / protocolo desconocido tras parche. *S · 🔷*

## 2. Métricas y precisión (MT)

- [x] **MT-01 Crítico, golpe perfecto, doble, frontal, de espalda, multi-golpe** (lo que desglosa Combat Analysis: CRI, PERF, DOUB, FRNT, BACK, MULT). *M · 🔶 (bits de `0x3804`)*
- [x] **MT-02 Curación (HPS)** separando sobrecuración si el HP máximo lo permite. *M · 🔶 (`0x3805` 0x09/0x0B sin validar)*
- [x] **MT-03 Daño recibido (DTPS)** por jugador y por skill del boss. *M · ✅ (mismo `0x3804` con objetivo jugador)*
- [x] **MT-04 Daño de invocaciones atribuido al dueño**, con opción de verlo separado. *M · 🔶 (invocaciones sin dueño)*
- [x] **MT-05 DoT separado del daño directo** por skill. *S · ✅*
- [x] **MT-06 DPS activo vs DPS real**: descontando el tiempo muerto o sin objetivo. *S · ✅*
- [x] **MT-07 Overkill** separado (el golpe final que pasa el HP restante). *S · ✅*
- [x] **MT-08 Uptime de buffs y debuffs** propios y sobre el boss. *L · 🔶 (paquetes de buff)*
- [x] **MT-09 Contribución por buffs ("rDPS")**: cuánto daño extra generó el buff de un support. *L · 🔶*
- [x] **MT-10 Cerrar el ~8 % faltante del jugador local** (sospecha `0x3806`). *M · 🔶*
- [x] **MT-11 Auto-verificación**: comparar el total contra la vida que perdió el boss y avisar si no cierra. *S · ✅*

## 3. Ventana de detalle de la pelea (DT)

- [x] **DT-01 Desglose por skill**: total, %, golpes, promedio, máximo, crit %, DoT, casts por minuto. *M · ✅/🔶*
- [x] **DT-02 Gráfico de DPS en el tiempo** por jugador, con fases del boss marcadas. *M · ✅*
- [x] **DT-03 Línea de tiempo de casts** (estilo "timeline" de WarcraftLogs): cada skill como un bloque por jugador. *M · ✅ (`0x3802`)*
- [x] **DT-04 Log de eventos filtrable**: quién, a quién, skill, monto, flags; buscar y exportar. *M · ✅*
- [x] **DT-05 Muertes**: qué te mató, los últimos 10 s de daño recibido antes de morir. *M · ✅ (`0x8D04` + `0x3804`)*
- [x] **DT-06 Íconos y nombres reales de skills** en vez de IDs. *M · 🔶 (tabla skillId → nombre propia, armada a mano o desde capturas)*
- [x] **DT-07 Comparar dos jugadores lado a lado** en la misma pelea. *S · ✅*

## 4. Analizador de rotaciones y recomendaciones (RT)

- [x] **RT-01 Reconstrucción de la rotación**: secuencia de skills con tiempos entre casts y huecos sin castear. *M · ✅ (`0x3802`)*
- [x] **RT-02 Detección de "tiempo muerto"**: segundos sin castear con objetivo vivo, y en qué momentos de la pelea. *S · ✅*
- [x] **RT-03 Uso de cooldowns**: cuántas veces usaste cada skill vs cuántas podías según su CD; "dejaste el CD X 40 s sin usar". *M · 🔶 (CDs: tabla propia por clase o inferidos del mínimo entre casts)*
- [x] **RT-04 Alineación de burst**: si los cooldowns grandes coincidieron con buffs propios, de party o ventanas de daño aumentado del boss. *L · 🔶*
- [x] **RT-05 Rotación de referencia por clase**: armar (o importar) una prioridad/opener y medir cuánto te desviaste. *L · 🔷 + ✅*
- [x] **RT-06 Opener**: comparar los primeros 15 s contra tu mejor opener registrado. *M · ✅*
- [x] **RT-07 Mantenimiento de DoTs y debuffs**: uptime y recasts antes de tiempo (clipping). *M · ✅ (DoT) / 🔶 (debuffs)*
- [x] **RT-08 Recomendaciones en lenguaje natural** al terminar la pelea: 3 a 5 cosas concretas ("tu skill X hizo 12 % menos que en tu mejor pelea porque la usaste 3 veces menos"). Reglas locales primero; opcional, resumen con LLM. *M (reglas) / M (LLM) · ✅*
- [x] **RT-09 Puntuación de la pelea** (0–100) por actividad, uso de CDs y DPS contra tu propio récord en ese boss. *M · ✅*
- [x] **RT-10 Comparar rotación contra tu mejor pelea** del mismo boss: dónde divergieron los casts, segundo a segundo. *M · ✅*
- [x] **RT-11 Comparar contra otro jugador de la misma clase** que estuvo en tu party (con su consentimiento si se comparte). *M · ✅*
- [x] **RT-12 Sesión de práctica contra muñeco**: modo "dummy" que mide 1, 3 o 5 minutos fijos para probar rotaciones y equipo. *S · ✅*
- [x] **RT-13 Experimentos A/B**: etiquetar peleas ("con piedra X", "rotación nueva") y comparar promedios con la variación entre peleas. *M · ✅*

## 5. Historial y comparación (HS)

- [x] **HS-01 Lista de encuentros** con fecha, zona, boss, duración, resultado (kill/wipe), mi DPS y el de la party. *M · ✅*
- [x] **HS-02 Filtros y búsqueda**: por boss, dungeon, personaje, fecha, etiqueta, kill/wipe. *S · 🔷*
- [x] **HS-03 Agrupar por "run" de dungeon**: todos los bosses de una entrada juntos, con tiempo total. *M · ✅ (cambio de zona + kills)*
- [x] **HS-04 Récords personales** por boss y por skill (mejor DPS, golpe máximo), con aviso al batirlos. *S · ✅*
- [x] **HS-05 Progreso en el tiempo**: gráfico de mi DPS en un boss a lo largo de semanas, con marcas de cambios de equipo o parche. *M · ✅*
- [x] **HS-06 Comparar 2 a 4 peleas lado a lado**: totales, por skill, gráfico superpuesto de DPS, rotación. *M · ✅*
- [x] **HS-07 Varios personajes** (Belzebubs, Griffith...): historial y récords separados por personaje. *S · 🔶 (nombre del jugador local)*
- [x] **HS-08 Notas y etiquetas** por pelea. *S · 🔷*
- [x] **HS-09 Guardar el `.pcap` crudo** de peleas importantes para re-analizarlas con decoders nuevos después de un parche. *S · 🔷*
- [x] **HS-10 Re-procesar historial** cuando mejora el decoder (por ejemplo, cuando se descubre el bit de crítico). *M · 🔷 (requiere HS-09)*
- [x] **HS-11 Retención y limpieza**: borrar peleas de trash viejas, conservar las de boss. *S · 🔷*

## 6. Party, roles y legión (PT)

- [x] **PT-01 Nombres de toda la party** aunque no lleguen en `0x8D04`. *M · 🔶*
- [x] **PT-02 Vista de healer**: HPS, sobrecuración, cleanse, a quién curó. *L · 🔶*
- [x] **PT-03 Vista de tank**: daño recibido, mitigado, tiempo con aggro. *L · 🔶*
- [x] **PT-04 Asistencia de la legión (Hysteria)**: quién vino a qué run, estadísticas por miembro a lo largo del tiempo. *M · ✅*
- [x] **PT-05 Detección de rol** automática por skills usados. *M · 🔶*

## 7. Alertas y timers informativos (AL)

Solo muestran información; no presionan teclas ni actúan sobre el juego.

- [x] **AL-01 Aviso de cooldown sin usar** ("X listo hace 10 s") en el overlay. *M · 🔶 (CDs)*
- [x] **AL-02 Timers de mecánicas del boss** a partir de casts del boss vistos en peleas anteriores. *L · ✅ (`0x3802` del boss)*
- [x] **AL-03 Aviso de enrage** según HP restante y DPS actual. *S · ✅*
- [x] **AL-04 Aviso de buff caído** propio. *M · 🔶*
- [x] **AL-05 Sonidos opcionales** para cada alerta. *S · 🔷*

## 8. Bosses, dungeons y zonas (BS)

- [x] **BS-01 Detección automática de boss** (HP máximo alto, ID de NPC conocido) para nombrar el encuentro. *M · ✅ (`0x3641`)*
- [x] **BS-02 Tabla editable de NPCs** (`npcId → nombre, dungeon, es boss`) en `data/`, como los opcodes. *S · 🔷*
- [x] **BS-03 Fases del boss** por umbrales de HP o cambios de skill. *M · ✅*
- [x] **BS-04 Kill vs wipe** detectado (boss muerto o party muerta). *S · ✅*
- [x] **BS-05 Separar adds del boss** en el DPS. *M · ✅*

## 9. Exportar y compartir (EX)

- [x] **EX-01 Copiar resumen al portapapeles** con formato para el chat del juego o Discord. *S · ✅*
- [x] **EX-02 Imagen de la tabla** (screenshot limpio del meter) para pegar en Discord. *S · ✅*
- [x] **EX-03 Exportar CSV / JSON** de una pelea o del historial. *S · ✅*
- [x] **EX-04 Webhook de Discord** opcional al matar un boss (canal de la legión). *S · ✅*
- [x] **EX-05 Archivo de pelea compartible** (`.hmlog`) que otro Hysteria Meter abre y analiza. *M · ✅*
- [x] **EX-06 Sitio web propio de logs** (estilo abysslogs/WarcraftLogs) para la legión. Grande y con temas de privacidad. *L · ✅*

## 10. Configuración y experiencia (UX)

- [x] **UX-01 Primer arranque guiado**: detectar Npcap, elegir placa (Wi-Fi, VPN, ExitLag), probar captura. *M · 🔷*
- [x] **UX-02 Atajos globales configurables** (bloquear, reset, cambiar vista, marcar momento). *S · 🔷*
- [x] **UX-03 Reset manual del encuentro** y "dividir aquí". *S · 🔷*
- [x] **UX-04 Tiempo de inactividad configurable** por tipo de contenido (campo vs boss). *S · 🔷*
- [x] **UX-05 Temas visuales**: estilo AION (por defecto), oscuro minimalista, alto contraste. *M · 🔷 (lo trabaja el hilo de mocks)*
- [x] **UX-06 Idiomas**: español, inglés, coreano. *M · 🔷*
- [x] **UX-07 Icono en la bandeja del sistema** con menú rápido. *S · 🔷*
- [x] **UX-08 Formato de números** (2,04M / 2.040.321 / 2.04M). *S · 🔷*
- [x] **UX-09 Perfil por personaje** de layout y columnas. *S · 🔶*

## 11. Robustez ante parches y diagnóstico (RB)

- [x] **RB-01 Detección de parche**: si aparecen muchos opcodes desconocidos o fallan decoders, avisar "el juego se actualizó, el meter puede fallar". *S · 🔷*
- [x] **RB-02 Opcodes con varias versiones** en `opcodes.json` para que convivan viejo y nuevo. *S · 🔷*
- [x] **RB-03 Actualizar `opcodes.json` desde el repo** sin reinstalar (descarga manual o al iniciar, con confirmación). *M · 🔷*
- [x] **RB-04 Panel de diagnóstico**: frames/s, desconocidos, errores, desyncs, bytes descartados. *S · 🔷*
- [x] **RB-05 Reporte de bug con un click**: empaqueta logs y los últimos minutos de `.pcap` (solo servidor → cliente). *M · 🔷*
- [x] **RB-06 Recuperación de desyncs del framer** (los 3 de la dungeon). *M · 🔷*
- [x] **RB-07 Suite de regresión con capturas reales** en CI: cada pelea grabada con su total esperado de Combat Analysis. *M · 🔷*

## 12. Herramientas para seguir decodificando (DV)

- [x] **DV-01 Diff de opcodes entre dos capturas** (antes/después de un parche, con/sin un buff). *M · 🔷*
- [x] **DV-02 Buscador de valores**: "encontrá en qué paquetes aparece el número 12.345" (para mapear crit, heal, buffs). *S · 🔷*
- [x] **DV-03 Marcadores durante la grabación**: atajo que inserta "acá usé skill X" en la captura. *S · 🔷*
- [x] **DV-04 Visor de paquetes** con layout aplicado y campos coloreados (estilo Wireshark dissector). *M · 🔷*
- [x] **DV-05 Plugin de Wireshark** (Lua) generado desde nuestros layouts. *M · 🔷*

## 13. Distribución (DS)

- [x] **DS-01 Instalador** (MSIX o Velopack) con chequeo de Npcap. *M · 🔷*
- [x] **DS-02 Auto-actualización** opcional, con changelog. *M · 🔷*
- [x] **DS-03 Releases firmadas en GitHub** desde CI. *M · 🔷*
- [x] **DS-04 Modo portable** (sin instalar, todo en una carpeta). *S · 🔷*

## 14. Privacidad (PV)

- [x] **PV-01 Todo local por defecto**: nada sale de la PC salvo lo que actives (webhook, sitio). *S · 🔷*
- [x] **PV-02 Anonimizar nombres** al exportar o compartir. *S · 🔷*
- [x] **PV-03 No guardar el tráfico cliente → servidor** (ya no lo decodificamos; tampoco hace falta grabarlo). *S · 🔷*

---

## Tareas técnicas pendientes (habilitan muchas features)

Ordenadas por cuántas features destraban.

1. **Bit de crítico y demás flags de `0x3804`** (MT-01, DT-01, RT-09). Método: pelea grabada + Combat Analysis.
2. **Identificar al jugador local de forma confiable** y su nombre (OV-04, HS-07, UX-09).
3. **Nombres y clase de toda la party** (PT-01, OV-02, RT-11).
4. **El ~8 % faltante del local** (`0x3806`) (MT-10).
5. **Dueño de invocaciones sin nombre** (MT-04).
6. **Validar curación `0x3805` 0x09/0x0B** (MT-02, PT-02).
7. **Paquetes de buffs/debuffs** (MT-08, MT-09, RT-04, RT-07, AL-04).
8. **Tabla propia de skills y NPCs** (`skillId`, `npcId` → nombre) armada desde nuestras capturas, no desde Abyss (DT-06, BS-02).
9. **Cooldowns por skill** inferidos del mínimo entre casts en muchas peleas (RT-03, AL-01).
10. **Reducir desyncs del framer** (RB-06).

## Corte sugerido

Solo es una sugerencia para arrancar; decidís vos.

- **Siguiente versión (MVP+)**: OV-01, OV-04, OV-06, MT-01, MT-05, MT-11, DT-01, DT-02, HS-01, HS-04, HS-06, BS-01, BS-04, EX-01, RB-01, RB-04, tareas 1 a 3.
- **Analizador v1**: RT-01, RT-02, RT-06, RT-08 (solo reglas), RT-10, RT-12, HS-09, HS-10.
- **Después**: buffs, healer/tank, timers del boss, sitio de logs.

## Descartadas por la constitución

No se pueden hacer sin violar el principio I (solo lectura pasiva) o los términos del juego:

- Leer memoria del juego para obtener datos que no vienen en los paquetes (por ejemplo, cooldowns exactos o buffs ocultos).
- Cualquier automatización: presionar teclas, macros, auto-rotación, "auto-pot".
- Enviar, modificar o inyectar paquetes (incluido pedir datos al servidor).
- Mostrar información que el juego oculta deliberadamente para dar ventaja (posición de enemigos fuera de vista en PvP, HP de jugadores rivales ocultos, etc.). Conviene limitar el meter a PvE o a datos que el propio juego ya muestra.
- Reutilizar datos u opcodes extraídos de Abyss DPS Meter (cerrado y sin licencia).
