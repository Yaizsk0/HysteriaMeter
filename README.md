# Hysteria Meter

DPS meter de escritorio para **AION 2**. Lee el tráfico del juego con Npcap y muestra en un overlay el daño, la curación y el daño recibido de la party, en vivo.

Solo lectura pasiva: no envía ni modifica paquetes, no toca el proceso ni la memoria del juego y no automatiza nada. Ver la [constitución](.specify/memory/constitution.md).

## Descargar y usar

1. Instalá [Npcap](https://npcap.com/#download) con "WinPcap API-compatible Mode" (una sola vez).
2. Bajá el zip portable de [Releases](https://github.com/Yaizsk0/HysteriaMeter/releases) y descomprimilo en una carpeta tuya.
3. Abrí `HysteriaMeter.exe`. Trae .NET adentro. Si Windows muestra "Windows protegió su PC", tocá "Más información" y "Ejecutar de todas formas".

La primera vez, una bienvenida revisa Npcap y la captura. Las preferencias, el historial y los exportes quedan en la carpeta `datos`, al lado del `.exe`; sin `portable.txt` van a `%LOCALAPPDATA%\HysteriaMeter`.

El zip se actualiza solo: baja la versión nueva de los releases de GitHub, revisa su `.sha256` y la instala la próxima vez que abrís el meter. Se apaga en Configuración → PROGRAMA → "Actualizar solo". Un build compilado a mano no se actualiza.

Si Npcap se instaló con "Restrict Npcap driver's access to Administrators only", Windows pide permiso cada vez que el meter abre la captura.

## Qué tiene

- **Overlay:** pestañas DPS, CURA y RECIBIDO, y alcance PELEA, RUN o SESIÓN. Contra un boss cuenta solo el daño al boss (se cambia en Configuración). Muestra el símbolo de clase, en cuánto muere el boss al ritmo actual y el aviso de enrage; item level, power y columnas extra son opcionales. Tiene modo compacto, pastilla minimizada, pausa, temas y varias ventanas que se pegan a los bordes.
- **Detalle de un jugador:** daño por skill (crítico, perfecto, doble, frente, espalda y múltiple), muertes y lo que recibió antes, registro golpe a golpe, tiempo activo y muerto, fases del boss, consejos y comparación con otro jugador o con tus intentos.
- **Historial:** peleas agrupadas por entrada a la dungeon, intentos, récords y percentiles por boss y por personaje, notas y un resumen de la dungeon con kills, wipes y muertes.
- **Compartir:** imagen del overlay, CSV o JSON, "Abrir pelea…" para ver la de otro, resumen a un webhook de Discord y un perfil de configuración para llevar a otra PC.
- **Diagnóstico:** estado de la captura, contadores del lector, verificación del daño contra la vida que perdió cada monstruo y "Reportar un problema".
- **Prueba de DPS** contra el muñeco (60, 180 o 300 s) y grabación de la captura en `.pcap`.

Atajos de fábrica, que se cambian en Configuración → ATAJOS:

| Atajo | Acción |
|---|---|
| Ctrl+Shift+L | Bloquear los clics |
| Ctrl+Shift+R | Reiniciar la pelea |
| Ctrl+Shift+H | Ocultar el overlay |
| Ctrl+Shift+N | Minimizar a la pastilla |
| Ctrl+Shift+P | Pausar |
| Ctrl+Shift+M | Marcar un momento de la pelea |

Alt+← y Alt+→ (o las flechas ‹ ›) recorren las peleas guardadas, y Esc vuelve a la pelea actual.

## Nombres de monstruos y skills

El juego manda solo el ID de los monstruos y de las skills. Los nombres salen de:

- `data/npcs.json`: nombre, dungeon, si es boss y segundos hasta el enrage. Una pelea que sale como "NPC 2001234" se arregla con `"2001234": { "name": "...", "dungeon": "...", "boss": true, "enrage": 480 }`.
- `data/skills.json`, que completa la lista de MetaBot.GG de `Juego\skills.json`, o doble clic en una skill del detalle (queda en tus preferencias). En las skills de clase alcanza con nombrar la base: 15280143 usa el nombre de 15280000.
- `data/classes.json`: las clases y sus colores.
- `tools/HysteriaMeter.Extractor` (`hysteria-extract`): lee los archivos del juego instalado, con el juego cerrado y la clave que pongas vos, y deja nombres e íconos en `%LOCALAPPDATA%\HysteriaMeter\Juego`. Lo que cargues en `data/` les gana. Nada extraído va al repo.

## Compilar

Hace falta Windows 10/11, [Npcap](https://npcap.com/) en modo compatible WinPcap y el .NET 10 SDK.

```powershell
dotnet build
dotnet test
dotnet run --project src/HysteriaMeter.App -c Release
```

Para probar sin jugar, reproducí una grabación (`ReplaySpeed` la acelera):

```powershell
dotnet run --project src/HysteriaMeter.App -c Release -- --Capture:Mode=Replay --Capture:ReplayPath=C:\ruta\a\captura.pcap --Capture:ReplaySpeed=4
```

| Proyecto | Responsabilidad |
|---|---|
| `src/HysteriaMeter.Capture` | Npcap, filtro por proceso y puerto, reensamblado TCP, replay de `.pcap` |
| `src/HysteriaMeter.Protocol` | Framing varint, bundles LZ4, `data/opcodes.json`, decoders a eventos |
| `src/HysteriaMeter.Combat` | Modelo puro: entidades, encuentros y DPS |
| `src/HysteriaMeter.Storage` | Historial en SQLite |
| `src/HysteriaMeter.App` | Overlay y ventanas WPF |
| `tools/HysteriaMeter.Recorder` | `hysteria-rec`: grabar y analizar capturas (`dump <pcap>`) |

El zip portable lo arma `packaging/portable.ps1` y lo publica el workflow "Release portable" al subir un tag `v*`. El desarrollo usa [Spec Kit](https://github.com/github/spec-kit): las specs están en [`specs/`](specs/), las decisiones del diseño actual en [decisiones.md](specs/003-rediseno-ui/decisiones.md) y el protocolo en [protocol-notes.md](specs/001-dps-meter-mvp/protocol-notes.md).

## Licencia

El código es [MIT](LICENSE) y el zip la trae en `LICENSE.txt`. Las fuentes de `src/HysteriaMeter.App/Assets/Fonts` tienen su propia licencia (SIL Open Font License 1.1, el texto está al lado de cada una) y las bibliotecas que trae el zip, la suya: ver [`packaging/TERCEROS.txt`](packaging/TERCEROS.txt).

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- **Qué se firma:** `HysteriaMeter.exe` de los zips de [Releases](https://github.com/Yaizsk0/HysteriaMeter/releases). Lo compila el workflow "Release portable" en runners de GitHub, desde el tag del release y sin pasos a mano. Las bibliotecas de terceros que van al lado no se firman.
- **Committers y revisores:** [Yaizsk0](https://github.com/Yaizsk0).
- **Aprobadores:** [Yaizsk0](https://github.com/Yaizsk0). Cada firma se aprueba a mano en SignPath.
- **Estado:** falta que SignPath Foundation apruebe el proyecto. Hasta entonces, los releases salen sin firma.

### Privacidad

Hysteria Meter no manda información a otros sistemas, salvo en estos tres casos:

- **Actualización automática** (solo el zip portable, prendida de fábrica): consulta la lista de releases en `api.github.com` y, si hay una versión nueva, baja el zip y su `.sha256` de `github.com`. El pedido no lleva datos tuyos ni de tus peleas: GitHub ve tu IP y la versión del meter. Se apaga en Configuración → PROGRAMA → "Actualizar solo".
- **Discord:** solo si pegás la URL de un webhook en Configuración. Manda el resumen de una pelea a ese canal cuando tocás "Mandar ahora" o, si lo prendiste, al derrotar un boss. El resumen lleva los nombres de los jugadores, salvo que actives "Ocultar los nombres de los demás".
- **Reportar un problema:** abre el navegador con un issue de GitHub ya escrito (versión, sistema, estado de la captura y diagnóstico, sin nombres). Se publica solo si lo mandás vos.

La captura es local: el meter lee una copia del tráfico del juego y no la reenvía a ningún lado.
