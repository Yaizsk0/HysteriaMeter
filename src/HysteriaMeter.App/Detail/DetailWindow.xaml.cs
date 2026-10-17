using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Detail;

/// <summary>
/// Detalle del jugador elegido en el overlay: KPIs, skills, muertes, registro y comparación. Una sola ventana que
/// cambia de jugador.
/// </summary>
public partial class DetailWindow : Window
{
    private readonly ActorDetailViewModel _viewModel;

    public DetailWindow(ActorDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        viewModel.LogRequested += ShowLog;
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) =>
        {
            viewModel.LogRequested -= ShowLog;
            viewModel.PropertyChanged -= OnViewModelChanged;
        };
    }

    private void ShowLog(LogViewModel log) => new Registro.LogWindow(log) { Owner = this }.Show();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ActorDetailViewModel.IsRenamingSkill) when _viewModel.IsRenamingSkill:
                // El nombre con todo seleccionado, para escribir encima.
                Dispatcher.BeginInvoke(() =>
                {
                    RenameBox.Focus();
                    RenameBox.SelectAll();
                }, DispatcherPriority.Input);
                break;
            case nameof(ActorDetailViewModel.DeathLogLine):
            case nameof(ActorDetailViewModel.IsDeathsTab):
                ScrollToDeath();
                break;
        }
    }

    /// <summary>El registro de la derecha muestra la línea de la muerte que se está viendo.</summary>
    private void ScrollToDeath()
    {
        if (_viewModel.IsDeathsTab && _viewModel.DeathLogLine is { } line)
        {
            Dispatcher.BeginInvoke(() => DeathsLog.ScrollIntoView(line), DispatcherPriority.Loaded);
        }
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnOpenLogClick(object sender, RoutedEventArgs e) => MoreButton.IsChecked = false;

    private void OnRenameBackdropClick(object sender, MouseButtonEventArgs e) => _viewModel.CancelSkillRenameCommand.Execute(null);

    /// <summary>El menú "⋯" se cierra con un clic fuera de él.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (MoreButton.IsChecked == true && e.OriginalSource is DependencyObject source && !IsWithin(source, MoreMenu) && !IsWithin(source, MoreButton))
        {
            MoreButton.IsChecked = false;
        }
    }

    /// <summary>En Muertes, ← y → pasan de una muerte a otra (salvo escribiendo o eligiendo en una lista).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !_viewModel.IsDeathsTab || _viewModel.IsRenamingSkill || Keyboard.FocusedElement is TextBoxBase or ComboBox or ComboBoxItem)
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Left => _viewModel.PreviousDeathCommand,
            Key.Right => _viewModel.NextDeathCommand,
            _ => null,
        };
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Esc cierra lo que esté abierto: el menú, el diálogo de renombrar y, si no hay nada, la ventana (si una caja de
    /// texto no lo usó antes, como el filtro del registro al vaciarse).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.Handled)
        {
            return;
        }

        e.Handled = true;
        if (MoreButton.IsChecked == true)
        {
            MoreButton.IsChecked = false;
        }
        else if (_viewModel.IsRenamingSkill)
        {
            _viewModel.CancelSkillRenameCommand.Execute(null);
        }
        else
        {
            Close();
        }
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject container)
    {
        while (node is not null)
        {
            if (node == container)
            {
                return true;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }
}
