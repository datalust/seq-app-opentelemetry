using Grpc.Core;
using Grpc.Net.Client;

namespace Seq.App.OpenTelemetry.Export;

class GrpcExporter : IOtlpExporter
{
    readonly GrpcChannel _channel;
    readonly LogsService.LogsServiceClient _logsClient;
    readonly TraceService.TraceServiceClient _tracesClient;
    readonly Metadata? _metadata;

    public GrpcExporter(string endpoint, IReadOnlyList<KeyValuePair<string, string>>? headers)
    {
        _channel = GrpcChannel.ForAddress(endpoint);
        _logsClient = new LogsService.LogsServiceClient(_channel);
        _tracesClient = new TraceService.TraceServiceClient(_channel);

        if (headers is { Count: > 0 })
        {
            _metadata = new Metadata();
            foreach (var header in headers)
                _metadata.Add(header.Key, header.Value);
        }
    }

    public async Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct)
    {
        try
        {
            await _logsClient.ExportAsync(request, _metadata, cancellationToken: ct);
        }
        catch (RpcException ex) when (IsRetryableStatus(ex.StatusCode))
        {
            await Task.Delay(1000, ct);
            await _logsClient.ExportAsync(request, _metadata, cancellationToken: ct);
        }
    }

    public async Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct)
    {
        try
        {
            await _tracesClient.ExportAsync(request, _metadata, cancellationToken: ct);
        }
        catch (RpcException ex) when (IsRetryableStatus(ex.StatusCode))
        {
            await Task.Delay(1000, ct);
            await _tracesClient.ExportAsync(request, _metadata, cancellationToken: ct);
        }
    }

    static bool IsRetryableStatus(StatusCode code)
    {
        return code is StatusCode.Unavailable or StatusCode.DeadlineExceeded;
    }

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return default;
    }
}
