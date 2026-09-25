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

    // A candidate no night has evaluated has opened no window yet, and it reads the level over the
    // candidates standing beside it, the ones the next night evaluates with it, or, retired first, the
    // ones standing when it was retired. Each state below is worked by hand from the register.
    // The six registered at the instant the three retired, before any night evaluates them, while the
    // page's night holds the three's rows: the three at 0.05 over 3 and the six at 0.05 over 6, drawn
    // at the graph's first step. The six's first night moves no level. An acceptance retiring "d" and
    // registering "j": before either's first night all six read 0.05 over 6, "d" over the six standing
    // when it was retired; after the five and "d" were first evaluated on one night, "j" reads 0.05
    // over the six standing beside it and no level of the window before it moves. Each candidate reads
    // the window it opened with and no other: "e" promoted after the six's first night passes its 0.05
    // over 6 to the four of that window still standing, each then at 0.05 over 6 and a quarter of it
    // again at the graph's second, while "j", first evaluated with those four and not "e", reads 0.05
    // over 5. And a candidate retired, registered again and retired again reads the ones standing at
    // its last retirement, and one registered and retired in one second the ones standing before that
    // second, and itself.
    [Fact]
    public void ACandidateNoNightHasEvaluatedReadsTheLevelOverTheCandidatesStandingBesideIt()
    {
        var retiredAt = Registered.AddDays(21);
        var acceptedAt = Registered.AddDays(60);
        string[] six = ["d", "e", "f", "g", "h", "i"];
        string[] variants = ["e", "f", "g", "h", "i"];
        DateOnly night = Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9);
        var sixFirst = FirstNight.AddDays(22);
        var jFirst = FirstNight.AddDays(70);

        CandidateRow[] family =
        [
            .. Family(),
            .. Family().Select((row, at) => row with { Id = 4 + at, Event = CandidateFamily.Retired, Retires = row.Candidate, RegisteredAt = retiredAt, Evidence = "retired when six more registered" }),
            .. six.Select((candidate, at) => new CandidateRow(7 + at, candidate, MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, null, retiredAt, "{\"level\": 30}", null)),
        ];

        CandidateRow[] accepted =
        [
            .. family,
            new(13, "d", MomentumIndexReading.EvaluatorName, CandidateFamily.Retired, "d", acceptedAt, "{}", "retired by a shape acceptance"),
            new(14, "j", MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, null, acceptedAt, "{\"level\": 35}", null),
        ];

        CandidateNightRow[] threeRead = [.. Family().SelectMany(row => new[] { new CandidateNightRow(FirstNight, row.Candidate), new CandidateNightRow(night, row.Candidate) })];
        CandidateNightRow[] sixRead = [.. threeRead, .. six.Select(candidate => new CandidateNightRow(sixFirst, candidate))];

        Dictionary<string, CandidateRecordRow> Read(CandidateRow[] register, CandidateNightRow[] nights) =>
            RunScreen.Candidates(register, nights, [], night, Registered.AddYears(3))
                .Candidates.ToDictionary(candidate => candidate.Candidate, StringComparer.Ordinal);

        void Levels(Dictionary<string, CandidateRecordRow> read, IEnumerable<string> candidates, double level, int step = 1) =>
            Assert.All(candidates, candidate => Assert.Equal((Math.Round(level, 12), step), (Math.Round(read[candidate].Level, 12), read[candidate].Step)));

        // The six before any night evaluated them, the three's rows on the page's night.
        var unread = RunScreen.Candidates(family, threeRead, [], night, Registered.AddYears(3));
        var before = unread.Candidates.ToDictionary(candidate => candidate.Candidate, StringComparer.Ordinal);

        Levels(before, ["a", "b", "c"], 0.05 / 3);
        Levels(before, six, 0.05 / 6);

        // Drawn on the page, each of the six's own article reads the same level, matched on the whole
        // of its key and read up to the article's close.
        var drawn = new MarkRenderer().CandidateRecords(unread);

        Assert.All(six, candidate =>
        {
            var opening = $"<article class=\"candidate\" data-candidate=\"{candidate}\" ";
            var start = drawn.IndexOf(opening, StringComparison.Ordinal);

            Assert.True(start >= 0 && drawn.IndexOf(opening, start + 1, StringComparison.Ordinal) < 0, candidate);

            var article = drawn[start..drawn.IndexOf("</article>", start, StringComparison.Ordinal)];

            Assert.Contains("data-level=\"0.008333\" data-step=\"1\"", article, StringComparison.Ordinal);
            Assert.Contains($"Step {before[candidate].Step} of the graph, at a level of 0.00833 of the 0.05 the family is tested at.", article, StringComparison.Ordinal);
        });

        // The six's first night moves nothing.
        var after = Read(family, sixRead);

        Levels(after, ["a", "b", "c"], 0.05 / 3);
        Levels(after, six, 0.05 / 6);

        // An acceptance before any night evaluated the six: "d" retired, over the six standing when it
        // was, and "j" over the six standing beside it now.
        var acceptedUnread = Read(accepted, threeRead);

        Assert.False(acceptedUnread["d"].Standing);
        Levels(acceptedUnread, ["a", "b", "c"], 0.05 / 3);
        Levels(acceptedUnread, [.. six, "j"], 0.05 / 6);

        // An acceptance after the six's first night, "j" not yet evaluated.
        var acceptedAfter = Read(accepted, sixRead);

        Levels(acceptedAfter, ["a", "b", "c"], 0.05 / 3);
        Levels(acceptedAfter, [.. six, "j"], 0.05 / 6);

        // "e" promoted after the six's first night, and "j" first evaluated with the four of the six
        // still standing.
        CandidateRow[] promoted =
        [
            .. accepted,
            new(15, "e", MomentumIndexReading.EvaluatorName, CandidateFamily.Retired, "e", acceptedAt.AddDays(1), "{}", CandidateFamily.PromotedBy + " at the look of 12 blocks"),
        ];

        var ownWindows = Read(promoted, [.. sixRead, .. variants.Skip(1).Append("j").Select(candidate => new CandidateNightRow(jFirst, candidate))]);

        Assert.True(ownWindows["e"].Crossed);
        Levels(ownWindows, variants.Skip(1), 0.05 / 6 + (0.05 / 6 / 4), step: 2);
        Levels(ownWindows, ["d"], 0.05 / 6, step: 2);
        Levels(ownWindows, ["j"], 0.05 / 5);

        // "k" retired beside three and registered again, then retired beside four, before any night
        // evaluated it; "m" registered and retired in one second beside three.
        CandidateRow Written(long id, string candidate, string written, DateTimeOffset when) =>
            written == CandidateFamily.Retired
                ? new(id, candidate, MomentumIndexReading.EvaluatorName, CandidateFamily.Retired, candidate, when, "{}", "retired")
                : new(id, candidate, MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, null, when, "{\"level\": 30}", null);

        CandidateRow[] rejoined =
        [
            Written(1, "x", CandidateFamily.Registered, Registered),
            Written(2, "y", CandidateFamily.Registered, Registered),
            Written(3, "k", CandidateFamily.Registered, Registered),
            Written(4, "k", CandidateFamily.Retired, Registered.AddDays(1)),
            Written(5, "z", CandidateFamily.Registered, Registered.AddDays(2)),
            Written(6, "k", CandidateFamily.Registered, Registered.AddDays(3)),
            Written(7, "k", CandidateFamily.Retired, Registered.AddDays(4)),
            Written(8, "m", CandidateFamily.Registered, Registered.AddDays(5)),
            Written(9, "m", CandidateFamily.Retired, Registered.AddDays(5)),
        ];

        var again = Read(rejoined, []);

        Levels(again, ["x", "y", "z"], 0.05 / 3);
        Levels(again, ["k", "m"], 0.05 / 4);
        Assert.False(again["k"].Standing || again["m"].Standing);
    }

    // A standing candidate no night has evaluated reads the candidates standing at the page's instant,
    // which is not the reading a retired one takes. "k", retired before any night evaluated it and
    // registered again beside "x", "y" and "z", reads 0.05 over 4 with them, where the candidates
    // standing before its retirement were three. And a page read half a second after "z" registered
    // reads "z" standing and each of the three at 0.05 over 3, where the candidates standing before
    // that second were two. Each is at the graph's first step.
    [Fact]
    public void AStandingCandidateNoNightHasEvaluatedReadsTheCandidatesStandingAtThePagesInstant()
    {
        DateOnly night = Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9);

        CandidateRow Registration(long id, string candidate, DateTimeOffset when) =>
            new(id, candidate, MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, null, when, "{\"level\": 30}", null);

        CandidateRow[] back =
        [
            Registration(1, "x", Registered),
            Registration(2, "y", Registered),
            Registration(3, "k", Registered),
            new(4, "k", MomentumIndexReading.EvaluatorName, CandidateFamily.Retired, "k", Registered.AddDays(1), "{}", "retired"),
            Registration(5, "z", Registered.AddDays(2)),
            Registration(6, "k", Registered.AddDays(3)),
        ];

        var again = RunScreen.Candidates(back, [], [], night, Registered.AddYears(3)).Candidates;

        Assert.Equal(["k", "x", "y", "z"], again.Where(candidate => candidate.Standing).Select(candidate => candidate.Candidate));
        Assert.All(again, candidate => Assert.Equal((Math.Round(0.05 / 4, 12), 1), (Math.Round(candidate.Level, 12), candidate.Step)));

        CandidateRow[] justRegistered = [Registration(1, "x", Registered), Registration(2, "y", Registered), Registration(3, "z", Registered.AddDays(2))];

        var read = RunScreen.Candidates(justRegistered, [], [], night, Registered.AddDays(2).AddMilliseconds(500)).Candidates;

        Assert.Equal(["x", "y", "z"], read.Where(candidate => candidate.Standing).Select(candidate => candidate.Candidate));
        Assert.All(read, candidate => Assert.Equal((Math.Round(0.05 / 3, 12), 1), (Math.Round(candidate.Level, 12), candidate.Step)));
    }

    // The graph steps the promoted first, in the order their promotions were written, and then the
    // candidates whose records cross on one read, in the order they were registered, each case worked
    // by hand over three candidates registered together and first evaluated on one night. "b"
    // promoted a year before "a": "b" at the graph's first step at 0.05 over 3, "a" at its second at
    // 0.05 over 3 and half of it again, and "c" at its third holding the whole 0.05, where the name
    // order drew "a" first. "q" registered before "p", both crossing on one read: "q" first and "p"
    // second, where the name order puts "p" first. And "p" promoted while "q", registered before it,
    // crosses on the read: "p" first.
    [Fact]
    public void PromotedCandidatesStepInTheOrderTheirPromotionsWereWrittenAndCandidatesCrossingOnOneReadInTheOrderTheyWereRegistered()
    {
        DateOnly night = Nights(EquityBrief.Core.Returns.Blocks.Sessions * 9);

        CandidateRow Registration(long id, string candidate) =>
            new(id, candidate, MomentumIndexReading.EvaluatorName, CandidateFamily.Registered, null, Registered, "{\"level\": 30}", null);

        CandidateRow Promotion(long id, string candidate, DateTimeOffset when) =>
            new(id, candidate, MomentumIndexReading.EvaluatorName, CandidateFamily.Retired, candidate, when, "{}", CandidateFamily.PromotedBy + " at the look of 12 blocks");

        void Stepped(CandidateRegion region, string candidate, double level, int step, bool crossed)
        {
            var read = region.Candidates.Single(row => row.Candidate == candidate);

            Assert.Equal((Math.Round(level, 12), step, crossed), (Math.Round(read.Level, 12), read.Step, read.Crossed));
        }

        CandidateRow[] register = [Registration(1, "a"), Registration(2, "b"), Registration(3, "c"), Promotion(4, "b", Registered.AddYears(1)), Promotion(5, "a", Registered.AddYears(2))];

        var promoted = RunScreen.Candidates(
            register,
            [.. register.Where(row => row.Event == CandidateFamily.Registered).Select(row => new CandidateNightRow(FirstNight, row.Candidate))],
            [],
            night,
            Registered.AddYears(3));

        Stepped(promoted, "b", 0.05 / 3, 1, true);
        Stepped(promoted, "a", 0.05 / 3 + (0.05 / 3 / 2), 2, true);
        Stepped(promoted, "c", 0.05, 3, false);

        // Drawn on the page, each article matched on the whole of its key and read to its close.
        var drawn = new MarkRenderer().CandidateRecords(promoted);

        string Article(string candidate)
        {
            var opening = $"<article class=\"candidate\" data-candidate=\"{candidate}\" ";
            var start = drawn.IndexOf(opening, StringComparison.Ordinal);

            Assert.True(start >= 0 && drawn.IndexOf(opening, start + 1, StringComparison.Ordinal) < 0, candidate);

            return drawn[start..drawn.IndexOf("</article>", start, StringComparison.Ordinal)];
        }

        Assert.Contains("data-level=\"0.016667\" data-step=\"1\"", Article("b"), StringComparison.Ordinal);
        Assert.Contains("data-level=\"0.025\" data-step=\"2\"", Article("a"), StringComparison.Ordinal);

        // Two crossing on one read. A record crosses no earlier than its second look, at twelve blocks,
        // and twelve blocks run past the exchange closure table the tests place sessions by, so the order
        // is read off the order the page steps in, and the graph stepped over it with "q" and "p" each
        // crossing at a third of 0.05 or more.
        RegisterRow Row(CandidateRow row) =>
            new(row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator, row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence);

        RegisterRow[] both = [Row(Registration(1, "q")), Row(Registration(2, "p")), Row(Registration(3, "r"))];

        Assert.Equal(["q", "p", "r"], RunScreen.StepOrder(both, ["p", "q", "r"]));

        var onOneRead = HolmGraph.Levels(
                [.. RunScreen.StepOrder(both, ["p", "q", "r"]).Select(candidate => new GraphMember(candidate, false, false, level => candidate != "r" && level >= (0.05 / 3) - 1e-12))],
                0.05)
            .ToDictionary(level => level.Candidate, StringComparer.Ordinal);

        Assert.Equal((Math.Round(0.05 / 3, 12), 1, true), (Math.Round(onOneRead["q"].Level, 12), onOneRead["q"].Step, onOneRead["q"].Crossed));
        Assert.Equal((Math.Round(0.025, 12), 2, true), (Math.Round(onOneRead["p"].Level, 12), onOneRead["p"].Step, onOneRead["p"].Crossed));

        // "p" promoted while "q", registered before it, crosses on the read, whatever order they are
        // handed in.
        RegisterRow[] promotedFirst = [.. both, Row(Promotion(4, "p", Registered.AddYears(1)))];

        Assert.Equal(["p", "q", "r"], RunScreen.StepOrder(promotedFirst, ["q", "r", "p"]));
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
