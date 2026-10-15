namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Filtro para nombres leídos del protocolo: si el layout de un paquete varía, el "nombre" sale con
/// bytes de control o vacío y pisaría el nombre correcto de la entidad.
/// </summary>
internal static class PlayerNames
{
    private const int MaxLength = 32;

    public static bool IsPlausible(string name) =>
        name.Length is > 0 and <= MaxLength
        && !string.IsNullOrWhiteSpace(name)
        && !name.Any(c => char.IsControl(c) || c == '�');
}
