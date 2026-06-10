namespace Seq.App.OpenTelemetry.Export;

abstract class OtlpExporter : IAsyncDisposable
{
    protected static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public abstract Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct);
    public abstract Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct);
    public abstract ValueTask DisposeAsync();
}
