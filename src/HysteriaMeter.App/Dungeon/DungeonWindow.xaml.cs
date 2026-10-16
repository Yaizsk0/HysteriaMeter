using System.Windows;
using System.Windows.Input;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Dungeon;

/// <summary>Modo dungeon: resumen de una run con cada boss, sus intentos y las muertes de cada jugador.</summary>
public partial class DungeonWindow : Window
{
    private readonly DungeonSummaryViewModel _viewModel;

    public DungeonWindow(DungeonSummaryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.ShareText);
            CopyButton.Content = "Copiado ✓";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Otro programa tiene el portapapeles tomado: se puede reintentar.
            CopyButton.Content = "No se pudo copiar, reintentá";
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
