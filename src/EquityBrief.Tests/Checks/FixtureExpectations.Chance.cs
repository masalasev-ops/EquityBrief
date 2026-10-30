using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.7: the setups like a pick read for how often they won and what they made after costs. Over a
// thousand constructed setups the nearest 250 are read on both sides of the sentence, every one winning half a risk and
// half of them winning, the setups outside the 250 reading nothing of it, and at 29 and 30 holding a result; a target's, a
// trailing rule's and a book's forms each read a win their own way and their round trips are worked by hand from the
// published table; each form's sentence speaks in the reader's words; the breakout's ninth rule orders a constructed night
// by the figure as the hand does; and the remedy's line registers it over a register shaped as the operator's, where the
// drift at nine refuses the same order.
// see: Each pick's card says how often the setups like it won and what they made after costs, under the rule's heading
// see: An order by the expected value of the setups like each pick is registered where its family has room and its step can hand the figure, and walked unregistered elsewhere
public partial class FixtureExpectations
{
    const string ChanceRemedy = "2026-10-30-21.7-the-setups-like-each-pick.txt";

    // A thousand finished setups of a thousand stocks, the k-th reading k on a session of its own, each with what it came to.
    static SimilarSetup[] ThousandCame(Func<int, (double Result, bool Won)> came) =>
    [
        .. Enumerable.Range(0, 1000).Select(at =>
        {
            var (result, won) = came(at);

            return new SimilarSetup(FormattableString.Invariant($"T{at:0000}"), at * 30, [at], result, result, won);
        }),
    ];

    static string Said(SetupChance chance) => PlainWords.Visible(MarkRenderer.Chance(new CardSimilar().WithChance(chance)));

    [Fact]
    public void ThePicksChanceIsWorkedByHandOverAThousandSetupsOnBothSidesOfTheSentenceAndAtTheFloor()
    {
        // The pick reads 999.5, so the nearest 250 are the 750th to the 999th. Each of them reached its target for half a
        // risk after costs, and the 750 under them lost one: 250 of 250 won, their mean half a risk with no range around it.
        var made = SimilarSetups.Match(ThousandCame(at => at >= 750 ? (0.5, true) : (-1, false)), "PICK", [999.5], ["liquidity"], ChanceForm.Target)!.Chance!;

        Assert.Equal((ChanceForm.Target, 250, 250, 1.0), (made.Form, made.Count, made.Won, made.Share));
        Assert.Equal((0.5, 0.5, 0.5), (made.Mean, made.Low, made.High));
        Assert.Equal(
            "Of the 250 past setups most like this one under this setup, 250 (100%) reached their target before their stop. On average they made $0.50 per $1 risked after costs, and nine times in ten that average falls between $0.50 and $0.50. This is those setups' record, not this stock's chance.",
            Said(made));

        // The other side: among the 250, winning 0.8 of a risk and losing one in turn, and every setup outside them winning
        // five, which the figure reads none of. 125 of 250 won; the mean -0.1; each result 0.9 from it, so the standard
        // error 0.9 over the square root of 249, and the range 1.645 of them either side, -0.194 to -0.006.
        var lost = SimilarSetups.Match(ThousandCame(at => at < 750 ? (5, true) : at % 2 == 0 ? (0.8, true) : (-1, false)), "PICK", [999.5], ["liquidity"], ChanceForm.Target)!.Chance!;

        Assert.Equal((250, 125, 0.5), (lost.Count, lost.Won, lost.Share));
        Assert.Equal(-0.1, lost.Mean, 12);
        Assert.Equal(-0.1 - (SimilarSetups.Z * 0.9 / Math.Sqrt(249)), lost.Low, 12);
        Assert.Equal(-0.1 + (SimilarSetups.Z * 0.9 / Math.Sqrt(249)), lost.High, 12);
        Assert.Equal(
            "Of the 250 past setups most like this one under this setup, 125 (50%) reached their target before their stop. On average they lost $0.10 per $1 risked after costs, and nine times in ten that average falls between -$0.19 and -$0.01. This is those setups' record, not this stock's chance.",
            Said(lost));

        // Forty matched, the first so many holding a result and a win and the rest neither: thirty draw the chance, every
        // one winning a risk; twenty-nine draw none and the card's part says nothing of it, the part itself still drawn;
        // and thirty of which one holds a result and no win draw none either.
        SimilarSetup[] Forty(int held, bool oneUnread = false) =>
        [
            .. Enumerable.Range(0, 40).Select(at => new SimilarSetup(
                FormattableString.Invariant($"O{at:00}"),
                100 + (at * 30),
                [at],
                0,
                at < held ? 1 : null,
                at < held && !(oneUnread && at == 0) ? true : null)),
        ];

        var thirty = SimilarSetups.Match(Forty(30), "PICK", [20], ["liquidity"], ChanceForm.Target)!;
        var twentyNine = SimilarSetups.Match(Forty(29), "PICK", [20], ["liquidity"], ChanceForm.Target)!;

        Assert.Equal((40, 30, 30, 1.0), (thirty.Count, thirty.Chance!.Count, thirty.Chance.Won, thirty.Chance.Mean));
        Assert.Equal(30, SimilarSetups.Fewest);
        Assert.Equal(40, twentyNine.Count);
        Assert.Null(twentyNine.Chance);
        Assert.Null(SimilarSetups.Match(Forty(30, oneUnread: true), "PICK", [20], ["liquidity"], ChanceForm.Target)!.Chance);
        Assert.Equal(string.Empty, MarkRenderer.Chance(new CardSimilar(Count: 40, Mean: 0, Low: -0.1, High: 0.1, RuleMean: 0).WithChance(twentyNine.Chance)));

        // Matched with no form, as before 21.7, no chance is read.
        Assert.Null(SimilarSetups.Match(Forty(40), "PICK", [20], ["liquidity"])!.Chance);
    }

    [Fact]
    public void ATargetsATrailingRulesAndABooksChanceAreEachReadInTheirOwnFormWorkedByHand()
    {
        // The form: a book family's whatever its plan, and otherwise a target where the plan holds one.
        Assert.Equal(
            [ChanceForm.Target, ChanceForm.Trailing, ChanceForm.Book, ChanceForm.Book],
            new[] { SetupChance.FormOf(false, true), SetupChance.FormOf(false, false), SetupChance.FormOf(true, true), SetupChance.FormOf(true, false) });
        Assert.Equal(
            [true, true, true, false, false],
            new[] { HeavyweightRule.Name, QualityRule.Name, ImprovingRule.Name, BreakoutRule.Name, DipRule.Name }.Select(LikeSetups.IsBook));

        // A win: the target reached for a target's form, whatever the result; a result above nothing for a trailing rule,
        // nothing itself not one; and an edge above nothing for a book, whatever its own return; none where the ledger
        // holds what the form reads none of.
        Assert.Equal(
            new bool?[] { true, false, null, true, false, false, null, true, false, null },
            new[]
            {
                SetupChance.WonOf(ChanceForm.Target, "target", -0.2, -1),
                SetupChance.WonOf(ChanceForm.Target, "stop", 0.4, 1),
                SetupChance.WonOf(ChanceForm.Target, null, 1, 1),
                SetupChance.WonOf(ChanceForm.Trailing, "stop", 0.3, -1),
                SetupChance.WonOf(ChanceForm.Trailing, "trail", -0.2, 1),
                SetupChance.WonOf(ChanceForm.Trailing, "cap", 0, 1),
                SetupChance.WonOf(ChanceForm.Trailing, "trail", null, 1),
                SetupChance.WonOf(ChanceForm.Book, "rebalance", -0.05, 0.01),
                SetupChance.WonOf(ChanceForm.Book, "rebalance", 0.05, -0.01),
                SetupChance.WonOf(ChanceForm.Book, "rebalance", 0.05, null),
            });

        // The round trip, by hand from the published table. A swing trade bought at 50 with its stop at 48, a risk of 2,
        // sold a risk up at 52, its company's value not read so read in the $1 to 2 billion band: half of 0.129 per cent at
        // the buy and half at the sale, 0.032250 and 0.033540 dollars, 0.032895 of the risk. Valued at 10 to the 9.5, about
        // $3.2 billion, the band above $2 billion reads 0.072 per cent: 0.018 and 0.01872, 0.01836 of the risk. A cost the
        // night stored is subtracted as it stands; a stop not under the buy, or no result, reads none.
        Assert.Equal(1 - 0.032895, LikeSetups.AfterCost(ChanceForm.Trailing, 1, null, 50, 48, null)!.Value, 9);
        Assert.Equal(1 - 0.032895, LikeSetups.AfterCost(ChanceForm.Target, 1, null, 50, 48, null)!.Value, 9);
        Assert.Equal(1 - 0.01836, LikeSetups.AfterCost(ChanceForm.Trailing, 1, null, 50, 48, 9.5)!.Value, 9);
        Assert.Equal(0.98, LikeSetups.AfterCost(ChanceForm.Trailing, 1, 0.02, 50, 48, null)!.Value, 12);
        Assert.Null(LikeSetups.AfterCost(ChanceForm.Trailing, 1, null, 48, 48, null));
        Assert.Null(LikeSetups.AfterCost(ChanceForm.Trailing, null, 0.02, 50, 48, null));

        // A book's holding bought at 50 and up a tenth, sold at 55, in points of its buy: 10 less the round trip in per cent
        // of the buy, 0.032250 and 0.035475 dollars over 50, 0.13545 per cent.
        Assert.Equal(10 - 0.13545, LikeSetups.AfterCost(ChanceForm.Book, 0.10, null, 50, 0, null)!.Value, 9);

        // Each form's sentence: a trailing rule's setups ended above their buy, a book's above the equal-weighted hold, its
        // mean in points; and no term of the word list in any of the three, nor in the breakout's ninth rule's name as a page
        // says it.
        var trailing = Said(new SetupChance(ChanceForm.Trailing, 40, 22, 0.25, 0.05, 0.45));
        var book = Said(new SetupChance(ChanceForm.Book, 36, 21, 2.5, -0.7, 5.7));
        var target = Said(new SetupChance(ChanceForm.Target, 250, 112, 0.21, 0.1, 0.32));

        Assert.Equal(
            "Of the 40 past setups most like this one under this setup, 22 (55%) ended above their buy. On average they made $0.25 per $1 risked after costs, and nine times in ten that average falls between $0.05 and $0.45. This is those setups' record, not this stock's chance.",
            trailing);
        Assert.Equal(
            "Of the 36 past setups most like this one under this setup, 21 (58%) ended above the easy alternative, the same money spread across every stock of the index. On average they returned +2.5 points of their buying price after costs, and nine times in ten that average falls between -0.7 points and +5.7 points. This is those setups' record, not this stock's chance.",
            book);
        Assert.StartsWith("Of the 250 past setups most like this one under this setup, 112 (45%) reached their target before their stop. On average they made $0.21 per $1 risked after costs", target, StringComparison.Ordinal);

        var named = ReaderWords.Plain(TheSetupFamilies.BreakoutExpected.Candidate);

        Assert.EndsWith(", ordered by what the past setups most like each made on average after costs", named, StringComparison.Ordinal);
        Assert.All(
            PlainWords.Terms(),
            term => Assert.All(new[] { trailing, book, target, named }, said => Assert.False(PlainWords.Pattern(term).IsMatch(said), $"'{term}' in: {said}")));
        Assert.Matches(PlainWords.Pattern("expected value"), TheSetupFamilies.BreakoutExpected.Candidate);
    }

    [Fact]
    public void TheBreakoutsNinthRuleOrdersAConstructedNightByTheExpectedValueAsTheHandDoes()
    {
        static (string, ShadowOutcome) Fired(string ticker, double order) =>
            (ticker, new ShadowOutcome("a rule", true, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FamilyRuleEvaluator.EntryValue] = "100",
                [FamilyRuleEvaluator.StopValue] = "95",
                [FamilyRuleEvaluator.OrderValue] = order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [FamilyRuleEvaluator.MoveValue] = "2",
            }));

        // The ninth rule: the live rule's setting, nothing moved but the order, which reads the expected value and no reading.
        var ninth = TheSetupFamilies.BreakoutExpected;
        var live = TheSetupFamilies.Breakouts[0];
        var hooks = RuleHooks.Of(CandidateEvaluator.Read(CandidateEvaluator.Write(ninth.Parameters)));

        Assert.Equal((true, true, false), (hooks.ByExpected, hooks.ReadsExpected, hooks.ReadsReadings));
        Assert.Equal(
            live.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            ninth.Parameters.Where(pair => pair.Key != RuleHooks.ExpectedOrderParameter).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(live.Candidate.Replace("the live breakout rule", "the breakout rule", StringComparison.Ordinal) + TheSetupFamilies.ExpectedWords, ninth.Candidate);
        Assert.Equal((BreakoutRule.Name, TheSetupFamilies.BreakoutWords, FamilySweepReport.Test), (ninth.Evaluator, ninth.Rule, ninth.Test));

        // Seven fired in the family's own order, A to G, and the expected value of the setups like each: C and E at 0.30,
        // F 0.20, A 0.10, G nought, D -0.05 and B with no figure. By hand, highest first, a tie in the family's order and the
        // one with no figure last: C, E, F, A, G, D and B; five kept, C, E, F, A and G at places 1 to 5; with E held, C, F,
        // A, G and D.
        (string, ShadowOutcome)[] night = [Fired("A", 7), Fired("B", 6), Fired("C", 5), Fired("D", 4), Fired("E", 3), Fired("F", 2), Fired("G", 1)];
        var figures = new Dictionary<string, double?>(StringComparer.Ordinal) { ["A"] = 0.10, ["B"] = null, ["C"] = 0.30, ["D"] = -0.05, ["E"] = 0.30, ["F"] = 0.20, ["G"] = 0.0 };
        var nothingHeld = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal(["A", "B", "C", "D", "E"], FamilyRecorder.Keep(night, nothingHeld).Select(one => one.Ticker));
        Assert.Equal(["C", "E", "F", "A", "G"], FamilyRecorder.Keep(night, nothingHeld, hooks, null, ticker => figures[ticker]).Select(one => one.Ticker));
        Assert.Equal([1, 2, 3, 4, 5], FamilyRecorder.Keep(night, nothingHeld, hooks, null, ticker => figures[ticker]).Select(one => one.Place));
        Assert.Equal(["C", "F", "A", "G", "D"], FamilyRecorder.Keep(night, new HashSet<string>(["E"], StringComparer.Ordinal), hooks, null, ticker => figures[ticker]).Select(one => one.Ticker));
    }

    [Fact]
    public async Task TheRemedysLineRegistersTheBreakoutsNinthRuleWhereItsFamilyHasRoomAndTheDriftAtNineRefusesTheSameOrder()
    {
        using var store = new TemporaryStore().Migrated();
        var frozenAt = new DateTimeOffset(2026, 10, 30, 11, 30, 0, TimeSpan.Zero);
        var registrar = new CandidateRegistrar(FixedClock.At(frozenAt, SessionZones.UnitedStates), store.DatabaseFile);

        // The S&P 500's breakout and drift as the operator's register holds them after 21.4: the breakout's eight and the
        // drift's nine, its tone variant the ninth, stored as it stands since no reading version's history is read here.
        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterTogetherAsync(TheSetupFamilies.Breakouts, "register-breakouts")).Outcome);
        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterTogetherAsync(TheSetupFamilies.Drifts, "register-drifts")).Outcome);
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            $"(100, 'the drift tone variant as registered', 'the rule', 'the test', 'drift', '{{}}', '{new DriftCandidate().Version}', 'registered', NULL, '2026-10-30T11:30:00Z', NULL);");

        // The remedy's one line registers the breakout again whole: the eight replayed, none of them holding a trade here,
        // and the nine registered at one instant, the ninth ordered by the expected value.
        var line = Assert.Single(File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", ChanceRemedy)), one => one.Length > 0 && !one.StartsWith('#'));
        var words = Arguments(line);

        Assert.Equal(["register", RegisterVerb.FamilyAgain, BreakoutRule.Name, "--evidence"], words[..4]);

        var at = frozenAt.AddHours(3);
        var (code, said) = await RegisterVerbAt(store, at, words[1..]);
        var lines = said.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.True(code == 0, said);
        Assert.Equal(9, lines.Length);
        Assert.All(TheSetupFamilies.Breakouts, rule => Assert.Contains($"replay: '{rule.Candidate}' carries its record on: reproduced its 0 trade(s) over 0 night(s) from 2026-10-30", lines));
        Assert.StartsWith("register: retired 8 and registered 9 at one instant, family of 9 of 9", lines[^1], StringComparison.Ordinal);

        var standing = CandidateFamily.In(CandidateFamily.Standing(await registrar.RowsAsync(), at), BreakoutRule.Name);

        Assert.Equal([.. TheSetupFamilies.Breakouts.Select(one => one.Candidate), TheSetupFamilies.BreakoutExpected.Candidate], standing.OrderBy(row => row.Id).Select(row => row.Candidate));
        Assert.True(RuleHooks.Of(CandidateEvaluator.Read(standing.Single(row => row.Candidate == TheSetupFamilies.BreakoutExpected.Candidate).Parameters)).ByExpected);
        Assert.Single(standing.Select(row => row.RegisteredAt).Distinct());

        // The same order on the drift's live rule, its family at nine, is refused by the registrar and writes nothing.
        var rows = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");
        var refused = await new CandidateRegistrar(FixedClock.At(at.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile).RegisterAsync(
            "the drift rule at its live setting" + TheSetupFamilies.ExpectedWords,
            TheSetupFamilies.DriftWords,
            FamilySweepReport.Test,
            DriftCandidate.EvaluatorName,
            new Dictionary<string, double>(DriftCandidate.ParametersOf(DriftRule.Live), StringComparer.Ordinal) { [RuleHooks.ExpectedOrderParameter] = 1 },
            "register-drift-expected");

        Assert.Equal(CandidateRegistrar.Refused, refused.Outcome);
        Assert.StartsWith("9 candidates of the drift family already stand registered, which is the maximum family of 9.", refused.Detail, StringComparison.Ordinal);
        Assert.Equal(rows, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }
}
