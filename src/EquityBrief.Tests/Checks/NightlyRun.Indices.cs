using EquityBrief.Core.Time;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// nightly-run, 15.1: the night's six reports are taken in turn across the S&P 500's, 400's and 600's lists, one at a
// time in that order and each list in its own page's order, an index with no name left passing its turn to the next.
// see: The six reports a night are taken in turn across the three indices, one at a time in the page's order
public partial class NightlyRun
{
    [Fact]
    public void TheSixReportsANightAreTakenInTurnAcrossTheThreeIndicesOneAtATime()
    {
        // Every index with picks: two each, in turn, the S&P 500 first.
        Assert.Equal(
            [("A1", 0), ("B1", 1), ("C1", 2), ("A2", 0), ("B2", 1), ("C2", 2)],
            RequestDrain.TakenInTurn([["A1", "A2", "A3"], ["B1", "B2", "B3"], ["C1", "C2"]], 6));

        // The plan's own case: the S&P 600 lists nothing and the S&P 400 one, so the turns go 500, 400, 500, 500, 500,
        // 500, the 400 passing its second turn and the 600 every turn.
        Assert.Equal(
            [("A1", 0), ("B1", 1), ("A2", 0), ("A3", 0), ("A4", 0), ("A5", 0)],
            RequestDrain.TakenInTurn([["A1", "A2", "A3", "A4", "A5", "A6"], ["B1"], []], 6));

        // A name on two lists is taken once, under the first list to reach it; fewer names than six take every one.
        Assert.Equal(
            [("A1", 0), ("B1", 1), ("C2", 2), ("A2", 0)],
            RequestDrain.TakenInTurn([["A1", "A2"], ["B1"], ["A1", "C2"]], 6));
    }

    // The swing filter's step reads the S&P 400's and 600's provisional rules after the S&P 500's books, one night row
    // an index, under a stage of its own, the fixture's night holding none of either index's members.
    // see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
    [Fact]
    public async Task TheSwingFiltersStepReadsEachIndexsProvisionalRulesAfterTheSAndP500sBooks()
    {
        using var store = new TemporaryStore();

        var (code, _, error) = await NightAsync(store, launcher: new NightLauncherForTheSuite());

        Assert.True(code == 0, error);
        Assert.Equal("index-families", EquityBrief.Worker.Indices.IndexFamilies.Stage);

        var stages = Texts(store, "SELECT stage FROM run_log WHERE run_id LIKE 'night-%' AND instr(run_id, '-queue-') = 0 ORDER BY rowid;").ToList();

        Assert.Equal(1, stages.Count(stage => stage == EquityBrief.Worker.Indices.IndexFamilies.Stage));
        Assert.True(stages.IndexOf(EquityBrief.Worker.Families.HeavyweightBook.Stage) < stages.IndexOf(EquityBrief.Worker.Indices.IndexFamilies.Stage));
        // From 17.8 the S&P 500's four members are read first, for the fundamentals-first family alone.
        Assert.Equal(
            ["GSPC|4", "MID|0", "SML|0"],
            Texts(store, "SELECT index_code || '|' || members FROM index_family_night ORDER BY index_code;"));

        var detail = Texts(store, $"SELECT detail FROM run_log WHERE stage = '{EquityBrief.Worker.Indices.IndexFamilies.Stage}';").Single();

        Assert.StartsWith("GSPC: 4 member(s) read, ", detail, StringComparison.Ordinal);
        Assert.Contains(" passed by the fundamentals, ", detail, StringComparison.Ordinal);
        Assert.Contains("; MID: 0 member(s) read, breadth not read, the market check closed", detail, StringComparison.Ordinal);
    }

    // An S&P 400 book that throws: a holding whose buy close is not a price, which the book reads first. The S&P 400's
    // writes of the night are undone and its night row names the failure, the S&P 600 is read, the stage's row names
    // the failure under a word the run page draws, and the S&P 500's list, the rest of the night and its report step are
    // built regardless.
    // see: A failure in the S&P 400's or 600's part of the night is caught and named, and the S&P 500's night is built regardless
    [Fact]
    public async Task AnSAndP400BookThatThrowsLeavesTheSAndP500sListAndTheNightBuilt()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES " +
            "('MID', 'BROKEN', '2000-01-03', 'Energy', 'not a price', 1.0, '[]', '2000-01-03');");

        var (code, _, error) = await NightAsync(store, launcher: new NightLauncherForTheSuite());

        Assert.True(code == 0, error);

        const string Night = "run_id LIKE 'night-%' AND instr(run_id, '-queue-') = 0";
        var stage = EquityBrief.Worker.Indices.IndexFamilies.Stage;

        // The S&P 500's list drawn, its row ok, and the night closed and its report step run.
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM family_night;"));
        Assert.Equal(["ok"], Texts(store, $"SELECT outcome FROM run_log WHERE {Night} AND stage = '{EquityBrief.Worker.Families.FamilyLister.Stage}';"));
        Assert.Equal(["ok"], Texts(store, $"SELECT outcome FROM run_log WHERE {Night} AND stage = '{EquityBrief.Worker.Nights.NightClose.Stage}';"));
        Assert.Equal(1, Scalar(store, $"SELECT COUNT(*) FROM run_log WHERE {Night} AND stage = '{RequestDrain.NightStage}';"));

        // The S&P 400 not computed and named with its cause; the S&P 600 read.
        Assert.Equal([EquityBrief.Worker.Indices.IndexFamilies.NotComputed], Texts(store, $"SELECT outcome FROM run_log WHERE {Night} AND stage = '{stage}';"));

        var detail = Texts(store, $"SELECT detail FROM run_log WHERE {Night} AND stage = '{stage}';").Single();

        Assert.StartsWith("GSPC: 4 member(s) read, ", detail, StringComparison.Ordinal);
        Assert.Contains("; MID: not computed tonight, FormatException: ", detail, StringComparison.Ordinal);
        Assert.Contains("; SML: 0 member(s) read, breadth not read", detail, StringComparison.Ordinal);
        Assert.Equal(
            ["GSPC|4|", "MID|0|FormatException", "SML|0|"],
            Texts(store, "SELECT index_code || '|' || members || '|' || COALESCE(substr(fault, 1, instr(fault, ':') - 1), '') FROM index_family_night ORDER BY index_code;"));

        // The book's holding left as it was, carried by nothing.
        Assert.Equal(["2000-01-03"], Texts(store, "SELECT through FROM index_heavyweight_holding WHERE ticker = 'BROKEN';"));
    }

    [Fact]
    public async Task TheNightAsksForItsSixReportsInTurnAcrossTheThreeIndicesPagesAndNamesEachIndexsList()
    {
        using var store = new TemporaryStore().Migrated();

        // The S&P 500's families listed P1, P2 and P3. The S&P 400's listed M1 and its heavyweights bought M2 tonight;
        // the S&P 600's listed nothing, its heavyweights bought S1 tonight and carry S0 from a month before.
        store.Execute("INSERT INTO family_night (session_date, families) VALUES ('2026-10-01', '[\"pullback\",\"breakout\",\"drift\"]');");
        store.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES " +
            "('2026-10-01', 'P1', 'pullback', 'listed', 1, '[]', NULL, NULL), ('2026-10-01', 'P2', 'pullback', 'listed', 2, '[]', NULL, NULL), " +
            "('2026-10-01', 'P3', 'breakout', 'listed', 3, '[]', NULL, NULL);");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            "('MID', '2026-10-01', 'M1', 'breakout', 'listed', 1, '[]', NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES " +
            "('MID', 'M2', '2026-10-01', 'Energy', '10', 1.0, '[]', '2026-10-01'), " +
            "('SML', 'S1', '2026-10-01', 'Utilities', '10', 1.0, '[]', '2026-10-01'), " +
            "('SML', 'S0', '2026-09-01', 'Energy', '10', 1.1, '[]', '2026-10-01');");

        var ask = await RequestDrain.AskForTheNightAsync(store.DatabaseFile, new DateOnly(2026, 10, 1), FixedClock.At(new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        // The 500, the 400 and the 600 in turn, then the 500 and the 400 again, the 600 holding no second name and passing
        // its turn, then the 500's third.
        Assert.Equal(["P1", "M1", "S1", "P2", "M2", "P3"], ask.Asked);
        Assert.Contains("M2 is number 2 on the S&P 400's list, and a report on it was asked for", ask.Line, StringComparison.Ordinal);
        Assert.Contains("S1 is first on the S&P 600's list, and a report on it was asked for", ask.Line, StringComparison.Ordinal);
        Assert.Contains("P3 is number 3 on the list, and a report on it was asked for", ask.Line, StringComparison.Ordinal);

        // Six requests marked as asked by the night, and none for the holding carried.
        Assert.Equal(6, Scalar(store, "SELECT COUNT(*) FROM research_request WHERE asked_from = 'night';"));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM research_request WHERE ticker = 'S0';"));
    }
}
