using Google.Protobuf;
using Seq.App.OpenTelemetry.Conversion;
using Seq.App.OpenTelemetry.Parsing;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class LogRecordConverterTests
{
    [Theory]
    [InlineData(null, SeverityNumber.Info, "Information")]
    [InlineData("Verbose", SeverityNumber.Trace, "Verbose")]
    [InlineData("Debug", SeverityNumber.Debug, "Debug")]
    [InlineData("Information", SeverityNumber.Info, "Information")]
    [InlineData("Warning", SeverityNumber.Warn, "Warning")]
    [InlineData("Error", SeverityNumber.Error, "Error")]
    [InlineData("Fatal", SeverityNumber.Fatal, "Fatal")]
    // Seq doesn't constrain levels to the Serilog set; events ingested via OTLP, GELF, syslog,
    // etc. carry their original level strings, mapped case-insensitively with common aliases.
    [InlineData("WARN", SeverityNumber.Warn, "WARN")]
    [InlineData("ERROR", SeverityNumber.Error, "ERROR")]
    [InlineData("err", SeverityNumber.Error, "err")]
    [InlineData("trace", SeverityNumber.Trace, "trace")]
    [InlineData("Critical", SeverityNumber.Fatal, "Critical")]
    [InlineData("Mystery", SeverityNumber.Info, "Mystery")]
    public void MapsSeverityCorrectly(string? level, SeverityNumber expectedNumber, string expectedText)
    {
        var json = level != null
            ? $$"""{"@t":"2024-01-15T10:30:00Z","@l":"{{level}}","@m":"test"}"""
            : """{"@t":"2024-01-15T10:30:00Z","@m":"test"}""";

        using var evt = ClefParser.Parse(json);
        var record = LogRecordConverter.Convert(evt);

        Assert.Equal(expectedNumber, record.SeverityNumber);
        Assert.Equal(expectedText, record.SeverityText);
    }

    [Fact]
    public void BodyUsesRenderedMessage()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@m":"Hello World","@mt":"Hello {Name}"}""");
        var record = LogRecordConverter.Convert(evt);
        Assert.Equal("Hello World", record.Body.StringValue);
    }

    [Fact]
    public void BodyFallsBackToMessageTemplate()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@mt":"Hello {Name}"}""");
        var record = LogRecordConverter.Convert(evt);
        Assert.Equal("Hello {Name}", record.Body.StringValue);
    }

    [Fact]
    public void BodyIsEmptyWhenNoMessage()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z"}""");
        var record = LogRecordConverter.Convert(evt);
        Assert.Equal("", record.Body.StringValue);
    }

    [Fact]
    public void SetsTimestampNanos()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z"}""");
        var record = LogRecordConverter.Convert(evt);

        var ts = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var expectedNano = (ulong)(ts.UtcTicks - DateTimeOffset.UnixEpoch.Ticks) * 100;

        Assert.Equal(expectedNano, record.TimeUnixNano);
        Assert.Equal(expectedNano, record.ObservedTimeUnixNano);
    }

    [Fact]
    public void MissingTimestampMapsToUnknownNotWrapped()
    {
        // OTLP defines zero as "unknown or missing timestamp"; a default (pre-epoch) value must
        // not wrap around to a huge unsigned nanosecond count.
        using var evt = ClefParser.Parse("""{"@m":"no timestamp"}""");
        var record = LogRecordConverter.Convert(evt);

        Assert.Equal(0ul, record.TimeUnixNano);
        Assert.Equal(0ul, record.ObservedTimeUnixNano);
    }

    [Fact]
    public void SetsTraceAndSpanIds()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "@tr":"0af7651916cd43dd8448eb211c80319c",
            "@sp":"00f067aa0ba902b7"
        }
        """);

        var record = LogRecordConverter.Convert(evt);

        Assert.Equal(16, record.TraceId.Length);
        Assert.Equal(8, record.SpanId.Length);
        Assert.Equal(ByteString.CopyFrom(
            0x0a, 0xf7, 0x65, 0x19, 0x16, 0xcd, 0x43, 0xdd,
            0x84, 0x48, 0xeb, 0x21, 0x1c, 0x80, 0x31, 0x9c), record.TraceId);
    }

    [Fact]
    public void EmptyTraceAndSpanIdsWhenAbsent()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z"}""");
        var record = LogRecordConverter.Convert(evt);

        Assert.Equal(ByteString.Empty, record.TraceId);
        Assert.Equal(ByteString.Empty, record.SpanId);
    }

    [Fact]
    public void ConvertsPropertiesToAttributes()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "Name":"World",
            "Count":42
        }
        """);

        var record = LogRecordConverter.Convert(evt);
        var attrs = record.Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("World", attrs["Name"].StringValue);
        Assert.Equal(42, attrs["Count"].IntValue);
    }

    [Fact]
    public void AddsMessageTemplateAttribute()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@mt":"Hello {Name}"}""");
        var record = LogRecordConverter.Convert(evt);

        var attr = record.Attributes.FirstOrDefault(a => a.Key == "message_template.text");
        Assert.NotNull(attr);
        Assert.Equal("Hello {Name}", attr.Value.StringValue);
    }

    [Fact]
    public void AddsEventIdAttribute()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@i":"abc123"}""");
        var record = LogRecordConverter.Convert(evt);

        var attr = record.Attributes.FirstOrDefault(a => a.Key == "seq.event_type");
        Assert.NotNull(attr);
        Assert.Equal("abc123", attr.Value.StringValue);
    }

    [Fact]
    public void ParsesExceptionIntoAttributes()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@x":"System.InvalidOperationException: Something went wrong\n   at Foo.Bar()"}""");
        var record = LogRecordConverter.Convert(evt);
        var attrs = record.Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("System.InvalidOperationException", attrs["exception.type"].StringValue);
        Assert.Equal("Something went wrong", attrs["exception.message"].StringValue);
        Assert.Contains("System.InvalidOperationException", attrs["exception.stacktrace"].StringValue);
    }

    [Fact]
    public void ExceptionWithoutColonUsesFullStacktrace()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@x":"just a stack trace"}""");
        var record = LogRecordConverter.Convert(evt);
        var attrs = record.Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("just a stack trace", attrs["exception.stacktrace"].StringValue);
        Assert.False(attrs.ContainsKey("exception.type"));
        Assert.False(attrs.ContainsKey("exception.message"));
    }
}
