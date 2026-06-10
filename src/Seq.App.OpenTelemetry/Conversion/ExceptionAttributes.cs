using Google.Protobuf.Collections;

namespace Seq.App.OpenTelemetry.Conversion;

/// <summary>
/// Maps a CLEF <c>@x</c> exception string onto the OpenTelemetry <c>exception.*</c> semantic
/// convention attributes.
/// </summary>
static class ExceptionAttributes
{
    /// <summary>
    /// Adds <c>exception.type</c> and <c>exception.message</c> when the first line of
    /// <paramref name="exception"/> has the .NET "TypeName: Message" shape, and
    /// <c>exception.stacktrace</c> always.
    /// </summary>
    public static void AddTo(RepeatedField<KeyValue> attributes, string exception)
    {
        var firstLine = FirstLine(exception);
        var colonIndex = firstLine?.IndexOf(": ", StringComparison.Ordinal) ?? -1;
        if (firstLine != null && colonIndex > 0)
        {
            attributes.Add(new KeyValue
            {
                Key = "exception.type",
                Value = new AnyValue { StringValue = firstLine[..colonIndex] }
            });
            attributes.Add(new KeyValue
            {
                Key = "exception.message",
                Value = new AnyValue { StringValue = firstLine[(colonIndex + 2)..] }
            });
        }

        attributes.Add(new KeyValue
        {
            Key = "exception.stacktrace",
            Value = new AnyValue { StringValue = exception }
        });
    }

    public static string? FirstLine(string? exception)
    {
        if (string.IsNullOrEmpty(exception))
            return null;

        var newline = exception.IndexOf('\n');
        return (newline >= 0 ? exception[..newline] : exception).TrimEnd('\r');
    }
}
