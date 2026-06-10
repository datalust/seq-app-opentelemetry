using Seq.App.OpenTelemetry.Conversion;
using Seq.App.OpenTelemetry.Parsing;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class ResourceBuilderTests
{
    static Dictionary<string, AnyValue> ResourceAttributes(string json, IReadOnlyList<KeyValue>? configured = null)
    {
        using var evt = ClefParser.Parse(json);
        var resource = ResourceBuilder.Build(evt, configured);
        return resource.Attributes.ToDictionary(a => a.Key, a => a.Value);
    }

    [Fact]
    public void FlattensNestedResourceAttributes()
    {
        var attrs = ResourceAttributes("""{"@t":"2024-01-15T10:30:00Z","@ra":{"service":{"name":"test"}}}""");

        Assert.Equal("test", attrs["service.name"].StringValue);
    }

    [Fact]
    public void IncludesDefaultSdkAttributes()
    {
        var attrs = ResourceAttributes("""{"@t":"2024-01-15T10:30:00Z"}""");

        Assert.Equal("seq-app-opentelemetry", attrs["telemetry.sdk.name"].StringValue);
        Assert.Equal("dotnet", attrs["telemetry.sdk.language"].StringValue);
    }

    [Fact]
    public void FlattenedResourceAttributesOverrideConfiguredDefaults()
    {
        var configured = new[]
        {
            new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "configured" } }
        };

        var attrs = ResourceAttributes(
            """{"@t":"2024-01-15T10:30:00Z","@ra":{"service":{"name":"from-event"}}}""",
            configured);

        Assert.Equal("from-event", attrs["service.name"].StringValue);
    }

    [Fact]
    public void FlattensNestedScopeAttributes()
    {
        using var evt = ClefParser.Parse("""
        {
            "@t":"2024-01-15T10:30:00Z",
            "@sc":{"name":"MyLibrary","version":"1.0","attributes":{"feature":{"enabled":true}}}
        }
        """);

        var scope = ResourceBuilder.BuildScope(evt);

        Assert.NotNull(scope);
        Assert.Equal("MyLibrary", scope.Name);
        Assert.Equal("1.0", scope.Version);
        var attrs = scope.Attributes.ToDictionary(a => a.Key, a => a.Value);
        Assert.True(attrs["feature.enabled"].BoolValue);
    }
}
