using EquityBrief.Core.Providers;

namespace EquityBrief.Core.History;

// One span of one code's membership in the long history: the first snapshot holding it, the first later snapshot that does
// not or the provider's leave, none where it is a member still, and what it was read from.
public sealed record LongSpan(string Index, string Ticker, DateOnly Joined, DateOnly? Left, string ReadFrom);

// A span of the S&P 500 as the provider dates it, its start none where the provider carries none, and the company's name as
// the span states it, none where it states none.
public sealed record ProviderSpan(string Ticker, DateOnly? Joined, DateOnly? Left, string? Name = null);

// Where a code's provider spans under two names meet and the evidence parts them into two companies: the code, the earlier
// side's start and its end at the later side's start, the two names, the evidence, and whether the earlier side was left out,
// its code holding no price over it.
public sealed record LongParted(string Ticker, DateOnly? From, DateOnly To, string? Before, string? After, string Evidence, bool LeftOut);

// The long history's spans of membership. A member runs from a snapshot holding it to the first later snapshot that does not,
// so a stock falling within a quarter stays a member until its prices stop; a code away at one snapshot alone is read as held
// there, one away at two snapshots or more reads as two spans; a code two funds hold at one quarter end reads in the larger
// index. Before 2012 the S&P 500 reads IVV's snapshots, and from 2012 the provider's dates, or from the day after IVV's last
// schedule where the prices' count of the two sets them more than its margin apart, a span with no start date taking
// the first snapshot holding its company, read by its code and where no snapshot holds the code by its name, and where neither
// does, from the end of its code's latest dated span ending by its own end; a code's own provider spans that overlap or meet
// are one span, but where their names differ and a different filer on each side or the marks' price test parts them, two, the
// earlier left out where its code holds no price over it; two spans the provider files under two codes with one name as written
// and the same dates are one company's and read as the first code, two classes of shares never. From the first night that
// read the S&P 400's or 600's members, that index's spans are the nights' own.
// see: The long history's spans of the S&P 400 and 600 are the nights' own from the first night that read each index
// see: The S&P 500 before 2012 is read from IVV's filed holdings and from 2012 from the provider's dates
// see: A provider's span with no start date takes the first of IVV's snapshots holding its company by code or by name
// see: The S&P 500's provider dates from 2012 are held against IVV's holdings and read from IVV's last schedule where they stand more than 5 apart
public static class LongSpans
{
    public const string FromFund = "fund";

    public const string FromProvider = "provider";

    public const string FromNight = "night";

    public static DateOnly ProviderFrom { get; } = new(2012, 1, 1);

    // The provider's S&P 500 spans the operator dropped by hand, each a code and the session from which its spans are not read:
    // from the operator's answers of 2026-11-23, item 2, PARA from 2025-08-07, the day Paramount Skydance's PSKY took Paramount
    // Global's place, where the provider names PARA Banzai International, whose filer the long filings hold PARA under and not
    // Paramount Global's, and the long pull's PARA carries Banzai's prices; PSKY holds Paramount's sessions from that day.
    // From the operator's decisions of 2026-11-25, item B, BBT from 2018-10-01, its provider span to Truist's rename of 2019-12-09,
    // BB&T read as TFC throughout, whose spans hold it from 2004 with no gap; BBT's code later another company's.
    // see: PARA's provider span from 2025-08-07 is dropped by hand, its code then Banzai International's and PSKY holding Paramount
    // see: BB&T is read as TFC throughout, BBT's S&P 500 span to Truist's rename dropped by hand and IR's span started by hand at its entry
    public static IReadOnlyList<(string Ticker, DateOnly From)> DroppedByHand { get; } =
    [
        ("PARA", new DateOnly(2025, 8, 7)),
        ("BBT", new DateOnly(2018, 10, 1)),
    ];

    // The provider's S&P 500 spans the operator started by hand, each a code and the session its span starts at where the
    // provider dates it earlier: from the operator's decisions of 2026-11-25, item C, IR at Ingersoll Rand Inc.'s entry as the
    // membership rows date it, 2020-03-03, the code before it Ingersoll-Rand plc's, which TT's spans hold.
    // see: BB&T is read as TFC throughout, BBT's S&P 500 span to Truist's rename dropped by hand and IR's span started by hand at its entry
    public static IReadOnlyList<(string Ticker, DateOnly From)> StartedByHand { get; } = [("IR", new DateOnly(2020, 3, 3))];

    // The larger index first: a code two funds hold at one quarter end reads in the first of these holding it.
    public static IReadOnlyList<string> Larger { get; } = ["GSPC", "MID", "SML"];

    // Every span: each index's snapshots by quarter end with the codes each holds, the provider's spans of the S&P 500, and the
    // first of IVV's snapshots holding each company's name by its name key.
    public static IReadOnlyList<LongSpan> Of(
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>> snapshots,
        IReadOnlyList<ProviderSpan> provider,
        IReadOnlyDictionary<string, DateOnly>? firstHeldByName = null,
        DateOnly? providerFrom = null,
        IReadOnlyDictionary<string, IReadOnlyList<ProviderSpan>>? nights = null,
        Func<ProviderSpan, ProviderSpan, string?>? partsThem = null,
        ICollection<LongParted>? parted = null)
    {
        var spans = new List<LongSpan>();
        var cutover = providerFrom ?? ProviderFrom;

        foreach (var index in Larger.Where(snapshots.ContainsKey))
        {
            DateOnly[] periods = [.. snapshots[index].Keys.Where(period => index != "GSPC" || period < cutover).Order()];

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
                    spans.Add(new LongSpan(index, code, periods[open], index == "GSPC" ? cutover : null, FromFund));
                }
            }
        }

        {
            var ivv = snapshots.GetValueOrDefault("GSPC") ?? new Dictionary<DateOnly, IReadOnlySet<string>>();
            DateOnly[] periods = [.. ivv.Keys.Order()];

            DateOnly? Dated(ProviderSpan span) =>
                span.Joined
                ?? periods.Where(period => ivv[period].Contains(span.Ticker)).Select(period => (DateOnly?)period).FirstOrDefault()
                ?? (span.Name is { } name && firstHeldByName?.TryGetValue(HoldingMatcher.NameKey(name), out var held) == true ? held : null);

            // A span no snapshot and no name dates continues its code's latest dated span ending by its own end, so a code the
            // provider closed and listed again with no start reads as one span.
            // see: A provider span no snapshot and no name dates continues its code's latest dated span ending by its own end
            ProviderSpan Continued(ProviderSpan span) =>
                Dated(span) is not null ? span : span with
                {
                    Joined = provider
                        .Where(other => other.Ticker == span.Ticker && other.Joined is not null && other.Left is { } ended && !(span.Left < ended))
                        .Select(other => other.Left)
                        .Max(),
                };

            var once = provider
                .Select(Continued)
                .GroupBy(span => span.Ticker, StringComparer.Ordinal)
                .SelectMany(rows => Runs(rows, partsThem, parted))
                .Where(span => !(span.Left <= cutover))
                .GroupBy(span => (Name: span.Name is { } name ? Spelled(name) : span.Ticker, span.Joined, span.Left))
                .Select(group => group.OrderBy(span => span.Ticker, StringComparer.Ordinal).First());

            foreach (var span in once)
            {
                var first = Dated(span);

                if (first is { } dated && !DroppedByHand.Any(drop => drop.Ticker == span.Ticker && dated >= drop.From))
                {
                    var joined = StartedByHand.FirstOrDefault(start => start.Ticker == span.Ticker) is { Ticker: not null } start && start.From > dated ? start.From : dated;

                    if (!(span.Left <= joined))
                    {
                        spans.Add(new LongSpan("GSPC", span.Ticker, joined < cutover ? cutover : joined, span.Left, FromProvider));
                    }
                }
            }
        }

        return [.. Nightly(spans, nights).DistinctBy(span => (span.Index, span.Ticker, span.Joined)).OrderBy(span => span.Index, StringComparer.Ordinal).ThenBy(span => span.Ticker, StringComparer.Ordinal).ThenBy(span => span.Joined)];
    }

    // Whether a code is a member of an index on a day by the spans.
    public static bool IsMember(IEnumerable<LongSpan> spans, string index, string ticker, DateOnly day) =>
        spans.Any(span => span.Index == index && span.Ticker == ticker && span.Joined <= day && !(span.Left <= day));

    // How many sessions a current member's long history may end before its newest bar.
    public const int BehindMargin = 2;

    // The current members of each index whose long history's spans end more than the margin of sessions before their newest
    // bar: each with its index, its spans' last day, none where it holds no span there, and the sessions it is behind by.
    public static IReadOnlyList<(string Index, string Ticker, DateOnly? Ends, int Behind)> Behind(
        IEnumerable<LongSpan> spans,
        IEnumerable<(string Index, string Ticker, DateOnly NewestBar)> members,
        IReadOnlyList<DateOnly> calendar)
    {
        var byCode = spans.ToLookup(span => (span.Index, span.Ticker));
        var behind = new List<(string Index, string Ticker, DateOnly? Ends, int Behind)>();

        foreach (var (index, ticker, newest) in members)
        {
            var own = byCode[(index, ticker)].ToArray();

            if (own.Any(span => !span.Left.HasValue))
            {
                continue;
            }

            DateOnly? ends = own.Length == 0 ? null : own.Max(span => span.Left);
            var sessions = calendar.Count(session => session <= newest && (ends is not { } end || session >= end));

            if (sessions > BehindMargin)
            {
                behind.Add((index, ticker, ends, sessions));
            }
        }

        return [.. behind.OrderBy(one => one.Index, StringComparer.Ordinal).ThenBy(one => one.Ticker, StringComparer.Ordinal)];
    }

    // The evidence that parts a code's provider spans into two companies, as the worker reads it off the code's prices and the
    // filers of its names: its prices begin at the later span, so the earlier holds none of them; the marks' price test at the
    // later span's start; or a different filer on each side.
    public const string NoPriceBefore = "its prices begin at the later name";

    public const string DifferentFiler = "a different filer on each side";

    // One code's provider spans with those that overlap or meet read as one, a span with no start read as begun before any
    // other and one with no end as running still; from the operator's answers of 2026-11-23 at 16:50 two that meet under two
    // names read as two companies where the evidence parts them, never on the names alone, the earlier left out where its code
    // holds no price over it, so its member sessions are read under the code that does.
    // see: A code's provider spans under two names part into two companies only on a different filer or the marks' price test, never on the names alone
    static IEnumerable<ProviderSpan> Runs(IEnumerable<ProviderSpan> rows, Func<ProviderSpan, ProviderSpan, string?>? partsThem = null, ICollection<LongParted>? parted = null)
    {
        ProviderSpan? run = null;

        foreach (var row in rows.OrderBy(span => span.Joined ?? DateOnly.MinValue).ThenBy(span => span.Left ?? DateOnly.MaxValue))
        {
            if (run is { } open && (row.Joined ?? DateOnly.MinValue) <= (open.Left ?? DateOnly.MaxValue))
            {
                if (partsThem is not null && row.Joined is { } boundary && open.Name is { } before && row.Name is { } after
                    && Spelled(before) != Spelled(after) && partsThem(open, row) is { } evidence)
                {
                    var leftOut = evidence == NoPriceBefore;

                    parted?.Add(new LongParted(open.Ticker, open.Joined, boundary, before, after, evidence, leftOut));

                    if (!leftOut)
                    {
                        yield return open with { Left = boundary };
                    }

                    run = row;

                    continue;
                }

                run = open with { Left = open.Left is not { } was || row.Left is not { } now ? null : was > now ? was : now };

                continue;
            }

            if (run is not null)
            {
                yield return run;
            }

            run = row;
        }

        if (run is not null)
        {
            yield return run;
        }
    }

    // A company's name as the provider wrote it, its letters and digits alone, so two classes of one company's shares, whose
    // names differ by their class, are never read as one code.
    static string Spelled(string name) => new([.. name.ToUpperInvariant().Where(char.IsLetterOrDigit)]);

    // The S&P 400's and 600's spans from the first night that read each index's members: a fund's span still open on that
    // night ends there, or runs on as the night's own span where the nights hold the code from that night, and the nights'
    // other spans are read as they stand.
    static IEnumerable<LongSpan> Nightly(IEnumerable<LongSpan> spans, IReadOnlyDictionary<string, IReadOnlyList<ProviderSpan>>? nights)
    {
        if (nights is null || nights.Count == 0)
        {
            return spans;
        }

        var kept = new List<LongSpan>();
        var all = spans.ToArray();

        foreach (var index in all.Select(span => span.Index).Concat(nights.Keys).Distinct(StringComparer.Ordinal))
        {
            var ofIndex = all.Where(span => span.Index == index).ToArray();
            DateOnly[] starts = nights.TryGetValue(index, out var rows) ? [.. rows.Where(row => row.Joined is not null).Select(row => row.Joined!.Value)] : [];

            if (index == "GSPC" || starts.Length == 0)
            {
                kept.AddRange(ofIndex);

                continue;
            }

            var first = starts.Min();
            var night = rows!
                .GroupBy(row => row.Ticker, StringComparer.Ordinal)
                .SelectMany(group => Runs(group))
                .Select(row => (row.Ticker, Joined: row.Joined is { } joined && joined > first ? joined : first, row.Left))
                .Where(row => !(row.Left <= row.Joined))
                .ToList();

            foreach (var span in ofIndex.Where(span => span.Joined < first))
            {
                if (span.Left is { } left && left <= first)
                {
                    kept.Add(span);

                    continue;
                }

                var on = night.FindIndex(row => row.Ticker == span.Ticker && row.Joined == first);

                if (on >= 0)
                {
                    kept.Add(span with { Left = night[on].Left });
                    night.RemoveAt(on);
                }
                else
                {
                    kept.Add(span with { Left = first });
                }
            }

            kept.AddRange(night.Select(row => new LongSpan(index, row.Ticker, row.Joined, row.Left, FromNight)));
        }

        return kept;
    }
}
