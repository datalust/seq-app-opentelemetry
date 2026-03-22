using System.Text.Json;

namespace Seq.App.OpenTelemetry.Conversion;

static class AnyValueConverter
{
    public static AnyValue? Convert(JsonElement element)
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
                {
                    var converted = Convert(item);
                    if (converted != null)
                        arrayValue.Values.Add(converted);
                }
                return new AnyValue { ArrayValue = arrayValue };

            case JsonValueKind.Object:
                var kvList = new KeyValueList();
                foreach (var prop in element.EnumerateObject())
                {
                    var converted = Convert(prop.Value);
                    if (converted != null)
                    {
                        kvList.Values.Add(new KeyValue
                        {
                            Key = prop.Name,
                            Value = converted
                        });
                    }
                }
                return new AnyValue { KvlistValue = kvList };

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

}
