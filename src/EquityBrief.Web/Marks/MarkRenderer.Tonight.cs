using System.Text;

namespace EquityBrief.Web.Marks;

// The trades from earlier nights that ended on the night, by why where the store says why: at their target, at their stop
// or at their time limit, and those whose ending the store keeps no reason for.
public sealed record EndedTonightView(int AtTarget, int AtStop, int AtLimit, int Other)
{
    public int Count => AtTarget + AtStop + AtLimit + Other;

    // From the operator's order of 2026-11-14 the stocks behind each count, in the page's order, which the line names.
    public IReadOnlyList<string> TargetTickers { get; init; } = [];

    public IReadOnlyList<string> StopTickers { get; init; } = [];

    public IReadOnlyList<string> LimitTickers { get; init; } = [];

    public IReadOnlyList<string> OtherTickers { get; init; } = [];
}

// What Tonight in one minute reads: the market line, tonight's picks by setup, the trades from earlier nights still open
// with those that ended on the night, and the stocks one step from qualifying with the link opening them.
public sealed record OneMinuteView(MarketLineView Line, IReadOnlyList<SetupTileView> Tiles, EndedTonightView? Ended, string? PicksHref, DateOnly? Night = null, TradeGroupsView? Groups = null, int? Fundamentals = null)
{
    // From the operator's order of 2026-11-14 the stocks a setup's rules met again tonight while a trade holds them, each with
    // the setup that matched.
    public IReadOnlyList<(string Ticker, string Family)> HeldAgain { get; init; } = [];
}

// One setup in the reader's words: the line its tile states what it looks for in, and the two sentences its card's header
// describes it with.
public sealed record SetupWordsView(string Plain, string About);

// Tonight, redesigned from 22.6: every card opens on one sentence built from tonight's numbers saying what it shows, every
// count is a sentence, detail sits behind folds, and the words are the reader's, as section 15.23 lists them.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    // Each setup in the reader's words, by its family's word.
    public static IReadOnlyDictionary<string, SetupWordsView> SetupWords { get; } = new Dictionary<string, SetupWordsView>(StringComparer.Ordinal)
    {
        [EquityBrief.Core.Families.SetupFamilies.Pullback] = new(
            "A strong stock dips to a price where it has bounced before.",
            "A strong stock in an uptrend dips to a price where it has bounced before, and turns back up. It is sold at its target, at its stop or when its time runs out."),
        [EquityBrief.Core.Families.BreakoutRule.Name] = new(
            "A stock breaks above its recent high on heavy volume.",
            "A stock closes above its highest price of recent months on heavy volume, after a quiet spell. It is sold on a stop that rises as the price climbs, with no fixed target."),
        [EquityBrief.Core.Families.DriftRule.Name] = new(
            "A stock jumps after beating earnings; bought to ride the follow-through.",
            "Stocks that just beat their earnings estimate and jumped on heavy volume, bought to ride the follow-through. Prices often keep drifting the same way for weeks after a strong report."),
        [EquityBrief.Core.Families.FundamentalsRule.Name] = new(
            "A growing, profitable company dips in an uptrend.",
            "A growing, profitable company whose cash backs its earnings, bought when it dips in an uptrend. It buys at the pullback's buy point."),
        [EquityBrief.Core.Families.ImprovingRule.Name] = new(
            EquityBrief.Core.Families.ImprovingRule.Looks,
            EquityBrief.Core.Families.ImprovingRule.Description),
        [EquityBrief.Core.Families.DipRule.Name] = new(
            "A rising stock falls hard for a few days; bought for the bounce.",
            "A stock in a long uptrend falls hard for a few days with no earnings news, bought for the bounce. It is sold within days, once it closes back above its short average."),
        [EquityBrief.Core.Families.HeavyweightRule.Name] = new(
            "Holds the biggest companies beating their sector; reshuffled monthly.",
            "Holds the biggest companies that are beating their own sector over the past year. It is reshuffled on the first trading day of each month, and a stock is sold when it stops leading. " + BookExitSaid),
        [EquityBrief.Core.Families.QualityRule.Name] = new(
            "Holds companies with strong fundamentals at a fair price; reshuffled monthly.",
            "Holds the companies whose filings score strongest on nine checks and whose price over earnings is no higher than its own usual. It is reshuffled on the first trading day of each month, and a stock is sold when it no longer qualifies. " + BookExitSaid),
    };

    // Why a pick is here as a row draws it: the night's stored words in the reader's words, or from 22.17 on the read-only
    // copy its setup's plain sentence, which carries no reading, window or threshold.
    // see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
    string WhyWords(string family, string why) =>
        Copying ? WordsOf(family, family).Plain : ReaderWords.Plain(StoredWords(why));

    // A setup's words, its own label's where the list holds none for it.
    public static SetupWordsView WordsOf(string family, string label) =>
        SetupWords.TryGetValue(family, out var words) ? words : new SetupWordsView(label + ".", label + ".");

    // The session whose open a night's picks are bought at, in the reader's words: tomorrow's where the exchange trades the
    // day after the night, that session's weekday where a weekend or a closure comes between, and the next trading day's
    // where the closure table cannot place a day, rather than a guess.
    // see: Tonight names the session its picks are bought at, tomorrow only where the exchange trades the next day
    public static string OpenDay(DateOnly? night)
    {
        if (night is not { } after)
        {
            return "the next trading day's";
        }

        var day = after.AddDays(1);

        try
        {
            while (!EquityBrief.Core.Bars.ExchangeClosures.IsSession(day))
            {
                day = day.AddDays(1);
            }
        }
        catch (InvalidOperationException)
        {
            return "the next trading day's";
        }

        return day == after.AddDays(1) ? "tomorrow's" : day.DayOfWeek + "'s";
    }

    // A count as a sentence says it, one noun and its plural.
    static string Counted(int count, string one, string many) => Formatted($"{count} {(count == 1 ? one : many)}");

    // A whole number as a place in a run, 1st, 2nd, 3rd, 4th and on, 11th to 13th among them.
    static string Ordinal(int number) =>
        number.ToString(Invariant) + ((number % 100) is 11 or 12 or 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    // Dollars made for every $100 risked, from an average result in multiples of the risk, signed.
    static string PerHundred(double average) =>
        (average >= 0 ? "$" : "-$") + Math.Abs(average * 100).ToString("0.00", Invariant);

    // The record of a setup's rule over every stock it bought, as a pick's card read it, in sentences: a record in per
    // cent of the buy, the sector heavyweights', said as each holding's average return.
    // see: A rule's record is drawn under the rule's own heading, beside its reason or on a pick's card and never as the stock's own
    static string RecordSentence(CardRecordView record, string setup)
    {
        var said = FormattableString.Invariant($"From {record.From:yyyy} to {record.Through:yyyy} the {setup.ToLowerInvariant()} made {record.Trades:#,0} {(record.Trades == 1 ? "trade" : "trades")}");

        said += record.Won is { } won ? Formatted($"; {won * 100:0}% reached their target") : string.Empty;
        said += record.Average is not { } average
            ? string.Empty
            : record.Unit == "percent"
                ? Formatted($", and on average each returned {average.ToString("+0.0;-0.0;0.0", Invariant)}% of its buy, after costs")
                : Formatted($", and on average it made about {PerHundred(average)} for every $100 risked, after costs");
        said += ".";
        said += record.MedianSessions is { } held ? Formatted($" A trade was usually over in {held} trading {(held == 1 ? "day" : "days")}.") : string.Empty;

        return Formatted($"<p class=\"record-said\" data-trades=\"{record.Trades}\" data-won=\"{(record.Won is { } share ? share.ToString("R", Invariant) : "none")}\" data-average=\"{(record.Average is { } mean ? mean.ToString("R", Invariant) : "none")}\">{WithSignedFigures(said)}</p>");
    }

    // What a fixed card's search stated over the history its settings were tested on, in one sentence.
    static string FrozenRecordSaid(FrozenSweepView frozen)
    {
        var figures = frozen.Figures;
        var years = figures.YearEdges.Count;
        var above = EquityBrief.Core.Sweep.SweepLine.YearsAbove(figures);

        return frozen.Points
            ? Formatted($"Over the history its settings were tested on, its {figures.Trades:#,0} holdings came to {(figures.Edge is { } points ? (points * 100).ToString("+0.00;-0.00;0.00", Invariant) + " percentage points against the same companies by size" : "no stated result")}, ahead in {above} of the {years} years.")
            : Formatted($"Over the history its settings were tested on, its {figures.Trades:#,0} trades made about {(figures.Edge is { } risks ? PerHundred(risks) : "nothing stated")} for every $100 risked beyond the same plan on every stock, after costs, and came out ahead in {above} of the {years} years.");
    }

    // A share as a whole per cent with one place, as the page reads it.
    static string Percent(double share) => (share * 100).ToString("0.#", Invariant) + "%";

    // Tonight in one minute: four lines, each a bold sentence from tonight's numbers with one line beneath. The first,
    // the market, is the card's summary: whether the market check left the setups open, the share of the index above its
    // 200-day average against the floor with why the check exists and a bar marking the floor, on an S&P 400 or 600 night
    // each setup's own check, and the short-term dip's own setting named. Then tonight's picks and the setups they came
    // from, the trades from earlier nights still open with those that ended tonight, and the stocks one step from
    // qualifying, linking to them. The market line keeps every figure it states whole on its element.
    public string OneMinute(OneMinuteView view)
    {
        var line = view.Line;
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"one-minute\" data-picks=\"{line.BuyPoints}\" data-open-trades=\"{line.OpenTrades}\" data-ended=\"{(view.Ended is { } ended ? ended.Count.ToString(Invariant) : "none")}\" data-one-step=\"{(line.CloseRead ? line.Close.ToString(Invariant) : "none")}\">");

        // The market, the card's summary.
        body.Append("<div class=\"minute-line\" data-line=\"market\" data-summary>");
        body.Append(MarketLine(line));
        body.Append(MarketBar(line));
        body.Append("</div>");

        // Tonight's picks and the setups they came from.
        var listing = view.Tiles.Where(tile => tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name && tile.Count > 0).Select(tile => tile.Label).ToList();
        var setups = view.Tiles.Count(tile => tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name) + (view.Fundamentals is null ? 0 : 1);

        if (view.Fundamentals > 0)
        {
            listing.Add(EquityBrief.Core.Families.ImprovingRule.Label);
        }

        var from = listing.Count switch
        {
            0 => string.Empty,
            1 => ", all from the " + listing[0].ToLowerInvariant(),
            _ => ", from the " + string.Join(", the ", listing[..^1].Select(label => label.ToLowerInvariant())) + " and the " + listing[^1].ToLowerInvariant(),
        };
        var opens = OpenDay(view.Night);
        var newPicks = line.BuyPoints + (view.Fundamentals ?? 0);
        var reshuffled = (view.Groups ?? line.Groups)?.ToBuyFromReshuffles ?? 0;
        var picks = (newPicks == 0
            ? "No new picks to buy at " + opens + " open."
            : Counted(newPicks, "new pick", "new picks") + " to buy at " + opens + " open" + from + ".")
            + (reshuffled == 0 ? string.Empty : Formatted($" The monthly books' reshuffle adds {reshuffled} more."));

        // From the operator's order of 2026-11-14 the line beneath in their words: on a night with picks the setups that found
        // nothing named; on one without, a stock its setup's rules met again while held named with where its card is, or that
        // no stock met all the rules of any setup.
        // see: A stock a setup's rules met again while a trade holds it is drawn as a full card of its own, bought again by nothing and counted in nothing
        var quietSetups = view.Tiles
            .Where(tile => tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name && tile.Family != EquityBrief.Core.Families.QualityRule.Name && tile.Count == 0)
            .Select(tile => tile.Label)
            .ToList();
        var said = newPicks > 0
            ? quietSetups.Count == 0
                ? "Every setup found something new tonight."
                : Capitalised(Listed([.. quietSetups.Select((label, at) => at == 0 ? label : at == quietSetups.Count - 1 ? "the " + label.ToLowerInvariant() : label.ToLowerInvariant())])) + " found nothing tonight."
            : view.HeldAgain.Count > 0
                ? "No new picks. " + string.Join(" ", view.HeldAgain.Select(held => FormattableString.Invariant($"{held.Ticker} met the {(EquityBrief.Core.Families.SetupFamilies.Named(held.Family)?.Label ?? held.Family).ToLowerInvariant()} rules again but is already held; its card is below.")))
                : line.Passed is 0
                    ? Formatted($"No {line.Index} stock met all the rules of any of the {setups} setups tonight.")
                    : "No setup found anything new.";

        body.Append(Invariant, $"<div class=\"minute-line\" data-line=\"picks\"><p class=\"minute-head\"><b>{Escaped(picks)}</b></p><p class=\"minute-said\">{Escaped(said)}</p></div>");

        // The system's trades still open on the index, every setup's live or provisional rule under any version, how many of
        // them the current rules bought since they started, and those that ended tonight, each named, selling at the next
        // session's open, in the operator's words of 2026-11-14.
        // see: Every count of trades names its group and one function works the four groups
        var open = (view.Groups ?? line.Groups) is { } groups
            ? groups.OpenLine(line.Index)
            : line.OpenTrades == 0
                ? "No trade from an earlier night is still open."
                : Counted(line.OpenTrades, "trade from an earlier night is still open.", "trades from earlier nights are still open.");
        var why = view.Ended is { Count: > 0 } split
            ? EndedWords(split, opens)
            : (view.Groups ?? line.Groups) is { System.Selling: > 0 } selling
                ? Formatted($"{selling.System.Selling} {(selling.System.Selling == 1 ? "sells" : "sell")} at {opens} open.")
                : "None ended tonight.";
        var paper = Formatted($"<a href=\"#/picks?status={PickStatus.Open}{(line.Universe is { } chosen ? "&amp;universe=" + Uri.EscapeDataString(chosen) : string.Empty)}\">See them on Past picks</a>{((view.Groups ?? line.Groups) is null ? string.Empty : Formatted($", and those {TradeGroupsView.CurrentWords} on <a href=\"#/paper{(line.Universe is { } paperOn ? "?universe=" + Uri.EscapeDataString(paperOn) : string.Empty)}\">Paper trading</a>"))}");

        body.Append(Invariant, $"<div class=\"minute-line\" data-line=\"open\"><p class=\"minute-head\"><b>{Escaped(open + " " + why)}</b></p><p class=\"minute-said\">{paper}</p></div>");

        // The orders earlier nights placed still waiting for their price, a group of their own, in none of the counts above.
        // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
        if ((view.Groups ?? line.Groups) is { StillWaitingOrders.Count: > 0 } waiting)
        {
            body.Append(Invariant, $"<div class=\"minute-line\" data-line=\"still-waiting\">{StillWaitingOrders(waiting.StillWaitingOrders, line.Index)}</div>");
        }

        // The stocks one step from qualifying, which from 22.17 the read-only copy draws none of.
        if (Copying)
        {
        }
        else if (line.CloseRead)
        {
            var near = line.Close == 0
                ? "No stock is one step from qualifying."
                : line.Close == 1 ? "1 stock is one step from qualifying." : Formatted($"{line.Close} stocks are one step from qualifying.");
            var see = line.Close > 0 && line.CloseHref is { } href
                ? Formatted($" <a class=\"close-open\" href=\"{Escaped(href)}\">See which, and the step each is missing</a>")
                : string.Empty;

            body.Append(Invariant, $"<div class=\"minute-line\" data-line=\"one-step\"><p class=\"minute-head\"><b>{Escaped(near)}</b></p><p class=\"minute-said\">{(line.Close > 0 ? "They could become picks in the next few nights." : "None is close tonight.")}{see}</p></div>");
        }
        else
        {
            body.Append("<div class=\"minute-line\" data-line=\"one-step\"><p class=\"minute-head\"><b>Which stocks are one step from qualifying is not read tonight.</b></p><p class=\"minute-said\">This index's night stores only the first step each stock missed.</p></div>");
        }

        body.Append("</div>");

        return body.ToString();
    }

    // Why the trades that ended tonight ended, where the store says why, each group's stocks named and selling at the next
    // session's open: "2 reached their target and sell at Monday's open: TTWO and ROP."
    static string EndedWords(EndedTonightView ended, string opens)
    {
        var said = new List<string>();

        void Add(int count, IReadOnlyList<string> tickers, string one, string many)
        {
            if (count > 0)
            {
                said.Add(Formatted($"{Counted(count, one + " and sells", many + " and sell")} at {opens} open{(tickers.Count > 0 ? ": " + Listed(tickers) : string.Empty)}."));
            }
        }

        Add(ended.AtTarget, ended.TargetTickers, "reached its target", "reached their target");
        Add(ended.AtStop, ended.StopTickers, "hit its stop", "hit their stop");
        Add(ended.AtLimit, ended.LimitTickers, "ran out of time", "ran out of time");
        Add(ended.Other, ended.OtherTickers, "ended by its setup's own exit", "ended by their setups' own exits");

        return string.Join(" ", said);
    }

    // Names joined as a sentence reads them: "A", "A and B", "A, B and C".
    static string Listed(IReadOnlyList<string> names) =>
        names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };

    // The market check's bar: the share of the index above its 200-day average drawn against the floor the setups need,
    // the floor marked; none where the night read no share or no floor.
    static string MarketBar(MarketLineView line)
    {
        if (line.Breadth is not { } share || line.Floor is not { } floor)
        {
            return string.Empty;
        }

        var filled = Math.Clamp(share, 0, 1) * 100;
        var marked = Math.Clamp(floor, 0, 1) * 100;

        return Formatted($"<div class=\"market-bar\"><svg viewBox=\"0 0 100 6\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"{Escaped(Percent(share))} above their 200-day average; the setups need {Escaped(Percent(floor))}\"><rect class=\"bar-track\" x=\"0\" y=\"1\" width=\"100\" height=\"4\" rx=\"2\"/><rect class=\"bar-fill{(line.Open ? " open" : " closed")}\" x=\"0\" y=\"1\" width=\"{filled.ToString("0.##", Invariant)}\" height=\"4\" rx=\"2\"/><rect class=\"bar-floor\" x=\"{marked.ToString("0.##", Invariant)}\" y=\"0\" width=\"0.6\" height=\"6\"/></svg><p class=\"bar-labels\"><span>0%</span><span>needs {Escaped(Percent(floor))}</span><span>100%</span></p></div>");
    }

    // The six setups: a tile each, its name, whether it is live and since when or not yet tested, a line on what it looks
    // for, and tonight's line, opening the setup's card.
    public string SixSetups(IReadOnlyList<SetupTileView> tiles, IReadOnlyDictionary<string, DateOnly?> liveSince, IReadOnlyDictionary<string, string> tonight, IReadOnlyDictionary<string, DateOnly?>? firstLive = null)
    {
        var body = new StringBuilder();
        var setups = tiles.Count;
        var finding = tiles.Count(tile => tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name && tile.Family != EquityBrief.Core.Families.QualityRule.Name && tile.Count > 0);
        var summary = finding == 0
            ? Formatted($"None of the {setups} setups found a pick tonight; each tile says how close it came.")
            : Formatted($"{finding} of the {setups} setups found {(finding == 1 ? "a pick" : "picks")} tonight; each tile says what it looks for.");

        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>{Escaped(summary)}</p>");
        body.Append(Invariant, $"<nav class=\"setup-tiles\" aria-label=\"Tonight's setups\" data-tiles=\"{tiles.Count}\">");

        foreach (var tile in tiles)
        {
            var words = WordsOf(tile.Family, tile.Label);
            var since = liveSince.GetValueOrDefault(tile.Family);
            var first = since is null ? null : firstLive?.GetValueOrDefault(tile.Family) ?? since;
            var badge = first is { } live ? FormattableString.Invariant($"Live since {live:d MMM yyyy}") : "Not yet tested";
            var version = since is { } registered && first != since ? FormattableString.Invariant($"<span class=\"tile-version\">This version since {registered:d MMM yyyy}{RuleFreeze.Beside(registered)}; earlier trades kept.</span>") : string.Empty;

            body.Append(Invariant, $"<button type=\"button\" class=\"setup-tile fam-edge\" data-family=\"{Escaped(tile.Family)}\" data-card=\"{Escaped(tile.Family)}\" data-count=\"{tile.Count}\" data-state=\"{Escaped(tile.State)}\">");
            body.Append(Invariant, $"<span class=\"tile-top\"><span class=\"fam-tag\" data-family=\"{Escaped(tile.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(tile.Family)}\"></span>{Escaped(tile.Label)}</span><span class=\"status-pill {(since is null ? "provisional" : "live")}\">{Escaped(badge)}</span></span>");
            body.Append(version);
            body.Append(Invariant, $"<span class=\"tile-plain\">{Escaped(words.Plain)}</span>");
            body.Append(Invariant, $"<span class=\"tile-tonight\">{Escaped(tonight.GetValueOrDefault(tile.Family) ?? tile.State)}</span>{(tile.Quiet is { } quiet ? Formatted($"<span class=\"tile-quiet\">{Escaped(quiet)}</span>") : string.Empty)}{TileRecordLine(tile.LongRecord)}</button>");
        }

        body.Append("</nav>");

        return body.ToString();
    }

    // What each column of tomorrow's buys holds, in the order the columns are drawn.
    public static IReadOnlyList<(string Heading, string Says)> TomorrowHeadings { get; } =
    [
        ("Stock", "The ticker opens the stock's chart. Open plan opens the pick's own plan beneath the row; Report opens its written report, or asks for one where none is written."),
        ("Setup", "The setup that picked the stock tonight."),
        ("Why it qualified", "What the stock did that its setup buys, from the figures the night stored."),
        ("The business", "How the company's reported quarters read: improving, steady or getting worse, with what its numbers say under the pointer. It removes no pick."),
        ("Buy about", "Tonight's close. The pick is bought at the next trading day's open."),
        ("Stop", "A close below it sells the trade at a loss, at the next day's open."),
        ("Target", "A close at or above it sells the trade at a gain, at the next day's open. A breakout has a stop that rises with the price and no target; the short-term dip names its exit."),
        ("Risk $1 to make", "How far the target is against the stop: $2.00 means the target is twice as far away as the stop. It is a fact about the chart and not a chance of anything."),
        ("News, 30 days", "The stories of the last 30 days the labeller read as good or bad for the company."),
    ];

    // Every buy on the index in the order the page draws its setups: the setups' buy points, the Fundamentals family's buys
    // in its slot after the drift's and before the dip's, the dip's, then the monthly books', each set in its own order.
    // see: Tonight's buys name every buy on the index, a buy no plan sells drawn with how it sells in its stop's and target's place
    public static IReadOnlyList<(BuyPointView? Point, PlanlessBuyView? Planless)> BuysInOrder(IReadOnlyList<BuyPointView> points, IReadOnlyList<PlanlessBuyView> planless) =>
    [
        .. points.Where(point => point.Family != EquityBrief.Core.Families.DipRule.Name).Select(point => ((BuyPointView?)point, (PlanlessBuyView?)null)),
        .. planless.Where(buy => buy.Family == EquityBrief.Core.Families.ImprovingRule.Name).Select(buy => ((BuyPointView?)null, (PlanlessBuyView?)buy)),
        .. points.Where(point => point.Family == EquityBrief.Core.Families.DipRule.Name).Select(point => ((BuyPointView?)point, (PlanlessBuyView?)null)),
        .. planless.Where(buy => buy.Family != EquityBrief.Core.Families.ImprovingRule.Name).Select(buy => ((BuyPointView?)null, (PlanlessBuyView?)buy)),
    ];

    // What a buy no plan sells shows in its stop's and target's place: the Fundamentals family's filings, a book's reshuffle.
    public const string SellsOnFilings = "Sells when its filings weaken";

    public const string SellsAtAReshuffle = "Sells when a monthly reshuffle drops it";

    // Tomorrow's buys: one table across every setup, each row whose night wrote its pick's card folding the card beneath it
    // with, where the page hands one, the stock's entry and exit plan drawn inside it beneath its setup's own plan, closed
    // until a press on the row or its Open plan opens it, the row the link names open; a buy no plan sells drawn as a row
    // saying how it sells and linking to its row on its own card; and one line on how to read it.
    // see: A pick's page heads its rule's plan as traded and draws every other way in open beneath it, headed as not traded
    public string TomorrowsBuys(IReadOnlyList<BuyPointView> points, string? open, IReadOnlyDictionary<string, string>? links = null, IReadOnlyDictionary<string, string>? plans = null, DateOnly? night = null, IReadOnlyList<PlanlessBuyView>? planless = null)
    {
        var body = new StringBuilder();
        var opens = OpenDay(night);
        var rows = BuysInOrder(points, planless ?? []);

        // One count across the pages, the new picks, every buy a row of the table and the monthly books' reshuffle said apart.
        // see: Every count of trades names its group and one function works the four groups
        var reshuffled = rows.Count(row => row.Planless is { } buy && buy.Family != EquityBrief.Core.Families.ImprovingRule.Name);
        var picked = rows.Count - reshuffled;
        var summary = (picked == 0
                ? "No stock to buy at " + opens + " open: no setup picked one tonight."
                : Counted(picked, "stock", "stocks") + " to buy at " + opens + " open, at about tonight's close.")
            + (reshuffled == 0 ? string.Empty : Formatted($" The monthly books' reshuffle adds {reshuffled} more, each a row below."));

        body.Append(Invariant, $"<p class=\"card-summary\" data-summary data-buy-points=\"{rows.Count}\">{Escaped(summary)}</p>");

        if (rows.Count == 0)
        {
            return body.ToString();
        }

        body.Append(Invariant, $"<div class=\"tbl-wrap opens-rows\"><table class=\"list-table buy-points\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in TomorrowHeadings)
        {
            body.Append(TippedHeading(heading, says, heading is "Buy about" or "Stop" or "Target" or "Risk $1 to make" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var ((drawn, buy), number) in rows.Select((row, at) => (row, at + 1)))
        {
            if (buy is not null)
            {
                body.Append(PlanlessRow(buy, number));
                continue;
            }

            var point = drawn!;
            var pick = point.Pick;
            var row = pick.Row;
            var folds = pick.Card is not null;
            var fold = FoldId(point.Family, row.Ticker);
            var opened = folds && string.Equals(row.Ticker, open, StringComparison.Ordinal);

            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-family=\"{Escaped(point.Family)}\" data-place=\"{number}\"{(folds ? Formatted($" data-fold=\"{Escaped(fold)}\"") : string.Empty)}{(opened ? " class=\"sel\"" : string.Empty)}>");
            body.Append(PickNameCell(pick, (folds ? FoldToggle(fold, opened) : string.Empty) + (links?.GetValueOrDefault(row.Ticker) ?? string.Empty)));
            body.Append(Invariant, $"<td class=\"setup\"><span class=\"fam-tag\" data-family=\"{Escaped(point.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(point.Family)}\"></span>{Escaped(point.Label)}</span></td>");
            body.Append(Invariant, $"<td class=\"why\">{Escaped(WhyWords(point.Family, pick.Why))}</td>");
            body.Append(PickBusinessCell(pick));

            // From the 23.10 correction of 2026-11-13 a pick its live rule's plan buys at a price draws the price it waits for, the
            // stop and the sale that plan was registered with, and what it risks for what that sale aims at.
            // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
            if (pick.Card?.Zone is { NoBuy: false, Sale: { } sale } zone)
            {
                body.Append(PriceCell("buy", zone.Price, null));
                body.Append(PriceCell("stop", pick.Card.Stop, null));
                body.Append(SaleCell(sale));
                body.Append(sale.RewardPerRisk is { } ratio
                    ? Formatted($"<td class=\"r num\" data-reward-to-risk=\"{ratio.ToString(Invariant)}\">${Figures.Ratio(ratio)}</td>")
                    : Formatted($"<td class=\"r num\" data-reward-to-risk=\"none\">{(sale.OnTimeAlone ? "No target: it sells on its time limit" : sale.HoldsToItsLimit ? "No price above tonight's close: it holds to its time limit" : "<span class=\"degraded\">none above the buy</span>")}</td>"));
            }
            else
            {
                body.Append(PriceCell("buy", pick.Buy, null));
                body.Append(PriceCell("stop", pick.Stop, null));

                // From the operator's order of 2026-11-14 the price its plan takes profit at, where its card holds one.
                // see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
                body.Append(pick.Card?.TakeProfit is { } taking
                    ? PriceCell("target", taking.Price, null)
                    : PriceCell("target", pick.Target, pick.Trailing ? "rising stop, no target" : pick.Exit is { } exit ? ReaderWords.Plain(exit) : null));
                body.Append(RewardCell(pick));
            }

            body.Append(PickNewsCell(pick));
            body.Append("</tr>");

            if (folds)
            {
                body.Append(Invariant, $"<tr class=\"fold-row\" id=\"{Escaped(fold)}\" data-fold-for=\"{Escaped(row.Ticker)}\"{(opened ? string.Empty : " hidden")}><td colspan=\"{TomorrowHeadings.Count}\"><div class=\"row-open\">{DecisionCard(pick.Card!, pick, linked: true, levels: plans?.GetValueOrDefault(row.Ticker))}</div></td></tr>");
            }
        }

        body.Append("</tbody></table></div>");
        body.Append("<p class=\"buys-read\">The business is how the company's reported quarters read: improving, steady or getting worse. News counts the stories of the last 30 days the labeller read as good or bad for the company. Open a row for its full plan, what could go wrong and how its setup has done. Each pick is bought at ");
        body.Append(opens);
        body.Append(rows.Any(row => row.Planless is not null)
            ? " open, with its stop and target where tonight's close set them; a Fundamentals buy and a monthly book's set none, and How it sells opens its row on its own card.</p>"
            : " open, with its stop and target where tonight's close set them.</p>");

        return body.ToString();
    }

    // The target's column of a row whose plan buys at a price: the sale it was registered with in a few words, its target, its
    // staged levels, or what sells it where it names no price above the buy.
    static string SaleCell(PlanSaleView sale)
    {
        var after = Formatted($"{sale.Cap} trading days");

        return sale.Kind == EquityBrief.Core.Loop.PlanPoints.StagedSales && sale.Stages.Count > 0
            ? Formatted($"<td class=\"r num plan-target\" data-target=\"staged\" data-sale=\"{Escaped(sale.Kind)}\">{Escaped(PlanSaleView.SharesOf(sale.Stages.Count).Each)} at each of {Escaped(string.Join(", ", sale.Stages.Select(Figures.Price)))}{(sale.TakeProfit is { } rest ? Formatted($", the rest at {Figures.Price(rest.Price)}") : string.Empty)}</td>")
            // From the operator's order of 2026-11-14 the price it takes profit at where its sale names none of its own.
            : sale.TakeProfit is { } taking && (sale.Target is null || sale.OnTimeAlone)
                ? Formatted($"<td class=\"r num plan-target\" data-target=\"{taking.Price.ToString(Invariant)}\" data-sale=\"{Escaped(sale.Kind)}\">{Figures.Price(taking.Price)}</td>")
            : sale.Target is { } target && !sale.OnTimeAlone
                ? Formatted($"<td class=\"r num plan-target\" data-target=\"{target.ToString(Invariant)}\" data-sale=\"{Escaped(sale.Kind)}\">{Figures.Price(target)}</td>")
                : Formatted($"<td class=\"r num plan-target\" data-target=\"none\" data-sale=\"{Escaped(sale.Kind)}\">{Escaped(sale.OnTimeAlone ? "none: sells after " + after : sale.Trail is not null ? "rising stop, no target" : sale.RsiAbove is not null ? "sells on a bounce" : "none above tonight's close: holds for " + after)}</td>");
    }

    // A buy no plan sells, as a row of tomorrow's buys: its stock with the link to its row on its own card, its setup, why
    // it qualified with its entry check's line, its business, tonight's close, how it sells across the stop's and the
    // target's columns, and no reward to risk or news count of its own.
    string PlanlessRow(PlanlessBuyView buy, int number)
    {
        var row = new StringBuilder();

        row.Append(Invariant, $"<tr data-ticker=\"{Escaped(buy.Ticker)}\" data-family=\"{Escaped(buy.Family)}\" data-place=\"{number}\" data-planless>");
        row.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(buy.Ticker)}\">{Escaped(buy.Ticker)}</a> <a class=\"how-sells\" href=\"{buy.Href}\">How it sells</a>{(buy.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
        row.Append(Invariant, $"<td class=\"setup\"><span class=\"fam-tag\" data-family=\"{Escaped(buy.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(buy.Family)}\"></span>{Escaped(buy.Label)}</span></td>");
        row.Append(Invariant, $"<td class=\"why\">{Escaped(buy.Why)}{EntryCheckLine(buy.EntryCheck, buy.Ticker)}{SetupRecordBox(buy.LongRecord)}</td>");
        row.Append(Invariant, $"<td class=\"business-cell\">{Escaped(buy.Business)}</td>");
        row.Append(PriceCell("buy", buy.Close, null));
        row.Append(Invariant, $"<td class=\"r plan-sells\" colspan=\"2\" data-sells>{Escaped(buy.Sells)}</td>");
        row.Append("<td class=\"r num\" data-reward-to-risk=\"none\"><span class=\"degraded\">no stop or target</span></td>");
        row.Append("<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">not counted</span></td>");

        return row.Append("</tr>").ToString();
    }

    // The words this page uses, each with what it means here: what each card's guide said, in one place at the page's
    // foot.
    public static IReadOnlyList<(string Term, string Meaning)> TonightWords { get; } =
    [
        ("Stop", "The price that says the idea was wrong. If the stock closes below it, the trade is sold at the next day's open."),
        ("Target", "The price that says the idea worked. If the stock closes at or above it, the trade is sold at the next day's open. A breakout has none: its stop rises as the price climbs."),
        ("Risk $1 to make", "How far the target is compared with the stop. $2.50 means the target is 2.5 times as far away as the stop. It is a fact about the chart and not a chance of anything."),
        ("Typical day's move", "How much this stock usually moves in a day. Measuring jumps and distances in these units makes calm and jumpy stocks comparable."),
        ("Live", "The setup's settings were chosen from years of history and fixed; its record now counts only the nights after that."),
        ("Not yet tested", "Running on starting settings while its history test is done. Its picks count toward nothing yet."),
        ("Version", "The same setup with one thing changed, run in the background so it can be compared fairly with the live one. A version is never tonight's pick."),
        ("One stock, one trade", "A stock already bought on an earlier night is not bought again until that trade ends, across every setup on every index."),
        ("The business", "How the company's reported quarters read: improving, steady or getting worse, from sales and margins. It orders nothing and removes no pick."),
        ("Quiet night", "A night a setup finds nothing. Each setup shows how long its quiet runs usually last, so a long one stands out."),
        ("One step short", "A stock that passed every step of its setup but one. It is not a pick and is counted as none."),
        ("The market check", "The share of the index's stocks above their 200-day average, against the floor the setups need before they buy. The short-term dip reads a setting of its own."),
    ];

    // The page's words at its foot, as a glossary.
    public string WordsOnThisPage()
    {
        var body = new StringBuilder();

        // From 22.17 the read-only copy's words leave out a version and a stock one step short, which it draws none of.
        (string Term, string Meaning)[] words = [.. TonightWords.Where(word => !Copying || word.Term is not ("Version" or "One step short"))];

        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>The {words.Length} words this page uses most, each with what it means here.</p>");
        body.Append("<dl class=\"gloss tonight-words\">");

        foreach (var (term, meaning) in words)
        {
            body.Append(Invariant, $"<div class=\"gloss-row\"><dt>{Escaped(term)}</dt><dd>{Escaped(meaning)}</dd></div>");
        }

        body.Append("</dl>");

        return body.ToString();
    }
}
