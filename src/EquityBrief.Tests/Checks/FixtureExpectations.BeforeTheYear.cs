using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Members;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11: on a night whose store's year holds fewer sessions than a reading needs, the S&P 400
// breakout's year's high, the checks a breakout rule reads on it, the member readings' year's high and the sector
// heavyweights' closes read the sessions the year falls short by from the pulled history at the store's scale, the forming
// list reading that high inside the rule cards' own transaction, and a member the pulled history holds no session for
// reads none and is named; each worked by hand over a night holding 251 sessions.
// see: A night's reading needing more closes than the store's year holds reads the sessions before it from the pulled history at the store's scale
// see: The member readings' year's high and a breakout rule's checks on it read the sessions before a short year as the breakout's own high does
public partial class FixtureExpectations
{
    static readonly DateOnly BeforeNight = new(2026, 10, 2);

    // The exchange's sessions to the night, the given count of the newest.
    static DateOnly[] BeforeSessions(int count) =>
        [.. ExchangeClosures.SessionsBetween(BeforeNight.AddDays(-400), BeforeNight.AddDays(1)).TakeLast(count)];

    // A member's bar at its place among the 252 sessions to the night, the shape the index families' store gives a year:
    // highs of 100 and closes of 99 for the oldest 211, highs of 101 for the twenty after, the twenty after those at
    // ranges half as wide, and the night closing at 102 on twice the million shares a day.
    static FamilyBar BeforeBar(DateOnly session, int at) => at switch
    {
        < 211 => new FamilyBar(session, 100m, 98m, 99m, 1_000_000),
        < 231 => new FamilyBar(session, 101m, 99m, 100m, 1_000_000),
        < 251 => new FamilyBar(session, 100.5m, 99.5m, 100m, 1_000_000),
        _ => new FamilyBar(session, 102.5m, 101m, 102m, 2_000_000),
    };

    static string PulledBar(string ticker, DateOnly session, decimal high, decimal low, decimal close) =>
        FormattableString.Invariant($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('{ticker}', '{Day(session)}', '{close}', '{high}', '{low}', '{close}', '{close}', 1000000, 'pull-before-the-year');");

    static readonly DateTimeOffset BeforeFrozenAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // Three S&P 400 members over the 251 sessions to the night the store's year holds, the session before it held by the
    // pull alone, each with four profitable quarters, and the S&P 500's breadth of a half on the night. The pull holds IA's
    // and IB's bars at twice the store's prices, the first stored session at a close of 198 against the store's 99, and
    // the session before it at highs of 203 and 206; ID it holds none of. The S&P 400's breakout stands frozen, at its own
    // sweep's setting or the one given with the variants given beside it.
    static async Task<(TemporaryStore Store, IReadOnlyList<RegisterRow> Register)> BeforeTheYearStore(IReadOnlyDictionary<string, double>? live = null, string? variants = null)
    {
        var store = new TemporaryStore().Migrated();
        var sessions = BeforeSessions(252);

        foreach (var ticker in new[] { "IA", "IB", "ID" })
        {
            StoreYear(store, ticker, [.. sessions.Select((session, at) => BeforeBar(session, at)).Skip(1)]);
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
        }

        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) "
            + $"VALUES ('{Day(BeforeNight)}', 2, 2, 1, 0.5, 2, 1, 0.5, 2, 1.0);");

        foreach (var (ticker, high) in new[] { ("IA", 203m), ("IB", 206m) })
        {
            store.Execute(PulledBar(ticker, sessions[0], high, 196m, 198m));
            store.Execute(PulledBar(ticker, sessions[1], 200m, 196m, 198m));
        }

        Assert.Equal(251, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));

        string[] freeze = [RegisterVerb.IndexFamily, BreakoutRule.Name, "--index", "MID", "--parameters", Typed(live ?? FrozenBreakout()), .. variants is { } moved ? new[] { "--variants", moved } : []];

        Assert.Equal(0, (await RegisterVerbAt(store, BeforeFrozenAt, freeze)).Code);

        return (store, await new CandidateRegistrar(FixedClock.At(BeforeFrozenAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync());
    }

    static IClock BeforeClock => FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

    [Fact]
    public async Task TheSAndP400BreakoutsYearsHighOverANightHolding251SessionsReadsTheSessionBeforeFromThePulledHistoryAtTheStoresScale()
    {
        var (store, register) = await BeforeTheYearStore();

        using var held = store;

        await new IndexFamilies(BeforeClock, store.DatabaseFile).RunAsync("night-before-the-year", default, register, BeforeFrozenAt.AddHours(1));

        // The frozen breakout's high over the 251 sessions before the night: the store holds 250 of them, its highest 101,
        // and the pull the one before at the store's scale, the store's 99 over the pull's 198 a half, IA's 203 reading
        // 101.5 and IB's 206 reading 103. IA's close of 102 stands above its year's high on twice its volume and breaks
        // out; IB's stands under it; ID reads no year's high and lists nothing.
        Assert.Equal(
            ["IA|1", "IB|0", "ID|0"],
            FamilyRows(store, $"SELECT ticker, passed FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND session_date = '{Day(BeforeNight)}' ORDER BY ticker;"));
        Assert.Contains(
            "the store's year holds 251 of the 252 sessions the year's high and the heavyweights' closes read, the sessions before it read from the pulled history for 2 member(s), which holds too few for 1, each reading none of them: ID",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'index-families';"),
            StringComparison.Ordinal);
    }

    // The rule cards over the same night, which read the pull inside the step's own transaction: the forming list reads
    // the frozen breakout's high over the session before the year at the store's scale, IB's 206 reading 103. IB's close of
    // 102 stands a point under it, inside the one and a half typical moves the list reads and on the volume the rule needs,
    // and forms; IA closes above its high and ID reads none, and neither forms.
    [Fact]
    public async Task TheSAndP400sFormingListOverANightHolding251SessionsReadsThePulledHighInsideTheRuleCardsOwnTransaction()
    {
        var (store, register) = await BeforeTheYearStore();

        using var held = store;
        var live = register.Single(row => FamilyRecords.IsLive(row.Candidate)).Candidate;

        await new IndexFamilies(BeforeClock, store.DatabaseFile).RunAsync("night-before-the-year", default, register, BeforeFrozenAt.AddHours(1));

        var cards = await new RuleCards(BeforeClock, store.DatabaseFile).RunAsync("night-before-the-year-cards", register, BeforeFrozenAt.AddHours(1));

        Assert.Null(cards.Indices.Single(index => index.Index == "MID").Fault);
        Assert.Equal(RuleCards.Ok, Text(store, "SELECT outcome FROM run_log WHERE run_id = 'night-before-the-year-cards' AND stage = 'rule-cards';"));
        Assert.Equal([$"{live}|IB"], TextRows(store, "SELECT rule || '|' || ticker FROM forming_row WHERE index_code = 'MID' ORDER BY place;"));
        Assert.Equal(103m, decimal.Parse(Text(store, "SELECT high FROM forming_row WHERE index_code = 'MID' AND ticker = 'IB';"), CultureInfo.InvariantCulture));
    }

    // The member readings over the same night: the year's high over the 251 sessions before the night, the store holding
    // 250 of them, its highest the 101 made 21 sessions back, and the pull the one before at the store's scale, IA's 203
    // reading 101.5 and IB's 206 reading 103, each made 251 sessions back, so IA's close of 102 stands at 102 / 101.5 of its
    // high and IB's at 102 / 103 of its. ID, the pull holding none of it, reads no year's high, and the step's row names it.
    // GA of the S&P 500 and SA of the S&P 600, shaped and pulled as IA is, read as IA does, as every member of the three
    // indices is read.
    [Fact]
    public async Task TheMemberReadingsYearsHighOverANightHolding251SessionsReadsTheSessionBeforeFromThePulledHistoryAtTheStoresScale()
    {
        var (store, _) = await BeforeTheYearStore();

        using var held = store;
        var sessions = BeforeSessions(252);

        foreach (var (index, ticker) in new[] { ("GSPC", "GA"), ("SML", "SA") })
        {
            StoreYear(store, ticker, [.. sessions.Select((session, at) => BeforeBar(session, at)).Skip(1)]);
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{index}', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            store.Execute(PulledBar(ticker, sessions[0], 203m, 196m, 198m));
            store.Execute(PulledBar(ticker, sessions[1], 200m, 196m, 198m));
        }

        await new MemberReader(BeforeClock, store.DatabaseFile).RunAsync("GSPC", "night-before-the-year", wider: ["MID", "SML"]);

        var rows = TextRows(store, "SELECT index_code || '|' || ticker || '|' || IFNULL(year_high, 'none') || '|' || IFNULL(since_high, 'none') || '|' || CASE WHEN nearness IS NULL THEN 'none' ELSE printf('%.12f', nearness) END "
                + $"FROM member_reading WHERE session_date = '{Day(BeforeNight)}' ORDER BY ticker;")
            .Select(row => row.Split('|'))
            .ToDictionary(row => row[1], row => row, StringComparer.Ordinal);

        Assert.Equal(["GA", "IA", "IB", "ID", "SA"], rows.Keys);
        Assert.Equal((101.5m, "251"), (decimal.Parse(rows["IA"][2], CultureInfo.InvariantCulture), rows["IA"][3]));
        Assert.Equal((103m, "251"), (decimal.Parse(rows["IB"][2], CultureInfo.InvariantCulture), rows["IB"][3]));
        Assert.Equal(102.0 / 101.5, double.Parse(rows["IA"][4], CultureInfo.InvariantCulture), 9);
        Assert.Equal(102.0 / 103.0, double.Parse(rows["IB"][4], CultureInfo.InvariantCulture), 9);
        Assert.Equal(["MID", "ID", "none", "none", "none"], rows["ID"]);
        Assert.All(new[] { (Ticker: "GA", Index: "GSPC"), (Ticker: "SA", Index: "SML") }, member => Assert.Equal(
            (member.Index, 101.5m, "251"),
            (rows[member.Ticker][0], decimal.Parse(rows[member.Ticker][2], CultureInfo.InvariantCulture), rows[member.Ticker][3])));
        Assert.EndsWith(
            "; the store's year holds 251 of the 252 sessions the year's high reads, the sessions before it read from the pulled history for 4 member(s), which holds too few for 1, each reading no year's high: ID",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'member-readings';"),
            StringComparison.Ordinal);
    }

    // The S&P 400's breakout over the same night registered with its checks on the year's high: its live rule the frozen
    // breakout reading its year's high made within 63 sessions, a variant reading it older than 63 sessions, and a variant
    // at a 126-session high reading the close at its year's high or above. The checks read the year's high the member
    // readings read, IA's 101.5 and IB's 103 made 251 sessions back where the store's own 101 was made 21 back, and none for
    // ID. IA breaks out above its 251 sessions' high on twice its volume, so the live rule keeps it off for a high older
    // than 63 sessions and the variant reading it older keeps it; at the 126 sessions' high of 101 all three break out, and
    // that variant keeps IA alone, its close of 102 above its year's high where IB's stands under its 103 and ID reads none,
    // while the rule the page read before the freeze, kept in the background at that high and reading no check, keeps all
    // three. A variant at the 126 sessions' high reading its year's high older than 63 sessions keeps IA and IB, each high
    // made 251 sessions back, and not ID, whose year's high the night read none of: a check on a high not read fails, and
    // its stored part says so.
    [Fact]
    public async Task TheSAndP400BreakoutsChecksOnItsYearsHighOverANightHolding251SessionsReadTheSessionBeforeFromThePulledHistory()
    {
        var live = new Dictionary<string, double>(FrozenBreakout(), StringComparer.Ordinal) { [IndexBreakoutCandidate.RecencyParameter] = 63 };
        var (store, register) = await BeforeTheYearStore(live, "recency=-63;highSessions=126,recency=0,nearness=1;highSessions=126,recency=-63");

        using var held = store;

        await new IndexFamilies(BeforeClock, store.DatabaseFile).RunAsync("night-before-the-year", default, register, BeforeFrozenAt.AddHours(1));

        Assert.Equal(
            [$"IA|0|{IndexRules.HighNotAsStated}", $"IB|0|{IndexNightRead.NoSetup}", $"ID|0|{IndexNightRead.NoSetup}"],
            FamilyRows(store, $"SELECT ticker, passed, reason FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND session_date = '{Day(BeforeNight)}' ORDER BY ticker;"));

        (bool Passed, string Reason) Recency(string ticker) => FamilyRule
            .GatesOf(Text(store, $"SELECT gates FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND ticker = '{ticker}' AND session_date = '{Day(BeforeNight)}';"))
            .Where(gate => gate.Name == IndexRules.HighNotAsStated)
            .Select(gate => (gate.Passed, gate.Reason))
            .Single();

        Assert.Equal((false, IndexRules.HighNotAsStated), Recency("IA"));
        Assert.Equal((false, IndexRules.NoYearHigh), Recency("ID"));

        IReadOnlyList<string> Kept(params string[] words) =>
            TextRows(store, $"SELECT ticker FROM index_rule_trade WHERE {string.Join(" AND ", words.Select(word => $"candidate LIKE '%{word}%'"))} ORDER BY ticker;");

        Assert.Empty(Kept("made within 63 sessions"));
        Assert.Equal(["IA"], Kept("251-session high", "older than 63 sessions"));
        Assert.Equal(["IA"], Kept("the close at least 1 of its year"));
        Assert.Equal(["IA", "IB"], Kept("126-session high", "older than 63 sessions"));
        Assert.Equal(["IA", "IB", "ID"], TextRows(store, "SELECT ticker FROM index_rule_trade WHERE candidate LIKE 'the background %' ORDER BY ticker;"));
    }

    // The pull read stops at a session the pull is missing, as no reading reaches across one the store is missing: IH, its
    // year the store's 251 sessions shaped as IA's, is pulled at twice the store's prices on the year's first session and on
    // the session two before the year, at a high of 200 there that reads 100 at the store's scale, and not on the session
    // directly before the year. The index families read it no session before the year, so its breakout reads no year's
    // high and it lists nothing, where the session two back read in the missing one's place would give it the store's 101
    // made 21 sessions back and its close of 102 would break out; and the member readings read it none. Each step's row
    // names it beside ID.
    [Fact]
    public async Task ThePullReadBeforeAYearOf251SessionsStopsAtASessionThePullIsMissing()
    {
        var (store, register) = await BeforeTheYearStore();

        using var held = store;
        var sessions = BeforeSessions(253);

        StoreYear(store, "IH", [.. sessions[2..].Select((session, at) => BeforeBar(session, at + 1))]);
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'IH', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        Quarters(store, "IH", 10m, 10m, 10m, 10m);
        store.Execute(PulledBar("IH", sessions[0], 200m, 196m, 198m));
        store.Execute(PulledBar("IH", sessions[2], 200m, 196m, 198m));

        await using (var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile)))
        {
            await connection.OpenAsync();

            var (names, _) = await IndexFamilies.InputsAsync(connection, "MID", BeforeNight, default);

            Assert.Equal(["IA|1", "IB|1", "ID|0", "IH|0"], names.OrderBy(name => name.Ticker, StringComparer.Ordinal).Select(name => $"{name.Ticker}|{name.Before?.Length}"));
        }

        await new IndexFamilies(BeforeClock, store.DatabaseFile).RunAsync("night-before-the-year", default, register, BeforeFrozenAt.AddHours(1));
        await new MemberReader(BeforeClock, store.DatabaseFile).RunAsync("GSPC", "night-before-the-year", wider: ["MID"]);

        Assert.Equal(
            ["IA|1", "IB|0", "ID|0", "IH|0"],
            FamilyRows(store, $"SELECT ticker, passed FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND session_date = '{Day(BeforeNight)}' ORDER BY ticker;"));
        Assert.Equal("none", Text(store, $"SELECT IFNULL(year_high, 'none') FROM member_reading WHERE ticker = 'IH' AND session_date = '{Day(BeforeNight)}';"));
        Assert.Contains(
            "which holds too few for 2, each reading none of them: ID, IH",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'index-families';"),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "which holds too few for 2, each reading no year's high: ID, IH",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'member-readings';"),
            StringComparison.Ordinal);
    }

    // The pull read for what the year falls short by and not for what a member's own bars fall short by, over the same
    // night with two members more: IG's year misses its hundredth session and IL's starts on the year's eleventh, the pull
    // holding each the three sessions before the year and the year's first eleven at twice the store's prices. IG reads
    // the one session the year falls short by, the session before it at the store's 99, and not the two its own count
    // falls short by, so no reading of it reaches across its hole; IL reads none, the pull's sessions inside the year
    // being the store's to hold.
    [Fact]
    public async Task ThePullReadBeforeAYearOf251SessionsTakesWhatTheYearFallsShortByAndNoneForAMemberStartingAfterIt()
    {
        var (store, _) = await BeforeTheYearStore();

        using var held = store;
        var sessions = BeforeSessions(254);
        var year = sessions[3..];

        StoreYear(store, "IG", [.. year.Select((session, at) => BeforeBar(session, at + 1)).Where((_, at) => at != 99)]);
        StoreYear(store, "IL", [.. year.Select((session, at) => BeforeBar(session, at + 1)).Skip(10)]);

        foreach (var ticker in new[] { "IG", "IL" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");

            foreach (var session in sessions[..14])
            {
                store.Execute(PulledBar(ticker, session, 200m, 196m, 198m));
            }
        }

        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        var (names, _) = await IndexFamilies.InputsAsync(connection, "MID", BeforeNight, default);
        var read = names.ToDictionary(name => name.Ticker, StringComparer.Ordinal);

        Assert.Equal(
            ["IA|1", "IB|1", "ID|0", "IG|1", "IL|none"],
            read.Values.OrderBy(name => name.Ticker, StringComparer.Ordinal).Select(name => $"{name.Ticker}|{(name.Before is { } before ? before.Length.ToString(CultureInfo.InvariantCulture) : "none")}"));
        Assert.Equal((sessions[2], 99m), (read["IG"].Before![0].Session, read["IG"].Before![0].Close));
    }

    // The S&P 500's heavyweights over the store the wait reads, its year to a month's first session on a Monday holding
    // 251 sessions, one short of the 252 the twelve-month return reads: the pull holds W1's and W2's closes at twice the
    // store's on the year's first session and on the session before it, and none of W3's.
    [Fact]
    public async Task TheSAndP500sHeavyweightsReadTheCloseBeforeAYearOf251SessionsFromThePulledHistoryAndRebalanceInPlaceOfWaiting()
    {
        using var store = WaitStore(new DateOnly(2025, 9, 2), new DateOnly(2026, 9, 30));

        var (_, first) = await WaitNight(store, new DateOnly(2026, 10, 1), "night-first");

        Assert.Equal(["W1"], first.Entered);

        WaitBars(store, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 30));

        var monday = new DateOnly(2026, 11, 2);
        var before = new DateOnly(2025, 10, 31);
        var firstStored = new DateOnly(2025, 11, 3);

        foreach (var ticker in new[] { "W1", "W2" })
        {
            store.Execute(PulledBar(ticker, before, 2 * WaitClose(ticker, before), 2 * WaitClose(ticker, before), 2 * WaitClose(ticker, before)));
            store.Execute(PulledBar(ticker, firstStored, 2 * WaitClose(ticker, firstStored), 2 * WaitClose(ticker, firstStored), 2 * WaitClose(ticker, firstStored)));
        }

        var (cut, read) = await WaitNight(store, monday, "night-before-the-year");

        // The store keeps 251 sessions; the book reads W1's and W2's close of the session before them from the pull at the
        // store's scale and rebalances on the month's first session in place of waiting, W1 its leader and held.
        Assert.Equal(Day(firstStored), Text(store, "SELECT MIN(session_date) FROM bar;"));
        Assert.Equal(251, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));
        Assert.Equal((true, 0, 0, 1), (read.Rebalanced, read.Entered.Count, read.Ended.Count, read.Held));

        // W1's return over the 251 sessions worked by hand, its close on the night over its close on the session before the
        // year: the pull's twice its close brought back by the store's close over the pull's on the year's first session, a
        // half. W2 flat at 100 returns nothing, and W3, which the pull holds no close for, reads none.
        var w1 = Statistic.FromRatio(WaitClose("W1", monday) / WaitClose("W1", before)) - 1.0;

        Assert.Equal(w1, double.Parse(Text(store, $"SELECT printf('%.12f', look_back) FROM heavyweight_night WHERE session_date = '{Day(monday)}' AND ticker = 'W1';"), CultureInfo.InvariantCulture), 9);
        Assert.Equal(0.0, double.Parse(Text(store, $"SELECT printf('%.12f', look_back) FROM heavyweight_night WHERE session_date = '{Day(monday)}' AND ticker = 'W2';"), CultureInfo.InvariantCulture), 9);
        Assert.Equal(["W1|1|read", "W2|0|read", "W3|0|none"], TextRows(store, $"SELECT ticker || '|' || leader || '|' || CASE WHEN look_back IS NULL THEN 'none' ELSE 'read' END FROM heavyweight_night WHERE session_date = '{Day(monday)}' ORDER BY ticker;"));
        Assert.EndsWith(
            "; the store's year holds 251 of the 252 sessions the heavyweights' closes read, the sessions before it read from the pulled history for 2 member(s), which holds too few for 1, each reading no return and no beta: W3",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'heavyweights';"),
            StringComparison.Ordinal);
    }

    // The S&P 500's heavyweights over the same store with W2's year missing a session inside it, the pull holding W2's
    // closes on the two sessions before the year: W2 reads the one close the year falls short by and not the two its own
    // bars fall short by, so its return reaches across no hole and it reads none, as on a night whose year holds 252.
    [Fact]
    public async Task TheSAndP500sHeavyweightsReadNoMoreBeforeTheYearThanItFallsShortByForAMemberMissingASessionInsideIt()
    {
        using var store = WaitStore(new DateOnly(2025, 9, 2), new DateOnly(2026, 9, 30));

        await WaitNight(store, new DateOnly(2026, 10, 1), "night-first");
        WaitBars(store, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 30));
        store.Execute("DELETE FROM bar WHERE ticker = 'W2' AND session_date = '2026-03-02';");

        var monday = new DateOnly(2026, 11, 2);
        (string Ticker, DateOnly Session)[] pulled =
        [
            ("W1", new DateOnly(2025, 10, 31)),
            ("W1", new DateOnly(2025, 11, 3)),
            ("W2", new DateOnly(2025, 10, 30)),
            ("W2", new DateOnly(2025, 10, 31)),
            ("W2", new DateOnly(2025, 11, 3)),
        ];

        foreach (var (ticker, session) in pulled)
        {
            store.Execute(PulledBar(ticker, session, 2 * WaitClose(ticker, session), 2 * WaitClose(ticker, session), 2 * WaitClose(ticker, session)));
        }

        await WaitNight(store, monday, "night-before-the-year");

        Assert.Equal(250, Scalar(store, "SELECT COUNT(*) FROM bar WHERE ticker = 'W2';"));
        Assert.Equal(["W1|read", "W2|none", "W3|none"], TextRows(store, $"SELECT ticker || '|' || CASE WHEN look_back IS NULL THEN 'none' ELSE 'read' END FROM heavyweight_night WHERE session_date = '{Day(monday)}' ORDER BY ticker;"));
        Assert.EndsWith(
            "; the store's year holds 251 of the 252 sessions the heavyweights' closes read, the sessions before it read from the pulled history for 2 member(s), which holds too few for 1, each reading no return and no beta: W3",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'heavyweights';"),
            StringComparison.Ordinal);
    }

    // The S&P 500's heavyweights over the same store with W2's year whole and its pull missing the session directly before
    // the year, holding the one before that: the read stops at the session the pull is missing, so W2 reads no close before
    // the year and no return, as a member the pull holds none for, where the close two sessions back read in the missing
    // one's place would give it a return over a session more than the year; and the row names it beside W3.
    [Fact]
    public async Task TheSAndP500sHeavyweightsReadNoCloseBeforeTheYearPastASessionThePullIsMissing()
    {
        using var store = WaitStore(new DateOnly(2025, 9, 2), new DateOnly(2026, 9, 30));

        await WaitNight(store, new DateOnly(2026, 10, 1), "night-first");
        WaitBars(store, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 30));

        var monday = new DateOnly(2026, 11, 2);
        (string Ticker, DateOnly Session)[] pulled =
        [
            ("W1", new DateOnly(2025, 10, 31)),
            ("W1", new DateOnly(2025, 11, 3)),
            ("W2", new DateOnly(2025, 10, 30)),
            ("W2", new DateOnly(2025, 11, 3)),
        ];

        foreach (var (ticker, session) in pulled)
        {
            store.Execute(PulledBar(ticker, session, 2 * WaitClose(ticker, session), 2 * WaitClose(ticker, session), 2 * WaitClose(ticker, session)));
        }

        await WaitNight(store, monday, "night-before-the-year");

        Assert.Equal(251, Scalar(store, "SELECT COUNT(*) FROM bar WHERE ticker = 'W2';"));
        Assert.Equal(["W1|read", "W2|none", "W3|none"], TextRows(store, $"SELECT ticker || '|' || CASE WHEN look_back IS NULL THEN 'none' ELSE 'read' END FROM heavyweight_night WHERE session_date = '{Day(monday)}' ORDER BY ticker;"));
        Assert.EndsWith(
            "; the store's year holds 251 of the 252 sessions the heavyweights' closes read, the sessions before it read from the pulled history for 1 member(s), which holds too few for 2, each reading no return and no beta: W2, W3",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'heavyweights';"),
            StringComparison.Ordinal);
    }
}
