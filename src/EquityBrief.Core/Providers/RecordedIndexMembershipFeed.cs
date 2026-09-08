using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The index membership feed, answered from a recorded response rather than from
// the network.
//
// It ships rather than living in the suite because the fixture replay the corpus
// asks for at 2.1 runs the pipeline, not the tests, and a double the shipped code
// cannot be pointed at is one the pipeline cannot replay through. It reaches no
// network by construction: there is no HTTP client here to misconfigure, so a
// test using it cannot fall back to the live provider when a path is wrong.
//
// The payload shape is the provider's, so the parsing this exercises is the
// parsing the live feed will do (see: A frozen fixture per checkpoint, and the harness decides sign-off).
public sealed class RecordedIndexMembershipFeed(string capturedResponse) : IIndexMembershipFeed
{
    public int Calls { get; private set; }

    public static RecordedIndexMembershipFeed FromFile(string path) =>
        new(File.ReadAllText(path));

    public Task<IReadOnlyList<IndexConstituent>> ConstituentsAsync(
        string indexCode,
        CancellationToken cancellationToken = default)
    {
        // Counted so a caller can assert the whole index arrived in one request
        // rather than one per name, which is the limit the nightly path carries.
        Calls++;

        return Task.FromResult(Parse(capturedResponse, indexCode));
    }

    public static IReadOnlyList<IndexConstituent> Parse(string json, string indexCode)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("Components", out var components))
        {
            throw new FormatException(
                "The captured response carries no Components object. A feed payload that cannot be " +
                "parsed must fail rather than answer with an empty index, which reads as every " +
                "name having left at once.");
        }

        var constituents = components
            .EnumerateObject()
            .Select(entry => Read(entry.Value))
            .OrderBy(constituent => constituent.Ticker, StringComparer.Ordinal)
            .ToArray();

        return constituents.Length > 0
            ? constituents
            : throw new FormatException(
                $"The captured response holds no constituents for {indexCode}. An empty index is " +
                "not a result this system has any use for, and treating it as one would write a " +
                "leave date onto every name in the store.");
    }

    static IndexConstituent Read(JsonElement entry) => new(
        entry.GetProperty("Code").GetString()
            ?? throw new FormatException("A constituent carries no Code."),
        Date(entry, "StartDate") ?? throw new FormatException("A constituent carries no StartDate."),
        Date(entry, "EndDate"));

    // A missing or null end date means a current member, which is what the null
    // in the membership row's left column records.
    static DateOnly? Date(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && DateOnly.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}
