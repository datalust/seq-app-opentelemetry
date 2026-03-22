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
        // Pad with leading zeros if shorter than expected
        if (hex.Length < expectedLength * 2)
            hex = hex.PadLeft(expectedLength * 2, '0');

        var bytes = new byte[expectedLength];
        for (var i = 0; i < expectedLength; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return ByteString.CopyFrom(bytes);
    }
}
