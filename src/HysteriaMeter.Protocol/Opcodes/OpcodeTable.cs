using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace HysteriaMeter.Protocol.Opcodes;

/// <summary>
/// Mapa opcode → tipo de paquete, cargado de <c>opcodes.json</c>. Cada tipo acepta una lista de opcodes
/// para que convivan los valores de antes y después de un parche.
/// </summary>
public sealed class OpcodeTable
{
    private readonly FrozenDictionary<ushort, PacketKind> _map;

    public OpcodeTable(IReadOnlyDictionary<PacketKind, IReadOnlyList<ushort>> opcodes)
    {
        var map = new Dictionary<ushort, PacketKind>();
        foreach (var (kind, values) in opcodes)
        {
            foreach (var opcode in values)
            {
                if (!map.TryAdd(opcode, kind) && map[opcode] != kind)
                {
                    throw new InvalidDataException($"El opcode 0x{opcode:X4} está asignado a {map[opcode]} y a {kind}.");
                }
            }
        }

        _map = map.ToFrozenDictionary();
    }

    public static OpcodeTable Empty { get; } = new(new Dictionary<PacketKind, IReadOnlyList<ushort>>());

    public int Count => _map.Count;

    public PacketKind Resolve(ushort opcode) => _map.GetValueOrDefault(opcode, PacketKind.Unknown);

    public static OpcodeTable Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// Formato: <c>{ "opcodes": { "Damage": ["0x0638", "0x0639"], ... } }</c>. Se aceptan valores hex ("0x..") o decimales.
    /// </summary>
    public static OpcodeTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var result = new Dictionary<PacketKind, IReadOnlyList<ushort>>();
        if (!doc.RootElement.TryGetProperty("opcodes", out var opcodes))
        {
            return new OpcodeTable(result);
        }

        foreach (var entry in opcodes.EnumerateObject())
        {
            if (!Enum.TryParse<PacketKind>(entry.Name, ignoreCase: true, out var kind) || kind == PacketKind.Unknown)
            {
                throw new InvalidDataException($"Tipo de paquete desconocido en opcodes.json: '{entry.Name}'.");
            }

            result[kind] = entry.Value.EnumerateArray().Select(ParseOpcode).ToArray();
        }

        return new OpcodeTable(result);
    }

    private static ushort ParseOpcode(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetUInt16();
        }

        var text = value.GetString() ?? throw new InvalidDataException("Opcode vacío.");
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ushort.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : ushort.Parse(text, CultureInfo.InvariantCulture);
    }
}
