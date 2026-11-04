using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Chart;

// One session as the chart check reads it: the prices the chart draws, the close the provider sent and whether it is pulled.
public sealed record CheckedSession(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, decimal? RawClose, long Volume, bool Pulled);

public sealed record CheckedDividend(DateOnly ExDate, decimal Amount);

public sealed record CheckedSplit(DateOnly ExDate, decimal NewShares, decimal OldShares);

// What is stored of why a stock's price might move.
public sealed record StockEvents(
    IReadOnlySet<DateOnly> Results,
    IReadOnlySet<DateOnly> Filings,
    IReadOnlyList<CheckedDividend> Dividends,
    IReadOnlyList<CheckedSplit> Splits);

// What is stored of one stock: its series at the store's scale oldest first, the averages each session draws, the pull's own
// closes over the stored year with the page's scale, the raw close the store kept of the session before its year, its state,
// whether any history is pulled before its year, and a ticker change a row or a join names.
public sealed record StockToCheck(
    string Ticker,
    IReadOnlyList<CheckedSession> Sessions,
    IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, double>> Averages,
    IReadOnlyDictionary<DateOnly, (decimal Close, decimal? RawClose)> PullOverStoredYear,
    decimal? PullScale,
    decimal? KeptRawBefore,
    string State,
    bool NoHistoryBeforeTheYear,
    (string Old, DateOnly? On)? TickerChange);

// A close-to-close move past the limit.
public sealed record SeenMove(DateOnly Before, DateOnly Session, decimal BeforeClose, decimal Close, decimal? RawBefore, decimal? Raw, bool Pulled);

// A finding of the check, or a note a chart draws: the audit's flag or the word note, its kind, the session it stands on and the
// words a person reads.
public sealed record ChartFinding(string Flag, string Kind, DateOnly Session, string Says);

// The chart audit as a check over stored figures, asking nothing of anyone. Flag a: an average more than a quarter outside the
// candles in view. Flag b: a close-to-close change over a quarter with no event on the session or the one before. Flag c: a
// break where the pull meets the store or where the two disagree, or a drawn average that is not its closes'. Flag e: a session
// the exchange traded missing, a candle that could not have traded, one with no volume, a copied candle, a stock behind the night
// while it reads ok, and a stored action the bars do not carry. A note is a real event a chart says in words: a move an event or
// a market-wide session explains, a spin-off, a ticker change, a first session.
// see: The chart check reads every member's chart after the bars land and asks nothing of the network, and lists what it finds the morning it appears
// see: A real event the chart check confirms is a line beneath the chart saying what happened on that date
public static class ChartCheck
{
    public const string Note = "note";

    // A finding on a session before the store's year says so in its kind: the proof page folds those away, so a morning's list
    // is what stands over the bars the rules read.
    public const string InThePulledYears = " in the pulled years";

    static string Over(string kind, bool pulled) => pulled ? kind + InThePulledYears : kind;

    // A move past a quarter is one to explain, an average a quarter past the candles stretches a chart, and ten members past a
    // quarter on one session is the market's move rather than a stock's.
    public const decimal MoveLimit = 0.25m;
    public const double AverageLimit = 0.25;
    public const int MarketWideMembers = 10;

    // Where the pull and the store are held to each other: 5% on the adjusted close at the page's scale, 1% on the raw and 5 points
    // on the move across the join; a drawn average to its closes within 1%.
    public const decimal PullAdjustedLimit = 0.05m;
    public const decimal PullRawLimit = 0.01m;
    public const decimal JoinLimit = 0.05m;
    public const double AverageOffLimit = 0.01;

    // A company's own split comes to a whole number or to 5 for 4, 4 for 3 or 3 for 2, in either direction and within 0.1%, as 25 for 1,
    // 1 for 10 or the provider's 959692 for 319897. Any other is how the provider files a spin-off or a stock dividend, as 1241 for
    // 1000, 1333 for 200 or 2.793 for 1.
    public const decimal SplitRatioNear = 0.001m;

    static readonly decimal[] FractionsOfASplit = [5m / 4m, 4m / 3m, 3m / 2m];

    public static bool IsACompanysOwnSplit(decimal newShares, decimal oldShares)
    {
        var ratio = Math.Max(newShares / oldShares, oldShares / newShares);

        return Math.Abs((ratio / Math.Round(ratio)) - 1) <= SplitRatioNear
            || FractionsOfASplit.Any(fraction => Math.Abs((ratio / fraction) - 1) <= SplitRatioNear);
    }

    // A ratio within 3% of one leaves too small a step to read, and a dividend under half of 1% of the close the same.
    public const decimal SmallestSplitRead = 0.03m;
    public const decimal SmallestDividendRead = 0.005m;

    // A first session since two years before the night is a stock new to the market.
    public const int NewListingYears = 2;

    static readonly IReadOnlyDictionary<string, int> Spans = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["sma20"] = 20,
        ["sma50"] = 50,
        ["sma200"] = 200,
    };

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Money(decimal price) => price.ToString("#,##0.00", CultureInfo.InvariantCulture);

    static string Percent(decimal fraction) => (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    static string Signed(decimal fraction) => (fraction * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";

    // The moves past the limit, which the night counts across every member before it says what any of them was.
    public static IReadOnlyList<SeenMove> MovesOf(IReadOnlyList<CheckedSession> sessions)
    {
        var moves = new List<SeenMove>();

        for (var at = 1; at < sessions.Count; at++)
        {
            var (before, session) = (sessions[at - 1], sessions[at]);

            if (before.Close > 0 && Math.Abs((session.Close / before.Close) - 1) > MoveLimit)
            {
                moves.Add(new SeenMove(before.Session, session.Session, before.Close, session.Close, before.RawClose, session.RawClose, session.Pulled));
            }
        }

        return moves;
    }

    // Flag b, and the notes of the moves something explains: a split the prices still carry first, then an event on the session
    // or the one before, a dividend at least half the fall, or a session ten members moved that far on.
    public static IReadOnlyList<ChartFinding> ReadMoves(IReadOnlyList<SeenMove> moves, StockEvents events, IReadOnlyDictionary<DateOnly, int> movesEachSession)
    {
        var findings = new List<ChartFinding>();

        foreach (var move in moves)
        {
            var change = (move.Close / move.BeforeClose) - 1;
            var moved = FormattableString.Invariant($"On {Day(move.Session)} the close {(change > 0 ? "rose" : "fell")} {Percent(Math.Abs(change))} in a day, from {Money(move.BeforeClose)} to {Money(move.Close)}");
            bool Between(DateOnly day) => move.Before <= day && day <= move.Session;
            var fall = move.RawBefore is { } rawBefore && move.Raw is { } raw ? rawBefore - raw : move.BeforeClose - move.Close;

            if (events.Splits.FirstOrDefault(split => move.Before < split.ExDate && split.ExDate <= move.Session && SplitCarry.Reads(move.Close / move.BeforeClose, split.NewShares, split.OldShares)) is { } carried)
            {
                findings.Add(new("b", Over("split not applied", move.Pulled), move.Session, FormattableString.Invariant($"{moved}, matching a split the provider records of {carried.NewShares:0.####} for {carried.OldShares:0.####} that the prices still carry.")));
            }
            else if (events.Results.Any(Between))
            {
                findings.Add(new(Note, "move", move.Session, moved + ", on its results."));
            }
            else if (events.Filings.Any(Between))
            {
                findings.Add(new(Note, "move", move.Session, moved + ", around a filing."));
            }
            else if (fall > 0 && events.Dividends.Any(dividend => Between(dividend.ExDate) && dividend.Amount >= fall / 2))
            {
                findings.Add(new(Note, "move", move.Session, moved + ", on a dividend."));
            }
            else if (movesEachSession.GetValueOrDefault(move.Session) >= MarketWideMembers)
            {
                findings.Add(new(Note, "move", move.Session, FormattableString.Invariant($"{moved}, one of {movesEachSession[move.Session]} members moving that far that session.")));
            }
            else
            {
                findings.Add(new("b", Over("no event", move.Pulled), move.Session, moved + ", with no split, dividend, results or filing stored for it."));
            }
        }

        return findings;
    }

    // Everything but the moves.
    public static IReadOnlyList<ChartFinding> Read(StockToCheck stock, StockEvents events, DateOnly night, IReadOnlyList<DateOnly> traded)
    {
        var findings = new List<ChartFinding>();

        if (stock.Sessions.Count > 0)
        {
            AveragesOutside(stock, findings);
            Joins(stock, findings);
            Candles(stock, findings);
            Standing(stock, night, traded, findings);
            Actions(stock, events, findings);
        }

        return findings;
    }

    // Flag a on each interval's candles: the stored year's on the daily chart, the whole series' on the longer two.
    static void AveragesOutside(StockToCheck stock, List<ChartFinding> findings)
    {
        static SessionBar Bar(CheckedSession session) => new(session.Session, session.Open, session.High, session.Low, session.Close, session.Volume);

        var whole = stock.Sessions.Select(Bar).ToArray();
        var stored = stock.Sessions.Where(session => !session.Pulled).Select(Bar).ToArray();

        foreach (var (interval, series) in new[] { (ChartInterval.Daily, stored), (ChartInterval.Weekly, whole), (ChartInterval.Monthly, whole) })
        {
            if (series.Length == 0)
            {
                continue;
            }

            var candles = ChartSeries.Candles(series, interval);
            var (high, low) = (Statistic.FromPrice(candles.Max(candle => candle.High)), Statistic.FromPrice(candles.Min(candle => candle.Low)));
            var word = ChartSeries.Word(interval);

            foreach (var (name, values) in stock.Averages.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var drawn = candles.Where(candle => values.ContainsKey(candle.Last)).Select(candle => (candle.Last, Value: values[candle.Last])).ToArray();

                if (drawn.Length == 0)
                {
                    continue;
                }

                var (top, bottom) = (drawn.MaxBy(point => point.Value), drawn.MinBy(point => point.Value));

                if (top.Value > high * (1 + AverageLimit))
                {
                    findings.Add(new("a", FormattableString.Invariant($"{name} above on {word}"), top.Last, FormattableString.Invariant($"On the {word} chart {name} reaches {top.Value:0.00} on {Day(top.Last)}, over a quarter above the highest candle, {high:0.00}.")));
                }

                if (bottom.Value < low * (1 - AverageLimit))
                {
                    findings.Add(new("a", FormattableString.Invariant($"{name} below on {word}"), bottom.Last, FormattableString.Invariant($"On the {word} chart {name} falls to {bottom.Value:0.00} on {Day(bottom.Last)}, over a quarter below the lowest candle, {low:0.00}.")));
                }
            }
        }
    }

    // Flag c: no history before the year; the move across the join against the stock's own; the stored year against the provider's
    // own pull at the page's scale; and a drawn average against its closes.
    static void Joins(StockToCheck stock, List<ChartFinding> findings)
    {
        var first = stock.Sessions.FirstOrDefault(session => !session.Pulled);

        if (first is null)
        {
            return;
        }

        if (stock.NoHistoryBeforeTheYear)
        {
            findings.Add(new("c", "no history", first.Session, FormattableString.Invariant($"No history is pulled before {Day(first.Session)}, so the weekly and monthly charts show one year.")));
        }

        var at = stock.Sessions.ToList().IndexOf(first);

        if (at > 0 && stock.Sessions[at - 1].Close > 0 && first.RawClose is { } rawFirst && (stock.KeptRawBefore ?? stock.Sessions[at - 1].RawClose) is { } rawBefore && rawBefore > 0)
        {
            var (chart, own) = ((first.Close / stock.Sessions[at - 1].Close) - 1, (rawFirst / rawBefore) - 1);

            if (Math.Abs(chart - own) > JoinLimit)
            {
                findings.Add(new("c", "join", first.Session, FormattableString.Invariant($"Across the join at {Day(first.Session)} the chart moves {Signed(chart)} where the stock's own closes moved {Signed(own)}.")));
            }
        }

        foreach (var session in stock.Sessions.Where(session => !session.Pulled))
        {
            if (!stock.PullOverStoredYear.TryGetValue(session.Session, out var pulled) || stock.PullScale is not { } scale || pulled.Close <= 0)
            {
                continue;
            }

            var apart = (session.Close / (pulled.Close * scale)) - 1;

            if (Math.Abs(apart) > PullAdjustedLimit)
            {
                findings.Add(new("c", "store against pull", session.Session, FormattableString.Invariant($"On {Day(session.Session)} the stored close {Money(session.Close)} stands {Signed(apart)} from the pull's {Money(pulled.Close * scale)} at the page's scale.")));
            }
            else if (session.RawClose is { } raw && pulled.RawClose is { } rawPulled && rawPulled > 0 && Math.Abs((raw / rawPulled) - 1) > PullRawLimit)
            {
                findings.Add(new("c", "raw against pull", session.Session, FormattableString.Invariant($"On {Day(session.Session)} the stored raw close {Money(raw)} stands {Signed((raw / rawPulled) - 1)} from the pull's {Money(rawPulled)}.")));
            }
        }

        var closes = stock.Sessions.Select(session => Statistic.FromPrice(session.Close)).ToArray();
        var running = new double[closes.Length + 1];

        for (var index = 0; index < closes.Length; index++)
        {
            running[index + 1] = running[index] + closes[index];
        }

        foreach (var (name, span) in Spans)
        {
            if (!stock.Averages.TryGetValue(name, out var values))
            {
                continue;
            }

            var worst = (Session: default(DateOnly), Drawn: 0d, Worked: 0d, Apart: 0d);

            for (var index = span - 1; index < closes.Length; index++)
            {
                if (values.TryGetValue(stock.Sessions[index].Session, out var drawn))
                {
                    var worked = (running[index + 1] - running[index + 1 - span]) / span;
                    var apart = worked != 0 ? Math.Abs((drawn / worked) - 1) : 0;

                    if (apart > worst.Apart)
                    {
                        worst = (stock.Sessions[index].Session, drawn, worked, apart);
                    }
                }
            }

            if (worst.Apart > AverageOffLimit)
            {
                findings.Add(new("c", name + " against its closes", worst.Session, FormattableString.Invariant($"On {Day(worst.Session)} {name} draws {worst.Drawn:0.00} where the closes give {worst.Worked:0.00}.")));
            }
        }
    }

    // Flag e's candles: one that could not have traded, one copying the session before, one with no volume.
    static void Candles(StockToCheck stock, List<ChartFinding> findings)
    {
        for (var at = 0; at < stock.Sessions.Count; at++)
        {
            var session = stock.Sessions[at];

            if (Math.Min(Math.Min(session.Open, session.Close), Math.Min(session.High, session.Low)) <= 0 || session.High < session.Low || session.Close > session.High || session.Close < session.Low || session.Open > session.High || session.Open < session.Low)
            {
                findings.Add(new("e", Over("impossible candle", session.Pulled), session.Session, FormattableString.Invariant($"On {Day(session.Session)} the candle could not have traded: open {Money(session.Open)}, high {Money(session.High)}, low {Money(session.Low)}, close {Money(session.Close)}.")));
            }
            else if (session.Volume == 0 && at > 0 && CopiedCandle.Is(0, session.Open, session.High, session.Low, session.Close, stock.Sessions[at - 1].Close))
            {
                findings.Add(new("e", Over("copied candle", session.Pulled), session.Session, FormattableString.Invariant($"On {Day(session.Session)} the candle has no volume and every price the close before, {Money(session.Close)}.")));
            }
            else if (session.Volume == 0)
            {
                findings.Add(new("e", Over("zero volume", session.Pulled), session.Session, FormattableString.Invariant($"On {Day(session.Session)} the candle has no volume, at a close of {Money(session.Close)}.")));
            }
        }
    }

    // Flag e's sessions and the notes of a ticker change and a first session.
    static void Standing(StockToCheck stock, DateOnly night, IReadOnlyList<DateOnly> traded, List<ChartFinding> findings)
    {
        var stored = stock.Sessions.Where(session => !session.Pulled).Select(session => session.Session).ToHashSet();

        if (stored.Count == 0)
        {
            return;
        }

        var (first, newest) = (stored.Min(), stored.Max());

        foreach (var day in traded.Where(day => first <= day && day <= newest && !stored.Contains(day)))
        {
            findings.Add(new("e", "missing session", day, FormattableString.Invariant($"The exchange traded {Day(day)} and the stored year holds no row for it.")));
        }

        if (stock.State == "ok" && traded.Any(day => newest < day && day <= night))
        {
            findings.Add(new("e", "behind reading ok", newest, FormattableString.Invariant($"The newest bar is {Day(newest)}, behind the night of {Day(night)}, while the stock's state reads ok.")));
        }

        if (stock.TickerChange is { } change)
        {
            findings.Add(change.On is { } on
                ? new(Note, "ticker change", on, FormattableString.Invariant($"On {Day(on)} the ticker changed from {change.Old} to {stock.Ticker}."))
                : new(Note, "ticker change", stock.Sessions[0].Session, FormattableString.Invariant($"Its earlier sessions were traded under {change.Old}.")));
        }
        else if (stock.Sessions[0].Session > night.AddYears(-NewListingYears) && !stock.NoHistoryBeforeTheYear)
        {
            findings.Add(new(Note, "first session", stock.Sessions[0].Session, FormattableString.Invariant($"It started trading on {Day(stock.Sessions[0].Session)}.")));
        }
    }

    // Flag e's actions and the notes of the spin-offs: each stored split or dividend over the stored year read against the step it
    // should leave between the adjusted close and the raw one.
    static void Actions(StockToCheck stock, StockEvents events, List<ChartFinding> findings)
    {
        var stored = stock.Sessions.Where(session => !session.Pulled && session.RawClose is > 0).ToArray();

        (decimal Step, CheckedSession Before, CheckedSession After)? Across(DateOnly exDate)
        {
            var at = Array.FindIndex(stored, session => session.Session >= exDate);

            return at > 0 && stored[at].Close > 0
                ? ((stored[at - 1].Close / stored[at - 1].RawClose!.Value) / (stored[at].Close / stored[at].RawClose!.Value), stored[at - 1], stored[at])
                : null;
        }

        foreach (var split in events.Splits)
        {
            if (Across(split.ExDate) is not { } across)
            {
                continue;
            }

            var expected = split.OldShares / split.NewShares;
            var carried = Math.Abs((across.Step / expected) - 1) < Math.Abs(across.Step - 1);

            if (!carried && Math.Abs(expected - 1) >= SmallestSplitRead)
            {
                findings.Add(new("e", "action not carried", split.ExDate, FormattableString.Invariant($"The store holds a split of {split.NewShares:0.####} for {split.OldShares:0.####} on {Day(split.ExDate)} that the stored bars do not carry.")));
            }
            else if (carried && !IsACompanysOwnSplit(split.NewShares, split.OldShares))
            {
                findings.Add(new(Note, "spin-off", split.ExDate, FormattableString.Invariant($"On {Day(split.ExDate)} it paid out a spin-off or a stock dividend, filed as a split of {split.NewShares:0.####} for {split.OldShares:0.####}; the prices before it are adjusted for it, the raw close going from {Money(across.Before.RawClose!.Value)} to {Money(across.After.RawClose!.Value)}.")));
            }
        }

        foreach (var dividend in events.Dividends)
        {
            if (Across(dividend.ExDate) is not { } across || dividend.Amount / across.Before.RawClose!.Value < SmallestDividendRead)
            {
                continue;
            }

            // Half the step the dividend should leave, and no more than half the smallest step read: a split after it restates the
            // amount and not the raw close, so a step of any readable size is the dividend carried.
            if (Math.Abs(across.Step - 1) < Math.Min(dividend.Amount / across.Before.RawClose!.Value, SmallestDividendRead) / 2)
            {
                findings.Add(new("e", "action not carried", dividend.ExDate, FormattableString.Invariant($"The store holds a dividend of {Money(dividend.Amount)} on {Day(dividend.ExDate)} that the stored bars do not carry.")));
            }
        }
    }
}
