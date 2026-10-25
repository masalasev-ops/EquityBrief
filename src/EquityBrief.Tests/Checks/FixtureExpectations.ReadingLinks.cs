using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.12: which readings go with winners worked by hand over a constructed ledger, the shuffle's
// 95th percentile over every arrangement included, a reading going with losers, too few setups in a year and a reading
// held level read as too few, two years drawn at the seed past the arrangements read whole; and the tester's readings'
// step over a constructed store writing every reading's row for each family with its run log row, and printing and
// writing nothing with print.
// see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
public partial class FixtureExpectations
{
    // The row 19.12 adds that this check reaches: section 17's table of which readings go with winners.
    internal static readonly string[] ReadingLinkClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Which readings go with winners"),
    ];

    // A year's settled setups holding the last factor, the reading 1 and up in order: the first ten two a night on five
    // nights from the year's first trading day, and every other on a night of its own from February, each result the
    // function's of its place.
    static LinkedSetup[] LinkedYear(int year, int count, Func<int, double> result, Func<int, double>? reading = null) =>
    [
        .. Enumerable.Range(0, count).Select(at =>
        {
            var readings = new double?[LedgerReadings.Count];

            readings[LedgerReadings.IndexOf("alpha_six")] = reading?.Invoke(at) ?? at + 1;

            return new LinkedSetup(at < 10 ? new DateOnly(year, 1, 3).AddDays(at / 2) : new DateOnly(year, 2, 1).AddDays(at - 10), result(at), readings);
        }),
    ];

    [Fact]
    public void WhichReadingsGoWithWinnersIsWorkedByHandOverAConstructedLedgerItsShufflesPercentileIncluded()
    {
        var alpha = LedgerReadings.IndexOf("alpha_six");

        // Thirty setups of 2023 whose results rank as their readings do: a Spearman correlation of one over the one test
        // year, pointing its way. Shuffling each two-setup night's results swaps two ranks a place apart, so k swaps put the
        // correlation at 1 - 6 x 2k / (30 x 899) = 1 - 12k/26970; the 32 arrangements of the five nights are read whole, and
        // sorted the 31st of them by its nearest rank is one of the five single swaps', 1 - 12/26970, under which the one
        // stands: it goes with winners.
        var winners = ReadingLinks.Of(LinkedYear(2023, 30, at => at + 1), alpha);

        Assert.Equal(("alpha_six", 30, 1), (winners.Reading, winners.Setups, winners.Years));
        Assert.Equal(1.0, winners.Correlation!.Value, 12);
        Assert.Equal(1.0, winners.SameWay);
        Assert.Equal(1 - (12.0 / 26970), winners.Shuffled!.Value, 12);
        Assert.Equal(ReadingLinks.Winners, winners.Words);

        // The results in the reverse order: minus one, its size above the same percentile, so it goes with losers.
        var losers = ReadingLinks.Of(LinkedYear(2023, 30, at => -at), alpha);

        Assert.Equal(-1.0, losers.Correlation!.Value, 12);
        Assert.Equal(1 - (12.0 / 26970), losers.Shuffled!.Value, 12);
        Assert.Equal(ReadingLinks.Losers, losers.Words);

        // One setup short of the floor in the year, or the reading held level throughout it, and no year counts; a year
        // before the first test year is not read.
        Assert.Equal((0, ReadingLinks.TooFew), (ReadingLinks.Of(LinkedYear(2023, 29, at => at), alpha).Years, ReadingLinks.Of(LinkedYear(2023, 29, at => at), alpha).Words));
        Assert.Equal(ReadingLinks.TooFew, ReadingLinks.Of(LinkedYear(2023, 30, at => at, _ => 4), alpha).Words);
        Assert.Equal((0, 0, ReadingLinks.TooFew), (ReadingLinks.Of(LinkedYear(2021, 30, at => at), alpha).Setups, ReadingLinks.Of(LinkedYear(2021, 30, at => at), alpha).Years, ReadingLinks.Of(LinkedYear(2021, 30, at => at), alpha).Words));

        // A reading the setups do not hold is too few, with no setup counted.
        var unheld = ReadingLinks.Of(LinkedYear(2023, 30, at => at), LedgerReadings.IndexOf("rsi"));

        Assert.Equal((0, 0, null, ReadingLinks.TooFew), (unheld.Setups, unheld.Years, unheld.Correlation, unheld.Words));

        // Two such years: their 1,024 arrangements are more than the draws, so 199 are drawn at the seed, the same each
        // time, every one under the mean of one, which goes with winners over two years both pointing its way.
        LinkedSetup[] two = [.. LinkedYear(2023, 30, at => at), .. LinkedYear(2024, 30, at => at)];
        var drawn = ReadingLinks.Of(two, alpha);

        Assert.Equal((2, 1.0, ReadingLinks.Winners), (drawn.Years, drawn.SameWay!.Value, drawn.Words));
        Assert.True(drawn.Shuffled!.Value < 1, $"The drawn percentile was {drawn.Shuffled}.");
        Assert.Equal(drawn.Shuffled, ReadingLinks.Of(two, alpha).Shuffled);

        // Every reading of the catalogue holds a row, in its order.
        Assert.Equal(LedgerReadings.All.Select(reading => reading.Column), ReadingLinks.Of(two).Select(link => link.Reading));
    }

    [Fact]
    public async Task TheTestersReadingsStepWritesEveryReadingsRowForEachFamilyWithItsRunLogRowAndPrintWritesNothing()
    {
        using var store = new TemporaryStore().Migrated();

        // The thirty setups of 2023 above, settled on the S&P 500 under the pullback, and a breakout setup still open.
        foreach (var (setup, at) in LinkedYear(2023, 30, at => at + 1).Select((setup, at) => (setup, at)))
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, result, end, settled, source, pin, alpha_six) VALUES "
                + FormattableString.Invariant($"('GSPC', 'pullback', 'T{at:00}', '{setup.Session:yyyy-MM-dd}', 'the live rule', 1, NULL, '100', '95', '110', NULL, 21, {setup.Result}, 'target', 1, 'history', 'x', {setup.Readings[LedgerReadings.IndexOf("alpha_six")]});"));
        }

        store.Execute("INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, end, settled, source, pin) VALUES ('GSPC', 'breakout', 'OPEN', '2026-10-01', 'the live rule', 1, NULL, '100', '95', NULL, '3', 63, 'open', 0, 'night', 'x');");

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var printed = new StringWriter();

        Assert.Equal(0, await new WalkForwardTester(clock, store.DatabaseFile, store.Root, printed).LinksAsync("GSPC", "2026-10", print: true));
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM loop_link;"));
        Assert.Contains("pullback, alpha_six: goes with winners, 1.000 over 1 test year(s)", printed.ToString(), StringComparison.Ordinal);

        var output = new StringWriter();

        Assert.Equal(0, await new WalkForwardTester(clock, store.DatabaseFile, store.Root, output).LinksAsync("GSPC", "2026-10"));

        // One row a reading the month's run reads for the pullback alone, the family holding settled setups, the
        // scorecard's readings of 22.8 joining only from December: the last factor's goes with winners over its thirty
        // setups, and every other reading, held by none of them, too few.
        Assert.Equal((long)LedgerReadings.IndexOf(LedgerReadings.ScorecardFirst), Scalar(store, "SELECT COUNT(*) FROM loop_link WHERE family = 'pullback' AND month = '2026-10' AND index_code = 'GSPC';"));
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM loop_link WHERE family <> 'pullback';"));
        Assert.Equal(
            ["alpha_six|30|1|goes with winners"],
            Query(store, "SELECT reading || '|' || setups || '|' || years || '|' || words FROM loop_link WHERE words <> 'too few settled setups in a test year to read';"));
        Assert.Equal(
            ["loop-links|85|1 family(ies) on the S&P 500 for 2026-10: 1 reading(s) going with winners, 0 with losers, 0 with no link beyond the shuffle and 84 too few to read; no reading version's history read is complete, so no release answer was worked"],
            Query(store, "SELECT stage || '|' || rows_written || '|' || detail FROM run_log WHERE stage = 'loop-links';"));
        Assert.StartsWith("loop-links-GSPC-20261016T120000Z", Query(store, "SELECT DISTINCT run_id FROM loop_link;").Single(), StringComparison.Ordinal);

        // An index no run reads is refused with nothing written.
        Assert.Equal(2, await new WalkForwardTester(clock, store.DatabaseFile, store.Root, new StringWriter()).LinksAsync("NDX", "2026-10"));

        // December's run reads the scorecard's readings beside every other.
        var december = FixedClock.At(new DateTimeOffset(2026, 12, 5, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Assert.Equal(0, await new WalkForwardTester(december, store.DatabaseFile, store.Root, new StringWriter()).LinksAsync("GSPC", "2026-12"));
        Assert.Equal((long)LedgerReadings.Count, Scalar(store, "SELECT COUNT(*) FROM loop_link WHERE family = 'pullback' AND month = '2026-12' AND index_code = 'GSPC';"));
    }
}
