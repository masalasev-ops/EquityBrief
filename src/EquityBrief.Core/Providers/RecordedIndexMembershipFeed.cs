using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The index membership feed, answered from a recorded response rather than from
// the network.
//
// It ships rather than living in the suite because the fixture replay the corpus
// asks for at 3.1 runs the pipeline, not the tests, and a double the shipped code
// cannot be pointed at is one the pipeline cannot replay through. It reaches no
// network by construction: there is no HTTP client here to misconfigure, so a
// test using it cannot fall back to the live provider when a path is wrong.
//
// The payload shape is the provider's, so the parsing this exercises is the
// parsing the live feed will do (see: A frozen fixture per checkpoint, and the harness decides sign-off).
public sealed class RecordedIndexMembershipFeed(string capturedResponse) : IIndexMembershipFeed
{
    public int Requests { get; private set; }

    public static RecordedIndexMembershipFeed FromFile(string path) =>
        new(File.ReadAllText(path));

    public Task<IReadOnlyList<IndexConstituent>> ConstituentsAsync(
        string indexCode,
        CancellationToken cancellationToken = default)
    {
        // Counted so a caller can read the figure off the feed rather than
        // stating it, which is the limit the nightly path carries: the whole
        // index in one request and not one per name.
        Requests++;

        return Task.FromResult(Parse(capturedResponse, indexCode));
    }

    // The object carrying the spans.
    //
    // Not Components, which the provider also sends and which this read until
    // 1.2. Components is tonight's snapshot: Code, Exchange, Name, Sector,
    // Industry and Weight, with no StartDate and no EndDate on any entry and no
    // row at all for a name that has left. The spans are in
    // HistoricalTickerComponents, and the spans are the whole reason membership
    // is fetched rather than maintained
    // (see: The universe is the S&P 500, and membership is fetched, not maintained).
    //
    // The fixture was written by hand at 1.1 with StartDate and EndDate inside
    // Components, so this parsed it and nothing said otherwise. Against the real
    // payload it threw on the first constituent. That is the shape of fault a
    // fixture the provider did not write cannot catch, which is why the fixture
    // is captured now.
    const string Spans = "HistoricalTickerComponents";

    public static IReadOnlyList<IndexConstituent> Parse(string json, string indexCode)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(Spans, out var components))
        {
            throw new FormatException(
                $"The captured response carries no {Spans} object. A feed payload that cannot be " +
                "parsed must fail rather than answer with an empty index, which reads as every " +
                "name having left at once. A payload carrying only Components is tonight's " +
                "snapshot: it holds no dates and no departed name, so reading it would record " +
                "every name that has left as one that never existed.");
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

    static IndexConstituent Read(JsonElement entry)
    {
        var ticker = entry.TryGetProperty("Code", out var code) ? code.GetString() : null;

        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new FormatException("A constituent carries no Code.");
        }

        return new IndexConstituent(
            ticker,
            Date(entry, "StartDate", ticker)
                ?? throw new FormatException($"{ticker} carries no StartDate."),
            Date(entry, "EndDate", ticker));
    }

    // Three outcomes, kept apart on purpose.
    //
    // Absent or JSON null is null, and for EndDate that means a current member.
    // Present and unreadable throws. Folding those two together is a falsy value
    // standing in for an absent one, and here it has teeth: a name that left the
    // index, carrying an end date the parser could not read, would be stored
    // with no leave date and read as present. That is the one thing membership
    // exists to prevent, and nothing downstream would have noticed.
    //
    // The parse is exact and invariant. The provider's format is known, so this
    // is the parse that was meant rather than a tightening, and a culture-
    // sensitive parse resolves against the machine's locale, which is the same
    // class of fault as an instant resolving against the machine's zone.
    static DateOnly? Date(JsonElement entry, string name, string ticker)
    {
        if (!entry.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.String)
        {
            throw new FormatException(
                $"{ticker} carries a {name} that is {value.ValueKind} rather than a string. A value " +
                "that is present and unreadable is not an absent one.");
        }

        var text = value.GetString();

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new FormatException(
                $"{ticker} carries a {name} of '{text}', which is not a date in yyyy-MM-dd. Reading " +
                "it as absent would record a name that left the index as a current member.");
    }
}
