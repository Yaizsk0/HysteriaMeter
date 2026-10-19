using System.IO;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace HysteriaMeter.App.Config;

/// <summary>
/// Si Npcap quedó instalado con "Restrict Npcap driver's access to Administrators only" (<c>/admin_only</c>) y el meter
/// no corre elevado. Según la guía de Npcap, con esa opción el driver crea sus dispositivos con una ACL que solo deja
/// entrar a SYSTEM y a los Administradores, y NpcapHelper.exe pide elevación (UAC) por cada proceso que abre una
/// captura. El meter corre asInvoker (app.manifest). Solo lee el registro y el token del proceso: no cambia nada.
/// </summary>
public static class NpcapAccess
{
    // Las opciones de instalación, según la guía de desarrollo de Npcap; antes de la 0.93 estaban en Services\npcap.
    private const string ParametersKey = @"SYSTEM\CurrentControlSet\Services\npcap\Parameters";
    private const string LegacyKey = @"SYSTEM\CurrentControlSet\Services\npcap";
    private const string AdminOnlyValue = "AdminOnly";

    /// <summary>De dónde sale la detección. App pone <see cref="FromSystem"/>; en los tests queda en false y no se lee el registro ni el token.</summary>
    public static Func<bool> Source { get; set; } = () => false;

    /// <summary>
    /// <c>AdminOnly</c> es un REG_DWORD y 1 es la opción marcada. Si está en Parameters manda ese; si no, el de
    /// Services\npcap. Elevado, el driver deja entrar y no hay nada que avisar.
    /// </summary>
    public static bool AdminOnlyWithoutElevation(object? parameters, object? legacy, bool elevated) =>
        !elevated && (parameters ?? legacy) is 1;

    /// <summary>Lo de la máquina: el valor del registro (solo lectura) y si el proceso está elevado. Si algo falla, no avisa.</summary>
    public static bool FromSystem()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            return AdminOnlyWithoutElevation(Read(ParametersKey), Read(LegacyKey), elevated);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static object? Read(string key)
    {
        using var opened = Registry.LocalMachine.OpenSubKey(key);
        return opened?.GetValue(AdminOnlyValue);
    }
}
