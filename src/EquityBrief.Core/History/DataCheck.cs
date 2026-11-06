using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Chart;

namespace EquityBrief.Core.History;

// One session of a stock's pulled history as the data check reads it, the four prices one adjusted set.
public sealed record HeldSession(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, decimal? RawClose, long Volume);

// What is stored of one stock over the years the pull holds: its sessions oldest first, the splits and dividends the provider
// records, the code it traded under before and the day it changed, and the operator's word on its sessions.
public sealed record HeldStock(
    string Ticker,
    IReadOnlyList<HeldSession> Sessions,
    IReadOnlyList<CheckedSplit> Splits,
    IReadOnlyList<CheckedDividend> Dividends,
    (string Old, DateOnly? On)? TickerChange = null,
    IReadOnlyList<SessionMark>? Marks = null);

// A finding of the data check: the stock, the session it stands on, its class, its kind within the class and the words a person reads.
public sealed record DataFinding(string Ticker, DateOnly Session, string Class, string Kind, string Says);

// The data check over a stock's pulled history, asking nothing of anyone and moving no row: each session classified by stated
// rules, so the counts are read before anything is tested on the longer history. A missing session is one the calendar reads
// as a session inside the stock's series that the series does not hold; a candle with no volume is thin, copied or liquid; a
// jump is a close-to-close move past the limit, explained by a stored action, on a session the operator marked, in a thin
// stretch, on heavy volume, on a session ten or more stocks jumped, a spike back, or unexplained; a split or dividend the provider
// records that the closes do not carry; and three a person reads against the filings, a ticker change, a spin-off's
// when-issued start and a bankruptcy emergence.
// see: The data check classifies every stock's pulled sessions by stated rules into a table of its own, and moves no price row
public static class DataCheck
{
    public const string Missing = "missing session";
    public const string NoVolume = "no volume";
    public const string Jump = "jump";
    public const string NotCarried = "action not carried";
    public const string TickerChange = "ticker change";
    public const string WhenIssued = "when-issued start";
    public const string Emergence = "bankruptcy emergence";

    public static IReadOnlyList<string> Classes { get; } = [Missing, NoVolume, Jump, NotCarried, TickerChange, WhenIssued, Emergence];

    public const string Thin = "thin";
    public const string Liquid = "liquid";
    public const string Copied = "copied";
    public const string ByAnAction = "a stored action";
    public const string Marked = "the operator's word";
    public const string Wide = "market or sector wide";
    public const string OnVolume = "on volume";
    public const string SpikeBack = "spike back";
    public const string Unexplained = "unexplained";
    public const string Split = "split";
    public const string Dividend = "dividend";
    public const string ToRead = "to read against the filings";

    // The kinds of each class in the order the check tries them, the first that holds naming the session.
    public static IReadOnlyList<(string Class, string Kind)> Kinds { get; } =
    [
        (Missing, Thin), (Missing, Liquid),
        (NoVolume, Thin), (NoVolume, Copied), (NoVolume, Liquid),
        (Jump, ByAnAction), (Jump, Marked), (Jump, Thin), (Jump, OnVolume), (Jump, Wide), (Jump, SpikeBack), (Jump, Unexplained),
        (NotCarried, Split), (NotCarried, Dividend),
        (TickerChange, ToRead), (WhenIssued, ToRead), (Emergence, ToRead),
    ];

    // A session's volume is read against the stock's own sessions around it: the 31 before it and the 31 after, the session left
    // out and as many as the series holds at its ends. The stretch is thin where their median is under 100,000 shares or a quarter
    // of them or more hold no volume, and a session's volume is heavy at 1.5 times their median or more.
    public const int Around = 31;
    public const double ThinMedian = 100_000;
    public const double NoVolumeShare = 0.25;
    public const double HeavyMultiple = 1.5;

    // A jump is a move past the chart check's limit, and a session is market or sector wide where at least as many stocks as the
    // chart check's count jumped on it.
    public const decimal JumpLimit = ChartCheck.MoveLimit;
    public const int WideStocks = ChartCheck.MarketWideMembers;

    // A spike back: within 3 sessions after the jump a close comes back to within 10% of the close before it, the jump's own volume
    // not heavy. The move back is part of the spike and not a jump of its own.
    public const int SpikeSessions = 3;
    public const decimal SpikeWithin = 0.10m;

    // A when-issued start: a series starting after the first session the check reads whose first session trades under a quarter of
    // its stretch's median volume.
    public const double WhenIssuedShare = 0.25;

    // An emergence: a series that stops for 20 or more sessions the calendar reads and trades again, flagged on its first session back.
    public const int EmergenceGap = 20;

    // The four eras the History page draws, by the session's year.
    public static IReadOnlyList<string> Eras { get; } = ["2005 to 2009", "2010 to 2014", "2015 to 2019", "2020 on"];

    public const string BeforeTheEras = "before 2005";

    public static string Era(DateOnly session) => session.Year switch
    {
        < 2005 => BeforeTheEras,
        < 2010 => Eras[0],
        < 2015 => Eras[1],
        < 2020 => Eras[2],
        _ => Eras[3],
    };

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Money(decimal price) => price.ToString("#,##0.00", CultureInfo.InvariantCulture);

    static string Shares(double volume) => volume.ToString("#,##0", CultureInfo.InvariantCulture);

    static string Percent(decimal fraction) => (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    // The sessions a stock jumped on, which the run counts across every stock before it says what any jump was. The move back of a
    // spike is not one.
    public static IReadOnlyList<DateOnly> Jumps(IReadOnlyList<HeldSession> sessions)
    {
        var jumps = new List<DateOnly>();
        var through = -1;

        for (var at = 1; at < sessions.Count; at++)
        {
            if (at <= through || !Jumped(sessions, at))
            {
                continue;
            }

            jumps.Add(sessions[at].Session);
            through = BackAt(sessions, at) ?? -1;
        }

        return jumps;
    }

    static bool Jumped(IReadOnlyList<HeldSession> sessions, int at) =>
        sessions[at - 1].Close > 0 && Math.Abs((sessions[at].Close / sessions[at - 1].Close) - 1) > JumpLimit;

    // Where a jump comes back within reach of the close before it, none where it does not within the spike's sessions.
    static int? BackAt(IReadOnlyList<HeldSession> sessions, int at)
    {
        var before = sessions[at - 1].Close;

        for (var after = at + 1; after <= Math.Min(at + SpikeSessions, sessions.Count - 1); after++)
        {
            if (Math.Abs((sessions[after].Close / before) - 1) <= SpikeWithin)
            {
                return after;
            }
        }

        return null;
    }

    // The volumes around a place in the series: the 31 before it and the 31 after, the place itself left out. A place between two
    // sessions, a missing session's, reads the 31 at or before the first and the 31 from the second.
    static (double Median, double NoVolume) Stretch(IReadOnlyList<HeldSession> sessions, int before, int after)
    {
        var volumes = new List<long>(2 * Around);

        for (var at = Math.Max(0, before - Around + 1); at <= before && at < sessions.Count; at++)
        {
            volumes.Add(sessions[at].Volume);
        }

        for (var at = after; at < Math.Min(sessions.Count, after + Around); at++)
        {
            volumes.Add(sessions[at].Volume);
        }

        if (volumes.Count == 0)
        {
            return (0, 0);
        }

        volumes.Sort();
        var middle = volumes.Count / 2;
        var median = volumes.Count % 2 == 1 ? volumes[middle] : (volumes[middle - 1] + volumes[middle]) / 2.0;

        return (median, volumes.Count(volume => volume == 0) * 1.0 / volumes.Count);
    }

    static bool IsThin((double Median, double NoVolume) stretch) => stretch.Median < ThinMedian || stretch.NoVolume >= NoVolumeShare;

    static (double Median, double NoVolume) StretchAt(IReadOnlyList<HeldSession> sessions, int at) => Stretch(sessions, at - 1, at + 1);

    // Every finding over one stock. The calendar is the days the pulled history reads as sessions, the count the stocks that jumped
    // on each session, and the first session is the first the check reads of any stock.
    public static IReadOnlyList<DataFinding> Read(HeldStock stock, PulledCalendar calendar, IReadOnlyDictionary<DateOnly, int> jumpsEachSession, DateOnly firstRead)
    {
        var findings = new List<DataFinding>();
        var sessions = stock.Sessions;

        if (sessions.Count == 0)
        {
            return findings;
        }

        MissingSessions(stock, calendar, findings);
        Candles(stock, findings);
        Jumped(stock, jumpsEachSession, findings);
        Actions(stock, findings);
        Starts(stock, firstRead, findings);

        return findings;
    }

    static void MissingSessions(HeldStock stock, PulledCalendar calendar, List<DataFinding> findings)
    {
        var sessions = stock.Sessions;
        var held = sessions.Select(session => session.Session).ToArray();
        var (first, last) = (held[0], held[^1]);
        var missingRun = new List<DateOnly>();

        void EndRun(int resumesAt)
        {
            if (missingRun.Count >= EmergenceGap)
            {
                findings.Add(new(stock.Ticker, sessions[resumesAt].Session, Emergence, ToRead, Invariant($"It trades again on {Day(sessions[resumesAt].Session)} after {missingRun.Count} sessions the calendar reads with no row, from {Day(missingRun[0])}: a bankruptcy emergence or a relisting, its history cut at the new equity's first regular-way session where the filings say so.")));
            }

            missingRun.Clear();
        }

        foreach (var day in calendar.Sessions)
        {
            if (day < first || day > last)
            {
                continue;
            }

            var at = Array.BinarySearch(held, day);

            if (at >= 0)
            {
                if (missingRun.Count > 0)
                {
                    EndRun(at);
                }

                continue;
            }

            var next = ~at;
            var stretch = Stretch(sessions, next - 1, next);
            var kind = IsThin(stretch) ? Thin : Liquid;

            missingRun.Add(day);
            findings.Add(new(stock.Ticker, day, Missing, kind, Invariant($"The calendar reads {Day(day)} as a session and the series holds no row for it, in a {(kind == Thin ? "thin" : "liquid")} stretch whose median volume is {Shares(stretch.Median)} shares.")));
        }
    }

    static void Candles(HeldStock stock, List<DataFinding> findings)
    {
        var sessions = stock.Sessions;

        for (var at = 0; at < sessions.Count; at++)
        {
            var session = sessions[at];

            if (session.Volume != 0)
            {
                continue;
            }

            var stretch = StretchAt(sessions, at);
            var (kind, says) = IsThin(stretch)
                ? (Thin, Invariant($"On {Day(session.Session)} the candle has no volume in a thin stretch, whose median volume is {Shares(stretch.Median)} shares."))
                : at > 0 && CopiedCandle.Is(0, session.Open, session.High, session.Low, session.Close, sessions[at - 1].Close)
                    ? (Copied, Invariant($"On {Day(session.Session)} the candle has no volume and every price the close before, {Money(session.Close)}, in a liquid stretch."))
                    : (Liquid, Invariant($"On {Day(session.Session)} the candle has no volume in a liquid stretch, whose median volume is {Shares(stretch.Median)} shares."));

            findings.Add(new(stock.Ticker, session.Session, NoVolume, kind, says));
        }
    }

    static void Jumped(HeldStock stock, IReadOnlyDictionary<DateOnly, int> jumpsEachSession, List<DataFinding> findings)
    {
        var sessions = stock.Sessions;
        var jumps = Jumps(sessions).ToHashSet();

        for (var at = 1; at < sessions.Count; at++)
        {
            var (before, session) = (sessions[at - 1], sessions[at]);

            if (!jumps.Contains(session.Session))
            {
                continue;
            }

            var change = (session.Close / before.Close) - 1;
            var moved = Invariant($"On {Day(session.Session)} the close {(change > 0 ? "rose" : "fell")} {Percent(Math.Abs(change))} in a day, from {Money(before.Close)} to {Money(session.Close)}");
            var fall = before.RawClose is { } rawBefore && session.RawClose is { } raw ? rawBefore - raw : before.Close - session.Close;
            var stretch = StretchAt(sessions, at);
            var heavy = stretch.Median > 0 && session.Volume >= HeavyMultiple * stretch.Median;
            bool Between(DateOnly day) => before.Session < day && day <= session.Session;

            var (kind, says) =
                stock.Splits.FirstOrDefault(split => Between(split.ExDate) && SplitCarry.Reads(session.Close / before.Close, split.NewShares, split.OldShares)) is { } split
                    ? (ByAnAction, Invariant($"{moved}, matching a split the provider records of {split.NewShares:0.####} for {split.OldShares:0.####} that the closes still carry."))
                : fall > 0 && stock.Dividends.FirstOrDefault(dividend => before.Session <= dividend.ExDate && dividend.ExDate <= session.Session && dividend.Amount >= fall / 2) is { } dividend
                    ? (ByAnAction, Invariant($"{moved}, on a dividend of {Money(dividend.Amount)}."))
                : stock.Marks?.FirstOrDefault(mark => mark.Session == session.Session || mark.Session == before.Session) is { } mark
                    ? (Marked, Invariant($"{moved}, on a session the operator marked {mark.Kind}."))
                : IsThin(stretch)
                    ? (Thin, Invariant($"{moved}, in a thin stretch whose median volume is {Shares(stretch.Median)} shares."))
                : heavy
                    ? (OnVolume, Invariant($"{moved}, on {Shares(session.Volume)} shares against a median of {Shares(stretch.Median)}."))
                : jumpsEachSession.GetValueOrDefault(session.Session) >= WideStocks
                    ? (Wide, Invariant($"{moved}, one of {jumpsEachSession[session.Session]} stocks jumping that session."))
                : BackAt(sessions, at) is { } back
                    ? (SpikeBack, Invariant($"{moved}, back to {Money(sessions[back].Close)} on {Day(sessions[back].Session)} on no more than usual volume."))
                    : (Unexplained, Invariant($"{moved}, on no more than usual volume, with no stored action and no session the operator marked."));

            findings.Add(new(stock.Ticker, session.Session, Jump, kind, says));
        }
    }

    // Each stored split or dividend read against the step it should leave between the adjusted close and the raw one, as the chart
    // check reads the stored year's.
    static void Actions(HeldStock stock, List<DataFinding> findings)
    {
        var read = stock.Sessions.Where(session => session.RawClose is > 0 && session.Close > 0).ToArray();

        (decimal Step, HeldSession Before, HeldSession After)? Across(DateOnly exDate)
        {
            var at = Array.FindIndex(read, session => session.Session >= exDate);

            return at > 0 ? ((read[at - 1].Close / read[at - 1].RawClose!.Value) / (read[at].Close / read[at].RawClose!.Value), read[at - 1], read[at]) : null;
        }

        foreach (var split in stock.Splits)
        {
            if (Across(split.ExDate) is not { } across)
            {
                continue;
            }

            var expected = split.OldShares / split.NewShares;
            var carried = Math.Abs((across.Step / expected) - 1) < Math.Abs(across.Step - 1);

            if (!carried && Math.Abs(expected - 1) >= ChartCheck.SmallestSplitRead)
            {
                findings.Add(new(stock.Ticker, split.ExDate, NotCarried, Split, Invariant($"The provider records a split of {split.NewShares:0.####} for {split.OldShares:0.####} on {Day(split.ExDate)} that the closes do not carry.")));
            }
        }

        foreach (var dividend in stock.Dividends)
        {
            if (Across(dividend.ExDate) is not { } across)
            {
                continue;
            }

            var paid = stock.Splits.Where(split => split.ExDate > dividend.ExDate).Aggregate(dividend.Amount, (amount, split) => amount * split.NewShares / split.OldShares);

            if (DividendCarry.NotCarried(DividendCarry.Gap(paid, across.Before.Close, across.Before.RawClose!.Value, across.After.Close, across.After.RawClose!.Value)))
            {
                findings.Add(new(stock.Ticker, dividend.ExDate, NotCarried, Dividend, Invariant($"The provider records a dividend of {Money(dividend.Amount)} on {Day(dividend.ExDate)} that the closes do not carry.")));
            }
        }
    }

    static void Starts(HeldStock stock, DateOnly firstRead, List<DataFinding> findings)
    {
        var sessions = stock.Sessions;

        if (stock.TickerChange is { } change)
        {
            var on = change.On is { } changed && sessions.FirstOrDefault(session => session.Session >= changed) is { } first ? first.Session : sessions[0].Session;

            findings.Add(new(stock.Ticker, on, TickerChange, ToRead, change.On is { } day
                ? Invariant($"On {Day(day)} the code changed from {change.Old} to {stock.Ticker}.")
                : Invariant($"Its earlier sessions were traded under {change.Old}.")));
        }

        if (sessions[0].Session > firstRead)
        {
            var stretch = Stretch(sessions, -1, 1);

            if (stretch.Median > 0 && sessions[0].Volume < WhenIssuedShare * stretch.Median)
            {
                findings.Add(new(stock.Ticker, sessions[0].Session, WhenIssued, ToRead, Invariant($"Its series starts on {Day(sessions[0].Session)} on {Shares(sessions[0].Volume)} shares against a median of {Shares(stretch.Median)} over the sessions after: a when-issued start, its history cut at the first regular-way session where the filings say so.")));
            }
        }
    }

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
