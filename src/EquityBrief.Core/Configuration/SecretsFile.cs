using System.Text.Json;

namespace EquityBrief.Core.Configuration;

// The secrets file is read by the worker's configuration and by the read-only copy's check, and by nothing else, never
// into a shell or a log. A file that will not parse stops either read with its name, its line and its byte and none of
// what it holds, since a parser's own message quotes the text it stopped at.
// see: The secrets file is read by the worker alone, and no script or command or error prints any of it
public static class SecretsFile
{
    public static InvalidOperationException Unreadable(string file, Exception? failure) =>
        JsonFailureIn(failure) is { } json
            ? new(FormattableString.Invariant($"The secrets file {Path.GetFileName(file)} is not valid JSON at line {(json.LineNumber ?? 0) + 1}, byte {(json.BytePositionInLine ?? 0) + 1}; none of it is printed."))
            : new(FormattableString.Invariant($"The secrets file {Path.GetFileName(file)} could not be read; none of it is printed."));

    // The parser's error a load failure carries, however the reader wrapped it.
    static JsonException? JsonFailureIn(Exception? failure)
    {
        for (var at = failure; at is not null; at = at.InnerException)
        {
            if (at is JsonException json)
            {
                return json;
            }
        }

        return null;
    }
}
