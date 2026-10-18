using System.Windows;
using System.Windows.Threading;

namespace HysteriaMeter.App.Tests;

/// <summary>La App de WPF de los tests: los recursos de App.xaml, sin arrancar el meter.</summary>
internal static class TestApp
{
    /// <summary>
    /// La App actual o una nueva sin su OnStartup. El constructor de Application encola OnStartup en el Dispatcher (no
    /// hace falta Run) y cualquier bombeo lo ejecutaba: levantaba el host con la captura, reescribía el inicio con
    /// Windows apuntando al testhost, abría el overlay guardado, aplicaba el tema y la fuente del usuario, cargaba las
    /// novedades reales y abría la base de %LOCALAPPDATA%. Se aborta esa operación antes de bombear. Una subclase con
    /// OnStartup vacío no sirve: el InitializeComponent de App.xaml no carga en un tipo de otro ensamblado.
    /// </summary>
    public static App CurrentOrWithoutStartup() => Application.Current as App ?? WithoutStartup();

    private static App WithoutStartup()
    {
        var posted = new List<DispatcherOperation>();
        void Track(object? sender, DispatcherHookEventArgs e) => posted.Add(e.Operation);
        var hooks = Dispatcher.CurrentDispatcher.Hooks;
        hooks.OperationPosted += Track;
        App app;
        try
        {
            app = new App();
        }
        finally
        {
            hooks.OperationPosted -= Track;
        }

        // Si WPF dejara de encolarlo así, OnStartup volvería a correr sin aviso: mejor que el test falle. Antes se apaga
        // la App, porque el arranque que quede en la cola no corre si ya se está apagando.
        if (posted.Count == 0 || !posted.TrueForAll(operation => operation.Abort()))
        {
            app.Shutdown();
            throw new InvalidOperationException("No se pudo frenar el OnStartup que encola el constructor de la App.");
        }

        return app;
    }
}
