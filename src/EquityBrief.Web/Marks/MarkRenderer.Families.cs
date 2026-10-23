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
    IReadOnlyDictionary<string, IReadOnlyList<double?>>? Numbers = null,
    IReadOnlyList<int>? SweepLost = null,
    FrozenSweepView? Frozen = null,
    bool NotComputed = false);

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

    // The market line: the check's answer in words with both figures, then the night's counts, the open
    // trades linking to Past picks. Where an index's families each read a check of their own and they did not read
    // alike, the index's breadth and each family's check that closed its list, in the check's own words.
    public string MarketLine(MarketLineView line)
    {
        var check = line.Checks is { } checks
            ? (line.Breadth is { } read
                ? Formatted($"the {line.Index}'s breadth: {(read * 100).ToString("0.0", Invariant)}% of its members closed above their 200-day average")
                : $"the {line.Index}'s breadth is not available tonight") + "; " + ChecksSaid(checks)
            : line.Breadth is { } breadth && line.Floor is { } floor
                ? Formatted($"the {line.Index}'s breadth: {EquityBrief.Core.Cards.FloorShare.Percent(breadth, floor)}% of its members closed above their 200-day average, {(line.Open ? "at or above" : "below")} its floor of {floor * 100:0.#}%")
                : $"the {line.Index}'s breadth is not available tonight";

        var body = new StringBuilder();

        body.Append(Invariant, $"<p class=\"market-line\" data-index=\"{Escaped(line.Index)}\" data-open=\"{Flag(line.Open)}\" data-breadth=\"{(line.Breadth is { } held ? held.ToString("R", Invariant) : "none")}\" data-floor=\"{(line.Floor is { } bar ? bar.ToString("R", Invariant) : "none")}\" ");
        body.Append(Invariant, $"data-passed=\"{(line.Passed is { } passed ? passed.ToString(Invariant) : "none")}\" data-members=\"{(line.Members is { } members ? members.ToString(Invariant) : "none")}\" ");
        body.Append(Invariant, $"data-buy-points=\"{line.BuyPoints}\" data-setups-listing=\"{line.SetupsListing}\" data-setups=\"{line.Setups}\" data-close=\"{(line.CloseRead ? line.Close.ToString(Invariant) : "none")}\" data-open-trades=\"{line.OpenTrades}\"");
        body.Append(line.Dip is null ? string.Empty : Formatted($" data-dip=\"{(line.DipOpen ? "open" : "closed")}\""));
        body.Append(line.Checks is { } closing ? Formatted($" data-closed=\"{Escaped(string.Join(" ", closing.Where(one => !one.Open).Select(one => one.Family)))}\">") : ">");

        // The short-term dip reads its own market setting, so a night the check closed the swing lists says the dip's is
        // still open where its own setting left it so, and every night says which setting the dip read.
        // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
        var headline = line.Open ? "The lists are open" : line.Dip is not null && line.DipOpen ? "The lists are closed but the short-term dip's" : "The lists are closed";

        body.Append(Invariant, $"<b class=\"market-{(line.Open ? "open" : "closed")}\">{headline}</b>: {Escaped(check + (line.Dip is { } dip ? "; " + dip : string.Empty))}. ");
        body.Append("<span class=\"market-counts\">");

        if (line.Passed is { } through && line.Members is { } of)
        {
            body.Append(Invariant, $"{through} of {of} {Escaped(line.Index)} members passed a setup · ");
        }

        body.Append(Invariant, $"{Count(line.BuyPoints, "buy point")} tonight across {line.SetupsListing} of {Count(line.Setups, "setup")} · ");
        body.Append(!line.CloseRead
            ? string.Empty
            : line.CloseHref is { } closeHref && line.Close > 0
                ? Formatted($"<a class=\"close-open\" href=\"{Escaped(closeHref)}\">{line.Close} close to a buy point</a> · ")
                : Formatted($"{line.Close} close to a buy point · "));
        body.Append(Invariant, $"<a href=\"#/picks?status={PickStatus.Open}{(line.Universe is { } chosen ? "&amp;universe=" + Uri.EscapeDataString(chosen) : string.Empty)}\">{Count(line.OpenTrades, "open trade")}</a></span></p>");

        // Beneath it, what ran tonight and the watch list in one line, where the page hands them in.
        var links = new List<string>();

        if (line.RunHref is { } run)
        {
            links.Add(Formatted($"<a class=\"market-run\" href=\"{Escaped(run)}\">What ran tonight</a>"));
        }

        if (line.Watching is { } watching)
        {
            links.Add(watching == 0
                ? Formatted($"<span class=\"market-watch\" data-watching=\"0\">none watched · <a href=\"#/watch\">open the watch list</a></span>")
                : Formatted($"<span class=\"market-watch\" data-watching=\"{watching}\">{watching} watched · <a href=\"#/watch\">open the watch list</a></span>"));
        }

        if (links.Count > 0)
        {
            body.Append("<p class=\"market-links\">" + string.Join(" · ", links) + "</p>");
        }

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
                ? $"<p class=\"family-sweep\" data-sweep=\"none passed\">{Escaped(EquityBrief.Core.Sweep.SweepAnswer.Line)}</p>"
                : Formatted($"<p class=\"family-sweep\" data-sweep=\"none passed\" data-lost=\"{(lost.Count == 0 ? "none" : string.Join(" ", lost))}\">{Escaped(EquityBrief.Core.Sweep.SweepAnswer.Line + EquityBrief.Core.Sweep.SweepLine.LostLine(lost))}</p>");

    // The words a frozen card's figures close on where no setting its sweep read beat what it is measured against.
    public const string StartingPoint = "so this rule is a starting point and not a proven edge";

    // The sentence a frozen card draws where the operator ruled a setting bought on an entry its search did not keep.
    public const string EntryNotKept = "Its entry did not survive the search's second stage, so it is a starting point that depends on its exact setting.";

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
        var (scale, places, form, unit, noun) = frozen.Points ? (100d, 2, "+0.00;-0.00;0.00", "points", "holding") : (1d, 3, "+0.000;-0.000;0.000", "risks", "trade");

        string Signed(double edge) => SignedFigure(edge * scale, places, Unsigned(edge * scale, places).ToString(form, Invariant));

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
            ? $"the setting the operator ruled on {DayOf(ruled)} from among those its sweep read"
            : passed ? "the setting its sweep would run" : "the strongest setting its sweep read";

        body.Append(Invariant, $"Frozen on {DayOf(frozen.FrozenOn)} at {at}, <span class=\"frozen-setting\">{Escaped(figures.Setting)}</span>: ");
        body.Append(Invariant, $"{figures.Trades.ToString("#,0", Invariant)} {noun}{(figures.Trades == 1 ? string.Empty : "s")} at an edge after costs of {(figures.Edge is { } edge ? Signed(edge) + " " + unit : "none stated")}, ");
        body.Append(figures.WithoutLargest is { } without ? Formatted($"{Signed(without)} without its five largest results, ") : string.Empty);
        body.Append(Invariant, $"above nothing in {EquityBrief.Core.Sweep.SweepLine.YearsAbove(figures)} of the {years.Length} years it stored");

        var last = EquityBrief.Core.Sweep.SweepLine.Last(figures);

        if (last.Count > 0)
        {
            body.Append(", ");
            body.Append(string.Join(" and ", last.Select(year => year.Edge is { } held ? Formatted($"{year.Year} at {Signed(held)}") : Formatted($"{year.Year} with no {noun}"))));
        }

        body.Append('.');

        // A ruled setting bought on an entry the search's second stage did not keep says so.
        if (figures.RuledOn is not null && figures.EntryKept == false)
        {
            body.Append(' ');
            body.Append(Escaped(EntryNotKept));
        }

        // The sentence that no setting beat what it was read against, drawn only where neither measure stored says one did
        // and one of them says none did: the settings meeting the floors against luck, the tester's adjusted p against its
        // bar, or both.
        var luck = figures.BeatLuck;
        var tester = figures.TesterPassed;

        if (luck != true && tester != true && (luck == false || tester == false))
        {
            var against = new List<string>();

            if (luck == false)
            {
                against.Add(Formatted($"{figures.Meeting} of the {(figures.SettingsRead is { } read ? read.ToString(Invariant) + " " : string.Empty)}settings it read met the floors where luck alone passes about {figures.Luck!.Value.ToString("0.#", Invariant)}"));
            }

            if (tester == false)
            {
                against.Add(figures.Adjusted is { } p
                    ? Formatted($"the tester read its search at an adjusted p of {p.ToString("0.0000", Invariant)} against its bar of {EquityBrief.Core.Loop.LoopGate.Bar.ToString("0.0000", Invariant)}")
                    : "the tester read too few blocks of its search to pass it");
            }

            var bar = luck == false && tester == false ? "luck or the tester's bar" : luck == false ? "luck" : "the tester's bar";

            body.Append(Invariant, $" No setting it read beat {bar}: {Escaped(string.Join(", and ", against))}, {StartingPoint}.");
        }

        body.Append("</p>");

        return body.ToString();
    }

    // How a setup card is read, written once for every setup card on every index and opened from its header's "?",
    // where each card once repeated its key.
    public const string SetupCardGuide =
        "The header names the setup in its hue with its standing and tonight's count. The tabs choose the rule the card is drawn by: the live or provisional rule first, then each registered variant named by the settings it differs from the live rule on, the full rule in the tab's tooltip. The chips are the chosen rule's settings, the ones a variant differs on marked, and the rule in a sentence opens beneath them. The live rule's picks are drawn as rows in the card, each ticker opening the stock's chart page, as they are in tonight's buy points above; a chosen variant draws its own picks in their place under the band with what it shares with the live rule's passes tonight, adds and drops. The bar beneath is the rule's funnel, the members reaching each gate, with the gate that stopped the most named. Breakouts forming and stocks one gate short of a buy point open beneath it, and neither is a pick. A stock is listed once across every card of every index, and never while a trade for it is still open on any of them, though a short-term dip trade holds back no other setup's pick; the notes name what was held back and on which index's list. On the S&P 400 and 600 a rule runs on provisional settings until its freeze, read on its own index's members, and its record starts at the freeze. The reward to risk is a fact about the chart and not a chance of anything.";

    // A card's header: the setup's word in its tint beside its swatch, its standing as a pill, tonight's count, and the
    // "?" opening the guide its kind of card is read by.
    // see: Each setup family has a hue of its own drawn as a mark and a tint beside its word, the short-term dip's among them
    static string CardHead(string family, string heading, string pill, string pillClass, string? pillTitle, string count, string guide) =>
        $"<header class=\"fam-head\" data-family=\"{Escaped(family)}\"><span class=\"fam-tag\" data-family=\"{Escaped(family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(family)}\"></span>{Escaped(heading)}</span>"
        + $" <span class=\"status-pill {pillClass}\"{(pillTitle is null ? string.Empty : $" title=\"{Escaped(pillTitle)}\"")}>{Escaped(pill)}</span>"
        + $" <span class=\"fam-count\">{Escaped(count)}</span>"
        + $"<details class=\"guide\"><summary title=\"How to read this card\" aria-label=\"How to read this card\">?</summary><p class=\"guide-b\">{Escaped(guide)}</p></details></header>";

    // A card's standing as a pill: a chosen variant's dashed outline, the live rule's slate with the day it went live under
    // the pointer, or the provisional rule's amber.
    static (string Words, string Class, string? Title) Pill(DateOnly? liveSince, RuleView? rule) =>
        rule is { Chosen.Live: false } variant
            ? (FormattableString.Invariant($"Variant {variant.Chosen.Variant}"), "variant", variant.Chosen.Rule)
            : liveSince is { } since
                ? ("Live", "live", FormattableString.Invariant($"live since {since:yyyy-MM-dd}"))
                : ("Provisional", "provisional", EquityBrief.Core.Families.SetupFamilies.ProvisionalStatus);

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
        var (pill, pillClass, pillTitle) = Pill(card.LiveSince, card.RuleChoice);
        var count = variant is null ? Count(card.Picks.Count, "pick") + " tonight" : Count(variant.Picks?.Count ?? 0, "pick") + " tonight under this variant";
        var folded = Folded(card);

        body.Append(Invariant, $"<section class=\"family-card\" data-family=\"{Escaped(card.Family)}\" data-place=\"{card.Place}\" data-of=\"{card.Of}\" data-picks=\"{card.Picks.Count}\" ");
        body.Append(Invariant, $"data-state=\"{(provisional ? "provisional" : "live")}\" data-live-since=\"{(card.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{card.Variants}\" ");
        body.Append(Invariant, $"data-rule=\"{Escaped(card.RuleChoice?.Chosen.Slug ?? RuleScreenWords.LiveSlug)}\" data-variant=\"{(variant is null ? "none" : variant.Chosen.Variant!.Value.ToString(Invariant))}\" data-folded=\"{Flag(folded)}\">");

        if (folded)
        {
            body.Append("<details class=\"fam-fold\"><summary class=\"fam-line\">");
            body.Append(Invariant, $"<span class=\"fam-tag\" data-family=\"{Escaped(card.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(card.Family)}\"></span>{Escaped(card.Heading)}</span>");
            body.Append(Invariant, $" <span class=\"status-pill {pillClass}\"{(pillTitle is null ? string.Empty : Formatted($" title=\"{Escaped(pillTitle)}\""))}>{Escaped(pill)}</span> ");
            body.Append(EmptyLine(card));
            body.Append("</summary><div class=\"fam-fold-b\">");
            body.Append(Invariant, $"<details class=\"guide\"><summary title=\"How to read this card\" aria-label=\"How to read this card\">?</summary><p class=\"guide-b\">{Escaped(SetupCardGuide)}</p></details>");
        }
        else
        {
            body.Append(CardHead(card.Family, card.Heading, pill, pillClass, pillTitle, count, SetupCardGuide));
        }

        body.Append(SweepFoundNoneLine(card.SweepFoundNone, card.SweepLost));

        // A frozen card's figures are its live rule's, drawn while the live rule is the one chosen.
        body.Append(variant is null ? FrozenSweepLine(card.Frozen) : string.Empty);

        if (card.RuleChoice is { } rule)
        {
            body.Append(RuleTabs(card.Family, rule));
            body.Append(RuleChipsLine(rule));
        }

        if (variant is not null)
        {
            body.Append(Invariant, $"<p class=\"variant-band\" role=\"status\" data-variant=\"{variant.Chosen.Variant}\">{Escaped(RuleScreenWords.Band(variant.Chosen.Variant!.Value))}</p>");
            body.Append(ShareLine(variant.Share, "picks"));
            body.Append(VariantPicks(card.Family, variant.Picks ?? [], card.Numbers));
        }
        else if (card.Picks.Count > 0)
        {
            body.Append(LivePicks(card.Picks));
        }

        foreach (var note in card.Notes)
        {
            body.Append(Invariant, $"<p class=\"family-note\">{WithSignedFigures(note)}</p>");
        }

        // Why a folded card lists nothing where its funnel does not say it, as when the night did not evaluate the rule.
        if (folded && card.RuleChoice?.Funnel is not { Count: > 0 } && card.Empty is { } why && !why.StartsWith("The market check closed", StringComparison.Ordinal))
        {
            body.Append(Invariant, $"<p class=\"degraded family-empty\" data-picks=\"0\">{Escaped(why)}</p>");
        }

        if (card.RuleChoice is { } drawn)
        {
            // A folded card's line already names the gate that stopped the most, so its funnel draws the bars alone.
            body.Append(FunnelBar(drawn, said: !folded));

            if (variant is not null || card.Picks.Count > 0)
            {
                body.Append(StretchLine(drawn.Stretch, drawn.Evaluated));
            }

            body.Append(FormingList(drawn.Forming));
        }

        body.Append(CloseRows(card.Family, card.Close ?? [], card.Numbers));
        body.Append(folded ? "</div></details></section>" : "</section>");

        return body.ToString();
    }

    // What each column of a card's own picks holds, the plan's columns in the buy points' own words.
    public static IReadOnlyList<(string Heading, string Says)> CardPickHeadings =>
    [
        ("#", "The pick's place in this setup's own order tonight."),
        ("Stock", "The ticker opens the stock's chart page, and the company is named beneath it."),
        .. BuyPointHeadings.Where(heading => heading.Heading is "Business" or "Buy" or "Stop" or "Target" or "Reward to risk"),
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
                "Buy" or "Stop" or "Target" or "Reward to risk" => "r",
                _ => null,
            }));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var (pick, place) in picks.Select((pick, at) => (pick, at + 1)))
        {
            var row = pick.Row;

            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-card-pick=\"true\"><td class=\"place\" data-place=\"{place}\">{place}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{(row.Distance?.Name is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            body.Append(PickBusinessCell(pick));
            body.Append(PriceCell("buy", pick.Buy, null));
            body.Append(PriceCell("stop", pick.Stop, null));
            body.Append(PriceCell("target", pick.Target, pick.Trailing ? "trailing" : pick.Exit));
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
        // Where the market check closed the list the line says so, its breadth and floor standing in the market line above.
        var parts = new List<string> { "nothing tonight" };

        if (card.Empty is { } empty && empty.StartsWith("The market check closed", StringComparison.Ordinal))
        {
            parts.Add("the market check closed");
        }

        if (card.RuleChoice?.Funnel is { Count: > 0 } funnel && EquityBrief.Core.Cards.RuleFunnelDrop.Largest(funnel) is { } drop)
        {
            parts.Add(FormattableString.Invariant($"most stopped at the {drop.Gate} gate ({drop.Stopped} of {drop.Reached})"));
        }

        if (card.RuleChoice?.Stretch is { } stretch)
        {
            parts.Add(stretch.Mark is { } mark
                ? FormattableString.Invariant($"empty {stretch.Stretch} night{(stretch.Stretch == 1 ? string.Empty : "s")}, its usual longest {mark}")
                : FormattableString.Invariant($"no usual stretch yet ({stretch.Completed} of {EquityBrief.Core.Cards.RuleStretch.CompletedStretchesFloor})"));
        }

        var flagged = card.RuleChoice?.Stretch is { Flagged: true };

        return Formatted($"<span class=\"family-empty-line{(flagged ? " flagged" : string.Empty)}\" data-picks=\"0\" data-flagged=\"{Flag(flagged)}\">{Escaped(string.Join(" · ", parts))}</span>");
    }

    // The card's tabs: the live or provisional rule first, then each variant by its number and the settings it differs
    // on, the full rule under the pointer, the choice kept in the link under the family's own key; and on an index no
    // freeze has registered a rule on, the line saying so beside the one tab.
    static string RuleTabs(string family, RuleView rule)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<nav class=\"rule-tabs\" data-rule-choice=\"{Escaped(family)}\" aria-label=\"The rule this card is drawn by\">");

        foreach (var choice in rule.Choices)
        {
            var value = choice.Live ? RuleScreenWords.LiveSlug : choice.Slug;
            var current = choice.Slug == rule.Chosen.Slug;

            body.Append(Invariant, $"<a class=\"rule-tab{(current ? " current" : string.Empty)}\" href=\"#\" data-rule-tab=\"{Escaped(family)}\" data-value=\"{Escaped(value)}\" data-variant=\"{(choice.Variant is { } number ? number.ToString(Invariant) : "none")}\" title=\"{Escaped(choice.Rule)}\"{(current ? " aria-current=\"true\"" : string.Empty)}>");
            body.Append(Escaped(choice.Tab ?? (choice.Live ? "Live" : FormattableString.Invariant($"V{choice.Variant}"))));
            body.Append("</a>");
        }

        body.Append("</nav>");

        if (rule.NoVariantLine is { } line)
        {
            body.Append(Invariant, $"<p class=\"no-variant\" data-no-variant=\"true\">{Escaped(line)}</p>");
        }

        return body.ToString();
    }

    // The chosen rule's settings as chips, a chosen variant's differing ones marked, with its sentence a click away, its
    // clauses the live rule's words lack marked as well.
    static string RuleChipsLine(RuleView rule)
    {
        var body = new StringBuilder();

        if (rule.Chips is { Count: > 0 } chips)
        {
            body.Append(Invariant, $"<ul class=\"rule-chips\" data-chips=\"{chips.Count}\">");

            foreach (var chip in chips)
            {
                body.Append(Invariant, $"<li class=\"rule-chip{(chip.Differs ? " differs" : string.Empty)}\">{WithSignedFigures(chip.Words)}</li>");
            }

            body.Append("</ul>");
        }

        body.Append("<details class=\"rule-sentence\"><summary>The rule in a sentence</summary>");
        body.Append(rule.Words is { } words && !rule.Parts.Any(part => part.Differs)
            ? $"<p class=\"rule-words\">{WithSignedFigures(words)}</p>"
            : RuleWordsLine(rule.Parts));
        body.Append("</details>");

        return body.ToString();
    }

    // A chosen variant's picks or holdings beside the live rule's passes tonight, in one line.
    static string ShareLine(RuleShareView? share, string what)
    {
        if (share is null)
        {
            return string.Empty;
        }

        static string Named(IReadOnlyList<string> tickers) => tickers.Count == 0 ? "none" : string.Join(", ", tickers);

        return Formatted($"<p class=\"rule-share\" data-shared=\"{Escaped(string.Join(",", share.Shared))}\" data-added=\"{Escaped(string.Join(",", share.Added))}\" data-dropped=\"{Escaped(string.Join(",", share.Dropped))}\">")
            + Escaped(FormattableString.Invariant($"Beside the {share.Against} rule's {(what == "picks" ? "passes" : "holdings")} tonight: shares {Named(share.Shared)} · adds {Named(share.Added)} · drops {Named(share.Dropped)}."))
            + "</p>";
    }

    // The chosen rule's words in clauses, each the live rule's words lack marked.
    static string RuleWordsLine(IReadOnlyList<RulePartView> parts) =>
        "<p class=\"rule-words\">" + string.Join(", ", parts.Select(part => part.Differs ? $"<mark class=\"differs\">{WithSignedFigures(part.Text)}</mark>" : WithSignedFigures(part.Text))) + ".</p>";

    // A family's three numbers for a row, each in its own cell, a number the night stored no reading of drawn as none.
    static string NumberCells(string family, string ticker, IReadOnlyDictionary<string, IReadOnlyList<double?>>? numbers)
    {
        var columns = EquityBrief.Core.Cards.CardNumbers.Of(family);
        var values = numbers is not null && numbers.TryGetValue(ticker, out var held) ? held : null;

        return string.Concat(columns.Select((column, at) => values is not null && at < values.Count && values[at] is { } value
            ? Formatted($"<td class=\"r num\" data-number=\"{Escaped(column.Column)}\" data-value=\"{value.ToString("R", Invariant)}\">{CardNumber(column.Form, value)}</td>")
            : Formatted($"<td class=\"r num\" data-number=\"{Escaped(column.Column)}\" data-value=\"none\"><span class=\"degraded\">none</span></td>")));
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
    string VariantPicks(string family, IReadOnlyList<VariantPickView> picks, IReadOnlyDictionary<string, IReadOnlyList<double?>>? numbers)
    {
        if (picks.Count == 0)
        {
            return "<p class=\"degraded variant-empty\" data-picks=\"0\">This variant lists nothing tonight.</p>";
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table variant-table\" data-rows=\"{picks.Count}\"><thead><tr>");
        body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\" class=\"r\">Buy</th><th scope=\"col\" class=\"r\">Stop</th><th scope=\"col\" class=\"r\">Target</th><th scope=\"col\" class=\"r\">Reward to risk</th>");
        body.Append(NumberHeadings(family));
        body.Append("</tr></thead><tbody>");

        foreach (var pick in picks)
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(pick.Ticker)}\" data-variant-pick=\"true\"><td class=\"place\">{pick.Place}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(pick.Ticker)}\">{Escaped(pick.Ticker)}</a></td>");
            body.Append(Invariant, $"<td class=\"r\" data-entry=\"{pick.Entry.ToString(Invariant)}\">{Price(pick.Entry)}</td>");
            body.Append(Invariant, $"<td class=\"r\" data-stop=\"{pick.Stop.ToString(Invariant)}\">{Price(pick.Stop)}</td>");
            body.Append(Invariant, $"<td class=\"r\">{(pick.Target is { } target ? Price(target) : "trails")}</td>");
            body.Append(Invariant, $"<td class=\"r\">{(pick.RewardToRisk is { } reward ? reward.ToString("0.0", Invariant) : "none")}</td>");
            body.Append(NumberCells(family, pick.Ticker, numbers));
            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // The stocks one gate short of the family's buy point tonight, behind an expander: each with the gate it missed and
    // the family's three numbers. It recommends nothing.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    string CloseRows(string family, IReadOnlyList<CloseToBuyCell> rows, IReadOnlyDictionary<string, IReadOnlyList<double?>>? numbers)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<details class=\"expander close-rows\" data-close=\"{rows.Count}\"><summary>One gate short of a buy point: {rows.Count}</summary>");
        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table close-table\" data-rows=\"{rows.Count}\"><thead><tr>");
        body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\">The gate it missed</th>");
        body.Append(NumberHeadings(family));
        body.Append("</tr></thead><tbody>");

        foreach (var (row, at) in rows.Select((row, at) => (row, at + 1)))
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-gate=\"{Escaped(row.Gate)}\"><td class=\"place\">{at}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{(row.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            body.Append(Invariant, $"<td class=\"missed-gate\" title=\"{Escaped(row.Words)}\"><b>{Escaped(row.Gate)}</b></td>");
            body.Append(NumberCells(family, row.Ticker, numbers));
            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");
        body.Append("<p class=\"forming-key\">Each passed every gate of this setup but one tonight, with nothing excluding it. None is a pick.</p></details>");

        return body.ToString();
    }

    // A rule's funnel as one bar a gate, each as long as the share of the members reaching the gate that passed it, with
    // how many passed of how many reached it beside it: the first gate reached by the members the night read for the
    // rule, and each gate after it by those the gate before passed. The gate that stopped the most of those reaching it is
    // marked and, where the card does not say it already, named beneath; a first gate passing no member, as a closed
    // market check, is the one marked, since the funnel holds no fall before it. The market check's row is left out while
    // it is open, since it then passes every member read, and a first gate whose funnel holds no count of the members read
    // says how many passed it alone.
    static string FunnelBar(RuleView rule, bool said = true)
    {
        if (rule.Funnel is not { Count: > 0 } funnel)
        {
            return rule.Evaluated ? string.Empty : "<p class=\"degraded rule-unevaluated\" data-evaluated=\"false\">The night did not evaluate this rule.</p>";
        }

        var drop = EquityBrief.Core.Cards.RuleFunnelDrop.Largest(funnel);
        var shut = funnel[0].Passed == 0 ? funnel[0].Gate : null;
        var openMarket = funnel[0].Gate == EquityBrief.Core.Families.FamilyRule.Market && funnel[0].Passed > 0;
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"funnel-bar\" data-gates=\"{funnel.Count}\" data-largest-drop=\"{Escaped(drop?.Gate ?? shut ?? "none")}\" data-market-row=\"{Flag(!openMarket)}\">");
        body.Append("<ol class=\"rule-funnel\">");

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

            body.Append(Invariant, $"<li data-gate=\"{Escaped(gate)}\" data-passed=\"{passed}\" data-reached=\"{(reached is { } count ? count.ToString(Invariant) : "none")}\"{(marked ? " class=\"largest-drop\"" : string.Empty)}><span class=\"fb-name\">{Escaped(gate)}</span><span class=\"fb-bar\"><span class=\"fb-fill\" style=\"width:{(share * 100).ToString("0.#", Invariant)}%\"></span></span><b class=\"fb-count\">{words}</b></li>");
        }

        body.Append("</ol>");
        body.Append(!said
            ? string.Empty
            : drop is not null
                ? Formatted($"<p class=\"funnel-drop\" data-drop=\"{Escaped(drop.Gate)}\">Most stopped at the {Escaped(drop.Gate)} gate: {drop.Stopped} of the {drop.Reached} reaching it.</p>")
                : shut is not null
                    ? Formatted($"<p class=\"funnel-drop\" data-drop=\"{Escaped(shut)}\">The {Escaped(shut)} gate passed no member tonight, so none reached a later one.</p>")
                    : "<p class=\"funnel-drop\" data-drop=\"none\">No gate stopped a member tonight.</p>");
        body.Append("</div>");

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
            ? "Listed tonight, so its empty stretch is nothing"
            : FormattableString.Invariant($"No pick for {stretch.Stretch} night{(stretch.Stretch == 1 ? string.Empty : "s")}");
        var mark = stretch.Mark is { } level
            ? FormattableString.Invariant($"; on {RuleScreenWords.SharePercent}% of past empty nights the stretch was {level} or shorter")
            : FormattableString.Invariant($"; no mark yet: {stretch.Completed} of {EquityBrief.Core.Cards.RuleStretch.CompletedStretchesFloor} stretches completed over {stretch.Sessions} of {EquityBrief.Core.Cards.RuleStretch.SessionsFloor} sessions");

        return FormattableString.Invariant($"<p class=\"stretch-line{(stretch.Flagged ? " flagged" : string.Empty)}\" data-stretch=\"{stretch.Stretch}\" data-mark=\"{(stretch.Mark is { } held ? held.ToString(Invariant) : "none")}\" data-flagged=\"{Flag(stretch.Flagged)}\" data-completed=\"{stretch.Completed}\" data-sessions=\"{stretch.Sessions}\">")
            + Escaped(words + mark + (stretch.Flagged ? ". Past its mark." : "."))
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
        body.Append(Invariant, $"<summary>Breakouts forming: {forming.Forming}{(forming.Rows.Count < forming.Forming ? FormattableString.Invariant($", {forming.Rows.Count} drawn") : string.Empty)}</summary>");

        if (!forming.MarketOpen)
        {
            body.Append("<p class=\"degraded forming-closed\" data-market-open=\"false\">The market check closed this rule's list tonight, so it would list none of these whatever they did.</p>");
        }

        if (forming.Rows.Count == 0)
        {
            body.Append("<p class=\"forming-empty\" data-rows=\"0\">No member is forming a breakout under this rule tonight.</p>");
        }
        else
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table forming-table\" data-rows=\"{forming.Rows.Count}\"><thead><tr>");
            body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\" class=\"r\">Close</th><th scope=\"col\" class=\"r\">Close above</th><th scope=\"col\" class=\"r\">Moves under</th><th scope=\"col\" class=\"r\">Volume needed</th><th scope=\"col\" class=\"r\">Volume tonight</th><th scope=\"col\" class=\"r\">Range ratio</th><th scope=\"col\">Still fails</th><th scope=\"col\">Next report</th>");
            body.Append("</tr></thead><tbody>");

            foreach (var row in forming.Rows)
            {
                body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-forming-row=\"true\" data-high=\"{row.High.ToString(Invariant)}\" data-volume-needed=\"{row.VolumeNeeded.ToString("0", Invariant)}\"><td class=\"place\">{row.Place}</td>");
                body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>");
                body.Append(Invariant, $"<td class=\"r\">{Price(row.Close)}</td><td class=\"r\">{Price(row.High)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{row.MovesUnder.ToString("0.00", Invariant)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{Shares(row.VolumeNeeded)}</td><td class=\"r\">{Shares(row.Volume)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{row.RangeRatio.ToString("0.00", Invariant)}</td>");
                body.Append(Invariant, $"<td>{Escaped(string.Join(", ", row.Missing))}</td>");
                body.Append(Invariant, $"<td>{(row.NextEarnings is { } next ? DayOf(next) : "none within the window")}</td></tr>");
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

    // A pick's stories over the thirty days before the night as the labeller counted them.
    static string PickNewsCell(FamilyPickCell pick) =>
        pick.Row.NewsPositive is { } positive && pick.Row.NewsNegative is { } negative
            ? StoryCounts(positive, negative)
            : "<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">no label</span></td>";

    // A label's family with its article, as "also a sector leader" reads.
    static string Article(string label) =>
        label.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(label[0]), StringComparison.Ordinal) ? "an " + label.ToLowerInvariant() : "a " + label.ToLowerInvariant();

    static string PriceCell(string kind, decimal? price, string? instead) =>
        price is { } stated
            ? Formatted($"<td class=\"r num plan-{kind}\" data-{kind}=\"{stated.ToString(Invariant)}\">{Figures.Price(stated)}</td>")
            : $"<td class=\"r num plan-{kind}\" data-{kind}=\"none\">{(instead is null ? "<span class=\"degraded\">none</span>" : Escaped(instead))}</td>";
}
