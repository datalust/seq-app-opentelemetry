using Seq.App.OpenTelemetry.Batching;
using Seq.App.OpenTelemetry.Conversion;
using Seq.App.OpenTelemetry.Export;
using Seq.App.OpenTelemetry.Parsing;
using Seq.App.OpenTelemetry.Settings;
using Seq.Apps;

namespace Seq.App.OpenTelemetry;

[SeqApp("OpenTelemetry Exporter",
    Description = "Forwards Seq events to an OTLP-compatible endpoint as logs and traces.")]
public class OpenTelemetryApp : SeqApp, ISubscribeToJsonAsync, IAsyncDisposable
{
    IOtlpExporter? _exporter;
    BatchPump? _pump;
    IReadOnlyList<KeyValue>? _configuredResourceAttributes;

    public OpenTelemetryApp() { }

    internal OpenTelemetryApp(IOtlpExporter exporter)
    {
        _exporter = exporter;
    }

    [SeqAppSetting(
        DisplayName = "Endpoint",
        HelpText = "The OTLP endpoint base URL. The default for HTTP is `http://localhost:4318`, " +
                   "and for gRPC is `http://localhost:4317`.",
        IsOptional = true)]
    public string? Endpoint { get; set; }

    [SeqAppSetting(
        DisplayName = "Protocol",
        HelpText = "The OTLP export protocol: `HttpProtobuf` or `Grpc`. The default is `HttpProtobuf`.",
        IsOptional = true)]
    public string? Protocol { get; set; }

    [SeqAppSetting(
        DisplayName = "Headers",
        HelpText = "Additional headers sent with each export request, in `key=value,key=value` format.",
        IsOptional = true,
        InputType = SettingInputType.LongText,
        Syntax = "code")]
    public string? Headers { get; set; }

    [SeqAppSetting(
        DisplayName = "Resource attributes",
        HelpText = "Additional OTLP resource attributes in `key=value,key=value` format.",
        IsOptional = true,
        InputType = SettingInputType.LongText,
        Syntax = "code")]
    public string? ResourceAttributes { get; set; }

    [SeqAppSetting(
        DisplayName = "Max batch size",
        HelpText = "The maximum number of events per export request. The default is `1000`.",
        IsOptional = true,
        InputType = SettingInputType.Integer)]
    public int MaxBatchSize { get; set; } = 1000;

    [SeqAppSetting(
        DisplayName = "Max queue size",
        HelpText = "The maximum number of pending events before backpressure is applied. The default is `10000`.",
        IsOptional = true,
        InputType = SettingInputType.Integer)]
    public int MaxQueueSize { get; set; } = 10000;

    [SeqAppSetting(
        DisplayName = "Flush interval (ms)",
        HelpText = "How often the background thread flushes pending events, in milliseconds. The default is `2000`.",
        IsOptional = true,
        InputType = SettingInputType.Integer)]
    public int FlushIntervalMs { get; set; } = 2000;

    protected override void OnAttached()
    {
        if (MaxBatchSize <= 0)
            throw new ArgumentException("`Max batch size` must be greater than zero.");
        if (MaxQueueSize <= 0)
            throw new ArgumentException("`Max queue size` must be greater than zero.");
        if (FlushIntervalMs <= 0)
            throw new ArgumentException("`Flush interval (ms)` must be greater than zero.");

        var protocol = ParseProtocol(Protocol);

        var endpoint = Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = protocol == OtlpExportProtocol.Grpc
                ? "http://localhost:4317"
                : "http://localhost:4318";
        }

        var headers = ParseHeaders(Headers);
        _configuredResourceAttributes = ResourceBuilder.ParseConfiguredAttributes(ResourceAttributes);

        _exporter ??= protocol == OtlpExportProtocol.Grpc
            ? new GrpcExporter(endpoint, headers)
            : new HttpProtobufExporter(endpoint, headers);

        _pump = new BatchPump(_exporter, MaxBatchSize, MaxQueueSize, FlushIntervalMs, Log);
    }

    public async Task OnAsync(string json)
    {
        using var evt = ClefParser.Parse(json);

        var resource = ResourceBuilder.Build(evt, _configuredResourceAttributes);
        var scope = ResourceBuilder.BuildScope(evt);

        if (evt.IsSpan)
        {
            var span = SpanConverter.Convert(evt);
            await _pump!.EnqueueSpanAsync(span, resource, scope);
        }
        else
        {
            var record = LogRecordConverter.Convert(evt);
            await _pump!.EnqueueLogAsync(record, resource, scope);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pump != null)
            await _pump.DisposeAsync();

        if (_exporter != null)
            await _exporter.DisposeAsync();
    }

    static OtlpExportProtocol ParseProtocol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("HttpProtobuf", StringComparison.OrdinalIgnoreCase))
            return OtlpExportProtocol.HttpProtobuf;

        if (value.Equals("Grpc", StringComparison.OrdinalIgnoreCase))
            return OtlpExportProtocol.Grpc;

        throw new ArgumentException(
            $"Invalid `Protocol` value '{value}'. Expected `HttpProtobuf` or `Grpc`.");
    }

    static IReadOnlyList<KeyValuePair<string, string>>? ParseHeaders(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var result = new List<KeyValuePair<string, string>>();
        foreach (var pair in value.Split(','))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = pair[..eqIndex].Trim();
            var val = pair[(eqIndex + 1)..].Trim();
            result.Add(new KeyValuePair<string, string>(key, val));
        }

        return result.Count > 0 ? result : null;
    }
}
