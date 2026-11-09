using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.61: the Monthly check-up's own words, worked by hand. A stored chance read as one in so many at
// the tester's bar and at the operator's November figures, the words a change is stored in read for a new reader with each
// figure restated, each kind of change under its own heading, and the sentence the page opens on in each of its states.
// see: The Monthly check-up opens on what needs the reader and draws each setup's changes on a page of its own
public partial class FixtureExpectations
{
    static LoopProposalRow CheckUpRow(string proposal, string? words) =>
        new("drift", proposal, words, "today", "risks", 400, 19, 0.001, true, true, 4, 3, 1.5, true, 0.1, 3, true);

    [Fact]
    public void AStoredChanceReadsAsOneInSoManyWorkedByHand()
    {
        // The bar, 0.05 over 12 runs a year: 1 over 0.0041666 is 240, already two figures.
        Assert.Equal("About 1 in 240", LoopPlain.Luck(0.05 / 12));

        // Quality's November change: 1 over 0.00245 is 408.2, which reads 410 at two figures.
        Assert.Equal("About 1 in 410", LoopPlain.Luck(0.00245));
        Assert.Equal("About 1 in 340", LoopPlain.Luck(0.00291));

        // Under a hundred the whole number: 1 over 0.05844 is 17.1, and 1 over 0.26339 is 3.8.
        Assert.Equal("About 1 in 17", LoopPlain.Luck(0.05844));
        Assert.Equal("About 1 in 4", LoopPlain.Luck(0.26339));

        // A chance of one in two or worse is not dressed as odds, and a chance the run read none of says so.
        Assert.Equal("About 1 in 2", LoopPlain.Luck(0.49));
        Assert.Equal("Likely luck", LoopPlain.Luck(0.5));
        Assert.Equal("Likely luck", LoopPlain.Luck(1));
        Assert.Equal("Not measured", LoopPlain.Luck(null));
    }

    [Fact]
    public void EachShapeOfStoredChangeReadsInPlainWordsWorkedByHand()
    {
        static string Tried(string proposal, string? words) => LoopPlain.Tried(CheckUpRow(proposal, words));

        // A return in per cent: at or above a negative is a limit on the fall, and at or above a positive a rise.
        Assert.Equal("Also needs the stock not to be down more than 18.1% over the last 126 trading days", Tried("the idea generator, ranked 1", "also requires return_half_year at or above -18.0965"));
        Assert.Equal("Also needs a rise of at least 41.7% over the last 63 trading days", Tried("the idea generator, ranked 1", "also requires return_quarter at or above 41.7061"));

        // The close against an average is a ratio, so 1.0508 is 5.1% above it.
        Assert.Equal("Also needs a close at least 5.1% above its 50-day average", Tried("the idea generator, ranked 1", "also requires close_over_fifty at or above 1.0508"));

        // A fraction of a day's move in per cent, 0.0207 being 2.07%.
        Assert.Equal("Also needs day-to-day swings, apart from the market's, of at least 2.07%", Tried("winners against losers, ranked 1", "also requires residual_volatility at or above 0.0207"));

        // The last report's surprise in per cent, and a logarithm the words leave as a size with no figure.
        Assert.Equal("Also needs its last earnings report to beat forecasts by at least 10.4%", Tried("the idea generator, ranked 1", "also requires surprise_percent at or above 10.4"));
        Assert.Equal("Also needs a larger company, and a 2-day strength gauge of at most 93.8 out of 100", Tried("winners against losers, ranked 1", "also requires company_value at or above 11.2247 and rsi_two at or under 93.7862"));
        Assert.Equal("Also needs a stock that moves at least 1.43 times as much as its index, and a stock with heavy daily dollar trading", Tried("winners against losers, ranked 1", "also requires beta at or above 1.425 and liquidity at or above 8.938"));

        // The selling rules.
        Assert.Equal("Sell at a fixed target, up $4 for every $1 risked", Tried("the autopsy's exit, ranked 1", "exit: a target at 4 risks"));
        Assert.Equal("No fixed target. Once up 2 times the risk, sell on a fall of 1.5 typical days' moves from the best close", Tried("the autopsy's exit, ranked 3", "exit: a trail 1.5 typical moves under the highest close once a close is 2 risks up, with no target"));
        Assert.Equal("Also sell a holding at a close under its 200-day average", Tried("the autopsy's exit, ranked 1", "exit: a holding also sold at a close under its 200-day average"));

        // The learned scores by what each does, a settings key put in words, and a ranking and the graded exposure by name.
        Assert.Equal("Let a score learned from past results set the buying order", Tried("the learned score, ordering the list", "orders tonight's picks by the score abc"));
        Assert.Equal("Let a score learned from past results set the buying order and skip the lowest-scoring fifth", Tried("the learned score, its lowest fifth left off", "orders tonight's picks by the score abc"));
        Assert.Equal("Let a score from many learned decision trees set the buying order and skip the lowest-scoring two fifths", Tried("the learned trees, its lowest two fifths left off", "orders tonight's picks by the trees abc"));
        Assert.Equal("Try a different combination of this strategy's settings", Tried("the grid with the market check and its second stage, at the next open", "drop=-3|trend=200|exit=65|limit=10|stop=3|market=6"));
        Assert.Equal("Loosen the strategy's other steps, then take the top stocks by the score fitted to past setups", ReaderText.Words(Tried("the ranking by the index's newest learned score", "ranked over the family's setups")));
        Assert.Equal("Take more or fewer picks depending on two market checks, in place of today's one", Tried("the graded exposure", "exposure: 5 picks"));
    }

    [Fact]
    public void EachKindOfChangeStandsUnderItsOwnHeadingAndTheChangePutReadsAsASentence()
    {
        Assert.Equal(LoopPlain.ScoreGroup, LoopPlain.GroupOf("the learned score, ordering the list"));
        Assert.Equal(LoopPlain.ScoreGroup, LoopPlain.GroupOf("the learned trees, its lowest fifth left off"));
        Assert.Equal(LoopPlain.ScoreGroup, LoopPlain.GroupOf("the ranking by the index's newest learned score"));
        Assert.Equal(LoopPlain.IdeaGroup, LoopPlain.GroupOf("the idea generator, ranked 2"));
        Assert.Equal(LoopPlain.WinnersGroup, LoopPlain.GroupOf("winners against losers, ranked 5"));
        Assert.Equal(LoopPlain.ExitGroup, LoopPlain.GroupOf("the autopsy's exit, ranked 3"));
        Assert.Equal(LoopPlain.SettingsGroup, LoopPlain.GroupOf("the grid"));
        Assert.Equal(LoopPlain.SettingsGroup, LoopPlain.GroupOf("the graded exposure"));

        // A change to the order says only the order moves, and the same words name what is left off.
        Assert.Equal(
            "The same stocks still qualify. The only difference is which ones get bought first: a score learned from past results now sets the order.",
            LoopPlain.Explained(CheckUpRow("the learned score, ordering the list", "orders tonight's picks")));
        Assert.Equal(
            "The same stocks still qualify. The only difference is which ones get bought first: a score from many learned decision trees now sets the order. The lowest-scoring fifth are skipped.",
            LoopPlain.Explained(CheckUpRow("the learned trees, its lowest fifth left off", "orders tonight's picks")));
    }

    [Fact]
    public void ThePageOpensOnItsOwnSentenceInEachStateWorkedByHand()
    {
        var next = new DateOnly(2026, 12, 5);

        static LoopDecisionRow Word(string decision, string? outcome) =>
            new("run", "quality", "the learned score, ordering the list", decision, null, "2026-11-09T15:00:00Z", outcome, null, null, null);

        Assert.Equal(("One change is waiting for you.", "waiting"), Opening(new CheckUpState(119, 7, 1, 1, [], next)));
        Assert.Equal(("2 changes are waiting for you.", "waiting"), Opening(new CheckUpState(119, 7, 2, 2, [], next)));
        Assert.Equal(("Nothing needs you this month.", "quiet"), Opening(new CheckUpState(119, 7, 0, 0, [], next)));
        Assert.Equal(("Nothing needs you this month.", "decided"), Opening(new CheckUpState(119, 7, 1, 0, [Word(LoopDecisions.Approved, null)], next)));

        Assert.Equal("None of the 119 changes tried is put to you, so every strategy stays as it is.", Beneath(new CheckUpState(119, 7, 0, 0, [], next)));
        Assert.Equal("No change was tried this month.", Beneath(new CheckUpState(0, 7, 0, 0, [], next)));
        Assert.Equal("You approved one change on November 9, 2026. It goes live with the next night's run.", Beneath(new CheckUpState(119, 7, 1, 0, [Word(LoopDecisions.Approved, null)], next)));
        Assert.Equal("You declined one change on November 9, 2026, so every strategy stays as it is.", Beneath(new CheckUpState(119, 7, 1, 0, [Word(LoopDecisions.Declined, null)], next)));
        Assert.Equal("You decided on one change, the last on November 9, 2026. Your decisions below say what was applied.", Beneath(new CheckUpState(119, 7, 1, 0, [Word(LoopDecisions.Approved, LoopDecisions.Applied)], next)));

        static (string, string) Opening(CheckUpState state)
        {
            var (headline, _, kind) = MarkRenderer.CheckUpOpening(state);

            return (headline, kind);
        }

        static string Beneath(CheckUpState state) => MarkRenderer.CheckUpOpening(state).Beneath;
    }

    [Fact]
    public void AStrategysLineSaysWhatItsMonthCameToAndAWordGivenNoLongerWaits()
    {
        var put = CheckUpRow("the grid", "the change");

        static LoopSummaryRow Row(int tested, LoopProposalRow? chosen, LoopDecisionRow? word) => new("drift", [], tested, chosen, chosen, string.Empty, null, null, word);
        static LoopDecisionRow Word(string decision, string? outcome) => new("run", "drift", "the grid", decision, null, "2026-11-09T15:00:00Z", outcome, null, null, null);

        Assert.Equal(("Nothing tried", "none"), MarkRenderer.CheckUpResult(Row(0, null, null), null));
        Assert.Equal(("No change", "none"), MarkRenderer.CheckUpResult(Row(19, null, null), null));
        Assert.Equal(("Waiting for you", "waiting"), MarkRenderer.CheckUpResult(Row(19, put, null), null));
        Assert.Equal(("Changed", "changed"), MarkRenderer.CheckUpResult(Row(19, put, Word(LoopDecisions.Approved, null)), Word(LoopDecisions.Approved, null)));
        Assert.Equal(("No change, you declined", "none"), MarkRenderer.CheckUpResult(Row(19, put, Word(LoopDecisions.Declined, null)), Word(LoopDecisions.Declined, null)));

        Assert.Equal("17 changes were tried for this strategy this month; none beat luck.", MarkRenderer.LoopTriedSaid(17, 0, false, false));
        Assert.Equal("1 change was tried for this strategy this month; it did not beat luck.", MarkRenderer.LoopTriedSaid(1, 0, false, false));
        Assert.Equal("17 changes were tried for this strategy this month; one passed, and waits for your word.", MarkRenderer.LoopTriedSaid(17, 1, true, true));
        Assert.Equal("17 changes were tried for this strategy this month; 2 passed, and the strongest waits for your word.", MarkRenderer.LoopTriedSaid(17, 2, true, true));
        Assert.Equal("17 changes were tried for this strategy this month; one passed, and you have decided on it.", MarkRenderer.LoopTriedSaid(17, 1, true, false));
        Assert.Equal("17 changes were tried for this strategy this month; 2 passed, and you have decided on the strongest.", MarkRenderer.LoopTriedSaid(17, 2, true, false));
        Assert.Equal("17 changes were tried for this strategy this month; one passed, and none is put to you this month.", MarkRenderer.LoopTriedSaid(17, 1, false, false));
        Assert.Equal("No change was tried for this strategy this month.", MarkRenderer.LoopTriedSaid(0, 0, false, false));
    }
}
