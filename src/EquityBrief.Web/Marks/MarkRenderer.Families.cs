using System.Text;

namespace EquityBrief.Web.Marks;

// One stock a family's card lists: the night's row for it as every list draws a name, its place down the
// page, the trade its family's plan states, with no target where the plan trails its stop, the reward to risk as the card
// prints it, why the family lists it tonight in that family's words, and the labels of the other families
// it qualified under.
// see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
public sealed record FamilyPickCell(
    ListingCell Row,
    int Place,
    decimal? Buy,
    decimal? Stop,
    decimal? Target,
    bool Trailing,
    decimal? RewardToRisk,
    string Why,
    IReadOnlyList<string> Also,
    DecisionCardView? Card = null,
    string? Exit = null);

// One family's card on a night: the family's words, where it stands in the page's order, the day its
// rule went live or nothing while it runs on provisional settings, how many variants are scored beside
// it, its picks in the page's order, the notes beneath them, why it lists nothing where it lists none, and
// whether its part of the night failed, which its tile says in place of its market check.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
// see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
public sealed record FamilyCardView(
    string Family,
    string Label,
    string Heading,
    string Eyebrow,
    string Rule,
    int Place,
    int Of,
    DateOnly? LiveSince,
    int Variants,
    IReadOnlyList<FamilyPickCell> Picks,
    IReadOnlyList<string> Notes,
    string? Empty,
    bool SweepFoundNone = false,
    RuleView? RuleChoice = null,
    int HeldBack = 0,
    IReadOnlyList<string>? Passes = null,
    IReadOnlyList<CloseToBuyCell>? Close = null,
    IReadOnlyDictionary<string, IReadOnlyList<EquityBrief.Core.Cards.CardCell>>? Numbers = null,
    IReadOnlyList<int>? SweepLost = null,
    FrozenSweepView? Frozen = null,
    bool NotComputed = false,
    EquityBrief.Core.Cards.PaperProgress? Progress = null);

// What a frozen card states of the sweep its freeze followed: the day it was frozen, the run and the answer it stated,
// the figures that run stated for its family's setting, and whether its edge is drawn in points, the sector
// heavyweights' fraction of the buy against their size cut, or in risks.
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
public sealed record FrozenSweepView(DateOnly FrozenOn, string Run, string Answer, EquityBrief.Core.Sweep.AnswerFigures Figures, bool Points);

// One choice on a card's tabs: the rule's name as the register holds it, the slug the link keeps it under, its variant
// number by its first registration or none for the live rule, and the words its tab is drawn with, the settings it
// differs from the live rule by for a variant.
public sealed record RuleChoiceView(string Rule, string Slug, int? Variant, bool Live, string? Tab = null);

// One of a rule's settings as a chip, marked where the chosen variant differs from its live rule on it.
public sealed record RuleChipView(string Words, bool Differs);

// A chosen variant's picks or holdings beside the live rule's passes tonight: the live rule's word, those both hold,
// those the variant adds and those it drops.
public sealed record RuleShareView(string Against, IReadOnlyList<string> Shared, IReadOnlyList<string> Added, IReadOnlyList<string> Dropped);

// One clause of a rule's words, marked where the live rule's words do not carry it.
public sealed record RulePartView(string Text, bool Differs);

// One pick of a variant's own list on a night, drawn under the band and never as tonight's list.
public sealed record VariantPickView(int Place, string Ticker, decimal Entry, decimal Stop, decimal? Target, double? RewardToRisk, string Why);

// A rule's empty stretch on a night against its mark, with the completed stretches and the sessions the mark was read
// over, the mark none under the floors.
public sealed record StretchLineView(int Stretch, int? Mark, bool Flagged, int Completed, int Sessions);

// One member forming a breakout under a card's rule.
public sealed record FormingRowView(int Place, string Ticker, decimal Close, decimal High, double MovesUnder, double VolumeNeeded, double Volume, double RangeRatio, IReadOnlyList<string> Missing, DateOnly? NextEarnings);

// The breakouts forming under a card's rule: the rows drawn of the count forming, and whether the market check left
// the rule's list open on the night.
public sealed record FormingListView(IReadOnlyList<FormingRowView> Rows, int Forming, bool MarketOpen);

// The rule a card draws: the choice made, every choice, the chosen rule's words in clauses, a variant's own picks, the
// funnel, the stretch line, the forming list, whether the night evaluated the rule, the line an index with no variant
// carries, for a book registered before its family's freeze how the tester judged its design with the adjusted p it
// stored, and how many members the night read for the rule where its funnel holds the count, each of which reached its
// first gate.
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public sealed record RuleView(
    RuleChoiceView Chosen,
    IReadOnlyList<RuleChoiceView> Choices,
    IReadOnlyList<RulePartView> Parts,
    IReadOnlyList<VariantPickView>? Picks,
    IReadOnlyList<(string Gate, int Passed)>? Funnel,
    StretchLineView? Stretch,
    FormingListView? Forming,
    bool Evaluated,
    string? NoVariantLine,
    UntestedLineView? Untested = null,
    IReadOnlyList<RuleChipView>? Chips = null,
    RuleShareView? Share = null,
    string? Words = null,
    int? Reached = null);

// The line beside a book registered before its family's freeze: the line itself and the adjusted p it states, as the
// tester's newest run stored it.
public sealed record UntestedLineView(string Line, double? Adjusted);

// The line tonight's page opens on, on a night the families drew its list: the index it is over, whether the
// market check left the lists open, the breadth it read against its floor, how many of the index's members a
// setup passed, the buy points the page lists and how many of the setups list one, the stocks a single gate
// short of one where the night read them, and the trades still open; and from 22.1 the short-term dip's own market
// setting in words with whether it left the dip's list open, none on a night the dip's step did not read.
// see: The market check closes every swing family's list together but the short-term dip's, and the sector heavyweights read none
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public sealed record MarketLineView(
    bool Open,
    double? Breadth,
    double? Floor,
    int BuyPoints,
    int SetupsListing,
    int Setups,
    int Close,
    int OpenTrades,
    string Index = "S&P 500",
    int? Passed = null,
    int? Members = null,
    bool CloseRead = true,
    string? Universe = null,
    string? RunHref = null,
    int? Watching = null,
    string? CloseHref = null,
    IReadOnlyList<MarketCheckView>? Checks = null,
    string? Dip = null,
    bool DipOpen = false);

// One setup's own market check on an S&P 400 or 600 night, as its rule's first stored part read it for every member: the
// family, its label, whether it left the family's list open, the check's words, and the breadth and floor it read, none
// where it read none.
// see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
public sealed record MarketCheckView(string Family, string Setup, bool Open, string Words, double? Breadth = null, double? Floor = null);

// One stock a single gate short of a buy point under one setup, as the shared list draws it: the setup's
// word and label, the gate it missed, and that gate's own words.
public sealed record CloseToBuyCell(string Ticker, string? Company, string Family, string Setup, string Gate, string Words);

// What a name's page says of the page's list on its night: the night, the setup that lists the name with its
// place and the other setups it qualified under, or none where no setup lists it, and a sentence for each
// setup that passed it and holds it back.
public sealed record ListedUnderView(DateOnly Night, string? Family, string? Heading, int? Place, IReadOnlyList<string> Also, IReadOnlyList<string> HeldBack);

// One setup family on the run page: the day its rule went live or none while it is provisional, the variants
// scored beside it, what it lists on the night, the trades the page has listed under it with how many are
// open and finished, and its record in words.
public sealed record FamilyRunRow(string Family, string Heading, DateOnly? LiveSince, int Variants, int ListedTonight, int Trades, int Open, int Finished, string Record);

// One registered rule of a new setup family on the run page, read over its own trades: the family's heading,
// the rule's name and whether it is the live one, the trades it kept, those decided and their edge, the whole
// blocks against the look they wait for, the level its looks are read at, the record in words, the session its
// record counts from and, where a replay at its registration found a trade it would not have kept the same, why
// it restarted there.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
public sealed record FamilyRecordRow(string Family, string Heading, string Rule, bool Live, int Trades, int Decided, double? Edge, int Blocks, int NextLook, double Level, string Record, DateOnly? From = null, string? Restarted = null, string? Short = null, string? Verdict = null);

// One trade an S&P 400's or 600's list kept, as Past picks draws it: the stock and its company, the setup that listed
// it and the night, its plan, where it ended by the page's night, its result before its cost, its cost and its result
// after, each in multiples of its risk and none while open.
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public sealed record IndexTradeCell(
    string Ticker,
    string? Company,
    string Family,
    string Setup,
    DateOnly Listed,
    decimal Entry,
    decimal Stop,
    decimal? Target,
    bool Trailing,
    DateOnly? EndedOn,
    double? Result,
    double? Cost,
    double? AfterCost);

// How an S&P 400's or 600's night went, as its Run page states it: the members read, the index's own breadth against
// the floor and whether its swing lists were open, the members a setup passed, those listed and held back, the trades
// kept and ended, and its sector heavyweights' rebalance and holdings.
public sealed record IndexRunView(int Members, double? Breadth, double Floor, bool MarketOpen, int Passed, int Listed, int HeldBack, int Kept, int Ended, bool Rebalanced, int Holdings, string? Fault = null, IReadOnlyList<MarketCheckView>? Checks = null);

public sealed partial class MarkRenderer
{
    // What each column of an index's trades on Past picks holds.
    public static IReadOnlyList<(string Heading, string Says)> IndexTradeHeadings { get; } =
    [
        ("Stock", "The ticker opens the stock's page, with the company beneath."),
        ("Setup", "The setup that listed it, by its family's live rule from its freeze and on the index's provisional settings before it."),
        ("Listed", "The night its list drew it, bought at the open of the session after."),
        ("Buy", "The open it was bought at, the session after its night, or its plan's close while it buys at the next open."),
        ("Stop", "The price a close beneath ends it at a loss, or the first level of a stop that trails."),
        ("Target", "The price a close at or above ends it at a gain; a setup that trails its stop names none."),
        ("Status", "Open, or the session it ended on."),
        ("Result", "What it made in multiples of what it risked, before its cost."),
        ("Cost", "Its round trip at the published spread for its size and price, in multiples of its risk."),
        ("After cost", "Its result less its cost, which is what the index's tests read."),
    ];

    // An index's trades on Past picks, newest first, each with its result before and after its cost.
    public string IndexTrades(IReadOnlyList<IndexTradeCell> rows, string index)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table index-trades\" data-index=\"{Escaped(index)}\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in IndexTradeHeadings)
        {
            body.Append(TippedHeading(heading, says, heading is "Buy" or "Stop" or "Target" or "Result" or "Cost" or "After cost" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-index=\"{Escaped(index)}\" data-family=\"{Escaped(row.Family)}\" data-listed=\"{DayOf(row.Listed)}\" data-ended=\"{(row.EndedOn is { } end ? DayOf(end) : "open")}\" ");
            body.Append(Invariant, $"data-result=\"{Stated(row.Result)}\" data-cost=\"{Stated(row.Cost)}\" data-after-cost=\"{Stated(row.AfterCost)}\">");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{(row.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            body.Append(Invariant, $"<td class=\"setup\" data-setup=\"{Escaped(row.Family)}\">{Escaped(row.Setup)} · {Escaped(index)}</td>");
            body.Append(Invariant, $"<td>{DayOf(row.Listed)}</td>");
            body.Append(PriceCell("buy", row.Entry, null));
            body.Append(PriceCell("stop", row.Stop, null));
            body.Append(PriceCell("target", row.Target, row.Trailing ? "trailing" : null));
            body.Append(row.EndedOn is { } ended ? Formatted($"<td>ended {DayOf(ended)}</td>") : "<td><b>open</b></td>");
            body.Append(Invariant, $"<td class=\"r num\">{Multiple(row.Result)}</td><td class=\"r num\">{(row.Cost is { } cost ? Unsigned(cost, 2).ToString("0.00", Invariant) : "<span class=\"degraded\">none yet</span>")}</td><td class=\"r num\">{Multiple(row.AfterCost)}</td></tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    static string Multiple(double? value) =>
        value is { } stated ? SignedTwo(stated) : "<span class=\"degraded\">none yet</span>";

    // The run page's setups, one row a family in the page's order.
    public string FamilyRun(IReadOnlyList<FamilyRunRow> rows)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table family-run\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in new[]
        {
            ("Setup", "The setup family, in the page's order."),
            ("Rule", "Live since the day its rule was registered, or provisional while no freeze has registered it."),
            ("Variants", "The variants of its rule scored in the background beside it."),
            ("Listed tonight", "The stocks the page lists under it on this night."),
            ("Trades so far", "Every trade the page has listed under it, with how many are still open and how many finished."),
            ("Record", "Where its record stands against what it waits for. A provisional setup's record starts at its freeze."),
        })
        {
            body.Append(TippedHeading(heading, says, heading is "Variants" or "Listed tonight" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            body.Append(Invariant, $"<tr data-family=\"{Escaped(row.Family)}\" data-state=\"{(row.LiveSince is null ? "provisional" : "live")}\" data-live-since=\"{(row.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{row.Variants}\" data-listed=\"{row.ListedTonight}\" data-trades=\"{row.Trades}\" data-open=\"{row.Open}\" data-finished=\"{row.Finished}\">");
            body.Append(Invariant, $"<td class=\"setup\">{Escaped(row.Heading)}</td>");
            body.Append(row.LiveSince is { } live
                ? Formatted($"<td>live since {DayOf(live)}</td>")
                : "<td><span class=\"provisional\">provisional</span></td>");
            body.Append(Invariant, $"<td class=\"r num\">{row.Variants}</td><td class=\"r num\">{row.ListedTonight}</td>");
            body.Append(Invariant, $"<td>{Count(row.Trades, "trade")}, {row.Open} open and {row.Finished} finished</td>");
            body.Append(Invariant, $"<td class=\"family-record\">{Escaped(row.Record)}</td></tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // The registered rules of the new setups, one row a rule, each read over its own trades. From 19.7 the rules are
    // grouped under a heading row a family in its tint, each named by its short words with its full name, the session its
    // record counts from and why it restarted in the pointer's tooltip, beside its verdict in the newest per-rule test;
    // a level every rule of the family shares stands once in the family's heading and one that differs on its row.
    // see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
    public string FamilyRecords(IReadOnlyList<FamilyRecordRow> rows)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table family-records\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in new[]
        {
            ("Rule", "The registered rule by its short words, its live one first, each a variant otherwise; its full name, where its record counts from and why it restarted under the pointer."),
            ("Trades", "The trades its own list kept, five a night at most with one open a stock; a sector heavyweights rule's, the holdings its own book kept."),
            ("Decided", "Its trades ended with a result and the benchmark of the same plan on every member that night; a heavyweights rule's, its holdings sold, with its size cut's return over the same sessions."),
            ("Edge", "The average of each decided trade's result less its benchmark, in multiples of its risk; a heavyweights rule's in percentage points of the buy, its result less its size cut's return."),
            ("Whole blocks", "Blocks of 63 sessions whose trades have all had their cap, against the look they wait for; a heavyweights rule's holding counted in the block of the session it ended on."),
            ("Verdict", "The rule's verdict in the newest monthly per-rule test over the ledger's test years."),
        })
        {
            body.Append(TippedHeading(heading, says, heading is "Trades" or "Decided" or "Edge" or "Whole blocks" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var family in rows.GroupBy(row => row.Family))
        {
            var levels = family.Select(row => row.Level).Distinct().ToArray();
            double? shared = levels.Length == 1 ? levels[0] : null;
            var heading = family.First().Heading;

            body.Append(Invariant, $"<tr class=\"family-head\" data-family=\"{Escaped(family.Key)}\" data-level=\"{(shared is { } level ? level.ToString("0.#####", Invariant) : "differs")}\"><th colspan=\"6\"><span class=\"fam-tag\" data-family=\"{Escaped(family.Key)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(family.Key)}\"></span>{Escaped(heading)}</span>{(shared is { } one ? Formatted($" · its looks read at a level of {one.ToString("0.####", Invariant)}") : string.Empty)}</th></tr>");

            foreach (var row in family)
            {
                var tip = row.Rule
                    + (row.From is { } from ? ". Its record counts from " + DayOf(from) : string.Empty)
                    + (row.Restarted is { } restarted ? ". It restarted at its registration, since " + restarted : string.Empty)
                    + ".";

                body.Append(Invariant, $"<tr data-family=\"{Escaped(row.Family)}\" data-rule=\"{Escaped(row.Rule)}\" data-live=\"{Flag(row.Live)}\" data-trades=\"{row.Trades}\" data-decided=\"{row.Decided}\" data-edge=\"{(row.Edge is { } edge ? edge.ToString("0.###", Invariant) : "none")}\" data-blocks=\"{row.Blocks}\" data-look=\"{row.NextLook}\" data-level=\"{row.Level.ToString("0.#####", Invariant)}\" data-from=\"{(row.From is { } counted ? DayOf(counted) : "none")}\" data-restarted=\"{Flag(row.Restarted is not null)}\" data-verdict=\"{Escaped(row.Verdict ?? "none")}\">");
                body.Append(Invariant, $"<td class=\"fam-edge\" data-family=\"{Escaped(row.Family)}\" title=\"{Escaped(tip)}\"><span class=\"fam-tag\" data-family=\"{Escaped(row.Family)}\">{Escaped(row.Short ?? row.Rule)}</span>{(row.Live && row.Short is null ? " <b>live</b>" : string.Empty)}</td>");
                body.Append(Invariant, $"<td class=\"r num\">{row.Trades}</td><td class=\"r num\">{row.Decided}</td>");
                body.Append(Invariant, $"<td class=\"r num\">{(row.Edge is not { } shown ? "none yet" : row.Family == EquityBrief.Core.Families.HeavyweightRule.Name ? SignedTenth(shown * 100) + " points" : SignedFigure(shown, 3, Unsigned(shown, 3).ToString("+0.000;-0.000;0.000", Invariant)))}</td>");
                body.Append(Invariant, $"<td class=\"r num\">{row.Blocks} of {row.NextLook}{(shared is null ? Formatted($", level {row.Level.ToString("0.####", Invariant)}") : string.Empty)}</td>");
                body.Append(row.Verdict is { } verdict ? $"<td class=\"verdict-word\" data-rank=\"{EquityBrief.Core.Loop.RuleVerdict.Rank(verdict)}\">{Escaped(verdict)}</td></tr>" : "<td class=\"degraded\">not read</td></tr>");
            }
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // A name's line on the page's list for its night, a short-term dip pick's place read among the dip's own picks, which
    // its step numbers apart from every other family's place down the page.
    public string ListedUnder(ListedUnderView view)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<p class=\"listed-under\" data-night=\"{DayOf(view.Night)}\" data-family=\"{Escaped(view.Family ?? "none")}\" data-place=\"{(view.Place is { } at ? at.ToString(Invariant) : "none")}\" data-also=\"{Escaped(string.Join(",", view.Also))}\">");

        if (view.Family is not null)
        {
            body.Append(Invariant, $"On the page's list for {DayOf(view.Night)} under <b>{Escaped(view.Heading ?? view.Family)}</b>, at place {view.Place}{(view.Family == EquityBrief.Core.Families.DipRule.Name ? " among its own picks" : string.Empty)}");
            body.Append(view.Also.Count > 0
                ? Formatted($"; it also qualified as {Escaped(string.Join(" and ", view.Also.Select(Article)))}.")
                : ".");
        }

        foreach (var held in view.HeldBack)
        {
            body.Append(Invariant, $"{(view.Family is not null || held != view.HeldBack[0] ? " " : string.Empty)}{Escaped(held)}");
        }

        body.Append("</p>");

        return body.ToString();
    }

    // What a line says of an index's families' own market checks where they did not read alike: each family whose check
    // closed its list, with the check's own words, and that it left the others open; that every check closed every list,
    // each named, only where each did; and that each left its list open where none closed one.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    public static string ChecksSaid(IReadOnlyList<MarketCheckView> checks)
    {
        var closed = checks.Where(check => !check.Open).ToArray();

        static string Named(MarketCheckView check) => $"the {check.Setup.ToLowerInvariant()} list, {check.Words}";

        return closed.Length == 0
            ? "each setup's own market check left its list open"
            : closed.Length == checks.Count
                ? "the market check closed every list: " + string.Join("; ", closed.Select(Named))
                : "the market check closed " + string.Join("; and ", closed.Select(Named)) + ", and left the others open";
    }

    // The market line, the first of Tonight in one minute's lines from 22.6: whether the market check left the setups open
    // as a bold sentence, then the share of the index above its 200-day average against the floor the setups need and why
    // the check exists; where an index's setups each read a check of their own, how many left their lists open and each
    // that closed its list with what it needed; and the short-term dip's own setting. Every figure stands whole on the
    // element.
    public string MarketLine(MarketLineView line)
    {
        var body = new StringBuilder();
        var index = line.Index;
        var dipBuys = line.Dip is not null && line.DipOpen ? " but the short-term dip's own" : string.Empty;
        string head;
        string said;

        if (line.Checks is { } checks)
        {
            var closing = checks.Where(check => !check.Open).ToArray();

            head = closing.Length == 0
                ? "Each setup checks the market its own way here, and every check left its list open."
                : closing.Length == checks.Count
                    ? "The market checks are closed: no new buys tonight" + dipBuys + "."
                    : Formatted($"Each setup checks the market its own way here; {checks.Count - closing.Length} of {checks.Count} left their lists open.");
            said = (line.Breadth is { } read
                    ? Formatted($"{(read * 100).ToString("0.0", Invariant)}% of {index} stocks are above their 200-day average. ")
                    : "The share of stocks above their 200-day average could not be read tonight. ")
                + string.Concat(closing.Select(check => check.Floor is { } floor
                    ? Formatted($"The {check.Setup.ToLowerInvariant()}'s check needs {floor * 100:0.#}% and closed its list. ")
                    : Formatted($"The {check.Setup.ToLowerInvariant()}'s check closed its list. ")));
        }
        else
        {
            head = line.Open ? "The market is healthy enough to buy." : "The market check is closed: no new buys tonight" + dipBuys + ".";
            said = line.Breadth is { } breadth && line.Floor is { } floor
                ? Formatted($"{EquityBrief.Core.Cards.FloorShare.Percent(breadth, floor)}% of {index} stocks are above their 200-day average. The setups only buy when at least {floor * 100:0.#}% are, because buying when most stocks are falling has lost money in the past. ")
                : "The share of stocks above their 200-day average could not be read tonight. ";
        }

        var dipSaid = line.Dip is { } dip ? ReaderWords.Plain(dip) : null;

        said += dipSaid is { Length: > 0 } ? char.ToUpperInvariant(dipSaid[0]) + dipSaid[1..] + "." : string.Empty;

        body.Append(Invariant, $"<p class=\"market-line\" data-index=\"{Escaped(line.Index)}\" data-open=\"{Flag(line.Open)}\" data-breadth=\"{(line.Breadth is { } held ? held.ToString("R", Invariant) : "none")}\" data-floor=\"{(line.Floor is { } bar ? bar.ToString("R", Invariant) : "none")}\" ");
        body.Append(Invariant, $"data-passed=\"{(line.Passed is { } passed ? passed.ToString(Invariant) : "none")}\" data-members=\"{(line.Members is { } members ? members.ToString(Invariant) : "none")}\" ");
        body.Append(Invariant, $"data-buy-points=\"{line.BuyPoints}\" data-setups-listing=\"{line.SetupsListing}\" data-setups=\"{line.Setups}\" data-close=\"{(line.CloseRead ? line.Close.ToString(Invariant) : "none")}\" data-open-trades=\"{line.OpenTrades}\"");
        body.Append(line.Dip is null ? string.Empty : Formatted($" data-dip=\"{(line.DipOpen ? "open" : "closed")}\""));
        body.Append(line.Checks is { } closed ? Formatted($" data-closed=\"{Escaped(string.Join(" ", closed.Where(one => !one.Open).Select(one => one.Family)))}\">") : ">");

        // The short-term dip reads its own market setting, so a night the check closed the swing setups says the dip's
        // still buys where its own setting left it so, and every night says which setting the dip read.
        // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
        body.Append(Invariant, $"<b class=\"market-{(line.Open ? "open" : "closed")}\">{Escaped(head)}</b> {Escaped(said.TrimEnd())}</p>");

        return body.ToString();
    }

    // The one line a card draws under its standing while its family's recorded sweep found no setting that passed the
    // floors, and nothing otherwise; the card still lists its picks. Kept on a card frozen on that sweep, it adds the last
    // years its sweep stored in which the setting frozen there lost, read off the answer's own figures, both, one or none.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    // see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
    internal static string SweepFoundNoneLine(bool drawn, IReadOnlyList<int>? lost = null) =>
        !drawn
            ? string.Empty
            : lost is null
                ? $"<p class=\"family-sweep\" data-sweep=\"none passed\">{Escaped(ReaderWords.Plain(EquityBrief.Core.Sweep.SweepAnswer.Line))}.</p>"
                : Formatted($"<p class=\"family-sweep\" data-sweep=\"none passed\" data-lost=\"{(lost.Count == 0 ? "none" : string.Join(" ", lost))}\">{Escaped(ReaderWords.Plain(EquityBrief.Core.Sweep.SweepAnswer.Line + EquityBrief.Core.Sweep.SweepLine.LostLine(lost)))}.</p>");

    // The words a fixed card's figures close on where no setting its search read beat what it is measured against.
    public const string StartingPoint = "so treat it as a starting point rather than a proven edge";

    // The sentence a fixed card draws where the operator chose a setting bought on an entry its search did not keep.
    public const string EntryNotKept = "Its entry did not survive the search's second round, so it depends on its exact setting.";

    // A frozen card's figures, as the sweep its freeze followed stated them: the day it was frozen, the setting its sweep
    // would run or, where it passed none, the strongest it read, its trades or holdings, its edge after costs in risks or
    // in points for the sector heavyweights and that edge without its five largest results, how many of the years it
    // stored stood above nothing and its last two years' edges; then, where neither the count of settings against luck nor
    // the tester's verdict stored says a setting passed and either says none did, that none beat it, each figure it read
    // named, so the rule is a starting point and not a proven edge. Every stored value is whole on the element.
    // see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
    internal static string FrozenSweepLine(FrozenSweepView? frozen)
    {
        if (frozen is null)
        {
            return string.Empty;
        }

        var figures = frozen.Figures;
        var noun = frozen.Points ? "holding" : "trade";

        static string Whole(double? value) => value is { } held ? held.ToString("R", Invariant) : "none";

        static string Counted(int? value) => value is { } held ? held.ToString(Invariant) : "none";

        var passed = frozen.Answer == EquityBrief.Core.Sweep.SweepAnswer.PassedWord;
        var years = figures.YearEdges.OrderBy(year => year.Key).ToArray();
        var body = new StringBuilder();

        body.Append(Invariant, $"<p class=\"family-frozen\" data-frozen=\"{DayOf(frozen.FrozenOn)}\" data-run=\"{Escaped(frozen.Run)}\" data-answer=\"{Escaped(frozen.Answer)}\" data-setting=\"{Escaped(figures.Setting)}\" ");
        body.Append(Invariant, $"data-trades=\"{figures.Trades}\" data-edge=\"{Whole(figures.Edge)}\" data-years=\"{string.Join(" ", years.Select(year => year.Key.ToString(Invariant) + "=" + Whole(year.Value)))}\" data-without-largest=\"{Whole(figures.WithoutLargest)}\" ");
        body.Append(Invariant, $"data-settings-read=\"{Counted(figures.SettingsRead)}\" data-meeting=\"{Counted(figures.Meeting)}\" data-luck=\"{Whole(figures.Luck)}\" ");
        body.Append(Invariant, $"data-tester-run=\"{Escaped(figures.TesterRun ?? "none")}\" data-adjusted=\"{Whole(figures.Adjusted)}\" data-tester-passed=\"{(figures.TesterPassed is { } tested ? Flag(tested) : "none")}\" data-beat-luck=\"{(figures.BeatLuck is { } beat ? Flag(beat) : "none")}\" ");
        body.Append(Invariant, $"data-ruled=\"{(figures.RuledOn is { } ruledOn ? DayOf(ruledOn) : "none")}\" data-entry-kept=\"{(figures.EntryKept is { } entryKept ? Flag(entryKept) : "none")}\">");

        var at = figures.RuledOn is { } ruled
            ? FormattableString.Invariant($"the setting the operator chose on {ruled:d MMM yyyy} from among those its search read")
            : passed ? "the best setting its search over settings found" : "the strongest setting its search over settings read";

        // The setting is named by the chips beneath the sentence and kept whole on the element, never drawn by the search's key.
        string Short(double edge) => frozen.Points
            ? Formatted($"{(edge * 100).ToString("+0.00;-0.00;0.00", Invariant)} points")
            : Formatted($"about {(edge >= 0 ? "$" : "-$")}{Math.Abs(edge * 100).ToString("0.00", Invariant)}");

        string Made(double edge) => frozen.Points
            ? Short(edge)[..^" points".Length] + " percentage points against the same companies by size"
            : Short(edge) + " for every $100 risked beyond the same plan on every stock";

        body.Append(Invariant, $"Its settings were fixed on {frozen.FrozenOn:d MMM yyyy} at {at}. ");
        body.Append(Invariant, $"Over the history it was tested on, its {figures.Trades.ToString("#,0", Invariant)} {noun}{(figures.Trades == 1 ? string.Empty : "s")} came to {(figures.Edge is { } edge ? Made(edge) : "no stated result")} after costs");
        body.Append(figures.WithoutLargest is { } without ? Formatted($", or {Short(without)} without its five largest results") : string.Empty);
        body.Append(Invariant, $", and it came out ahead in {EquityBrief.Core.Sweep.SweepLine.YearsAbove(figures)} of the {years.Length} years");

        var last = EquityBrief.Core.Sweep.SweepLine.Last(figures);

        if (last.Count > 0)
        {
            body.Append(", ");
            body.Append(string.Join(" and ", last.Select(year => year.Edge is { } held ? Formatted($"{year.Year} at {(frozen.Points ? (held * 100).ToString("+0.00;-0.00;0.00", Invariant) + " points" : (held >= 0 ? "$" : "-$") + Math.Abs(held * 100).ToString("0.00", Invariant))}") : Formatted($"{year.Year} with no {noun}"))));
        }

        body.Append('.');

        // A chosen setting bought on an entry the search's second round did not keep says so.
        if (figures.RuledOn is not null && figures.EntryKept == false)
        {
            body.Append(' ');
            body.Append(Escaped(EntryNotKept));
        }

        // That no setting beat what it was read against, drawn only where neither measure stored says one did and one of
        // them says none did: the settings meeting its floors against luck, the tester's verdict on its search, or both.
        var luck = figures.BeatLuck;
        var tester = figures.TesterPassed;

        if (luck != true && tester != true && (luck == false || tester == false))
        {
            var against = new List<string>();

            if (luck == false)
            {
                against.Add(Formatted($"{figures.Meeting} of the {(figures.SettingsRead is { } read ? read.ToString(Invariant) + " " : string.Empty)}settings it tried met its floors, where luck alone would pass about {figures.Luck!.Value.ToString("0.#", Invariant)}"));
            }

            if (tester == false)
            {
                against.Add(figures.Adjusted is { } p
                    ? Formatted($"the tester put its search's chance of being luck at {p.ToString("0.0000", Invariant)}, against the {EquityBrief.Core.Loop.LoopGate.Bar.ToString("0.0000", Invariant)} it needs")
                    : "the tester read too little of its search to pass it");
            }

            var bar = luck == false && tester == false ? "what luck alone would produce or the tester's bar" : luck == false ? "what luck alone would produce" : "the tester's bar";

            body.Append(Invariant, $" The best of many tries always looks better than it is, and no setting it tried beat {bar}: {Escaped(string.Join(", and ", against))}, {StartingPoint}.");
        }

        body.Append("</p>");

        return body.ToString();
    }

    // A setup card's header, from 22.6: the setup's word in its tint beside its swatch, whether it is live and since when,
    // not yet tested, or a version chosen, tonight's count, the two sentences describing what it buys and why, and the
    // Showing list choosing the version the card is drawn by, the live setup first; the page's words at its foot read in
    // place of a guide the card once carried.
    // see: Each setup family has a hue of its own drawn as a mark and a tint beside its word, the Fundamentals family's among them
    // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
    public string FamilyCardHead(FamilyCardView card, string? about = null, string? count = null)
    {
        var (pill, pillClass, pillTitle) = Pill(card.LiveSince, card.RuleChoice);
        var variant = card.RuleChoice is { Chosen.Live: false } chosen ? chosen : null;

        count ??= variant is not null
            ? (variant.Picks?.Count ?? 0) switch { 0 => "This version finds no pick tonight", 1 => "1 pick under this version", var many => Formatted($"{many} picks under this version") }
            : card.Picks.Count switch { 0 => "No picks tonight", 1 => "1 pick tonight", var many => Formatted($"{many} picks tonight") };
        var body = new StringBuilder();

        body.Append(Invariant, $"<header class=\"fam-head\" data-family=\"{Escaped(card.Family)}\"><span class=\"fam-tag\" data-family=\"{Escaped(card.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(card.Family)}\"></span>{Escaped(card.Heading)}</span>");
        body.Append(Invariant, $" <span class=\"status-pill {pillClass}\"{(pillTitle is null ? string.Empty : Formatted($" title=\"{Escaped(pillTitle)}\""))}>{Escaped(pill)}</span>");
        body.Append(Invariant, $" <span class=\"fam-count\">{Escaped(count)}</span></header>");
        body.Append(Invariant, $"<p class=\"setup-about\">{Escaped(about ?? WordsOf(card.Family, card.Label).About)}</p>");

        if (card.RuleChoice is { } rule)
        {
            body.Append(RuleTabs(card.Family, rule));
        }

        return body.ToString();
    }

    // A card's standing as a pill: a chosen variant's dashed outline, the live rule's slate with the day it went live under
    // the pointer, or the provisional rule's amber.
    static (string Words, string Class, string? Title) Pill(DateOnly? liveSince, RuleView? rule) =>
        rule is { Chosen.Live: false } variant
            ? ("A version being tested", "variant", variant.Chosen.Rule)
            : liveSince is { } since
                ? (FormattableString.Invariant($"Live since {since:d MMM yyyy}"), "live", null)
                : ("Not yet tested", "provisional", EquityBrief.Core.Families.SetupFamilies.ProvisionalStatus);

    // Whether a card draws folded to one line: it lists nothing tonight and no variant is chosen on it.
    public static bool Folded(FamilyCardView card) => card.Picks.Count == 0 && card.RuleChoice is not { Chosen.Live: false };

    // One family's card: its header, the rule's tabs and chips with its sentence a click away, the live rule's picks as
    // rows of its own or a chosen variant's own picks, the notes, the funnel as one bar,
    // and the breakouts forming and the stocks one gate short behind expanders. A card listing nothing with no variant
    // chosen folds to one line, its name, its standing and why it lists nothing, every other part behind it.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
    public string FamilyCard(FamilyCardView card)
    {
        var body = new StringBuilder();
        var provisional = card.LiveSince is null;
        var variant = card.RuleChoice is { Chosen.Live: false } chosen ? chosen : null;
        var folded = Folded(card);

        body.Append(Invariant, $"<section class=\"family-card\" data-family=\"{Escaped(card.Family)}\" data-place=\"{card.Place}\" data-of=\"{card.Of}\" data-picks=\"{card.Picks.Count}\" ");
        body.Append(Invariant, $"data-state=\"{(provisional ? "provisional" : "live")}\" data-live-since=\"{(card.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{card.Variants}\" ");
        body.Append(Invariant, $"data-rule=\"{Escaped(card.RuleChoice?.Chosen.Slug ?? RuleScreenWords.LiveSlug)}\" data-variant=\"{(variant is null ? "none" : variant.Chosen.Variant!.Value.ToString(Invariant))}\" data-folded=\"{Flag(folded)}\">");

        // The summary, then the picks: a chosen version's own under its banner, or the live setup's with the notes on the
        // stocks it held back and its quiet nights.
        body.Append(CardSummary(card, variant));

        if (variant is not null)
        {
            body.Append(Invariant, $"<div class=\"variant-band\" role=\"status\" data-variant=\"{variant.Chosen.Variant}\"><p class=\"band-head\">A version being tested, not tonight's picks</p><p>{Escaped(VersionName(variant.Chosen))}</p><p class=\"band-note\">It runs every night beside the live setup with its own paper account. It replaces the live setup only if it proves better over enough trades and you approve it on the Loop page.</p></div>");
            body.Append(ShareLine(variant.Share, "picks"));
            body.Append(VariantPicks(card.Family, variant.Picks ?? [], card.Numbers));
        }
        else if (card.Picks.Count > 0)
        {
            body.Append(LivePicks(card.Picks));
        }

        // The notes beneath the live setup's picks alone: a chosen version is drawn as that version saw the night.
        if (variant is null)
        {
            foreach (var note in card.Notes)
            {
                body.Append(Invariant, $"<p class=\"family-note\">{WithSignedFigures(ReaderWords.Plain(note))}</p>");
            }
        }

        if (card.RuleChoice is { } drawn)
        {
            body.Append(StretchLine(drawn.Stretch, drawn.Evaluated));
        }

        // The folds, closed: the full rule and how its settings were chosen, how the stocks were narrowed, the stocks one
        // step short, the breakouts close to breaking out, every version side by side and how the setup has done.
        body.Append("<details class=\"fold fam-rule\"><summary>The full rule, and how its settings were chosen</summary><div class=\"fold-b\">");

        if (card.RuleChoice is { } rule)
        {
            body.Append(RuleChipsLine(rule));
        }

        body.Append(SweepFoundNoneLine(card.SweepFoundNone, card.SweepLost));

        // A fixed card's figures are its live setup's, drawn while the live setup is the one chosen.
        body.Append(variant is null ? FrozenSweepLine(card.Frozen) : string.Empty);
        body.Append(card.RuleChoice?.NoVariantLine is { } none ? Formatted($"<p class=\"no-variant\" data-no-variant=\"true\">{Escaped(ReaderWords.Plain(none))}</p>") : string.Empty);
        body.Append(card.RuleChoice?.Untested is { } untested ? Formatted($"<p class=\"untested-line\">{Escaped(ReaderWords.Plain(untested.Line))}</p>") : string.Empty);
        body.Append("</div></details>");

        if (card.RuleChoice is { } narrowed)
        {
            body.Append(FunnelBar(card.Family, narrowed));
            body.Append(FormingList(narrowed.Forming));
        }

        body.Append(CloseRows(card.Family, card.Close ?? [], card.Numbers));

        if (card.RuleChoice is { Choices.Count: > 1 } versions)
        {
            body.Append(VersionsSideBySide(versions));
        }

        body.Append(SetupRecord(card));
        body.Append("</section>");

        return body.ToString();
    }

    // A setup card's summary: what the chosen version would buy tonight; the live setup's picks with how many it held
    // back; or that it found none and why, its part of the night not computed, the market check closing its list, or how
    // far the stocks it read got with the one closest.
    static string CardSummary(FamilyCardView card, RuleView? variant)
    {
        if (variant is not null)
        {
            var picked = variant.Picks ?? [];

            return Formatted($"<p class=\"card-summary\" data-summary>{Escaped(picked.Count == 0 ? "This version finds no pick tonight." : (picked.Count == 1 ? "This version would buy 1 stock tonight: " : Formatted($"This version would buy {picked.Count} stocks tonight: ")) + string.Join(", ", picked.Select(pick => pick.Ticker)) + ".")}</p>");
        }

        if (card.Picks.Count > 0)
        {
            return Formatted($"<p class=\"card-summary\" data-summary>{Escaped(PickedSaid(card))}</p>");
        }

        return Formatted($"<p class=\"card-summary\" data-summary>{EmptyLine(card)}</p>");
    }

    // A card with picks in a sentence from its own counts: how many of the stocks its rule read qualified, out of how many
    // passed its first step and what that step asks, that its picks come first in its own order, and how many more
    // qualified, those a trade still open holds back among them; where its rule's counts were not read, its picks and
    // those held back alone.
    static string PickedSaid(FamilyCardView card)
    {
        var picks = card.Picks.Count;
        var funnel = card.RuleChoice?.Funnel?.Where(row => row.Gate != EquityBrief.Core.Families.FamilyRule.Market).ToArray();

        if (funnel is not { Length: > 0 } || card.RuleChoice?.Reached is not { } reached)
        {
            return (picks == 1 ? "It picked 1 stock tonight" : Formatted($"It picked {picks} stocks tonight"))
                + (card.HeldBack switch
                {
                    0 => string.Empty,
                    1 => "; 1 more qualified but was already bought on an earlier night, named beneath",
                    var held => Formatted($"; {held} more qualified but were already bought on earlier nights, named beneath"),
                })
                + ".";
        }

        var qualified = Math.Max(funnel[^1].Passed, picks);
        var first = funnel[0];
        var others = qualified - picks;
        var said = Formatted($"{qualified} of the {reached} stocks it read qualified tonight, out of {first.Passed} that passed its first step, {StepPassed(card.Family, first.Gate)}. ");

        said += picks == 1 ? "This one comes first in its own order." : Formatted($"These {picks} come first in its own order.");

        if (others > 0)
        {
            said += others == 1 ? " The other 1 also qualified" : Formatted($" The other {others} also qualified");
            said += card.HeldBack > 0 ? Formatted($": {card.HeldBack} already bought on earlier nights with their trades still open, named beneath with the rest.") : ", named beneath.";
        }

        return said;
    }

    // A version's name by what it changes, from the settings it differs from the live setup on.
    static string VersionName(RuleChoiceView choice) =>
        choice.Live
            ? choice.Tab == "Provisional" ? "The setup as it runs, not yet tested" : "The live setup"
            : "Version: " + ReaderWords.Plain(System.Text.RegularExpressions.Regex.Replace(choice.Tab ?? choice.Rule, @"^V\d+ · ", string.Empty).Replace(" · ", ", ", StringComparison.Ordinal));

    // Every version beside the live setup, each by what it changes, with the press choosing it.
    static string VersionsSideBySide(RuleView rule)
    {
        var body = new StringBuilder();
        var versions = rule.Choices.Where(choice => !choice.Live).ToArray();

        body.Append(Invariant, $"<details class=\"fold fam-versions\" data-versions=\"{versions.Length}\"><summary>{(versions.Length == 1 ? "The 1 version being tested" : Formatted($"All {versions.Length} versions being tested, side by side"))}</summary><div class=\"fold-b\">");
        body.Append("<p class=\"versions-said\">Each runs every night beside the live setup with its own paper account, and none is tonight's pick. Choose one in Showing above to draw what it would buy tonight; its record is on Paper trading.</p><ul class=\"versions\">");

        foreach (var choice in versions)
        {
            body.Append(Invariant, $"<li data-variant=\"{(choice.Variant is { } number ? number.ToString(Invariant) : "none")}\"{(choice.Slug == rule.Chosen.Slug ? " class=\"current\"" : string.Empty)}>{Escaped(VersionName(choice)[("Version: ").Length..])}</li>");
        }

        body.Append("</ul></div></details>");

        return body.ToString();
    }

    // How the setup has done, as the pick cards it draws state the record of its rule over every stock it bought, or
    // where it holds none, where its record stands.
    // see: A rule's record is drawn under the rule's own heading, beside its reason or on a pick's card and never as the stock's own
    string SetupRecord(FamilyCardView card)
    {
        var body = new StringBuilder();

        body.Append("<details class=\"fold fam-record\"><summary>How this setup has done</summary><div class=\"fold-b\">");

        if (card.Picks.Select(pick => pick.Card?.Record).FirstOrDefault(record => record is not null) is { } record)
        {
            body.Append(RecordSentence(record, card.Label));

            // Against the same plan on any stock that day, as the setups like a pick read the setup's own finished setups.
            if (card.Picks.Select(pick => pick.Card?.Score?.Similar).FirstOrDefault(similar => similar?.RuleMean is not null) is { RuleMean: { } usual } similar)
            {
                body.Append(Invariant, $"<p class=\"record-said\" data-rule-mean=\"{usual.ToString("R", Invariant)}\">Against the same plan on any stock that day, its {similar.RuleSetups ?? 0:#,0} finished setups came to {PerHundred(usual)} for every $100 risked, before costs.</p>");
            }
        }
        else if (card.Frozen is { } frozen)
        {
            body.Append(Invariant, $"<p class=\"record-said\">{Escaped(FrozenRecordSaid(frozen))}</p>");
        }
        else
        {
            body.Append("<p class=\"record-said\">No record is drawn here tonight: a pick's card states the setup's record, and every version's is on Paper trading.</p>");
        }

        var progress = card.Progress is { } held
            ? Formatted($" Since then it has finished {held.Counted:#,0} of the {held.Needed:#,0} trades a verdict needs.")
            : Formatted($" A verdict needs {EquityBrief.Core.Loop.LoopScreens.SwingTrades:#,0} finished trades.");

        body.Append(Invariant, $"<p class=\"record-said\" data-finished=\"{(card.Progress is { } counted ? counted.Counted.ToString(Invariant) : "none")}\">{(card.LiveSince is { } since ? FormattableString.Invariant($"It went live on {since:d MMM yyyy}.{progress} Its trades are on <a href=\"#/paper\">Paper trading</a>.") : "It has not been tested yet, so its picks count toward nothing; its trades are on <a href=\"#/paper\">Paper trading</a>.")}</p>");
        body.Append(Invariant, $"<p class=\"record-note\">{Escaped(RecordIsTheRules)}</p>");
        body.Append("</div></details>");

        return body.ToString();
    }

    // What each column of a card's own picks holds, the plan's columns in the buy points' own words.
    public static IReadOnlyList<(string Heading, string Says)> CardPickHeadings =>
    [
        ("#", "The pick's place in this setup's own order tonight."),
        ("Stock", "The ticker opens the stock's chart page, and the company is named beneath it."),
        ("Why it's here", "What the stock did that this setup buys, from the figures the night stored."),
        .. TomorrowHeadings.Where(heading => heading.Heading is "Buy about" or "Stop" or "Target" or "Risk $1 to make"),
    ];

    // The live rule's picks as rows of the card's own, the way the heavyweights card draws its holdings: each in the
    // setup's own order with its ticker opening the stock's chart page, its business state and its plan, read from the
    // same pick cells the buy points draw so the two tables state the same figures.
    // see: A setup's card draws its live picks as rows of its own, each ticker opening its chart page
    string LivePicks(IReadOnlyList<FamilyPickCell> picks)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table card-picks\" data-picks=\"{picks.Count}\"><thead><tr>");

        foreach (var (heading, says) in CardPickHeadings)
        {
            body.Append(TippedHeading(heading, says, heading switch
            {
                "#" => "place",
                "Buy about" or "Stop" or "Target" or "Risk $1 to make" => "r",
                _ => null,
            }));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var (pick, place) in picks.Select((pick, at) => (pick, at + 1)))
        {
            var row = pick.Row;

            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-card-pick=\"true\"><td class=\"place\" data-place=\"{place}\">{place}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{(row.Distance?.Name is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            body.Append(Invariant, $"<td class=\"why\">{Escaped(ReaderWords.Plain(StoredWords(pick.Why)))}</td>");
            body.Append(PriceCell("buy", pick.Buy, null));
            body.Append(PriceCell("stop", pick.Stop, null));
            body.Append(PriceCell("target", pick.Target, pick.Trailing ? "rising stop, no target" : pick.Exit is { } exit ? ReaderWords.Plain(exit) : null));
            body.Append(RewardCell(pick));
            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // A card listing nothing tonight in one line: why it lists nothing, the gate that stopped the most of the members
    // reaching it, and its empty stretch against its usual longest, or that no usual stretch is read yet under the floors.
    // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
    static string EmptyLine(FamilyCardView card)
    {
        // Where the market check closed the list the line says so, its figures standing in tonight in one minute above.
        var closed = card.Empty is { } closing && closing.StartsWith("The market check closed", StringComparison.Ordinal);
        var said = card.NotComputed
            ? card.Empty is { Length: > 0 } failed ? "No picks tonight. " + Capital(ReaderWords.Plain(failed)) : "No picks tonight: its part of the night was not computed."
            : closed ? "No picks tonight: the market check closed its list." : "No picks tonight.";

        if (card.NotComputed)
        {
            return Formatted($"<span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">{Escaped(said)}</span>");
        }

        // Stocks that qualified and are held back by a trade still open from an earlier night, named in the notes beneath.
        if (card.HeldBack > 0)
        {
            said += card.HeldBack == 1
                ? " 1 stock qualified but was already bought on an earlier night and its trade is still open, named beneath."
                : Formatted($" {card.HeldBack} stocks qualified but were already bought on earlier nights and their trades are still open, named beneath.");
        }

        if (card.RuleChoice?.Funnel is { Count: > 0 } funnel && EquityBrief.Core.Cards.RuleFunnelDrop.Largest(funnel) is { } drop)
        {
            said += FormattableString.Invariant($" The step most stocks missed: {StepMissed(card.Family, drop.Gate)}, {drop.Stopped} of the {drop.Reached} that got that far.");

            // How far the rest got: the last step any stock passed, and how many passed it, where some passed the step most
            // missed and went on.
            var dropAt = funnel.ToList().FindIndex(row => row.Gate == drop.Gate);
            var lastAt = funnel.ToList().FindLastIndex(row => row.Passed > 0 && row.Gate != EquityBrief.Core.Families.FamilyRule.Market);

            if (dropAt >= 0 && lastAt >= dropAt)
            {
                said += FormattableString.Invariant($" The furthest any got: {StepPassed(card.Family, funnel[lastAt].Gate)}, {funnel[lastAt].Passed} of them.");
            }
        }
        else if (!closed && card.HeldBack == 0 && card.Empty is { Length: > 0 } why)
        {
            // Where no rule's counts were stored for the setup, why it lists none as the night's answers say it.
            const string NonePassed = "No stock passed this setup tonight: ";
            var plain = ReaderWords.Plain(why.StartsWith(NonePassed, StringComparison.Ordinal) ? why[NonePassed.Length..] : why);

            said += " " + Capital(plain);
        }
        else if (card.RuleChoice is { Evaluated: false } && card.HeldBack == 0 && !closed)
        {
            said += " The night did not read this setup.";
        }

        if (card.Close is { Count: > 0 } close)
        {
            said += FormattableString.Invariant($" The closest, {close[0].Ticker}, missed one step: {StepMissed(card.Family, close[0].Gate, close[0].Words)}.");
        }

        var flagged = card.RuleChoice?.Stretch is { Flagged: true };

        return Formatted($"<span class=\"family-empty-line{(flagged ? " flagged" : string.Empty)}\" data-picks=\"0\" data-flagged=\"{Flag(flagged)}\">{Escaped(said)}</span>");
    }

    // A step a stock missed in the reader's words, the build's own name rewritten where the word list holds none for it,
    // and for one stock the words its night stored beside the step deciding which way it was missed.
    static string StepMissed(string family, string step, string? stored = null) => ReaderWords.MissedBy(family, step, stored) ?? ReaderWords.Plain(step);

    // A step a stock passed in the reader's words.
    static string StepPassed(string family, string step) => ReaderWords.Passed(family, step) ?? ReaderWords.Plain(step);

    // The card's Showing list: the live setup first, then each version named by what it changes, the full rule under the
    // pointer, the choice kept in the link under the family's own key.
    static string RuleTabs(string family, RuleView rule)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"rule-view\"><label for=\"rule-{Escaped(family)}\">Showing</label> <select id=\"rule-{Escaped(family)}\" class=\"rule-select\" data-rule-choice=\"{Escaped(family)}\" aria-label=\"The version this card is drawn by\">");

        foreach (var choice in rule.Choices)
        {
            var value = choice.Live ? RuleScreenWords.LiveSlug : choice.Slug;
            var current = choice.Slug == rule.Chosen.Slug;

            body.Append(Invariant, $"<option value=\"{Escaped(value)}\" data-variant=\"{(choice.Variant is { } number ? number.ToString(Invariant) : "none")}\" title=\"{Escaped(choice.Rule)}\"{(current ? " selected" : string.Empty)}>{Escaped(VersionName(choice))}</option>");
        }

        body.Append("</select></div>");

        return body.ToString();
    }

    // The chosen version's rule in a sentence, its clauses the live setup's words lack marked, and its settings as chips,
    // a chosen version's differing ones marked.
    static string RuleChipsLine(RuleView rule)
    {
        var body = new StringBuilder();

        body.Append(rule.Words is { } words && !rule.Parts.Any(part => part.Differs)
            ? $"<p class=\"rule-words\">{WithSignedFigures(ReaderWords.Plain(words))}</p>"
            : RuleWordsLine(rule.Parts));

        if (rule.Chips is { Count: > 0 } chips)
        {
            body.Append(Invariant, $"<ul class=\"rule-chips\" data-chips=\"{chips.Count}\">");

            foreach (var chip in chips)
            {
                body.Append(Invariant, $"<li class=\"rule-chip{(chip.Differs ? " differs" : string.Empty)}\">{WithSignedFigures(ReaderWords.Plain(chip.Words))}</li>");
            }

            body.Append("</ul>");
        }

        return body.ToString();
    }

    // A chosen version's picks or holdings beside the live setup's tonight, in one line.
    static string ShareLine(RuleShareView? share, string what)
    {
        if (share is null)
        {
            return string.Empty;
        }

        static string Named(IReadOnlyList<string> tickers) => tickers.Count == 0 ? "none" : string.Join(", ", tickers);

        var against = share.Against == "live" ? "the live setup's" : "the starting setup's";

        return Formatted($"<p class=\"rule-share\" data-shared=\"{Escaped(string.Join(",", share.Shared))}\" data-added=\"{Escaped(string.Join(",", share.Added))}\" data-dropped=\"{Escaped(string.Join(",", share.Dropped))}\">")
            + Escaped(FormattableString.Invariant($"Beside {against} {(what == "picks" ? "qualifying stocks" : "holdings")} tonight, it shares {Named(share.Shared)}, adds {Named(share.Added)} and drops {Named(share.Dropped)}."))
            + "</p>";
    }

    // The chosen version's words in clauses, each the live setup's words lack marked.
    static string RuleWordsLine(IReadOnlyList<RulePartView> parts) =>
        "<p class=\"rule-words\">" + string.Join(", ", parts.Select(part => part.Differs ? $"<mark class=\"differs\">{WithSignedFigures(ReaderWords.Plain(part.Text))}</mark>" : WithSignedFigures(ReaderWords.Plain(part.Text)))) + ".</p>";

    // A family's three numbers for a row, each in its own cell, a number the stored rows do not hold drawn as none with why.
    static string NumberCells(string family, string ticker, IReadOnlyDictionary<string, IReadOnlyList<EquityBrief.Core.Cards.CardCell>>? numbers)
    {
        var columns = EquityBrief.Core.Cards.CardNumbers.Of(family);
        var cells = numbers is not null && numbers.TryGetValue(ticker, out var held) ? held : null;

        return string.Concat(columns.Select((column, at) =>
        {
            var cell = cells is not null && at < cells.Count ? cells[at] : null;

            return cell?.Value is { } value
                ? Formatted($"<td class=\"r num\" data-number=\"{Escaped(column.Column)}\" data-value=\"{value.ToString("R", Invariant)}\">{CardNumber(column.Form, value)}</td>")
                : Formatted($"<td class=\"r num\" data-number=\"{Escaped(column.Column)}\" data-value=\"none\" data-none=\"{Escaped(cell?.None ?? EquityBrief.Core.Cards.CardNumbers.NotRead)}\"><span class=\"degraded\">none, {Escaped(cell?.None ?? EquityBrief.Core.Cards.CardNumbers.NotRead)}</span></td>");
        }));
    }

    static string NumberHeadings(string family) =>
        string.Concat(EquityBrief.Core.Cards.CardNumbers.Of(family).Select(column => TippedHeading(column.Heading, column.Says, "r")));

    // A number in its form: typical moves and multiples to two places, a share as a whole per cent and sessions whole.
    static string CardNumber(EquityBrief.Core.Cards.CardNumberForm form, double value) => form switch
    {
        EquityBrief.Core.Cards.CardNumberForm.Moves => value.ToString("0.00", Invariant) + " moves",
        EquityBrief.Core.Cards.CardNumberForm.Times => value.ToString("0.00", Invariant) + " times",
        EquityBrief.Core.Cards.CardNumberForm.Share => (value * 100).ToString("0", Invariant) + "%",
        EquityBrief.Core.Cards.CardNumberForm.Sessions => value.ToString("0", Invariant),
        _ => value.ToString("0.00", Invariant),
    };

    // A variant's own picks, drawn under the band: each with its plan and the family's three numbers in place of words.
    string VariantPicks(string family, IReadOnlyList<VariantPickView> picks, IReadOnlyDictionary<string, IReadOnlyList<EquityBrief.Core.Cards.CardCell>>? numbers)
    {
        if (picks.Count == 0)
        {
            return "<p class=\"degraded variant-empty\" data-picks=\"0\">This version would buy nothing tonight.</p>";
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table variant-table\" data-rows=\"{picks.Count}\"><thead><tr>");
        body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\" class=\"r\">Buy about</th><th scope=\"col\" class=\"r\">Stop</th><th scope=\"col\" class=\"r\">Target</th><th scope=\"col\" class=\"r\">Risk $1 to make</th>");
        body.Append(NumberHeadings(family));
        body.Append("</tr></thead><tbody>");

        foreach (var pick in picks)
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(pick.Ticker)}\" data-variant-pick=\"true\"><td class=\"place\">{pick.Place}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(pick.Ticker)}\">{Escaped(pick.Ticker)}</a></td>");
            body.Append(Invariant, $"<td class=\"r\" data-entry=\"{pick.Entry.ToString(Invariant)}\">{Price(pick.Entry)}</td>");
            body.Append(Invariant, $"<td class=\"r\" data-stop=\"{pick.Stop.ToString(Invariant)}\">{Price(pick.Stop)}</td>");
            body.Append(Invariant, $"<td class=\"r\">{(pick.Target is { } target ? Price(target) : "rising stop")}</td>");
            body.Append(Invariant, $"<td class=\"r\">{(pick.RewardToRisk is { } reward ? "$" + reward.ToString("0.00", Invariant) : "none")}</td>");
            body.Append(NumberCells(family, pick.Ticker, numbers));
            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // The stocks one gate short of the family's buy point tonight, behind an expander: each with the gate it missed and
    // the family's three numbers. It recommends nothing.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    string CloseRows(string family, IReadOnlyList<CloseToBuyCell> rows, IReadOnlyDictionary<string, IReadOnlyList<EquityBrief.Core.Cards.CardCell>>? numbers)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<details class=\"expander close-rows\" data-close=\"{rows.Count}\"><summary>Stocks one step short ({rows.Count})</summary>");
        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table close-table\" data-rows=\"{rows.Count}\"><thead><tr>");
        body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\">The step it missed</th>");
        body.Append(NumberHeadings(family));
        body.Append("</tr></thead><tbody>");

        foreach (var (row, at) in rows.Select((row, at) => (row, at + 1)))
        {
            var missed = StepMissed(family, row.Gate, row.Words);

            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-gate=\"{Escaped(row.Gate)}\"><td class=\"place\">{at}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{(row.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            body.Append(Invariant, $"<td class=\"missed-gate\" title=\"{Escaped(ReaderWords.Plain(StoredWords(row.Words)))}\"><b>{Escaped(char.ToUpperInvariant(missed[0]) + missed[1..])}</b></td>");
            body.Append(NumberCells(family, row.Ticker, numbers));
            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");
        body.Append("<p class=\"forming-key\">Each passed every step of this setup but one tonight, with nothing ruling it out. None is a pick.</p></details>");

        return body.ToString();
    }

    // A rule's funnel as one bar a gate, each as long as the share of the members reaching the gate that passed it, with
    // how many passed of how many reached it beside it: the first gate reached by the members the night read for the
    // rule, and each gate after it by those the gate before passed. The gate that stopped the most of those reaching it is
    // marked and, where the card does not say it already, named beneath; a first gate passing no member, as a closed
    // market check, is the one marked, since the funnel holds no fall before it. The market check's row is left out while
    // it is open, since it then passes every member read, and a first gate whose funnel holds no count of the members read
    // says how many passed it alone.
    static string FunnelBar(string family, RuleView rule)
    {
        if (rule.Funnel is not { Count: > 0 } funnel)
        {
            return rule.Evaluated ? string.Empty : "<p class=\"degraded rule-unevaluated\" data-evaluated=\"false\">The night did not read this setup.</p>";
        }

        var drop = EquityBrief.Core.Cards.RuleFunnelDrop.Largest(funnel);
        var shut = funnel[0].Passed == 0 ? funnel[0].Gate : null;
        var openMarket = funnel[0].Gate == EquityBrief.Core.Families.FamilyRule.Market && funnel[0].Passed > 0;
        var first = openMarket && funnel.Count > 1 ? funnel[0].Passed : rule.Reached;
        var body = new StringBuilder();

        body.Append(Invariant, $"<details class=\"fold fam-funnel\"><summary>{(first is { } read ? Formatted($"How the {read} stocks it read were narrowed to {funnel[^1].Passed}") : Formatted($"How the stocks it read were narrowed to {funnel[^1].Passed}"))}</summary><div class=\"fold-b\">");
        body.Append("<p class=\"funnel-said\">Each step keeps only the stocks that passed the step above it.</p>");
        body.Append(Invariant, $"<div class=\"funnel-bar\" data-gates=\"{funnel.Count}\" data-largest-drop=\"{Escaped(drop?.Gate ?? shut ?? "none")}\" data-market-row=\"{Flag(!openMarket)}\">");
        body.Append("<ol class=\"rule-funnel\">");

        var drawn = 0;

        foreach (var ((gate, passed), at) in funnel.Select((step, at) => (step, at)))
        {
            if (at == 0 && openMarket)
            {
                continue;
            }

            var reached = at > 0 ? funnel[at - 1].Passed : rule.Reached;
            var share = reached is > 0 ? Math.Clamp(1.0 * passed / reached.Value, 0, 1) : 0;
            var marked = drop is not null ? drop.Gate == gate : shut is not null && at == 0;
            var words = reached is { } of ? Formatted($"{passed} of {of}") : passed.ToString(Invariant);
            var step = StepPassed(family, gate);
            var name = drawn == 0 ? char.ToUpperInvariant(step[0]) + step[1..] : "Of those, " + step;

            body.Append(Invariant, $"<li data-gate=\"{Escaped(gate)}\" data-passed=\"{passed}\" data-reached=\"{(reached is { } count ? count.ToString(Invariant) : "none")}\"{(marked ? " class=\"largest-drop\"" : string.Empty)}><span class=\"fb-name\">{Escaped(name)}</span><span class=\"fb-bar\"><span class=\"fb-fill\" style=\"width:{(share * 100).ToString("0.#", Invariant)}%\"></span></span><b class=\"fb-count\">{words}</b></li>");
            drawn++;
        }

        body.Append("</ol>");
        body.Append(drop is not null
            ? Formatted($"<p class=\"funnel-drop\" data-drop=\"{Escaped(drop.Gate)}\">The step most stocks missed: {Escaped(StepMissed(family, drop.Gate))}, {drop.Stopped} of the {drop.Reached} that got that far.</p>")
            : shut is not null
                ? Formatted($"<p class=\"funnel-drop\" data-drop=\"{Escaped(shut)}\">No stock got past the first step tonight: {Escaped(StepMissed(family, shut))}.</p>")
                : "<p class=\"funnel-drop\" data-drop=\"none\">No step stopped a stock tonight.</p>");
        body.Append("</div></div></details>");

        return body.ToString();
    }

    // The stretch line: how many nights the rule has listed nothing for and the mark its past empty nights set, flagged
    // past the mark; no mark under the floors, with how far the floors are from being met.
    // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
    static string StretchLine(StretchLineView? stretch, bool evaluated)
    {
        if (stretch is null)
        {
            return string.Empty;
        }

        var words = stretch.Stretch == 0
            ? "Quiet nights: it found picks tonight; "
            : FormattableString.Invariant($"Quiet nights: this is its {Ordinal(stretch.Stretch)} quiet night in a row; ");
        var usual = stretch.Mark is { } level
            ? stretch.Stretch == 0
                ? FormattableString.Invariant($"when it finds none, a run of up to {level} quiet nights is normal for this setup")
                : FormattableString.Invariant($"runs of up to {level} have happened before{(stretch.Flagged ? ", so this one is longer than usual" : string.Empty)}")
            : FormattableString.Invariant($"it is too new to know a normal quiet run, {stretch.Completed} of the {EquityBrief.Core.Cards.RuleStretch.CompletedStretchesFloor} runs it needs having ended");

        return FormattableString.Invariant($"<p class=\"stretch-line{(stretch.Flagged ? " flagged" : string.Empty)}\" data-stretch=\"{stretch.Stretch}\" data-mark=\"{(stretch.Mark is { } held ? held.ToString(Invariant) : "none")}\" data-flagged=\"{Flag(stretch.Flagged)}\" data-completed=\"{stretch.Completed}\" data-sessions=\"{stretch.Sessions}\">")
            + Escaped(words + usual + ".")
            + "</p>";
    }

    // The breakouts forming under a breakout rule: the rows drawn of the count forming, nearest misses first, each with
    // the price it would have to close above, the volume the rule would need against tonight's, its ranges' ratio, the
    // gates it still fails and its next earnings date where one falls within the stated sessions; a line where the
    // market check closed the rule's list; and the closing line saying what the rule would read that night.
    // see: The forming list advises and never lists a stock
    static string FormingList(FormingListView? forming)
    {
        if (forming is null)
        {
            return string.Empty;
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<details class=\"expander forming\" data-forming=\"{forming.Forming}\" data-drawn=\"{forming.Rows.Count}\" data-market-open=\"{Flag(forming.MarketOpen)}\">");
        body.Append(Invariant, $"<summary>Close to breaking out ({forming.Forming}{(forming.Rows.Count < forming.Forming ? FormattableString.Invariant($", the nearest {forming.Rows.Count} shown") : string.Empty)})</summary>");

        if (!forming.MarketOpen)
        {
            body.Append("<p class=\"degraded forming-closed\" data-market-open=\"false\">The market check closed this setup's list tonight, so it would pick none of these whatever they did.</p>");
        }

        if (forming.Rows.Count == 0)
        {
            body.Append("<p class=\"forming-empty\" data-rows=\"0\">No stock is close to breaking out tonight.</p>");
        }
        else
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table forming-table\" data-rows=\"{forming.Rows.Count}\"><thead><tr>");
            body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\" class=\"r\">Closed at</th><th scope=\"col\" class=\"r\">Needs to close above</th><th scope=\"col\" class=\"r\">How far</th><th scope=\"col\" class=\"r\">Volume it needs</th><th scope=\"col\" class=\"r\">Volume tonight</th><th scope=\"col\" class=\"r\">Ranges</th><th scope=\"col\">Still missing</th><th scope=\"col\">Next report</th>");
            body.Append("</tr></thead><tbody>");

            foreach (var row in forming.Rows)
            {
                body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-forming-row=\"true\" data-high=\"{row.High.ToString(Invariant)}\" data-volume-needed=\"{row.VolumeNeeded.ToString("0", Invariant)}\"><td class=\"place\">{row.Place}</td>");
                body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>");
                body.Append(Invariant, $"<td class=\"r\">{Price(row.Close)}</td><td class=\"r\">{Price(row.High)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{row.MovesUnder.ToString("0.00", Invariant)} days' moves</td>");
                body.Append(Invariant, $"<td class=\"r\">{Shares(row.VolumeNeeded)}</td><td class=\"r\">{Shares(row.Volume)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{row.RangeRatio.ToString("0.00", Invariant)}</td>");
                body.Append(Invariant, $"<td>{Escaped(string.Join(", ", row.Missing.Select(missing => StepMissed(EquityBrief.Core.Families.BreakoutRule.Name, missing))))}</td>");
                body.Append(Invariant, $"<td>{(row.NextEarnings is { } next ? DayOf(next) : "no date on file")}</td></tr>");
            }

            body.Append("</tbody></table></div>");
        }

        body.Append(Invariant, $"<p class=\"forming-key\">{Escaped(RuleScreenWords.FormingClosingLine)}</p>");
        body.Append("</details>");

        return body.ToString();
    }

    static string Shares(double volume) => volume >= 1_000_000 ? (volume / 1_000_000).ToString("0.0", Invariant) + "M" : volume >= 1_000 ? (volume / 1_000).ToString("0", Invariant) + "K" : volume.ToString("0", Invariant);

    static string Count(int count, string noun) => Formatted($"{count} {noun}{(count == 1 ? string.Empty : "s")}");

    // A pick's name cell as tonight's buy points draw it, carrying what a list row's does: the other setups it qualified
    // under, the control opening its fold, the report it holds or the control asking for one, what the queue holds for it,
    // the company and a suspect series.
    string PickNameCell(FamilyPickCell pick, string control)
    {
        var row = pick.Row;
        var cells = new StringBuilder();

        cells.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>");

        foreach (var also in pick.Also)
        {
            cells.Append(Invariant, $" <span class=\"also-family\" data-also=\"{Escaped(also)}\">also {Escaped(Article(also))}</span>");
        }

        cells.Append(control);

        var researched = row.ResearchedOn is not null;

        cells.Append(Invariant, $" <a class=\"open{(researched ? string.Empty : " unwritten")}\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\" ");
        cells.Append(Invariant, $"data-report-state=\"{ReportState(row)}\" data-researched=\"{(researched ? "true" : "false")}\"");

        if (row.ResearchedOn is { } on)
        {
            cells.Append(Invariant, $" data-researched-on=\"{on:yyyy-MM-dd}\" title=\"open the report, written {on:yyyy-MM-dd}\">report</a>");
        }
        else
        {
            cells.Append(" title=\"open the name, whose researched sections are not written\">not written</a>");

            if (row.Queue is null)
            {
                cells.Append(AskForAReport(row.Ticker));
            }
        }

        if (row.Queue is { } queued)
        {
            cells.Append(Invariant, $"<span class=\"report-state\" data-report-state=\"{Escaped(queued.State)}\" data-at=\"{Escaped(queued.At ?? string.Empty)}\">{Escaped(queued.Words)}</span>");
        }

        if (row.Distance?.Name is { Length: > 0 } company)
        {
            cells.Append(Invariant, $"<span class=\"co\">{Escaped(company)}</span>");
        }

        cells.Append(row.Suspect is { } suspect
            ? $" <span class=\"prices-suspect\" data-last-asked-at=\"{Escaped(suspect.LastAskedAt)}\" title=\"last tried {Escaped(suspect.LastAskedAt)}, because {Escaped(suspect.Reason)}\">prices may not reflect a dividend or split</span></td>"
            : "</td>");

        return cells.ToString();
    }

    // A pick's business state as its night stored it, with what its numbers say under the pointer.
    static string PickBusinessCell(FamilyPickCell pick) =>
        pick.Row.Business is null
            ? "<td class=\"business-cell\"><span class=\"degraded\" data-state=\"none\">not read</span></td>"
            : $"<td class=\"business-cell\">{BusinessWord(pick.Row.Business)}</td>";

    // A pick's stories over the thirty days before the night as the labeller counted them, in words: so many good and so
    // many bad. Where it holds no label, an S&P 500 pick says so and an S&P 400's or 600's says why, the labeller reading
    // the S&P 500's list alone.
    // see: The news labeller reads the S&P 500's list alone until the operator widens it
    static string PickNewsCell(FamilyPickCell pick, bool labelled) =>
        pick.Row.NewsPositive is { } positive && pick.Row.NewsNegative is { } negative
            ? Formatted($"<td class=\"news-counts\" data-positive=\"{positive}\" data-negative=\"{negative}\"><span class=\"np {(positive > 0 ? "gain" : "flat")}\">{positive} good</span> · <span class=\"nn {(negative > 0 ? "loss" : "flat")}\">{negative} bad</span></td>")
            : $"<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">{(labelled ? "no label" : "News labels cover S&amp;P 500 picks only")}</span></td>";

    // A label's family with its article, as "also a sector leader" reads.
    static string Article(string label) =>
        label.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(label[0]), StringComparison.Ordinal) ? "an " + label.ToLowerInvariant() : "a " + label.ToLowerInvariant();

    static string PriceCell(string kind, decimal? price, string? instead) =>
        price is { } stated
            ? Formatted($"<td class=\"r num plan-{kind}\" data-{kind}=\"{stated.ToString(Invariant)}\">{Figures.Price(stated)}</td>")
            : $"<td class=\"r num plan-{kind}\" data-{kind}=\"none\">{(instead is null ? "<span class=\"degraded\">none</span>" : Escaped(instead))}</td>";
}
