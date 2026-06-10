using Seq.App.OpenTelemetry.Export;

namespace Seq.App.OpenTelemetry.Tests.Support;

sealed class TestExporter : OtlpExporter
{
    public List<ExportLogsServiceRequest> LogRequests { get; } = [];
    public List<ExportTraceServiceRequest> TraceRequests { get; } = [];
    public int LogCallCount;
    public int TraceCallCount;
    public int FailCount { get; set; }
    public TimeSpan ExportDelay { get; set; }

    public override async Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct)
    {
        if (ExportDelay > TimeSpan.Zero)
            await Task.Delay(ExportDelay, ct);

        var count = Interlocked.Increment(ref LogCallCount);
        if (count <= FailCount)
            throw new HttpRequestException("Transient failure");

        LogRequests.Add(request);
    }

    public override async Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct)
    {
        if (ExportDelay > TimeSpan.Zero)
            await Task.Delay(ExportDelay, ct);

        var count = Interlocked.Increment(ref TraceCallCount);
        if (count <= FailCount)
            throw new HttpRequestException("Transient failure");

        TraceRequests.Add(request);
    }

    public override ValueTask DisposeAsync() => default;
}
