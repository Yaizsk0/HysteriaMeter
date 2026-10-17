// Arnés de capturas de la UI: un PNG por ventana y estado con los datos de demo (DemoEncounter), para comparar
// contra los mocks del rediseño (redesign/project/*.dc.html) y revisar los cambios de tema.
//
// Es opt-in: sin la variable HYSTERIA_SNAPSHOTS el test sale enseguida, sin crear la App, así el CI y un
// "dotnet test" normal no lo corren. WPF admite una sola App por proceso, atada a su hilo: correrlo SOLO, con --filter.
//
//   Git Bash:   HYSTERIA_SNAPSHOTS='C:\ruta\snapshots' dotnet test tests/HysteriaMeter.App.Tests --filter "FullyQualifiedName~UiSnapshotTests"
//   PowerShell: $env:HYSTERIA_SNAPSHOTS = "C:\ruta\snapshots"; dotnet test tests/HysteriaMeter.App.Tests --filter "FullyQualifiedName~UiSnapshotTests"
//
// Variables opcionales:
//   HYSTERIA_SNAPSHOTS_SOLO     estados a generar: partes del nombre separadas por coma (por ejemplo "01,detalle").
//   HYSTERIA_SNAPSHOTS_ESCALA   1 por defecto; 2 para PNG al doble.
//   HYSTERIA_SNAPSHOTS_CULTURA  es-AR por defecto ("38,4 %" como en los mocks).
//   HYSTERIA_SNAPSHOTS_FONDO    color detrás de las ventanas translúcidas. Por defecto #3A4A3F, el fondo de los mocks:
//                               sin fondo, los visores componen el PNG sobre blanco y los paneles translúcidos se ven
//                               grises. "ninguno" deja el PNG translúcido.
//
// Las fuentes (Barlow, Inter) salen del ensamblado de la app con URI "HysteriaMeter;component", que resuelven también
// bajo el testhost. Antes de dibujar, el arnés comprueba que NumberFont, TitleFont y BodyFont sean las embebidas: si
// cayeran en una fuente de reemplazo, los anchos y el aspecto de los PNG no serían los de la app.
//
// Agregar un estado: una línea más en States con el nombre del PNG y una función que arma la ventana con las
// propiedades reales del VM (nada de datos inventados). Cada estado arma su propia sesión: no dependen entre sí.
// Si un estado falla, los demás se generan igual y el test falla al final con la lista de los que no salieron.

using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Dungeon;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.Registro;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.Tests;

public sealed class UiSnapshotTests
{
    /// <summary>Hora fija para las peleas guardadas en el historial y el dungeon: los PNG salen iguales en cada corrida.</summary>
    private static readonly DateTimeOffset Base = new(2026, 10, 15, 21, 30, 0, TimeSpan.Zero);

    /// <summary>Un PNG: su nombre (sin extensión) y cómo se arma la ventana.</summary>
    private sealed record State(string Name, Func<Harness, Window> Build);

    private static readonly int DenseRows = MeterPreferences.RowHeightOptions[0];
    private static readonly int LooseRows = MeterPreferences.RowHeightOptions[^1];

    /// <summary>
    /// Los estados, en orden. Estados del mock que hoy no se pueden armar (no hay datos o la UI no existe todavía):
    /// "Intento N" y "Enrage en" (la demo no trae TargetEnrageSeconds y el TTK solo sale de Refresh en vivo),
    /// overheal, pestañas Skills/Muertes/Log del detalle (ActorDetailViewModel no tiene propiedad de pestaña),
    /// tarjeta de fin de pelea, avisos dentro del overlay, ventanas propias de Historial, Configuración y Bienvenida,
    /// y la pantalla "sin Npcap". Cuando existan, se agregan acá.
    /// </summary>
    private static readonly State[] States =
    [
        // En vivo: la demo fijada con aspecto de vivo (Live va al final: Refresh lo deshace).
        new("01-vivo-dps", h => Overlay(Live(h.Demo()))),
        new("02-vivo-cura", h => Overlay(Live(h.Demo(MeterTab.Healing)))),
        new("03-vivo-recibido", h => Overlay(Live(h.Demo(MeterTab.Taken)))),

        // Sin pelea: Refreshed corre Refresh y UpdateStatus, que dan el título y el texto reales.
        new("04-esperando-combate", h => Overlay(Refreshed(new MeterViewModel(h.Session())))),

        // Pausa: IsPaused = true no refresca, así que el vivo forzado se mantiene. Más ancho que el de fábrica: a 356 px
        // los íconos de la derecha tapan la parte izquierda del encabezado y "▶ PAUSA" no se ve (pasa igual en la app).
        new("05-pausa", h =>
        {
            var vm = Live(h.Demo());
            vm.IsPaused = true;
            return Overlay(vm, width: 480);
        }),

        // Historial por el camino real: ToggleHistory lee la base temporal; OpenEncounter fija la pelea guardada.
        new("06-historial-abierto", h => Overlay(h.HistoryOpen())),
        new("07-historial-pelea-abierta", h =>
        {
            var vm = h.HistoryOpen();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(vm.History[0]));
            return Overlay(vm);
        }),

        // Configuración: con el panel abierto, UpdateStatus también llena Diagnostics.
        new("08-configuracion", h =>
        {
            var vm = new MeterViewModel(h.Session());
            vm.ToggleSettingsCommand.Execute(null);
            return Overlay(Refreshed(vm));
        }),

        // Minimizado: la pastilla muestra MinimizedText, que ShowDemo ya calculó.
        new("09-minimizado", h =>
        {
            var vm = Live(h.Demo());
            vm.IsMinimized = true;
            return Overlay(vm);
        }),

        new("10-compacto", h => Overlay(Live(h.Demo(compact: true)))),
        new("11-vista-run", h => Overlay(Live(h.Demo(runView: true)))),

        // Densidades de fila: la más chica y la más grande de las opciones reales, en una sesión propia.
        new($"12-filas-{DenseRows}px", h => Overlay(Live(h.Demo(preferences: new MeterPreferences { LastSeenNews = 1, RowHeight = DenseRows })))),
        new($"13-filas-{LooseRows}px", h => Overlay(Live(h.Demo(preferences: new MeterPreferences { LastSeenNews = 1, RowHeight = LooseRows })))),

        // Bienvenida: sesión nueva con LastSeenNews = 0; la primera ventana recibe el aviso de NewsFeed.
        new("14-bienvenida", h => Overlay(Refreshed(new MeterViewModel(h.Session(new MeterPreferences()))))),

        // Sin tráfico: la captura arrancó hace 30 s y nunca vio paquetes (el silencio es de 15 s).
        new("15-sin-trafico", h => Overlay(Refreshed(new MeterViewModel(
            h.Session(status: new CaptureStatus(DateTime.UtcNow.AddSeconds(-30))))))),

        // Error de captura. El texto es de prueba y se ve como tal: en producción MeterPipeline pasa ex.Message.
        new("16-error-captura", h =>
        {
            var failed = new CaptureStatus(DateTime.UtcNow);
            failed.Fail("[captura de prueba] en producción acá va el mensaje de la excepción");
            return Overlay(Refreshed(new MeterViewModel(h.Session(status: failed))));
        }),

        // Detalle de jugador. El VM no tiene overlay: DetailRequested no tiene suscriptor y la ventana la crea el arnés.
        new("20-detalle", h => new DetailWindow(h.DetailOf("Kaelis"))),
        new("21-detalle-muertes", h => new DetailWindow(h.DetailOf("Tharos"))),
        new("22-detalle-comparar", h =>
        {
            var detail = h.DetailOf("Kaelis");
            detail.CompareWith = detail.Others[0];
            return new DetailWindow(detail);
        }),
        new("23-detalle-renombrar-skill", h =>
        {
            var detail = h.DetailOf("Kaelis");
            detail.StartRenameSkillCommand.Execute(detail.Skills[0]);
            return new DetailWindow(detail);
        }),

        // Registro de la primera muerte de la demo (como en WindowSmokeTests).
        new("24-registro", _ =>
        {
            var death = DemoEncounter.Create(Base).Deaths![0];
            return new LogWindow(new LogViewModel(() => death.LastHits, SkillNames.Empty, death.ActorId, death.Name));
        }),

        // Modo dungeon: un wipe y un kill del boss. Son la demo marcada como boss: no hay otros números.
        new("30-dungeon", _ =>
        {
            var wipe = DemoEncounter.Create(Base.AddMinutes(6)) with { Id = Guid.NewGuid(), TargetIsBoss = true };
            var kill = DemoEncounter.Create(Base.AddMinutes(10)) with { Id = Guid.NewGuid(), TargetIsBoss = true, TargetKilled = true };
            return new DungeonWindow(new DungeonSummaryViewModel(DungeonSummary.From([wipe, kill], Base)));
        }),
    ];

    [Fact]
    public void Generate_ui_snapshots()
    {
        // Lo primero, antes de tocar Application: sin la variable no se crea la App y no choca con WindowSmokeTests.
        var folder = Environment.GetEnvironmentVariable("HYSTERIA_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var scale = double.TryParse(Environment.GetEnvironmentVariable("HYSTERIA_SNAPSHOTS_ESCALA"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 1;
        var culture = new CultureInfo(Environment.GetEnvironmentVariable("HYSTERIA_SNAPSHOTS_CULTURA") ?? "es-AR");
        var backdrop = Environment.GetEnvironmentVariable("HYSTERIA_SNAPSHOTS_FONDO") switch
        {
            null or "" => "#3A4A3F",
            "ninguno" => null,
            var color => color,
        };
        var only = (Environment.GetEnvironmentVariable("HYSTERIA_SNAPSHOTS_SOLO") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var database = Path.Combine(Path.GetTempPath(), $"hysteria-snapshots-{Guid.NewGuid():N}.db");
        var failures = new List<string>();

        try
        {
            RunOnSta(culture, () =>
            {
                var harness = Harness.Create(database);
                var renderer = new Renderer(folder, scale, backdrop);
                foreach (var state in States.Where(st => only.Length == 0 || only.Any(o => st.Name.Contains(o, StringComparison.OrdinalIgnoreCase))))
                {
                    try
                    {
                        renderer.Shot(state.Build(harness), state.Name);
                    }
                    catch (Exception ex)
                    {
                        // Un estado roto no corta los demás: se junta y se informa al final.
                        failures.Add($"{state.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            });
        }
        finally
        {
            // El pool de SQLite ya lo soltó el hilo STA al apagarse; acá solo se borra la base temporal.
            foreach (var file in new[] { database, database + "-wal", database + "-shm" })
            {
                File.Delete(file);
            }
        }

        Assert.True(failures.Count == 0, "Estados que no se generaron:\n" + string.Join('\n', failures));
    }

    /// <summary>
    /// Aspecto "en vivo" sobre la demo: cambia solo banderas de presentación; los números son los de DemoEncounter.
    /// Va al final porque Refresh() (lo disparan Tab, IsPaused = false, IsRunView...) vuelve IsLive a false con la demo fijada.
    /// </summary>
    private static MeterViewModel Live(MeterViewModel vm)
    {
        vm.IsViewingHistory = false;
        vm.HistoryLabel = string.Empty;
        vm.IsLive = true;
        vm.StatusKind = "live";
        vm.StatusText = "En combate"; // el mismo texto que pone UpdateStatus con captura y combate
        return vm;
    }

    /// <summary>Corre el Refresh() real (y UpdateStatus): OnIsPausedChanged(false) lo llama.</summary>
    private static MeterViewModel Refreshed(MeterViewModel vm)
    {
        vm.IsPaused = true;
        vm.IsPaused = false;
        return vm;
    }

    /// <summary>
    /// Overlay sin OverlayWindows: no registra atajos globales (IsMain da false). Sin <paramref name="width"/>, el ancho
    /// del XAML (el de una instalación nueva).
    /// </summary>
    private static OverlayWindow Overlay(MeterViewModel viewModel, double? width = null)
    {
        var window = new OverlayWindow(viewModel);
        if (width is { } w)
        {
            window.Width = w;
        }

        // OverlayWindow arranca el timer de Refresh en Loaded. Este handler se suscribe después, corre después y lo
        // frena, así ningún Refresh() pisa el estado armado a mano.
        window.Loaded += (_, _) => viewModel.Stop();
        // El overlay crece con sus filas (hasta MaxHeight): en el PNG no se corta ninguna.
        window.SizeToContent = SizeToContent.Height;
        return window;
    }

    /// <summary>Lo compartido entre estados: la base temporal del historial. Todo lo demás lo arma cada estado.</summary>
    private sealed class Harness
    {
        private readonly EncounterRepository _history;

        private Harness(EncounterRepository history) => _history = history;

        /// <summary>
        /// Historial por el camino real: tres intentos guardados en SQLite que vuelven por ListRecentAsync y HistoryItem.From.
        /// Son la misma demo (no se inventan otros números) con id y hora distintos; el último cuenta como kill.
        /// </summary>
        public static Harness Create(string database)
        {
            var repository = new EncounterRepository(database);
            Pump(repository.InitializeAsync());
            EncounterSnapshot[] fights =
            [
                DemoEncounter.Create(Base) with { Id = Guid.NewGuid() },
                DemoEncounter.Create(Base.AddMinutes(4)) with { Id = Guid.NewGuid() },
                DemoEncounter.Create(Base.AddMinutes(8)) with { Id = Guid.NewGuid(), TargetKilled = true },
            ];
            foreach (var fight in fights)
            {
                Pump(repository.SaveAsync(fight));
            }

            return new Harness(repository);
        }

        /// <summary>
        /// Sesión nueva con captura "con tráfico". LastSeenNews = 1 por defecto: sin el aviso de bienvenida
        /// (tiene su propio estado).
        /// </summary>
        public MeterSession Session(MeterPreferences? preferences = null, CaptureStatus? status = null)
        {
            if (status is null)
            {
                status = new CaptureStatus(DateTime.UtcNow);
                status.MarkTraffic(DateTime.UtcNow);
            }

            return new MeterSession(
                new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, preferences ?? new MeterPreferences { LastSeenNews = 1 },
                _ => { }, history: _history, status: status);
        }

        /// <summary>Overlay con la pelea de demo fijada. La pestaña y la vista van por el constructor, antes de Live().</summary>
        public MeterViewModel Demo(MeterTab tab = MeterTab.Damage, bool compact = false, bool runView = false, MeterPreferences? preferences = null)
        {
            var vm = new MeterViewModel(Session(preferences), tab, compact: compact, runView: runView);
            vm.ShowDemoCommand.Execute(null);
            return vm;
        }

        public MeterViewModel HistoryOpen()
        {
            var vm = new MeterViewModel(Session());
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            Assert.NotEmpty(vm.History);
            return vm;
        }

        /// <summary>Detalle de un jugador de la demo, como lo abre el clic en su fila.</summary>
        public ActorDetailViewModel DetailOf(string name)
        {
            var vm = Demo();
            vm.ShowDetailCommand.Execute(vm.Actors.Single(a => a.Name == name));
            return vm.Detail;
        }
    }

    /// <summary>Muestra cada ventana fuera de pantalla, la dibuja a PNG y la cierra.</summary>
    private sealed class Renderer(string folder, double scale, string? backdrop)
    {
        public void Shot(Window window, string name)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            Pump(); // Loaded, bindings, layout y primer render
            try
            {
                Render(window, name);
            }
            finally
            {
                window.Close();
                Pump();
            }
        }

        private void Render(Window window, string name)
        {
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;

            // Mismo método que OverlayWindow.RenderOverlay: VisualBrush del contenido, sin el corrimiento del Margin.
            // El LayoutTransform equivale al zoom del overlay (1 sin OverlayLayout).
            var zoom = content.LayoutTransform.Value;
            var width = content.ActualWidth * zoom.M11;
            var height = content.ActualHeight * zoom.M22;
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                if (backdrop is not null)
                {
                    // Las ventanas tienen AllowsTransparency y los paneles son translúcidos: fondo opcional tipo "juego".
                    context.DrawRectangle((Brush)new BrushConverter().ConvertFromInvariantString(backdrop)!, null, new Rect(0, 0, width, height));
                }

                // Viewbox explícito: sin él, el pincel toma los límites de todos los descendientes (paneles ocultos,
                // contenido de los ScrollViewer) y achica la ventana dentro del PNG.
                var brush = new VisualBrush(content)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, content.ActualWidth, content.ActualHeight),
                };
                context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }

            // DPI fijo (96 × escala): el PNG sale igual en cualquier monitor.
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(file);
        }
    }

    /// <summary>Vacía la cola del Dispatcher hasta quedar ocioso: Loaded, bindings, layout y render.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.InvokeAsync(() => frame.Continue = false, DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Espera una tarea bombeando la cola. Los comandos async del VM vuelven por el Dispatcher, así que bloquear el
    /// hilo esperándolos los dejaría colgados.
    /// </summary>
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult(); // ya terminó: solo relanza su excepción, si la hubo
        Pump();
    }

    private static void RunOnSta(CultureInfo culture, Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                // Los números del VM usan CurrentCulture; la app toma la del sistema. Acá se fija.
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                // Con este contexto, los await con ConfigureAwait(true) vuelven a este hilo y no al ThreadPool.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                if (Application.Current is { } existing && existing.Dispatcher.Thread != Thread.CurrentThread)
                {
                    throw new InvalidOperationException(
                        "Ya hay una App de WPF creada en otro hilo de este proceso (corrió otro test de ventanas). " +
                        "Correr este test solo: dotnet test --filter FullyQualifiedName~UiSnapshotTests");
                }

                app = Application.Current as App ?? new App();
                app.InitializeComponent();
                // App.xaml usa OnMainWindowClose: cerrar la primera ventana encolaría el apagado del Dispatcher y,
                // como acá se bombea la cola, cortaría la generación a la mitad.
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                CheckFonts(app);

                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try
                {
                    ShutDown(app);
                }
                catch (Exception ex)
                {
                    // Una excepción que escapara del hilo tiraría abajo el testhost: se informa como fallo del test.
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>
    /// Apaga todo en el hilo STA, antes de que termine: las ventanas que hayan quedado abiertas (un estado que falló a
    /// mitad de camino), la App (Shutdown: con OnExplicitShutdown nadie más lo llama), el pool de SQLite y el
    /// Dispatcher. Dispatcher.Run procesa la cola hasta el apagado y vuelve cuando terminó (OLE, render y ventanas
    /// ocultas incluidos). Sin esto, el testhost a veces se caía al cerrar con un "Assert failure" del CLR.
    /// </summary>
    private static void ShutDown(App? app)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (app is not null)
        {
            foreach (var window in app.Windows.Cast<Window>().ToList())
            {
                window.Close();
            }

            app.Shutdown(); // encola el apagado de la App (Exit) con prioridad Normal
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        // SystemIdle: después del apagado de la App y de lo que quede en la cola.
        dispatcher.BeginInvokeShutdown(DispatcherPriority.SystemIdle);
        Dispatcher.Run();
        if (!dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("El Dispatcher del arnés no terminó de apagarse.");
        }
    }

    /// <summary>
    /// Las tres fuentes del tema tienen que ser las embebidas. Si un URI no resuelve, WPF cae sin avisar en una fuente
    /// del sistema y los PNG salen con otros anchos.
    /// </summary>
    private static void CheckFonts(Application app)
    {
        foreach (var (key, family) in new[] { ("NumberFont", "Barlow Semi Condensed"), ("TitleFont", "Barlow Semi Condensed"), ("BodyFont", "Inter") })
        {
            var font = (FontFamily)app.Resources[key];
            var typeface = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphs) || !glyphs.FamilyNames.Values.Contains(family))
            {
                throw new InvalidOperationException($"{key} no es {family} (quedó {font.Source}): los PNG saldrían con una fuente de reemplazo.");
            }
        }
    }
}
