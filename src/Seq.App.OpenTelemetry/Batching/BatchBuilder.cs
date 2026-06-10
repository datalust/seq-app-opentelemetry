namespace Seq.App.OpenTelemetry.Batching;

class BatchBuilder
{
    // Grouped by resource fingerprint → scope name → records
    readonly Dictionary<string, ResourceLogGroup> _logGroups = new();
    readonly Dictionary<string, ResourceSpanGroup> _spanGroups = new();

    int _count;

    public int Count => _count;

    public void AddLog(LogRecord record, Resource resource, InstrumentationScope? scope)
    {
        var resourceKey = GetResourceKey(resource);

        if (!_logGroups.TryGetValue(resourceKey, out var group))
        {
            group = new ResourceLogGroup(resource);
            _logGroups[resourceKey] = group;
        }

        var scopeKey = GetScopeKey(scope);
        if (!group.ScopeGroups.TryGetValue(scopeKey, out var scopeGroup))
        {
            scopeGroup = new ScopeLogGroup(scope);
            group.ScopeGroups[scopeKey] = scopeGroup;
        }

        scopeGroup.Records.Add(record);
        _count++;
    }

    public void AddSpan(Span span, Resource resource, InstrumentationScope? scope)
    {
        var resourceKey = GetResourceKey(resource);

        if (!_spanGroups.TryGetValue(resourceKey, out var group))
        {
            group = new ResourceSpanGroup(resource);
            _spanGroups[resourceKey] = group;
        }

        var scopeKey = GetScopeKey(scope);
        if (!group.ScopeGroups.TryGetValue(scopeKey, out var scopeGroup))
        {
            scopeGroup = new ScopeSpanGroup(scope);
            group.ScopeGroups[scopeKey] = scopeGroup;
        }

        scopeGroup.Spans.Add(span);
        _count++;
    }

    public (ExportLogsServiceRequest? Logs, ExportTraceServiceRequest? Traces) Build()
    {
        ExportLogsServiceRequest? logsRequest = null;
        ExportTraceServiceRequest? tracesRequest = null;

        if (_logGroups.Count > 0)
        {
            logsRequest = new ExportLogsServiceRequest();
            foreach (var group in _logGroups.Values)
            {
                var resourceLogs = new ResourceLogs { Resource = group.Resource };
                foreach (var scopeGroup in group.ScopeGroups.Values)
                {
                    var scopeLogs = new ScopeLogs();
                    if (scopeGroup.Scope != null)
                        scopeLogs.Scope = scopeGroup.Scope;
                    scopeLogs.LogRecords.AddRange(scopeGroup.Records);
                    resourceLogs.ScopeLogs.Add(scopeLogs);
                }
                logsRequest.ResourceLogs.Add(resourceLogs);
            }
        }

        if (_spanGroups.Count > 0)
        {
            tracesRequest = new ExportTraceServiceRequest();
            foreach (var group in _spanGroups.Values)
            {
                var resourceSpans = new ResourceSpans { Resource = group.Resource };
                foreach (var scopeGroup in group.ScopeGroups.Values)
                {
                    var scopeSpans = new ScopeSpans();
                    if (scopeGroup.Scope != null)
                        scopeSpans.Scope = scopeGroup.Scope;
                    scopeSpans.Spans.AddRange(scopeGroup.Spans);
                    resourceSpans.ScopeSpans.Add(scopeSpans);
                }
                tracesRequest.ResourceSpans.Add(resourceSpans);
            }
        }

        Reset();
        return (logsRequest, tracesRequest);
    }

    void Reset()
    {
        _logGroups.Clear();
        _spanGroups.Clear();
        _count = 0;
    }

    static string GetResourceKey(Resource resource)
    {
        // Simple fingerprint: sorted key=value pairs
        var parts = resource.Attributes
            .OrderBy(a => a.Key)
            .Select(a => $"{a.Key}={a.Value}");
        return string.Join("|", parts);
    }

    static string GetScopeKey(InstrumentationScope? scope)
    {
        // Name alone isn't sufficient: same-named scopes with different versions or attributes
        // must not be merged, or records would be reported under the wrong scope.
        if (scope == null)
            return "";

        var parts = scope.Attributes
            .OrderBy(a => a.Key)
            .Select(a => $"{a.Key}={a.Value}");
        return $"{scope.Name}\n{scope.Version}\n{string.Join("|", parts)}";
    }

    sealed class ResourceLogGroup(Resource resource)
    {
        public Resource Resource { get; } = resource;
        public Dictionary<string, ScopeLogGroup> ScopeGroups { get; } = new();
    }

    sealed class ScopeLogGroup(InstrumentationScope? scope)
    {
        public InstrumentationScope? Scope { get; } = scope;
        public List<LogRecord> Records { get; } = [];
    }

    sealed class ResourceSpanGroup(Resource resource)
    {
        public Resource Resource { get; } = resource;
        public Dictionary<string, ScopeSpanGroup> ScopeGroups { get; } = new();
    }

    sealed class ScopeSpanGroup(InstrumentationScope? scope)
    {
        public InstrumentationScope? Scope { get; } = scope;
        public List<Span> Spans { get; } = [];
    }
}
