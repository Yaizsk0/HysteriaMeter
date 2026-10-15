# Quickstart

## Requisitos

- Windows 10/11 x64
- [Npcap](https://npcap.com/) con **"WinPcap API-compatible Mode"** activado
- .NET 10 SDK

## Compilar y testear

```powershell
dotnet build
dotnet test
```

## Correr el overlay

```powershell
dotnet run --project src/HysteriaMeter.App
```

Configuración en `src/HysteriaMeter.App/appsettings.json`:

- `Capture:ProcessNames`: nombre del ejecutable del juego sin `.exe` (verificar en el Administrador de tareas).
- `Capture:Device`: parte del nombre de la placa si hay varias (VPN, ExitLag…). `hysteria-rec devices` las lista.
- `Capture:Mode`: `Live` o `Replay` (con `ReplayPath` y `ReplaySpeed`).

Overlay: arrastrar desde el encabezado; 🔒 o **Ctrl+Shift+L** alterna el click-through; ⟲ reinicia el encuentro.

## Descubrir opcodes

1. Grabar una pelea simple (un solo monstruo, pocas skills distintas):

   ```powershell
   dotnet run --project tools/HysteriaMeter.Recorder -- record pelea.pcap --process AION2
   ```

2. Ver el histograma de opcodes y ejemplos:

   ```powershell
   dotnet run --project tools/HysteriaMeter.Recorder -- dump pelea.pcap --samples 3
   ```

3. Buscar el opcode que aparece una vez por golpe y contiene el daño que mostró el juego (en varint). Inspeccionarlo con `--opcode 0xNNNN --samples 20`.
4. Si el framer reporta muchas desincronizaciones, ajustar `ProtocolOptions` (tamaño/endianness del opcode, si el largo incluye el prefijo).
5. Cargar el opcode en `data/opcodes.json`, ajustar el layout en `DamagePacketDecoder` y agregar un test con esos bytes reales.
6. Validar con `Capture:Mode=Replay` contra Combat Analysis (Ctrl+X).
