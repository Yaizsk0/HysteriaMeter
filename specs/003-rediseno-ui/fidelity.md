# Corrección de fidelidad con los mocks

Revisión local del 17/10/2026 sobre los HTML originales del rediseño. Complementa [decisiones.md](decisiones.md) y la [validación de la integración inicial](validacion.md). Este documento describe el trabajo de corrección local; no acredita identidad visual completa, publicación ni aplicación de estos cambios a una instancia en uso.

## Qué faltaba en la revisión inicial

- Se habían aceptado Barlow Semi Condensed e Inter como sustituciones. Sus métricas cambian nombres, cifras, saltos de línea y densidad respecto de Barlow Condensed e IBM Plex Sans.
- La traslación de `border-radius: 999px` a WPF producía elipses en algunos controles, en lugar de extremos semicirculares. También había diferencias de tamaño y relleno en las vistas previas de temas.
- Algunos estados y colores dependientes de triggers WPF no reflejaban el mock; debían comprobarse en el árbol visual, además de revisar bindings y recursos. La pausa y la adaptación del encabezado a anchos pequeños necesitaban revisión específica.
- Dungeon no había tenido una pista de fidelidad propia dentro de la recuperación de las cinco pistas. Tener una captura de esa ventana no equivalía a revisar su composición contra el HTML.
- Faltaba revisar en conjunto la tarjeta de novedades, los avisos de enrage y captura guardada, la pastilla y la tipografía del menú de bandeja. Los controles funcionales ya existentes no bastaban para acreditar el aspecto del mock.

## Alcance corregido

- Fuentes originales OFL embebidas, sin instalación global: Barlow Condensed para números, títulos y nombres del overlay; IBM Plex Sans para el cuerpo por defecto. Se comprueba que WPF resuelva las familias y pesos embebidos. Los nombres usan `NumberFont` a 16 unidades en filas de 30, con tamaños menores o mayores según densidad.
- Se conservan Inter y Barlow Semi Condensed como opciones. La selección del usuario sigue controlando el cuerpo. Los chips de Configuración heredan una fuente uniforme; no dependen de encontrar cada nombre de familia instalado en Windows.
- Radios ajustados a la altura real de chips y toggles. Vista previa de cada tema de Configuración a 66 unidades de alto exterior, como el contenido CSS de 54 más su relleno vertical.
- Encabezado adaptable al ancho y al indicador de grabación; acciones accesibles al reducirlo. Pausa con su propio encabezado y presentación de filas, sin duplicar los bloques del vivo. Estados y colores mediante recursos y triggers compatibles con WPF.
- Pastilla con `MiniPanel` al 88 %, insignia, cifras, puesto, tendencia y reloj cuando corresponden. El fondo de un aviso elevado sigue siendo un recurso distinto.
- Novedades con tarjeta y acción de altura completa; enrage y captura guardada con jerarquía, iconografía y espaciado revisados. Se preservan sus comandos y condiciones de aparición.
- Menú de bandeja con fuente elegida a 13 px lógicos. Las fuentes embebidas se exponen a GDI mediante un registro privado del proceso, sin instalarse globalmente; el menú libera sus recursos al cambiar de fuente y cerrarse.
- Dungeon con composición de resumen, indicadores, línea de tiempo, tarjetas y matriz revisadas contra el mock. Se mantienen los datos reales y la acción de copiar para Discord; se agrega evidencia al ancho mínimo.

## Diferencias intencionales

- No se inventan zona, nombres, métricas, percentiles ni estados que la sesión no proporciona. Se ocultan o adaptan los elementos sin datos. Los fixtures del arnés se identifican como datos de prueba.
- Se conservan las acciones funcionales anteriores aunque el mock no las dibuje: siguen accesibles en botones, menús o tooltips. La interfaz también debe funcionar con ventanas redimensionadas, zoom y densidades distintas.
- La opción de opacidad mantiene `Window.Opacity` sobre toda la ventana, junto con sus comportamientos al pasar el mouse y fuera de combate. No se convierte en una opacidad exclusiva del fondo.
- IBM Plex Sans y Sparklines desactivadas son los valores por defecto para instalaciones nuevas. Los valores ya guardados de fuente, columnas y demás preferencias no se sobrescriben para imitar el mock.
- WPF y WinForms no reproducen todos los mecanismos de CSS: el espaciado entre letras se omite y la bandeja utiliza renderizado GDI con escalado DPI. Estas diferencias requieren revisión visual a los tamaños relevantes.

## Evidencia disponible y límites

La validación comunicada para esta corrección es de **330 pruebas aprobadas: 196 de App, 6 de Capture, 58 de Combat, 47 de Protocol y 23 de Storage**. Incluye las pruebas de fuentes del menú en GDI, cambio al abrir y liberación observable de los objetos `Font`. No se afirma que esas pruebas enumeren los registros privados de fuentes de Windows.

Se generaron **59 PNG del arnés WPF y un PNG del menú de bandeja**, incluidas novedades y Dungeon al ancho mínimo. Los artefactos están en el área de visualizaciones, fuera de Git. La generación del PNG comprueba que el estado se puede renderizar; la comparación con el HTML sigue siendo una revisión visual.

Se agregó además `TraySnapshotTests.Generate_tray_snapshot`, opt-in mediante `HYSTERIA_TRAY_SNAPSHOT`. Se ejecutó aislado y pasó; el PNG generado se revisó visualmente. Usa `CreateMenu`, layout y `DrawToBitmap` con un handle oculto, sin mostrar el popup ni crear un `NotifyIcon`. Debe ejecutarse en un proceso separado del arnés WPF, porque WPF permite una sola `Application` por proceso.

Las verificaciones usan `App.InitializeComponent` sin `OnStartup`, datos controlados y ventanas fuera de pantalla. No se inició otra aplicación ni captura de tráfico para esta revisión. La aplicación de la sesión en juego corresponde a otro checkout y la captura de comparación sigue activa; esta evidencia no demuestra cómo se ve esa instancia ni que haya recibido los cambios locales.
