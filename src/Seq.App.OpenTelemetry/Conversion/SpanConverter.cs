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
            EndTimeUnixNano = LogRecordConverter.ToUnixNano(evt.Timestamp),
            Status = MapStatus(evt.Level)
        };

        // User properties → attributes
        foreach (var prop in evt.Properties)
        {
            var converted = AnyValueConverter.Convert(prop.Value);
            if (converted != null)
            {
                span.Attributes.Add(new KeyValue
                {
                    Key = prop.Key,
                    Value = converted
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

    static Status MapStatus(string? level)
    {
        var isError = level is "Error" or "Fatal";
        return new Status
        {
            Code = isError ? Status.Types.StatusCode.Error : Status.Types.StatusCode.Ok
        };
    }
}
