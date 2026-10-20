using System.Windows;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Overlay;

/// <summary>
/// Las ventanas del overlay, como en Details!: una principal y las que se agreguen con el botón +,
/// cada una con su propia vista (daño, curación, daño con HPS). Cerrar la principal cierra la app.
/// </summary>
public sealed class OverlayWindows(MeterSession session)
{
    private readonly List<OverlayWindow> _windows = [];
    private bool _closingAll;

    public IReadOnlyList<OverlayWindow> All => _windows;

    /// <summary>Abre las ventanas de la sesión anterior (o una sola de daño) y devuelve la principal.</summary>
    public OverlayWindow OpenSaved()
    {
        var (preferences, layouts) = Migrate(session.Preferences, OverlayLayout.LoadAll());
        if (!ReferenceEquals(preferences, session.Preferences))
        {
            session.Update(preferences);
            OverlayLayout.SaveAll(layouts);
        }

        if (layouts.Count == 0)
        {
            return Open(null);
        }

        foreach (var layout in layouts)
        {
            Open(layout);
        }

        return _windows[0];
    }

    /// <summary>
    /// Cambios de una sola vez sobre lo guardado por versiones anteriores; devuelve las mismas preferencias (la misma
    /// instancia) si no hay nada que hacer.
    /// <list type="bullet">
    /// <item>"Solo boss" pasó a venir prendido (como cuenta Abyss): se prende en las ventanas guardadas.</item>
    /// <item>
    /// El rediseño: las preferencias de antes (modo compacto, línea de DPS, item level, columnas TOTAL, CRIT y MÁX...)
    /// dejaban el overlay lejos del mock aunque el código ya lo dibujara. Pasan a la vista de fábrica y las ventanas salen
    /// del compacto con el ancho del mock. Lo que se vuelva a prender después en Configuración se respeta.
    /// </item>
    /// </list>
    /// </summary>
    public static (MeterPreferences Preferences, IReadOnlyList<OverlayLayout> Layouts) Migrate(
        MeterPreferences preferences, IReadOnlyList<OverlayLayout> layouts)
    {
        if (!preferences.BossOnlyDefaulted)
        {
            layouts = [.. layouts.Select(l => l with { BossOnly = true })];
            preferences = preferences with { BossOnlyDefaulted = true };
        }

        if (!preferences.RedesignViewApplied)
        {
            layouts = [.. layouts.Select(l => l.WithFactoryView())];
            preferences = preferences.WithFactoryView() with { RedesignViewApplied = true };
        }

        return (preferences, layouts);
    }

    /// <summary>Ventana nueva al lado de <paramref name="from"/>, mostrando curación para que se note la diferencia.</summary>
    public OverlayWindow OpenNext(OverlayWindow from)
    {
        var layout = from.CurrentLayout() is { } current
            ? current with { Left = current.Left + 40, Top = current.Top + 40, Tab = MeterTab.Healing, ShowHealing = false }
            : null;
        return Open(layout);
    }

    public void SaveAll() =>
        OverlayLayout.SaveAll(_windows.Select(w => w.CurrentLayout()).OfType<OverlayLayout>());

    /// <summary>Ejecuta una acción en todas las ventanas (atajos globales: bloquear, ocultar).</summary>
    public void ForEach(Action<OverlayWindow> action)
    {
        foreach (var window in _windows.ToList())
        {
            action(window);
        }
    }

    private OverlayWindow Open(OverlayLayout? layout)
    {
        var viewModel = new MeterViewModel(
            session,
            layout?.Tab ?? MeterTab.Damage,
            layout?.ShowHealing ?? true,
            layout?.Compact ?? false,
            layout?.Session ?? false,
            layout?.BossOnly ?? true,
            layout?.Run ?? false);
        var window = new OverlayWindow(viewModel, layout, this);
        viewModel.NewWindowRequested += () => OpenNext(window);
        window.Closing += (_, _) => OnClosing(window);
        _windows.Add(window);
        window.Show();
        return window;
    }

    private void OnClosing(OverlayWindow window)
    {
        if (_closingAll)
        {
            return;
        }

        if (_windows.Count == 0 || !ReferenceEquals(_windows[0], window))
        {
            // Una ventana extra se cierra sola y deja de abrirse en la próxima sesión.
            _windows.Remove(window);
            SaveAll();
            return;
        }

        // La principal: se guarda todo y se cierran las demás sin volver a guardar.
        SaveAll();
        _closingAll = true;
        foreach (var other in _windows.Skip(1).ToList())
        {
            _windows.Remove(other);
            other.Close();
        }

        _windows.Clear();
        Application.Current?.Shutdown();
    }
}
