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
        [CheckReach.Key("15.7 Tonight", "The list, one row per name that fired")] = new Scoped(
            Verdict.Pass,
            "one row per fired name, counted off the markup",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, ordered by how many fired then by band strength")] = new Scoped(
            Verdict.Pass,
            "the order is asserted over rows whose fired counts are equal, so the tiebreaker is the thing read",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "The list, at most twenty drawn")] = new Scoped(
            Verdict.Pass,
            "twenty at most are drawn and the undrawn count is stated beside them",
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
        [CheckReach.Key("15.7 Tonight", "The list, the reasons")] = new Scoped(
            Verdict.Pass,
            "each reason that fired is named in the row's own cell",
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
            "every stage the night's run log carries is drawn, in the order they ran",
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
            "the class declares the stores it reads and the run log it appends to, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Read API")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the eleven reads and the one write",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Mark renderer")] = new Scoped(
            Verdict.Pass,
            "the class declares an empty access, which is a claim rather than an omission, and it matches a row reading the API and writing none",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Mark renderer")] = new Scoped(
            Verdict.Pass,
            "all eleven cells are blank and the declaration is empty, asserted cell by cell",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Single page app")] = new Scoped(
            Verdict.Pass,
            "the class declares an empty access and it matches a row reading the API and writing none",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Single page app")] = new Scoped(
            Verdict.Pass,
            "all eleven cells are blank and the declaration is empty, asserted cell by cell",
            ByAccess),
                [CheckReach.Key("15.5 The mark vocabulary", "Level chart, candles")] = new Scoped(
            Verdict.Pass,
            "one candle is drawn per stored session, counted off the rendered markup and matched session by session against the store, hollow above the open and filled below in neutral ink",
            ByReadSurface),
        // The shortlist builder and tonight's list, 5.4.
        // The forward returns, the news pulse and the night's close, 5.5.
        [CheckReach.Key(CatalogueTable, "Forward return filler")] = new Scoped(
            Verdict.Pass,
            "the class declares the bars and listings it reads and the forward returns it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "News pulse counter")] = new Scoped(
            Verdict.Pass,
            "the class declares the membership it reads, the news feed it calls and the pulse it writes and drops, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(CatalogueTable, "Night close")] = new Scoped(
            Verdict.Pass,
            "the class declares the four stores it counts off and the run log it appends to, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
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
            "every horizon is recomputed in the suite from the bars after the listing and the plan the listing stored, with the matured cases over constructed series because the committed fixture has no session after its listings",
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
        [CheckReach.Key(NightlyRunSteps.Heading, "Fill forward returns for past listings that matured today, and recompute the universe base rate.")] = new Scoped(
            Verdict.Pass,
            "the night runs the stage once rather than per name, and its run log row records the listings, the matured rows and the ones not yet matured apart",
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
            "the class declares the seven stores it reads and the listings it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
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
            "each of the six reasons is recomputed in the suite from the tables it reads and compared against what the builder wrote, rather than diffed against a set frozen from that builder",
            ByExpectations),
        [CheckReach.Key(NightlyRunSteps.Heading, "Evaluate the list reasons for every name.")] = new Scoped(
            Verdict.Pass,
            "the night runs the stage in the order section 14 states, before the facts file, and its run log row records the members, the fired names and the reasons",
            ByNight),
        [CheckReach.Key(LimitsTable, "List display")] = new Scoped(
            Verdict.Pass,
            "at most twenty rows are drawn and the true fired count is stated whatever is drawn, asserted against a constructed night of forty",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Listing strip")] = new Scoped(
            Verdict.Pass,
            "one cell per evening over the window, a listed evening drawn differently in shape as well as in ink, and the count of listed evenings read off the markup",
            ByReadSurface),
                [CheckReach.Key("15.7 Tonight", "Watch list")] = new Scoped(
            Verdict.Pass,
            "the region sits above the list rather than inside it and states that no watch list is on file, because no store holds one and none is invented",
            ByReadSurface),
                                        [CheckReach.Key("15.9 Name", "Why it is here")] = new Scoped(
            Verdict.Pass,
            "each reason that fired is a full sentence with the values beside it, a reason the mapping has no sentence for fails rather than rendering a default, and a name that fired nothing says it is not on tonight's list",
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
            "each reason on a drawn row is named with the values the store holds for it and the reason's own record beside it, in the dashed not-yet-measured state carrying its count against the minimum",
            ByReadSurface),
        [CheckReach.Key("15.7 Tonight", "Reason totals")] = new Scoped(
            Verdict.Pass,
            "the track across tonight's fired names, counted per reason off the stored listings, with every name on it in the unresolved state because nothing has scored tonight",
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
            "the class declares the eight stores it reads and the facts row it inserts, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
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
            "the class declares the two feeds it reads and the stores it touches, including the refetch delete and the series state SCHEMA now declares, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
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
            "a real captured action on a current member triggers a full-year refetch, the replacement is atomic, and a failure of the check itself marks the name suspect with its reason rather than passing",
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
            "the second book is keyed to the next dated event the calendar holds, a name with none produces no setups and says why, and every setup carries a trigger, an entry, a stop and a target with each figure stated on the page as a proposal",
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
            "the number the row states is read against the constant the finder uses, and the diff is run over a fixture holding a session the two lookbacks disagree about",
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
            "half a typical day's move is read off the stored average true range per name, and no two stored bands are closer to each other than that",
            ByExpectations),
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, the level bands")] = new Scoped(
            Verdict.Pass,
            "the bands are drawn behind the candles rather than over them, asserted by position in the markup, each carrying its role in words as well as in one of the two hues those roles own",
            ByReadSurface),
        [CheckReach.Key(CatalogueTable, "Level builder")] = new Scoped(
            Verdict.Pass,
            "the class declares the four stores it reads and the levels it writes, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Level builder")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key("15.5 The mark vocabulary", "Momentum panel")] = new Scoped(
            Verdict.Pass,
            "each reading is drawn on its own small axis with its neutral rule across it, counted off the rendered markup, and the rule's value is read from the arithmetic that defines the reading rather than chosen by the mark",
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
            "zero over the shipped source and zero on every stage the recorded night wrote to the run log",
            ByCost),
        [CheckReach.Key(LimitsTable, "Per-name network calls in the nightly run")] = new Scoped(
            Verdict.Pass,
            "the night is run twice over universes of three members and two, and the request count is one in both, so it is shown not to grow with the population rather than measured once",
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
        [CheckReach.Key(LimitsTable, "Per-request timeout and the night's deadline")] = new Scoped(
            Verdict.Pass,
            "the three attempts, the doubling wait and both bounds are read off the row and asserted against the policy the code uses, and a night given a deadline it cannot meet stops on the step it was on and says so",
            ByNight),
        [CheckReach.Key(LimitsTable, "Weighted-call budget")] = new Scoped(
            Verdict.Pass,
            "every weight and the allowance are read back out of RUNBOOK rather than repeated in code, the night reports its weighted total beside its request count, and a night already at the allowance stops before its next step",
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
        // nothing until 7.1 mentions forward returns in prose.
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

        // 1.2 names the run log, because that is where the backfill's request
        // count first reaches it. The catalogue row is not about one stage: its
        // Reads cell says "every component appends", so the row is a claim about
        // every component, and the last of them lands in phase 6.
        ["Run log"] = "6.7",

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

    };

    // Components the plan does not name. The catalogue and the matrix share it.
    static readonly Dictionary<string, string> Components = new(StringComparer.Ordinal)
    {
        // 4.2 builds the ladder and never uses the component's name.
        ["Ladder builder"] = "4.4",
    };

    static readonly Dictionary<string, string> Stores = new(StringComparer.Ordinal)
    {
        // One row over six tables, and it is owed where the last of them
        // arrives rather than where the first does. The move annotator at 5.2
        // is that point; indicators land at 3.1 and ladders at 4.2.
        ["Indicators, swings, volume profile, levels, ladders, moves"] = "5.2",
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
        [CheckReach.Key("15.7 Tonight", "The list, one row per name that fired")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "The list, ordered by how many fired then by band strength")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "The list, at most twenty drawn")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "The list, name")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "The list, close")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "The list, day change")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, trend state in a word")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, the distance row mark")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "The list, the reasons")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Selected name, the plan column")] = "5.4",
        [CheckReach.Key("15.7 Tonight", "Selected name, the level summary")] = "5.8",
        [CheckReach.Key("15.7 Tonight", "Selected name, whichever row is selected")] = "5.8",
        [CheckReach.Key("15.8 Universe", "Sector strip, one line per sector")] = "5.1",
        [CheckReach.Key("15.8 Universe", "Sector strip, how many are on tonight's list")] = "5.4",
        [CheckReach.Key("15.8 Universe", "Sector strip, names")] = "5.1",
        [CheckReach.Key("15.8 Universe", "Sector strip, how many are in an uptrend")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, every name in the index")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, the listing strip over sixty sessions")] = "5.4",
        [CheckReach.Key("15.8 Universe", "The table, sorted by distance to the nearest level ascending")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, paged")] = "5.8",
        [CheckReach.Key("15.8 Universe", "The table, name")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, sector")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, close")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, trend state")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, the distance row mark")] = "5.1",
        [CheckReach.Key("15.8 Universe", "The table, sessions until earnings")] = "5.8",
        [CheckReach.Key("15.8 Universe", "The table, the evening last on the list")] = "5.4",
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
        [CheckReach.Key("15.10 Run", "Operational header, what ran")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, the instant each stage started and how long it took")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, model calls")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, network requests")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, spend")] = "5.6",
        [CheckReach.Key("15.10 Run", "Operational header, what each stage said about itself")] = "5.6",
        [CheckReach.Key("15.10 Run", "Reason records, the resolved count")] = "5.6",
        [CheckReach.Key("15.10 Run", "Reason records, the share that reached target before stop")] = "7.5",
        [CheckReach.Key("15.10 Run", "Reason records, one row per reason with the reason track mark")] = "5.6",
        [CheckReach.Key("15.10 Run", "Reason records, the break-even those setups demanded")] = "7.5",
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
        [CheckReach.Key("15.8 Universe", "Filters")] = "5.1",

        [CheckReach.Key("15.9 Name", "Why it is here")] = "5.4",
        [CheckReach.Key("15.9 Name", "Fact strip")] = "6.1",
        [CheckReach.Key("15.9 Name", "The short version")] = "6.8",
        [CheckReach.Key("15.9 Name", "What it sells, the numbers, the cycle, the two cases, the risks")] = "6.8",
        [CheckReach.Key("15.9 Name", "Dates and sources")] = "6.8",
        [CheckReach.Key("15.9 Name", "Provenance footer")] = "6.6",
        [CheckReach.Key("15.9 Name", "Research not yet written")] = "6.5",
        [CheckReach.Key("15.9 Name", "Research stale")] = "6.5",
        [CheckReach.Key("15.9 Name", "Research paused")] = "6.7",
        [CheckReach.Key("15.10 Run", "Overnight queue")] = "6.10",
        [CheckReach.Key("15.9 Name", "Walk")] = "5.4",
        // 7.5 is "Reason verdicts on the run page" and 7.4 is "The shadow
        // column", so two rows of this section are owed two phases after it.
        [CheckReach.Key("15.10 Run", "Shadow candidates")] = "7.4",
        [CheckReach.Key("15.10 Run", "Stale and failed, names carrying yesterday's bars")] = "5.6",
        [CheckReach.Key("15.10 Run", "Stale and failed, the stage a night stopped on")] = "5.6",
        [CheckReach.Key("15.10 Run", "Stale and failed, sections that fell back")] = "6.4",
        [CheckReach.Key("15.10 Run", "Stale and failed, documents refused by admissibility")] = "6.3",

        [CheckReach.Key("15.11 How a reason's record is displayed", "Below the minimum")] = "7.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum")] = "7.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "Unresolved setups")] = "7.5",
        [CheckReach.Key("15.11 How a reason's record is displayed", "Never shown")] = "7.5",
    };

    // Contradiction F. Section 15.5's Level chart names four elements, candles,
    // the level bands, the moving averages and a volume pane, and the phase
    // table puts a chart in phase 1 while two of the four cannot exist until
    // phase 2. Resolved per element rather than per mark, so what exists is
    // asserted where it exists and only what does not stays out of scope.
    //
    // The decomposition lives here rather than in the document, and that is
    // deliberate. Section 15.5 opens by stating seven marks and the table has
    // seven rows; splitting the row into four would make the document disagree
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
            ["names in the index", "names that fired", "reports carrying fresh prose against reused", "spend", "run duration", "the harness verdict"],
        [CheckReach.Key("15.7 Tonight", "The list")] =
            ["one row per name that fired", "ordered by how many fired then by band strength", "at most twenty drawn", "name", "close", "day change", "trend state in a word", "the distance row mark", "the reasons"],
        [CheckReach.Key("15.7 Tonight", "Selected name")] =
            ["the plan column", "the level summary", "whichever row is selected"],
        [CheckReach.Key("15.8 Universe", "Sector strip")] =
            ["one line per sector", "how many are on tonight's list", "names", "how many are in an uptrend"],
        [CheckReach.Key("15.8 Universe", "The table")] =
            ["every name in the index", "the listing strip over sixty sessions", "sorted by distance to the nearest level ascending", "paged", "name", "sector", "close", "trend state", "the distance row mark", "sessions until earnings", "the evening last on the list"],
        [CheckReach.Key("15.9 Name", "The chart")] =
            ["the level chart", "the volume profile beside it on the same price axis", "the momentum panel beneath", "the level summary table with each band's members and dates"],
        [CheckReach.Key("15.9 Name", "The plan")] =
            ["the plan column mark", "the tranche table with conditions and stops", "the exit table with actions", "the earnings setups", "the sizing arithmetic"],
        [CheckReach.Key("15.9 Name", "How it got here")] =
            ["the table of the biggest moves", "the cause of each where research has been written", "the twelve-month picture"],
        [CheckReach.Key("15.10 Run", "Operational header")] =
            ["what ran", "the instant each stage started and how long it took", "model calls", "network requests", "spend", "what each stage said about itself"],
        [CheckReach.Key("15.10 Run", "Reason records")] =
            ["the resolved count", "the share that reached target before stop", "one row per reason with the reason track mark", "the break-even those setups demanded"],
        [CheckReach.Key("15.10 Run", "Harness")] =
            ["passed", "failed", "unexamined"],
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
        // read. Its verdicts need resolved setups and are 7.5's. Read as one
        // claim it would be owed at 7.5, and the obligations would name a
        // surface the harness says arrives two phases after the checkpoint the
        // plan says builds it.
        
        // The run page's stale-and-failed region, decomposed at the phase 5
        // sign-off. It was PASS whole from 5.6 while two of its parts describe
        // components phase 6 builds: sections that fell back are the claim
        // checker's at 6.3 and documents refused by admissibility are 6.2's. A
        // PASS over a row whose check reaches part of it is an unexamined claim
        // wearing a verdict, and the region itself says the research halves
        // are absent. The stage a night stopped on is the part the sign-off
        // repaired, since until then no night could put one there.
        [CheckReach.Key("15.10 Run", "Stale and failed")] =
            ["names carrying yesterday's bars", "the stage a night stopped on", "sections that fell back", "documents refused by admissibility"],

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
        ["fundamentals"] = "6.1",
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
        ["A condition has fired but nothing has resolved yet"] = "7.5",
        ["A search returns snippets rather than full page text"] = "6.9",
        ["A search returns a site the applicable list does not carry"] = "6.9",
        ["A document's publish date falls outside the window the pass asked for"] = "6.3",
        ["The search tool is unavailable"] = "6.9",
        ["The local model is unavailable"] = "6.6",
        ["A theme refresh fails while a name's pass depends on it"] = "6.9",
        ["Something tries to edit or delete a register row"] = "7.3",
        ["The candidate register and the correction disagree"] = "7.3",
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
        ["Tranches, exits"] = "4.5",
        ["Tranche eligibility"] = "4.4",
        ["Earnings horizon"] = "4.7",
        ["Nightly row coverage"] = "5.4",
        ["Reason record display"] = "7.5",
        ["Minimum resolved setups"] = "7.5",
        ["Family size and correction"] = "7.1",
        ["Frozen measurement windows"] = "7.6",
    };

    static readonly Dictionary<string, string> NightlySteps = new(StringComparer.Ordinal)
    {
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
        ["Evaluate the list reasons"] = "5.4",
        ["Run the overnight queue"] = "6.10",
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
        [CheckReach.Key("Figure 12.1", "Computed sections appear")] = "6.1",
        [CheckReach.Key("Figure 12.1", "Is there a research record?")] = "6.5",
        [CheckReach.Key("Figure 12.1", "Does it still stand?")] = "6.5",
        [CheckReach.Key("Figure 12.1", "All four no")] = "6.5",
        [CheckReach.Key("Figure 12.1", "A pass is warranted")] = "6.8",
        [CheckReach.Key("Figure 12.1", "Write the sections")] = "6.8",
        [CheckReach.Key("Figure 12.1", "Check every claim")] = "6.4",
        [CheckReach.Key("Figure 12.1", "Store it")] = "6.4",
    };

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
    // levels gave 5.1 against 3.4, and forward returns gave 7.1 against 5.5.
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
