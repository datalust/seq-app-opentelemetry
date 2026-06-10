using System.Text.Json;
using Seq.App.OpenTelemetry.Conversion;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class AttributeFlattenerTests
{
    static Dictionary<string, AnyValue> Flatten(string json)
    {
        using var document = JsonDocument.Parse(json);
        var attributes = new Dictionary<string, AnyValue>();
        AttributeFlattener.Flatten(document.RootElement, attributes);
        return attributes;
    }

    [Fact]
    public void FlattensNestedObjectIntoDottedKey()
    {
        var attrs = Flatten("""{"service":{"name":"test"}}""");

        Assert.Single(attrs);
        Assert.Equal("test", attrs["service.name"].StringValue);
    }

    [Fact]
    public void LeavesScalarKeysUntouched()
    {
        var attrs = Flatten("""{"host.name":"web-01"}""");

        Assert.Single(attrs);
        Assert.Equal("web-01", attrs["host.name"].StringValue);
    }

    [Fact]
    public void FlattensMultipleSiblingsAndBranches()
    {
        var attrs = Flatten("""
        {
            "service":{"name":"checkout","version":"2.1.0","namespace":{"id":"shop"}},
            "host":{"name":"web-01"}
        }
        """);

        Assert.Equal(4, attrs.Count);
        Assert.Equal("checkout", attrs["service.name"].StringValue);
        Assert.Equal("2.1.0", attrs["service.version"].StringValue);
        Assert.Equal("shop", attrs["service.namespace.id"].StringValue);
        Assert.Equal("web-01", attrs["host.name"].StringValue);
    }

    [Fact]
    public void PreservesDataTypesAtLeaves()
    {
        var attrs = Flatten("""
        {
            "process":{
                "pid":1234,
                "cpu":{"load":0.75},
                "is_root":true,
                "is_container":false
            }
        }
        """);

        Assert.Equal(1234, attrs["process.pid"].IntValue);
        Assert.Equal(0.75, attrs["process.cpu.load"].DoubleValue);
        Assert.True(attrs["process.is_root"].BoolValue);
        Assert.False(attrs["process.is_container"].BoolValue);
    }

    [Fact]
    public void KeepsArraysAsArrayValuesWithoutFlattening()
    {
        var attrs = Flatten("""{"service":{"tags":["a","b","c"]}}""");

        Assert.Single(attrs);
        var array = attrs["service.tags"].ArrayValue;
        Assert.NotNull(array);
        Assert.Equal(3, array.Values.Count);
        Assert.Equal("a", array.Values[0].StringValue);
        Assert.Equal("c", array.Values[2].StringValue);
    }

    [Fact]
    public void KeepsObjectsNestedInsideArraysAsStructuredValues()
    {
        // Arrays are never flattened, so objects inside them stay structured (kvlist) values.
        var attrs = Flatten("""{"servers":[{"name":"a"},{"name":"b"}]}""");

        Assert.Single(attrs);
        var array = attrs["servers"].ArrayValue;
        Assert.Equal(2, array.Values.Count);
        Assert.Equal("a", array.Values[0].KvlistValue.Values[0].Value.StringValue);
        Assert.Equal("name", array.Values[0].KvlistValue.Values[0].Key);
    }

    [Fact]
    public void FlattensUpToMaxDepth()
    {
        // Five segments (a.b.c.d.e) is exactly MaxDepth and is fully flattened.
        var attrs = Flatten("""{"a":{"b":{"c":{"d":{"e":"deep"}}}}}""");

        Assert.Single(attrs);
        Assert.Equal("deep", attrs["a.b.c.d.e"].StringValue);
    }

    [Fact]
    public void FallsBackToStructuredValueBeyondMaxDepth()
    {
        // a.b.c.d.e would be the sixth level; flattening stops at the fifth key segment and the
        // remaining object is emitted as a structured kvlist value.
        var attrs = Flatten("""{"a":{"b":{"c":{"d":{"e":{"f":"deep"}}}}}}""");

        Assert.Single(attrs);
        var fallback = attrs["a.b.c.d.e"];
        Assert.NotNull(fallback.KvlistValue);
        var inner = fallback.KvlistValue.Values;
        Assert.Single(inner);
        Assert.Equal("f", inner[0].Key);
        Assert.Equal("deep", inner[0].Value.StringValue);
    }

    [Fact]
    public void FallbackPreservesEvenDeeperNesting()
    {
        // Everything below the depth limit is kept verbatim as nested structure, not dropped.
        var attrs = Flatten("""{"a":{"b":{"c":{"d":{"e":{"f":{"g":"deepest"}}}}}}}""");

        Assert.Single(attrs);
        var level5 = attrs["a.b.c.d.e"].KvlistValue.Values;
        var f = Assert.Single(level5);
        Assert.Equal("f", f.Key);
        var g = Assert.Single(f.Value.KvlistValue.Values);
        Assert.Equal("g", g.Key);
        Assert.Equal("deepest", g.Value.StringValue);
    }

    [Fact]
    public void EmptyNestedObjectIsPreservedAsEmptyStructuredValue()
    {
        var attrs = Flatten("""{"service":{}}""");

        Assert.Single(attrs);
        Assert.NotNull(attrs["service"].KvlistValue);
        Assert.Empty(attrs["service"].KvlistValue.Values);
    }

    [Fact]
    public void PreservesNullLeavesAsEmptyValues()
    {
        var attrs = Flatten("""{"service":{"name":"test","absent":null}}""");

        Assert.Equal(2, attrs.Count);
        Assert.Equal("test", attrs["service.name"].StringValue);
        Assert.Equal(AnyValue.ValueOneofCase.None, attrs["service.absent"].ValueCase);
    }

    [Fact]
    public void LaterKeysOverwriteSeededDefaults()
    {
        using var document = JsonDocument.Parse("""{"service":{"name":"override"}}""");
        var attributes = new Dictionary<string, AnyValue>
        {
            ["service.name"] = new() { StringValue = "default" }
        };

        AttributeFlattener.Flatten(document.RootElement, attributes);

        Assert.Equal("override", attributes["service.name"].StringValue);
    }

    [Fact]
    public void NonObjectRootProducesNothing()
    {
        var attrs = Flatten("""["a","b"]""");
        Assert.Empty(attrs);
    }
}
