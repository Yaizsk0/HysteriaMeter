using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HysteriaMeter.App.Themes;

/// <summary>Una ventana que suma la escala a su propio manejo del tamaño (el overlay, con su zoom de Ctrl + rueda).</summary>
public interface IUiScaled
{
    void ApplyUiScale(double before, double after);
}

/// <summary>
/// Escala de la interfaz (Configuración → Apariencia): agranda texto y controles de todas las ventanas con un
/// LayoutTransform sobre su contenido, sin tocar los tamaños de fuente de cada estilo. El valor vive en el recurso
/// <see cref="ResourceKey"/> de la App y cada ventana lo sigue desde su constructor (<see cref="Follow"/>): cambiarlo
/// reescala en vivo lo que esté abierto. Los popups (tooltips, desplegables y menús) se dibujan en su propia ventana y
/// no heredan la transformación de la de abajo: Theme.xaml y el overlay les ponen <c>Factor</c> con el mismo recurso.
/// Sin el recurso (los tests sin App) queda en 1.
/// </summary>
public static class UiScale
{
    public const string ResourceKey = "UiScale";

    public static readonly DependencyProperty FactorProperty = DependencyProperty.RegisterAttached(
        "Factor", typeof(double), typeof(UiScale), new PropertyMetadata(1.0, OnFactorChanged));

    /// <summary>Tamaño mínimo de cada ventana sin escalar: acotado a la pantalla, ir y volver no lo achica.</summary>
    private static readonly ConditionalWeakTable<Window, StrongBox<Size>> BaseMinimum = new();

    public static double GetFactor(DependencyObject element) => (double)element.GetValue(FactorProperty);

    public static void SetFactor(DependencyObject element, double value) => element.SetValue(FactorProperty, value);

    /// <summary>
    /// El elemento sigue el recurso UiScale. En una ventana va después de InitializeComponent: escala su contenido y,
    /// si todavía no se mostró, el tamaño con el que se abre.
    /// </summary>
    public static void Follow(FrameworkElement element) => element.SetResourceReference(FactorProperty, ResourceKey);

    /// <summary>Una escala fuera de 1–2 (un archivo editado a mano, por ejemplo) vuelve a 1.</summary>
    public static double Normalize(double value) => value is >= 1 and <= 2 ? value : 1;

    private static void OnFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var before = Normalize((double)e.OldValue);
        var after = Normalize((double)e.NewValue);
        switch (d)
        {
            case IUiScaled scaled:
                scaled.ApplyUiScale(before, after);
                break;
            case Window window:
                Scale(window, before, after);
                break;
            case FrameworkElement element:
                element.LayoutTransform = ScaleOf(after);
                break;
        }
    }

    private static Transform ScaleOf(double scale) => scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);

    /// <summary>
    /// El contenido se agranda y la ventana con él, para que no se apriete: el tamaño y el mínimo crecen en la misma
    /// proporción, sin pasarse del área de trabajo (sin barra de título, una ventana más grande que la pantalla no se
    /// puede reacomodar). El lado que sigue al contenido (SizeToContent) se acomoda solo.
    /// </summary>
    private static void Scale(Window window, double before, double after)
    {
        if (window.Content is FrameworkElement content)
        {
            content.LayoutTransform = ScaleOf(after);
        }

        var minimum = BaseMinimum.GetValue(window, w => new StrongBox<Size>(new Size(w.MinWidth / before, w.MinHeight / before))).Value;
        var area = WorkArea(window);
        var ratio = after / before;
        if (window.SizeToContent is not (SizeToContent.Width or SizeToContent.WidthAndHeight))
        {
            window.MinWidth = Math.Min(minimum.Width * after, area.Width);
            if (!double.IsNaN(window.Width))
            {
                window.Width = Math.Min(window.Width * ratio, area.Width);
            }
        }

        if (window.SizeToContent is not (SizeToContent.Height or SizeToContent.WidthAndHeight))
        {
            window.MinHeight = Math.Min(minimum.Height * after, area.Height);
            if (!double.IsNaN(window.Height))
            {
                window.Height = Math.Min(window.Height * ratio, area.Height);
            }
        }

        // Abierta, la que creció no queda colgando fuera de su monitor.
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            if (!double.IsNaN(window.Left) && !double.IsNaN(window.Width))
            {
                window.Left = Math.Max(area.Left, Math.Min(window.Left, area.Right - window.Width));
            }

            if (!double.IsNaN(window.Top) && !double.IsNaN(window.Height))
            {
                window.Top = Math.Max(area.Top, Math.Min(window.Top, area.Bottom - window.Height));
            }
        }
    }

    /// <summary>Área de trabajo del monitor de la ventana, en unidades de WPF; la del principal si no se mostró.</summary>
    private static Rect WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return SystemParameters.WorkArea;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
    }
}
