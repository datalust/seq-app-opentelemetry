using System.Text.Json;
using Seq.App.OpenTelemetry.Parsing;

namespace Seq.App.OpenTelemetry.Conversion;

static class ResourceBuilder
{
    public static Resource Build(ClefEvent evt, IReadOnlyList<KeyValue>? configuredAttributes)
    {
        // Collect attributes in priority order; higher-priority sources overwrite lower.
        var attrs = new Dictionary<string, AnyValue>();

        // 1. Defaults (lowest priority)
        attrs["telemetry.sdk.name"] = new AnyValue { StringValue = "seq" };
        attrs["telemetry.sdk.language"] = new AnyValue { StringValue = "dotnet" };

        // 2. User-configured resource attributes
        if (configuredAttributes != null)
        {
            foreach (var attr in configuredAttributes)
                attrs[attr.Key] = attr.Value;
        }

        // 3. Event-level @ra (highest priority, from OTLP ingestion)
        if (evt.ResourceAttributes is { } ra && ra.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in ra.EnumerateObject())
            {
                var converted = AnyValueConverter.Convert(prop.Value);
                if (converted != null)
                    attrs[prop.Name] = converted;
            }
        }

        var resource = new Resource();
        foreach (var (key, value) in attrs)
        {
            resource.Attributes.Add(new KeyValue { Key = key, Value = value });
        }

        return resource;
    }

    public static InstrumentationScope? BuildScope(ClefEvent evt)
    {
        if (evt.Scope is not { } sc || sc.ValueKind != JsonValueKind.Object)
            return null;

        var scope = new InstrumentationScope();

        foreach (var prop in sc.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "name":
                    scope.Name = prop.Value.GetString() ?? "";
                    break;
                case "version":
                    scope.Version = prop.Value.GetString() ?? "";
                    break;
                case "attributes" when prop.Value.ValueKind == JsonValueKind.Object:
                    foreach (var attr in prop.Value.EnumerateObject())
                    {
                        var converted = AnyValueConverter.Convert(attr.Value);
                        if (converted != null)
                        {
                            scope.Attributes.Add(new KeyValue
                            {
                                Key = attr.Name,
                                Value = converted
                            });
                        }
                    }
                    break;
            }
        }

        return scope;
    }

    public static IReadOnlyList<KeyValue>? ParseConfiguredAttributes(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
            return null;

        var result = new List<KeyValue>();
        foreach (var pair in setting.Split(','))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = pair[..eqIndex].Trim();
            var value = pair[(eqIndex + 1)..].Trim();

            result.Add(new KeyValue
            {
                Key = key,
                Value = new AnyValue { StringValue = value }
            });
        }

        return result.Count > 0 ? result : null;
    }
}
