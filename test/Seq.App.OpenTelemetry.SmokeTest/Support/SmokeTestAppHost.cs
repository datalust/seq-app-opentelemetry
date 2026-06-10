using Seq.Apps;
using Seq.Apps.Testing.Hosting;
using Serilog;

namespace Seq.App.OpenTelemetry.SmokeTest.Support;

class SmokeTestAppHost(ILogger logger) : IAppHost
{
    readonly TestAppHost _defaults = new();

    public Apps.App App => _defaults.App;
    public Host Host => _defaults.Host;
    public ILogger Logger { get; } = logger;
    public string StoragePath => _defaults.StoragePath;
}