using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using TestVerdict = EquityBrief.Core.Loop.RuleVerdict;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.4: the S&P 500 pullback version leaving off a deteriorating business, registered again on the
// operator's ruling of 2026-10-30 to read the SEC's facts as first filed through the ledger's catalogue, walked by the
// per-rule test over a constructed history from its first session. Its trades are the live filter's less the listings of a
// stock whose filings read deteriorating on the listing's session, each state read through the catalogue's own function
// over the facts filed before that session; the version as it was registered before, reading the provider's quarters, is
// kept with the reading it lacks; and a run handing the walk no reading of the facts keeps the version with what it lacks.
// see: The seventh swing family candidate leaves off a member whose business the SEC's facts as first filed read deteriorating through the ledger's catalogue, and the nine are registered again at 21.4 with the eight unchanged versions keeping their records
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheVersionLeavingOffADeterioratingBusinessIsWalkedByThePerRuleTestOverTheListingsItsFiledStateKeeps()
    {
        using var store = new TemporaryStore().Migrated();
        var night = BusinessFirstHistory.Seed(store);
        var calendar = BusinessFirstHistory.Calendar(BusinessFirstHistory.Night + 1);
        var lastFiled = new DateOnly(2025, 11, 3);

        static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A fourth strong member, DDD, with AAA's bars and a filer whose two newest quarters shrank their sales on the same
        // quarter a year earlier and narrowed their operating margin, 90 and 80 against 100 and 100 with margins of 0.056
        // and 0.05 against 0.10, the newest filed on 2025-11-03: by hand its business reads deteriorating from the session
        // after that filing, and none before it, when one of its two newest quarters has no quarter a year earlier.
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'DDD', '2020-01-02', NULL, '2025-01-06T21:00:00Z');");

        for (var at = 0; at < calendar.Length; at++)
        {
            var bar = BusinessFirstHistory.Bar(calendar, "AAA", at);

            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                FormattableString.Invariant($"VALUES ('DDD', '{Stamp(bar.Session)}', '{bar.Open}', '{bar.High}', '{bar.Low}', '{bar.Close}', {bar.Volume}, 'constructed', '{Stamp(bar.Session)}T21:00:00Z', '{bar.RawClose}');"));
        }

        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('DDD', '2025-11-04T23:40:00.000Z', '0000000104');");

        foreach (var (concept, start, end, value, filed) in new[]
        {
            ("Revenues", "2024-04-01", "2024-06-30", 100m, "2024-08-01"),
            ("Revenues", "2024-07-01", "2024-09-30", 100m, "2024-11-01"),
            ("Revenues", "2025-04-01", "2025-06-30", 90m, "2025-08-01"),
            ("Revenues", "2025-07-01", "2025-09-30", 80m, "2025-11-03"),
            ("OperatingIncomeLoss", "2024-04-01", "2024-06-30", 10m, "2024-08-01"),
            ("OperatingIncomeLoss", "2024-07-01", "2024-09-30", 10m, "2024-11-01"),
            ("OperatingIncomeLoss", "2025-04-01", "2025-06-30", 5m, "2025-08-01"),
            ("OperatingIncomeLoss", "2025-07-01", "2025-09-30", 4m, "2025-11-03"),
        })
        {
            store.Execute(
                "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ("
                + FormattableString.Invariant($"'0000000104', '{concept}', '{start}', '{end}', '{value}', '{filed}', '10-Q', '{concept}-{end}', 'refresh');"));
        }

        var history = new SweepHistory(store.DatabaseFile);
        var inputs = await history.ReadAsync(night);
        var read = await WalkForwardTester.ReadAsync("GSPC", history, night, inputs, default);
        var readings = await LoopReadings.ReadAsync(read, store.DatabaseFile, night);
        var market = await history.MarketAsync(night);
        var (replay, _, candidates) = SweepIdeasRunner.ReadWithCandidates(read.Inputs, market);
        var pullbacks = new LargePullback(replay.Series, candidates);
        var ddd = Array.FindIndex(read.Series, one => one.Name.Ticker == "DDD");
        var filedAt = Array.IndexOf(read.Calendar, lastFiled);

        // The catalogue's own reading of DDD's state through the history's context: deteriorating on the night and from the
        // session after its last filing, none on that filing's day and before it; and none for the three strong members,
        // whose facts hold operating income for one quarter alone.
        Assert.True(ddd >= 0 && filedAt > 0);
        Assert.Equal(FundamentalState.Deteriorating, readings.BusinessStateFiled(ddd, read.Newest));
        Assert.Equal(FundamentalState.Deteriorating, readings.BusinessStateFiled(ddd, filedAt + 1));
        Assert.Null(readings.BusinessStateFiled(ddd, filedAt));
        Assert.Null(readings.BusinessStateFiled(ddd, Array.IndexOf(read.Calendar, new DateOnly(2025, 8, 1))));
        Assert.All(BusinessFirstHistory.Strong, ticker => Assert.Null(readings.BusinessStateFiled(Array.FindIndex(read.Series, one => one.Name.Ticker == ticker), read.Newest)));

        // The register: the live filter at the open version, the version leaving off a deteriorating business as 21.4
        // writes it, and the version as it was registered before 21.4, reading the provider's quarters.
        var family = TheSwingFamily.For("1", VersionFive);
        var liveRule = family[0];
        var filedRule = family.Single(one => one.Candidate == TheSwingFamily.Variant(TheSwingFamily.DeterioratingName, "1"));
        var providerRule = new Registration(
            TheSwingFamily.Variant(TheSwingFamily.ProviderDeterioratingName, "1"),
            TheSwingFamily.RuleSkippingDeteriorating,
            TheSwingFamily.Test,
            SwingFilterRule.EvaluatorName,
            SwingFilterRule.ParametersOf(VersionFive, skipDeteriorating: true));
        var registeredAt = new DateTimeOffset(2025, 1, 5, 12, 0, 0, TimeSpan.Zero);
        RegisterRow[] standing =
        [
            .. new[] { liveRule, filedRule, providerRule }.Select((one, id) =>
                new RegisterRow(id + 1, one.Candidate, one.Rule, one.Test, one.Evaluator, CandidateEvaluator.Write(one.Parameters), new SwingFilterRule().Version, CandidateFamily.Registered, null, registeredAt, null)),
        ];

        RuleTestInputs Inputs(LoopReadings? handed, Func<int, int, string?>? filed) =>
            new(read, standing, new Dictionary<string, LoopChange>(StringComparer.Ordinal), market, null!, handed!, _ => null, ("1", VersionFive, pullbacks), _ => { }, Candidates: replay, FiledStates: filed);

        var rules = RuleTests.LargePullbacks(Inputs(readings, null)).ToArray();

        RuleUnderTest Of(Registration rule) => rules.Single(one => one.Rule == rule.Candidate);

        Assert.Equal([liveRule.Candidate, filedRule.Candidate, providerRule.Candidate], rules.Select(rule => rule.Rule));
        Assert.Equal((null, null, RuleTests.BusinessState), (Of(liveRule).Why, Of(filedRule).Why, Of(providerRule).Why));

        // The walk by hand: the live filter's listings, DDD among them on the night, and the version's trades the walk over
        // the listings whose stock's state on the listing's session is not deteriorating, which leaves off DDD's listings
        // after its last filing and keeps every other listing, DDD's before that filing among them.
        var walk = RuleWalk.LargeSwingFilter(read, pullbacks, "1", VersionFive, _ => { }, null, readings, _ => null, SetupFamilies.ListedANight, PlanRule.Clear);

        bool Kept(RuleListing listing) => listing.Name != ddd || listing.Session <= filedAt;

        static IReadOnlyList<(int Entry, double? Edge)> Units(IEnumerable<ExitProcedures.Walked> walked) => [.. walked.Select(one => (one.Entry, one.Edge))];

        Assert.Contains(walk.Listings, listing => listing.Name == ddd && listing.Session == read.Newest);
        Assert.Equal(Units(walk.Own), Of(liveRule).Trades!);
        Assert.Equal(Units(walk.Keeping(at => Kept(walk.Listings[at]))), Of(filedRule).Trades!);
        Assert.Contains(Of(liveRule).Trades!, trade => trade.Entry == read.Newest);
        Assert.True(Of(liveRule).Trades!.Count > Of(filedRule).Trades!.Count);

        // States handed by hand in the catalogue's place: every business improving walks the version as the live filter,
        // and DDD deteriorating from the first session keeps none of its listings.
        Assert.Equal(Of(liveRule).Trades!, RuleTests.LargePullbacks(Inputs(readings, (_, _) => FundamentalState.Improving)).Single(rule => rule.Rule == filedRule.Candidate).Trades!);
        Assert.Equal(
            Units(walk.Keeping(at => walk.Listings[at].Name != ddd)),
            RuleTests.LargePullbacks(Inputs(readings, (name, _) => name == ddd ? FundamentalState.Deteriorating : null)).Single(rule => rule.Rule == filedRule.Candidate).Trades!);

        // A run handing the walk no reading of the facts keeps the version with what it lacks, naming its judge, and walks
        // the live filter as before.
        var unread = RuleTests.LargePullbacks(Inputs(null, null)).ToArray();

        Assert.Equal(RuleTests.FiledStateUnread, unread.Single(rule => rule.Rule == filedRule.Candidate).Why);
        Assert.Null(unread.Single(rule => rule.Rule == liveRule.Candidate).Why);
        Assert.Equal(Of(liveRule).Trades!, unread.Single(rule => rule.Rule == liveRule.Candidate).Trades!);
        Assert.Equal(
            "not testable: " + RuleTests.FiledStateUnread + "; judged by its record from its registration, on paper trading and the Run page",
            TestVerdict.Of(false, RuleTests.FiledStateUnread, null, null, null, null, null));
    }
}
