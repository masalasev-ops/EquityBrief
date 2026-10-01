namespace EquityBrief.Tests.Harness;

internal sealed record Scoped(Verdict Verdict, string Note, string By);

// Which half of Scope supplied a due point. Reported per claim so the
// population can be partitioned by what actually answered rather than by what
// could have, and so the four counts can be asserted to sum to the whole.
internal enum DueOrigin
{
    // Nothing answered. The claim has no due point and the caller refuses,
    // which is what keeps a subject from being left unexamined by omission.
    Nothing,

    // A declared exception, because the plan names the subject in a way that
    // cannot be read. Four of these, each with its reason written beside it.
    Exception,

    // Section 15, keyed on the table and the row together.
    Screens,

    // One of the written residual maps: components, stores, failures, matrix
    // rows, limits, nightly steps.
    Residual,

    // Read from BUILD_PLAN's checkpoint text, which is the half that moves when
    // the plan is reordered.
    Plan,
}

// Where every claim in the architecture is answered.
//
// A claim is PASS only when a named check reaches it. Everything else names the
// checkpoint or phase that ends it, which is what out of scope means: the corpus
// places it at a point that has not landed. Nothing is left unexamined by
// omission, because a subject with no entry here stops the harness.
internal static class Scope
{
    const string ByMigration = "schema-columns";
    const string ByAccess = "component-access";
    const string ByHarness = "architecture-conformance";

    // The screens claims are reached by a behavioural check rather than by
    // component-access, because a claim that something is drawn is a claim
    // about a surface and a declaration says nothing about one.
    const string ByReadSurface = "read-surface";
    const string ByGap = "gap-refusal";
    const string ByActions = "corporate-actions";
    const string ByExpectations = "fixture-expectations";
    const string ByCost = "nightly-cost";
    const string ByNight = "nightly-run";
    const string ByListings = "listings-coverage";
    const string ByAdmissibility = "claim-admissibility";
    const string ByRegister = "register-append-only";
    const string ByRules = "rule-versions-scored";
    const string ByCandidateVerdicts = "candidate-verdicts";
    const string ByCandidateConditions = "candidate-conditions";
    const string ByTrendVersions = "trend-versions";

    internal const string MatrixTable = "Read and write matrix";
    internal const string CatalogueTable = "7. Component catalogue";
    internal const string StoresTable = "16. Data stores and the read and write matrix";
    internal const string FailureTable = "18. Failure behaviour";
    internal const string LimitsTable = "17. Limits, spend and the numbers the harness asserts";
    internal const string FixtureTable = "19.1 What a fixture holds";

    // The ones a check has actually reached, keyed on the table and the subject
    // together. Keyed on the subject alone until 0.7's review, which meant a
    // catalogue verdict was reused verbatim for the row of the same name in the
    // read and write matrix, where the claim is a different one.
    static readonly Dictionary<string, Scoped> Reached = new(StringComparer.Ordinal)
    {
        // The drawn parts of the rows the fifth phase 5 sign-off review decomposed.
        [CheckReach.Key("15.4 The two surfaces", "The app, the single page")] = new Scoped(
            Verdict.Pass,
            "the shell is one page whose regions the router swaps, read back off the markup",
            ByReadSurface),
        [CheckReach.Key("15.4 The two surfaces", "The app, routing")] = new Scoped(
            Verdict.Pass,
            "the hash routes to a region and back, asserted over the shell the server writes",
            ByReadSurface),
        [CheckReach.Key("15.4 The two surfaces", "The app, filters")] = new Scoped(
            Verdict.Pass,
            "the universe filters arrive in the query the shell passes through, so a filtered view is a link",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, Everything above the marker is a sale")] = new Scoped(
            Verdict.Pass,
            "the exits are drawn above the price marker and nothing else is",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, everything below is a purchase")] = new Scoped(
            Verdict.Pass,
            "the tranches are drawn below the price marker and nothing else is",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, stops are horizontal rules")] = new Scoped(
            Verdict.Pass,
            "each stop is drawn as a rule rather than as a zone",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, the invalidation is the lowest one")] = new Scoped(
            Verdict.Pass,
            "the invalidation is the lowest rule the mark draws",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row, A name's close between its nearest support and its nearest resistance")] = new Scoped(
            Verdict.Pass,
            "the close sits between the two edges that exist, each drawn in its own hue",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row, sized for a table cell")] = new Scoped(
            Verdict.Pass,
            "the mark is drawn at the one cell width the table gives it, read off the markup",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row, the distances in typical days")] = new Scoped(
            Verdict.Pass,
            "both distances are stated in typical days rather than in prices",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track, setups resolved as a win")] = new Scoped(
            Verdict.Pass,
            "the won segment is counted out of the one denominator",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track, resolved as a loss")] = new Scoped(
            Verdict.Pass,
            "the lost segment is counted out of the same denominator",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track, unresolved")] = new Scoped(
            Verdict.Pass,
            "the unresolved segment is a dashed outline and never a third colour",
            ByReadSurface),
        [CheckReach.Key("15.4 The two surfaces", "The app, selection")] = new Scoped(
            Verdict.Pass,
            "the hash is split into a path and a query before anything routes, so the selection is a link the server is given",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Night header, the harness verdict")] = new Scoped(
            Verdict.Pass,
            "the four verdict counts are drawn in the header, separately, and a machine with no report says so",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Night header, names in the index")] = new Scoped(
            Verdict.Pass,
            "the index size is read off the markup and matched against the store",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Night header, names that fired")] = new Scoped(
            Verdict.Pass,
            "the fired count is over the whole index rather than over the drawn rows",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Night header, run duration")] = new Scoped(
            Verdict.Pass,
            "the night's duration is drawn from the run log, and a night the log does not carry says so",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "An evening before the switch, one row per name that fired")] = new Scoped(
            Verdict.Pass,
            "one row per fired name, counted off the markup",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "An evening before the switch, ordered by how many fired then by the plan's reward to risk")] = new Scoped(
            Verdict.Pass,
            "the order is read back off the route's markup against the stored fired counts and each night's stored plan in both directions, over both fixture nights, and over constructed rows whose fired counts are equal, so the tiebreak is the thing read and a row with no reward to risk is drawn after every row with one in its group",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, the reward to risk or the plan's reason for none")] = new Scoped(
            Verdict.Pass,
            "each drawn row carries the ratio its night's plan stored, read back against the ladder row, or the plan's own reason for none, over the fixture and over constructed plans for each of the reasons a plan gives",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, at most twenty drawn")] = new Scoped(
            Verdict.Pass,
            "twenty at most are drawn and the undrawn count is stated beside them",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, each numbered by its place in that order")] = new Scoped(
            Verdict.Pass,
            "each drawn row opens with its place in the order the rows are drawn in, counted from one, over nights of forty, twenty, seven and one, with the footer's label spanning every column before the first reason",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, a line above them stating how many are drawn of how many are listed")] = new Scoped(
            Verdict.Pass,
            "the line stands above the rows and states the drawn count against the fired count, naming the universe page where the rows leave a name out, over nights of forty, twenty, seven and one",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, name")] = new Scoped(
            Verdict.Pass,
            "the name cell carries the ticker the row is about",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, close")] = new Scoped(
            Verdict.Pass,
            "the close cell carries what the night stored, and a name it computed nothing for says so",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, day change")] = new Scoped(
            Verdict.Pass,
            "the row's change is the one its two stored closes make, drawn with its sign because the two hues are spoken for",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, trend state in a word")] = new Scoped(
            Verdict.Pass,
            "the row carries the ladder's own label in words, and a name with no ladder row says so",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, the distance row mark")] = new Scoped(
            Verdict.Pass,
            "one distance mark per drawn row, each carrying that row's own name rather than one shape repeated",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, the reasons as context")] = new Scoped(
            Verdict.Pass,
            "each reason that fired is named in the row's own cell",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, beside the name a line saying so where its prices may not reflect a dividend or split")] = new Scoped(
            Verdict.Pass,
            "the row of a name whose stored series is suspect carries the line beside the name with the row's instant and reason as its title, escaped, and a trusted name's row carries none, read off tonight's route over a store the whole pipeline populated",
            ByReadSurface),
        // The 5.4 correction's row: listings written before it are kept as written and the
        // routes drawing their session say what those rows could not do.
        [CheckReach.Key(FailureTable, "Listings written before the 5.4 correction")] = new Scoped(
            Verdict.Pass,
            "the tonight, run and universe routes each draw the line once for a session whose rows carry no event date among earnings soon's values, naming that session and no other, and the tonight and run routes draw none for a session the corrected rule wrote, read over a store the pipeline populated beside one session written in the old shape; the name route and the file it exports draw it once when the newest night's rows are in the old shape and none before; the run page's record counts earnings soon and breakout on volume only off rows carrying their markers, and each record and the line above them state the nights it stands on counted the same way",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Prices may be out of date, one line saying its prices may not reflect a recent dividend or split")] = new Scoped(
            Verdict.Pass,
            "the name route and the exported file each draw the line once for a name whose stored series is suspect, and the name route none for a trusted name",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Prices may be out of date, when the refetch was last tried and why it failed")] = new Scoped(
            Verdict.Pass,
            "the line carries the row's instant and reason as the store holds them, the reason escaped in the markup and whole once read",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Prices may be out of date, above everything the page draws from those prices")] = new Scoped(
            Verdict.Pass,
            "the line is the first thing inside the name's region, ahead of the trend state and the fact strip",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Selected name, the level summary")] = new Scoped(
            Verdict.Pass,
            "the selected row's stored bands are drawn beside its plan column, so a plan is read with the levels it rests on",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Selected name, whichever row is selected")] = new Scoped(
            Verdict.Pass,
            "the region is drawn for the row the reader picked, asserted on a row that is not the first, and every row is selectable",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Selected name, the plan column")] = new Scoped(
            Verdict.Pass,
            "the plan column and its tables are composed for the selected row, so checking a plan needs no navigation",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "Sector strip, one line per sector")] = new Scoped(
            Verdict.Pass,
            "one line per sector, the lines summing to the index",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "Sector strip, how many are on tonight's list")] = new Scoped(
            Verdict.Pass,
            "the count is per sector rather than over the index, asserted over constructed rows where one sector holds more than another",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "Sector strip, names")] = new Scoped(
            Verdict.Pass,
            "each line carries its sector's name count",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "Sector strip, how many are in an uptrend")] = new Scoped(
            Verdict.Pass,
            "each line carries its uptrend count beside its name count",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, every name in the index")] = new Scoped(
            Verdict.Pass,
            "one row per index member, counted off the markup",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, the listing strip over sixty sessions")] = new Scoped(
            Verdict.Pass,
            "the strip over the window is drawn from the stored listings",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, each ticker a link to its own page that draws its year line while the pointer is over it")] = new Scoped(
            Verdict.Pass,
            "each row's name cell links to the name's page and carries its year line drawn over every close the store holds for it with its own nearest bands, read off the table and one sector of it against the store, shown by the stylesheet while the cell is under the pointer or holds the focus",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, each column heading saying what its column holds while the pointer is over it")] = new Scoped(
            Verdict.Pass,
            "one heading for each column a row draws, each saying what its column holds word for word against sentences written with the windows as numbers, read off a constructed table and off the fixture's screen, shown by the stylesheet while the heading is under the pointer or holds the focus",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, each column heading saying what its column holds while the pointer is over it")] = new Scoped(
            Verdict.Pass,
            "one heading for each column a row draws on an evening of either rule, the gates' column on the swing filter's alone and each reason's under its short name, each saying what its column holds word for word against sentences written with the thresholds as numbers, and every heading on the fixture's screen carrying one",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, each column heading saying what its column holds while the pointer is over it")] = new Scoped(
            Verdict.Pass,
            "one heading for each column a row draws, each saying what its column holds word for word against sentences written with the windows as numbers, and every heading on a fixture name's page carrying one",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, each column heading saying what its column holds while the pointer is over it")] = new Scoped(
            Verdict.Pass,
            "each heading of the screen's table and of a name page's says what its column holds word for word, the stock's column on the screen's alone and the night's sentence naming the link on the name page's",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, sorted by distance to the nearest level ascending")] = new Scoped(
            Verdict.Pass,
            "the rows are ordered by distance to the nearest level, read off the markup",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, name")] = new Scoped(
            Verdict.Pass,
            "the name cell carries the ticker",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, sector")] = new Scoped(
            Verdict.Pass,
            "the sector cell carries the sector the membership row holds",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, close")] = new Scoped(
            Verdict.Pass,
            "the close cell carries what the night stored, and a name it computed nothing for says so",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, trend state")] = new Scoped(
            Verdict.Pass,
            "the trend cell carries the ladder's label, drawn in words",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, the distance row mark")] = new Scoped(
            Verdict.Pass,
            "the distance mark is drawn in its own cell of each row",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, paged")] = new Scoped(
            Verdict.Pass,
            "the pages partition the rows in order, the region draws the page it was handed, and every page link keeps its filters",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, sessions until earnings")] = new Scoped(
            Verdict.Pass,
            "the count is off the exchange calendar and the date agrees with the per-name read, with no event and no calendar stated apart",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, the evening last on the list")] = new Scoped(
            Verdict.Pass,
            "the evening a name was last listed is drawn, and a name never listed says never",
            ByReadSurface),
        // 9.2 to 9.4, the queue the operator asked for: the ask on a row, the screen that
        // reads what is waiting, the control that takes one out, and which lane would
        // write one.
        [CheckReach.Key("15.7 Tonight", "Ask for a report")] = new Scoped(
            Verdict.Pass,
            "a row whose name holds no research carries a control that writes one request and starts one drain, read off the row's own markup and the route it posts to, and a row holding research carries none; the selected name's region carries the same control beside a line saying no report is written where the name holds none, and a link to its report, dated, where it holds one",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The report's state")] = new Scoped(
            Verdict.Pass,
            "each of the four states is read back off a row's own markup and off the selected name's region against the store in both directions, over a store holding a request in each state: written with its day, being written since its pass's start, queued with the instant it starts, and not written with the control, the control drawn for the last alone",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Each move beside its group")] = new Scoped(
            Verdict.Pass,
            "every row of the table of the biggest moves carries its group's median as the annotator stored it, named as an industry or a sector with how many members it was taken over, read back off the name's page against the store, and a group holding nobody says so",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, beneath the table of the biggest moves")] = new Scoped(
            Verdict.Pass,
            "the peers card is drawn on the name's page immediately after the card holding the table of the biggest moves, read off the page's own markup",
            ByReadSurface),
        // 11.6's correction on the operator's ruling of 2026-09-27: the name and at most ten members of its
        // group, those sharing its industry first and then the most alike, each with its likeness and its year.
        [CheckReach.Key("15.9 Name", "Peers, the name's own row marked and then at most ten members of its group by price alone")] = new Scoped(
            Verdict.Pass,
            "the name's own row is drawn first and is the one marked, then the members the annotator stored for it and no other name, read off each fixture name's page against the members worked by hand from the captured bars, and ten of a constructed group of sixteen are chosen",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, those sharing its industry first and then by how closely each one's daily moves followed the name's")] = new Scoped(
            Verdict.Pass,
            "the members are drawn in the order the annotator stored them, a member sharing the name's industry first whatever its likeness and then the higher likeness first, a member sharing too few sessions last and a tie settled by the ticker, worked by hand over constructed closes and read off each fixture page against the order worked from the captured bars",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, each row carrying how closely it moved with the name")] = new Scoped(
            Verdict.Pass,
            "each member's likeness is the annotator's stored correlation of the two names' daily returns, drawn to two places and whole on its element with the returns both hold, read off each fixture page against the store and against figures worked by hand from the captured bars, and a member sharing too few sessions says so with the count",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, whether it shares the name's industry")] = new Scoped(
            Verdict.Pass,
            "a member sharing the name's industry carries a mark saying so and one that does not carries none, over constructed rows",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, the close")] = new Scoped(
            Verdict.Pass,
            "each row's close is the universe row's stored close for its ticker, read off the page against the store",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, each ticker a link to its own page that draws its year line while the pointer is over it")] = new Scoped(
            Verdict.Pass,
            "each member's ticker links to its own page and carries its year line drawn over every close the store holds for it, read off each fixture page against the store, the name's own row linking nowhere",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, how far it sits below the stored year's high")] = new Scoped(
            Verdict.Pass,
            "each row's distance below the year's high and the high itself are the annotator's stored reading for its ticker, with the bars they were read over, read off the page against the store",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, its return over sixty sessions")] = new Scoped(
            Verdict.Pass,
            "each row's return is the annotator's stored reading for its ticker, and a name holding too few bars for it says not available with its bar count, read off the page against the store and over constructed rows",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, its trend state")] = new Scoped(
            Verdict.Pass,
            "each row's trend state is the ladder's stored state for its ticker as the universe row carries it, in a word",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, the distance row mark")] = new Scoped(
            Verdict.Pass,
            "each row draws the same distance row mark the universe table draws for its ticker, in typical days",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Peers, a key saying how to read it")] = new Scoped(
            Verdict.Pass,
            "the card closes on a key saying how to read the table and what to take from it",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, beside the earnings setups")] = new Scoped(
            Verdict.Pass,
            "the reactions card is drawn on the name's page immediately after the plan's card, which closes on the earnings setups, read off the page's own markup",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, one row per print over the calendar's year behind")] = new Scoped(
            Verdict.Pass,
            "every print the calendar holds for a fixture name whose bars reach it is one row of the name's record, read off every fixture name's page against the store",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, each row carrying its report date")] = new Scoped(
            Verdict.Pass,
            "each row's report date is the stored print's, in the order the store holds them",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, its timing")] = new Scoped(
            Verdict.Pass,
            "each row's timing is the stored print's, drawn as before the open, after the close or timing not filed",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, the session it moved on")] = new Scoped(
            Verdict.Pass,
            "each row's session is the one the earnings rule takes for the print, as the annotator stored it",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, the estimate")] = new Scoped(
            Verdict.Pass,
            "each row's estimate is the provider's as stored, whole on its element",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, the actual")] = new Scoped(
            Verdict.Pass,
            "each row's actual is the provider's as stored, whole on its element",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, the provider's surprise")] = new Scoped(
            Verdict.Pass,
            "each row's surprise is the provider's as stored, and none is drawn where it filed none",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, that session's move")] = new Scoped(
            Verdict.Pass,
            "each row's move is the reaction session's as the annotator stored it, read off the page against the store",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, a print with no filed estimate saying none was filed")] = new Scoped(
            Verdict.Pass,
            "a print with no filed estimate draws its actual, says none was filed and draws no surprise, over constructed rows",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Earnings reactions, a key saying how to read it")] = new Scoped(
            Verdict.Pass,
            "the card closes on a key saying how to read the record and what to take from it",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dividend, in the numbers section")] = new Scoped(
            Verdict.Pass,
            "the dividend is drawn inside the numbers section of a name's page, read off each payer's and non-payer's page",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dividend, from the newest filing alone")] = new Scoped(
            Verdict.Pass,
            "the part sits on the newest filing's row alone, every older row carrying none, and the page draws it from that row",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dividend, the forward annual rate")] = new Scoped(
            Verdict.Pass,
            "a payer's forward annual rate is the stored value whole on its element, drawn as a figure a share",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dividend, the forward yield as the provider states it")] = new Scoped(
            Verdict.Pass,
            "a payer's forward yield is the provider's as stored, whole on its element and drawn as a percentage",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dividend, the payout ratio")] = new Scoped(
            Verdict.Pass,
            "a payer's payout ratio is the provider's as stored, whole on its element and drawn as a percentage",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dividend, the ex-dividend date and the pay date")] = new Scoped(
            Verdict.Pass,
            "a payer's ex-dividend date and pay date are the provider's as stored, each on its element",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dividend, nothing where the provider files none")] = new Scoped(
            Verdict.Pass,
            "a non-payer draws no dividend part at all, and a row holding no dividend part says it is absent and why",
            ByReadSurface),
        [CheckReach.Key(StoresTable, "Earnings reactions")] = new Scoped(
            Verdict.Pass,
            "one row per name and print carrying the session the earnings rule takes, the estimate, the actual, the surprise and the move, each read back off the store the fixture's replay wrote against reactions worked by hand from the captured calendar and bars, and a print the bars do not reach left out and counted",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "reactions")] = new Scoped(
            Verdict.Pass,
            "each fixture print's session and move and the provider's figures are diffed against an expectation worked by hand from the captured calendar and bars through the replay's own rules rather than frozen from a run",
            ByExpectations),
        [CheckReach.Key("15.7 Tonight", "Night header, the night's breadth with the share above the 50-day average beside it as context")] = new Scoped(
            Verdict.Pass,
            "the night's breadth and the members it was counted over are drawn in the header whole as the store holds them, with the share above the 50-day average beside it, and a night holding none says so",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, relative strength")] = new Scoped(
            Verdict.Pass,
            "each row's relative strength is the mean of the two places the store holds, drawn whole on the row, read back off the table against the stored rows",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, the pullback from the recent high in typical days")] = new Scoped(
            Verdict.Pass,
            "each row's pullback in typical days is drawn whole as the store holds it",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, the volume while it came down")] = new Scoped(
            Verdict.Pass,
            "each row's volume while it came down is drawn whole as the store holds it",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "The table, the tightness of the range")] = new Scoped(
            Verdict.Pass,
            "each row's range tightness is drawn whole as the store holds it",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Swing readings, the return over 63 sessions and over 126 sessions with each one's place among the members' returns")] = new Scoped(
            Verdict.Pass,
            "both returns and each one's place are drawn whole on their elements as the store holds them, read back off the name's page against the stored row, a return the name holds too few bars for saying not available with the bars it holds",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Swing readings, the highest high of the last 20 sessions and how far the close sits below it in typical days' moves")] = new Scoped(
            Verdict.Pass,
            "the recent high, the session making it and the pullback in typical days' moves are drawn whole as the store holds them, read back off the page against the stored row",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Swing readings, the median volume of the sessions since that high against the fifty-day average")] = new Scoped(
            Verdict.Pass,
            "the median volume since the high against the fifty-day average is drawn whole as the store holds it, and a high made tonight says no session has come down from it",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Swing readings, the mean true range of the last ten sessions against the last fifty")] = new Scoped(
            Verdict.Pass,
            "the ten-session true range against the fifty-session one is drawn whole as the store holds it, read back off the page against the stored row",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Swing readings, a key saying how to read it")] = new Scoped(
            Verdict.Pass,
            "the card closes on a key saying how to read the readings and what to take from them",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Market reading, the night's breadth with how many members it was counted over")] = new Scoped(
            Verdict.Pass,
            "the breadth and the members it was counted over are drawn whole as the store holds them, and a night the store holds no breadth for says it is not available",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Market reading, the share above the 50-day average beside it as context")] = new Scoped(
            Verdict.Pass,
            "the share above the 50-day average is drawn beside the breadth and said to be context",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Market reading, the index's median volume against its fifty-day average")] = new Scoped(
            Verdict.Pass,
            "the median of volume against the fifty-day average is drawn whole as the store holds it",
            ByReadSurface),
        [CheckReach.Key(CatalogueTable, "Swing reader")] = new Scoped(
            Verdict.Pass,
            "the class declares the membership, bars and indicators it reads and the stores it writes and deletes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Swing reader")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Swing readings")] = new Scoped(
            Verdict.Pass,
            "one row per member per night carrying the readings or the reason there are none, each read back off the store the fixture's replay wrote against readings worked by hand from the captured bars",
            ByExpectations),
        [CheckReach.Key(StoresTable, "Market readings")] = new Scoped(
            Verdict.Pass,
            "one row per night carrying the members, the breadth over the members read, the same over the 50-day average and the median volume ratio, read back off the store the fixture's replay wrote against a night worked by hand",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "swing readings")] = new Scoped(
            Verdict.Pass,
            "each fixture member's readings and the night's breadth are diffed against an expectation worked by hand from the captured bars through the replay's own rules rather than frozen from a run",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Relative strength windows")] = new Scoped(
            Verdict.Pass,
            "a return is taken over each span and a series one bar short of it has none and no place, a tie counts half, over constructed series, and the constants the row states are the ones the reading uses",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Recent high window")] = new Scoped(
            Verdict.Pass,
            "the high is taken over the sessions the row states, a high made tonight leaves no session to have come down, and a high made twice is measured from the later, over constructed series",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Range tightness windows")] = new Scoped(
            Verdict.Pass,
            "the shorter mean true range against the longer is read over constructed series one session either side of the longer span, and a series too short for it has none",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Breadth")] = new Scoped(
            Verdict.Pass,
            "breadth is counted over the members read holding a close and the average, available at exactly half the night's members and not one short of it, over constructed nights, and the averages are selected by the constants the row states",
            ByExpectations),
        [CheckReach.Key(NightlyRunSteps.Heading, "Compute the swing readings for every member and the night's breadth: each return over 63 and over 126 sessions with its place among the members' returns, the highest high of the last 20 sessions and the pullback from it in typical days' moves, the volume while it came down, the tightness of the range, and the share of the members closing above their own 200-day average, a member read over nothing keeping its row with the reason.")] = new Scoped(
            Verdict.Pass,
            "the night runs the step after the moves and before the listings and writes a row for every member and one for the night",
            ByNight),
        [CheckReach.Key("15.9 Name", "Gates, each of the five gates with whether it passed and why")] = new Scoped(
            Verdict.Pass,
            "each gate's pass and its reason are drawn as the row stores them, read back off the name's page against the stored row",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Gates, the setup's family and whether the trigger's event happened")] = new Scoped(
            Verdict.Pass,
            "the family and whether the trigger's event happened are drawn as the row stores them",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Gates, the trade read from the ladder's first tranche and from the one swing plan that night's live rule used with that plan marked and no candidate's plan drawn whichever filter version the night ran under")] = new Scoped(
            Verdict.Pass,
            "the ladder's reading and the one swing plan the night's live rule read, its reward to risk and its stop's distance in typical moves with its entry, stop and target, are drawn whole as the row stores them, and the other swing plan is drawn on no night of any version",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Gates, the exclusions with a key saying how to read it")] = new Scoped(
            Verdict.Pass,
            "the exclusions and the notes are drawn as the row stores them, and the card closes on a key saying how to read the gates and what to take from them",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Swing filter funnel, how many members each gate passed in order and how many it removed")] = new Scoped(
            Verdict.Pass,
            "each gate's count passing it and every gate before it, and what it removed, are drawn whole and read back off the run page against counts the test makes from the stored rows",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Swing filter funnel, the setup's families on a night that held a breakout")] = new Scoped(
            Verdict.Pass,
            "a night holding no breakout draws no families, and one holding a breakout draws the pullbacks and the breakouts among the members through the market and the trend whole, read back against the stored rows",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Swing filter funnel, what each exclusion removed and how many pass")] = new Scoped(
            Verdict.Pass,
            "each exclusion's count and how many pass are drawn whole and read back against the stored rows",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Swing filter funnel, the version the night ran under")] = new Scoped(
            Verdict.Pass,
            "the version the night ran under is drawn, and a night with none open says it ran on section 17's proposed values",
            ByReadSurface),
        [CheckReach.Key(CatalogueTable, "Swing filter")] = new Scoped(
            Verdict.Pass,
            "the class declares every store it reads and the gate results it inserts and deletes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Swing filter")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Filter counts")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and that it writes none, and the declaration matches this row, its matrix row and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Filter counts")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Filter history")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and the gate results it writes, and the declaration matches this row, its matrix row and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Filter history")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "History pull")] = new Scoped(
            Verdict.Pass,
            "the class declares the feeds it asks and the pulled tables it reads, inserts and deletes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "History pull")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Section trial")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and the run log it appends to, and the declaration matches this row, its matrix row and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Section trial")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Pulled history")] = new Scoped(
            Verdict.Pass,
            "both tables' columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(StoresTable, "Gate results")] = new Scoped(
            Verdict.Pass,
            "one row per member per night carrying each gate's answer, the trade read both ways and the exclusions, read back off the two-night store against results worked by hand, and a night run again replacing its own rows",
            ByExpectations),
        [CheckReach.Key(StoresTable, "Filter versions")] = new Scoped(
            Verdict.Pass,
            "the filter reads the open version's settings and names it on every row, and runs on section 17's proposed values naming none where none is open",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "gate results")] = new Scoped(
            Verdict.Pass,
            "each fixture member's gates on both of the two-night store's nights are diffed against an expectation worked by hand, the arrival read across the two",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Market gate")] = new Scoped(
            Verdict.Pass,
            "breadth passes at the floor and fails a step below it, over constructed members, and the counts read the two floors the row states",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Strength gate")] = new Scoped(
            Verdict.Pass,
            "a mean place of exactly two thirds passes and one a step below fails, over constructed members",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Pullback depth")] = new Scoped(
            Verdict.Pass,
            "a pullback at either end of the span passes and one a step outside either end fails, over constructed members",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Volume dry-up")] = new Scoped(
            Verdict.Pass,
            "a dry-up a step under the ceiling passes and one at it fails, over constructed members",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Trade reward to risk")] = new Scoped(
            Verdict.Pass,
            "a reward to risk at the floor passes and a step under it fails, over constructed members, on each plan the trade gate can read",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Trade stop distance")] = new Scoped(
            Verdict.Pass,
            "a stop at either end of the span passes and one a step outside either end fails, over constructed members",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Earnings exclusion")] = new Scoped(
            Verdict.Pass,
            "a print at the window's last session excludes the name and one a session later does not, a date not on file excluding nothing and saying so",
            ByExpectations),
        [CheckReach.Key(FailureTable, "Breadth not available on a night")] = new Scoped(
            Verdict.Pass,
            "a night whose breadth is not available fails the market gate for every member with the counts in its reason while every other gate is still evaluated and stored, over a constructed night",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A gate's reading is absent for a name")] = new Scoped(
            Verdict.Pass,
            "each gate reading an absent value fails with the reason it is absent and none passes on an absence, over constructed members and the fixture's member the night read nothing for",
            ByExpectations),
        [CheckReach.Key(NightlyRunSteps.Heading, "Evaluate every member through the swing filter: the market gate on the night's breadth, the trend and strength gate, the pullback, the trigger where it first fired within the arrival window, the trade read from the ladder's first tranche, from the swing trade at the nearest bands and from section 10's plan for it, and the exclusions, storing every answer with the values that decided it and ranking the names passing, which are the pullback family's, and record the night's session as listed by the swing filter once the rows are stored (see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it); then evaluate every member under each other setup family, on the market check the filter stored, storing every answer with the values that decided it (see: The market check closes every family's list together); then draw the page's list from what each setup family passed, the families in the page's order and each family's names in its own, at most five a family, a stock once and none whose trade from any family is still open, and record the session as one the families drew (see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night) (see: A stock holds one trade across every family, and one qualifying under two is listed once under the first in the page's order).")] = new Scoped(
            Verdict.Pass,
            "the night runs the step after the listings and before the facts, writes a row for every member, and records its session as listed by the swing filter",
            ByNight),
        [CheckReach.Key("15.10 Run", "The shape clock, the ordinary nights under the open filter version against the sixty the calibration waits on")] = new Scoped(
            Verdict.Pass,
            "the ordinary nights under the open version, or with none open the nights under section 17's values counting toward nothing, are read back off the page against a count worked by hand over constructed nights",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape clock, every event night with what made it one")] = new Scoped(
            Verdict.Pass,
            "each event night is named with the gate or reason that flooded it, its share and its median, or the index's volume ratio, read back against the test's own classification",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape clock, each gate's median count through it against its band")] = new Scoped(
            Verdict.Pass,
            "each gate's median count through the funnel over the ordinary nights is drawn whole beside its band, read back against a median worked by hand, an event night read by none",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape clock, the list's median size against its band")] = new Scoped(
            Verdict.Pass,
            "the list's median over the ordinary nights is drawn whole beside its band, read back against a median worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape clock, drawn as not yet measured until the trigger")] = new Scoped(
            Verdict.Pass,
            "until sixty ordinary nights are stored under an open version every median is marked not yet measured, and after it is not",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape clock, said at the top of the page once the trigger is crossed")] = new Scoped(
            Verdict.Pass,
            "once the trigger is crossed a line at the top of the run page says the shape calibration is due, and before it the page carries none",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape clock, each reason's share of the index as context")] = new Scoped(
            Verdict.Pass,
            "each reason's share on the night and its median over the ordinary nights are drawn as context, read back against shares worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "What the two clocks can do")] = new Scoped(
            Verdict.Pass,
            "the region opens with the sentence the code holds once, read off the page and off section 13.8 and the two found to be one sentence",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights run for the session the clock fell on against five")] = new Scoped(
            Verdict.Pass,
            "the nights run for the session the clock fell on that closed, counted off the run log against five, a night run by hand for a named session not among them",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the research passes carrying a recorded cost against twenty")] = new Scoped(
            Verdict.Pass,
            "the research passes carrying a recorded cost, counted as the operational header counts them, against twenty",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights the version step replayed both kinds against five")] = new Scoped(
            Verdict.Pass,
            "the nights the version step replayed a merge distance version and another rule's, read off the step's own line, against five",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the resolved event-book setups against 250")] = new Scoped(
            Verdict.Pass,
            "the event book's resolved setups against 250, the line saying no stage scores one yet",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights of trend labels under the third trend version against sixty")] = new Scoped(
            Verdict.Pass,
            "the nights of trend labels scored under the version whose new label holds more than one night, since its window opened, against sixty",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Event volume ratio")] = new Scoped(
            Verdict.Pass,
            "a night whose median volume ratio is at the constant is an event night and one a hundredth under it is not, over constructed nights",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Shape calibration nights")] = new Scoped(
            Verdict.Pass,
            "sixty ordinary nights under an open version cross the trigger, fifty-nine do not, and none counts before a version is open, over constructed runs",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Gate bands")] = new Scoped(
            Verdict.Pass,
            "each gate's row carries the band the constants hold, drawn beside its median on the run page",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "List band")] = new Scoped(
            Verdict.Pass,
            "the list's row carries the band of 1 to 9 the constants hold, drawn beside its median on the run page",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape proposal, each gate's setting held and proposed with its median count and the list's under each against their bands")] = new Scoped(
            Verdict.Pass,
            "each gate's setting held and proposed, its median count under each, its band, and the list's two medians and band are read back off the run page against a proposal worked out by hand over constructed nights",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape proposal, every gate no value in its range brings inside its band named as a finding")] = new Scoped(
            Verdict.Pass,
            "the gate no value in its range brings inside its band is drawn as a finding in the proposer's own words, read back against the constructed nights' answer",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The shape proposal, beside it the non-empty blocks accepting it would restart")] = new Scoped(
            Verdict.Pass,
            "the live filter's non-empty blocks are drawn beside the proposal against a count worked by hand, and read off the page they are the count a later acceptance must state, refused without it and at another",
            ByReadSurface),
        [CheckReach.Key(CatalogueTable, "Shape proposer")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and the proposals it inserts, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Shape proposer")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Shape command")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and the versions and decisions it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Shape command")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Shape proposals")] = new Scoped(
            Verdict.Pass,
            "one row per proposal carrying the settings held and proposed, each lever, the list's medians and the findings, written at the trigger and not before, and its decision written by the command alone, over constructed stores",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Shape lever ranges")] = new Scoped(
            Verdict.Pass,
            "over constructed nights whose right settings are worked out by hand, the proposer moves each gate to the value in its range nearest the one held that reaches the band and names the gate its range cannot reach, and the ranges the row states are the ones the proposer holds",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Shape acceptance bound")] = new Scoped(
            Verdict.Pass,
            "the ruled settings open the first version restarting nothing, the first acceptance while a live candidate stands states nothing, and a later one is refused without the page's count and at another with nothing changed and accepted at it; and `read-surface` reads that count off the run page beside the proposal and holds the command's refusals and its acceptance to it",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A shape proposal rejected")] = new Scoped(
            Verdict.Pass,
            "a rejection writes the decision, when and why on the proposal's row and nothing else, a second decision is refused, and the next proposal for the version waits on sixty more ordinary nights, over constructed stores",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The night's checkout is off main or holds a commit the remote's main lacks")] = new Scoped(
            Verdict.Pass,
            "the script's check mode over temporary repositories off main, holding a commit the remote's main lacks, clean, edited with a stray file, and behind the remote's main, the refusal file read back with its reason, the rest of a night run from the night's own commit, and the refusal drawn on both pages by `read-surface`",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Count the ordinary nights stored under the open filter version and, once they reach sixty, propose for each gate the one setting that brings its median count inside its band, writing one proposal for the version and stating the crossed trigger on the night's run log; nothing proposed is applied until the operator's command accepts it.")] = new Scoped(
            Verdict.Pass,
            "the night runs the step after the swing filter and before the facts, and it writes a proposal at the trigger and none before",
            ByNight),
        [CheckReach.Key(LimitsTable, "Trigger arrival window")] = new Scoped(
            Verdict.Pass,
            "a trigger first fired tonight, one session back and two sessions back passes, one that fired on every session of the window and the one before fails, and a session the answer turns on that stored no result fails and is named, over constructed sessions, and the constant the row states is the one the gate reads",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "The swing family")] = new Scoped(
            Verdict.Pass,
            "each of the six fires on its own side of every setting it moves and not a step past it, over constructed members, the family's command writes the six at one instant, and the constants the row states are the ones the family is registered with",
            ByExpectations),
        // 12.7's: the edge half, the near misses and section 17's two outcomes of the swing plan.
        [CheckReach.Key("15.10 Run", "The edge clock, each swing family candidate's non-empty blocks against the 8 its first look is read at")] = new Scoped(
            Verdict.Pass,
            "each candidate's non-empty blocks are read back off the run page against a count worked by hand over constructed nights, and over nine constructed closed blocks the rendered region draws nine of 8",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The edge clock, its resolved setups")] = new Scoped(
            Verdict.Pass,
            "each candidate's resolved setups are read back off the run page against the stored outcomes of the rows it fired on, worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The edge clock, its share against its planned break-even and calibrated null from the floor on and nothing below it")] = new Scoped(
            Verdict.Pass,
            "below the floor the page draws no share and the words that it is withheld, and over nine constructed closed blocks the rendered region draws the share, the planned break-even and the calibrated null worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The edge clock, that the first look can retire or leave a candidate and never promote one")] = new Scoped(
            Verdict.Pass,
            "the sentence the region states is read off the page with the sessions of the first look and of the earliest promotion, which the arithmetic takes from the looks and the block and cap lengths",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The edge clock, when the earliest promotion can come")] = new Scoped(
            Verdict.Pass,
            "the earliest promotion's 818 sessions are read off the page, being the look at 12 whole blocks and the last one's outcome window",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The edge clock, each variant with the filter version it was defined against and the settings the live filter has since moved")] = new Scoped(
            Verdict.Pass,
            "each variant names version 1, and after an acceptance moves the strength floor it names the setting moved while the live filter's new candidate names version 2, read off the page; the open version's settings rewritten under the rows change no figure the half draws, which reads the stored rows and recounts nothing",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Near misses, the setups the filter admitted")] = new Scoped(
            Verdict.Pass,
            "the admitted rows are counted off the run page against a constructed night's rows, and over nine constructed closed blocks the rendered group draws its share worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Near misses, beside each gate and each exclusion the setups it alone rejected with every other gate passing")] = new Scoped(
            Verdict.Pass,
            "each gate's and each exclusion's group is counted off the run page, a row failing two gates in none of them",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Near misses, each group against its own planned break-even and calibrated null")] = new Scoped(
            Verdict.Pass,
            "the rendered groups draw their share against their own break-even and bar worked by hand over nine closed blocks; the arithmetic over a constructed population with known outcomes is `fixture-expectations`'",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Near misses, every figure withheld below the block floor")] = new Scoped(
            Verdict.Pass,
            "every group below the floor draws the words that it is withheld and no share, on the run page and in the rendered region",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Swing plan outcome")] = new Scoped(
            Verdict.Pass,
            "worked by hand over constructed bars, the plan entered at the night's close wins, loses and runs out at the cap on both sides of it, the filler writes both horizons for every row carrying a plan and none for a row with none or one whose close sits outside its range, and the constant the row states is the one the scorer caps at",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Twenty-session outcome")] = new Scoped(
            Verdict.Pass,
            "worked by hand over constructed bars, a target reached on the twentieth session is a win and one reached on the twenty-first is unresolved at the twentieth, and the constant the row states is the one the scorer caps at",
            ByExpectations),
        // The 12.2 correction's: section 17's plan clear of the noise, the plan the live trade gate reads.
        [CheckReach.Key(LimitsTable, "Swing trade plan")] = new Scoped(
            Verdict.Pass,
            "worked by hand over constructed members, the stop at the setup band's low edge at one typical move below the entry and at the next support band's under it, the target at the band two typical moves above and not at one under it, the fixture's eight name-nights held to a derivation outside the repository, and the constants the row states are the ones the plan reads",
            ByExpectations),
        // 12.6's: tonight's list switched to the filter, the evening before the switch drawn as it was listed,
        // the rule's store, the run page's overlap and section 18's two rows.
        [CheckReach.Key("15.7 Tonight", "Night header, names the swing filter listed")] = new Scoped(
            Verdict.Pass,
            "the header states how many names the swing filter passed on an evening it listed, with the fired count beside it as context, read back off the page against the store's gate rows over a constructed night of forty passing, and an evening before the switch states the fired count alone",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, one row per name on the list")] = new Scoped(
            Verdict.Pass,
            "on a constructed night of forty passing the list draws the twenty first in the filter's order and no name that did not pass, read off the page against the store's gate rows in both directions, and a member the filter did not pass is drawn on no row whatever it fired",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, ordered by the state its reported quarters give it and within a state by the swing filter's reward to risk then strength then band strength")] = new Scoped(
            Verdict.Pass,
            "the rows' order is read off the page against an order worked by hand over constructed rows tied on the reward to risk and on strength, so each key of the order is the thing read, and over a constructed night holding one name in each state drawn improving, steady, the two reading no state, then deteriorating, whatever the filter's own rank",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, a line naming the rule that listed the evening")] = new Scoped(
            Verdict.Pass,
            "the line names the swing filter on an evening the store records as its own and the reasons on an evening it records nothing for, read off the page over both",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, the gates with the values that decided them")] = new Scoped(
            Verdict.Pass,
            "each drawn row carries its rank, the setup's family, the session its trigger arrived on, the plan its trade gate read with the reward to risk and the stop's distance, and each gate with why, read back off the row against the stored gate row",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "An evening before the switch, drawn as it was listed")] = new Scoped(
            Verdict.Pass,
            "an evening the store records no rule for is drawn by the names that fired in their order with the rule named, while its gate rows hold names that passed, which the page does not draw",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The list from night to night, how many of the night's names were on the list the evening before")] = new Scoped(
            Verdict.Pass,
            "worked by hand over constructed evenings, the evening before read by its own rule, read back off the run page",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The list from night to night, how many at least once over the five evenings before")] = new Scoped(
            Verdict.Pass,
            "worked by hand over constructed evenings with a name listed only on the sixth evening before, which the five do not count and the twenty do, read back off the run page",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The list from night to night, how many over the twenty evenings before")] = new Scoped(
            Verdict.Pass,
            "worked by hand over constructed evenings, each read by the rule that listed it, so a name that fired on an evening before the switch counts and one that fired on an evening the filter listed without passing does not, read back off the run page",
            ByReadSurface),
        [CheckReach.Key(StoresTable, "List rules")] = new Scoped(
            Verdict.Pass,
            "the night records the filter's session once the filter has stored its rows and not where it stored none, a session drawn again is recorded by the night that drew it last and an earlier session keeps its own, over constructed stores and the fixture's night",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The market gate closed on a night")] = new Scoped(
            Verdict.Pass,
            "a constructed night whose market gate closed draws no row and the line with the night's breadth and the gate's floor, and one whose breadth is not available the line saying so, read off tonight's page, the header's count 0, and close to a buy point drawing the members short of the market alone beneath its one line; the night asking for no report and saying why on its row is `nightly-run`'s",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "No name passed the swing filter on a night")] = new Scoped(
            Verdict.Pass,
            "a constructed night no name passed draws the line with how many reached each gate, worked by hand, read off tonight's page; the night asking for no report and saying why on its row is `nightly-run`'s, over the fixture's night, on which no member passes",
            ByReadSurface),
        // The 12.2 correction's two rows for the Past picks screen.
        [CheckReach.Key(FailureTable, "No trade listed yet")] = new Scoped(
            Verdict.Pass,
            "a store whose filter nights passed no name draws the one line saying no trade has been listed yet, and no count, share or table",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "A trade whose outcome row is missing")] = new Scoped(
            Verdict.Pass,
            "a trade with no forward return row is drawn with its plan, counted as listed and in no status, its status and its trade line saying no outcome stored with no dot",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "A still open trade whose outcome row is missing")] = new Scoped(
            Verdict.Pass,
            "a stock listed again inside the cap of a trade with no outcome row stands on Still open saying no outcome stored and read as open until its sessions run out, its listing marked on Past picks, and one listed again past the cap is a trade of its own",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Peer return window")] = new Scoped(
            Verdict.Pass,
            "a return is taken over sixty sessions and a series one short of the window and the close it is measured from has none, over constructed bars, and the constant the row states is the one the reading uses",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Peers drawn")] = new Scoped(
            Verdict.Pass,
            "a constructed group of sixteen others draws ten, the member sharing the name's industry and nine of the thirteen tied, and the constant the row states is the one the choice reads",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Peer likeness floor")] = new Scoped(
            Verdict.Pass,
            "sixty-one closes share sixty daily returns with the name and read a likeness, and sixty share fifty-nine and read none with the count, over constructed closes, and the constant the row states is the one the reading uses",
            ByExpectations),
        [CheckReach.Key(StoresTable, "Peer readings")] = new Scoped(
            Verdict.Pass,
            "one row per name carrying the session, the group, the year's high, the distance below it, the return, the bars and the members its table draws with each one's likeness, each read back off the store the fixture's replay wrote against figures worked by hand from the captured bars",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "peers")] = new Scoped(
            Verdict.Pass,
            "each fixture name's two readings and the bars they rest on are diffed against an expectation worked by hand from the captured bars through the replay's own rules rather than frozen from a run",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Event session share")] = new Scoped(
            Verdict.Pass,
            "a gate or reason whose median share over every night in the window is below the share the constant holds marks a night where it passes or fires for more than it, and one above it every night marks none, over constructed nights",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Group floor")] = new Scoped(
            Verdict.Pass,
            "an industry of five other members is the group and one of four gives way to the sector, over constructed members, and the constant the row states is the one the rule reads",
            ByExpectations),
        [CheckReach.Key("15.15 Queue", "Outstanding")] = new Scoped(
            Verdict.Pass,
            "every request nobody has started is drawn in this region and no other, read back off the page against the store in both directions, oldest first",
            ByReadSurface),
        [CheckReach.Key("15.15 Queue", "Being written")] = new Scoped(
            Verdict.Pass,
            "a request the worker has claimed is drawn here with the pass it is running under, and carries no control to take it out",
            ByReadSurface),
        [CheckReach.Key("15.15 Queue", "Settled")] = new Scoped(
            Verdict.Pass,
            "what each request came to is drawn with the reason where there is one, a withdrawn request among them, because nothing is deleted and what was asked for is still read",
            ByReadSurface),
        [CheckReach.Key("15.15 Queue", "Take it out")] = new Scoped(
            Verdict.Pass,
            "the control is drawn on an outstanding request and on no other, names the request by the instant it was asked at, and the press is refused once the worker holds it with the refusal naming the state that refused it",
            ByReadSurface),
        [CheckReach.Key("15.15 Queue", "When each will be written")] = new Scoped(
            Verdict.Pass,
            "every request drawn carries when its pass starts or started and ends or ended, read back off the page against a computation of the test's own: now, the end of a peak window, and after the requests ahead of it on the median of the finished passes, which names how many, with a store holding none saying it cannot estimate, in New York's time with its offset on both sides of the change of 2026-11-01 and UTC beside it",
            ByReadSurface),
        [CheckReach.Key("15.15 Queue", "Which lane would write one")] = new Scoped(
            Verdict.Pass,
            "the head of the page states the lane in the operator's two words and carries no model's name, the local choice is drawn with no control on it at all, and what it waits on is stated on the screen a reader decides on rather than on the element alone",
            ByReadSurface),
        [CheckReach.Key("15.8 Universe", "Researched")] = new Scoped(
            Verdict.Pass,
            "every name holding an accepted section besides the key under each figure is listed on its own route with the day its newest one was written and how many it holds, a name holding the key alone is not, and the masthead's search and the universe table carry the same day",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "A pass as it runs")] = new Scoped(
            Verdict.Pass,
            "the line states the step the pass's own rows are on and how many sections the name holds, saying the pass has started before its first row lands and that it has ended when its own row does, read for the run the page was handed and never for an earlier pass, and the page asks again and redraws as each section lands",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The numbers snapshot")] = new Scoped(
            Verdict.Pass,
            "the name page's numbers open on twelve rows of the newest filing, each drawn at the places it is read at with the stored value whole on its element and what it is measured against beneath it, read back against the stored payload by a query of the test's own; the five quarters stand under their own heading; management's passage is folded and drawn a point to an item that joins back into the passage as filed; the other filed tables are folded each under its own heading, the segments with each group's label once over its lines and a line filed with a colon and no figure drawn as a heading; and no provider is named in the page's words",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Regenerate Report")] = new Scoped(
            Verdict.Pass,
            "the control is offered where research stands or has gone stale and no pass has run on the day the page is read, and not on the day one ran, nor where none is written, nor while the cap has paused research; a press carrying it writes a request holding the ask, read back off the store by a query of the test's own, and the drain hands that request's pass the rewrite, read off the verb it runs",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Fact strip, close")] = new Scoped(
            Verdict.Pass,
            "the last stored close, read back off the strip's own attribute against the bar the store holds",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Fact strip, market capitalisation")] = new Scoped(
            Verdict.Pass,
            "the figure the provider files for the whole company, stored on the newest filing's row because a price moves every session, and stated as not on file for a name holding none",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Fact strip, the high and low of the move")] = new Scoped(
            Verdict.Pass,
            "the high and the low of the sessions the largest move spans, read as an aggregate over exactly those stored bars rather than over the calendar days between them",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Fact strip, next earnings date")] = new Scoped(
            Verdict.Pass,
            "the next dated event the calendar holds on or after the last stored session, which says the date is not on file rather than drawing a blank",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Fact strip, the multiples")] = new Scoped(
            Verdict.Pass,
            "the trailing and forward multiples, each drawn beside the earnings basis it was struck on, and each copied from the provider rather than computed from a close this payload does not carry",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Fact strip, the averages")] = new Scoped(
            Verdict.Pass,
            "the three moving averages the indicator engine wrote for the last stored session, each as its own attribute, with a reading the window was too short for stated as none rather than as a zero",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Fact strip, momentum and the typical daily move")] = new Scoped(
            Verdict.Pass,
            "the four momentum readings and the typical daily move, on the same terms as the averages",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "How it got here, the twelve-month picture")] = new Scoped(
            Verdict.Pass,
            "the level chart mark over the year to the newest stored session, drawn above the table of moves and with no bands",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The chart, the level chart")] = new Scoped(
            Verdict.Pass,
            "the region draws the level chart with its bands",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The chart, the volume profile beside it on the same price axis")] = new Scoped(
            Verdict.Pass,
            "the profile is drawn against the chart's own price axis",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The chart, the momentum panel beneath")] = new Scoped(
            Verdict.Pass,
            "the momentum panel is drawn beneath the chart with its neutral rules",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The chart, the level summary table with each band's members and dates")] = new Scoped(
            Verdict.Pass,
            "the summary table carries each band's members and their dates",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The plan, the plan column mark")] = new Scoped(
            Verdict.Pass,
            "the region draws the plan column",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The plan, the tranche table with conditions and stops")] = new Scoped(
            Verdict.Pass,
            "the tranche table carries each tranche's condition and its stop",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The plan, the exit table with actions")] = new Scoped(
            Verdict.Pass,
            "the exit table carries each exit and what it does",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The plan, the earnings setups")] = new Scoped(
            Verdict.Pass,
            "the earnings rule's setups are drawn where the plan states them",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The plan, the sizing arithmetic")] = new Scoped(
            Verdict.Pass,
            "the worked sizing example is drawn from the risk budget the reader chooses",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "How it got here, the table of the biggest moves")] = new Scoped(
            Verdict.Pass,
            "every stored move is drawn with its session, its span and its change, counted off the markup",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Operational header, what ran")] = new Scoped(
            Verdict.Pass,
            "every stage the night's run log carries is drawn, in the order they ran, with a command run by hand that day drawn as run by hand, left out of the night's stage time and never listed among the stages that failed, and a day holding only such a command never the night the page opens on",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Operational header, the instant each stage started and how long it took")] = new Scoped(
            Verdict.Pass,
            "the instant and the elapsed time are drawn from different values, on the cells as well as the attributes",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Operational header, model calls")] = new Scoped(
            Verdict.Pass,
            "each stage's model calls are drawn from the row rather than from a constant",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Operational header, network requests")] = new Scoped(
            Verdict.Pass,
            "each stage's request count is drawn from its row, over a night where they differ",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Operational header, spend")] = new Scoped(
            Verdict.Pass,
            "each stage's spend is drawn from its row",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Operational header, what each stage said about itself")] = new Scoped(
            Verdict.Pass,
            "the stage's detail is drawn in a cell rather than behind a pointer",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Reason records, the resolved count")] = new Scoped(
            Verdict.Pass,
            "a setup is counted against every reason that fired on the night it was listed",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Reason records, one row per reason with the reason track mark")] = new Scoped(
            Verdict.Pass,
            "one row per reason, each carrying the reason track mark",
            ByReadSurface),
        // 8.1. The setups whose price never reached the entry the plan named, in
        // their own column beside the resolved count and outside every rate.
        [CheckReach.Key("15.10 Run", "Reason records, the never-entered count")] = new Scoped(
            Verdict.Pass,
            "the setups a reason fired for whose price never closed at or below the entry zone's top edge, counted in a column of their own and in neither the resolved count nor any rate, read off the rendered row",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Harness, passed")] = new Scoped(
            Verdict.Pass,
            "the passed count is drawn from the phase report and never summed with the others",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Harness, failed")] = new Scoped(
            Verdict.Pass,
            "the failed count is drawn separately",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Harness, unexamined")] = new Scoped(
            Verdict.Pass,
            "the unexamined count is drawn separately from out of scope",
            ByReadSurface),
        // 8.6, the rule versions. One claim ends and six arrive, being the
        // scorer's catalogue and matrix rows, the two store rows, the nightly
        // step and the bound.
        [CheckReach.Key(LimitsTable, "Frozen measurement windows, a rule")] = new Scoped(
            Verdict.Pass,
            "a whole recorded night over a live window whose rule moved inside it stops at the version step naming the rule, with nothing scored and no close, and once the windows are closed with their evidence and opened again the next night keeps the closed rows as they were opened and scores under the new windows alone",
            ByNight),
        [CheckReach.Key(LimitsTable, "Frozen measurement windows, threshold")] = new Scoped(
            Verdict.Pass,
            "a reason's record, its track and its verdict count a fired reason only where its row stores the threshold the code carries, a row under another or under none counting toward nothing and staying stored, and every numeric threshold a reason is evaluated under is written onto its rows under its own name",
            ByReadSurface),
        [CheckReach.Key(CatalogueTable, "Rule version scorer")] = new Scoped(
            Verdict.Pass,
            "the class declares each store it reads, the versions it reads and writes, the scores it writes and drops, and the run log it appends to, reconciled against its row's cells and against SCHEMA's ownership; and the verb the row says a window opens and closes through is one the worker dispatches, names in its help and the runbook shows",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Rule version scorer")] = new Scoped(
            Verdict.Pass,
            "its row is read against the declaration cell by cell with the blanks included, the version columns filled and every store it does not touch left empty",
            ByAccess),
        [CheckReach.Key(StoresTable, "Rule versions")] = new Scoped(
            Verdict.Pass,
            "a window carries its rule, version, parameters, their hash with the code version, and the instant it opened, and a closed one carries when it closed, the evidence it was closed on and, for a replacement, the version the same write opened, with every other column as it was opened with, read back off a migrated store and off the fixture's version commands; a close without evidence is refused; and a window is refused at a value its replay would not apply as given or at its rule's live values, with nothing written and the refusal on the run log",
            ByRules),
        [CheckReach.Key(StoresTable, "Version scores")] = new Scoped(
            Verdict.Pass,
            "a score carries the name, the night, the rule, the version, the window it belongs to and the plan that version produced, flagged in sample for a session on or before the New York date its window opened on, read back off a migrated store and off the fixture's version commands; each version's plan over the fixture matches the plan worked by hand; a backfill replays only a night the store computed, at that night's own price scale, and keeps every score already stored; and a score is dropped one year back from the newest stored session and kept at it, whatever night is scored",
            ByRules),
        [CheckReach.Key(StoresTable, "Version blocks")] = new Scoped(
            Verdict.Pass,
            "a block carries its rule, its version, the window it belongs to, the session that window's blocks are counted from and, over that block, each side's excess and setup count with the two pairs of sums, read back off a store the scorer froze; every field a record exposes is derived from those rows alone, a record reaches its floor over a store the retention has already emptied behind it, a block's sums and its window's origin do not move once the rows they were computed from are gone, and a block past the last look is never written",
            ByTrendVersions),
        [CheckReach.Key(NightlyRunSteps.Heading, "Replay tonight's name-nights under every open version of each ladder rule from the stored bars, and store the plan each version produced, flagging as in sample any score for a session on or before the New York date its version's window opened on so it counts toward no record (see: A version's score counts only for a session after the New York date its window opened on). This makes no request: the bars are already stored, so a version costs the ladder arithmetic run again and, for a version of the merge distance, the level arithmetic as well. A live rule whose parameters or code have moved while a window measuring it is open stops the night at this step and names the rule (see: Adding a candidate later restarts the clock).")] = new Scoped(
            Verdict.Pass,
            "the step runs after every stage of the arithmetic it replays and before the close, writes a score per name per open version from stored bars flagged in sample for a session on or before the New York date its window opened on, and stops the night naming the rule where a live rule moved inside an open window",
            ByNight),
        [CheckReach.Key(LimitsTable, "Rule versions scored at once")] = new Scoped(
            Verdict.Pass,
            "the window past each rule's cap is refused and one below is admitted, live windows counted, with a version refused beside no live window; the fullest register the caps admit is projected from the night's own stage durations, read off the row, and held inside the deadline the night is bounded by; and `nightly-cost` runs a recorded night at one version and at the fullest register and finds the same requests on every step of both, counted off the feeds, none on the version step, and a score per version per name",
            ByRules),
        // 8.5, the reason verdicts. Nine claims end here and one arrives, being
        // section 17's significance threshold row.
        [CheckReach.Key("15.10 Run", "Reason records, the share that reached target before stop")] = new Scoped(
            Verdict.Pass,
            "the share is drawn with its denominator and its break-even, never one without the others, and only where the verdict was not withheld, over the resolved setups that set a bar with that set's own count as the denominator, read off the region's markup over populations holding unresolved setups and losses that set no bar",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Reason records, the break-even those setups demanded")] = new Scoped(
            Verdict.Pass,
            "the mean bar those setups set is drawn beside the share it is compared with, over the wins and losses that set one rather than over every resolved row or any unresolved setup",
            ByReadSurface),
        [CheckReach.Key("15.11 How a reason's record is displayed", "Below the minimum")] = new Scoped(
            Verdict.Pass,
            "a dashed outline carrying the count of resolved setups that set a bar against the floor the verdict named as short, and no rate, on the run page and on tonight's list, asserted at 249 of 250 and at 300 over 59 of 60 sessions, and over losses that set no bar and unresolved setups on sessions of their own, which fill neither floor",
            ByReadSurface),
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the share that reached the target before the stop")] = new Scoped(
            Verdict.Pass,
            "the share is drawn on the run page's cell and in tonight's reason span for a reason whose verdict was not withheld, read off the markup at 50 per cent of 300 and at 60 per cent of 250 with six losses that set no bar beside them",
            ByReadSurface),
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the number resolved")] = new Scoped(
            Verdict.Pass,
            "the denominator drawn with the share is the count the share was computed over, the resolved setups that set a bar, rather than the row's own resolved column, and a population where the two differ draws the first and never the second",
            ByReadSurface),
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the break-even those setups demanded")] = new Scoped(
            Verdict.Pass,
            "the mean bar is drawn beside the share it is compared with, read off the run page's markup at 33.5 per cent and tonight's reason span at 40 per cent",
            ByReadSurface),
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, always the three together")] = new Scoped(
            Verdict.Pass,
            "the three arrive on one cell or none of them does: over three records, one clearing both floors and two short of one each, exactly one cell carries a rate and the other two carry the count against the floor that is short; and on tonight's list a reason whose verdict was not withheld carries the share, the count and the bar in its one span while one short of a floor carries the count alone",
            ByReadSurface),
        [CheckReach.Key("15.11 How a reason's record is displayed", "Unresolved setups")] = new Scoped(
            Verdict.Pass,
            "the unresolved segment is its own and never folded into the rate, the tail or either floor: thirty unresolved setups that would turn a clearing verdict if read as losing draws leave the share, the tail and the sessions as they were, and the win and loss split is withheld from the mark under the verdict's own answer",
            ByReadSurface),
        [CheckReach.Key("15.11 How a reason's record is displayed", "Never shown")] = new Scoped(
            Verdict.Pass,
            "no single number with no denominator and no bar is drawn beside a ticker on any route, which the tonight and universe pages are read for",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "A condition has fired but nothing has resolved yet")] = new Scoped(
            Verdict.Pass,
            "a reason that fired and resolved nothing draws its count of nought against the minimum inside the dashed outline rather than an empty cell, so the state is readable as nothing resolved rather than as nothing measured",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Reason record display")] = new Scoped(
            Verdict.Pass,
            "the three states of section 15.11 are each drawn over constructed counts, on the run page and on tonight's list, and the record is beside the reason there and beside no ticker on any route",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Minimum resolved setups")] = new Scoped(
            Verdict.Pass,
            "no verdict of any kind appears below 250 resolved setups that set a bar or below 60 distinct listing sessions they arrived on, asserted at each boundary and one either side and over every population either side of each floor holding losses that set no bar and unresolved setups that would fill a floor if counted, with a withheld verdict never drawn as one that failed and the count drawn beside every withheld verdict; and the higher floor a live condition's retirement waits on is the constant section 17 and the runbook state, which no page computes and which is read against the resolved count drawn beside the reason",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Significance threshold")] = new Scoped(
            Verdict.Pass,
            "the exact one-sided tail matches four cases worked by hand, including one where no two break-evens agree so a binomial at any single value differs, the binomial at the mean is asserted to bound it at counts at or above the mean plus one and to be strictly larger over a spread, and the threshold, the level it was divided from as the record carries it, and the divisor are read off the run page beside every verdict, with the exact p drawn to five places and below them as the bound it lies under",
            ByReadSurface),
        // 10.1, the blocks the ordering region counts in, which 10.2's test reads as well.
        [CheckReach.Key(LimitsTable, "Looks a candidate's verdict is read at")] = new Scoped(
            Verdict.Pass,
            "the looks are read at the block counts section 17 states, the level each releases is the spending function's own figure at that fraction, and a first look over eight blocks all one way does not cross at the level the family is tested at while the same blocks cross at the whole level, each worked by hand",
            ByCandidateVerdicts),
        [CheckReach.Key(LimitsTable, "The calibrated bar")] = new Scoped(
            Verdict.Pass,
            "a plan whose target sits three times as far as its stop is given a lower bar than one whose target and stop are the same distance away, the round trip raises the bar and the sensitivity raises it further, and the same setup yields the same bar however often it is read",
            ByCandidateVerdicts),
        // 10.3, the numbers the three conditions are registered at.
        [CheckReach.Key(LimitsTable, "The three candidates' numbers")] = new Scoped(
            Verdict.Pass,
            "each condition fires where the expectation worked by hand says it does and nowhere else, at the numbers the registration writes and over the edges each turns on, being a close that fell through the zone, a zone exactly one typical move wide, a quiet night's median, a night the whole index traded heavily, a ratio exactly at the multiple, and a close exactly at the margin and a tenth short of it; and the command writes the three rows at one instant at those numbers or writes none of them",
            ByCandidateConditions),
        [CheckReach.Key(LimitsTable, "Power stated at a look")] = new Scoped(
            Verdict.Pass,
            "each look states the smallest excess it could have detected beside what it read, drawn on the region and read back off it, with the approximation it is named there",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Blocks a record is judged over")] = new Scoped(
            Verdict.Pass,
            "a session's block is counted in exchange sessions from the record's first, read at the last session of one block and the first of the next, a setup's window has closed at the sixty-third session traded after its listing and not the session before, and the region draws no comparison below the floor and says it is due at it, over constructed nights worked by hand",
            ByReadSurface),
        // 8.4, the shadow candidates region, read as the three claims its row
        // states. The half none of them says out loud is asserted with them: no
        // evaluation of a name reaches any route, which is what the region
        // exists to keep true.
        [CheckReach.Key("15.10 Run", "Shadow candidates, how many candidate conditions are registered")] = new Scoped(
            Verdict.Pass,
            "the count is the candidates standing, read off the region's markup against the register, at nothing registered, at two, and at one after a retirement, so it moves with the register rather than being drawn once, and one night's page read before and after a retirement draws the register as it stands when the page is read",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Shadow candidates, the family's divisor that number sets")] = new Scoped(
            Verdict.Pass,
            "the divisor is the family's own figure and the count the candidates standing, computed apart and asserted equal over the register before anything is registered, at one, at two and at one after a retirement, and a region handed a divisor that is not its count draws each from its own field, so a divisor that stops being the count shows on the page",
            ByReadSurface),
        // The 12.5 correction's count of distinct trials beside the divisor, on the operator's ruling of
        // 2026-09-27 that the level is shared across every trial for the life of the system.
        [CheckReach.Key("15.10 Run", "Shadow candidates, the count of distinct trials and the level each starts at beside it")] = new Scoped(
            Verdict.Pass,
            "over a register written back from the operator's store the region draws six trials beside a divisor of six, the level 0.05 over 6 and what the three looks release of it, 0.00019, 0.00232 and 0.00833, word for word after the divisor's line, with the first look said to be unable to promote; a seventh rule lowers every release and the same rule under a new name moves nothing, a retirement taken unread moves the count down with the divisor, a single trial's region does not say its first look cannot promote, and an empty register draws no trial",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Shadow candidates, one line saying each candidate's record is withheld until it is promoted")] = new Scoped(
            Verdict.Pass,
            "the line is drawn whether or not anything is registered, and over a store holding a shadow evaluation of every name on the night no evaluation and no candidate name reaches the run, tonight, universe or name routes, read off the rendered markup of each",
            ByReadSurface),
        // 8.3, the candidate register. The store row, the two failure rows and
        // section 17's family row all end here, because the migration that
        // creates the table is what lets each be put to something.
        [CheckReach.Key(StoresTable, "Candidate register")] = new Scoped(
            Verdict.Pass,
            "a registration carries the candidate, its rule, its test, the evaluator the code carries, the parameters it will run with, that evaluator's version and the instant, and a retirement is a new row naming what it retires with the retired row still standing byte for byte, read back off a migrated store",
            ByRegister),
        [CheckReach.Key(FailureTable, "Something tries to edit or delete a register row")] = new Scoped(
            Verdict.Pass,
            "an update, a delete and a replace in each form SQLite accepts one, against a migrated store with recursive triggers off and on, are each refused by the table itself, the register reads afterwards exactly as it did and nothing reaches the run log, because the refusal rolls back the statement it refuses; a change asked of the registrar is refused with the attempt on the run log naming the candidate that already stands; and nothing in the shipped source updates, deletes or replaces a register row, read in each of those forms",
            ByRegister),
        [CheckReach.Key(FailureTable, "The candidate register and the correction disagree")] = new Scoped(
            Verdict.Pass,
            "the divisor is computed over a register holding a retirement, a name retired and registered again and rows after the window, by two routes that state the rule differently, through the reader by the last row naming each candidate and by a query counting the registrations no later row names, at instants taking at least three values, and the check fails where they differ rather than reporting whichever answered; over the same store a set of the names registered less the names retired gives a different answer, so the routes do not share that rule",
            ByRegister),
        // The 12.5 correction's budget, on the operator's ruling of 2026-09-27.
        [CheckReach.Key(LimitsTable, "Distinct trials")] = new Scoped(
            Verdict.Pass,
            "a rule is read as the evaluator and every number its registration states, so the same numbers in another order or under another version are one trial and one number moved is two, and two names carrying one rule are one; over a register written back from the operator's store's 38 rows the sixteen names are fifteen rules and six trials, the retirements taken unread counted in none; the graph's first step is the significance over the count and not over the window, a promotion passing a sixth in equal parts where six are counted; and a look is read at the count as of its night",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Family size and correction")] = new Scoped(
            Verdict.Pass,
            "the maximum the row states is the bound the registrar refuses at, asserted at the bound and one below it and over a name retired and registered again, which stands once so the ninth candidate is refused; and the divisor counts the candidates standing before the window opened, over hand-worked rows covering a registration after the window, a retirement after it, a name retired and registered again and a registration in the second the window opened, which is read as after it, and over the fixture's derived rows",
            ByRegister),
        // The registrar's own two claims, which are what 8.3 adds.
        [CheckReach.Key(CatalogueTable, "Candidate registrar")] = new Scoped(
            Verdict.Pass,
            "the class declares the register it reads and writes and the run log it appends to, reconciled against its row's cells and against SCHEMA's ownership, which gives it the one insert on the table and no update and no delete; and the verb the row says it writes through is one the worker dispatches, names in its help and the runbook shows",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Candidate registrar")] = new Scoped(
            Verdict.Pass,
            "its row is read against the declaration cell by cell with the blanks included, the register's column filled for a read and a write and every other store's left empty",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Migration runner")] = new Scoped(
            Verdict.Pass,
            "the schema it writes is asserted against SCHEMA.md, column by column and type by type",
            ByMigration),
        [CheckReach.Key(CatalogueTable, "Verification harness")] = new Scoped(
            Verdict.Pass,
            "every claim this document makes carries a verdict and every table and figure carrying none is placed, and both artifacts are written and read back",
            ByHarness),
        [CheckReach.Key(CatalogueTable, "Backfill")] = new Scoped(
            Verdict.Pass,
            "the class declares the feed it reads and the stores it touches, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Backfill")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Bar store")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(StoresTable, "Membership")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(StoresTable, "Run log")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(CatalogueTable, "Membership loader")] = new Scoped(
            Verdict.Pass,
            "the class declares the feed it reads and the stores it touches, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Membership loader")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Migration runner")] = new Scoped(
            Verdict.Pass,
            "the row is blank throughout, the runner declares no store, and no statement against a declared table appears in its source",
            ByAccess),
        [CheckReach.Key(FailureTable, "The harness cannot parse this document")] = new Scoped(
            Verdict.Pass,
            "the parse guard fails rather than reporting zero claims",
            ByHarness),
        [CheckReach.Key(CatalogueTable, "Read API")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads, the run log it appends to and, from 9.2, the request store it inserts into and updates, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source; and the verbs the row names are ones the worker dispatches, names in its help and the runbook shows",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Read API")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, each read cell by cell, the run log read and written, and the request store the one other column carrying a write",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Mark renderer")] = new Scoped(
            Verdict.Pass,
            "the class declares an empty access, which is a claim rather than an omission, and it matches a row reading the API and writing none",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Mark renderer")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is blank and the declaration is empty, asserted cell by cell",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Single page app")] = new Scoped(
            Verdict.Pass,
            "the class declares an empty access and it matches a row reading the API and writing none",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Single page app")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is blank and the declaration is empty, asserted cell by cell",
            ByAccess),
                [CheckReach.Key("15.5 The mark vocabulary", "Level chart, candles")] = new Scoped(
            Verdict.Pass,
            "one candle is drawn per stored session, counted off the rendered markup and matched session by session against the store, hollow above the open and filled below in neutral ink",
            ByReadSurface),
        // The shortlist builder and tonight's list, 5.4.
        // The forward returns, the news pulse and the night's close, 5.5.
        [CheckReach.Key(CatalogueTable, "Forward return filler")] = new Scoped(
            Verdict.Pass,
            "the class declares the bars, listings and forward returns it reads and the forward returns it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "News pulse counter")] = new Scoped(
            Verdict.Pass,
            "the class declares the membership it reads, the news feed it calls and the pulse it writes and drops, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Night close")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it counts off and the run log it appends to, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Forward return filler")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(MatrixTable, "News pulse counter")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Night close")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Forward returns")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, with the base rate beside every return it is shown against",
            ByMigration),
        [CheckReach.Key(StoresTable, "News pulse")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, and its retention is owned by the component that writes it",
            ByMigration),
        [CheckReach.Key(FixtureTable, "gap stop")] = new Scoped(
            Verdict.Pass,
            "the five computed tables withhold every row for a name whose stored series holds an interior hole and the ladder and the listing still carry one naming the gap's date, asserted per table over a constructed store and with the split between the two halves asserted rather than a loop run over all seven",
            ByGap),
        [CheckReach.Key(FixtureTable, "forward returns")] = new Scoped(
            Verdict.Pass,
            "every horizon is recomputed in the suite from the bars after the listing and the plan the listing stored, with the matured cases over constructed series and constructed stores because the committed fixture has no session after its listings",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "news pulse")] = new Scoped(
            Verdict.Pass,
            "the count per name is recomputed from the same captured payload rather than read back from the counter, and no symbol outside the index carries a row",
            ByExpectations),
        // 5.6's expectation file, which section 19.1 gained a row for at the
        // phase 5 sign-off. Reached by the check that draws the page, since what
        // the file holds is the counts a record is computed from across every
        // night the store holds rather than one stage's serialised output.
        [CheckReach.Key(FixtureTable, "run page")] = new Scoped(
            Verdict.Pass,
            "each reason's firings and its resolved count are recomputed in the suite from the stored listings and forward returns, and the state each record is drawn in follows from the count against the minimum rather than from a value the test supplies",
            ByReadSurface),
        [CheckReach.Key(NightlyRunSteps.Heading, "Fill forward returns for past listings that matured today, and recompute the universe base rate, and score every swing filter row on each plan it carries (see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads).")] = new Scoped(
            Verdict.Pass,
            "the night runs the stage once rather than per name, and its run log row records the listings, the rows written, the rows kept as decided, the newly matured and the ones not yet matured apart",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Count today's articles per name from one dated news query, paged until the day is covered and every page counted, fanned out to names in code rather than asked for per name. The page count follows the day's news volume and not the size of the universe (see: News is one dated query, paged to cover the day, and attributed to names locally).")] = new Scoped(
            Verdict.Pass,
            "one dated query is fanned out to names in code, every page is counted on the run log row, and a day that reaches the page cap refuses rather than storing a truncated count",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Close the arithmetic and record its counts: names computed, names on the list, reasons fired, stale names, duration.")] = new Scoped(
            Verdict.Pass,
            "every count on the closing row is taken off the store the night has just written rather than reported by the stage that wrote it, and a run with no span says so rather than reporting a duration of zero",
            ByNight),

        [CheckReach.Key(CatalogueTable, "Shortlist builder")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and the listings it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Shortlist builder")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, which is what refused contradiction L on the first run",
            ByAccess),
        [CheckReach.Key(StoresTable, "Listings")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, and it carries a row per member per night with no deleter",
            ByMigration),
        [CheckReach.Key(FixtureTable, "listings")] = new Scoped(
            Verdict.Pass,
            "each of the six reasons is recomputed in the suite from the tables it reads and compared against what the builder wrote, rather than diffed against a set frozen from that builder, with breakout on volume asserted on each of its three edges as the expectation states them",
            ByExpectations),
        [CheckReach.Key(NightlyRunSteps.Heading, "Evaluate the list reasons for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs the stage in the order section 14 states, before the facts file, and its run log row records the members, the fired names and the reasons",
            ByNight),
        [CheckReach.Key(LimitsTable, "List display")] = new Scoped(
            Verdict.Pass,
            "at most twenty rows are drawn and the true fired count is stated whatever is drawn, asserted against a constructed night of forty, and the two lists share them with each count stated, asserted with fifteen and twenty on the list and fourteen one gate short",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Listing strip")] = new Scoped(
            Verdict.Pass,
            "one cell per evening over the window, a listed evening drawn differently in shape as well as in ink, and the count of listed evenings read off the markup",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Watch list")] = new Scoped(
            Verdict.Pass,
            "the region sits above the list and states how many names are watched, read off the store, with a link to the watch list page",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Add a name, a box offering the names of the index")] = new Scoped(
            Verdict.Pass,
            "the box is drawn on the page and offers the index's names from the list the masthead's search reads",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Add a name, which adds one while the list holds fewer than twenty and otherwise gives way to a line saying the limit is reached")] = new Scoped(
            Verdict.Pass,
            "a press adds a name of the index and the store holds it; at twenty the box is gone and the line saying the limit is drawn in its place, and a twenty-first is refused with nothing written",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Add a name, the same press beside a name on its own page")] = new Scoped(
            Verdict.Pass,
            "a name's own page offers Watch for a name not watched and Stop watching for one that is, each posting to the route the page's own press posts to",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Add a name, a refusal with the line saying why for a name not of the index tonight or already watched or a twenty-first")] = new Scoped(
            Verdict.Pass,
            "a name not of the index tonight, one already watched and a twenty-first are each refused with the line saying why and nothing written, and a press without the page's own header is refused",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Each name watched, in the order it was added")] = new Scoped(
            Verdict.Pass,
            "the rows are drawn in the order the store says each name was added, not the order of the tickers",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Each name watched, its company")] = new Scoped(
            Verdict.Pass,
            "each row carries the company the membership store names for it",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Each name watched, its close and the day's change")] = new Scoped(
            Verdict.Pass,
            "each row's close and day change are the night's close and its change from the session before, worked from the stored bars",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Each name watched, its trend in a word")] = new Scoped(
            Verdict.Pass,
            "each row's trend is the trend state the newest ladder on or before the night holds for it",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Each name watched, the reward to risk of its trade")] = new Scoped(
            Verdict.Pass,
            "each row's reward to risk is the one its trade gate read, as the gate row stores it",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Each name watched, the day it was added and a link to its page")] = new Scoped(
            Verdict.Pass,
            "each row carries the day of the press that added it and a link to the name's own page",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Each name watched, drawn for the newest night whether or not the list holds it")] = new Scoped(
            Verdict.Pass,
            "the page is dated by the newest night and a name the filter did not list is drawn as fully as one it did",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, listed and its number")] = new Scoped(
            Verdict.Pass,
            "a name the filter passed is drawn listed with its rank as the gate row stores it",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, the first gate that stopped it with that gate's reason")] = new Scoped(
            Verdict.Pass,
            "a name stopped is drawn with the first gate its stored row failed and that gate's own reason",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, what excluded it")] = new Scoped(
            Verdict.Pass,
            "a name that passed every gate and was excluded is drawn with the exclusion its stored row names",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, that no answer is stored for the night")] = new Scoped(
            Verdict.Pass,
            "a name with no gate row for the night is drawn saying so rather than as stopped",
            ByReadSurface),
        [CheckReach.Key("15.16 Watch list", "Take it out")] = new Scoped(
            Verdict.Pass,
            "each row's press takes the name off, and the page drawn again holds it no more",
            ByReadSurface),
        // The 12.2 correction's Past picks screen, the name page's region and the eighth mark, on the
        // operator's ruling of 2026-09-27, each read off the rendered page over a constructed store holding a
        // version 2 night and a version 3 night with a trade in every state.
        [CheckReach.Key("15.5 The mark vocabulary", "Trade line")] = new Scoped(
            Verdict.Pass,
            "over a full input the stop, the buy and the target are drawn at their places on the line by the prices' ratios to the buy and the dot hollow for an open trade and filled for a finished one, a price past either end sits at that end, and over the inputs it degrades on it draws no line or no dot and says what it lacks",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Year line")] = new Scoped(
            Verdict.Pass,
            "over a full input the closes are one line with the nearest support and the nearest resistance drawn across it in their hues and named with their prices set a line apart where the two sit close, and over an input of fewer than two closes it draws nothing and says how many it holds",
            ByReadSurface),

        // The 12.3 correction that opens the Run page on its pictures: its first three regions as the parts
        // their rows state, the folded detail, the four marks they draw and section 17's market label.
        [CheckReach.Key("15.10 Run", "How last night went, a status mark and a headline naming the night's state as its own run log rows give it")] = new Scoped(
            Verdict.Pass,
            "each state worked by hand over constructed run log rows, finished, stopped, running and left unfinished either side of the deadline, not yet run, never ran and no session, and the finished and stopped headlines read off the rendered page with their marks' tones",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "How last night went, four headline figures being the stocks read and the provider requests with the research spend and the steps run again")] = new Scoped(
            Verdict.Pass,
            "the stocks the close counted, the requests of every run of the night, the evening's spend and the stops of an earlier run worked by hand and read off the rendered tiles",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "How last night went, a time bar of the night's steps in six named groups showing where a stopped night stopped")] = new Scoped(
            Verdict.Pass,
            "the six groups read off the page with each group's seconds worked by hand, a group not reached drawn dashed, the group a stopped night stopped in marked, and every stage the fixture's night writes in exactly one group",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The market, a one-word label read by a stated rule")] = new Scoped(
            Verdict.Pass,
            "the word worked by hand at the floor and a hair below it and at the healthy point and a hair below it, and a night's weak word read off the page against the floor of the version it ran under",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The market, a gauge of the share of members above their 200-day average with the market gate's floor marked")] = new Scoped(
            Verdict.Pass,
            "the gauge's share and the version's floor read off the rendered region",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The market, the share above the 50-day average and the index's volume against its fifty-day average")] = new Scoped(
            Verdict.Pass,
            "the stored context share and volume ratio read off the rendered region",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The market, a line of that share over the sixty sessions before the night that the store holds averages for")] = new Scoped(
            Verdict.Pass,
            "each session's share worked by hand over constructed closes and averages, a name holding a close and no average counted among the names, and the line's session count read off the page",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "From the index to tonight's list, a funnel of how many members passed each of the filter's checks in turn down to those left after the exclusions")] = new Scoped(
            Verdict.Pass,
            "each bar's count through the checks in turn worked by hand over constructed members and read off the rendered bars",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "From the index to tonight's list, the count listed with a link opening tonight's list")] = new Scoped(
            Verdict.Pass,
            "the count listed and the link to the night's list read off the rendered region",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "The detail")] = new Scoped(
            Verdict.Pass,
            "every table the page drew before read off the page inside a section folded shut beneath the pictures",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Night status")] = new Scoped(
            Verdict.Pass,
            "the ring's tone for each state and the time bar's groups, a group not reached dashed and a stopped group marked, read off the rendered mark, and its rules drawn in the status colours and never a level's",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Market gauge")] = new Scoped(
            Verdict.Pass,
            "the share, the floor and the word read off the rendered gauge, its rules drawn in the status colours and never a level's",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Breadth line")] = new Scoped(
            Verdict.Pass,
            "the sessions read from the store worked by hand and the count it holds read off the page",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Funnel bars")] = new Scoped(
            Verdict.Pass,
            "one bar for the index and one for each check in turn with its count, read off the rendered mark",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Market label")] = new Scoped(
            Verdict.Pass,
            "the word worked by hand at the floor and a hair below it and at 60% and a hair below it, and the healthy point pinned to the code",
            ByReadSurface),

        // The 12.3 correction drawing the Run page's trades, freshness, research and checklist.
        [CheckReach.Key("15.10 Run", "How the list's trades are going, the live list's trades still open and those that reached the target or were stopped out or ran out of time")] = new Scoped(
            Verdict.Pass, "each status's count read off the rendered tiles from Past picks' own summary, over constructed summaries and a constructed night", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How the list's trades are going, a ring of the trades decided against the minimum a share waits on")] = new Scoped(
            Verdict.Pass, "the decided count against the minimum on the ring, its track dashed below the minimum", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How the list's trades are going, the share and the break-even and the average result together once both minimums are met as Past picks draws them")] = new Scoped(
            Verdict.Pass, "the three figures drawn together at the minimums and none of them below, with the counts it waits on said instead", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How the list's trades are going, a link opening Past picks")] = new Scoped(
            Verdict.Pass, "the link to Past picks read off the rendered region", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Is the list finding new stocks, a bar for each of the last twenty evenings split into the names new that evening and those the evening before also listed")] = new Scoped(
            Verdict.Pass, "each evening's new and repeated names worked by hand over constructed evenings of both rules, the evening before the first filter night read and not drawn", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Is the list finding new stocks, the night's split in words")] = new Scoped(
            Verdict.Pass, "the night's split and the share new over the evenings drawn read off the rendered words", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Research and spend, the month's spend against the month cap")] = new Scoped(
            Verdict.Pass, "the month's spend and cap read off the rendered region", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Research and spend, the reports the paid model wrote on each of the last seven nights")] = new Scoped(
            Verdict.Pass, "each night's paid passes worked by hand over constructed run logs, a refused call and a command run by hand counting none", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Research and spend, the reports and the overnight drafts written over those nights")] = new Scoped(
            Verdict.Pass, "the reports and the drafts the queue completed summed over the nights and read off the rendered tiles", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How each report did, one row per report over the seven nights with its stock and day and what it cost")] = new Scoped(
            Verdict.Pass, "a pass whose paid model answered drawn as a row with the sum of its calls worked by hand and one whose only call was paused drawn as none, read through the read API over a constructed store and back off the rendered page, and a report older than the region's nights held for the rates and not drawn", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How each report did, a cell per section saying whether it passed first time or on retry or was left out with why or was not warranted, with what its own calls cost and none of a trial's")] = new Scoped(
            Verdict.Pass, "every outcome worked by hand over one constructed pass, the retry's accepted version read as on retry, a fallback's refusal as the name page words it, a declined cycle's line off the theme pass and a section standing from earlier as not warranted, each with its own calls' cost and a trial's calls in none", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How each report did, the two cases' cell marked where a draft of the pass carried a figure on both sides, with how many of the newest twenty reports' two cases did")] = new Scoped(
            Verdict.Pass, "the detector worked by hand over the coarser rounding, a percentage against an amount and a draft whose sides cannot be cut, and the count over three reports reading a refused first draft whose accepted retry carried none", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How each report did, each section's share passed first time and its share left out over the newest twenty reports that warranted it")] = new Scoped(
            Verdict.Pass, "worked by hand over more reports than the window holds, the oldest outside it, a section warranted by fewer than the window read over those it had and one warranted by none over none, and at an earlier night over the reports written by then", ByReadSurface),
        [CheckReach.Key(LimitsTable, "Report rates")] = new Scoped(
            Verdict.Pass, "the window read off the document against the constant, and the rates and the two cases' count worked by hand at it and below it", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Anything to worry about, a checklist of plain items each turning red with its reason where it fails")] = new Scoped(
            Verdict.Pass, "every item held with no reason, failed with its reason worked by hand, and not read where the night stored nothing", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Anything to worry about, every stock holding the night's prices and every step of the night finished")] = new Scoped(
            Verdict.Pass, "the stale names and the night's own state, a command run by hand none of it and a failure after the close a failure, worked by hand and read off the page", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Anything to worry about, no research document refused and no section fallen back")] = new Scoped(
            Verdict.Pass, "the refusals and the sections that fell back counted and named", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Anything to worry about, every company awaiting a quarter asked on schedule")] = new Scoped(
            Verdict.Pass, "the night's quarters step read held, refused, left at its limit and not run", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Anything to worry about, the four harness counts beneath")] = new Scoped(
            Verdict.Pass, "the four counts read off the rendered tiles", ByReadSurface),
        // 12.6's correction, the model profiles: a profile near its retirement date and a report costing more
        // than section 17 names, each an item of the checklist.
        [CheckReach.Key("15.10 Run", "Anything to worry about, no paid model a job uses within thirty days of its retirement date, and where one is its profile named with its job and date")] = new Scoped(
            Verdict.Pass, "a profile thirty days before its date named with its job and date, one thirty-one days before and one whose provider publishes none not named, worked by hand over constructed profiles", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Anything to worry about, no report whose pass ran on the night's session costing more than section 17 names, and where one did its stock named with its cost")] = new Scoped(
            Verdict.Pass, "a pass whose calls sum to a cent over the amount named with its stock and cost, and one summing to the amount exactly not named, worked by hand over constructed rows", ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Trades ring")] = new Scoped(
            Verdict.Pass, "the ring's arc and its dashed track below the minimum read off the rendered mark", ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Freshness bars")] = new Scoped(
            Verdict.Pass, "each evening's two parts read off the rendered bars, and an empty input saying so", ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Research bars")] = new Scoped(
            Verdict.Pass, "the spend bar and each night's bar read off the rendered mark", ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Checklist")] = new Scoped(
            Verdict.Pass, "each item's state and reason read off the rendered list", ByReadSurface),

        // The 12.3 correction drawing how the system learns, the comparison of tonight's picks and each version at a
        // checkpoint.
        [CheckReach.Key("15.10 Run", "How the system learns, the shape clock as the ordinary nights under the open filter version against the sixty its calibration waits on")] = new Scoped(
            Verdict.Pass, "the ordinary nights and the nights wanted read off the rendered bar from the shape clock the page already draws", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How the system learns, each check's typical pass count against its range drawn dashed until measured")] = new Scoped(
            Verdict.Pass, "each check's range read off the rendered mark, dashed and holding no dot before the trigger", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How the system learns, the edge clock as a line from today to its two checkpoints with the blocks the live list has gathered")] = new Scoped(
            Verdict.Pass, "the sessions run and the two checkpoints' sessions read off the rendered line with the live list's blocks against the floor", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How the system learns, each version running beside the live list with what it changes and the stocks it has picked and the share the live list also picked and its blocks against the floor")] = new Scoped(
            Verdict.Pass, "each version's picks and the share the live list also picked worked by hand over constructed gate rows and read off the rendered table", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How the system learns, a link comparing tonight's picks")] = new Scoped(
            Verdict.Pass, "the link naming the first version after the live list read off the rendered region", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, a choice of the versions running beside the live list kept in the link")] = new Scoped(
            Verdict.Pass, "the version the link names chosen and selected on the page, and the shell's listener writing a choice to the link", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, the names only the live list picked and those both picked and those only the version picked with the setting that made the difference beside each name only one side picked")] = new Scoped(
            Verdict.Pass, "the three groups and the reason beside each one-sided name worked by hand over constructed gate rows, for a version picking what the live list picked and one picking nothing it did", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, two overlapping rings holding the three counts")] = new Scoped(
            Verdict.Pass, "the three counts read off the rendered rings", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, the version's picks over the last twenty evenings with the share the live list also picked and the evenings it picked a stock the live list did not")] = new Scoped(
            Verdict.Pass, "the picks, the share and the evenings ahead worked by hand over constructed evenings", ByReadSurface),
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, picks alone with no figure from how a pick turned out")] = new Scoped(
            Verdict.Pass, "a version's pick with a stored outcome draws no outcome in the region, and its names reach no other screen and no other region of the run page", ByReadSurface),
        [CheckReach.Key("15.10 Run", "At a checkpoint, one row per version on a scale from nought to a hundred")] = new Scoped(
            Verdict.Pass, "a row for each version read off the rendered region", ByReadSurface),
        [CheckReach.Key("15.10 Run", "At a checkpoint, before its first look a locked dashed outline with its trades and blocks so far")] = new Scoped(
            Verdict.Pass, "a version below its first look locked with its setups and blocks worked by hand", ByReadSurface),
        [CheckReach.Key("15.10 Run", "At a checkpoint, from its first look the share of its trades that reached the target with the break-even they needed and what no skill scored from the same starts and how far luck alone could move it")] = new Scoped(
            Verdict.Pass, "the share, the break-even and the null worked by hand over nine constructed blocks and read off the rendered scale", ByReadSurface),
        [CheckReach.Key("15.10 Run", "At a checkpoint, the verdict in words")] = new Scoped(
            Verdict.Pass, "the last look's verdict read off the rendered row", ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Clock lines")] = new Scoped(
            Verdict.Pass, "the shape bar, each range and the edge line read off the rendered marks", ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Overlap rings")] = new Scoped(
            Verdict.Pass, "the two rings and the three counts read off the rendered mark", ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Checkpoint scale")] = new Scoped(
            Verdict.Pass, "a locked row and an unlocked row with its dot, rule and band read off the rendered mark", ByReadSurface),

        // The 12.3 correction building the night's tries, tonight's notice and the press running the rest of a night.
        [CheckReach.Key("15.7 Tonight", "The night's state, a notice at the top naming the night's state as the Run page's headline names it")] = new Scoped(
            Verdict.Pass, "the notice's state read off tonight's page and the headline's off the Run page, equal for every state a night can be in over a run log worked by hand for each", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The night's state, each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished")] = new Scoped(
            Verdict.Pass, "try 1 with the step it stopped at and its reason read off the notice while the night waits on try 2", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The night's state, a press running the rest of a night left unfinished")] = new Scoped(
            Verdict.Pass, "the press drawn once on a night left unfinished and on no waiting night, and the route it posts to refused without the header, while a night holds the lock and on a finished night", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The night's state, a one-line note where the night finished")] = new Scoped(
            Verdict.Pass, "the finished night's notice read whole as its one line", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How last night went, each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished")] = new Scoped(
            Verdict.Pass, "the tries worked by hand over constructed run log rows, a try again read with what the earlier try stored, and read off the Run page", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How last night went, a press running the rest of a night left unfinished")] = new Scoped(
            Verdict.Pass, "the press read off the Run page on a night left unfinished and absent while it waits", ByReadSurface),

        // The 12.3 build of the night from a clean copy of the committed code: a refusal before any worker exists
        // on tonight's notice and the Run page, and the commit the night recorded on the Run page.
        [CheckReach.Key("15.7 Tonight", "The night's state, a refusal before its first step with its reason")] = new Scoped(
            Verdict.Pass, "the notice read off tonight's page over a refusal file in the script's own shape, naming the refusal and its reason for the session it fell on and never ran for another", ByReadSurface),
        [CheckReach.Key("15.10 Run", "How last night went, the commit the night was built from")] = new Scoped(
            Verdict.Pass, "the line read off the Run page over a build row the night writes, and absent over a night that recorded none", ByReadSurface),

        // 12.7's correction, close to a buy point, on the operator's specification of 2026-09-29.
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, one row per member whose stored result missed exactly one of the five gates and carries no exclusion")] = new Scoped(
            Verdict.Pass, "a constructed night's members one gate short drawn and none missing two, excluded or on the list, read off the page against the store's gate rows in both directions", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, nearest to qualifying first with a tie in the list's own order")] = new Scoped(
            Verdict.Pass, "the order read off the page against distances worked by hand from the version's settings, a tie at half a bar drawn by the trade's reward to risk and four members at one distance on a closed market by reward to risk then strength", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, drawing the places the list leaves of the twenty")] = new Scoped(
            Verdict.Pass, "fifteen on the list leaving five rows and twenty leaving none, read off both lists on the page", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, a line above them stating how many are drawn of how many are one gate short")] = new Scoped(
            Verdict.Pass, "the line read whole where every row is drawn, where five of fourteen are and where none is", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, what a row of the list carries")] = new Scoped(
            Verdict.Pass, "each row drawn by the list's own row writer with its column headings, the gates' column alone replaced", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, the gate it missed in place of the gates")] = new Scoped(
            Verdict.Pass, "each row's gate named in its cell and the column headed for it, with no gates column drawn", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, what it had against the bar it needed in plain words")] = new Scoped(
            Verdict.Pass, "each gate's words read whole off its row against the constructed values and the version's bars", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, how far short that is as a share of the bar")] = new Scoped(
            Verdict.Pass, "each row's share short read off the page against the distance worked by hand, a trigger four sessions back against a window of three reading two thirds", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, the trade its plan states")] = new Scoped(
            Verdict.Pass, "the entry, stop and target of the plan the version's trade gate reads, read off each row against the stored row", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, one line above the rows stated once saying they would qualify if the market turned")] = new Scoped(
            Verdict.Pass, "on a closed market the line with the breadth and its bar read once off the list and no row saying it again", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, a key saying it recommends nothing")] = new Scoped(
            Verdict.Pass, "the key's sentence the code holds read off the page, and drawing the list storing nothing and Past picks holding none of its names", ByReadSurface),
        // The 12.2 correction that reads one open trade per stock on the pages: Still open on tonight's page, read
        // as the nine parts its row enumerates.
        [CheckReach.Key("15.7 Tonight", "Still open, one row per stock that passed every gate on the night with nothing excluding it but an open trade while a trade the live list recommended for it on an earlier night is still open on this one")] = new Scoped(
            Verdict.Pass, "over a constructed store, the three stocks passed on every gate while an earlier trade is open drawn in the list's order, a stock freed the session before and one gate short on neither", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, the night that trade was listed on")] = new Scoped(
            Verdict.Pass, "each row's night cell and its link carry the earlier night the open trade was listed on", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, the trade line")] = new Scoped(
            Verdict.Pass, "each row draws the open trade's line, hollow at the newest close while open and filled where it ended", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, where the price stands against that trade's stop and target")] = new Scoped(
            Verdict.Pass, "the newest close in words against the trade's stop and target, each whole on the cell", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, whether the trade ended at this night's own close and frees the stock from the next night")] = new Scoped(
            Verdict.Pass, "a trade stopped out at the night's own close is drawn as still open on that night and its words say it is free again from the next night", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, before the rule reaches the filter the stock stands on the list as well and is marked as listed again while that trade is open")] = new Scoped(
            Verdict.Pass, "the same three rows stand on the list carrying the night of the open trade and the mark, and the freed stock's row carries neither", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, once the rule reaches the filter the stock is excluded there and drawn here alone")] = new Scoped(
            Verdict.Pass, "the region reads a stock whose only exclusion is the open trade's, and the key states the change", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, a line where there is none")] = new Scoped(
            Verdict.Pass, "an evening holding no stock passed again while an earlier trade is open draws the line saying so", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Still open, a key saying it is not a new trade")] = new Scoped(
            Verdict.Pass, "the key's sentence the code holds read off the page", ByReadSurface),
        [CheckReach.Key(FailureTable, "Both lists on tonight's page empty on a night the swing filter listed")] = new Scoped(
            Verdict.Pass, "the second list's line drawn where its rows are empty, beside the list's own line on a night no name passed", ByReadSurface),

        // 13.1, the family framework: the lister's catalogue and matrix rows, its two stores, section 17's five a
        // family, section 18's three rows, and the card tonight's page draws a family, read as the parts its
        // row enumerates. The two parts and the row that need a second family on the page are due with it.
        [CheckReach.Key(CatalogueTable, "Family lister")] = new Scoped(
            Verdict.Pass,
            "the class declares the bars, gate results, filter versions, fundamental readings, list rules and forward returns it reads and the family nights and family picks it reads, inserts and deletes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Family lister")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Family nights")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(StoresTable, "Family picks")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(LimitsTable, "Names a family lists")] = new Scoped(
            Verdict.Pass,
            "over three constructed families worked by hand, a family lists its first five that hold no open trade and are on no earlier card, a family passing exactly five lists all five, the sixth is held back and may be listed by a later family, and over a constructed night the lister stores the first of those the family passed and names the ones past them; the count is read off the document against the constant",
            ByExpectations),
        [CheckReach.Key("15.7 Tonight", "A family's card, one card a family in the page's order")] = new Scoped(
            Verdict.Pass, "over a constructed night the lister drew, the card read off the page with its place of how many, in place of the one list and of Still open, and an earlier night drawn as it was listed", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, the family's rule in a sentence")] = new Scoped(
            Verdict.Pass, "the card's heading and its rule's sentence read off the page against the family's own words", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, the day its rule went live or the words saying it is provisional")] = new Scoped(
            Verdict.Pass, "the day the family's live candidate was registered read off the card over a constructed register, and the ruling's words where no registration stands", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, how many it lists tonight with how many variants are scored beside it")] = new Scoped(
            Verdict.Pass, "five picks and the two variants standing of three registered, one retired, read off the card's line", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, its place down the page")] = new Scoped(
            Verdict.Pass, "each row's place read off the card against the stored list's places, in both directions", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, the stock with a label for each other family it qualified under")] = new Scoped(
            Verdict.Pass, "a stock two families passed drawn once on the earlier card with the other's label, read off the page", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, the state its reported quarters give it")] = new Scoped(
            Verdict.Pass, "the state the night read drawn in the row's cell, and a row whose business was not read saying so", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, the buy and the stop and the target")] = new Scoped(
            Verdict.Pass, "the three prices of the plan the night's trade gate read, read off the row against the stored gate row", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, where the buy sits between the stop and the target")] = new Scoped(
            Verdict.Pass, "the mark's place worked by hand, 4 of the 14 from the stop to the target, read off the row", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, the reward to risk")] = new Scoped(
            Verdict.Pass, "the figure the trade gate stored read off each row to the hundredth", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, why it is listed tonight in the figures its family stored")] = new Scoped(
            Verdict.Pass, "the sentence read whole off the row against the depth, the band's edges, the dry-up and the trigger's session its gates stored, and a row whose gates stored none saying only what they stored", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, the positive and negative stories of the thirty days before")] = new Scoped(
            Verdict.Pass, "two positive and one negative labelled stories read off the row's cell, and a row holding no label saying so", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, a note for each stock it passed that a trade still open holds back")] = new Scoped(
            Verdict.Pass, "the note naming the stock and the night its open trade was listed on read off the card, with no row drawn for it", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, a note for each stock the page lists under an earlier family")] = new Scoped(
            Verdict.Pass, "the note under the later family's card naming the card that draws the stock, read off the page", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, a note counting the stocks past its five")] = new Scoped(
            Verdict.Pass, "the note counting the two past the five and naming them read off the card", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, a line saying why where it lists none")] = new Scoped(
            Verdict.Pass, "a night the market check closed drawing the line with the breadth and its floor, and a night no stock passed the line with how many reached each gate, each read whole off the card", ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "A family's card, a key saying how to read it")] = new Scoped(
            Verdict.Pass, "the key's sentence the code holds read off the page", ByReadSurface),
        [CheckReach.Key(FailureTable, "A stock a family passes while a trade for it is still open")] = new Scoped(
            Verdict.Pass, "over a constructed night the stock stored as held back with the family and the night that listed its open trade, the card's note saying so and no row drawn for it; a stop's night still holding the stock and a name past five holding none", ByReadSurface),
        [CheckReach.Key(FailureTable, "A stock two families pass on one night")] = new Scoped(
            Verdict.Pass, "a stock two families passed stored as listed under the earlier and under another for the later, drawn once with the other's label and named in the later card's note", ByReadSurface),
        [CheckReach.Key(FailureTable, "A night the families list no stock")] = new Scoped(
            Verdict.Pass, "the session recorded as one the families drew with no listed row, the night asking for no report, and the card drawn with why it lists none", ByReadSurface),

        // 13.2, breakouts: the family evaluator's catalogue and matrix rows, its store, section 17's rows for the
        // breakout's settings and section 18's two rows.
        [CheckReach.Key(CatalogueTable, "Family evaluator")] = new Scoped(
            Verdict.Pass,
            "the class declares the bars, indicators and gate results it reads and the family results it reads, inserts and deletes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Family evaluator")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Family results")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(LimitsTable, "Breakout high window")] = new Scoped(
            Verdict.Pass,
            "over a constructed year worked by hand, a close a cent above the highest high of the sessions before passes, one at that high and one a cent beneath do not, and a member one session short reads not available with the count it holds",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Breakout volume multiple")] = new Scoped(
            Verdict.Pass,
            "over constructed volumes against a stored average, the volume at the multiple passes, one share under it does not and one over it does, and the names passing are ordered by the multiple, largest first, with the ticker where two tie",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Breakout range window")] = new Scoped(
            Verdict.Pass,
            "over constructed sessions the mean daily range of each window is worked by hand as a share of the close, and a member holding a session too few for both windows reads not available with the count it holds",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Breakout range ceiling")] = new Scoped(
            Verdict.Pass,
            "the newer window's ranges equal to the older's pass at the ceiling, and ranges a hundredth wider do not, each with the ratio the gate stored",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Breakout stop")] = new Scoped(
            Verdict.Pass,
            "the stop placed by hand beneath the close from a stored typical move, and over constructed closes the stop raised with a new high close, held where a later close is lower, a close at it not selling and a close under it selling, with the result in multiples of the risk",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Breakout session cap")] = new Scoped(
            Verdict.Pass,
            "a constructed trade never under its stop ends at its last session as unresolved with what it made, one a session short of its last is not yet matured, and over a constructed store the filler writes the same",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A member holding too few sessions for a breakout to be read")] = new Scoped(
            Verdict.Pass,
            "a member one session short of the year stored as not passed, one gate short, with the reason naming the count it holds, and a member holding no bar for the night stored the same way",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A breakout with no typical move to place its stop by")] = new Scoped(
            Verdict.Pass,
            "a member passing every other gate on a night storing no typical move is not passed and stores no stop, and a stored trade whose night's raw close sits under its stop is counted as not scorable and given no outcome row",
            ByExpectations),

        // 12.6's correction drawing the news: the name page's region, read off the rendered page over a constructed store.
        [CheckReach.Key("15.7 Tonight", "The list, the positive and negative stories of the thirty days before the night as the name page's bar counts them")] = new Scoped(
            Verdict.Pass,
            "each row of the first list carries the positive and negative stories of the window as the test's own arithmetic over the store counts them, the newest label of each article with opinion pieces and articles before the window left out, a name with no label saying so, and the second list's rows carrying none",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Research and spend, the news labeller's line with what the night's labelling cost and the month's against its limit and the articles labelled and the unreadable answers by cause and what stopped it")] = new Scoped(
            Verdict.Pass,
            "the night's line is read off the labeller's own row for that night with its cost, the month's and the limit, the labels, the unreadable by cause, the refused, the names reached and the stop, and a night with no row says so",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights the news labeller ran against twenty with its measured duration and its month's spend and the share of answers refused for a digit")] = new Scoped(
            Verdict.Pass,
            "the labeller's nights are counted off its own rows against twenty, with the median run in minutes, the month's spend the newest row states and the answers refused for a digit of every answer read, and a store with no run counts none",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "News")] = new Scoped(
            Verdict.Pass,
            "the region is drawn after the nights the list picked the name, its bar and tabs counting as the test's own arithmetic over the store gives them, the article labelled twice reading its newer label, an opinion piece under its tab alone and in no count, a refused and an unreadable row saying so, the model named once, a name holding no label for the night saying why by each cause the store is given, and a name with nothing stored saying so",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "On the list before")] = new Scoped(
            Verdict.Pass,
            "a name the live list picked before the page's night draws the region after the plan and its earnings reactions, its line counting the picks and each group as the test's own arithmetic over the store gives them and a row per earlier listing as it stood on the page's night, and a name never picked before draws none",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, the trades listed with the nights they were listed on")] = new Scoped(
            Verdict.Pass,
            "the count of trades and of their nights is the test's own count of the names the filter passed on the nights it listed, a candidate's plan, a member one gate short and an evening the reasons listed left out",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, how many are still open and how many finished")] = new Scoped(
            Verdict.Pass,
            "the open and finished counts are the test's own over the constructed trades",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, how many reached the target and how many were stopped out or ran out of time")] = new Scoped(
            Verdict.Pass,
            "each finished status's count is the test's own over the constructed trades",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, a line saying how many carry no outcome row")] = new Scoped(
            Verdict.Pass,
            "a trade with no forward return row is counted as listed and in no status, and the line states how many",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, below the minimum a dashed outline stating the trades decided at the target or the stop and their listing nights against the numbers needed with no share")] = new Scoped(
            Verdict.Pass,
            "below either minimum the dashed outline states the decided trades and their nights against the constants the run page's records read and no share is drawn, at one decided trade short of the count and at one night short of the nights",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, at or above it a bar of the finished trades in three steps of one neutral hue with a line at the decided trades' average break-even")] = new Scoped(
            Verdict.Pass,
            "at both minimums the bar draws the finished trades in three steps sized by their counts and the line at the decided trades' mean break-even across the decided part, worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, the share that reached the target first beside the share needed to break even and the average result in multiples of the risk taken, the three always together")] = new Scoped(
            Verdict.Pass,
            "at both minimums the three figures are drawn together and each is the test's own arithmetic over the constructed trades, a trade that ran out of time in no share",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, a key saying how to read it")] = new Scoped(
            Verdict.Pass,
            "the region's key says how to read it and closes on what to take from it",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, a line saying how many were listed again while an earlier trade was open, drawn and not counted")] = new Scoped(
            Verdict.Pass,
            "over a constructed store with three listings made while an earlier trade was open, the counts read the trades alone and the line states the three, drawn and not counted",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Filters, a chip for all and one for each status a trade can stand in with its count")] = new Scoped(
            Verdict.Pass,
            "each chip carries its status's count, the hash's status lights its chip and draws only its trades, and all draws every one",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Filters, none by setup")] = new Scoped(
            Verdict.Pass,
            "the chips are all and the four statuses and no other",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, newest first")] = new Scoped(
            Verdict.Pass,
            "the rows are drawn by the night listed, newest first, and in the list's own order within a night",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, a line above the rows stating how many are shown of how many were listed")] = new Scoped(
            Verdict.Pass,
            "the line above the rows states the rows drawn and the trades listed, filtered and not",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the night listed")] = new Scoped(
            Verdict.Pass,
            "each row carries the night the store listed it on",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the stock with a link to its page for that night")] = new Scoped(
            Verdict.Pass,
            "each row's stock links to its own page for the night it was listed",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the buy and the stop and the target")] = new Scoped(
            Verdict.Pass,
            "each row's buy, stop and target are the plan the night's trade gate read, section 10's on a version 3 night and the nearest bands' on a version 2 night, whole on their cells",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the trade line")] = new Scoped(
            Verdict.Pass,
            "each row draws the trade line for its own plan and status",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the status in words")] = new Scoped(
            Verdict.Pass,
            "each row's status is the words for its stored outcome as of the newest night",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, a repeat listing made while the trade from an earlier night was open marked as listed again and counted once")] = new Scoped(
            Verdict.Pass,
            "a listing made while the stock's kept trade was open carries the night of that trade on its row and its mark in its status cell, the kept trade's row carries none, and the name page's line counts the repeat apart",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the sessions held to its resolution or to the newest night")] = new Scoped(
            Verdict.Pass,
            "each row's sessions are counted on the exchange calendar from the night listed to the session it finished or to the newest night, worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the result as a signed multiple of the risk or open")] = new Scoped(
            Verdict.Pass,
            "a finished row's result is the stored return over the risk as a share of the buy, worked by hand, and an open row says open",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, a key saying how to read the trade line and that only the live list's trades appear with the alternatives hidden until one is promoted")] = new Scoped(
            Verdict.Pass,
            "the table's key says how to read the trade line and closes on only the live list's trades appearing",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Research, per row")] = new Scoped(
            Verdict.Pass,
            "each drawn row says whether the name holds a researched section besides the key under each figure, read back off the row against the store in both directions, and the link is drawn with the words of whichever it opens",
            ByReadSurface),
                                        [CheckReach.Key("15.9 Name", "Why it is here")] = new Scoped(
            Verdict.Pass,
            "each reason that fired is a full sentence with the values beside it, a reason the mapping has no sentence for fails rather than rendering a default, and a name that fired nothing says it is not on tonight's list, and breakout on volume's sentence states section 11's condition as that row's cell states it; on an evening the swing filter listed, the region names the rule and each gate with why it passed and the reasons as context, a name one gate short names the gate it missed with its words, how far short and its trade beside the line that it is not a pick, and a name the filter did not pass otherwise has no region whatever it fired",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Walk")] = new Scoped(
            Verdict.Pass,
            "previous and next on tonight's list are links, and either end says so rather than wrapping, asserted at both ends",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "Bulk price feed unavailable, banner")] = new Scoped(
            Verdict.Pass,
            "a night with no list shows the data date the store does have and draws no list at all, which is the half a banner above a stale list would not satisfy",
            ByReadSurface),
        // The phase 5 report, 5.7. The row states a figure and the deadline
        // follows it by three, which is assertable between two stated numbers
        // and in the policy that derives one from the other. What the figure
        // should be is a property of the running system, and it is carried as
        // an operating obligation read on the operational header rather than
        // waited for by a checkpoint.
        [CheckReach.Key(LimitsTable, "Nightly wall clock, at index size")] = new Scoped(
            Verdict.Pass,
            "the deadline is derived from this row's own figure at three times it, asserted in the document and in the retry policy together, with the figure stated as proposed and naming the surface that settles it",
            ByNight),

        // The run page, 5.6. Every one of these is a claim about a surface, so
        // each is reached by the check that draws the surface and reads it back.
        [CheckReach.Key(LimitsTable, "Base rate")] = new Scoped(
            Verdict.Pass,
            "the base rate is pinned per window above every record, read off the column the filler wrote beside each return, and the region refuses to draw at all with no pinned line rather than showing a figure a reader cannot judge",
            ByReadSurface),
                [CheckReach.Key("15.7 Tonight", "Reasons, per row")] = new Scoped(
            Verdict.Pass,
            "each reason on a drawn row is named with the values the store holds for it and the reason's own record beside it, in the dashed not-yet-measured state carrying its count against the minimum over the fixture, and over constructed records as the share, the count and the bar together where the verdict was not withheld and as the count against the named floor where it was",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Reason totals")] = new Scoped(
            Verdict.Pass,
            "each reason's count of tonight's fired names as a bar out of the fired count with the count written on it, counted per reason off the stored listings, and no won or lost segment because nothing has scored tonight",
            ByReadSurface),
                        [CheckReach.Key("15.10 Run", "Stale and failed, names carrying yesterday's bars")] = new Scoped(
            Verdict.Pass,
            "the stale names are listed rather than counted, over the index rather than the names with bars",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Stale and failed, the stage a night stopped on")] = new Scoped(
            Verdict.Pass,
            "a night whose feed does not answer writes a failed row naming the step, the reason and the requests it made, read back through the read API and drawn in the region; a night that stopped before its list is the night the run page opens on when no date is asked for, where the newest listing is the night before; a deadline or an allowance stop writes a stopped row against the step it stopped on, the actions step included; and a night refused before its first step writes a failed row under it",
            ByNight),
        
        [CheckReach.Key(FailureTable, "Earnings date missing, the earnings reason")] = new Scoped(
            Verdict.Pass,
            "the reason does not fire with no date on file and says not on file rather than a guessed date, with the horizon asserted either side of its boundary",
            ByExpectations),

        // The shortlist builder and tonight's list, 5.4.
        [CheckReach.Key(LimitsTable, "Nightly row coverage")] = new Scoped(
            Verdict.Pass,
            "a listings row exists for every index member on every night the store holds, with the fired and quiet rows partitioning the whole, and a member the night computed nothing for still getting one",
            ByListings),

        // The facts assembler and the change detector, 5.3. Two components on one
        // table's disjoint columns, which is what permits an inserter and a
        // different updater under a rule that forbids two owners for one
        // operation.
        [CheckReach.Key(CatalogueTable, "Facts assembler")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and the facts row it inserts, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Change detector")] = new Scoped(
            Verdict.Pass,
            "the class declares the facts it reads and updates and nothing else, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Facts assembler")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Change detector")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Facts")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, and the two writers own disjoint columns of one row at one grain",
            ByMigration),
        [CheckReach.Key(FixtureTable, "facts")] = new Scoped(
            Verdict.Pass,
            "every declared fact and the source of each is asserted against a set written from the rules, and every value against the store it was read from rather than against a copy of the payload",
            ByExpectations),
        [CheckReach.Key(NightlyRunSteps.Heading, "Write the facts file for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs the stage in the order section 14 states, writing the file and then comparing it, and its run log row records both",
            ByNight),

        // The move annotator, 5.2. It is the last of the six computed tables to
        // gain a deleter, which is why the store row covering all six is owed
        // here rather than where the first of them arrived.
        [CheckReach.Key(CatalogueTable, "Move annotator")] = new Scoped(
            Verdict.Pass,
            "the class declares the bars it reads and the moves it inserts, updates and deletes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Move annotator")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Indicators, swings, volume profile, levels, ladders, moves")] = new Scoped(
            Verdict.Pass,
            "all six tables carry the columns and types SCHEMA declares, and each is dropped at the same one-year boundary by its own writer, which the last of them gained here",
            ByMigration),
        [CheckReach.Key(FixtureTable, "moves")] = new Scoped(
            Verdict.Pass,
            "the largest single-day and multi-day moves over the committed bars are diffed against an expectation computed outside this repository from the captured payloads rather than frozen from a run",
            ByExpectations),
        [CheckReach.Key(NightlyRunSteps.Heading, "Annotate the largest moves for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs the stage in the order section 14 states and its run log row records what it wrote and what it dropped",
            ByNight),
        
        // The universe screen, 5.1. Every one is read off the rendered markup
        // and matched against the store rather than by eye, which is what a
        // claim about a surface requires.
                                [CheckReach.Key("15.8 Universe", "Filters")] = new Scoped(
            Verdict.Pass,
            "one chip per trend state and one per sector, each a hash route carrying its value, and a filter narrowing the table while the strip keeps stating the whole index",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "A name leaves the index")] = new Scoped(
            Verdict.Pass,
            "a departed name is absent from the universe and keeps its membership row with its leave date, asserted over the two the fixture holds",
            ByReadSurface),

        // Figure 9.1's five steps and the store they write, added at 5.0 when
        // the figures were first read at all. Each is asserted over the
        // committed fixture by the level expectations, which is the same
        // instrument that reaches the levels row of section 19.1.
        [CheckReach.Key("Figure 9.1", "Collect candidates")] = new Scoped(
            Verdict.Pass,
            "the four sources are collected over the fixture and the members of every band name their kind and date, swings, averages, retracements and shelves alike",
            ByExpectations),
        [CheckReach.Key("Figure 9.1", "Merge into bands")] = new Scoped(
            Verdict.Pass,
            "candidates closer than half a typical day's move join one band whose edges are its lowest and highest member, asserted against the expectation and at the merge distance either side",
            ByExpectations),
        [CheckReach.Key("Figure 9.1", "Add touches")] = new Scoped(
            Verdict.Pass,
            "a session reaching a band joins it as evidence and no band exists that only touches created, asserted over the fixture's own touch counts",
            ByExpectations),
        [CheckReach.Key("Figure 9.1", "Assign roles")] = new Scoped(
            Verdict.Pass,
            "bands above the close are resistance and below are support, with the nearest on each side marked immediate, asserted band by band against the expectation",
            ByExpectations),
        [CheckReach.Key("Figure 9.1", "Score strength")] = new Scoped(
            Verdict.Pass,
            "the strength of every band over the fixture matches the score the rule produces, member by member",
            ByExpectations),
        [CheckReach.Key("Figure 9.1", "Levels")] = new Scoped(
            Verdict.Pass,
            "the stored row carries the edges, the role, the strength and every member with its date and kind, diffed against the levels expectation",
            ByExpectations),

        // Figure 10.1's eight steps and the store they write. The figure the
        // phase 4 sign-off found unreachable, which is how the trailing stop
        // rule ran for a phase against a corpus that said something else.
        [CheckReach.Key("Figure 10.1", "Read the trend state")] = new Scoped(
            Verdict.Pass,
            "each of the four labels produces the shape this box states, the range and uptrend placing tranches and the downtrend and unclassified placing none with the reason named",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Place tranches")] = new Scoped(
            Verdict.Pass,
            "at most three, one per support band whose low edge is below the close, nearest first, average-only bands skipped and a straddling band keeping its full width, with the eligibility boundary asserted at the close itself",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Place stops")] = new Scoped(
            Verdict.Pass,
            "each stop is the low edge of the next band beneath in a range and the higher of that and the last swing low in an uptrend, the stops are strictly decreasing, and the invalidation is the lowest of them",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Attach conditions")] = new Scoped(
            Verdict.Pass,
            "exactly one of the four patterns matches each tranche, the order asserted in both directions over the three pairs that can both hold, with the fifth answer being the absence of the four",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Place exits")] = new Scoped(
            Verdict.Pass,
            "one per resistance band above the price, at most five, a band closer than two typical days' moves from the blended entry listed and not traded, and the top of the ladder a trailing rule",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Arithmetic")] = new Scoped(
            Verdict.Pass,
            "risk per tranche at the zone midpoint and reward to risk from the first tranche and the blended first two, asserted against cases computed by hand rather than against the code's own output",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Build the earnings trade")] = new Scoped(
            Verdict.Pass,
            "the three setups keyed to the print, each with its trigger, entry, stop and target, and none produced for a name with no date on file",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Apply the earnings rule")] = new Scoped(
            Verdict.Pass,
            "the last two prints' one-day moves stated against the stop distance, with a print outside the stored bars producing no figure rather than a wrong one",
            ByExpectations),
        [CheckReach.Key("Figure 10.1", "Ladders")] = new Scoped(
            Verdict.Pass,
            "the stored plan carries both books with every number and the band each came from, diffed against the ladder expectation",
            ByExpectations),

        [CheckReach.Key(FixtureTable, "bars")] = new Scoped(
            Verdict.Pass,
            "the fixture holds a year of daily bars per name, and the pipeline over them is asserted against a session count derived from the trading calendar rather than frozen from a run",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "news")] = new Scoped(
            Verdict.Pass,
            "the fixture holds captured articles with their publish dates and their text, readable without a network",
            ByExpectations),
        [CheckReach.Key(CatalogueTable, "Corporate action checker")] = new Scoped(
            Verdict.Pass,
            "the class declares the feeds it reads and the stores it touches, including the refetch delete and the series state SCHEMA now declares, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Corporate action checker")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Series state")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md. The marking itself is the corporate action checker's and is claimed by its own failure row, not here",
            ByMigration),
        [CheckReach.Key(FailureTable, "A split or dividend not caught")] = new Scoped(
            Verdict.Pass,
            "a real captured action on a current member triggers a full-year refetch, the replacement is atomic, a failure of the check itself marks the name suspect with its reason rather than passing, a suspect name is asked for again on the nights its retries allow and not after, a new action starts its count again, and the run page's stale and failed region names a suspect name on every night it stays suspect, with when it was last asked for and why from the night its retries are spent",
            ByActions),
        [CheckReach.Key(NightlyRunSteps.Heading, "Check splits and dividends, and refetch the full year for any name affected.")] = new Scoped(
            Verdict.Pass,
            "the action feed is read once per kind and only current members with an action are refetched",
            ByActions),
        [CheckReach.Key(FailureTable, "A gap in one name's series, chart")] = new Scoped(
            Verdict.Pass,
            "the holed series is refused with its date named, the clean one is unaffected, and the chart draws one candle per stored session with none for the missing one, so the gap is visible as an absence rather than closed over",
            ByGap),
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, a volume pane")] = new Scoped(
            Verdict.Pass,
            "one volume bar is drawn per candle on the same time axis, counted off the rendered markup",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, the moving averages")] = new Scoped(
            Verdict.Pass,
            "the three averages are drawn as paths on the candles' own price scale, counted off the rendered markup, broken where the average has no value rather than joined across it, and a line whose length does not match the sessions is refused",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "Fewer than 200 bars for a new index member, 200-day average")] = new Scoped(
            Verdict.Pass,
            "every session with fewer than two hundred bars behind it records its long average as absent with the bar count that explains it, and no session with two hundred records it absent, asserted over the committed fixture",
            ByExpectations),
        // The three rows section 19.1 never named. The fixture has held a
        // membership expectation since 1.1, a fetch expectation since 2.1 and a
        // series state expectation since 1.6, and the table listed none of
        // them: it listed ten files and the fixture holds three it does not
        // name. `fixture-replay` found the third by reporting a populated table
        // nothing expected, and the first two had been unlisted for two phases.
        // The rows are written at 4.0 and they pass on the day they are
        // written, because what they describe has existed all along.
        [CheckReach.Key(FixtureTable, "membership")] = new Scoped(
            Verdict.Pass,
            "the constituents the fixture's captured payload carries are diffed against the stored spans, with the unknown join date kept as unknown rather than folded to a value outside the index that enforces uniqueness",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "fetch")] = new Scoped(
            Verdict.Pass,
            "the bars a night stored from the bulk file are diffed against the file's own rows, and the names it carried nothing for are counted rather than inferred from an absence",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "series state")] = new Scoped(
            Verdict.Pass,
            "every name the corporate action check reached carries a state, and a name whose own check failed is suspect with its reason rather than absent",
            ByExpectations),
        // ---- 4.1, the nightly chain, the trend state and the ladder row ----

        // The five stages the night now runs in section 14's own order. Four of
        // them existed and no night ran one: the swing finder, the volume
        // profile builder and the level builder were called only from the
        // suite, which is what one step over nine computations hid.
        [CheckReach.Key(NightlyRunSteps.Heading, "Compute the indicators for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs it in the order section 14 states and the store holds the rows afterwards",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Mark the swings for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs it after the indicators and the store holds the rows afterwards, where before 4.1 no night ran it at all",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Build the volume profile for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs it after the swings and the store holds the rows afterwards, where before 4.1 no night ran it at all",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Build the levels for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs it after the profile, which is the order the levels depend on, and the store holds the bands afterwards",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Classify the trend state and build the ladder for every name, writing a row whether or not it carries a tranche (see: A ladder row is written for every index member every night) (see: The trend classifier returns its label to the ladder builder).")] = new Scoped(
            Verdict.Pass,
            "the night runs it last of the five and writes one row per index member, counted against the membership expectation rather than against the names carrying a plan",
            ByNight),

        [CheckReach.Key(CatalogueTable, "Trend classifier")] = new Scoped(
            Verdict.Pass,
            "the class declares the indicators and swings it reads and declares no write, which is what its row says in words, and the declaration matches its matrix row cell by cell",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Trend classifier")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, including the run log cell this checkpoint blanked: the row gave it a write and its catalogue row says it writes nothing",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Ladder builder")] = new Scoped(
            Verdict.Pass,
            "the class declares the levels, indicators and calendar it reads and the ladder it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Ladder builder")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, against the row contradiction E repaired at 4.0",
            ByAccess),
        // 6.1, the fundamentals. The store's own row, the fetcher's catalogue row
        // and its matrix row, which are the three places one component is declared
        // and which the reconciliation reads against each other.
        [CheckReach.Key(StoresTable, "Fundamentals")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, and the store is asserted to be one the file describes",
            ByMigration),
        [CheckReach.Key("15.10 Run", "Stale and failed, documents refused by admissibility")] = new Scoped(
            Verdict.Pass,
            "the refusals are read back off the region's own markup against the rows the store holds, with the category beside each document and its address drawn rather than counted, the count per category above them, the admitted documents in the same table on the same night not drawn, and no body drawn for any of them",
            ByReadSurface),
        // 6.3, the admissibility test. The limits row and the two failure rows
        // are the behavioural half and the store row below is the shape half,
        // which fail apart: the store could hold the right columns while nothing
        // refused anything, and every category could be refused into a table with
        // no room for the reason.
        [CheckReach.Key(LimitsTable, "Source admissibility")] = new Scoped(
            Verdict.Pass,
            "each of the four denied categories, the missing publish date and the unretrievable text is reached by a document the fixture holds, the six real documents it holds are admitted, a document failing two gates is refused by the kind rather than by the date, the row's categories and the thresholds it states are read off it against the constants the rule reads, a page at each count the row states is refused and one below it admitted, and each marketing page the fixture holds carries the markers its expectation counts",
            ByAdmissibility),
        [CheckReach.Key(FailureTable, "A source is returned but its text cannot be retrieved")] = new Scoped(
            Verdict.Pass,
            "the document with no text is refused for that and nothing else, the row it leaves carries no body, and the run log line names its address and the reason the fetch gave, which is the case the measurement produced rather than constructed",
            ByAdmissibility),
        [CheckReach.Key(FailureTable, "A document's publish date falls outside the window the pass asked for")] = new Scoped(
            Verdict.Pass,
            "one document is admitted for a window that holds its date and refused for one that does not, with both edges of the window asserted to be inside it, so the rule is about the window the pass asked for rather than about the document's own age",
            ByAdmissibility),
        // 6.3's three fixture rows. The inadmissible document row is six claims
        // rather than one, decomposed here at the checkpoint the six documents
        // arrive at, and each names the rule that refuses it rather than the
        // document that trips it: a document is what the fixture holds and the
        // rule is what the row claims.
        [CheckReach.Key(FixtureTable, "an inadmissible document, a machine-generated price forecast")] = new Scoped(
            Verdict.Pass,
            "refused on a price word paired with a prediction word in the title or the address, on an address whose last segment is the forecast, or on a stated algorithmic projection in the text, with four real reporting headlines pairing forecast with revenue asserted to be admitted",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "an inadmissible document, a broker marketing page")] = new Scoped(
            Verdict.Pass,
            "refused on a regulatory risk warning, or on a leveraged-product term beside an invitation to open an account, with a page the fixture holds refused by each half, invitation language alone asserted to refuse nothing, and the cost of the rule stated rather than left to be found",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "an inadmissible document, a summary written by another AI system")] = new Scoped(
            Verdict.Pass,
            "refused where the page declares that a system wrote it, and only then, with a captured article titled for the subject asserted to be admitted so the marker is a phrase rather than the two letters",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "an inadmissible document, a quote page carrying no article")] = new Scoped(
            Verdict.Pass,
            "refused where the address or the title says it is a symbol page and no paragraph of it runs to the measured length, both halves required, with the same address carrying an article asserted to be admitted",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "an inadmissible document, a page with no publish date")] = new Scoped(
            Verdict.Pass,
            "refused for the absence alone, over a document written to be admissible in every other respect, so the date rule is what refuses it rather than something else firing first",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "an inadmissible document, a page whose text cannot be retrieved")] = new Scoped(
            Verdict.Pass,
            "refused before anything else is judged, because a document with no text is nothing to judge, and the run log names its address and the reason the fetch gave",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "source documents")] = new Scoped(
            Verdict.Pass,
            "every document the intake stored, with the verdict the rules reach on it and the body kept on an admission and dropped on a refusal, diffed against what those rules produce over the documents the fixture holds",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "refused documents")] = new Scoped(
            Verdict.Pass,
            "every document refused, with the category that refused each, the count per category the run log line states, and the order that decides which of two failed gates the row records",
            ByAdmissibility),
        [CheckReach.Key("15.9 Name", "Research not yet written, the researched sections absent with one line saying they have not been written")] = new Scoped(
            Verdict.Pass,
            "a name with no accepted section is drawn with the line saying its researched sections have not been written, read back off the markup, over a store whose other names carry research",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Research not yet written, beside the computed sections rendered whole")] = new Scoped(
            Verdict.Pass,
            "the same page carries the chart, the level summary, the plan tables and the numbers section in full beside that line, asserted on the markup that carries the line",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Research stale, one line naming which of the four triggers fired")] = new Scoped(
            Verdict.Pass,
            "a name whose section predates its stored filing and its passed earnings date is drawn with one line naming both, which is the line the shipped judge writes to the run log over the same store, and a name whose research stands is drawn as standing",
            ByReadSurface),
        // 6.11, the phase report. The report exporter's two rows and the surface it writes, and
        // the harness's own matrix row, which had waited here since 0.5.
        [CheckReach.Key(CatalogueTable, "Report exporter")] = new Scoped(
            Verdict.Pass,
            "the class declares an empty access, which is a claim rather than an omission, and it matches a row reading the API and writing a file the person exporting chooses where to keep, which is no store",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Report exporter")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is blank and the declaration is empty, asserted cell by cell",
            ByAccess),
        // 12.6, the comparison files, which the read surface writes when started with its argument.
        [CheckReach.Key(CatalogueTable, "Comparison files")] = new Scoped(
            Verdict.Pass,
            "the class declares an empty access, and it matches a row reading the API and writing files in a folder the repository ignores, which is no store",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Comparison files")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is blank and the declaration is empty, asserted cell by cell",
            ByAccess),
        [CheckReach.Key("15.4 The two surfaces", "The exported report")] = new Scoped(
            Verdict.Pass,
            "the file the export route offers is a document of its own carrying no script, no router link, no form and nothing fetched, every disclosure open, the name page's own marks and written sections byte for byte with no value the page does not state, and one candle for each session the store holds for the name",
            ByReadSurface),
        [CheckReach.Key(MatrixTable, "Verification harness")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is blank, read against the catalogue row's words for what the harness reads and writes, which name no store the matrix carries, and every store the suite opens is a temporary one outside the data root",
            ByAccess),
        // 6.10, the overnight queue. The component, section 14's last step, section 17's row and the
        // model calls row the carve changes, section 18's row for a night the machine slept and the
        // half of its local model row the queue records, and the run page's region.
        [CheckReach.Key(CatalogueTable, "Overnight queue")] = new Scoped(
            Verdict.Pass,
            "the class declares the listings it reads and the run log it appends to and nothing else, holding no feed, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source, with the judge, the writer and the checker it has do the deciding and the writing under declarations of their own",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Overnight queue")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, which is where the queue writing no research is a claim: the sections a pass writes are inserted by the prose writer and moved by the checker, each under its own row",
            ByAccess),
        [CheckReach.Key(NightlyRunSteps.Heading, "Run the overnight queue on the local model, writing the sections in the local lane that rest on no document for every name in the index whose research is missing or stale, the names on tonight's list first in the order it is drawn in, then the names close to a buy point in the order that list is drawn in, then every other name (see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing) (see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures), until the configured time limit rather than until a count of names is reached (see: The overnight queue is bounded by time, not by a count of names), a limit of its own rather than the night's deadline (see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed). It holds the machine awake while it works and reports whether it ran (see: The overnight run holds the machine awake and reports whether it ran). This makes no paid call and no request, and no part of the arithmetic above depends on it (see: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about).")] = new Scoped(
            Verdict.Pass,
            "the night runs the queue after the close has recorded the arithmetic's counts and before its own request, on its own output and on the run log's own order, and states the queue's local model calls apart from the arithmetic's none; over a copy of the fixture's night whose readings are stored the queue takes the names the filter passed improving first, the ones reading no state next in the filter's order and deteriorating last, whatever their ranks; and over another copy it takes the list's name first, then the two one gate short nearer first against their tickers' order, then the member missing two",
            ByNight),
        // 11.4, the night's own request, after the queue.
        [CheckReach.Key(NightlyRunSteps.Heading, "Ask for a report on the first six names the page draws, in the page's order across every family, one request a name marked as asked by the night unless that name has one outstanding or being written and none on a night the page lists no stock, and start the drain as a press does, whose passes are its own runs at the off-peak rate with their calls and their requests on their own rows (see: The night asks for a report on the first six names its page draws).")] = new Scoped(
            Verdict.Pass,
            "over the fixture's night, on which no family passes a stock, the night asks for no report, starts no drain, runs after the queue with no model call and no request, and its row says why; over a constructed night the families drew, seven listed across two cards with one held back and one past five, it asks for the first six places in the page's order and no other, each marked as asked by the night; over a night before the families it asks down the swing filter's order, worked out by the test's own arithmetic off the gate rows rather than the stored rank, the improving business first where that night stored its readings; a name with a request waiting gets none, the next is asked for and the row says so, and a night run again for an earlier session asks for none",
            ByNight),
        [CheckReach.Key(LimitsTable, "Reports the night asks for")] = new Scoped(
            Verdict.Pass,
            "the night asks for one name, the constant the row states, over the fixture's night, and the launcher it was handed is asked to start one drain",
            ByNight),
        [CheckReach.Key(LimitsTable, "Overnight queue")] = new Scoped(
            Verdict.Pass,
            "over a whole fixture night the queue writes every name's key under each figure, the listed names first in order of reasons fired, handed no document, with nothing spent and no request on its row or any other, and at a limit set on a clock only a model call moves, a name whose turn comes at the limit is left while a pass started a tick inside it runs to its end",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The machine slept and the overnight queue did not run")] = new Scoped(
            Verdict.Pass,
            "the run page for a night two traded sessions after the queue last ran names both nights it did not run, each on a line of its own, read through the page's route, and the calendar's closed days and the nights before the queue first ran are named as nothing; and a name page whose key under each figure was written for another night names that night in the key's card and draws no key, read through the name page's route",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "The local model is unavailable, the overnight queue records that it could not run")] = new Scoped(
            Verdict.Pass,
            "with the local model not answering, the queue's row says it could not run and why, names the pass that found out with its one call, leaves every name, writes no section, and the night it ran at the end of still exits clean",
            ByExpectations),
        [CheckReach.Key("15.10 Run", "Tonight's order, the three orders of tonight's list over the twenty each would draw")] = new Scoped(
            Verdict.Pass,
            "the region is drawn by the run route, and each order's twenty are the first rows the projection's own ordering gives on each recorded night, read off the region against constructed nights of more than twenty fired rows where the three orders draw different rows",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Tonight's order, the old order named as the benchmark")] = new Scoped(
            Verdict.Pass,
            "the order the list had before phase 10 is the row marked the benchmark, and it is drawn first, read off the region's markup",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Tonight's order, the setups each order drew")] = new Scoped(
            Verdict.Pass,
            "each order's count of drawn rows whose plan computes a reward to risk is read off the region against a count worked by hand over constructed nights, a drawn row with none counting for nothing",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Tonight's order, the setups whose whole window has closed")] = new Scoped(
            Verdict.Pass,
            "each order's closed-window setups are read off the region against counts worked by hand over constructed nights either side of the sixty-third session after a listing",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Tonight's order, the blocks holding one against the floor")] = new Scoped(
            Verdict.Pass,
            "each order's blocks are read off the region against counts worked by hand over constructed nights either side of a block edge, with the floor drawn beside them, and a night that recorded no band strength is left out",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Tonight's order, no comparison drawn before every order reaches it")] = new Scoped(
            Verdict.Pass,
            "the region states that no comparison is drawn while any order holds fewer blocks than the floor and says the comparison is due once every order reaches it, over constructed nights either side of the floor",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the verdict field stating what the last look read and what triggers the next")] = new Scoped(
            Verdict.Pass,
            "the field states what the last look came to, how many looks remain and the block count the next waits for, read off the region over a record at its first look and over one below the floor of blocks, which states no verdict at all",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the nightly figure beside it labelled as monitoring")] = new Scoped(
            Verdict.Pass,
            "the running figure is drawn as monitoring rather than as a verdict, with the setups and the share they won against the calibrated bar, read off the region",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the Poisson-binomial tail labelled and deciding nothing")] = new Scoped(
            Verdict.Pass,
            "the tail that assumes the setups independent is drawn beside the verdict saying so, read off the region, and the arithmetic behind it is the live family's own",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, each look's setups and the share they won against the bar the calibration set")] = new Scoped(
            Verdict.Pass,
            "the look's row carries the setups its blocks hold and the share of them that won against the mean calibrated bar, read off the region against a record worked by hand",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the level that look spent and whether it crossed its boundary")] = new Scoped(
            Verdict.Pass,
            "the look's row carries the level the spending function released at that look and whether the record crossed the boundary found over its own arrangements, read off the region against the level computed from the candidate's own",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the smallest excess that look could have detected")] = new Scoped(
            Verdict.Pass,
            "each look states the smallest excess it could have detected beside what it read, named on the region as the approximation it is",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the design effect and what a loss cost in multiples of the planned risk")] = new Scoped(
            Verdict.Pass,
            "both are drawn in the reported line the region ends with, from the blocks' own scatter and the losses' own returns over the risk their plans stated, and the line says they are tested nowhere",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the setups entered and stopped on one session and those stopped out on a session the name reported on")] = new Scoped(
            Verdict.Pass,
            "both counts are drawn in the reported line, the first over the losses whose fill the store does not place and the second over the losses whose resolving session the calendar dates as a print",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, the step the graph stands at with its level and the count of distinct trials")] = new Scoped(
            Verdict.Pass,
            "the step and the level the graph gives are drawn on the candidate's own entry and the count of distinct trials on the region, read off the markup over a family of one and over the region handed counts of names and trials apart, each drawn from its own field; the level is 0.05 over the trials over constructed registers worked by hand, a candidate retired after its first look was read staying counted and one retired unread counted in none, three candidates whose first looks were read before six more registered sharing 0.05 over 9 with them, and a look keeping the bar of the count as of its night, crossing at 0.05 alone and not at 0.025 beside a rule evaluated with it",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Candidates' records, no name anywhere in it")] = new Scoped(
            Verdict.Pass,
            "every ticker the store holds is looked for inside the region as the run route draws it, and the shadow column the night writes for every member reaches no screen",
            ByReadSurface),
        [CheckReach.Key("15.10 Run", "Overnight queue")] = new Scoped(
            Verdict.Pass,
            "the run page's route draws whether the queue ran on the night, with the queued passes completed and left read off the queue's own row, where section 15.10 puts the region, and names every traded session since it last ran on which it did not",
            ByReadSurface),
        // 6.9, the theme research runner and the search tool. The component, the record one
        // theme pass writes and every member of its industry reads, section 17's three rows
        // about a theme search, and section 18's four rows about what a search returns and a
        // theme that could not be refreshed.
        [CheckReach.Key(CatalogueTable, "Theme research runner")] = new Scoped(
            Verdict.Pass,
            "the class declares the theme store and source documents it reads and inserts into, the run log it reads and appends to and the search tool it reads, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Theme research runner")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, which is where the runner reading no membership is a claim: the industry it researches is handed to it by the name's pass that read it",
            ByAccess),
        [CheckReach.Key(FixtureTable, "theme record")] = new Scoped(
            Verdict.Pass,
            "one theme pass over the fixture's recordings writes one record for the theme and the industry that maps to it, handed every page its search kept in the order the tool ranked them, its prose the recording its request is keyed on, and a second member of the industry opened the same evening searches and calls for nothing and draws the same record, against the expectation derived from section 12's rules and the capture's own bytes",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Theme search parameters")] = new Scoped(
            Verdict.Pass,
            "the request a theme pass made names the industry and no ticker, carries the quarter's two dates, names the industry list and no other site, and asks for the page's text and its publish date, read off the body the feed sends for that request and off the bytes a live feed sent over a handler",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Source lists")] = new Scoped(
            Verdict.Pass,
            "every page the whole replay's theme search stored is from a site the industry list carries, the result from a site it does not carry is nowhere in the store, and both lists carry a review date and sit under the tool's domain limit",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Scheduling of queued work")] = new Scoped(
            Verdict.Pass,
            "a theme refresh asked inside a peak window of the configured prices starts nothing, makes no request and names the UTC instant the window closes on its run log row, a name opened then is written without it, and the one paid call the whole replay's theme pass made started outside every configured window",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A search returns snippets rather than full page text")] = new Scoped(
            Verdict.Pass,
            "a search answering with a result that has no text and one whose text is shorter than its snippet stores no document and calls for nothing, the run log records each address as short of a document, and the cycle is absent with the reason every candidate failed that way",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A search returns a site the applicable list does not carry")] = new Scoped(
            Verdict.Pass,
            "the result from a site the industry list does not carry is dropped and its site named on the run log, and over the captured company search the seven results from sites the list does not carry are named for their site before their text is read, two of them having none",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The search tool is unavailable")] = new Scoped(
            Verdict.Pass,
            "with the search tool not answering, the theme pass does not start and the stored theme record is as it was, and the name's pass writes every section of its own from its filings and news with the cycle absent and its reason",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A theme refresh fails while a name's pass depends on it")] = new Scoped(
            Verdict.Pass,
            "with the search refused, every section of the name's own is written and stored, the theme record is as it was, and the page draws every section but the cycle and one line saying the theme could not be refreshed, read back off the markup, and a cycle written before that refresh under its own date beside the line",
            ByReadSurface),
        // 6.8, the research runner. The component, the record one pass writes over the
        // fixture's recordings, one pass an open, figure 12.1's two boxes the runner is,
        // section 18's cloud model row and the halves of its two local lane rows the
        // paid path and the page's option make true, and the name page's regions a
        // written section is drawn in, with tonight's count of fresh prose against reused.
        [CheckReach.Key(CatalogueTable, "Research runner")] = new Scoped(
            Verdict.Pass,
            "the class declares the membership, facts, fundamentals, research store, theme store, source documents and run log it reads, the research store and source documents it inserts into and the filings archive and news feed it reads, and the declaration matches this row, repaired at 6.8 to name the research store and the run log and at 6.9 to name the membership its industry is read from, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Research runner")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, which is where the runner writing no fundamentals and no facts is a claim: it reads the quarter a fetch stored and never writes one",
            ByAccess),
        // 9.2, the request drain. The worker's half of the request store: what it
        // takes, and what it settles under.
        [CheckReach.Key(CatalogueTable, "Request drain")] = new Scoped(
            Verdict.Pass,
            "SCHEMA gives it the update on the request store and the insert of the night's own request, its own source carries the claim, the settle and that insert, and the verbs the row names are ones the worker dispatches, names in its help and the runbook shows",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Request drain")] = new Scoped(
            Verdict.Pass,
            "the row reads and writes the request store, reads the listings for the night's own request, and appends to the run log through the pass it runs, and every other cell is blank: the drain writes no research, which is the claim the blanks carry",
            ByAccess),
        [CheckReach.Key(FixtureTable, "research record")] = new Scoped(
            Verdict.Pass,
            "the pass over the fixture's recordings stores every version with the lane that wrote it and the checker's verdict, each draft byte for byte the recording its request is keyed on, the documents fetched, admitted and refused, what each section was handed in order, the stages in the order they landed and what the paid calls cost, against the expectation derived from section 12's rules and the lane configuration, with the lane comparison run both ways over one evidence set",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Research passes per name per open")] = new Scoped(
            Verdict.Pass,
            "a second open of a researched name writes, asks and fetches nothing and its row says so, read off the run log rather than a return value, and so does a second open of a name whose pass found nothing to write from, which the per-section rule alone let fetch again; a pass the page asks for explicitly still runs, and a later day's open still does",
            ByExpectations),
        [CheckReach.Key("Figure 12.1", "A pass is warranted")] = new Scoped(
            Verdict.Pass,
            "over constructed versions of every state the rule reads, a pass warrants the sections never written, left out on an earlier day, refused, and accepted and gone stale by the judge's own trigger, and not those accepted today, waiting on the checker or left out today, read off the pass's own row, with the industry cycle warranted for the theme the name's industry is where no theme pass has written it",
            ByExpectations),
        [CheckReach.Key("Figure 12.1", "Write the sections")] = new Scoped(
            Verdict.Pass,
            "each section is written by the lane the configuration assigns it, asserted from both ends over the requests each recorded model was asked, a section refused once is written again in the same pass and the short version last from the sections accepted, and every stored draft is the recording its request is keyed on",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A section is assigned to the local lane that the machine cannot hold, the section is left for the paid path")] = new Scoped(
            Verdict.Pass,
            "at a context too small for the two sections handed the release, neither reaches the local model and both are asked of the paid model in the same pass, while the writer's own row names each with the reason",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The local model is unavailable, a pass on demand writes the paid lane's sections and leaves the local lane's absent")] = new Scoped(
            Verdict.Pass,
            "with the local model not answering, a pass writes exactly the paid lane's sections through the paid model and stores no row by the local one, and names each local lane section with the reason the writer recorded",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A section is assigned to the local lane that the machine cannot hold, the option to have it written")] = new Scoped(
            Verdict.Pass,
            "a name whose section the machine could not hold is drawn with a control asking the paid model to write the local lane's sections, the option read off the form a press would send, with the cost stated before it",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "The local model is unavailable, the option to have the paid model write them")] = new Scoped(
            Verdict.Pass,
            "after a pass the local model did not answer, each local lane section is drawn absent with its reason and the page offers the control asking the paid model to write them, read off the form a press would send",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "Cloud model unavailable")] = new Scoped(
            Verdict.Pass,
            "a rewrite asked with the research model not answering stores no section and fetches no document, and the page still draws every stored section under the date the store holds with a line saying the pass did not start, and a name with nothing stored is drawn with the control that writes it later",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The short version")] = new Scoped(
            Verdict.Pass,
            "the accepted short version is drawn above the chart, the table of moves and every other written section, its paragraphs the stored prose drawn as text and its date beneath them with its model on the element, read back against the store, and a name with none draws a summary written by code under the heading its record names, refused, not written or no research, each part read off the page against the store and the plan the trade gate read",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "What it sells, the numbers, the cycle, the two cases, the risks")] = new Scoped(
            Verdict.Pass,
            "every accepted section is drawn once in section 4's order, the key beneath the chart's figures and before the plan, what the company sells and its segments after the plan and before the numbers, the two cases after them and the risks after those, each with the date the store holds and its model on the element, every section figure 12.2 names placed exactly once, and the cycle drawn from the theme record for the name's industry, or absent with the reason the pass stored where the theme's search found nothing to write it from",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Dates and sources")] = new Scoped(
            Verdict.Pass,
            "the region draws every calendar event from the newest session, the dated items as written, and every document a written section cites once each with its date and link, against queries of the test's own, and each section's markers resolve in its stored source order",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Research not yet written, a control that writes them with its cost stated before it is pressed")] = new Scoped(
            Verdict.Pass,
            "a name with no research draws a control asking a plain pass for it, preceded by the priced passes the run log holds, what they cost and the most one cost, read against sums of the store's own rows, and the route the control reaches starts the worker's verb with what the form asks, refuses a request without the page's header or for a name the index does not hold, and writes nothing",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Research stale, the stored sections rendered with their own dates")] = new Scoped(
            Verdict.Pass,
            "a name whose sections predate its newest filing draws every accepted section under the date the store holds beside the stale line",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Research stale, the option to have them rewritten")] = new Scoped(
            Verdict.Pass,
            "the same page offers a control that rewrites the stale sections, with the cost before it, and offers none on the day a pass ran, where a second plain pass writes nothing",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Research paused, with the stored sections still rendered under their own dates")] = new Scoped(
            Verdict.Pass,
            "at the day cap the page draws the pause line and every accepted section under the date the store holds, and no control, since a press would be refused",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Night header, reports carrying fresh prose against reused")] = new Scoped(
            Verdict.Pass,
            "the header counts the names with a section accepted on the night against those whose accepted sections all predate it, over the names with any accepted section as of the night, against counts of the test's own and figures stated in advance over rows either side of the night and in every status, and the tonight route draws it",
            ByReadSurface),
        // 6.7, the spend cap and the paid lane. The component, the run log's row it
        // makes true, section 17's cap, section 18's row about reaching it in its two
        // halves, and the two pages that state what research spent and that it paused.
        [CheckReach.Key(CatalogueTable, "Spend cap")] = new Scoped(
            Verdict.Pass,
            "the class declares the run log it reads and appends to and the research model it holds, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source, with the two runners' rows no longer reading the model",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Spend cap")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, which is where the cap reading and writing the run log and touching no other store is a claim",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Run log")] = new Scoped(
            Verdict.Pass,
            "every shipped component that writes a store declares a row of its own on the run log and the three that write nothing declare none, read in both directions over the declarations and against each row's Writes cell, with the row's own words read off the document; the spend the log carries is written by the spend cap for every paid call, asserted over the store",
            ByAccess),
        [CheckReach.Key(LimitsTable, "Spend cap")] = new Scoped(
            Verdict.Pass,
            "the day cap and the month cap each refuse on their own over constructed ledgers, at the UTC edges either side, a call that could take spend past either is refused before it is made, the ledger is read off the run log rather than kept, and the row's proposed figures are read off the document against the constants",
            ByExpectations),
        // 12.6's correction, the model profiles: section 17's three rows, and section 18's row about a key the
        // secrets file does not hold in its two halves, the job stopping and the line the run page draws.
        // 12.6's correction, the news labeller: its catalogue and matrix rows, its two stores, section 14's step,
        // section 17's five rows and section 18's eight.
        [CheckReach.Key(CatalogueTable, "News labeller")] = new Scoped(
            Verdict.Pass,
            "the class declares the membership, listings, gate results, fundamental readings, articles and run log it reads, the paid model it asks through the spend cap and the labels and run log it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "News labeller")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "News articles")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(StoresTable, "News labels")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(NightlyRunSteps.Heading, "Start the news labeller as the drain is started, a process of its own that labels the stored admitted articles of the names on tonight's list, newest first and at most twenty a name over the thirty days before the night, through the news job's paid model one article at a time, with every call and every dollar on the labeller's own run and never the night's, bounded by its own time and month limits, and starting none on a night run again for an earlier session (see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own).")] = new Scoped(
            Verdict.Pass,
            "over a constructed night the launcher the night was handed is asked to start one labeller, after the night's own request and as the last row the night writes, with no model call on the night's row; a night run again for an earlier session starts none and its row says so, and a night handed nothing to start one with says so",
            ByNight),
        [CheckReach.Key(LimitsTable, "News labelling window")] = new Scoped(
            Verdict.Pass,
            "over a constructed store the labeller sends each listed name's admitted articles of the window newest first and none outside it, the window, the count a name and the cut read off the document against the constants, and the counter stores each article cut at the instruction's length",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "News labeller time limit")] = new Scoped(
            Verdict.Pass,
            "a labeller whose limit has passed before its first article sends nothing and names the limit as its stop with the names it reached, the twenty minutes read off the document against the constant",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "News labeller month limit")] = new Scoped(
            Verdict.Pass,
            "a month already at the limit sends nothing, and a limit the first call's ceiling would pass sends nothing, each naming the limit as its stop, the five dollars read off the document against the constant",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Unreadable answers for a digit")] = new Scoped(
            Verdict.Pass,
            "the instruction refuses a reason holding a digit by that cause, and the labeller stores an answer unreadable twice with its cause and counts it by cause on its row",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "News article retention")] = new Scoped(
            Verdict.Pass,
            "the counter drops an article older than the retention and keeps the day's, the thirty-one read off the document against the constant",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The news job's model does not answer")] = new Scoped(
            Verdict.Pass,
            "a model that answers nothing stops the labeller where it is with the model named as its stop and the labels before it kept, and a run refused before it asked writes its own refused row with the line",
            ByExpectations),
        [CheckReach.Key(FailureTable, "An answer the labeller cannot read")] = new Scoped(
            Verdict.Pass,
            "an answer failing a check is asked for once more, and one failing twice is stored as unreadable with the second answer's cause, counted by cause on the run's row and not sent again under the profile",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The labeller's month limit reached")] = new Scoped(
            Verdict.Pass,
            "a month at the limit sends nothing and names the limit as its stop with the names reached",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The day or month cap pauses a label")] = new Scoped(
            Verdict.Pass,
            "a cap the first call would pass makes no call, and the labeller names the cap as its stop with no label written",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The labeller's time limit passes")] = new Scoped(
            Verdict.Pass,
            "a limit passed before the first article sends nothing and names the limit as its stop with the names reached",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A peak window of the news profile opens while the labeller runs")] = new Scoped(
            Verdict.Pass,
            "a clock inside the recorded profile's peak window sends nothing and names the window as its stop",
            ByExpectations),
        [CheckReach.Key(FailureTable, "An article refused by admissibility")] = new Scoped(
            Verdict.Pass,
            "an article the counter stored refused is never among the requests and is counted on the run's row as refused",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The labeller fails")] = new Scoped(
            Verdict.Pass,
            "a run refused before it asked anything writes its own row saying why and no row of the night's, and a labeller that stopped partway leaves the night's rows, listings and gate results as they were",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Paid model profiles")] = new Scoped(
            Verdict.Pass,
            "each shipped profile resolves from the shipped settings with its format, model, key and its provider's prices and dates, the one word a job names reaches the feed of that profile's format asking for that model, and the fixture's own models file holds its recordings to the profile they were made under whatever the shipped word says",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Paid model retirement warning")] = new Scoped(
            Verdict.Pass,
            "the checklist names a profile from thirty days before its date and not the day before, and never one whose provider publishes none, the thirty read off the document against the constant",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "A report named for its cost")] = new Scoped(
            Verdict.Pass,
            "the checklist names a pass whose calls sum past the amount with its stock and cost and not one summing to it exactly, the amount read off the document against the constant",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Risk kinds")] = new Scoped(
            Verdict.Pass,
            "every kind the row lists is accepted for an event risk, a kind outside them refused naming it and two event risks of one kind refused, over constructed answers, the kinds read off the document against the constant",
            ByAdmissibility),
        [CheckReach.Key(LimitsTable, "Sector sites")] = new Scoped(
            Verdict.Pass,
            "a site joins with an admitted page about a declined industry at the density the pass hands a page at, and not with a refused page, one just below the density, one about an industry the sector did not decline or another site's page, over constructed results, the page count read off the document against the constant",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Section review")] = new Scoped(
            Verdict.Pass,
            "a review off as shipped, and when named asked over the fixture's pass with the pass's own draft handed back, writing no section row, standing its calls under the review's round and drawn beside the pass's drafts, the count read off the document against the shipped setting",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Section trial")] = new Scoped(
            Verdict.Pass,
            "a trial over the fixture's pass writes no section row, records its own model and the pass's draft it was asked beside, stands its calls under the trial's round once each on the run, left out of the report's priced calls, and asks nothing on the report past the count it names, the count read off the document against the shipped setting",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A paid job's profile names a key the secrets file does not hold, the job stops before it fetches or asks anything and no other profile answers for it; a research pass writes the plain line saying which profile and which key as its own run log row, which the drain settles the request under as refused")] = new Scoped(
            Verdict.Pass,
            "with DeepSeek's key held and Claude's not, the research job naming Claude is refused in the line naming the profile and the key's path, no settings are resolved for another profile, and the pass's own row carries the line with no call and no spend, under which the drain settles the request refused",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A paid job's profile names a key the secrets file does not hold, the run page's stages that failed name the pass in the line's own words")] = new Scoped(
            Verdict.Pass,
            "a refused pass's row, a record carrying its reason, is drawn among the stages that failed in that reason's words, read off the rendered region, and a stage whose record carries no reason keeps its detail",
            ByReadSurface),

        // 12.5's correction on the operator's rulings of 2026-09-29: the sweep history, the sweep's figures and
        // the chunk that fails twice.
        [CheckReach.Key(CatalogueTable, "Sweep history")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and that it writes none, and the declaration matches this row, its matrix row and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Sweep history")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(LimitsTable, "Sweep grid")] = new Scoped(
            Verdict.Pass,
            "the grid's counts are the ones its axes make, the walk keeps the picks the pages' open trade rule keeps over the same listings, every setting's summary is the record read in full with the rule inside over constructed candidates, the first stage's viable count and median edge are the ones every coarse setting read directly gives, and the strongest distinct rows skip a tie and rank a design with too few viable settings below every other",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Sweep starting point")] = new Scoped(
            Verdict.Pass,
            "over landscapes worked by hand depth is the fewest single steps on any dial with the ends the grid does not limit, the leaders are in edge order and the deepest is proposed with ties to the edge and to the live rule, a leader trailing the live rule in a recent year is passed over, a record one year carries is refused, across the designs the deeper within the margin is chosen and a deeper one outside it is not, and the report says the results read better than the market was a tenth over the threshold and not at it",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Sweep variants")] = new Scoped(
            Verdict.Pass,
            "each of the four tests refuses a variant on its own on the edge and passes it at its edge, and the live design's structural neighbours are twenty-three",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Sweep run")] = new Scoped(
            Verdict.Pass,
            "over a clock the run's own wait moves the run pauses at the window's edges and not a minute before, goes on two polls after a night lets its lock go and after a drain's, gives up a night that never came, a run stopped after a chunk goes on from the next and holds what an unstopped run holds, a run's folder is named by its instant with the newest report found, and a finished run or another build's is refused",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Sweep edge")] = new Scoped(
            Verdict.Pass,
            "the benchmark is worked by hand over three members under both exits with the edge read from it and none where a series runs out, the walk keeps one open trade a stock as the pages' rule does with the boundary by hand, and over constructed closes the stepped plan's result is its return over the risk its plan stated, a trade run out of sessions is counted at its last close as the benchmark's walk counts it, a plan bought at the close is counted on its own fill, and a record is stated again without its five largest results by size with each kind of plan's results counted beyond the bound",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Sweep conditions")] = new Scoped(
            Verdict.Pass,
            "each condition's reading is worked by hand at its edge over constructed bars, a setting is kept at six years and two recent and not at five or one, a condition survives on six designs and not five with both its settings carried as a dial, and every survivor is crossed on and off at its middle",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Sweep point in time")] = new Scoped(
            Verdict.Pass,
            "over the fixture's two-night store every name-session is rebuilt with the night's own components and found the same, a seed difference constructed is named, the sample draws its counts and adds the live list's, and Wilder's averages over a window match the indicator series' own",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Sweep search")] = new Scoped(
            Verdict.Pass,
            "over landscapes worked by hand the sample covers every value of every dial equally under one seed, depth reads a grid end and an end the grid does not limit, the refinement takes the deepest move and stops when none is deeper, a grid end is looked beyond with the proposal moving into it, a dial's own end is named as a limit, and the slices hold every other dial at the starting point",
            ByExpectations),
        [CheckReach.Key(CatalogueTable, "Sweep point in time")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and that it writes none, and the declaration matches this row, its matrix row and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Sweep point in time")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(FailureTable, "The point-in-time check finds a difference")] = new Scoped(
            Verdict.Pass,
            "a seed difference constructed on one name's bar is found and named with both figures, and the page a stopped run writes lists it and proposes nothing",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A sweep candidate with no sector label")] = new Scoped(
            Verdict.Pass,
            "a name with no sector is ranked in no sector and reads no rank, a sector condition that is on fails it, and one that is off passes it",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A stepped plan filled nearer its stop than the stop setting's floor")] = new Scoped(
            Verdict.Pass,
            "a stepped plan filled a twentieth of a typical move over its stop is listed and counted as no trade at a floor of half a move, with nothing entered and no result, and one filled at the floor is entered with its return over the risk its plan stated",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A chunk of the sweep fails twice")] = new Scoped(
            Verdict.Pass,
            "a chunk failing once is tried again and goes on, one failing twice stops the run with the chunk and the error recorded and the page it writes naming both and proposing nothing, and a run stopped after a chunk goes on from the next",
            ByExpectations),
        [CheckReach.Key(FailureTable, "Spend cap reached, research pauses for the period")] = new Scoped(
            Verdict.Pass,
            "at the day cap and at the month cap on its own the shipped spend cap refuses before the call, the recorded model is asked nothing, the refusal is a row carrying no call and no spend, and the period it names ends at the next UTC midnight or the first of the next UTC month",
            ByExpectations),
        [CheckReach.Key(FailureTable, "Spend cap reached, the name says research is paused and when it resumes")] = new Scoped(
            Verdict.Pass,
            "a name page over a store whose run log reaches a cap draws one line saying research is paused, naming the cap and the instant it resumes in the words the cap refuses a call with, read back off the markup, and draws none below the cap",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Night header, spend")] = new Scoped(
            Verdict.Pass,
            "the header states what research spent on the night's UTC day and in its month to that day beside both caps, each read back off the markup against sums of the run log by a query of the test's own, over rows either side of both edges",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Research paused, one line saying that research is paused and when it resumes")] = new Scoped(
            Verdict.Pass,
            "a name page over a store whose run log reaches a cap draws one line saying research is paused and the instant it resumes, the day cap and the month cap each on its own, read back off the markup against sums of the run log by a query of the test's own, and draws none below both caps",
            ByReadSurface),
        // 6.6, the prose writer. The component, the two regions of the name page a
        // written section first reaches, and the parts of section 18's two local
        // lane rows this checkpoint can draw, each over the recorded model.
        [CheckReach.Key(CatalogueTable, "Prose writer")] = new Scoped(
            Verdict.Pass,
            "the class declares the facts it reads, the research store it reads and inserts into, the run log it writes and the local model it calls, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Prose writer")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, which is where the writer reading no source document is a claim: the documents a section rests on are handed to it by the component that fetched them",
            ByAccess),
        [CheckReach.Key("15.9 Name", "How it got here, the cause of each where research has been written")] = new Scoped(
            Verdict.Pass,
            "a name whose cause section the shipped checker accepted over the recorded model draws that section's sentence in the row of the one move it names and states no cause in every other row, with the date and model read back off the markup against the store, and a name with none draws the column as absent",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The case for and the case against")] = new Scoped(
            Verdict.Pass,
            "the section is drawn a claim to a row under its two labels over a store the recorded model wrote in the parts it is asked for, each case its own paragraph cut at its sentences and read back against the stored prose by a query of the test's own, eight rows each counted by hand off the recording; one paragraph whose case against opens mid-way is cut where it opens, a third paragraph is read in the case it was set in, and prose opening on neither case is drawn a claim to a row with no label",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "How far each band is")] = new Scoped(
            Verdict.Pass,
            "each band's row states the gap from the stored close to that band's nearer edge over the name's own newest typical move, read back off the markup against a computation of the test's own from queries of its own, with a close inside a band stating no distance rather than the gap to one of its sides",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Level evidence")] = new Scoped(
            Verdict.Pass,
            "the rows run from the highest band to the lowest with the close between the resistance and the support, read off the fixture name's page and off constructed bands worked by hand; each band's line counts its swings and visits by kind with the first and last dates or the one date they share, names an average and a shelf by kind with no date, a band of the 200-day average alone included; and each strength carries a bar three pixels a point beside a key saying what a point is",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "The risks as parts")] = new Scoped(
            Verdict.Pass,
            "the section is drawn as a table of four rows over a store the recorded model wrote in the parts it is asked for, each risk beside what would confirm it and each row its own paragraph read back against the stored prose by a query of the test's own, and constructed prose every part of which confirms apart draws the same table; over prose whose parts do not all confirm apart the parts are cut where it says a risk starts with what would confirm each set beneath it and the run before the first cut opening the first part, the parts joined back up asserted to be the prose as it was written; a section saying nowhere that a part ends is drawn a claim to a row, and a section of another name written in the same shape draws no part at all",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Contents")] = new Scoped(
            Verdict.Pass,
            "the contents is read against the regions the page drew in both directions, so neither a region nobody can reach nor an entry pointing at nothing passes, with the numbering contiguous from where a reader starts and the contents standing above the first region it names; and the page's script is read to follow a link whose address is not a screen's to its place on the page below the masthead, where a screen's own address still opens that screen",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "Sections left out")] = new Scoped(
            Verdict.Pass,
            "a section the checker left out and one the newest pass did not write each draw one line naming every rule that refused them, with what the checker extracted beside each rule and the draft's own sentence never drawn, repeats collected under their rule, read over the reasons the shipped checker composes for prose written to break each rule in turn, and a reason carrying no second refusal drawn as it was stored",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "A section is assigned to the local lane that the machine cannot hold, the pass is refused before it starts")] = new Scoped(
            Verdict.Pass,
            "at a context the release's prompts cannot fit, the three sections carrying it are refused while the section that fits is asked, and none of the three reaches the model although a recording answers each, so the refusal came before a call rather than after one failed",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A section is assigned to the local lane that the machine cannot hold, the run log names the section and the reason")] = new Scoped(
            Verdict.Pass,
            "the prose stage's run log row names each refused section with the estimate and the context it was refused against, read off the stored detail",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A section is assigned to the local lane that the machine cannot hold, the section is absent as usual")] = new Scoped(
            Verdict.Pass,
            "a refused section has no stored row, the name page draws no written section for it, and the line it does draw carries the reason the run log stored",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "The local model is unavailable, the sections in the local lane are left unwritten")] = new Scoped(
            Verdict.Pass,
            "a runtime with nothing listening leaves every section in the lane unwritten after one call, stores no row and records the pass as unavailable, through the shipped feed over a transport that refuses",
            ByExpectations),
        [CheckReach.Key(FailureTable, "The local model is unavailable, the local-lane sections absent with their reason")] = new Scoped(
            Verdict.Pass,
            "each section the pass could not write is drawn on the name page with the reason the writer recorded, read back off the markup against the stored run log detail",
            ByReadSurface),
        // 6.5, the staleness judge. The component, section 17's trigger row, and
        // figure 12.1's three questions, each over the fixture's own dates.
        [CheckReach.Key(CatalogueTable, "Staleness judge")] = new Scoped(
            Verdict.Pass,
            "the class declares the research store, facts, calendar, news pulse and fundamentals it reads and the run log it writes, and no feed, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Staleness judge")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, which is where the judge's writing nothing but the run log is a claim",
            ByAccess),
        [CheckReach.Key(LimitsTable, "Research staleness triggers")] = new Scoped(
            Verdict.Pass,
            "one test per trigger, each with the case that must not fire beside the case that must, the news window's floor, multiple and baseline read off this row against the constants the rules use, and a spike dated by the session its run began so a story over several days does not fire twice",
            ByExpectations),
        [CheckReach.Key("Figure 12.1", "Is there a research record?")] = new Scoped(
            Verdict.Pass,
            "a name with no accepted section is answered as missing rather than as stale, with every trigger firing, and a name whose sections only ever fell back is missing too",
            ByExpectations),
        [CheckReach.Key("Figure 12.1", "Does it still stand?")] = new Scoped(
            Verdict.Pass,
            "the shipped judge over the fixture's own stored filing, earnings date and facts night reaches the verdict the staleness expectation worked out by hand for each of four sections, and each trigger is attributed to the sections it reaches",
            ByExpectations),
        [CheckReach.Key("Figure 12.1", "All four no")] = new Scoped(
            Verdict.Pass,
            "a record whose sections were all written after every event stands, and the judge's own run log rows record no request, no model call and no spend, over a class that declares no feed and takes no client",
            ByExpectations),
        // 6.4, the claim checker. The component and its two tables' surfaces, the
        // limits row and the two expected rejections, and figure 12.1's two boxes.
        [CheckReach.Key(CatalogueTable, "Claim checker")] = new Scoped(
            Verdict.Pass,
            "the class declares the research store and the theme store it reads and updates, the facts and source documents it reads, and the declaration matches this row, repaired at 6.4 to name the theme store, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Claim checker")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(LimitsTable, "Claim rejection")] = new Scoped(
            Verdict.Pass,
            "a figure the facts file does not hold and a sentence naming no stored document are each refused over a replayed store, the retry is asserted to be one over six consecutive refusals read off the stored rows, and a section refused twice is left out with both attempts' offending text kept",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "a poisoned paragraph")] = new Scoped(
            Verdict.Pass,
            "the committed paragraph with one figure the facts file does not hold is refused naming exactly that figure, beside the same paragraph with the figure restored, which is accepted",
            ByAdmissibility),
        [CheckReach.Key(FixtureTable, "an unsourced claim")] = new Scoped(
            Verdict.Pass,
            "the committed paragraph whose second sentence names no document is refused naming that sentence, and a sentence citing a stored row admissibility refused is refused as well, because stored is not enough",
            ByAdmissibility),
        [CheckReach.Key("Figure 12.1", "Check every claim")] = new Scoped(
            Verdict.Pass,
            "a number is checked against the facts file of the day the section was written, a claim against a stored and admitted document, and a section failing twice falls back rather than being retried a third time, each over the store",
            ByAdmissibility),
        [CheckReach.Key("Figure 12.1", "Store it")] = new Scoped(
            Verdict.Pass,
            "the checker moves a pending section and changes no column but the status and the reason, every earlier version is kept, and the update statement's own column set is read against SCHEMA's declaration",
            ByAdmissibility),
        [CheckReach.Key("15.10 Run", "Stale and failed, sections that fell back")] = new Scoped(
            Verdict.Pass,
            "the sections the shipped checker left out on the night are read back off the region's markup with whose each was and the reason it stored, a theme's beside a name's, and one that fell back on another day is not drawn",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "Claim checker rejects twice")] = new Scoped(
            Verdict.Pass,
            "a section the shipped checker refused twice is absent from the name page with one line carrying the reason and the figure, and a section still waiting on its retry is drawn as neither written nor left out",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "A pass finds no admissible source for a section")] = new Scoped(
            Verdict.Pass,
            "a section whose source list holds nothing admitted falls back on its first check and is absent from the name page with one line saying no admissible source was found, read back off the markup",
            ByReadSurface),
        // 6.4, the research store and the theme store. Their columns against
        // SCHEMA, the theme table written out rather than described by
        // difference, and the status guarded by the store itself.
        [CheckReach.Key(StoresTable, "Research store")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, the reason is asserted to be the only column admitting null, and a status outside the four SCHEMA declares is refused by the store rather than by the checker",
            ByMigration),
        [CheckReach.Key(StoresTable, "Theme store")] = new Scoped(
            Verdict.Pass,
            "the table's columns are asserted against SCHEMA.md, which wrote them out at 6.4 rather than describing them as the research table's with a subject renamed, and the store refuses a status it does not declare as the research table's does",
            ByMigration),
        // 6.3, the source documents store. Its columns against SCHEMA as every
        // other table's are, plus the two that admit null, which is the first
        // table here where nullability carries a property rather than being a
        // detail: a refusal is kept as a row with no body, and one of the things
        // a document is refused for is carrying no publish date.
        [CheckReach.Key(StoresTable, "Source documents")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, and exactly two of them are asserted to admit null in both directions, being the date a refusal for a missing date has to record and the body a refusal never carries",
            ByMigration),
        [CheckReach.Key(CatalogueTable, "Fundamentals fetcher")] = new Scoped(
            Verdict.Pass,
            "the class declares both feeds and the stores it touches, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Fundamentals fetcher")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(FailureTable, "Filing not yet parsed for a name")] = new Scoped(
            Verdict.Pass,
            "the numbers section is drawn over a store the fetcher filled and read back off its own markup, over a name whose archive was read and a name whose was not: the segment table and the guidance are drawn for the first and marked absent for the second with the reason the row states, and no cell anywhere in the section is drawn empty",
            ByReadSurface),
        [CheckReach.Key("Figure 12.1", "Computed sections appear")] = new Scoped(
            Verdict.Pass,
            "the name route serves the computed sections from the nightly store for a name holding no filing at all, and the numbers section says so rather than delaying the page or drawing an empty table",
            ByReadSurface),
        [CheckReach.Key(StoresTable, "Calendar")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md, including the timing column that replaced the status 4.0 invented",
            ByMigration),
        [CheckReach.Key(CatalogueTable, "Calendar fetcher")] = new Scoped(
            Verdict.Pass,
            "the class declares the feed and the stores it touches, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Calendar fetcher")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(NightlyRunSteps.Heading, "Fetch the index's dated events for the horizon, one request, and store what the provider files (see: A calendar event is fetched once for the whole index, and the calendar holds provider events only).")] = new Scoped(
            Verdict.Pass,
            "the night runs it after the corporate actions and before the per-name work, and the feed's own count is one for the window whatever the universe size",
            ByNight),
        // 6.1, the fundamentals. Two rows, because the captured payload is an
        // input and the twelve filings one fetch keeps of the fourteen it holds
        // are an expected output, and one test reads the capture against the
        // store, which is what reaches both.
        [CheckReach.Key(FixtureTable, "fundamentals")] = new Scoped(
            Verdict.Pass,
            "the four captured payloads are parsed and the store is diffed against what the rules produce over them: the filing date read as the filing date rather than the period it covers, the quarter the provider files no filing date for counted and not stored, and money taken from both of the two forms one payload sends it in",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "stored filings")] = new Scoped(
            Verdict.Pass,
            "twelve of the fourteen filings each capture holds, asserted to be the twelve most recent by filing date, with the margin divided by the test rather than read from the expectation, the three as-of-the-fetch parts asserted to sit on the newest filing alone, and the company financials provider asserted never to be named as the source of a part the archive supplies",
            ByExpectations),
        // 6.2, the archive. One row rather than two: the captured archive responses
        // fall under the fundamentals input row, which has said since 0.7 that it
        // holds the provider payload and the filing extracts as they stood on the
        // fixture date, so the input half was named before either existed.
        [CheckReach.Key(FixtureTable, "archive extracts")] = new Scoped(
            Verdict.Pass,
            "the store is diffed against what the archive's own rendering rules produce over thirteen captures: the report the route chose and how many it read to choose it, the scale applied by the test rather than read from the expectation, two groups sharing a label kept as two, a row stating its own unit left out of the scale, the guidance located for one filer and not for the other and stored as the passage either way, and the balance-sheet fact kept although the archive sends it no start date",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "calendar")] = new Scoped(
            Verdict.Pass,
            "the captured response is diffed against what the rules produce over it, read off the file by hand before the fetcher was run: three events inside the window, two stored, and one refused for a name the index does not hold",
            ByExpectations),
        [CheckReach.Key(FailureTable, "Earnings date missing, the calendar")] = new Scoped(
            Verdict.Pass,
            "two of the four fixture names have no row over the window, and the page states that the date is not on file rather than drawing a blank",
            ByExpectations),
        [CheckReach.Key(FixtureTable, "ladder")] = new Scoped(
            Verdict.Pass,
            "the trend state and the tranches of all four names are diffed against a set derived from the rules outside this repository, with the averages, the last two swings of each kind and the band each stop comes from stated beside the answer so a disagreement is traceable to an input",
            ByExpectations),
                [CheckReach.Key(FailureTable, "A gap in one name's series, level and plan sections")] = new Scoped(
            Verdict.Pass,
            "a name whose series was refused holds no bars, so it has no bands and no plan, and both sections state the absence rather than drawing nothing, with a name whose series was not refused carrying both over the same run",
            ByGap),
                [CheckReach.Key(LimitsTable, "Earnings horizon")] = new Scoped(
            Verdict.Pass,
            "the second book is keyed to the next dated event the calendar holds, a name with none produces no setups and says why, and every setup carries a trigger, an entry, a stop and a target with each figure stated on the page as a proposal; and earnings soon fires on the sessions the exchange calendar counts to that event, asserted through the shipped builder over a store holding no bar after its night at the night itself, the twentieth session, the twenty-first, across the year-end closures, forty sessions out and past the closure table's end, with the fixture's two dated prints recomputed from the calendar against counts walked by hand",
            ByExpectations),
        // 8.1, the setup horizon counted from the entry.
        [CheckReach.Key(LimitsTable, "Setup resolution")] = new Scoped(
            Verdict.Pass,
            "a setup starts on the first close at or below its entry zone's top edge and resolves as a win, a loss, unresolved at the cap or never entered, each asserted over the cases the forward returns expectation works by hand, with the cap counted from the listing, a target past the cap left unresolved on the cap's own session and the return measured from the entry close; the filler stores those cases from the plan a listing carries, scores a setup still in play with its plan scaled by the listing session's adjustment over cases restated by a dividend and a split, and keeps a decided row when the bars it was scored on leave the store",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Tranches, exits")] = new Scoped(
            Verdict.Pass,
            "at most three tranches and at most five exits over four names, and an exit within two typical days' moves of the blended entry is listed and not traded with its reason on the row rather than omitted",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Tranche eligibility")] = new Scoped(
            Verdict.Pass,
            "a support band whose low edge is below the close carries a tranche and keeps its full width where it straddles, and a band anchored on an average alone carries none and is still the stop of the tranche above it, asserted over the fixture and over a constructed band",
            ByExpectations),
        [CheckReach.Key(FailureTable, "No band is eligible to carry a tranche")] = new Scoped(
            Verdict.Pass,
            "a name whose only support band below the price is anchored on an average produces no tranche and a plan saying so, told apart from a name with no support band below the price at all",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A name whose trend state cannot be classified")] = new Scoped(
            Verdict.Pass,
            "each missing input is induced over constructed input and produces the fourth state naming what was absent, and the row is still written with its plan carrying the reason",
            ByExpectations),
        
        [CheckReach.Key(FixtureTable, "indicators")] = new Scoped(
            Verdict.Pass,
            "every name, session and indicator carries a row, the averages match arithmetic done over the committed bars outside this repository, and an indicator without its window is null with the bar count that explains it",
            ByExpectations),
        [CheckReach.Key(CatalogueTable, "Indicator engine")] = new Scoped(
            Verdict.Pass,
            "the class declares the bar store it reads and the indicators it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Indicator engine")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included, against the row contradiction K repaired at this checkpoint",
            ByAccess),
        [CheckReach.Key(FixtureTable, "swings")] = new Scoped(
            Verdict.Pass,
            "every swing the committed bars carry is diffed against a set found outside this repository, with the session that would be a swing at two bars each side and is not one at three named so the lookback is asserted rather than agreed with",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Swing lookback")] = new Scoped(
            Verdict.Pass,
            "the number the row states is read off the row against the constant the finder uses and against the lookback the expectation was derived at, and the diff is run over a fixture holding a session the two lookbacks disagree about",
            ByExpectations),
        [CheckReach.Key(CatalogueTable, "Swing finder")] = new Scoped(
            Verdict.Pass,
            "the class declares the bar store it reads and the swings it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Swing finder")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(FixtureTable, "volume profile")] = new Scoped(
            Verdict.Pass,
            "the whole profile is diffed against bands found outside this repository, the shares in them are asserted to sum to the window's total read back off the bar table, and one session's spreading is checked against the overlaps its own range makes with the bands it covers",
            ByExpectations),
        [CheckReach.Key("15.5 The mark vocabulary", "Volume profile")] = new Scoped(
            Verdict.Pass,
            "the profile is drawn against the price axis the chart computed, asserted by the two marks declaring the same axis, by the topmost band sitting where the chart puts that price rather than at the top of its own pane, and by the same bands moving when a different axis is given",
            ByReadSurface),
        [CheckReach.Key(CatalogueTable, "Volume profile builder")] = new Scoped(
            Verdict.Pass,
            "the class declares the bar store it reads and the profile it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Volume profile builder")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(FixtureTable, "levels")] = new Scoped(
            Verdict.Pass,
            "every band, every member and every score is diffed against a band set built outside this repository from figure 9.1's five steps, with the edges asserted to be the anchors alone so a touch cannot widen one",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Level window")] = new Scoped(
            Verdict.Pass,
            "the sixty sessions are the population the bands, the swings and the profile are all built over, asserted as the same window in the diff of each",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Band merge distance")] = new Scoped(
            Verdict.Pass,
            "the fraction the row states is read off it against the multiple the builder merges at, each current member's distance in the expectation is that multiple of the average true range the store holds at its as-of session, at a price's four places, and no two stored bands are closer to each other than that distance but the parts of a chain split for its width, every run of which spans more than the widest a band may be, two runs in the fixture counted in advance",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Band width")] = new Scoped(
            Verdict.Pass,
            "the multiple the row states is read off it against the one the builder splits at, no stored band of the fixture is wider than that multiple of the average true range the store holds at its as-of session, and constructed chains either side of the width and exactly at it split where the row says, at the widest gap and the lower of two equal ones",
            ByExpectations),
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, the level bands")] = new Scoped(
            Verdict.Pass,
            "the bands are drawn behind the candles rather than over them, asserted by position in the markup, each carrying its role in words as well as in one of the two hues those roles own",
            ByReadSurface),
        [CheckReach.Key(CatalogueTable, "Level builder")] = new Scoped(
            Verdict.Pass,
            "the class declares the stores it reads and the levels it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Level builder")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key("15.5 The mark vocabulary", "Momentum panel")] = new Scoped(
            Verdict.Pass,
            "relative strength is drawn on an axis of its own and the three convergence readings on one, each axis with one neutral rule across it counted off the rendered markup, relative strength's edges at 70 and 30 drawn and named, and each rule's value read from the arithmetic that defines the reading rather than chosen by the mark",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "Fewer than 200 bars for a new index member, nn bars")] = new Scoped(
            Verdict.Pass,
            "an average with no value anchors no band, so the level summary table names it in its own row with the bar count that explains it, and the row is absent where nothing is absent rather than standing as a permanent caveat",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Volume shelf threshold")] = new Scoped(
            Verdict.Pass,
            "the figure is measured against the candidates either side of it across all four fixture names: at one even share it names most of the chart, at three it leaves three of the four with no shelf at all, and at two every name has one and it is a small minority of bands holding a fifth to a half of the period",
            ByExpectations),
        [CheckReach.Key(CatalogueTable, "Bar fetcher")] = new Scoped(
            Verdict.Pass,
            "the class declares the bulk feed it reads and the stores it touches, including the retention delete SCHEMA now declares, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Bar fetcher")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(LimitsTable, "Model calls in the nightly run")] = new Scoped(
            Verdict.Pass,
            "zero on every stage of the arithmetic a whole recorded night wrote, read off the run log, with every model call that night made sitting on the overnight queue's own row or a pass that row names, nothing spent on any row, and the night's composition reaching no lane an open reaches, read off what the components it constructs declare",
            ByCost),
        [CheckReach.Key(LimitsTable, "Per-name network calls in the nightly run")] = new Scoped(
            Verdict.Pass,
            "the night is run twice over universes of three members and two, and the request count is one in both, so it is shown not to grow with the population rather than measured once; a recorded night at one rule version and at the fullest register the bound admits makes the same requests on every step and none on the version step, counted off the feeds and put on the step each was made on; and a suspect name whose refetch keeps failing is measured night by night against a sequence derived from the decision, one request on its action's night and on each retry night and none once its retries are spent, with the figure the row states held to the constant",
            ByCost),
        [CheckReach.Key(FailureTable, "Bulk price feed unavailable, run log")] = new Scoped(
            Verdict.Pass,
            "a night whose feed does not answer stores nothing, leaves the bars it already held exactly as they were, names the step and exits non-zero, and the run log carries the step and the reason as a failed row that the run page's stale-and-failed region draws",
            ByNight),
        [CheckReach.Key(FailureTable, "A feed answers with a session other than the one asked for")] = new Scoped(
            Verdict.Pass,
            "a payload carrying a session other than the requested one is refused inside the feed, naming both dates, and the stored series is unchanged",
            ByNight),
        [CheckReach.Key(FailureTable, "A feed answers with none of the index in it")] = new Scoped(
            Verdict.Pass,
            "a payload carrying nothing for any current member is refused by the fetcher before the transaction opens, and one short of some but not all is stored for the rest with the count carried out of the stage",
            ByNight),
        // Added at the phase 5 sign-off, when the unnamed refusal 5.7 read as
        // the provider serving something other than prices turned out to be one
        // fund's fractional volume refusing the whole exchange's file.
        [CheckReach.Key(FailureTable, "A feed answers with a row the reader cannot read")] = new Scoped(
            Verdict.Pass,
            "a night over the captured file with a fund's fractional volume added outside the index stores every member, and the same volume on a member's own row refuses the night at the fetch step with the ticker and what arrived, leaving the stored bars as they were",
            ByNight),
        // Added at the phase 5 sign-off with the bulk catch-up.
        [CheckReach.Key(FailureTable, "A session the night finds missing")] = new Scoped(
            Verdict.Pass,
            "a night run over a store whose last night was two sessions back fetches the missed session in bulk before its own and stores every member on both, a closure between them costs no request, a missed session whose file carries none of the index stops the night at the fetch with the session named and nothing stored, and a joiner backfilled through tonight does not hide the missed session",
            ByNight),
        // Added at the phase 5 sign-off with the rebalance and the closed day.
        [CheckReach.Key(FailureTable, "A night on a day the exchange did not trade")] = new Scoped(
            Verdict.Pass,
            "a night whose session is a Saturday and one whose session is a closure each write one row under the closing stage with the no-session outcome, make no request, store nothing and exit 0, and the run page's failed region does not count the row",
            ByNight),
        [CheckReach.Key(FailureTable, "An index change announced before it takes effect")] = new Scoped(
            Verdict.Pass,
            "over a membership carrying a leaver and a joiner dated after the night, the leaver is backfilled, stored, listed and laddered and the joiner is backfilled and stored and neither listed nor laddered, and on its effective date each goes the other way",
            ByNight),
        [CheckReach.Key(FailureTable, "A ticker the index feed stops listing")] = new Scoped(
            Verdict.Pass,
            "over the fixture's second night, a member the feed drops, a second span of a member it lists under another join date and an announced joiner it stopped listing are each closed on the night's session, the dropped member keeps the night before's listing and has none that night, is out of the universe and is named on the run page's membership line, and a second run whose feed lists it again reopens it and lists it; a feed dropping one ticker more than the figure the row states closes none and ends partial on the stale-and-failed region with the tickers named, and one dropping that many closes every one",
            ByNight),
        [CheckReach.Key(FailureTable, "The provider serves no year for a name the backfill asks for")] = new Scoped(
            Verdict.Pass,
            "over the fixture's night and the backfill's own nights after it, a member the feed lists and neither price file serves holds no bar, is asked for on each of the five nights after the first, not again until the seventh day after the session last asked for and then on it, each row naming it with its nights and its next ask, and the stage ends partial on every night so the run page's stale-and-failed region names it; its page opens with the line stating the nights it was asked for and the first night it is asked again",
            ByNight),
        [CheckReach.Key(LimitsTable, "Per-request timeout and the night's deadline")] = new Scoped(
            Verdict.Pass,
            "the three attempts, the doubling wait and both bounds are read off the row and asserted against the policy the code uses, and a night given a deadline it cannot meet stops on the step it was on and says so",
            ByNight),
        [CheckReach.Key(LimitsTable, "Waiting on another writer")] = new Scoped(
            Verdict.Pass,
            "the wait is read off the row and asserted against the one every connection the worker and the store open carries, read from the shipped source, where no file but the helper and the sources the two pin lists name writes a connection string, and a pass and a theme pass whose store refuses a document partway keep none of the documents they fetched",
            ByNight),
        [CheckReach.Key(LimitsTable, "Weighted-call budget")] = new Scoped(
            Verdict.Pass,
            "every weight and the allowance the code holds, the earnings calendar's among them, are read back out of RUNBOOK rather than repeated in code, a request from every feed role the night composes is counted in weighted calls against the example the row's reason gives and against the cost of a calendar window its expectation records, the night reports its weighted total beside its request count, and a night already at the allowance stops before its next step",
            ByCost),
        [CheckReach.Key(LimitsTable, "Bar history kept")] = new Scoped(
            Verdict.Pass,
            "the retention boundary is the fetched session less one year, and every session below it is gone from the store while none inside it is",
            ByCost),
        [CheckReach.Key(LimitsTable, "Backfill")] = new Scoped(
            Verdict.Pass,
            "the run log's request count for the backfill stage is one per name lacking history, which is the carve-out the row states and the reason the steady-state limit does not fail on it",
            ByCost),
        [CheckReach.Key(NightlyRunSteps.Heading, "Load index membership and record any joins and leaves.")] = new Scoped(
            Verdict.Pass,
            "the night runs it first, and the step is named in the order section 14 states",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Backfill one year for any member with no stored history, which on the first run is every name and afterwards is only a new joiner.")] = new Scoped(
            Verdict.Pass,
            "the night runs it after membership, and a second night backfills nothing",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, "Fetch the day's bulk bar file, one request, and store the bars for every name that has not left the index by the session, a name announced to join included, first fetching in bulk, one request each, any session the store is missing since the last night that ran (see: A session the night finds missing is fetched in bulk before tonight's) (see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement).")] = new Scoped(
            Verdict.Pass,
            "the night runs it after the backfill, in one request on a night that follows one that ran, storing the day for the names that have not left and for no other, an announced joiner and an announced leaver included and a departed name not, and a night after one that did not run fetches the missed session first, one request more, and stores both",
            ByNight),

        // The fundamentals item, a 12.2 correction: the fetch after the close, the readings, the state, the
        // order and where each is drawn.
        [CheckReach.Key(CatalogueTable, "Fundamental reader")] = new Scoped(
            Verdict.Pass,
            "the class declares the membership, bars, calendar and reported quarters it reads and the readings it inserts and deletes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Fundamental reader")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Quarter fetcher")] = new Scoped(
            Verdict.Pass,
            "the class declares the membership and calendar it reads, the reported quarters and asks it reads and inserts and the feeds it reaches, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Quarter fetcher")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Reported quarters")] = new Scoped(
            Verdict.Pass,
            "one row per member per fetch per quarter over the twelve newest the answer carries, with the figures worked out at the fetch, read back off the store the fixture's replay wrote against NFLX's growths, margins and closes worked by hand from its captures",
            ByExpectations),
        [CheckReach.Key(StoresTable, "Quarter asks")] = new Scoped(
            Verdict.Pass,
            "one row per member asked, carrying why, what came of it, the quarters it stored and the weighted calls it spent, read back off the store the fixture's replay wrote, each of the four names asked once for the fill and stored",
            ByExpectations),
        [CheckReach.Key(StoresTable, "Fundamental readings")] = new Scoped(
            Verdict.Pass,
            "one row per member per night, every member of the fixture's night reading no fundamentals yet since no quarter was fetched before it, read back off the store the fixture's replay wrote, and each name's readings over its stored quarters worked by hand from its captures",
            ByExpectations),
        [CheckReach.Key(NightlyRunSteps.Heading, Checks.ArchitectureConformance.ReadingsStep)] = new Scoped(
            Verdict.Pass,
            "the night runs the step after the swing readings and before the listings, writing a row for every member, and the fixture's night reads every member as no fundamentals yet, holding no quarter fetched before it",
            ByNight),
        [CheckReach.Key(NightlyRunSteps.Heading, Checks.ArchitectureConformance.QuartersStep)] = new Scoped(
            Verdict.Pass,
            "the night runs the step after the close and before the overnight queue, the fixture's night asking for each of its four members once and storing their quarters, and a night run again for an earlier session asking for none and saying so",
            ByNight),
        [CheckReach.Key("15.7 Tonight", "The list, the state-first order from the first night whose readings are stored and a night before it in the filter's own order")] = new Scoped(
            Verdict.Pass,
            "over one store holding a night before the first readings and a night after, the earlier night's page draws the filter's own order and the later night's the state-first order over the same gate rows",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, the state its reported quarters give it beside the trend")] = new Scoped(
            Verdict.Pass,
            "each row's state word is drawn in its trend cell as the night stored it, and a night that stored no readings draws none",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, what the numbers say while the state is under the pointer or holds focus")] = new Scoped(
            Verdict.Pass,
            "the word can hold focus and carries the sentences its readings give, read back word for word, and the stylesheet shows them on hover and on focus and hides them otherwise",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "What the numbers say, a heading carrying the state and the quarter it was read from")] = new Scoped(
            Verdict.Pass,
            "the heading is section 4's heading pattern with the night's state, and the quarter the readings were read from stands beneath it, read back off the name's page over a constructed store",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "What the numbers say, one sentence per reading written from section 4's patterns")] = new Scoped(
            Verdict.Pass,
            "the sentences drawn are read back word for word against sentences worked by hand from constructed readings, and each matches one of section 4's patterns",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "What the numbers say, a sentence saying so where a reading is absent or read over too few quarters")] = new Scoped(
            Verdict.Pass,
            "readings over too few quarters, a year lacking a figure and a member holding no quarter each draw the sentence saying so, read back word for word",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "What the numbers say, the quarters the readings read with the dates each was filed and reported on")] = new Scoped(
            Verdict.Pass,
            "the quarters the readings read are drawn with their filing and report dates as the fetch stored them, and only those",
            ByReadSurface),
        [CheckReach.Key("15.9 Name", "What the numbers say, the full numbers table folded beneath them")] = new Scoped(
            Verdict.Pass,
            "the numbers table stands inside the folded part beneath the sentences, and a night that stored no readings draws the numbers with no summary",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, within a night the order that night's list was drawn in, improving businesses first where it stored readings and the filter's own order where it stored none")] = new Scoped(
            Verdict.Pass,
            "over a night whose readings put the filter's first name deteriorating and its second improving, Past picks draws the second first within that night, and a night that stored none draws the filter's own order",
            ByReadSurface),
        [CheckReach.Key("15.17 Past picks", "Every trade, the state its reported quarters gave it on the night it was listed, or not read that night")] = new Scoped(
            Verdict.Pass,
            "each trade draws the state its listing night stored, and one listed on a night that stored none reads not read that night",
            ByReadSurface),
        [CheckReach.Key(LimitsTable, "Quarters step")] = new Scoped(
            Verdict.Pass,
            "a member is asked on the first night after its report and on each of the five after while its quarter is not posted, then not until seven days after its last ask, a step at its limit starts no ask and an ask that would pass the allowance is not made, over constructed nights, and the constants the row states are the ones the step uses",
            ByCost),
        [CheckReach.Key(LimitsTable, "Quarters fill")] = new Scoped(
            Verdict.Pass,
            "over a constructed index larger than a night's fill, the first night asks a reporting member and then the fill's count in ticker order and the next night the rest, and a member the provider returns nothing for is marked absent and asked again on the schedule",
            ByCost),
        [CheckReach.Key(LimitsTable, "Weighted calls a quarters ask")] = new Scoped(
            Verdict.Pass,
            "an ask storing a quarter spends the fundamentals' weight and the closes', one storing nothing the fundamentals' alone, each on its row, against the weights RUNBOOK states",
            ByCost),
        [CheckReach.Key(LimitsTable, "Quarter prices")] = new Scoped(
            Verdict.Pass,
            "each stored quarter's close after its report is the first captured close within a week after the report and the basis the newest captured close, worked by hand for NFLX from its captures",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Trajectory quarters")] = new Scoped(
            Verdict.Pass,
            "the trajectory reads the two newest quarters and a fetch one short or lacking a figure reads too few, over constructed quarters, and NFLX's two growths and margins are worked by hand from its capture",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Business state")] = new Scoped(
            Verdict.Pass,
            "each of the five states is worked by hand over constructed quarters, sales at nought or a margin equal to a year earlier reading steady, and the fixture's four names' states are worked by hand from their captures",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Estimate record")] = new Scoped(
            Verdict.Pass,
            "the eight newest quarters carrying both figures are counted, four being the fewest stated and three reading too few, and a quarter with no actual is never met, over constructed quarters, with NFLX's record worked by hand from its capture",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Met tolerance")] = new Scoped(
            Verdict.Pass,
            "a gap of exactly a cent meets and a cent more beats, and a gap of exactly the share of an estimate above a dollar meets and a step more misses, over constructed quarters",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Earnings quality")] = new Scoped(
            Verdict.Pass,
            "ratios of exactly each cut point read in line and a step outside each the band beyond, and a total at or below nought reads a loss, over constructed quarters, with NFLX's ratio worked by hand",
            ByExpectations),
        [CheckReach.Key(LimitsTable, "Valuation position")] = new Scoped(
            Verdict.Pass,
            "twelve constructed quarters place tonight's multiple in each third and on each edge, seven multiples read too few and earnings at or below nought read none, and the fixture's captures, holding one year of closes, read too few",
            ByExpectations),
        [CheckReach.Key(FailureTable, "A member the provider returns no quarter for")] = new Scoped(
            Verdict.Pass,
            "a member holding no quarter reads no fundamentals yet in its trend cell and on its page and is drawn with the members reading no state rather than left off, and the quarters step's line in the run page's operational header counts the asks that returned nothing, over a constructed store",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "A member's new quarter is not yet posted when it is asked")] = new Scoped(
            Verdict.Pass,
            "what the numbers say names the quarter awaited and the quarter read from, and the quarters step's line in the run page's operational header counts the asks whose quarter was not yet posted, over a constructed store",
            ByReadSurface),
        [CheckReach.Key(FailureTable, "The provider refuses a quarters ask")] = new Scoped(
            Verdict.Pass,
            "a member whose ask the feed refuses is recorded refused with the reason and asked again on the schedule, and the next member is still asked, over a constructed night",
            ByCost),
        [CheckReach.Key(FailureTable, "The quarters step reaches its limit or the day's allowance")] = new Scoped(
            Verdict.Pass,
            "a step past its limit starts no ask and one at the allowance makes none, each counting what it left on its row, and the members left are asked on the next night, over constructed nights",
            ByCost),
        [CheckReach.Key(FailureTable, "A night that stored no readings of the reported quarters")] = new Scoped(
            Verdict.Pass,
            "on a night that stored no readings tonight's rows carry no state word and are drawn in the filter's own order, a Past picks trade listed that night reads not read that night, and the name page draws its numbers with no summary",
            ByReadSurface),
        [CheckReach.Key(FixtureTable, "reported quarters")] = new Scoped(
            Verdict.Pass,
            "the fixture's replay stores each of its four names' quarters from one ask each, read against figures worked by hand from NFLX's capture, every member reads no fundamentals yet on the fixture's night, and each name's readings over its stored quarters match readings worked by hand",
            ByExpectations),
    };

    // Where the plan names a subject, the due point is read from the plan and
    // is not written here. What follows is the residue: subjects BUILD_PLAN's
    // checkpoint text does not name, and two it names in a way that cannot be
    // read. Every entry below is one the derivation could not supply, and the
    // reconciliation asserts that in both directions, so an entry that becomes
    // derivable later fails rather than silently shadowing the plan.

    // The plan names these and the derivation must not take them. They are kept
    // in two lists rather than one, because the two directions are not the same
    // kind of thing and treating them alike hides the dangerous one.
    //
    // Each entry says why. An exception with no reason is the mechanism by which
    // a derivation gets quietly switched off, and the direction each list
    // declares is asserted against the plan rather than trusted, because a
    // mislabelled exception is the one failure the split cannot otherwise catch.

    // Derived later than the truth. A late due point can only delay a claim: it
    // says nothing asserts something that already does, which is a smaller
    // statement than the truth and never a failing one. Declared, and quiet.
    static readonly Dictionary<string, string> DerivedIsLate = new(StringComparer.Ordinal)
    {
        // 5.5 creates the table and the plan writes `forward_return` there in
        // the snake case the schema uses, so the plural store name matches
        // nothing until 8.1 mentions forward returns in prose.
        ["Forward returns"] = "5.5",
    };

    // Derived earlier than the truth. An early due point fails the day the
    // checkpoint it names lands, because out of scope means a point that has
    // not been reached and the reconciliation refuses one that has.
    //
    // These report themselves on the phase report every run. A known unsafe
    // derivation sitting in the tree and visible only in a source comment is an
    // exception nobody sees again, and an exception nobody sees again becomes
    // one nobody remembers.
    static readonly Dictionary<string, string> DerivedIsEarly = new(StringComparer.Ordinal)
    {
        // 1.7 produces the first draft of the source lists, which is why it
        // names them. The limits row is not about the lists existing: it is
        // about a search returning only sites on the list that applies to it,
        // and the row's own Asserted by column names a fixture search. That
        // arrives with the research pass at 6.1.
        ["Source lists"] = "6.9",

        // 1.2 builds the backfill, and this row is the limit on it rather than
        // the component. Its own Asserted by column names the run log's request
        // count against the names lacking history, which is nightly-cost reading
        // a recorded run, and that arrives at 1.4.
        ["Backfill"] = "1.4",

        // 5.1 names the listings store to say it is absent there: its universe
        // screen draws every region except the ones that store feeds, and its
        // text has to name what it is not drawing. The store itself is created
        // at 5.4. Read from the plan alone the subject resolves to the first
        // checkpoint whose text carries the word, which is the prefix shape the
        // verification rules name as having arrived four times and which this
        // dictionary exists to declare rather than to hide.
        ["Listings"] = "5.4",

        // 5.4's own text names the fundamentals, in the sentence saying the
        // shortlist reads none of them. The store arrives at 6.1 with the
        // fetcher that writes it, and read from the plan alone the subject
        // resolves to the first checkpoint whose text carries the word.
        ["Fundamentals"] = "6.1",

        // The base rate limit's own Asserted by cell names a run page test, and
        // the run page is 5.6. 5.5 computes the figure and stores it beside
        // every return; the claim is that no forward-return figure is shown
        // without it, which is a claim about a surface.
        ["Base rate"] = "5.6",

        // 6.1's text names the reported quarters the numbers section shows, which are the provider's
        // quarters on the name page and not this store. The store arrives with the quarter fetcher that
        // writes it, a 12.2 correction.
        ["Reported quarters"] = "12.2",
    };

    // Components the plan does not name. The catalogue and the matrix share it.
    static readonly Dictionary<string, string> Components = new(StringComparer.Ordinal)
    {
        // 4.2 builds the ladder and never uses the component's name.
        ["Ladder builder"] = "4.4",
        // 12.2's verb, which the plan calls the counts rather than by the component's name.
        ["Filter counts"] = "12.2",
        // 12.4's two, which the plan calls the proposer and the command.
        ["Shape proposer"] = "12.4",
        ["Shape command"] = "12.4",
        // The operator's ruling of 2026-09-25, built as 12.6's correction.
        ["Filter history"] = "12.6",
        // The operator's ruling of 2026-09-26, built as 12.2's correction.
        ["History pull"] = "12.2",
        // The operator's rulings of 2026-09-29, built as 12.5's correction.
        ["Sweep history"] = "12.5",
        // The operator's rulings of 2026-09-30, the sweep's rerun, built as 12.5's correction.
        ["Sweep point in time"] = "12.5",
    };

    static readonly Dictionary<string, string> Stores = new(StringComparer.Ordinal)
    {
        // One row over six tables, and it is owed where the last of them
        // arrives rather than where the first does. The move annotator at 5.2
        // is that point; indicators land at 3.1 and ladders at 4.2.
        ["Indicators, swings, volume profile, levels, ladders, moves"] = "5.2",

        // A store of its own from 11.6, one row per name rather than a year of them.
        ["Peer readings"] = "11.6",

        // And 11.7's, one row per name and print over the calendar's year behind.
        ["Earnings reactions"] = "11.7",

        // 12.2's versions, which the plan names by the table rather than the store.
        ["Filter versions"] = "12.2",

        // The history pull's two tables, built as 12.2's correction.
        ["Pulled history"] = "12.2",

        // 12.4's proposals, which the plan names by the table.
        ["Shape proposals"] = "12.4",
        // 12.6's store, the rule each evening's list was drawn by.
        ["List rules"] = "12.6",

        // 12.1's two, one row per member per night and one per night.
        ["Swing readings"] = "12.1",
        ["Market readings"] = "12.1",

        // The fundamentals item's asks and readings, a 12.2 correction, which the plan names by what they do.
        ["Quarter asks"] = "12.2",
        ["Fundamental readings"] = "12.2",
        // 12.6's correction, the news labeller's two stores; the labeller itself derives from the plan.
        ["News articles"] = "12.6",
        ["News labels"] = "12.6",
        // 13.1's two, the sessions the families drew and the page's list they drew.
        ["Family nights"] = "13.1",
        ["Family picks"] = "13.1",
        // 13.2's, every member's answer under each family but the pullback.
        ["Family results"] = "13.2",
    };

    // Where a screen row is complete, not where its first pixel appears. Naming
    // a later point than strictly needed says only that nothing asserts it yet,
    // which is true; naming an earlier one would be a claim that something does.
    //
    // Keyed on the table and the row together, which is contradiction D. Keyed
    // on the table heading alone, every row of a section shared one due point,
    // so "The exported report" was owed at the chart checkpoint because it sits
    // in the same two-row table as "The app". A phase 6 surface asserted at 1.3
    // is a claim that fails the day 1.3 lands, and the row it fails on is not
    // the row anybody was working on.
    //
    // The rows that do not simply inherit their table's point are the six the
    // plan names at a checkpoint of their own, and every one of them moves the
    // point later rather than earlier. That direction is the whole safety
    // argument: a late due point can only delay a claim.
    //
    // This map stays written rather than derived, and the reason is one reason
    // rather than thirty-seven. Section 15's rows are screen elements headed in
    // ordinary English, "The table", "The chart", "Filters", "Walk", so a
    // whole-word search of the plan's prose finds fifteen of them by coincidence
    // and none of them by naming. A due point derived from a prose coincidence
    // is worse than one written down, because it looks derived. What the harness
    // asserts instead is that this map and section 15 hold the same rows in both
    // directions, so a row added to the document with no entry here fails, and
    // an entry naming a row the document no longer has fails too.
    static readonly Dictionary<string, string> Screens = new(StringComparer.Ordinal)
    {
        // The parts of the rows the fifth phase 5 sign-off review decomposed.
        [CheckReach.Key("15.4 The two surfaces", "The app, the single page")] = "1.3",
        [CheckReach.Key("15.4 The two surfaces", "The app, routing")] = "1.3",
        [CheckReach.Key("15.4 The two surfaces", "The app, filters")] = "5.1",
        [CheckReach.Key("15.4 The two surfaces", "The app, selection")] = "5.8",
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, Everything above the marker is a sale")] = "4.6",
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, everything below is a purchase")] = "4.6",
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, stops are horizontal rules")] = "4.6",
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column, the invalidation is the lowest one")] = "4.6",
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row, A name's close between its nearest support and its nearest resistance")] = "5.1",
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row, sized for a table cell")] = "5.1",
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row, the distances in typical days")] = "5.1",
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track, setups resolved as a win")] = "5.6",
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track, resolved as a loss")] = "5.6",
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track, unresolved")] = "5.6",
        [CheckReach.Key("15.7 Tonight", "Night header, names in the index")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Night header, names that fired")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Night header, reports carrying fresh prose against reused")] = "6.8",
        [CheckReach.Key("15.7 Tonight", "Night header, spend")] = "6.7",
        [CheckReach.Key("15.7 Tonight", "Night header, run duration")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Night header, the harness verdict")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "An evening before the switch, one row per name that fired")] = "5.4",
        // 10.1 changed the tiebreak to the plan's reward to risk and drew it on the row.
        [CheckReach.Key("15.7 Tonight", "An evening before the switch, ordered by how many fired then by the plan's reward to risk")] = "10.1",
        [CheckReach.Key("15.7 Tonight", "The list, the reward to risk or the plan's reason for none")] = "10.1",
        [CheckReach.Key("15.7 Tonight", "The list, at most twenty drawn")] = "5.4",
        // Two parts a 5.8 correction adds, at the checkpoint that states the screens' parts.
        [CheckReach.Key("15.7 Tonight", "The list, each numbered by its place in that order")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, a line above them stating how many are drawn of how many are listed")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, name")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "The list, close")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "The list, day change")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, trend state in a word")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, the distance row mark")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, the reasons as context")] = "5.4",

        // The parts the 7.0 ruling adds, owed at 7.0, which the ruling belongs to and which
        // the operator ruled ahead of the pass that plans the phase.
        [CheckReach.Key("15.7 Tonight", "The list, beside the name a line saying so where its prices may not reflect a dividend or split")] = "7.0",
        [CheckReach.Key("15.9 Name", "Prices may be out of date, one line saying its prices may not reflect a recent dividend or split")] = "7.0",
        [CheckReach.Key("15.9 Name", "Prices may be out of date, when the refetch was last tried and why it failed")] = "7.0",
        [CheckReach.Key("15.9 Name", "Prices may be out of date, above everything the page draws from those prices")] = "7.0",
        [CheckReach.Key("15.7 Tonight", "Selected name, the plan column")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Selected name, the level summary")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "Selected name, whichever row is selected")] = "5.8",
        [CheckReach.Key("15.8 Universe", "Sector strip, one line per sector")] = "5.1",
        [CheckReach.Key("15.8 Universe", "Sector strip, how many are on tonight's list")] = "5.4",
        [CheckReach.Key("15.8 Universe", "Sector strip, names")] = "5.1",
        [CheckReach.Key("15.8 Universe", "Sector strip, how many are in an uptrend")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, every name in the index")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, the listing strip over sixty sessions")] = "5.4",
        [CheckReach.Key("15.8 Universe", "The table, each ticker a link to its own page that draws its year line while the pointer is over it")] = "5.8",
        [CheckReach.Key("15.8 Universe", "The table, each column heading saying what its column holds while the pointer is over it")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, each column heading saying what its column holds while the pointer is over it")] = "5.8",
        [CheckReach.Key("15.9 Name", "Peers, each column heading saying what its column holds while the pointer is over it")] = "5.8",
        [CheckReach.Key("15.17 Past picks", "Every trade, each column heading saying what its column holds while the pointer is over it")] = "5.8",
        [CheckReach.Key("15.8 Universe", "The table, sorted by distance to the nearest level ascending")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, paged")] = "5.8",
        [CheckReach.Key("15.8 Universe", "The table, name")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, sector")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, close")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, trend state")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, the distance row mark")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, sessions until earnings")] = "5.8",
        [CheckReach.Key("15.8 Universe", "The table, the evening last on the list")] = "5.4",
        [CheckReach.Key("15.8 Universe", "Researched")] = "5.8",
        [CheckReach.Key("15.9 Name", "The numbers snapshot")] = "5.8",
        [CheckReach.Key("15.9 Name", "Regenerate Report")] = "5.8",
        [CheckReach.Key("15.9 Name", "A pass as it runs")] = "5.8",
        // The fact strip, whole at 6.1. Five of its seven parts existed from
        // phase 3 and two did not, and the row is one claim per part rather than
        // one for the row, so the five could not pass while the two were absent.
        [CheckReach.Key("15.9 Name", "Fact strip, close")] = "6.1",
        [CheckReach.Key("15.9 Name", "Fact strip, market capitalisation")] = "6.1",
        [CheckReach.Key("15.9 Name", "Fact strip, the high and low of the move")] = "6.1",
        [CheckReach.Key("15.9 Name", "Fact strip, next earnings date")] = "6.1",
        [CheckReach.Key("15.9 Name", "Fact strip, the multiples")] = "6.1",
        [CheckReach.Key("15.9 Name", "Fact strip, the averages")] = "6.1",
        [CheckReach.Key("15.9 Name", "Fact strip, momentum and the typical daily move")] = "6.1",
        [CheckReach.Key("15.9 Name", "The chart, the level chart")] = "4.1",
        [CheckReach.Key("15.9 Name", "The chart, the volume profile beside it on the same price axis")] = "3.3",
        [CheckReach.Key("15.9 Name", "The chart, the momentum panel beneath")] = "3.5",
        [CheckReach.Key("15.9 Name", "The chart, the level summary table with each band's members and dates")] = "3.4",
        [CheckReach.Key("15.9 Name", "The plan, the plan column mark")] = "4.6",
        [CheckReach.Key("15.9 Name", "The plan, the tranche table with conditions and stops")] = "4.6",
        [CheckReach.Key("15.9 Name", "The plan, the exit table with actions")] = "4.6",
        [CheckReach.Key("15.9 Name", "The plan, the earnings setups")] = "4.6",
        [CheckReach.Key("15.9 Name", "The plan, the sizing arithmetic")] = "4.6",
        [CheckReach.Key("15.9 Name", "How it got here, the table of the biggest moves")] = "5.2",
        [CheckReach.Key("15.9 Name", "How it got here, the cause of each where research has been written")] = "6.6",
        [CheckReach.Key("15.9 Name", "How it got here, the twelve-month picture")] = "5.8",
        // 8.5, the reason verdicts. Section 15.10's two figures, and section
        // 15.11's four rows with the four parts the second of them enumerates.
        [CheckReach.Key("15.10 Run", "Reason records, the share that reached target before stop")] = "8.5",
        [CheckReach.Key("15.10 Run", "Reason records, the break-even those setups demanded")] = "8.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "Below the minimum")] = "8.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the share that reached the target before the stop")] = "8.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the number resolved")] = "8.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, the break-even those setups demanded")] = "8.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum, always the three together")] = "8.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "Unresolved setups")] = "8.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "Never shown")] = "8.5",
        // 8.4, the shadow candidates row's three parts. Each carries its own
        // point rather than inheriting the row's, which is what contradiction D
        // was: a part added to the document with no point of its own is a claim
        // nobody decided when it is owed.
        [CheckReach.Key("15.10 Run", "Shadow candidates, how many candidate conditions are registered")] = "8.4",
        [CheckReach.Key("15.10 Run", "Shadow candidates, the family's divisor that number sets")] = "8.4",
        [CheckReach.Key("15.10 Run", "Shadow candidates, the count of distinct trials and the level each starts at beside it")] = "12.5",
        [CheckReach.Key("15.10 Run", "Shadow candidates, one line saying each candidate's record is withheld until it is promoted")] = "8.4",
        [CheckReach.Key("15.10 Run", "Operational header, what ran")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, the instant each stage started and how long it took")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, model calls")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, network requests")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, spend")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, what each stage said about itself")] = "5.6",
        [CheckReach.Key("15.10 Run", "Reason records, the resolved count")] = "5.6",
        [CheckReach.Key("15.10 Run", "Reason records, the never-entered count")] = "8.1",
        [CheckReach.Key("15.10 Run", "Reason records, one row per reason with the reason track mark")] = "5.6",
        [CheckReach.Key("15.10 Run", "Harness, passed")] = "5.6",
        [CheckReach.Key("15.10 Run", "Harness, failed")] = "5.6",
        [CheckReach.Key("15.10 Run", "Harness, unexamined")] = "5.6",
        // Contradiction D's own case. The exporter is a phase 6 component and
        // this row is the surface it writes.
        [CheckReach.Key("15.4 The two surfaces", "The exported report")] = "6.11",

        // Contradiction F. This row names four elements and they are drawn at
        // three different points, so the row is read as four claims. See
        // Elements below for why the decomposition is here and not in the
        // document.
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, candles")] = "1.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, a volume pane")] = "1.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, the moving averages")] = "3.1",
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, the level bands")] = "3.4",

        [CheckReach.Key("15.5 The mark vocabulary", "Volume profile")] = "3.3",
        // 4.4 is "The plan column mark and the tables", so this mark is owed a
        // phase later than the section it sits in.
        [CheckReach.Key("15.5 The mark vocabulary", "Momentum panel")] = "3.5",
        // Drawn from levels, which arrive at 3.4, and used by the universe
        // and tonight screens. 5.1 is where the first of those exists, and a
        // mark with no screen to sit on is a mark nothing can be asserted about.
        // Both need a listing and a reason behind them, which phase 5 is the
        // first to write.
        [CheckReach.Key("15.5 The mark vocabulary", "Listing strip")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Watch list")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Reasons, per row")] = "5.6",
        [CheckReach.Key("15.7 Tonight", "Reason totals")] = "5.6",

        // Phase 9's rows, placed at the checkpoints that draw them so each reads
        // as out of scope rather than unexamined until it lands. The request
        // store they rest on is declared at 9.2 with the components that write
        // it, rather than here ahead of them.
        [CheckReach.Key("15.7 Tonight", "Research, per row")] = "9.1",
        // The ask writes a request, and the store it writes to is 9.2's, so the row
        // comes into scope with the thing behind it rather than with the screen.
        [CheckReach.Key("15.7 Tonight", "Ask for a report")] = "9.2",
        [CheckReach.Key("15.15 Queue", "Outstanding")] = "9.3",
        [CheckReach.Key("15.15 Queue", "Being written")] = "9.3",
        [CheckReach.Key("15.15 Queue", "Settled")] = "9.3",
        [CheckReach.Key("15.15 Queue", "Take it out")] = "9.3",
        [CheckReach.Key("15.16 Watch list", "Add a name, a box offering the names of the index")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Add a name, which adds one while the list holds fewer than twenty and otherwise gives way to a line saying the limit is reached")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Add a name, the same press beside a name on its own page")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Add a name, a refusal with the line saying why for a name not of the index tonight or already watched or a twenty-first")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Each name watched, in the order it was added")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Each name watched, its company")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Each name watched, its close and the day's change")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Each name watched, its trend in a word")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Each name watched, the reward to risk of its trade")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Each name watched, the day it was added and a link to its page")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Each name watched, drawn for the newest night whether or not the list holds it")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, listed and its number")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, the first gate that stopped it with that gate's reason")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, what excluded it")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it, that no answer is stored for the night")] = "5.8",
        [CheckReach.Key("15.16 Watch list", "Take it out")] = "5.8",
        // The 12.2 correction's Past picks screen, the name page's region and the eighth mark.
        [CheckReach.Key("15.5 The mark vocabulary", "Trade line")] = "12.2",
        [CheckReach.Key("15.5 The mark vocabulary", "Year line")] = "11.6",
        [CheckReach.Key("15.10 Run", "How last night went, a status mark and a headline naming the night's state as its own run log rows give it")] = "12.3",
        [CheckReach.Key("15.10 Run", "How last night went, four headline figures being the stocks read and the provider requests with the research spend and the steps run again")] = "12.3",
        [CheckReach.Key("15.10 Run", "How last night went, a time bar of the night's steps in six named groups showing where a stopped night stopped")] = "12.3",
        [CheckReach.Key("15.10 Run", "The market, a one-word label read by a stated rule")] = "12.3",
        [CheckReach.Key("15.10 Run", "The market, a gauge of the share of members above their 200-day average with the market gate's floor marked")] = "12.3",
        [CheckReach.Key("15.10 Run", "The market, the share above the 50-day average and the index's volume against its fifty-day average")] = "12.3",
        [CheckReach.Key("15.10 Run", "The market, a line of that share over the sixty sessions before the night that the store holds averages for")] = "12.3",
        [CheckReach.Key("15.10 Run", "From the index to tonight's list, a funnel of how many members passed each of the filter's checks in turn down to those left after the exclusions")] = "12.3",
        [CheckReach.Key("15.10 Run", "From the index to tonight's list, the count listed with a link opening tonight's list")] = "12.3",
        [CheckReach.Key("15.10 Run", "The detail")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Night status")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Market gauge")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Breadth line")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Funnel bars")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the list's trades are going, the live list's trades still open and those that reached the target or were stopped out or ran out of time")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the list's trades are going, a ring of the trades decided against the minimum a share waits on")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the list's trades are going, the share and the break-even and the average result together once both minimums are met as Past picks draws them")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the list's trades are going, a link opening Past picks")] = "12.3",
        [CheckReach.Key("15.10 Run", "Is the list finding new stocks, a bar for each of the last twenty evenings split into the names new that evening and those the evening before also listed")] = "12.3",
        [CheckReach.Key("15.10 Run", "Is the list finding new stocks, the night's split in words")] = "12.3",
        [CheckReach.Key("15.10 Run", "Research and spend, the month's spend against the month cap")] = "12.3",
        [CheckReach.Key("15.10 Run", "Research and spend, the reports the paid model wrote on each of the last seven nights")] = "12.3",
        [CheckReach.Key("15.10 Run", "Research and spend, the reports and the overnight drafts written over those nights")] = "12.3",
        [CheckReach.Key("15.10 Run", "How each report did, one row per report over the seven nights with its stock and day and what it cost")] = "12.6",
        [CheckReach.Key("15.10 Run", "How each report did, a cell per section saying whether it passed first time or on retry or was left out with why or was not warranted, with what its own calls cost and none of a trial's")] = "12.6",
        [CheckReach.Key("15.10 Run", "How each report did, the two cases' cell marked where a draft of the pass carried a figure on both sides, with how many of the newest twenty reports' two cases did")] = "12.6",
        [CheckReach.Key("15.10 Run", "How each report did, each section's share passed first time and its share left out over the newest twenty reports that warranted it")] = "12.6",
        [CheckReach.Key("15.10 Run", "Anything to worry about, a checklist of plain items each turning red with its reason where it fails")] = "12.3",
        [CheckReach.Key("15.10 Run", "Anything to worry about, every stock holding the night's prices and every step of the night finished")] = "12.3",
        [CheckReach.Key("15.10 Run", "Anything to worry about, no research document refused and no section fallen back")] = "12.3",
        [CheckReach.Key("15.10 Run", "Anything to worry about, every company awaiting a quarter asked on schedule")] = "12.3",
        [CheckReach.Key("15.10 Run", "Anything to worry about, the four harness counts beneath")] = "12.3",
        [CheckReach.Key("15.10 Run", "Anything to worry about, no paid model a job uses within thirty days of its retirement date, and where one is its profile named with its job and date")] = "12.6",
        [CheckReach.Key("15.10 Run", "Anything to worry about, no report whose pass ran on the night's session costing more than section 17 names, and where one did its stock named with its cost")] = "12.6",
        [CheckReach.Key("15.5 The mark vocabulary", "Trades ring")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Freshness bars")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Research bars")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Checklist")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the system learns, the shape clock as the ordinary nights under the open filter version against the sixty its calibration waits on")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the system learns, each check's typical pass count against its range drawn dashed until measured")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the system learns, the edge clock as a line from today to its two checkpoints with the blocks the live list has gathered")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the system learns, each version running beside the live list with what it changes and the stocks it has picked and the share the live list also picked and its blocks against the floor")] = "12.3",
        [CheckReach.Key("15.10 Run", "How the system learns, a link comparing tonight's picks")] = "12.3",
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, a choice of the versions running beside the live list kept in the link")] = "12.3",
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, the names only the live list picked and those both picked and those only the version picked with the setting that made the difference beside each name only one side picked")] = "12.3",
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, two overlapping rings holding the three counts")] = "12.3",
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, the version's picks over the last twenty evenings with the share the live list also picked and the evenings it picked a stock the live list did not")] = "12.3",
        [CheckReach.Key("15.10 Run", "Compare tonight's picks, picks alone with no figure from how a pick turned out")] = "12.3",
        [CheckReach.Key("15.10 Run", "At a checkpoint, one row per version on a scale from nought to a hundred")] = "12.3",
        [CheckReach.Key("15.10 Run", "At a checkpoint, before its first look a locked dashed outline with its trades and blocks so far")] = "12.3",
        [CheckReach.Key("15.10 Run", "At a checkpoint, from its first look the share of its trades that reached the target with the break-even they needed and what no skill scored from the same starts and how far luck alone could move it")] = "12.3",
        [CheckReach.Key("15.10 Run", "At a checkpoint, the verdict in words")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Clock lines")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Overlap rings")] = "12.3",
        [CheckReach.Key("15.5 The mark vocabulary", "Checkpoint scale")] = "12.3",
        [CheckReach.Key("15.7 Tonight", "The night's state, a notice at the top naming the night's state as the Run page's headline names it")] = "12.3",
        [CheckReach.Key("15.7 Tonight", "The night's state, each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished")] = "12.3",
        [CheckReach.Key("15.7 Tonight", "The night's state, a press running the rest of a night left unfinished")] = "12.3",
        [CheckReach.Key("15.7 Tonight", "The night's state, a one-line note where the night finished")] = "12.3",
        [CheckReach.Key("15.10 Run", "How last night went, each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished")] = "12.3",
        [CheckReach.Key("15.10 Run", "How last night went, a press running the rest of a night left unfinished")] = "12.3",
        [CheckReach.Key("15.7 Tonight", "The night's state, a refusal before its first step with its reason")] = "12.3",
        [CheckReach.Key("15.10 Run", "How last night went, the commit the night was built from")] = "12.3",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, one row per member whose stored result missed exactly one of the five gates and carries no exclusion")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, nearest to qualifying first with a tie in the list's own order")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, drawing the places the list leaves of the twenty")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, a line above them stating how many are drawn of how many are one gate short")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, what a row of the list carries")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, the gate it missed in place of the gates")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, what it had against the bar it needed in plain words")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, how far short that is as a share of the bar")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, the trade its plan states")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, one line above the rows stated once saying they would qualify if the market turned")] = "12.7",
        [CheckReach.Key("15.7 Tonight", "Close to a buy point, a key saying it recommends nothing")] = "12.7",

        // 13.1, a family's card. The label and the note a second family brings are drawn once breakouts land.
        [CheckReach.Key("15.7 Tonight", "A family's card, one card a family in the page's order")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, the family's rule in a sentence")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, the day its rule went live or the words saying it is provisional")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, how many it lists tonight with how many variants are scored beside it")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, its place down the page")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, the stock with a label for each other family it qualified under")] = "13.2",
        [CheckReach.Key("15.7 Tonight", "A family's card, the state its reported quarters give it")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, the buy and the stop and the target")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, where the buy sits between the stop and the target")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, the reward to risk")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, why it is listed tonight in the figures its family stored")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, the positive and negative stories of the thirty days before")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, a note for each stock it passed that a trade still open holds back")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, a note for each stock the page lists under an earlier family")] = "13.2",
        [CheckReach.Key("15.7 Tonight", "A family's card, a note counting the stocks past its five")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, a line saying why where it lists none")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "A family's card, a key saying how to read it")] = "13.1",
        [CheckReach.Key("15.7 Tonight", "Still open, one row per stock that passed every gate on the night with nothing excluding it but an open trade while a trade the live list recommended for it on an earlier night is still open on this one")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, the night that trade was listed on")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, the trade line")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, where the price stands against that trade's stop and target")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, whether the trade ended at this night's own close and frees the stock from the next night")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, before the rule reaches the filter the stock stands on the list as well and is marked as listed again while that trade is open")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, once the rule reaches the filter the stock is excluded there and drawn here alone")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, a line where there is none")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "Still open, a key saying it is not a new trade")] = "12.2",
        [CheckReach.Key("15.9 Name", "On the list before")] = "12.2",
        // 12.6's correction drawing the news on the name page.
        [CheckReach.Key("15.9 Name", "News")] = "12.6",
        [CheckReach.Key("15.7 Tonight", "The list, the positive and negative stories of the thirty days before the night as the name page's bar counts them")] = "12.6",
        [CheckReach.Key("15.10 Run", "Research and spend, the news labeller's line with what the night's labelling cost and the month's against its limit and the articles labelled and the unreadable answers by cause and what stopped it")] = "12.6",
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights the news labeller ran against twenty with its measured duration and its month's spend and the share of answers refused for a digit")] = "12.6",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, the trades listed with the nights they were listed on")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, how many are still open and how many finished")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, how many reached the target and how many were stopped out or ran out of time")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, a line saying how many carry no outcome row")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, below the minimum a dashed outline stating the trades decided at the target or the stop and their listing nights against the numbers needed with no share")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, at or above it a bar of the finished trades in three steps of one neutral hue with a line at the decided trades' average break-even")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, the share that reached the target first beside the share needed to break even and the average result in multiples of the risk taken, the three always together")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, a key saying how to read it")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done, a line saying how many were listed again while an earlier trade was open, drawn and not counted")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Filters, a chip for all and one for each status a trade can stand in with its count")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Filters, none by setup")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, newest first")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, a line above the rows stating how many are shown of how many were listed")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the night listed")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the stock with a link to its page for that night")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the buy and the stop and the target")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the trade line")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the status in words")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, a repeat listing made while the trade from an earlier night was open marked as listed again and counted once")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the sessions held to its resolution or to the newest night")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the result as a signed multiple of the risk or open")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, a key saying how to read the trade line and that only the live list's trades appear with the alternatives hidden until one is promoted")] = "12.2",
        // The fundamentals item's screens, a 12.2 correction, each part owed where it is drawn.
        [CheckReach.Key("15.7 Tonight", "The list, the state-first order from the first night whose readings are stored and a night before it in the filter's own order")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "The list, the state its reported quarters give it beside the trend")] = "12.2",
        [CheckReach.Key("15.7 Tonight", "The list, what the numbers say while the state is under the pointer or holds focus")] = "12.2",
        [CheckReach.Key("15.9 Name", "What the numbers say, a heading carrying the state and the quarter it was read from")] = "12.2",
        [CheckReach.Key("15.9 Name", "What the numbers say, one sentence per reading written from section 4's patterns")] = "12.2",
        [CheckReach.Key("15.9 Name", "What the numbers say, a sentence saying so where a reading is absent or read over too few quarters")] = "12.2",
        [CheckReach.Key("15.9 Name", "What the numbers say, the quarters the readings read with the dates each was filed and reported on")] = "12.2",
        [CheckReach.Key("15.9 Name", "What the numbers say, the full numbers table folded beneath them")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, within a night the order that night's list was drawn in, improving businesses first where it stored readings and the filter's own order where it stored none")] = "12.2",
        [CheckReach.Key("15.17 Past picks", "Every trade, the state its reported quarters gave it on the night it was listed, or not read that night")] = "12.2",
        [CheckReach.Key("15.15 Queue", "Which lane would write one")] = "9.4",
        [CheckReach.Key("15.15 Queue", "When each will be written")] = "11.2",
        [CheckReach.Key("15.7 Tonight", "The report's state")] = "11.3",
        [CheckReach.Key("15.8 Universe", "Filters")] = "5.1",

        [CheckReach.Key("15.9 Name", "Why it is here")] = "5.4",
        [CheckReach.Key("15.9 Name", "The short version")] = "6.8",
        [CheckReach.Key("15.9 Name", "What it sells, the numbers, the cycle, the two cases, the risks")] = "6.8",
        [CheckReach.Key("15.9 Name", "Dates and sources")] = "6.8",
        // The lines were drawn from 6.5 and nothing in section 15 claimed them until the
        // 5.8 correction that stopped them carrying the draft a checker refused.
        [CheckReach.Key("15.9 Name", "The case for and the case against")] = "5.8",
        [CheckReach.Key("15.9 Name", "How far each band is")] = "5.8",
        [CheckReach.Key("15.9 Name", "Level evidence")] = "3.4",
        [CheckReach.Key("15.9 Name", "The risks as parts")] = "5.8",
        [CheckReach.Key("15.9 Name", "Contents")] = "5.8",
        [CheckReach.Key("15.9 Name", "Sections left out")] = "6.5",
        // The four parts phase 11's document pass wrote before any of them is drawn, each
        // owed at the checkpoint that builds it and out of scope until that one lands.
        [CheckReach.Key("15.9 Name", "Each move beside its group")] = "11.5",
        // 12.3's Calibration region, each part owed where it is drawn.
        [CheckReach.Key("15.10 Run", "The shape clock, the ordinary nights under the open filter version against the sixty the calibration waits on")] = "12.3",
        [CheckReach.Key("15.10 Run", "The shape clock, every event night with what made it one")] = "12.3",
        [CheckReach.Key("15.10 Run", "The shape clock, each gate's median count through it against its band")] = "12.3",
        [CheckReach.Key("15.10 Run", "The shape clock, the list's median size against its band")] = "12.3",
        [CheckReach.Key("15.10 Run", "The shape clock, drawn as not yet measured until the trigger")] = "12.3",
        [CheckReach.Key("15.10 Run", "The shape clock, said at the top of the page once the trigger is crossed")] = "12.3",
        [CheckReach.Key("15.10 Run", "The shape clock, each reason's share of the index as context")] = "12.3",
        [CheckReach.Key("15.10 Run", "What the two clocks can do")] = "12.3",
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights run for the session the clock fell on against five")] = "12.3",
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the research passes carrying a recorded cost against twenty")] = "12.3",
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights the version step replayed both kinds against five")] = "12.3",
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the resolved event-book setups against 250")] = "12.3",
        [CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights of trend labels under the third trend version against sixty")] = "12.3",
        // 12.4's proposal, each part owed where it is drawn.
        [CheckReach.Key("15.10 Run", "The shape proposal, each gate's setting held and proposed with its median count and the list's under each against their bands")] = "12.4",
        [CheckReach.Key("15.10 Run", "The shape proposal, every gate no value in its range brings inside its band named as a finding")] = "12.4",
        [CheckReach.Key("15.10 Run", "The shape proposal, beside it the non-empty blocks accepting it would restart")] = "12.4",

        // 12.6's switch, each part owed where it is drawn.
        [CheckReach.Key("15.7 Tonight", "Night header, names the swing filter listed")] = "12.6",
        [CheckReach.Key("15.7 Tonight", "The list, one row per name on the list")] = "12.6",
        [CheckReach.Key("15.7 Tonight", "The list, ordered by the state its reported quarters give it and within a state by the swing filter's reward to risk then strength then band strength")] = "12.6",
        [CheckReach.Key("15.7 Tonight", "The list, a line naming the rule that listed the evening")] = "12.6",
        [CheckReach.Key("15.7 Tonight", "The list, the gates with the values that decided them")] = "12.6",
        [CheckReach.Key("15.7 Tonight", "An evening before the switch, drawn as it was listed")] = "12.6",
        [CheckReach.Key("15.10 Run", "The list from night to night, how many of the night's names were on the list the evening before")] = "12.6",
        [CheckReach.Key("15.10 Run", "The list from night to night, how many at least once over the five evenings before")] = "12.6",
        [CheckReach.Key("15.10 Run", "The list from night to night, how many over the twenty evenings before")] = "12.6",
        // 12.7's edge half and near misses, each part owed where it is drawn.
        [CheckReach.Key("15.10 Run", "The edge clock, each swing family candidate's non-empty blocks against the 8 its first look is read at")] = "12.7",
        [CheckReach.Key("15.10 Run", "The edge clock, its resolved setups")] = "12.7",
        [CheckReach.Key("15.10 Run", "The edge clock, its share against its planned break-even and calibrated null from the floor on and nothing below it")] = "12.7",
        [CheckReach.Key("15.10 Run", "The edge clock, that the first look can retire or leave a candidate and never promote one")] = "12.7",
        [CheckReach.Key("15.10 Run", "The edge clock, when the earliest promotion can come")] = "12.7",
        [CheckReach.Key("15.10 Run", "The edge clock, each variant with the filter version it was defined against and the settings the live filter has since moved")] = "12.7",
        [CheckReach.Key("15.10 Run", "Near misses, the setups the filter admitted")] = "12.7",
        [CheckReach.Key("15.10 Run", "Near misses, beside each gate and each exclusion the setups it alone rejected with every other gate passing")] = "12.7",
        [CheckReach.Key("15.10 Run", "Near misses, each group against its own planned break-even and calibrated null")] = "12.7",
        [CheckReach.Key("15.10 Run", "Near misses, every figure withheld below the block floor")] = "12.7",
        // 12.2's gates and funnel, each part owed where it is drawn.
        [CheckReach.Key("15.9 Name", "Gates, each of the five gates with whether it passed and why")] = "12.2",
        [CheckReach.Key("15.9 Name", "Gates, the setup's family and whether the trigger's event happened")] = "12.2",
        [CheckReach.Key("15.9 Name", "Gates, the trade read from the ladder's first tranche and from the one swing plan that night's live rule used with that plan marked and no candidate's plan drawn whichever filter version the night ran under")] = "12.2",
        [CheckReach.Key("15.9 Name", "Gates, the exclusions with a key saying how to read it")] = "12.2",
        [CheckReach.Key("15.10 Run", "Swing filter funnel, how many members each gate passed in order and how many it removed")] = "12.2",
        [CheckReach.Key("15.10 Run", "Swing filter funnel, the setup's families on a night that held a breakout")] = "12.2",
        [CheckReach.Key("15.10 Run", "Swing filter funnel, what each exclusion removed and how many pass")] = "12.2",
        [CheckReach.Key("15.10 Run", "Swing filter funnel, the version the night ran under")] = "12.2",
        // 12.1's readings, each part owed where it is drawn.
        [CheckReach.Key("15.7 Tonight", "Night header, the night's breadth with the share above the 50-day average beside it as context")] = "12.1",
        [CheckReach.Key("15.8 Universe", "The table, relative strength")] = "12.1",
        [CheckReach.Key("15.8 Universe", "The table, the pullback from the recent high in typical days")] = "12.1",
        [CheckReach.Key("15.8 Universe", "The table, the volume while it came down")] = "12.1",
        [CheckReach.Key("15.8 Universe", "The table, the tightness of the range")] = "12.1",
        [CheckReach.Key("15.9 Name", "Swing readings, the return over 63 sessions and over 126 sessions with each one's place among the members' returns")] = "12.1",
        [CheckReach.Key("15.9 Name", "Swing readings, the highest high of the last 20 sessions and how far the close sits below it in typical days' moves")] = "12.1",
        [CheckReach.Key("15.9 Name", "Swing readings, the median volume of the sessions since that high against the fifty-day average")] = "12.1",
        [CheckReach.Key("15.9 Name", "Swing readings, the mean true range of the last ten sessions against the last fifty")] = "12.1",
        [CheckReach.Key("15.9 Name", "Swing readings, a key saying how to read it")] = "12.1",
        [CheckReach.Key("15.10 Run", "Market reading, the night's breadth with how many members it was counted over")] = "12.1",
        [CheckReach.Key("15.10 Run", "Market reading, the share above the 50-day average beside it as context")] = "12.1",
        [CheckReach.Key("15.10 Run", "Market reading, the index's median volume against its fifty-day average")] = "12.1",
        // 11.6 read the Peers row as its parts, each owed where the table is drawn.
        [CheckReach.Key("15.9 Name", "Peers, beneath the table of the biggest moves")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, the name's own row marked and then at most ten members of its group by price alone")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, those sharing its industry first and then by how closely each one's daily moves followed the name's")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, each row carrying how closely it moved with the name")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, whether it shares the name's industry")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, the close")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, each ticker a link to its own page that draws its year line while the pointer is over it")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, how far it sits below the stored year's high")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, its return over sixty sessions")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, its trend state")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, the distance row mark")] = "11.6",
        [CheckReach.Key("15.9 Name", "Peers, a key saying how to read it")] = "11.6",
        // 11.7 read the Earnings reactions row as its parts, each owed where the record is drawn.
        [CheckReach.Key("15.9 Name", "Earnings reactions, beside the earnings setups")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, one row per print over the calendar's year behind")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, each row carrying its report date")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, its timing")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, the session it moved on")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, the estimate")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, the actual")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, the provider's surprise")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, that session's move")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, a print with no filed estimate saying none was filed")] = "11.7",
        [CheckReach.Key("15.9 Name", "Earnings reactions, a key saying how to read it")] = "11.7",
        // 11.8 read the Dividend row as its parts, each owed where the lines are drawn.
        [CheckReach.Key("15.9 Name", "Dividend, in the numbers section")] = "11.8",
        [CheckReach.Key("15.9 Name", "Dividend, from the newest filing alone")] = "11.8",
        [CheckReach.Key("15.9 Name", "Dividend, the forward annual rate")] = "11.8",
        [CheckReach.Key("15.9 Name", "Dividend, the forward yield as the provider states it")] = "11.8",
        [CheckReach.Key("15.9 Name", "Dividend, the payout ratio")] = "11.8",
        [CheckReach.Key("15.9 Name", "Dividend, the ex-dividend date and the pay date")] = "11.8",
        [CheckReach.Key("15.9 Name", "Dividend, nothing where the provider files none")] = "11.8",
        // Decomposed at 6.5, each into the part the judge's verdict draws and the
        // parts the research runner draws. The line saying research is missing or
        // naming the trigger that fired is a reading of the stores this checkpoint
        // judges; a control that writes research with its cost stated, and the stored
        // sections rendered with their dates, need the runner and the prose it
        // writes, which arrive at 6.8.
        [CheckReach.Key("15.9 Name", "Research not yet written, the researched sections absent with one line saying they have not been written")] = "6.5",
        [CheckReach.Key("15.9 Name", "Research not yet written, a control that writes them with its cost stated before it is pressed")] = "6.8",
        [CheckReach.Key("15.9 Name", "Research not yet written, beside the computed sections rendered whole")] = "6.5",
        [CheckReach.Key("15.9 Name", "Research stale, the stored sections rendered with their own dates")] = "6.8",
        [CheckReach.Key("15.9 Name", "Research stale, one line naming which of the four triggers fired")] = "6.5",
        [CheckReach.Key("15.9 Name", "Research stale, the option to have them rewritten")] = "6.8",
        // Decomposed at 6.7, which draws the line and none of the sections: a written
        // section is drawn under its own date from 6.8, which is where research is first
        // written to be shown.
        [CheckReach.Key("15.9 Name", "Research paused, one line saying that research is paused and when it resumes")] = "6.7",
        [CheckReach.Key("15.9 Name", "Research paused, with the stored sections still rendered under their own dates")] = "6.8",
        [CheckReach.Key("15.9 Name", "Walk")] = "5.4",
        // 8.5 is "Reason verdicts on the run page" and 8.4 is "The shadow
        // column", so two rows of this section are owed three phases after it.
        [CheckReach.Key("15.10 Run", "Stale and failed, names carrying yesterday's bars")] = "5.6",
        [CheckReach.Key("15.10 Run", "Stale and failed, the stage a night stopped on")] = "5.6",
        [CheckReach.Key("15.10 Run", "Stale and failed, sections that fell back")] = "6.4",
        // Still here with the region's other parts although 6.3 reached it. This
        // map is the whole population of section 15's rows and their parts, in
        // both directions, rather than only the ones nothing asserts yet: a part
        // with no entry would inherit nothing, which is what contradiction D was.
        [CheckReach.Key("15.10 Run", "Stale and failed, documents refused by admissibility")] = "6.3",
        [CheckReach.Key("15.10 Run", "Overnight queue")] = "6.10",
        [CheckReach.Key("15.10 Run", "Tonight's order, the three orders of tonight's list over the twenty each would draw")] = "10.1",
        [CheckReach.Key("15.10 Run", "Tonight's order, the old order named as the benchmark")] = "10.1",
        [CheckReach.Key("15.10 Run", "Tonight's order, the setups each order drew")] = "10.1",
        [CheckReach.Key("15.10 Run", "Tonight's order, the setups whose whole window has closed")] = "10.1",
        [CheckReach.Key("15.10 Run", "Tonight's order, the blocks holding one against the floor")] = "10.1",
        [CheckReach.Key("15.10 Run", "Tonight's order, no comparison drawn before every order reaches it")] = "10.1",
        [CheckReach.Key("15.10 Run", "Candidates' records, the verdict field stating what the last look read and what triggers the next")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, the nightly figure beside it labelled as monitoring")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, the Poisson-binomial tail labelled and deciding nothing")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, each look's setups and the share they won against the bar the calibration set")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, the level that look spent and whether it crossed its boundary")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, the smallest excess that look could have detected")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, the design effect and what a loss cost in multiples of the planned risk")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, the setups entered and stopped on one session and those stopped out on a session the name reported on")] = "10.2",
        [CheckReach.Key("15.10 Run", "Candidates' records, the step the graph stands at with its level and the count of distinct trials")] = "12.5",
        [CheckReach.Key("15.10 Run", "Candidates' records, no name anywhere in it")] = "10.2",

    };

    // Contradiction F. Section 15.5's Level chart names four elements, candles,
    // the level bands, the moving averages and a volume pane, and the phase
    // table puts a chart in phase 1 while two of the four cannot exist until
    // phase 2. Resolved per element rather than per mark, so what exists is
    // asserted where it exists and only what does not stays out of scope.
    //
    // The decomposition lives here rather than in the document, and that is
    // deliberate. Section 15.5 opens by stating nine marks and the table has
    // nine rows; splitting the row into four would make the document disagree
    // with itself and would turn one mark into four in a vocabulary whose whole
    // point is that a mark is defined once. So the row stays one row and the
    // harness reads it as four claims.
    //
    // What keeps that from being a second statement of the row's content is
    // that each element phrase is asserted to appear in the row's own
    // description cell. An element renamed in the document, or one invented
    // here, fails. The count in the opening sentence is asserted against the
    // table's rows by stated-counts, so the other repair, adding rows to the
    // table, fails too.
    static readonly Dictionary<string, string[]> Elements = new(StringComparer.Ordinal)
    {
        [CheckReach.Key(LimitsTable, "Frozen measurement windows")] = ["a rule", "threshold"],
        // Every part these rows enumerate, added at the fifth phase 5 sign-off
        // review, which found a row's parts chosen by this reader rather than read
        // off the row: a clause the reader left out had no verdict of its own
        // while its row passed whole, so 15.7's list passed while the page drew
        // neither the day change, nor the trend state, nor the distance mark it
        // states. The parts are now read off the document and every one of them
        // carries a verdict.
        [CheckReach.Key("15.4 The two surfaces", "The app")] =
            ["the single page", "routing", "filters", "selection"],
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column")] =
            ["Everything above the marker is a sale", "everything below is a purchase", "stops are horizontal rules", "the invalidation is the lowest one"],
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row")] =
            ["A name's close between its nearest support and its nearest resistance", "sized for a table cell", "the distances in typical days"],
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track")] =
            ["setups resolved as a win", "resolved as a loss", "unresolved"],
        [CheckReach.Key("15.7 Tonight", "Night header")] =
            ["names in the index", "names the swing filter listed", "names that fired", "the night's breadth with the share above the 50-day average beside it as context", "reports carrying fresh prose against reused", "spend", "run duration", "the harness verdict"],
        [CheckReach.Key("15.7 Tonight", "The list")] =
            ["one row per name on the list", "ordered by the state its reported quarters give it and within a state by the swing filter's reward to risk then strength then band strength", "the state-first order from the first night whose readings are stored and a night before it in the filter's own order", "at most twenty drawn", "each numbered by its place in that order", "a line above them stating how many are drawn of how many are listed", "a line naming the rule that listed the evening", "name", "close", "day change", "trend state in a word", "the state its reported quarters give it beside the trend", "what the numbers say while the state is under the pointer or holds focus", "the distance row mark", "the reward to risk or the plan's reason for none", "the gates with the values that decided them", "the reasons as context", "beside the name a line saying so where its prices may not reflect a dividend or split", "each column heading saying what its column holds while the pointer is over it", "the positive and negative stories of the thirty days before the night as the name page's bar counts them"],
        // 5.8's watch list page, each row's parts as the row states them.
        [CheckReach.Key("15.16 Watch list", "Add a name")] =
            ["a box offering the names of the index", "which adds one while the list holds fewer than twenty and otherwise gives way to a line saying the limit is reached", "the same press beside a name on its own page", "a refusal with the line saying why for a name not of the index tonight or already watched or a twenty-first"],
        [CheckReach.Key("15.16 Watch list", "Each name watched")] =
            ["in the order it was added", "its company", "its close and the day's change", "its trend in a word", "the reward to risk of its trade", "the day it was added and a link to its page", "drawn for the newest night whether or not the list holds it"],
        [CheckReach.Key("15.16 Watch list", "What the swing filter said of it")] =
            ["listed and its number", "the first gate that stopped it with that gate's reason", "what excluded it", "that no answer is stored for the night"],
        // The 12.2 correction's Past picks screen, each row's clauses as the row states them.
        // The 12.2 correction that reads one open trade per stock on the pages: Still open, as the nine parts its
        // row enumerates and states.
        [CheckReach.Key("15.7 Tonight", "Still open")] =
        [
            "one row per stock that passed every gate on the night with nothing excluding it but an open trade while a trade the live list recommended for it on an earlier night is still open on this one",
            "the night that trade was listed on",
            "the trade line",
            "where the price stands against that trade's stop and target",
            "whether the trade ended at this night's own close and frees the stock from the next night",
            "before the rule reaches the filter the stock stands on the list as well and is marked as listed again while that trade is open",
            "once the rule reaches the filter the stock is excluded there and drawn here alone",
            "a line where there is none",
            "a key saying it is not a new trade",
        ],
        // 13.1. A family's card on tonight's page, as the parts its row enumerates and states.
        [CheckReach.Key("15.7 Tonight", "A family's card")] =
        [
            "one card a family in the page's order",
            "the family's rule in a sentence",
            "the day its rule went live or the words saying it is provisional",
            "how many it lists tonight with how many variants are scored beside it",
            "its place down the page",
            "the stock with a label for each other family it qualified under",
            "the state its reported quarters give it",
            "the buy and the stop and the target",
            "where the buy sits between the stop and the target",
            "the reward to risk",
            "why it is listed tonight in the figures its family stored",
            "the positive and negative stories of the thirty days before",
            "a note for each stock it passed that a trade still open holds back",
            "a note for each stock the page lists under an earlier family",
            "a note counting the stocks past its five",
            "a line saying why where it lists none",
            "a key saying how to read it",
        ],
        [CheckReach.Key("15.17 Past picks", "How the list's picks have done")] =
        [
            "the trades listed with the nights they were listed on",
            "how many are still open and how many finished",
            "how many reached the target and how many were stopped out or ran out of time",
            "a line saying how many carry no outcome row",
            "below the minimum a dashed outline stating the trades decided at the target or the stop and their listing nights against the numbers needed with no share",
            "at or above it a bar of the finished trades in three steps of one neutral hue with a line at the decided trades' average break-even",
            "the share that reached the target first beside the share needed to break even and the average result in multiples of the risk taken, the three always together",
            "a line saying how many were listed again while an earlier trade was open, drawn and not counted",
            "a key saying how to read it",
        ],
        [CheckReach.Key("15.17 Past picks", "Filters")] =
            ["a chip for all and one for each status a trade can stand in with its count", "none by setup"],
        [CheckReach.Key("15.17 Past picks", "Every trade")] =
        [
            "newest first",
            "within a night the order that night's list was drawn in, improving businesses first where it stored readings and the filter's own order where it stored none",
            "a line above the rows stating how many are shown of how many were listed",
            "the night listed",
            "the stock with a link to its page for that night",
            "the state its reported quarters gave it on the night it was listed, or not read that night",
            "the buy and the stop and the target",
            "the trade line",
            "the status in words",
            "the sessions held to its resolution or to the newest night",
            "the result as a signed multiple of the risk or open",
            "a repeat listing made while the trade from an earlier night was open marked as listed again and counted once",
            "each column heading saying what its column holds while the pointer is over it",
            "a key saying how to read the trade line and that only the live list's trades appear with the alternatives hidden until one is promoted",
        ],
        // The fundamentals item's two regions, each read as the parts its row states.
        [CheckReach.Key("15.9 Name", "What the numbers say")] =
        [
            "a heading carrying the state and the quarter it was read from",
            "one sentence per reading written from section 4's patterns",
            "a sentence saying so where a reading is absent or read over too few quarters",
            "the quarters the readings read with the dates each was filed and reported on",
            "the full numbers table folded beneath them",
        ],
        // 12.6. An evening before the switch, drawn as it was listed.
        [CheckReach.Key("15.7 Tonight", "An evening before the switch")] =
            ["drawn as it was listed", "one row per name that fired", "ordered by how many fired then by the plan's reward to risk"],
        [CheckReach.Key("15.7 Tonight", "Selected name")] =
            ["the plan column", "the level summary", "whichever row is selected"],
        [CheckReach.Key("15.8 Universe", "Sector strip")] =
            ["one line per sector", "how many are on tonight's list", "names", "how many are in an uptrend"],
        [CheckReach.Key("15.8 Universe", "The table")] =
            ["every name in the index", "the listing strip over sixty sessions", "sorted by distance to the nearest level ascending", "paged", "name", "sector", "close", "trend state", "the distance row mark", "sessions until earnings", "relative strength", "the pullback from the recent high in typical days", "the volume while it came down", "the tightness of the range", "the evening last on the list", "each ticker a link to its own page that draws its year line while the pointer is over it", "each column heading saying what its column holds while the pointer is over it"],
        [CheckReach.Key("15.9 Name", "Prices may be out of date")] =
            ["one line saying its prices may not reflect a recent dividend or split", "when the refetch was last tried and why it failed", "above everything the page draws from those prices"],
        [CheckReach.Key("15.9 Name", "Fact strip")] =
            ["close", "market capitalisation", "the high and low of the move", "next earnings date", "the multiples", "the averages", "momentum and the typical daily move"],
        [CheckReach.Key("15.9 Name", "The chart")] =
            ["the level chart", "the volume profile beside it on the same price axis", "the momentum panel beneath", "the level summary table with each band's members and dates"],
        [CheckReach.Key("15.9 Name", "The plan")] =
            ["the plan column mark", "the tranche table with conditions and stops", "the exit table with actions", "the earnings setups", "the sizing arithmetic"],
        // 11.6. The row names the table's place, its population, its order, its seven columns and
        // the link each ticker carries, and a verdict over it whole would pass with one column undrawn.
        [CheckReach.Key("15.9 Name", "Peers")] =
            ["beneath the table of the biggest moves", "the name's own row marked and then at most ten members of its group by price alone", "those sharing its industry first and then by how closely each one's daily moves followed the name's", "each row carrying how closely it moved with the name", "whether it shares the name's industry", "the close", "how far it sits below the stored year's high", "its return over sixty sessions", "its trend state", "the distance row mark", "each ticker a link to its own page that draws its year line while the pointer is over it", "each column heading saying what its column holds while the pointer is over it", "a key saying how to read it"],
        // 11.7. The row names the record's place, its population, the seven things each row
        // carries, how a print with no estimate is drawn and its key.
        // 11.8. The row names the part's place, the filing it is read from, the five values and
        // the line a company paying none is drawn as.
        [CheckReach.Key("15.9 Name", "Dividend")] =
            ["in the numbers section", "from the newest filing alone", "the forward annual rate", "the forward yield as the provider states it", "the payout ratio", "the ex-dividend date and the pay date", "nothing where the provider files none"],
        [CheckReach.Key("15.9 Name", "Earnings reactions")] =
            ["beside the earnings setups", "one row per print over the calendar's year behind", "each row carrying its report date", "its timing", "the session it moved on", "the estimate", "the actual", "the provider's surprise", "that session's move", "a print with no filed estimate saying none was filed", "a key saying how to read it"],
        [CheckReach.Key("15.9 Name", "How it got here")] =
            ["the table of the biggest moves", "the cause of each where research has been written", "the twelve-month picture"],
        [CheckReach.Key("15.10 Run", "Operational header")] =
            ["what ran", "the instant each stage started and how long it took", "model calls", "network requests", "spend", "what each stage said about itself"],
        [CheckReach.Key("15.10 Run", "Reason records")] =
            ["the resolved count", "the never-entered count", "the share that reached target before stop", "one row per reason with the reason track mark", "the break-even those setups demanded"],
        // 12.3. The Calibration region's shape clock and its trigger lines, each read as the parts its row enumerates.
        [CheckReach.Key("15.10 Run", "The shape clock")] =
            ["the ordinary nights under the open filter version against the sixty the calibration waits on", "every event night with what made it one", "each gate's median count through it against its band", "the list's median size against its band", "drawn as not yet measured until the trigger", "said at the top of the page once the trigger is crossed", "each reason's share of the index as context"],
        [CheckReach.Key("15.10 Run", "What else is waiting on a count")] =
            ["the nights run for the session the clock fell on against five", "the research passes carrying a recorded cost against twenty", "the nights the version step replayed both kinds against five", "the resolved event-book setups against 250", "the nights of trend labels under the third trend version against sixty", "the nights the news labeller ran against twenty with its measured duration and its month's spend and the share of answers refused for a digit"],
        // 12.4. The run page's shape proposal, read as the parts its row enumerates.
        // 12.7. The edge half and the near misses, read as the parts their rows enumerate.
        [CheckReach.Key("15.10 Run", "The edge clock")] =
            ["each swing family candidate's non-empty blocks against the 8 its first look is read at", "its resolved setups", "its share against its planned break-even and calibrated null from the floor on and nothing below it", "that the first look can retire or leave a candidate and never promote one", "when the earliest promotion can come", "each variant with the filter version it was defined against and the settings the live filter has since moved"],
        [CheckReach.Key("15.10 Run", "Near misses")] =
            ["the setups the filter admitted", "beside each gate and each exclusion the setups it alone rejected with every other gate passing", "each group against its own planned break-even and calibrated null", "every figure withheld below the block floor"],
        // 12.6. The run page's overlap, read as the three counts its row enumerates.
        [CheckReach.Key("15.10 Run", "The list from night to night")] =
            ["how many of the night's names were on the list the evening before", "how many at least once over the five evenings before", "how many over the twenty evenings before"],
        [CheckReach.Key("15.10 Run", "The shape proposal")] =
            ["each gate's setting held and proposed with its median count and the list's under each against their bands", "every gate no value in its range brings inside its band named as a finding", "beside it the non-empty blocks accepting it would restart"],
        // 12.1. The name page's readings and the run page's market row, each read as the parts its row enumerates.
        [CheckReach.Key("15.9 Name", "Swing readings")] =
            ["the return over 63 sessions and over 126 sessions with each one's place among the members' returns", "the highest high of the last 20 sessions and how far the close sits below it in typical days' moves", "the median volume of the sessions since that high against the fifty-day average", "the mean true range of the last ten sessions against the last fifty", "a key saying how to read it"],
        [CheckReach.Key("15.10 Run", "Market reading")] =
            ["the night's breadth with how many members it was counted over", "the share above the 50-day average beside it as context", "the index's median volume against its fifty-day average"],
        // The 12.3 correction that opens the Run page on its pictures, each of its first three regions read as
        // the parts its row states.
        [CheckReach.Key("15.10 Run", "How last night went")] =
            ["a status mark and a headline naming the night's state as its own run log rows give it", "four headline figures being the stocks read and the provider requests with the research spend and the steps run again", "a time bar of the night's steps in six named groups showing where a stopped night stopped", "each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished", "a press running the rest of a night left unfinished", "the commit the night was built from"],
        [CheckReach.Key("15.7 Tonight", "The night's state")] =
            ["a notice at the top naming the night's state as the Run page's headline names it", "each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished", "a press running the rest of a night left unfinished", "a one-line note where the night finished", "a refusal before its first step with its reason"],
        // 12.7's correction: the nine parts close to a buy point enumerates, and the line a closed market draws
        // and the key, which its row states outside the enumerations.
        [CheckReach.Key("15.7 Tonight", "Close to a buy point")] =
            ["one row per member whose stored result missed exactly one of the five gates and carries no exclusion", "nearest to qualifying first with a tie in the list's own order", "drawing the places the list leaves of the twenty", "a line above them stating how many are drawn of how many are one gate short", "what a row of the list carries", "the gate it missed in place of the gates", "what it had against the bar it needed in plain words", "how far short that is as a share of the bar", "the trade its plan states", "one line above the rows stated once saying they would qualify if the market turned", "a key saying it recommends nothing"],
        [CheckReach.Key("15.10 Run", "The market")] =
            ["a one-word label read by a stated rule", "a gauge of the share of members above their 200-day average with the market gate's floor marked", "the share above the 50-day average and the index's volume against its fifty-day average", "a line of that share over the sixty sessions before the night that the store holds averages for"],
        [CheckReach.Key("15.10 Run", "From the index to tonight's list")] =
            ["a funnel of how many members passed each of the filter's checks in turn down to those left after the exclusions", "the count listed with a link opening tonight's list"],
        [CheckReach.Key("15.10 Run", "How the list's trades are going")] =
            ["the live list's trades still open and those that reached the target or were stopped out or ran out of time", "a ring of the trades decided against the minimum a share waits on", "the share and the break-even and the average result together once both minimums are met as Past picks draws them", "a link opening Past picks"],
        [CheckReach.Key("15.10 Run", "Is the list finding new stocks")] =
            ["a bar for each of the last twenty evenings split into the names new that evening and those the evening before also listed", "the night's split in words"],
        [CheckReach.Key("15.10 Run", "How the system learns")] =
            ["the shape clock as the ordinary nights under the open filter version against the sixty its calibration waits on", "each check's typical pass count against its range drawn dashed until measured", "the edge clock as a line from today to its two checkpoints with the blocks the live list has gathered", "each version running beside the live list with what it changes and the stocks it has picked and the share the live list also picked and its blocks against the floor", "a link comparing tonight's picks"],
        [CheckReach.Key("15.10 Run", "Compare tonight's picks")] =
            ["a choice of the versions running beside the live list kept in the link", "the names only the live list picked and those both picked and those only the version picked with the setting that made the difference beside each name only one side picked", "two overlapping rings holding the three counts", "the version's picks over the last twenty evenings with the share the live list also picked and the evenings it picked a stock the live list did not", "picks alone with no figure from how a pick turned out"],
        [CheckReach.Key("15.10 Run", "At a checkpoint")] =
            ["one row per version on a scale from nought to a hundred", "before its first look a locked dashed outline with its trades and blocks so far", "from its first look the share of its trades that reached the target with the break-even they needed and what no skill scored from the same starts and how far luck alone could move it", "the verdict in words"],
        [CheckReach.Key("15.10 Run", "Research and spend")] =
            ["the month's spend against the month cap", "the reports the paid model wrote on each of the last seven nights", "the reports and the overnight drafts written over those nights", "the news labeller's line with what the night's labelling cost and the month's against its limit and the articles labelled and the unreadable answers by cause and what stopped it"],
        [CheckReach.Key("15.10 Run", "How each report did")] =
            ["one row per report over the seven nights with its stock and day and what it cost", "a cell per section saying whether it passed first time or on retry or was left out with why or was not warranted, with what its own calls cost and none of a trial's", "the two cases' cell marked where a draft of the pass carried a figure on both sides, with how many of the newest twenty reports' two cases did", "each section's share passed first time and its share left out over the newest twenty reports that warranted it"],
        [CheckReach.Key("15.10 Run", "Anything to worry about")] =
            ["a checklist of plain items each turning red with its reason where it fails", "every stock holding the night's prices and every step of the night finished", "no research document refused and no section fallen back", "every company awaiting a quarter asked on schedule", "no paid model a job uses within thirty days of its retirement date, and where one is its profile named with its job and date", "no report whose pass ran on the night's session costing more than section 17 names, and where one did its stock named with its cost", "the four harness counts beneath"],
        // 12.2. The name page's gates and the run page's funnel, each read as the parts its row enumerates.
        [CheckReach.Key("15.9 Name", "Gates")] =
            ["each of the five gates with whether it passed and why", "the setup's family and whether the trigger's event happened", "the trade read from the ladder's first tranche and from the one swing plan that night's live rule used with that plan marked and no candidate's plan drawn whichever filter version the night ran under", "the exclusions with a key saying how to read it"],
        [CheckReach.Key("15.10 Run", "Swing filter funnel")] =
            ["how many members each gate passed in order and how many it removed", "the setup's families on a night that held a breakout", "what each exclusion removed and how many pass", "the version the night ran under"],
        [CheckReach.Key("15.10 Run", "Harness")] =
            ["passed", "failed", "unexamined"],

        // 8.5. The row states the three figures and then states that they are
        // shown together or not at all, which is a fourth claim and the one the
        // other three rest on: a share drawn without its denominator is the
        // defect this row exists to forbid, and a verdict over the three as one
        // would pass with two of them drawn.
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum")] =
        [
            "the share that reached the target before the stop",
            "the number resolved",
            "the break-even those setups demanded",
            "always the three together",
        ],

        // 8.4. The row states three things and the harness reads it as three
        // claims, for the reason every other decomposed row is read that way: a
        // row passes for what it says, and a single verdict over three
        // statements passes when one of them is drawn and two are not. The
        // count and the divisor are separate claims, computed apart and drawn
        // from their own fields: one is how many stand registered and the other
        // is what a threshold is divided by, and the day those stop being the
        // same number the page shows both.
        [CheckReach.Key("15.10 Run", "Tonight's order")] =
        [
            "the three orders of tonight's list over the twenty each would draw",
            "the old order named as the benchmark",
            "the setups each order drew",
            "the setups whose whole window has closed",
            "the blocks holding one against the floor",
            "no comparison drawn before every order reaches it",
        ],
        // The candidates' record region, one part for each thing the row says it holds. A single
        // verdict over ten statements passes when one is drawn and nine are not, which is the
        // reading the shadow region's own decomposition was written for.
        [CheckReach.Key("15.10 Run", "Candidates' records")] =
        [
            "the verdict field stating what the last look read and what triggers the next",
            "the nightly figure beside it labelled as monitoring",
            "the Poisson-binomial tail labelled and deciding nothing",
            "each look's setups and the share they won against the bar the calibration set",
            "the level that look spent and whether it crossed its boundary",
            "the smallest excess that look could have detected",
            "the design effect and what a loss cost in multiples of the planned risk",
            "the setups entered and stopped on one session and those stopped out on a session the name reported on",
            "the step the graph stands at with its level and the count of distinct trials",
            "no name anywhere in it",
        ],
        [CheckReach.Key("15.10 Run", "Shadow candidates")] =
        [
            "how many candidate conditions are registered",
            "the family's divisor that number sets",
            "the count of distinct trials and the level each starts at beside it",
            "one line saying each candidate's record is withheld until it is promoted",
        ],
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart")] =
            ["candles", "the level bands", "the moving averages", "a volume pane"],

        // The universe screen, per region, and the reason is the one that
        // resolved contradiction F. 15.8 reads the listings, and 5.1 builds
        // this screen three checkpoints before 5.4 creates that table. Its
        // sector strip counts how many names are on tonight's list and its
        // table carries the evening a name was last on it and the listing strip
        // over sixty sessions, none of which exists at 5.1. Read as one claim
        // each, both rows would be owed at 5.4 and the halves that draw from
        // membership, indicators, levels and ladders would sit unasserted for
        // the whole of the phase's visible output.
                
        // The run page's reason record, and the same argument again. Its counts
        // and the base rate pinned above them are what 5.6 builds, and three
        // operating obligations name that surface as where their trigger is
        // read. Its verdicts need resolved setups and are 8.5's. Read as one
        // claim it would be owed at 8.5, and the obligations would name a
        // surface the harness says arrives three phases after the checkpoint the
        // plan says builds it.
        
        // The run page's stale-and-failed region, decomposed at the phase 5
        // sign-off. It was PASS whole from 5.6 while two of its parts describe
        // components phase 6 builds: sections that fell back are the claim
        // checker's at 6.4 and documents refused by admissibility are 6.3's. The
        // two checkpoint numbers here read 6.3 and 6.2 until 6.3, which is the
        // resplit 6.0 made moving every phase 6 due point by one and a comment
        // the re-pointing did not reach, because a comment is not a placement. A
        // PASS over a row whose check reaches part of it is an unexamined claim
        // wearing a verdict, and the region itself says the research halves
        // are absent. The stage a night stopped on is the part the sign-off
        // repaired, since until then no night could put one there.
        [CheckReach.Key("15.10 Run", "Stale and failed")] =
            ["names carrying yesterday's bars", "the stage a night stopped on", "sections that fell back", "documents refused by admissibility"],

        // Section 15.9's two research-state rows, decomposed at 6.5 for the reason
        // contradiction F gives: each states a line the judge's verdict draws now and
        // parts only the research runner can draw, being a control that writes the
        // sections and the sections themselves under their dates. Read whole, each
        // row would be owed at 6.8 and the line that works would sit unasserted for
        // three checkpoints.
        [CheckReach.Key("15.9 Name", "Research not yet written")] =
        [
            "the researched sections absent with one line saying they have not been written",
            "a control that writes them with its cost stated before it is pressed",
            "beside the computed sections rendered whole",
        ],
        [CheckReach.Key("15.9 Name", "Research stale")] =
        [
            "the stored sections rendered with their own dates",
            "one line naming which of the four triggers fired",
            "the option to have them rewritten",
        ],

        // Section 19.1's inadmissible document row, decomposed at 6.3, which is
        // the obligation 6.0 filed against this checkpoint rather than a choice
        // made here. The row names six kinds the test refuses and carried one
        // verdict over all of them, so five could have been missing and the row
        // would still have passed, which is the fault the fifth phase 5 sign-off
        // review found on section 15's rows. 6.0 widened the row's text and left
        // the decomposition, because the reader that reads parts off a row is
        // scoped to section 15 and widening it would have made every fixture row
        // enumerating three or more items owe parts in the same pass. What
        // discharges it instead is this decomposition plus a check that reads
        // this row's parts off its own words in both directions, which is why
        // the row's rationale sentence was reworded at 6.3: as written it formed
        // a second comma run and the reader picked up three items of prose.
        [CheckReach.Key(FixtureTable, "an inadmissible document")] =
        [
            "a machine-generated price forecast",
            "a broker marketing page",
            "a summary written by another AI system",
            "a quote page carrying no article",
            "a page with no publish date",
            "a page whose text cannot be retrieved",
        ],

        // The name page's how-it-got-here row, decomposed at 5.2 for the reason
        // the universe screen's two were at 5.0. Its table of the biggest moves
        // is what the annotator writes and is drawable the night that store
        // exists; the cause of each is a researched claim living in
        // `research_section` with its source, and it arrives at 6.5. Read as one
        // claim the table would be owed at 6.5 and the half that works would sit
        // unasserted for a phase, which is contradiction F's argument.
        
        // The same shape in the failure table, and it arrived by the same
        // route. This row's "What you see" cell names two surfaces drawn a
        // phase and a half apart: the chart, which exists from 1.3 and shows
        // the gap as an absence, and the level and plan sections, which arrive
        // at 3.5 and 4.4. Read as one claim it would be owed at 4.4 and the
        // half that works would sit unasserted for two phases, which is the
        // argument that resolved contradiction F.
        //
        // This was nearly the third failure row re-dated whole, after "A name
        // leaves the index" and "Bulk price feed unavailable". Those two are
        // correct, because neither names a surface that exists yet. This one
        // does, and re-dating it would have been the habit rather than the
        // rule.
        [CheckReach.Key(FailureTable, "A gap in one name's series")] =
            ["chart", "level and plan sections"],

        // The second row to be read per surface, and for the same reason. Its
        // behaviour half is what the night does with the store and the run log,
        // and that is assertable the moment a feed can fail. Its other half is a
        // banner giving the data date and tonight's list absent rather than
        // wrong, and both of those are surfaces phase 5 builds.
        //
        // Read as one claim it would be owed at 5.4 and the half that works
        // would sit unasserted for two phases, which is the argument that
        // resolved contradiction F and the shape 1.5 gave the gap row.
        [CheckReach.Key(FailureTable, "Bulk price feed unavailable")] =
            ["run log", "banner"],

        // The third, and the same argument a third time. This row's behaviour
        // is the stored indicator, which exists from 3.1 and records the long
        // average as absent with the bar count that explains it. Its other half
        // is the string "not available, nn bars" on the name page, which is a
        // surface, and no page shows an average yet. Read as one claim it would
        // be owed where the page is and the half that works would sit
        // unasserted for the rest of the phase.
        [CheckReach.Key(FailureTable, "Fewer than 200 bars for a new index member")] =
            ["200-day average", "nn bars"],

        // The fifth, and the one row that could not be re-pointed at 4.0
        // without being read per surface. Its two halves are a phase apart: the
        // calendar saying the date is not on file is what the fetcher stores or
        // fails to store, and the earnings reason not firing is one of the six
        // conditions, which arrives with the shortlist builder. Read whole it
        // would be owed at 5.4 and the half that works would sit unasserted
        // through the phase that builds it.
        [CheckReach.Key(FailureTable, "Earnings date missing")] =
            ["the earnings reason", "the calendar"],

        // Section 15.9's research paused row, decomposed at 6.7 for contradiction F's
        // argument: the line is the spend cap's and is drawn here, and the stored sections
        // under their own dates are drawn when a section is first written to be shown.
        [CheckReach.Key("15.9 Name", "Research paused")] =
            ["one line saying that research is paused and when it resumes", "with the stored sections still rendered under their own dates"],

        // Section 18's spend cap row, decomposed at 6.7, where both halves are built:
        // the cap refusing a call is the component's, and the line on the name page is
        // a surface, and a claim that something is visible is a claim about a surface.
        [CheckReach.Key(FailureTable, "Spend cap reached")] =
            ["research pauses for the period", "the name says research is paused and when it resumes"],

        // Section 18's row about a key the secrets file does not hold, decomposed at 12.6's correction for the
        // spend cap row's reason: the job stopping is the worker's, and the line on the run page is a surface.
        [CheckReach.Key(FailureTable, "A paid job's profile names a key the secrets file does not hold")] =
            [
                "the job stops before it fetches or asks anything and no other profile answers for it; a research pass writes the plain line saying which profile and which key as its own run log row, which the drain settles the request under as refused",
                "the run page's stages that failed name the pass in the line's own words",
            ],

        // Section 18's two local lane rows, decomposed at 6.6 for contradiction F's
        // argument. Each names what the writer does and what the page draws, which
        // this checkpoint builds, beside what the paid path does with the section, the
        // control that asks it to, and what the overnight queue records, which arrive
        // at 6.8 and 6.10. Read whole, each row would be owed at the last of those and
        // the parts that work would sit unasserted for four checkpoints.
        [CheckReach.Key(FailureTable, "A section is assigned to the local lane that the machine cannot hold")] =
        [
            "the pass is refused before it starts",
            "the section is left for the paid path",
            "the run log names the section and the reason",
            "the section is absent as usual",
            "the option to have it written",
        ],
        [CheckReach.Key(FailureTable, "The local model is unavailable")] =
        [
            "the sections in the local lane are left unwritten",
            "the overnight queue records that it could not run",
            "a pass on demand writes the paid lane's sections and leaves the local lane's absent",
            "the local-lane sections absent with their reason",
            "the option to have the paid model write them",
        ],
    };

    // The claim subjects a row yields. One, itself, unless the row decomposes.
    internal static IReadOnlyList<string> SubjectsOf(string table, string row) =>
        Elements.TryGetValue(CheckReach.Key(table, row), out var elements)
            ? [.. elements.Select(element => $"{row}, {element}")]
            : [row];

    internal static IReadOnlyCollection<string> DecomposedRows() => Elements.Keys;

    internal static IReadOnlyList<string> ElementsOf(string key) => Elements[key];

    // The screens tables, named so the reconciliation can read the document's
    // rows against the map above in both directions. Written here rather than
    // derived from the keys, because deriving the table list from the same
    // dictionary the check compares against would make the comparison circular.
    internal static readonly string[] ScreensTables =
    [
        "15.4 The two surfaces",
        "15.5 The mark vocabulary",
        "15.7 Tonight",
        "15.8 Universe",
        "15.9 Name",
        "15.10 Run",
        "15.15 Queue",
        "15.16 Watch list",
        "15.17 Past picks",
        "15.11 How a reason's record is displayed",
    ];

    internal static IReadOnlyCollection<string> ScreensKeys() => Screens.Keys;

    internal static IReadOnlyCollection<string> NightlyStepKeys() => NightlySteps.Keys;

    // How many keys match a nightly step's full text. Exactly one is the
    // property: the steps are sentences and the keys are their openings, so a
    // key that is the opening of another key answers for both and whichever
    // the dictionary yields first wins silently.
    internal static int NightlyStepKeysMatching(string subject) =>
        NightlySteps.Keys.Count(key => subject.StartsWith(key, StringComparison.Ordinal));

    // Section 19.1's rows, each owed where the artefact it describes arrives.
    //
    // Read as one table it would have been asserted whole at 1.8 with eleven of
    // its thirteen rows describing artefacts that do not exist: seven expected
    // outputs arriving across phases 3 to 5, three rejections at phase 6, and a
    // fundamentals input at 6.1. A placement owed at 1.8 was the shape this
    // replaced, and it had the same defect as reading a failure row whole.
    static readonly Dictionary<string, string> FixtureRows = new(StringComparer.Ordinal)
    {
        ["a poisoned paragraph"] = "6.4",
        ["an unsourced claim"] = "6.4",
        ["an inadmissible document"] = "6.3",
        ["research record"] = "6.8",
        ["theme record"] = "6.9",
        ["source documents"] = "6.3",
        ["refused documents"] = "6.3",
        ["calendar"] = "4.3",
        ["membership"] = "1.1",
        ["fetch"] = "2.1",
        ["series state"] = "1.6",
        ["indicators"] = "3.1",
        ["swings"] = "3.2",
        ["volume profile"] = "3.3",
        ["levels"] = "3.4",
        ["ladder"] = "4.4",
        ["moves"] = "5.2",
        ["peers"] = "11.6",
        ["swing readings"] = "12.1",
        ["gate results"] = "12.2",
        ["reported quarters"] = "12.2",
        ["reactions"] = "11.7",
        ["listings"] = "5.4",
        ["facts"] = "5.3",
        ["forward returns"] = "5.5",
        ["news pulse"] = "5.5",
    };

    static readonly Dictionary<string, string> Failures = new(StringComparer.Ordinal)
    {
        // The unavailable row's other half. Its behaviour half is asserted at
        // 2.3 by nightly-run, which induces a feed that does not answer and
        // reads the stored bars and the run log back. The banner giving the data
        // date and tonight's list absent rather than wrong are phase 5 surfaces,
        // and a claim that something is visible is a claim about a surface.
        ["Bulk price feed unavailable, banner"] = "5.4",
        // The chart half is reached at 1.5 by gap-refusal, so only the other
        // element is owed. The level sections arrive at 3.5 and the plan
        // section's tables at 4.4, which is the last surface the cell names.
        ["A gap in one name's series, level and plan sections"] = "4.6",
        ["A split or dividend not caught"] = "1.6",
        ["A name whose trend state cannot be classified"] = "4.1",
        ["No band is eligible to carry a tranche"] = "4.4",
        ["Earnings date missing, the earnings reason"] = "5.4",
        ["Earnings date missing, the calendar"] = "4.3",
        // The store half is reached at 3.1 by fixture-expectations, so only the
        // other element is owed. The name page's fact strip is where
        // "not available, nn bars" is read, and 3.5 is the checkpoint that first
        // draws an indicator-derived region of that page.
        ["Fewer than 200 bars for a new index member, nn bars"] = "3.5",
        // The row's "What you see" cell claims the name disappears from the
        // universe screen, which is 5.1. 1.1 records the leave date and asserts
        // nothing a reader looks at.
        ["A name leaves the index"] = "5.1",
        ["A search returns snippets rather than full page text"] = "6.9",
        ["A search returns a site the applicable list does not carry"] = "6.9",
        ["The search tool is unavailable"] = "6.9",
        // Section 18's two local lane rows, decomposed at 6.6. The writer's parts and
        // the page's arrived there, the paid path's and the page's option at 6.8, and
        // what the overnight queue records at 6.10, each now reached where it landed.
        ["The local model is unavailable, the overnight queue records that it could not run"] = "6.10",
        ["A theme refresh fails while a name's pass depends on it"] = "6.9",
        // 12.2's two, each answered by the gate that reads the absent value.
        ["Breadth not available on a night"] = "12.2",
        ["A gate's reading is absent for a name"] = "12.2",
        // 12.4's, answered by the command's rejection.
        ["A shape proposal rejected"] = "12.4",
        // 12.3's, answered by the night's script before any worker exists.
        ["The night's checkout is off main or holds a commit the remote's main lacks"] = "12.3",
        // 12.6's, answered by tonight's page and the night's own request.
        ["The market gate closed on a night"] = "12.6",
        ["No name passed the swing filter on a night"] = "12.6",
        ["No trade listed yet"] = "12.2",
        ["A trade whose outcome row is missing"] = "12.2",
        ["A still open trade whose outcome row is missing"] = "12.2",
        // The fundamentals item's five, a 12.2 correction.
        ["A member the provider returns no quarter for"] = "12.2",
        ["A member's new quarter is not yet posted when it is asked"] = "12.2",
        ["The provider refuses a quarters ask"] = "12.2",
        ["The quarters step reaches its limit or the day's allowance"] = "12.2",
        ["A night that stored no readings of the reported quarters"] = "12.2",
        // 12.7's correction, answered by tonight's page.
        ["Both lists on tonight's page empty on a night the swing filter listed"] = "12.7",
        // The model profiles, a 12.6 correction.
        ["A paid job's profile names a key the secrets file does not hold"] = "12.6",
        // The news labeller, a 12.6 correction.
        ["The news job's model does not answer"] = "12.6",
        ["An answer the labeller cannot read"] = "12.6",
        ["The labeller's month limit reached"] = "12.6",
        ["The day or month cap pauses a label"] = "12.6",
        ["The labeller's time limit passes"] = "12.6",
        ["A peak window of the news profile opens while the labeller runs"] = "12.6",
        ["An article refused by admissibility"] = "12.6",
        ["The labeller fails"] = "12.6",
        // 13.1, the family framework. The row about two families is read on the page once a second family is on it.
        ["A stock a family passes while a trade for it is still open"] = "13.1",
        ["A stock two families pass on one night"] = "13.2",
        ["A night the families list no stock"] = "13.1",
        // 13.2, breakouts.
        ["A member holding too few sessions for a breakout to be read"] = "13.2",
        ["A breakout with no typical move to place its stop by"] = "13.2",
        // The sweep, a 12.5 correction, and its rerun.
        ["A chunk of the sweep fails twice"] = "12.5",
        ["The point-in-time check finds a difference"] = "12.5",
        ["A sweep candidate with no sector label"] = "12.5",
        // The stepped plan's risk, the 12.5 correction of 2026-10-01.
        ["A stepped plan filled nearer its stop than the stop setting's floor"] = "12.5",
    };

    // The two rows of the read and write matrix whose component already exists.
    // Every other row resolves through Components, which the catalogue shares.
    // A matrix row claims what its component touches across every store, and
    // most of those stores are not built, so the row is not assertable until
    // the last of them is.
    static readonly Dictionary<string, string> MatrixRows = new(StringComparer.Ordinal)
    {
    };

    // Section 17's limits. Each row is a claim about the code, and the code
    // that would carry it arrives with the component the row constrains.
    static readonly Dictionary<string, string> LimitDuePoints = new(StringComparer.Ordinal)
    {
        // nightly-cost is implemented at 1.4, over the shipped source and a
        // recorded run, which is the first point either limit is asserted.
        ["Model calls in the nightly run"] = "1.4",
        ["Per-name network calls in the nightly run"] = "1.4",
        ["Nightly wall clock, at index size"] = "5.7",
        // Retention is what makes the year a limit rather than a description,
        // and it lands with the fetcher at 1.4.
        ["Bar history kept"] = "1.4",
        ["Level window"] = "3.4",
        ["Swing lookback"] = "3.2",
        ["Band merge distance"] = "3.4",
        ["Band width"] = "3.4",
        ["Tranches, exits"] = "4.5",
        ["Tranche eligibility"] = "4.4",
        ["Earnings horizon"] = "4.7",
        ["Nightly row coverage"] = "5.4",
        ["Reports the night asks for"] = "11.4",
        ["Group floor"] = "11.5",
        ["Peer return window"] = "11.6",
        ["Peers drawn"] = "11.6",
        ["Peer likeness floor"] = "11.6",
        ["Relative strength windows"] = "12.1",
        ["Pullback depth"] = "12.2",
        ["Volume dry-up"] = "12.2",
        ["Trade reward to risk"] = "12.2",
        ["Trade stop distance"] = "12.2",
        ["Earnings exclusion"] = "12.2",
        ["Recent high window"] = "12.1",
        ["Range tightness windows"] = "12.1",
        ["Event session share"] = "11.9",
        ["Event volume ratio"] = "12.3",
        ["Shape calibration nights"] = "12.3",
        ["Gate bands"] = "12.3",
        ["List band"] = "12.3",
        ["Shape lever ranges"] = "12.4",
        ["Trigger arrival window"] = "12.4",
        ["Shape acceptance bound"] = "12.4",
        ["Swing trade plan"] = "12.2",
        ["Distinct trials"] = "12.5",
        // The fundamentals item's ten, a 12.2 correction.
        ["Quarters step"] = "12.2",
        ["Quarters fill"] = "12.2",
        ["Quarter prices"] = "12.2",
        ["Trajectory quarters"] = "12.2",
        ["Business state"] = "12.2",
        ["Estimate record"] = "12.2",
        ["Met tolerance"] = "12.2",
        ["Earnings quality"] = "12.2",
        ["Valuation position"] = "12.2",
        ["Weighted calls a quarters ask"] = "12.2",
        // The model profiles, a 12.6 correction.
        ["Paid model profiles"] = "12.6",
        ["Paid model retirement warning"] = "12.6",
        ["A report named for its cost"] = "12.6",
        // The news labeller, a 12.6 correction.
        ["News labelling window"] = "12.6",
        ["News labeller time limit"] = "12.6",
        ["News labeller month limit"] = "12.6",
        ["Unreadable answers for a digit"] = "12.6",
        ["News article retention"] = "12.6",
        // The family framework, 13.1.
        ["Names a family lists"] = "13.1",
        // The breakout's settings, 13.2.
        ["Breakout high window"] = "13.2",
        ["Breakout volume multiple"] = "13.2",
        ["Breakout range window"] = "13.2",
        ["Breakout range ceiling"] = "13.2",
        ["Breakout stop"] = "13.2",
        ["Breakout session cap"] = "13.2",
        // The research template, 12.6 corrections.
        ["Risk kinds"] = "12.6",
        ["Sector sites"] = "12.6",
        ["Report rates"] = "12.6",
        ["Section review"] = "12.6",
        // The sweep, a 12.5 correction, and its rerun.
        ["Sweep grid"] = "12.5",
        ["Sweep starting point"] = "12.5",
        ["Sweep variants"] = "12.5",
        ["Sweep run"] = "12.5",
        ["Sweep edge"] = "12.5",
        ["Sweep conditions"] = "12.5",
        ["Sweep point in time"] = "12.5",
        ["Sweep search"] = "12.5",
    };

    static readonly Dictionary<string, string> NightlySteps = new(StringComparer.Ordinal)
    {
        // 8.6, the rule version scorer's own step.
        ["Replay tonight's name-nights"] = "8.6",
        // The step is a claim about the nightly script running it in order, and
        // the script is built at 1.4. A step whose component lands earlier is
        // still not run by a night until then.
        ["Load index membership"] = "1.4",
        ["Backfill one year"] = "1.4",
        ["Fetch the day"] = "1.4",
        ["Check splits and dividends"] = "1.6",
        ["Fetch the index's dated events"] = "4.3",

        // The nine that were one step until 4.0. Written as "For every name:
        // indicators, swings, volume profile, levels, trend state, ladder,
        // moves, list reasons, facts file" they carried one due point, phase 5,
        // and a claim whose due point is a phase cannot fail until that phase's
        // first checkpoint lands. What that hid is that the swing finder, the
        // volume profile builder and the level builder have shipped since phase
        // 3 and no night has ever run one: their only callers are in this
        // suite. Nine claims, each owed at the checkpoint that puts its stage
        // into the night's own order.
        ["Compute the indicators"] = "4.1",
        ["Mark the swings"] = "4.1",
        ["Build the volume profile"] = "4.1",
        ["Build the levels"] = "4.1",
        ["Classify the trend state and build the ladder"] = "4.1",
        ["Annotate the largest moves"] = "5.2",
        ["Compute the swing readings"] = "12.1",
        // The 12.2 correction that reads the reported quarters every night and asks for them after the
        // close, on the operator's ruling of 2026-09-27.
        ["Read the reported quarters of every member"] = "12.2",
        ["Ask the provider for the reported quarters"] = "12.2",
        ["Evaluate every member through the swing filter"] = "12.2",
        ["Count the ordinary nights stored under the open filter version"] = "12.4",
        ["Evaluate the list reasons"] = "5.4",
        ["Run the overnight queue"] = "6.10",
        ["Ask for a report on the first six names"] = "11.4",
        // 12.6's correction, the news labeller the night starts after its request.
        ["Start the news labeller"] = "12.6",
        ["Write the facts file"] = "5.3",

        ["Fill forward returns"] = "5.5",
        ["Count today"] = "5.5",
        ["Close the arithmetic"] = "5.5",
    };

    // Figure 12.1's boxes, which are the research flow and land in phase 6.
    //
    // Keyed on the figure and the box together, as everything else here is keyed
    // on the table and the subject: a box name is unique inside a figure and
    // nowhere else, and a matcher keyed on the opening of a value answers about
    // everything sharing it.
    //
    // Figures 9.1 and 10.1 are not here. Every one of their boxes has landed and
    // names the check that reached it in Reached above, which is where a claim
    // with a verdict belongs.
    static readonly Dictionary<string, string> Figures = new(StringComparer.Ordinal)
    {
        [CheckReach.Key("Figure 12.1", "Is there a research record?")] = "6.5",
        [CheckReach.Key("Figure 12.1", "Does it still stand?")] = "6.5",
        [CheckReach.Key("Figure 12.1", "All four no")] = "6.5",
        [CheckReach.Key("Figure 12.1", "A pass is warranted")] = "6.8",
        [CheckReach.Key("Figure 12.1", "Write the sections")] = "6.8",
        [CheckReach.Key("Figure 12.1", "Check every claim")] = "6.4",
        [CheckReach.Key("Figure 12.1", "Store it")] = "6.4",
    };

    // Every verdict note this map holds, for the guard that keeps a count of a
    // row's own parts out of them: a count typed into a note is read against
    // nothing and the report prints it whatever the row holds.
    // see: A verdict note states no count of the row's own parts
    internal static IEnumerable<string> Notes => Reached.Values.Select(scoped => scoped.Note);

    internal static Scoped For(string table, string subject)
    {
        // The ones that have landed. Each names the check that reached it,
        // because a PASS naming none is a PASS by fiat.
        if (Reached.TryGetValue(CheckReach.Key(table, subject), out var reached))
        {
            return reached;
        }

        var due = Resolve(table, subject).Due;

        return string.IsNullOrEmpty(due)
            ? throw new InvalidOperationException(
                $"No scope is declared for '{subject}' under '{table}'. A claim with no entry " +
                "would be left unexamined by omission, which is the one thing this map exists " +
                "to make impossible.")
            : new Scoped(Verdict.OutOfScope, $"nothing asserts this until {due}", string.Empty);
    }

    // Every subject and the due point it resolves to, so the reconciliation can
    // read the two halves apart: what the plan supplied, and what is written
    // here because the plan could not.
    // Every map Resolve consults, and the list is the population the shadow
    // check reads. FixtureRows and Screens were absent from it until 6.0, which
    // is the same under-reporting the check exists to catch arriving inside the
    // check: three fixture rows held a written value the plan could supply and
    // nothing could say so, because the map they sat in was not in the
    // population. Screens keys are composite and can never derive, and they are
    // listed anyway, because a population defined by what could match is
    // defined by the thing it is checking.
    internal static IReadOnlyList<string> ResidualSubjects() =>
        [.. DerivedIsLate.Keys, .. DerivedIsEarly.Keys, .. Components.Keys, .. Stores.Keys,
            .. Failures.Keys, .. MatrixRows.Keys, .. LimitDuePoints.Keys, .. NightlySteps.Keys,
            .. Figures.Keys, .. FixtureRows.Keys, .. Screens.Keys];

    internal static IReadOnlyList<string> DeclaredExceptions() =>
        [.. DerivedIsLate.Keys, .. DerivedIsEarly.Keys];

    // A nightly-step key is a prefix of a subject and not a subject. The
    // resolver matches a step's whole text against these, so it never asks
    // PlanCheckpoints.DueFor with a key, and the plan naming a prefix says
    // nothing about whether that value could derive. Reading one as shadowing
    // the plan reports a question nobody asks, which is what 6.0 found by
    // writing "Run the overnight queue" into the checkpoint that builds it.
    // Named here rather than excluded inside the check, so the exclusion is a
    // property of the map and the check asserts what makes it excludable.
    internal static IReadOnlyList<string> ResidualPrefixSubjects() => [.. NightlySteps.Keys];

    // A fixture row's key is the name of a file and not a claim subject, and
    // the names are ordinary words: fundamentals, calendar, membership, fetch,
    // indicators, swings, levels, ladder, moves, facts, listings. The plan uses
    // every one of them in several checkpoints, so a derivation over the key
    // answers about the first checkpoint that happens to use the word and not
    // about the row. Widening the shadow check's population at 6.0 showed it in
    // one run: fourteen of them derived, and moves gave 2.1 against a real 5.2,
    // levels gave 5.1 against 3.4, and forward returns gave what is now 8.1 against 5.5.
    // So the whole map is written, uniformly, and this is the reason rather
    // than three of it deriving by the luck of a distinctive phrase.
    internal static IReadOnlyList<string> ResidualFileNameSubjects() => [.. FixtureRows.Keys];

    // Late first, then early, each with the direction it claims, so the
    // reconciliation can assert the claim rather than take the label.
    internal static IReadOnlyList<DuePointException> Exceptions() =>
    [
        .. DerivedIsLate.Select(pair => new DuePointException(pair.Key, pair.Value, Later: true)),
        .. DerivedIsEarly.Select(pair => new DuePointException(pair.Key, pair.Value, Later: false)),
    ];

    // Which half of this file answered, alongside the answer itself.
    //
    // The origin is returned rather than inferred, and that is the repair. The
    // old metric asked whether a subject was absent from the residual list and
    // whether the plan could name it, which is a question about capability and
    // not about what happened. Fifteen screens rows answered here by their table
    // heading were counted as derived from the plan, because the plan's prose
    // contains the words "The table" and "The chart" and the count never asked
    // which branch had run. A third of a floored population was measuring the
    // wrong thing, and the floor sat under it saying nothing.
    internal static (string? Due, DueOrigin Origin) Resolve(string table, string subject)
    {
        // The ones the plan names in a way that cannot be read, each carrying
        // the reason beside it above.
        if (DerivedIsLate.TryGetValue(subject, out var late))
        {
            return (late, DueOrigin.Exception);
        }

        if (DerivedIsEarly.TryGetValue(subject, out var early))
        {
            return (early, DueOrigin.Exception);
        }

        if (Screens.TryGetValue(CheckReach.Key(table, subject), out var screen))
        {
            return (screen, DueOrigin.Screens);
        }

        if (Figures.TryGetValue(CheckReach.Key(table, subject), out var figure))
        {
            return (figure, DueOrigin.Residual);
        }

        if (table == LimitsTable && LimitDuePoints.TryGetValue(subject, out var limit))
        {
            return (limit, DueOrigin.Residual);
        }

        if (table == MatrixTable && MatrixRows.TryGetValue(subject, out var row))
        {
            return (row, DueOrigin.Residual);
        }

        if (table == NightlyRunSteps.Heading)
        {
            var step = NightlySteps
                .FirstOrDefault(entry => subject.StartsWith(entry.Key, StringComparison.Ordinal))
                .Value;

            if (step is not null)
            {
                return (step, DueOrigin.Residual);
            }
        }

        if (Components.TryGetValue(subject, out var component))
        {
            return (component, DueOrigin.Residual);
        }

        if (Stores.TryGetValue(subject, out var store))
        {
            return (store, DueOrigin.Residual);
        }

        if (Failures.TryGetValue(subject, out var failure))
        {
            return (failure, DueOrigin.Residual);
        }

        if (table == FixtureTable && FixtureRows.TryGetValue(subject, out var row2))
        {
            return (row2, DueOrigin.Residual);
        }

        // Nothing above it carried this subject, so the plan is asked. This is
        // the half that cannot go stale: BUILD_PLAN's checkpoint text is the
        // first statement of which checkpoint does the work, and a due point
        // read from it moves when the plan is reordered.
        var derived = PlanCheckpoints.DueFor(subject);

        return derived is null ? (null, DueOrigin.Nothing) : (derived, DueOrigin.Plan);
    }
}
