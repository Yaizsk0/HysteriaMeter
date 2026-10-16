using Microsoft.Win32;

namespace HysteriaMeter.App;

/// <summary>
/// "Iniciar con Windows": una entrada en la clave Run del usuario (no pide administrador; el meter corre como el usuario).
/// Si el programa cambia de carpeta, al abrirlo se actualiza la ruta.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string ValueName = "HysteriaMeter";

    /// <summary>Comando que guarda Windows: la ruta del .exe entre comillas (puede tener espacios).</summary>
    public static string CommandFor(string exePath) => $"\"{exePath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, CommandFor(exePath));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Si está prendido y la ruta guardada no es la de este .exe (se movió la carpeta), la corrige.</summary>
    public static void Refresh(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && current != CommandFor(exePath))
        {
            key.SetValue(ValueName, CommandFor(exePath));
        }
    }
}
