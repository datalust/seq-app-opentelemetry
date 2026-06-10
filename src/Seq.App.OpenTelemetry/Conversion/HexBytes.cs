using Google.Protobuf;

namespace Seq.App.OpenTelemetry.Conversion;

static class HexBytes
{
    static readonly ByteString Empty = ByteString.Empty;

    public static ByteString ToTraceId(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Empty;

        return ToByteString(hex, 16);
    }

    public static ByteString ToSpanId(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Empty;

        return ToByteString(hex, 8);
    }

    static ByteString ToByteString(string hex, int expectedLength)
    {
        // OTLP treats absent and invalid ids equivalently, so rather than throwing (and losing the
        // whole event), unparseable values are omitted.
        if (hex.Length > expectedLength * 2)
            return Empty;

        // Pad with leading zeros if shorter than expected
        if (hex.Length < expectedLength * 2)
            hex = hex.PadLeft(expectedLength * 2, '0');

        var bytes = new byte[expectedLength];
        for (var i = 0; i < expectedLength; i++)
        {
            var hi = HexDigit(hex[i * 2]);
            var lo = HexDigit(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0)
                return Empty;

            bytes[i] = (byte)(hi << 4 | lo);
        }

        return ByteString.CopyFrom(bytes);
    }

    static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };
}
