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

    [Fact]
    public async Task TheShadowRegionStatesHowManyAreRegisteredAndTheDivisorThatNumberSets()
    {
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);

        // Nothing registered: the region says so with the maximum the family may
        // reach, rather than drawing an empty row a reader would read as a night
        // that produced nothing.
        var empty = new MarkRenderer().ShadowCandidates(
            RunScreen.Shadow(await api.RegisteredCandidatesAsync(), Registered.AddDays(2)));

        Assert.Contains("data-shadow=\"0\"", empty, StringComparison.Ordinal);
        Assert.Contains("no candidate condition is registered", empty, StringComparison.Ordinal);
        Assert.Contains("at most 8", empty, StringComparison.Ordinal);

        await RegisterForTheRegionAsync(store, "momentum index at thirty", 30, Registered);
        await RegisterForTheRegionAsync(store, "momentum index at twenty", 20, Registered.AddMinutes(1));

        var two = new MarkRenderer().ShadowCandidates(
            RunScreen.Shadow(await api.RegisteredCandidatesAsync(), Registered.AddDays(2)));

        // The count and the divisor are the same number, drawn once as both,
        // because the threshold is divided by the family and the family is what
        // stands registered. Two names for one fact would be one fact in two
        // places.
        Assert.Contains("data-shadow=\"2\"", two, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"2\"", two, StringComparison.Ordinal);
        Assert.Contains("2 candidate condition(s) registered", two, StringComparison.Ordinal);
        Assert.Contains("divided by 2", two, StringComparison.Ordinal);
        Assert.Contains("withheld until it is promoted", two, StringComparison.Ordinal);

        // A retirement moves the divisor down, which is what the region is for:
        // a reader comparing a verdict against it needs the number as it stands
        // now rather than the number the family once held.
        var withdrawn = await new CandidateRegistrar(
            FixedClock.At(Registered.AddHours(1), SessionZones.UnitedStates),
            store.DatabaseFile).RetireAsync(
                "momentum index at thirty",
                "0 resolved setups of a minimum of 250",
                "shadow-region-retire");

        Assert.Equal(CandidateRegistrar.Retired, withdrawn.Outcome);

        var one = new MarkRenderer().ShadowCandidates(
            RunScreen.Shadow(await api.RegisteredCandidatesAsync(), Registered.AddDays(2)));

        Assert.Contains("data-shadow=\"1\"", one, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"1\"", one, StringComparison.Ordinal);
        Assert.Contains("divided by 1", one, StringComparison.Ordinal);

        // The two figures are computed apart and drawn from their own fields, so a divisor that is not the count shows.
        foreach (var (at, count) in new[] { (Registered.AddMinutes(-1), 0), (Registered.AddSeconds(30), 1), (Registered.AddMinutes(30), 2), (Registered.AddDays(2), 1) })
        {
            var region = RunScreen.Shadow(await api.RegisteredCandidatesAsync(), at);

            Assert.Equal((at, count, count), (at, region.Registered, region.Divisor));
        }

        var apart = new MarkRenderer().ShadowCandidates(new ShadowRegion(Registered: 2, Divisor: 3, Maximum: 8));

        Assert.Contains("data-shadow=\"2\"", apart, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"3\"", apart, StringComparison.Ordinal);
        Assert.Contains("2 candidate condition(s) registered", apart, StringComparison.Ordinal);
        Assert.Contains("divided by 3", apart, StringComparison.Ordinal);

        // The empty region names the candidate family, since the live reasons' threshold is divided by their own.
        Assert.Contains("so no candidate's threshold is divided", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("no threshold is divided", empty, StringComparison.Ordinal);
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
        // about the one page the region happens to sit on.
        foreach (var route in new[] { "/screens/run/2026-09-08", "/screens/tonight", "/screens/universe" })
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
        Assert.Contains("withheld until it is promoted", run, StringComparison.Ordinal);
    }
}
