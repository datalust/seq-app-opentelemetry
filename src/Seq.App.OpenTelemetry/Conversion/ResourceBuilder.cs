using System.Text.Json;
using Seq.App.OpenTelemetry.Parsing;

namespace Seq.App.OpenTelemetry.Conversion;

static class ResourceBuilder
{
    public static Resource Build(ClefEvent evt, IReadOnlyList<KeyValue>? configuredAttributes)
    {
        // Collect attributes in priority order; higher-priority sources overwrite lower.
        var attrs = new Dictionary<string, AnyValue>
        {
            ["telemetry.sdk.name"] = new() { StringValue = "seq-app-opentelemetry" },
            ["telemetry.sdk.language"] = new() { StringValue = "dotnet" }
        };

        if (configuredAttributes != null)
        {
            foreach (var attr in configuredAttributes)
                attrs[attr.Key] = attr.Value;
        }

        if (evt.ResourceAttributes is { ValueKind: JsonValueKind.Object } ra)
        {
            AttributeFlattener.Flatten(ra, attrs);
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
                    var attributes = new Dictionary<string, AnyValue>();
                    AttributeFlattener.Flatten(prop.Value, attributes);
                    foreach (var (key, value) in attributes)
                    {
                        scope.Attributes.Add(new KeyValue { Key = key, Value = value });
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
