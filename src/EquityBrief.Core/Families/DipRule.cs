using System.Globalization;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Families;

// One setting of the short-term dip: the 2-session relative strength index the drop must close under, the trend the
// close must stand in, the sessions back the drop is read over for a report, the stop's distance under the listing
// close in typical moves, the average of closes a close above sells on, the sessions a trade is held at most, and the
// market setting the family reads, off where it reads none.
public sealed record DipSetting(double RsiUnder, bool FiftyOverLong, int DropSessions, double StopMoves, int ExitAverage, int TimeLimit, string Market)
{
    // The setting as a card's gates and an approval's words state it, each dial and its level.
    public string Short => FormattableString.Invariant(
        $"rsi {RsiUnder:0.##}|drop {DropSessions}|trend {(FiftyOverLong ? "fifty" : "long")}|stop {StopMoves:0.##}|exit {ExitAverage}|limit {TimeLimit}|market {Market}");

    // The setting as the chips a card draws beneath its heading, each dial in a few words, the floors its index reads
    // first: the dollar volume floor where the index holds one, and the least close on every index.
    public IReadOnlyList<string> ChipsOn(string index) =>
    [
        .. Readings.MemberReadings.DollarVolumeFloor(index) is not null ? ["dollar floor 1 times"] : Array.Empty<string>(),
        FormattableString.Invariant($"close at least ${Readings.MemberReadings.LowestPrice:0.##}"),
        FormattableString.Invariant($"above its {DipRule.TrendSessions}-session average"),
        .. FiftyOverLong ? [FormattableString.Invariant($"its {DipRule.FiftySessions}-session above it")] : Array.Empty<string>(),
        FormattableString.Invariant($"{DipRule.RsiSessions}-session RSI under {RsiUnder:0.##}"),
        FormattableString.Invariant($"no report in {DropSessions} sessions or ahead"),
        FormattableString.Invariant($"stop {StopMoves:0.##} moves"),
        FormattableString.Invariant($"sold over its {ExitAverage}-session average"),
        FormattableString.Invariant($"{TimeLimit} sessions at most"),
        Market == DipRule.MarketOff ? "market setting off" : FormattableString.Invariant($"market setting {Market}"),
    ];

    // A setting read back from its short form, none where the form does not state every dial.
    public static DipSetting? FromShort(string stated)
    {
        var dials = stated.Split('|', StringSplitOptions.TrimEntries)
            .Select(part => part.Split(' ', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);

        static double? Number(IReadOnlyDictionary<string, string> dials, string name) =>
            dials.TryGetValue(name, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

        return Number(dials, "rsi") is { } rsi && Number(dials, "drop") is { } drop && dials.TryGetValue("trend", out var trend)
            && Number(dials, "stop") is { } stop && Number(dials, "exit") is { } exit && Number(dials, "limit") is { } limit && dials.TryGetValue("market", out var market)
            ? new DipSetting(rsi, trend == "fifty", (int)drop, stop, (int)exit, (int)limit, market)
            : null;
    }

    // The setting in words, as a card states the rule.
    public string Words =>
        FormattableString.Invariant($"A stock closing above its {DipRule.TrendSessions}-session average")
        + (FiftyOverLong ? FormattableString.Invariant($", its {DipRule.FiftySessions}-session average above that,") : string.Empty)
        + FormattableString.Invariant($" whose {DipRule.RsiSessions}-session relative strength index closes under {RsiUnder:0.##} after a sharp drop, with no report on any of the {DropSessions} sessions to the listing nor one due before its time limit ends. Bought at the next session's open, its stop {StopMoves:0.##} typical moves under the listing close and no target; sold at the next open after the first close above its {ExitAverage}-session average of closes or a close under its stop, and at the close of its {TimeLimit}th session where neither came.");
}

// One member as the dip reads it on one night: its stored closes to the night, oldest first; its typical move on the
// night; whether it clears the floors every
// family on its index reads, with the words its row says where it does not; each report on file, by the sessions it
// falls from the night, nought the night itself, a session back negative and one ahead positive; the date of the next
// report ahead, none where the calendar holds none; the market setting's answer on the night; the first session of
// the year behind the night its stored series misses, none where it misses none; and whether its series is marked
// suspect, its corporate action's refetch not stored.
public sealed record DipMember(
    string Ticker,
    IReadOnlyList<double> Closes,
    double? TypicalMove,
    bool ClearsTheFloors,
    string FloorWords,
    IReadOnlyList<int> Reports,
    DateOnly? NextReport,
    Gate Market,
    DateOnly? Gap = null,
    bool Suspect = false);

// A member's answer under the dip on one night: its gates in the rule's order, each with the values that decided it,
// the plan it is bought on where one could be placed, the figure the family orders by, and the first gate it failed.
public sealed record DipAnswer(
    string Ticker,
    IReadOnlyList<Gate> Gates,
    decimal? Entry,
    decimal? Stop,
    int? TimeLimit,
    double? Rsi,
    string? Reason)
{
    public bool Passed => Reason is null;
}

// How a dip trade ended, or that it has not: sold at the next open after a close above the exit's average or under
// the stop, sold at its last session's close, or still held.
public static class DipEnd
{
    public const string Exit = "exit";

    public const string Stop = "stop";

    public const string Limit = "limit";

    public const string Open = "open";
}

// One dip trade's path from its listing: the session it was bought on at the next open and that open, where it ended,
// its sale's price and why, the sessions it was held to its sale, and its result in multiples of the risk the listing
// close set, bought at the listing close and so before the open's gap; none of it held where its next session is not
// stored yet, the trade waiting to be bought, or where the session its sale waits on is not stored, the trade still
// held.
public sealed record DipPath(
    DateOnly? BoughtOn,
    double? Open,
    DateOnly? EndedOn,
    double? Sale,
    string End,
    int Sessions,
    double? ResultAtClose);

// One session of a trade's path: its date, open and close, and the average of closes the exit reads on it, none where
// the member holds too few closes for it; or, for an exit a sweep reads in the average's place, whether its close
// triggers that exit, which the walk then reads instead.
public readonly record struct DipSession(DateOnly Session, double Open, double Close, double? Average, bool? Trigger = null);

// The short-term dip: a stock in an uptrend, its 2-session relative strength index driven under its floor by a sharp
// drop no report caused, bought at the next session's open and sold on the bounce within days. Every part is read by
// code from the stored bars, the stored calendar and, over the history, the pulled history, and no model reads any of
// it. Its gates are read in order: the market setting, which the provisional rule holds off; the floors every family on
// the index reads; a whole history of the sessions its average reads, no session of its stored year missing and its
// series not suspect; the close above that average; the drop; no report in the drop or before the time limit ends; and
// a typical move to place the stop by. A gate a member cannot be read on for want of a figure fails, its reason saying
// what it lacked; every gate is stored, and the first a member fails names it. A member whose history is not whole has
// nothing read from its closes, so no figure is computed across a missing session.
// see: The short-term dip buys a stock in an uptrend after a sharp drop no report caused and sells the bounce within days
// see: Bars are never interpolated
// see: Adjusted history is re-fetched after a corporate action
public static class DipRule
{
    public const string Name = "dip";

    // The sessions the trend's average and its second form read, and the relative strength index's window.
    public const int TrendSessions = 200;

    public const int FiftySessions = 50;

    public const int RsiSessions = 2;

    // The market setting's word where the family reads none.
    public const string MarketOff = "off";

    // The report gate's word for the next report where the stored calendar holds none ahead of the night.
    public const string NoReportOnFile = "none";

    // The provisional setting, Connors and Alvarez's: the close above its 200-session average, the 2-session RSI under
    // 10, no report in the three sessions to the listing nor before the time limit, the stop 4 typical moves under the
    // listing close, sold on the first close above the 5-session average and at the close of the 10th session, and no
    // market setting.
    public static DipSetting Provisional { get; } = new(10, false, 3, 4, 5, 10, MarketOff);

    // The gates in the rule's order, by the names the funnel draws them under.
    public const string MarketGate = "market";

    public const string FloorsGate = "floors";

    public const string HistoryGate = "history";

    public const string TrendGate = "trend";

    public const string DropGate = "drop";

    public const string ReportGate = "report";

    public const string MoveGate = "typical move";

    public static IReadOnlyList<string> Gates { get; } = [MarketGate, FloorsGate, HistoryGate, TrendGate, DropGate, ReportGate, MoveGate];

    // The reasons a member failing a gate is named by.
    public const string MarketClosed = "the market setting closed";

    public const string UnderTheFloors = "under the price or dollar volume floor";

    public const string ShortHistory = "fewer than 200 sessions stored";

    public const string Gapped = "a session missing from its stored year";

    public const string SuspectSeries = "its series marked suspect";

    public const string NoTrend = "not above its 200-session average";

    public const string NoDrop = "the 2-session RSI not under its floor";

    public const string ReportNear = "a report in the drop or before the time limit";

    public const string NoMove = "no typical move to place its stop by";

    public const string StopUnderNothing = "its stop at or under nothing";

    // The market setting when the family reads none, which every member passes.
    public static Gate NoMarket { get; } = new(MarketGate, true, "the dip reads no market setting", FamilyRule.Values(("setting", MarketOff)));

    // Wilder's relative strength index over a run of closes, oldest first, at the given sessions: the first average gain
    // and loss the simple means of the window's first changes and each later one smoothed by the window, none before the
    // window's first change, and 100 on a close no loss has touched.
    public static double?[] Rsi(IReadOnlyList<double> closes, int sessions = RsiSessions)
    {
        var rsi = new double?[closes.Count];

        if (sessions < 1 || closes.Count <= sessions)
        {
            return rsi;
        }

        var gain = 0.0;
        var loss = 0.0;

        for (var at = 1; at <= sessions; at++)
        {
            var change = closes[at] - closes[at - 1];

            gain += Math.Max(change, 0);
            loss += Math.Max(-change, 0);
        }

        gain /= sessions;
        loss /= sessions;
        rsi[sessions] = Of(gain, loss);

        for (var at = sessions + 1; at < closes.Count; at++)
        {
            var change = closes[at] - closes[at - 1];

            gain = ((gain * (sessions - 1)) + Math.Max(change, 0)) / sessions;
            loss = ((loss * (sessions - 1)) + Math.Max(-change, 0)) / sessions;
            rsi[at] = Of(gain, loss);
        }

        return rsi;
    }

    static double Of(double gain, double loss) => loss == 0 ? 100 : 100 - (100 / (1 + (gain / loss)));

    // The simple average of the closes ending on a session, the session's own among them, none where fewer stand behind it.
    public static double? Average(IReadOnlyList<double> closes, int at, int sessions)
    {
        if (sessions < 1 || at < sessions - 1 || at >= closes.Count)
        {
            return null;
        }

        var sum = 0.0;

        for (var back = 0; back < sessions; back++)
        {
            sum += closes[at - back];
        }

        return sum / sessions;
    }

    // Whether a report on file falls in the drop or before the time limit ends: on any of the sessions to the listing,
    // the night's own included, or on any session from the one it is bought on to the last its time limit holds.
    public static bool ReportInside(IReadOnlyList<int> reports, DipSetting setting) =>
        reports.Any(sessions => sessions > -setting.DropSessions && sessions <= setting.TimeLimit);

    // A member's answer at a setting: each gate in order with the values that decided it, the first failed naming the
    // member, and where every gate passes the plan at the listing close, its stop the setting's typical moves under it.
    // A member whose stored year misses a session or whose series is suspect has no average, relative strength index
    // or typical move read from its closes, its history gate failing with the session or the mark named.
    public static DipAnswer Read(DipMember member, DipSetting setting)
    {
        var gates = new List<Gate> { member.Market };
        var night = member.Closes.Count - 1;
        var whole = member.Gap is null && !member.Suspect;
        double? close = night >= 0 ? member.Closes[night] : null;
        var average = whole && night >= 0 ? Average(member.Closes, night, TrendSessions) : null;
        var fifty = whole && night >= 0 ? Average(member.Closes, night, FiftySessions) : null;
        var rsi = whole && night >= 0 ? Rsi(member.Closes)[night] : null;
        var reported = ReportInside(member.Reports, setting);

        gates.Add(new Gate(FloorsGate, member.ClearsTheFloors, member.ClearsTheFloors ? string.Empty : member.FloorWords, FamilyRule.Values()));

        gates.Add(new Gate(
            HistoryGate,
            average is not null,
            average is not null ? string.Empty : HistoryWords(member),
            member.Gap is { } missing
                ? FamilyRule.Values(("sessions", FamilyRule.Whole(member.Closes.Count)), ("gap", missing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                : member.Suspect
                    ? FamilyRule.Values(("sessions", FamilyRule.Whole(member.Closes.Count)), ("suspect", "yes"))
                    : FamilyRule.Values(("sessions", FamilyRule.Whole(member.Closes.Count)))));

        var trend = close is { } last && average is { } mean && last > mean && (!setting.FiftyOverLong || (fifty is { } short50 && short50 > mean));

        gates.Add(new Gate(
            TrendGate,
            trend,
            trend ? string.Empty : average is null ? "its average not read" : FamilyRule.Invariant($"closed {close:0.##} against its {TrendSessions}-session average of {average:0.##}"),
            FamilyRule.Values(("close", FamilyRule.Figure(close)), ("average", FamilyRule.Figure(average)), ("fifty", FamilyRule.Figure(fifty)))));

        var dropped = rsi is { } read && read < setting.RsiUnder;

        gates.Add(new Gate(
            DropGate,
            dropped,
            dropped ? string.Empty : rsi is null ? "its 2-session RSI not read" : FamilyRule.Invariant($"its 2-session RSI of {rsi:0.#} not under {setting.RsiUnder:0.##}"),
            FamilyRule.Values(("rsi", FamilyRule.Figure(rsi)), ("floor", FamilyRule.Figure(setting.RsiUnder)))));

        gates.Add(new Gate(
            ReportGate,
            !reported,
            reported ? ReportWords(member.Reports, setting) : string.Empty,
            FamilyRule.Values(("reports", string.Join(",", member.Reports.Select(sessions => FamilyRule.Whole(sessions)))), ("next", member.NextReport?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? NoReportOnFile))));

        double? move = whole && member.TypicalMove is { } typical && typical > 0 ? typical : null;

        // A stop the setting's typical moves would place at or under nothing is no stop a trade can be sized on.
        var above = move is { } reach && close is { } at && at - (setting.StopMoves * reach) > 0;

        gates.Add(new Gate(
            MoveGate,
            above,
            above ? string.Empty
                : move is not null ? FamilyRule.Invariant($"a stop {setting.StopMoves:0.##} typical moves of {move:0.##} under its close of {close:0.##} falls at or under nothing")
                : whole ? NoMove : "its typical move not read",
            FamilyRule.Values(("move", FamilyRule.Figure(whole ? member.TypicalMove : null)))));

        var failed = gates.FirstOrDefault(gate => !gate.Passed);

        if (failed is not null || close is not { } entry || move is not { } placed || !above)
        {
            return new DipAnswer(member.Ticker, gates, null, null, null, rsi, failed?.Name == MoveGate && move is not null ? StopUnderNothing : ReasonOf(failed, member));
        }

        var buy = Prices.Statistic.ToPrice(entry);

        return new DipAnswer(member.Ticker, gates, buy, Prices.Statistic.ToPrice(entry - (setting.StopMoves * placed)), setting.TimeLimit, rsi, null);
    }

    // The place in the rule's order of the gate a reason names, the history gate's for each of its three reasons and the
    // typical move's for both of its own, and none for a reason the rule does not give.
    public static int GateOf(string reason) => reason switch
    {
        MarketClosed => 0,
        UnderTheFloors => 1,
        ShortHistory or Gapped or SuspectSeries => 2,
        NoTrend => 3,
        NoDrop => 4,
        ReportNear => 5,
        NoMove or StopUnderNothing => 6,
        _ => -1,
    };

    // Why a member's history gate failed: the session its stored year misses, its series marked suspect, or the
    // sessions it holds against those its average reads.
    static string HistoryWords(DipMember member) =>
        member.Gap is { } missing
            ? "its stored year missing the session of " + missing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", so nothing is read across it"
            : member.Suspect
                ? "its series marked suspect, its corporate action's refetch not stored, so nothing is read from it"
                : FamilyRule.Invariant($"{member.Closes.Count} sessions stored, fewer than the {TrendSessions} its average reads");

    // The reason a failed gate names the member by.
    static string? ReasonOf(Gate? failed, DipMember member) => failed?.Name switch
    {
        null => null,
        MarketGate => MarketClosed,
        FloorsGate => UnderTheFloors,
        HistoryGate => member.Gap is not null ? Gapped : member.Suspect ? SuspectSeries : ShortHistory,
        TrendGate => NoTrend,
        DropGate => NoDrop,
        ReportGate => ReportNear,
        _ => NoMove,
    };

    static string ReportWords(IReadOnlyList<int> reports, DipSetting setting)
    {
        var inside = reports.Where(sessions => sessions > -setting.DropSessions && sessions <= setting.TimeLimit).Order().First();

        return inside switch
        {
            0 => "a report on the night",
            < 0 => FamilyRule.Invariant($"a report {-inside} session{(inside == -1 ? string.Empty : "s")} back, inside the drop"),
            _ => FamilyRule.Invariant($"a report due in {inside} session{(inside == 1 ? string.Empty : "s")}, before its time limit ends"),
        };
    }

    // The members passing in the family's order: the lowest 2-session RSI first, then the ticker.
    public static IReadOnlyList<DipAnswer> Ranked(IEnumerable<DipAnswer> answers) =>
    [
        .. answers
            .Where(answer => answer.Passed)
            .OrderBy(answer => answer.Rsi)
            .ThenBy(answer => answer.Ticker, StringComparer.Ordinal),
    ];

    // A trade's path from its listing session over the sessions after it, oldest first: bought at the first one's open;
    // then on each session's close, a close under the stop or above the exit's average sells at the next session's open,
    // and the last session its time limit holds sells at its close where neither came on it. A trigger on the last
    // session's close still sells at the next open, since the rule sells on its trigger. The path waits where a session
    // it needs is not stored: none bought before the first, and held where the sale's next session is not stored.
    // see: The dip buys at the next session's open and sells at the next open after its exit's close or its stop's, and at its last session's close
    public static DipPath Walk(double listingClose, double stop, int timeLimit, IReadOnlyList<DipSession> after)
    {
        if (after.Count == 0 || !(after[0].Open > 0))
        {
            return new DipPath(null, null, null, null, DipEnd.Open, 0, null);
        }

        var risk = listingClose - stop;
        var bought = after[0];

        for (var held = 1; held <= timeLimit; held++)
        {
            if (held > after.Count)
            {
                return new DipPath(bought.Session, bought.Open, null, null, DipEnd.Open, after.Count, null);
            }

            var session = after[held - 1];
            var stopped = session.Close < stop;
            var exited = !stopped && (session.Trigger ?? (session.Average is { } average && session.Close > average));

            if (stopped || exited)
            {
                if (held >= after.Count || !(after[held].Open > 0))
                {
                    return new DipPath(bought.Session, bought.Open, null, null, DipEnd.Open, held, null);
                }

                var sale = after[held].Open;

                return new DipPath(bought.Session, bought.Open, after[held].Session, sale, stopped ? DipEnd.Stop : DipEnd.Exit, held, risk > 0 ? (sale - listingClose) / risk : null);
            }

            if (held == timeLimit)
            {
                return new DipPath(bought.Session, bought.Open, session.Session, session.Close, DipEnd.Limit, held, risk > 0 ? (session.Close - listingClose) / risk : null);
            }
        }

        return new DipPath(bought.Session, bought.Open, null, null, DipEnd.Open, after.Count, null);
    }

    // Each session after a listing as the walk reads it, the exit's average over the closes to it, the listing's and
    // the closes before it among them; none from the first session of the exchange's calendar the stock's bars skip,
    // so a trade waits for a missing session where it would otherwise read past it.
    public static IReadOnlyList<DipSession> Sessions(IReadOnlyList<(DateOnly Session, double Open, double Close)> bars, int listing, int exitAverage, IReadOnlyList<DateOnly> calendar)
    {
        var closes = bars.Select(bar => bar.Close).ToArray();
        var sessions = new List<DipSession>();

        for (var at = listing + 1; at < bars.Count; at++)
        {
            if (Skipped(calendar, bars[at - 1].Session, bars[at].Session))
            {
                break;
            }

            sessions.Add(new DipSession(bars[at].Session, bars[at].Open, bars[at].Close, Average(closes, at, exitAverage)));
        }

        return sessions;
    }

    // Whether the calendar holds a session strictly between two of a stock's stored sessions, one its bars skip.
    static bool Skipped(IReadOnlyList<DateOnly> calendar, DateOnly from, DateOnly to)
    {
        var low = 0;
        var high = calendar.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (calendar[middle] <= from)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < calendar.Count && calendar[low] < to;
    }
}
