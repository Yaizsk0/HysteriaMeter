using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.Themes;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Escala de la interfaz (Themes/UiScale): las ventanas, el overlay y los popups la siguen en vivo. El recurso se pone en
/// la ventana o en el popup y no en la App, así los demás tests siguen en 100 %.
/// </summary>
[Collection("Overlay WPF")]
public sealed class UiScaleTests
{
    [Fact]
    public void Scale_outside_one_to_two_goes_back_to_one()
    {
        Assert.Equal(1.25, UiScale.Normalize(1.25));
        Assert.Equal(2, UiScale.Normalize(2));
        Assert.Equal(1, UiScale.Normalize(0.5));
        Assert.Equal(1, UiScale.Normalize(2.5));
        Assert.Equal(1, UiScale.Normalize(double.NaN));
    }

    [Fact]
    public void Preference_is_saved_normalized_and_stays_out_of_the_profile()
    {
        RunOnSta(() =>
        {
            var session = Session();
            var changed = new List<string>();
            session.PreferencesChanged += names => changed.AddRange(names);
            var vm = new MeterViewModel(session);
            Assert.Equal(1, vm.UiScale);

            vm.UiScale = 1.25;
            Assert.Equal(1.25, session.Preferences.UiScale);
            Assert.Contains(nameof(MeterViewModel.UiScale), changed);

            vm.UiScale = 5;
            Assert.Equal(1, session.Preferences.UiScale);
        });

        // La escala depende del monitor: el perfil no la lleva y al importarlo queda la de esta PC.
        var json = ProfileFile.ToJson(new MeterPreferences { UiScale = 1.5 });
        Assert.Equal(1, ProfileFile.FromJson(json, new MeterPreferences())!.UiScale);
        Assert.Equal(1.25, ProfileFile.FromJson(json, new MeterPreferences { UiScale = 1.25 })!.UiScale);
    }

    [Fact]
    public void Window_grows_with_its_content_and_goes_back()
    {
        RunOnSta(() =>
        {
            var window = new ConfigWindow(new MeterViewModel(Session()));
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                Assert.True(content.LayoutTransform.Value.IsIdentity);
                var area = SystemParameters.WorkArea;

                window.Resources[UiScale.ResourceKey] = 1.25;
                Assert.Equal(1.25, UiScale.GetFactor(window));
                Assert.Equal(1.25, content.LayoutTransform.Value.M11, 3);
                Assert.Equal(1.25, content.LayoutTransform.Value.M22, 3);
                Assert.Equal(Math.Min(1040 * 1.25, area.Width), window.Width, 3);
                Assert.Equal(Math.Min(700 * 1.25, area.Height), window.Height, 3);
                Assert.Equal(Math.Min(880 * 1.25, area.Width), window.MinWidth, 3);
                Assert.Equal(Math.Min(480 * 1.25, area.Height), window.MinHeight, 3);

                // De vuelta en 100 %: el mínimo es el original aunque la pantalla lo haya acotado.
                window.Resources[UiScale.ResourceKey] = 1.0;
                Assert.True(content.LayoutTransform.Value.IsIdentity);
                Assert.Equal(880, window.MinWidth, 3);
                Assert.Equal(480, window.MinHeight, 3);
                Assert.Equal(Math.Min(1040 * 1.25, area.Width) / 1.25, window.Width, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Overlay_adds_the_scale_to_its_zoom_and_saves_the_layout_without_it()
    {
        RunOnSta(() =>
        {
            var window = new OverlayWindow(new MeterViewModel(Session()), new OverlayLayout(Left: 100, Top: 100, Width: 400, Zoom: 1.2, Height: 320));
            try
            {
                window.Left = 100;
                window.Top = 100;
                var zoom = Assert.IsType<ScaleTransform>(window.FindName("Zoom"));
                Assert.Equal(1.2, zoom.ScaleX, 3);

                window.Resources[UiScale.ResourceKey] = 1.5;
                Assert.Equal(1.8, zoom.ScaleX, 3);
                Assert.Equal(1.8, zoom.ScaleY, 3);
                Assert.Equal(600, window.Width, 3);
                Assert.Equal(480, window.Height, 3);
                Assert.Equal(390, window.MinWidth, 3);
                Assert.Equal(1350, window.MaxWidth, 3);

                // No pasa por la escala de las demás ventanas: su contenido sigue con el zoom propio, sin escalar dos veces.
                Assert.Same(zoom, Assert.IsAssignableFrom<FrameworkElement>(window.FindName("Root")).LayoutTransform);

                // Lo guardado va sin la escala: en otra PC (o en 100 %) se abre igual que antes.
                var layout = Assert.IsType<OverlayLayout>(window.CurrentLayout());
                Assert.Equal(400, layout.Width, 3);
                Assert.Equal(320, layout.Height, 3);
                Assert.Equal(1.2, layout.Zoom, 3);

                window.Resources[UiScale.ResourceKey] = 1.0;
                Assert.Equal(1.2, zoom.ScaleX, 3);
                Assert.Equal(400, window.Width, 3);
                Assert.Equal(320, window.Height, 3);
                Assert.Equal(260, window.MinWidth, 3);
                Assert.Equal(900, window.MaxWidth, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Tooltips_dropdowns_and_the_overlay_menu_follow_the_scale()
    {
        // Se dibujan en su propia ventana: la transformación de la de abajo no les llega.
        RunOnSta(() =>
        {
            var vm = new MeterViewModel(Session());
            var app = Application.Current;

            var tooltip = new ToolTip { Style = (Style)app.FindResource("ToolTipDark") };
            tooltip.Resources[UiScale.ResourceKey] = 1.25;
            Assert.Equal(1.25, tooltip.LayoutTransform.Value.M11, 3);

            var combo = new ComboBox { Style = (Style)app.FindResource("DarkComboBox") };
            Assert.True(combo.ApplyTemplate());
            var dropdown = Assert.IsType<Border>(Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo)).Child);
            dropdown.Resources[UiScale.ResourceKey] = 1.25;
            Assert.Equal(1.25, dropdown.LayoutTransform.Value.M11, 3);

            var window = new OverlayWindow(vm);
            try
            {
                var menu = Assert.IsType<ContextMenu>(Assert.IsType<Button>(window.FindName("MoreButton")).ContextMenu);
                menu.Resources[UiScale.ResourceKey] = 1.25;
                Assert.Equal(1.25, menu.LayoutTransform.Value.M11, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(1120, true)]
    [InlineData(900, false)]
    public void Detail_hides_the_modifier_columns_by_the_unscaled_content_width(double width, bool visible)
    {
        // Al 150 % la ventana mide 1,5 veces el contenido: las columnas PERF. a MULTI dependen del contenido, que es lo
        // que tiene lugar para ellas (con el ancho de la ventana quedaban apretando la columna SKILL).
        RunOnSta(() =>
        {
            var window = new DetailWindow(new MeterViewModel(Session()).Detail);
            try
            {
                window.Resources[UiScale.ResourceKey] = 1.5;
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(width * 1.5, 680 * 1.5));
                content.Arrange(new Rect(0, 0, width * 1.5, 680 * 1.5));
                content.UpdateLayout();
                Assert.InRange(content.ActualWidth, width - 1, width + 1);

                var perfect = Logical(content).OfType<TextBlock>().First(t => t.Text == "PERF.");
                Assert.Equal(visible ? Visibility.Visible : Visibility.Collapsed, perfect.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static MeterSession Session()
    {
        Application.ResourceAssembly ??= typeof(App).Assembly;
        TestApp.CurrentOrWithoutStartup().InitializeComponent();
        return new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { });
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Logical(child))
            {
                yield return descendant;
            }
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
