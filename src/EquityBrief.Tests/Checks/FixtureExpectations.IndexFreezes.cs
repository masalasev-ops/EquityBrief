using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 15.5: a family on the S&P 400 or the S&P 600 frozen as rules of its own, its live rule and its
// variants registered at one instant or none, each read on the night at its own settings with its levels read off the
// readings the night stored, each keeping its own list and trades, and the family's page drawn by its live rule.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
public partial class FixtureExpectations
{
    // The row the freezes add that this check reaches: section 18's on a registered rule the night does not read.
    internal static readonly string[] IndexFreezeClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A rule registered on the S&P 400 or 600 whose evaluator moved, or whose settings its sweep's grid no longer holds"),
    ];

    static readonly DateTimeOffset FreezeAt = new(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);

    static IndexRuleCandidate IndexEvaluator(string name) => (IndexRuleCandidate)CandidateEvaluators.Find(name)!;

    static string Typed(IReadOnlyDictionary<string, double> parameters) =>
        string.Join(",", parameters.Select(pair => FormattableString.Invariant($"{pair.Key}={pair.Value}")));

    [Fact]
    public async Task AnIndexFreezeRegistersItsLiveRuleAndItsVariantsAtOneInstantOrNone()
    {
        using var store = new TemporaryStore().Migrated();
        var breakout = IndexEvaluator("breakout-400");
        var live = Typed(IndexRules.Provisional(breakout));

        // Refused, each writing nothing: a dial off its sweep's grid, a level off the values its index's second stage read,
        // nine variants, a variant stating the live rule's settings, a family no rule is carried for and an index no rule
        // reads, each a second apart since a command's run id is its instant.
        foreach (var ((args, expected), at) in new (string[] Args, string Said)[]
        {
            ([RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", live.Replace("highSessions=126", "highSessions=200", StringComparison.Ordinal)], "'high' is 200"),
            ([RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", live.Replace("floors=1", "floors=3", StringComparison.Ordinal)], "'floors' is 3"),
            ([RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", live, "--variants", string.Join(";", Enumerable.Repeat("quality=2", 9))], "9 variants were given"),
            ([RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", live, "--variants", "quality=1"], "variant 1 states the settings of a rule given before it"),
            ([RegisterVerb.IndexFamily, "heavyweights", "--index", "MID", "--parameters", live], "no rule of a family named 'heavyweights'"),
            ([RegisterVerb.IndexFamily, "breakout", "--index", "GSPC", "--parameters", live], "for an index coded 'GSPC'"),
        }.Select((refusal, at) => (refusal, at)))
        {
            var (code, refused) = await RegisterVerbAt(store, FreezeAt.AddSeconds(-1 - at), args);

            Assert.Equal(1, code);
            Assert.Contains(expected, refused, StringComparison.Ordinal);
        }

        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // Registered: the live rule at its provisional settings and two variants, the cover and four times the volume, at one
        // instant, named for the index, on the S&P 400's breakout at its version, counted in a family of their own.
        var (written, said) = await RegisterVerbAt(store, FreezeAt, RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", live, "--variants", "quality=2;highVolume=3");

        Assert.Equal(0, written);
        Assert.Contains("registered 3 at one instant, family of 3 of 9", said, StringComparison.Ordinal);

        const string Settings = "breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath, the profit gate";

        Assert.Equal(
            [
                $"the live S&P 400 {Settings}|breakout-400|{breakout.Version}",
                $"the S&P 400 {Settings.Replace("the profit gate", "the profit gate and the interest cover", StringComparison.Ordinal)}|breakout-400|{breakout.Version}",
                $"the S&P 400 {Settings}, volume at least 3 times its average|breakout-400|{breakout.Version}",
            ],
            TextRows(store, "SELECT candidate || '|' || evaluator || '|' || evaluator_version FROM candidate_register ORDER BY id;"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(DISTINCT registered_at) FROM candidate_register;"));
        Assert.Equal("breakout on the S&P 400", CandidateFamily.SetupFamilyOf("breakout-400"));

        // A second freeze of the family on the index is refused, and the S&P 600's breakout is a family of its own.
        var (again, refusal) = await RegisterVerbAt(store, FreezeAt.AddHours(1), RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", live);

        Assert.Equal(1, again);
        Assert.Contains("rules of the breakout on the S&P 400 already stand registered", refusal, StringComparison.Ordinal);
        Assert.Equal(0, (await RegisterVerbAt(store, FreezeAt.AddHours(2), RegisterVerb.IndexFamily, "breakout", "--index", "SML", "--parameters", Typed(IndexRules.Provisional(IndexEvaluator("breakout-600"))))).Code);

        // And each family's provisional rule on each index is one its own refusal reads as a setting its sweep read.
        Assert.All(CandidateEvaluators.All.OfType<IndexRuleCandidate>(), evaluator => Assert.Null(IndexRules.Refusal(evaluator, IndexRules.Provisional(evaluator))));
    }

    // The S&P 400 of the index families' store with three more, each breaking out with four profitable quarters: IE on four
    // times its volume, MV on 2.2 times a volume of 150,000 shares a day, about $15 million of dollar volume a day, and PN at a
    // twelfth of the others' prices, about $8.50, on 2.1 times two million shares a day; and the night's member readings,
    // each member's coverage and its industry's return over a month, and the switches with IJH against SPY over half a year
    // at the figure given.
    static TemporaryStore FrozenIndexStore(double fundHalfYear)
    {
        var store = IndexStore();
        IReadOnlyList<FamilyBar> cheap = [.. IndexYear(102m, 4_200_000, 2_000_000).Select(bar => bar with { High = bar.High / 12, Low = bar.Low / 12, Close = bar.Close / 12 })];

        foreach (var (ticker, bars) in new (string, IReadOnlyList<FamilyBar>)[] { ("IE", IndexYear(102m, 4_000_000)), ("MV", IndexYear(102m, 330_000, 150_000)), ("PN", cheap) })
        {
            StoreYear(store, ticker, bars);
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
        }

        foreach (var (ticker, coverage, month) in new[] { ("IA", "1", "-0.01"), ("IB", "NULL", "NULL"), ("IC", "1", "0.01"), ("ID", "0", "0"), ("IE", "1", "0.02"), ("MV", "1", "0"), ("PN", "1", "0") })
        {
            store.Execute(
                "INSERT INTO member_reading (index_code, session_date, ticker, profit, coverage, industry, industry_month) " +
                $"VALUES ('MID', '{IndexNight}', '{ticker}', 1, {coverage}, 'Semiconductors', {month});");
        }

        store.Execute(FormattableString.Invariant($"INSERT INTO switch_reading (session_date, ijh_half_year) VALUES ('{IndexNight}', {fundHalfYear});"));

        return store;
    }

    // The S&P 400 breakout's live rule and one variant a level and a dial, each the live rule with that one setting moved.
    static (string Name, IReadOnlyDictionary<string, double> Parameters)[] BreakoutVariants(IReadOnlyDictionary<string, double> live)
    {
        IReadOnlyDictionary<string, double> Moved(string name, double value) => new Dictionary<string, double>(live, StringComparer.Ordinal) { [name] = value };

        return
        [
            ("live", live),
            ("cover", Moved(IndexRuleCandidate.QualityParameter, 2)),
            ("volume", Moved(IndexBreakoutCandidate.HighVolumeParameter, 3)),
            ("floors", Moved(IndexRuleCandidate.FloorsParameter, 2)),
            ("industry", Moved(IndexRuleCandidate.IndustryFallParameter, 21)),
            ("fund", Moved(IndexRuleCandidate.FundOverSpyParameter, 126)),
            ("price", Moved(IndexRuleCandidate.LowestCloseParameter, 10)),
            ("hold", Moved(IndexRuleCandidate.HoldParameter, 21)),
            ("stop", Moved(IndexBreakoutCandidate.StopMovesParameter, 3)),
        ];
    }

    [Fact]
    public async Task EachIndexRuleFiresAtItsOwnSettingsOnBothSidesOfTheSettingItMoves()
    {
        var breakout = IndexEvaluator("breakout-400");
        var rules = BreakoutVariants(IndexRules.Provisional(breakout));
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.All(rules, rule => Assert.Null(IndexRules.Refusal(breakout, rule.Parameters)));

        async Task<IReadOnlyDictionary<string, IReadOnlyList<IndexAnswer>>> AnswersOver(double fundHalfYear)
        {
            using var store = FrozenIndexStore(fundHalfYear);
            await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
            await connection.OpenAsync();

            var (names, income) = await IndexFamilies.InputsAsync(connection, "MID", night, default);
            var inputs = IndexNightRead.Prepare("MID", night, names, income)!;
            var levels = await IndexFamilies.LevelsAsync(connection, inputs, default);

            return rules.ToDictionary(
                rule => rule.Name,
                rule => IndexNightRead.Answers(inputs, BreakoutRule.Name, IndexRules.Read(inputs, new IndexRule(rule.Name, breakout, rule.Parameters, FreezeAt), levels)),
                StringComparer.Ordinal);
        }

        var closed = await AnswersOver(1.0);
        var open = await AnswersOver(1.01);

        static string Fired(IReadOnlyList<IndexAnswer> answers) =>
            string.Join(",", answers.Where(answer => answer.Passed).OrderBy(answer => answer.Place).Select(answer => answer.Ticker));

        static string? Why(IReadOnlyList<IndexAnswer> answers, string ticker) => answers.Single(answer => answer.Ticker == ticker).Reason;

        // The live rule fires on every member breaking out above its floors with four profitable quarters, by its volume
        // over its average: IE, MV, PN, IA, IC, ID and IB; FL under the floor, PR on its loss and NH with no setup.
        Assert.Equal("IE,MV,PN,IA,IC,ID,IB", Fired(closed["live"]));

        // The cover: a member whose coverage the night stored as passing fires, one stored failing or not read does not.
        Assert.Equal("IE,MV,PN,IA,IC", Fired(closed["cover"]));
        Assert.Equal((IndexNightRead.NoCover, IndexNightRead.NoCover), (Why(closed["cover"], "ID"), Why(closed["cover"], "IB")));

        // Four times the volume: IE at about 3.8 times fires and MV at about 2.1 does not.
        Assert.Equal("IE", Fired(closed["volume"]));
        Assert.Equal(IndexRules.VolumeUnder, Why(closed["volume"], "MV"));

        // The floor at twice: IA's $100 million a day clears it and MV's $15 million and PN's $16 million do not.
        Assert.Equal("IE,IA,IC,ID,IB", Fired(closed["floors"]));
        Assert.Equal((IndexNightRead.UnderTheFloors, IndexNightRead.UnderTheFloors), (Why(closed["floors"], "MV"), Why(closed["floors"], "PN")));

        // The industry's fall: IA's industry down a hundredth over a month is kept off; ID's at nothing and IB's not read are
        // not.
        Assert.Equal("IE,MV,PN,IC,ID,IB", Fired(closed["industry"]));
        Assert.Equal(IndexRules.IndustryFell, Why(closed["industry"], "IA"));

        // IJH against SPY over half a year: closed at exactly 1, every member kept off; open at 1.01, every member the live
        // rule fires on fires.
        Assert.Equal(string.Empty, Fired(closed["fund"]));
        Assert.Equal(IndexRules.SwitchClosed, Why(closed["fund"], "IA"));
        Assert.Equal(Fired(open["live"]), Fired(open["fund"]));

        // The lowest close at $10: PN at about $8.50 is under it, and every member at $102 clears it.
        Assert.Equal("IE,MV,IA,IC,ID,IB", Fired(closed["price"]));
        Assert.Equal(IndexNightRead.UnderTheFloors, Why(closed["price"], "PN"));

        // The hold of 21 sessions: the same members, each trade given 21 sessions where the live rule's is given 63.
        Assert.Equal(Fired(closed["live"]), Fired(closed["hold"]));
        Assert.All(closed["hold"].Where(answer => answer.Passed), answer => Assert.Equal(21, answer.Cap));
        Assert.All(closed["live"].Where(answer => answer.Passed), answer => Assert.Equal(BreakoutRule.CapSessions, answer.Cap));

        // The stop at three typical moves: the same members, each stop twice as far under the buy as the live rule's, a
        // hundredth of a cent apart at most for the rounding to four places.
        Assert.Equal(Fired(closed["live"]), Fired(closed["stop"]));

        foreach (var answer in closed["stop"].Where(answer => answer.Passed))
        {
            var near = closed["live"].Single(one => one.Ticker == answer.Ticker);

            Assert.InRange(Math.Abs((answer.Entry!.Value - answer.Stop!.Value) - (2 * (near.Entry!.Value - near.Stop!.Value))), 0m, 0.0002m);
        }
    }

    [Fact]
    public async Task AnIndexRuleWhoseEvaluatorMovedOrWhoseSettingsLeftItsGridIsReadByNoneAndNamed()
    {
        using var store = FrozenIndexStore(1.01);
        var breakout = IndexEvaluator("breakout-400");
        var live = IndexRules.Provisional(breakout);

        // The breakout frozen on the S&P 400 with its live rule at four times the volume, which passes IE alone, and a
        // variant at the cover.
        Assert.Equal(0, (await RegisterVerbAt(store, FreezeAt, RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", Typed(new Dictionary<string, double>(live, StringComparer.Ordinal) { [IndexBreakoutCandidate.HighVolumeParameter] = 3 }), "--variants", "quality=2")).Code);

        var register = await new CandidateRegistrar(FixedClock.At(FreezeAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        string Listed() => string.Join(",", TextRows(store, "SELECT ticker FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND passed = 1 ORDER BY place;"));

        // Read as registered, the live rule draws the family's list in the provisional rule's place, IE alone, and each
        // rule keeps IE, the variant at the cover reading IE's coverage as stored passing.
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("read-night", default, register, FreezeAt.AddHours(1));

        Assert.Equal("IE", Listed());
        Assert.Equal(["IE", "IE"], TextRows(store, "SELECT ticker FROM index_rule_trade ORDER BY candidate DESC, place;"));

        // The live rule as registered under an evaluator version the code no longer carries, and the variant stating a
        // high window its sweep's grid does not hold.
        Core.Candidates.RegisterRow[] moved =
        [
            .. register.Select(row => FamilyRecords.IsLive(row.Candidate)
                ? row with { EvaluatorVersion = "0123456789ab" }
                : row with { Parameters = row.Parameters.Replace("\"highSessions\": 126", "\"highSessions\": 200", StringComparison.Ordinal) }),
        ];

        Assert.Contains(moved, row => row.Parameters.Contains("\"highSessions\": 200", StringComparison.Ordinal));

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("moved-night", default, moved, FreezeAt.AddHours(1));

        // The night run again with neither read: the trades it kept are gone and none is kept, the night names no live
        // rule, and the family's list is the provisional rule's, every member breaking out above its floors with four
        // profitable quarters and not IE alone.
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM index_rule_trade;"));
        Assert.Equal(0, JsonDocument.Parse(TextRows(store, "SELECT settings FROM index_family_night WHERE index_code = 'MID';").Single()).RootElement.GetProperty("live").GetArrayLength());
        Assert.Equal("IE,MV,PN,IA,IC,ID,IB", Listed());

        // And the stage's row names each with why: the evaluator's version as registered and as the code carries it, and
        // the setting its grid refuses.
        var detail = Assert.Single(TextRows(store, "SELECT detail FROM run_log WHERE run_id = 'moved-night' AND detail LIKE '%not read:%';"));

        Assert.Contains($"its evaluator moved from 0123456789ab to {breakout.Version}", detail, StringComparison.Ordinal);
        Assert.Contains("'high' is 200", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachFrozenIndexRuleKeepsItsOwnListAndTheFamilysPageIsDrawnByItsLiveRule()
    {
        using var store = FrozenIndexStore(1.01);
        var breakout = IndexEvaluator("breakout-400");
        var live = IndexRules.Provisional(breakout);

        // The breakout frozen on the S&P 400 at its provisional settings with one variant at four times the volume.
        Assert.Equal(0, (await RegisterVerbAt(store, FreezeAt, RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", Typed(live), "--variants", "highVolume=3")).Code);

        var register = await new CandidateRegistrar(FixedClock.At(FreezeAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new IndexFamilies(clock, store.DatabaseFile).RunAsync("frozen-night", default, register, FreezeAt.AddHours(1));
        var mid = outcome.Nights.Single(one => one.Index == "MID");

        // Each rule keeps its own list, five a night in its order whatever another rule or index holds: the live rule IE,
        // MV, PN, IA and IC, and the variant IE alone.
        Assert.Equal(
            [
                "S&P 400 breakout|IE|1", "S&P 400 breakout|MV|2", "S&P 400 breakout|PN|3", "S&P 400 breakout|IA|4", "S&P 400 breakout|IC|5",
                "variant|IE|1",
            ],
            TextRows(store, "SELECT CASE WHEN candidate LIKE 'the live %' THEN 'S&P 400 breakout' ELSE 'variant' END || '|' || ticker || '|' || place FROM index_rule_trade ORDER BY candidate DESC, place;"));
        Assert.Equal((2, 6), (mid.Rules, mid.RuleTrades.Trades));

        // The family's page is drawn by its live rule, here at the provisional settings, and the night's row names it.
        Assert.Equal("IE,MV,PN,IA,IC,ID,IB", string.Join(",", TextRows(store, "SELECT ticker FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND passed = 1 ORDER BY place;")));
        var drawnBy = JsonDocument.Parse(TextRows(store, "SELECT settings FROM index_family_night WHERE index_code = 'MID';").Single()).RootElement.GetProperty("live");

        Assert.Equal(["breakout"], drawnBy.EnumerateArray().Select(rule => rule.GetProperty("family").GetString()));
        Assert.StartsWith("the live S&P 400 breakout rule at a 126-session high", drawnBy[0].GetProperty("candidate").GetString(), StringComparison.Ordinal);

        // Run again, the night's trades are written again and no other night's.
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("frozen-night-again", default, register, FreezeAt.AddHours(1));

        Assert.Equal(6, Scalar(store, "SELECT COUNT(*) FROM index_rule_trade;"));

        // The next session IA closes at 50, through its stop: its trade on the live rule's list ends there with its result in
        // multiples of its risk and its round trip at the published table beside it. A company whose value the night read none
        // of is read in the $1 to 2 billion band, where a price of $40 and over pays 0.129 per cent, half of it at the buy and
        // half at the sale at 50.
        var next = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1);

        foreach (var ticker in new[] { "IA", "IB", "IC", "ID", "IE", "MV", "PN", "FL", "PR", "NH" })
        {
            var close = ticker == "IA" ? 50m : ticker == "PN" ? 8.5m : 102m;

            StoreYear(store, ticker, [new FamilyBar(next, close + 0.5m, close - 1m, close, 1_000_000)]);
        }

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("frozen-next", default, register, FreezeAt.AddHours(1));

        var ended = TextRows(store, $"SELECT entry || '|' || stop || '|' || ended_on || '|' || printf('%.6f', result) || '|' || printf('%.6f', cost) FROM index_rule_trade WHERE ticker = 'IA' AND candidate LIKE 'the live %';").Single().Split('|');
        var (entry, stop) = (decimal.Parse(ended[0], CultureInfo.InvariantCulture), decimal.Parse(ended[1], CultureInfo.InvariantCulture));
        var risk = (double)(entry - stop);

        Assert.Equal(next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ended[2]);
        Assert.Equal(((50 - (double)entry) / risk).ToString("F6", CultureInfo.InvariantCulture), ended[3]);
        Assert.Equal((((0.129 / 200 * (double)entry) + (0.129 / 200 * 50)) / risk).ToString("F6", CultureInfo.InvariantCulture), ended[4]);
    }
}
