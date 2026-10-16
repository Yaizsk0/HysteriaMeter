# Hysteria Meter

DPS meter de escritorio para AION 2. .NET 10, WPF, captura pasiva con Npcap. Desarrollo spec-driven con Spec Kit.

- Constitución (reglas no negociables): `.specify/memory/constitution.md`. El principio I (solo lectura pasiva) no se negocia: nunca enviar/modificar paquetes ni tocar el proceso o la memoria del juego.
- Feature activa: `specs/001-dps-meter-mvp/` (spec, plan, tasks). Marcar `[X]` en `tasks.md` al completar tareas.
- Capas: `Capture → Protocol → Combat → (Storage, App)`. Combat es puro, sin I/O.
- Opcodes en `data/opcodes.json`, no en código.
- Comandos: `dotnet build`, `dotnet test`, `dotnet run --project tools/HysteriaMeter.Recorder -- dump <pcap>`.
- Idioma: comentarios, docs y mensajes en español.

## Cómo trabajar ante un bloqueo

Lección del PR #65 (nombres de skills): se perdieron varios intercambios discutiendo la restricción de una fuente (Questlog) cuando había otra con términos compatibles (MetaBot.GG) que resolvía todo.

1. Verificar si el bloqueo es real y si afecta al objetivo entero o solo al camino actual. La restricción de una fuente no prohíbe el objetivo: buscar otra fuente.
2. Investigar antes de negarse: leer los términos o la documentación. Distinguir lo verificado de lo interpretado.
3. Elegir la alternativa permitida más simple: fuentes directas antes que algoritmos, carga manual o infraestructura nueva.
4. Ejecutar sin pedir permiso de nuevo lo que ya está autorizado y dentro del alcance. Volver a preguntar solo si hay riesgo, una autorización nueva o un cambio de alcance.
5. Explicar una limitación una sola vez y entregar resultados parciales que funcionen, no planes.
6. Nunca inventar datos. Los límites reales siguen en pie: el principio I de la constitución, no saltear el cifrado de los archivos del juego, no obtener ni rastrear su clave AES y respetar los términos de cada fuente (Questlog prohíbe la extracción automatizada; MetaBot permite reutilizar con atribución).
