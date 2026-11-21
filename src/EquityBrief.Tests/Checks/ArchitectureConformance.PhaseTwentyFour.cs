using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, phase 24 as it lands: each checkpoint's rows against its count in the plan and where every
// difference went, every row the phase added reached once and passing, and the actual inside the plan's range so far.
public partial class ArchitectureConformance
{
    // The rows each checkpoint of phase 24 landed so far, in the plan's order, and the rows each took out, none: none at 24.0;
    // none at 24.2, whose refusal landed as clauses of section 15.20's and section 18's rows that stood; 24.3's fact
    // writer and naming test, their stores, section 17's two rows and section 18's row; and 24.4's picker, its four stores,
    // section 14's step, section 17's menu and section 18's two rows; 24.5's section 17 row of the four accounts; and 24.6's
    // page, its five regions and Home's line of it; 24.7's section 18 row; and none at 24.10, the long search's live setting
    // read where its rule stores no range ceiling, landed ahead of 24.8 and 24.9 on the operator's word of 2026-11-20; and
    // 24.11's Run page row of the splits the share check does not read and section 18's row of their causes; and 24.13's
    // row of the judge's page on an empty record; and 24.14's section 17 row of the facts a judge pick names, section 18's row
    // of a pick refused for them and the judge's page's two rows of the facts named and a pick made before.
    static (string Checkpoint, string[] Added, string[] Taken)[] PhaseTwentyFourLanded =>
    [
        ("24.0", [], []),
        ("24.2", [], []),
        ("24.3", [.. FixtureExpectations.JudgeFactRows], []),
        ("24.4", [.. FixtureExpectations.JudgePickRows], []),
        ("24.5", [.. FixtureExpectations.JudgeAccountClaims], []),
        ("24.6", [.. Reading.ReadSurface.JudgePageRows], []),
        ("24.7", [.. FixtureExpectations.JudgeReplayClaims], []),
        ("24.10", [], []),
        ("24.11", [.. Reading.ReadSurface.SplitStepRows, .. FixtureExpectations.SplitCauseClaims], []),
        ("24.12", [.. FixtureExpectations.IncomeFallbackRows], []),
        ("24.13", [.. Reading.ReadSurface.JudgeEmptyRecordRows], []),
        ("24.14", [.. FixtureExpectations.JudgeNamedRows], []),
    ];

    // How many more rows each checkpoint landed than its count in the plan, a negative figure where it landed fewer. 24.0,
    // none as counted. 24.2, none of two: its refusal landed as clauses of two rows that stood. 24.3, nine of six: each
    // component's catalogue and matrix rows and each store a row of its own, section 17's settings two rows, and section 1's
    // line and the check's row landing as words where the plan counted them as rows. 24.4, ten of eight: each of its four
    // stores a row of its own where the plan counted them as one. 24.5, one of four: the accounts are worked by the core when
    // the judge's page is read, so their rows land with the page at 24.6 and the guide's words are no claim. 24.6, six of six.
    // 24.7, one of two: the replay is a verb over the judge's own components, so it lands as section 18's row and the
    // runbook's words where the plan counted a row of its own. 24.10, none as counted. 24.11, two of two. 24.12, four of four:
    // the fill's catalogue and matrix rows and its store's row where the plan counted the Run page's row and section 17's,
    // and section 18's row, the cards' line naming the line used landing as its words. 24.13, one of two: a replayed night drawn
    // labelled is 24.6's row, which stood. 24.14, four of four: the store's column landing as words in its store's row.
    static readonly (string Checkpoint, int More)[] PhaseTwentyFourMore =
    [
        ("24.0", 0),
        ("24.2", -2),
        ("24.3", 3),
        ("24.4", 2),
        ("24.5", -3),
        ("24.6", 0),
        ("24.7", -1),
        ("24.10", 0),
        ("24.11", 0),
        ("24.12", 0),
        ("24.13", -1),
        ("24.14", 0),
    ];

    [Fact]
    public void ThePhaseTwentyFourCheckpointsLandedSoFarAreCheckedAgainstThePlanWithEveryClaimThatMovedNamed()
    {
        // The pair and each landed checkpoint's count read off the plan, so the figures checked are the ones it carries.
        var plan = Corpus.Read("docs/BUILD_PLAN.md");
        var pair = Regex.Match(plan, @"([\d,]+)\s+claims\s+and\s+\1\s+PASS\s+on\s+main\s+at\s+[0-9a-f]{8}\.\s+After\s+24\.\d+\s+about\s+([\d,]+),\s+within\s+([\d,]+)\s+to\s+([\d,]+):");

        Assert.True(pair.Success, "The plan states no pair for phase 24.");

        int Figure(Match match, int group) => int.Parse(match.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        int Counted(string checkpoint)
        {
            if (Regex.IsMatch(plan, $@"-\s+none\s+at\s+{Regex.Escape(checkpoint)},"))
            {
                return 0;
            }

            var count = Regex.Match(plan, $@"-\s+about\s+(\d+)\s+at\s+{Regex.Escape(checkpoint)},");

            Assert.True(count.Success, $"The plan states no count at {checkpoint}.");

            return Figure(count, 1);
        }

        var (before, low, high) = (Figure(pair, 1), Figure(pair, 3), Figure(pair, 4));
        var landed = PhaseTwentyFourLanded;

        Assert.Equal(landed.Select(part => part.Checkpoint), PhaseTwentyFourMore.Select(part => part.Checkpoint));

        // What each checkpoint landed: its count in the plan and the rows it landed beyond it.
        int Net((string Checkpoint, string[] Added, string[] Taken) part) => part.Added.Length - part.Taken.Length;

        Assert.Equal(landed.Select((part, at) => Counted(part.Checkpoint) + PhaseTwentyFourMore[at].More), landed.Select(Net));

        // The actual past the pair's first figure by the net of what each checkpoint landed, and inside the plan's range by
        // the phase's end; so far at least the first figure.
        var actual = before + landed.Sum(Net);

        Assert.InRange(actual, before, high);

        // Every row the phase added reached once and passing, and the report holding them over what stood before it, none
        // out of scope and none unexamined.
        var rows = landed.SelectMany(part => part.Added).ToArray();
        var report = Report();

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.Equal((actual, 0, 0, actual), (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1632, 1700, 1800, 1670), (before, low, high, actual));
    }
}
