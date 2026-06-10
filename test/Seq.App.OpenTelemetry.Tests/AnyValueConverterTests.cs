using System.Text.Json;
using Seq.App.OpenTelemetry.Conversion;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class AnyValueConverterTests
{
    static AnyValue Convert(string json)
    {
        using var document = JsonDocument.Parse(json);
        return AnyValueConverter.Convert(document.RootElement);
    }

    [Fact]
    public void NullConvertsToValueWithNoFieldSet()
    {
        var value = Convert("null");
        Assert.Equal(AnyValue.ValueOneofCase.None, value.ValueCase);
    }

    [Fact]
    public void UndefinedConvertsToValueWithNoFieldSet()
    {
        // A default JsonElement has ValueKind Undefined.
        var value = AnyValueConverter.Convert(default);
        Assert.Equal(AnyValue.ValueOneofCase.None, value.ValueCase);
    }

    [Fact]
    public void NullArrayElementsArePreservedToKeepPositions()
    {
        var value = Convert("[1,null,3]");

        var values = value.ArrayValue.Values;
        Assert.Equal(3, values.Count);
        Assert.Equal(1, values[0].IntValue);
        Assert.Equal(AnyValue.ValueOneofCase.None, values[1].ValueCase);
        Assert.Equal(3, values[2].IntValue);
    }

    [Fact]
    public void NullObjectPropertiesArePreservedAsEmptyValues()
    {
        var value = Convert("""{"present":"x","absent":null}""");

        var entries = value.KvlistValue.Values.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal("x", entries["present"].StringValue);
        Assert.Equal(AnyValue.ValueOneofCase.None, entries["absent"].ValueCase);
    }
}
