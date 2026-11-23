using EquityBrief.Core.Chart;
using EquityBrief.Core.History;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction: the long reader's marks read span by span, a mark inside a span the stock trades
// on past settled by its filer and its prices, two codes of one company joined only by the rename test, each company counted
// once on a session, and the every-session table's seven readings at the traded price and over a fixed length of history,
// each worked by hand.
// see: A history-ends mark ends only the member span it falls in, and a mark inside a span the stock trades on past is settled by its filer and its prices
// see: Two codes of one filer join as one history where they are never priced on the same day or trade in one ratio on the days they share
// see: The every-session table reads four readings at the traded price and three over a fixed length of history, the night's own
public partial class FixtureExpectations
{
    // The correction's two rows: the marks ruled's store and the History page's row of the marks the evidence leaves to the
    // operator.
    internal static string[] LongCorrectionRows => [.. SchemaColumns.MarkRulingStoreRows, .. Reading.ReadSurface.MarkRulingRows, .. LongSpansClaims];

    // The correction's second part's row: section 17's renames and spans.
    internal static readonly string[] LongSpansClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The long history's renames and spans"),
    ];

    static SweepBar Dated(int day, decimal close, long volume = 1000, decimal raw = 0) =>
        new(new DateOnly(2010, 1, 4).AddDays(day), close, close, close, volume, close, raw);

    static (DateOnly? Joined, DateOnly? Left) HeldSpan(int from, int? to) =>
        (new DateOnly(2010, 1, 4).AddDays(from), to is { } end ? new DateOnly(2010, 1, 4).AddDays(end) : null);

    // A code whose history ends on day 4, one company to day 4 and another taking the code from day 10: the earlier span reads
    // days 0 to 4, the later one days 10 to 13 alone and never a day before the mark; read through, one series and both spans.
    [Fact]
    public void AMarkEndsOnlyTheSpanItFallsInAndALaterSpanReadsItsOwnBarsAlone()
    {
        SweepBar[] bars = [.. Enumerable.Range(0, 14).Select(day => Dated(day, 10m + day))];
        SessionMark[] marks = [new(bars[4].Session, SessionMarking.HistoryEnds, "e"), new(bars[2].Session, SessionMarking.BadPrint, "b")];
        (DateOnly?, DateOnly?)[] spans = [HeldSpan(0, 5), HeldSpan(10, null)];

        var parts = LongMarks.Parts(bars, marks, spans, readThrough: false);

        Assert.Equal(2, parts.Count);
        Assert.Equal([0, 1, 3, 4], parts[0].Bars.Select(bar => bar.Session.DayNumber - bars[0].Session.DayNumber));
        Assert.Equal([HeldSpan(0, 5)], parts[0].Spans);
        Assert.Equal([5, 6, 7, 8, 9, 10, 11, 12, 13], parts[1].Bars.Select(bar => bar.Session.DayNumber - bars[0].Session.DayNumber));
        Assert.Equal([HeldSpan(10, null)], parts[1].Spans);

        var through = Assert.Single(LongMarks.Parts(bars, marks, spans, readThrough: true));

        Assert.Equal(13, through.Bars.Length);
        Assert.Equal(spans, through.Spans);

        // The old reading cut every bar after the mark, the later span reading none.
        Assert.DoesNotContain(LongLedger.Marked(bars, marks), bar => bar.Session > bars[4].Session);
    }

    // A mark inside a span with six traded sessions after it is odd, and with five it is not; spans of one index on both sides
    // of a mark make it odd whatever trades after it.
    [Fact]
    public void AMarkIsOddInsideASpanTradedPastMoreThanFiveTimesOrWithSpansOfOneIndexOnBothSides()
    {
        var mark = new DateOnly(2010, 1, 10);
        DateOnly[] six = [.. Enumerable.Range(1, 6).Select(day => mark.AddDays(day))];

        Assert.True(LongMarks.Odd(mark, [("MID", new DateOnly(2010, 1, 1), null)], six));
        Assert.False(LongMarks.Odd(mark, [("MID", new DateOnly(2010, 1, 1), null)], six[..5]));
        Assert.False(LongMarks.Odd(mark, [("MID", new DateOnly(2010, 1, 1), mark.AddDays(3))], six));
        Assert.True(LongMarks.Odd(mark, [("MID", new DateOnly(2009, 1, 1), new DateOnly(2009, 6, 1)), ("MID", new DateOnly(2011, 1, 1), null)], []));
        Assert.False(LongMarks.Odd(mark, [("MID", new DateOnly(2009, 1, 1), new DateOnly(2009, 6, 1)), ("SML", new DateOnly(2011, 1, 1), null)], []));
    }

    // The filer files a 10-Q 100 days before the mark and a 10-K 50 days after: both sides. A report 40 days after is the
    // company's own closing quarter and does not count. A filer whose reports stop stops; one with none in the 200 days before
    // is not on file. The price, its median daily move over the sessions before a quarter of a percent... worked by hand.
    [Fact]
    public void AMarksFilerAndPriceAreReadAndSettleAsTheyAgreeOrNot()
    {
        var mark = new DateOnly(2012, 6, 1);

        Assert.Equal(LongMarks.SameFiler, LongMarks.FilerOf(mark, [(mark.AddDays(-100), "10-Q"), (mark.AddDays(50), "10-K")]));
        Assert.Equal(LongMarks.FilerStops, LongMarks.FilerOf(mark, [(mark.AddDays(-100), "10-Q"), (mark.AddDays(40), "10-Q"), (mark.AddDays(90), "8-K")]));
        Assert.Equal(LongMarks.NotOnFile, LongMarks.FilerOf(mark, [(mark.AddDays(-201), "10-Q"), (mark.AddDays(90), "10-Q")]));

        // Thirty sessions moving 1% a day up then down, the median move 1%: a next move of 4% carries on, past 4% jumps.
        SweepBar[] Series(decimal next)
        {
            var closes = new List<decimal>();
            var price = 100m;

            for (var at = 0; at < 30; at++)
            {
                price = at % 2 == 0 ? price * 1.01m : price / 1.01m;
                closes.Add(price);
            }

            closes.Add(closes[^1] * next);

            return [.. closes.Select((close, at) => new SweepBar(mark.AddDays(at - 29), close, close, close, 1000, close))];
        }

        DateOnly[] calendar = [.. Enumerable.Range(-29, 31).Select(day => mark.AddDays(day))];

        Assert.Equal(LongMarks.Continues, LongMarks.PriceOf(Series(1.039m), mark, calendar));
        Assert.Equal(LongMarks.Jump, LongMarks.PriceOf(Series(1.05m), mark, calendar));

        var gapped = Series(1.0m);
        gapped[^1] = gapped[^1] with { Session = mark.AddDays(10) };

        Assert.Equal(LongMarks.Gap, LongMarks.PriceOf(gapped, mark, [.. calendar, .. Enumerable.Range(2, 8).Select(day => mark.AddDays(day))]));
        Assert.Equal(LongMarks.TooFew, LongMarks.PriceOf(Series(1.0m)[^12..], mark, calendar));

        Assert.Equal(LongMarks.ReadThrough, LongMarks.Settle(LongMarks.SameFiler, LongMarks.Continues));
        Assert.Equal(LongMarks.Kept, LongMarks.Settle(LongMarks.FilerStops, LongMarks.Jump));
        Assert.Equal(LongMarks.Kept, LongMarks.Settle(LongMarks.FilerStops, LongMarks.Gap));
        Assert.Equal(LongMarks.ToOperator, LongMarks.Settle(LongMarks.SameFiler, LongMarks.Jump));
        Assert.Equal(LongMarks.ToOperator, LongMarks.Settle(LongMarks.FilerStops, LongMarks.Continues));
        Assert.Equal(LongMarks.ToOperator, LongMarks.Settle(LongMarks.NotOnFile, LongMarks.Continues));
    }

    // Two codes never priced on the same day join; two priced on two shared days at one close and volume join as one company
    // listed twice; one shared day at another volume, as a share class trades, keeps them apart. A session a code holds no
    // price of its own on, its company priced under another member code, is taken out of its span.
    [Fact]
    public void TwoCodesJoinOnlyByTheRenameTestAndACompanyIsCountedOnce()
    {
        SweepBar[] old = [Dated(0, 10m), Dated(1, 11m), Dated(2, 12m)];
        SweepBar[] renamed = [Dated(3, 13m), Dated(4, 14m)];

        Assert.Equal(LongRenames.NeverSameDay, LongRenames.Test(old, renamed));
        Assert.Equal(LongRenames.ListedTwice, LongRenames.Test(old, [Dated(1, 11m), Dated(2, 12m), Dated(3, 13m)]));
        Assert.Null(LongRenames.Test(old, [Dated(2, 12m, volume: 999), Dated(3, 13m)]));
        Assert.Equal(LongRenames.NeverSameDay, LongRenames.Test(old, [Dated(2, 12m, volume: 0), Dated(3, 13m)]));

        var (joined, taken) = LongRenames.Joined(old, renamed);

        Assert.Equal([10m, 11m, 12m, 13m, 14m], joined.Select(bar => bar.Close));
        Assert.Equal([Dated(3, 0m).Session, Dated(4, 0m).Session], taken.Order());

        DateOnly[] calendar = [.. Enumerable.Range(0, 10).Select(day => Dated(day, 0m).Session)];

        Assert.Equal([HeldSpan(0, 3), HeldSpan(5, 7), HeldSpan(8, null)], LongRenames.Without([HeldSpan(0, 7), HeldSpan(8, null)], new HashSet<DateOnly> { calendar[3], calendar[4] }, calendar));
        Assert.Equal([HeldSpan(1, 7)], LongRenames.Without([HeldSpan(0, 7)], new HashSet<DateOnly> { calendar[0] }, calendar));
        Assert.Equal([HeldSpan(0, 6)], LongRenames.Without([HeldSpan(0, 7)], new HashSet<DateOnly> { calendar[6] }, calendar));

        Assert.Equal("FB", LongRenames.BaseOf("FB_old"));
        Assert.Equal("LB", LongRenames.BaseOf("LB_old2"));
        Assert.Equal("CB", LongRenames.BaseOf("CB1"));

        // FB and META share a filer and FB_old is FB's delisted spelling; DOC_old1, a delisted DOC, shares no filer with HCP,
        // which shares Healthpeak's filer with today's DOC, so the link is never followed from DOC_old1 to HCP.
        var companies = LongCompanies.Of(
            new Dictionary<string, IEnumerable<string>> { ["1"] = ["FB", "META"], ["2"] = ["DOC", "HCP"], ["3"] = ["DOC_old1"] },
            ["FB", "FB_old", "META", "ABC", "DOC", "DOC_old1", "HCP"]);

        Assert.Equal(["FB_old", "META"], companies.Linked("FB"));
        Assert.True(companies.Same("META", "FB"));
        Assert.False(companies.Same("META", "FB_old"));
        Assert.Equal(["DOC"], companies.Linked("DOC_old1"));
        Assert.Equal(["DOC_old1", "HCP"], companies.Linked("DOC"));
        Assert.DoesNotContain("HCP", companies.Linked("DOC_old1"));
        Assert.Empty(companies.Linked("ABC"));
    }

    // Over a constructed store: a code reused after its history ends reads its later span from its own bars, a rename joins
    // its old code's prices, and a company held under two member codes is counted once where one holds no price of its own.
    [Fact]
    public async Task TheLongReaderReadsALaterSpanOfAReusedCodeJoinsARenameAndCountsACompanyOnce()
    {
        using var store = new TemporaryStore().Migrated();
        string[] days = [.. Enumerable.Range(0, 12).Select(at => new DateOnly(2010, 1, 4).AddDays(at)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(day => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))];

        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", days.Select(day => $"('GSPC', '{day}', '1000', '1000', '1000', '1000', 'r')")) + ";");
        store.Execute("INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES "
            + $"('MID', 'AAA', '{days[0]}', '{days[2]}', 'fund', 'r'), ('MID', 'AAA', '{days[5]}', NULL, 'fund', 'r'), "
            + $"('MID', 'NEW', '{days[0]}', NULL, 'fund', 'r'), "
            + $"('MID', 'ONE', '{days[0]}', NULL, 'fund', 'r'), ('MID', 'TWO', '{days[0]}', NULL, 'fund', 'r');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days.Select((day, at) => LongBarRow("AAA", day, 10m + at))
                .Concat(days[..4].Select((day, at) => LongBarRow("NEW_old", day, 50m + at)))
                .Concat(days[4..].Select((day, at) => LongBarRow("NEW", day, 60m + at)))
                .Concat(days[..6].Select((day, at) => LongBarRow("ONE", day, 70m + at)))
                .Concat(days.Select((day, at) => LongBarRow("TWO", day, 80m + at)))) + ";");
        store.Execute($"INSERT INTO session_mark (ticker, session_date, kind, says, ruled, run_id) VALUES ('AAA', '{days[2]}', '{SessionMarking.HistoryEnds}', 'e', '2026-11-08T00:00:00Z', 'r');");
        store.Execute("INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull) VALUES ('ONE', '7', NULL, NULL, NULL, NULL, NULL, 'p'), ('TWO', '7', NULL, NULL, NULL, NULL, NULL, 'p');");

        var reader = new LongHistoryReader(store.DatabaseFile);
        var read = await reader.ReadAsync("MID", DateOnly.ParseExact(days[^1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var aaa = read.Names.Where(name => name.Ticker == "AAA").ToArray();

        Assert.Equal(2, aaa.Length);
        Assert.Equal([10m, 11m, 12m], aaa[0].Bars.Select(bar => bar.Close));
        Assert.Equal([.. Enumerable.Range(3, days.Length - 3).Select(at => 10m + at)], aaa[1].Bars.Select(bar => bar.Close));

        var renamed = read.Names.Single(name => name.Ticker == "NEW");

        Assert.Equal([50m, 51m, 52m, 53m, .. Enumerable.Range(0, days.Length - 4).Select(at => 60m + at)], renamed.Bars.Select(bar => bar.Close));

        var one = read.Names.Single(name => name.Ticker == "ONE");
        var day = (string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(one.MemberOn(day(days[5])));
        Assert.False(one.MemberOn(day(days[6])));
        Assert.False(one.MemberOn(day(days[^1])));
        Assert.Contains(await reader.JoinsAsync("MID", day(days[^1])), join => join is { Ticker: "NEW", Other: "NEW_old", Test: LongRenames.NeverSameDay, Sessions: 4 });
    }

    // At the traded price: three sessions at a stored close of 10 and a raw close of 20 on volumes of 100, 200 and 300, the
    // dollars traded 2,000, 4,000 and 6,000; the returns as stored, nought, 10% and minus 10%. A day with no raw close reads none.
    [Fact]
    public void TheTablesReadingsAtTheTradedPriceAndOverTheFixedHistoryAreWorkedByHand()
    {
        SweepBar[] bars = [Dated(0, 10m, 100, 20m), Dated(1, 11m, 200, 22m), Dated(2, 9.9m, 300, 19.8m)];

        Assert.Equal(20m * 1.5m, TableReadings.Traded(bars[0], 15m));
        Assert.Null(TableReadings.Traded(Dated(0, 10m), 15m));

        // Illiquidity over the last two sessions: (0.10 / 4,400 + 0.10 / 5,940) / 2.
        var expected = Math.Log10(((0.1 / (22.0 * 200)) + (0.1 / (19.8 * 300))) / 2);

        Assert.Equal(expected, TableReadings.Illiquidity(bars, 2, 2)!.Value, 12);
        Assert.Null(TableReadings.Illiquidity([bars[0], Dated(1, 11m, 200), bars[2]], 2, 2));

        // Liquidity over its window of sessions each at a stored close of 10, a raw close of 20 and 1,000 shares: 20,000
        // dollars a day, its logarithm 4.30, where the stored close would read 10,000 and 4.
        SweepBar[] window = [.. Enumerable.Range(0, Core.Ledger.LedgerReadings.LiquiditySessions).Select(day => Dated(day, 10m, 1000, 20m))];

        Assert.Equal(Math.Log10(20_000), TableReadings.Liquidity(window, window.Length - 1)!.Value, 12);
        Assert.Null(TableReadings.Liquidity(window, window.Length - 2));
        Assert.Null(TableReadings.Liquidity([.. window[..^1], Dated(window.Length - 1, 10m, 1000)], window.Length - 1));

        // A bar at 1,000 reads all its history; one at 5,000 reads from bar 2,209, its 2,792 sessions; a window starting
        // before that is read whole.
        Assert.Equal(0, TableReadings.OlderFrom(300, 1000));
        Assert.Equal(2209, TableReadings.OlderFrom(4800, 5000));
        Assert.Equal(2000, TableReadings.OlderFrom(2000, 5000));
    }
}
