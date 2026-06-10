using Google.Protobuf;
using Seq.App.OpenTelemetry.Conversion;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class HexBytesTests
{
    [Fact]
    public void ParsesFullLengthTraceId()
    {
        var traceId = HexBytes.ToTraceId("0af7651916cd43dd8448eb211c80319c");

        Assert.Equal(ByteString.CopyFrom(
            0x0a, 0xf7, 0x65, 0x19, 0x16, 0xcd, 0x43, 0xdd,
            0x84, 0x48, 0xeb, 0x21, 0x1c, 0x80, 0x31, 0x9c), traceId);
    }

    [Fact]
    public void ParsesUppercaseHex()
    {
        var spanId = HexBytes.ToSpanId("00F067AA0BA902B7");

        Assert.Equal(ByteString.CopyFrom(0x00, 0xf0, 0x67, 0xaa, 0x0b, 0xa9, 0x02, 0xb7), spanId);
    }

    [Fact]
    public void PadsShortValuesWithLeadingZeros()
    {
        var spanId = HexBytes.ToSpanId("abc");

        Assert.Equal(ByteString.CopyFrom(0, 0, 0, 0, 0, 0, 0x0a, 0xbc), spanId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingValuesAreEmpty(string? hex)
    {
        Assert.Equal(ByteString.Empty, HexBytes.ToTraceId(hex));
        Assert.Equal(ByteString.Empty, HexBytes.ToSpanId(hex));
    }

    [Theory]
    [InlineData("not hex at all!")]
    [InlineData("00-f067aa0ba902b7")]
    [InlineData("00f067aa0ba902b7ff")]
    public void InvalidValuesAreOmittedRatherThanThrowing(string hex)
    {
        // OTLP treats absent and invalid ids equivalently; a malformed id shouldn't cost the
        // whole event.
        Assert.Equal(ByteString.Empty, HexBytes.ToSpanId(hex));
    }
}
