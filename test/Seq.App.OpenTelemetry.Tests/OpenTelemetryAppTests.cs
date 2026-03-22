using Seq.App.OpenTelemetry.Tests.Support;
using Seq.Apps.Testing.Hosting;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class OpenTelemetryAppTests
{
    [Fact]
    public async Task LogEventFlowsEndToEnd()
    {
        var exporter = new TestExporter();
        var app = new OpenTelemetryApp(exporter);
        var host = new TestAppHost();
        app.Attach(host);

        await app.OnAsync("""{"@t":"2024-01-15T10:30:00Z","@l":"Warning","@mt":"Disk space low on {Drive}","Drive":"C:"}""");

        await app.DisposeAsync();

        Assert.Single(exporter.LogRequests);
        var logRecord = exporter.LogRequests[0].ResourceLogs[0].ScopeLogs[0].LogRecords[0];
        Assert.Equal("Disk space low on {Drive}", logRecord.Body.StringValue);
        Assert.Equal(SeverityNumber.Warn, logRecord.SeverityNumber);
    }

    [Fact]
    public async Task SpanEventFlowsEndToEnd()
    {
        var exporter = new TestExporter();
        var app = new OpenTelemetryApp(exporter);
        var host = new TestAppHost();
        app.Attach(host);

        await app.OnAsync("""
        {
            "@t":"2024-01-15T10:30:01Z",
            "@tr":"0af7651916cd43dd8448eb211c80319c",
            "@sp":"00f067aa0ba902b7",
            "@st":"2024-01-15T10:30:00Z",
            "@mt":"GET /api/users",
            "@sk":"Server"
        }
        """);

        await app.DisposeAsync();

        Assert.Single(exporter.TraceRequests);
        var span = exporter.TraceRequests[0].ResourceSpans[0].ScopeSpans[0].Spans[0];
        Assert.Equal("GET /api/users", span.Name);
        Assert.Equal(Span.Types.SpanKind.Server, span.Kind);
    }

    [Fact]
    public void InvalidProtocolThrowsOnAttach()
    {
        var exporter = new TestExporter();
        var app = new OpenTelemetryApp(exporter);
        app.Protocol = "Invalid";

        var ex = Record.Exception(() => app.Attach(new TestAppHost()));
        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public void InvalidNumericSettingsThrowOnAttach()
    {
        var exporter = new TestExporter();
        var app = new OpenTelemetryApp(exporter);
        app.MaxBatchSize = 0;

        var ex = Record.Exception(() => app.Attach(new TestAppHost()));
        Assert.IsType<ArgumentException>(ex);
    }
}
