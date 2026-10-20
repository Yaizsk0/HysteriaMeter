using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HysteriaMeter.App.Themes;

/// <summary>
/// Barra de daño que se desliza en vez de saltar: va en el ScaleTransform de la barra en lugar de bindear ScaleX
/// (<c>ui:SmoothBar.ScaleX="{Binding Bar}"</c>). El primer valor se pone directo, así una fila nueva no arranca en la
/// escala 1 del transform y un contenedor que se regenera no anima. Los siguientes llegan en <see cref="Duration"/> con
/// salida suave, desde donde esté la barra en ese momento: con el refresco de 100 o 250 ms el siguiente valor llega antes
/// de terminar y la barra se mueve sin cortes. El valor base queda en el destino y la animación se suelta al terminar.
/// Las filas son por posición: si dos jugadores cambian de puesto, la barra va del largo del anterior al del nuevo.
/// </summary>
public static class SmoothBar
{
    /// <summary>Largo del deslizamiento: un poco más que el refresco por defecto (250 ms).</summary>
    public static readonly Duration Duration = new(TimeSpan.FromMilliseconds(350));

    /// <summary>
    /// Todas las ventanas son transparentes (layered): cada cuadro se copia entero a la pantalla. A 30 cuadros por
    /// segundo se ve fluido y cuesta la mitad que los 60 por defecto mientras se juega.
    /// </summary>
    public const int FrameRate = 30;

    public static readonly DependencyProperty ScaleXProperty = DependencyProperty.RegisterAttached(
        "ScaleX", typeof(double), typeof(SmoothBar), new PropertyMetadata(double.NaN, OnScaleXChanged));

    private static readonly IEasingFunction Ease = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Windows con "Mostrar animaciones" apagado: la barra salta como antes. Los tests lo reemplazan.</summary>
    internal static Func<bool> Enabled { get; set; } = () => SystemParameters.ClientAreaAnimation;

    public static double GetScaleX(DependencyObject element) => (double)element.GetValue(ScaleXProperty);

    public static void SetScaleX(DependencyObject element, double value) => element.SetValue(ScaleXProperty, value);

    private static void OnScaleXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScaleTransform { IsFrozen: false } transform)
        {
            return;
        }

        var target = (double)e.NewValue;
        if (double.IsNaN(target))
        {
            return;
        }

        var from = transform.ScaleX;
        transform.ScaleX = target;
        if (double.IsNaN((double)e.OldValue) || !Enabled() || from == target)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            return;
        }

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, Slide(from, target), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Del largo actual al nuevo, con salida suave y a <see cref="FrameRate"/>; al terminar queda el valor base.</summary>
    internal static DoubleAnimation Slide(double from, double to)
    {
        var animation = new DoubleAnimation(from, to, Duration) { EasingFunction = Ease, FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        animation.Freeze();
        return animation;
    }

    private static IEasingFunction Frozen(CubicEase ease)
    {
        ease.Freeze();
        return ease;
    }
}
