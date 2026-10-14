namespace HysteriaMeter.Capture;

public static class FlowDirection
{
    /// <summary>
    /// Heurística: el servidor escucha en un puerto fijo más bajo que el puerto efímero del cliente.
    /// Solo decodificamos servidor → cliente; lo que manda el cliente no es legible (parece cifrado)
    /// y decodificarlo podría generar eventos falsos.
    /// </summary>
    public static bool IsFromServer(this FlowKey flow) => flow.Source.Port < flow.Destination.Port;
}
