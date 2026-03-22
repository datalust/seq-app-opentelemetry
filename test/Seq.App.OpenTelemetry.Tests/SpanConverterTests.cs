using Google.Protobuf;
using Seq.App.OpenTelemetry.Conversion;
using Seq.App.OpenTelemetry.Parsing;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class SpanConverterTests
{
    const string BaseSpanJson = """
    {
        "@t":"2024-01-15T10:30:01Z",
        "@tr":"0af7651916cd43dd8448eb211c80319c",
        "@sp":"00f067aa0ba902b7",
        "@st":"2024-01-15T10:30:00Z",
        "@mt":"GET /api/users"
    }
    """;

    [Fact]
    public void SetsTraceAndSpanIds()
    {
        using var evt = ClefParser.Parse(BaseSpanJson);
        var span = SpanConverter.Convert(evt);

        Assert.Equal(16, span.TraceId.Length);
        Assert.Equal(8, span.SpanId.Length);
        Assert.Equal(ByteString.CopyFrom(
            0x0a, 0xf7, 0x65, 0x19, 0x16, 0xcd, 0x43, 0xdd,
            0x84, 0x48, 0xeb, 0x21, 0x1c, 0x80, 0x31, 0x9c), span.TraceId);
    }

    [Fact]
    public void SetsParentSpanId()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"0af7651916cd43dd8448eb211c80319c",
            "@sp":"00f067aa0ba902b7",
            "@ps":"b7ad6b7169203331",
            "@st":"2024-01-15T10:30:00Z",
            "@mt":"GET /api/users"
        }
        """);

        var span = SpanConverter.Convert(evt);
        Assert.Equal(8, span.ParentSpanId.Length);
    }

    [Fact]
    public void EmptyParentSpanIdForRootSpan()
    {
        using var evt = ClefParser.Parse(BaseSpanJson);
        var span = SpanConverter.Convert(evt);
        Assert.Equal(ByteString.Empty, span.ParentSpanId);
    }

    [Fact]
    public void SetsNameFromMessageTemplate()
    {
        using var evt = ClefParser.Parse(BaseSpanJson);
        var span = SpanConverter.Convert(evt);
        Assert.Equal("GET /api/users", span.Name);
    }

    [Fact]
    public void NameFallsBackToRenderedMessage()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"abc","@sp":"def","@st":"2024-01-15T10:30:00Z",
            "@m":"GET /api/users/42"
        }
        """);

        var span = SpanConverter.Convert(evt);
        Assert.Equal("GET /api/users/42", span.Name);
    }

    [Fact]
    public void NameDefaultsToUnknown()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"abc","@sp":"def","@st":"2024-01-15T10:30:00Z"
        }
        """);

        var span = SpanConverter.Convert(evt);
        Assert.Equal("unknown", span.Name);
    }

    [Theory]
    [InlineData("Client", Span.Types.SpanKind.Client)]
    [InlineData("Server", Span.Types.SpanKind.Server)]
    [InlineData("Internal", Span.Types.SpanKind.Internal)]
    [InlineData("Producer", Span.Types.SpanKind.Producer)]
    [InlineData("Consumer", Span.Types.SpanKind.Consumer)]
    [InlineData(null, Span.Types.SpanKind.Internal)]
    [InlineData("Unknown", Span.Types.SpanKind.Internal)]
    public void MapsSpanKind(string? kind, Span.Types.SpanKind expected)
    {
        var kindPart = kind != null ? $",\"@sk\":\"{kind}\"" : "";
        var json = "{\"@t\":\"2024-01-15T10:30:01Z\",\"@tr\":\"abc\",\"@sp\":\"def\",\"@st\":\"2024-01-15T10:30:00Z\"" + kindPart + "}";
        using var evt = ClefParser.Parse(json);

        var span = SpanConverter.Convert(evt);
        Assert.Equal(expected, span.Kind);
    }

    [Fact]
    public void SetsStartAndEndTime()
    {
        using var evt = ClefParser.Parse(BaseSpanJson);
        var span = SpanConverter.Convert(evt);

        var startTs = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var endTs = new DateTimeOffset(2024, 1, 15, 10, 30, 1, TimeSpan.Zero);
        var start = (ulong)(startTs.UtcTicks - DateTimeOffset.UnixEpoch.Ticks) * 100;
        var end = (ulong)(endTs.UtcTicks - DateTimeOffset.UnixEpoch.Ticks) * 100;

        Assert.Equal(start, span.StartTimeUnixNano);
        Assert.Equal(end, span.EndTimeUnixNano);
    }

    [Theory]
    [InlineData(null, Status.Types.StatusCode.Ok)]
    [InlineData("Information", Status.Types.StatusCode.Ok)]
    [InlineData("Warning", Status.Types.StatusCode.Ok)]
    [InlineData("Error", Status.Types.StatusCode.Error)]
    [InlineData("Fatal", Status.Types.StatusCode.Error)]
    public void MapsStatusCode(string? level, Status.Types.StatusCode expectedCode)
    {
        var levelPart = level != null ? $",\"@l\":\"{level}\"" : "";
        var json = "{\"@t\":\"2024-01-15T10:30:01Z\",\"@tr\":\"abc\",\"@sp\":\"def\",\"@st\":\"2024-01-15T10:30:00Z\"" + levelPart + "}";
        using var evt = ClefParser.Parse(json);

        var span = SpanConverter.Convert(evt);
        Assert.Equal(expectedCode, span.Status.Code);
    }

    [Fact]
    public void ConvertsPropertiesToAttributes()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"abc","@sp":"def","@st":"2024-01-15T10:30:00Z",
            "http.method":"GET",
            "http.status_code":200
        }
        """);

        var span = SpanConverter.Convert(evt);
        var attrs = span.Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("GET", attrs["http.method"].StringValue);
        Assert.Equal(200, attrs["http.status_code"].IntValue);
    }
}
