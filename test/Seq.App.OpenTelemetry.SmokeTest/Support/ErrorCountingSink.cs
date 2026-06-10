using Serilog.Core;
using Serilog.Events;

namespace Seq.App.OpenTelemetry.SmokeTest.Support;

class ErrorCountingSink : ILogEventSink
{
    int _errorCount;

    public int ErrorCount => _errorCount;

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level >= LogEventLevel.Error)
            Interlocked.Increment(ref _errorCount);
    }
}