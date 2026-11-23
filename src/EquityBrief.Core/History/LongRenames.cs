using System.Text.RegularExpressions;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// Two codes of one company over the long history. A member span whose code holds no price on some of its sessions reads the
// prices another code of its filer, or its own code under the provider's delisted spelling, holds there, joined as one
// history, only where the two are never priced on the same day, or where on every day both are priced they carry the same
// close and the same volume, one company listed twice. Two codes priced on the same days at different volumes, as share
// classes are, never join. And each company is counted once on a session: a member code holding no price of its own that
// session, whose company another member code holds a price of its own for, is not counted there.
// see: Two codes of one filer join as one history only where they are never priced on the same day or carry the same close and volume on every shared day
public static partial class LongRenames
{
    public const string NeverSameDay = "never priced on the same day";

    public const string ListedTwice = "the same close and volume on every day both are priced";

    // The code a delisted spelling stands for: the provider's "_old" and "_old2" endings and a digit added to a code, which
    // the long pull gave a code a delisted company held.
    public static string BaseOf(string ticker) => Delisted().Replace(ticker, string.Empty);

    // Whether two codes join, and by which test; none where they are priced together on a day their close or volume differ.
    // Only sessions a code traded on are read.
    public static string? Test(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> other)
    {
        var mine = own.Where(bar => bar.Volume > 0).ToDictionary(bar => bar.Session);
        var shared = other.Where(bar => bar.Volume > 0 && mine.ContainsKey(bar.Session)).ToArray();

        if (shared.Length == 0)
        {
            return NeverSameDay;
        }

        return shared.All(bar => mine[bar.Session].Close == bar.Close && mine[bar.Session].Volume == bar.Volume) ? ListedTwice : null;
    }

    // A code's bars with another code's on every session it holds none of, and the sessions taken from the other.
    public static (SweepBar[] Bars, IReadOnlySet<DateOnly> Joined) Joined(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> other)
    {
        var held = own.Select(bar => bar.Session).ToHashSet();
        var taken = other.Where(bar => !held.Contains(bar.Session)).ToArray();

        return ([.. own.Concat(taken).OrderBy(bar => bar.Session)], taken.Select(bar => bar.Session).ToHashSet());
    }

    // A code's spans with the sessions it is not counted on taken out, each span parted around a run of them, the calendar
    // naming where each part resumes.
    public static (DateOnly? Joined, DateOnly? Left)[] Without(
        IReadOnlyList<(DateOnly? Joined, DateOnly? Left)> spans,
        IReadOnlySet<DateOnly> notCounted,
        IReadOnlyList<DateOnly> calendar)
    {
        if (notCounted.Count == 0)
        {
            return [.. spans];
        }

        var parts = new List<(DateOnly? Joined, DateOnly? Left)>();

        foreach (var span in spans)
        {
            var from = span.Joined;
            var open = true;

            foreach (var session in calendar.Where(session => (span.Joined is not { } joined || session >= joined) && (span.Left is not { } left || session < left)))
            {
                if (notCounted.Contains(session))
                {
                    if (open && (from is not { } start || start < session))
                    {
                        parts.Add((from, session));
                    }

                    open = false;
                }
                else if (!open)
                {
                    (from, open) = (session, true);
                }
            }

            if (open)
            {
                parts.Add((from, span.Left));
            }
        }

        return [.. parts];
    }

    [GeneratedRegex(@"(_old\d*|\d)$")]
    private static partial Regex Delisted();
}

// One part of a code's history the long reader reads on an index: its bars, with another code's joined where the two pass
// the rename test, its member spans, the sessions its bars were taken from the other code on, and the codes joined with the
// test each passed.
public sealed record LongPart(
    string Ticker,
    SweepBar[] Bars,
    (DateOnly? Joined, DateOnly? Left)[] Spans,
    IReadOnlySet<DateOnly> Joined,
    IReadOnlyList<(string Ticker, string Test)> JoinedWith);

// A pair of one company's codes the reader read on an index: joined by the test named, or left apart, none named, for being
// priced together; and the member sessions of the first its prices were taken from the second on, or would have been.
public sealed record LongJoin(string Ticker, string Other, string? Test, int Sessions);

// One odd history-ends mark and what the reader does with it: its code and session, the indices its code's spans are on,
// what its filer and its prices say, and the outcome, and whether the operator ruled it.
public sealed record SettledMark(string Ticker, DateOnly Mark, IReadOnlyList<string> Indices, string Filer, string Price, string Outcome)
{
    public bool Ruled { get; init; }
}

// The codes of one company as a code sees them: the codes the store files under a filer it files the code under, and the
// code's own delisted spellings. The link is never followed further, since a code a later company took links the two
// companies through the code alone: Physicians Realty's delisted DOC and Healthpeak's HCP share no filer and no spelling.
public sealed class LongCompanies
{
    readonly Dictionary<string, string[]> filersOf;
    readonly Dictionary<string, string[]> codesOfFiler;
    readonly Dictionary<string, string[]> spellings;

    LongCompanies(Dictionary<string, string[]> filersOf, Dictionary<string, string[]> codesOfFiler, Dictionary<string, string[]> spellings) =>
        (this.filersOf, this.codesOfFiler, this.spellings) = (filersOf, codesOfFiler, spellings);

    // From each filer's codes and every code the prices hold.
    public static LongCompanies Of(IReadOnlyDictionary<string, IEnumerable<string>> filers, IEnumerable<string> codes)
    {
        var codesOfFiler = filers.ToDictionary(pair => pair.Key, pair => pair.Value.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var filersOf = codesOfFiler
            .SelectMany(pair => pair.Value.Select(code => (Code: code, Filer: pair.Key)))
            .GroupBy(pair => pair.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Filer).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var spellings = codes.Distinct(StringComparer.Ordinal)
            .GroupBy(LongRenames.BaseOf, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        return new LongCompanies(filersOf, codesOfFiler, spellings);
    }

    // The other codes of a code's company, in order: its filers' codes and its own spellings.
    public IReadOnlyList<string> Linked(string code) =>
    [
        .. (filersOf.GetValueOrDefault(code) ?? []).SelectMany(filer => codesOfFiler[filer])
            .Concat(spellings.GetValueOrDefault(LongRenames.BaseOf(code)) ?? [])
            .Where(other => other != code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    // Whether two codes are of one company as the first sees it.
    public bool Same(string code, string other) => code == other || Linked(code).Contains(other, StringComparer.Ordinal);
}
