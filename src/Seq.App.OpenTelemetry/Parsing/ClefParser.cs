using System.Text.Json;

namespace Seq.App.OpenTelemetry.Parsing;

static class ClefParser
{
    static ReadOnlySpan<byte> Prop_t => "@t"u8;
    static ReadOnlySpan<byte> Prop_l => "@l"u8;
    static ReadOnlySpan<byte> Prop_mt => "@mt"u8;
    static ReadOnlySpan<byte> Prop_m => "@m"u8;
    static ReadOnlySpan<byte> Prop_x => "@x"u8;
    static ReadOnlySpan<byte> Prop_i => "@i"u8;
    static ReadOnlySpan<byte> Prop_tr => "@tr"u8;
    static ReadOnlySpan<byte> Prop_sp => "@sp"u8;
    static ReadOnlySpan<byte> Prop_ps => "@ps"u8;
    static ReadOnlySpan<byte> Prop_st => "@st"u8;
    static ReadOnlySpan<byte> Prop_sk => "@sk"u8;
    static ReadOnlySpan<byte> Prop_sa => "@sa"u8;
    static ReadOnlySpan<byte> Prop_ra => "@ra"u8;

    public static ClefEvent Parse(string json)
    {
        var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        DateTimeOffset timestamp = default;
        string? level = null;
        string? messageTemplate = null;
        string? renderedMessage = null;
        string? exception = null;
        string? eventId = null;
        string? traceId = null;
        string? spanId = null;
        string? parentSpanId = null;
        DateTimeOffset? spanStart = null;
        string? spanKind = null;
        List<KeyValuePair<string, JsonElement>>? scopeAttributes = null;
        List<KeyValuePair<string, JsonElement>>? resourceAttributes = null;
        var properties = new List<KeyValuePair<string, JsonElement>>();

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals(Prop_t))
            {
                timestamp = prop.Value.ValueKind == JsonValueKind.String
                    ? DateTimeOffset.Parse(prop.Value.GetString()!)
                    : default;
            }
            else if (prop.NameEquals(Prop_l))
            {
                level = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_mt))
            {
                messageTemplate = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_m))
            {
                renderedMessage = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_x))
            {
                exception = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_i))
            {
                eventId = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.GetRawText();
            }
            else if (prop.NameEquals(Prop_tr))
            {
                traceId = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_sp))
            {
                spanId = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_ps))
            {
                parentSpanId = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_st))
            {
                spanStart = prop.Value.ValueKind == JsonValueKind.String
                    ? DateTimeOffset.Parse(prop.Value.GetString()!)
                    : null;
            }
            else if (prop.NameEquals(Prop_sk))
            {
                spanKind = prop.Value.GetString();
            }
            else if (prop.NameEquals(Prop_sa))
            {
                // Seq only ever sends an object-valued `@sa`.
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    scopeAttributes = ReadProperties(prop.Value);
            }
            else if (prop.NameEquals(Prop_ra))
            {
                // Seq only ever sends an object-valued `@ra`.
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    resourceAttributes = ReadProperties(prop.Value);
            }
            else
            {
                var name = prop.Name;
                if (name.Length > 1 && name[0] == '@' && name[1] == '@')
                {
                    // @@escaped → user property named @something
                    properties.Add(new KeyValuePair<string, JsonElement>(name[1..], prop.Value.Clone()));
                }
                else if (name.Length == 0 || name[0] != '@')
                {
                    properties.Add(new KeyValuePair<string, JsonElement>(name, prop.Value.Clone()));
                }
                // else: unrecognized @-prefixed property, ignore
            }
        }

        return new ClefEvent(document)
        {
            Timestamp = timestamp,
            Level = level,
            MessageTemplate = messageTemplate,
            RenderedMessage = renderedMessage,
            Exception = exception,
            EventId = eventId,
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            SpanStart = spanStart,
            SpanKind = spanKind,
            ScopeAttributes = scopeAttributes ?? [],
            ResourceAttributes = resourceAttributes ?? [],
            Properties = properties
        };
    }

    static List<KeyValuePair<string, JsonElement>> ReadProperties(JsonElement obj)
    {
        var properties = new List<KeyValuePair<string, JsonElement>>();
        foreach (var prop in obj.EnumerateObject())
            properties.Add(new KeyValuePair<string, JsonElement>(prop.Name, prop.Value.Clone()));
        return properties;
    }
}
