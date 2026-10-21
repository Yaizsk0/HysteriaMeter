using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace HysteriaMeter.App.Update;

/// <summary>
/// Volver a abrir el meter: después de instalar una versión al arrancar, o desde la bandeja para instalar la que ya está
/// bajada. En el segundo caso la instancia nueva espera a que cierre la vieja (<see cref="WaitArgument"/>), así no
/// corren las dos juntas ni se pisan al guardar las preferencias y las ventanas.
/// </summary>
public static class AppRestart
{
    public const string WaitArgument = "--esperar-cierre";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    /// <summary>Los argumentos sin el par "--esperar-cierre &lt;pid&gt;", y ese pid si estaba.</summary>
    public static (string[] Args, int? Pid) Split(IReadOnlyList<string> args)
    {
        var rest = new List<string>();
        int? pid = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == WaitArgument && i + 1 < args.Count)
            {
                pid = int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
                i++;
                continue;
            }

            rest.Add(args[i]);
        }

        return ([.. rest], pid);
    }

    /// <summary>Si lo abrió el reinicio de la bandeja, espera (hasta 15 s) a que cierre el meter anterior.</summary>
    public static string[] WaitForPrevious(IReadOnlyList<string> args)
    {
        var (rest, pid) = Split(args);
        if (pid is { } id && id != Environment.ProcessId)
        {
            try
            {
                using var previous = Process.GetProcessById(id);
                previous.WaitForExit(WaitLimit);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Ya cerró.
            }
        }

        return rest;
    }

    /// <summary>
    /// Abre otra vez este .exe con <paramref name="args"/>. Sin shell: así Windows no le pide confirmación a un .exe que
    /// bajó el propio meter. Devuelve <c>false</c> si no se pudo.
    /// </summary>
    public static bool Start(IEnumerable<string> args)
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return false;
        }

        var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Reinicio desde la bandeja: abre el meter de nuevo (que espera a este) con los mismos argumentos.</summary>
    public static bool StartAfterThis()
    {
        var (rest, _) = Split(Environment.GetCommandLineArgs().Skip(1).ToArray());
        return Start([.. rest, WaitArgument, Environment.ProcessId.ToString(CultureInfo.InvariantCulture)]);
    }
}
