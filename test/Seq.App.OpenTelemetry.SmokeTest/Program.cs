using Seq.Apps.Testing.Hosting;

var app = new Seq.App.OpenTelemetry.OpenTelemetryApp();
var host = new TestAppHost();
var settings = (IDictionary<string, string>)host.App.Settings;

// Read settings from SEQ_APP_SETTING_* environment variables
var settingNames = new[]
{
    "Endpoint", "Protocol", "Headers", "ResourceAttributes",
    "MaxBatchSize", "MaxQueueSize", "FlushIntervalMs"
};

foreach (var name in settingNames)
{
    var envValue = Environment.GetEnvironmentVariable($"SEQ_APP_SETTING_{name.ToUpperInvariant()}");
    if (envValue != null)
        settings[name] = envValue;
}

try
{
    app.Attach(host);
    Console.WriteLine("App attached successfully.");

    var sampleLog = """{"@t":"2024-01-15T10:30:00Z","@l":"Information","@mt":"Smoke test event from {Source}","Source":"Seq.App.OpenTelemetry.SmokeTest"}""";

    await app.OnAsync(sampleLog);
    Console.WriteLine("Sample event sent.");

    await app.DisposeAsync();
    Console.WriteLine("App disposed. Smoke test passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Smoke test failed: {ex}");
    return 1;
}

return 0;
