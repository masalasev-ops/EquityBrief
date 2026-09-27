using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// Section 15.10's shadow candidates region, from 8.4.
//
// The region states how many candidate conditions are registered and the
// divisor that number sets, and says each candidate's record is withheld until
// it is promoted. The half that matters more is what it does not state: no
// evaluation of a name reaches this region or any other, because seeing a
// candidate's record before it is promoted is the thing pre-registration exists
// to prevent, and a page that drew one would make the register a formality.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public partial class ReadSurface
{
    static readonly DateTimeOffset Registered = new(2026, 9, 7, 21, 0, 0, TimeSpan.Zero);

    static async Task RegisterForTheRegionAsync(TemporaryStore store, string candidate, double level, DateTimeOffset at)
    {
        var outcome = await new CandidateRegistrar(
            FixedClock.At(at, SessionZones.UnitedStates),
            store.DatabaseFile).RegisterAsync(
                candidate,
                "the relative strength index at or below the level",
                "the share of its setups that beat their own break-even",
                MomentumIndexReading.EvaluatorName,
                new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = level },
                "shadow-region-" + candidate.Replace(' ', '-'));

        Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
    }

    // The region as the run page reads it at an instant, over the register, the nights and the setups the
    // store holds.
    static async Task<ShadowRegion> ShadowAsync(ReadApi api, DateTimeOffset at) =>
        RunScreen.Shadow(
            await api.RegisteredCandidatesAsync(),
            await api.CandidateNightsAsync(),
            await api.CandidateSetupsAsync(),
            DateOnly.FromDateTime(at.UtcDateTime),
            at);

    [Fact]
    public async Task TheShadowRegionStatesHowManyAreRegisteredAndTheDivisorThatNumberSets()
    {
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);

        // Nothing registered: the region says so with the maximum the family may
        // reach, rather than drawing an empty row a reader would read as a night
        // that produced nothing.
        var empty = new MarkRenderer().ShadowCandidates(await ShadowAsync(api, Registered.AddDays(2)));

        Assert.Contains("data-shadow=\"0\"", empty, StringComparison.Ordinal);
        Assert.Contains("no candidate condition is registered", empty, StringComparison.Ordinal);
        Assert.Contains("at most 8", empty, StringComparison.Ordinal);
        Assert.Contains("<p data-trials=\"0\">no distinct trial is counted", empty, StringComparison.Ordinal);

        await RegisterForTheRegionAsync(store, "momentum index at thirty", 30, Registered);
        await RegisterForTheRegionAsync(store, "momentum index at twenty", 20, Registered.AddMinutes(1));

        var two = new MarkRenderer().ShadowCandidates(
            RunScreen.Shadow(
                await api.RegisteredCandidatesAsync(),
                await api.CandidateNightsAsync(),
                await api.CandidateSetupsAsync(),
                DateOnly.FromDateTime(Registered.AddDays(2).UtcDateTime),
                Registered.AddDays(2)));

        // The count and the divisor are the same number here, the family being what stands registered,
        // and the trials are two as well, two rules still running.
        Assert.Contains("data-shadow=\"2\"", two, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"2\"", two, StringComparison.Ordinal);
        Assert.Contains("2 candidate condition(s) registered as this page is read, of at most 8, and the family's divisor is 2", two, StringComparison.Ordinal);
        Assert.Contains("data-trials=\"2\" data-level=\"0.025\"", two, StringComparison.Ordinal);
        Assert.Contains("withheld until it is promoted", two, StringComparison.Ordinal);

        // A retirement moves the divisor down, which is what the region is for:
        // a reader comparing a verdict against it needs the number as it stands
        // now rather than the number the family once held. Taken with no result
        // read, it moves the count of trials down with it.
        var withdrawn = await new CandidateRegistrar(
            FixedClock.At(Registered.AddHours(1), SessionZones.UnitedStates),
            store.DatabaseFile).RetireAsync(
                "momentum index at thirty",
                "0 resolved setups of a minimum of 250",
                "shadow-region-retire");

        Assert.Equal(CandidateRegistrar.Retired, withdrawn.Outcome);

        var one = new MarkRenderer().ShadowCandidates(await ShadowAsync(api, Registered.AddDays(2)));

        Assert.Contains("data-shadow=\"1\"", one, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"1\"", one, StringComparison.Ordinal);
        Assert.Contains("the family's divisor is 1", one, StringComparison.Ordinal);
        Assert.Contains("data-trials=\"1\" data-level=\"0.05\"", one, StringComparison.Ordinal);

        // A single trial's first look releases more than the 1 in 256 eight blocks can reach, so the
        // region does not say the first look cannot promote.
        Assert.DoesNotContain("never promote one", one, StringComparison.Ordinal);

        // The two figures are computed apart and drawn from their own fields, so a divisor that is not the count shows.
        foreach (var (at, count) in new[] { (Registered.AddMinutes(-1), 0), (Registered.AddSeconds(30), 1), (Registered.AddMinutes(30), 2), (Registered.AddDays(2), 1) })
        {
            var region = await ShadowAsync(api, at);

            Assert.Equal((at, count, count), (at, region.Registered, region.Divisor));
        }

        var apart = new MarkRenderer().ShadowCandidates(new ShadowRegion(2, 3, 8, 6, 0.05 / 6, [0.00019, 0.00232, 0.05 / 6], false));

        Assert.Contains("data-shadow=\"2\"", apart, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"3\"", apart, StringComparison.Ordinal);
        Assert.Contains("2 candidate condition(s) registered", apart, StringComparison.Ordinal);
        Assert.Contains("the family's divisor is 3", apart, StringComparison.Ordinal);
        Assert.Contains("data-trials=\"6\"", apart, StringComparison.Ordinal);

        // The empty region names the candidate family, since the live reasons' threshold is divided by their own.
        Assert.Contains("so the family's divisor is none", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("no threshold is divided", empty, StringComparison.Ordinal);
    }

    // The register as the operator's store held it on 2026-09-27, its 38 rows written back as the
    // commands wrote them: phase 10's three registered and retired unread, the live filter and its five
    // variants of version 1, version 2's live filter, every re-registration with only its version moved,
    // and the six of version 3 standing. Sixteen names are fifteen rules, and the count of distinct trials
    // is six: no candidate has reached a look, every retirement was taken unread, and the six standing are
    // six rules. Beside the family's divisor the region draws the six and the bar they set, each worked by
    // hand: 0.05 over 6 is 0.00833, of which the first look at half the information releases 0.00019, below
    // the 1 in 256 eight blocks can reach, the second 0.00232 and the last the whole 0.00833.
    [Fact]
    public void TheLiveRegistersThirtyEightRowsAreSixDistinctTrialsDrawnBesideTheDivisor()
    {
        var register = TheLiveRegister();
        var at = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        var region = RunScreen.Shadow(register, [], [], new DateOnly(2026, 9, 27), at);

        Assert.Equal(38, register.Length);

        string[] names = [.. register.Where(row => row.Event == CandidateFamily.Registered).Select(row => row.Candidate).Distinct(StringComparer.Ordinal)];
        RegisterRow[] rows = [.. register.Select(row => new RegisterRow(row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator, row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence))];

        Assert.Equal((16, 15), (names.Length, CandidateFamily.Trials(rows, names)));
        Assert.Equal((6, 6, 6), (region.Registered, region.Divisor, region.Trials));
        Assert.Equal(0.05 / 6, region.Level!.Value, 12);
        Assert.False(region.FirstLookPromotes);

        var drawn = new MarkRenderer().ShadowCandidates(region);

        Assert.Contains(
            "<p data-trials=\"6\" data-level=\"0.00833\" data-releases=\"0.00019, 0.00232, 0.00833\">6 distinct trial(s) counted, the rules still running and the rules a look has read, " +
            "so the level at Holm's first step is 0.05 over 6, 0.00833, which a candidate's looks release as 0.00019 by the first, 0.00232 by the second and 0.00833 by the last, " +
            "the first below the 1 in 256 that eight blocks can reach, so it can retire a candidate and never promote one</p>",
            drawn,
            StringComparison.Ordinal);

        // Beside the divisor: the divisor's line comes first and the count's after it, in one region.
        Assert.True(
            drawn.IndexOf("the family's divisor is 6", StringComparison.Ordinal) < drawn.IndexOf("data-trials=\"6\"", StringComparison.Ordinal),
            "The count of trials is not drawn beside the divisor.");

        // A seventh rule registered beside the six makes seven trials and lowers every look's release.
        var seventh = RunScreen.Shadow(
            [.. register, new CandidateRow(39, "the seventh", SwingFilterRule.EvaluatorName, CandidateFamily.Registered, null, at.AddHours(-1), Filter(breadth: 0.45, trade: 2, rewardToRisk: 2.5), null)],
            [],
            [],
            new DateOnly(2026, 9, 27),
            at);

        Assert.Equal(7, seventh.Trials);
        Assert.Equal(0.05 / 7, seventh.Level!.Value, 12);
        Assert.True(seventh.Releases[0] < region.Releases[0] && seventh.Releases[1] < region.Releases[1]);

        // The same rule registered again under a new name after a correction moves neither.
        var again = RunScreen.Shadow(
            [.. register, new CandidateRow(39, "the live swing filter, version 3, again", SwingFilterRule.EvaluatorName, CandidateFamily.Registered, null, at.AddHours(-1), Filter(breadth: 0.45, trade: 2), null)],
            [],
            [],
            new DateOnly(2026, 9, 27),
            at);

        Assert.Equal(6, again.Trials);
    }

    // The swing filter's parameters as a registration writes them, version 1's settings unless named.
    static string Filter(double breadth = 0.5, int trade = 1, double depthHigh = 5, int marketGate = 1, double rewardToRisk = 1.5, double strength = 0.5, int arrival = 3) =>
        CandidateEvaluator.Write(new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["arrivalSessions"] = arrival,
            ["breadthFloor"] = breadth,
            ["breakoutVolumeMultiple"] = 1.5,
            ["depthHigh"] = depthHigh,
            ["depthLow"] = 1,
            ["dryUpCeiling"] = 1.5,
            ["earningsWindowSessions"] = 15,
            ["marketGate"] = marketGate,
            ["rewardToRiskFloor"] = rewardToRisk,
            ["stopHigh"] = 4,
            ["stopLow"] = 0.5,
            ["strengthFloor"] = strength,
            ["tightnessCeiling"] = 0.7,
            ["trade"] = trade,
        });

    static CandidateRow[] TheLiveRegister()
    {
        var phaseTen = new DateTimeOffset(2026, 9, 23, 15, 52, 25, TimeSpan.Zero);
        var family = new DateTimeOffset(2026, 9, 25, 10, 39, 49, TimeSpan.Zero);
        var versionTwo = new DateTimeOffset(2026, 9, 25, 14, 4, 7, TimeSpan.Zero);
        var moved = new DateTimeOffset(2026, 9, 26, 12, 51, 26, TimeSpan.Zero);
        var versionThree = new DateTimeOffset(2026, 9, 27, 4, 18, 30, TimeSpan.Zero);
        const string Unread = "retired when the swing family registered; no result of this candidate was read before it was retired";
        const string Restarting = "a rule correction opening filter version 3, restarting 0 non-empty block(s)";
        const string Pins = "the 3.4 correction's touches and band width moved every evaluator's version";
        var swing = SwingFilterRule.EvaluatorName;

        (string Name, string Parameters)[] versionOne =
        [
            ("the live swing filter, version 1", Filter()),
            ("the swing filter at a reward to risk of 2", Filter(rewardToRisk: 2)),
            ("the swing filter at a pullback of 1 to 3 typical moves", Filter(depthHigh: 3)),
            ("the swing filter with the market gate off", Filter(marketGate: 0)),
            ("the swing filter with strength in the top third", Filter(strength: 2d / 3)),
            ("the swing filter with one-session arrival", Filter(arrival: 1)),
        ];

        (string Name, string Parameters)[] carried =
        [
            ("the live swing filter, version 2", Filter(breadth: 0.45)),
            .. versionOne.Skip(1),
        ];

        (string Name, string Parameters)[] versionThreeSix =
        [
            ("the live swing filter, version 3", Filter(breadth: 0.45, trade: 2)),
            ("the swing filter on the plan at the nearest bands, from version 3", Filter(breadth: 0.45)),
            ("the swing filter at a pullback of 1 to 3 typical moves, from version 3", Filter(breadth: 0.45, trade: 2, depthHigh: 3)),
            ("the swing filter with the market gate off, from version 3", Filter(breadth: 0.45, trade: 2, marketGate: 0)),
            ("the swing filter with strength in the top third, from version 3", Filter(breadth: 0.45, trade: 2, strength: 2d / 3)),
            ("the swing filter with one-session arrival, from version 3", Filter(breadth: 0.45, trade: 2, arrival: 1)),
        ];

        var rows = new List<CandidateRow>
        {
            new(1, "arrived and narrow", ArrivedAndNarrow.EvaluatorName, CandidateFamily.Registered, null, phaseTen, "{\"width\": 1}", null),
            new(2, "volume against the night", VolumeAgainstTheNight.EvaluatorName, CandidateFamily.Registered, null, phaseTen, "{\"multiple\": 2}", null),
            new(3, "crossed by a margin", CrossedByAMargin.EvaluatorName, CandidateFamily.Registered, null, phaseTen, "{\"margin\": 0.5}", null),
        };

        void Retire(string name, DateTimeOffset when, string evidence, string? evaluator = null) =>
            rows.Add(new(rows.Count + 1, name, evaluator ?? swing, CandidateFamily.Retired, name, when, "{}", evidence));

        void Register((string Name, string Parameters) rule, DateTimeOffset when) =>
            rows.Add(new(rows.Count + 1, rule.Name, swing, CandidateFamily.Registered, null, when, rule.Parameters, null));

        foreach (var retired in rows.ToArray())
        {
            Retire(retired.Candidate, family, Unread, retired.Evaluator);
        }

        foreach (var rule in versionOne)
        {
            Register(rule, family);
        }

        Retire("the live swing filter, version 1", versionTwo, "a shape acceptance opening filter version 2, restarting 0 non-empty block(s)");
        Register(carried[0], versionTwo);

        foreach (var rule in carried)
        {
            Retire(rule.Name, moved, Pins);
        }

        foreach (var rule in carried)
        {
            Register(rule, moved);
        }

        foreach (var rule in carried)
        {
            Retire(rule.Name, versionThree, Restarting);
        }

        foreach (var rule in versionThreeSix)
        {
            Register(rule, versionThree);
        }

        return [.. rows];
    }

    [Fact]
    public async Task APastNightsRunPageStatesTheRegisterAsThePageIsRead()
    {
        using var store = await FixtureExpectations.WithListings();

        await RegisterForTheRegionAsync(store, "momentum index at thirty", 30, Registered);
        await RegisterForTheRegionAsync(store, "momentum index at twenty", 20, Registered.AddMinutes(1));

        var withdrawn = await new CandidateRegistrar(
            FixedClock.At(Registered.AddDays(3), SessionZones.UnitedStates),
            store.DatabaseFile).RetireAsync("momentum index at thirty", "0 resolved setups of a minimum of 250", "shadow-region-past-night");

        Assert.Equal(CandidateRegistrar.Retired, withdrawn.Outcome);

        // One night's page read before the retirement and after it: the count follows the reading, not the night.
        foreach (var (readAt, standing) in new[] { (Registered.AddDays(2), 2), (Registered.AddDays(5), 1) })
        {
            using var host = new ClockedHost(store.Root, FixedClock.At(readAt, SessionZones.UnitedStates));
            using var client = host.CreateClient();

            var run = await client.GetStringAsync("/screens/run/2026-09-08");

            Assert.Contains("data-night=\"2026-09-08\"", run, StringComparison.Ordinal);
            Assert.Contains($"data-shadow=\"{standing}\"", run, StringComparison.Ordinal);
            Assert.Contains($"{standing} candidate condition(s) registered as this page is read", run, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task NoScreenCarriesAShadowEvaluationOfAName()
    {
        // The done condition's third half, asserted over the surfaces rather than
        // by reading the projection. The store holds shadow evaluations for every
        // name on the night, each naming the candidate and the values it was
        // evaluated on, and not one of those reaches any page.
        using var store = await FixtureExpectations.WithListings();

        await RegisterForTheRegionAsync(store, "momentum index at one hundred", 100, Registered);

        Insert(store, "DELETE FROM listing;");

        await new Worker.Shortlist.ShortlistBuilder(
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "shadow-screens", new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero));

        // The store carries them, which is what makes the absence below a
        // statement about the screens rather than about an empty column.
        var stored = Rows(store, "SELECT shadow_reasons FROM listing;").Select(row => row[0]).ToArray();

        Assert.True(stored.Length >= 4, $"Read {stored.Length} listing row(s), expected at least 4.");
        Assert.All(stored, row => Assert.Contains("momentum index at one hundred", row, StringComparison.Ordinal));

        var names = Rows(store, "SELECT ticker FROM listing ORDER BY ticker;").Select(row => row[0]).ToArray();

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Every route the app serves, so the claim is about the screens and not
        // about the one page the region happens to sit on. From 10.2 the run page names a
        // registered candidate in its record region, which is the candidate's own record and
        // never an evaluation of a name, so that page is read apart below.
        foreach (var route in new[] { "/screens/tonight", "/screens/universe" })
        {
            var body = await client.GetStringAsync(route);

            Assert.DoesNotContain("momentum index at one hundred", body, StringComparison.Ordinal);
            Assert.DoesNotContain("shadow_reasons", body, StringComparison.Ordinal);

            // And no name is paired with a shadow evaluation, which is the shape
            // a region drawing the column would take.
            foreach (var ticker in names)
            {
                Assert.DoesNotContain($"{ticker}\" data-shadow", body, StringComparison.Ordinal);
            }
        }

        foreach (var ticker in names)
        {
            var body = await client.GetStringAsync("/screens/name/" + ticker);

            Assert.DoesNotContain("momentum index at one hundred", body, StringComparison.Ordinal);
        }

        // The run page does draw the region, so the absence above is not the
        // absence of a page that failed to render.
        var run = await client.GetStringAsync("/screens/run/2026-09-08");

        Assert.Contains("class=\"shadow-candidates\"", run, StringComparison.Ordinal);
        Assert.Contains("data-shadow=\"1\"", run, StringComparison.Ordinal);
        Assert.DoesNotContain("shadow_reasons", run, StringComparison.Ordinal);

        // The candidate is named on the run page in its own record and nowhere else on it, and
        // no name is paired with a shadow evaluation there either: the column the night writes
        // for every member reaches no screen, which is the claim this test carries.
        var record = run[run.IndexOf("class=\"candidate-records\"", StringComparison.Ordinal)..];

        Assert.Contains("momentum index at one hundred", record[..record.IndexOf("</section>", StringComparison.Ordinal)], StringComparison.Ordinal);

        foreach (var ticker in names)
        {
            Assert.DoesNotContain($"{ticker}\" data-shadow", run, StringComparison.Ordinal);
            Assert.DoesNotContain($"{ticker}</td><td>momentum", run, StringComparison.Ordinal);
        }

        Assert.Contains("withheld until it is promoted", run, StringComparison.Ordinal);
    }
}
