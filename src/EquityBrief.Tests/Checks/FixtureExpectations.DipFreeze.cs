using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.3: the short-term dip's freezes. Each dial the rule gains is worked by hand on both sides at a
// constructed session, the fall in typical moves, the drop's volume, the RSI's exit and the up close's, the VIX above its
// median and a fund above its average, and the fall and the volume read the same as the ledger's readings; the remedy's
// lines freeze each index's dip over a register shaped as the operator's stood after the quality freezes, its live rule
// and its versions at one instant, and the form refuses what it cannot write, writing nothing; the family's correction for
// luck worked by hand over its own rules on each index alone; the night over a constructed store keeping the live rule
// and each version under its own name, the live rule's market setting and its market variant read on both sides of the
// S&P 500's breadth floor, and a rule whose code moved not kept; the monthly run reading the family from its next run
// after the freeze; the answer of a dip's sweep recorded with its tester's verdict; and no source another rule's version
// pins one 22.3 writes.
// see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
// see: A dip rule is registered as the family on its index and kept by the dip's own step alone
public partial class FixtureExpectations
{
    const string DipRemedy = "2026-10-25-22.3-the-dip-freezes.txt";

    static readonly DateTimeOffset DipFrozenAt = new(2026, 10, 25, 23, 0, 0, TimeSpan.Zero);

    // The remedy's register lines, one an index, in the order it runs them.
    static string[] DipRegisterLines() =>
        [.. File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", DipRemedy)).Where(line => line.StartsWith("register ", StringComparison.Ordinal))];

    static IReadOnlyDictionary<string, double> Dials(string stated) => VerbArguments.Parameters(stated);

    // 196 closes of 100, three of 112 and the night's 106: a fall of 6 over the drop's three sessions, 3 typical moves of
    // 2, its 200-session average (19,600 + 336 + 106) / 200 = 100.21 under the night's close.
    static double[] FallOfSix => [.. Enumerable.Repeat(100.0, 197), 112, 112, 112, 106];

    static DipMember Falling(double move, IReadOnlyList<long>? volumes = null) =>
        new("DX", FallOfSix, move, true, "under the floors", [], null, DipRule.NoMarket, Volumes: volumes);

    // The 50 sessions before the drop trading 1,000 shares each and the drop's three the given volume each.
    static long[] VolumesOf(long drop) => [.. Enumerable.Repeat(9_999L, 148), .. Enumerable.Repeat(1_000L, 50), drop, drop, drop];

    [Fact]
    public void EachNewDialIsWorkedByHandOnBothSidesAtAConstructedSession()
    {
        var fall = DipRule.SettingOf(-3, 200, DipRule.RsiExit, 7, 3, DipRule.MarketOff, 0);

        // The fall: 6 over the drop's three sessions in a typical move of 2 is 3 moves, which a fall of at least 3 passes;
        // in a typical move of 2.01 it is 2.985, which it does not.
        Assert.Equal(3.0, DipRule.Fall(FallOfSix, 200, 2)!.Value, 12);
        Assert.True(Passes(DipRule.Read(Falling(2), fall), DipRule.DropGate));
        Assert.True(DipRule.Read(Falling(2), fall).Passed);
        Assert.False(Passes(DipRule.Read(Falling(2.01), fall), DipRule.DropGate));
        Assert.Equal("a fall of 2.99 typical moves over its 3 sessions, under 3", DipRule.Read(Falling(2.01), fall).Gates.Single(gate => gate.Name == DipRule.DropGate).Reason);
        Assert.Null(DipRule.Fall(FallOfSix, 2, 2));
        Assert.Null(DipRule.Fall(FallOfSix, 200, 0));

        // The drop's volume: three sessions of 1,499 over fifty of 1,000 is 1.499 times, under 1.5; three of 1,500 is 1.5,
        // not under it. The 148 sessions before the fifty are not read.
        var quiet = fall with { VolumeUnder = 1.5 };

        Assert.Equal(1.499, DipRule.DropVolume(VolumesOf(1_499), 200)!.Value, 12);
        Assert.True(Passes(DipRule.Read(Falling(2, VolumesOf(1_499)), quiet), DipRule.DropGate));
        Assert.False(Passes(DipRule.Read(Falling(2, VolumesOf(1_500)), quiet), DipRule.DropGate));
        Assert.False(Passes(DipRule.Read(Falling(2), quiet), DipRule.DropGate));
        Assert.Null(DipRule.DropVolume(VolumesOf(1_500), 51));

        // The RSI's exit: closes 10, 11 and 10 read an RSI of 50; a close of 10.5 after them averages a gain of 0.5 and a
        // loss of 0.25, an RSI of 66.67 over 65, and sells at the next open; 10.4 averages 0.45 against 0.25, 64.29, and
        // does not.
        static IReadOnlyList<DipSession> After(int exit, params double[] closes) =>
            DipRule.Sessions([.. closes.Select((close, at) => (new DateOnly(2026, 10, 1).AddDays(at), close, close))], 2, exit, [.. closes.Select((_, at) => new DateOnly(2026, 10, 1).AddDays(at))]);

        Assert.Equal(100 - (100 / 3.0), DipRule.Rsi([10, 11, 10, 10.5])[3]!.Value, 9);
        Assert.Equal(true, After(DipRule.RsiExit, 10, 11, 10, 10.5)[0].Trigger);
        Assert.Equal(false, After(DipRule.RsiExit, 10, 11, 10, 10.4)[0].Trigger);
        Assert.Null(After(DipRule.RsiExit, 10, 11, 10, 10.5)[0].Average);

        // The up close: a close above the close before sells, one equal to it does not.
        Assert.Equal(true, After(DipRule.UpCloseExit, 10, 11, 10, 10.01)[0].Trigger);
        Assert.Equal(false, After(DipRule.UpCloseExit, 10, 11, 10, 10)[0].Trigger);

        // The average's exit as it stood: no trigger and the five closes' average.
        Assert.Null(After(5, 10, 11, 10, 10.5, 12)[1].Trigger);
        Assert.Equal((10 + 11 + 10 + 10.5 + 12) / 5, After(5, 10, 11, 10, 10.5, 12)[1].Average!.Value, 12);

        // The VIX over three sessions before: closes 1, 2, 3, 2 and 5. The fourth reads 2 against the median of 1, 2 and 3,
        // 2, and is closed; the fifth reads 5 against that of 2, 3 and 2, 2, and is open; a session missing its close or
        // one of those before it is closed.
        DateOnly[] five = [.. Enumerable.Range(0, 5).Select(at => new DateOnly(2026, 10, 5).AddDays(at))];

        Assert.Equal([false, false, false, false, true], DipRule.VixAboveItsMedian(five, [.. five.Zip([1.0, 2, 3, 2, 5])], 3));
        Assert.Equal([false, false, false, false, false], DipRule.VixAboveItsMedian(five, [.. five.Zip([1.0, 2, 3, 2, 5]).Where((_, at) => at != 2)], 3));

        // A fund over two sessions: closes 10, 12, 11 and 11. The second's 12 is above the mean of 10 and 12, 11; the
        // third's 11 under 11.5; the fourth's 11 at its mean of 11, closed.
        Assert.Equal([false, true, false, false], DipRule.FundAboveItsAverage(five[..4], [.. five[..4].Zip([10.0, 12, 11, 11])], 2));

        // The market levels each index reads, the sweep's own in its order and keyed alike; the S&P 500's breadth a level
        // of the S&P 400 and 600 alone; and the floors the index rules read.
        Assert.Equal(
            ["off", "own 0.35", "own 0.40", "own 0.45", "own 0.50", "fund trend", "VIX"],
            DipRule.MarketLevels(IndexSweepRunner.DipIndices.Keys.First()).Select(level => level.Key));
        Assert.Equal("S&P 500 0.45", DipRule.MarketLevels("MID")[5].Key);
        Assert.All(IndexSweepRunner.DipIndices.Keys, index => Assert.Equal(IndexSweepRunner.DipMarketLevels(index).Select(level => level.Key), DipRule.MarketLevels(index).Select(level => level.Key)));
        Assert.Equal(IndexRuleCandidate.BreadthFloors, DipRule.OwnBreadthFloors);
        Assert.Equal(IndexRuleCandidate.LargeBreadthFloor, DipRule.LargeBreadthFloor);
        Assert.Equal(Core.Ledger.LedgerReadings.DropSessions, DipRule.DropFallSessions);
        Assert.Equal(Core.Ledger.LedgerReadings.DropVolumeSessions, DipRule.VolumeSessions);

        // The market gate: a level's answer with what it read, closed naming it, and the setting off passing every member.
        var large = DipRule.MarketLevels("MID")[5];

        Assert.True(DipRule.MarketOn(large, true, "the S&P 500's breadth at 0.45").Passed);
        Assert.Equal("its market setting, the S&P 500's breadth at 45 per cent, closed: the S&P 500's breadth at 0.4499", DipRule.MarketOn(large, false, "the S&P 500's breadth at 0.4499").Reason);
        Assert.Same(DipRule.NoMarket, DipRule.MarketOn(DipRule.MarketLevels("MID")[0], false, "nothing"));

        // The provisional setting reads as it did: its short form, its words and its exit's level.
        Assert.Equal("rsi 10|drop 3|trend long|stop 4|exit 5|limit 10|market off", DipRule.Provisional.Short);
        Assert.Equal(DipRule.Provisional, DipSetting.FromShort(DipRule.Provisional.Short));
        Assert.Equal(5, DipRule.Provisional.ExitLevel);
        Assert.Equal(fall with { VolumeUnder = 1.5 }, DipSetting.FromShort((fall with { VolumeUnder = 1.5 }).Short));
    }

    // The fall and the drop's volume the night and the sweep read through the dip's own rule are the ledger's readings of
    // them, at every session of a constructed series of moving closes and volumes.
    [Fact]
    public void TheDipsFallAndDropVolumeReadTheSameAsTheLedgersReadings()
    {
        var first = new DateOnly(2026, 1, 2);
        SweepBar[] bars =
        [
            .. Enumerable.Range(0, 120).Select(at =>
            {
                var close = Statistic.ToPrice(100 + (5 * Math.Sin(at / 3.0)) + (at % 7));

                return new SweepBar(first.AddDays(at), close + 1.5m, close - 1.5m, close, 1_000 + (at * 37 % 900), close, close);
            }),
        ];
        var series = SweepColumns.Series(new SweepName("DX", bars, [(null, null)], []), bars.Select((bar, at) => (bar.Session, at)).ToDictionary(pair => pair.Session, pair => pair.at));
        var closes = series.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray();
        var volumes = series.Bars.Select(bar => bar.Volume).ToArray();
        var read = 0;

        for (var bar = 0; bar < series.Bars.Length; bar++)
        {
            var ledger = Worker.Ledger.LedgerContext.DropFall(series, bar);
            var dip = DipRule.Fall(closes, bar, series.Atr[bar]);

            Assert.Equal(double.IsNaN(ledger) ? (double?)null : ledger, dip);
            Assert.Equal(Worker.Ledger.LedgerSetups.DropVolume(series, bar), DipRule.DropVolume(volumes, bar));
            read += dip is null ? 0 : 1;
        }

        Assert.True(read >= 100, $"Read {read} fall(s), expected at least 100.");
    }

    // The register as it stood after the quality freezes of 2026-10-25: the operator's 58 rules and the three indices'
    // quality families, eight each.
    static async Task<TemporaryStore> RegisterAfterTheQualityFreezes()
    {
        var store = OperatorShapedRegister();
        var (large, saidLarge) = await RegisterVerbAt(store, QualityFrozenAt, Arguments(Assert.Single(RemedyRegisterLines()))[1..]);

        Assert.True(large == 0, saidLarge);

        foreach (var (line, at) in MidSmallRemedyLines().Where(line => line.StartsWith("register ", StringComparison.Ordinal)).Select((line, at) => (line, at)))
        {
            var (code, said) = await RegisterVerbAt(store, MidSmallFrozenAt.AddMinutes(at), Arguments(line)[1..]);

            Assert.True(code == 0, said);
        }

        return store;
    }

    // Each index's live rule and versions as the operator's second answers name them, by the dials each changes.
    static readonly (string Index, string Named, string Live, string[] Versions)[] DipFreezes =
    [
        ("GSPC", "S&P 500", "drop=-3,trend=50,exit=65,limit=10,stop=3,market=6,volume=0", ["trend=200", "limit=7", "stop=4", "market=0"]),
        ("MID", "S&P 400", "drop=-3,trend=50,exit=65,limit=5,stop=3,market=5,volume=0", ["trend=200", "stop=4", "market=0"]),
        ("SML", "S&P 600", "drop=-3,trend=200,exit=65,limit=7,stop=3,market=1,volume=1.5", ["exit=5", "trend=50", "limit=5", "market=0"]),
    ];

    [Fact]
    public async Task TheRemedysLinesFreezeEachIndexsDipOverARegisterShapedAsTheOperatorsStood()
    {
        using var store = await RegisterAfterTheQualityFreezes();
        var lines = DipRegisterLines();

        Assert.Equal(3, lines.Length);

        foreach (var ((index, named, live, versions), at) in DipFreezes.Select((one, at) => (one, at)))
        {
            var words = Arguments(lines[at]);

            // The line states the live rule and each version as the answers name it, the live rule's dials but the one changed.
            Assert.Equal(["register", RegisterVerb.IndexFamily, DipRule.Name, "--index", index, "--parameters", live, "--variants"], words[..8]);
            Assert.Equal(
                versions.Select(change => string.Join(",", Dials(live).Select(pair => pair.Key == change.Split('=')[0] ? change : pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)))),
                words[8].Split(';'));

            var (code, said) = await RegisterVerbAt(store, DipFrozenAt.AddMinutes(at), words[1..]);

            Assert.True(code == 0, said);
            Assert.Contains(FormattableString.Invariant($"registered {versions.Length + 1} at one instant"), said, StringComparison.Ordinal);
        }

        var rows = await new CandidateRegistrar(FixedClock.At(DipFrozenAt.AddMinutes(5), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var standing = CandidateFamily.Standing(rows, DipFrozenAt.AddMinutes(5));

        // Standing after them: the 58, the quality families' 24 and the dip's 5, 4 and 5.
        Assert.Equal(58 + 24 + 14, standing.Count);

        foreach (var (index, named, live, versions) in DipFreezes)
        {
            var family = CandidateFamily.In(standing, "dip on the " + named).OrderBy(row => row.Id).ToArray();
            var evaluator = CandidateEvaluators.All.OfType<DipCandidate>().Single(dip => dip.Index == index);

            Assert.Equal(versions.Length + 1, family.Length);
            Assert.StartsWith(FamilyRecords.LivePrefix + named + " dip rule at a fall of 3 moves", family[0].Candidate, StringComparison.Ordinal);
            Assert.All(family.Skip(1), row => Assert.StartsWith("the " + named + " dip rule at ", row.Candidate, StringComparison.Ordinal));
            Assert.All(family, row => Assert.Equal((evaluator.Name, evaluator.Version), (row.Evaluator, row.EvaluatorVersion)));
            Assert.Single(family.Select(row => row.RegisteredAt).Distinct());
            Assert.Equal(Dials(live).OrderBy(pair => pair.Key), CandidateEvaluator.Read(family[0].Parameters).OrderBy(pair => pair.Key));
            Assert.Equal(DipRule.SettingOf(-3, Dials(live)["trend"], DipRule.RsiExit, (int)Dials(live)["limit"], 3, DipRule.MarketLevels(index)[(int)Dials(live)["market"]].Key, Dials(live)["volume"]), evaluator.SettingOf(CandidateEvaluator.Read(family[0].Parameters)));

            // The market variant is the live rule with its market setting off, every other dial the live rule's.
            var variant = evaluator.SettingOf(CandidateEvaluator.Read(family[^1].Parameters));

            Assert.Equal(evaluator.SettingOf(CandidateEvaluator.Read(family[0].Parameters)) with { Market = DipRule.MarketOff }, variant);
        }

        // The S&P 600's live rule reads the drop's volume and its 7 sessions; the S&P 500's the VIX above its median.
        Assert.Equal((1.5, 7, "VIX"), (new DipCandidate("SML").SettingOf(Dials(DipFreezes[2].Live)).VolumeUnder!.Value, new DipCandidate("SML").SettingOf(Dials(DipFreezes[2].Live)).TimeLimit, new DipCandidate("GSPC").SettingOf(Dials(DipFreezes[0].Live)).Market));

        // The same line again is refused while the family stands, and writes nothing.
        var (again, refused) = await RegisterVerbAt(store, DipFrozenAt.AddMinutes(10), Arguments(lines[0])[1..]);

        Assert.NotEqual(0, again);
        Assert.Contains("the dip on the S&P 500 holds 5 rule(s) standing", refused, StringComparison.Ordinal);
        Assert.Equal(rows.Count, (await new CandidateRegistrar(FixedClock.At(DipFrozenAt.AddMinutes(11), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync()).Count);
    }

    [Fact]
    public async Task ADipFreezeIsRefusedOffTheGridOnAnIndexItCarriesNoRuleForWithOtherParametersAVersionNoNeighbourOrPastNineRulesAndWritesNothing()
    {
        using var store = new TemporaryStore().Migrated();
        const string Live = "drop=-3,trend=50,exit=65,limit=10,stop=3,market=6,volume=0";
        var nine = string.Join(";", new[] { "drop=-2", "drop=-4", "trend=200", "exit=5", "exit=1", "limit=7", "stop=4", "market=5", "market=0" }
            .Select(change => Live.Replace(change.Split('=')[0] + "=" + Dials(Live)[change.Split('=')[0]].ToString(CultureInfo.InvariantCulture), change, StringComparison.Ordinal)));

        foreach (var ((args, expected), at) in new (string[] Args, string Said)[]
        {
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", "drop=-5,trend=50,exit=65,limit=10,stop=3,market=6,volume=0"], "drop=-5, which the sweep's grid does not hold"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", "drop=-3,trend=50,exit=65,limit=10,stop=3,market=7,volume=0"], "market=7, which the sweep's grid does not hold"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", "drop=-3,trend=50,exit=65,limit=10,stop=3,market=6"], "the dip rule reads [drop, exit, limit, market, stop, trend, volume]"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "NDX", "--parameters", Live], "no dip rule is carried for an index coded 'NDX'"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", Live, "--variants", "drop=-3,trend=50,exit=65,limit=5,stop=3,market=6,volume=0"], "is neither one of the live rule's one-step neighbours"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", Live, "--variants", "drop=-3,trend=200,exit=65,limit=10,stop=4,market=6,volume=0"], "is neither one of the live rule's one-step neighbours"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", Live, "--variants", "drop=-3,trend=200,exit=65,limit=10,stop=3,market=6,volume=0;drop=-3,trend=200,exit=65,limit=10,stop=3,market=6,volume=0"], "a version is given twice"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", Live, "--variants", nine], "would hold 10 rules, past the 9 a family keeps"),
            ([RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", Live, "--replacing", "the live rule"], "takes no --replacing"),
        }.Select((one, at) => (one, at)))
        {
            var (code, said) = await RegisterVerbAt(store, DipFrozenAt.AddSeconds(at), args);

            Assert.NotEqual(0, code);
            Assert.Contains(expected, said, StringComparison.Ordinal);
        }

        Assert.Empty(await new CandidateRegistrar(FixedClock.At(DipFrozenAt.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync());
    }

    [Fact]
    public async Task TheDipsLuckCorrectionCountsItsOwnRulesOnEachIndexAlone()
    {
        using var store = await RegisterAfterTheQualityFreezes();
        var registrar = new CandidateRegistrar(FixedClock.At(DipFrozenAt.AddMinutes(10), SessionZones.UnitedStates), store.DatabaseFile);
        var before = await registrar.RowsAsync();

        foreach (var (line, at) in DipRegisterLines().Select((line, at) => (line, at)))
        {
            var (code, said) = await RegisterVerbAt(store, DipFrozenAt.AddMinutes(at), Arguments(line)[1..]);

            Assert.True(code == 0, said);
        }

        var after = await registrar.RowsAsync();
        var window = DipFrozenAt.AddDays(1);

        int Count(IReadOnlyList<RegisterRow> rows, string family) => CandidateFamily.Divisor(CandidateFamily.In(rows, family), window);

        var expected = Expected("dip-freeze");

        foreach (var (family, place) in new[] { ("dip on the S&P 500", 0), ("dip on the S&P 400", 1), ("dip on the S&P 600", 2) })
        {
            var stated = expected.GetProperty("families")[place];

            Assert.Equal(stated.GetProperty("family").GetString(), family);
            Assert.Equal(stated.GetProperty("rules").GetInt32(), Count(after, family));
            Assert.Equal(stated.GetProperty("level").GetDouble(), 0.05 / Count(after, family), 12);
        }

        foreach (var family in new[] { "pullback", "breakout", "drift", "heavyweight", "drift on the S&P 400", "heavyweight on the S&P 600", "quality on the S&P 500", "quality on the S&P 400", "quality on the S&P 600" })
        {
            Assert.Equal(Count(before, family), Count(after, family));
        }
    }

    // The S&P 400 of the dip's constructed store frozen on the night of 2026-10-02 at a setting of the grid its members
    // pass, the 2-session RSI under 10 and the rest the provisional rule's but its market setting, the S&P 500's breadth
    // at 0.45, with three versions: the market variant, the stop at 3 typical moves and the time limit at 7 sessions.
    const string FrozenDip = "drop=10,trend=200,exit=5,limit=10,stop=4,market=5,volume=0";

    static readonly string[] FrozenDipVersions =
    [
        "drop=10,trend=200,exit=5,limit=10,stop=4,market=0,volume=0",
        "drop=10,trend=200,exit=5,limit=10,stop=3,market=5,volume=0",
        "drop=10,trend=200,exit=5,limit=7,stop=4,market=5,volume=0",
    ];

    static async Task<(TemporaryStore Store, string[] Rules)> FrozenDipStore(double largeBreadth)
    {
        var store = DipStore();
        var (code, said) = await RegisterVerbAt(store, new DateTimeOffset(2026, 10, 2, 21, 0, 0, TimeSpan.Zero), RegisterVerb.IndexFamily, DipRule.Name, "--index", "MID", "--parameters", FrozenDip, "--variants", string.Join(";", FrozenDipVersions));

        Assert.True(code == 0, said);

        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) "
                + FormattableString.Invariant($"VALUES ('{DipNightSession}', 2, 2, 1, {largeBreadth}, 2, 1, 0.5, 2, 1.0);"));

        var rules = FamilyRows(store, "SELECT candidate FROM candidate_register WHERE evaluator = 'dip-400' ORDER BY id;").ToArray();

        return (store, rules);
    }

    [Fact]
    public async Task TheNightKeepsTheLiveRuleAndEachVersionUnderItsOwnNameAndReadsTheMarketSettingOnBothSidesOfItsFloor()
    {
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var registerAt = new DateTimeOffset(2026, 10, 2, 22, 45, 0, TimeSpan.Zero);

        // The S&P 500's breadth at its floor of 0.45 exactly: the live rule's market setting open.
        var (open, rules) = await FrozenDipStore(0.45);

        using (open)
        {
            var outcome = (await new DipFamily(clock, open.DatabaseFile).RunAsync("night-dip", registerAt: registerAt)).Indices.Single(index => index.Index == "MID");

            Assert.Equal(4, rules.Length);
            Assert.Equal((10, 8, 5, 4), (outcome.Members, outcome.Passed, outcome.Listed, outcome.Rules));

            // The card's night drawn by the live rule, its market setting the S&P 500's breadth and open, its list as the
            // provisional rule's was over these members.
            Assert.Equal(
                [$"S&P 500 0.45|1|{rules[0]}"],
                FamilyRows(open, "SELECT market || '|' || market_open || '|' || json_extract(settings, '$.rule') FROM dip_night WHERE index_code = 'MID';"));
            Assert.Equal(["DA", "DB", "DC", "DD", "DE"], FamilyRows(open, $"SELECT ticker FROM dip_pick WHERE index_code = 'MID' AND state = '{Core.Families.FamilyList.Listed}' ORDER BY place;"));

            // Each rule's trades under its own name, the live rule's and each version's at its own stop, time limit and exit,
            // the provisional rule kept on no index a freeze stands on.
            Assert.Equal(
                [
                    $"{rules[0]}|5|4|10|5", $"{rules[1]}|5|4|10|5", $"{rules[2]}|5|3|10|5", $"{rules[3]}|5|4|7|5",
                ],
                rules.Select(rule => rule + "|" + FamilyRows(open, $"SELECT COUNT(*), MIN(risk_moves), MIN(time_limit), MIN(exit_average) FROM dip_trade WHERE rule = '{rule.Replace("'", "''", StringComparison.Ordinal)}';").Single()));
            Assert.Equal(["0"], FamilyRows(open, $"SELECT COUNT(*) FROM dip_trade WHERE rule = '{DipFamily.ProvisionalRule}';"));

            // The night's cards word each pick's gates by the live rule, its market setting the S&P 500's breadth, and the
            // card's funnel stands under the live rule's name.
            open.Execute($"INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES ('MID', '{DipNightSession}', 10, 0.5, 1, '{Worker.Indices.IndexFamilies.Settings("MID")}', 0);");
            await new Worker.Cards.DecisionCards(clock, open.DatabaseFile).RunAsync("night-dip-cards");
            await new Worker.Cards.RuleCards(clock, open.DatabaseFile).RunAsync("night-dip-rules", [], new DateTimeOffset(2026, 10, 2, 23, 0, 0, TimeSpan.Zero));

            Assert.Contains("market, its own setting, the S&P 500's breadth at 45 per cent", System.Text.RegularExpressions.Regex.Unescape(FamilyRows(open, "SELECT lines FROM decision_card WHERE index_code = 'MID' AND family = 'dip' AND ticker = 'DA';").Single()), StringComparison.Ordinal);
            Assert.Equal([$"{rules[0]}|1|5"], FamilyRows(open, "SELECT rule, evaluated, listed FROM rule_night WHERE index_code = 'MID' AND family = 'dip';"));
        }

        // A breadth of 0.4499 under the floor closes the live rule's list and every member reads its market setting closed,
        // while the market variant, reading none, lists the same five.
        var (closed, same) = await FrozenDipStore(0.4499);

        using (closed)
        {
            var outcome = (await new DipFamily(clock, closed.DatabaseFile).RunAsync("night-dip", registerAt: registerAt)).Indices.Single(index => index.Index == "MID");

            Assert.Equal((0, 0), (outcome.Passed, outcome.Listed));
            Assert.Equal(["S&P 500 0.45|0"], FamilyRows(closed, "SELECT market || '|' || market_open FROM dip_night WHERE index_code = 'MID';"));
            Assert.Equal([$"10|{DipRule.MarketClosed}"], FamilyRows(closed, "SELECT COUNT(*) || '|' || MIN(reason) FROM dip_result WHERE index_code = 'MID';"));
            Assert.Equal(["0", "5", "0", "0"], same.Select(rule => FamilyRows(closed, $"SELECT COUNT(*) FROM dip_trade WHERE rule = '{rule.Replace("'", "''", StringComparison.Ordinal)}';").Single()));
        }
    }

    // A rule of the family registered at a version the code no longer carries is not kept, and where it is the card's
    // rule the card's night names why and lists nothing, its versions kept beside it.
    [Fact]
    public async Task ADipRuleWhoseCodeMovedIsNotKeptAndTheCardsNightNamesWhy()
    {
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        using var store = DipStore();

        // The live rule written at a version the code no longer carries and its three versions at the one it does, as a
        // registration made before the code moved would stand beside ones made after it.
        string[] rules = ["the live S&P 400 dip rule at an older version", .. FrozenDipVersions.Select((_, at) => FormattableString.Invariant($"the S&P 400 dip rule version {at + 1}"))];
        string[] versions = ["aaaaaaaaaaaa", .. FrozenDipVersions.Select(_ => CandidateEvaluators.Find("dip-400")!.Version)];
        string[] settings = [FrozenDip, .. FrozenDipVersions];

        for (var at = 0; at < rules.Length; at++)
        {
            store.Execute(
                "INSERT INTO candidate_register (candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
                $"('{rules[at]}', 'the rule', 'the test', 'dip-400', '{JsonSerializer.Serialize(Dials(settings[at]))}', '{versions[at]}', 'registered', NULL, '2026-10-02T21:00:00Z', NULL);");
        }

        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) "
                + $"VALUES ('{DipNightSession}', 2, 2, 1, 0.5, 2, 1, 0.5, 2, 1.0);");

        var outcome = (await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip", registerAt: new DateTimeOffset(2026, 10, 2, 22, 45, 0, TimeSpan.Zero))).Indices.Single(index => index.Index == "MID");

        Assert.Equal(DipFamily.Moved, outcome.Fault);
        Assert.Equal([DipFamily.Moved], FamilyRows(store, "SELECT fault FROM dip_night WHERE index_code = 'MID';"));
        Assert.Equal(["0", "5", "5", "5"], rules.Select(rule => FamilyRows(store, $"SELECT COUNT(*) FROM dip_trade WHERE rule = '{rule}';").Single()));
    }

    // The monthly run reads the family on an index only once a rule of it stands there when the run begins: before the
    // freeze none, after it each rule walked at its own setting, the live rule first and each version in the order it was
    // registered, in risks, its time limit and the next open the sessions a block waits, and none on an index no freeze
    // stands on.
    [Fact]
    public async Task TheMonthlyRunReadsTheDipFromItsNextRunAfterTheFreeze()
    {
        using var store = await RegisterAfterTheQualityFreezes();
        var registrar = new CandidateRegistrar(FixedClock.At(DipFrozenAt.AddDays(7), SessionZones.UnitedStates), store.DatabaseFile);
        var before = ShadowColumn.StandingAt(await registrar.RowsAsync(), DipFrozenAt.AddDays(7));

        Assert.False(WalkForwardTester.ReadsDip(before, "MID"));
        Assert.Empty(RuleTests.Dip(before, "MID", _ => []));

        var (code, said) = await RegisterVerbAt(store, DipFrozenAt, Arguments(DipRegisterLines()[1])[1..]);

        Assert.True(code == 0, said);

        var rows = await registrar.RowsAsync();

        Assert.False(WalkForwardTester.ReadsDip(ShadowColumn.StandingAt(rows, DipFrozenAt.AddSeconds(-1)), "MID"));

        var standing = ShadowColumn.StandingAt(rows, DipFrozenAt.AddDays(7));

        Assert.True(WalkForwardTester.ReadsDip(standing, "MID"));
        Assert.False(WalkForwardTester.ReadsDip(standing, "SML"));

        // Each rule's trades a stand-in figure of its own stop, so each walked rule is shown to read its own setting.
        var walked = RuleTests.Dip(standing, "MID", parameters => [(0, parameters[DipCandidate.StopParameter] / 10)]).ToArray();

        Assert.Equal(4, walked.Length);
        Assert.Equal([RuleTests.Live, RuleTests.Variant, RuleTests.Variant, RuleTests.Variant], walked.Select(rule => rule.Standing));
        Assert.All(walked, rule => Assert.Equal((DipRule.Name, LoopProcedures.Risks, false), (rule.Family, rule.Unit, rule.Book)));
        Assert.Equal([6, 6, 6, 6], walked.Select(rule => rule.Cap));
        Assert.Equal([0.3, 0.3, 0.4, 0.3], walked.Select(rule => Assert.Single(rule.Trades!).Edge!.Value));
        Assert.Equal(new DipCandidate("MID").SettingOf(Dials(DipFreezes[1].Live)).WordsOn("MID"), walked[0].Words);
        Assert.Empty(RuleTests.Dip(standing, "SML", _ => []));
    }

    // From 22.3 the command records a dip sweep's answer, the setting it would run with its figures, its settings and
    // those meeting the floors against luck, and its tester's verdict, where it refused a dip run before.
    [Fact]
    public async Task TheCommandRecordsADipSweepsAnswerWithItsTestersVerdict()
    {
        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();
        var command = new SweepAnswers(new SweepClock(DipFrozenAt), store.DatabaseFile, sweeps.Path, new StringWriter());
        const string run = "20261024T060843Z";
        const string tester = "20261024T060946Z";
        const string setting = "drop=-3|trend=50|exit=65|limit=5|stop=3|market=5";
        double[] years = [0.01, 0.013, 0.02, 0.009, 0.03, 0.04, 0.013, -0.05];

        void Run(string name, SweepAnswer? answer, object figures)
        {
            Directory.CreateDirectory(Path.Combine(sweeps.Path, name));
            File.WriteAllText(Path.Combine(sweeps.Path, name, IndexSweepRunner.FiguresFile), JsonSerializer.Serialize(figures));

            if (answer is not null)
            {
                File.WriteAllText(Path.Combine(sweeps.Path, name, SweepAnswer.File), answer.Json());
            }
        }

        Run(run, new SweepAnswer("MID", DipRule.Name, null, true), new
        {
            index = "MID",
            family = DipRule.Name,
            afterCosts = new[] { new { Key = setting, MeetsFloors = true }, new { Key = "drop=10|trend=200|exit=5|limit=10|stop=4|market=0", MeetsFloors = false } },
            luck = new { settings = 249.8 },
            wouldRun = new { key = setting, passed = true, figures = new { Key = setting, Trades = 546, Edge = 0.024, YearEdge = years, EdgeWithoutLargest = 0.033, MeetsFloors = true } },
        });
        Run(tester, null, new
        {
            index = "MID",
            family = IndexSweepRunner.TesterOwn,
            tested = new object[] { new { Family = DipRule.Name, Proposal = OwnProcedures.SwingGrid, verdict = new { Adjusted = 0.0397 }, passed = false } },
        });

        Assert.Equal(0, await command.RecordAsync(run, tester: tester));
        Assert.Equal(
            [$"passed|{setting}|546|0.024|0.033|2|1|249.8|{tester}|0.0397|0"],
            TextRows(store, "SELECT answer || '|' || setting || '|' || trades || '|' || edge || '|' || without_largest || '|' || settings_read || '|' || meeting || '|' || luck || '|' || tester_run || '|' || adjusted || '|' || tester_passed FROM sweep_answer;"));
    }

    // The sources 22.3 writes or edits beside the dip rules' own, which they pin.
    static readonly string[] DipFreezeSources =
    [
        "src/EquityBrief.Core/Candidates/CandidateEvaluators.cs",
        "src/EquityBrief.Core/Cards/Management.cs",
        "src/EquityBrief.Worker/Dips/DipRules.cs",
        "src/EquityBrief.Worker/Candidates/RegisterVerb.cs",
        "src/EquityBrief.Worker/Cards/DecisionCards.cs",
        "src/EquityBrief.Worker/Cards/RuleCards.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Dip.cs",
        "src/EquityBrief.Worker/Sweep/SweepAnswers.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/BriefScreen.cs",
        "src/EquityBrief.Api/Reading/CardScreen.cs",
        "src/EquityBrief.Api/Reading/PaperScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Dips.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Paper.cs",
        "src/EquityBrief.Api/Reading/RuleScreen.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Dips.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
    ];

    // The dip rules' own sources, which no rule registered before them pins.
    static readonly string[] DipRuleSources =
    [
        "src/EquityBrief.Core/Candidates/DipCandidate.cs",
        "src/EquityBrief.Core/Families/DipRule.cs",
        "src/EquityBrief.Worker/Dips/DipFamily.cs",
    ];

    // No source a rule registered before the dip's freeze pins is one 22.3 writes, so no other rule's version moves, the
    // reader shown to find a source a rule pins; and the dip's rules pin their own rule and step and none of the others
    // 22.3 writes.
    [Fact]
    public void NoSourceAnotherRulesVersionPinsIsOne22Point3Writes()
    {
        var pinned = CandidateEvaluators.All
            .Where(evaluator => evaluator is not (QualityCandidate or DipCandidate))
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var dip = CandidateEvaluators.All.OfType<DipCandidate>().SelectMany(evaluator => evaluator.OwnSources).Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(DipFreezeSources.Concat(DipRuleSources), path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(DipFreezeSources.Concat(DipRuleSources).Intersect(pinned, StringComparer.Ordinal));
        Assert.Empty(DipFreezeSources.Intersect(dip, StringComparer.Ordinal));
        Assert.Subset(dip.ToHashSet(StringComparer.Ordinal), DipRuleSources.Where(path => !path.EndsWith("/DipCandidate.cs", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    // From the 22.3 correction of 2026-10-26 the night reads hooks only off the rules the S&P 500's recorder keeps: a dip
    // rule's exit dial of 65, read as a hook, names an exit the menu does not hold, and stopped the night of 2026-10-23 run
    // again at its swing filter; a breakout rule whose hook scores on the readings is still read, so the night reads them.
    [Fact]
    public void TheNightReadsHooksOffTheRecordersOwnRulesAloneSoADipsExitDialStopsNoNight()
    {
        var at = new DateTimeOffset(2026, 10, 25, 23, 27, 0, TimeSpan.Zero);

        RegisterRow Row(long id, CandidateEvaluator evaluator, IReadOnlyDictionary<string, double> parameters) =>
            new(id, "rule " + id, "rule", "test", evaluator.Name, CandidateEvaluator.Write(parameters), evaluator.Version, "registered", null, at, null);

        var dip = Row(1, new DipCandidate(MarketCloses.Index), new Dictionary<string, double>
        {
            [DipCandidate.DropParameter] = -3,
            [DipCandidate.TrendParameter] = 50,
            [DipCandidate.SellParameter] = DipRule.RsiExit,
            [DipCandidate.LimitParameter] = 10,
            [DipCandidate.StopParameter] = 3,
            [DipCandidate.MarketParameter] = 6,
            [DipCandidate.VolumeParameter] = 0,
        });
        var scored = Row(2, CandidateEvaluators.All.OfType<BreakoutCandidate>().First(), new Dictionary<string, double>
        {
            [Core.Loop.RuleHooks.ScorePrefix + Core.Ledger.LedgerReadings.All[0].Column] = 1,
        });

        // From 21.4 the dip's sell dial is no hook's name, so its rule states no hook where it states none; a registration
        // from before, naming the dial exit, is read as the same setting, and the hooks read off it as 21.4 reads them would
        // take the dial for the menu's exit, which is why the recorder reads no own-step rule's hooks.
        Assert.True(Core.Loop.RuleHooks.Of(CandidateEvaluator.Read(dip.Parameters)).IsOff);
        Assert.Equal(DipRule.RsiExit, DipCandidate.SellOf(new Dictionary<string, double> { [DipCandidate.SellParameterBefore] = DipRule.RsiExit }));
        Assert.Equal(5, DipCandidate.SellOf(new Dictionary<string, double> { [DipCandidate.SellParameter] = 5, [DipCandidate.SellParameterBefore] = DipRule.RsiExit }));
        Assert.False(EquityBrief.Worker.Families.FamilyRecorder.Keeps(dip));
        Assert.True(EquityBrief.Worker.Families.FamilyRecorder.Keeps(scored));
        Assert.False(EquityBrief.Worker.Families.FamilyRecorder.HooksReadReadings([dip]));
        Assert.True(EquityBrief.Worker.Families.FamilyRecorder.HooksReadReadings([dip, scored]));

        // The swing filter's step asks the recorder, and reads no standing rule's hooks of its own.
        var nightly = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Nightly.cs"));

        Assert.Contains("FamilyRecorder.HooksReadReadings(rules.Standing)", nightly, StringComparison.Ordinal);
        Assert.DoesNotContain("rules.Standing.Any(", nightly, StringComparison.Ordinal);
    }

    // A whole night over the fixture with the S&P 500's dip frozen at the RSI's exit runs every step: its live rule and a
    // version beside it registered before the night, the dip's step reading the live rule.
    [Fact]
    public async Task AWholeNightOverTheFixtureRunsEveryStepWithTheSAndP500sDipFrozenAtTheRsisExit()
    {
        var evaluator = new DipCandidate(MarketCloses.Index);

        static string Parameters(double stop) => CandidateEvaluator.Write(new Dictionary<string, double>
        {
            [DipCandidate.DropParameter] = -3,
            [DipCandidate.TrendParameter] = 50,
            [DipCandidate.SellParameter] = DipRule.RsiExit,
            [DipCandidate.LimitParameter] = 10,
            [DipCandidate.StopParameter] = stop,
            [DipCandidate.MarketParameter] = 0,
            [DipCandidate.VolumeParameter] = 0,
        });

        var (store, code, output, error) = await FixtureReplay.NightAsync(before: migrated =>
        {
            foreach (var (id, candidate, stop) in new[] { (1L, "the live S&P 500 dip rule", 3.0), (2L, "the S&P 500 dip rule at a stop of 4", 4.0) })
            {
                migrated.Execute(
                    "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
                    $"({id}, '{candidate}', 'rule', 'test', '{evaluator.Name}', '{Parameters(stop)}', '{evaluator.Version}', '{CandidateFamily.Registered}', NULL, '2026-09-08T12:00:00Z', NULL);");
            }
        });

        using (store)
        {
            Assert.True(code == 0, output + error);
            Assert.DoesNotContain("The exit hook names", output + error, StringComparison.Ordinal);
            Assert.Equal(["the live S&P 500 dip rule"], Query(store, "SELECT json_extract(settings, '$.rule') FROM dip_night WHERE index_code = 'GSPC';"));
        }
    }
}
