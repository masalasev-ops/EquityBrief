using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Members;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 15.2's second pull request: every reading the member reader stores for every member of the three
// indices each night, each worked by hand at a constructed session as it stood, the switches on the store's own sessions,
// and the fixture's night worked from its captured payloads with the rating counts and the interest expense the quarters
// step stores after it.
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
public partial class FixtureExpectations
{
    // The rows the rest of the readings add that this check reaches: section 17's, section 18's and the fixture's.
    internal static readonly string[] MemberReadingExpectationClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Readings of the 400's and 600's rules"),
        CheckReach.Key(Scope.FailureTable, "A member's quarters fetched before their interest expense was stored"),
        CheckReach.Key(Scope.FixtureTable, "member readings"),
    ];

    // The night a constructed member is read on, a Friday the exchange traded.
    static readonly DateOnly ReadNight = new(2026, 9, 4);

    // The sessions to the night, the last of them the night.
    static IReadOnlyList<DateOnly> ReadSessions(int count) =>
        [.. ExchangeClosures.SessionsBetween(ReadNight.AddDays(-2 * count), ReadNight.AddDays(1)).TakeLast(count)];

    // A member's bars, one a session where it is held: its close, its high the close unless given, its volume a thousand
    // unless given, and its raw close the close.
    static void ReadBars(TemporaryStore store, string ticker, IReadOnlyList<DateOnly> sessions, Func<int, decimal> close, Func<int, decimal>? high = null, Func<int, long>? volume = null, Func<int, bool>? held = null) =>
        Insert(
            store,
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            string.Join(", ", Enumerable.Range(0, sessions.Count)
                .Where(at => held?.Invoke(at) ?? true)
                .Select(at => FormattableString.Invariant(
                    $"('{ticker}', '{Day(sessions[at])}', '{close(at)}', '{high?.Invoke(at) ?? close(at)}', '{close(at)}', '{close(at)}', {volume?.Invoke(at) ?? 1000}, 'test', '{Day(sessions[at])}T21:00:00Z', '{close(at)}')"))) + ";");

    static void ReadMember(TemporaryStore store, string index, string ticker, DateOnly? joined = null) =>
        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES " +
            $"('{index}', '{ticker}', {(joined is { } on ? $"'{Day(on)}'" : "NULL")}, NULL, '2026-01-02T21:00:00Z');");

    // One fetch made on the session before the night: the company's sector and industry, and its four quarters to
    // 2026-06-30, each filed a month after it ends with the net income, operating income and interest expense given, the
    // count given and the basis its counts are stated on, the interest expense read on every quarter but the one given.
    static void ReadFetch(
        TemporaryStore store,
        string ticker,
        DateOnly fetchedOn,
        string? sector,
        string? industry,
        decimal? shares,
        DateOnly basis,
        decimal basisClose,
        decimal net,
        decimal operating,
        decimal? interest,
        string? interestUnread = null)
    {
        var at = $"{Day(fetchedOn)}T22:00:00Z";

        Insert(
            store,
            "INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES " +
            $"('{ticker}', '{at}', NULL, {Text(sector)}, NULL, {Text(industry)}, NULL);");

        foreach (var (periodEnd, filed) in new[] { ("2025-09-30", "2025-10-30"), ("2025-12-31", "2026-01-30"), ("2026-03-31", "2026-04-30"), ("2026-06-30", "2026-07-30") })
        {
            Insert(
                store,
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income, interest_expense, interest_read, shares, basis_session, basis_close) VALUES " +
                FormattableString.Invariant(
                    $"('{ticker}', '{at}', '{Day(fetchedOn)}', '{periodEnd}', '{filed}', '{net}', '{operating}', {Text(interest?.ToString(CultureInfo.InvariantCulture))}, {(periodEnd == interestUnread ? 0 : 1)}, {Text(shares?.ToString(CultureInfo.InvariantCulture))}, '{Day(basis)}', '{basisClose}');"));
        }
    }

    static string Text(string? value) => value is null ? "NULL" : $"'{value}'";

    static void Report(TemporaryStore store, string ticker, DateOnly on, string surprise) =>
        Insert(
            store,
            "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES " +
            $"('{ticker}', '{Day(on)}', 'earnings', 'after', '{{\"surprise\":\"{surprise}\"}}', '{Day(on)}T22:00:00Z');");

    // A member's row as the assertions read it, each figure to its places and null where it holds none: printf reads a
    // null as nought.
    static IReadOnlyList<string> MemberRow(TemporaryStore store, string ticker, DateOnly night)
    {
        static string Figure(string column, int places) => $"CASE WHEN {column} IS NULL THEN 'null' ELSE printf('%.{places}f', {column}) END";

        return Query(
            store,
            "SELECT index_code, IFNULL(close, 'null'), IFNULL(dollar_volume, 'null'), IFNULL(company_value, 'null'), " +
            $"{Figure("cost", 6)}, {Figure("cost_double", 6)}, profit, IFNULL(coverage, 'null'), IFNULL(state, 'null'), " +
            $"IFNULL(year_high, 'null'), {Figure("nearness", 10)}, IFNULL(since_high, 'null'), {Figure("volume_ratio", 10)}, " +
            $"IFNULL(industry, 'null'), {Figure("industry_month", 10)}, {Figure("industry_quarter", 10)}, {Figure("peer_surprise", 10)} " +
            $"FROM member_reading WHERE ticker = '{ticker}' AND session_date = '{Day(night)}';").Single().Split('|');
    }

    static string Fixed(double value, int places) => value.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    [Fact]
    public async Task EachMemberReadingIsWorkedByHandAtAConstructedSessionAsItStood()
    {
        using var store = new TemporaryStore().Migrated();

        var sessions = ReadSessions(70);
        var at = sessions.Count - 1;
        var night = sessions[at];
        var before = sessions[at - 1];

        // The S&P 500's A1 to A5, the S&P 400's M1 and the S&P 600's S1; A5 joined three sessions before the night.
        foreach (var (index, ticker) in new[] { ("GSPC", "A1"), ("GSPC", "A2"), ("GSPC", "A3"), ("GSPC", "A4"), ("MID", "M1"), ("SML", "S1") })
        {
            ReadMember(store, index, ticker);
        }

        ReadMember(store, "GSPC", "A5", sessions[at - 3]);

        // A1: 80 to 63 sessions before the night, 100 from there and 110 over the last 21 sessions, its high 120 on the
        // sessions 30 and 25 before the night, and 1,500 shares on the night. A2: 50 and 45 over the last 21. A3: 20.
        // A4: 10 and then 30, its series holding a hole 40 sessions before the night. A5: 200 over its last 16 sessions.
        // M1: 10 and then 20. S1: 8.
        ReadBars(store, "A1", sessions, bar => bar <= at - 63 ? 80m : bar <= at - 21 ? 100m : 110m, bar => bar == at - 30 || bar == at - 25 ? 120m : bar <= at - 63 ? 80m : bar <= at - 21 ? 100m : 110m, bar => bar == at ? 1500 : 1000);
        ReadBars(store, "A2", sessions, bar => bar <= at - 21 ? 50m : 45m);
        ReadBars(store, "A3", sessions, _ => 20m);
        ReadBars(store, "A4", sessions, bar => bar <= at - 21 ? 10m : 30m, held: bar => bar != at - 40);
        ReadBars(store, "A5", sessions, _ => 200m, held: bar => bar >= at - 15);
        ReadBars(store, "M1", sessions, bar => bar <= at - 21 ? 10m : 20m);
        ReadBars(store, "S1", sessions, _ => 8m);

        // The night's fifty-day average volumes: A1's a thousand, A2's nothing, and none stored for the rest.
        Insert(store, $"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('A1', '{Day(night)}', 'vol_avg50', 1000.0, 50), ('A2', '{Day(night)}', 'vol_avg50', 0.0, 50);");

        // Each fetch on the session before the night, its counts stated on that session's close. A1 earns 10 a quarter on 20
        // of operating income against 10 of interest; A2 loses 5, its newest quarter fetched without its interest expense;
        // A3 files no count, earning 1 on 10 against 6 of interest; A4 and A5 earn 1 and file no interest expense; M1 earns
        // 2 on 1 against 1; S1, a financial company filing no industry and no count, earns 1 on a tenth against 5.
        ReadFetch(store, "A1", before, "Information Technology", "Semiconductors", 30_000_000m, before, 110m, 10m, 20m, 10m);
        ReadFetch(store, "A2", before, "Information Technology", "Semiconductors", 90_000_000m, before, 45m, -5m, 20m, 1m, interestUnread: "2026-06-30");
        ReadFetch(store, "A3", before, "Consumer Staples", "Tobacco", null, before, 20m, 1m, 10m, 6m);
        ReadFetch(store, "A4", before, "Information Technology", "Semiconductors", 1_000_000_000m, before, 30m, 1m, 10m, null);
        ReadFetch(store, "A5", before, "Information Technology", "Semiconductors", 10_000_000m, before, 200m, 1m, 10m, null);
        ReadFetch(store, "M1", before, "Information Technology", "Semiconductors", 1_000_000_000m, before, 20m, 2m, 1m, 1m);
        ReadFetch(store, "S1", before, "Financials", null, null, before, 8m, 1m, 0.1m, 5m);

        // A quarter A1 filed on the night itself, a loss of 1,000, which the night does not read.
        Insert(
            store,
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income, interest_expense, interest_read, shares, basis_session, basis_close) VALUES " +
            $"('A1', '{Day(before)}T22:00:00Z', '{Day(before)}', '2026-07-31', '{Day(night)}', '-1000', '-1000', '10', 1, '30000000', '{Day(before)}', '110');");

        // The states the night's fundamental readings stored.
        Insert(store, $"INSERT INTO fundamental_reading (ticker, session_date, state, readings) VALUES ('A1', '{Day(night)}', 'improving', '{{}}'), ('A2', '{Day(night)}', 'deteriorating', '{{}}');");

        // The reports: A1's 5 per cent beat five sessions before the night and A2's 2 per cent miss ten before it, the two
        // that count; A1's 40 per cent 25 sessions before, outside the window; A5's 50 per cent eight sessions before, before
        // it joined; M1's 30 per cent, an S&P 400 member's; and A2's 60 per cent on the night itself.
        Report(store, "A1", sessions[at - 5], "5.0");
        Report(store, "A2", sessions[at - 10], "-2.0");
        Report(store, "A1", sessions[at - 25], "40.0");
        Report(store, "A5", sessions[at - 8], "50.0");
        Report(store, "M1", sessions[at - 6], "30.0");
        Report(store, "A2", night, "60.0");

        // A row of an earlier session, which a night run again leaves.
        Insert(store, $"INSERT INTO member_reading (index_code, session_date, ticker, profit) VALUES ('GSPC', '{Day(sessions[at - 2])}', 'A1', 1);");

        var clock = FixedClock.At(new DateTimeOffset(night.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new MemberReader(clock, store.DatabaseFile).RunAsync("GSPC", "members-first", wider: ["MID", "SML"]);

        Assert.Equal((night, 7, 6, 1), (outcome.Night, outcome.Members, outcome.WithABar, outcome.Industries));

        // The industry's S&P 500 members over 21 sessions, each weighted by its company's value on the first: A1 worth 30
        // million times 100, 3 billion, up 10 per cent, and A2 worth 90 million times 50, 4.5 billion, down 10 per cent,
        // (3 x 0.10 - 4.5 x 0.10) / 7.5 = -0.02. Over 63: A1 worth 2.4 billion at 80, up 37.5 per cent, and A2 4.5 billion,
        // down 10, (2.4 x 0.375 - 4.5 x 0.10) / 6.9 = 0.0652173913. A4, whose series holds a hole, A5, holding no bar on
        // either first session, and M1, no S&P 500 member, read in neither; A3's Tobacco holds no member with a value.
        var month = (3.0 * 0.10 - 4.5 * 0.10) / 7.5;
        var quarter = (2.4 * 0.375 - 4.5 * 0.10) / 6.9;

        // Their mean surprise over the 20 sessions before the night, each weighted by its value on the session before it:
        // A1's 5 at 3.3 billion and A2's -2 at 4.05 billion, (3.3 x 5 - 4.05 x 2) / 7.35 = 1.1428571429.
        var surprise = (3.3 * 5.0 - 4.05 * 2.0) / 7.35;

        string Spread(int price, int value, int multiple = 1) => (PublishedSpread[price][value] * multiple).ToString("F6", CultureInfo.InvariantCulture);

        // A1: a close of 110 as traded; the mean of 29 sessions at 100 and 20 at 110 on a thousand shares and the night's 110
        // on 1,500, 5,265,000 / 50 = 105,300; worth 30 million times 110, 3.3 billion, at $40 and over the table's 0.072; the
        // profit gate and the coverage passing over the four quarters filed before the night, its loss filed on the night
        // not read; its high of 120 made 25 sessions before the night, the newer of two, and the close at 110 / 120 of it;
        // and 1,500 against its average of 1,000.
        Assert.Equal(
            ["GSPC", "110", "105300", "3300000000", Spread(4, 3), Spread(4, 3, 2), "1", "1", "improving", "120", Fixed(110.0 / 120.0, 10), "25", Fixed(1.5, 10), "Semiconductors", Fixed(month, 10), Fixed(quarter, 10), Fixed(surprise, 10)],
            MemberRow(store, "A1", night));

        // A2: 45; 29 sessions at 50 and 21 at 45, 47,900; worth 4.05 billion; the profit gate failing on its losses and its
        // coverage not read, a quarter fetched without its interest expense; its high of 50 made 21 sessions before; and no
        // volume ratio over an average of nothing.
        Assert.Equal(
            ["GSPC", "45", "47900", "4050000000", Spread(4, 3), Spread(4, 3, 2), "0", "null", "deteriorating", "50", Fixed(0.9, 10), "21", "null", "Semiconductors", Fixed(month, 10), Fixed(quarter, 10), Fixed(surprise, 10)],
            MemberRow(store, "A2", night));

        // A3: no count, so no value and its cost read in the $1 to 2 billion band at $20 to 40, 0.089; its coverage failing
        // at 40 against twice 24; and its Tobacco reading none.
        Assert.Equal(
            ["GSPC", "20", "20000", "null", Spread(3, 2), Spread(3, 2, 2), "1", "0", "null", "20", Fixed(1.0, 10), "1", "null", "Tobacco", "null", "null", "null"],
            MemberRow(store, "A3", night));

        // A4: its series holds a hole, so it is read over none of its bars, and its quarters and its industry still read.
        Assert.Equal(
            ["GSPC", "null", "null", "null", "null", "null", "1", "1", "null", "null", "null", "null", "null", "Semiconductors", Fixed(month, 10), Fixed(quarter, 10), Fixed(surprise, 10)],
            MemberRow(store, "A4", night));

        // A5: 16 bars, fewer than the 50 its dollar volume needs; worth 10 million times 200, 2 billion, at the band's edge.
        Assert.Equal(
            ["GSPC", "200", "null", "2000000000", Spread(4, 3), Spread(4, 3, 2), "1", "1", "null", "200", Fixed(1.0, 10), "1", "null", "Semiconductors", Fixed(month, 10), Fixed(quarter, 10), Fixed(surprise, 10)],
            MemberRow(store, "A5", night));

        // M1, under the S&P 400: 20 billion at $20 to 40, 0.056; its coverage failing at 4 against twice 4; and its
        // industry read off the S&P 500's members, its own rise and its own report counting in neither.
        Assert.Equal(
            ["MID", "20", "14200", "20000000000", Spread(3, 3), Spread(3, 3, 2), "1", "0", "null", "20", Fixed(1.0, 10), "1", "null", "Semiconductors", Fixed(month, 10), Fixed(quarter, 10), Fixed(surprise, 10)],
            MemberRow(store, "M1", night));

        // S1, under the S&P 600: no count, at $6 to 10, 0.127; a financial company passing the coverage; no industry filed.
        Assert.Equal(
            ["SML", "8", "8000", "null", Spread(1, 2), Spread(1, 2, 2), "1", "1", "null", "8", Fixed(1.0, 10), "1", "null", "null", "null", "null", "null"],
            MemberRow(store, "S1", night));

        // The run log names the night, the members read, those holding a bar, and the name stopped at its hole.
        Assert.Contains(
            $"7 member(s) read for {Day(night)}, 6 holding a bar on it, 1 industr(ies) of the S&P 500 read; the switches read, 1 stopped at a gap (A4 {Day(sessions[at - 40])})",
            Query(store, "SELECT detail FROM run_log WHERE run_id = 'members-first';").Single(),
            StringComparison.Ordinal);

        // A night run again writes its own session's rows once more and leaves the earlier session's.
        await new MemberReader(clock, store.DatabaseFile).RunAsync("GSPC", "members-again", wider: ["MID", "SML"]);

        Assert.Equal(["7", "1"], Query(store, $"SELECT COUNT(*) FROM member_reading GROUP BY session_date ORDER BY session_date DESC;"));
        Assert.Equal(["1"], Query(store, "SELECT COUNT(*) FROM switch_reading;"));
    }

    [Fact]
    public async Task TheSwitchesAreReadOnTheStoresOwnSessionsAndNoneWhereAFundsCloseIsMissing()
    {
        using var store = new TemporaryStore().Migrated();

        var sessions = ReadSessions(253);
        var at = sessions.Count - 1;
        var night = sessions[at];

        ReadMember(store, "GSPC", "Z");
        ReadBars(store, "Z", sessions, _ => 10m);

        // SPY at 100 a year of sessions before the night, 110 half a year before and 121 on it; IJH at 50, 60 and 72; IJR at
        // 80, 70 and 70; and HYG at 80 until the last 50 sessions, which rise a tenth a session to 85 on the night.
        decimal Fund(string fund, int bar) => fund switch
        {
            "SPY" => bar <= at - 252 ? 100m : bar <= at - 126 ? 110m : bar < at ? 115m : 121m,
            "IJH" => bar <= at - 252 ? 50m : bar <= at - 126 ? 60m : bar < at ? 65m : 72m,
            "IJR" => bar <= at - 252 ? 80m : 70m,
            _ => bar <= at - 50 ? 80m : 80m + ((bar - (at - 50)) * 0.1m),
        };

        foreach (var fund in new[] { "SPY", "IJH", "IJR", "HYG" })
        {
            Insert(
                store,
                "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
                string.Join(", ", Enumerable.Range(0, sessions.Count).Select(bar => FormattableString.Invariant(
                    $"('{fund}', '{Day(sessions[bar])}', '{Fund(fund, bar)}', '{Fund(fund, bar)}', '{Fund(fund, bar)}', '{Fund(fund, bar)}', 'test')"))) + ";");
        }

        var clock = FixedClock.At(new DateTimeOffset(night.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero), SessionZones.UnitedStates);

        await new MemberReader(clock, store.DatabaseFile).RunAsync("GSPC", "switches-first");

        // IJH over SPY: (72 / 121) / (60 / 110) over half a year and (72 / 121) / (50 / 100) over a year; IJR (70 / 121) /
        // (70 / 110) and (70 / 121) / (80 / 100); HYG at 85 over the mean of its last 50 closes, 80 plus a tenth of the mean of
        // 1 to 50, 82.55, and over its close 63 sessions before, 80.
        string Switches() => Query(
            store,
            "SELECT printf('%.10f|%.10f|%.10f|%.10f|%.10f|%.10f', ijh_half_year, ijh_year, ijr_half_year, ijr_year, hyg_average, hyg_change) " +
            $"FROM switch_reading WHERE session_date = '{Day(night)}';").Single();

        string Read(params double[] figures) => string.Join("|", figures.Select(figure => Fixed(figure, 10)));

        Assert.Equal(
            Read(72.0 / 121.0 / (60.0 / 110.0), 72.0 / 121.0 / (50.0 / 100.0), 70.0 / 121.0 / (70.0 / 110.0), 70.0 / 121.0 / (80.0 / 100.0), 85.0 / 82.55, 85.0 / 80.0),
            Switches());

        // A close HYG misses inside its 50 sessions, and IJR's a year of sessions before the night: the average and IJR's
        // year read none, and the rest read as before.
        Insert(store, $"DELETE FROM market_bar WHERE (series = 'HYG' AND session_date = '{Day(sessions[at - 10])}') OR (series = 'IJR' AND session_date = '{Day(sessions[at - 252])}');");

        await new MemberReader(clock, store.DatabaseFile).RunAsync("GSPC", "switches-again");

        Assert.Equal(
            [Read(72.0 / 121.0 / (60.0 / 110.0), 72.0 / 121.0 / (50.0 / 100.0), 70.0 / 121.0 / (70.0 / 110.0)) + "|||1|" + Fixed(85.0 / 80.0, 10)],
            Query(
                store,
                "SELECT printf('%.10f|%.10f|%.10f', ijh_half_year, ijh_year, ijr_half_year) || '|' || IFNULL(ijr_year, '') || '|' || IFNULL(hyg_average, '') || '|' || " +
                "(hyg_average IS NULL) || '|' || printf('%.10f', hyg_change) " +
                $"FROM switch_reading WHERE session_date = '{Day(night)}';"));
    }

    [Fact]
    public void TheSharedReadingsAreWorkedByHandAtTheirEdges()
    {
        // The year's high: the highest high of the 251 bars before a bar, a high 251 bars back read and one 252 back not,
        // the newer of two equal highs, and none for the first bar.
        SweepBar Bar(int day, decimal high) => new(new DateOnly(2025, 1, 1).AddDays(day), high, high, high, 1000, high, high);

        var bars = Enumerable.Range(0, 253).Select(day => Bar(day, day == 0 ? 90m : day == 1 ? 80m : 50m)).ToArray();

        Assert.Equal((80m, 251), MemberReadings.YearHigh(bars, 252));
        Assert.Equal((90m, 251), MemberReadings.YearHigh(bars, 251));
        Assert.Null(MemberReadings.YearHigh(bars, 0));

        var twice = new[] { Bar(0, 60m), Bar(1, 60m), Bar(2, 50m) };

        Assert.Equal((60m, 1), MemberReadings.YearHigh(twice, 2));

        // The volume over its average, none where the average is nothing or not held.
        Assert.Equal(1.5, MemberReadings.VolumeRatio(1500, 1000.0));
        Assert.Null(MemberReadings.VolumeRatio(1500, 0.0));
        Assert.Null(MemberReadings.VolumeRatio(1500, null));

        // The value-weighted mean, a member with no value above nothing and a figure not held left out, and a group left
        // with none reading none.
        Assert.Equal((3.0 * 0.1 - 4.5 * 0.1) / 7.5, MemberReadings.ValueWeighted([(3.0, 0.1), (4.5, -0.1), (0.0, 9.0), (2.0, double.NaN)])!.Value, 12);
        Assert.Null(MemberReadings.ValueWeighted([(0.0, 0.1), (-1.0, 0.2)]));
        Assert.Null(MemberReadings.ValueWeighted([]));

        // The switches: a fund's change over SPY's, none where one close is not held; a close over the mean of the 50 to it,
        // read at the 50th close and not the 49th, none where one of them is missing; and a close over the one 63 sessions
        // before, read at the 63rd session and not the 62nd.
        Assert.Equal(72.0 / 121.0 / (60.0 / 110.0), IndexSwitches.Relative(72, 121, 60, 110)!.Value, 12);
        Assert.Null(IndexSwitches.Relative(72, double.NaN, 60, 110));

        var closes = Enumerable.Range(0, 64).Select(day => 80.0 + day).ToArray();

        Assert.Equal(129.0 / (Enumerable.Range(0, 50).Average(day => 80.0 + day)), IndexSwitches.OverAverage(closes, 49, IndexSwitches.CreditAverageSessions)!.Value, 12);
        Assert.Null(IndexSwitches.OverAverage(closes, 48, IndexSwitches.CreditAverageSessions));
        Assert.Null(IndexSwitches.OverAverage([.. closes.Select((close, day) => day == 10 ? double.NaN : close)], 49, IndexSwitches.CreditAverageSessions));
        Assert.Equal(143.0 / 80.0, IndexSwitches.Change(closes, 63, IndexSwitches.CreditChangeSessions)!.Value, 12);
        Assert.Null(IndexSwitches.Change(closes, 62, IndexSwitches.CreditChangeSessions));
    }

    [Fact]
    public async Task TheFixturesMemberReadingsAreTheOnesWorkedByHandFromTheCapturedPayloads()
    {
        var expected = Expected("member-readings");
        var windows = expected.GetProperty("windows");

        Assert.Equal(
            (MemberReadings.DollarVolumeSessions, MemberReadings.YearSessions),
            (windows.GetProperty("dollarVolume").GetInt32(), windows.GetProperty("yearHigh").GetInt32()));

        using var store = await FixtureReplay.ReplayedAsync();

        var night = expected.GetProperty("night").GetString()!;
        var index = expected.GetProperty("indexCode").GetString()!;
        var cost = expected.GetProperty("cost");
        var state = expected.GetProperty("state").GetString()!;

        // The cost the expectation states is the published table's, read apart from the code's: no count, $40 and over.
        Assert.Equal(cost.GetProperty("percent").GetDecimal(), PublishedSpread[4][TradeCost.NoCountBand]);
        Assert.Equal(cost.GetProperty("doubled").GetDecimal(), PublishedSpread[4][TradeCost.NoCountBand] * TradeCost.Doubled);

        // One row for every member on the night under its index, the one holding no bar among them, and none on any other.
        Assert.Equal(
            [.. expected.GetProperty("readings").EnumerateObject().Select(name => name.Name)
                .Concat(expected.GetProperty("noBar").EnumerateArray().Select(name => name.GetString()!))
                .Order(StringComparer.Ordinal)
                .Select(name => index + "|" + name)],
            Query(store, $"SELECT index_code || '|' || ticker FROM member_reading WHERE session_date = '{night}' ORDER BY ticker;"));
        Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM member_reading WHERE session_date <> '{night}';"));

        var read = 0;

        foreach (var name in expected.GetProperty("readings").EnumerateObject())
        {
            var want = name.Value;
            var stored = Query(
                store,
                "SELECT close, dollar_volume, IFNULL(company_value, 'null'), cost, cost_double, profit, coverage, state, year_high, since_high, nearness, volume_ratio, " +
                "IFNULL(industry, 'null'), IFNULL(industry_month, 'null'), IFNULL(industry_quarter, 'null'), IFNULL(peer_surprise, 'null') " +
                $"FROM member_reading WHERE ticker = '{name.Name}' AND session_date = '{night}';").Single().Split('|');

            Assert.Equal(decimal.Parse(want.GetProperty("close").GetString()!, CultureInfo.InvariantCulture), decimal.Parse(stored[0], CultureInfo.InvariantCulture));
            Assert.Equal(
                decimal.Parse(want.GetProperty("dollarVolume").GetString()!, CultureInfo.InvariantCulture),
                Math.Round(decimal.Parse(stored[1], CultureInfo.InvariantCulture), 2, MidpointRounding.ToEven));
            Assert.Equal("null", stored[2]);
            Assert.InRange(Math.Abs(double.Parse(stored[3], CultureInfo.InvariantCulture) - cost.GetProperty("percent").GetDouble()), 0, 1e-9);
            Assert.InRange(Math.Abs(double.Parse(stored[4], CultureInfo.InvariantCulture) - cost.GetProperty("doubled").GetDouble()), 0, 1e-9);
            Assert.Equal(("0", "0", state), (stored[5], stored[6], stored[7]));
            Assert.Equal(decimal.Parse(want.GetProperty("yearHigh").GetString()!, CultureInfo.InvariantCulture), decimal.Parse(stored[8], CultureInfo.InvariantCulture));
            Assert.Equal(want.GetProperty("sinceHigh").GetInt32(), int.Parse(stored[9], CultureInfo.InvariantCulture));
            Near(want.GetProperty("nearness"), stored[10]);
            Near(want.GetProperty("volumeRatio"), stored[11]);
            Assert.Equal(["null", "null", "null", "null"], stored[12..]);

            // The session the high was made on is the one the stored bars put that many sessions before the night.
            Assert.Equal(
                want.GetProperty("yearHighSession").GetString(),
                Query(store, $"SELECT session_date FROM bar WHERE ticker = '{name.Name}' AND session_date <= '{night}' ORDER BY session_date DESC LIMIT 1 OFFSET {want.GetProperty("sinceHigh").GetInt32()};").Single());

            read++;
        }

        Assert.Equal(3, read);

        // The member holding no bar on the night is read over none, its quarters' gates and state still stored.
        foreach (var name in expected.GetProperty("noBar").EnumerateArray().Select(name => name.GetString()!))
        {
            Assert.Equal(
                $"null|null|null|null|null|0|0|{state}|null|null|null|null",
                Query(
                    store,
                    "SELECT IFNULL(close, 'null'), IFNULL(dollar_volume, 'null'), IFNULL(company_value, 'null'), IFNULL(cost, 'null'), IFNULL(cost_double, 'null'), profit, coverage, state, " +
                    "IFNULL(year_high, 'null'), IFNULL(nearness, 'null'), IFNULL(since_high, 'null'), IFNULL(volume_ratio, 'null') " +
                    $"FROM member_reading WHERE ticker = '{name}' AND session_date = '{night}';").Single());
        }

        // The switches' row, none of its six readings held, since the fixture captures no fund's series.
        Assert.Equal(
            [$"{night}|||||||"],
            Query(store, "SELECT session_date || '|' || IFNULL(ijh_half_year, '') || '|' || IFNULL(ijh_year, '') || '|' || IFNULL(ijr_half_year, '') || '|' || IFNULL(ijr_year, '') || '|' || IFNULL(hyg_average, '') || '|' || IFNULL(hyg_change, '') || '|' FROM switch_reading;"));

        // And the quarters step after the close stores each company's five rating counts and each quarter's interest
        // expense with whether its fetch read one, as the captured answers file them, read off the payloads here rather
        // than through the parser.
        var quarters = 0;

        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            using var payload = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), $"fundamentals-{ticker}.json")));
            var ratings = payload.RootElement.GetProperty("AnalystRatings");

            Assert.Equal(
                string.Join("|", new[] { "StrongBuy", "Buy", "Hold", "Sell", "StrongSell" }.Select(count => ratings.GetProperty(count).GetInt32().ToString(CultureInfo.InvariantCulture))),
                Query(store, $"SELECT strong_buy || '|' || buy || '|' || hold || '|' || sell || '|' || strong_sell FROM company WHERE ticker = '{ticker}' ORDER BY fetched_at DESC LIMIT 1;").Single());

            var filed = payload.RootElement.GetProperty("Financials").GetProperty("Income_Statement").GetProperty("quarterly");

            foreach (var row in Query(store, $"SELECT period_end || '|' || IFNULL(interest_expense, 'null') || '|' || interest_read FROM reported_quarter WHERE ticker = '{ticker}' ORDER BY period_end;"))
            {
                var cells = row.Split('|');
                var stated = filed.TryGetProperty(cells[0], out var figures) && figures.TryGetProperty("interestExpense", out var interest) && interest.ValueKind == JsonValueKind.String
                    ? decimal.Parse(interest.GetString()!, CultureInfo.InvariantCulture)
                    : (decimal?)null;

                Assert.Equal(stated, cells[1] == "null" ? null : decimal.Parse(cells[1], CultureInfo.InvariantCulture));
                Assert.Equal("1", cells[2]);

                quarters++;
            }
        }

        Assert.Equal(StoredQuarters, quarters);
    }

    // The quarters the fixture's quarters step stores for its four names, each read above against its capture.
    const int StoredQuarters = 48;
}
