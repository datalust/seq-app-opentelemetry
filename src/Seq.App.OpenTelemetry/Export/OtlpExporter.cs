namespace Seq.App.OpenTelemetry.Export;

abstract class OtlpExporter : IAsyncDisposable
{
    protected static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    // The OTLP spec recommends exporters identify themselves with a `name/version` user agent.
    protected static readonly string UserAgent =
        $"SeqAppOpenTelemetry/{typeof(OtlpExporter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    public abstract Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct);
    public abstract Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct);
    public abstract ValueTask DisposeAsync();
}
