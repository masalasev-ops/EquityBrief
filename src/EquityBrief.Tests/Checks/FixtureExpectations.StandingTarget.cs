using System.Net;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Fills;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.18: the plan's target standing as a sell order from the buy, an exit of the menu, worked by hand
// over constructed paths: a session whose high reaches the target sold at the target whatever its close, a later session
// opening above it sold at that open, the buy's own session sold at the target and never at its open, a stop judged on the
// close and the cap at its close, a trailing plan walked as its own and a caller handing no highs refused; the read-only
// print's walk agreeing with the menu's; the fill step selling a stored trade at the target on that session; the register
// form reaching the S&P 400's and 600's pullback and drift alone; and the screens' words in the operator's.
// see: The target as a sell order standing from the buy is an exit of the menu, and its variant registers where its family has room
public partial class FixtureExpectations
{
    static ExitChoice StandingExit => ExitMenu.Swing.Single(choice => choice.Kind == ExitKind.Standing);

    // A plan bought at 100's close, its stop at 95, five a risk, its target at 110 and five sessions; closes, opens and
    // highs oldest first, the listing at bar 0 and the buy at bar 1's open.
    [Fact]
    public void TheStandingTargetSellsAtTheTargetOrAnOpenAboveItAndTheStopOnTheCloseWorkedByHand()
    {
        var anchor = new SetupAnchor(DateOnly.MinValue, 100, 95, 110, null, 5, 1);

        // Bar 3's high reaches 111 and its close falls to 94, under the stop: the standing order sells at 110, two risks.
        double[] closes = [100, 102, 104, 94, 93, 92];
        double[] opens = [100, 101, 103, 105, 93, 92];
        double[] highs = [100, 103, 108, 111, 94, 93];
        var touched = ExitMenu.Replay(closes, 0, anchor, 5, StandingExit, opens, highs);

        Assert.Equal((2.0, 3, ExitMenu.StandingEnd), (Math.Round(touched.Result!.Value, 12), touched.Sessions, touched.End));

        // Bar 3 opens at 112, above the target, after a close under it: sold at that open, 2.4 risks.
        double[] gapped = [100, 102, 108, 112, 113, 113];
        var opened = ExitMenu.Replay(gapped, 0, anchor, 5, StandingExit, [100, 101, 105, 112, 112, 113], [100, 103, 109, 113, 114, 114]);

        Assert.Equal((2.4, 3, ExitMenu.StandingEnd), (Math.Round(opened.Result!.Value, 12), opened.Sessions, opened.End));

        // The buy's own session opening at 111 is not sold at that open; its high, 112, sells at the target, 2.0.
        var bought = ExitMenu.Replay([100, 105, 106, 107, 108, 109], 0, anchor, 5, StandingExit, [100, 111, 106, 107, 108, 109], [100, 112, 107, 108, 109, 110]);

        Assert.Equal((2.0, 1, ExitMenu.StandingEnd), (Math.Round(bought.Result!.Value, 12), bought.Sessions, bought.End));

        // A close under the stop with no high near the target is the stop's, at that close, -1.2 before the fill step
        // sells it at the next open; five closes reaching neither are the cap's at its close, 0.8.
        var stopped = ExitMenu.Replay([100, 98, 94, 93, 92, 91], 0, anchor, 5, StandingExit, [100, 99, 97, 93, 92, 91], [100, 99, 98, 94, 93, 92]);
        var capped = ExitMenu.Replay([100, 101, 102, 103, 104, 104], 0, anchor, 5, StandingExit, [100, 100, 101, 102, 103, 104], [100, 102, 103, 104, 105, 109]);

        Assert.Equal((-1.2, 2, SetupEnds.Stop), (Math.Round(stopped.Result!.Value, 12), stopped.Sessions, stopped.End));
        Assert.Equal((0.8, 5, SetupEnds.Cap), (Math.Round(capped.Result!.Value, 12), capped.Sessions, capped.End));

        // The standing sale is sold where it is and never at the next open; the stop's is.
        Assert.False(ExitFills.AtNextOpen(ExitMenu.StandingEnd));
        Assert.True(ExitFills.AtNextOpen(SetupEnds.Stop));

        // A plan whose stop trails stands no order and walks as its own; one handed no highs is refused.
        var trailing = new SetupAnchor(DateOnly.MinValue, 100, 95, null, 5, 5, 1);

        Assert.Equal(SetupReplay.Replay(closes, 0, trailing), ExitMenu.Replay(closes, 0, trailing, 5, StandingExit));
        Assert.Throws<InvalidOperationException>(() => ExitMenu.Replay(closes, 0, anchor, 5, StandingExit));

        // The menu numbers it after the thirty that stood, so every stored exit keeps its meaning.
        Assert.Equal(31, StandingExit.Number);
        Assert.Equal(31, ExitMenu.Swing.Count);
    }

    [Fact]
    public void TheReadOnlyPrintsWalkAgreesWithTheMenusOnceTheBuyIsAtTheOpen()
    {
        // The same path as the menu's touched case, bought at bar 1's open of 101: the standing order sells at 110, (110 -
        // 101) / 5 = 1.8 risks, the menu's 2.0 bought at the close less the gap of (101 - 100) / 5.
        var anchor = new SetupAnchor(DateOnly.MinValue, 100, 95, 110, null, 5, 1);
        double[] closes = [100, 102, 104, 94, 93, 92];
        double[] opens = [100, 101, 103, 105, 93, 92];
        double[] highs = [100, 103, 108, 111, 94, 93];
        var printed = StandingTarget.Sold(closes, opens, highs, 0, anchor, 101);
        var menu = ExitMenu.Replay(closes, 0, anchor, 5, StandingExit, opens, highs);

        Assert.Equal((1.8, 3, SetupEnds.Target), (Math.Round(printed!.Result!.Value, 12), printed.Sessions, printed.End));
        Assert.Equal(Math.Round(printed.Result.Value, 12), Math.Round(TradeFills.AtOpen(menu.Result, 100, 5, 101)!.Value, 12));

        // The members' benchmark the same way: two members, one touching the target and one held to the cap.
        var plan = new SetupPlan(1, 2, null, 5);
        var benchmark = StandingTarget.Benchmark([(closes, opens, highs, 0, 5), ([100, 101, 102, 103, 104, 104], [100, 100, 101, 102, 103, 104], [100, 102, 103, 104, 105, 109], 0, 5)], DateOnly.MinValue, plan);

        // (1.8 + (104 - 100) / 5) / 2 = 1.3.
        Assert.Equal(1.3, Math.Round(benchmark!.Value, 12));
    }

    [Fact]
    public void TheFillStepSellsAStandingTargetsTradeAtTheTargetOnThatSession()
    {
        var night = new DateOnly(2026, 11, 2);
        var trade = new TradeFiller.StoredTrade("index_rule_trade", "MID", "the S&P 400 drift rule, " + StandingExit.Words, "KEY", night, 100, 95, 110, 1.0, 1.8, null, null)
        {
            EndedOn = night.AddDays(3),
            Cap = 5,
            Exit = StandingExit.Number,
        };
        IReadOnlyList<(DateOnly, double, double, double)> path =
            [(night, 100, 100, 100), (night.AddDays(1), 101, 102, 103), (night.AddDays(2), 103, 104, 108), (night.AddDays(3), 105, 94, 111), (night.AddDays(4), 93, 93, 94)];
        var sold = TradeFiller.Sold(trade, 1.8, path);

        // Sold on the session its high reached the target, at the target, its result as the walk bought it at the open.
        Assert.Equal((night.AddDays(3), 110.0, 1.8), (sold.On, Math.Round(sold.Price!.Value, 9), sold.Result));
    }

    [Fact]
    public void TheStandingVariantRegistersOnTheSandP400And600sPullbackAndDriftAlone()
    {
        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        RegisterRow Row(long id, string candidate, string evaluator, string parameters = "{}") =>
            new(id, candidate, "the rule", "the test", evaluator, parameters, CandidateEvaluators.Find(evaluator)!.Version, CandidateFamily.Registered, null, at.AddDays(-1), null);
        RegisterRow[] register =
        [
            Row(1, FamilyRecords.LivePrefix + "S&P 400 drift rule", "drift-400"),
            Row(2, FamilyRecords.LivePrefix + "S&P 400 breakout rule", "breakout-400"),
            Row(3, FamilyRecords.LivePrefix + "S&P 600 pullback rule", "pullback-600", "{\"exit\":3}"),
        ];

        var (drift, none) = RegisterVerb.StandingVariant("drift", "MID", register, at);

        Assert.Null(none);
        Assert.Equal("the S&P 400 drift rule, " + StandingExit.Words, drift!.Candidate);
        Assert.Equal(StandingExit.Number, drift.Parameters[RuleHooks.ExitParameter]);
        Assert.Equal("drift-400", drift.Evaluator);

        foreach (var (family, index, why) in new[]
        {
            ("drift", "GSPC", "hold nine rules"),
            ("breakout", "MID", "sets no target"),
            ("pullback", "SML", "stands at an exit of the menu already"),
            ("pullback", "MID", "no live pullback rule stands"),
        })
        {
            var (registered, refused) = RegisterVerb.StandingVariant(family, index, register, at);

            Assert.Null(registered);
            Assert.Contains(why, refused, StringComparison.Ordinal);
        }

        Assert.Equal(
            [true, true, false, true, true, false, false, false, false],
            new[] { ("MID", "pullback"), ("SML", "drift"), ("GSPC", "pullback"), ("GSPC", "drift"), ("MID", "drift"), ("MID", "breakout"), ("SML", "dip"), ("GSPC", "quality"), ("SML", "improving") }
                .Select(pair => ExitProcedures.StandsFor(pair.Item1, pair.Item2)));
    }

    [Fact]
    public void TheRemedysLinesRegisterEveryMovedRuleAgainAndThenTheFourVariants()
    {
        string[] lines = [.. File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", "2026-11-01-21.18-the-standing-target.txt")).Where(line => line.Length > 0 && !line.StartsWith('#'))];

        Assert.Equal(5, lines.Length);
        Assert.Equal(["register", "--moved", "--evidence"], Arguments(lines[0])[..3]);
        Assert.Equal(
            [("pullback", "MID"), ("drift", "MID"), ("pullback", "SML"), ("drift", "SML")],
            lines[1..].Select(line => Arguments(line)).Select(words => (words[2], words[4])));
        Assert.All(lines[1..], line => Assert.Equal(["register", RegisterVerb.Standing], Arguments(line)[..2]));
    }

    [Fact]
    public void AStandingTargetsPickAndItsVariantsRowSayHowItSellsInTheOperatorsWords()
    {
        const string Words = "Sells at the target the moment the price touches it during the day, or at the open if the stock opens above it. Stops still sell at the next open after a close below them.";

        Assert.Equal(Words, ExitMenu.StandingWords);
        Assert.True(MarkRenderer.StandsAtTheTarget("the S&P 400 drift rule, " + StandingExit.Words));
        Assert.False(MarkRenderer.StandsAtTheTarget("the live S&P 400 drift rule"));
        Assert.False(MarkRenderer.StandsAtTheTarget(null));
        Assert.Contains(WebUtility.HtmlEncode(Words), Reading.ReadSurface.StandingCardPlan("the S&P 400 drift rule, " + StandingExit.Words), StringComparison.Ordinal);
        Assert.DoesNotContain(WebUtility.HtmlEncode(Words), Reading.ReadSurface.StandingCardPlan("the live S&P 400 drift rule"), StringComparison.Ordinal);
    }
}
