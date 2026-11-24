using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The operator's decision of 2026-11-18, D: for a month after the history the records are read over was corrected, a record
// whose newest run started on or after the correction's day says beneath its figure the trades the correction added and removed
// under its rule where it changed any, and that it was checked and changed none where it changed none, a book's as a price
// family's; nothing before the correction, after the month, or on a record whose newest run started before the day.
// see: A record the history was corrected under says for a month how many of its rule's trades the correction added and removed, or that none changed
public partial class ReadSurface
{
    static readonly RecordCorrection Correction = MarkRenderer.RecordCorrections[^1];

    static readonly DateOnly Corrected = Correction.On;

    static string Stamp(DateOnly day, int hour) => FormattableString.Invariant($"{day:yyyyMMdd}T{hour:00}0000Z");

    static ReadApi ApiOn(TemporaryStore store, DateOnly day) =>
        new(store.DatabaseFile, FixedClock.At(new DateTimeOffset(day.ToDateTime(new TimeOnly(22, 0)), TimeSpan.Zero), SessionZones.UnitedStates));

    [Fact]
    public async Task ARecordSaysForAMonthTheTradesTheCorrectionAddedAndRemovedOrThatItCheckedAndNoneChanged()
    {
        using var store = new TemporaryStore().Migrated();

        // The S&P 600's dip, whose trades the correction of 18 Nov changed, and the S&P 500's pullback, whose trades it did not,
        // both run on the day; the S&P 500's breakout last ran before it. The S&P 400's heavyweights book ran on the day.
        store.Execute(FormattableString.Invariant($@"
            INSERT INTO plan_point (run_id, index_code, family, through, setups, picks, traded_later_edge_per_pick, traded_later_picks) VALUES
                ('plan-points-SML-{Stamp(Corrected.AddDays(-2), 3)}', 'SML', 'dip', '2026-11-13', 10, 5, 0.05, 35523),
                ('plan-points-SML-{Stamp(Corrected, 2)}', 'SML', 'dip', '2026-11-17', 10, 5, 0.0503, 36567),
                ('plan-points-GSPC-{Stamp(Corrected, 2)}', 'GSPC', 'pullback', '2026-11-17', 10, 5, -0.124, 2933),
                ('plan-points-GSPC-{Stamp(Corrected.AddDays(-2), 2)}', 'GSPC', 'breakout', '2026-11-13', 10, 5, 0.186, 517);
            INSERT INTO book_record (run_id, index_code, family, rule, exit_words, from_year, through, months, points) VALUES
                ('loop-books-MID-{Stamp(Corrected, 5)}', 'MID', 'heavyweight', 'the rule', 'the exit', 2023, '2026-11-17', 35, -0.2);"));

        var inside = await ApiOn(store, Corrected.AddDays(10)).SetupRecordsAsync();
        var dip = inside.Of("SML", DipRule.Name)!;
        var pullback = inside.Of("GSPC", SetupFamilies.Pullback)!;
        var book = inside.Of("MID", HeavyweightRule.Name)!;

        Assert.Equal(Correction, dip.Correction);
        Assert.Null(inside.Of("GSPC", BreakoutRule.Name)!.Correction);

        string Line(SetupLongRecord record) => Read(MarkRenderer.CorrectedLine(record));

        Assert.Equal((1018, 23), Correction.Trades[("SML", DipRule.Name)]);
        Assert.Equal("Corrected on 18 Nov 2026: 1018 trades added, 23 removed, from a fix to how dollar volume was read.", Line(dip));
        Assert.Equal("Corrected on 18 Nov 2026: 169 trades added, 0 removed, from a fix to how dollar volume was read.", Line(dip with { Index = "MID" }));
        Assert.Equal("Checked on 18 Nov 2026 after a data correction: no trade changed.", Line(pullback));
        Assert.Equal("Checked on 18 Nov 2026 after a data correction: no trade changed.", Line(book));

        // The box draws it beneath the figure and above the rule's note, and its figure as before.
        var box = Read(MarkRenderer.SetupRecordBox(pullback));

        Assert.Contains("About $0.12 less for every $1 risked", box, StringComparison.Ordinal);
        Assert.True(box.IndexOf("Checked on", StringComparison.Ordinal) < box.IndexOf(MarkRenderer.RecordNote, StringComparison.Ordinal));

        // The day before the correction and a month after it, nothing is said.
        foreach (var day in new[] { Corrected.AddDays(-1), Corrected.AddMonths(1) })
        {
            var outside = await ApiOn(store, day).SetupRecordsAsync();

            Assert.Null(outside.Of("SML", DipRule.Name)!.Correction);
            Assert.Equal(string.Empty, MarkRenderer.CorrectedLine(outside.Of("MID", HeavyweightRule.Name)!));
        }

        Assert.DoesNotMatch(new Regex("Corrected on|Checked on"), MarkRenderer.SetupRecordBox(inside.Of("GSPC", BreakoutRule.Name)));
    }

    // The operator's answer of 2026-11-17: the Run page lists each step in a stock's closes the share check reads as no split,
    // with the stock and the date, and each split the split events or the pulled splits hold whose closes show no step; a stock
    // holding no bar on the night is not read, and a step a table holds is listed nowhere; from 24.11 each with its cause and
    // the count by cause above each list.
    // see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
    // see: A split table's entry is read only where the closes step by its own ratio, and an entry of another size is set aside and listed
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

        // From 24.11 each with its cause: AAA's half a split's ratio no table holds and its split no step in the closes; DDD's
        // step of 0.6 on 3 Sep the dividend of 40 on its close as traded of 100 makes.
        Bar("DDD", first, "50", "100");
        Bar("DDD", first.AddDays(2), "51", "61.2");
        Bar("DDD", night, "53", "63.6");
        store.Execute("INSERT INTO dividend_event (ticker, ex_date, amount, unadjusted, source, run_id) VALUES ('DDD', '2026-09-03', '40', '40', 'night', 'night');");

        var rows = await Api(store).SplitStepsOnAsync(night);

        Assert.Equal(
            [
                new SplitStepRow("AAA", first.AddDays(2), 0.5m, "a split's ratio no table holds"),
                new SplitStepRow("AAA", new DateOnly(2026, 9, 20), null, "no step in the closes"),
                new SplitStepRow("DDD", first.AddDays(2), 0.6m, "a special dividend"),
            ],
            rows);

        var drawn = Read(MarkRenderer.SplitSteps(rows));

        Assert.Contains("A jump in the price adjustment with no split in the split tables is not read as a split (2):By cause: a special dividend 1; a split's ratio no table holds 1.AAA: 3 Sep 2026, a step of 0.5: a split's ratio no table holds", drawn, StringComparison.Ordinal);
        Assert.Contains("DDD: 3 Sep 2026, a step of 0.6: a special dividend", drawn, StringComparison.Ordinal);
        Assert.Contains("A split in the split tables with no matching jump in the prices is not read either (1):By cause: no step in the closes 1.AAA: 20 Sep 2026: no step in the closes", drawn, StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.SplitSteps([]));
    }
}
