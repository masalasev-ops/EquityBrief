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
        [CheckReach.Key(CatalogueTable, "Migration runner")] = new Scoped(
            Verdict.Pass,
            "the schema it writes is asserted against SCHEMA.md, column by column and type by type",
            ByMigration),
        [CheckReach.Key(CatalogueTable, "Verification harness")] = new Scoped(
            Verdict.Pass,
            "every claim in sections 7, 14, 15, 16, 17 and 18 carries a verdict, and both artifacts are written and read back",
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
            "the row is eleven blanks, the runner declares no store, and no statement against a declared table appears in its source",
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
        [CheckReach.Key("15.4 The two surfaces", "The app")] = new Scoped(
            Verdict.Pass,
            "the shell routes on the hash and carries no drawing element of its own, so the marks it shows are the server's",
            ByReadSurface),
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart, candles")] = new Scoped(
            Verdict.Pass,
            "one candle is drawn per stored session, counted off the rendered markup and matched session by session against the store, hollow above the open and filled below in neutral ink",
            ByReadSurface),
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
            "a night whose feed does not answer stores nothing, leaves the bars it already held exactly as they were, names the step and exits non-zero",
            ByNight),
        [CheckReach.Key(FailureTable, "A feed answers with a session other than the one asked for")] = new Scoped(
            Verdict.Pass,
            "a payload carrying a session other than the requested one is refused inside the feed, naming both dates, and the stored series is unchanged",
            ByNight),
        [CheckReach.Key(FailureTable, "A feed answers with none of the index in it")] = new Scoped(
            Verdict.Pass,
            "a payload carrying nothing for any current member is refused by the fetcher before the transaction opens, and one short of some but not all is stored for the rest with the count carried out of the stage",
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
        [CheckReach.Key(NightlyRunSteps.Heading, "Fetch the day's bulk bar file, one request, and store the bars for current members.")] = new Scoped(
            Verdict.Pass,
            "the night runs it after the backfill, in one request, storing the day for current members only",
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
        ["Source lists"] = "6.1",

        // 1.2 names the run log, because that is where the backfill's request
        // count first reaches it. The catalogue row is not about one stage: its
        // Reads cell says "every component appends", so the row is a claim about
        // every component, and the last of them lands in phase 6.
        ["Run log"] = "phase 7",

        // 1.2 builds the backfill, and this row is the limit on it rather than
        // the component. Its own Asserted by column names the run log's request
        // count against the names lacking history, which is nightly-cost reading
        // a recorded run, and that arrives at 1.4.
        ["Backfill"] = "1.4",

    };

    // Components the plan does not name. The catalogue and the matrix share it.
    static readonly Dictionary<string, string> Components = new(StringComparer.Ordinal)
    {
        // 4.2 builds the ladder and never uses the component's name.
        ["Ladder builder"] = "4.2",
        ["Report exporter"] = "phase 6",
    };

    static readonly Dictionary<string, string> Stores = new(StringComparer.Ordinal)
    {
        // One row over six tables, and it is owed where the last of them
        // arrives rather than where the first does. The move annotator at 5.2
        // is that point; indicators land at 3.1 and ladders at 4.2.
        ["Indicators, swings, volume profile, levels, ladders, moves"] = "5.2",
        ["Research store"] = "phase 6",
        ["Theme store"] = "phase 6",
        ["Source documents"] = "phase 6",
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
        [CheckReach.Key("15.4 The two surfaces", "The app")] = "1.3",
        // Contradiction D's own case. The exporter is a phase 6 component and
        // this row is the surface it writes.
        [CheckReach.Key("15.4 The two surfaces", "The exported report")] = "phase 6",

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
        [CheckReach.Key("15.5 The mark vocabulary", "Plan column")] = "4.4",
        [CheckReach.Key("15.5 The mark vocabulary", "Momentum panel")] = "3.5",
        // Drawn from levels, which arrive at 3.4, and used by the universe
        // and tonight screens. 5.1 is where the first of those exists, and a
        // mark with no screen to sit on is a mark nothing can be asserted about.
        [CheckReach.Key("15.5 The mark vocabulary", "Distance row")] = "5.1",
        // Both need a listing and a reason behind them, which phase 5 is the
        // first to write.
        [CheckReach.Key("15.5 The mark vocabulary", "Reason track")] = "phase 5",
        [CheckReach.Key("15.5 The mark vocabulary", "Listing strip")] = "phase 5",

        [CheckReach.Key("15.7 Tonight", "Night header")] = "phase 5",
        [CheckReach.Key("15.7 Tonight", "Watch list")] = "phase 5",
        [CheckReach.Key("15.7 Tonight", "The list")] = "phase 5",
        [CheckReach.Key("15.7 Tonight", "Reasons, per row")] = "phase 5",
        [CheckReach.Key("15.7 Tonight", "Selected name")] = "phase 5",
        [CheckReach.Key("15.7 Tonight", "Reason totals")] = "phase 5",

        [CheckReach.Key("15.8 Universe", "Sector strip")] = "phase 5",
        [CheckReach.Key("15.8 Universe", "The table")] = "phase 5",
        [CheckReach.Key("15.8 Universe", "Filters")] = "phase 5",

        [CheckReach.Key("15.9 Name", "Why it is here")] = "phase 6",
        [CheckReach.Key("15.9 Name", "Fact strip")] = "phase 6",
        [CheckReach.Key("15.9 Name", "The short version")] = "phase 6",
        [CheckReach.Key("15.9 Name", "How it got here")] = "phase 6",
        [CheckReach.Key("15.9 Name", "The chart")] = "phase 6",
        [CheckReach.Key("15.9 Name", "The plan")] = "phase 6",
        [CheckReach.Key("15.9 Name", "What it sells, the numbers, the cycle, the two cases, the risks")] = "phase 6",
        [CheckReach.Key("15.9 Name", "Dates and sources")] = "phase 6",
        [CheckReach.Key("15.9 Name", "Provenance footer")] = "phase 6",
        [CheckReach.Key("15.9 Name", "Walk")] = "phase 6",

        [CheckReach.Key("15.10 Run", "Operational header")] = "phase 5",
        // 7.5 is "Reason verdicts on the run page" and 7.4 is "The shadow
        // column", so two rows of this section are owed two phases after it.
        [CheckReach.Key("15.10 Run", "Reason records")] = "7.5",
        [CheckReach.Key("15.10 Run", "Shadow candidates")] = "7.4",
        [CheckReach.Key("15.10 Run", "Stale and failed")] = "phase 5",
        [CheckReach.Key("15.10 Run", "Harness")] = "phase 5",

        [CheckReach.Key("15.11 How a reason's record is displayed", "Below the minimum")] = "phase 7",
        [CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum")] = "phase 7",
        [CheckReach.Key("15.11 How a reason's record is displayed", "Unresolved setups")] = "phase 7",
        [CheckReach.Key("15.11 How a reason's record is displayed", "Never shown")] = "phase 7",
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
        [CheckReach.Key("15.5 The mark vocabulary", "Level chart")] =
            ["candles", "the level bands", "the moving averages", "a volume pane"],

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
        ["indicators"] = "3.1",
        ["swings"] = "3.2",
        ["volume profile"] = "3.3",
        ["levels"] = "3.4",
        ["ladder"] = "4.2",
        ["listings"] = "5.4",
        ["facts"] = "5.3",
        ["a poisoned paragraph"] = "6.3",
        ["an unsourced claim"] = "6.3",
        ["an inadmissible document"] = "6.2",
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
        ["A gap in one name's series, level and plan sections"] = "4.4",
        ["A split or dividend not caught"] = "1.6",
        ["Cloud model unavailable"] = "phase 6",
        ["A source is returned but its text cannot be retrieved"] = "phase 6",
        ["A pass finds no admissible source for a section"] = "phase 6",
        ["Claim checker rejects twice"] = "phase 6",
        ["Spend cap reached"] = "phase 6",
        ["Filing not yet parsed for a name"] = "phase 6",
        ["No band is eligible to carry a tranche"] = "phase 4",
        ["Earnings date missing"] = "phase 4",
        // The store half is reached at 3.1 by fixture-expectations, so only the
        // other element is owed. The name page's fact strip is where
        // "not available, nn bars" is read, and 3.5 is the checkpoint that first
        // draws an indicator-derived region of that page.
        ["Fewer than 200 bars for a new index member, nn bars"] = "3.5",
        // The row's "What you see" cell claims the name disappears from the
        // universe screen, which is 5.1. 1.1 records the leave date and asserts
        // nothing a reader looks at.
        ["A name leaves the index"] = "5.1",
        ["A condition has fired but nothing has resolved yet"] = "phase 7",
        ["A section is assigned to the local lane that the machine cannot hold"] = "phase 6",
        ["The machine slept and the overnight queue did not run"] = "phase 6",
        ["Something tries to edit or delete a register row"] = "phase 7",
        ["The candidate register and the correction disagree"] = "phase 7",
    };

    // The two rows of the read and write matrix whose component already exists.
    // Every other row resolves through Components, which the catalogue shares.
    // A matrix row claims what its component touches across eleven stores, and
    // eight of those stores are not built, so the row is not assertable until
    // the last of them is.
    static readonly Dictionary<string, string> MatrixRows = new(StringComparer.Ordinal)
    {
        ["Verification harness"] = "phase 7",
    };

    // Section 17's limits. Each row is a claim about the code, and the code
    // that would carry it arrives with the component the row constrains.
    static readonly Dictionary<string, string> LimitDuePoints = new(StringComparer.Ordinal)
    {
        // nightly-cost is implemented at 1.4, over the shipped source and a
        // recorded run, which is the first point either limit is asserted.
        ["Model calls in the nightly run"] = "1.4",
        ["Per-name network calls in the nightly run"] = "1.4",
        ["Nightly wall clock, 500 names"] = "phase 5",
        // Retention is what makes the year a limit rather than a description,
        // and it lands with the fetcher at 1.4.
        ["Bar history kept"] = "1.4",
        ["Level window"] = "3.4",
        ["Swing lookback"] = "3.2",
        ["Band merge distance"] = "3.4",
        ["Tranches, exits"] = "phase 4",
        ["Tranche eligibility"] = "phase 4",
        ["Earnings horizon"] = "phase 4",
        ["List display"] = "phase 5",
        ["Research passes per name per open"] = "phase 6",
        ["Research staleness triggers"] = "phase 6",
        ["Scheduling of queued work"] = "phase 6",
        ["Claim rejection"] = "6.1",
        ["Theme search parameters"] = "phase 6",
        ["Source admissibility"] = "6.1",
        ["Nightly row coverage"] = "5.1",
        ["Reason record display"] = "phase 7",
        ["Minimum resolved setups"] = "phase 7",
        ["Family size and correction"] = "7.1",
        ["Frozen measurement windows"] = "phase 7",
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
        ["For every name"] = "phase 5",
        ["Fill forward returns"] = "phase 5",
        ["Count today"] = "phase 5",
        ["Close the arithmetic"] = "phase 5",
        ["Run the overnight queue"] = "phase 6",
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
    internal static IReadOnlyList<string> ResidualSubjects() =>
        [.. DerivedIsLate.Keys, .. DerivedIsEarly.Keys, .. Components.Keys, .. Stores.Keys,
            .. Failures.Keys, .. MatrixRows.Keys, .. LimitDuePoints.Keys, .. NightlySteps.Keys];

    internal static IReadOnlyList<string> DeclaredExceptions() =>
        [.. DerivedIsLate.Keys, .. DerivedIsEarly.Keys];

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
