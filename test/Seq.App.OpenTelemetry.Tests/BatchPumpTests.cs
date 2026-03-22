using Seq.App.OpenTelemetry.Batching;
using Seq.App.OpenTelemetry.Tests.Support;
using Serilog;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class BatchPumpTests
{
    [Fact]
    public async Task FlushesOnTimer()
    {
        var exporter = new TestExporter();
        var pump = new BatchPump(exporter, maxBatchSize: 1000, maxQueueSize: 10000,
            flushIntervalMs: 100, log: Log.Logger);

        var resource = new Resource();
        resource.Attributes.Add(new KeyValue
        {
            Key = "service.name",
            Value = new AnyValue { StringValue = "test" }
        });

        var record = new LogRecord
        {
            TimeUnixNano = 1000,
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "Information",
            Body = new AnyValue { StringValue = "test" }
        };

        await pump.EnqueueLogAsync(record, resource, null);

        // Wait for the timer-based flush
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Single(exporter.LogRequests);
        Assert.Empty(exporter.TraceRequests);

        await pump.DisposeAsync();
    }

    [Fact]
    public async Task FlushesOnBatchSize()
    {
        var exporter = new TestExporter();
        var pump = new BatchPump(exporter, maxBatchSize: 5, maxQueueSize: 10000,
            flushIntervalMs: 60000, log: Log.Logger);

        var resource = new Resource();

        for (var i = 0; i < 5; i++)
        {
            var record = new LogRecord
            {
                TimeUnixNano = (ulong)i,
                Body = new AnyValue { StringValue = $"event {i}" }
            };
            await pump.EnqueueLogAsync(record, resource, null);
        }

        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.NotEmpty(exporter.LogRequests);

        await pump.DisposeAsync();
    }

    [Fact]
    public async Task FinalFlushOnDispose()
    {
        var exporter = new TestExporter();
        var pump = new BatchPump(exporter, maxBatchSize: 1000, maxQueueSize: 10000,
            flushIntervalMs: 60000, log: Log.Logger);

        var resource = new Resource();
        var record = new LogRecord
        {
            TimeUnixNano = 1000,
            Body = new AnyValue { StringValue = "test" }
        };

        await pump.EnqueueLogAsync(record, resource, null);

        // Dispose triggers final flush (flush interval is very long, so timer won't fire)
        await pump.DisposeAsync();

        Assert.Single(exporter.LogRequests);
    }

    [Fact]
    public async Task ExportsSpans()
    {
        var exporter = new TestExporter();
        var pump = new BatchPump(exporter, maxBatchSize: 1000, maxQueueSize: 10000,
            flushIntervalMs: 100, log: Log.Logger);

        var resource = new Resource();
        var span = new Span
        {
            Name = "test-span",
            StartTimeUnixNano = 1000,
            EndTimeUnixNano = 2000
        };

        await pump.EnqueueSpanAsync(span, resource, null);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Single(exporter.TraceRequests);
        Assert.Empty(exporter.LogRequests);

        await pump.DisposeAsync();
    }

    [Fact]
    public async Task SurvivesExportFailureAndContinues()
    {
        var exporter = new TestExporter { FailCount = 1 };
        var pump = new BatchPump(exporter, maxBatchSize: 1000, maxQueueSize: 10000,
            flushIntervalMs: 100, log: Log.Logger);

        var resource = new Resource();

        // First batch — will fail in the exporter
        await pump.EnqueueLogAsync(new LogRecord
        {
            TimeUnixNano = 1000,
            Body = new AnyValue { StringValue = "will fail" }
        }, resource, null);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Second batch — should succeed (pump still running after failure)
        await pump.EnqueueLogAsync(new LogRecord
        {
            TimeUnixNano = 2000,
            Body = new AnyValue { StringValue = "should succeed" }
        }, resource, null);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Single(exporter.LogRequests);

        await pump.DisposeAsync();
    }

    [Fact]
    public async Task BackpressureBlocksEnqueue()
    {
        var exporter = new TestExporter();

        // maxBatchSize is large so enqueues don't trigger a batch-size flush.
        // maxQueueSize=4: the first 3 enqueues pass (count 1,2,3 < 4),
        // the 4th hits backpressure (count 4 >= 4).
        // flushIntervalMs=500 so the pump eventually drains via timer.
        var pump = new BatchPump(exporter, maxBatchSize: 100, maxQueueSize: 4,
            flushIntervalMs: 500, log: Log.Logger);

        var resource = new Resource();

        // Fill the queue to maxQueueSize without triggering a flush
        for (var i = 0; i < 3; i++)
        {
            await pump.EnqueueLogAsync(new LogRecord
            {
                TimeUnixNano = (ulong)i,
                Body = new AnyValue { StringValue = $"event {i}" }
            }, resource, null);
        }

        // The 4th enqueue should block: count >= maxQueueSize and
        // the pump hasn't been signaled (count < maxBatchSize)
        var enqueueTask = pump.EnqueueLogAsync(new LogRecord
        {
            TimeUnixNano = 4,
            Body = new AnyValue { StringValue = "blocked event" }
        }, resource, null);

        // Within 200ms the enqueue should still be blocked (timer hasn't fired)
        var completedTask = await Task.WhenAny(enqueueTask, Task.Delay(200, TestContext.Current.CancellationToken));
        Assert.NotEqual(enqueueTask, completedTask);

        // After the timer fires (~500ms) the pump drains and releases backpressure
        await enqueueTask;

        await pump.DisposeAsync();
    }
}
