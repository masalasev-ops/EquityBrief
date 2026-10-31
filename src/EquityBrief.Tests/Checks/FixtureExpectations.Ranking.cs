using EquityBrief.Core.Candidates;
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

// fixture-expectations, 21.8: ranking in place of pass-or-fail gates, as a variant. The order is chosen by hand over a
// constructed audit on both sides of the score's top fifth standing above its bottom, then the expected value's, and the
// not-built branch where neither does; the variant's candidates over constructed nights are the family's setups at the
// ledger's loose gates, one under the live rule's volume gate among them, the best five taken by the score and by the
// expected value, a held stock passed over until the night after its trade ends; the tester walks it inside each fold by
// that fold's own score; and the remedy's lines register it over a register shaped as the operator's where the family has
// room and the step lists by the order, refusing elsewhere.
// see: A family's ranking variant orders by the learned score where its audit's top fifth stands above the bottom and otherwise by the expected value, and is built nowhere neither orders
public partial class FixtureExpectations
{
    const string RankingRemedy = "2026-10-30-21.8-the-ranking-variants.txt";

    // The row 21.8 adds that this check reaches: section 17's ranking variant.
    internal static readonly string[] RankingClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The ranking variant"),
    ];

    // A score reading one column of the catalogue as it stands, a score of 0.37 standing at the 37th hundredth.
    sealed class HundredthsScore(string column) : IScoreModel
    {
        readonly int at = LedgerReadings.IndexOf(column);

        public string Hash => "hundredths-" + column;

        public int Rows => 0;

        public IReadOnlyList<int> Readings => [at];

        public double? Score(IReadOnlyList<double?> readings) => readings[at];

        public int? Rank(string index, double score) => Math.Clamp((int)Math.Floor(score * 100), 0, 100);

        public double? FloorAt(string index, double share) => null;

        public IReadOnlyDictionary<string, double> HookParameters(double? floor) => new Dictionary<string, double>(StringComparer.Ordinal);

        public string Canonical() => Hash;
    }

    // A setup of the walk: its stock and session, its readings of the two columns the constructed scores read, its edge,
    // the session its path ended on, settled, and a buy of 50 over a stop of 48 unless one is given.
    static RankingWalk.Setup RankedSetup(string ticker, DateOnly session, double first, double? edge, DateOnly? ended = null, double second = 0, double stop = 48, double? volumeMultiple = null)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[LedgerReadings.IndexOf("close_over_twenty")] = first;
        readings[LedgerReadings.IndexOf("close_over_fifty")] = second;
        readings[LedgerReadings.IndexOf("volume_multiple")] = volumeMultiple;

        return new RankingWalk.Setup(ticker, session, ended ?? session.AddDays(5), true, edge, edge, 50, stop, readings);
    }

    [Fact]
    public void TheRankingsOrderIsChosenByHandOverAConstructedAuditOnBothSidesAndNotBuiltWhereNeitherOrders()
    {
        var score = new HundredthsScore("close_over_twenty");
        IScoreModel? ScoreOf(int year) => year == 2024 ? score : null;

        // Ten settled setups of 2024 scoring 0.05 to 0.95, at the 5th to the 95th hundredth, two to a fifth; one of 2023,
        // which no fold score stands for, and one not settled, neither placed.
        RankingWalk Ten(double[] edges) => new(
            "MID",
            DriftRule.Name,
            [
                .. edges.Select((edge, at) => RankedSetup(FormattableString.Invariant($"S{at}"), new DateOnly(2024, 3, 4).AddDays(at), 0.05 + (0.1 * at), edge)),
                RankedSetup("OLD", new DateOnly(2023, 6, 1), 0.99, 5),
                RankedSetup("OPEN", new DateOnly(2024, 6, 3), 0.99, 5) with { Settled = false },
            ]);

        // The score's top fifth, S8 and S9 at 0.30 and 0.10, a mean of 0.20, above its bottom, S0 and S1 at -0.10 and
        // -0.20, a mean of -0.15: the score orders.
        var (order, byScore, _) = Ten([-0.10, -0.20, 0, 0, 0, 0, 0, 0, 0.30, 0.10]).Choose(ScoreOf, (_, _) => null);

        Assert.Equal(RankingOrder.Score, order);
        Assert.Equal((2, 2), (byScore.BottomSetups, byScore.TopSetups));
        Assert.Equal(-0.15, byScore.Bottom!.Value, 12);
        Assert.Equal(0.20, byScore.Top!.Value, 12);

        // The edges turned about: the score's top fifth at -0.15 under its bottom at 0.20, so it does not order. The
        // expected value of the setups like each stands the other way, one less the score, so its bottom fifth is S8 and
        // S9 at -0.15 and its top S0 and S1 at 0.20: the expected value orders.
        double[] turned = [0.30, 0.10, 0, 0, 0, 0, 0, 0, -0.10, -0.20];
        var walk = Ten(turned);
        double? Opposite(int year, RankingWalk.Setup setup) => 1 - setup.Readings[LedgerReadings.IndexOf("close_over_twenty")];
        var (second, secondScore, secondExpected) = walk.Choose(ScoreOf, Opposite);

        Assert.Equal(RankingOrder.Expected, second);
        Assert.False(secondScore.Orders);
        Assert.Equal(-0.15, secondExpected.Bottom!.Value, 12);
        Assert.Equal(0.20, secondExpected.Top!.Value, 12);

        // Neither: every edge nought, each fifth's mean nought, and nought is not above nought; and handed no score, none.
        Assert.Equal(RankingOrder.None, Ten(new double[10]).Choose(ScoreOf, Opposite).Order);
        Assert.Equal(RankingOrder.None, walk.Choose(_ => null, Opposite).Order);

        // The choice itself and the groups by hand: the score first, the expected value second, none where neither.
        Assert.Equal(RankingOrder.Score, RankingChoice.Of(new RankingFifths(-0.1, 1, 0.2, 1), RankingFifths.None));
        Assert.Equal(RankingOrder.Expected, RankingChoice.Of(new RankingFifths(0.1, 1, 0.1, 1), new RankingFifths(0.0, 1, 0.05, 1)));
        Assert.Equal(RankingOrder.None, RankingChoice.Of(new RankingFifths(0.2, 1, 0.1, 1), new RankingFifths(0.1, 1, 0.1, 1)));
        Assert.Equal([3, 0, 1, 2, 4], RankingChoice.GroupsOf([0.3, 0.1, 0.1, 0.2, 0.5]));
        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 3, 4, 4], RankingChoice.GroupsOf([.. Enumerable.Range(0, 10).Select(at => (double)at)]));
        Assert.Equal((double?)RuleHooks.RankingByScore, RankingChoice.HookOf(RankingOrder.Score));
        Assert.Equal((double?)RuleHooks.RankingByExpected, RankingChoice.HookOf(RankingOrder.Expected));
        Assert.Null(RankingChoice.HookOf(RankingOrder.None));
    }

    [Fact]
    public void TheRankingVariantsCandidatesAndOrderOverConstructedNightsAreWorkedByHand()
    {
        DateOnly[] calendar = [new(2024, 3, 4), new(2024, 3, 5), new(2024, 3, 6), new(2024, 3, 7), new(2024, 3, 8)];
        var places = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var (first, second, fifth) = (calendar[0], calendar[1], calendar[4]);

        // The first night's seven, every one a breakout setup the ledger laid at the family's loose gates: C's volume at
        // 1.2 times its average, under the live rule's 1.5, which the loose gates admit. B's trade ends on the fourth
        // session, so on the second night it is held and passed over, and on the fifth it is free again; K on the fifth
        // has its stop at its buy and is passed over too.
        var walk = new RankingWalk(
            "GSPC",
            BreakoutRule.Name,
            [
                RankedSetup("A", first, 0.2, 0.5),
                RankedSetup("B", first, 0.9, 1.0, ended: calendar[3]),
                RankedSetup("C", first, 0.4, 0.1, volumeMultiple: 1.2),
                RankedSetup("D", first, 0.7, -0.3),
                RankedSetup("E", first, 0.9, 0.2),
                RankedSetup("F", first, 0.1, 0.4),
                RankedSetup("G", first, 0.6, -0.1),
                RankedSetup("B", second, 0.95, 0.7),
                RankedSetup("H", second, 0.8, 0.3),
                RankedSetup("I", second, 0.3, -0.2),
                RankedSetup("B", fifth, 0.9, 0.6),
                RankedSetup("J", fifth, 0.5, 0.0),
                RankedSetup("K", fifth, 0.99, 2.0, stop: 50),
            ]);

        // By the score, the best five of the first night: B and E at 0.9, B first in the night's own order, then D at 0.7,
        // G at 0.6 and C at 0.4, the gate it misses loosened; A and F left. The second night: B at 0.95 is held, so H and
        // I. The fifth: K's stop stands at its buy, then B, free since the night after its trade ended, and J.
        var byScore = walk.Walk(RankingWalk.Ranking(RankingOrder.Score).WithRanker(new HundredthsScore("close_over_twenty")), places, first, fifth);

        Assert.Equal(
            [(0, (double?)1.0), (0, 0.2), (0, -0.3), (0, -0.1), (0, 0.1), (1, 0.3), (1, -0.2), (4, 0.6), (4, 0.0)],
            byScore);

        // By the expected value of the setups like each: A at 0.5, F at 0.4, D at 0.3, E at 0.2 and C at 0.1 the first
        // night's five, G at -0.1 sixth and B, with no figure, left off; the second night I at 0.25 and H at 0.2, B having
        // none; the fifth K passed over at its stop, then J at 0.2 and B at 0.1, never held under this order.
        var figures = new Dictionary<(string, DateOnly), double?>
        {
            [("A", first)] = 0.5, [("B", first)] = null, [("C", first)] = 0.1, [("D", first)] = 0.3, [("E", first)] = 0.2, [("F", first)] = 0.4, [("G", first)] = -0.1,
            [("B", second)] = null, [("H", second)] = 0.2, [("I", second)] = 0.25,
            [("B", fifth)] = 0.1, [("J", fifth)] = 0.2, [("K", fifth)] = 0.3,
        };
        var byExpected = walk.Walk(RankingWalk.Ranking(RankingOrder.Expected), places, first, fifth, setup => figures[(setup.Ticker, setup.Session)]);

        Assert.Equal(
            [(0, (double?)0.5), (0, 0.4), (0, -0.3), (0, 0.2), (0, 0.1), (1, -0.2), (1, 0.3), (4, 0.0), (4, 0.6)],
            byExpected);

        // A member under the index's floors is never laid by the ledger, so it never stands among the candidates: the
        // S&P 400's floors refuse a close of 4.99 and a dollar volume of 4,999,999 a session.
        Assert.False(SetupGates.ClearsTheFloors("MID", 4.99m, 6_000_000m));
        Assert.False(SetupGates.ClearsTheFloors("MID", 5m, 4_999_999m));
        Assert.DoesNotContain(byScore, unit => unit.Entry == 4 && unit.Edge == 2.0);
    }

    [Fact]
    public void TheTesterWalksTheRankingVariantInsideEachFoldByThatFoldsOwnScore()
    {
        // Two folds, 2023 and 2024, each with a score of its own: 2023's reads the close over its 20-session average and
        // 2024's the close over its 50-session average, which order the same six setups each night the other way. A fold
        // takes the best five of its own year's night by its own score; a fold no score stands for walks none.
        LoopFold[] folds = [new(2023, new(2023, 1, 3), new(2023, 12, 29), true), new(2024, new(2024, 1, 2), new(2024, 12, 31), true), new(2025, new(2025, 1, 2), new(2025, 12, 31), true)];
        DateOnly[] calendar = [new(2023, 5, 1), new(2024, 5, 1)];
        var places = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        RankingWalk.Setup[] Night(DateOnly session) =>
        [
            .. Enumerable.Range(0, 6).Select(at => RankedSetup(FormattableString.Invariant($"N{at}"), session, 0.1 * (at + 1), at, second: 0.1 * (6 - at))),
        ];
        var walk = new RankingWalk("SML", BreakoutRule.Name, [.. Night(calendar[0]), .. Night(calendar[1])]);
        var scores = new Dictionary<int, IScoreModel> { [2023] = new HundredthsScore("close_over_twenty"), [2024] = new HundredthsScore("close_over_fifty") };

        var walked = walk.Folds(RankingOrder.Score, folds, places, year => scores.GetValueOrDefault(year), (_, _) => null);

        // 2023: N5 to N1 by the close over its 20-session average, N0 left; 2024: N0 to N4 by the close over its 50, N5 left.
        Assert.Equal([(0, (double?)5), (0, 4), (0, 3), (0, 2), (0, 1)], walked[0]);
        Assert.Equal([(1, (double?)0), (1, 1), (1, 2), (1, 3), (1, 4)], walked[1]);
        Assert.Null(walked[2]);
        Assert.All(walk.Folds(RankingOrder.None, folds, places, year => scores.GetValueOrDefault(year), (_, _) => null), Assert.Null);
    }

    [Fact]
    public async Task TheRemedysLinesRegisterTheRankingVariantsWhereTheFamilyHasRoomAndTheStepListsByTheOrderAndRefuseElsewhere()
    {
        using var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);

        static Registration LiveOn(string family, string index)
        {
            var evaluator = CandidateEvaluators.All.OfType<IndexRuleCandidate>().Single(one => one.SetupFamily == family && one.Index == index);

            return new Registration(FormattableString.Invariant($"the live {IndexRuleCandidate.NameOf(index)} {family} rule at its provisional setting"), "the rule", "the test", evaluator.Name, IndexRules.Provisional(evaluator));
        }

        // The S&P 400's drift and the S&P 600's breakout each a live rule with room beside it, and the S&P 500's drift at
        // nine, as the operator's register stands.
        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterTogetherAsync([LiveOn(DriftRule.Name, "MID")], "register-mid-drift")).Outcome);
        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterTogetherAsync([LiveOn(BreakoutRule.Name, "SML")], "register-sml-breakout")).Outcome);
        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterTogetherAsync(TheSetupFamilies.Drifts, "register-drifts")).Outcome);
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            $"(100, 'the drift tone variant as registered', 'the rule', 'the test', 'drift', '{{}}', '{new DriftCandidate().Version}', 'registered', NULL, '2026-10-30T11:30:00Z', NULL);");

        // The remedy's two lines, each its family's live rule ranked by the index's newest learned score beside it.
        var lines = File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", RankingRemedy)).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();

        Assert.Equal(["register --ranked drift --index MID --by score", "register --ranked breakout --index SML --by score"], lines);

        foreach (var (line, place) in lines.Select((line, place) => (line, place)))
        {
            var (code, said) = await RegisterVerbAt(store, at.AddMinutes(1 + place), Arguments(line)[1..]);

            Assert.True(code == 0, said);
            Assert.Contains("family of 2 of 9", said, StringComparison.Ordinal);
        }

        var rows = await new CandidateRegistrar(FixedClock.At(at.AddMinutes(5), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var ranked = CandidateFamily.Standing(rows, at.AddMinutes(5)).Where(row => RuleHooks.Of(CandidateEvaluator.Read(row.Parameters)).Ranks).OrderBy(row => row.Id).ToArray();
        var words = RankingWalk.Ranking(RankingOrder.Score).Words();

        Assert.Equal(
            [$"the S&P 400 drift rule at its provisional setting, {words}", $"the S&P 600 breakout rule at its provisional setting, {words}"],
            ranked.Select(row => row.Candidate));
        Assert.Equal(["drift-400", "breakout-600"], ranked.Select(row => row.Evaluator));
        Assert.All(ranked, row => Assert.Equal((double)RuleHooks.RankingByScore, CandidateEvaluator.Read(row.Parameters)[RuleHooks.RankingParameter]));
        Assert.Equal(
            IndexRules.Provisional(CandidateEvaluators.All.OfType<IndexRuleCandidate>().Single(one => one.SetupFamily == DriftRule.Name && one.Index == "MID")).OrderBy(pair => pair.Key, StringComparer.Ordinal),
            CandidateEvaluator.Read(ranked[0].Parameters).Where(pair => pair.Key != RuleHooks.RankingParameter).OrderBy(pair => pair.Key, StringComparer.Ordinal));

        // On the card the version differs from its live rule by its order alone, its last chip, which its tab is named by,
        // and Tonight says it with none of the word list's terms.
        var live = IndexRules.Provisional(CandidateEvaluators.All.OfType<IndexRuleCandidate>().Single(one => one.SetupFamily == DriftRule.Name && one.Index == "MID"));
        var chips = Core.Cards.RuleChips.Of(Core.Cards.RuleKind.IndexDrift, CandidateEvaluator.Read(ranked[0].Parameters));
        var plain = Web.Marks.ReaderWords.Plain(words!);

        Assert.Equal(new Core.Cards.RuleChip("hooks", words!), chips[^1]);
        Assert.Equal("V2 · " + words, Core.Cards.RuleChips.TabWords(2, Core.Cards.RuleChips.Differing(Core.Cards.RuleChips.Of(Core.Cards.RuleKind.IndexDrift, live), chips)));
        Assert.Equal("ranked, among every stock its event found that night with its other steps loosened, by the index's newest score fitted to past setups, the best 5", plain);
        Assert.All(PlainWords.Terms(), term => Assert.DoesNotMatch(PlainWords.Pattern(term), plain));

        // Refused, writing nothing: the S&P 500's drift at nine, by the registrar's maximum; the S&P 400's breakout by the
        // expected value, which its step does not list by; the S&P 500's pullback, whose step reads no ranking; an order
        // the form does not name; and a family with no live rule on the index.
        var before = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");

        foreach (var ((args, expected), place) in new (string[] Args, string Said)[]
        {
            ([RegisterVerb.Ranked, DriftRule.Name, "--index", "GSPC", "--by", "score"], "9 candidates of the drift family already stand registered"),
            ([RegisterVerb.Ranked, BreakoutRule.Name, "--index", "MID", "--by", "expected"], "lists no breakout rule ranked by the expected value of the setups like each"),
            ([RegisterVerb.Ranked, SetupFamilies.Pullback, "--index", "GSPC", "--by", "score"], "lists no pullback rule ranked by the index's newest learned score"),
            ([RegisterVerb.Ranked, DriftRule.Name, "--index", "MID", "--by", "best"], "'--by best' names no order"),
            ([RegisterVerb.Ranked, DipRule.Name, "--index", "MID", "--by", "score"], "no live dip rule stands registered on the S&P 400"),
        }.Select((refusal, place) => (refusal, place)))
        {
            var (code, said) = await RegisterVerbAt(store, at.AddMinutes(10 + place), args);

            Assert.Equal(1, code);
            Assert.Contains(expected, said, StringComparison.Ordinal);
        }

        Assert.Equal(before, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }
}
