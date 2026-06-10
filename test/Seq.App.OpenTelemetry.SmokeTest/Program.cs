using System.Globalization;
using System.Reflection;
using Seq.App.OpenTelemetry;
using Seq.App.OpenTelemetry.SmokeTest.Support;
using Seq.Apps;
using Serilog;

var app = new OpenTelemetryApp();

foreach (var property in typeof(OpenTelemetryApp).GetProperties())
{
    if (property.GetCustomAttribute<SeqAppSettingAttribute>() == null)
        continue;

    var envValue = Environment.GetEnvironmentVariable($"SEQ_APP_SETTING_{property.Name.ToUpperInvariant()}");
    if (envValue == null)
        continue;

    var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
    var value = targetType.IsEnum
        ? Enum.Parse(targetType, envValue, ignoreCase: true)
        : Convert.ChangeType(envValue, targetType, CultureInfo.InvariantCulture);

    property.SetValue(app, value);
    Console.WriteLine($"{property.Name} = {envValue}");
}

var errors = new ErrorCountingSink();
await using var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.Sink(errors)
    .CreateLogger();

try
{
    app.Attach(new SmokeTestAppHost(logger));
    Console.WriteLine("App attached successfully.");

    var traceId = RandomHex(16);
    var spanId = RandomHex(8);
    var spanEnd = DateTime.UtcNow;
    var spanStart = spanEnd - TimeSpan.FromMilliseconds(120);

    // Nested resource attributes; the exporter should flatten this to a `service.name` attribute.
    var resourceAttributes = "\"@ra\":{\"service\":{\"name\":\"Seq.App.OpenTelemetry.SmokeTest\"}}";

    var sampleLog = $$"""
        {"@t":"{{Timestamp(spanEnd)}}","@l":"Information","@mt":"Smoke test event from {Source}","@tr":"{{traceId}}","@sp":"{{spanId}}",{{resourceAttributes}},"Source":"Program.cs"}
        """;
    await app.OnAsync(sampleLog);
    Console.WriteLine("Sample log event sent.");

    var sampleSpan = $$"""
        {"@t":"{{Timestamp(spanEnd)}}","@mt":"Smoke test span","@tr":"{{traceId}}","@sp":"{{spanId}}","@st":"{{Timestamp(spanStart)}}","@sk":"Internal",{{resourceAttributes}}}
        """;
    await app.OnAsync(sampleSpan);
    Console.WriteLine("Sample span sent.");

    await app.DisposeAsync();
    Console.WriteLine("App disposed.");

    if (errors.ErrorCount > 0)
    {
        Console.Error.WriteLine($"Smoke test failed: {errors.ErrorCount} error(s) were logged during export.");
        return 1;
    }

    Console.WriteLine($"Smoke test passed. Check the target server for the log event and span with trace id {traceId}.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Smoke test failed: {ex}");
    return 1;
}

return 0;

static string Timestamp(DateTime instant) => instant.ToString("O", CultureInfo.InvariantCulture);

static string RandomHex(int bytes)
{
    var buffer = new byte[bytes];
    Random.Shared.NextBytes(buffer);
    return Convert.ToHexStringLower(buffer);
}