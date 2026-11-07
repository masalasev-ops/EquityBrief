using EquityBrief.Core.Providers;

namespace EquityBrief.Core.History;

// One span of one code's membership in the long history: the first snapshot holding it, the first later snapshot that does
// not or the provider's leave, none where it is a member still, and what it was read from.
public sealed record LongSpan(string Index, string Ticker, DateOnly Joined, DateOnly? Left, string ReadFrom);

// A span of the S&P 500 as the provider dates it, its start none where the provider carries none, and the company's name as
// the span states it, none where it states none.
public sealed record ProviderSpan(string Ticker, DateOnly? Joined, DateOnly? Left, string? Name = null);

// The long history's spans of membership. A member runs from a snapshot holding it to the first later snapshot that does not,
// so a stock falling within a quarter stays a member until its prices stop; a code away at one snapshot alone is read as held
// there, one away at two snapshots or more reads as two spans; a code two funds hold at one quarter end reads in the larger
// index. Before 2012 the S&P 500 reads IVV's snapshots, and from 2012 the provider's dates, a span with no start date taking
// the first snapshot holding its company, read by its code and where no snapshot holds the code by its name; two spans the
// provider files under two codes with one name and the same dates are one company's and read as the first code.
// see: The S&P 500 before 2012 is read from IVV's filed holdings and from 2012 from the provider's dates
public static class LongSpans
{
    public const string FromFund = "fund";

    public const string FromProvider = "provider";

    public static DateOnly ProviderFrom { get; } = new(2012, 1, 1);

    // The larger index first: a code two funds hold at one quarter end reads in the first of these holding it.
    public static IReadOnlyList<string> Larger { get; } = ["GSPC", "MID", "SML"];

    // Every span: each index's snapshots by quarter end with the codes each holds, the provider's spans of the S&P 500, and the
    // first of IVV's snapshots holding each company's name by its name key.
    public static IReadOnlyList<LongSpan> Of(
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>> snapshots,
        IReadOnlyList<ProviderSpan> provider,
        IReadOnlyDictionary<string, DateOnly>? firstHeldByName = null)
    {
        var spans = new List<LongSpan>();

        foreach (var index in Larger.Where(snapshots.ContainsKey))
        {
            DateOnly[] periods = [.. snapshots[index].Keys.Where(period => index != "GSPC" || period < ProviderFrom).Order()];

            // The codes this index's fund holds at each quarter end, less those a larger index's fund holds there too.
            HashSet<string> Held(DateOnly period) =>
            [
                .. snapshots[index][period].Where(code => Larger.TakeWhile(larger => larger != index)
                    .All(larger => !snapshots.TryGetValue(larger, out var other) || !other.TryGetValue(period, out var there) || !there.Contains(code))),
            ];

            var held = periods.Select(Held).ToArray();

            foreach (var code in held.SelectMany(codes => codes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                int? start = null;

                for (var at = 0; at < periods.Length; at++)
                {
                    var here = held[at].Contains(code);
                    var bridged = !here && start is not null && at + 1 < periods.Length && held[at + 1].Contains(code);

                    if ((here || bridged) && start is null)
                    {
                        start = at;
                    }
                    else if (!here && !bridged && start is { } from)
                    {
                        spans.Add(new LongSpan(index, code, periods[from], periods[at], FromFund));
                        start = null;
                    }
                }

                if (start is { } open)
                {
                    spans.Add(new LongSpan(index, code, periods[open], index == "GSPC" ? ProviderFrom : null, FromFund));
                }
            }
        }

        if (snapshots.TryGetValue("GSPC", out var ivv))
        {
            DateOnly[] periods = [.. ivv.Keys.Order()];

            var once = provider
                .Where(span => span.Left is null || span.Left > ProviderFrom)
                .GroupBy(span => (Name: span.Name is { } name ? HoldingMatcher.NameKey(name) : span.Ticker, span.Joined, span.Left))
                .Select(group => group.OrderBy(span => span.Ticker, StringComparer.Ordinal).First());

            foreach (var span in once)
            {
                var first = span.Joined
                    ?? periods.Where(period => ivv[period].Contains(span.Ticker)).Select(period => (DateOnly?)period).FirstOrDefault()
                    ?? (span.Name is { } name && firstHeldByName?.TryGetValue(HoldingMatcher.NameKey(name), out var held) == true ? held : null);

                if (first is { } joined)
                {
                    spans.Add(new LongSpan("GSPC", span.Ticker, joined < ProviderFrom ? ProviderFrom : joined, span.Left, FromProvider));
                }
            }
        }

        return [.. spans.DistinctBy(span => (span.Index, span.Ticker, span.Joined)).OrderBy(span => span.Index, StringComparer.Ordinal).ThenBy(span => span.Ticker, StringComparer.Ordinal).ThenBy(span => span.Joined)];
    }

    // Whether a code is a member of an index on a day by the spans.
    public static bool IsMember(IEnumerable<LongSpan> spans, string index, string ticker, DateOnly day) =>
        spans.Any(span => span.Index == index && span.Ticker == ticker && span.Joined <= day && (span.Left is null || day < span.Left));
}
