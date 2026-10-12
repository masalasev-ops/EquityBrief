using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.12: the setup ledger's valuation reading. The share counts a filer's facts hold are read off
// its cover as the archive sent them, each for the day it stands for, and a filer stating its count by class holds none.
// The reading is worked by hand over sixteen quarters of constructed facts and a stock whose close steps a quarter at a
// time: the current figure at either end and a tie counting half, a split between two counts moving no place, a filing
// made on the session read from the next, a count standing for a day after the session not read, and a quarter missing
// a part leaving none. Over a constructed store the night reads the closes before the store's year from the pulled
// history at the store's scale for this reading and no other, and reads it again once the refresh has stored a filing;
// and the reading rebuilt from a history cut at its session is the one read off the whole.
// see: The valuation reading reads the prices before the store's year at the store's scale from the history pulled for those years
public partial class FixtureExpectations
{
    // The night the readings are taken on, a Friday after the newest quarter's report.
    static readonly DateOnly ValuationNight = new(2026, 10, 9);

    // The k-th quarter's last and first days, newest first: 2026-06-30 back to 2022-09-30.
    static DateOnly ValuationEnd(int k) => new DateOnly(2026, 7, 1).AddMonths(-3 * k).AddDays(-1);

    static DateOnly ValuationStart(int k) => new DateOnly(2026, 4, 1).AddMonths(-3 * k);

    const int ValuationQuarters = 16;

    // A session's close at the store's scale: 100 through the newest quarter, ten more for each quarter older, 100 after
    // it and 115 on the night.
    static decimal ValuationClose(DateOnly day)
    {
        if (day == ValuationNight)
        {
            return 115m;
        }

        var k = 0;

        while (k < 40 && day <= ValuationEnd(k + 1))
        {
            k++;
        }

        return day > ValuationEnd(0) ? 100m : 100m + (10m * k);
    }

    // Each quarter earning one, filed thirty days after its end, and its cover stating eight shares, or as many as given,
    // standing for the twentieth day after its end, filed with it.
    static List<FiledFactRow> ValuationFacts(Func<int, decimal>? shares = null, int? withoutIncome = null, int? withoutCount = null)
    {
        var facts = new List<FiledFactRow>();

        for (var k = 0; k < ValuationQuarters; k++)
        {
            var filed = ValuationEnd(k).AddDays(30);

            if (k != withoutIncome)
            {
                facts.Add(new FiledFactRow("NetIncomeLoss", ValuationStart(k), ValuationEnd(k), 1m, filed, "10-Q", FormattableString.Invariant($"q-{k}")));
            }

            if (k != withoutCount)
            {
                var day = ValuationEnd(k).AddDays(20);

                facts.Add(new FiledFactRow(FiledFacts.SharesOutstanding[0], day, day, shares?.Invoke(k) ?? 8m, filed, "10-Q", FormattableString.Invariant($"q-{k}")));
            }
        }

        return facts;
    }

    static List<DateOnly> ValuationWeekdays(DateOnly from, DateOnly through)
    {
        var days = new List<DateOnly>();

        for (var day = from; day <= through; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return days;
    }

    static PriceHistory ValuationPrices(DateOnly from, Func<DateOnly, decimal> traded, decimal? night = null) =>
        PriceHistory.Of([.. ValuationWeekdays(from, ValuationNight).Select(day =>
        {
            var close = day == ValuationNight && night is { } held ? held : ValuationClose(day);
            var asTraded = day == ValuationNight && night is { } same ? same : traded(day);

            return new SweepBar(day, close, close, close, 1_000_000, close, asTraded);
        })]);

    static double? Valued(IReadOnlyList<FiledFactRow> facts, PriceHistory prices, DateOnly session) =>
        ValuationReading.Place(ValuationReading.PartsOf(facts, session), prices, session);

    [Fact]
    public void TheShareCountsAreReadOffTheCoverForTheDayEachStandsForAndAFilerStatingThemByClassHoldsNone()
    {
        var concept = FiledFacts.SharesOutstanding.Single();
        var apple = Captured("company-facts-AAPL.json");
        var counts = ConceptAnswers.FromFacts(apple, "0000320193", FiledFacts.Concepts)[concept];

        // Read here from the capture with this test's own reading: every count under the entity taxonomy's concept, in
        // shares, each for its day.
        using var capture = JsonDocument.Parse(apple);
        var sent = capture.RootElement.GetProperty("facts").GetProperty("dei").GetProperty(concept).GetProperty("units").GetProperty("shares").EnumerateArray()
            .Select(fact => (End: fact.GetProperty("end").GetString()!, Value: fact.GetProperty("val").GetDecimal(), Filed: fact.GetProperty("filed").GetString()!, Accession: fact.GetProperty("accn").GetString()!))
            .ToArray();

        Assert.Equal(70, sent.Length);
        Assert.Equal(sent.Select(fact => (fact.End, fact.Value, fact.Filed, fact.Accession)), counts.Select(count => (Stamp(count.End), count.Value, Stamp(count.Filed), count.Accession)));
        Assert.All(counts, count => Assert.Equal(count.End, count.Start));
        Assert.Equal(new ConceptFact(new DateOnly(2026, 7, 17), new DateOnly(2026, 7, 17), 14_594_180_000m, new DateOnly(2026, 7, 31), "10-Q", "0000320193-26-000020"), counts[^1]);

        // Stored a day once as first filed, from the first day stored: 47 of the 70, the one day stated twice before it.
        var stored = FiledFacts.Stored(new Dictionary<string, IReadOnlyList<ConceptFact>>(StringComparer.Ordinal) { [concept] = counts });
        var worked = sent
            .Where(fact => string.CompareOrdinal(fact.End, "2015-01-01") >= 0)
            .GroupBy(fact => fact.End)
            .Select(day => day.OrderBy(fact => fact.Filed + fact.Accession, StringComparer.Ordinal).First())
            .OrderBy(fact => fact.End, StringComparer.Ordinal)
            .Select(fact => (fact.End, fact.Value, fact.Accession))
            .ToArray();

        Assert.Equal(47, worked.Length);
        Assert.Equal(worked, stored.Select(row => (Stamp(row.End), row.Value, row.Accession)));
        Assert.All(stored, row => Assert.Equal((concept, row.End), (row.Concept, row.Start)));

        // A count stated for a period rather than a day is passed over, and an empty set of shares reads as none.
        const string period = """{"facts":{"dei":{"EntityCommonStockSharesOutstanding":{"units":{"shares":[{"start":"2026-01-01","end":"2026-03-31","val":5,"accn":"x","form":"10-Q","filed":"2026-04-30"},{"end":"2026-04-20","val":8,"accn":"y","form":"10-Q","filed":"2026-04-30"}]}}}}}""";
        const string empty = """{"facts":{"dei":{"EntityCommonStockSharesOutstanding":{"units":{"shares":{}}}}}}""";

        Assert.Equal([new ConceptFact(new DateOnly(2026, 4, 20), new DateOnly(2026, 4, 20), 8m, new DateOnly(2026, 4, 30), "10-Q", "y")], ConceptAnswers.FromFacts(period, "0000000001", FiledFacts.SharesOutstanding)[concept]);
        Assert.Empty(ConceptAnswers.FromFacts(empty, "0000000001", FiledFacts.SharesOutstanding)[concept]);

        // Alphabet states its count a class at a time, which the archive's facts do not carry: its entity taxonomy holds
        // the public float alone, so it holds no count, while its net income is read.
        var alphabet = ConceptAnswers.FromFacts(Captured("phase17-sec-facts-GOOGL.json"), "0001652044", FiledFacts.Concepts);

        Assert.Empty(alphabet[concept]);
        Assert.Equal(4, alphabet["NetIncomeLoss"].Count);
    }

    [Fact]
    public void TheValuationReadingIsWorkedByHandOverSixteenQuartersAndAStocksCloses()
    {
        var facts = ValuationFacts();
        var prices = ValuationPrices(new DateOnly(2023, 1, 2), ValuationClose);

        // Each quarter's four quarters earn four on eight shares, a half a share. On the night the half over the close of
        // 115 stands above the same figure at the ends of the ten of its twelve quarters closing at 120 to 210, and below
        // the two closing at 100 and 110: ten twelfths.
        Assert.Equal(10.0 / 12, Valued(facts, prices, ValuationNight)!.Value, 12);

        // At 110 it ties the quarter closing there, which counts half; under every quarter's close it stands above all
        // twelve, and over every one above none.
        Assert.Equal(10.5 / 12, Valued(facts, ValuationPrices(new DateOnly(2023, 1, 2), ValuationClose, 110m), ValuationNight)!.Value, 12);
        Assert.Equal(1.0, Valued(facts, ValuationPrices(new DateOnly(2023, 1, 2), ValuationClose, 95m), ValuationNight)!.Value, 12);
        Assert.Equal(0.0, Valued(facts, ValuationPrices(new DateOnly(2023, 1, 2), ValuationClose, 215m), ValuationNight)!.Value, 12);

        // A two-for-one split on 2026-06-01, between the newest quarter's count and the one before's: every close at the
        // store's scale as it was, each close as traded before it twice that, the counts before it four shares and the
        // newest eight. Each count is brought to the store's scale by its own day's close as traded, so the place is the
        // one without the split; read with the counts as filed and no close as traded, the quarters before it would
        // stand twice as high and the night above none of them.
        var split = new DateOnly(2026, 6, 1);
        var splitFacts = ValuationFacts(shares: k => ValuationEnd(k).AddDays(20) < split ? 4m : 8m);
        var splitPrices = ValuationPrices(new DateOnly(2023, 1, 2), day => day < split ? 2 * ValuationClose(day) : ValuationClose(day));

        Assert.Equal(10.0 / 12, Valued(splitFacts, splitPrices, ValuationNight)!.Value, 12);

        // The newest quarter and its count were filed on 2026-07-30. Read that session, the twelve quarters are the twelve
        // before it, closing at 110 to 220, each standing under the session's half over 100 against the count before; read
        // the session after, the newest is among them and closes at 100 as the session does, a tie.
        Assert.Equal(1.0, Valued(facts, prices, new DateOnly(2026, 7, 30))!.Value, 12);
        Assert.Equal(11.5 / 12, Valued(facts, prices, new DateOnly(2026, 7, 31))!.Value, 12);

        // A count filed before the night standing for a day after it is not read, so no close after the night is: read
        // as the newest count, its one share would put the night above every quarter.
        FiledFactRow ahead = new(FiledFacts.SharesOutstanding[0], new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 20), 1m, new DateOnly(2026, 10, 1), "10-Q", "ahead");

        Assert.Equal(10.0 / 12, Valued([.. facts, ahead], prices, ValuationNight)!.Value, 12);

        // A quarter missing a part leaves none: a quarter of income missing breaks the run of quarters, the newest
        // quarter's cover stating no count leaves it no count standing on or after its end, prices beginning after the
        // oldest quarter's end leave it no close, and a day the prices hold no session of is no session.
        Assert.Null(Valued(ValuationFacts(withoutIncome: 5), prices, ValuationNight));
        Assert.Null(Valued(ValuationFacts(withoutCount: 0), prices, ValuationNight));
        Assert.Null(Valued(facts, ValuationPrices(ValuationEnd(11).AddDays(2), ValuationClose), ValuationNight));
        Assert.Null(Valued(facts, prices, ValuationNight.AddDays(1)));
        Assert.Equal(ValuationEnd(11), ValuationReading.OldestDay(ValuationReading.PartsOf(facts, ValuationNight)));
    }

    // AAA and BBB on the S&P 500, the store's year the 71 weekdays to the night from 2026-07-03, each at 100 on a
    // one-point range and a million shares, and AAA at 115 on the night, a breakout; AAA's pull from 2023-01-02 to the
    // night at twice the store's scale with its closes as traded the store's; and AAA's filer's facts, the newest
    // quarter's held back where asked. The newest quarter ends on 2026-06-30, before the store's year, and its count
    // stands for 2026-07-20, inside it, so the place reads one close of each.
    static TemporaryStore ValuationStore(bool newestFiled)
    {
        var store = new TemporaryStore().Migrated();
        var statements = new List<string>
        {
            "INSERT INTO company (ticker, fetched_at, cik) VALUES ('AAA', '2026-10-01T23:40:00.000Z', '0000000101');",
        };

        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            statements.Add($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2020-01-02', NULL, '2020-01-02T21:00:00Z');");

            foreach (var day in ValuationWeekdays(new DateOnly(2026, 7, 3), ValuationNight))
            {
                var close = ticker == "AAA" ? ValuationClose(day) : 100m;

                statements.Add(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) "
                    + FormattableString.Invariant($"VALUES ('{ticker}', '{Stamp(day)}', '{close}', '{close + 1m}', '{close - 1m}', '{close}', 1000000, 'constructed', '{Stamp(day)}T21:00:00Z', '{close}');"));
            }
        }

        foreach (var day in ValuationWeekdays(new DateOnly(2023, 1, 2), ValuationNight))
        {
            var close = ValuationClose(day);

            statements.Add(
                "INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) "
                + FormattableString.Invariant($"VALUES ('AAA', '{Stamp(day)}', '{2 * close}', '{2 * close}', '{2 * close}', '{2 * close}', '{close}', 1000000, 'history-pull-a');"));
        }

        statements.AddRange(ValuationFacts().Where(fact => newestFiled || fact.Accession != "q-0").Select(FactRow));
        Batch(store, statements);

        return store;
    }

    static string FactRow(FiledFactRow fact) =>
        "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) "
        + FormattableString.Invariant($"VALUES ('0000000101', '{fact.Concept}', '{Stamp(fact.Start)}', '{Stamp(fact.End)}', '{fact.Value}', '{Stamp(fact.Filed)}', '{fact.Form}', '{fact.Accession}', 'refresh');");

    static void Batch(TemporaryStore store, IEnumerable<string> statements)
    {
        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    [Fact]
    public async Task TheNightReadsTheClosesBeforeTheStoresYearAtItsScaleForTheValuationReadingAloneAndReadsItAgainOnceTheRefreshStoresAFiling()
    {
        using var store = ValuationStore(newestFiled: false);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 9, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var ledger = new SetupLedger(clock, store.DatabaseFile);
        string Place() => Query(store, "SELECT value_place FROM setup WHERE family = 'breakout' AND ticker = 'AAA';").Single();

        // Before the refresh has stored the newest quarter, the night reads the twelve before it, closing at 110 to 220 on
        // the pull at the store's scale, and the night's half over 115 against the count before stands above the eleven
        // closing at 120 or more.
        var first = await ledger.NightAsync("night-valuation");

        Assert.Equal(ValuationNight, first.Session);
        Assert.All(first.Indices, index => Assert.Null(index.Fault));
        Assert.Equal(11.0 / 12, double.Parse(Place(), CultureInfo.InvariantCulture), 12);

        // The refresh stores the newest quarter, filed before the night, and the night's setup reads it again: the newest
        // quarter closing at 100 on the pull brought to the store's scale, ten twelfths. Read at the pull's own scale, its
        // close of 200 against its count's close of 100 in the store would put the night above it too, eleven twelfths.
        Batch(store, ValuationFacts().Where(fact => fact.Accession == "q-0").Select(FactRow));

        Assert.Equal(1, await ledger.BusinessAgainAsync(ValuationNight, ["AAA"]));
        Assert.Equal(10.0 / 12, double.Parse(Place(), CultureInfo.InvariantCulture), 12);

        // The night run again reads the same, and the readings a rule's hooks read on the night are its setup's.
        var again = await ledger.NightAsync("night-valuation-again");

        Assert.All(again.Indices, index => Assert.Null(index.Fault));
        Assert.Equal(10.0 / 12, double.Parse(Place(), CultureInfo.InvariantCulture), 12);
        Assert.Equal(10.0 / 12, (await ledger.ReadingsTonightAsync("GSPC"))("AAA")![LedgerReadings.IndexOf("value_place")]!.Value, 12);

        // No other reading reads the pull: the store's year of 71 sessions reaches no 126-session return, no 200-session
        // average and no 252-session high, though the pull holds near four years.
        Assert.Equal(["1|1|1|1"], Query(store, "SELECT (return_half_year IS NULL) || '|' || (close_over_long IS NULL) || '|' || (high_ratio IS NULL) || '|' || (return_twelve_less_one IS NULL) FROM setup WHERE family = 'breakout' AND ticker = 'AAA';"));
    }

    [Fact]
    public void TheValuationReadingRebuiltFromTheHistoryCutAtItsSessionIsTheOneReadOffTheWholeAndOneReadingAFilingMadeOnItsSessionIsNamed()
    {
        // AAA and BBB over the weekdays from 2023-01-02 to the night, AAA's closes at the store's scale as the history
        // merges them, its filer's facts beside them.
        var calendar = ValuationWeekdays(new DateOnly(2023, 1, 2), ValuationNight);

        SweepName Member(string ticker, Func<DateOnly, decimal> close) =>
            new(ticker, [.. calendar.Select(day => new SweepBar(day, close(day) + 1, close(day) - 1, close(day), 1_000_000, close(day), close(day)))], [(new DateOnly(2020, 1, 2), null)], []);

        var inputs = new SweepHistoryInputs(calendar[^1], [.. calendar], [Member("AAA", ValuationClose), Member("BBB", _ => 100m)], 0, 0, 0, 0, "constructed");
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal) { ["AAA"] = ValuationFacts() };
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var columns = SweepColumns.Sessions(series, inputs.Sessions);
        var members = SweepBenchmark.On(series, calendar.Count);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Count);
        var place = LedgerReadings.IndexOf("value_place");

        double?[] Whole(DateOnly session)
        {
            var at = calendar.IndexOf(session);

            return LedgerSetups.Readings(
                IndexFamilies.LargeIndex,
                0,
                series[0],
                [.. series[0].Bars.Select(bar => Statistic.FromPrice(bar.Close))],
                IndexNightRead.BarOf(series[0], at),
                columns[at],
                members,
                calendar,
                at,
                LedgerMarket.None,
                new LedgerContext(NoIncome, NoSectors, highs, lows, facts));
        }

        double?[] Rebuilt(DateOnly session) =>
            LedgerPointInTime.Rebuilt(IndexFamilies.LargeIndex, LedgerPointInTime.Cut(inputs, session), LedgerMarket.None, NoIncome, NoSectors, facts, "AAA")!;

        // Read off the whole history on the session after the newest quarter's report and rebuilt from the history cut
        // there, the same: the newest quarter's close of 100 ties the session's, eleven and a half twelfths.
        var after = new DateOnly(2026, 7, 31);

        Assert.Equal(11.5 / 12, Whole(after)[place]!.Value, 12);
        Assert.Empty(LedgerPointInTime.Differences(Whole(after), Rebuilt(after)));

        // On the session it was filed it is not read, every quarter of the twelve before it under the session; a stored
        // reading that read it there is named by its column with both figures.
        var on = new DateOnly(2026, 7, 30);
        var stored = Rebuilt(on);

        Assert.Equal(1.0, stored[place]!.Value, 12);
        Assert.Empty(LedgerPointInTime.Differences(Whole(on), stored));

        var readAhead = (double?[])stored.Clone();

        readAhead[place] = ValuationReading.Place(ValuationReading.PartsOf(facts["AAA"], on.AddDays(1)), PriceHistory.Of(series[0].Bars), on);

        Assert.Equal([("value_place", (double?)(11.5 / 12), (double?)1.0)], LedgerPointInTime.Differences(readAhead, stored));
    }
}
