namespace Seq.App.OpenTelemetry.Conversion;

static class SeverityMapping
{
    /// <summary>
    /// Maps a CLEF <c>@l</c> value to an OTLP severity number. Seq doesn't constrain levels to the
    /// Serilog set — events ingested via OTLP, GELF, syslog, and so on carry their original level
    /// strings (<c>WARN</c>, <c>err</c>, <c>Critical</c>, ...) — so matching is case-insensitive
    /// over common aliases. Unrecognized levels map to <see cref="SeverityNumber.Info"/>;
    /// <see cref="ToText"/> preserves the original string regardless.
    /// </summary>
    public static SeverityNumber ToNumber(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return SeverityNumber.Info;

        return level.Trim().ToLowerInvariant() switch
        {
            "verbose" or "trace" or "trc" => SeverityNumber.Trace,
            "debug" or "dbg" => SeverityNumber.Debug,
            "ok" or "information" or "informational" or "info" or "notice" => SeverityNumber.Info,
            "warning" or "warn" or "wrn" => SeverityNumber.Warn,
            "error" or "err" => SeverityNumber.Error,
            "fatal" or "critical" or "crit" or "alert" or "emergency" or "panic" => SeverityNumber.Fatal,
            _ => SeverityNumber.Info
        };
    }

    public static string ToText(string? level)
    {
        return level ?? "Information";
    }

    public static bool IsFailure(string? level)
    {
        return ToNumber(level) >= SeverityNumber.Error;
    }

    public static bool IsSimpleSpanOkLevel(string? level)
    {
        return level?.ToLowerInvariant() is "ok" or "information" or "informational" or "info" or "notice";
    }
}
