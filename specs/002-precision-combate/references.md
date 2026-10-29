# Peleas de referencia

Capturas grabadas en la PC de Mati (`captures/`, fuera del repo) con su Combat Analysis (Ctrl+X) como verdad.

| # | Fecha | Pelea | Tipo | Duración | Daño local (juego) | Daño local (meter) | Captura |
|---|---|---|---|---|---|---|---|
| 1 | 2026-10-14 | Divine Auldor (Urugugu Canyon) | Dungeon en party de 5 | 02:25 | 2.040.000 (14,02K DPS) | 2.040.321 (directo + DoT) | `dungeon-20261014-184438.pcap` |
| 2 | 2026-10-15 | Destroyer Tarka | Mini boss de evento, solo | 00:27 | 648.468 (23.453 DPS), 92 golpes, 51 CRI, 5 PERF, 5 DOUB, 62 FRNT, 25 BACK, 23 MULT | 648.468, todas las columnas exactas por skill | `miniboss-20261015-103328.pcap` |

Para validarlas, con las capturas en la PC: `$env:HYSTERIA_CAPTURES = '<ruta absoluta de la carpeta de las capturas>'; dotnet test tests/HysteriaMeter.Protocol.Tests --filter FullyQualifiedName~ReferenceFightsTests`. Sin la variable o sin el `.pcap`, el test se saltea. Cada fila nueva lleva su caso en `ReferenceFightsTests`.

En las dos peleas el local es de clase 12 y no tiene invocaciones: no sirven para saber si Combat Analysis las cuenta (ver "Validación contra el juego" en `protocol-notes.md` de la 001).

Pendiente: muñeco, campo, party con healer, invocador (T001; también dice si Combat Analysis cuenta las invocaciones), y una pelea de jefe en party con pausas de fase, para validar contra Combat Analysis que los golpes de los adds la sostengan (ver "Solo los golpes de otro NPC" en `combat-regressions.md` de la 001).
