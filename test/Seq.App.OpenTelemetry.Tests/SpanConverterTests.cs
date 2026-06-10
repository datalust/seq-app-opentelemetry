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
    [InlineData(null)]
    public void StatusIsUnsetForNullLevel(string? level)
    {
        // In OTLP, `Ok` is an explicit assertion of success; an absent status (`Unset`) is the
        // idiomatic representation of "completed normally".
        var levelPart = level != null ? $",\"@l\":\"{level}\"" : "";
        var json = "{\"@t\":\"2024-01-15T10:30:01Z\",\"@tr\":\"abc\",\"@sp\":\"def\",\"@st\":\"2024-01-15T10:30:00Z\"" + levelPart + "}";
        using var evt = ClefParser.Parse(json);

        var span = SpanConverter.Convert(evt);
        Assert.Null(span.Status);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Fatal")]
    [InlineData("ERROR")]
    [InlineData("err")]
    [InlineData("Critical")]
    public void StatusIsErrorForErrorLevels(string level)
    {
        var json = "{\"@t\":\"2024-01-15T10:30:01Z\",\"@tr\":\"abc\",\"@sp\":\"def\",\"@st\":\"2024-01-15T10:30:00Z\",\"@l\":\"" + level + "\"}";
        using var evt = ClefParser.Parse(json);

        var span = SpanConverter.Convert(evt);
        Assert.NotNull(span.Status);
        Assert.Equal(Status.Types.StatusCode.Error, span.Status.Code);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("Ok")]
    [InlineData("ok")]
    [InlineData("Information")]
    public void OkLevelRoundTripsAsExplicitOkStatus(string level)
    {
        // Seq surfaces a span's explicitly-set `Ok` status as an "OK" level, so converting it
        // back to `StatusCode.Ok` (rather than leaving the status unset) preserves the original
        // assertion. The level itself is fully represented by the status, so no `seq.level`
        // attribute is added.
        var json = "{\"@t\":\"2024-01-15T10:30:01Z\",\"@tr\":\"abc\",\"@sp\":\"def\",\"@st\":\"2024-01-15T10:30:00Z\",\"@l\":\"" + level + "\"}";
        using var evt = ClefParser.Parse(json);

        var span = SpanConverter.Convert(evt);

        Assert.NotNull(span.Status);
        Assert.Equal(Status.Types.StatusCode.Ok, span.Status.Code);
        Assert.DoesNotContain(span.Attributes, a => a.Key == "seq.level");
    }

    [Fact]
    public void ErrorStatusMessageComesFromExceptionFirstLine()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"abc","@sp":"def","@st":"2024-01-15T10:30:00Z",
            "@l":"Error",
            "@x":"System.TimeoutException: The operation timed out\n   at Foo.Bar()"
        }
        """);

        var span = SpanConverter.Convert(evt);
        Assert.Equal("System.TimeoutException: The operation timed out", span.Status.Message);
    }

    [Fact]
    public void LevelIsPreservedAsAttribute()
    {
        var json = "{\"@t\":\"2024-01-15T10:30:01Z\",\"@tr\":\"abc\",\"@sp\":\"def\",\"@st\":\"2024-01-15T10:30:00Z\",\"@l\":\"Warning\"}";
        using var evt = ClefParser.Parse(json);

        var span = SpanConverter.Convert(evt);
        var attr = span.Attributes.FirstOrDefault(a => a.Key == "seq.level");
        Assert.NotNull(attr);
        Assert.Equal("Warning", attr.Value.StringValue);
    }

    [Fact]
    public void ExceptionBecomesSpanEvent()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"abc","@sp":"def","@st":"2024-01-15T10:30:00Z",
            "@x":"System.InvalidOperationException: Something went wrong\n   at Foo.Bar()"
        }
        """);

        var span = SpanConverter.Convert(evt);

        var exceptionEvent = Assert.Single(span.Events);
        Assert.Equal("exception", exceptionEvent.Name);
        Assert.Equal(span.EndTimeUnixNano, exceptionEvent.TimeUnixNano);

        var attrs = exceptionEvent.Attributes.ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal("System.InvalidOperationException", attrs["exception.type"].StringValue);
        Assert.Equal("Something went wrong", attrs["exception.message"].StringValue);
        Assert.Contains("at Foo.Bar()", attrs["exception.stacktrace"].StringValue);
    }

    [Fact]
    public void StructuredPropertiesBecomeJsonStringAttributes()
    {
        // Span attributes are restricted to standard (primitive and array) values.
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"abc","@sp":"def","@st":"2024-01-15T10:30:00Z",
            "Request":{"Path":"/api/users"}
        }
        """);

        var span = SpanConverter.Convert(evt);
        var attrs = span.Attributes.ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal("""{"Path":"/api/users"}""", attrs["Request"].StringValue);
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
