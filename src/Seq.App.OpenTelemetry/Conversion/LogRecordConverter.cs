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
            var converted = AnyValueConverter.Convert(prop.Value);
            if (converted != null)
            {
                record.Attributes.Add(new KeyValue
                {
                    Key = prop.Key,
                    Value = converted
                });
            }
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
                Key = "seq.event.id",
                Value = new AnyValue { StringValue = evt.EventId }
            });
        }

        if (evt.Exception != null)
        {
            AddExceptionAttributes(record, evt.Exception);
        }

        return record;
    }

    static void AddExceptionAttributes(LogRecord record, string exception)
    {
        // Try to parse "TypeName: Message\n   at ..." format
        var firstNewline = exception.IndexOf('\n');
        if (firstNewline > 0)
        {
            var firstLine = exception[..firstNewline].TrimEnd('\r');
            var colonIndex = firstLine.IndexOf(": ", StringComparison.Ordinal);
            if (colonIndex > 0)
            {
                record.Attributes.Add(new KeyValue
                {
                    Key = "exception.type",
                    Value = new AnyValue { StringValue = firstLine[..colonIndex] }
                });
                record.Attributes.Add(new KeyValue
                {
                    Key = "exception.message",
                    Value = new AnyValue { StringValue = firstLine[(colonIndex + 2)..] }
                });
            }
        }

        record.Attributes.Add(new KeyValue
        {
            Key = "exception.stacktrace",
            Value = new AnyValue { StringValue = exception }
        });
    }

    internal static ulong ToUnixNano(DateTimeOffset timestamp)
    {
        return (ulong)(timestamp.UtcTicks - DateTimeOffset.UnixEpoch.Ticks) * 100;
    }
}
