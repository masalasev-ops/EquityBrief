using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.9: graded exposure as a variant. The floor each rule's exposure grades at is read off its
// market check by hand, the exposure worked at that floor at both switches on both sides and at each level over a
// constructed night; a paper account sizes a night's trade at half where its exposure held one switch, worked by hand;
// and the remedy's lines register it over a register shaped as the operator's where the family has room and the rule
// reads a market check, refusing elsewhere.
// see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
public partial class FixtureExpectations
{
    const string ExposureRemedy = "2026-10-30-21.9-the-graded-exposures.txt";

    // The row 21.9 adds that this check reaches: section 17's graded exposure variant.
    internal static readonly string[] ExposureClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The graded exposure variant"),
    ];

    static IndexRuleCandidate IndexEvaluatorOf(string family, string index) =>
        CandidateEvaluators.All.OfType<IndexRuleCandidate>().Single(one => one.SetupFamily == family && one.Index == index);

    // An S&P 400 or 600 rule's provisional setting with its market check as given.
    static Dictionary<string, double> CheckedAt(string family, string index, double largeBreadth, double breadthFloor) =>
        new(IndexRules.Provisional(IndexEvaluatorOf(family, index)), StringComparer.Ordinal)
        {
            [IndexRuleCandidate.LargeBreadthParameter] = largeBreadth,
            [IndexRuleCandidate.BreadthFloorParameter] = breadthFloor,
        };

    static double?[] SwitchReadings(double? breadth, double? vixOverMedian)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[RuleHooks.BreadthColumn] = breadth;
        readings[RuleHooks.VixOverMedianColumn] = vixOverMedian;

        return readings;
    }

    [Fact]
    public void TheGradedExposureIsWorkedByHandAtTheRulesOwnFloorAtBothSwitchesOnBothSidesAndAtEachLevel()
    {
        // The floor each rule grades at, its market check's: an S&P 400 or 600 rule's own floor on its index's breadth, the
        // S&P 500's floor of 0.45 where it reads the S&P 500's breadth, none where it reads no check; the S&P 500's
        // families at the market floor their sweep reads.
        Assert.Equal(0.40, ExposureProcedures.FloorOf(IndexEvaluatorOf(BreakoutRule.Name, "SML"), CheckedAt(BreakoutRule.Name, "SML", 0, 0.40)));
        Assert.Equal(0.45, ExposureProcedures.FloorOf(IndexEvaluatorOf(DriftRule.Name, "MID"), CheckedAt(DriftRule.Name, "MID", 0, 0.45)));
        Assert.Equal(IndexRuleCandidate.LargeBreadthFloor, ExposureProcedures.FloorOf(IndexEvaluatorOf(BreakoutRule.Name, "MID"), CheckedAt(BreakoutRule.Name, "MID", 1, 0.35)));
        Assert.Null(ExposureProcedures.FloorOf(IndexEvaluatorOf(DriftRule.Name, "SML"), CheckedAt(DriftRule.Name, "SML", IndexRuleCandidate.NoMarketCheck, 0)));
        Assert.Equal(FamilySweep.MarketFloor, ExposureProcedures.FloorOf(new BreakoutCandidate(), BreakoutCandidate.ParametersOf(BreakoutRule.Live)));
        Assert.Equal(0.45, FamilySweep.MarketFloor, 12);

        // At a floor of 0.40: the breadth at 0.40 holds and 0.3999 does not, the VIX at its median holds and a hair over
        // does not; five picks where both hold, two where one, none where neither. Over seven candidates in the rule's
        // own order the first five, the first two, or none.
        var hooks = RuleHooks.Of(ExposureProcedures.Exposed(LoopChange.NoHooks, 0.40));
        string[] seven = ["A", "B", "C", "D", "E", "F", "G"];

        Assert.Equal(
            [5, 2, 2, 0],
            new[] { SwitchReadings(0.40, 1.0), SwitchReadings(0.3999, 1.0), SwitchReadings(0.40, 1.0001), SwitchReadings(0.3999, 1.0001) }.Select(readings => hooks.PicksUnder(readings)!.Value));
        Assert.Equal(["A", "B", "C", "D", "E"], hooks.Order(seven, _ => SwitchReadings(0.41, 0.9)));
        Assert.Equal(["A", "B"], hooks.Order(seven, _ => SwitchReadings(0.39, 0.9)));
        Assert.Empty(hooks.Order(seven, _ => SwitchReadings(0.39, 1.1)));

        // The exposure's words name the floor its market check reads, as the variant's name and its chip carry them.
        Assert.Equal(hooks.Words(), ExposureProcedures.WordsOf(0.40));
        Assert.Contains("0.4", ExposureProcedures.WordsOf(0.40), StringComparison.Ordinal);
        Assert.Equal(ExposureVariant.Proposal, ExposureProcedures.Proposal);
        Assert.Equal([SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, DipRule.Name], ExposureProcedures.Families);
    }

    [Fact]
    public void APaperAccountSizesANightsTradeAtHalfWhereItsExposureHeldOneSwitchWorkedByHand()
    {
        // An account of $100,000 risking 1 per cent a trade and at most a fifth in one stock. AAA bought at 20 over a stop at
        // 18 on a full night: $1,000 at risk over $2 a share, 500 shares, under the fifth's 1,000. BBB the same plan the next
        // session on a half night: the account still worth $100,000 at cost, half of 1 per cent at risk, 250 shares.
        var account = new AccountSettings(100_000m, 1m, AccountSettings.ProposedCap);
        DateOnly[] sessions = [new(2026, 11, 2), new(2026, 11, 3)];
        PaperTrade[] trades =
        [
            new("AAA", sessions[0], 20m, 18m, null, null, null, 1),
            new("BBB", sessions[1], 20m, 18m, null, null, null, 1, Halved: true),
        ];
        var walked = PaperAccount.Walk(account, trades, sessions, (_, _) => null);

        Assert.Equal([500L, 250L], walked.Trades.Select(one => one.Shares));
        Assert.Equal((1m, 0.5m), (trades[0].RiskShare, trades[1].RiskShare));

        // Which nights are half nights, at a floor of 0.40: the breadth short and the VIX under its median, one switch, half;
        // both, full; the breadth held and the VIX over, one, half; neither, none listed and so none sized; a night whose
        // readings are not held, whole.
        var exposure = RuleHooks.Of(ExposureProcedures.Exposed(LoopChange.NoHooks, 0.40));
        var nights = new Dictionary<DateOnly, (double? Breadth, double? VixOverMedian)>
        {
            [new(2026, 11, 2)] = (0.39, 0.9),
            [new(2026, 11, 3)] = (0.41, 0.9),
            [new(2026, 11, 4)] = (0.41, 1.1),
            [new(2026, 11, 5)] = (0.39, 1.1),
        };

        Assert.Equal(
            [true, false, true, false, false],
            new DateOnly[] { new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4), new(2026, 11, 5), new(2026, 11, 6) }.Select(night => PaperScreen.HalvedOn(exposure, nights, night)));

        // The account of a rule standing at the exposure has its trades marked, by the rule its words name; another rule's
        // account is left whole; and the trade log carries the mark.
        const string Graded = "the S&P 400 drift rule at its provisional setting, graded";
        CandidateRow[] register =
        [
            new(1, Graded, "drift-400", "registered", null, DateTimeOffset.UnixEpoch, CandidateEvaluator.Write(ExposureProcedures.Exposed(CheckedAt(DriftRule.Name, "MID", 0, 0.40), 0.40))),
            new(2, "the S&P 400 drift rule at its provisional setting", "drift-400", "registered", null, DateTimeOffset.UnixEpoch, CandidateEvaluator.Write(CheckedAt(DriftRule.Name, "MID", 0, 0.40))),
        ];
        PaperAccountSource Source(string words) => new("key-" + words.Length, DriftRule.Name, "variant", "V", words, words, false, [(new PaperTrade("AAA", new(2026, 11, 3), 20m, 18m, null, null, null, 1, Listed: new DateOnly(2026, 11, 2)), null)], null);
        var marked = PaperScreen.Halved([Source(Graded), Source(register[1].Candidate)], register, nights);

        Assert.Equal([new DateOnly(2026, 11, 2)], PaperScreen.ExposedNights([Source(Graded), Source(register[1].Candidate)], register));
        Assert.Equal([true, false], marked.Select(account => account.Trades.Single().Trade.Halved));
        Assert.True(PaperScreen.Log(marked[0], null, new DateOnly(2026, 11, 6), (_, _) => null).Single().Halved);
    }

    [Fact]
    public async Task TheRemedysLinesRegisterTheGradedExposuresWhereTheFamilyHasRoomAndTheRuleReadsAMarketCheckAndRefuseElsewhere()
    {
        using var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);
        var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);

        Registration Live(string family, string index, double largeBreadth, double floor) =>
            new(FormattableString.Invariant($"the live {IndexRuleCandidate.NameOf(index)} {family} rule at its provisional setting"), "the rule", "the test", IndexEvaluatorOf(family, index).Name, CheckedAt(family, index, largeBreadth, floor));

        // The S&P 400's breakout reading the S&P 500's breadth, its drift its own at 0.45, the S&P 600's breakout its own at
        // 0.40 and its drift no check, as the operator's register stands; and the S&P 500's drift at nine.
        foreach (var live in new[] { Live(BreakoutRule.Name, "MID", 1, 0.45), Live(DriftRule.Name, "MID", 0, 0.45), Live(BreakoutRule.Name, "SML", 0, 0.40), Live(DriftRule.Name, "SML", IndexRuleCandidate.NoMarketCheck, 0) })
        {
            Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterTogetherAsync([live], "register-" + live.Evaluator)).Outcome);
        }

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterTogetherAsync(TheSetupFamilies.Drifts, "register-drifts")).Outcome);
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            $"(100, 'the drift tone variant as registered', 'the rule', 'the test', 'drift', '{{}}', '{new DriftCandidate().Version}', 'registered', NULL, '2026-10-30T11:30:00Z', NULL);");

        var lines = File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", ExposureRemedy)).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();

        Assert.Equal(["register --exposed breakout --index MID", "register --exposed drift --index MID", "register --exposed breakout --index SML"], lines);

        foreach (var (line, place) in lines.Select((line, place) => (line, place)))
        {
            var (code, said) = await RegisterVerbAt(store, at.AddMinutes(1 + place), Arguments(line)[1..]);

            Assert.True(code == 0, said);
            Assert.Contains("family of 2 of 9", said, StringComparison.Ordinal);
        }

        var rows = await new CandidateRegistrar(FixedClock.At(at.AddMinutes(5), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var graded = CandidateFamily.Standing(rows, at.AddMinutes(5)).Where(row => RuleHooks.Of(CandidateEvaluator.Read(row.Parameters)).Exposure is not null).OrderBy(row => row.Id).ToArray();

        Assert.Equal(
            [
                $"the S&P 400 breakout rule at its provisional setting, {ExposureProcedures.WordsOf(0.45)}",
                $"the S&P 400 drift rule at its provisional setting, {ExposureProcedures.WordsOf(0.45)}",
                $"the S&P 600 breakout rule at its provisional setting, {ExposureProcedures.WordsOf(0.40)}",
            ],
            graded.Select(row => row.Candidate));
        Assert.Equal([0.45, 0.45, 0.40], graded.Select(row => CandidateEvaluator.Read(row.Parameters)[RuleHooks.ExposureParameter]));
        Assert.Equal(
            CheckedAt(BreakoutRule.Name, "SML", 0, 0.40).OrderBy(pair => pair.Key, StringComparer.Ordinal),
            CandidateEvaluator.Read(graded[2].Parameters).Where(pair => pair.Key != RuleHooks.ExposureParameter).OrderBy(pair => pair.Key, StringComparer.Ordinal));

        // Refused, writing nothing: the S&P 600's drift, which reads no market check; the S&P 500's drift at nine; from
        // 21.16 a pullback on the S&P 400 with no live rule standing here, its walk now reading the exposure; and the S&P
        // 500's breakout, with no live rule standing here.
        var before = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");

        foreach (var ((args, expected), place) in new (string[] Args, string Said)[]
        {
            ([RegisterVerb.Exposed, DriftRule.Name, "--index", "SML"], "reads no market check"),
            ([RegisterVerb.Exposed, DriftRule.Name, "--index", "GSPC"], "9 candidates of the drift family already stand registered"),
            ([RegisterVerb.Exposed, SetupFamilies.Pullback, "--index", "MID"], "no live pullback rule stands registered on the S&P 400"),
            ([RegisterVerb.Exposed, BreakoutRule.Name, "--index", "GSPC"], "no live breakout rule stands registered on the S&P 500"),
        }.Select((refusal, place) => (refusal, place)))
        {
            var (code, said) = await RegisterVerbAt(store, at.AddMinutes(10 + place), args);

            Assert.Equal(1, code);
            Assert.Contains(expected, said, StringComparison.Ordinal);
        }

        Assert.Equal(before, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }
}
