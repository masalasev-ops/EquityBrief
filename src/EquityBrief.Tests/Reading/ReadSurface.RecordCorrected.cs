using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The operator's ruling of 2026-11-17, item 5: for a month after the history the records are read over was corrected, a
// record whose newest run started on or after the correction's day says beneath its figure the day and the figure it stood at
// before, read off the newest run started before that day; a book's in percentage points a month; none before the correction,
// after the month, or where no run before it held a figure.
// see: A record corrected after a fault in the history says so beneath its figure for a month, with the figure it stood at before
public partial class ReadSurface
{
    static readonly DateOnly Corrected = MarkRenderer.RecordCorrections[^1];

    static string Stamp(DateOnly day, int hour) => FormattableString.Invariant($"{day:yyyyMMdd}T{hour:00}0000Z");

    static ReadApi ApiOn(TemporaryStore store, DateOnly day) =>
        new(store.DatabaseFile, FixedClock.At(new DateTimeOffset(day.ToDateTime(new TimeOnly(22, 0)), TimeSpan.Zero), SessionZones.UnitedStates));

    [Fact]
    public async Task ACorrectedRecordSaysBeneathItsFigureForAMonthWhatItWasBeforeTheCorrection()
    {
        using var store = new TemporaryStore().Migrated();

        // The pullback's run before the correction made $0.50; its run after it lost $0.124. The breakout's runs both came after
        // it, so nothing says what it was. The S&P 400's heavyweights book was 0.35 points a month more and is now 0.20 less.
        store.Execute(FormattableString.Invariant($@"
            INSERT INTO plan_point (run_id, index_code, family, through, setups, picks, traded_later_edge_per_pick, traded_later_picks) VALUES
                ('plan-points-GSPC-{Stamp(Corrected.AddDays(-9), 3)}', 'GSPC', 'pullback', '2026-11-05', 10, 5, 0.5, 999),
                ('plan-points-GSPC-{Stamp(Corrected, 2)}', 'GSPC', 'pullback', '2026-11-18', 10, 5, -0.124, 2933),
                ('plan-points-GSPC-{Stamp(Corrected, 2)}', 'GSPC', 'breakout', '2026-11-18', 10, 5, 0.186, 517);
            INSERT INTO book_record (run_id, index_code, family, rule, exit_words, from_year, through, months, points) VALUES
                ('loop-books-MID-{Stamp(Corrected.AddDays(-3), 4)}', 'MID', 'heavyweight', 'the rule', 'the exit', 2023, '2026-11-05', 34, 0.35),
                ('loop-books-MID-{Stamp(Corrected, 5)}', 'MID', 'heavyweight', 'the rule', 'the exit', 2023, '2026-11-18', 35, -0.2);"));

        var inside = await ApiOn(store, Corrected.AddDays(10)).SetupRecordsAsync();
        var pullback = inside.Of("GSPC", SetupFamilies.Pullback)!;
        var book = inside.Of("MID", HeavyweightRule.Name)!;

        Assert.Equal((Corrected, 0.5), (pullback.CorrectedOn!.Value, pullback.Was!.Value));
        Assert.Null(inside.Of("GSPC", BreakoutRule.Name)!.CorrectedOn);
        Assert.Equal(0.35, book.Was);

        string Line(SetupLongRecord record) => Read(MarkRenderer.CorrectedLine(record));

        Assert.Equal(FormattableString.Invariant($"Corrected on {Corrected:d MMM yyyy} after a fault in the history; was $0.50."), Line(pullback));
        Assert.Equal(FormattableString.Invariant($"Corrected on {Corrected:d MMM yyyy} after a fault in the history; was 0.35 percentage points a month more."), Line(book));
        Assert.Equal(FormattableString.Invariant($"Corrected on {Corrected:d MMM yyyy} after a fault in the history; was a loss of $0.12."), Line(pullback with { Was = -0.124 }));

        // The box draws it beneath the figure and above the rule's note.
        var box = Read(MarkRenderer.SetupRecordBox(pullback));

        Assert.Contains("picks like this lost about $0.12 for every $1 risked", box, StringComparison.Ordinal);
        Assert.True(box.IndexOf("Corrected on", StringComparison.Ordinal) < box.IndexOf(MarkRenderer.RecordNote, StringComparison.Ordinal));

        // The day before the correction and a month after it, nothing is said.
        foreach (var day in new[] { Corrected.AddDays(-1), Corrected.AddMonths(1) })
        {
            var outside = await ApiOn(store, day).SetupRecordsAsync();

            Assert.Null(outside.Of("GSPC", SetupFamilies.Pullback)!.CorrectedOn);
            Assert.Equal(string.Empty, MarkRenderer.CorrectedLine(outside.Of("MID", HeavyweightRule.Name)!));
        }

        Assert.DoesNotMatch(new Regex("Corrected on"), MarkRenderer.SetupRecordBox(inside.Of("GSPC", BreakoutRule.Name)));
    }

    // The operator's answer of 2026-11-17: the Run page lists each step in a stock's closes the share check reads as no split,
    // with the stock and the date, and each split the split events or the pulled splits hold whose closes show no step; a stock
    // holding no bar on the night is not read, and a step a table holds is listed nowhere.
    // see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
    [Fact]
    public async Task TheRunPageListsTheStepsTheShareCheckReadsAsNoSplitAndTheHeldSplitsTheClosesDoNotShow()
    {
        using var store = new TemporaryStore().Migrated();
        DateOnly first = new(2026, 9, 1), night = new(2026, 9, 30);

        // AAA steps by half on 3 Sep with no split held, and holds a split on 20 Sep its closes do not show. BBB steps by half on
        // 3 Sep with a pulled split two days later. CCC steps and holds nothing, but holds no bar on the night.
        void Bar(string ticker, DateOnly day, string close, string raw) =>
            store.Execute(FormattableString.Invariant($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{day:yyyy-MM-dd}', '{close}', '{close}', '{close}', '{close}', 1000, 'constructed', '{day:yyyy-MM-dd}T21:00:00Z', '{raw}');"));

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            Bar(ticker, first, "50", "100");
            Bar(ticker, first.AddDays(2), "51", "51");
            Bar(ticker, first.AddDays(25), "52", "52");
        }

        Bar("AAA", night, "53", "53");
        Bar("BBB", night, "53", "53");
        store.Execute("INSERT INTO split_event (ticker, ex_date, new_shares, old_shares, run_id) VALUES ('AAA', '2026-09-20', '2', '1', 'night');");
        store.Execute("INSERT INTO pulled_split (ticker, ex_date, new_shares, old_shares, pull) VALUES ('BBB', '2026-09-05', '2', '1', 'pull');");

        var rows = await Api(store).SplitStepsOnAsync(night);

        Assert.Equal([new SplitStepRow("AAA", first.AddDays(2), 0.5m), new SplitStepRow("AAA", new DateOnly(2026, 9, 20), null)], rows);

        var drawn = Read(MarkRenderer.SplitSteps(rows));

        Assert.Contains("A jump in the price adjustment with no split in the split tables is not read as a split (1):AAA: 3 Sep 2026, a step of 0.5", drawn, StringComparison.Ordinal);
        Assert.Contains("A split in the split tables with no matching jump in the prices is not read either (1):AAA: 20 Sep 2026", drawn, StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.SplitSteps([]));
    }
}
