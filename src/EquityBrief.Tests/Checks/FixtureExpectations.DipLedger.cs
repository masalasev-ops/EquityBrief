using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.2: the short-term dip in the setup ledger, its setups' paths through the rule's own walk and
// its benchmark of the same plan on every member worked by hand over constructed bars, its loose gates on both sides of
// their step past the widest its sweep reads, and each reading 22.2 adds to the catalogue worked by hand at a
// constructed session.
// see: The short-term dip's setups clear its drop a grid step past the widest its sweep reads with no trend condition, and the ledger's history budget is raised to hold them
// see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
public partial class FixtureExpectations
{
    // The rows 22.2 adds that this check reaches: section 17's grid of the dip's sweep and section 18's row for a market
    // series missing a session it reads.
    internal static readonly string[] DipLedgerClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The short-term dip's sweep grid"),
        CheckReach.Key(Scope.FailureTable, "A market series missing a session the dip's sweep reads"),
    ];

    // Three members over sixteen sessions, each listed on the fifth. A closes at 100 under four of 104, its move 1 and its
    // stop 96: it closes 101 under its 5-session average of 102.6, then 105 over its average of 102.8, and sells at the
    // next open of 104.5, 1.125 of its risk of 4. B closes at 50, its move 0.5 and its stop 48: it closes 47.9 under its
    // stop and sells at the next open of 47.5, -1.25 of its risk of 2. C closes at 20 and falls a cent a session, its move
    // 0.1 and its stop 19.6: it never closes over its average or under its stop, and sells at its tenth session's close of
    // 19.90, -0.25 of its risk of 0.4.
    static (double[] Closes, double[] Opens, int[] SessionAt, int Bar, double Move, bool Stopped)[] DipMembers()
    {
        double[] a = [104, 104, 104, 104, 100, 101, 105, 106, 106, 106, 106, 106, 106, 106, 106, 106];
        double[] aOpens = [.. a];
        double[] b = [50, 50, 50, 50, 50, 47.9, 47, 47, 47, 47, 47, 47, 47, 47, 47, 47];
        double[] bOpens = [.. b];
        double[] c = [.. Enumerable.Range(0, 16).Select(at => at <= 4 ? 20.0 : Math.Round(20 - (0.01 * (at - 4)), 2))];
        int[] sessionAt = [.. Enumerable.Range(0, 16)];

        aOpens[5] = 100.5;
        aOpens[7] = 104.5;
        bOpens[5] = 49.7;
        bOpens[6] = 47.5;

        return [(a, aOpens, sessionAt, 4, 1.0, false), (b, bOpens, sessionAt, 4, 0.5, false), (c, [.. c], sessionAt, 4, 0.1, false)];
    }

    [Fact]
    public void ADipSetupsPathAndItsBenchmarkOfTheSamePlanOnEveryMemberAreWorkedByHand()
    {
        var members = DipMembers();

        // Each member's path at the plan: its anchor the close less 4 of its typical moves, no target, the time limit's 10
        // sessions its cap; sold at the next open after its trigger's close, counted to the sale, or at its last close.
        var paths = members.Select(member => DipPaths.Replay(member.Closes, member.Opens, member.SessionAt, member.Bar, DipPaths.Anchor(LedgerDay, member.Closes[member.Bar], member.Move)!)).ToArray();

        Assert.Equal(new SetupAnchor(LedgerDay, 100, 96, null, null, 10, 4), DipPaths.Anchor(LedgerDay, 100, 1));
        Assert.Equal(new SetupOutcome(1.125, 3, SetupEnds.Exit), paths[0]);
        Assert.Equal(new SetupOutcome(-1.25, 2, SetupEnds.Stop), paths[1]);
        Assert.Equal(-0.25, paths[2].Result!.Value, 12);
        Assert.Equal((10, SetupEnds.Cap), (paths[2].Sessions, paths[2].End));

        // The benchmark at the close is their mean, -0.125, settled; at the open each moves by its own gap over its own
        // risk, 0.5 over 4, -0.3 over 2 and -0.01 over 0.4, a mean of -0.05 over 3 taken off.
        var (reading, listed) = DipPaths.WithMembers(members, LedgerDay);

        Assert.Equal(-0.125, reading.Mean!.Value, 12);
        Assert.Equal((3, 3, 0, true), (reading.Ended, reading.Entered, reading.Stopped, reading.Settled));
        Assert.Equal(-0.125 + (0.05 / 3), TradeFills.Benchmark(reading.Mean, listed).Mean!.Value, 12);

        // A fourth member whose bars skip the session after its listing holds its path open, so the benchmark is not
        // settled; where its prices stopped there it is left out and the benchmark settles on the other three.
        double[] d = [30, 30, 30, 30, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41];
        int[] skipped = [.. Enumerable.Range(0, 16).Select(at => at <= 4 ? at : at + 1)];
        var open = DipPaths.WithMembers([.. members, (d, d, skipped, 4, 1.0, false)], LedgerDay).Reading;
        var left = DipPaths.WithMembers([.. members, (d, d, skipped, 4, 1.0, true)], LedgerDay).Reading;

        Assert.Equal(new SetupOutcome(null, 0, SetupEnds.Open), DipPaths.Replay(d, d, skipped, 4, DipPaths.Anchor(LedgerDay, 30, 1)!));
        Assert.Equal((3, 4, false), (open.Ended, open.Entered, open.Settled));
        Assert.Equal(-0.125, left.Mean!.Value, 12);
        Assert.Equal((1, true), (left.Stopped, left.Settled));

        // A member whose move places its stop at or under nothing is not entered.
        Assert.Null(DipPaths.Anchor(LedgerDay, 3, 1));
    }

    [Fact]
    public void TheDipsLooseGatesAreWorkedOnBothSidesOfTheirStepPastTheWidestItsSweepReads()
    {
        // The 2-session RSI under 20, a step past the sweep's widest floor of 15, or a fall of at least one typical move,
        // a step past its least fall of two, whatever the trend; neither read passes nothing.
        Assert.True(SetupGates.Dip(19.99, double.NaN));
        Assert.False(SetupGates.Dip(20, double.NaN));
        Assert.True(SetupGates.Dip(null, 1));
        Assert.False(SetupGates.Dip(null, 0.9999));
        Assert.True(SetupGates.Dip(50, 1));
        Assert.False(SetupGates.Dip(null, double.NaN));
        Assert.Equal((20.0, 1.0, 15.0, 2.0), (SetupGates.DipRsiUnder, SetupGates.DipFallMoves, IndexSweepRunner.DipDrops.Where(drop => drop > 0).Max(), -IndexSweepRunner.DipDrops.Where(drop => drop < 0).Max()));
    }

    [Fact]
    public void EachDipReadingIsWorkedByHandAtAConstructedSession()
    {
        // Three S&P 400 members of one sector over 300 days, every bar's range a point either side of its close and a
        // million shares traded. A rises a tenth a day to the drop, falls 0.3 and then 3 over its last two sessions on two
        // million shares each; B rises the same and falls 1 and then 1; C rises the same and does not fall.
        var calendar = Enumerable.Range(0, 300).Select(at => new DateOnly(2025, 1, 1).AddDays(at)).ToArray();
        var at = calendar.Length - 1;
        var sessionAt = calendar.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);

        SweepBar[] Bars(double first, double second)
        {
            var bars = new SweepBar[calendar.Length];

            for (var day = 0; day < calendar.Length; day++)
            {
                var close = day < at - 1 ? 100 + (0.1 * day) : day == at - 1 ? 100 + (0.1 * (at - 2)) - first : 100 + (0.1 * (at - 2)) - first - second;
                var price = Core.Prices.Statistic.ToPrice(close);

                bars[day] = new SweepBar(calendar[day], price + 1m, price - 1m, price, day >= at - 1 && first + second > 0 ? 2_000_000 : 1_000_000, price, price);
            }

            return bars;
        }

        var series = new[]
        {
            SweepColumns.Series(new SweepName("A", Bars(0.3, 3), [(null, null)], []), sessionAt),
            SweepColumns.Series(new SweepName("B", Bars(1, 1), [(null, null)], []), sessionAt),
            SweepColumns.Series(new SweepName("C", Bars(0, 0), [(null, null)], []), sessionAt),
        };
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var closes = series.Select(one => one.Bars.Select(bar => Core.Prices.Statistic.FromPrice(bar.Close)).ToArray()).ToArray();
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);
        var day = calendar[at];

        // The market: IJH at 50 for 199 days and 60 on the session, over its average of 50.05; the S&P 500's index quoted on
        // every day but the fifth before the session, which the VIX quotes at 9999; and the VIX at its day's place plus 10
        // otherwise and 400 on the session, so the 252 days the index is quoted before it read 56 to 308 less 304, a median
        // of 181.5, where a median taking the session's own 400 in place of the oldest 56 would read 182.5.
        var indexDays = calendar.Where((_, place) => place != at - 5).ToArray();
        var market = new LedgerMarket(new Dictionary<string, IReadOnlyList<(DateOnly, double)>>(StringComparer.Ordinal)
        {
            [LedgerMarket.Mid] = [.. calendar.Skip(at - 199).Select(session => (session, session == day ? 60.0 : 50.0))],
            [LedgerMarket.Index] = [.. indexDays.Select(session => (session, 4000.0))],
            [LedgerMarket.Vix] = [.. calendar.Select((session, place) => (session, place == at ? 400.0 : place == at - 5 ? 9999.0 : place + 10.0))],
        });
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal)
        {
            ["A"] = [new FiledFactRow("EntityCommonStockSharesOutstanding", day.AddDays(-40), day.AddDays(-40), 1_000_000m, day.AddDays(-30), "10-Q", "0000000001-25-000001")],
        };
        var context = new LedgerContext(new Dictionary<string, IReadOnlyList<FiledIncome>>(), new Dictionary<string, string?> { ["A"] = "Energy", ["B"] = "Energy", ["C"] = "Energy" }, highs, lows, facts)
        {
            LargeBreadth = new Dictionary<DateOnly, double> { [day] = 0.55 },
        };
        var read = LedgerSetups.Readings("MID", 0, series[0], closes[0], at, sessions[at], members, calendar, at, market, context, series);

        double Reading(string column) => read[LedgerReadings.IndexOf(column)]!.Value;

        var move = series[0].Atr[at];
        var fallA = (closes[0][at - 3] - closes[0][at]) / move;
        var fallB = (closes[1][at - 3] - closes[1][at]) / series[1].Atr[at];
        var fallC = (closes[2][at - 3] - closes[2][at]) / series[2].Atr[at];

        // The 2-session RSI: after the steady rise the mean gain a session is 0.1 and the loss nothing; the fall of 0.3 makes
        // them 0.05 and 0.15, and the fall of 3 makes them 0.025 and 1.575, an RSI of 1.5625.
        Assert.Equal(1.5625, Reading("rsi_two"), 9);

        // The drop's fall, from a close a tenth under the rise's last, so 3.3 less 0.1 over the session's typical move; its
        // largest session, the fall of 3 over the typical move standing before it; and its volume, two sessions of two
        // million and one of a million over a million.
        Assert.Equal(3.2 / move, Reading("drop_fall"), 9);
        Assert.Equal(fallA, Reading("drop_fall"), 12);
        Assert.Equal(3 / series[0].Atr[at - 1], Reading("drop_largest"), 9);
        Assert.Equal(5.0 / 3, Reading("drop_volume"), 12);

        // Against its sector: the median of A's, B's and C's falls, each in its own moves, is B's, which falls between.
        Assert.True(fallC < fallB && fallB < fallA);
        Assert.Equal(fallA - fallB, Reading("drop_against_sector"), 12);

        // The S&P 500's breadth on an S&P 400 row; the fund over its 200-session average; the VIX over its median, the day
        // the index is not quoted passed over; and the value, a million shares at the session's close.
        Assert.Equal(0.55, Reading("large_breadth"), 12);
        Assert.Equal(60 / ((50.0 * 199 + 60) / 200), Reading("fund_over_long"), 12);
        Assert.Equal(400 / 181.5, Reading("vix_over_median"), 12);
        Assert.Equal(Math.Log10(1_000_000 * closes[0][at]), Reading("company_value"), 9);

        // On an S&P 500 row no S&P 500 breadth is read, and without the members' series no fall against the sector.
        var large = LedgerSetups.Readings("GSPC", 0, series[0], closes[0], at, sessions[at], members, calendar, at, market, context);

        Assert.Null(large[LedgerReadings.IndexOf("large_breadth")]);
        Assert.Null(large[LedgerReadings.IndexOf("drop_against_sector")]);

        // The nine stand at the catalogue's end in this order, after the release readings of 20.8.
        Assert.Equal(
            ["rsi_two", "drop_fall", "drop_largest", "drop_volume", "drop_against_sector", "large_breadth", "fund_over_long", "vix_over_median", "company_value"],
            LedgerReadings.All.TakeLast(9).Select(reading => reading.Column));
    }

    // Three S&P 400 members of one sector over 270 weekdays, each drifting at its own pace with a weekly wobble that drops
    // it four points on every fifth session, and the index, IJH and the VIX moving every session, so a reading taken past
    // its session would read another figure. BBB falls a fifth of a point a session, so its wobble's drop takes its 2-session
    // RSI under 20 on the 265th, a dip setup.
    [Fact]
    public void ADipSetupsReadingsRebuiltByItsOwnGatesOverTheHistoryCutAtItsSessionAreTheStoredOnes()
    {
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2025, 1, 6); calendar.Count < 270; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                calendar.Add(day);
            }
        }

        SweepName Member(string ticker, decimal start, decimal step) =>
            new(
                ticker,
                [.. calendar.Select((day, at) => start + (step * at) + ((at % 5) - 2)).Select((close, at) => new SweepBar(calendar[at], close + 1, close - 1, close, 1_000_000 + (at * 1_000), close, close))],
                [(new DateOnly(2020, 1, 2), null)],
                []);

        var inputs = new SweepHistoryInputs(calendar[^1], [.. calendar], [Member("AAA", 100, 0.5m), Member("BBB", 80, -0.2m), Member("CCC", 50, 0.1m)], 0, 0, 0, 0, "constructed");
        var market = new LedgerMarket(new Dictionary<string, IReadOnlyList<(DateOnly Session, double Close)>>(StringComparer.Ordinal)
        {
            [LedgerMarket.Index] = [.. calendar.Select((day, at) => (day, 4000.0 + (at * 3)))],
            [LedgerMarket.Mid] = [.. calendar.Select((day, at) => (day, 300.0 + (at * 0.2) + (at % 3)))],
            [LedgerMarket.Vix] = [.. calendar.Select((day, at) => (day, 15.0 + (at % 7)))],
        });
        var sectors = new Dictionary<string, string?>(StringComparer.Ordinal) { ["AAA"] = "Energy", ["BBB"] = "Energy", ["CCC"] = "Energy" };
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal)
        {
            ["BBB"] = [new FiledFactRow("EntityCommonStockSharesOutstanding", calendar[10], calendar[10], 2_000_000m, calendar[20], "10-Q", "0000000002-25-000001")],
        };
        var breadth = calendar.Select((day, at) => (day, 0.4 + (0.001 * at))).ToDictionary(pair => pair.day, pair => pair.Item2);
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var columns = SweepColumns.Sessions(series, inputs.Sessions);
        var members = SweepBenchmark.On(series, calendar.Count);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Count);
        var at = 265;
        var context = new LedgerContext(NoIncome, sectors, highs, lows, facts) { Ranks = new FactorRanks(series, members), LargeBreadth = breadth };
        var stored = LedgerSetups.On("MID", series, columns, members, calendar, at, market, context)
            .Single(setups => setups.Family == DipRule.Name)
            .Rows
            .Single(row => row.Ticker == "BBB");

        Assert.True(stored.Reading("rsi_two") < SetupGates.DipRsiUnder);

        // Rebuilt by the dip's own gates over the history cut at its session, every reading the same, each of the nine 22.2
        // adds a figure.
        var cut = LedgerPointInTime.Cut(inputs, calendar[at]);
        var cutMarket = LedgerPointInTime.Cut(market, calendar[at]);
        var (held, rebuilt) = LedgerPointInTime.Setup("MID", cut, cutMarket, NoIncome, sectors, facts, DipRule.Name, "BBB", largeBreadth: breadth);

        Assert.True(held);
        Assert.Empty(LedgerPointInTime.Differences(stored.Readings, rebuilt!));
        Assert.All(LedgerReadings.All.TakeLast(9), reading => Assert.NotNull(rebuilt![LedgerReadings.IndexOf(reading.Column)]));

        // A stored VIX reading a hundredth over the one the cut reads is named with both figures, and nothing else is.
        var off = (double?[])stored.Readings.Clone();

        off[LedgerReadings.IndexOf("vix_over_median")] = stored.Reading("vix_over_median") + 0.01;

        Assert.Equal([("vix_over_median", off[LedgerReadings.IndexOf("vix_over_median")], rebuilt![LedgerReadings.IndexOf("vix_over_median")])], LedgerPointInTime.Differences(off, rebuilt));

        // The session after holds no dip of BBB, its wobble rising there.
        Assert.Equal((true, (double?[]?)null), LedgerPointInTime.Setup("MID", LedgerPointInTime.Cut(inputs, calendar[at + 1]), LedgerPointInTime.Cut(market, calendar[at + 1]), NoIncome, sectors, facts, DipRule.Name, "BBB", largeBreadth: breadth));
    }

    // Three S&P 500 members over 260 weekdays, rising from 60 to 100 on a point's range either side and a million shares a
    // session. On the last CCC closes at 95 instead, a fall of about five after a steady rise: its 2-session RSI about 3,
    // under the provisional rule's 10, its close above its 200-session average of about 84.5 and no report on file, so
    // its dip setup is one the provisional rule passes, and the dip's own list picked it that night. AAA and BBB rise on,
    // their 2-session RSI 100, and hold no dip setup.
    [Fact]
    public async Task ADipSetupOnTheNightCarriesTheProvisionalRulesPassAndTheDipsOwnPick()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2025, 6, 2); sessions.Count < 260; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                sessions.Add(day);
            }
        }

        var night = sessions[^1];
        var statements = new List<string>();

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            statements.Add($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2020-01-02', NULL, '2020-01-02T21:00:00Z');");

            for (var at = 0; at < sessions.Count; at++)
            {
                var close = ticker == "CCC" && at == sessions.Count - 1 ? 95m : Math.Round(60m + (40m * at / (sessions.Count - 1)), 2);
                var stamp = Stamp(sessions[at]);

                statements.Add(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) "
                    + FormattableString.Invariant($"VALUES ('{ticker}', '{stamp}', '{close}', '{close + 1m}', '{close - 1m}', '{close}', 1000000, 'constructed', '{stamp}T21:00:00Z', '{close}');"));
            }
        }

        statements.Add($"INSERT INTO dip_pick (index_code, session_date, ticker, state, place, listed_under, held_index, held_family) VALUES ('GSPC', '{Stamp(night)}', 'CCC', 'listed', 1, NULL, NULL, NULL);");
        Batch(store, statements);

        var outcome = await new SetupLedger(Core.Time.FixedClock.At(new DateTimeOffset(night.ToDateTime(new TimeOnly(21, 10)), TimeSpan.Zero), Core.Time.SessionZones.UnitedStates), store.DatabaseFile).NightAsync("night-dip-pick");

        Assert.Equal(night, outcome.Session);
        Assert.All(outcome.Indices, index => Assert.Null(index.Fault));
        Assert.Equal([$"CCC|{Stamp(night)}|1|1"], Query(store, "SELECT ticker || '|' || session_date || '|' || live_pass || '|' || picked FROM setup WHERE family = 'dip';"));
        Assert.True(double.Parse(Query(store, "SELECT rsi_two FROM setup WHERE family = 'dip';").Single(), System.Globalization.CultureInfo.InvariantCulture) < DipRule.Provisional.RsiUnder);
    }

    // The sources 22.2 writes, the ledger's, the sweep's, the tester's procedure, the dip's rule and the Ledger page's.
    internal static readonly string[] DipLedgerSources =
    [
        "src/EquityBrief.Core/Families/DipRule.cs",
        "src/EquityBrief.Core/Ledger/DipPaths.cs",
        "src/EquityBrief.Core/Ledger/LedgerHeadings.cs",
        "src/EquityBrief.Core/Ledger/LedgerReadings.cs",
        "src/EquityBrief.Core/Ledger/SetupAnchor.cs",
        "src/EquityBrief.Core/Ledger/SetupGates.cs",
        "src/EquityBrief.Worker/Ledger/LedgerPointInTime.cs",
        "src/EquityBrief.Worker/Ledger/LedgerSetups.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.NextOpen.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.cs",
        "src/EquityBrief.Worker/Loop/LoopReadings.cs",
        "src/EquityBrief.Worker/Loop/OwnProcedures.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Dip.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.OwnSwing.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
    ];

    [Fact]
    public void NoSourceARulesVersionPinsIsOne22Point2WritesSoNoExistingRulesVersionMoves()
    {
        // Every source an evaluator pins, its own and the shared evaluation's, and the hooks'.
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(DipLedgerSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(DipLedgerSources.Intersect(pinned, StringComparer.Ordinal));

        // The reader is shown to find a source both lists hold.
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheCompanysValueIsReadAgainWithTheValuationReadingOnceTheRefreshStoresAFiling()
    {
        using var store = ValuationStore(newestFiled: false);
        var ledger = new SetupLedger(Core.Time.FixedClock.At(new DateTimeOffset(2026, 10, 9, 23, 30, 0, TimeSpan.Zero), Core.Time.SessionZones.UnitedStates), store.DatabaseFile);
        double Value() => double.Parse(Query(store, "SELECT company_value FROM setup WHERE family = 'breakout' AND ticker = 'AAA';").Single(), System.Globalization.CultureInfo.InvariantCulture);

        // Before the refresh the newest count AAA's filings state stands for 2026-04-20, eight shares on a close as traded
        // of 100 at the store's scale of 100, so the night's close of 115 is 115 on its basis, a value of 920.
        var first = await ledger.NightAsync("night-company-value");

        Assert.All(first.Indices, index => Assert.Null(index.Fault));
        Assert.Equal(Math.Log10(920), Value(), 12);

        // The refresh stores the newest quarter, its cover stating ten shares for 2026-07-20, filed before the night, and the
        // night's setup reads the value again over that count, 1,150.
        var day = ValuationEnd(0).AddDays(20);

        Batch(store,
        [
            FactRow(new FiledFactRow("NetIncomeLoss", ValuationStart(0), ValuationEnd(0), 1m, ValuationEnd(0).AddDays(30), "10-Q", "q-0")),
            FactRow(new FiledFactRow(FiledFacts.SharesOutstanding[0], day, day, 10m, ValuationEnd(0).AddDays(30), "10-Q", "q-0")),
        ]);

        Assert.Equal(1, await ledger.BusinessAgainAsync(ValuationNight, ["AAA"]));
        Assert.Equal(Math.Log10(1150), Value(), 12);
    }
}
