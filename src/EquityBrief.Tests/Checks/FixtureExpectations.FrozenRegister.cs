using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11: the register and the night around the S&P 400's and 600's freezes, each worked by hand: the
// rule of the day before a freeze retired by the night that reaches the block after its stand began, a registration
// again in one second moving nothing; a retired rule's book sold at the close of the session it was retired on and a
// retired swing rule's open trade walked to its own exit; the sweep-answer command recording a run's figures and the
// tester's verdict beside its answer; and the remedy's lines registering the eight freezes over a register shaped as the
// operator's stood on 2026-10-20.
// see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
// see: A retired rule's open positions are ended on every index, a book's holdings sold at the close of the session it was retired on and a swing rule's trades walked to their own exits
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
public partial class FixtureExpectations
{
    const string FreezeLine = "the S&P 400 breakout's freeze of the remedy";

    static readonly string[] MidBreakoutFrozen =
    [
        "--parameters", "highSessions=251,volumeMultiple=1.5,rangeCeiling=1,stopMoves=3,floors=1,lowestClose=5,quality=1,hold=0,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=1,breadthFloor=0.45,nearness=0,recency=0,highVolume=0",
        "--variants", "largeBreadth=2,breadthFloor=0",
    ];

    [Fact]
    public async Task TheNightRetiresTheRuleKeptInTheBackgroundOnTheNightReachingTheBlockAfterItsFreeze()
    {
        // From the freeze's first session, Wednesday 2026-10-21 as session 0, the 63rd session after it is Friday 2027-01-22,
        // counted by hand over the exchange's closures of Thanksgiving, Christmas, New Year's Day and Martin Luther King Jr.
        // Day: October holds 8 from the 21st, November 20, December 22 and January 4 to the 21st 13, 62 sessions after it.
        var first = new DateOnly(2026, 10, 21);
        var before = new DateOnly(2027, 1, 21);
        var reached = new DateOnly(2027, 1, 22);

        Assert.Equal(0, Blocks.Of(first, before));
        Assert.Equal(1, Blocks.Of(first, reached));

        using var store = new TemporaryStore().Migrated();
        var frozenAt = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);

        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt, [RegisterVerb.IndexFamily, BreakoutRule.Name, "--index", "MID", .. MidBreakoutFrozen])).Code);

        var background = Text(store, $"SELECT candidate FROM candidate_register WHERE candidate LIKE '{FamilyRecords.BackgroundPrefix}%';");
        var live = Text(store, $"SELECT candidate FROM candidate_register WHERE candidate LIKE '{FamilyRecords.LivePrefix}%';");

        Assert.Equal(FamilyRecords.BackgroundPrefix + "S&P 400 " + IndexRules.Words(IndexEvaluator("breakout-400"), IndexRules.Provisional(IndexEvaluator("breakout-400"))), background);

        // Registered again with its retirement in one second on 2026-11-10, as a moved rule is: its stand still from the
        // freeze's session, so the night it retires on does not move.
        var moved = "2026-11-10T22:00:00Z";
        var quoted = background.Replace("'", "''", StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            $"SELECT MAX(id) + 1, candidate, rule, test, evaluator, parameters, evaluator_version, 'retired', candidate, '{moved}', 'moved' FROM candidate_register WHERE candidate = '{quoted}';");
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            $"SELECT MAX(r.id) + 1, b.candidate, b.rule, b.test, b.evaluator, b.parameters, b.evaluator_version, 'registered', NULL, '{moved}', NULL FROM candidate_register r, (SELECT * FROM candidate_register WHERE candidate = '{quoted}' AND event = 'registered' LIMIT 1) b;");

        // The night of the 21st retires nothing; the night of the 22nd retires the rule kept in the background with its
        // evidence, the live rule and its market variant standing on.
        LargeSession(store, "2027-01-21");
        Assert.Empty((await new LoopApply(At(2027, 1, 21, 23, 35), store.DatabaseFile, null).NightAsync("night-before")).Retired!);

        LargeSession(store, "2027-01-22");

        var retiring = await new LoopApply(At(2027, 1, 22, 23, 35), store.DatabaseFile, null).NightAsync("night-reached");

        Assert.Equal([background], retiring.Retired);
        Assert.NotNull(retiring.WroteFrom);
        Assert.Equal(
            [$"the rule of the day before the freeze of '{live}', kept beside it in the background from 2026-10-21, and the 63 sessions it ran beside it have passed by 2027-01-22"],
            FamilyRows(store, "SELECT evidence FROM candidate_register WHERE event = 'retired' AND evidence <> 'moved';"));

        var family = CandidateFamily.In(CandidateFamily.Standing(await new CandidateRegistrar(At(2027, 1, 22, 23, 40), store.DatabaseFile).RowsAsync(), At(2027, 1, 22, 23, 40).UtcNow), "breakout on the S&P 400");

        Assert.Equal(2, family.Count);
        Assert.DoesNotContain(family, row => FamilyRecords.IsBackground(row.Candidate));
    }

    [Fact]
    public async Task ARetiredRulesBookIsSoldAtTheCloseOfTheSessionItWasRetiredOnAndItsOtherRulesBookCarriesOn()
    {
        var (store, closes) = HeavyweightRuleStore();

        using (store)
        {
            var heavyweights = HeavyweightIndexEvaluator("heavyweight-400");
            var freezeAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

            // The live rule at the index's own book's settings and one variant at one leader, which holds L1 alone.
            Assert.Equal(0, (await RegisterVerbAt(store, freezeAt, RegisterVerb.IndexFamily, HeavyweightRule.Name, "--index", "MID", "--parameters", Typed(IndexRules.Provisional(heavyweights)), "--variants", "leaders=1")).Code);

            async Task NightAsync(DateOnly session, string run, DateTimeOffset registerAt) =>
                await new IndexFamilies(FixedClock.At(new DateTimeOffset(session.ToDateTime(new TimeOnly(23, 40)), TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
                    .RunAsync(run, default, await new CandidateRegistrar(FixedClock.At(registerAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), registerAt);

            await NightAsync(new DateOnly(2026, 10, 1), "first", freezeAt.AddHours(1));

            IReadOnlyList<string> Books() => TextRows(
                store,
                "SELECT r.id || '|' || h.ticker || '|' || COALESCE(h.ended_on, 'open') || '|' || COALESCE(h.reason, 'held') || '|' || COALESCE(h.exit_close, '-') FROM index_heavyweight_rule_holding h " +
                "JOIN candidate_register r ON r.candidate = h.candidate AND r.event = 'registered' ORDER BY r.id, h.ticker;");

            Assert.Equal(["1|L1|open|held|-", "1|L2|open|held|-", "2|L1|open|held|-"], Books());

            // The variant retired at 11:00 on Friday 2026-10-02, before that session's open; that session closes L1 at 110,
            // L2 at 105 and L3 at 100. The night of the 2nd sells the retired book's L1 at 110, its result L1's growth from
            // its buy at the 1st's close to 110, and carries the live rule's book as before.
            var retiredAt = new DateTimeOffset(2026, 10, 2, 11, 0, 0, TimeSpan.Zero);
            var variant = Text(store, "SELECT candidate FROM candidate_register WHERE id = 2;");

            Assert.Equal(0, (await RegisterVerbAt(store, retiredAt, "--retire", variant, "--evidence", "a variant retired with its book open")).Code);

            var next = new DateOnly(2026, 10, 2);

            foreach (var (ticker, close) in new[] { ("L1", 110m), ("L2", 105m), ("L3", 100m) })
            {
                StoreYear(store, ticker, [new FamilyBar(next, close + 0.5m, close - 0.5m, close, 1_000_000)]);
            }

            await NightAsync(next, "second", new DateTimeOffset(2026, 10, 2, 21, 10, 0, TimeSpan.Zero));

            Assert.Equal(["1|L1|open|held|-", "1|L2|open|held|-", $"2|L1|2026-10-02|{IndexHeavyweights.RuleRetired}|110"], Books());
            Assert.Equal(
                (Statistic.FromRatio(110m / closes["L1"][260]) - 1.0).ToString("F9", CultureInfo.InvariantCulture),
                double.Parse(Text(store, "SELECT printf('%.12f', h.result) FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate AND r.event = 'registered' WHERE r.id = 2;"), CultureInfo.InvariantCulture).ToString("F9", CultureInfo.InvariantCulture));

            // Run again, the night writes what it wrote and sells nothing twice.
            await NightAsync(next, "second-again", new DateTimeOffset(2026, 10, 2, 21, 10, 0, TimeSpan.Zero));

            Assert.Equal(["1|L1|open|held|-", "1|L2|open|held|-", $"2|L1|2026-10-02|{IndexHeavyweights.RuleRetired}|110"], Books());
        }
    }

    [Fact]
    public async Task ARetiredSwingRulesOpenTradeIsWalkedToItsOwnExit()
    {
        using var store = FrozenIndexStore(1.01);
        var breakout = IndexEvaluator("breakout-400");
        var live = IndexRules.Provisional(breakout);

        // The breakout frozen on the S&P 400 at its provisional settings with one variant at four times the volume, which
        // keeps IE alone; the night keeps its trade.
        Assert.Equal(0, (await RegisterVerbAt(store, FreezeAt, RegisterVerb.IndexFamily, BreakoutRule.Name, "--index", "MID", "--parameters", Typed(live), "--variants", "highVolume=3")).Code);

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("frozen-night", default, await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync(), FreezeAt.AddHours(1));

        const string Variant = "SELECT ticker || '|' || IFNULL(ended_on, 'open') FROM index_rule_trade WHERE candidate NOT LIKE 'the live %' ORDER BY ticker;";

        Assert.Equal(["IE|open"], TextRows(store, Variant));

        // The variant retired before the next session, on which IE closes at 50, through its stop: the night walks its open
        // trade to that stop all the same, ended there with its result in multiples of its risk.
        var variant = Text(store, "SELECT candidate FROM candidate_register WHERE candidate NOT LIKE 'the live %' AND event = 'registered';");

        Assert.Equal(0, (await RegisterVerbAt(store, new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero), "--retire", variant, "--evidence", "a variant retired with its trade open")).Code);

        var next = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1);

        foreach (var ticker in new[] { "IA", "IB", "IC", "ID", "IE", "MV", "PN", "FL", "PR", "NH" })
        {
            var close = ticker == "IE" ? 50m : ticker == "PN" ? 8.5m : 102m;

            StoreYear(store, ticker, [new FamilyBar(next, close + 0.5m, close - 1m, close, 1_000_000)]);
        }

        var nextClock = FixedClock.At(new DateTimeOffset(2026, 10, 3, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new IndexFamilies(nextClock, store.DatabaseFile).RunAsync("next-night", default, await new CandidateRegistrar(nextClock, store.DatabaseFile).RowsAsync(), new DateTimeOffset(2026, 10, 3, 21, 10, 0, TimeSpan.Zero));

        var ended = Text(store, "SELECT entry || '|' || stop || '|' || ended_on || '|' || printf('%.6f', result) FROM index_rule_trade WHERE candidate NOT LIKE 'the live %';").Split('|');
        var (entry, stop) = (decimal.Parse(ended[0], CultureInfo.InvariantCulture), decimal.Parse(ended[1], CultureInfo.InvariantCulture));

        Assert.Equal(next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ended[2]);
        Assert.Equal(((50 - (double)entry) / (double)(entry - stop)).ToString("F6", CultureInfo.InvariantCulture), ended[3]);
    }

    [Fact]
    public async Task TheCommandRecordsARunsFiguresAndTheTestersVerdictBesideItsAnswer()
    {
        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();
        var said = new StringWriter();
        var command = new SweepAnswers(new SweepClock(new DateTimeOffset(2026, 10, 21, 11, 10, 0, TimeSpan.Zero)), store.DatabaseFile, sweeps.Path, said);

        void Run(string name, SweepAnswer? answer, object figures)
        {
            Directory.CreateDirectory(Path.Combine(sweeps.Path, name));
            File.WriteAllText(Path.Combine(sweeps.Path, name, IndexSweepRunner.FiguresFile), JsonSerializer.Serialize(figures));

            if (answer is not null)
            {
                File.WriteAllText(Path.Combine(sweeps.Path, name, SweepAnswer.File), answer.Json());
            }
        }

        double[] years = [0.1, -0.3, -0.05, 0.03, 0.08, 0.21, -0.07, -0.09];
        object FamilyFigures(string key, int trades, bool meets) => new { Key = key, Trades = trades, Edge = 0.023, YearEdge = years, EdgeWithoutLargest = -0.01, MeetsFloors = meets };

        // A breakout's search on the S&P 400 that passed none, the strongest of its three settings read, one meeting the
        // floors against 0.4 by luck; a pullback's search stating no count; a heavyweights design's search proposing one of
        // two settings; and the tester's run over the S&P 400 holding a verdict for each.
        Run("20261020T053729Z", new SweepAnswer("MID", "breakout", null, false), new
        {
            index = "MID",
            family = "breakout",
            afterCosts = new[] { FamilyFigures("a", 1033, false), FamilyFigures("b", 290, true), FamilyFigures("c", 640, false) },
            luck = new { settings = 0.4, levels = 0.02 },
            wouldRun = new { key = "high=251|volume=1.5|ceiling=1|stop=3|market=5", passed = false, figures = FamilyFigures("high=251|volume=1.5|ceiling=1|stop=3|market=5", 1033, false) },
        });
        Run("20261020T055645Z", new SweepAnswer("MID", "pullback", null, true), new
        {
            index = "MID",
            family = "pullback-own",
            wouldRun = new { key = "strength 0.75 with quality, profit and cover", passed = true, after = new { Scored = 717, Edge = 0.19, YearEdge = years }, withoutTheLargest = new { Edge = 0.148 } },
        });
        Run("20261020T074857Z", new SweepAnswer("MID", "heavyweight", "a", true), new
        {
            index = "MID",
            family = "heavyweights-own",
            proposal = "quality=off|exit=break",
            settings = new[]
            {
                new { Key = "quality=profit|exit=drop", After = new { Trades = 446, Edge = 0.0075, YearEdge = years, EdgeWithoutLargest = 0.001, MeetsFloors = false } },
                new { Key = "quality=off|exit=break", After = new { Trades = 335, Edge = 0.0217, YearEdge = years, EdgeWithoutLargest = -0.0008, MeetsFloors = true } },
            },
        });
        Run("20261020T062626Z", null, new
        {
            index = "MID",
            family = IndexSweepRunner.TesterOwn,
            tested = new object[]
            {
                new { Family = "breakout", Proposal = OwnProcedures.SwingGrid, verdict = new { Adjusted = 0.1798 }, passed = false },
                new { Family = "heavyweight", Proposal = OwnProcedures.DesignA, verdict = new { Adjusted = 0.261 }, passed = false },
                new { Family = "heavyweight", Proposal = OwnProcedures.DesignB, verdict = new { Adjusted = 0.2905 }, passed = false },
            },
        });
        Run("20261020T062627Z", null, new { index = "SML", family = IndexSweepRunner.TesterOwn, tested = Array.Empty<object>() });

        Assert.Equal(0, await command.RecordAsync("20261020T053729Z", tester: "20261020T062626Z"));
        Assert.Equal(0, await command.RecordAsync("20261020T055645Z"));
        Assert.Equal(0, await command.RecordAsync("20261020T074857Z", tester: "20261020T062626Z"));

        // The breakout's row: the strongest setting with its trades, edge, each year from 2019 and its edge without the
        // five largest, one of its three settings meeting the floors against 0.4 by luck, and the tester's run, adjusted p
        // and verdict; the pullback's its measures and no count or tester; the heavyweights' its proposal's figures among
        // the design's two settings, with luck at two settings times 37 over 256, and design (a)'s verdict.
        Assert.Equal(
            [
                "20261020T053729Z|high=251|volume=1.5|ceiling=1|stop=3|market=5|1033|0.023|-0.01|3|1|0.4|20261020T062626Z|0.1798|0",
                "20261020T055645Z|strength 0.75 with quality, profit and cover|717|0.19|0.148|-|-|-|-|-|-",
                FormattableString.Invariant($"20261020T074857Z|quality=off|exit=break|335|0.0217|-0.0008|2|1|{2 * 37.0 / 256}|20261020T062626Z|0.261|0"),
            ],
            TextRows(store, "SELECT run || '|' || setting || '|' || trades || '|' || edge || '|' || COALESCE(without_largest, '-') || '|' || COALESCE(settings_read, '-') || '|' || COALESCE(meeting, '-') || '|' || COALESCE(luck, '-') || '|' || COALESCE(tester_run, '-') || '|' || COALESCE(adjusted, '-') || '|' || COALESCE(tester_passed, '-') FROM sweep_answer ORDER BY run;"));
        Assert.Equal(
            "{\"2019\":0.1,\"2020\":-0.3,\"2021\":-0.05,\"2022\":0.03,\"2023\":0.08,\"2024\":0.21,\"2025\":-0.07,\"2026\":-0.09}",
            Text(store, "SELECT year_edges FROM sweep_answer WHERE run = '20261020T053729Z';"));

        // Refused with nothing written: a tester run over another index, one holding no verdict for the family, a run that
        // is no tester's, and a tester named beside a run stating no setting it would run.
        var rows = Scalar(store, "SELECT COUNT(*) FROM sweep_answer;");

        Assert.Equal(2, await command.RecordAsync("20261020T053729Z", tester: "20261020T062627Z"));
        Run("20261020T053730Z", new SweepAnswer("MID", "drift", null, true), new { index = "MID", family = "drift", afterCosts = new[] { FamilyFigures("d", 400, true) }, luck = new { settings = 0.4 }, wouldRun = new { key = "d", passed = true, figures = FamilyFigures("d", 400, true) } });
        Assert.Equal(2, await command.RecordAsync("20261020T053730Z", tester: "20261020T062626Z"));
        Assert.Equal(2, await command.RecordAsync("20261020T053729Z", tester: "20261020T055645Z"));
        Assert.Equal(rows, Scalar(store, "SELECT COUNT(*) FROM sweep_answer;"));

        var refusals = said.ToString();

        Assert.Contains("run 20261020T062627Z read the index 'SML', and the answer is the MID's", refusals, StringComparison.Ordinal);
        Assert.Contains("run 20261020T062626Z holds 0 verdicts for the drift, and one is read", refusals, StringComparison.Ordinal);
        Assert.Contains("run 20261020T055645Z is not a tester's run on an index's own history", refusals, StringComparison.Ordinal);
    }

    // A freeze replacing a retired live rule, on the operator's ruling of the evening of 2026-10-20: the S&P 400 drift
    // frozen at its strongest with its market variant, then both retired, is frozen again at the setting the operator
    // ruled with its own market variant, and the rule it replaces, the strongest, is kept in the background in the
    // provisional rule's place, the provisional rule standing in the background already; the freeze naming a rule that is
    // no live rule of the family is refused with nothing written.
    [Fact]
    public async Task AFreezeReplacingARetiredLiveRuleKeepsThatRuleInTheBackground()
    {
        using var store = new TemporaryStore().Migrated();
        var drift = IndexEvaluator("drift-400");
        var strongest = FrozenParameters(RemedyFreezes[2].Parameters);
        const string ruled = "windowSessions=10,reactionMoves=0.5,volumeMultiple=1.25,targetMultiple=3,stopFloor=1,floors=1,lowestClose=5,quality=1,hold=0,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=0,breadthFloor=0.45,wideWindow=0,peers=0,entry=1,revenueGrew=0";
        var freezeAt = new DateTimeOffset(2026, 10, 20, 18, 37, 11, TimeSpan.Zero);

        Assert.Equal(0, (await RegisterVerbAt(store, freezeAt, RegisterVerb.IndexFamily, DriftRule.Name, "--index", "MID", "--parameters", Typed(strongest), "--variants", "largeBreadth=2,breadthFloor=0")).Code);

        var live = Text(store, "SELECT candidate FROM candidate_register WHERE candidate LIKE 'the live %';");
        var variant = Text(store, "SELECT candidate FROM candidate_register WHERE candidate LIKE 'the S&P 400 drift%';");
        var retiredAt = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(0, (await RegisterVerbAt(store, retiredAt, "--retire", variant, "--evidence", "the market variant of the rule the operator's ruling replaced")).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, retiredAt.AddSeconds(1), "--retire", live, "--evidence", "replaced by the setting the operator ruled")).Code);

        // Named a rule that is no live rule: refused, nothing written.
        var rows = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");
        var (refused, why) = await RegisterVerbAt(store, retiredAt.AddMinutes(1), RegisterVerb.IndexFamily, DriftRule.Name, "--index", "MID", "--parameters", ruled, "--variants", "largeBreadth=2,breadthFloor=0", RegisterVerb.Replacing, variant);

        Assert.NotEqual(0, refused);
        Assert.Contains($"'{variant}' is no live rule the {drift.Family} registered", why, StringComparison.Ordinal);
        Assert.Equal(rows, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // Replacing the strongest: the ruled setting live, its variant with the check off, and the strongest in the
        // background at its own settings, at one instant.
        Assert.Equal(0, (await RegisterVerbAt(store, retiredAt.AddMinutes(2), RegisterVerb.IndexFamily, DriftRule.Name, "--index", "MID", "--parameters", ruled, "--variants", "largeBreadth=2,breadthFloor=0", RegisterVerb.Replacing, live)).Code);

        var standing = CandidateFamily.Standing(await new CandidateRegistrar(FixedClock.At(retiredAt.AddMinutes(3), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), retiredAt.AddMinutes(3));
        var background = FamilyRecords.BackgroundPrefix + "S&P 400 " + IndexRules.Words(drift, strongest);

        Assert.Equal(
            [
                FamilyRecords.BackgroundPrefix + "S&P 400 " + IndexRules.Words(drift, IndexRules.Provisional(drift)),
                background,
                FamilyRecords.LivePrefix + "S&P 400 " + IndexRules.Words(drift, FrozenParameters(ruled)),
                "the S&P 400 " + IndexRules.Words(drift, FrozenMoved(FrozenParameters(ruled), (IndexRuleCandidate.LargeBreadthParameter, IndexRuleCandidate.NoMarketCheck), (IndexRuleCandidate.BreadthFloorParameter, 0))),
            ],
            standing.Select(row => row.Candidate).Order(StringComparer.Ordinal).ToArray().OrderBy(name => name.StartsWith(FamilyRecords.BackgroundPrefix, StringComparison.Ordinal) ? 0 : name.StartsWith(FamilyRecords.LivePrefix, StringComparison.Ordinal) ? 1 : 2));
        Assert.Equal(strongest, CandidateEvaluator.Read(standing.Single(row => row.Candidate == background).Parameters));
        Assert.Single(standing.Select(row => row.RegisteredAt).Where(instant => instant == retiredAt.AddMinutes(2)).Distinct());
    }

    // The S&P 400 drift's ruled setting, on the operator's ruling of the evening of 2026-10-20: a drift search passing none,
    // its run stating the strongest of the two settings it read, records in its place the second, which the operator ruled,
    // as its second stage read it at the first pullback's entry, 1,807 trades at +0.098 with its edge each year, beside the
    // run's settings read and those meeting the floors against luck and the tester's verdict on the search, with the day of
    // the ruling and that the stage kept no level; a setting not among the strongest and an entry the stage did not read on
    // the setting are each refused with nothing written.
    [Fact]
    public async Task TheCommandRecordsTheSettingTheOperatorRuledAsTheSecondStageReadItAtItsEntry()
    {
        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();
        var said = new StringWriter();
        var command = new SweepAnswers(new SweepClock(new DateTimeOffset(2026, 10, 21, 11, 10, 0, TimeSpan.Zero)), store.DatabaseFile, sweeps.Path, said);
        double[] years = [0.031, 0.144, -0.116, 0.216, 0.189, 0.331, -0.067, 0.099];
        const string entry = "the first pullback after the reaction within 10 sessions";
        const string ruled = "window=10|reaction=0.5|volume=1.25|target=3|market=3";
        const string strongest = "window=3|reaction=1.5|volume=2|target=2|market=4";

        void Run(string name, SweepAnswer? answer, object figures)
        {
            Directory.CreateDirectory(Path.Combine(sweeps.Path, name));
            File.WriteAllText(Path.Combine(sweeps.Path, name, IndexSweepRunner.FiguresFile), JsonSerializer.Serialize(figures));

            if (answer is not null)
            {
                File.WriteAllText(Path.Combine(sweeps.Path, name, SweepAnswer.File), answer.Json());
            }
        }

        Run("20261020T140546Z", new SweepAnswer("MID", "drift", null, false), new
        {
            index = "MID",
            family = "drift",
            afterCosts = new[] { new { Key = strongest, MeetsFloors = false }, new { Key = ruled, MeetsFloors = false } },
            luck = new { settings = 81.9 },
            wouldRun = new { key = strongest, passed = false, figures = new { Key = strongest, Trades = 1082, Edge = 0.041, YearEdge = years, EdgeWithoutLargest = 0.027, MeetsFloors = false } },
            strongestTen = new[] { new { setting = strongest }, new { setting = ruled } },
            tries = new object[]
            {
                new { Dial = "entry", Level = entry, Setting = 0, Kept = false, with = new { Scored = 1000, Edge = 0.01, YearEdge = years } },
                new { Dial = "entry", Level = entry, Setting = 1, Kept = true, with = new { Scored = 1807, Edge = 0.098, YearEdge = years } },
            },
            crossedLevels = Array.Empty<object>(),
        });
        Run("20261020T141632Z", null, new
        {
            index = "MID",
            family = IndexSweepRunner.TesterOwn,
            tested = new object[] { new { Family = "drift", Proposal = OwnProcedures.SwingGrid, verdict = new { Adjusted = 0.4104 }, passed = false } },
        });

        var on = new DateOnly(2026, 10, 20);

        Assert.Equal(0, await command.RecordAsync("20261020T140546Z", tester: "20261020T141632Z", ruled: new SweepAnswers.RuledSetting(on, ruled, entry)));
        Assert.Equal(
            [$"none passed|{ruled} entering on {entry}|1807|0.098|-|2|0|81.9|0.4104|0|2026-10-20|0"],
            TextRows(store, "SELECT answer || '|' || setting || '|' || trades || '|' || edge || '|' || COALESCE(without_largest, '-') || '|' || settings_read || '|' || meeting || '|' || luck || '|' || adjusted || '|' || tester_passed || '|' || ruled_on || '|' || entry_kept FROM sweep_answer;"));
        Assert.Equal(
            "{\"2019\":0.031,\"2020\":0.144,\"2021\":-0.116,\"2022\":0.216,\"2023\":0.189,\"2024\":0.331,\"2025\":-0.067,\"2026\":0.099}",
            Text(store, "SELECT year_edges FROM sweep_answer;"));

        Assert.Equal(2, await command.RecordAsync("20261020T140546Z", ruled: new SweepAnswers.RuledSetting(on, "window=5|reaction=1|volume=1.5|target=3|market=6", entry)));
        Assert.Equal(2, await command.RecordAsync("20261020T140546Z", ruled: new SweepAnswers.RuledSetting(on, ruled, "the first pullback after the reaction within 5 sessions")));
        Assert.Contains("is not among the strongest settings run 20261020T140546Z's search read", said.ToString(), StringComparison.Ordinal);
        Assert.Contains("second stage read no entry 'the first pullback after the reaction within 5 sessions'", said.ToString(), StringComparison.Ordinal);
        Assert.Equal(["1807"], TextRows(store, "SELECT trades FROM sweep_answer;"));
    }

    // A remedy line split into its arguments as the remedy script's shell splits it: words apart, a double-quoted run one.
    static string[] Arguments(string line)
    {
        var arguments = new List<string>();
        var word = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var letter in line)
        {
            if (letter == '"')
            {
                quoted = !quoted;
            }
            else if (letter == ' ' && !quoted)
            {
                if (word.Length > 0)
                {
                    arguments.Add(word.ToString());
                    word.Clear();
                }
            }
            else
            {
                word.Append(letter);
            }
        }

        if (word.Length > 0)
        {
            arguments.Add(word.ToString());
        }

        return [.. arguments];
    }

    [Fact]
    public async Task TheRemedysRegisterLinesFreezeTheEightFamiliesOverARegisterShapedAsTheOperatorsStood()
    {
        using var store = new TemporaryStore().Migrated();
        var stood = new DateTimeOffset(2026, 10, 18, 22, 42, 26, TimeSpan.Zero);

        // The operator's register on 2026-10-20 held eight S&P 400 and 600 rules standing, each at the version main's code
        // carried, which the merged code no longer carries: the six market-off variants of 19.1, named as their stored rows
        // name them, and the S&P 600 heavyweights' two books of 17.11.
        var old = new (string Name, string Evaluator, IReadOnlyDictionary<string, double> Parameters)[]
        {
            ("the S&P 400 pullback rule at a strength of 0.5, 1 to 5 typical moves deep, a dry-up under 1.5, fresh within 3, 2 times the risk, the stop 0.5 to 4 moves, the market off and band strength 0, the profit gate, the market check off", "pullback-400", Moved(IndexRules.Provisional(IndexEvaluator("pullback-400")), (IndexRuleCandidate.LargeBreadthParameter, 2), (IndexPullbackCandidate.MarketParameter, 0))),
            ("the S&P 400 breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath, the profit gate, the market check off", "breakout-400", Moved(IndexRules.Provisional(IndexEvaluator("breakout-400")), (IndexRuleCandidate.LargeBreadthParameter, 2))),
            ("the S&P 400 drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the target at 2.5 times the risk, the profit gate, the market check off", "drift-400", Moved(IndexRules.Provisional(IndexEvaluator("drift-400")), (IndexRuleCandidate.LargeBreadthParameter, 2))),
            ("the S&P 600 pullback rule at a strength of 0.5, 1 to 5 typical moves deep, a dry-up under 1.5, fresh within 3, 2 times the risk, the stop 0.5 to 4 moves, the market off and band strength 0, the profit gate, the market check off", "pullback-600", Moved(IndexRules.Provisional(IndexEvaluator("pullback-600")), (IndexRuleCandidate.LargeBreadthParameter, 2), (IndexPullbackCandidate.MarketParameter, 0))),
            ("the S&P 600 breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath, the profit gate, the market check off", "breakout-600", Moved(IndexRules.Provisional(IndexEvaluator("breakout-600")), (IndexRuleCandidate.LargeBreadthParameter, 2))),
            ("the S&P 600 drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the target at 2.5 times the risk, the profit gate, the market check off", "drift-600", Moved(IndexRules.Provisional(IndexEvaluator("drift-600")), (IndexRuleCandidate.LargeBreadthParameter, 2))),
            ("the S&P 600 heavyweights rule of each sector's 10 largest members leading it on twelve months' strength skipping the latest month, from 251 sessions back to 21, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate", "heavyweight-600", Moved(IndexRules.Provisional(IndexEvaluator("heavyweight-600")), (IndexHeavyweightCandidate.TwelveOneParameter, 1))),
            ("the S&P 600 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate and the interest cover", "heavyweight-600", Moved(IndexRules.Provisional(IndexEvaluator("heavyweight-600")), (IndexRuleCandidate.QualityParameter, 2))),
        };

        foreach (var ((name, evaluator, parameters), id) in old.Select((one, at) => (one, at + 1)))
        {
            var written = CandidateEvaluator.Write(parameters.Where(pair => pair.Key is not (IndexRuleCandidate.BreadthFloorParameter or IndexDriftCandidate.EntryParameter or IndexDriftCandidate.RevenueGrewParameter)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

            store.Execute(
                "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
                $"VALUES ({id}, '{name.Replace("'", "''", StringComparison.Ordinal)}', 'the rule', 'the test', '{evaluator}', '{written}', '0123456789ab', 'registered', NULL, '{stood:yyyy-MM-ddTHH:mm:ssZ}', NULL);");
        }

        // Every register line of the remedy, in its order, a second apart: each written.
        var remedy = File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", "2026-10-20-20.11-the-index-freezes.txt"))
            .Where(line => line.StartsWith("register ", StringComparison.Ordinal))
            .ToArray();
        var at = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);

        Assert.Equal(15, remedy.Length);

        // The eight freezes the remedy writes are the eight the record's replay is read at, family, index and settings.
        var freezes = remedy
            .Select(Arguments)
            .Where(words => words[1] == RegisterVerb.IndexFamily)
            .Select(words => (Evaluator: words[2] + (words[4] == "MID" ? "-400" : "-600"), Parameters: words[6]))
            .ToArray();

        Assert.Equal(RemedyFreezes, freezes);

        foreach (var (line, place) in remedy.Select((line, place) => (line, place)))
        {
            var (code, said) = await RegisterVerbAt(store, at.AddSeconds(place), Arguments(line)[1..]);

            Assert.True(code == 0, $"{line[..Math.Min(80, line.Length)]}: {said}");
        }

        // Standing after it: on each index the pullback, breakout and drift each a live rule, its market variant and the
        // rule of the day before in the background, the S&P 600 pullback the tester's own choice as well; the S&P 400
        // heavyweights its live rule and the rule of the day before; and the S&P 600 heavyweights those and the two books.
        var standing = CandidateFamily.Standing(await new CandidateRegistrar(FixedClock.At(at.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), at.AddMinutes(1));

        Assert.Equal(
            ["breakout on the S&P 400 3", "breakout on the S&P 600 3", "drift on the S&P 400 3", "drift on the S&P 600 3", "heavyweight on the S&P 400 2", "heavyweight on the S&P 600 4", "pullback on the S&P 400 3", "pullback on the S&P 600 4"],
            standing.GroupBy(row => CandidateFamily.SetupFamilyOf(row.Evaluator)).Select(group => group.Key + " " + group.Count()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(8, standing.Count(row => FamilyRecords.IsLive(row.Candidate)));
        Assert.Equal(8, standing.Count(row => FamilyRecords.IsBackground(row.Candidate)));
        Assert.All(standing, row => Assert.Equal(CandidateEvaluators.Find(row.Evaluator)!.Version, row.EvaluatorVersion));
        Assert.DoesNotContain(standing, row => row.Candidate.EndsWith(", the market check off", StringComparison.Ordinal) && row.Candidate.Contains("the market off and band strength", StringComparison.Ordinal));

        // And every rule standing is one the night reads, none skipped and none refused.
        Assert.Empty(IndexFamilies.StandingRules([.. standing], at.AddMinutes(1)).Skipped);
    }

    // A register shaped as the operator's stood before the remedy below, each row as the store holds it: the S&P 500's four
    // sector heavyweights and the S&P 400's and 600's twenty-five rules, at versions the code no longer carries.
    static readonly (int Id, string Evaluator, string Version, string Registered, string Name, string Parameters)[] OperatorsRegisterBeforeTheRemedy =
    [
        (348, "heavyweight", "161a4d9d9dce", "2026-10-18T04:37:57Z",
            "the live sector heavyweights rule at the 10 largest, 251 sessions against the sector's fund, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
            "{\"fundReturn\": 1, \"highBeta\": 1, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"weekly\": 0}"),
        (349, "heavyweight", "161a4d9d9dce", "2026-10-18T04:37:57Z",
            "the sector heavyweights rule at every company, 251 sessions against the sector's fund, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
            "{\"fundReturn\": 1, \"highBeta\": 1, \"largest\": 0, \"leaders\": 2, \"lookBack\": 251, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"weekly\": 0}"),
        (350, "heavyweight", "161a4d9d9dce", "2026-10-18T04:37:57Z",
            "the sector heavyweights rule at the 10 largest, 251 sessions against the sector's fund, 2 leaders, a beta of at least 1, monthly, sold on no longer leading or a close under the 200-day average",
            "{\"fundReturn\": 1, \"highBeta\": 1, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"soldOnLeading\": 1, \"soldUnderAverage\": 1, \"weekly\": 0}"),
        (351, "heavyweight", "161a4d9d9dce", "2026-10-18T04:37:57Z",
            "the sector heavyweights rule at the 10 largest, 251 sessions against the sector's members, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
            "{\"fundReturn\": 0, \"highBeta\": 1, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"weekly\": 0}"),
        (408, "heavyweight-600", "7e11d1133547", "2026-10-20T18:37:08Z",
            "the S&P 600 heavyweights rule of each sector's 10 largest members leading it on twelve months' strength skipping the latest month, from 251 sessions back to 21, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate",
            "{\"design\": 0, \"floors\": 1, \"highBeta\": 1, \"industries\": 0, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"members\": 0, \"quality\": 1, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"twelveOne\": 1, \"window\": 0}"),
        (409, "heavyweight-600", "7e11d1133547", "2026-10-20T18:37:08Z",
            "the S&P 600 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate and the interest cover",
            "{\"design\": 0, \"floors\": 1, \"highBeta\": 1, \"industries\": 0, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"members\": 0, \"quality\": 2, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"twelveOne\": 0, \"window\": 0}"),
        (410, "pullback-400", "51c963c077f7", "2026-10-20T18:37:09Z",
            "the live S&P 400 pullback rule at a strength of 0.75, 1 to 3 typical moves deep, a dry-up under 1.5, fresh within 8, 2 times the risk, the stop 0.5 to 4 moves, its own market floor off and band strength 6, the profit gate and the interest cover, its market check on the index's own breadth at 35%",
            "{\"band\": 6, \"breadthFloor\": 0.35, \"creditAverage\": 0, \"creditChange\": 0, \"depthHigh\": 3, \"depthLow\": 1, \"dryUp\": 1.5, \"floors\": 1, \"freshness\": 8, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"market\": 0, \"quality\": 2, \"rewardToRisk\": 2, \"stopHigh\": 4, \"stopLow\": 0.5, \"strength\": 0.75}"),
        (411, "pullback-400", "51c963c077f7", "2026-10-20T18:37:09Z",
            "the S&P 400 pullback rule at a strength of 0.75, 1 to 3 typical moves deep, a dry-up under 1.5, fresh within 8, 2 times the risk, the stop 0.5 to 4 moves, its own market floor off and band strength 6, the profit gate and the interest cover, the market check off",
            "{\"band\": 6, \"breadthFloor\": 0, \"creditAverage\": 0, \"creditChange\": 0, \"depthHigh\": 3, \"depthLow\": 1, \"dryUp\": 1.5, \"floors\": 1, \"freshness\": 8, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 2, \"lowestClose\": 5, \"market\": 0, \"quality\": 2, \"rewardToRisk\": 2, \"stopHigh\": 4, \"stopLow\": 0.5, \"strength\": 0.75}"),
        (412, "pullback-400", "51c963c077f7", "2026-10-20T18:37:09Z",
            "the background S&P 400 pullback rule at a strength of 0.5, 1 to 5 typical moves deep, a dry-up under 1.5, fresh within 3, 2 times the risk, the stop 0.5 to 4 moves, the market at 0.45 and band strength 0, the profit gate",
            "{\"band\": 0, \"breadthFloor\": 0.45, \"creditAverage\": 0, \"creditChange\": 0, \"depthHigh\": 5, \"depthLow\": 1, \"dryUp\": 1.5, \"floors\": 1, \"freshness\": 3, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"market\": 0.45, \"quality\": 1, \"rewardToRisk\": 2, \"stopHigh\": 4, \"stopLow\": 0.5, \"strength\": 0.5}"),
        (413, "breakout-400", "c8bbfa8055e9", "2026-10-20T18:37:10Z",
            "the live S&P 400 breakout rule at a 251-session high, 1.5 times the volume, ranges at 1 and the stop 3 typical moves beneath, the profit gate, its market check on the S&P 500's breadth",
            "{\"breadthFloor\": 0.45, \"creditAverage\": 0, \"creditChange\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"highSessions\": 251, \"highVolume\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 1, \"lowestClose\": 5, \"nearness\": 0, \"quality\": 1, \"rangeCeiling\": 1, \"recency\": 0, \"stopMoves\": 3, \"volumeMultiple\": 1.5}"),
        (414, "breakout-400", "c8bbfa8055e9", "2026-10-20T18:37:10Z",
            "the S&P 400 breakout rule at a 251-session high, 1.5 times the volume, ranges at 1 and the stop 3 typical moves beneath, the profit gate, the market check off",
            "{\"breadthFloor\": 0, \"creditAverage\": 0, \"creditChange\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"highSessions\": 251, \"highVolume\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 2, \"lowestClose\": 5, \"nearness\": 0, \"quality\": 1, \"rangeCeiling\": 1, \"recency\": 0, \"stopMoves\": 3, \"volumeMultiple\": 1.5}"),
        (415, "breakout-400", "c8bbfa8055e9", "2026-10-20T18:37:10Z",
            "the background S&P 400 breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath, the profit gate",
            "{\"breadthFloor\": 0.45, \"creditAverage\": 0, \"creditChange\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"highSessions\": 126, \"highVolume\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"nearness\": 0, \"quality\": 1, \"rangeCeiling\": 0.85, \"recency\": 0, \"stopMoves\": 1.5, \"volumeMultiple\": 1.5}"),
        (416, "drift-400", "b1770e2c06a5", "2026-10-20T18:37:11Z",
            "the live S&P 400 drift rule within 3 sessions, up 1.5 typical moves on 2 times the volume, the target at 2 times the risk, the stop at least 1 typical move beneath, the profit gate, its market check on the index's own breadth at 50%",
            "{\"breadthFloor\": 0.5, \"creditAverage\": 0, \"creditChange\": 0, \"entry\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"peers\": 0, \"quality\": 1, \"reactionMoves\": 1.5, \"revenueGrew\": 0, \"stopFloor\": 1, \"targetMultiple\": 2, \"volumeMultiple\": 2, \"wideWindow\": 0, \"windowSessions\": 3}"),
        (417, "drift-400", "b1770e2c06a5", "2026-10-20T18:37:11Z",
            "the S&P 400 drift rule within 3 sessions, up 1.5 typical moves on 2 times the volume, the target at 2 times the risk, the stop at least 1 typical move beneath, the profit gate, the market check off",
            "{\"breadthFloor\": 0, \"creditAverage\": 0, \"creditChange\": 0, \"entry\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 2, \"lowestClose\": 5, \"peers\": 0, \"quality\": 1, \"reactionMoves\": 1.5, \"revenueGrew\": 0, \"stopFloor\": 1, \"targetMultiple\": 2, \"volumeMultiple\": 2, \"wideWindow\": 0, \"windowSessions\": 3}"),
        (418, "drift-400", "b1770e2c06a5", "2026-10-20T18:37:11Z",
            "the background S&P 400 drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the target at 2.5 times the risk, the profit gate",
            "{\"breadthFloor\": 0.45, \"creditAverage\": 0, \"creditChange\": 0, \"entry\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"peers\": 0, \"quality\": 1, \"reactionMoves\": 0.5, \"revenueGrew\": 0, \"stopFloor\": 0, \"targetMultiple\": 2.5, \"volumeMultiple\": 2, \"wideWindow\": 0, \"windowSessions\": 3}"),
        (419, "heavyweight-400", "7e11d1133547", "2026-10-20T18:37:12Z",
            "the live S&P 400 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold under its 200-day average, no quality gate",
            "{\"design\": 0, \"floors\": 1, \"highBeta\": 1, \"industries\": 0, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"members\": 0, \"quality\": 0, \"soldOnLeading\": 0, \"soldUnderAverage\": 1, \"twelveOne\": 0, \"window\": 0}"),
        (420, "heavyweight-400", "7e11d1133547", "2026-10-20T18:37:12Z",
            "the background S&P 400 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate",
            "{\"design\": 0, \"floors\": 1, \"highBeta\": 1, \"industries\": 0, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"members\": 0, \"quality\": 1, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"twelveOne\": 0, \"window\": 0}"),
        (421, "pullback-600", "51c963c077f7", "2026-10-20T18:37:13Z",
            "the live S&P 600 pullback rule at a strength of 0.75, 1 to 4 typical moves deep, a dry-up under 1, fresh within 8, 2.5 times the risk, the stop 0.5 to 2.5 moves, its own market floor off and band strength 2, the profit gate and the interest cover, the market check off",
            "{\"band\": 2, \"breadthFloor\": 0, \"creditAverage\": 0, \"creditChange\": 0, \"depthHigh\": 4, \"depthLow\": 1, \"dryUp\": 1, \"floors\": 1, \"freshness\": 8, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 2, \"lowestClose\": 5, \"market\": 0, \"quality\": 2, \"rewardToRisk\": 2.5, \"stopHigh\": 2.5, \"stopLow\": 0.5, \"strength\": 0.75}"),
        (422, "pullback-600", "51c963c077f7", "2026-10-20T18:37:13Z",
            "the S&P 600 pullback rule at a strength of 0.75, 1 to 4 typical moves deep, a dry-up under 1, fresh within 8, 2.5 times the risk, the stop 0.5 to 2.5 moves, its own market floor off and band strength 2, the profit gate and the interest cover, its market check on the index's own breadth at 35%",
            "{\"band\": 2, \"breadthFloor\": 0.35, \"creditAverage\": 0, \"creditChange\": 0, \"depthHigh\": 4, \"depthLow\": 1, \"dryUp\": 1, \"floors\": 1, \"freshness\": 8, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"market\": 0, \"quality\": 2, \"rewardToRisk\": 2.5, \"stopHigh\": 2.5, \"stopLow\": 0.5, \"strength\": 0.75}"),
        (423, "pullback-600", "51c963c077f7", "2026-10-20T18:37:13Z",
            "the S&P 600 pullback rule at a strength of 0.75, 1 to 4 typical moves deep, a dry-up under 1, fresh within 8, 2.5 times the risk, the stop 0.5 to 4 moves, its own market floor off and band strength 0, the profit gate and the interest cover, the market check off",
            "{\"band\": 0, \"breadthFloor\": 0, \"creditAverage\": 0, \"creditChange\": 0, \"depthHigh\": 4, \"depthLow\": 1, \"dryUp\": 1, \"floors\": 1, \"freshness\": 8, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 2, \"lowestClose\": 5, \"market\": 0, \"quality\": 2, \"rewardToRisk\": 2.5, \"stopHigh\": 4, \"stopLow\": 0.5, \"strength\": 0.75}"),
        (424, "pullback-600", "51c963c077f7", "2026-10-20T18:37:13Z",
            "the background S&P 600 pullback rule at a strength of 0.5, 1 to 5 typical moves deep, a dry-up under 1.5, fresh within 3, 2 times the risk, the stop 0.5 to 4 moves, the market at 0.45 and band strength 0, the profit gate",
            "{\"band\": 0, \"breadthFloor\": 0.45, \"creditAverage\": 0, \"creditChange\": 0, \"depthHigh\": 5, \"depthLow\": 1, \"dryUp\": 1.5, \"floors\": 1, \"freshness\": 3, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"market\": 0.45, \"quality\": 1, \"rewardToRisk\": 2, \"stopHigh\": 4, \"stopLow\": 0.5, \"strength\": 0.5}"),
        (425, "breakout-600", "c8bbfa8055e9", "2026-10-20T18:37:14Z",
            "the live S&P 600 breakout rule at a 126-session high, 1.25 times the volume, ranges at 0.85 and the stop 3 typical moves beneath, the profit gate, its market check on the index's own breadth at 40%",
            "{\"breadthFloor\": 0.4, \"creditAverage\": 0, \"creditChange\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"highSessions\": 126, \"highVolume\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"nearness\": 0, \"quality\": 1, \"rangeCeiling\": 0.85, \"recency\": 0, \"stopMoves\": 3, \"volumeMultiple\": 1.25}"),
        (426, "breakout-600", "c8bbfa8055e9", "2026-10-20T18:37:14Z",
            "the S&P 600 breakout rule at a 126-session high, 1.25 times the volume, ranges at 0.85 and the stop 3 typical moves beneath, the profit gate, the market check off",
            "{\"breadthFloor\": 0, \"creditAverage\": 0, \"creditChange\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"highSessions\": 126, \"highVolume\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 2, \"lowestClose\": 5, \"nearness\": 0, \"quality\": 1, \"rangeCeiling\": 0.85, \"recency\": 0, \"stopMoves\": 3, \"volumeMultiple\": 1.25}"),
        (427, "breakout-600", "c8bbfa8055e9", "2026-10-20T18:37:14Z",
            "the background S&P 600 breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath, the profit gate",
            "{\"breadthFloor\": 0.45, \"creditAverage\": 0, \"creditChange\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"highSessions\": 126, \"highVolume\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"nearness\": 0, \"quality\": 1, \"rangeCeiling\": 0.85, \"recency\": 0, \"stopMoves\": 1.5, \"volumeMultiple\": 1.5}"),
        (428, "drift-600", "b1770e2c06a5", "2026-10-20T18:37:15Z",
            "the live S&P 600 drift rule within 3 sessions, up 0.5 typical moves on 1.25 times the volume, the target at 2.5 times the risk, the stop at least 1 typical move beneath, the profit gate, held 63 sessions, the market check off, its print's quarter's revenue above the same quarter's a year before, both as first filed and stored before the night",
            "{\"breadthFloor\": 0, \"creditAverage\": 0, \"creditChange\": 0, \"entry\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"hold\": 63, \"industryFall\": 0, \"largeBreadth\": 2, \"lowestClose\": 5, \"peers\": 0, \"quality\": 1, \"reactionMoves\": 0.5, \"revenueGrew\": 1, \"stopFloor\": 1, \"targetMultiple\": 2.5, \"volumeMultiple\": 1.25, \"wideWindow\": 0, \"windowSessions\": 3}"),
        (429, "drift-600", "b1770e2c06a5", "2026-10-20T18:37:15Z",
            "the S&P 600 drift rule within 3 sessions, up 0.5 typical moves on 1.25 times the volume, the target at 2.5 times the risk, the stop at least 1 typical move beneath, the profit gate, held 63 sessions, its market check on the index's own breadth at 40%, its print's quarter's revenue above the same quarter's a year before, both as first filed and stored before the night",
            "{\"breadthFloor\": 0.4, \"creditAverage\": 0, \"creditChange\": 0, \"entry\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"hold\": 63, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"peers\": 0, \"quality\": 1, \"reactionMoves\": 0.5, \"revenueGrew\": 1, \"stopFloor\": 1, \"targetMultiple\": 2.5, \"volumeMultiple\": 1.25, \"wideWindow\": 0, \"windowSessions\": 3}"),
        (430, "drift-600", "b1770e2c06a5", "2026-10-20T18:37:15Z",
            "the background S&P 600 drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the target at 2.5 times the risk, the profit gate",
            "{\"breadthFloor\": 0.45, \"creditAverage\": 0, \"creditChange\": 0, \"entry\": 0, \"floors\": 1, \"fundOverSpy\": 0, \"hold\": 0, \"industryFall\": 0, \"largeBreadth\": 0, \"lowestClose\": 5, \"peers\": 0, \"quality\": 1, \"reactionMoves\": 0.5, \"revenueGrew\": 0, \"stopFloor\": 0, \"targetMultiple\": 2.5, \"volumeMultiple\": 2, \"wideWindow\": 0, \"windowSessions\": 3}"),
        (431, "heavyweight-600", "7e11d1133547", "2026-10-20T18:37:16Z",
            "the live S&P 600 heavyweights rule of each sector's 10 largest members leading it over 63 sessions, 1 leader(s) a sector with a beta of at least 1, sold under its 200-day average, no quality gate",
            "{\"design\": 0, \"floors\": 1, \"highBeta\": 1, \"industries\": 0, \"largest\": 10, \"leaders\": 1, \"lookBack\": 63, \"members\": 0, \"quality\": 0, \"soldOnLeading\": 0, \"soldUnderAverage\": 1, \"twelveOne\": 0, \"window\": 0}"),
        (432, "heavyweight-600", "7e11d1133547", "2026-10-20T18:37:16Z",
            "the background S&P 600 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate",
            "{\"design\": 0, \"floors\": 1, \"highBeta\": 1, \"industries\": 0, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"members\": 0, \"quality\": 1, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"twelveOne\": 0, \"window\": 0}"),
    ];

    // The remedy's register lines over that register, in their order a second apart: the S&P 400 drift's strongest and its
    // market variant retired, every rule whose evaluator moved registered again, and the drift frozen at the setting the
    // operator ruled in the strongest's place. Each line is written; standing after them is every rule that stood but
    // those two, the ruled setting live with its market variant, and the strongest in the background at its own settings
    // beside the rule of the day before, every rule at the version the code carries and none of the index rules skipped by
    // the night.
    [Fact]
    public async Task TheRemedyFreezesTheSAndP400DriftAtTheRuledSettingOverARegisterShapedAsTheOperatorsStood()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var (id, evaluator, version, registered, name, parameters) in OperatorsRegisterBeforeTheRemedy)
        {
            store.Execute(
                "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
                $"VALUES ({id}, '{name.Replace("'", "''", StringComparison.Ordinal)}', 'the rule', 'the test', '{evaluator}', '{parameters}', '{version}', 'registered', NULL, '{registered}', NULL);");
        }

        var remedy = File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", "2026-10-20-20.11-the-evening-rulings.txt"))
            .Where(line => line.StartsWith("register ", StringComparison.Ordinal))
            .ToArray();
        var at = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);

        Assert.Equal(4, remedy.Length);

        foreach (var (line, place) in remedy.Select((line, place) => (line, place)))
        {
            var (code, said) = await RegisterVerbAt(store, at.AddSeconds(place), Arguments(line)[1..]);

            Assert.True(code == 0, $"{line[..Math.Min(80, line.Length)]}: {said}");
        }

        var drift = IndexEvaluator("drift-400");
        var freeze = Arguments(remedy[^1]);
        var ruled = FrozenParameters(freeze[Array.IndexOf(freeze, "--parameters") + 1]);
        var strongest = CandidateEvaluator.Read(OperatorsRegisterBeforeTheRemedy.Single(row => row.Id == 416).Parameters);
        var background = FamilyRecords.BackgroundPrefix + "S&P 400 " + IndexRules.Words(drift, strongest);
        var standing = CandidateFamily.Standing(await new CandidateRegistrar(FixedClock.At(at.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), at.AddMinutes(1));

        Assert.Equal(
            OperatorsRegisterBeforeTheRemedy
                .Where(row => row.Id is not (416 or 417))
                .Select(row => row.Name)
                .Concat(
                [
                    FamilyRecords.LivePrefix + "S&P 400 " + IndexRules.Words(drift, ruled),
                    "the S&P 400 " + IndexRules.Words(drift, FrozenMoved(ruled, (IndexRuleCandidate.LargeBreadthParameter, IndexRuleCandidate.NoMarketCheck), (IndexRuleCandidate.BreadthFloorParameter, 0))),
                    background,
                ])
                .Order(StringComparer.Ordinal),
            standing.Select(row => row.Candidate).Order(StringComparer.Ordinal));
        Assert.Equal(strongest, CandidateEvaluator.Read(standing.Single(row => row.Candidate == background).Parameters));
        Assert.All(standing, row => Assert.Equal(CandidateEvaluators.Find(row.Evaluator)!.Version, row.EvaluatorVersion));
        Assert.Empty(IndexFamilies.StandingRules([.. standing], at.AddMinutes(1)).Skipped);
    }
}
