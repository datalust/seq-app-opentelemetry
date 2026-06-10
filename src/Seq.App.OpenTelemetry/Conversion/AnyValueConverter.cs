using System.Text.Json;

namespace Seq.App.OpenTelemetry.Conversion;

static class AnyValueConverter
{
    /// <summary>
    /// Converts to a full OTLP <see cref="AnyValue"/>, preserving objects as structured kvlist
    /// values. The OTel spec only permits kvlist attribute values on log records; use
    /// <see cref="ConvertStandard"/> for span, resource, and scope attributes.
    /// </summary>
    public static AnyValue Convert(JsonElement element)
    {
        return Convert(element, structured: true);
    }

    /// <summary>
    /// Converts to a "standard" OTLP attribute value — primitives and arrays only, the value
    /// types permitted on span, resource, and scope attributes. Objects (at any depth) are
    /// JSON-encoded as strings, since backends commonly drop kvlist values in these positions.
    /// </summary>
    public static AnyValue ConvertStandard(JsonElement element)
    {
        return Convert(element, structured: false);
    }

    static AnyValue Convert(JsonElement element, bool structured)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return new AnyValue { StringValue = element.GetString()! };

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var intVal))
                    return new AnyValue { IntValue = intVal };
                return new AnyValue { DoubleValue = element.GetDouble() };

            case JsonValueKind.True:
                return new AnyValue { BoolValue = true };

            case JsonValueKind.False:
                return new AnyValue { BoolValue = false };

            case JsonValueKind.Array:
                var arrayValue = new ArrayValue();
                foreach (var item in element.EnumerateArray())
                    arrayValue.Values.Add(Convert(item, structured));
                return new AnyValue { ArrayValue = arrayValue };

            case JsonValueKind.Object:
                if (!structured)
                    return new AnyValue { StringValue = element.GetRawText() };

                var kvList = new KeyValueList();
                foreach (var prop in element.EnumerateObject())
                {
                    kvList.Values.Add(new KeyValue
                    {
                        Key = prop.Name,
                        Value = Convert(prop.Value, structured)
                    });
                }
                return new AnyValue { KvlistValue = kvList };

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                // OTLP represents a null/absent value as an AnyValue with no oneof field set.
                return new AnyValue();
        }
    }
}
