using Seq.App.OpenTelemetry.Export;
using Serilog;

namespace Seq.App.OpenTelemetry.Batching;

class BatchPump : IAsyncDisposable
{
    readonly OtlpExporter _exporter;
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

    public BatchPump(OtlpExporter exporter, int maxBatchSize, int maxQueueSize, int flushIntervalMs, ILogger log)
    {
        _exporter = exporter;
        _maxBatchSize = maxBatchSize;
        _maxQueueSize = maxQueueSize;
        _flushIntervalMs = flushIntervalMs;
        _log = log;

        _pumpTask = Task.Run(PumpLoop);
    }

    public Task EnqueueLogAsync(LogRecord record, Resource resource, InstrumentationScope? scope)
    {
        int count;
        lock (_sync)
        {
            _builder.AddLog(record, resource, scope);
            count = AfterAddLocked();
        }

        return AfterAdd(count);
    }

    public Task EnqueueSpanAsync(Span span, Resource resource, InstrumentationScope? scope)
    {
        int count;
        lock (_sync)
        {
            _builder.AddSpan(span, resource, scope);
            count = AfterAddLocked();
        }

        return AfterAdd(count);
    }

    int AfterAddLocked()
    {
        var count = _builder.Count;

        // Setting the flag inside the lock means the pump can't miss it between this add and the
        // producer's subsequent wait: any flush that observes the over-full builder also observes
        // the flag and releases (or has already released) `_backpressure`.
        if (count >= _maxQueueSize)
            _backpressureActive = true;

        return count;
    }

    Task AfterAdd(int count)
    {
        // Best-effort guard: the host stops delivering events before disposing, but a late
        // arrival shouldn't hit the disposed semaphores.
        if (_disposed)
            return Task.CompletedTask;

        // `==` rather than `>=`: producers are serial, so the threshold is crossed exactly once
        // per batch, and permits don't accumulate on the semaphore.
        if (count == _maxBatchSize)
            _signal.Release();

        if (count >= _maxQueueSize)
            return _backpressure.WaitAsync();

        return Task.CompletedTask;
    }

    async Task PumpLoop()
    {
        var ct = _cts.Token;
        Task? signalTask = null;

        while (!ct.IsCancellationRequested)
        {
            // Reuse a pending signal waiter across timer-elapsed iterations; creating a new
            // `WaitAsync` each time would queue an abandoned waiter per flush interval.
            signalTask ??= _signal.WaitAsync(ct);
            var delayTask = Task.Delay(_flushIntervalMs, ct);
            var completed = await Task.WhenAny(signalTask, delayTask);

            if (ct.IsCancellationRequested)
                break;

            if (completed == signalTask)
                signalTask = null;

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
