using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

[Collection("Overlay WPF")]
public sealed class OverlayMenuTests
{
    [Fact]
    public void More_menu_builds_its_items_with_the_overlay_styles()
    {
        // El menú "⋯" se armaba con un ItemContainerStyle de MenuItem, que WPF también les aplica a los Separator y tira
        // InvalidOperationException al abrirlo: la app se cerraba. Se mide el menú sin abrir el popup, que es donde WPF
        // genera los contenedores de los ítems.
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var window = new OverlayWindow(new MeterViewModel(session));
            try
            {
                var button = Assert.IsType<Button>(window.FindName("MoreButton"));
                var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
                menu.PlacementTarget = button;

                menu.Measure(new Size(400, 800));

                var items = menu.Items.Cast<object>().ToList();
                var menuItems = items.OfType<MenuItem>().ToList();
                var separators = items.OfType<Separator>().ToList();
                Assert.Equal(2, separators.Count);
                Assert.NotEmpty(menuItems);
                foreach (var item in menuItems)
                {
                    // El template de LiveMenuItem, no el del tema de Windows.
                    item.ApplyTemplate();
                    Assert.NotNull(item.Template.FindName("ItemFrame", item));
                }

                foreach (var separator in separators)
                {
                    // La línea del diseño (1 px con el color Track), no el separador del tema de Windows.
                    separator.ApplyTemplate();
                    var line = Assert.IsType<Border>(VisualTreeHelper.GetChild(separator, 0));
                    Assert.Equal(1, line.Height);
                }
            }
            finally
            {
                window.Close();
            }
        });
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
