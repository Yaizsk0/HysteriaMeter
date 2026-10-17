namespace HysteriaMeter.App.Config;

/// <summary>
/// Secciones de la navegación lateral de la configuración, en el orden en que se ven. El view model elige en cuál se abre
/// (<see cref="ViewModels.MeterViewModel.SettingsSection"/>).
/// </summary>
public enum ConfigSection
{
    Apariencia,
    Columnas,
    Alertas,
    Atajos,
    Captura,
    DatosDelJuego,
    Historial,
    Diagnostico,
}
