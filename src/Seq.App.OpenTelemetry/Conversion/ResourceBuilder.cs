using Seq.App.OpenTelemetry.Parsing;

namespace Seq.App.OpenTelemetry.Conversion;

static class ResourceBuilder
{
    public static Resource Build(ClefEvent evt, IReadOnlyList<KeyValue>? configuredAttributes)
    {
        // Collect attributes in priority order; higher-priority sources overwrite lower. The
        // `service.name` fallback follows the OTel `unknown_service:` convention; most backends
        // group telemetry by `service.name` and behave poorly when it's absent.
        var attrs = new Dictionary<string, AnyValue>
        {
            ["service.name"] = new() { StringValue = "unknown_service:seq-app-opentelemetry" },
            ["telemetry.sdk.name"] = new() { StringValue = "seq-app-opentelemetry" },
            ["telemetry.sdk.language"] = new() { StringValue = "dotnet" }
        };

        if (configuredAttributes != null)
        {
            foreach (var attr in configuredAttributes)
                attrs[attr.Key] = attr.Value;
        }

        foreach (var (key, value) in evt.ResourceAttributes)
            AttributeFlattener.Flatten(key, value, attrs);

        var resource = new Resource();
        foreach (var (key, value) in attrs)
        {
            resource.Attributes.Add(new KeyValue { Key = key, Value = value });
        }

        return resource;
    }

    public static InstrumentationScope? BuildScope(ClefEvent evt)
    {
        if (evt.ScopeAttributes.Count == 0)
            return null;

        var scope = new InstrumentationScope();
        var attributes = new Dictionary<string, AnyValue>();

        // `@sa` is flat: `name` and `version` are well-known attributes that sit alongside any other
        // scope attributes, which become OTLP scope attributes.
        foreach (var (key, value) in evt.ScopeAttributes)
        {
            switch (key)
            {
                case "name":
                    scope.Name = value.GetString() ?? "";
                    break;
                case "version":
                    scope.Version = value.GetString() ?? "";
                    break;
                default:
                    AttributeFlattener.Flatten(key, value, attributes);
                    break;
            }
        }

        foreach (var (key, value) in attributes)
            scope.Attributes.Add(new KeyValue { Key = key, Value = value });

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
