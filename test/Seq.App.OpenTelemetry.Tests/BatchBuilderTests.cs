using Seq.App.OpenTelemetry.Batching;
using Xunit;

namespace Seq.App.OpenTelemetry.Tests;

public class BatchBuilderTests
{
    static InstrumentationScope Scope(string name, string version)
    {
        return new InstrumentationScope { Name = name, Version = version };
    }

    [Fact]
    public void RecordsWithIdenticalScopesShareAScopeGroup()
    {
        var builder = new BatchBuilder();
        var resource = new Resource();

        builder.AddLog(new LogRecord(), resource, Scope("MyLibrary", "1.0"));
        builder.AddLog(new LogRecord(), resource, Scope("MyLibrary", "1.0"));

        var (logs, _) = builder.Build();

        var scopeLogs = Assert.Single(logs!.ResourceLogs[0].ScopeLogs);
        Assert.Equal(2, scopeLogs.LogRecords.Count);
    }

    [Fact]
    public void SameNamedScopesWithDifferentVersionsAreNotMerged()
    {
        var builder = new BatchBuilder();
        var resource = new Resource();

        builder.AddLog(new LogRecord(), resource, Scope("MyLibrary", "1.0"));
        builder.AddLog(new LogRecord(), resource, Scope("MyLibrary", "2.0"));

        var (logs, _) = builder.Build();

        Assert.Equal(2, logs!.ResourceLogs[0].ScopeLogs.Count);
    }

    [Fact]
    public void SameNamedScopesWithDifferentAttributesAreNotMerged()
    {
        var builder = new BatchBuilder();
        var resource = new Resource();

        var first = Scope("MyLibrary", "1.0");
        first.Attributes.Add(new KeyValue { Key = "tenant", Value = new AnyValue { StringValue = "a" } });
        var second = Scope("MyLibrary", "1.0");
        second.Attributes.Add(new KeyValue { Key = "tenant", Value = new AnyValue { StringValue = "b" } });

        builder.AddSpan(new Span(), resource, first);
        builder.AddSpan(new Span(), resource, second);

        var (_, traces) = builder.Build();

        Assert.Equal(2, traces!.ResourceSpans[0].ScopeSpans.Count);
    }

    [Fact]
    public void ScopelessRecordsGroupTogether()
    {
        var builder = new BatchBuilder();
        var resource = new Resource();

        builder.AddLog(new LogRecord(), resource, null);
        builder.AddLog(new LogRecord(), resource, null);

        var (logs, _) = builder.Build();

        var scopeLogs = Assert.Single(logs!.ResourceLogs[0].ScopeLogs);
        Assert.Equal(2, scopeLogs.LogRecords.Count);
        Assert.Null(scopeLogs.Scope);
    }
}
