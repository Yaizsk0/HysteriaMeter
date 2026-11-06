namespace HysteriaMeter.Capture.Tests;

/// <summary>
/// Un test que escribe o lee un .pcap con SharpPcap, que carga wpcap.dll de Npcap. El runner de Windows del CI no trae
/// Npcap: ahí el test sale como salteado en vez de fallar.
/// </summary>
public sealed class NpcapFactAttribute : FactAttribute
{
    public NpcapFactAttribute()
    {
        if (!Installed)
        {
            Skip = "Necesita Npcap (wpcap.dll), que el CI no tiene";
        }
    }

    private static bool Installed =>
        OperatingSystem.IsWindows() &&
        (File.Exists(Path.Combine(Environment.SystemDirectory, "Npcap", "wpcap.dll")) ||
         File.Exists(Path.Combine(Environment.SystemDirectory, "wpcap.dll")));
}
