using System.Buffers.Binary;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Item level y power dentro de la aparición de otro jugador (0x3645). El paquete tiene secciones de largo
/// variable, así que se buscan por su forma (validado contra la lista de la legión, 0x8A05, que trae los dos):
/// <code>u32 personaje | 00 00 | u16 servidor | u8 n | n × (u16 tag, i32) | u32 nivel | u32 0 | u32 item level</code>
/// <code>... | ?? f4 cb 1f | u32 | u32 | [u8] | u64 power | 00</code>
/// Algunas apariciones no traen la sección del item level; el power aparece en casi todas.
/// </summary>
public static class PlayerGear
{
    public const int MaxItemLevel = 10_000;

    public const long MaxCombatPower = 10_000_000;

    private static ReadOnlySpan<byte> PowerMarker => [0xF4, 0xCB, 0x1F];

    /// <summary>Item level, o <c>null</c> si esta aparición no lo trae.</summary>
    public static int? FindItemLevel(ReadOnlySpan<byte> body)
    {
        for (var k = 0; k + 4 < body.Length; k++)
        {
            // 00 00 | u16 servidor | u8 n: el servidor puede variar, la forma de los tags no.
            if (body[k] != 0 || body[k + 1] != 0)
            {
                continue;
            }

            var count = body[k + 4];
            var start = k + 5;
            var end = start + (count * 6);
            if (count is < 1 or > 8 || end + 12 > body.Length || !LooksLikeTags(body[start..end]))
            {
                continue;
            }

            var level = BinaryPrimitives.ReadUInt32LittleEndian(body[end..]);
            var zero = BinaryPrimitives.ReadUInt32LittleEndian(body[(end + 4)..]);
            var itemLevel = BinaryPrimitives.ReadUInt32LittleEndian(body[(end + 8)..]);
            if (level is >= 1 and <= 99 && zero == 0 && itemLevel is > 0 and < MaxItemLevel)
            {
                return (int)itemLevel;
            }
        }

        return null;
    }

    /// <summary>Power (CP), o <c>null</c> si no se encuentra.</summary>
    public static long? FindCombatPower(ReadOnlySpan<byte> body)
    {
        var marker = body.LastIndexOf(PowerMarker);
        if (marker < 0)
        {
            return null;
        }

        // A veces hay un byte extra antes del u64: se prueba con y sin él.
        for (var skip = 0; skip <= 1; skip++)
        {
            var at = marker + PowerMarker.Length + 8 + skip;
            if (at + 8 > body.Length)
            {
                break;
            }

            var power = BinaryPrimitives.ReadUInt64LittleEndian(body[at..]);
            if (power is > 0 and < MaxCombatPower)
            {
                return (long)power;
            }
        }

        return null;
    }

    // Cada tag es u16 chico (byte alto 0, salvo 0x0227) seguido de un i32.
    private static bool LooksLikeTags(ReadOnlySpan<byte> tags)
    {
        for (var i = 0; i < tags.Length; i += 6)
        {
            if (tags[i + 1] != 0 && !(tags[i] == 0x27 && tags[i + 1] == 0x02))
            {
                return false;
            }
        }

        return true;
    }
}
