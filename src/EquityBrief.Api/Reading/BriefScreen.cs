using System.Globalization;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// One setup's standing tonight on a stock's index as the night's stored answers give it: on the list under it, passed
// and held off the list, one step short naming the step, not close, no recent report, or for the sector heavyweights
// held or not held in its book; and not read where the night stored no answer for the stock.
public sealed record BriefStanding(string Family, string State, string? Step = null, int Missed = 0);

// The states a setup's chip names.
public static class BriefStates
{
    public const string Listed = "listed";
    public const string Passed = "passed";
    public const string Short = "short";
    public const string NotClose = "not-close";
    public const string NoReport = "no-report";
    public const string Held = "held";
    public const string NotHeld = "not-held";
    public const string NotRead = "not-read";
}

// The rule listing the stock tonight: its family, the decision card the night stored for the pick, none where it stored
// none, and for the dip the sessions its exit's average of closes reads.
public sealed record BriefListed(string Family, DecisionCardRow? Card, int? DipExitLevel = null);

// The answers the night stored that a stock's standing under each setup is read from: on the S&P 500 the swing filter's
// row and the family evaluator's results with the night's picks; on every index the index families' results and picks,
// on the S&P 500 the fundamentals-first family's alone; the dip's results and picks; and whether the heavyweights' book
// holds the stock, none where the night kept no book.
public sealed record BriefAnswers(
    string Ticker,
    bool Large,
    GateResultRow? Gate,
    IReadOnlyList<FamilyResultRow> FamilyResults,
    IReadOnlyList<FamilyPickRow> FamilyPicks,
    IReadOnlyList<IndexResultRow> IndexResults,
    IReadOnlyList<FamilyPickRow> IndexPicks,
    IReadOnlyList<IndexResultRow> DipResults,
    IReadOnlyList<DipPickRow> DipPicks,
    bool? Held);

// What a stock's brief read is written from, each read as the full page or the decision card reads it: the stock's
// membership row with its trend and nearest bands, the newest value of each indicator series, its reported quarters'
// readings, its six-month return's place among its index's members, the analysts' newest reading, the rule listing it
// tonight, the night's levels plan, the account, each setup's standing, its next report, its stored report moves, its
// insiders' open-market trades, and the reward to risk the trade step read on the levels plan's first tranche.
public sealed record BriefInputs(
    string Ticker,
    string? IndexName,
    DateOnly? Night,
    UniverseRow? Member,
    IReadOnlyDictionary<string, double?> Latest,
    Readings? Business,
    double? HalfYear,
    AnalystReading? Analysts,
    BriefListed? Listed,
    IReadOnlyList<PlanRow> Levels,
    bool LadderWorked,
    AccountSettings? Account,
    IReadOnlyList<BriefStanding> Standing,
    DateOnly? NextReport,
    IReadOnlyList<double> ReportMoves,
    EquityBrief.Core.Insiders.InsiderView? Insiders,
    double? LevelsRewardToRisk = null,
    FundamentalScoreRow? Score = null);

// A stock's brief read, written by code from templates over the figures the full page and the decision card already
// work: each through the same core function, none worked out a second way, and a figure not stored said so in its line.
// see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
public static class BriefScreen
{
    public static BriefView Of(BriefInputs inputs)
    {
        var member = inputs.Member;
        var close = member?.Close;
        var plan = PlanOf(inputs);

        return new BriefView(
            Summary(inputs),
            [
                new BriefPanel("chart", "The chart says", [Trend(member), Bands(member), Momentum(inputs.Latest), HalfYear(inputs)]),
                new BriefPanel("business", "The business says", [FundamentalsScreen.Line(inputs.Score), Sales(inputs.Business), Margin(inputs.Business), Multiple(inputs.Business), Analysts(inputs.Analysts)]),
                new BriefPanel("way-in", "If you want in", WayIn(inputs, plan)),
            ],
            [.. inputs.Standing.Select(Chip)],
            WatchFor(inputs, plan, close));
    }

    // Each setup's standing on the stock's index tonight in the page's order, read off the answer the night stored for the
    // stock by the function its card reads it with: on the list from the night's picks; one step short as the card's table
    // of the stocks one gate short counts it; no recent report where the S&P 500 drift's answer stops at its report;
    // held where the heavyweights' book holds it; and not read where the night stored no answer for the stock.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public static IReadOnlyList<BriefStanding> Standing(BriefAnswers answers)
    {
        var ticker = answers.Ticker;
        var standing = new List<BriefStanding>();

        bool OnTheList(IReadOnlyList<FamilyPickRow> picks, string family) =>
            picks.Any(pick => pick.Ticker == ticker && pick.Family == family && pick.State == FamilyList.Listed);

        if (answers.Large)
        {
            standing.Add(answers.Gate is not { } gate
                ? new BriefStanding(SetupFamilies.Pullback, BriefStates.NotRead)
                : OnTheList(answers.FamilyPicks, SetupFamilies.Pullback) ? new BriefStanding(SetupFamilies.Pullback, BriefStates.Listed)
                : gate.Passed ? new BriefStanding(SetupFamilies.Pullback, BriefStates.Passed)
                : CloseScreen.MissedOne(gate) ? new BriefStanding(SetupFamilies.Pullback, BriefStates.Short, TonightScreen.FilterRowOf(gate).Gates.First(one => !one.Passed).Name, 1)
                : new BriefStanding(SetupFamilies.Pullback, BriefStates.NotClose));

            foreach (var family in new[] { BreakoutRule.Name, DriftRule.Name })
            {
                var result = answers.FamilyResults.FirstOrDefault(one => one.Ticker == ticker && one.Family == family);
                var missed = result is null ? null : FamilyRule.GatesOf(result.Gates).FirstOrDefault(one => !one.Passed);

                standing.Add(result is null
                    ? new BriefStanding(family, BriefStates.NotRead)
                    : OnTheList(answers.FamilyPicks, family) ? new BriefStanding(family, BriefStates.Listed)
                    : result.Passed ? new BriefStanding(family, BriefStates.Passed)
                    : result.Missed == 1 && result.Exclusions.Count == 0 && missed is not null ? new BriefStanding(family, BriefStates.Short, missed.Name, 1)
                    : family == DriftRule.Name && missed?.Name == DriftRule.Print ? new BriefStanding(family, BriefStates.NoReport, Missed: result.Missed)
                    : new BriefStanding(family, BriefStates.NotClose, Missed: result.Missed));
            }
        }

        IReadOnlyList<SetupFamily> indexFamilies = answers.Large ? [SetupFamilies.FundamentalsFirst] : SetupFamilies.OnEveryIndex;

        foreach (var family in indexFamilies.Select(one => one.Name))
        {
            var result = answers.IndexResults.FirstOrDefault(one => one.Ticker == ticker && one.Family == family);

            standing.Add(result is null
                ? new BriefStanding(family, BriefStates.NotRead)
                : OnTheList(answers.IndexPicks, family) ? new BriefStanding(family, BriefStates.Listed)
                : result.Passed ? new BriefStanding(family, BriefStates.Passed)
                : TonightScreen.IndexCloseAcross([result], [])?.FirstOrDefault() is { } close ? new BriefStanding(family, BriefStates.Short, close.Gate, 1)
                : new BriefStanding(family, BriefStates.NotClose));
        }

        var dip = answers.DipResults.FirstOrDefault(one => one.Ticker == ticker);

        standing.Add(dip is null
            ? new BriefStanding(DipRule.Name, BriefStates.NotRead)
            : answers.DipPicks.Any(pick => pick.Ticker == ticker && pick.State == FamilyList.Listed) ? new BriefStanding(DipRule.Name, BriefStates.Listed)
            : dip.Passed ? new BriefStanding(DipRule.Name, BriefStates.Passed)
            : TonightScreen.DipCloseAcross([dip], []).FirstOrDefault() is { } near ? new BriefStanding(DipRule.Name, BriefStates.Short, near.Gate, 1)
            : new BriefStanding(DipRule.Name, BriefStates.NotClose));

        standing.Add(new BriefStanding(
            SetupFamilies.SectorHeavyweights.Name,
            answers.Held switch
            {
                true => BriefStates.Held,
                false => BriefStates.NotHeld,
                null => BriefStates.NotRead,
            }));

        return standing;
    }

    // ---- the summary line ----

    static string Summary(BriefInputs inputs)
    {
        var member = inputs.Member;
        var trend = member?.TrendState switch
        {
            TrendState.Uptrend => "is in an uptrend",
            TrendState.Downtrend => "is in a downtrend",
            TrendState.Range => "is moving sideways",
            TrendState.NotClassified => "has a trend its stored bars cannot classify",
            _ => "has no trend read for the night",
        };

        return $"{inputs.Ticker} {trend}, {BandsPhrase(member)}. {BusinessPhrase(inputs.Business)}. {StandingPhrase(inputs.Standing)}.";
    }

    static string BandsPhrase(UniverseRow? member)
    {
        if (member?.Close is not { } close || member.TypicalMove is not > 0)
        {
            return "with no close and typical day's move to read its bands against";
        }

        var support = Inside(close, member.SupportFloor, member.NearestSupport)
            ? "inside its support band"
            : Distances.InTypicalDays(close, member.NearestSupport, member.TypicalMove) is { } below
                ? Away(below, "above its nearest support", "at its nearest support")
                : "with no support band below it";
        var resistance = Inside(close, member.NearestResistance, member.ResistanceCeiling)
            ? "inside its resistance band"
            : Distances.InTypicalDays(close, member.NearestResistance, member.TypicalMove) is { } above
                ? Away(above, "below its nearest resistance", "at its nearest resistance")
                : "with no resistance band above it";

        return support + " and " + resistance;
    }

    static string BusinessPhrase(Readings? business) => business?.State switch
    {
        null => "No reading of its reported quarters is stored for the night",
        FundamentalState.NotEnoughQuarters => "Too few of its reported quarters are stored to read its business",
        FundamentalState.NoFundamentalsYet => "No reported quarter is stored for it yet",
        { } state => $"Its business reads {state}, from sales and operating margin",
    };

    // The rules' standing tonight: the setup listing it, or the one holding it, or the one it is fewest steps short of in
    // the page's order, or that none is close.
    static string StandingPhrase(IReadOnlyList<BriefStanding> standing)
    {
        if (standing.FirstOrDefault(one => one.State is BriefStates.Listed) is { } listed)
        {
            return $"Tonight {Label(listed.Family)} picked it";
        }

        if (standing.FirstOrDefault(one => one.State is BriefStates.Held) is { } held)
        {
            return $"Tonight it is in the {Label(held.Family)} setup's holdings";
        }

        if (standing.FirstOrDefault(one => one.State is BriefStates.Passed) is { } passed)
        {
            return $"Tonight it qualified for {Label(passed.Family)} but is not among tonight's picks";
        }

        return standing.Where(one => one.State is BriefStates.Short).OrderBy(one => one.Missed).ThenBy(one => SetupFamilies.OrderOf(one.Family)).FirstOrDefault() is { } closest
            ? $"Tonight the closest setup is {Label(closest.Family)}, {StepShort(closest.Family, closest.Step)}"
            : standing.Count == 0 || standing.All(one => one.State is BriefStates.NotRead)
                ? "No setup's answer for it is stored tonight"
                : "Tonight no setup is one step from it";
    }

    // ---- the chart panel ----

    static BriefLine Trend(UniverseRow? member) => member?.TrendState switch
    {
        TrendState.Uptrend => new BriefLine("trend", "Trend: an uptrend.", member.TrendState),
        TrendState.Downtrend => new BriefLine("trend", "Trend: a downtrend.", member.TrendState),
        TrendState.Range => new BriefLine("trend", "Trend: moving sideways.", member.TrendState),
        TrendState.NotClassified => new BriefLine("trend", "Trend: its stored bars cannot classify it.", member.TrendState),
        _ => new BriefLine("trend", "Trend: not read for the night."),
    };

    static BriefLine Bands(UniverseRow? member)
    {
        if (member?.Close is not { } close)
        {
            return new BriefLine("bands", "Its bands: no close is stored to read them against.");
        }

        var support = member.NearestSupport is { } edge
            ? member.SupportFloor is { } floor && Inside(close, floor, edge)
                ? "inside its support band at " + Zone(floor, edge)
                : Distances.InTypicalDays(close, edge, member.TypicalMove) is { } below
                    ? Invariant($"support at {Price(edge)}, ") + Away(below, "below", "at the price")
                    : Invariant($"support at {Price(edge)}, its distance not read")
            : "no support band below";
        var resistance = member.NearestResistance is { } top
            ? member.ResistanceCeiling is { } ceiling && Inside(close, top, ceiling)
                ? "inside its resistance band at " + Zone(top, ceiling)
                : Distances.InTypicalDays(close, top, member.TypicalMove) is { } above
                    ? Invariant($"resistance at {Price(top)}, ") + Away(above, "above", "at the price")
                    : Invariant($"resistance at {Price(top)}, its distance not read")
            : "no resistance band above";

        return new BriefLine("bands", Capitalised(support) + "; " + resistance + ".");
    }

    // Momentum in words, each reading against the middle the core states for it: the 14-session relative strength
    // against 50 and the trend momentum line against zero.
    static BriefLine Momentum(IReadOnlyDictionary<string, double?> latest)
    {
        var rsi = latest.GetValueOrDefault(IndicatorSeries.Rsi14);
        var trend = latest.GetValueOrDefault(IndicatorSeries.MacdHist);

        if (rsi is not { } strength || trend is not { } line)
        {
            return new BriefLine("momentum", "Momentum: not read for the night.");
        }

        var rsiUp = strength > IndicatorSeries.NeutralOf(IndicatorSeries.Rsi14);
        var lineUp = line > IndicatorSeries.NeutralOf(IndicatorSeries.MacdHist);
        var words = rsiUp && lineUp ? "rising" : !rsiUp && !lineUp ? "falling" : "mixed";

        return new BriefLine(
            "momentum",
            Invariant($"Momentum {words}: relative strength {strength:0.0} {(rsiUp ? "above" : "at or below")} its middle of 50, the trend momentum line {(lineUp ? "above" : "at or below")} zero."),
            strength.ToString(CultureInfo.InvariantCulture));
    }

    static BriefLine HalfYear(BriefInputs inputs) =>
        inputs.HalfYear is { } share
            ? new BriefLine("strength", $"Six months: its return ranks at the {Ordinal((int)Math.Round(share * 100, MidpointRounding.AwayFromZero))} percentile of the {inputs.IndexName ?? "index"}'s stocks.", share.ToString(CultureInfo.InvariantCulture))
            : new BriefLine("strength", "Six months: its return against the index's stocks is not read.");

    // ---- the business panel ----

    static BriefLine Sales(Readings? business)
    {
        if (business?.Trajectory is not { Absent: null, Quarters.Count: >= QuarterReadings.TrajectoryQuarters } trajectory)
        {
            return new BriefLine("sales", "Sales: too few quarters are stored to read them against a year earlier.");
        }

        var (newer, older) = (trajectory.Quarters[0], trajectory.Quarters[1]);
        var pace = newer.SalesGrowth > older.SalesGrowth ? "faster than" : newer.SalesGrowth < older.SalesGrowth ? "slower than" : "the same as";

        return new BriefLine(
            "sales",
            Invariant($"Sales {Change(newer.SalesGrowth)} on a year in the quarter to {newer.Quarter:yyyy-MM-dd}, {pace} the {Signed(older.SalesGrowth)} of the quarter before."),
            newer.SalesGrowth.ToString(CultureInfo.InvariantCulture));
    }

    static BriefLine Margin(Readings? business)
    {
        if (business?.Trajectory is not { Absent: null, Quarters.Count: >= QuarterReadings.TrajectoryQuarters } trajectory)
        {
            return new BriefLine("margin", "Operating margin: too few quarters are stored to read it against a year earlier.");
        }

        var newer = trajectory.Quarters[0];
        var side = newer.Margin > newer.MarginYearEarlier ? "wider than" : newer.Margin < newer.MarginYearEarlier ? "narrower than" : "the same as";

        return new BriefLine(
            "margin",
            Invariant($"Operating margin {Percent(newer.Margin)}, {side} the {Percent(newer.MarginYearEarlier)} of a year earlier."),
            newer.Margin.ToString(CultureInfo.InvariantCulture));
    }

    static BriefLine Multiple(Readings? business)
    {
        if (business?.Valuation is not { Multiple: { } multiple, Low: { } low, High: { } high, Position: { } position })
        {
            return new BriefLine("multiple", business?.Valuation.Absent is { } absent
                ? $"Earnings multiple: not read, {absent}."
                : "Earnings multiple: not read for the night.");
        }

        return new BriefLine(
            "multiple",
            Invariant($"It trades at {multiple:0.0} times its last four quarters' earnings, {position} of its own range of {low:0.0} to {high:0.0}."),
            multiple.ToString(CultureInfo.InvariantCulture));
    }

    // The analysts' figures each labelled as theirs and dated by the fetch that kept them.
    // see: The name page draws the analysts' figures each labelled as theirs and dated by its fetch, and no written sentence states one
    static BriefLine Analysts(AnalystReading? analysts)
    {
        if (analysts?.Target is not { } target || analysts.TargetAgainstPrice is not { } against)
        {
            return new BriefLine("analysts", "The analysts' mean target: not stored.");
        }

        var year = analysts.Revisions.FirstOrDefault(row => row.Period == EstimatePeriod.CurrentYear);
        var moved = year is { Up30: { } up, Down30: { } down }
            ? Invariant($"; over 30 days {up} raised and {down} cut their estimate for the year")
            : "; which way their estimates moved over 30 days is not stored";

        return new BriefLine(
            "analysts",
            Invariant($"The analysts' mean target, as fetched on {analysts.FetchedOn:yyyy-MM-dd}: {Price(target)}, {Math.Abs(against):0.0}% {(against >= 0 ? "above" : "below")} the price{moved}."),
            target.ToString(CultureInfo.InvariantCulture));
    }

    // ---- the way in ----

    // The plan the way in reads: the listing rule's buy and stop, or the levels plan's first tranche; none where neither
    // is stored.
    sealed record WayInPlan(decimal Buy, decimal? Stop, int? Cap, int? BookHoldings, decimal? RoundTrip);

    static WayInPlan? PlanOf(BriefInputs inputs)
    {
        if (inputs.Listed is { } listed)
        {
            return listed.Card is { Entry: { } entry } card ? new WayInPlan(entry, card.Stop, card.Cap, card.BookHoldings, card.RoundTrip) : null;
        }

        return inputs.Levels.FirstOrDefault(row => row.Kind == PlanKind.Tranche && row.Traded) is { } tranche
            ? new WayInPlan(tranche.HighEdge, tranche.Stop, null, null, null)
            : null;
    }

    static IReadOnlyList<BriefLine> WayIn(BriefInputs inputs, WayInPlan? plan)
    {
        if (inputs.Listed is { Card: null } uncarded)
        {
            return
            [
                new BriefLine("buy", $"Picked tonight by {Label(uncarded.Family)}."),
                new BriefLine("stop", "Its pick's card is not stored for the night, so its stop is not read here."),
                new BriefLine("exit", "Its target or exit is not read here either."),
                new BriefLine("size", "No plan is stored to size."),
            ];
        }

        if (inputs.Listed is { Card: { Entry: { } entry } card } listed)
        {
            var steps = listed.Family == DipRule.Name
                ? Management.ForDip(card.Stop, listed.DipExitLevel ?? DipRule.Provisional.ExitLevel, card.Cap)
                : Management.For(card.Stop, card.Target, card.Trail, card.Cap, listed.Family == HeavyweightRule.Name);

            return
            [
                new BriefLine("buy", Invariant($"Picked tonight by {Label(listed.Family)}: buy at the next open, near tonight's close of {Price(entry)}."), entry.ToString(CultureInfo.InvariantCulture)),
                new BriefLine("stop", steps.Count > 0 ? steps[0] : "No stop is stored."),
                new BriefLine("exit", steps.Count > 1 ? string.Join(" ", steps.Skip(1)) : "No target or exit is stored."),
                Size(inputs.Account, plan),
            ];
        }

        if (!inputs.LadderWorked)
        {
            return
            [
                new BriefLine("buy", "Not among tonight's picks, and " + NameScreen.PlanForTheLargeIndexAlone + "."),
                new BriefLine("stop", "No stop is stored."),
                new BriefLine("exit", "Its bands stand on its full page."),
                new BriefLine("size", "No plan is stored to size."),
            ];
        }

        var tranche = inputs.Levels.FirstOrDefault(row => row.Kind == PlanKind.Tranche && row.Traded);
        var sale = inputs.Levels.FirstOrDefault(row => row.Kind == PlanKind.Exit && row.Traded);

        if (tranche is null)
        {
            return
            [
                new BriefLine("buy", "Not among tonight's picks, and no levels plan is stored for the night."),
                new BriefLine("stop", "No stop is stored."),
                new BriefLine("exit", "No first sale is stored."),
                new BriefLine("size", "No plan is stored to size."),
            ];
        }

        return
        [
            new BriefLine("buy", "Not among tonight's picks. The night's levels plan buys in its first zone at " + Zone(tranche.LowEdge, tranche.HighEdge) + ".", tranche.HighEdge.ToString(CultureInfo.InvariantCulture)),
            new BriefLine("stop", tranche.Stop is { } stop ? Invariant($"Its stop: a daily close below {Price(stop)}.") : "Its first zone has no stop beneath it."),
            new BriefLine("exit", sale is null
                ? "No first sale is stored."
                : "Its first sale at " + Zone(sale.LowEdge, sale.HighEdge) + (inputs.LevelsRewardToRisk is { } ratio ? Invariant($": risk $1 to make ${ratio:0.00}.") : ".")),
            Size(inputs.Account, plan),
        ];
    }

    // The size through the decision card's own function, or the line to the settings where the account is not set.
    static BriefLine Size(AccountSettings? account, WayInPlan? plan)
    {
        if (account is null)
        {
            return new BriefLine("size", "Your account is not set:", Href: MarkRenderer.AccountRoute, LinkWords: "set your account's size and risk", After: " to see a size.");
        }

        if (plan is null || PositionSize.Of(account, plan.Buy, plan.Stop, plan.BookHoldings, plan.RoundTrip) is not { } size)
        {
            return new BriefLine("size", "No size: the plan states no stop under its buy.");
        }

        return new BriefLine(
            "size",
            Invariant($"Your size: {size.Shares:N0} shares, {size.Value.ToString("N2", CultureInfo.InvariantCulture)}, {size.ShareOfAccount:0.0%} of the account") + (size.WholeAtRisk
                ? ", the whole position at risk."
                : Invariant($", {size.AtRisk.ToString("N2", CultureInfo.InvariantCulture)} at risk to the stop.")),
            size.Shares.ToString(CultureInfo.InvariantCulture));
    }

    // ---- the chips ----

    static BriefChip Chip(BriefStanding standing) => new(
        standing.Family,
        Label(standing.Family),
        standing.State,
        standing.State switch
        {
            BriefStates.Listed => "picked tonight",
            BriefStates.Passed => "qualified, not among tonight's picks",
            BriefStates.Short => StepShort(standing.Family, standing.Step),
            BriefStates.NotClose => "not close",
            BriefStates.NoReport => "no recent report",
            BriefStates.Held => "in its holdings",
            BriefStates.NotHeld => "not in its holdings",
            _ => "not read tonight",
        });

    // ---- what to watch for ----

    // A report inside a typical hold, the hold the decision card reads; report moves past the first stop's distance by
    // the card's own reading of them; and insiders' open-market selling over 90 days. None where none applies.
    static string? WatchFor(BriefInputs inputs, WayInPlan? plan, decimal? close)
    {
        var said = new List<string>();

        if (inputs.Night is { } night && inputs.NextReport is { } report && report > night && report <= CardHitsReading.HoldThrough(night, plan?.Cap))
        {
            said.Add(Invariant($"its report on {report:yyyy-MM-dd} falls inside a typical hold"));
        }

        if (plan is { Stop: { } stop } && CardHitsReading.Reactions(inputs.ReportMoves, close, inputs.Member?.TypicalMove, plan.Buy, stop) is { MedianRisks: > 1 } moves)
        {
            said.Add(Invariant($"its reports have moved it a median {moves.MedianRisks:0.0} times the first stop's distance"));
        }

        if (inputs.Insiders?.Ninety is { Sold: > 0 } sold)
        {
            said.Add($"insiders sold {Figures.Money(sold.Sold)} in the open market over 90 days");
        }

        return said.Count == 0 ? null : "Watch for: " + string.Join("; ", said) + ".";
    }

    // ---- words ----

    static string Label(string family) =>
        SetupFamilies.Named(family)?.Label
        ?? (family == SetupFamilies.SectorHeavyweights.Name ? SetupFamilies.SectorHeavyweights.Label : family);

    // One step short, naming the step missed in the reader's words where the word list holds it.
    static string StepShort(string family, string? step) => ReaderWords.Missed(family, step) is { } words ? "one step short: " + words : "one step short";

    static bool Inside(decimal close, decimal? low, decimal? high) => low is { } bottom && high is { } top && close >= bottom && close <= top;

    // A distance in typical days' moves with the side it lies on, or the words for one that rounds to none.
    static string Away(double days, string side, string at) =>
        Math.Round(days, 1) == 0 ? at : Invariant($"{days:0.0} typical {(Math.Round(days, 1) == 1 ? "day's move" : "days' moves")} {side}");

    static string Zone(decimal low, decimal high) => low == high ? Price(low) : Price(low) + " to " + Price(high);

    static string Ordinal(int number) =>
        number.ToString(CultureInfo.InvariantCulture) + ((number % 100) is 11 or 12 or 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    static string Price(decimal? price) => price is { } held ? Figures.Price(held) : "none";

    static string Percent(decimal share) => (share * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    static string Signed(decimal share) => (share >= 0m ? "+" : string.Empty) + Percent(share);

    static string Change(decimal growth) => growth > 0m ? "up " + Percent(growth) : growth < 0m ? "down " + Percent(-growth) : "level";

    static string Capitalised(string words) => char.ToUpperInvariant(words[0]) + words[1..];

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
