using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Tests;

public class OpcodeTableTests
{
    [Fact]
    public void Parses_hex_and_decimal_and_keeps_old_values()
    {
        var table = OpcodeTable.Parse("""
            {
              // comentarios permitidos
              "opcodes": {
                "Damage": ["0x0638", "0x0639"],
                "Bundle": [42],
              }
            }
            """);

        Assert.Equal(PacketKind.Damage, table.Resolve(0x0638));
        Assert.Equal(PacketKind.Damage, table.Resolve(0x0639));
        Assert.Equal(PacketKind.Bundle, table.Resolve(42));
        Assert.Equal(PacketKind.Unknown, table.Resolve(0xFFFF));
    }

    [Fact]
    public void Rejects_same_opcode_for_two_kinds()
    {
        Assert.Throws<InvalidDataException>(() => OpcodeTable.Parse("""{ "opcodes": { "Damage": ["0x01"], "Heal": ["0x01"] } }"""));
    }

    [Fact]
    public void Rejects_unknown_kind()
    {
        Assert.Throws<InvalidDataException>(() => OpcodeTable.Parse("""{ "opcodes": { "Teleport": ["0x01"] } }"""));
    }

    [Fact]
    public void Shipped_opcodes_file_is_valid()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "opcodes.json");
        _ = OpcodeTable.Load(path);
    }
}
