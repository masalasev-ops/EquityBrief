using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 24.17: the case for promotion under the Improvement loop. Over a constructed store the page draws every
// running revision against its variants, the menu names it under the Improvement loop, and a press of Promote or Keep is
// refused with nothing written on the freeze's last day and on the day it ends, the swap being its own checkpoint's. A case
// read through its renderer over constructed records draws the operator's title, the figures code computed, code's words
// for and against holding no digit, the counted part above, and the buttons drawn and refused inside the freeze.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
// see: An approval inside the rule freeze is refused and replaces no live rule
public partial class ReadSurface
{
    // The case for promotion as the word checks name it: held to the Improvement loop's four rules, and a page of its own
    // beside the Loop page, whose cards the plain-words check reads by the Loop page's regions.
    internal const string CasePage = "The case for promotion";

    // Section 15.27's three regions and section 18's row of a press on a case.
    internal static string[] PromotionCaseRows =>
    [
        CheckReach.Key("15.27 The case for promotion", "Every running revision against its variants"),
        CheckReach.Key("15.27 The case for promotion", "A case"),
        CheckReach.Key("15.27 The case for promotion", "No case"),
        CheckReach.Key(Scope.FailureTable, "A press on a case for promotion"),
    ];

    static readonly Regex CodeWordsDrawn = new(@"<p class=""ai-quote"" data-words=""code"">(?<words>[^<]*)<small>", RegexOptions.CultureInvariant);

    [Fact]
    public async Task APressOnACaseWritesNothingOnTheFreezesLastDayOrOnTheDayItEnds()
    {
        using var store = await FamilyPagesStore();

        string Written()
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT (SELECT COUNT(*) FROM candidate_register) || '|' || (SELECT COUNT(*) FROM loop_decision) || '|' || (SELECT COUNT(*) FROM loop_applied) || '|' || (SELECT COUNT(*) FROM promotion_press);";

            return (string)command.ExecuteScalar()!;
        }

        var before = Written();
        const string Frozen = "the rules are frozen until the monthly run of 6 Feb 2027, so an approval changes no rule before it";

        using (var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2027, 2, 5, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)))
        using (var client = host.CreateClient())
        {
            var page = await client.GetStringAsync("/screens/loop/case");

            Assert.Contains("data-title=\"The case for promotion\"", page, StringComparison.Ordinal);
            Assert.Contains("Every running revision against its variants, over the same nights", page, StringComparison.Ordinal);
            Assert.Contains("<p class=\"lm-freeze\" data-frozen=\"2027-02-06\">", page, StringComparison.Ordinal);
            Assert.Contains($"Nothing was promoted: {Frozen}, so no press on a case changes a rule before it.", (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/pullback", [("variant", "any"), ("press", "promote")])).Said, StringComparison.Ordinal);
            Assert.Contains($"Nothing was kept: {Frozen}", (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/pullback", [("variant", "any"), ("press", "keep")])).Said, StringComparison.Ordinal);
            Assert.Equal(before, Written());
        }

        using (var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2027, 2, 6, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)))
        using (var client = host.CreateClient())
        {
            Assert.DoesNotContain("data-frozen", await client.GetStringAsync("/screens/loop/case"), StringComparison.Ordinal);
            // After the freeze a Promote stands only on a variant whose case stands at the newest monthly run, and an Undo only
            // on a promotion pressed; neither writes anything otherwise.
            Assert.Contains($"Nothing was promoted: {EquityBrief.Api.Reading.ReadApi.NoCase}.", (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/pullback", [("variant", "any"), ("press", "promote")])).Said, StringComparison.Ordinal);
            Assert.Contains($"Nothing was undone: {EquityBrief.Api.Reading.ReadApi.NothingToUndo}.", (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/pullback", [("variant", "any"), ("press", "undo"), ("undoes", "1")])).Said, StringComparison.Ordinal);
            Assert.Equal(before, Written());
        }
    }

    [Fact]
    public void TheMenuNamesTheCaseForPromotionUnderTheImprovementLoop()
    {
        var shell = new SinglePageApp().Shell("T");

        Assert.Contains($"<button type=\"button\" class=\"m-group-btn\" aria-expanded=\"false\">{MarkRenderer.CheckUpName}</button><div class=\"m-drop\"><a href=\"{SinglePageApp.LoopRoute}\" data-view=\"loop\">This week", shell, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{SinglePageApp.CaseRoute}\" data-view=\"case\">The case for promotion</a>", shell, StringComparison.Ordinal);
        Assert.Contains("fetch('/screens/loop/case')", shell, StringComparison.Ordinal);
    }

    // A constructed case: the S&P 600's pullback's variant buying only where its sector is rising, 54 closed at +$0.21 against
    // revision 7's 61 at +$0.06 over the same nights from 21 Nov 2026, ahead over the long history, two picks of revision 7's
    // tonight of which it keeps one.
    internal static PromotionCaseView ConstructedCase()
    {
        var from = new DateOnly(2026, 11, 23);
        var night = new DateOnly(2027, 2, 5);
        var same = new PromotionRecord(from, night, 60, 54, 0.21, 2);
        var ran = new PromotionRecord(from, night, 100, 61, 0.06, 2);

        return new PromotionCaseView(
            "SML",
            "S&P 600",
            "pullback",
            false,
            7,
            new DateOnly(2026, 11, 16),
            "pullback-variant-sector",
            "Only where its sector is rising",
            ["It buys only where the stock's sector is rising, its sector fund above its 20-day average"],
            same,
            ran,
            new PromotionLong(0.09, 900),
            new PromotionLong(0.03, 1400),
            PromotionRule.Judge(false, same, ran, new PromotionLong(0.09, 900), new PromotionLong(0.03, 1400)),
            50,
            40,
            ["ABCD"],
            ["ABCD", "EFGH"]);
    }

    // On the operator's word of 2026-11-22, item 8: each long-history figure says which month's long test it comes from, with
    // the day the run started, and the stored figures carry the run, so a later long test changes the figures; a case no long
    // test judged names none.
    [Fact]
    public void ACasesLongHistoryFigureNamesTheLongTestItCameFromAndItsStoredFiguresCarryTheRun()
    {
        var one = ConstructedCase() with { LongTestMonth = "2026-11", LongTestRun = "loop-rules-long-SML-20261109T095723Z" };
        PromotionRowView[] rows = [new("SML", "S&P 600", "pullback", false, 7, new DateOnly(2026, 11, 16), one.RunningSame, [one])];
        var page = new SinglePageApp().CaseRegion(new PromotionPageView(new DateOnly(2027, 2, 5), null, rows, 1214, 3));

        Assert.Equal("From the long test of November 2026, run on 9 Nov 2026", SinglePageApp.LongTestLine(one));
        Assert.Contains("<small data-part=\"long-test\">From the long test of November 2026, run on 9 Nov 2026</small>", page, StringComparison.Ordinal);
        Assert.Null(SinglePageApp.LongTestLine(ConstructedCase()));
        Assert.DoesNotContain("data-part=\"long-test\"", new SinglePageApp().CaseRegion(new PromotionPageView(new DateOnly(2027, 2, 5), null, [rows[0] with { Variants = [ConstructedCase()] }], 1214, 3)), StringComparison.Ordinal);

        var stored = PromotionCases.Figures(one);
        var read = PromotionCases.FromFigures(stored)!;

        Assert.Equal("loop-rules-long-SML-20261109T095723Z", read.LongTestRun);
        Assert.Equal("2026-11", read.LongTestMonth);
        Assert.NotEqual(PromotionCases.FiguresKey(stored), PromotionCases.FiguresKey(PromotionCases.Figures(one with { LongTestRun = "loop-rules-long-SML-20261205T120000Z", LongTestMonth = "2026-12" })));
        Assert.Null(PromotionCases.FromFigures(PromotionCases.Figures(ConstructedCase()))!.LongTestRun);

        // The long test a case reads is the one the index's long rule rows were stored by, its month and its run.
        RuleTestRow[] longRules = [new("loop-rules-long-SML-20261109T095723Z", "2026-11", "pullback", "the rule", "live", "words", "against", "per $1", 900, 12, 0.09, true, true, 12, 9, 0.08, true, true, null)];

        Assert.Equal(("2026-11", "loop-rules-long-SML-20261109T095723Z"), PromotionCases.LongTestOf(longRules));
        Assert.Null(PromotionCases.LongTestOf([]));
    }

    [Fact]
    public void ACaseDrawsTheOperatorsTitleCodesFiguresAndWordsHoldingNoDigitAndItsButtonsRefusedInsideTheFreeze()
    {
        var one = ConstructedCase();
        PromotionRowView[] rows = [new("SML", "S&P 600", "pullback", false, 7, new DateOnly(2026, 11, 16), one.RunningSame, [one])];
        var frozen = new SinglePageApp().CaseRegion(new PromotionPageView(new DateOnly(2027, 2, 5), new DateOnly(2027, 2, 6), rows, 1214, 3));

        Assert.Equal($"{MarkRenderer.FamilyName("pullback")}, S&P 600: the variant 'only where its sector is rising' against revision 7", SinglePageApp.CaseTitle(one));
        Assert.Contains("This variant becomes revision 8; revision 7 runs on as a variant, each keeping its own record.", frozen, StringComparison.Ordinal);
        Assert.Contains("+$0.21 over 54 closed", frozen, StringComparison.Ordinal);
        Assert.Contains("+$0.06 over 61 closed", frozen, StringComparison.Ordinal);
        Assert.Contains("Each condition: beat revision 7 over the same nights, yes; over the long history, yes; 50 closed trades, yes, 54.", frozen, StringComparison.Ordinal);
        Assert.Contains("<b>Counted by code:</b> 1,214 ideas tested to date, 3 passed.", frozen, StringComparison.Ordinal);
        Assert.Contains("It is 4 trades past the 50.", frozen, StringComparison.Ordinal);
        Assert.Contains("Tonight it would have kept 1 of revision 7's 2 picks, ABCD, and left out EFGH.", frozen, StringComparison.Ordinal);
        Assert.Contains("Fewer picks, 1.2 a night against 2.0 over the same 50 nights. 40 of its 60 picks were revision 7's picks too.", frozen, StringComparison.Ordinal);
        Assert.Contains("<button type=\"submit\" disabled>Promote</button>", frozen, StringComparison.Ordinal);
        Assert.Contains("<button type=\"submit\" class=\"btn-2\" disabled>Keep revision 7</button>", frozen, StringComparison.Ordinal);

        // The case for and the case against in code's words, each read off the page, hold no digit; the counted part sits
        // above the case against's words.
        string[] words = [.. CodeWordsDrawn.Matches(frozen).Select(match => match.Groups["words"].Value)];

        Assert.Equal(2, words.Length);
        Assert.All(words, said => Assert.DoesNotMatch("[0-9]", said));
        Assert.True(frozen.IndexOf("Counted by code:", StringComparison.Ordinal) < frozen.IndexOf(SinglePageApp.CodeAgainst, StringComparison.Ordinal));

        var open = new SinglePageApp().CaseRegion(new PromotionPageView(new DateOnly(2027, 2, 6), null, rows, 1214, 3));

        Assert.Contains("<button type=\"submit\">Promote</button>", open, StringComparison.Ordinal);
        Assert.DoesNotContain("lm-freeze", open, StringComparison.Ordinal);
        Assert.Empty(Checks.RevisionPages.Faults(frozen));
    }

    [Fact]
    public void WithNoCaseThePageSaysSoAndNamesTheNearestMisses()
    {
        var one = ConstructedCase();
        var short22 = one with { Same = one.Same with { Closed = 22 }, Conditions = one.Conditions with { Enough = false } };
        PromotionRowView[] rows = [new("SML", "S&P 600", "pullback", false, 7, new DateOnly(2026, 11, 16), one.RunningSame, [short22])];
        var page = new SinglePageApp().CaseRegion(new PromotionPageView(new DateOnly(2027, 2, 5), null, rows, 1214, 3));

        Assert.Contains("No variant meets the rule this month. The nearest is:", page, StringComparison.Ordinal);
        Assert.Contains($"{MarkRenderer.FamilyName("pullback")}, S&amp;P 600, variant of revision 7: only where its sector is rising, 22 of its 50 trades closed, ahead of revision 7 over the same nights.", page, StringComparison.Ordinal);
        Assert.Contains("Not yet: 22 of 50 trades.", page, StringComparison.Ordinal);
        Assert.Empty(Checks.RevisionPages.Faults(page));
    }
}
