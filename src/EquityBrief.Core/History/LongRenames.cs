using System.Text.RegularExpressions;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// Two codes of one company over the long history. A member span whose code holds no price on some of its sessions reads the
// prices another code of its filer, or its own code under the provider's delisted spelling, holds there, joined as one
// history, only where the two are never priced on the same day, or where on the days both are priced the prices they traded
// at and their volumes stand in one ratio, one company listed twice, the other's prices then put on the code's own scale.
// Two codes whose prices or volumes drift against each other, as share classes do, never join. And each company is counted once on a
// session: a member code holding no price of its own that session, whose company another member code holds a price of its
// own for, is not counted there.
// see: Two codes of one filer join as one history where they are never priced on the same day or trade in one ratio on the days they share
public static partial class LongRenames
{
    public const string NeverSameDay = "never priced on the same day";

    public const string ListedTwice = "the same close and volume on every day both are priced";

    public const string ConstantRatio = "closes and volumes in one ratio on the days both are priced";

    // How near a day's ratio of the prices the two traded at stands to the pair's median ratio, and on what share of the
    // shared days at least. The prices traded at are read and not the stored closes, whose dividends a code's history
    // carries only to its own last day.
    public const decimal CloseTolerance = 0.001m;

    public const decimal CloseShare = 0.99m;

    // How near a day's volume ratio stands to the pair's median ratio, and on what share of the shared days at least, two
    // feeds counting one company's shares a little apart.
    public const decimal VolumeTolerance = 0.25m;

    public const decimal VolumeShare = 0.80m;

    // The shared days the ratio test needs at least, since a few days fit one ratio whatever the two codes are.
    public const int RatioDays = 20;

    // The code a delisted spelling stands for: the provider's "_old" and "_old2" endings and a digit added to a code, which
    // the long pull gave a code a delisted company held.
    public static string BaseOf(string ticker) => Delisted().Replace(ticker, string.Empty);

    // Whether two codes join, and by which test; none where they are priced together and their closes or volumes drift
    // against each other, as share classes do. Only sessions a code traded on are read.
    public static string? Test(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> other)
    {
        var shared = Shared(own, other);

        if (shared.Length == 0)
        {
            return NeverSameDay;
        }

        if (shared.All(pair => pair.Own.Close == pair.Other.Close && pair.Own.Volume == pair.Other.Volume))
        {
            return ListedTwice;
        }

        var traded = shared.Where(pair => pair.Own.RawClose > 0 && pair.Other.RawClose > 0).ToArray();

        if (traded.Length < RatioDays)
        {
            return null;
        }

        static bool Holds(decimal[] ratios, decimal tolerance, decimal share)
        {
            var median = Median(ratios);

            return ratios.Count(ratio => Math.Abs(ratio / median - 1m) <= tolerance) >= share * ratios.Length;
        }

        // The volumes are read on the shared days both carry one; where none does, the volume test has nothing on file and the
        // closes decide.
        decimal[] volumes = [.. Volumes(shared)];

        return Holds([.. traded.Select(pair => pair.Other.RawClose / pair.Own.RawClose)], CloseTolerance, CloseShare)
            && (volumes.Length == 0 || Holds(volumes, VolumeTolerance, VolumeShare))
            ? ConstantRatio
            : null;
    }

    // The ratio of the two volumes on each shared day both carry one.
    static IEnumerable<decimal> Volumes(IEnumerable<(SweepBar Own, SweepBar Other)> shared) =>
        shared.Where(pair => pair.Own.Volume > 0 && pair.Other.Volume > 0).Select(pair => Shares(pair.Other.Volume) / pair.Own.Volume);

    // A code's bars with another code's on every session it holds none of, the other's put on the code's own scale: its
    // stored prices by their ratio over the last shared days, its traded prices and its volume by their median ratios.
    public static (SweepBar[] Bars, IReadOnlySet<DateOnly> Joined) Joined(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> other)
    {
        var held = own.Select(bar => bar.Session).ToHashSet();
        var shared = Shared(own, other);
        var traded = shared.Where(pair => pair.Own.RawClose > 0 && pair.Other.RawClose > 0).ToArray();
        var (price, raw, volume) = shared.Length == 0
            ? (1m, 1m, 1m)
            : (Median(shared[^Math.Min(RatioDays, shared.Length)..].Select(pair => pair.Other.Close / pair.Own.Close)),
                traded.Length == 0 ? 1m : Median(traded.Select(pair => pair.Other.RawClose / pair.Own.RawClose)),
                Volumes(shared).ToArray() is { Length: > 0 } volumes ? Median(volumes) : 1m);
        var taken = other.Where(bar => !held.Contains(bar.Session))
            .Select(bar => price == 1m && raw == 1m && volume == 1m
                ? bar
                : bar with { High = bar.High / price, Low = bar.Low / price, Close = bar.Close / price, Open = bar.Open / price, RawClose = bar.RawClose / raw, Volume = (long)Math.Round(bar.Volume / volume) })
            .ToArray();

        return ([.. own.Concat(taken).OrderBy(bar => bar.Session)], taken.Select(bar => bar.Session).ToHashSet());
    }

    // The links the operator made by hand: a code, the code whose prices it reads from the session given on, and that session,
    // here SITE Centers' 8-K carrying item 5.03 of 2018-10-12, the charter amendment of its rename from DDR Corp.
    // see: DDR reads SITC's prices from SITE Centers' 2018 rename by hand where SITC's close there continues DDR's
    public static IReadOnlyList<(string Ticker, string Later, DateOnly From)> ByHand { get; } = [("DDR", "SITC", new DateOnly(2018, 10, 12))];

    // A code's bars before a hand link's session with the later code's from it on, the later's put on the code's own scale at
    // the two codes' ratio on the last day both traded before it, and the sessions taken; none where the later code's first
    // close there does not continue the code's last before it as the marks' price test reads a continuing price.
    public static (SweepBar[] Bars, IReadOnlySet<DateOnly> Joined, string Test)? ByHandJoined(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> other, DateOnly from, IReadOnlyList<DateOnly> calendar)
    {
        SweepBar[] before = [.. own.Where(bar => bar.Session < from)];
        var shared = Shared(before, other.Where(bar => bar.Session < from).ToArray());

        if (shared.Length == 0)
        {
            return null;
        }

        var (last, theirs) = shared[^1];
        var price = theirs.Close / last.Close;
        var raw = last.RawClose > 0 && theirs.RawClose > 0 ? theirs.RawClose / last.RawClose : 1m;
        var volume = last.Volume > 0 && theirs.Volume > 0 ? Shares(theirs.Volume) / last.Volume : 1m;
        SweepBar[] taken =
        [
            .. other.Where(bar => bar.Session >= from)
                .Select(bar => bar with { High = bar.High / price, Low = bar.Low / price, Close = bar.Close / price, Open = bar.Open / price, RawClose = bar.RawClose / raw, Volume = (long)Math.Round(bar.Volume / volume) }),
        ];
        SweepBar[] together = [.. before.Concat(taken).OrderBy(bar => bar.Session)];
        var traded = LongTraded.Of(together).ToArray();

        if (taken.Length == 0 || traded.Where(bar => bar.Session < from).Select(bar => (DateOnly?)bar.Session).LastOrDefault() is not { } end
            || LongMarks.PriceOf(traded, end, calendar) != LongMarks.Continues)
        {
            return null;
        }

        return (together, taken.Select(bar => bar.Session).ToHashSet(), FormattableString.Invariant($"linked by hand from {from:yyyy-MM-dd}, its first close there continuing the last before it"));
    }

    // The sessions both codes traded on, a session whose close moved with no volume among them, each code's bar there, in order.
    // see: A bar whose close moved with no volume traded with its volume missing
    static (SweepBar Own, SweepBar Other)[] Shared(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> other)
    {
        var mine = LongTraded.Of(own).Where(bar => bar.Close > 0).ToDictionary(bar => bar.Session);

        return [.. LongTraded.Of(other).Where(bar => bar.Close > 0 && mine.ContainsKey(bar.Session)).OrderBy(bar => bar.Session).Select(bar => (mine[bar.Session], bar))];
    }

    // A count of shares as a decimal, so a ratio of two volumes is read exactly.
    static decimal Shares(long volume) => volume;

    static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();

        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2m;
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
