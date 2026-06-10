using Seq.App.OpenTelemetry.Parsing;

namespace Seq.App.OpenTelemetry.Conversion;

static class LogRecordConverter
{
    public static LogRecord Convert(ClefEvent evt)
    {
        var record = new LogRecord
        {
            TimeUnixNano = ToUnixNano(evt.Timestamp),
            ObservedTimeUnixNano = ToUnixNano(evt.Timestamp),
            SeverityNumber = SeverityMapping.ToNumber(evt.Level),
            SeverityText = SeverityMapping.ToText(evt.Level),
            Body = new AnyValue { StringValue = evt.RenderedMessage ?? evt.MessageTemplate ?? "" },
            TraceId = HexBytes.ToTraceId(evt.TraceId),
            SpanId = HexBytes.ToSpanId(evt.SpanId)
        };

        // User properties → attributes
        foreach (var prop in evt.Properties)
        {
            record.Attributes.Add(new KeyValue
            {
                Key = prop.Key,
                Value = AnyValueConverter.Convert(prop.Value)
            });
        }

        // Well-known attributes
        if (evt.MessageTemplate != null)
        {
            record.Attributes.Add(new KeyValue
            {
                Key = "message_template.text",
                Value = new AnyValue { StringValue = evt.MessageTemplate }
            });
        }

        if (evt.EventId != null)
        {
            record.Attributes.Add(new KeyValue
            {
                Key = "seq.event_type",
                Value = new AnyValue { StringValue = evt.EventId }
            });
        }

        if (evt.Exception != null)
        {
            ExceptionAttributes.AddTo(record.Attributes, evt.Exception);
        }

        return record;
    }

    internal static ulong ToUnixNano(DateTimeOffset timestamp)
    {
        // OTLP defines zero as "unknown or missing timestamp"; pre-epoch values (including
        // `default`, from events with missing or malformed `@t`) would otherwise wrap.
        var ticks = timestamp.UtcTicks - DateTimeOffset.UnixEpoch.Ticks;
        return ticks <= 0 ? 0ul : (ulong)ticks * 100;
    }
}
