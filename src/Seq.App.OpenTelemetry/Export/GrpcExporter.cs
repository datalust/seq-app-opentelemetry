using Grpc.Core;
using Grpc.Net.Client;
using Serilog;

namespace Seq.App.OpenTelemetry.Export;

class GrpcExporter : OtlpExporter
{
    readonly GrpcChannel _channel;
    readonly LogsService.LogsServiceClient _logsClient;
    readonly TraceService.TraceServiceClient _tracesClient;
    readonly Metadata? _metadata;
    readonly ILogger _log;

    public GrpcExporter(string endpoint, IReadOnlyList<KeyValuePair<string, string>>? headers, ILogger log)
    {
        _log = log;
        _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            ThrowOperationCanceledOnCancellation = true
        });
        _logsClient = new LogsService.LogsServiceClient(_channel);
        _tracesClient = new TraceService.TraceServiceClient(_channel);

        if (headers is { Count: > 0 })
        {
            _metadata = new Metadata();
            foreach (var header in headers)
                _metadata.Add(header.Key, header.Value);
        }
    }

    public override async Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct)
    {
        ExportLogsServiceResponse response;
        try
        {
            response = await _logsClient.ExportAsync(request, _metadata, DateTime.UtcNow + RequestTimeout, ct);
        }
        catch (RpcException ex) when (IsRetryableStatus(ex.StatusCode))
        {
            await Task.Delay(1000, ct);
            response = await _logsClient.ExportAsync(request, _metadata, DateTime.UtcNow + RequestTimeout, ct);
        }

        var partialSuccess = response.PartialSuccess;
        if (partialSuccess != null && (partialSuccess.RejectedLogRecords > 0 || partialSuccess.ErrorMessage.Length > 0))
        {
            _log.Warning("OTLP logs export partially succeeded; {RejectedCount} log record(s) rejected: {ErrorMessage}",
                partialSuccess.RejectedLogRecords, partialSuccess.ErrorMessage);
        }
    }

    public override async Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct)
    {
        ExportTraceServiceResponse response;
        try
        {
            response = await _tracesClient.ExportAsync(request, _metadata, DateTime.UtcNow + RequestTimeout, ct);
        }
        catch (RpcException ex) when (IsRetryableStatus(ex.StatusCode))
        {
            await Task.Delay(1000, ct);
            response = await _tracesClient.ExportAsync(request, _metadata, DateTime.UtcNow + RequestTimeout, ct);
        }

        var partialSuccess = response.PartialSuccess;
        if (partialSuccess != null && (partialSuccess.RejectedSpans > 0 || partialSuccess.ErrorMessage.Length > 0))
        {
            _log.Warning("OTLP traces export partially succeeded; {RejectedCount} span(s) rejected: {ErrorMessage}",
                partialSuccess.RejectedSpans, partialSuccess.ErrorMessage);
        }
    }

    static bool IsRetryableStatus(StatusCode code)
    {
        // The OTLP spec names Unavailable, DeadlineExceeded, and ResourceExhausted (throttling)
        // as retryable.
        return code is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted;
    }

    public override ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return default;
    }
}
