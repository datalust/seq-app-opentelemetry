namespace Seq.App.OpenTelemetry.Conversion;

static class SeverityMapping
{
    public static SeverityNumber ToNumber(string? level)
    {
        return level switch
        {
            "Verbose" => SeverityNumber.Trace,
            "Debug" => SeverityNumber.Debug,
            "Information" or null => SeverityNumber.Info,
            "Warning" => SeverityNumber.Warn,
            "Error" => SeverityNumber.Error,
            "Fatal" => SeverityNumber.Fatal,
            _ => SeverityNumber.Info
        };
    }

    public static string ToText(string? level)
    {
        return level ?? "Information";
    }
}
