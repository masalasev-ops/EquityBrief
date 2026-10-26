using System.Text;

namespace EquityBrief.Web.Marks;

// The trades from earlier nights that ended on the night, by why where the store says why: at their target, at their stop
// or at their time limit, and those whose ending the store keeps no reason for.
public sealed record EndedTonightView(int AtTarget, int AtStop, int AtLimit, int Other)
{
    public int Count => AtTarget + AtStop + AtLimit + Other;
}

// What Tonight in one minute reads: the market line, tonight's picks by setup, the trades from earlier nights still open
// with those that ended on the night, and the stocks one step from qualifying with the link opening them.
public sealed record OneMinuteView(MarketLineView Line, IReadOnlyList<SetupTileView> Tiles, EndedTonightView? Ended, string? PicksHref);

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
            "Holds the biggest companies that are beating their own sector over the past year. It is reshuffled on the first trading day of each month, and a stock is sold when it stops leading. No stop and no target."),
        [EquityBrief.Core.Families.QualityRule.Name] = new(
            "Holds companies with strong fundamentals at a fair price; reshuffled monthly.",
            "Holds the companies whose filings score strongest on nine checks and whose price over earnings is no higher than its own usual. It is reshuffled on the first trading day of each month, and a stock is sold when it no longer qualifies. No stop and no target."),
    };

    // A setup's words, its own label's where the list holds none for it.
    public static SetupWordsView WordsOf(string family, string label) =>
        SetupWords.TryGetValue(family, out var words) ? words : new SetupWordsView(label + ".", label + ".");

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
        var listing = view.Tiles.Where(tile => tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name && tile.Count > 0).ToArray();
        var setups = view.Tiles.Count(tile => tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name);
        var from = listing.Length switch
        {
            0 => string.Empty,
            1 => ", all from the " + listing[0].Label.ToLowerInvariant(),
            _ => ", from the " + string.Join(", the ", listing[..^1].Select(tile => tile.Label.ToLowerInvariant())) + " and the " + listing[^1].Label.ToLowerInvariant(),
        };
        var picks = line.BuyPoints == 0
            ? "No new picks to buy at tomorrow's open."
            : Counted(line.BuyPoints, "new pick", "new picks") + " to buy at tomorrow's open" + from + ".";
        var qualified = line.Passed is { } passed && line.Members is { } members
            ? Formatted($"{passed} of the {members} {line.Index} stocks qualified under some setup tonight. ")
            : string.Empty;
        var others = setups - listing.Length;
        var quiet = line.BuyPoints == 0
            ? "No setup found anything new."
            : others switch
            {
                0 => "Every setup found something new.",
                1 => "The other setup found nothing new.",
                _ => Formatted($"The other {others} setups found nothing new."),
            };

        body.Append(Invariant, $"<div class=\"minute-line\" data-line=\"picks\"><p class=\"minute-head\"><b>{Escaped(picks)}</b></p><p class=\"minute-said\">{Escaped(qualified + quiet)}</p></div>");

        // The trades from earlier nights still open, and those that ended tonight.
        var open = line.OpenTrades == 0
            ? "No trade from an earlier night is still open"
            : Counted(line.OpenTrades, "trade from an earlier night is still open", "trades from earlier nights are still open");
        var endedWords = view.Ended is not { Count: > 0 } done
            ? "."
            : Formatted($"; {done.Count} ended tonight.");
        var why = view.Ended is { Count: > 0 } split ? EndedWords(split) : "None ended tonight.";
        var paper = Formatted($"<a href=\"#/picks?status={PickStatus.Open}{(line.Universe is { } chosen ? "&amp;universe=" + Uri.EscapeDataString(chosen) : string.Empty)}\">See them on Past picks</a>");

        body.Append(Invariant, $"<div class=\"minute-line\" data-line=\"open\"><p class=\"minute-head\"><b>{Escaped(open + endedWords)}</b></p><p class=\"minute-said\">{Escaped(why)} {paper}</p></div>");

        // The stocks one step from qualifying.
        if (line.CloseRead)
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

    // Why the trades that ended tonight ended, where the store says why.
    static string EndedWords(EndedTonightView ended)
    {
        var said = new List<string>();

        if (ended.AtTarget > 0)
        {
            said.Add(Counted(ended.AtTarget, "reached its target", "reached their target"));
        }

        if (ended.AtStop > 0)
        {
            said.Add(Counted(ended.AtStop, "hit its stop", "hit their stop"));
        }

        if (ended.AtLimit > 0)
        {
            said.Add(Counted(ended.AtLimit, "ran out of time", "ran out of time"));
        }

        if (ended.Other > 0)
        {
            said.Add(Counted(ended.Other, "ended by its setup's own exit", "ended by their setups' own exits"));
        }

        return said.Count switch
        {
            0 => string.Empty,
            1 => said[0] + ".",
            _ => string.Join(", ", said.Take(said.Count - 1)) + " and " + said[^1] + ".",
        };
    }

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
    public string SixSetups(IReadOnlyList<SetupTileView> tiles, IReadOnlyDictionary<string, DateOnly?> liveSince, IReadOnlyDictionary<string, string> tonight)
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
            var badge = since is { } live ? FormattableString.Invariant($"Live since {live:d MMM yyyy}") : "Not yet tested";

            body.Append(Invariant, $"<button type=\"button\" class=\"setup-tile fam-edge\" data-family=\"{Escaped(tile.Family)}\" data-card=\"{Escaped(tile.Family)}\" data-count=\"{tile.Count}\" data-state=\"{Escaped(tile.State)}\">");
            body.Append(Invariant, $"<span class=\"tile-top\"><span class=\"fam-tag\" data-family=\"{Escaped(tile.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(tile.Family)}\"></span>{Escaped(tile.Label)}</span><span class=\"status-pill {(since is null ? "provisional" : "live")}\">{Escaped(badge)}</span></span>");
            body.Append(Invariant, $"<span class=\"tile-plain\">{Escaped(words.Plain)}</span>");
            body.Append(Invariant, $"<span class=\"tile-tonight\">{Escaped(tonight.GetValueOrDefault(tile.Family) ?? tile.State)}</span></button>");
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
        ("Buy about", "Tonight's close. The pick is bought at tomorrow's open."),
        ("Stop", "A close below it sells the trade at a loss."),
        ("Target", "Reaching it sells the trade at a gain. A breakout has a stop that rises with the price and no target; the short-term dip names its exit."),
        ("Risk $1 to make", "How far the target is against the stop: $2.00 means the target is twice as far away as the stop. It is a fact about the chart and not a chance of anything."),
        ("News, 30 days", "The stories of the last 30 days the labeller read as good or bad for the company."),
    ];

    // Tomorrow's buys: one table across every setup, each row whose night wrote its pick's card folding the card beneath it
    // with, where the page hands one, the stock's entry and exit plan drawn inside it beneath its setup's own plan, closed
    // until a press on the row or its Open plan opens it, the row the link names open; and one line on how to read it. An
    // index the labeller reads no list of says so in each row holding no label.
    // see: A buy point's fold draws the stock's entry and exit plan beneath its setup's own plan, as the full page draws it
    public string TomorrowsBuys(IReadOnlyList<BuyPointView> points, string? open, IReadOnlyDictionary<string, string>? links = null, IReadOnlyDictionary<string, string>? plans = null, bool labelled = true)
    {
        var body = new StringBuilder();
        var summary = points.Count == 0
            ? "No stock to buy at tomorrow's open: no setup picked one tonight."
            : Counted(points.Count, "stock", "stocks") + " to buy at tomorrow's open, at about tonight's close.";

        body.Append(Invariant, $"<p class=\"card-summary\" data-summary data-buy-points=\"{points.Count}\">{Escaped(summary)}</p>");

        if (points.Count == 0)
        {
            return body.ToString();
        }

        body.Append(Invariant, $"<div class=\"tbl-wrap opens-rows\"><table class=\"list-table buy-points\" data-rows=\"{points.Count}\"><thead><tr>");

        foreach (var (heading, says) in TomorrowHeadings)
        {
            body.Append(TippedHeading(heading, says, heading is "Buy about" or "Stop" or "Target" or "Risk $1 to make" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var (point, number) in points.Select((point, at) => (point, at + 1)))
        {
            var pick = point.Pick;
            var row = pick.Row;
            var folds = pick.Card is not null;
            var fold = FoldId(point.Family, row.Ticker);
            var opened = folds && string.Equals(row.Ticker, open, StringComparison.Ordinal);

            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-family=\"{Escaped(point.Family)}\" data-place=\"{number}\"{(folds ? Formatted($" data-fold=\"{Escaped(fold)}\"") : string.Empty)}{(opened ? " class=\"sel\"" : string.Empty)}>");
            body.Append(PickNameCell(pick, (folds ? FoldToggle(fold, opened) : string.Empty) + (links?.GetValueOrDefault(row.Ticker) ?? string.Empty)));
            body.Append(Invariant, $"<td class=\"setup\"><span class=\"fam-tag\" data-family=\"{Escaped(point.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(point.Family)}\"></span>{Escaped(point.Label)}</span></td>");
            body.Append(Invariant, $"<td class=\"why\">{Escaped(ReaderWords.Plain(StoredWords(pick.Why)))}</td>");
            body.Append(PickBusinessCell(pick));
            body.Append(PriceCell("buy", pick.Buy, null));
            body.Append(PriceCell("stop", pick.Stop, null));
            body.Append(PriceCell("target", pick.Target, pick.Trailing ? "rising stop, no target" : pick.Exit is { } exit ? ReaderWords.Plain(exit) : null));
            body.Append(RewardCell(pick));
            body.Append(PickNewsCell(pick, labelled));
            body.Append("</tr>");

            if (folds)
            {
                body.Append(Invariant, $"<tr class=\"fold-row\" id=\"{Escaped(fold)}\" data-fold-for=\"{Escaped(row.Ticker)}\"{(opened ? string.Empty : " hidden")}><td colspan=\"{TomorrowHeadings.Count}\"><div class=\"row-open\">{DecisionCard(pick.Card!, pick, linked: true, levels: plans?.GetValueOrDefault(row.Ticker))}</div></td></tr>");
            }
        }

        body.Append("</tbody></table></div>");
        body.Append("<p class=\"buys-read\">The business is how the company's reported quarters read: improving, steady or getting worse. News counts the stories of the last 30 days the labeller read as good or bad for the company. Open a row for its full plan, what could go wrong and how its setup has done. Each pick is bought at tomorrow's open, with its stop and target where tonight's close set them.</p>");

        return body.ToString();
    }

    // The words this page uses, each with what it means here: what each card's guide said, in one place at the page's
    // foot.
    public static IReadOnlyList<(string Term, string Meaning)> TonightWords { get; } =
    [
        ("Stop", "The price that says the idea was wrong. If the stock closes below it, the trade is sold."),
        ("Target", "The price that says the idea worked. If the stock reaches it, the trade is sold. A breakout has none: its stop rises as the price climbs."),
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

        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>The {TonightWords.Count} words this page uses most, each with what it means here.</p>");
        body.Append("<dl class=\"gloss tonight-words\">");

        foreach (var (term, meaning) in TonightWords)
        {
            body.Append(Invariant, $"<div class=\"gloss-row\"><dt>{Escaped(term)}</dt><dd>{Escaped(meaning)}</dd></div>");
        }

        body.Append("</dl>");

        return body.ToString();
    }
}
