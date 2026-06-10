using System.Text.Json;

namespace Seq.App.OpenTelemetry.Parsing;

class ClefEvent : IDisposable
{
    readonly JsonDocument _document;

    public ClefEvent(JsonDocument document)
    {
        _document = document;
    }

    // Core CLEF properties
    public DateTimeOffset Timestamp { get; init; }
    public string? Level { get; init; }
    public string? MessageTemplate { get; init; }
    public string? RenderedMessage { get; init; }
    public string? Exception { get; init; }
    public string? EventId { get; init; }

    // Trace/span properties
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public DateTimeOffset? SpanStart { get; init; }
    public string? SpanKind { get; init; }

    // Resource and scope attributes. Seq sends these as object-valued `@ra`/`@sa` properties, so
    // they share the same flat shape as `Properties`. Scope's `name` and `version` are well-known
    // attributes that sit alongside the other scope attributes here.
    public List<KeyValuePair<string, JsonElement>> ResourceAttributes { get; init; } = [];
    public List<KeyValuePair<string, JsonElement>> ScopeAttributes { get; init; } = [];

    // All non-reified properties
    public List<KeyValuePair<string, JsonElement>> Properties { get; init; } = [];

    /// <summary>
    /// An event is a span if @tr, @sp, and @st are all present.
    /// </summary>
    public bool IsSpan => TraceId != null && SpanId != null && SpanStart != null;

    public void Dispose() => _document.Dispose();
}
