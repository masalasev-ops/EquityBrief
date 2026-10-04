using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, the Run page's learning regions: how the system learns, tonight's picks compared with a version's
// and each version at a checkpoint, each worked by hand over constructed gate rows and read back off the rendered
// page, a version's picks drawn in the comparison alone and no outcome of one drawn anywhere before its look.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
// see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow
public partial class ReadSurface
{
    // The claims the 12.3 correction drawing how the system learns, the comparison of tonight's picks and each
    // version at a checkpoint adds, which this check reaches and the phase's pair names. Declared before the reach
    // that takes them in.
    internal static readonly string[] RunBottomClaims =
    [
        CheckReach.Key("15.10 Run", "How the system learns, the shape clock as the ordinary nights under the open filter version against the sixty its calibration waits on"),
        CheckReach.Key("15.10 Run", "How the system learns, each check's typical pass count against its range drawn dashed until measured"),
        CheckReach.Key("15.10 Run", "How the system learns, the edge clock as a line from today to its two checkpoints with the blocks the live list has gathered"),
        CheckReach.Key("15.10 Run", "How the system learns, each version running beside the live list with what it changes and the stocks it has picked and the share the live list also picked and its blocks against the floor"),
        CheckReach.Key("15.10 Run", "How the system learns, a link comparing tonight's picks"),
        CheckReach.Key("15.10 Run", "Compare tonight's picks, a choice of the versions running beside the live list kept in the link"),
        CheckReach.Key("15.10 Run", "Compare tonight's picks, the names only the live list picked and those both picked and those only the version picked with the setting that made the difference beside each name only one side picked"),
        CheckReach.Key("15.10 Run", "Compare tonight's picks, two overlapping rings holding the three counts"),
        CheckReach.Key("15.10 Run", "Compare tonight's picks, the version's picks over the last twenty evenings with the share the live list also picked and the evenings it picked a stock the live list did not"),
        CheckReach.Key("15.10 Run", "Compare tonight's picks, picks alone with no figure from how a pick turned out"),
        CheckReach.Key("15.10 Run", "At a checkpoint, one row per version on a scale from nought to a hundred"),
        CheckReach.Key("15.10 Run", "At a checkpoint, before its first look a locked dashed outline with its trades and blocks so far"),
        CheckReach.Key("15.10 Run", "At a checkpoint, from its first look the share of its trades that reached the target with the break-even they needed and what no skill scored from the same starts and how far luck alone could move it"),
        CheckReach.Key("15.10 Run", "At a checkpoint, the verdict in words"),
        CheckReach.Key("15.5 The mark vocabulary", "Clock lines"),
        CheckReach.Key("15.5 The mark vocabulary", "Overlap rings"),
        CheckReach.Key("15.5 The mark vocabulary", "Checkpoint scale"),
    ];

    static readonly string MarketOff = TheSwingFamily.Variant(TheSwingFamily.MarketOffName, "1");
    static readonly string Strength = TheSwingFamily.Variant(TheSwingFamily.StrengthName, "1");
    static readonly string Depth = TheSwingFamily.Variant(TheSwingFamily.DepthName, "1");

    const string NoGates = "{\"gates\":[],\"notes\":[]}";

    // One gate row under filter version 1 with the six candidates' verdicts on it: those named fired, every other
    // evaluated and quiet, and the nearest bands' variant answering with the gates given where some are.
    static void PickRow(TemporaryStore store, string session, string ticker, bool passed, string gates, IReadOnlyDictionary<string, string>? nearest, params string[] fired)
    {
        var shadow = JsonSerializer.Serialize(new
        {
            candidates = TheFamilyNine.Select(candidate => new
            {
                candidate,
                fired = fired.Contains(candidate),
                values = candidate == NearestBands && nearest is not null ? nearest : new Dictionary<string, string>(),
            }),
            skipped = Array.Empty<object>(),
        });

        var bit = passed ? 1 : 0;

        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_stop, swing_target, swing_reward_to_risk, exclusions, passed, gates, shadow) " +
            $"VALUES ('{ticker}', '{session}', '1', 'code', 1, 1, 1, {bit}, 1, '95', '110', '1.4', '[]', {bit}, '{gates}', '{shadow}');");
    }

    // Two evenings under version 1. Worked by hand, on 2026-09-16 the live list passes AAQ and CCQ and fires on
    // both; the nearest bands' variant picks AAQ and BBQ, which the live list's trigger stopped, and answers CCQ
    // with its trade gate failed; the market-off variant picks exactly what the live list picked; the strength
    // variant picks BBQ alone; the depth variant picks nothing. On 2026-09-15 the nearest bands' variant and the
    // strength variant pick DDQ, which the live list stopped and whose trade has since won.
    static async Task<TemporaryStore> PicksStoreAsync()
    {
        var store = await FixtureExpectations.FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, (await FixtureExpectations.RegisterVerbAt(store, new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero), RegisterVerb.TheFamily)).Code);

        var trigger = "{\"gates\":[{\"gate\":\"trigger\",\"passed\":false,\"reason\":\"no buy signal inside three sessions\",\"values\":{}}],\"notes\":[]}";
        var live = SwingFamily.LiveCandidate("1");

        PickRow(store, "2026-09-15", "DDQ", false, trigger, null, NearestBands, Strength);
        PickRow(store, "2026-09-16", "AAQ", true, NoGates, null, live, NearestBands, MarketOff);
        PickRow(store, "2026-09-16", "BBQ", false, trigger, null, NearestBands, Strength);
        PickRow(store, "2026-09-16", "CCQ", true, NoGates, new Dictionary<string, string> { [SwingGates.Trade] = "failed" }, live, MarketOff);
        SwingOutcome(store, "2026-09-15", "DDQ", "win");

        return store;
    }

    static string Card(string page, string card)
    {
        var at = page.IndexOf($"data-card=\"{card}\"", StringComparison.Ordinal);

        Assert.True(at > 0, $"The run page draws no {card}.");

        var next = page.IndexOf("data-card=\"", at + 1, StringComparison.Ordinal);

        return page[at..(next < 0 ? page.Length : next)];
    }

    [Fact]
    public async Task ACandidatesPickOfANameIsDrawnInTheRunPagesComparisonAlone()
    {
        using var store = await PicksStoreAsync();

        // The store carries the version's picks, which is what makes their absence elsewhere a statement about
        // the screens rather than about an empty column.
        var stored = await Api(store).ShadowPicksAsync(new DateOnly(2026, 9, 16));

        Assert.Contains(stored, pick => pick.Ticker == "BBQ" && pick.Candidate == NearestBands && pick.Fired);
        Assert.Contains(stored, pick => pick.Ticker == "DDQ" && pick.Candidate == NearestBands && pick.Fired);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16"));
        var compare = Card(page, "compare-picture");

        Assert.Contains("<li data-ticker=\"BBQ\">", compare, StringComparison.Ordinal);

        // A name only a version picked is drawn in the comparison and in no other region of the page, and no
        // other screen names it or the version, the listings' own shadow column reaching none of them.
        var elsewhere = page.Replace(compare, string.Empty, StringComparison.Ordinal);

        Assert.DoesNotContain("BBQ", elsewhere, StringComparison.Ordinal);
        Assert.DoesNotContain("DDQ", elsewhere, StringComparison.Ordinal);
        Assert.DoesNotContain("shadow_reasons", page, StringComparison.Ordinal);

        foreach (var route in new[] { "/screens/tonight/2026-09-16", "/screens/universe", "/screens/picks", "/screens/name/BBQ", "/screens/name/DDQ" })
        {
            var body = WebUtility.HtmlDecode(await (await client.GetAsync(route)).Content.ReadAsStringAsync());

            Assert.DoesNotContain(TheSwingFamily.NearestBandsName, body, StringComparison.Ordinal);
            Assert.DoesNotContain("compare-picture", body, StringComparison.Ordinal);
            Assert.DoesNotContain("shadow_reasons", body, StringComparison.Ordinal);
        }

        // DDQ's trade won, and the comparison and the versions draw picks alone: no outcome of a version's pick
        // is drawn before its look reads it.
        var versions = Assert.Single(Blocks(page, "<table class=\"versions-table\">.*?</table>"));

        foreach (var drawn in new[] { compare, versions })
        {
            Assert.DoesNotMatch("(?i)\\b(?:win|wins|won|loss|lost|reached)\\b", drawn);
            Assert.DoesNotContain("data-outcome", drawn, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheComparisonDrawsEachSidesNamesWithWhatMadeTheDifferenceForAMixedAnIdenticalAndADisjointVersion()
    {
        using var store = await PicksStoreAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // With no version named, the first after the live list: the nearest bands' variant.
        var mixed = Card(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16")), "compare-picture");

        Assert.Contains($"<div class=\"compare-picture\" data-version=\"{RunScreen.Slug(NearestBands)}\" data-evaluated=\"true\" data-only-live=\"1\" data-both=\"1\" data-only-version=\"1\">", mixed, StringComparison.Ordinal);
        // CCQ's reward to risk of 1.4 on the nearest bands' plan, against the floor of 1.5 version 1 was opened with.
        Assert.Matches("data-group=\"only-live\">.*?<li data-ticker=\"CCQ\">.*?its reward to risk on this version's plan is 1.40, against a floor of 1.5</span></li>", mixed);
        Assert.Matches("data-group=\"both\">.*?<li data-ticker=\"AAQ\">", mixed);
        Assert.Matches("data-group=\"only-version\">.*?<li data-ticker=\"BBQ\">.*?the live list's trigger gate: no buy signal inside three sessions</span></li>", mixed);

        // The rings hold the three counts, left to right.
        Assert.Matches("<svg class=\"overlap-rings\".*?<circle class=\"or-live\".*?<circle class=\"or-version\".*?>1</text>.*?>1</text>.*?>1</text>", mixed);

        // Over the two evenings: DDQ, AAQ and BBQ picked, one of the three also the live list's, and both evenings
        // with a pick the live list did not make.
        Assert.Contains("<b>3</b><span>stocks this version picked over the last 2 evening(s)</span>", mixed, StringComparison.Ordinal);
        Assert.Contains("<b>33%</b><span>of them also on the live list</span>", mixed, StringComparison.Ordinal);
        Assert.Contains("<b>2</b><span>evenings it picked a stock the live list did not</span>", mixed, StringComparison.Ordinal);

        // A version picking what the live list picked, and one sharing none of it.
        var identical = Card(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/2026-09-16?version={RunScreen.Slug(MarketOff)}")), "compare-picture");

        Assert.Contains($"data-version=\"{RunScreen.Slug(MarketOff)}\" data-evaluated=\"true\" data-only-live=\"0\" data-both=\"2\" data-only-version=\"0\"", identical, StringComparison.Ordinal);
        Assert.Contains("<b>100%</b><span>of them also on the live list</span>", identical, StringComparison.Ordinal);
        Assert.Contains("<b>0</b><span>evenings it picked a stock the live list did not</span>", identical, StringComparison.Ordinal);

        var disjoint = Card(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/2026-09-16?version={RunScreen.Slug(Strength)}")), "compare-picture");

        Assert.Contains($"data-version=\"{RunScreen.Slug(Strength)}\" data-evaluated=\"true\" data-only-live=\"2\" data-both=\"0\" data-only-version=\"1\"", disjoint, StringComparison.Ordinal);
        Assert.Contains("<b>0%</b><span>of them also on the live list</span>", disjoint, StringComparison.Ordinal);
        Assert.Contains("<b>2</b><span>evenings it picked a stock the live list did not</span>", disjoint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheVersionChosenForTheComparisonIsKeptInTheLink()
    {
        using var store = await PicksStoreAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/2026-09-16?version={RunScreen.Slug(Depth)}"));
        var compare = Card(page, "compare-picture");

        // The choice names the page's own route, offers the eight versions beside the live list and selects the
        // one the link names; the shell writes a new choice back to the link.
        Assert.Contains($"<select id=\"compare-version\" data-compare=\"{SinglePageApp.RunRoute}2026-09-16\">", compare, StringComparison.Ordinal);
        Assert.Equal(8, Regex.Matches(compare, "<option ").Count);
        Assert.Contains($"<option value=\"{RunScreen.Slug(Depth)}\" selected>{Depth}</option>", compare, StringComparison.Ordinal);
        Assert.DoesNotContain($"<option value=\"{RunScreen.Slug(SwingFamily.LiveCandidate("1"))}\"", compare, StringComparison.Ordinal);
        Assert.Contains("data-only-live=\"2\" data-both=\"0\" data-only-version=\"0\"", compare, StringComparison.Ordinal);

        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains("closest('select[data-compare]')", shell, StringComparison.Ordinal);
        Assert.Contains("location.hash = chosen.getAttribute('data-compare') + '?version=' + encodeURIComponent(chosen.value);", shell, StringComparison.Ordinal);
        Assert.Contains("fetch('/screens/run/' + night + (query ? '?' + query : ''))", shell, StringComparison.Ordinal);

        // The learning region's link opens the comparison on the first version after the live list.
        Assert.Contains($"<a href=\"{SinglePageApp.RunRoute}2026-09-16?version={RunScreen.Slug(NearestBands)}\">", Card(page, "learning-picture"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLearningRegionDrawsBothClocksAndEachVersionsPicksAboveTheDetail()
    {
        using var store = await PicksStoreAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16"));
        var detail = page.IndexOf("<section class=\"run-detail\">", StringComparison.Ordinal);

        foreach (var card in new[] { "learning-picture", "compare-picture", "checkpoint-picture" })
        {
            var at = page.IndexOf($"data-card=\"{card}\"", StringComparison.Ordinal);

            Assert.True(at > 0 && at < detail, $"The run page draws no {card} above the detail.");
        }

        var learning = Card(page, "learning-picture");

        // The shape clock waits on sixty ordinary nights, each check's range dashed and holding no dot before
        // the trigger.
        Assert.Matches("<svg class=\"progress-bar\" viewBox=\"0 0 380 16\" role=\"img\" data-ordinary=\"[0-9]+\" data-wanted=\"60\"", learning);
        Assert.Contains("class=\"bd-band bd-unmeasured\"", learning, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"bd-dot\"", learning, StringComparison.Ordinal);

        // The edge clock: the live list has run the one session since its first night, 2026-09-15, against
        // checkpoints at 566 and 818 sessions, and gathered no block.
        Assert.Contains($"<svg class=\"edge-line\" viewBox=\"0 0 520 96\" role=\"img\" data-sessions=\"1\" data-first-look=\"{EdgeClock.FirstLookSessions}\" data-promotion=\"{EdgeClock.EarliestPromotionSessions}\"", learning, StringComparison.Ordinal);
        Assert.Equal((566, 818), (EdgeClock.FirstLookSessions, EdgeClock.EarliestPromotionSessions));
        Assert.Contains("Block 0 of 8 gathered by the live list.", learning, StringComparison.Ordinal);

        // Each version with what it changes, its picks up to the night and the share of them the live list also
        // picked, the live list first.
        var rows = Blocks(learning, "<tr data-version=.*?</tr>");

        Assert.Equal(9, rows.Count);
        Assert.Contains($"data-version=\"{RunScreen.Slug(SwingFamily.LiveCandidate("1"))}\" data-picks=\"2\"", rows[0], StringComparison.Ordinal);
        Assert.Contains("<td>is the live list</td>", rows[0], StringComparison.Ordinal);

        var nearest = Assert.Single(rows, row => row.Contains($"data-version=\"{RunScreen.Slug(NearestBands)}\"", StringComparison.Ordinal));

        Assert.Contains("data-picks=\"3\"", nearest, StringComparison.Ordinal);
        Assert.Contains("A stop and target at the nearest bands, not a stop and target clear of the noise", nearest, StringComparison.Ordinal);
        Assert.Contains("33% of its picks", nearest, StringComparison.Ordinal);
        Assert.Contains("<td>block 0 of 8</td>", nearest, StringComparison.Ordinal);
        Assert.Contains("no pick yet", Assert.Single(rows, row => row.Contains($"data-version=\"{RunScreen.Slug(Depth)}\"", StringComparison.Ordinal)), StringComparison.Ordinal);

        // The seventh's change in plain words: it leaves off a deteriorating business, which the live list keeps.
        Assert.Contains(
            "Leaves off a business whose reported quarters read deteriorating, which the live list keeps",
            Assert.Single(rows, row => row.Contains($"data-version=\"{RunScreen.Slug(TheSwingFamily.Variant(TheSwingFamily.DeterioratingName, "1"))}\"", StringComparison.Ordinal)),
            StringComparison.Ordinal);

        // The eighth's: it lists only a stock whose analysts raised their estimate.
        Assert.Contains(
            "Only a stock whose analysts raised their estimate for its year over the last 30 days, which the live list does not ask",
            Assert.Single(rows, row => row.Contains($"data-version=\"{RunScreen.Slug(TheSwingFamily.Variant(TheSwingFamily.RevisionsName, "1"))}\"", StringComparison.Ordinal)),
            StringComparison.Ordinal);

        // The rule it took the place of keeps its words, read off its own registration.
        Assert.Equal(
            "Only the top quarter of one of the 3 strongest sectors, in place of an uptrend's strength",
            RunScreen.Changes(TheSwingFamily.Leaders("1", FilterSettings.Proposed).Parameters, TheSwingFamily.For("1", FilterSettings.Proposed)[0].Parameters));

        // The ninth's: it keeps the night's first three in the list's own order.
        Assert.Contains(
            "Keeps only the night's first 3 in the list's own order, where the live list keeps every name it passes",
            Assert.Single(rows, row => row.Contains($"data-version=\"{RunScreen.Slug(TheSwingFamily.Variant(TheSwingFamily.BestThreeName, "1"))}\"", StringComparison.Ordinal)),
            StringComparison.Ordinal);

        // And no version's change is drawn as a setting's own name against the live list's.
        Assert.DoesNotContain(rows, row => row.Contains("where the live list has", StringComparison.Ordinal));

        // Every version below its first look is a locked row at the checkpoint.
        var checkpoint = Card(page, "checkpoint-picture");

        Assert.Contains("<div class=\"checkpoint-picture\" data-rows=\"9\" data-unlocked=\"0\">", checkpoint, StringComparison.Ordinal);
        Assert.Equal(9, Regex.Matches(checkpoint, "unlocks at checkpoint 1, after block 8 of 8").Count);
    }

    [Fact]
    public void EveryRuleOfTheSwingFamilySaysWhatItChangesInWordsWrittenForIt()
    {
        // Each rule the family registers beside the live filter, at the open version's settings, is drawn in words
        // written for what it moves, and none as a setting's own name, which is how the ninth was drawn on the run
        // page of 2026-10-02 before its words were written.
        // see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
        // Filter version 5's settings as the operator's store holds them since the freeze of 2026-10-02.
        var five = FilterSettings.Read(
            "{\"breadthFloor\":0.45,\"strengthFloor\":0.5,\"depthLow\":1,\"depthHigh\":5,\"dryUpCeiling\":1.5,\"rewardToRiskFloor\":2," +
            "\"stopLow\":0.5,\"stopHigh\":4,\"earningsWindowSessions\":15,\"arrivalSessions\":3,\"trade\":\"clear\"}");
        var family = TheSwingFamily.For("5", five);
        var live = family[0].Parameters;

        Assert.Equal(9, family.Count);

        foreach (var rule in family.Skip(1))
        {
            var words = RunScreen.Changes(rule.Parameters, live);

            Assert.DoesNotContain("where the live list has", words, StringComparison.Ordinal);
            Assert.NotEqual("The same settings as the live list", words);
        }

        // A registration written before the count a night was stated keeps every name it passes, as the live
        // filter does, and the other way round the words say what the live list keeps.
        var unstated = live.Where(setting => setting.Key != SwingFilterRule.BestOfParameter).ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal);

        Assert.Equal("The same settings as the live list", RunScreen.Changes(unstated, live));
        Assert.Equal(
            "Keeps every name it passes, where the live list keeps only the night's first 3",
            RunScreen.Changes(live, family.Single(rule => rule.Candidate == TheSwingFamily.Variant(TheSwingFamily.BestThreeName, "5")).Parameters));
    }

    [Fact]
    public void ACheckpointRowIsLockedBeforeItsFirstLookAndDrawsTheShareTheBreakEvenAndTheNullFromIt()
    {
        var (night, _, candidates, _) = NineBlocks();
        var rows = RunScreen.Checkpoints(new EdgeView(night, candidates, EdgeClock.FirstLookSessions, EdgeClock.EarliestPromotionSessions));
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().CheckpointRegion(rows));
        var each = Blocks(drawn, "<div class=\"ck-row\".*?</div>");

        Assert.Contains("data-rows=\"2\" data-unlocked=\"1\"", drawn, StringComparison.Ordinal);

        // The live filter's first look is read at nine blocks: 50% of its trades reached the target against the
        // 35% they needed, and no skill scored 40%. On a scale 560 wide, 280, 196 and 224.
        Assert.Contains("data-unlocked=\"true\"", each[0], StringComparison.Ordinal);
        Assert.Contains("<circle class=\"ck-share\" cx=\"280\"", each[0], StringComparison.Ordinal);
        Assert.Contains("<line class=\"ck-even\" x1=\"196\"", each[0], StringComparison.Ordinal);
        Assert.Contains("<circle class=\"ck-null\" cx=\"224\"", each[0], StringComparison.Ordinal);
        Assert.Contains("50.0% reached the target against 35.0% needed", each[0], StringComparison.Ordinal);
        Assert.Contains($"<span class=\"ck-verdict\">{rows[0].Verdict}</span>", each[0], StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(rows[0].Verdict));

        // The variant holds five blocks, below its first look: locked, with its trades and blocks so far.
        Assert.Contains("data-unlocked=\"false\" data-setups=\"5\" data-blocks=\"5\"", each[1], StringComparison.Ordinal);
        Assert.Contains("unlocks at checkpoint 1, after block 8 of 8", each[1], StringComparison.Ordinal);
        Assert.Contains("5 trade(s) so far, block 5 of 8", each[1], StringComparison.Ordinal);
        Assert.DoesNotContain("ck-share", each[1], StringComparison.Ordinal);
    }
}
