using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.10: choosing a version by its recent record. The choice at one month over constructed records
// is worked by hand at the floor of ended trades and one under it, a book's at its floor of months; a choice reads no
// trade that has not ended by its month, the cut a fold's year opens on among them; and the procedure walked over a
// constructed history is judged against the live rule as the tester judges a proposal, in the family's correction with
// the family's variant.
// see: A family's version is chosen each month by its recent record as a procedure judged against staying with the live rule, and an approval of it waits on the operator's word
public partial class FixtureExpectations
{
    // The row 21.10 adds that this check reaches: section 17's windows and floors.
    internal static readonly string[] VersionChoiceClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Choosing a version by its record"),
    ];

    // So many trades entered one a session from a place, each at an edge.
    static (int Entry, double? Edge)[] Entered(int from, int count, double edge) =>
        [.. Enumerable.Range(from, count).Select(at => (at, (double?)edge))];

    [Fact]
    public void TheChoiceAtAMonthIsWorkedByHandAtTheFloorAndOneUnder()
    {
        // A month opening on session 1,000, a window of 126 sessions and a cap of 10: a trade counts where it was entered
        // on 874 or after and its cap's sessions passed before 1,000, so entered on 989 at the latest. The live rule holds
        // 50 such trades at +0.1; the variant 49 at +0.5, one under the floor, so the live rule is chosen; with its 50th,
        // the variant at +0.5 stands highest and is chosen.
        IReadOnlyList<(int Entry, double? Edge)> live = Entered(880, 50, 0.1);

        Assert.Equal(0.1, VersionChoices.Record(live, 10, 1000, 126, false)!.Value, 12);
        Assert.Null(VersionChoices.Record(Entered(880, 49, 0.5), 10, 1000, 126, false));
        Assert.Equal(0, VersionChoices.Choose([(live, 10), (Entered(880, 49, 0.5), 10)], 1000, 126, false));
        Assert.Equal(1, VersionChoices.Choose([(live, 10), (Entered(880, 50, 0.5), 10)], 1000, 126, false));

        // A tie stays with the earlier rule, the live one; a variant under the live rule's record is not chosen; and where
        // no rule reaches the floor the live rule is.
        Assert.Equal(0, VersionChoices.Choose([(live, 10), (Entered(880, 50, 0.1), 10)], 1000, 126, false));
        Assert.Equal(0, VersionChoices.Choose([(live, 10), (Entered(880, 50, 0.05), 10)], 1000, 126, false));
        Assert.Equal(0, VersionChoices.Choose([(Entered(880, 10, 0.1), 10), (Entered(880, 10, 0.5), 10)], 1000, 126, false));

        // A book's floor is twelve months, eleven not enough; and the windows and floors are the stated ones.
        Assert.Null(VersionChoices.Record(Entered(880, 11, 0.02), 10, 1000, 126, true));
        Assert.Equal(0.02, VersionChoices.Record(Entered(880, 12, 0.02), 10, 1000, 126, true)!.Value, 12);
        Assert.Equal([126, 252, 504], VersionChoices.Windows);
        Assert.Equal((50, 12), (VersionChoices.TradeFloor, VersionChoices.MonthFloor));
        Assert.True(VersionChoices.Is(VersionChoices.NameOf(252)));
        Assert.False(VersionChoices.Is("the live S&P 400 drift rule"));
    }

    [Fact]
    public void AChoiceReadsNoTradeThatHasNotEndedByItsMonth()
    {
        // The variant's fifty trades at +2 are entered from 991 on, each with ten sessions to run past 1,000, so none has
        // ended by the month and none is read; fifty entered before the window are not read either; the live rule's fifty
        // within it are, and it is chosen.
        IReadOnlyList<(int Entry, double? Edge)> live = Entered(880, 50, 0.1);

        Assert.Null(VersionChoices.Record(Entered(991, 50, 2), 10, 1000, 126, false));
        Assert.Null(VersionChoices.Record(Entered(800, 50, 2), 10, 1000, 126, false));
        Assert.Equal(0, VersionChoices.Choose([(live, 10), ([.. Entered(800, 50, 2), .. Entered(991, 50, 2)], 10)], 1000, 126, false));

        // The cut a fold's year opens on is one of these months: over a weekday calendar the first session of 2024 opens a
        // month, and a trade entered in the last ten sessions of 2023 is not read by the choice made on it.
        var calendar = Weekdays(new DateOnly(2023, 1, 2), 300);
        var starts = VersionChoices.MonthStarts(calendar);
        var newYear = Array.IndexOf(calendar, new DateOnly(2024, 1, 1));

        Assert.Contains(newYear, starts);
        Assert.Null(VersionChoices.Record(Entered(newYear - 10, 50, 2), 10, newYear, 504, false));
        Assert.Equal(12, starts.Count(start => calendar[start].Year == 2023));
    }

    [Fact]
    public void TheProcedureIsJudgedAgainstTheLiveRuleAsTheTesterJudgesAProposalInTheFamilysCorrection()
    {
        // A weekday history from 2019 with the tester's folds; the live rule and one variant each enter a trade every
        // session, the live rule at +0.05 and the variant at +0.2, each with five sessions to run. A month's choice reads
        // the trades entered in its window and ended five sessions before it, fifty of each first by April 2019's first
        // session, the 63rd; so each procedure walks the live rule's trades before it and the variant's from it, the
        // variant chosen ever after.
        var calendar = Weekdays(new DateOnly(2019, 1, 2), 2000);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), calendar, 0, LoopFolds.Of(calendar, calendar[^1]));
        var head = new RuleUnderTest("drift", "the live S&P 400 drift rule", RuleTests.Live, "the live rule", LoopProcedures.Risks, 5, Entered(0, calendar.Length, 0.05));
        var variant = new RuleUnderTest("drift", "the variant", RuleTests.Variant, "a variant", LoopProcedures.Risks, 5, Entered(0, calendar.Length, 0.2));
        var procedures = RuleTests.Choosing(read, [head, variant]).ToArray();
        IReadOnlyList<(int Entry, double? Edge)> byHand = [.. Entered(0, 63, 0.05), .. Entered(63, calendar.Length - 63, 0.2)];

        Assert.Equal(63, Array.IndexOf(calendar, new DateOnly(2019, 4, 1)));
        Assert.Equal([.. VersionChoices.Windows.Select(VersionChoices.NameOf)], procedures.Select(one => one.Rule));
        Assert.All(procedures, procedure => Assert.Equal(byHand, procedure.Trades));
        Assert.All(procedures, procedure => Assert.Contains("it would run 'the variant' now, and switched 0 time(s)", procedure.Words, StringComparison.Ordinal));
        Assert.All(procedures, procedure => Assert.Equal((RuleTests.Variant, LoopProcedures.Risks, 5), (procedure.Standing, procedure.Unit, procedure.Cap)));

        // Judged as the tester judges: each procedure's evidence its trades against the live rule's over the folds, and its
        // verdict the one the tester's step-down gives it among the family's four, the variant and the three procedures.
        var tested = RuleTests.Judge(read, [head, variant, .. procedures]);
        RuleUnderTest[] judgedTogether = [variant, .. procedures];
        LoopEvidence[] evidences = [.. judgedTogether.Select(one => LoopProcedures.Evidence(read.Folds, read.Calendar, [.. read.Folds.Select(_ => one.Trades)], head.Trades!))];
        var verdicts = LoopJudge.Judge(evidences, read.Opens, read.Newest, 5);

        for (var at = 0; at < procedures.Length; at++)
        {
            var judged = Assert.Single(tested, one => one.Rule.Rule == procedures[at].Rule);

            Assert.Equal(head.Rule, judged.Against);
            Assert.Equal(evidences[at + 1].ProposedResults, judged.Evidence!.ProposedResults);
            Assert.Equal((verdicts[at + 1].Adjusted, verdicts[at + 1].Passes, verdicts[at + 1].Blocks), (judged.Verdict!.Adjusted, judged.Verdict.Passes, judged.Verdict.Blocks));
        }

        // A family whose live rule stands alone, or cannot be walked, has nothing to choose among and no procedure.
        Assert.Empty(RuleTests.Choosing(read, [head]));
        Assert.Empty(RuleTests.Choosing(read, [head with { Why = "not walked" }, variant]));
    }
}
