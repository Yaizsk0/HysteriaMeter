using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// La tarjeta de novedades en ventanas chicas: flota sobre las filas sin empujarlas, no pasa del alto de la ventana,
/// la lista se desplaza solo si hace falta y "Entendido" (o Esc, con el overlay entero) siempre la cierra.
/// </summary>
[Collection("Overlay WPF")]
public sealed class OverlayNewsTests
{
    // Seis novedades del largo de las reales (110 a 160 caracteres): las que tapaban todo en una ventana de 455 × 275.
    internal static readonly string[] LongNews =
    [
        "Novedad de prueba 1: un texto del largo de las novedades reales, que ocupa dos o tres renglones en el overlay angosto.",
        "Novedad de prueba 2: otro texto largo para llenar la tarjeta y comprobar que la lista se desplaza sin tapar el botón.",
        "Novedad de prueba 3: el botón Entendido tiene que quedar adentro de la ventana aunque la lista no entre completa.",
        "Novedad de prueba 4: las filas de la pelea siguen midiendo lo mismo debajo de la tarjeta, que flota sin empujarlas.",
        "Novedad de prueba 5: en modo compacto la tarjeta se aprieta, sin el título grande, con texto y botón más chicos.",
        "Novedad de prueba 6: la sexta y última, porque NewsFeed.NoticeFor muestra hasta seis novedades sin ver por vez.",
    ];

    [Fact]
    public void Escape_closes_the_open_panel_first_then_the_news_then_the_saved_fight()
    {
        RunOnSta(() => WithNews(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var session = NewSession();
            var vm = new MeterViewModel(session);
            vm.ShowDemoCommand.Execute(null);
            Assert.True(vm.HasNotice);
            Assert.True(vm.IsViewingHistory);

            vm.IsSettingsOpen = true;
            vm.EscapeCommand.Execute(null);
            Assert.False(vm.IsSettingsOpen);
            Assert.True(vm.HasNotice);

            // Esc hace lo mismo que "Entendido": las marca como vistas.
            vm.EscapeCommand.Execute(null);
            Assert.False(vm.HasNotice);
            Assert.Equal(LongNews.Length + 1, session.Preferences.LastSeenNews);
            Assert.True(vm.IsViewingHistory);

            vm.EscapeCommand.Execute(null);
            Assert.False(vm.IsViewingHistory);
        }));
    }

    [Fact]
    public void Escape_in_the_pill_closes_the_panel_and_the_saved_fight_but_leaves_the_news_pending()
    {
        RunOnSta(() => WithNews(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var session = NewSession();
            var vm = new MeterViewModel(session);
            vm.ShowDemoCommand.Execute(null);
            vm.IsMinimized = true;
            Assert.True(vm.HasNotice);
            Assert.True(vm.IsViewingHistory);
            Assert.True(vm.HasPillRow); // la pastilla muestra tu fila de la demo fijada

            // Minimizar cierra los paneles; si igual queda uno abierto (es otra ventana, encima), Esc lo cierra primero.
            vm.IsSettingsOpen = true;
            vm.EscapeCommand.Execute(null);
            Assert.False(vm.IsSettingsOpen);
            Assert.True(vm.HasNotice);
            Assert.True(vm.IsViewingHistory);

            // En la pastilla las novedades no se ven: Esc no las marca como vistas y deja la pelea fijada.
            vm.EscapeCommand.Execute(null);
            Assert.True(vm.HasNotice);
            Assert.Equal(1, session.Preferences.LastSeenNews);
            Assert.False(vm.IsViewingHistory);
            Assert.False(vm.HasPillRow);

            // Sin nada más que cerrar, Esc no hace nada: tampoco abre la pastilla.
            vm.EscapeCommand.Execute(null);
            Assert.True(vm.IsMinimized);
            Assert.True(vm.HasNotice);

            // Con el overlay abierto vuelven a verse, y ahí Esc las cierra como "Entendido".
            vm.IsMinimized = false;
            vm.EscapeCommand.Execute(null);
            Assert.False(vm.HasNotice);
            Assert.Equal(LongNews.Length + 1, session.Preferences.LastSeenNews);
        }));
    }

    [Theory]
    [InlineData(380, false)]
    [InlineData(455, true)]
    public void News_scrollbar_appears_only_when_text_is_hidden(double width, bool compact)
    {
        RunOnSta(() => WithNews(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var vm = new MeterViewModel(NewSession(), compact: compact);
            vm.ShowDemoCommand.Execute(null);
            var window = new OverlayWindow(vm);
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                var card = Assert.IsType<Border>(window.FindName("NewsCard"));
                var list = Assert.IsType<ScrollViewer>(window.FindName("NewsScroller"));
                var body = Assert.IsType<DockPanel>(window.FindName("LiveBody"));
                void Layout(double height)
                {
                    content.Measure(new Size(width, height));
                    content.Arrange(new Rect(0, 0, width, height));
                    content.UpdateLayout();
                }

                // Ventana alta: entran las seis. Lo desplazable termina con el último renglón: la separación entre
                // novedades (10 px, 6 en compacta) no queda debajo de la última.
                const double tall = 1000;
                Layout(tall);
                var items = Assert.IsAssignableFrom<ItemsControl>(list.Content);
                var last = Descendants<TextBlock>(items).Last(t => t.Name == "NewsText");
                var textBottom = last.TransformToAncestor(items).Transform(new Point(0, last.ActualHeight)).Y;
                Assert.InRange(list.ExtentHeight - textBottom, -0.5, 0.5);
                // La separación va arriba de cada una menos la primera, que arranca pegada al título.
                var lines = Descendants<DockPanel>(items).Where(d => d.Name == "NewsLine").ToList();
                Assert.Equal(LongNews.Length, lines.Count);
                Assert.Equal(new Thickness(0), lines[0].Margin);
                Assert.All(lines.Skip(1), line => Assert.Equal(new Thickness(0, compact ? 6 : 10, 0, 0), line.Margin));

                // Lo que la ventana pone fuera de la lista con la tarjeta en su tope (el cuerpo menos su margen): con
                // un alto H, la lista mide H - fuera. Los altos de abajo salen del layout, no de números fijos.
                var cardBounds = card.TransformToAncestor(content).TransformBounds(new Rect(card.RenderSize));
                var bodyBounds = body.TransformToAncestor(content).TransformBounds(new Rect(body.RenderSize));
                var outside = tall - list.ActualHeight - (bodyBounds.Bottom - card.Margin.Bottom - cardBounds.Bottom);

                // El texto entra con 3 px de sobra (menos que la separación): sin barra.
                Layout(outside + textBottom + 3);
                Assert.Equal(Visibility.Collapsed, list.ComputedVerticalScrollBarVisibility);
                Assert.InRange(list.ScrollableHeight, 0, 0.5);

                // Falta 1 px (la caja vacía debajo del último renglón): tampoco hay barra, aunque desplace ese píxel.
                Layout(outside + textBottom - 1);
                Assert.Equal(Visibility.Collapsed, list.ComputedVerticalScrollBarVisibility);
                Assert.InRange(list.ScrollableHeight, 0.5, 1.5);

                // Falta lugar para un renglón: barra, y la lista se desplaza.
                Layout(outside + textBottom - 20);
                Assert.Equal(Visibility.Visible, list.ComputedVerticalScrollBarVisibility);
                Assert.True(list.ScrollableHeight >= 20, $"la lista se desplaza {list.ScrollableHeight} px");
            }
            finally
            {
                window.Close();
            }
        }));
    }

    [Theory]
    [InlineData(455, 275, true)]
    [InlineData(380, 300, false)]
    [InlineData(380, 560, false)]
    public void News_card_floats_inside_the_window_with_the_button_always_visible(double width, double height, bool compact)
    {
        RunOnSta(() => WithNews(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var vm = new MeterViewModel(NewSession(), compact: compact);
            vm.ShowDemoCommand.Execute(null);
            var window = new OverlayWindow(vm);
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();

                var card = Assert.IsType<Border>(window.FindName("NewsCard"));
                var button = Assert.IsType<Button>(window.FindName("NewsDismiss"));
                var list = Assert.IsType<ScrollViewer>(window.FindName("NewsScroller"));
                var rows = Assert.IsType<ScrollViewer>(window.FindName("ActorScroller"));
                var body = Assert.IsType<DockPanel>(window.FindName("LiveBody"));
                Assert.Equal(Visibility.Visible, card.Visibility);

                // La tarjeta y el botón quedan adentro de la ventana; el botón con su alto entero.
                var cardBounds = card.TransformToAncestor(content).TransformBounds(new Rect(card.RenderSize));
                var buttonBounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                Assert.InRange(cardBounds.Bottom, 0, height + 0.5);
                Assert.True(button.ActualHeight >= 32, $"Entendido mide {button.ActualHeight} px de alto");
                Assert.InRange(buttonBounds.Top, cardBounds.Top, height);
                Assert.InRange(buttonBounds.Bottom, buttonBounds.Top + 32, Math.Min(cardBounds.Bottom, height) + 0.5);

                // Flota: el cuerpo (y con él las filas) no le cede alto a la tarjeta.
                var bodyBounds = body.TransformToAncestor(content).TransformBounds(new Rect(body.RenderSize));
                Assert.InRange(bodyBounds.Bottom, height - 2, height + 0.5);
                Assert.True(rows.ActualHeight > 60, $"las filas quedaron en {rows.ActualHeight} px");

                // Lo que no entra se desplaza dentro de la lista (en la ventana chica no entran las seis).
                if (height < 400)
                {
                    Assert.True(list.ScrollableHeight > 0, "la lista de novedades debería desplazarse");
                }

                // Esc cierra la tarjeta.
                vm.EscapeCommand.Execute(null);
                content.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, card.Visibility);
            }
            finally
            {
                window.Close();
            }
        }));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static MeterSession NewSession() =>
        new(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { });

    private static void WithNews(Action action) => WithNews(() =>
    {
        action();
        return 0;
    });

    /// <summary>
    /// Carga las seis novedades de prueba (ids 2 a 7) mientras corre <paramref name="build"/> y deja las de antes al
    /// terminar. El arnés de capturas también la usa: el VM toma las novedades al crearse.
    /// </summary>
    internal static T WithNews<T>(Func<T> build)
    {
        var file = Path.Combine(Path.GetTempPath(), $"hysteria-news-test-{Guid.NewGuid():N}.json");
        var previous = JsonSerializer.Serialize(new { news = NewsFeed.Current.Select(n => new { id = n.Id, text = n.Text }) });
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new { news = LongNews.Select((text, i) => new { id = i + 2, text }) }));
            NewsFeed.Load(file);
            return build();
        }
        finally
        {
            File.WriteAllText(file, previous);
            NewsFeed.Load(file);
            File.Delete(file);
        }
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
