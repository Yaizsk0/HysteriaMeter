namespace HysteriaMeter.Capture.Sources;

public sealed record LiveCaptureOptions
{
    /// <summary>Nombre o descripción (parcial) de la placa. Vacío = escuchar en todas y quedarse con la que tenga tráfico del juego.</summary>
    public string? Device { get; init; }

    /// <summary>Filtro BPF aplicado en el driver. Se puede restringir a los puertos del juego una vez conocidos.</summary>
    public string BpfFilter { get; init; } = "tcp";

    /// <summary>Si se indica, todo lo capturado se graba además en este .pcap.</summary>
    public string? RecordPath { get; init; }

    public int QueueCapacity { get; init; } = 50_000;
}
