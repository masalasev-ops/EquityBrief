using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// Section 15.10's candidates' record region, from 10.2.
//
// What a reader sees of a registered candidate: the verdict field, the running figure labelled as
// monitoring beside it, what each look read, and the numbers the candidate was registered with.
// What they do not see is any name: a record is a count of setups and the sessions they were
// listed on, and the evaluation of a name stays in the column the night writes and no screen draws.
// see: The nightly running figure is monitoring and never the verdict
// see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow
public partial class ReadSurface
{
    const string Judged = "momentum index at thirty";

    static readonly DateOnly FirstNight = new(2025, 1, 2);

    [Fact]
    public void TheRecordRegionStatesEachLooksPowerAndTheNumbersTheCandidateWasRegisteredWith()
    {
        // Eight whole blocks, each holding one setup that won against a bar of a half, which is a
        // record at the first look and short of the two after it.
        var region = Region(WonInEachBlock(8), Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9));
        var drawn = new MarkRenderer().CandidateRecords(region);

        Assert.Contains("class=\"candidate-records\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-registered=\"1\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-looks=\"8, 12, 16\"", drawn, StringComparison.Ordinal);
        Assert.Contains($"data-candidate=\"{Judged}\"", drawn, StringComparison.Ordinal);

        // The verdict field: what the last look read, the looks left and what the next waits for.
        Assert.Contains("data-field=\"verdict\"", drawn, StringComparison.Ordinal);
        Assert.Contains(CandidateRecord.Crossed, drawn, StringComparison.Ordinal);
        Assert.Contains("2 look(s) remain, the next at 12 non-empty blocks", drawn, StringComparison.Ordinal);

        // The running figure is drawn as monitoring rather than as a verdict, with the tail that
        // assumes independence beside it, labelled and deciding nothing.
        Assert.Contains("Monitoring, not the verdict", drawn, StringComparison.Ordinal);
        Assert.Contains("data-monitoring=\"true\"", drawn, StringComparison.Ordinal);
        Assert.Contains("Poisson binomial, which assumes the setups are independent and decides nothing", drawn, StringComparison.Ordinal);

        // The look's own row: its blocks, the level it spent, and the smallest excess it could have
        // detected, which is the figure the power obligation is read on.
        Assert.Contains("data-look=\"8\"", drawn, StringComparison.Ordinal);
        Assert.Contains(
            FormattableString.Invariant($"data-spends=\"{Looks.Spent(region.Significance, Looks.Fraction(0)):0.######}\""),
            drawn,
            StringComparison.Ordinal);
        Assert.Contains("data-smallest-excess=\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-smallest-excess=\"none\"", drawn, StringComparison.Ordinal);

        // The numbers it was registered with, which a later reading may not quietly change.
        Assert.Contains("data-proposed=\"{&quot;level&quot;: 30}\"", drawn, StringComparison.Ordinal);
        Assert.Contains("a changed number is a new registration", drawn, StringComparison.Ordinal);

        // The step the graph stands at and the level it gives, beside the count ever registered.
        Assert.Contains("data-step=\"1\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-level=\"0.05\"", drawn, StringComparison.Ordinal);
        Assert.Contains("1 candidate condition(s) have ever been registered", drawn, StringComparison.Ordinal);

        // And the figures reported beside the verdict and tested nowhere.
        Assert.Contains("Reported and tested nowhere", drawn, StringComparison.Ordinal);
        Assert.Contains("data-same-session=\"0\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-earnings=\"0\"", drawn, StringComparison.Ordinal);
    }

    // The count ever registered is read against the count the per-window level is revisited at: below it
    // the line says when the revisit comes, and at it or past it, which the swing family's registration took
    // the store to with the three it retired, the line says the revisit is due and is the operator's, where
    // it read nine of at most eight. Section 13.6 names the candidates each window opened with, as many as
    // the code registers in each.
    [Fact]
    public void TheLifetimeCountIsReadAgainstTheCountItsRevisitIsDueAt()
    {
        var region = Region(WonInEachBlock(3), Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9));

        foreach (var (registered, due) in new[] { (7, false), (8, true), (9, true) })
        {
            var drawn = new MarkRenderer().CandidateRecords(region with { Registered = registered });
            var line = FormattableString.Invariant($"{registered} candidate condition(s) have ever been registered, against the 8 at which the per-window level is revisited");

            Assert.Contains(line + (due ? ": the count has reached it, so the revisit is due and is the operator's ruling. " : ". "), drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("of at most", drawn, StringComparison.Ordinal);
        }

        string[] words = ["none", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        var architecture = File.ReadAllText(Repository.Architecture);
        var first = Regex.Match(architecture, "<p data-phase=\"10\">The level at Holm's first step is (.*?);", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains($"the {words[TheThreeCandidates.All.Count]} registered on 2026-09-23", first, StringComparison.Ordinal);
        Assert.Contains($"the swing family's {words[TheSwingFamily.For("1", FilterSettings.Proposed).Count]}", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordBelowTheBlockFloorDrawsNoVerdictAndSaysHowFarItHasToGo()
    {
        var drawn = new MarkRenderer().CandidateRecords(Region(WonInEachBlock(3), Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9)));

        Assert.Contains($"data-withheld=\"{CandidateRecord.BelowTheBlockFloor}\"", drawn, StringComparison.Ordinal);
        Assert.Contains("no verdict is read below 8 non-empty blocks, and 3 stand", drawn, StringComparison.Ordinal);
        Assert.Contains(CandidateRecord.NoLookYet, drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-look=\"8\"", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageDrawsEachRegisteredCandidatesRecordAndNoTickerBesideOne()
    {
        using var store = await FixtureExpectations.WithListings();

        await RegisterForTheRegionAsync(store, Judged, 30, Registered);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync("/screens/run");

        Assert.Contains("class=\"candidate-records\"", page, StringComparison.Ordinal);
        Assert.Contains($"data-candidate=\"{Judged}\"", page, StringComparison.Ordinal);
        Assert.Contains("no evaluation of a name is drawn here or anywhere else", page, StringComparison.Ordinal);
        Assert.Contains("1 candidate condition(s) have ever been registered", page, StringComparison.Ordinal);

        // The region sits between the shadow count and the order comparison, which is where
        // section 15.10 puts it.
        Assert.True(
            page.IndexOf("class=\"shadow-candidates\"", StringComparison.Ordinal)
                < page.IndexOf("class=\"candidate-records\"", StringComparison.Ordinal)
                && page.IndexOf("class=\"candidate-records\"", StringComparison.Ordinal)
                    < page.IndexOf("class=\"tonights-order\"", StringComparison.Ordinal),
            "The run page drew its regions in another order than section 15.10 states.");

        // No name sits beside a record. The region is read out of the page and every ticker the
        // store holds is looked for inside it, which is the shape a region drawing an evaluation
        // would take.
        var from = page.IndexOf("class=\"candidate-records\"", StringComparison.Ordinal);
        var region = page[from..page.IndexOf("</section>", from, StringComparison.Ordinal)];

        foreach (var ticker in Rows(store, "SELECT DISTINCT ticker FROM listing ORDER BY ticker;").Select(row => row[0]))
        {
            Assert.DoesNotContain(ticker, region, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void APromotedCandidateLeavesTheFamilyByARetirementWhoseEvidenceSaysSo()
    {
        // Three candidates registered at one instant, one of them promoted: the promotion is a
        // retirement row whose evidence opens with the word the runbook's procedure writes, and the
        // level it held passes in equal shares to the two still standing. A retirement that says
        // anything else took the candidate out of the family without it having been shown, and its
        // level reaches nobody.
        var registered = Family();

        var nights = new[]
        {
            new CandidateNightRow(FirstNight, "a"),
            new CandidateNightRow(FirstNight, "b"),
            new CandidateNightRow(FirstNight, "c"),
        };

        DateOnly night = Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9);

        var standing = RunScreen.Candidates(registered, nights, [], night, Registered.AddYears(3));

        Assert.All(standing.Candidates, candidate => Assert.Equal(standing.Significance / 3, candidate.Level, 12));

        // The promotion, written as the runbook writes it.
        var promoted = registered.Append(new CandidateRow(
            4,
            "a",
            MomentumIndexReading.EvaluatorName,
            CandidateFamily.Retired,
            Retires: "a",
            Registered.AddYears(2),
            "{}",
            CandidateFamily.PromotedBy + " at the look of 12 blocks: 214 setups, 41.2% against a calibrated 37.0%")).ToArray();

        var passed = RunScreen.Candidates(promoted, nights, [], night, Registered.AddYears(3))
            .Candidates.ToDictionary(candidate => candidate.Candidate, StringComparer.Ordinal);

        Assert.False(passed["a"].Standing);
        Assert.True(passed["a"].Crossed);
        Assert.Equal(standing.Significance / 3 + (standing.Significance / 3 / 2), passed["b"].Level, 12);
        Assert.Equal(passed["b"].Level, passed["c"].Level, 12);

    }

    [Fact]
    public void ARetirementNamesTheRegistrationItRetiresAndOnlyAPromotedOnesLevelPasses()
    {
        // The row that takes a candidate out of the family names the registration it retires, and
        // the register keeps both: the retirement is a new row and the registration it names still
        // stands in the table behind it. A retirement that is not a promotion passes its level to
        // nobody, so the two candidates still standing hold what they opened with.
        var registered = Family();

        var nights = new[]
        {
            new CandidateNightRow(FirstNight, "a"),
            new CandidateNightRow(FirstNight, "b"),
            new CandidateNightRow(FirstNight, "c"),
        };

        DateOnly night = Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9);
        var opened = RunScreen.Candidates(registered, nights, [], night, Registered.AddYears(3));

        var retired = registered.Append(new CandidateRow(
            4,
            "a",
            MomentumIndexReading.EvaluatorName,
            CandidateFamily.Retired,
            Retires: "a",
            Registered.AddYears(2),
            "{}",
            "the futility guideline was met at the first look")).ToArray();

        var kept = RunScreen.Candidates(retired, nights, [], night, Registered.AddYears(3))
            .Candidates.ToDictionary(candidate => candidate.Candidate, StringComparer.Ordinal);

        Assert.Equal("a", retired[^1].Retires);
        Assert.False(kept["a"].Standing);
        Assert.False(kept["a"].Crossed);
        Assert.Equal(opened.Significance / 3, kept["b"].Level, 12);
        Assert.Equal(opened.Significance / 3, kept["c"].Level, 12);

        // The registration it retires is still in the register, which is what makes the retirement
        // a row rather than an edit.
        Assert.Contains(retired, row => row.Candidate == "a" && row.Event == CandidateFamily.Registered);
    }

    // Two windows, as the register has held them since the swing family registered: three candidates
    // first evaluated on one night and retired at the instant six more registered, and the six first
    // evaluated on a later night. Each window's first step is the level over the candidates it opened
    // with, 0.05 over 3 and 0.05 over 6, and never over the nine the register has ever held.
    [Fact]
    public void EachWindowsFirstStepIsTheLevelOverTheCandidatesItOpenedWith()
    {
        var retiredAt = Registered.AddDays(21);
        string[] six = ["d", "e", "f", "g", "h", "i"];

        CandidateRow[] registered =
        [
            .. Family(),
            .. Family().Select((row, at) => row with { Id = 4 + at, Event = CandidateFamily.Retired, Retires = row.Candidate, RegisteredAt = retiredAt, Evidence = "retired when six more registered" }),
            .. six.Select((candidate, at) => new CandidateRow(7 + at, candidate, MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, null, retiredAt, "{\"level\": 30}", null)),
        ];

        var later = FirstNight.AddDays(22);
        CandidateNightRow[] nights =
        [
            .. Family().Select(row => new CandidateNightRow(FirstNight, row.Candidate)),
            .. six.Select(candidate => new CandidateNightRow(later, candidate)),
        ];

        var region = RunScreen.Candidates(registered, nights, [], Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9), Registered.AddYears(3));
        var levels = region.Candidates.ToDictionary(candidate => candidate.Candidate, candidate => candidate.Level, StringComparer.Ordinal);

        Assert.Equal(9, region.Registered);
        Assert.Equal(6, region.Standing);
        Assert.All(["a", "b", "c"], candidate => Assert.Equal(0.05 / 3, levels[candidate], 12));
        Assert.All(six, candidate => Assert.Equal(0.05 / 6, levels[candidate], 12));
    }

    // Three candidates registered at one instant, which is the family the level is divided by.
    static CandidateRow[] Family() =>
    [
        new(1, "a", MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, Retires: null, Registered, "{\"level\": 30}", null),
        new(2, "b", MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, Retires: null, Registered, "{\"level\": 25}", null),
        new(3, "c", MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, Retires: null, Registered, "{\"level\": 20}", null),
    ];

    // One registered candidate and its setups, as the projection reads them off the store.
    static CandidateRegion Region(IReadOnlyList<CandidateSetupRow> setups, DateOnly night) =>
        RunScreen.Candidates(
            [new CandidateRow(1, Judged, MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, null, Registered, "{\"level\": 30}", null)],
            [new CandidateNightRow(FirstNight, Judged)],
            setups,
            night,
            Registered.AddYears(3));

    // One won setup in each of the first blocks, each listed on the block's own first session and
    // each against a bar of a half, so the excess is a half a block.
    static IReadOnlyList<CandidateSetupRow> WonInEachBlock(int blocks) =>
    [
        .. Enumerable.Range(0, blocks).Select(block => new CandidateSetupRow(
            Judged,
            Nights(block * EquityBrief.Core.Returns.Blocks.Sessions),
            ForwardReturnSeries.Win,
            0.5,
            0.51,
            40,
            8,
            5,
            false)),
    ];

    // The session that many exchange sessions after the record's first.
    static DateOnly Nights(int sessions)
    {
        var at = FirstNight;

        for (var counted = 0; counted < sessions;)
        {
            at = at.AddDays(1);

            if (ExchangeClosures.IsSession(at))
            {
                counted++;
            }
        }

        return at;
    }
}
