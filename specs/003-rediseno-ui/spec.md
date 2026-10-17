# Rediseño de la interfaz

Estado: continuación del diseño aprobado en [decisiones.md](decisiones.md), sin nuevas decisiones de alcance. La base de tema y tipografía se integró en #70. El plan de integración está en [plan.md](plan.md).

## Objetivo y alcance

Actualizar las pantallas del meter al diseño aprobado, conservar todas las funciones y preferencias existentes, y separar Historial y Configuración del área de filas del overlay. El producto sigue siendo local y de captura pasiva.

## Requisitos por pista

- Historial: ventana propia con búsqueda y filtros excluyentes, runs, datos reales de intentos/muertes/percentiles, notas y borrado, progreso por boss/personaje y resumen de run. Los filtros no alteran el denominador de kills del progreso.
- Configuración: ventana propia con ocho secciones; todas las opciones anteriores accesibles; sliders de filas, cantidad y opacidad; Todos conserva el valor 0; temas con paletas reales; atajo opcional para rotar pestañas; selector de red persistido y aplicado al reiniciar; conteos locales de datos del juego. Las ventanas auxiliares permanecen abiertas al usar exportación, demo y apertura de peleas.
- Detalle: skills, muertes y log integrados, comparación de jugadores e intentos, datos existentes disponibles aunque el mock no los muestre, gráficos y marcas reales.
- Inicio: bienvenida con estados reales de instalación/captura/datos; tarjeta al derrotar un boss con acciones sobre su propia pelea; bandeja y novedades con todas las acciones conservadas.
- Overlay: cabecera y menú, filas, objetivo, estados vacíos y de error, avisos y pastilla, con datos reales y adaptación al tamaño y zoom.

## Restricciones y aceptación

La constitución sigue vigente: no se envían ni modifican paquetes y no se toca el proceso ni la memoria del juego. No se inventan métricas ni nombres. Se conservan las preferencias guardadas y las opciones por ventana. Las fuentes y colores proceden del sistema visual compartido y los temas se repintan en vivo.

Cada pista debe compilar sin advertencias, pasar las pruebas relevantes y la suite completa antes de integrarse, y revisarse con capturas del arnés fuera de pantalla. No se ejecuta la app ni se inicia captura de red durante este trabajo. README y novedades describen únicamente cambios efectivamente preparados para publicar; la integración final requiere CI verde.
