using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.1: the swing readings and the night's breadth, worked by hand over
// constructed series at each window's edge and over the bars the fixture's replay stores.
public partial class FixtureExpectations
{
    static readonly DateOnly SwingFirst = new(2026, 1, 1);

    // A flat series of the given length closing at 100 on a range of two, with the session at
    // `special` closing at `close` instead. Sessions are consecutive days: the readings count bars,
    // never the calendar, so the days between them are not what is asserted.
    static ReadingBar[] Flat(int length, int special = -1, decimal close = 100m) =>
    [
        .. Enumerable.Range(0, length).Select(day => day == special
            ? new ReadingBar(SwingFirst.AddDays(day), close + 1m, close - 1m, close, 1000)
            : new ReadingBar(SwingFirst.AddDays(day), 101m, 99m, 100m, 1000)),
    ];

    [Fact]
    public void AReturnIsReadAtItsWindowsEdgeAndNotOneBarShortOfIt()
    {
        // 64 bars, the first closing at 80 and the newest at 120, every other at 100. Worked by hand:
        // the return over 63 sessions is 120 against the close 63 sessions before it, the first bar's
        // 80, which is +50 per cent. 64 bars is the fewest holding that close.
        var bars = Flat(64, 0, 80m);
        bars[^1] = bars[^1] with { Close = 120m, High = 121m };

        Assert.Equal(63, SwingReadings.ReturnShortSessions);
        Assert.Equal(50.0, SwingReadings.ReturnOver(bars, SwingReadings.ReturnShortSessions)!.Value, 9);

        // 63 bars is one short: no return, and the row keeps its bar count so the absence says why.
        var shorter = SwingReadings.Of(bars[1..], null, null);

        Assert.Null(shorter.ReturnShort);
        Assert.Equal(63, shorter.Bars);

        // The longer span at its own edge: 127 bars, the first at 150, reads (120 - 150) / 150,
        // which is -20 per cent, and 126 bars read nothing.
        var longer = Flat(127, 0, 150m);
        longer[^1] = longer[^1] with { Close = 120m, High = 121m };

        Assert.Equal(126, SwingReadings.ReturnLongSessions);
        Assert.Equal(-20.0, SwingReadings.Of(longer, null, null).ReturnLong!.Value, 9);
        Assert.Null(SwingReadings.Of(longer[1..], null, null).ReturnLong);
        Assert.Equal(126, SwingReadings.Of(longer[1..], null, null).Bars);

        // A close it would be measured from at nought reads nothing rather than a division by it.
        var fromNothing = Flat(64, 0, 0m);
        Assert.Null(SwingReadings.ReturnOver(fromNothing, SwingReadings.ReturnShortSessions));
    }

    [Fact]
    public void APlaceIsTheShareOfTheOtherMembersStrictlyLowerAndATieCountsHalf()
    {
        // Four members. Worked by hand over the three others each: A at 5 has C below it and B tied
        // with it, (1 + 0.5) / 3 = 0.5; B the same; C at 1 has nobody below, 0; D at 10 has all
        // three below, 1.
        var places = SwingReadings.Places(new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["A"] = 5,
            ["B"] = 5,
            ["C"] = 1,
            ["D"] = 10,
        });

        Assert.Equal(0.5, places["A"], 9);
        Assert.Equal(0.5, places["B"], 9);
        Assert.Equal(0.0, places["C"], 9);
        Assert.Equal(1.0, places["D"], 9);

        // A lone member has no other to be placed among, and nothing is invented for it.
        Assert.Empty(SwingReadings.Places(new Dictionary<string, double>(StringComparer.Ordinal) { ["A"] = 5 }));

        // Two members tied read a half each, the middle of a place with nobody on either side.
        var two = SwingReadings.Places(new Dictionary<string, double>(StringComparer.Ordinal) { ["A"] = 3, ["B"] = 3 });
        Assert.Equal((0.5, 0.5), (two["A"], two["B"]));
    }

    [Fact]
    public void TheRecentHighIsTheNewestSessionMakingItAndThePullbackIsMeasuredFromIt()
    {
        // Twenty bars, the fewest the window reads. Highs 101 but for 110 on the fifth and 110 again
        // on the twelfth, the newest closing at 98. Worked by hand: the high is 110, made last on the
        // twelfth, day 11, so eight sessions have traded since it; with a typical move of 4 the close
        // sits (110 - 98) / 4 = 3 typical moves below it.
        var bars = Flat(20);
        bars[4] = bars[4] with { High = 110m };
        bars[11] = bars[11] with { High = 110m };
        bars[^1] = bars[^1] with { Close = 98m, Low = 97m };

        // Volumes over the eight sessions since: 0, then 100 to 700 in steps of 100. The median of the
        // eight is the mean of the middle two, 300 and 400, so 350, against an average of 500, 0.7.
        // A session trading nothing is a reading of nought and is counted, not skipped.
        for (var day = 12; day < 20; day++)
        {
            bars[day] = bars[day] with { Volume = (day - 12) * 100 };
        }

        var reading = SwingReadings.Of(bars, 4.0, 500.0);

        Assert.Equal(20, SwingReadings.HighWindow);
        Assert.Equal(110m, reading.High);
        Assert.Equal(SwingFirst.AddDays(11), reading.HighSession);
        Assert.Equal(8, reading.PullbackSessions);
        Assert.Equal(3.0, reading.Depth!.Value, 9);
        Assert.Equal(0.7, reading.DryUp!.Value, 9);

        // Nineteen bars is one short of the window: no high, no pullback, no dry-up, and the bar count.
        var shorter = SwingReadings.Of(bars[1..], 4.0, 500.0);

        Assert.Equal((19, (decimal?)null, (DateOnly?)null, (int?)null, (double?)null, (double?)null),
            (shorter.Bars, shorter.High, shorter.HighSession, shorter.PullbackSessions, shorter.Depth, shorter.DryUp));

        // A higher high one session before the window is outside it and moves nothing: the window is
        // the last twenty sessions, ending tonight.
        var longer = SwingReadings.Of([new ReadingBar(SwingFirst.AddDays(-1), 200m, 99m, 100m, 1000), .. bars], 4.0, 500.0);

        Assert.Equal((110m, SwingFirst.AddDays(11), 8), (longer.High, longer.HighSession, longer.PullbackSessions));

        // No typical move and no average leave the two readings that divide by them absent rather than
        // infinite, with the high and the sessions since it still read.
        var bare = SwingReadings.Of(bars, null, 0.0);

        Assert.Equal((110m, (double?)null, (double?)null), (bare.High, bare.Depth, bare.DryUp));
    }

    [Fact]
    public void AHighMadeTonightHasNoPullbackAndNoDryUp()
    {
        // The newest bar makes the high at 120 and closes at 119. Worked by hand: no session has traded
        // since the high, so the pullback is 0 sessions and there is no volume to take a median of;
        // the close sits 1 below the high, a quarter of a typical move of 4.
        var bars = Flat(20);
        bars[^1] = bars[^1] with { High = 120m, Close = 119m };

        var reading = SwingReadings.Of(bars, 4.0, 500.0);

        Assert.Equal((120m, bars[^1].SessionDate, 0), (reading.High, reading.HighSession, reading.PullbackSessions));
        Assert.Equal(0.25, reading.Depth!.Value, 9);
        Assert.Null(reading.DryUp);
    }

    [Fact]
    public void TightnessIsTheLastTenSessionsRangeAgainstTheLastFiftyAndNeedsOneBarBeforeThem()
    {
        // Fifty-one bars closing at 100, so each session's true range is its own high less its low.
        // The first forty of the last fifty trade a range of 2 and the last ten a range of 1. Worked by
        // hand: the ten-session mean is 1, the fifty-session mean (40 x 2 + 10 x 1) / 50 = 1.8, and the
        // tightness 1 / 1.8.
        var bars = Flat(51);

        for (var day = 41; day < 51; day++)
        {
            bars[day] = bars[day] with { High = 100.5m, Low = 99.5m };
        }

        Assert.Equal((10, 50), (SwingReadings.TightShortSessions, SwingReadings.TightLongSessions));
        Assert.Equal(1.0 / 1.8, SwingReadings.Tightness(bars)!.Value, 9);

        // Fifty bars hold the fifty sessions but not the close before the first of them, so the first
        // session's true range cannot be read: none, rather than a mean over forty-nine.
        Assert.Null(SwingReadings.Tightness(bars[1..]));

        // The distance from the close before counts in the true range. The session before the newest
        // trades 100.5 to 89.5 and closes at 90, a range of 11; the newest trades 100.5 to 99.5 after
        // that close of 90, so its true range is 10.5 rather than 1. Worked by hand: the ten-session
        // mean is (8 x 1 + 11 + 10.5) / 10 = 2.95 and the fifty-session mean (40 x 2 + 8 x 1 + 11 + 10.5)
        // / 50 = 2.19.
        var gapped = bars.ToArray();
        gapped[^2] = gapped[^2] with { Close = 90m, High = 100.5m, Low = 89.5m };

        Assert.Equal(2.95 / 2.19, SwingReadings.Tightness(gapped)!.Value, 9);
    }

    [Fact]
    public void BreadthIsReadAtExactlyHalfTheMembersAndNotOneShortOfIt()
    {
        // Four members, two holding a close and an average, one of them above it. Worked by hand: two
        // of four is exactly half, so breadth is read, one above of two counted, 0.5.
        var half = SwingReadings.BreadthOf(4, [(110m, 100.0), (90m, 100.0)]);

        Assert.Equal((4, 2, 1), (half.Members, half.Counted, half.Above));
        Assert.Equal(0.5, half.Share!.Value, 9);

        // One of four is one short of half: not available, with the counts kept so the page can say why.
        var short1 = SwingReadings.BreadthOf(4, [(110m, 100.0)]);

        Assert.Equal((4, 1, 1), (short1.Members, short1.Counted, short1.Above));
        Assert.Null(short1.Share);

        // An odd count: two of five is under half and three of five is over it.
        Assert.Null(SwingReadings.BreadthOf(5, [(110m, 100.0), (110m, 100.0)]).Share);
        Assert.Equal(2.0 / 3.0, SwingReadings.BreadthOf(5, [(110m, 100.0), (110m, 100.0), (90m, 100.0)]).Share!.Value, 9);

        // A close exactly on its average is not above it.
        Assert.Equal(0, SwingReadings.BreadthOf(2, [(100m, 100.0), (100m, 100.0)]).Above);

        // Nobody held is no breadth, whatever the member count.
        Assert.Null(SwingReadings.BreadthOf(0, []).Share);
    }

    [Fact]
    public void TheAveragesBreadthReadsAreTheIndicatorsTheNightStores()
    {
        // The reader selects the stored averages by the name the constant gives, so a figure stated in
        // section 17 is the average read, and a window moved in one place and not the other finds no row.
        Assert.Equal(IndicatorSeries.Sma200, SwingReadings.AverageNamed(SwingReadings.BreadthAverageSessions));
        Assert.Equal(IndicatorSeries.Sma50, SwingReadings.AverageNamed(SwingReadings.ContextAverageSessions));

        // The median's rule, over an odd count, an even one and nothing.
        Assert.Equal(2.0, SwingReadings.Median([3.0, 1.0, 2.0]));
        Assert.Equal(2.5, SwingReadings.Median([4.0, 1.0, 2.0, 3.0]));
        Assert.Null(SwingReadings.Median([]));
    }

    [Fact]
    public async Task TheReaderPlacesEachReturnAmongTheMembersOverItsOwnSpanAndKeepsARowForEveryMember()
    {
        // Constructed so the two spans order the members differently, which the fixture's names do
        // not: over 126 sessions ZZA is ahead, over 63 ZZB is. Each series closes at its first value
        // until the session 63 before the night, at its second from there, and at its third on the
        // night. Worked by hand:
        //   ZZA 100, 200, 220: +10% over 63, +120% over 126
        //   ZZB 100, 100, 150: +50% over 63, +50% over 126
        //   ZZC 100, 150, 180: +20% over 63, +80% over 126
        // so over 63 ZZB places 1, ZZC 0.5 and ZZA 0; over 126 ZZA places 1, ZZC 0.5 and ZZB 0.
        // ZZD holds no bar at all and ZZE's last bar is the session before the night.
        using var store = new TemporaryStore().Migrated();

        var sessions = RecordedHistoricalBarFeed
            .Parse(File.ReadAllText(Path.Combine(Folder(), "bars-KEYS.json")), "KEYS")
            .Select(bar => bar.SessionDate)
            .Where(session => session <= new DateOnly(2026, 9, 4))
            .Order()
            .TakeLast(SwingReadings.ReturnLongSessions + 1)
            .ToArray();
        var night = sessions[^1];

        string Day(DateOnly on) => on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        void Series(string ticker, int first, int middle, int last, int drop = 0)
        {
            for (var at = 0; at < sessions.Length - drop; at++)
            {
                var close = at == sessions.Length - 1 ? last : at >= sessions.Length - 1 - SwingReadings.ReturnShortSessions ? middle : first;

                store.Execute(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                    $"VALUES ('{ticker}', '{Day(sessions[at])}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '2026-09-05T21:00:00Z', '{close}');");
            }
        }

        Series("ZZA", 100, 200, 220);
        Series("ZZB", 100, 100, 150);
        Series("ZZC", 100, 150, 180);
        Series("ZZE", 100, 100, 100, drop: 1);

        foreach (var ticker in new[] { "ZZA", "ZZB", "ZZC", "ZZD", "ZZE" })
        {
            store.Execute(
                "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) " +
                $"VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-09-05T21:00:00Z');");
        }

        // The night's indicators for the three read: a typical move, the fifty-day volume and the
        // shorter average, and no 200-day average, so breadth is held by none of the five.
        foreach (var ticker in new[] { "ZZA", "ZZB", "ZZC" })
        {
            foreach (var (name, value) in new[] { (IndicatorSeries.Atr14, 2.0), (IndicatorSeries.VolAvg50, 1000.0), (IndicatorSeries.Sma50, 120.0) })
            {
                store.Execute(
                    "INSERT INTO indicator (ticker, session_date, name, value, bar_count) " +
                    $"VALUES ('{ticker}', '{Day(night)}', '{name}', {value.ToString(CultureInfo.InvariantCulture)}, {sessions.Length});");
            }
        }

        var outcome = await new SwingReader(
            FixedClock.At(new DateTimeOffset(2026, 9, 5, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "constructed-swing-readings");

        Assert.Equal(
            [
                "ZZA|10|120|0|1|0.5|",
                "ZZB|50|50|1|0|0.5|",
                "ZZC|20|80|0.5|0.5|0.5|",
                "ZZD||||||" + SwingReader.NoBarStored,
                $"ZZE||||||no bar for this session; the last session stored for the name is {Day(sessions[^2])}",
            ],
            Query(
                store,
                "SELECT ticker || '|' || CASE WHEN return_short IS NULL THEN '' ELSE printf('%g', return_short) END || '|' || CASE WHEN return_long IS NULL THEN '' ELSE printf('%g', return_long) END || '|' || " +
                "CASE WHEN place_short IS NULL THEN '' ELSE printf('%g', place_short) END || '|' || CASE WHEN place_long IS NULL THEN '' ELSE printf('%g', place_long) END || '|' || CASE WHEN strength IS NULL THEN '' ELSE printf('%g', strength) END || '|' || " +
                $"IFNULL(note, '') FROM swing_reading WHERE session_date = '{Day(night)}' ORDER BY ticker;"));

        // Breadth is held by none, so it is not available and says how many held it; the shorter
        // average is held by the three read, all above it, which is three of five and so read.
        Assert.Equal(
            ["5|0|0||3|3|1|3|1"],
            Query(
                store,
                "SELECT members || '|' || counted || '|' || above || '|' || IFNULL(breadth, '') || '|' || counted_context || '|' || " +
                $"above_context || '|' || printf('%g', breadth_context) || '|' || volume_counted || '|' || printf('%g', median_volume_ratio) FROM market_reading WHERE session_date = '{Day(night)}';"));

        Assert.Equal((5, 5, 3, 1, 0, 1), (outcome.Members, outcome.RowsWritten, outcome.Read, outcome.Stale, outcome.Gapped, outcome.NoBars));
        Assert.Contains(
            "5 member(s), 3 read, 1 with no bar for the session, 0 gapped, 1 holding no bar, 0 dropped; breadth not available, 0 of 5 members holding a close and a 200-day average",
            Query(store, "SELECT detail FROM run_log WHERE stage = 'swing-readings';").Single(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFixturesSwingReadingsAreTheOnesWorkedByHandFromTheCapturedBars()
    {
        var expected = Expected("swing-readings");
        var windows = expected.GetProperty("windows");

        Assert.Equal(
            (SwingReadings.ReturnShortSessions, SwingReadings.ReturnLongSessions, SwingReadings.HighWindow,
                SwingReadings.TightShortSessions, SwingReadings.TightLongSessions,
                SwingReadings.BreadthAverageSessions, SwingReadings.ContextAverageSessions),
            (windows.GetProperty("returnShort").GetInt32(), windows.GetProperty("returnLong").GetInt32(), windows.GetProperty("high").GetInt32(),
                windows.GetProperty("tightShort").GetInt32(), windows.GetProperty("tightLong").GetInt32(),
                windows.GetProperty("breadthAverage").GetInt32(), windows.GetProperty("contextAverage").GetInt32()));

        using var store = await FixtureReplay.ReplayedAsync();

        var night = expected.GetProperty("night").GetString()!;

        // One row per member on the night and none for any other, the names the night read nothing
        // for among them.
        Assert.Equal(
            expected.GetProperty("readings").EnumerateObject().Select(name => name.Name)
                .Concat(expected.GetProperty("notes").EnumerateObject().Select(name => name.Name))
                .Order(StringComparer.Ordinal),
            Query(store, $"SELECT ticker FROM swing_reading WHERE session_date = '{night}' ORDER BY ticker;"));

        var read = 0;

        foreach (var name in expected.GetProperty("readings").EnumerateObject())
        {
            var want = name.Value;
            var stored = Assert.Single(Query(
                store,
                "SELECT bars || '|' || return_short || '|' || return_long || '|' || place_short || '|' || place_long || '|' || strength || '|' || " +
                "recent_high || '|' || high_session || '|' || pullback_sessions || '|' || depth || '|' || dry_up || '|' || tightness || '|' || IFNULL(note, '') " +
                $"FROM swing_reading WHERE ticker = '{name.Name}' AND session_date = '{night}';")).Split('|');

            Assert.Equal(want.GetProperty("bars").GetInt32(), int.Parse(stored[0], CultureInfo.InvariantCulture));
            Near(want.GetProperty("returnShort"), stored[1]);
            Near(want.GetProperty("returnLong"), stored[2]);
            Near(want.GetProperty("placeShort"), stored[3]);
            Near(want.GetProperty("placeLong"), stored[4]);
            Near(want.GetProperty("strength"), stored[5]);
            Assert.Equal(
                decimal.Parse(want.GetProperty("recentHigh").GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(stored[6], CultureInfo.InvariantCulture));
            Assert.Equal(want.GetProperty("highSession").GetString(), stored[7]);
            Assert.Equal(want.GetProperty("pullbackSessions").GetInt32(), int.Parse(stored[8], CultureInfo.InvariantCulture));
            Near(want.GetProperty("depth"), stored[9]);
            Near(want.GetProperty("dryUp"), stored[10]);
            Near(want.GetProperty("tightness"), stored[11]);
            Assert.Equal(string.Empty, stored[12]);

            // The ends the figures rest on, each read off what the replay stored: the close, the two
            // sessions the returns are measured from, and the typical move and the averages the reader
            // took from the night's indicators rather than working its own.
            Assert.Equal(
                decimal.Parse(want.GetProperty("close").GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(Query(store, $"SELECT close FROM bar WHERE ticker = '{name.Name}' AND session_date = '{night}';").Single(), CultureInfo.InvariantCulture));
            Assert.Equal(
                want.GetProperty("returnShortFrom").GetString(),
                Query(store, $"SELECT session_date FROM bar WHERE ticker = '{name.Name}' ORDER BY session_date DESC LIMIT 1 OFFSET {SwingReadings.ReturnShortSessions};")[0]);
            Assert.Equal(
                want.GetProperty("returnLongFrom").GetString(),
                Query(store, $"SELECT session_date FROM bar WHERE ticker = '{name.Name}' ORDER BY session_date DESC LIMIT 1 OFFSET {SwingReadings.ReturnLongSessions};")[0]);
            Near(want.GetProperty("typicalMove"), StoredIndicator(store, name.Name, night, IndicatorSeries.Atr14));
            Near(want.GetProperty("volumeAverage50"), StoredIndicator(store, name.Name, night, IndicatorSeries.VolAvg50));
            Near(want.GetProperty("sma200"), StoredIndicator(store, name.Name, night, IndicatorSeries.Sma200));
            Near(want.GetProperty("sma50"), StoredIndicator(store, name.Name, night, IndicatorSeries.Sma50));

            read++;
        }

        Assert.Equal(3, read);

        // The member the night read nothing for keeps its row, every reading absent and the reason on it.
        foreach (var note in expected.GetProperty("notes").EnumerateObject())
        {
            Assert.Equal(
                "|||||" + note.Value.GetString(),
                Assert.Single(Query(
                    store,
                    "SELECT IFNULL(return_short, '') || '|' || IFNULL(place_short, '') || '|' || IFNULL(recent_high, '') || '|' || " +
                    "IFNULL(depth, '') || '|' || IFNULL(tightness, '') || '|' || note " +
                    $"FROM swing_reading WHERE ticker = '{note.Name}' AND session_date = '{night}';")));
        }

        var market = expected.GetProperty("market");
        var row = Assert.Single(Query(
            store,
            "SELECT members || '|' || counted || '|' || above || '|' || breadth || '|' || counted_context || '|' || above_context || '|' || " +
            $"breadth_context || '|' || volume_counted || '|' || median_volume_ratio FROM market_reading WHERE session_date = '{night}';")).Split('|');

        Assert.Equal(
            (market.GetProperty("members").GetInt32(), market.GetProperty("counted").GetInt32(), market.GetProperty("above").GetInt32()),
            (int.Parse(row[0], CultureInfo.InvariantCulture), int.Parse(row[1], CultureInfo.InvariantCulture), int.Parse(row[2], CultureInfo.InvariantCulture)));
        Near(market.GetProperty("breadth"), row[3]);
        Assert.Equal(
            (market.GetProperty("countedContext").GetInt32(), market.GetProperty("aboveContext").GetInt32()),
            (int.Parse(row[4], CultureInfo.InvariantCulture), int.Parse(row[5], CultureInfo.InvariantCulture)));
        Near(market.GetProperty("breadthContext"), row[6]);
        Assert.Equal(market.GetProperty("volumeCounted").GetInt32(), int.Parse(row[7], CultureInfo.InvariantCulture));
        Near(market.GetProperty("medianVolumeRatio"), row[8]);

        // The night's row is the only one, and the two stores are the ones the expectation names.
        Assert.Equal(["1"], Query(store, "SELECT COUNT(*) FROM market_reading;"));
        Assert.Equal(
            ["market_reading", "swing_reading"],
            expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!).Order(StringComparer.Ordinal));
    }

    static string StoredIndicator(TemporaryStore store, string ticker, string night, string name) =>
        Query(store, $"SELECT value FROM indicator WHERE ticker = '{ticker}' AND session_date = '{night}' AND name = '{name}';").Single();

    static void Near(JsonElement want, string stored) =>
        Assert.InRange(Math.Abs(double.Parse(stored, CultureInfo.InvariantCulture) - want.GetDouble()), 0, 0.0001);
}
