namespace Seq.App.OpenTelemetry.Export;

interface IOtlpExporter : IAsyncDisposable
{
    Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct);
    Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct);
}
