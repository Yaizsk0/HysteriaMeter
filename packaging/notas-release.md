**Versión alfa.** Funciona, pero puede tener errores y cambiar entre versiones. Si algo falla, usá "Reportar un problema" en Configuración.

### Cómo se instala

1. Instalá [Npcap](https://npcap.com/#download) con "WinPcap API-compatible Mode" (una sola vez).
2. Bajá el zip `HysteriaMeter-…-portable-win-x64.zip` y descomprimilo en una carpeta tuya.
3. Abrí `HysteriaMeter.exe`. No está firmado: si Windows muestra "Windows protegió su PC", tocá "Más información" y después "Ejecutar de todas formas".

Trae .NET adentro, no hace falta instalar nada más. Las preferencias, el historial y los exportes quedan en la carpeta `datos`, al lado del `.exe`. Para seguir con el historial de una versión compilada, copiá lo que hay en `%LOCALAPPDATA%\HysteriaMeter` a `datos` (más detalles en `LEEME.txt`).

### Actualizaciones

El meter se actualiza solo: cada 6 horas mira si hay una versión nueva en los releases de GitHub, la baja, revisa su hash y la instala la próxima vez que lo abrís (o al tocar "Reiniciar con la versión …" en el ícono de la bandeja). Tus datos no se tocan. Desde una alfa sigue a la próxima alfa y a la estable. Se apaga en Configuración → PROGRAMA → "Actualizar solo".

### Qué hace y qué no

Solo lee una copia del tráfico de red del juego con Npcap. No envía ni modifica paquetes, no toca el proceso ni la memoria del juego y no automatiza acciones.

### Verificar la descarga

El `.sha256` trae el hash del zip. En PowerShell, en la carpeta de la descarga:

```powershell
(Get-FileHash .\HysteriaMeter-*-portable-win-x64.zip -Algorithm SHA256).Hash
```

Tiene que coincidir con el primer campo del `.sha256` (sin importar mayúsculas).
