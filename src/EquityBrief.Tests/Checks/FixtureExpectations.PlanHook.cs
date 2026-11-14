using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.10: the plan a rule's registration states read from its five parameters and stated back, the plan
// stated off read as none, each malformed plan refused; a plan trade walked by hand over constructed bars, filled at its level
// within its window, its stop placed from its fill, sold where the test sells and read in the risk its rule's own plan set; one
// never filled ended at its window's last session with no result, and one still waiting left open; and a live rule given its
// plan by the register, today's plan kept beside it as a variant where its family has room and walked unregistered where not.
// see: At phase 23's restart each price family's live rule takes the plan its long test recommends where it beats today's plan from 2019 above the ruled benchmark, today's plan kept beside it as a variant
public partial class FixtureExpectations
{
    static readonly PlanCombination SecondZoneWide = new(PlanPoints.SecondZone, 20, PlanPoints.TypicalStop(4), PlanPoints.FirstBand);

    static readonly PlanCombination DistanceStaged = new(PlanPoints.DistanceEntry(2.11), 20, PlanPoints.TypicalStop(2), PlanPoints.StagedSales);

    static readonly PlanCombination PullbackTimed = new(PlanPoints.FirstPullback, PlanPoints.FirstPullbackSessions, PlanPoints.RuleStop, PlanPoints.TimeLimit);

    [Fact]
    public void APlansFiveParametersReadOneCombinationAndAPlanStatedOffReadsAsNone()
    {
        foreach (var combination in new[] { SecondZoneWide, DistanceStaged, PullbackTimed, new PlanCombination(PlanPoints.Average, 5, PlanPoints.RuleStop, PlanPoints.RuleSale), new PlanCombination(PlanPoints.FirstZone, 10, PlanPoints.ZoneStop, PlanPoints.TimeLimit) })
        {
            Assert.Equal(combination, PlanHook.Of(PlanHook.ParametersOf(combination)));
            Assert.Equal(combination, PlanPrices.Named(combination.Words));
        }

        // By hand: the second zone within 20 sessions, 4 typical days under the buy, the first band above.
        Assert.Equal(
            new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 20, [PlanHook.StopParameter] = 4, [PlanHook.SaleParameter] = 1 },
            PlanHook.ParametersOf(SecondZoneWide));

        // The plan stated off, and a rule stating none, trade the rule's own plan.
        Assert.Null(PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 0, [PlanHook.WindowParameter] = 0, [PlanHook.StopParameter] = 0, [PlanHook.SaleParameter] = 0 }));
        Assert.Null(PlanHook.Of(new Dictionary<string, double> { ["stopMoves"] = 1.5 }));

        // Each malformed plan is refused rather than read as the rule's own.
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 7, [PlanHook.WindowParameter] = 5 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 7 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 4, [PlanHook.WindowParameter] = 5, [PlanHook.StopParameter] = -1 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 5, [PlanHook.DistanceParameter] = 1 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 5, [PlanHook.StopParameter] = 3.5 }));

        // The hooks read the plan, say it in their words, and take no exit of the menu beside it.
        var hooks = RuleHooks.Of(PlanHook.ParametersOf(SecondZoneWide));

        Assert.Equal(SecondZoneWide, hooks.Plan);
        Assert.False(hooks.IsOff);
        Assert.Equal("plan: " + SecondZoneWide.Words, hooks.Words());
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(new Dictionary<string, double>(PlanHook.ParametersOf(SecondZoneWide)) { [RuleHooks.ExitParameter] = 1 }));

        // The dip's family word and its sale's level, stated where every family rule pins them, are the dip's own.
        Assert.Equal(Core.Families.DipRule.Name, PlanPoints.DipName);
        Assert.Equal(Core.Families.DipRule.RsiExit, PlanPoints.DipRsiAbove);
    }

    // Six sessions: the night's close at 100 with the rule's stop at 95, a level at 98 traded into on the second session after
    // the night, its stop 2 typical days of 1 under the fill at 96, the target of 103 closed through on the fourth and sold at
    // the fifth's open of 103.20: (103.20 - 98) / (100 - 95) = 1.04 risks.
    static PlanBars SixSessions(int count = 6) => new(
        [.. Enumerable.Range(0, count).Select(day => new DateOnly(2026, 10, 1).AddDays(day))],
        [.. new[] { 100, 99.5, 98.5, 99, 102, 103.2 }.Take(count)],
        [.. new[] { 99.5, 99, 97.5, 98.2, 100.5, 102.8 }.Take(count)],
        [.. new[] { 100, 99.2, 98.4, 101, 103.5, 103 }.Take(count)]);

    static PlanOrder OrderAt(double? level) =>
        new(DistanceStaged.Words, PlanPoints.DistanceEntry(2), level, 5, PlanPoints.TypicalStop(2), 95, null, 1, 103, [], null, null, 10);

    [Fact]
    public void APlanTradeFillsAtItsLevelSellsWhereTheTestSellsAndReadsInItsRulesRisk()
    {
        var bars = SixSessions();
        var night = bars.Sessions[0];

        var end = PlanTrades.Ended(OrderAt(98), 100m, 95m, night, bars)!;

        Assert.Equal((bars.Sessions[2], 98.0, bars.Sessions[4], bars.Sessions[5], 103.2, PlanPoints.Reached), (end.FilledOn!.Value, end.Fill!.Value, end.On, end.SoldOn!.Value, end.Sold!.Value, end.End));
        Assert.Equal(1.04, end.Result!.Value, 9);

        // Bought at its open where a session opens under the level.
        var opensUnder = PlanTrades.Ended(OrderAt(99.8), 100m, 95m, night, bars)!;

        Assert.Equal((bars.Sessions[1], 99.5), (opensUnder.FilledOn!.Value, opensUnder.Fill!.Value));

        // A level no session trades into within its window ends unfilled on the window's last session, with no result.
        var unfilled = PlanTrades.Ended(OrderAt(90), 100m, 95m, night, bars)!;

        Assert.Equal((bars.Sessions[5], (double?)null, PlanEnd.Unfilled), (unfilled.On, unfilled.Result, unfilled.End));

        // A window the stored sessions have not passed yet is still waiting, and a filled trade not yet sold is held.
        Assert.Null(PlanTrades.Where(OrderAt(90), 100m, 95m, night, SixSessions(4)));
        Assert.Equal((bars.Sessions[2], 98.0, (PlanEnd?)null), PlanTrades.Where(OrderAt(98), 100m, 95m, night, SixSessions(4)) is { } held ? (held.FilledOn!.Value, held.Fill!.Value, held.End) : default);

        // An adjustment since the night moves every price by the same factor: the same trade over bars stored at half.
        var halved = new PlanBars(bars.Sessions, [.. bars.Opens.Select(one => one / 2)], [.. bars.Lows.Select(one => one / 2)], [.. bars.Closes.Select(one => one / 2)]);
        var scaled = PlanTrades.Ended(OrderAt(98), 100m, 95m, night, halved)!;

        Assert.Equal((98.0, 103.2), (scaled.Fill!.Value, scaled.Sold!.Value));
        Assert.Equal(1.04, scaled.Result!.Value, 9);

        // On the operator's ruling of 2026-11-14 an order its levels placed no price for is not placed: it ends unfilled on
        // its own night, before any window passes and over sessions a waiting order would still wait through, and its words
        // say no order was placed; an order at a price that never fills keeps the words it had.
        var placedNone = PlanTrades.Ended(OrderAt(null), 100m, 95m, night, SixSessions(2))!;

        Assert.Equal((night, (double?)null, (DateOnly?)null, PlanEnd.Unfilled), (placedNone.On, placedNone.Result, placedNone.FilledOn, placedNone.End));
        Assert.Equal("No price to buy at from its levels tonight, so no order was placed.", Web.Marks.PlanOrderWords.NotFilledOf(OrderAt(null)));
        Assert.Equal("not filled, no trade", Web.Marks.PlanOrderWords.NotFilledOf(OrderAt(90)));
        Assert.False(new PlanOrder(DistanceStaged.Words, PlanPoints.NextOpen, null, 0, PlanPoints.TypicalStop(2), 95, null, 1, 103, [], null, null, 10).PlacesNoPrice());
    }

    // On the operator's ruling of 2026-11-14 on sign-off F1 and F3 a rule's name is in plain words: a breakout with no range
    // ceiling says any range rather than Infinity, and one typical day or move is singular, an old plural name still read.
    [Fact]
    public void ARulesNameSaysAnyRangeAndOneTypicalDayInTheSingular()
    {
        var open = new Dictionary<string, double>(Worker.Indices.IndexRules.Provisional(IndexEvaluator("breakout-400")), StringComparer.Ordinal)
        {
            [IndexBreakoutCandidate.RangeCeilingParameter] = double.PositiveInfinity,
            [IndexBreakoutCandidate.StopMovesParameter] = 1,
        };
        var words = Worker.Indices.IndexRules.Words(IndexEvaluator("breakout-400"), open);

        Assert.Contains("times the volume, any range and the stop 1 typical move beneath", words, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", words, StringComparison.Ordinal);
        Assert.Equal("1 typical day under the buy", PlanPoints.TypicalStop(1));
        Assert.Equal("2 typical days under the buy", PlanPoints.TypicalStop(2));
        Assert.True(PlanPoints.NamesStop("1 typical days under the buy", 1));
        Assert.Equal(1, PlanPoints.DistanceOf(PlanPoints.DistanceEntry(1)));
    }

    // The test of the buy and sell points reads as traded the plan each family's live rule on the index states: the S&P 500
    // breakout's plan on the S&P 500 alone, none for the S&P 400 drift standing at its own plan, and nothing for a family with
    // no live rule on the index or for a variant.
    [Fact]
    public void ThePlanPointTestReadsAsTradedThePlanEachFamilysLiveRuleStates()
    {
        var at = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);

        RegisterRow Row(long id, string candidate, string evaluator, IReadOnlyDictionary<string, double> parameters) =>
            new(id, candidate, "a rule", "a test", evaluator, CandidateEvaluator.Write(parameters), "v", CandidateFamily.Registered, null, at.AddHours(-1), null);

        var planned = new Dictionary<string, double>(BreakoutLive, StringComparer.Ordinal);

        foreach (var (name, value) in PlanHook.ParametersOf(SecondZoneWide))
        {
            planned[name] = value;
        }

        // From the 23.10 correction of 2026-11-10 the dip's rules, kept by their own step and named for their index, are read on
        // their own index under the family the test walks, the S&P 400's stating a plan beside its dials and the S&P 500's none.
        var dials = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [DipCandidate.DropParameter] = 3, [DipCandidate.TrendParameter] = 1, [DipCandidate.SellParameter] = 0, [DipCandidate.LimitParameter] = 5,
            [DipCandidate.StopParameter] = 3, [DipCandidate.MarketParameter] = 0, [DipCandidate.VolumeParameter] = 0,
        };
        var dipPlanned = new Dictionary<string, double>(dials, StringComparer.Ordinal);

        foreach (var (name, value) in PlanHook.ParametersOf(SecondZoneWide))
        {
            dipPlanned[name] = value;
        }

        RegisterRow[] rows =
        [
            Row(1, PlanLive, "breakout", planned),
            Row(2, "the breakout rule at 1.6 times the volume", "breakout", PlanHook.ParametersOf(PullbackTimed)),
            Row(3, "the live S&P 400 drift rule within 10 sessions", "drift-400", new Dictionary<string, double> { ["windowSessions"] = 10 }),
            Row(4, "the live S&P 400 dip rule at a fall of 3 moves", "dip-400", dipPlanned),
            Row(5, "the live S&P 500 dip rule at a fall of 3 moves", "dip-500", dials),
        ];

        var large = PlanPointTest.LivePlans(rows, at, "GSPC");

        Assert.Equal(SecondZoneWide, large["breakout"]);
        Assert.False(large.ContainsKey("drift"));
        Assert.True(large.ContainsKey(PlanPoints.DipName));
        Assert.Null(large[PlanPoints.DipName]);

        var mid = PlanPointTest.LivePlans(rows, at, "MID");

        Assert.True(mid.ContainsKey("drift"));
        Assert.Null(mid["drift"]);
        Assert.False(mid.ContainsKey("breakout"));
        Assert.Equal(SecondZoneWide, mid[PlanPoints.DipName]);
        Assert.False(PlanPointTest.LivePlans(rows, at, "SML").ContainsKey(PlanPoints.DipName));
    }

    // From the 23.10 correction of 2026-11-10 an order the stock's levels placed no price for, holding no typical move, reads as no
    // buy tonight on every card, with no stop and no target and nothing sized; a placed next open's draws its own prices.
    // see: A plan that buys at a zone draws the price it waits for and how long it waits beside tonight's close on every card
    [Fact]
    public void AnOrderItsLevelsPlacedNoPriceForReadsAsNoBuyTonight()
    {
        var unplaced = PlanTrades.Unplaced(SecondZoneWide, 95, 10);
        var (zone, stop, target) = Api.Reading.CardScreen.ZoneOf(unplaced.Json(), 100m)!.Value;

        Assert.True(zone.NoBuy);
        Assert.Equal(((decimal?)null, (decimal?)null), (stop, target));

        var drawn = Api.Reading.CardScreen.View(new Api.Reading.DecisionCardRow("MID", new DateOnly(2026, 11, 10), "dip", "AAA", 1, 100m, 95m, null, "The dip on the S&P 400", "{}", "[]", null) { Plan = unplaced.Json() }, new Api.Reading.CardContext(null, [], [], true));

        Assert.True(drawn.Zone!.NoBuy);
        Assert.Equal(((decimal?)null, (decimal?)null), (drawn.Stop, drawn.Target));
        Assert.Null(drawn.Money);
    }

    // From the 23.10 correction of 2026-11-10 the card step reads the plan each family's live rule trades: a card stored before
    // its rule stated the plan is handed the order the plan places on its night from the stock's own bars, a card already holding
    // one keeps it, and a recommendation that is the traded plan is no alternative to it, so none is stored.
    // see: A watched pick on Home draws the plan the long test recommends for its kind of pick, priced on its own levels and not traded
    [Fact]
    public async Task ACardStoredBeforeItsRuleStatedThePlanIsHandedItsOrderAndTheTradedPlanIsNoAlternative()
    {
        var traded = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.TypicalStop(1), PlanPoints.TimeLimit);
        var (store, at) = await PlanRegisterAsync(0);

        using (store)
        {
            Assert.Equal(CandidateRegistrar.Registered, (await new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile).RegisterPlannedAsync(PlanLive, traded, true, "23.10's table", "plan-card")).Outcome);

            // 260 sessions rising 5 cents a day with a range of a dollar, so the typical daily move is a dollar.
            var sessions = new List<DateOnly>();

            for (var day = new DateOnly(2025, 10, 1); sessions.Count < 260; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    sessions.Add(day);
                }
            }

            var night = sessions[^1].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", sessions.Select((session, index) =>
            {
                var close = 50m + (0.05m * index);

                return FormattableString.Invariant($"('TST', '{session:yyyy-MM-dd}', '{close}', '{close + 0.5m}', '{close - 0.5m}', '{close}', 1000000, 'test', '2026-11-10T00:00:00Z')");
            })) + ";");
            store.Execute($@"
                INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, cap, plan) VALUES
                    ('GSPC', '{night}', 'breakout', 'TST', 1, '62.95', '61', '70', 'r', '{{}}', '[]', 60, NULL),
                    ('MID', '{night}', 'breakout', 'TST', 1, '62.95', '61', '70', 'r', '{{}}', '[]', 60, NULL),
                    ('GSPC', '{night}', 'drift', 'TST', 2, '62.95', '61', '70', 'r', '{{}}', '[]', 60, 'kept');
                INSERT INTO plan_point (run_id, index_code, family, through, setups, combination, later_fills, later_edge_per_pick, traded_later_edge_per_pick) VALUES
                    ('p1', 'GSPC', 'breakout', '{night}', 10, '{traded.Words}', 500, 0.05, 0.05);");

            await new EquityBrief.Worker.Cards.DecisionCards(FixedClock.At(at.AddHours(2), SessionZones.UnitedStates), store.DatabaseFile).PriceAsync(null);

            string? Column(string column, string index, string family)
            {
                using var connection = store.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {column} FROM decision_card WHERE index_code = '{index}' AND family = '{family}';";

                return command.ExecuteScalar() as string;
            }

            // The S&P 500 breakout's card: the order of the next open a typical move of a dollar under the buy, sold at its time
            // limit, set at the close of 62.95, and no alternative, its recommendation being the plan it trades.
            var order = PlanOrder.FromJson(Column("plan", "GSPC", "breakout"))!;

            Assert.Equal((traded.Words, (double?)null, 60), (order.Words, order.Level, order.Cap));
            Assert.Equal(1.0, order.TypicalDay, 6);
            Assert.Null(Column("alternative", "GSPC", "breakout"));
            Assert.Null(Api.Reading.CardScreen.ZoneOf(order.Json(), 62.95m));

            // The S&P 400 holds no live breakout rule stating a plan, and a card already holding one keeps it.
            Assert.Null(Column("plan", "MID", "breakout"));
            Assert.Equal("kept", Column("plan", "GSPC", "drift"));
        }
    }

    // A card whose family's live rule trades a plan buying at a level draws the level, the days it waits, tonight's close and
    // the plan's stop placed from the level, worked by hand: a level of 98 waiting 5 days, its stop 2 typical days of 1 under
    // it at 96, and with no target the 10 days after which it sells. A card holding no plan, or one buying at the next open,
    // draws its own prices as before.
    // see: A plan that buys at a zone draws the price it waits for and how long it waits beside tonight's close on every card
    [Fact]
    public void ACardWhosePlanBuysAtALevelIsHandedItsZoneAndOneAtTheNextOpenDrawsAsBefore()
    {
        var zoned = OrderAt(98) with { Target = null };
        var (zone, stop, target) = Api.Reading.CardScreen.ZoneOf(zoned.Json(), 100m)!.Value;

        Assert.Equal(new Web.Marks.ZoneBuy(98m, 5, 100m, 10), zone with { Sale = null });
        Assert.Equal((96m, (decimal?)null), (stop!.Value, target));

        // From the 23.10 correction of 2026-11-13 the zone carries the sale its plan names, worked from the price it waits for.
        Assert.Equal((Web.Marks.PlanSaleView.KindOf(zoned.Words), 98m, (decimal?)96m, 10), (zone.Sale!.Kind, zone.Sale.Buy, zone.Sale.Stop, zone.Sale.Cap));

        var nextOpen = new PlanOrder(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.TypicalStop(4), PlanPoints.FirstBand).Words, PlanPoints.NextOpen, null, 0, PlanPoints.TypicalStop(4), 95, null, 1, 104, [], null, null, 10);

        Assert.Null(Api.Reading.CardScreen.ZoneOf(nextOpen.Json(), 100m));
        Assert.Null(Api.Reading.CardScreen.ZoneOf(null, 100m));

        Api.Reading.DecisionCardRow Card(string? plan) =>
            new("MID", new DateOnly(2026, 11, 10), "pullback", "AAA", 1, 100m, 95m, 110m, "The pullback on the S&P 400", "{}", "[]", null) { Plan = plan };

        var drawn = Api.Reading.CardScreen.View(Card(zoned.Json()));

        Assert.Equal((zone with { Sale = null }, 96m, (decimal?)null), (drawn.Zone! with { Sale = null }, drawn.Stop!.Value, drawn.Target));

        var plain = Api.Reading.CardScreen.View(Card(null));

        Assert.Equal(((Web.Marks.ZoneBuy?)null, 95m, 110m), (plain.Zone, plain.Stop!.Value, plain.Target!.Value));
    }

    // The loop walks a rule standing at a plan under it, worked by hand over one constructed series: a listing at session 20
    // closing at 100 with its rule's stop at 95, bought at the next open of 101 under the next open at the rule's stop and the
    // time limit alone of 3 sessions, held through the closes of sessions 21 to 23 and sold at session 24's open of 104, which
    // is (104 - 101) / (100 - 95) = 0.6 risks over 4 sessions; and a plan buying at a zone the flat series draws none of ends
    // unfilled after its window with no result.
    [Fact]
    public void TheLoopWalksARuleStandingAtAPlanUnderItWorkedByHand()
    {
        var bars = Enumerable.Range(0, 30)
            .Select(day => new Core.Sweep.SweepBar(new DateOnly(2025, 1, 1).AddDays(day), 101m, 99m, 100m, 1_000, day switch { 21 => 101m, 24 => 104m, _ => 100m }))
            .ToArray();
        var sessionAt = bars.Select((bar, at) => (bar.Session, at)).ToDictionary(pair => pair.Session, pair => pair.at);
        var series = Worker.Sweep.SweepColumns.Series(new Worker.Sweep.SweepName("AAA", bars, [(null, null)], []), sessionAt);
        var read = new LoopRead("MID", null!, [series], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), [.. bars.Select(bar => bar.Session)], 0, []);
        var listing = new Worker.Sweep.FamilyListing(0, 20, 20, 0, 0, 100, 95, 110, double.NaN, 3, 2);

        var timed = new PlannedListings(read, new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.TimeLimit), "breakout");
        var (result, sessions, end) = timed.Of(listing);

        Assert.Equal((4, PlanPoints.Timed), (sessions, end));
        Assert.Equal(0.6, result!.Value, 9);

        var zoned = new PlannedListings(read, new PlanCombination(PlanPoints.FirstZone, 5, PlanPoints.RuleStop, PlanPoints.RuleSale), "breakout");

        Assert.Equal(((double?)null, 5, PlanEnd.Unfilled), zoned.Of(listing));
    }

    // A setup's benchmark leaves out a member past 20 risks either way, worked by hand: at a stop of one typical move and a
    // target of twice the risk, one member closes at 98 under its stop of 99 for -2 risks, one at 102.5 over its target of 102
    // for +2.5, and one whose prices barely move, its typical move 0.001, reaches its target at 100.03 for +30 risks; the mean
    // is (-2 + 2.5) / 2 = 0.25 over two ended, three entered, the third left out and the reading settled.
    // see: At phase 23's restart a setup's benchmark leaves out a member past 20 risks either way, as the long test's does
    [Fact]
    public void ASetupsBenchmarkLeavesOutAMemberPastTwentyRisksWorkedByHand()
    {
        var plan = new Core.Ledger.SetupPlan(1, 2, null, 3);
        (double[] Closes, int Bar, double Move, bool Stopped)[] members =
        [
            ([100, 98, 98, 98], 0, 1, false),
            ([100, 102.5, 102.5, 102.5], 0, 1, false),
            ([100, 100.03, 100.03, 100.03], 0, 0.001, false),
        ];

        var reading = Core.Ledger.SetupBenchmark.Of(members, new DateOnly(2026, 11, 10), plan);

        Assert.Equal((2, 3, 1, true), (reading.Ended, reading.Entered, reading.Stopped, reading.Settled));
        Assert.Equal(0.25, reading.Mean!.Value, 9);
        Assert.True(Core.Ledger.SetupBenchmark.Outlying(20.01));
        Assert.False(Core.Ledger.SetupBenchmark.Outlying(-20));
    }

    static readonly Dictionary<string, double> BreakoutLive = new(StringComparer.Ordinal)
    {
        ["highSessions"] = 126, ["indexAverageSessions"] = 0, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5, ["vixLookbackSessions"] = 0, ["volumeMultiple"] = 1.5,
    };

    const string PlanLive = "the live breakout rule at a 126-session high";

    static async Task<(TemporaryStore Store, DateTimeOffset At)> PlanRegisterAsync(int variants)
    {
        var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
        var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync(PlanLive, "the breakout rule", "its record", "breakout", BreakoutLive, "plan-live")).Outcome);

        for (var variant = 1; variant <= variants; variant++)
        {
            var moved = new Dictionary<string, double>(BreakoutLive, StringComparer.Ordinal) { ["volumeMultiple"] = 1.5 + (variant / 10.0) };
            var said = await new CandidateRegistrar(FixedClock.At(at.AddMinutes(variant), SessionZones.UnitedStates), store.DatabaseFile)
                .RegisterAsync(FormattableString.Invariant($"the breakout rule at {moved["volumeMultiple"]} times the volume"), "the breakout rule", "its record", "breakout", moved, FormattableString.Invariant($"plan-variant-{variant}"));

            Assert.Equal(CandidateRegistrar.Registered, said.Outcome);
        }

        return (store, at.AddHours(1));
    }

    [Fact]
    public async Task AFamilysLiveRuleIsGivenItsPlanAndTodaysPlanStandsBesideItWhereTheFamilyHasRoom()
    {
        var (store, at) = await PlanRegisterAsync(0);

        using (store)
        {
            var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);
            var given = await registrar.RegisterPlannedAsync(PlanLive, SecondZoneWide, true, "23.10's table", "plan-given");

            Assert.Equal(CandidateRegistrar.Registered, given.Outcome);

            var standing = CandidateFamily.Standing(await registrar.RowsAsync(), at);
            var live = standing.Single(row => row.Candidate == PlanLive);
            var today = standing.Single(row => row.Candidate == CandidateRegistrar.VariantOf(PlanLive, CandidateRegistrar.TodaysPlan));

            Assert.Equal(SecondZoneWide, RuleHooks.Of(CandidateEvaluator.Read(live.Parameters)).Plan);
            Assert.Null(RuleHooks.Of(CandidateEvaluator.Read(today.Parameters)).Plan);
            Assert.Equal(1.5, RuleHooks.Without(CandidateEvaluator.Read(today.Parameters))["stopMoves"]);
            Assert.Equal(2, standing.Count);
            Assert.Equal(1, (await registrar.RowsAsync()).Count(row => row.Event == CandidateFamily.Retired && row.Retires == PlanLive));

            // Where today's plan stays traded the plan stands beside the live rule as a variant, which retires nothing.
            var later = new CandidateRegistrar(FixedClock.At(at.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile);
            var beside = await later.RegisterPlannedAsync(PlanLive, PullbackTimed, false, "23.10's table", "plan-beside");

            Assert.Equal(CandidateRegistrar.Registered, beside.Outcome);

            var after = CandidateFamily.Standing(await later.RowsAsync(), at.AddHours(1));

            Assert.Equal(PullbackTimed, RuleHooks.Of(CandidateEvaluator.Read(after.Single(row => row.Candidate == CandidateRegistrar.VariantOf(PlanLive, CandidateRegistrar.PlannedBy(PullbackTimed))).Parameters)).Plan);
            Assert.Equal(1, (await later.RowsAsync()).Count(row => row.Event == CandidateFamily.Retired && row.Retires == PlanLive));
        }
    }

    [Fact]
    public async Task AFamilyHoldingNineGivesItsLiveRuleThePlanAndWalksTodaysPlanUnregistered()
    {
        var (store, at) = await PlanRegisterAsync(CandidateFamily.Maximum - 1);

        using (store)
        {
            var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);
            var given = await registrar.RegisterPlannedAsync(PlanLive, SecondZoneWide, true, "23.10's table", "plan-full");

            Assert.Equal(CandidateRegistrar.Registered, given.Outcome);
            Assert.Contains($"'{CandidateRegistrar.VariantOf(PlanLive, CandidateRegistrar.TodaysPlan)}' walked unregistered, the family keeping 9", given.Detail, StringComparison.Ordinal);
            Assert.Equal(CandidateFamily.Maximum, CandidateFamily.Standing(await registrar.RowsAsync(), at).Count);

            // A variant with no room and no plan to trade writes nothing, and a rule that is not live is refused.
            var none = await new CandidateRegistrar(FixedClock.At(at.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile).RegisterPlannedAsync(PlanLive, PullbackTimed, false, "23.10's table", "plan-none");

            Assert.Equal(CandidateRegistrar.Refused, none.Outcome);
            Assert.Equal(CandidateRegistrar.Refused, (await registrar.RegisterPlannedAsync("the breakout rule at 1.6 times the volume", SecondZoneWide, true, "23.10's table", "plan-not-live")).Outcome);
            Assert.Equal(CandidateRegistrar.Refused, (await registrar.RegisterPlannedAsync(PlanLive, PlanPoints.Combinations.First(PlanPrices.Traded), true, "23.10's table", "plan-own")).Outcome);
        }
    }
}
