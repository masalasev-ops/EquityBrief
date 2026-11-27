using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EquityBrief.Core.Loop;

// From 24.36 one past case the reviewer matches a pick among: a stock's readings on a session, the session its trade under
// the plan closed on, what it made per $1 risked and whether it reached its take-profit before its stop.
public sealed record ReviewCase(string Ticker, DateOnly Session, DateOnly ClosedOn, IReadOnlyList<double?> Readings, double? Result, bool? Won);

// What history expected of a pick: the cases matched, those that reached the take-profit before the stop, and their mean
// result per $1 risked.
public sealed record Expectation(int Cases, int Reached, double Mean)
{
    public double Share => Cases == 0 ? 0 : 1.0 * Reached / Cases;
}

// From 24.36 the reviewer, by code with no model and no request: for a pick, the past cases most like it under the same plan
// that had closed before its night, matched as the cards match the setups like a pick, at most 250 and drawing no figure under
// 30; and the same for a seeded draw of the night's members, so a rule reads as better or worse than random by history. Its
// line names no stock and no date.
// see: The reviewer matches each pick among the past cases like it that closed before its night, and a seeded draw of the night's members the same way
public static class Reviewer
{
    // The most cases matched and the fewest a figure is drawn from, as the cards match the setups like a pick.
    public static int Nearest => SimilarSetups.Nearest;

    public static int Fewest => SimilarSetups.Fewest;

    // The members drawn at random on a night for each plan.
    public const int RandomMembers = 100;

    public const string TooFew = "Too few past cases like this one to count.";

    // What history expected of a pick on a night: the cases closed before the night holding every reading matched on, the
    // pick's own stock left out, matched on those readings; none where the pick holds none of them or fewer than the fewest
    // are matched.
    public static Expectation? Expect(IReadOnlyList<ReviewCase> cases, DateOnly night, string ticker, IReadOnlyList<double?> readings, IReadOnlyList<int> matchedOn, IReadOnlyList<string> columns, ChanceForm form)
    {
        if (matchedOn.Count == 0 || matchedOn.Any(place => readings[place] is null))
        {
            return null;
        }

        ReviewCase[] closed = [.. cases.Where(one => one.ClosedOn < night && matchedOn.All(place => one.Readings[place] is not null))];
        var places = closed.Select(one => one.Session).Distinct().Order().Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        SimilarSetup[] setups =
        [
            .. closed.Select(one => new SimilarSetup(one.Ticker, places[one.Session], [.. matchedOn.Select(place => one.Readings[place]!.Value)], 0, one.Result, one.Won)),
        ];

        return SimilarSetups.Match(setups, ticker, [.. matchedOn.Select(place => readings[place]!.Value)], columns, form)?.Chance is { } chance
            ? new Expectation(chance.Count, chance.Won, chance.Mean)
            : null;
    }

    // The night's members drawn for a plan: each ordered by the hash of the index, the night, the plan and its code, the first
    // so many kept, so the same draw is made again from the same night.
    public static IReadOnlyList<string> Draw(IEnumerable<string> members, string index, DateOnly night, string plan, int count = RandomMembers) =>
    [
        .. members
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ticker => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{index}|{night:yyyy-MM-dd}|{plan}|{ticker}")))), StringComparer.Ordinal)
            .ThenBy(ticker => ticker, StringComparer.Ordinal)
            .Take(count),
    ];

    // The random draw's figure: the mean of the expected results of the drawn members holding one, none where none does.
    public static double? RandomMean(IEnumerable<Expectation?> drawn)
    {
        double[] means = [.. drawn.OfType<Expectation>().Select(one => one.Mean)];

        return means.Length == 0 ? null : means.Average();
    }

    // The line a pick carries: what the cases like it did, or that too few closed before its night to count.
    public static string Line(Expectation? expectation) =>
        expectation is not { } held
            ? TooFew
            : string.Create(CultureInfo.InvariantCulture, $"Past cases most like this one: of the {held.Cases} most like it under this plan, {held.Reached} ({held.Share * 100:0}%) reached the take-profit before the stop; on average {(held.Mean < 0 ? "-$" : "$")}{Math.Abs(held.Mean):0.00} made or lost per $1 risked.");
}
