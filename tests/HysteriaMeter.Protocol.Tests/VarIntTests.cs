using HysteriaMeter.Protocol.Framing;

namespace HysteriaMeter.Protocol.Tests;

public class VarIntTests
{
    [Theory]
    [InlineData(0UL, new byte[] { 0x00 })]
    [InlineData(1UL, new byte[] { 0x01 })]
    [InlineData(127UL, new byte[] { 0x7F })]
    [InlineData(128UL, new byte[] { 0x80, 0x01 })]
    [InlineData(300UL, new byte[] { 0xAC, 0x02 })]
    [InlineData(ulong.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
    public void Reads_and_writes_known_values(ulong value, byte[] encoded)
    {
        Assert.True(VarInt.TryRead(encoded, out var read, out var size));
        Assert.Equal(value, read);
        Assert.Equal(encoded.Length, size);

        var buffer = new byte[VarInt.MaxBytes];
        Assert.Equal(encoded.Length, VarInt.Write(buffer, value));
        Assert.Equal(encoded, buffer[..encoded.Length]);
        Assert.Equal(encoded.Length, VarInt.SizeOf(value));
    }

    [Fact]
    public void Incomplete_varint_returns_false()
    {
        Assert.False(VarInt.TryRead([0x80, 0x80], out _, out var size));
        Assert.Equal(0, size);
    }

    [Fact]
    public void Too_long_varint_throws()
    {
        var bytes = Enumerable.Repeat((byte)0x80, 11).ToArray();
        Assert.Throws<InvalidDataException>(() => VarInt.TryRead(bytes, out _, out _));
    }

    [Fact]
    public void Reader_decodes_zigzag()
    {
        var reader = new PacketReader([0x03, 0x04]);
        Assert.Equal(-2, reader.ReadVarInt());
        Assert.Equal(2, reader.ReadVarInt());
        Assert.Equal(0, reader.Remaining);
    }
}
