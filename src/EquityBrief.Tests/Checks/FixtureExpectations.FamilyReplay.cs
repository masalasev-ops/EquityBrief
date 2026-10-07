using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 14.1: a family rule registered again is replayed first, at its settings under the code as it
// stands, over every night the store holds since its record began, through the night's own inputs read as of each
// night, the night's shadow and the list the recorder keeps, and each trade it keeps is compared with the one the
// record stored. Where every trade is the same the record carries on from where it began, and where one is not it
// restarts at the registration with that trade named.
// see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
public partial class FixtureExpectations
{
    // The rows the replay adds that this check reaches: section 18's row for a replay finding a trade differing.
    internal static readonly string[] FamilyReplayClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A replay at a family rule's registration finds a trade the rule would keep differently"),
    ];

    // Every row the replay adds, whichever check reaches it, which the phase's pair names apart.
    internal static readonly string[] FamilyReplayRows =
    [
        CheckReach.Key(Scope.CatalogueTable, "Family replay"),
        CheckReach.Key(Scope.MatrixTable, "Family replay"),
        .. FamilyReplayClaims,
        .. Reading.ReadSurface.FamilyReplayPageClaims,
    ];

    static readonly string LiveBreakoutRule = TheSetupFamilies.Breakouts[0].Candidate;

    // The breakouts frozen on Friday 2026-10-02 at 20:00 UTC and the night's own two stages run over that night, the
    // evaluator storing every rule's verdicts and the recorder each rule's own list; then Monday 2026-10-05's bars
    // arrive at each member's own close, so the replay of 2026-10-02 reads a store holding a later session.
    static async Task<TemporaryStore> ReplayStore()
    {
        var store = BreakoutStore();
        var nightAt = new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero);
        var clock = FixedClock.At(nightAt, SessionZones.UnitedStates);

        Assert.Equal(0, (await RegisterVerbAt(store, new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero), RegisterVerb.Family, BreakoutRule.Name)).Code);

        var rows = await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync();

        await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules", FamilyRuleShadow.For(rows, nightAt));
        await new FamilyRecorder(clock, store.DatabaseFile).RunAsync("GSPC", "records", CandidateFamily.Standing(rows, nightAt));

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "SELECT ticker, '2026-10-05', close, close, close, close, 1000, 'test', '2026-10-05T21:00:00Z', close FROM bar WHERE session_date = '" + BreakoutNight + "';");

        return store;
    }

    // The replay's rows as the run log holds them, a rule a row: the rule, its outcome and what it found.
    static IReadOnlyList<string> ReplayRows(TemporaryStore store) =>
        FamilyRows(store, $"SELECT json_extract(detail, '$.candidate') || '|' || outcome || '|' || json_extract(detail, '$.said') FROM run_log WHERE stage LIKE '{FamilyRecords.ReplayStages}' ORDER BY json_extract(detail, '$.candidate');");

    static IReadOnlyList<FamilyReplayRow> Replays(TemporaryStore store) =>
        [
            .. FamilyRows(store, $"SELECT outcome || '|' || started_at || '|' || detail FROM run_log WHERE stage LIKE '{FamilyRecords.ReplayStages}' ORDER BY rowid;")
                .Select(row => row.Split('|', 3))
                .Select(parts => FamilyRecords.ReplayOf(parts[0], parts[2], DateTimeOffset.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture))!),
        ];

    static async Task<IReadOnlyList<RegisterRow>> StandingBreakoutsAsync(TemporaryStore store, DateTimeOffset at) =>
        CandidateFamily.In(CandidateFamily.Standing(await new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), at), BreakoutRule.Name);

    [Fact]
    public async Task AFamilyRuleWhoseReplayReproducesItsTradesCarriesItsRecordFromItsFirstRegistration()
    {
        using var store = await ReplayStore();

        // Worked by hand off the breakout store: on 2026-10-02 every rule stating no switch fires on BA, BC and BB in
        // that order, the volume of two times on BA alone, and the rule switched on the index on none, its closes
        // not held; so the record holds 19 trades, the live rule's three among them, all still open.
        Assert.Equal(["19"], FamilyRows(store, "SELECT COUNT(*) FROM family_trade;"));
        Assert.Equal(
            ["BA|1|open", "BC|2|open", "BB|3|open"],
            FamilyRows(store, $"SELECT ticker || '|' || place || '|' || IFNULL(ended_on, 'open') FROM family_trade WHERE candidate = '{LiveBreakoutRule}' ORDER BY place;"));

        // Registered once, each rule counts from its registration's first session, the night it was first evaluated.
        var frozen = await StandingBreakoutsAsync(store, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(8, frozen.Count);
        Assert.All(frozen, rule => Assert.Equal((new DateOnly(2026, 10, 2), (string?)null), FamilyRecords.StartOf(rule, [])));

        // Registered again on Saturday 2026-10-03 under the code as it stands: each rule is replayed over the one
        // night its record holds, reading 2026-10-02's inputs though the store now holds 2026-10-05's bars, and each
        // reproduces its trades.
        var again = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var (code, said) = await RegisterVerbAt(store, again, RegisterVerb.FamilyAgain, BreakoutRule.Name, "--evidence", "a change of code");

        Assert.Equal(0, code);
        Assert.Contains($"replay: '{LiveBreakoutRule}' carries its record on: reproduced its 3 trade(s) over 1 night(s) from 2026-10-02", said, StringComparison.Ordinal);
        Assert.Equal(8, ReplayRows(store).Count);
        Assert.All(ReplayRows(store), row => Assert.Contains($"|{FamilyRecords.ReplayReproduced}|reproduced its ", row, StringComparison.Ordinal));

        // The replay writes under a run of its own and the registration's one row stands under the registration's.
        Assert.Equal(["1|8"], FamilyRows(store, $"SELECT COUNT(DISTINCT run_id) || '|' || COUNT(*) FROM run_log WHERE stage LIKE '{FamilyRecords.ReplayStages}' AND run_id LIKE '{FamilyReplay.RunPrefix}%';"));

        // Each rule stands by the registration of Saturday, whose first session is Monday 2026-10-05, and its record
        // counts from Friday 2026-10-02, its first registration's session, with nothing restarted.
        var standing = await StandingBreakoutsAsync(store, again.AddHours(1));
        var replays = Replays(store);

        Assert.Equal(8, standing.Count);
        Assert.All(standing, rule => Assert.Equal(new DateOnly(2026, 10, 5), FamilyRecords.FirstSession(rule.RegisteredAt)));
        Assert.All(standing, rule => Assert.Equal((new DateOnly(2026, 10, 2), (string?)null), FamilyRecords.StartOf(rule, replays)));
    }

    [Fact]
    public async Task AFamilyRuleWhoseReplayFindsATradeDifferingRestartsAtItsRegistrationNamingIt()
    {
        using var store = await ReplayStore();

        // The record says the live rule's BA trade ended on Monday at one risk, where Monday's close of 102 above
        // its stop of 99.75 keeps it open: the trade's stock, night, place, stop and target are the replay's, and
        // its end and its result alone differ.
        store.Execute($"UPDATE family_trade SET ended_on = '2026-10-05', result = 1.0 WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

        var again = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var (code, said) = await RegisterVerbAt(store, again, RegisterVerb.FamilyAgain, BreakoutRule.Name, "--evidence", "a change of code");

        const string Differed = "BA on 2026-10-02 ends still open in the replay and on 2026-10-05 at 1.000 in the record";

        Assert.Equal(0, code);
        Assert.Contains($"replay: '{LiveBreakoutRule}' restarts its record at this registration: {Differed}", said, StringComparison.Ordinal);
        Assert.Contains($"{LiveBreakoutRule}|{FamilyRecords.ReplayDiffers}|{Differed}", ReplayRows(store));
        Assert.Equal(7, ReplayRows(store).Count(row => row.Contains($"|{FamilyRecords.ReplayReproduced}|", StringComparison.Ordinal)));

        // The live rule counts from Monday 2026-10-05, the first session of the registration it stands by, saying
        // what the replay found; the seven the replay reproduced carry on from Friday 2026-10-02.
        var standing = await StandingBreakoutsAsync(store, again.AddHours(1));
        var replays = Replays(store);

        Assert.Equal((new DateOnly(2026, 10, 5), Differed), FamilyRecords.StartOf(Assert.Single(standing, rule => rule.Candidate == LiveBreakoutRule), replays));
        Assert.All(
            standing.Where(rule => rule.Candidate != LiveBreakoutRule),
            rule => Assert.Equal((new DateOnly(2026, 10, 2), (string?)null), FamilyRecords.StartOf(rule, replays)));
    }

    // The breakouts registered again over the replay store after one stored trade of the live rule was changed: the live
    // rule restarts naming the trade with the sentence given, where one is, and every other rule carries its record on.
    static async Task ReplayFinds(TemporaryStore store, string? differed)
    {
        var again = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var (code, said) = await RegisterVerbAt(store, again, RegisterVerb.FamilyAgain, BreakoutRule.Name, "--evidence", "a change of code");
        var rows = ReplayRows(store);

        Assert.Equal(0, code);
        Assert.Equal(8, rows.Count);

        if (differed is null)
        {
            Assert.All(rows, row => Assert.Contains($"|{FamilyRecords.ReplayReproduced}|", row, StringComparison.Ordinal));

            return;
        }

        Assert.Contains($"replay: '{LiveBreakoutRule}' restarts its record at this registration: {differed}", said, StringComparison.Ordinal);
        Assert.Contains($"{LiveBreakoutRule}|{FamilyRecords.ReplayDiffers}|{differed}", rows);
        Assert.Equal(7, rows.Count(row => row.Contains($"|{FamilyRecords.ReplayReproduced}|", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AReplayRestartsARuleWhoseStoredTradeDiffersInItsEndAloneWithNoResultEitherSide()
    {
        using var store = await ReplayStore();

        // The record says BA ended on Monday with no result, as a trade run out of its closes is ended, where Monday's
        // close above its stop keeps it open: the end alone differs, the result none on both sides.
        store.Execute($"UPDATE family_trade SET ended_on = '2026-10-05', result = NULL WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

        await ReplayFinds(store, "BA on 2026-10-02 ends still open in the replay and on 2026-10-05 with no result in the record");
    }

    [Fact]
    public async Task AReplayRestartsARuleWhoseStoredTradeDiffersInItsResultAloneOnATradeTheReplayEnds()
    {
        using var store = await ReplayStore();

        // A session more on which BA closes at 99.30, under its stop of 99.75, and the record walked over it by the night's
        // own recorder: every rule's BA trade ends there, the live rule's at 99.30 less 102 over a risk of 2.25, -1.2.
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "SELECT ticker, '2026-10-06', close, close, close, CASE ticker WHEN 'BA' THEN '99.3' ELSE close END, 1000, 'test', '2026-10-06T21:00:00Z', CASE ticker WHEN 'BA' THEN '99.3' ELSE close END FROM bar WHERE session_date = '2026-10-05';");

        var nightAt = new DateTimeOffset(2026, 10, 6, 23, 40, 0, TimeSpan.Zero);

        await new FamilyRecorder(FixedClock.At(nightAt, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", "records-after", await StandingBreakoutsAsync(store, nightAt));

        Assert.Equal(["2026-10-06|-1.2"], FamilyRows(store, $"SELECT ended_on || '|' || ROUND(result, 9) FROM family_trade WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';"));

        // Its result alone moved, its end where the replay ends it.
        store.Execute($"UPDATE family_trade SET result = -0.5 WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

        await ReplayFinds(store, "BA on 2026-10-02 ends on 2026-10-06 at -1.200 in the replay and on 2026-10-06 at -0.500 in the record");
    }

    [Fact]
    public async Task AReplayRestartsARuleWhoseStoredTradeDiffersInItsPlaceAlone()
    {
        using var store = await ReplayStore();

        store.Execute($"UPDATE family_trade SET place = 4 WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

        await ReplayFinds(store, "BA on 2026-10-02 is at place 1 in the replay and 4 in the record");
    }

    [Fact]
    public async Task AReplayRestartsARuleWhoseStoredStopMovedBeyondAMillionthOfItsBuyAndNotOneMovedWithin()
    {
        // A stop of 99.7499, a ten-thousandth under 99.75, moves its distance from the buy of 102 by under a millionth,
        // inside what a rescale needs, and reproduces; one of 99.70 moves it by about five ten-thousandths and restarts.
        using (var within = await ReplayStore())
        {
            within.Execute($"UPDATE family_trade SET stop = '99.7499' WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

            await ReplayFinds(within, null);
        }

        using var store = await ReplayStore();

        store.Execute($"UPDATE family_trade SET stop = '99.70' WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

        await ReplayFinds(store, "BA on 2026-10-02 has its stop at another distance from its buy in the replay");
    }

    [Fact]
    public async Task AReplayRestartsARuleWhoseStoredTargetDiffersFromTheOneTheReplayPlaces()
    {
        using var store = await ReplayStore();

        // The live rule trails its stop and places no target; the record states one of 110.
        store.Execute($"UPDATE family_trade SET target = '110' WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

        await ReplayFinds(store, "BA on 2026-10-02 has its target at another distance from its buy in the replay");
    }

    [Fact]
    public async Task AReplayRestartsARuleWhoseRecordLacksATradeTheReplayKeeps()
    {
        using var store = await ReplayStore();

        store.Execute($"DELETE FROM family_trade WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BB';");

        await ReplayFinds(store, "the replay keeps BB on 2026-10-02 where the record keeps none");
    }

    [Fact]
    public async Task AReplayRestartsARuleWhoseRecordKeepsATradeTheReplayDoesNot()
    {
        using var store = await ReplayStore();

        store.Execute(
            "INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap) " +
            $"SELECT candidate, 'NH', session_date, family, 4, entry, stop, target, risk_moves, reward_to_risk, cap FROM family_trade WHERE candidate = '{LiveBreakoutRule}' AND ticker = 'BA';");

        await ReplayFinds(store, "the record keeps NH on 2026-10-02 where the replay keeps none");
    }
}
