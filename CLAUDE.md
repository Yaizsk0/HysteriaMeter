# Hysteria Meter

DPS meter de escritorio para AION 2. .NET 10, WPF, captura pasiva con Npcap. Desarrollo spec-driven con Spec Kit.

- Constitución (reglas no negociables): `.specify/memory/constitution.md`. El principio I (solo lectura pasiva) no se negocia: nunca enviar/modificar paquetes ni tocar el proceso o la memoria del juego.
- Feature activa: `specs/001-dps-meter-mvp/` (spec, plan, tasks). Marcar `[X]` en `tasks.md` al completar tareas.
- Capas: `Capture → Protocol → Combat → (Storage, App)`. Combat es puro, sin I/O.
- Opcodes en `data/opcodes.json`, no en código.
- Comandos: `dotnet build`, `dotnet test`, `dotnet run --project tools/HysteriaMeter.Recorder -- dump <pcap>`.
- Idioma: comentarios, docs y mensajes en español.
