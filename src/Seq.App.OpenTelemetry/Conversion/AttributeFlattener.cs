using System.Text.Json;

namespace Seq.App.OpenTelemetry.Conversion;

/// <summary>
/// Seq stores scope and resource attributes as nested JSON objects (e.g.
/// <c>{"service":{"name":"x"}}</c>), but OTLP resource and scope attributes are a flat list of
/// dotted keys (e.g. <c>service.name = "x"</c>). This flattens the former into the latter.
/// </summary>
static class AttributeFlattener
{
    /// <summary>
    /// The maximum object nesting depth that is unpacked into dotted attribute keys. Once a key
    /// reaches this many segments, any remaining object structure is emitted as a single
    /// JSON-encoded string rather than being flattened further. This keeps pathological or deeply
    /// recursive payloads from producing an unbounded number of attributes.
    /// </summary>
    const int MaxDepth = 5;

    /// <summary>
    /// Flattens the properties of <paramref name="obj"/> into <paramref name="attributes"/>, unpacking
    /// nested objects into dotted keys. Later keys overwrite earlier ones, so callers can seed
    /// <paramref name="attributes"/> with lower-priority defaults before flattening. Leaves are
    /// converted with <see cref="AnyValueConverter.ConvertStandard"/> — resource and scope
    /// attributes only permit standard (primitive and array) values, so any object remaining at a
    /// leaf (below <see cref="MaxDepth"/>, or nested inside an array) becomes a JSON string.
    /// </summary>
    public static void Flatten(JsonElement obj, IDictionary<string, AnyValue> attributes)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return;

        foreach (var prop in obj.EnumerateObject())
            Flatten(prop.Name, prop.Value, attributes);
    }

    /// <summary>
    /// Flattens a single <paramref name="key"/>/<paramref name="value"/> pair into
    /// <paramref name="attributes"/> with the same nested-object unpacking as
    /// <see cref="Flatten(JsonElement,IDictionary{string,AnyValue})"/>.
    /// </summary>
    public static void Flatten(string key, JsonElement value, IDictionary<string, AnyValue> attributes)
    {
        FlattenInto(key, value, 1, attributes);
    }

    static void FlattenInto(string key, JsonElement value, int depth, IDictionary<string, AnyValue> attributes)
    {
        if (value.ValueKind == JsonValueKind.Object && depth < MaxDepth)
        {
            var empty = true;
            foreach (var prop in value.EnumerateObject())
            {
                empty = false;
                FlattenInto($"{key}.{prop.Name}", prop.Value, depth + 1, attributes);
            }

            // A non-empty object is fully represented by its flattened children. An empty object has
            // no children, so fall through and emit it as a JSON string to keep the key.
            if (!empty)
                return;
        }

        attributes[key] = AnyValueConverter.ConvertStandard(value);
    }
}
