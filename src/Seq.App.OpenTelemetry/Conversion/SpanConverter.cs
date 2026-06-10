using Seq.App.OpenTelemetry.Parsing;

namespace Seq.App.OpenTelemetry.Conversion;

static class SpanConverter
{
    public static Span Convert(ClefEvent evt)
    {
        var span = new Span
        {
            TraceId = HexBytes.ToTraceId(evt.TraceId),
            SpanId = HexBytes.ToSpanId(evt.SpanId),
            ParentSpanId = HexBytes.ToSpanId(evt.ParentSpanId),
            Name = evt.MessageTemplate ?? evt.RenderedMessage ?? "unknown",
            Kind = MapSpanKind(evt.SpanKind),
            StartTimeUnixNano = LogRecordConverter.ToUnixNano(evt.SpanStart!.Value),
            EndTimeUnixNano = LogRecordConverter.ToUnixNano(evt.Timestamp)
        };

        // User properties → attributes. Span attributes are restricted to standard (primitive and
        // array) values, so structured properties become JSON strings.
        foreach (var prop in evt.Properties)
        {
            span.Attributes.Add(new KeyValue
            {
                Key = prop.Key,
                Value = AnyValueConverter.ConvertStandard(prop.Value)
            });
        }

        if (evt.Exception != null)
        {
            var exceptionEvent = new Span.Types.Event
            {
                Name = "exception",
                TimeUnixNano = span.EndTimeUnixNano
            };
            ExceptionAttributes.AddTo(exceptionEvent.Attributes, evt.Exception);
            span.Events.Add(exceptionEvent);
        }

        // Status is left unset for non-error spans: in OTLP, `Ok` is a deliberate assertion of
        // success, while `Unset` (the default for an absent status) means "completed normally".
        // Seq surfaces an explicitly-set `Ok` status as an "OK" level, so that round-trips.
        if (SeverityMapping.IsFailure(evt.Level))
        {
            span.Status = new Status
            {
                Code = Status.Types.StatusCode.Error,
                Message = ExceptionAttributes.FirstLine(evt.Exception) ?? ""
            };
        }
        else if (!string.IsNullOrWhiteSpace(evt.Level))
        {
            span.Status = new Status { Code = Status.Types.StatusCode.Ok };

            if (!SeverityMapping.IsSimpleSpanOkLevel(evt.Level))
            {
                span.Attributes.Add(new KeyValue
                {
                    Key = "seq.level",
                    Value = new AnyValue { StringValue = evt.Level }
                });
            }
        }

        return span;
    }

    static Span.Types.SpanKind MapSpanKind(string? kind)
    {
        return kind switch
        {
            "Client" => Span.Types.SpanKind.Client,
            "Server" => Span.Types.SpanKind.Server,
            "Internal" => Span.Types.SpanKind.Internal,
            "Producer" => Span.Types.SpanKind.Producer,
            "Consumer" => Span.Types.SpanKind.Consumer,
            _ => Span.Types.SpanKind.Internal
        };
    }
}
