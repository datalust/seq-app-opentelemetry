using System.Text.Json;
using Seq.App.OpenTelemetry.Parsing;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class ClefParserTests
{
    [Fact]
    public void ParsesTimestamp()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00.123Z"}""");
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 10, 30, 0, 123, TimeSpan.Zero), evt.Timestamp);
    }

    [Theory]
    [InlineData("Verbose", "Verbose")]
    [InlineData("Debug", "Debug")]
    [InlineData("Information", "Information")]
    [InlineData("Warning", "Warning")]
    [InlineData("Error", "Error")]
    [InlineData("Fatal", "Fatal")]
    public void ParsesLevel(string level, string expected)
    {
        using var evt = ClefParser.Parse($$"""{"@t":"2024-01-15T10:30:00Z","@l":"{{level}}"}""");
        Assert.Equal(expected, evt.Level);
    }

    [Fact]
    public void LevelDefaultsToNull()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z"}""");
        Assert.Null(evt.Level);
    }

    [Fact]
    public void ParsesMessageTemplate()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@mt":"Hello {Name}"}""");
        Assert.Equal("Hello {Name}", evt.MessageTemplate);
    }

    [Fact]
    public void ParsesRenderedMessage()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@m":"Hello World"}""");
        Assert.Equal("Hello World", evt.RenderedMessage);
    }

    [Fact]
    public void ParsesException()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@x":"System.Exception: boom\n   at Foo.Bar()"}""");
        Assert.Equal("System.Exception: boom\n   at Foo.Bar()", evt.Exception);
    }

    [Fact]
    public void ParsesEventId()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@i":"abc123"}""");
        Assert.Equal("abc123", evt.EventId);
    }

    [Fact]
    public void IgnoresRenderings()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@r":["formatted"]}""");
        Assert.Empty(evt.Properties);
    }

    [Fact]
    public void ParsesTraceFields()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "@tr":"0af7651916cd43dd8448eb211c80319c",
            "@sp":"00f067aa0ba902b7",
            "@ps":"b7ad6b7169203331",
            "@st":"2024-01-15T10:29:59Z",
            "@sk":"Server"
        }
        """);

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", evt.TraceId);
        Assert.Equal("00f067aa0ba902b7", evt.SpanId);
        Assert.Equal("b7ad6b7169203331", evt.ParentSpanId);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 10, 29, 59, TimeSpan.Zero), evt.SpanStart);
        Assert.Equal("Server", evt.SpanKind);
    }

    [Fact]
    public void ParsesScopeObject()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "@sc":{"name":"MyLibrary","version":"1.0"}
        }
        """);

        Assert.NotNull(evt.Scope);
        Assert.Equal(JsonValueKind.Object, evt.Scope.Value.ValueKind);
    }

    [Fact]
    public void ParsesResourceAttributes()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "@ra":{"service.name":"my-service","service.version":"2.0"}
        }
        """);

        Assert.NotNull(evt.ResourceAttributes);
        Assert.Equal(JsonValueKind.Object, evt.ResourceAttributes.Value.ValueKind);
    }

    [Fact]
    public void CollectsUserProperties()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "@mt":"Hello {Name}",
            "Name":"World",
            "Count":42,
            "Active":true
        }
        """);

        Assert.Equal(3, evt.Properties.Count);
        Assert.Equal("Name", evt.Properties[0].Key);
        Assert.Equal("World", evt.Properties[0].Value.GetString());
        Assert.Equal("Count", evt.Properties[1].Key);
        Assert.Equal(42, evt.Properties[1].Value.GetInt32());
        Assert.Equal("Active", evt.Properties[2].Key);
        Assert.True(evt.Properties[2].Value.GetBoolean());
    }

    [Fact]
    public void HandlesDoubleAtEscape()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@@custom":"value"}""");
        Assert.Single(evt.Properties);
        Assert.Equal("@custom", evt.Properties[0].Key);
        Assert.Equal("value", evt.Properties[0].Value.GetString());
    }

    [Fact]
    public void NestedObjectProperty()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "Address":{"City":"Sydney","Country":"AU"}
        }
        """);

        Assert.Single(evt.Properties);
        Assert.Equal(JsonValueKind.Object, evt.Properties[0].Value.ValueKind);
    }

    [Fact]
    public void ArrayProperty()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "Tags":["web","api"]
        }
        """);

        Assert.Single(evt.Properties);
        Assert.Equal(JsonValueKind.Array, evt.Properties[0].Value.ValueKind);
    }

    [Theory]
    [InlineData(true, """{"@t":"2024-01-15T10:30:00Z","@tr":"abc","@sp":"def","@st":"2024-01-15T10:29:59Z"}""")]
    [InlineData(false, """{"@t":"2024-01-15T10:30:00Z","@tr":"abc","@sp":"def"}""")]
    [InlineData(false, """{"@t":"2024-01-15T10:30:00Z","@tr":"abc"}""")]
    [InlineData(false, """{"@t":"2024-01-15T10:30:00Z"}""")]
    public void IsSpanClassification(bool expectedIsSpan, string json)
    {
        using var evt = ClefParser.Parse(json);
        Assert.Equal(expectedIsSpan, evt.IsSpan);
    }

    [Fact]
    public void IgnoresUnrecognizedAtProperties()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z","@unknown":"val"}""");
        Assert.Empty(evt.Properties);
    }

    [Fact]
    public void AbsentOptionalFieldsAreNull()
    {
        using var evt = ClefParser.Parse("""{"@t":"2024-01-15T10:30:00Z"}""");
        Assert.Null(evt.Level);
        Assert.Null(evt.MessageTemplate);
        Assert.Null(evt.RenderedMessage);
        Assert.Null(evt.Exception);
        Assert.Null(evt.EventId);
        Assert.Null(evt.TraceId);
        Assert.Null(evt.SpanId);
        Assert.Null(evt.ParentSpanId);
        Assert.Null(evt.SpanStart);
        Assert.Null(evt.SpanKind);
        Assert.Null(evt.Scope);
        Assert.Null(evt.ResourceAttributes);
        Assert.Empty(evt.Properties);
    }
}
