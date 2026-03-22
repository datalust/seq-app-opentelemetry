namespace Seq.App.OpenTelemetry.Settings;

static class HeaderParser
{
    public static IReadOnlyList<KeyValuePair<string, string>>? ParseHeaders(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var result = new List<KeyValuePair<string, string>>();
        foreach (var pair in value.Split(','))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = pair[..eqIndex].Trim();
            var val = pair[(eqIndex + 1)..].Trim();
            result.Add(new KeyValuePair<string, string>(key, val));
        }

        return result.Count > 0 ? result : null;
    }
}
