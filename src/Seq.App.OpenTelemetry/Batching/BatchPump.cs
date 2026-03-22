using Seq.App.OpenTelemetry.Export;
using Serilog;

namespace Seq.App.OpenTelemetry.Batching;

class BatchPump : IAsyncDisposable
{
    readonly IOtlpExporter _exporter;
    readonly int _maxBatchSize;
    readonly int _maxQueueSize;
    readonly int _flushIntervalMs;
    readonly ILogger _log;

    readonly BatchBuilder _builder = new();
    readonly object _sync = new();
    readonly SemaphoreSlim _signal = new(0);
    readonly SemaphoreSlim _backpressure = new(0);
    readonly CancellationTokenSource _cts = new();
    readonly Task _pumpTask;

    volatile bool _backpressureActive;
    volatile bool _disposed;

    public BatchPump(IOtlpExporter exporter, int maxBatchSize, int maxQueueSize, int flushIntervalMs, ILogger log)
    {
        _exporter = exporter;
        _maxBatchSize = maxBatchSize;
        _maxQueueSize = maxQueueSize;
        _flushIntervalMs = flushIntervalMs;
        _log = log;

        _pumpTask = Task.Run(PumpLoop);
    }

    public async Task EnqueueLogAsync(LogRecord record, Resource resource, InstrumentationScope? scope)
    {
        int count;
        lock (_sync)
        {
            _builder.AddLog(record, resource, scope);
            count = _builder.Count;
        }

        if (count >= _maxBatchSize)
            _signal.Release();

        if (count >= _maxQueueSize)
        {
            _backpressureActive = true;
            await _backpressure.WaitAsync();
        }
    }

    public async Task EnqueueSpanAsync(Span span, Resource resource, InstrumentationScope? scope)
    {
        int count;
        lock (_sync)
        {
            _builder.AddSpan(span, resource, scope);
            count = _builder.Count;
        }

        if (count >= _maxBatchSize)
            _signal.Release();

        if (count >= _maxQueueSize)
        {
            _backpressureActive = true;
            await _backpressure.WaitAsync();
        }
    }

    async Task PumpLoop()
    {
        var ct = _cts.Token;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait for signal or timer
                var signalTask = _signal.WaitAsync(ct);
                var delayTask = Task.Delay(_flushIntervalMs, ct);
                await Task.WhenAny(signalTask, delayTask);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushAsync();
        }

        // Final flush on shutdown
        await FlushAsync();
    }

    async Task FlushAsync()
    {
        var (logsRequest, tracesRequest) = BuildBatch();

        if (logsRequest == null && tracesRequest == null)
            return;

        try
        {
            if (logsRequest != null)
                await _exporter.ExportLogsAsync(logsRequest, CancellationToken.None);

            if (tracesRequest != null)
                await _exporter.ExportTracesAsync(tracesRequest, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "OTLP export failed; batch dropped");
        }
    }

    (ExportLogsServiceRequest?, ExportTraceServiceRequest?) BuildBatch()
    {
        lock (_sync)
        {
            var result = _builder.Build();

            if (_backpressureActive)
            {
                _backpressureActive = false;
                _backpressure.Release();
            }

            return result;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _cts.CancelAsync();

        try
        {
            await _pumpTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _cts.Dispose();
        _signal.Dispose();
        _backpressure.Dispose();
    }
}
