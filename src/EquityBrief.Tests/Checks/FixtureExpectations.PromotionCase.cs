using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.17: the rule a variant replaces its running revision by, each condition worked by hand on both
// sides over constructed records: over the same nights, read from a variant's own first night where it registered after
// the restart and from the freeze's first day where it registered before; over the long history's years from 2019, the
// years before never read; and 50 closed trades or a book's 12 months. A case is put from the run that ends the freeze; and
// no source 24.17 changes is one a rule's version pins or one that writes the register.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
public partial class FixtureExpectations
{
    internal static readonly string[] PromotionCaseSources =
    [
        "src/EquityBrief.Core/Loop/PromotionRule.cs",
        "src/EquityBrief.Api/Reading/CaseScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Case.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Case.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
    ];

    // Section 17's row of the rule a variant replaces its running revision by.
    internal static readonly string[] PromotionRuleClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The case for promotion's rule"),
    ];

    static IReadOnlyList<PromotionTrade> Closed(DateOnly first, int count, double result) =>
        [.. Enumerable.Range(0, count).Select(day => new PromotionTrade(first.AddDays(day % 20), first.AddDays((day % 20) + 5), result))];

    [Fact]
    public void AVariantRegisteredAfterTheRestartIsReadFromItsOwnFirstNightAndOneBeforeItFromTheFreezesFirstDay()
    {
        var restart = new DateOnly(2026, 11, 16);
        var december = new DateOnly(2026, 12, 1);

        Assert.Equal(december, PromotionRule.SameFrom(december, restart));
        Assert.Equal(december, PromotionRule.SameFrom(restart, december));
        Assert.Equal(RuleFreezeDates.From, PromotionRule.SameFrom(new DateOnly(2026, 10, 20), new DateOnly(2026, 11, 1)));

        // The running revision's trade listed before the variant's first night, the best it made, is read on neither side.
        var night = new DateOnly(2027, 2, 5);
        IReadOnlyList<PromotionTrade> running = [new(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 25), 5.0), .. Closed(december, 60, 0.1)];
        var from = PromotionRule.SameFrom(december, restart);
        var ran = PromotionRule.Record(false, running, from, night);

        Assert.Equal(60, ran.Closed);
        Assert.Equal(60, ran.Listed);
        Assert.Equal(0.1, ran.Figure!.Value, 12);
        Assert.Equal(new DateOnly(2026, 11, 16), PromotionRule.Record(false, running, restart, night).From);
        Assert.Equal(61, PromotionRule.Record(false, running, restart, night).Closed);

        // A trade still open on the night counts as listed and not as closed.
        var open = PromotionRule.Record(false, [.. running, new(night, null, null)], from, night);

        Assert.Equal(61, open.Listed);
        Assert.Equal(60, open.Closed);
    }

    [Fact]
    public void EachConditionOfTheRuleIsMetOnOneSideAndNotTheOther()
    {
        var from = new DateOnly(2026, 12, 1);
        var night = new DateOnly(2027, 2, 5);
        var ran = PromotionRule.Record(false, Closed(from, 60, 0.125), from, night);
        var better = new PromotionLong(0.15, 20);
        var worse = new PromotionLong(0.05, 20);

        // Over the same nights: +$0.25 against +$0.125 beats it, level does not.
        var ahead = PromotionRule.Record(false, Closed(from, 50, 0.25), from, night);
        var level = PromotionRule.Record(false, Closed(from, 50, 0.125), from, night);

        Assert.True(PromotionRule.Judge(false, ahead, ran, better, worse).SameNights);
        Assert.False(PromotionRule.Judge(false, level, ran, better, worse).SameNights);

        // 50 closed trades meet the count, 49 do not.
        Assert.True(PromotionRule.Judge(false, ahead, ran, better, worse).Enough);
        Assert.False(PromotionRule.Judge(false, PromotionRule.Record(false, Closed(from, 49, 0.2), from, night), ran, better, worse).Enough);

        // Over the long history: ahead meets it, behind or untested does not.
        Assert.True(PromotionRule.Judge(false, ahead, ran, better, worse).LongHistory);
        Assert.False(PromotionRule.Judge(false, ahead, ran, worse, better).LongHistory);
        Assert.False(PromotionRule.Judge(false, ahead, ran, null, worse).LongHistory);

        Assert.True(PromotionRule.Judge(false, ahead, ran, better, worse).Meets);
        Assert.Equal(2, PromotionRule.Judge(false, level, ran, better, worse).Met);

        // A book's 12 months from 16 Nov 2026 are held on 16 Nov 2027, and on the day before only 11.
        var start = new DateOnly(2026, 11, 16);
        IReadOnlyList<PromotionTrade> held = [new(start, new DateOnly(2027, 1, 4), 0.06)];

        Assert.Equal(12, PromotionRule.Record(true, held, start, new DateOnly(2027, 11, 16)).Months);
        Assert.True(PromotionRule.Judge(true, PromotionRule.Record(true, held, start, new DateOnly(2027, 11, 16)), PromotionRule.Record(true, [new(start, new DateOnly(2027, 1, 4), 0.02)], start, new DateOnly(2027, 11, 16)), better, worse).Meets);
        Assert.False(PromotionRule.Judge(true, PromotionRule.Record(true, held, start, new DateOnly(2027, 11, 15)), PromotionRule.Record(true, [new(start, new DateOnly(2027, 1, 4), 0.02)], start, new DateOnly(2027, 11, 15)), better, worse).Enough);
    }

    // A book whose holdings closed inside its first month: from 1 Mar 2027 to 31 Mar, 22 sessions and no whole month, two
    // holdings closed at +4% and +2%, so +3.00 points so far, by hand, never spread over 22/21 of a month (2.86) and never a
    // rate over 0 months. On 1 Apr a whole month has passed and the figure is a rate again.
    [Fact]
    public void ABooksRecordInsideItsFirstMonthReadsItsPointsSoFarAndNeverARateOverNoMonths()
    {
        var from = new DateOnly(2027, 3, 1);
        IReadOnlyList<PromotionTrade> held = [new(from, new DateOnly(2027, 3, 10), 0.04), new(from, new DateOnly(2027, 3, 24), 0.02)];
        var first = PromotionRule.Record(true, held, from, new DateOnly(2027, 3, 31));

        Assert.Equal(0, first.Months);
        Assert.Equal(3.0, first.Figure!.Value, 12);
        Assert.Equal("+3.00 points so far, in its first month", Web.App.SinglePageApp.Record(true, first));
        Assert.DoesNotContain("over 0 months", Web.App.SinglePageApp.Record(true, first), StringComparison.Ordinal);
        Assert.Equal("-1.50 points so far, in its first month", Web.App.SinglePageApp.Record(true, PromotionRule.Record(true, [new(from, new DateOnly(2027, 3, 10), -0.015)], from, new DateOnly(2027, 3, 31))));

        var whole = PromotionRule.Record(true, held, from, new DateOnly(2027, 4, 1));

        Assert.Equal(1, whole.Months);
        Assert.Equal(3.0 / PromotionRule.TradingMonths(from, new DateOnly(2027, 4, 1)), whole.Figure!.Value, 12);
        Assert.EndsWith("points a month over 1 month", Web.App.SinglePageApp.Record(true, whole), StringComparison.Ordinal);
    }

    // The long history from 2019: 2017's years are never read, 2019 and 2020 read 1 and 2 over 10 units each against the
    // running revision's 0.5 and 0.5, $0.15 against $0.05.
    [Fact]
    public void TheLongHistoryIsReadFromTheYearsItWasNotChosenOn()
    {
        var (variant, running) = PromotionRule.Long([(2017, 10, 50.0, 10, -50.0), (2019, 10, 1.0, 10, 0.5), (2020, 10, 2.0, 10, 0.5)]);

        Assert.Equal(0.15, variant.Figure!.Value, 12);
        Assert.Equal(20, variant.Units);
        Assert.Equal(0.05, running.Figure!.Value, 12);
        Assert.Null(PromotionRule.Long([(2018, 10, 1.0, 10, 0.5)]).Variant.Figure);
    }

    [Fact]
    public void ACaseIsPutFromTheMonthlyRunThatEndsTheFreeze()
    {
        Assert.False(PromotionRule.PutOn(new DateOnly(2027, 2, 5)));
        Assert.True(PromotionRule.PutOn(new DateOnly(2027, 2, 6)));
        Assert.Equal(50, PromotionRule.Trades);
        Assert.Equal(12, PromotionRule.Months);
    }

    [Fact]
    public void NoSource24Point17ChangesIsOneARulesVersionPinsOrOneThatWritesTheRegister()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(Core.Loop.RuleHooks.PlanWalkSources)
            .Concat(Worker.Rules.RuleVersionScorer.CodeVersionSources)
            .Concat(Worker.Filter.SwingFilter.CodeVersionSources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 100, $"Read {pinned.Length} pinned source(s), expected at least 100.");
        Assert.All(PromotionCaseSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(PromotionCaseSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.All(PromotionCaseSources, path => Assert.DoesNotContain("INSERT INTO candidate_register", File.ReadAllText(Path.Combine(Repository.Root, path)), StringComparison.Ordinal));
    }
}
