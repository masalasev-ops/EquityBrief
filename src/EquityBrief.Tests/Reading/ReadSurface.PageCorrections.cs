using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Moves;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Fills;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Rules;

namespace EquityBrief.Tests.Reading;

// read-surface, the page corrections of 2026-10-26 on the operator's rulings of 2026-10-25 and 2026-10-26: each moves a
// page alone, and no source a rule's code version pins is one they write.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
public partial class ReadSurface
{
    // The shipped sources the page corrections of 2026-10-26 write or edit, each correction adding those it writes.
    static readonly string[] PageCorrectionSources =
    [
        "src/EquityBrief.Core/Cards/PlanRatio.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Indices.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.TonightTop.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Families.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Tonight.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Tonight.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Dips.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.History.cs",
    ];

    // No source a rule's code version pins is one the page corrections write, so no rule's version moves: no candidate
    // evaluator's own file or sources, the sources every family rule shares, the hooks', the swing filter's, the rule
    // versions' scorer's or the fills'; the reader shown to find a source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneThePageCorrectionsWrite()
    {
        var pinned = CandidateEvaluators.All
            .Select(evaluator => "src/EquityBrief.Core/Candidates/" + evaluator.GetType().Name + ".cs")
            .Concat(CandidateEvaluators.All.SelectMany(evaluator => evaluator.OwnSources))
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(SwingFilter.CodeVersionSources)
            .Concat(RuleVersionScorer.CodeVersionSources)
            .Concat(TradeFiller.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 40, $"Read {pinned.Length} pinned source(s), expected at least 40.");
        Assert.All(PageCorrectionSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(PageCorrectionSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Worker/Indices/IndexFamilies.cs", pinned);
        Assert.Contains("src/EquityBrief.Core/Candidates/DipCandidate.cs", pinned);
    }

    // The S&P 400's night on the universes' store with a pick of each kind among tomorrow's buys: M1 the breakout's, its
    // stop rising with the price and no target; M3 the drift's at RLI's plan of 2026-10-23, bought about 64.83 with its
    // stop at 60.76 and its target at 77.03; and M2 the short-term dip's, sold on its exit with no target.
    static TemporaryStore EachKindStore()
    {
        var store = UniversesStore();

        store.Execute($"UPDATE index_family_result SET passed = 1, place = 1, entry = '64.83', stop = '60.76', target = '77.03', cap = 60, reason = NULL WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND ticker = 'M3' AND family = 'drift';");
        store.Execute($"INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES ('MID', '{IndexNight}', 'M3', 'drift', 'listed', 1, '[]', NULL, NULL, NULL);");
        DipNightOn(store);

        return store;
    }

    // From the 22.6 correction of 2026-10-26 each buy's risk line reads its own plan: the drift's worked from its plan to
    // $3.00, and where a setup sets no target the line says why, the breakout's selling on a rising stop and the dip's on
    // its exit signal, where both read "no target".
    [Fact]
    public async Task EachBuysRiskLineReadsItsOwnPlanAndSaysWhyWhereItsSetupSetsNoTarget()
    {
        using var store = EachKindStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Equal(["M1", "M3", "M2"], Regex.Matches(Assert.Single(Blocks(page, "<table class=\"list-table buy-points\".*?</table>")), "<tr data-ticker=\"([^\"]+)\" data-family=").Select(match => match.Groups[1].Value));
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"2.9975\">$3.00</td>", PickRowOf(page, "M3"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">No target: it sells on a rising stop</td>", PickRowOf(page, "M1"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">No target: it sells on its exit signal</td>", PickRowOf(page, "M2"), StringComparison.Ordinal);
        Assert.DoesNotContain(">no target<", page, StringComparison.Ordinal);

        // In the reader's words: no term of section 15.23's list reaches the page.
        Assert.Empty(Checks.PlainWords.FaultsOn(Checks.PlainWords.TonightPage, page, Checks.PlainWords.Terms()));
    }

    // From the 19.3 correction of 2026-10-26 a buy holding no label says why on an S&P 400 or 600 night, the labeller
    // reading the S&P 500's list alone, and says no label on the S&P 500's; a pick holding labels draws its counts on
    // either, as the labeller would count them were the operator to widen its list.
    [Fact]
    public async Task ABuyHoldingNoLabelSaysTheLabelsCoverSAndP500PicksOnlyOffTheSAndP500AndNoLabelOnIt()
    {
        const string Covers = "<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">News labels cover S&amp;P 500 picks only</span></td>";
        const string None = "<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">no label</span></td>";

        using (var store = EachKindStore())
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400");

            Assert.Equal(3, Regex.Matches(Assert.Single(Blocks(page, "<table class=\"list-table buy-points\".*?</table>")), Regex.Escape(Covers)).Count);
            Assert.DoesNotContain(">no label<", page, StringComparison.Ordinal);
        }

        // Drawn by the page's own renderer for an S&P 500 night and for an S&P 400 one: a pick holding no label and one
        // holding two good and one bad.
        static string Drawn(bool labelled, int? good, int? bad) => new MarkRenderer().TomorrowsBuys(
            [new BuyPointView(DriftRule.Name, "Earnings drift", new FamilyPickCell(new ListingCell("X1", new DateOnly(2026, 10, 2), 0, 0, 50m, [], NewsPositive: good, NewsNegative: bad), 1, 50m, 47m, 56m, false, 2m, "its report beat", []))],
            null,
            labelled: labelled);

        Assert.Contains(None, Drawn(true, null, null), StringComparison.Ordinal);
        Assert.Contains(Covers, Drawn(false, null, null), StringComparison.Ordinal);
        Assert.All(new[] { Drawn(true, 2, 1), Drawn(false, 2, 1) }, drawn => Assert.Contains("<td class=\"news-counts\" data-positive=\"2\" data-negative=\"1\"><span class=\"np gain\">2 good</span> · <span class=\"nn loss\">1 bad</span></td>", drawn, StringComparison.Ordinal));
    }

    // The night of 2026-10-02 run again at noon on 2026-10-04 with the register as it stood, after a freeze registered
    // the half hour before: the freeze's instant, and the instant the night read the register at.
    static readonly DateTimeOffset FrozenAfterTheNight = new(2026, 10, 4, 11, 30, 0, TimeSpan.Zero);

    static readonly DateTimeOffset RunAgainReadAt = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    // An index's night on the universes' store stating the instant a night run again read the register at, as the night
    // writes it into its settings.
    static void RunAgainAt(TemporaryStore store, string index, DateTimeOffset readAt) =>
        store.Execute($"UPDATE index_family_night SET settings = '{IndexFamilies.Settings(index, registerAt: readAt).Replace("'", "''", StringComparison.Ordinal)}' WHERE index_code = '{index}' AND session_date = '{IndexNight}';");

    // From the 22.3 correction of 2026-10-26 the dip's card and its Run row read the register as the page reads it: on the
    // S&P 400's night run again after its dip was frozen, the dip's night drawn by the live rule, the card is live since
    // the freeze with its three versions and the line its recorded answer gives, and its Run row live since the freeze,
    // where both read the register past the night's evening and drew the provisional rule.
    [Fact]
    public async Task AFrozenDipsCardAndRunRowFollowTheRuleItsNightRanOnANightRunAgainAfterItsFreeze()
    {
        var (store, _) = await FrozenDipStore(FrozenAfterTheNight);

        using (store)
        {
            RunAgainAt(store, "MID", RunAgainReadAt);

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var dip = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")), DipRule.Name);

            Assert.Contains("data-state=\"live\" data-live-since=\"2026-10-04\" data-variants=\"3\"", dip, StringComparison.Ordinal);
            Assert.Contains("Live since 4 Oct 2026", dip, StringComparison.Ordinal);
            Assert.DoesNotContain(SetupFamilies.ProvisionalStatus, dip, StringComparison.Ordinal);
            Assert.Contains("data-frozen=\"2026-10-04\" data-run=\"20261024T060843Z\" data-answer=\"passed\"", dip, StringComparison.Ordinal);

            var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400"));

            Assert.Contains("<tr data-family=\"dip\" data-state=\"live\" data-live-since=\"2026-10-04\" data-variants=\"3\"", Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>")), StringComparison.Ordinal);
        }
    }

    // The same on the S&P 500's own page, which reads the instant off its own index's night: the families' night of
    // 2026-10-02 run again after the S&P 500's dip was frozen at the VIX's setting, its dip's night drawn by the live rule
    // listing F6.
    [Fact]
    public async Task TheSAndP500sFrozenDipCardAndRunRowFollowTheRuleItsNightRanOnANightRunAgainAfterItsFreeze()
    {
        using var store = await FamilyNightStore();

        var (code, said) = await Checks.FixtureExpectations.RegisterVerbAt(store, FrozenAfterTheNight, Worker.Candidates.RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", "drop=-3,trend=50,exit=65,limit=10,stop=3,market=6,volume=0");

        Assert.True(code == 0, said);

        var live = Query(store, "SELECT candidate FROM candidate_register WHERE evaluator = 'dip-500' ORDER BY id;").Single();
        var setting = new DipCandidate("GSPC").SettingOf(CandidateEvaluator.Read(Query(store, "SELECT parameters FROM candidate_register WHERE evaluator = 'dip-500';").Single()));

        store.Execute(
            "INSERT INTO dip_night (index_code, session_date, members, market, market_open, settings, passed, listed, fault) VALUES " +
            $"('GSPC', '{TheSwitch}', 9, 'VIX', 1, '{Worker.Dips.DipFamily.Settings(setting, live, "GSPC").Replace("'", "''", StringComparison.Ordinal)}', 1, 1, NULL);");
        store.Execute(
            "INSERT INTO dip_result (index_code, session_date, ticker, passed, place, entry, stop, time_limit, rsi, reason, gates) VALUES " +
            $"('GSPC', '{TheSwitch}', 'F6', 1, 1, '100', '94', 10, 4.0, NULL, '{DipGatesPassed()}');");
        store.Execute(
            "INSERT INTO dip_pick (index_code, session_date, ticker, state, place, listed_under, held_index, held_family, held_night) VALUES " +
            $"('GSPC', '{TheSwitch}', 'F6', 'listed', 1, NULL, NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('GSPC', '{TheSwitch}', 9, 0.6, 1, '{IndexFamilies.Settings("GSPC", registerAt: RunAgainReadAt).Replace("'", "''", StringComparison.Ordinal)}', 0);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var dip = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), DipRule.Name);

        Assert.Contains("data-state=\"live\" data-live-since=\"2026-10-04\" data-variants=\"0\"", dip, StringComparison.Ordinal);
        Assert.DoesNotContain(SetupFamilies.ProvisionalStatus, dip, StringComparison.Ordinal);

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));

        Assert.Contains("<tr data-family=\"dip\" data-state=\"live\" data-live-since=\"2026-10-04\"", Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>")), StringComparison.Ordinal);
    }

    // From the 22.11 correction of 2026-10-26 paper trading reads the register as the page reads it on every index: on the
    // universes' store's night run again after Quality at a fair price was frozen on the S&P 500 and on the S&P 400 the
    // morning after it, the family's tile is its live rule's, both pickers list it, its rule's choice naming the live
    // rule, and the scorecard draws the live rule with its seven versions and never the provisional rule's line that no
    // variant is registered; the S&P 500's read off its own index's night, where it read the register past the night's
    // evening.
    [Fact]
    public async Task PaperTradingDrawsAFrozenQualityRuleAndItsVersionsOnANightRunAgainAfterItsFreezeOnTheSAndP500AndThe400()
    {
        using var store = UniversesStore();

        // A minute apart, since one register command writes under one run id an instant.
        foreach (var (index, at) in new[] { ("GSPC", 0), ("MID", 1) })
        {
            var (code, said) = await Checks.FixtureExpectations.RegisterVerbAt(store, FrozenAfterTheNight.AddMinutes(at), Worker.Candidates.RegisterVerb.IndexFamily, QualityRule.Name, "--index", index, "--parameters", "floor=7,valuation=1,trend=0,book=10,sale=0");

            Assert.True(code == 0, said);
        }

        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('GSPC', '{IndexNight}', 2, 0.6, 1, '{IndexFamilies.Settings("GSPC", registerAt: RunAgainReadAt).Replace("'", "''", StringComparison.Ordinal)}', 0);");
        RunAgainAt(store, "MID", RunAgainReadAt);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var (word, slug) in new[] { ("500", "s-p-500"), ("400", "s-p-400") })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/paper?universe={word}&variants=quality&rule=quality"));
            var rule = Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value;

            Assert.Equal(
                ("quality", "live"),
                Regex.Matches(page, "<div class=\"paper-tile\" data-family=\"([^\"]+)\" data-standing=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)).Last());
            Assert.Contains("<option value=\"quality\" selected>Quality at a fair price · live rule</option>", rule, StringComparison.Ordinal);
            Assert.Contains("<option value=\"quality\" selected>", Regex.Match(page, "<select data-merge-key=\"variants\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
            Assert.Contains("<div class=\"score\" data-family=\"quality\" data-standing=\"live\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("No variant is registered for this rule.", page, StringComparison.Ordinal);
            Assert.Equal(7, Regex.Matches(page, $"<tr data-account=\"[^\"]*{slug}-quality-rule-at[^\"]*\"[^>]*data-standing=\"variant\"").Count);
        }
    }

    // From the 20.11 correction of 2026-10-26 the Loop page reads the register as the page reads it on the S&P 500 as on
    // the 400 and 600: the universes' store with a run for October, its sector heavyweights frozen on the S&P 500 the
    // morning after its night of 2026-10-02 by the register's own verb. Read on its own evening the night read no rule
    // of the family and the summary draws its rule today not read; run again at noon with the register as it stood, the
    // summary draws the live rule's chips, worked by hand from its setting, the 10 largest over a 251-session look-back,
    // 2 leaders against the fund with a beta of at least 1 and sold on losing the lead, where it read the register past
    // the night's evening and drew the family not read.
    [Fact]
    public async Task TheSAndP500sLoopPageDrawsTheRulesItsNightReadOnANightRunAgainAfterAFreeze()
    {
        using var store = UniversesStore();

        Run(store, "loop-test-GSPC-20261009T120000Z", "GSPC", "2026-10");

        var (code, said) = await Checks.FixtureExpectations.RegisterVerbAt(store, FrozenAfterTheNight, Worker.Candidates.RegisterVerb.Family, HeavyweightRule.Name);

        Assert.True(code == 0, said);

        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('GSPC', '{IndexNight}', 2, 0.6, 1, '{IndexFamilies.Settings("GSPC").Replace("'", "''", StringComparison.Ordinal)}', 0);");

        async Task<string> RuleToday()
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            return Regex.Match(Card(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")), "loop-summary"), "<tr data-family=\"heavyweight\".*?</td><td.*?</td>", RegexOptions.Singleline).Value;
        }

        Assert.EndsWith("<td class=\"degraded\">not read</td>", await RuleToday(), StringComparison.Ordinal);

        RunAgainAt(store, "GSPC", RunAgainReadAt);

        Assert.EndsWith(
            "<td><span class=\"rule-chips\"><span class=\"chip\">10 largest</span><span class=\"chip\">251-session look-back</span><span class=\"chip\">2 leaders</span><span class=\"chip\">against the fund</span><span class=\"chip\">beta at least 1</span><span class=\"chip\">sold on losing the lead</span></span></td>",
            await RuleToday(),
            StringComparison.Ordinal);
    }

    // From the 18.1 correction of 2026-10-26 a stock's rules region says which family the page's list holds it under where
    // the swing filter did not list it: M1, on the S&P 400's list under its breakout, where it read "this name is not on
    // tonight's list" beneath a page saying it was on it; S1, on no list, still reads that it is not.
    [Fact]
    public async Task AStocksRulesRegionNamesTheFamilyThePagesListHoldsItUnderWhereTheSwingFilterDidNotListIt()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        static string Why(string page) => Assert.Single(Blocks(page, "<section class=\"why-it-is-here\".*?</section>"));

        var m1 = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M1/{IndexNight}"));

        Assert.Contains("On the page's list for 2026-10-02 under <b>Breakouts to a new high</b>, at place 1.", m1, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-listed=\"family\">the swing filter did not list this name; the page's list holds it under Breakouts to a new high</p>", Why(m1), StringComparison.Ordinal);
        Assert.DoesNotContain("not on tonight's list", m1, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-listed=\"false\">this name is not on tonight's list</p>", Why(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/S1/{IndexNight}"))), StringComparison.Ordinal);
    }

    // From the 18.3 correction of 2026-10-26 the long view's axis is ticked at round values as every report chart is, where
    // it ticked four values evenly between its ends: CVX's closes from 86.34 to 211.55 and its trail at 217.78 take an axis
    // from 75.83 to 228.29 with eight per cent of the span's room each side, a quarter of the span 38.1, so a step of 50
    // and the ticks 100, 150 and 200, where it drew 76, 127, 177 and 228. Worked by hand.
    [Fact]
    public void TheLongViewTicksItsAxisAtRoundValues()
    {
        MonthEnd[] closes = [new(new DateOnly(2018, 1, 31), 86.3376m), new(new DateOnly(2022, 6, 30), 160m), new(new DateOnly(2026, 10, 9), 211.55m)];
        LongViewLevel[] levels =
        [
            new(200.78m, 208.98m, "support", "Tranche 1, 200.78 to 208.98", "Tranche 1"),
            new(217.78m, 217.78m, "resistance", "Trail begins, 217.78 to 217.78", "Trail begins"),
            new(160m, 160m, "rule", "The year's low, 160.00 on 2026-04-14", "Year's low"),
        ];
        var drawn = new MarkRenderer().LongView("CVX", closes, levels);

        Assert.Equal(["100", "150", "200"], Regex.Matches(drawn, "<text class=\"m-tick\" x=\"[^\"]+\" y=\"[^\"]+\" text-anchor=\"end\">([^<]+)</text>").Select(match => match.Groups[1].Value));
        Assert.Equal(3, Regex.Matches(drawn, "<line class=\"m-gridline\"").Count);
    }

    // From the 18.3 correction of 2026-10-26 a reward to risk bar past the axis's end says so inside the figure, where BX's
    // "6.80, past the axis's end" was cut at the figure's edge to "6.80, past the": the note's 25 characters at the row
    // note's eleven pixels, about 6.2 a character, end inside the figure's 640, and a plan whose notes are short keeps its
    // axis, CVX's rule at one at 322, two hundred and a third of the 366 the bars run over at an axis ending at three.
    [Fact]
    public void ARewardToRiskBarPastTheAxissEndSaysSoInsideTheFigure()
    {
        var marks = new MarkRenderer();
        var past = marks.RewardToRiskBars("XYZ", [new TrancheShare(10m, 11m, 10.5m, 1, 9.5m, 1m, [new TargetReward(18.5m, 8m, 8m, false)])]);
        var note = Regex.Match(past, "<text class=\"m-rownote\" x=\"([^\"]+)\" y=\"[^\"]+\">(8.00, past the axis's end)</text>");

        Assert.True(note.Success, "The bar past the axis's end draws no note saying so.");
        Assert.True(
            double.Parse(note.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + (note.Groups[2].Value.Length * 6.2) <= 640,
            $"The note at {note.Groups[1].Value} runs past the figure's 640.");

        var kept = marks.RewardToRiskBars("CVX", [new TrancheShare(200.78m, 208.98m, 204.88m, 1, 195.84m, 9.04m, [new TargetReward(217.78m, 12.90m, 1.4271m, true)])]);

        Assert.Contains("data-axis=\"3\"", kept, StringComparison.Ordinal);
        Assert.Contains("<line class=\"rk-one\" x1=\"322\"", kept, StringComparison.Ordinal);
    }

    // From the 18.3 correction of 2026-10-26 a member of the S&P 400 or 600 holding no ladder row says the plan is read for
    // the S&P 500's members alone, as its plan region does, where it said "no ladder row for this name yet"; an S&P 500
    // member holding none still says one is to come.
    [Fact]
    public async Task AMemberOfAnotherIndexSaysItHoldsNoLadderRowSinceThePlanIsReadForTheSAndP500Alone()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        static string Trend(string page) => Assert.Single(Blocks(page, "<p class=\"trend-state\".*?</p>"));

        Assert.Equal(
            "<p class=\"trend-state\" data-trend-state=\"none\" data-index=\"S&amp;P 600\">no ladder row, the plan being read for the S&amp;P 500's members alone</p>",
            Trend(await client.GetStringAsync($"/screens/name/S1/{IndexNight}")));
        Assert.Equal(
            "<p class=\"trend-state\" data-trend-state=\"none\">no ladder row for this name yet</p>",
            Trend(await client.GetStringAsync($"/screens/name/AAA/{IndexNight}")));
    }
}
