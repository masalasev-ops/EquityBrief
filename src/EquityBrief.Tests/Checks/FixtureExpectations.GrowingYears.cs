using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the January pin move: the sweeps score every year from 2019 once its last session has closed, none
// dropped, eight through 2026 and nine once 2027 is complete; the year in progress is tallied in a slot of its own beside the
// scored years and counted in no figure, floor or verdict; and a floor stated as a count of the eight years is read as the
// same share of the scored years, rounded up, 6 of 8 and 7 of 9, a count of the last three years read as stated.
// see: A year is scored once it is complete, and the bar is three quarters of the scored years from a ninth
public partial class FixtureExpectations
{
    [Fact]
    public async Task AYearIsScoredOnceItsLastSessionHasClosedAndTheBarIsThreeQuartersOfTheScoredYears()
    {
        await Task.Yield();

        // The first scored year is the sweeps' own.
        Assert.Equal(SweepFigures.FirstYear, SweepColumns.FirstScored.Year);

        // By hand: today, and through 2027 while a session of it remains, eight years; from 2027's last session, Friday 31
        // December, nine.
        Assert.Equal(8, SweepFigures.ScoredThrough(new DateOnly(2026, 11, 26)));
        Assert.Equal(8, SweepFigures.ScoredThrough(new DateOnly(2027, 2, 5)));
        Assert.Equal(8, SweepFigures.ScoredThrough(new DateOnly(2027, 12, 30)));
        Assert.Equal(9, SweepFigures.ScoredThrough(new DateOnly(2027, 12, 31)));
        Assert.Equal(9, SweepFigures.ScoredThrough(new DateOnly(2028, 1, 3)));

        // Eight years: the bar 6 of 8, the plateau's 5, the blocks' 22 of 30, the year in progress the ninth slot.
        Assert.Equal((8, 6, 5, 30, 22, 8), (SweepFigures.Years, SweepFigures.Share(6), SweepFigures.Share(5), SweepFigures.WholeBlocks, SweepFigures.BlockShare(22), SweepFigures.InProgress));

        var six = new SweepSummary(300, 60, 50, 50, 1, 0.1f, 6, 6, true, 22, 0.7f, 0);
        var seven = six with { YearsBeatingBoth = 7, YearsBeatingBreakEven = 7, Blocks = 25 };

        Assert.True(six.Viable);
        Assert.True(six.MeetsTheFloors);

        // A trade of the year in progress tallies in its own slot and in none of the scored years' figures.
        var figures = new float[SweepFigures.Width];

        SweepFigures.Tally(figures, 0, 0, 1, 40, 45, 2, 0.5f);
        SweepFigures.Tally(figures, SweepFigures.InProgress, 40, 1, 40, 45, 3, 1.5f);

        var measures = SweepMeasures.Of(figures, 2, 2);

        Assert.Equal((1, 1), (measures.Scored, measures.Listed));
        Assert.Equal(0.5, measures.Edge!.Value, 6);
        Assert.Equal(new YearSoFar(1, 1, 1.5), measures.SoFar);
        Assert.Equal(1, measures.BlocksWithTrades);

        // Nine years once 2027 is complete: the bar 7 of 9, ceil(6 x 9 / 8); the plateau's 6, ceil(5 x 9 / 8); 34 whole blocks
        // and 25 of them, ceil(22 x 34 / 30); the year in progress the tenth slot. Six years beating no longer passes; seven do.
        SweepFigures.ScoreThrough(new DateOnly(2028, 1, 3));

        try
        {
            Assert.Equal((9, 7, 6, 34, 25, 9), (SweepFigures.Years, SweepFigures.Share(6), SweepFigures.Share(5), SweepFigures.WholeBlocks, SweepFigures.BlockShare(22), SweepFigures.InProgress));
            Assert.False(six.Viable);
            Assert.False(six.MeetsTheFloors);
            Assert.True(seven.Viable);
            Assert.True(seven.MeetsTheFloors);
            Assert.Equal(SweepFigures.BlockOffset + SweepFigures.Blocks, SweepFigures.Width);
            Assert.Equal(10 * SweepFigures.PerYear, SweepFigures.BlockOffset);
        }
        finally
        {
            SweepFigures.ScoreThrough(new DateOnly(2026, 11, 26));
        }

        Assert.Equal(8, SweepFigures.Years);
    }

    // see: A history-ends mark ends only the member span it falls in, and a mark inside a span the stock trades on past is settled by its filer and its prices
    [Fact]
    public void TheSweepsHistoryReadsAHistoryEndsMarkAsEndingOnlyTheMemberSpanItFallsIn()
    {
        static SweepBar Bar(int day) => new(new DateOnly(2021, 4, day), 10m + day, 10m + day, 10m + day, 1000, 10m + day);

        static int[] Days(IEnumerable<SweepBar> series) => [.. series.Select(bar => bar.Session.Day)];

        static DateOnly On(int day) => new(2021, 4, day);

        SweepBar[] pulled = [Bar(1), Bar(5), Bar(6)];
        SweepBar[] stored = [Bar(7), Bar(8), Bar(9)];
        IReadOnlyList<Core.Chart.SessionMark> ends = [new(On(6), Core.Chart.SessionMarking.HistoryEnds, "Its history ends here.")];
        (DateOnly?, DateOnly?)[] reused = [(null, On(6)), (On(8), null)];

        // By hand: a mark on the 6th with one company's span to the 6th and another's from the 8th parts the code in two, the
        // first reading the 1st to the 6th over its span and the second the 7th to the 9th over its own, never the bars before.
        var parts = SweepHistory.SpanAware(pulled, stored, ends, reused, readThrough: false);

        Assert.Equal(2, parts.Count);
        Assert.Equal([1, 5, 6], Days(parts[0].Bars));
        Assert.Equal([((DateOnly?)null, (DateOnly?)On(6))], parts[0].Spans);
        Assert.Equal([7, 8, 9], Days(parts[1].Bars));
        Assert.Equal([((DateOnly?)On(8), (DateOnly?)null)], parts[1].Spans);

        // One span before the mark: the bars after it go, as before the move. Read through: one series over both spans. No mark:
        // the series merged as it stood.
        var once = Assert.Single(SweepHistory.SpanAware(pulled, stored, ends, [(null, null)], readThrough: false));

        Assert.Equal([1, 5, 6], Days(once.Bars));
        Assert.Equal(Days(SweepHistory.UnderMarks(pulled, stored, ends)), Days(once.Bars));
        Assert.Equal([1, 5, 6, 7, 8, 9], Days(Assert.Single(SweepHistory.SpanAware(pulled, stored, ends, reused, readThrough: true)).Bars));
        Assert.Equal([1, 5, 6, 7, 8, 9], Days(Assert.Single(SweepHistory.SpanAware(pulled, stored, null, reused, readThrough: false)).Bars));
    }
}
