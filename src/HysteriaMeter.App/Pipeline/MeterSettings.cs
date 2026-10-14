namespace HysteriaMeter.App.Pipeline;

public enum CaptureMode
{
    Live,
    Replay,
}

public sealed class CaptureSettings
{
    public CaptureMode Mode { get; set; } = CaptureMode.Live;

    public string? ReplayPath { get; set; }

    public double ReplaySpeed { get; set; } = 1;

    public string? Device { get; set; }

    public List<string> ProcessNames { get; set; } = ["AION2"];

    public List<int> ServerPorts { get; set; } = [];

    public string? RecordPath { get; set; }

    public string OpcodesPath { get; set; } = "data/opcodes.json";
}

public sealed class MeterSettings
{
    public int IdleTimeoutSeconds { get; set; } = 8;

    public string DatabasePath { get; set; } = "hysteria-meter.db";
}
