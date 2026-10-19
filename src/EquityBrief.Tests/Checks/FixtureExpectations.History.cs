using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Moves;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Moves;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 18.3: the sessions the pulled years are read on, the typical earnings move's median and floor,
// the prints read off the pull that stand, the long view's month-ends at the store's scale, the plan's shares with
// their risk and reward to risk, a move's cause in its stated order, a band's turns, and the page history reader over
// a constructed store, each worked by hand.
// see: A stock's typical earnings move is the median size of its reaction over every print the store holds, read through the history pulled before its year
// see: The long view draws the month-end closes over the pulled history at the bar store's scale with the plan's levels on the same prices
// see: Each tranche buys an equal share of a full position as the exits divide it, and its reward to risk is read to every traded exit
// see: A move's cause is research's where written, then the labelled news inside it and then the filings dated inside it
public partial class FixtureExpectations
{
    // The rows 18.3 adds that this check reaches: section 17's floor and four of section 18's failures.
    internal static string[] HistoryRows =>
    [
        CheckReach.Key(Scope.LimitsTable, "Typical earnings move"),
        CheckReach.Key(Scope.FailureTable, "No pull holds a name's first stored session"),
        CheckReach.Key(Scope.FailureTable, "A pulled print whose session or the session before it the pull misses"),
        CheckReach.Key(Scope.FailureTable, "Fewer than four prints read"),
        CheckReach.Key(Scope.FailureTable, "A move with no written cause, no labelled story and no filing inside it"),
    ];

    static DateOnly Date(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public void ThePulledYearsSessionsAreTheDaysHalfTheNamesSpanningThemHoldAndNoOther()
    {
        // Four names span one week. Monday all four hold, Tuesday two, exactly half, Wednesday one, a
        // stray of one name's, Thursday three and Friday all four.
        var (monday, tuesday, wednesday, thursday, friday) = (Date("2019-03-04"), Date("2019-03-05"), Date("2019-03-06"), Date("2019-03-07"), Date("2019-03-08"));
        var calendar = new PulledCalendar(
            new Dictionary<DateOnly, int> { [monday] = 4, [tuesday] = 2, [wednesday] = 1, [thursday] = 3, [friday] = 4 },
            Enumerable.Repeat((monday, friday), 4));

        Assert.Equal([monday, tuesday, thursday, friday], calendar.Sessions);
        Assert.Equal(4, calendar.Spanning(wednesday));
        Assert.True(calendar.IsSession(tuesday));
        Assert.False(calendar.IsSession(wednesday));

        // The first session on or after a day, after it, and before it, none past either end.
        Assert.Equal(thursday, calendar.OnOrAfter(wednesday));
        Assert.Equal(tuesday, calendar.OnOrAfter(tuesday));
        Assert.Equal(thursday, calendar.After(tuesday));
        Assert.Equal(tuesday, calendar.SessionBefore(thursday));
        Assert.Null(calendar.SessionBefore(monday));
        Assert.Null(calendar.After(friday));

        // A fifth name spanning Wednesday alone, holding it, makes Wednesday held by two of five, under half.
        var wider = new PulledCalendar(
            new Dictionary<DateOnly, int> { [monday] = 4, [tuesday] = 2, [wednesday] = 2, [thursday] = 3, [friday] = 4 },
            [.. Enumerable.Repeat((monday, friday), 4), (wednesday, wednesday)]);

        Assert.False(wider.IsSession(wednesday));
        Assert.Equal(5, wider.Spanning(wednesday));
    }

    [Fact]
    public void ATypicalEarningsMoveIsTheMedianSizeOfItsReactionsStatedOverFourPrintsAndTwoOnOneSessionAreOne()
    {
        // Five prints moving 2, -4, 1, -3 and 6 per cent: their sizes in order 1, 2, 3, 4 and 6, so the median is 3, a fall
        // and a rise of one size alike.
        var five = EarningsMoves.Of(
            [
                (Date("2024-01-30"), Date("2024-01-30"), 2.0),
                (Date("2024-04-30"), Date("2024-04-30"), -4.0),
                (Date("2024-07-30"), Date("2024-07-31"), 1.0),
                (Date("2023-10-30"), Date("2023-10-30"), -3.0),
                (Date("2024-10-30"), Date("2024-10-30"), 6.0),
            ],
            unread: 2);

        Assert.Equal((5, 2, Date("2023-10-30"), 3.0, true), (five.Prints, five.Unread, five.FirstPrint!.Value, five.MedianPct!.Value, five.Stated));

        // Four prints sized 1, 2, 3 and 10: the median of an even count is the mean of the middle two, 2.5, stated.
        var four = EarningsMoves.Of(
            [
                (Date("2024-01-30"), Date("2024-01-30"), -1.0),
                (Date("2024-04-30"), Date("2024-04-30"), 2.0),
                (Date("2024-07-30"), Date("2024-07-30"), -3.0),
                (Date("2024-10-30"), Date("2024-10-30"), 10.0),
            ],
            unread: 0);

        Assert.Equal((4, 2.5, true), (four.Prints, four.MedianPct!.Value, four.Stated));

        // Three prints read a median and state none: a year's reports are four.
        var three = EarningsMoves.Of(
            [
                (Date("2024-01-30"), Date("2024-01-30"), -1.0),
                (Date("2024-04-30"), Date("2024-04-30"), 2.0),
                (Date("2024-07-30"), Date("2024-07-30"), -3.0),
            ],
            unread: 0);

        Assert.Equal((3, 2.0, false), (three.Prints, three.MedianPct!.Value, three.Stated));
        Assert.Equal(4, EarningsMoves.FewestPrints);

        // A report filed after the close on the 29th and again before the open on the 30th, both read on the 30th's
        // session, is one print, read once from the earlier filing.
        var twice = EarningsMoves.Of(
            [
                (Date("2024-01-29"), Date("2024-01-30"), 5.0),
                (Date("2024-01-30"), Date("2024-01-30"), 5.0),
                (Date("2024-04-30"), Date("2024-04-30"), 1.0),
            ],
            unread: 0);

        Assert.Equal((2, Date("2024-01-29"), 3.0), (twice.Prints, twice.FirstPrint!.Value, twice.MedianPct!.Value));

        // None read states none and has no oldest print.
        var none = EarningsMoves.Of([], unread: 1);

        Assert.Equal((0, 1, (DateOnly?)null, (double?)null, false), (none.Prints, none.Unread, none.FirstPrint, none.MedianPct, none.Stated));
    }

    [Fact]
    public void APrintReadOffThePullStandsOnlyWhereThePullHoldsItsSessionAndTheOneBeforeWithNoSessionMissedBetween()
    {
        // Two names span the week; one holds every day, the other misses Wednesday, and a third holds a Saturday no other
        // name holds, which is a stray and no session.
        var (monday, tuesday, wednesday, thursday, friday, saturday) = (Date("2019-03-04"), Date("2019-03-05"), Date("2019-03-06"), Date("2019-03-07"), Date("2019-03-08"), Date("2019-03-09"));
        var calendar = new PulledCalendar(
            new Dictionary<DateOnly, int> { [monday] = 3, [tuesday] = 3, [wednesday] = 2, [thursday] = 3, [friday] = 3, [saturday] = 1 },
            Enumerable.Repeat((monday, saturday), 3));

        // A report before the open on Tuesday read on Tuesday against Monday stands.
        Assert.True(EarningsMoves.Stands(tuesday, EventTiming.Before, tuesday, monday, calendar));

        // One after the close on Monday is read on Tuesday; read on Monday it does not stand.
        Assert.True(EarningsMoves.Stands(monday, EventTiming.After, tuesday, monday, calendar));
        Assert.False(EarningsMoves.Stands(monday, EventTiming.After, monday, monday.AddDays(-3), calendar));

        // The name missing Wednesday reads a Thursday report against Tuesday, across the hole: left out.
        Assert.False(EarningsMoves.Stands(thursday, EventTiming.Before, thursday, tuesday, calendar));

        // A name holding the stray Saturday reads a Friday report after the close on it: the stray is no session, so the
        // reaction read there is left out.
        Assert.False(EarningsMoves.Stands(friday, EventTiming.After, saturday, friday, calendar));

        // A report the provider left unstated is read on its own day, as the earnings rule reads it.
        Assert.True(EarningsMoves.Stands(wednesday, EventTiming.Unstated, wednesday, tuesday, calendar));
    }

    [Fact]
    public void TheLongViewIsEachMonthsLastCloseAtTheStoresScaleTheStoresOwnWhereBothHoldAMonth()
    {
        // The pull stands at 0.98 of the store's scale: brought to it by 100 over 98, a pulled close of 98 reads 100, and
        // one of 49.49 reads 50.5. The store's year starts inside March, so March's point is the store's.
        var ratio = 100m / 98m;

        Assert.Equal(100m, LongViews.Scaled(98m, ratio));
        Assert.Equal(50.5m, LongViews.Scaled(49.49m, ratio));

        var view = LongViews.Of(
            [
                (Date("2025-01-30"), 49m),
                (Date("2025-01-31"), 49.49m),
                (Date("2025-02-27"), 98m),
                (Date("2025-02-28"), 97.02m),
                (Date("2025-03-13"), 98m),
            ],
            ratio,
            [
                (Date("2025-03-14"), 101m),
                (Date("2025-03-31"), 102m),
                (Date("2025-04-30"), 103.5m),
            ]);

        Assert.Equal(
            [
                new MonthEnd(Date("2025-01-31"), 50.5m),
                new MonthEnd(Date("2025-02-28"), 99m),
                new MonthEnd(Date("2025-03-31"), 102m),
                new MonthEnd(Date("2025-04-30"), 103.5m),
            ],
            view);

        // A store whose year opens on the last session of a month the pull holds as well takes that month's point from its
        // own close: October's reads the store's 104 on 2025-10-31 and not the pull's 99 there, which reads 101.0204.
        Assert.Equal(
            [new MonthEnd(Date("2025-09-30"), 100m), new MonthEnd(Date("2025-10-31"), 104m), new MonthEnd(Date("2025-11-28"), 105m)],
            LongViews.Of([(Date("2025-09-30"), 98m), (Date("2025-10-31"), 99m)], ratio, [(Date("2025-10-31"), 104m), (Date("2025-11-28"), 105m)]));
        Assert.Equal(101.0204m, LongViews.Scaled(99m, ratio));

        // A name the pulls never held draws its stored year alone.
        Assert.Equal([new MonthEnd(Date("2025-03-31"), 102m), new MonthEnd(Date("2025-04-30"), 103.5m)], LongViews.Of([], 1m, [(Date("2025-03-14"), 101m), (Date("2025-03-31"), 102m), (Date("2025-04-30"), 103.5m)]));
    }

    [Fact]
    public void EachTrancheOfCvxsPlanIsAThirdOfAFullPositionWithItsRiskAndRewardToRiskAndTheThirdStatesNone()
    {
        // A plan as the ladder stores one: three tranches and one traded exit, the top, at 217.78.
        Tranche[] tranches =
        [
            new(200.78m, 208.98m, TrancheCondition.FirstCloseBackAbove, 195.8409m),
            new(195.8409m, 197.82m, TrancheCondition.ReachesTheZone, 184.2619m),
            new(184.2619m, 185.7375m, TrancheCondition.ReachesTheZone, null),
        ];
        Exit[] exits = [new(217.78m, 217.78m, true, true, "1/1", null)];

        var shares = PlanSizes.Of(tranches, exits);

        // Each a third, carried at its zone's midpoint: 409.76 / 2, 393.6609 / 2 and 369.9994 / 2.
        Assert.Equal(["1/3", "1/3", "1/3"], shares.Select(share => share.Share));
        Assert.Equal([204.88m, 196.83045m, 184.9997m], shares.Select(share => share.Buy));
        Assert.Equal((3, 3), PlanSizes.Sum(shares));

        // The first risks 204.88 - 195.8409 = 9.0391 and reaches 217.78 for 12.90, 1.4271 to four places; the second
        // risks 196.83045 - 184.2619 = 12.56855 for 20.94955, 1.6668; the third has no stop and states neither.
        Assert.Equal([9.0391m, 12.56855m, null], shares.Select(share => share.Risk));
        Assert.Equal([12.90m, 20.94955m, 32.7803m], shares.Select(share => share.Targets.Single().Reward));
        Assert.Equal(1.4271m, Math.Round(shares[0].Targets.Single().RewardToRisk!.Value, 4));
        Assert.Equal(1.6668m, Math.Round(shares[1].Targets.Single().RewardToRisk!.Value, 4));
        Assert.Null(shares[2].Targets.Single().RewardToRisk);

        // The one traded exit is the top, where the trail begins.
        Assert.All(shares, share => Assert.True(share.Targets.Single().TrailBegins));

        // An exit not traded is no target, and a plan with no tranche has no share.
        Assert.Empty(PlanSizes.Of(tranches, [new(217.78m, 217.78m, false, false, "0", "closer than two typical days' moves")])[0].Targets);
        Assert.Equal((0, 0), PlanSizes.Sum(PlanSizes.Of([], exits)));
    }

    [Fact]
    public void AMovesCauseIsResearchsThenTheLabelledStoriesInsideItThenItsResultsAndFilingsThenNone()
    {
        // A move from one session's close to the close three sessions later, its first session the one after the
        // first close.
        var from = new DateTimeOffset(2026, 1, 12, 21, 0, 0, TimeSpan.Zero);
        var through = new DateTimeOffset(2026, 1, 15, 21, 0, 0, TimeSpan.Zero);
        var window = new MoveWindow(Date("2026-01-13"), Date("2026-01-15"), from, through);

        CauseStory Story(DateTimeOffset published, string title, string? kind = "company news") =>
            new(published, title, "wire.example", "https://wire.example/" + title, "positive", kind);

        CauseStory[] stories =
        [
            Story(from, "at the base close"),
            Story(from.AddSeconds(1), "a second after"),
            Story(through, "at the last close"),
            Story(through.AddSeconds(1), "a second past"),
            Story(from.AddHours(5), "a column", NewsInstruction.Opinion),
        ];
        CausePrint[] prints = [new(Date("2026-01-13"), "after", Date("2026-01-14")), new(Date("2026-01-20"), "before", Date("2026-01-20"))];
        CauseFiling[] filings = [new(Date("2026-01-15"), "10-Q", "https://www.sec.gov/a"), new(Date("2026-01-16"), "10-K", "https://www.sec.gov/b")];

        // Research's sentences come first, whatever else the move holds.
        Assert.Equal((MoveCauses.Research, "Shares rose on the guidance."), (MoveCauses.For(window, "Shares rose on the guidance.", stories, prints, filings).Kind, MoveCauses.For(window, "Shares rose on the guidance.", stories, prints, filings).Written));

        // Then the stories published after the base close and by the last close, newest first, the opinion piece and the
        // two on either side of the window left out, with the result and the filing inside the move beneath them.
        var news = MoveCauses.For(window, null, stories, prints, filings);

        Assert.Equal(MoveCauses.News, news.Kind);
        Assert.Equal(["at the last close", "a second after"], news.Stories.Select(story => story.Title));
        Assert.Equal([Date("2026-01-14")], news.Prints.Select(print => print.Session));
        Assert.Equal(["10-Q"], news.Filings.Select(filing => filing.Form));

        // With no story inside it, the result and the filing alone.
        var filed = MoveCauses.For(window, null, [stories[0], stories[3], stories[4]], prints, filings);

        Assert.Equal((MoveCauses.Filed, 1, 1), (filed.Kind, filed.Prints.Count, filed.Filings.Count));

        // With none of the four, no cause.
        var none = MoveCauses.For(window, null, [], [prints[1]], [filings[1]]);

        Assert.Equal((MoveCauses.Neither, "none"), (none.Kind, MoveCauses.Neither));
        Assert.Empty(none.Stories);

        // The instants the labels cover are the first and the newest published, none where none was.
        Assert.Equal((from, through.AddSeconds(1)), MoveCauses.LabelsCover(stories[..4])!.Value);
        Assert.Null(MoveCauses.LabelsCover([]));
    }

    [Fact]
    public void ABandsTurnsAreItsSwingsAndVisitsAndTheNewestOfThemIsItsLastTouch()
    {
        // Two swings and a visit, and an average, a retracement and a shelf, which carry no turn.
        var (count, last) = BandTurns.Of(
            [
                ("swing high", Date("2026-06-02")),
                ("touch", Date("2026-09-15")),
                ("swing low", Date("2026-08-01")),
                ("sma200", Date("2026-10-09")),
                ("retracement 61.8", Date("2026-10-09")),
                ("shelf", Date("2026-10-09")),
            ]);

        Assert.Equal((3, Date("2026-09-15")), (count, last!.Value));
        Assert.Equal((0, (DateOnly?)null), BandTurns.Of([("shelf", Date("2026-10-09"))]));
    }

    // The exchange's sessions from a day, as many as asked for.
    static IReadOnlyList<DateOnly> PageSessionsFrom(DateOnly first, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = first; sessions.Count < count; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return sessions;
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Every column of every row a query returns, as text.
    static IReadOnlyList<string[]> PageRows(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var rows = new List<string[]>();

        while (reader.Read())
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(at => Convert.ToString(reader.GetValue(at), CultureInfo.InvariantCulture)!)]);
        }

        return rows;
    }

    [Fact]
    public async Task TheReaderReadsAYearOffItsBarsTheYearsBeforeOffThePullAtItsScaleAndCountsWhatItLeavesOut()
    {
        // Forty sessions before the store's year and sixty in it. AAA's year closes at 100 + n, and its
        // pull stands at 0.98 of the store's scale, closing at (200 + n) x 0.98 before the year and 98 on the year's
        // first, missing the twentieth session before the year. BBB's pull holds every session at the store's scale,
        // closing at 300 + n, its year at 300 + n too. CCC holds a year and no pull.
        var all = PageSessionsFrom(Date("2025-03-03"), 100);
        var before = all.Take(40).ToArray();
        var year = all.Skip(40).ToArray();
        using var store = new TemporaryStore().Migrated();

        void Bar(string ticker, DateOnly day, decimal close) =>
            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{Stamp(day)}', '{close}', '{close}', '{close}', '{close}', 1000, 'bulk', '2025-08-01T21:10:00Z', '{close}');");

        void Pulled(string ticker, DateOnly day, decimal close) =>
            store.Execute($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('{ticker}', '{Stamp(day)}', '{close}', '{close}', '{close}', '{close}', '{close}', 1000, 'history-pull-a');");

        void Print(string table, string day, string timing) =>
            store.Execute(table == "calendar"
                ? $"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('AAA', '{day}', 'earnings', '{timing}', '{{}}', '2025-08-01T21:00:00Z');"
                : $"INSERT INTO pulled_earnings (ticker, event_date, timing, pull) VALUES ('AAA', '{day}', '{timing}', 'history-pull-a');");

        for (var at = 0; at < year.Length; at++)
        {
            Bar("AAA", year[at], 100 + at);
            Bar("BBB", year[at], 300 + at);
            Bar("CCC", year[at], 50 + at);
        }

        for (var at = 0; at < before.Length; at++)
        {
            if (at != 20)
            {
                Pulled("AAA", before[at], (200 + at) * 0.98m);
            }

            Pulled("BBB", before[at], 300 - before.Length + at);
        }

        Pulled("AAA", year[0], 98m);
        Pulled("BBB", year[0], 300m);

        // The calendar's year: a report before the open on the eleventh session of the year, one after the close on the
        // thirty-first, and one after the close on the newest stored session, which has not reacted.
        Print("calendar", Stamp(year[10]), "before");
        Print("calendar", Stamp(year[30]), "after");
        Print("calendar", Stamp(year[^1]), "after");

        // The pull's: before the open on the sixth session before the year, before the open on the twenty-second, read
        // across the twenty-first AAA misses, after the close on the thirty-first, the calendar's eleventh-session report
        // filed again, and one from before the pull starts.
        Print("pulled", Stamp(before[5]), "before");
        Print("pulled", Stamp(before[21]), "before");
        Print("pulled", Stamp(before[30]), "after");
        Print("pulled", Stamp(year[10]), "after");
        Print("pulled", "2024-12-02", "before");

        var reader = new PageHistoryReader(FixedClock.At(new DateTimeOffset(2025, 8, 1, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        var outcome = await reader.RunAsync("page-history-test");

        Assert.Equal((2, 1, 1, 6, 0), (outcome.NamesRead, outcome.NamesWithout, outcome.MovesStated, outcome.RowsWritten, outcome.Stopped));

        var moves = PageRows(store, "SELECT ticker, prints, unread, IFNULL(first_print, ''), CASE WHEN median_move IS NULL THEN '' ELSE printf('%.6f', median_move) END, IFNULL(pull, ''), IFNULL(reason, '') FROM earnings_move ORDER BY ticker;");

        // AAA reads four: the eleventh session's (110 - 109) / 109 off its bars, the thirty-first's after the close,
        // (131 - 130) / 130, the sixth session before the year's (205 - 204) / 204 off the pull, the ratio leaving a
        // move's size as it was, and the thirty-first before it after the close, (231 - 230) / 230. The twenty-second's
        // read across the hole and the one before the pull starts are left out, the newest session's counted nowhere and
        // the eleventh's filed twice read once. The median of the four sizes is the mean of 1/204 and 1/130.
        var median = ((1.0 / 204) + (1.0 / 130)) / 2 * 100;

        Assert.Equal(
            [
                ["AAA", "4", "2", Stamp(before[5]), median.ToString("F6", CultureInfo.InvariantCulture), "history-pull-a", ""],
                ["BBB", "0", "0", "", "", "history-pull-a", ""],
                ["CCC", "0", "0", "", "", "", ChartAverager.NoPull],
            ],
            moves);

        // AAA's long view: each month's last session, the pull's before the year at the store's scale, (200 + n) x 0.98
        // brought back to 200 + n, and the store's year after it at its own closes; the ratio stored beside them.
        var view = PageRows(store, "SELECT ticker, closes, IFNULL(pull, ''), IFNULL(ratio, ''), IFNULL(reason, '') FROM long_view ORDER BY ticker;");
        var expected = before
            .Select((day, at) => (day, close: 200m + at))
            .Where(point => point.day != before[20])
            .Concat(year.Select((day, at) => (day, close: 100m + at)))
            .GroupBy(point => (point.day.Year, point.day.Month))
            .Select(month => month.MaxBy(point => point.day))
            .Select(point => new[] { Stamp(point.day), point.close.ToString(CultureInfo.InvariantCulture) });

        Assert.Equal(JsonSerializer.Serialize(expected), view[0][1]);
        Assert.Equal(("history-pull-a", 100m / 98m), (view[0][2], decimal.Parse(view[0][3], CultureInfo.InvariantCulture)));
        Assert.Equal(["CCC", ChartAverager.NoPull], [view[2][0], view[2][4]]);
    }

    [Fact]
    public async Task TheFixturesNightReadsEachNamesFourPrintsAndItsStoredYearsMonthEndsWithNoPullAsTheExpectationDerives()
    {
        var expected = Expected("page-history");

        using var store = await FixtureReplay.ReplayedAsync();

        var reason = expected.GetProperty("reason").GetString()!;

        Assert.Equal(ChartAverager.NoPull, reason);

        foreach (var name in expected.GetProperty("earningsMoves").EnumerateObject())
        {
            var stated = name.Value;
            var row = PageRows(store, $"SELECT prints, unread, first_print, median_move, IFNULL(pull, ''), reason FROM earnings_move WHERE ticker = '{name.Name}';").Single();

            Assert.Equal(
                [stated.GetProperty("prints").GetInt32().ToString(CultureInfo.InvariantCulture), stated.GetProperty("unread").GetInt32().ToString(CultureInfo.InvariantCulture), stated.GetProperty("firstPrint").GetString()!, string.Empty, reason],
                [row[0], row[1], row[2], row[4], row[5]]);

            // The reactions are stated to four places, so the median read off the stored bars is held to a ten-thousandth.
            Assert.InRange(double.Parse(row[3], CultureInfo.InvariantCulture), stated.GetProperty("medianMove").GetDouble() - 0.0001, stated.GetProperty("medianMove").GetDouble() + 0.0001);

            // The long view: the stored close of the last session of each month the name's stored year holds, and no pull.
            var view = PageRows(store, $"SELECT closes, IFNULL(pull, ''), IFNULL(ratio, ''), reason FROM long_view WHERE ticker = '{name.Name}';").Single();
            string[] sessions = [.. expected.GetProperty("monthEnds").GetProperty(name.Name).EnumerateArray().Select(day => day.GetString()!)];
            var closes = sessions.Select(session => Query(store, $"SELECT close FROM bar WHERE ticker = '{name.Name}' AND session_date = '{session}';").SingleOrDefault() ?? "no bar on " + session).ToArray();

            Assert.Equal(JsonSerializer.Serialize(sessions.Zip(closes, (session, close) => new[] { session, close })), view[0]);
            Assert.Equal([string.Empty, string.Empty, reason], view[1..]);
        }

        Assert.Equal(["4"], Query(store, "SELECT COUNT(*) FROM earnings_move;"));
        Assert.Equal(["4"], Query(store, "SELECT COUNT(*) FROM long_view;"));
    }

    [Fact]
    public async Task AMemberTheCalendarFilesNoPrintOfReadsItsYearsPrintsOffThePullsAndANameItNoLongerHoldsKeepsNoRow()
    {
        // A member that has left the index keeps its bars and loses its calendar rows; the pull's prints over its year
        // are read off its stored bars. Sixty sessions, its pull holding the year's first alone.
        var year = PageSessionsFrom(Date("2025-03-03"), 60);
        using var store = new TemporaryStore().Migrated();

        for (var at = 0; at < year.Count; at++)
        {
            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('GONE', '{Stamp(year[at])}', '{100 + at}', '{100 + at}', '{100 + at}', '{100 + at}', 1000, 'bulk', '2025-06-01T21:10:00Z', '{100 + at}');");
        }

        store.Execute($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('GONE', '{Stamp(year[0])}', '100', '100', '100', '100', '100', 1000, 'history-pull-a');");
        store.Execute($"INSERT INTO pulled_earnings (ticker, event_date, timing, pull) VALUES ('GONE', '{Stamp(year[20])}', 'before', 'history-pull-a');");

        // A name the bar store no longer holds keeps neither row.
        store.Execute("INSERT INTO earnings_move (ticker, night, prints, unread, first_print, median_move, pull, reason) VALUES ('LEFT', '2025-05-01', 4, 0, '2024-01-30', 2.5, NULL, NULL);");
        store.Execute("INSERT INTO long_view (ticker, night, closes, pull, ratio, reason) VALUES ('LEFT', '2025-05-01', '[]', NULL, NULL, NULL);");

        await new PageHistoryReader(FixedClock.At(new DateTimeOffset(2025, 6, 2, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("page-history-test");

        // The print reacted on the twenty-first session, (120 - 119) / 119.
        Assert.Equal(
            [["GONE", "1", "0", Stamp(year[20]), (1.0 / 119 * 100).ToString("F6", CultureInfo.InvariantCulture)]],
            PageRows(store, "SELECT ticker, prints, unread, first_print, printf('%.6f', median_move) FROM earnings_move;"));
        Assert.Equal([["GONE"]], PageRows(store, "SELECT ticker FROM long_view;"));
    }
}
