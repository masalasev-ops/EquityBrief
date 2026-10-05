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
