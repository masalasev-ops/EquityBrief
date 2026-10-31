using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.16: the second move of the pins' remedy over a register shaped as the operator's, and the
// register's two forms the move widens: the S&P 400's and 600's swing families' order by the expected value, registered
// beside a live rule where its family has room and refused on the S&P 500 and for a family the index families' step lists
// by no hook; and the graded exposure for the pullback and the dip, at the floor the live rule's market check reads, and
// refused where a pullback reads no check or a dip's setting reads no breadth.
// see: An order by the expected value of the setups like each pick is registered where its family has room and its step can hand the figure, and walked unregistered elsewhere
// see: The pullback's and the dip's graded exposure stand in their market check's place as a variant, walked as the breakout's and the drift's are
public partial class FixtureExpectations
{
    const string SecondMoveRemedy = "2026-10-31-21.16-the-second-move-of-the-pins.txt";

    const string MidDip = "drop=-3,trend=50,sell=65,limit=5,stop=3,market=5,volume=0";

    const string SmlDip = "drop=-3,trend=200,sell=65,limit=7,stop=3,market=1,volume=1.5";

    // A frozen S&P 600 breakout ordered by the expected value over a constructed night, worked by hand: seven passing in the
    // rule's own places, A to G, and the expected value of the setups like each, C and E at 0.30, F 0.20, A 0.10, G nought,
    // D -0.05 and B with no figure; highest first, a tie in the rule's own order and the one with no figure last, C, E, F,
    // A, G, D and B, and with E held C, F, A, G, D and B. A failing member is never a candidate, and the rule stating no
    // order keeps its own places.
    [Fact]
    public void AnSAndP600RuleOrderedByTheExpectedValueOrdersAConstructedNightAsTheHandDoes()
    {
        static IndexAnswer Passing(string ticker, int place) => new(ticker, BreakoutRule.Name, true, place, 100m, 95m, null, 5m, 63, place, null);

        IndexAnswer[] night = [Passing("A", 1), Passing("B", 2), Passing("C", 3), Passing("D", 4), Passing("E", 5), Passing("F", 6), Passing("G", 7), new("X", BreakoutRule.Name, false, null, null, null, null, null, null, null, "a part failed")];
        var figures = new Dictionary<string, double?>(StringComparer.Ordinal) { ["A"] = 0.10, ["B"] = null, ["C"] = 0.30, ["D"] = -0.05, ["E"] = 0.30, ["F"] = 0.20, ["G"] = 0.0, ["X"] = 0.9 };
        var ordered = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExpectedOrderParameter] = 1 });
        var nothingHeld = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal(["C", "E", "F", "A", "G", "D", "B"], EquityBrief.Worker.Indices.IndexRuleTrades.Candidates(ordered, BreakoutRule.Name, night, nothingHeld, null, null, ticker => figures[ticker]).Select(one => one.Ticker));
        Assert.Equal(["C", "F", "A", "G", "D", "B"], EquityBrief.Worker.Indices.IndexRuleTrades.Candidates(ordered, BreakoutRule.Name, night, new HashSet<string>(["E"], StringComparer.Ordinal), null, null, ticker => figures[ticker]).Select(one => one.Ticker));
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G"], EquityBrief.Worker.Indices.IndexRuleTrades.Candidates(RuleHooks.Off, BreakoutRule.Name, night, nothingHeld, null, null, ticker => figures[ticker]).Select(one => one.Ticker));
    }

    [Fact]
    public async Task TheSecondMovesRemedyRegistersEachVariantBesideItsLiveRuleAndTheFormsRefuseWhereNoneReads()
    {
        using var store = new TemporaryStore().Migrated();
        var frozenAt = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        // The S&P 400's pullback at its own breadth of 0.35, the S&P 600's pullback reading no check and its breakout at its
        // own 0.40, the S&P 400's dip on the S&P 500's breadth and the S&P 600's on its own at 0.35, and the S&P 500's dip on
        // the VIX, as the operator's register stands.
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt, RegisterVerb.IndexFamily, SetupFamilies.Pullback, "--index", "MID", "--parameters", RemedyFreezes[0].Parameters)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(1), RegisterVerb.IndexFamily, SetupFamilies.Pullback, "--index", "SML", "--parameters", RemedyFreezes[4].Parameters)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(2), RegisterVerb.IndexFamily, BreakoutRule.Name, "--index", "SML", "--parameters", RemedyFreezes[5].Parameters)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(3), RegisterVerb.IndexFamily, DipRule.Name, "--index", "MID", "--parameters", MidDip)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(4), RegisterVerb.IndexFamily, DipRule.Name, "--index", "SML", "--parameters", SmlDip)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(5), RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", FamilyApprovalDip)).Code);

        // The remedy's lines: the migration, every moved rule registered again, then the four variants.
        var lines = File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", SecondMoveRemedy)).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();

        Assert.Equal("migrate", lines[0]);
        Assert.StartsWith("register --moved --evidence ", lines[1], StringComparison.Ordinal);
        Assert.Equal(
            ["register --ordered breakout --index SML", "register --exposed pullback --index MID", "register --exposed dip --index MID", "register --exposed dip --index SML"],
            lines[2..]);

        var at = frozenAt.AddMinutes(10);

        foreach (var (line, place) in lines[2..].Select((line, place) => (line, place)))
        {
            var (code, said) = await RegisterVerbAt(store, at.AddMinutes(place), Arguments(line)[1..]);

            Assert.True(code == 0, said);
        }

        var standing = CandidateFamily.Standing(await new CandidateRegistrar(FixedClock.At(at.AddMinutes(10), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), at.AddMinutes(10));

        RegisterRow Newest(string evaluator) => standing.Where(row => row.Evaluator == evaluator).OrderBy(row => row.Id).Last();

        IReadOnlyDictionary<string, double> ParametersOf(RegisterRow row) => CandidateEvaluator.Read(row.Parameters);

        // The S&P 600's breakout ordered by the expected value, its own setting otherwise.
        var ordered = Newest("breakout-600");
        var orderWords = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExpectedOrderParameter] = 1 }).Words();

        Assert.True(RuleHooks.Of(ParametersOf(ordered)).ByExpected);
        Assert.EndsWith(", " + orderWords, ordered.Candidate, StringComparison.Ordinal);
        Assert.Equal(FrozenParameters(RemedyFreezes[5].Parameters).OrderBy(pair => pair.Key, StringComparer.Ordinal), RuleHooks.Without(ParametersOf(ordered)).OrderBy(pair => pair.Key, StringComparer.Ordinal));

        // The exposure at each floor: the S&P 400's pullback at its own 0.35, its dip at the S&P 500's 0.45 and the S&P 600's
        // dip at its own 0.35.
        Assert.Equal(0.35, RuleHooks.Of(ParametersOf(Newest("pullback-400"))).Exposure);
        Assert.Equal(0.45, DipCandidate.HooksOf(ParametersOf(Newest("dip-400"))).Exposure);
        Assert.Equal(0.35, DipCandidate.HooksOf(ParametersOf(Newest("dip-600"))).Exposure);

        // Refused: the order on the S&P 500 and for a dip; the exposure for a pullback reading no check and a dip whose
        // setting reads the VIX.
        Assert.StartsWith("the S&P 500's breakout registers its order by the expected value with its family", RegisterVerb.OrderedVariant(BreakoutRule.Name, "GSPC", standing, at).Refusal, StringComparison.Ordinal);
        Assert.StartsWith("the index families' step does not list the dip", RegisterVerb.OrderedVariant(DipRule.Name, "MID", standing, at).Refusal, StringComparison.Ordinal);
        Assert.StartsWith("the live pullback rule on the S&P 600 reads no market check", RegisterVerb.ExposedVariant(SetupFamilies.Pullback, "SML", standing, at).Refusal, StringComparison.Ordinal);
        Assert.StartsWith("the live dip rule on the S&P 500 reads no market check", RegisterVerb.ExposedVariant(DipRule.Name, "GSPC", standing, at).Refusal, StringComparison.Ordinal);
        Assert.Null(ExposureProcedures.FloorOf(CandidateEvaluators.Find("dip-500"), ParametersOf(FamilyRecords.LiveOf(standing.Where(row => row.Evaluator == "dip-500").ToArray())!)));
    }
}
