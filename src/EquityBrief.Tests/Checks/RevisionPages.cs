using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;

namespace EquityBrief.Tests.Checks;

// revision-pages. Every page Paper trading, Past picks and a stock's page draw holds to four rules: one trade or order is
// drawn with one take-profit wherever it stands on the page; a variant changes one setting from its running revision; a
// list of stocks draws at most ten rows a page; and no visible text says "version" or names a variant by a number.
// see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
// see: Every list of stocks on Paper trading draws at most ten rows a page in a stated order, with the rows it shows counted above its pager
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public class RevisionPages
{
    static readonly Regex Marked = new(@"<tr\b[^>]*\bdata-ticker=""(?<ticker>[^""]+)""[^>]*\bdata-(?:listed|placed|night)=""(?<day>[^""]+)""[^>]*\bdata-(?:take-profit|target)=""(?<price>[^""]+)""", RegexOptions.CultureInvariant);

    static readonly Regex Pager = new(@"class=""pager""[^>]*\bdata-rows=""(?<rows>\d+)""", RegexOptions.CultureInvariant);

    static readonly Regex Changes = new(@"class=""vrow[^""]*""[^>]*\bdata-changes=""(?<changes>\d+)""", RegexOptions.CultureInvariant);

    static readonly Regex Hidden = new(@"<(script|style)\b.*?</\1>|<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // The words no page names a rule's revision, its variants or its trades by: a version or a numbered variant, a provisional
    // rule, a live setup or a live rule, and a rule's list, the waiting list standing as the orders' own name and the watch list
    // as the operator's; "The short version" is a report's section in ordinary English and stands outside the check, as does a
    // release reading's version.
    static readonly Regex Version = new(@"(?<![\w'’-])(?:(?<!reading )versions?|V[1-9]|provisional|live|(?<!waiting |watch )lists?)(?![\w'’-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex ShortVersion = new(@"The short version", RegexOptions.CultureInvariant);

    // What a drawn page breaks of the four rules, none where it keeps them all.
    internal static IReadOnlyList<string> Faults(string html)
    {
        var faults = new List<string>();

        foreach (var trade in Marked.Matches(html).GroupBy(match => (match.Groups["ticker"].Value, match.Groups["day"].Value)))
        {
            string[] prices = [.. trade.Select(match => match.Groups["price"].Value).Where(price => price != "none").Distinct(StringComparer.Ordinal)];

            if (prices.Length > 1)
            {
                faults.Add($"{trade.Key.Item1} of {trade.Key.Item2} drawn with {prices.Length} take-profits: {string.Join(", ", prices)}");
            }
        }

        faults.AddRange(Pager.Matches(html).Where(match => int.Parse(match.Groups["rows"].Value, System.Globalization.CultureInfo.InvariantCulture) > 10).Select(match => $"a list draws {match.Groups["rows"].Value} rows on one page"));
        faults.AddRange(Changes.Matches(html).Where(match => match.Groups["changes"].Value != "1").Select(match => $"a variant changes {match.Groups["changes"].Value} settings from its running revision"));

        var text = ShortVersion.Replace(WebUtility.HtmlDecode(Hidden.Replace(html, " ")), " ");

        faults.AddRange(Version.Matches(text).Select(match => $"'{match.Value}' in the visible text: ...{text[Math.Max(0, match.Index - 40)..Math.Min(text.Length, match.Index + 40)]}..."));

        if (text.Contains(Api.Reading.RuleSentences.NoSentence, StringComparison.Ordinal))
        {
            faults.Add("a setting is drawn with no sentence written for it");
        }

        return faults;
    }

    [Fact]
    public async Task PaperTradingKeepsTheFourRulesOverEveryConstructedStore()
    {
        var pages = (await Reading.ReadSurface.LoopPagesForPlainWords()).Where(page => page.Page == PlainWords.PaperPage).ToArray();

        Assert.NotEmpty(pages);

        foreach (var (_, drawn, html) in pages)
        {
            var faults = Faults(html).Where(fault => !fault.StartsWith("a variant changes", StringComparison.Ordinal)).ToArray();

            Assert.True(faults.Length == 0, drawn + ":\n" + string.Join("\n", faults));
        }
    }

    [Fact]
    public void EachRuleIsFoundWhereAPageBreaksIt()
    {
        Assert.Empty(Faults("<tr data-ticker=\"RL\" data-listed=\"2026-11-16\" data-take-profit=\"469.92\"></tr><tr data-ticker=\"RL\" data-night=\"2026-11-16\" data-target=\"469.92\"></tr><div class=\"pager\" data-rows=\"10\" data-of=\"12\"></div><tr class=\"vrow\" data-account=\"a\" data-changes=\"1\"></tr><p>Revision 7, running since 16 Nov 2026; V is Visa</p>"));

        Assert.Single(Faults("<tr data-ticker=\"RL\" data-listed=\"2026-11-16\" data-take-profit=\"469.92\"></tr><tr data-ticker=\"RL\" data-night=\"2026-11-16\" data-target=\"413.60\"></tr>"));
        Assert.Single(Faults("<div class=\"pager\" data-rows=\"11\" data-of=\"30\"></div>"));
        Assert.Single(Faults("<tr class=\"vrow open\" data-account=\"a\" data-changes=\"2\"></tr>"));
        Assert.Single(Faults("<p>This version since 16 Nov 2026</p>"));
        Assert.Single(Faults("<option>V3 · best 3 a night</option>"));
        Assert.Empty(Faults("<p data-version=\"v\" title=\"the live swing filter, version 5\">Revision 7</p>"));
    }

    // RL's order of the redesign's own report: bought at $375.23 under a plan whose stop is 4 typical days under the buy and
    // whose take-profit is 2 times the risk alone, its sale placing no band above the buy; a row stored the swing plan's
    // first band, $413.60, which the walk never reads.
    [Fact]
    public void ATradeTakesProfitWhereItsPlansWalkSellsAndNeverAtItsStoredTarget()
    {
        var order = new PlanOrder("words", "1.13 typical days under the close", 376.2147623491241, 20, "4 typical days under the buy", 382.0201, null, 11.836493496350329, null, [], null, null, 63)
        {
            TakeProfit = 2,
            Placing = TakeProfitPlacing.Risk,
        };
        var exit = TradeExit.Filled(order, 375.23)!;
        var stop = 375.23 - (4 * 11.836493496350329);

        Assert.Equal(375.23 + (2 * (375.23 - stop)), exit.First!.Value, 6);
        Assert.Null(exit.Half);
        Assert.False(exit.FirstIsPart);
        Assert.Equal(exit.First, order.SaleFrom(375.23, stop).Target);
    }

    // code-revisions. The exit the code places for every rule is the one its newest code revision recorded, so an exit changed
    // in code with no revision recorded for it fails here; and every family rule's evaluator has an entry.
    // see: Any change that changes what a rule's trades do starts a new revision, in its settings or in its code
    [Fact]
    public void EveryRulesExitInCodeIsTheOneItsNewestRevisionRecorded()
    {
        string[] moved =
        [
            .. CodeRevisions.All.Select(entry => entry.Evaluator).Distinct(StringComparer.Ordinal)
                .Select(evaluator => (Evaluator: evaluator, Newest: CodeRevisions.All.Where(entry => entry.Evaluator == evaluator).OrderBy(entry => entry.From ?? DateOnly.MinValue).Last(), Now: CodeRevisions.ExitNow(evaluator)))
                .Where(one => one.Newest.Exit != one.Now)
                .Select(one => $"{one.Evaluator}: the code's exit reads {one.Now} and its newest revision recorded {one.Newest.Exit}"),
        ];

        Assert.True(moved.Length == 0, "A change to what a rule's trades do starts a revision: record it in CodeRevisions with the session it first runs and what it changed.\n" + string.Join("\n", moved));

        string[] families = [.. Core.Candidates.CandidateEvaluators.All.Where(evaluator => Api.Reading.RuleScreen.KindOf(evaluator) is not null || evaluator is Core.Candidates.DipCandidate).Select(evaluator => evaluator.Name)];

        Assert.Empty(families.Except(CodeRevisions.All.Select(entry => entry.Evaluator), StringComparer.Ordinal));
    }

    // variant-sentences. Every setting a rule's card draws has a sentence a variant is named by, filled with its value, and one
    // for the setting left out: each kind's settings stated at every branch its chips draw, and the dip's on every index.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    [Fact]
    public void EverySettingAVariantCanChangeHasItsSentence()
    {
        Dictionary<string, double> Of(params (string Key, double Value)[] pairs) => pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var stated = new List<(Core.Cards.RuleKind Kind, Dictionary<string, double> Parameters)>();

        foreach (var trade in new[] { 0.0, 1, 2 })
        {
            stated.Add((Core.Cards.RuleKind.Pullback, Of(("strengthFloor", 0.5), ("depthLow", 1), ("depthHigh", 5), ("dryUpCeiling", trade == 0 ? 1e9 : 1.5), ("arrivalSessions", trade == 1 ? 1 : 3), ("earningsWindowSessions", 15), ("rewardToRiskFloor", 2), ("stopLow", 0.5), ("stopHigh", 4), (SwingFilterRule.MarketGateParameter, trade == 2 ? 0 : 1), ("breadthFloor", 0.45), (SwingFilterRule.TradeParameter, trade), (SwingFilterRule.SkipDeterioratingParameter, 1), (SwingFilterRule.FiledStateParameter, trade == 1 ? 1 : 0), (SwingFilterRule.LeaderSectorsParameter, 3), (SwingFilterRule.LeaderShareOfParameter, 4), (SwingFilterRule.BestOfParameter, 3), (SwingFilterRule.RaisedEstimatesParameter, 1))));
        }

        foreach (var kind in new[] { Core.Cards.RuleKind.Breakout, Core.Cards.RuleKind.IndexBreakout })
        {
            stated.Add((kind, Of((BreakoutCandidate.HighSessionsParameter, 126), (BreakoutCandidate.VolumeMultipleParameter, 1.5), (BreakoutCandidate.RangeCeilingParameter, 0.85), (BreakoutCandidate.StopMovesParameter, 1.5), (IndexBreakoutCandidate.NearnessParameter, 0.02), (IndexBreakoutCandidate.RecencyParameter, 1), (IndexBreakoutCandidate.HighVolumeParameter, 2), (Core.Families.MarketSwitches.IndexAverageParameter, 200), (Core.Families.MarketSwitches.VixLookbackParameter, 10))));
            stated.Add((kind, Of((BreakoutCandidate.HighSessionsParameter, 251), (BreakoutCandidate.VolumeMultipleParameter, 1.25), (BreakoutCandidate.RangeCeilingParameter, 0), (BreakoutCandidate.StopMovesParameter, 1), (IndexBreakoutCandidate.RecencyParameter, -1), (IndexRuleCandidate.LargeBreadthParameter, 1), (IndexRuleCandidate.BreadthFloorParameter, 0.4), (IndexRuleCandidate.QualityParameter, 0), (IndexRuleCandidate.LowestCloseParameter, 5), (IndexRuleCandidate.HoldParameter, 63), (IndexRuleCandidate.IndustryFallParameter, 21), (IndexRuleCandidate.FundOverSpyParameter, 63), (IndexRuleCandidate.CreditAverageParameter, 1), (IndexRuleCandidate.CreditChangeParameter, 1))));
        }

        foreach (var kind in new[] { Core.Cards.RuleKind.Drift, Core.Cards.RuleKind.IndexDrift })
        {
            stated.Add((kind, Of((DriftCandidate.WindowSessionsParameter, 3), (DriftCandidate.ReactionMovesParameter, 0.5), (DriftCandidate.VolumeMultipleParameter, 2), (DriftCandidate.TargetRiskMultipleParameter, 2.5), (DriftCandidate.StopFloorMovesParameter, 1), (IndexDriftCandidate.WideWindowParameter, 10), (IndexDriftCandidate.PeersParameter, 1), (IndexDriftCandidate.EntryParameter, 1), (IndexDriftCandidate.RevenueGrewParameter, 1), (IndexRuleCandidate.LargeBreadthParameter, IndexRuleCandidate.NoMarketCheck), (IndexRuleCandidate.QualityParameter, 2))));
        }

        stated.Add((Core.Cards.RuleKind.IndexPullback, Of((IndexPullbackCandidate.StrengthParameter, 0.75), (IndexPullbackCandidate.DepthLowParameter, 1), (IndexPullbackCandidate.DepthHighParameter, 3), (IndexPullbackCandidate.DryUpParameter, 1.5), (IndexPullbackCandidate.FreshnessParameter, 8), (IndexPullbackCandidate.RewardToRiskParameter, 2), (IndexPullbackCandidate.StopLowParameter, 0.5), (IndexPullbackCandidate.StopHighParameter, 4), (IndexPullbackCandidate.BandParameter, 6), (IndexPullbackCandidate.MarketParameter, 0.35), (IndexRuleCandidate.LargeBreadthParameter, 2), (IndexRuleCandidate.BreadthFloorParameter, 0.35))));
        stated.Add((Core.Cards.RuleKind.IndexPullback, Of((IndexPullbackCandidate.DryUpParameter, 0), (IndexPullbackCandidate.MarketParameter, 0))));

        foreach (var kind in new[] { Core.Cards.RuleKind.Heavyweights, Core.Cards.RuleKind.IndexHeavyweights })
        {
            foreach (var exit in new[] { (1.0, 1.0), (1, 0), (0, 1) })
            {
                stated.Add((kind, Of((SectorHeavyweightCandidate.LargestParameter, exit.Item1 == 1 && exit.Item2 == 1 ? 0 : 10), (SectorHeavyweightCandidate.LookBackParameter, 251), (IndexHeavyweightCandidate.TwelveOneParameter, exit.Item2 == 0 ? 1 : 0), (SectorHeavyweightCandidate.LeadersParameter, 2), (SectorHeavyweightCandidate.FundParameter, exit.Item1), (SectorHeavyweightCandidate.BetaParameter, 1), (SectorHeavyweightCandidate.WeeklyParameter, 1), (SectorHeavyweightCandidate.LeadingExitParameter, exit.Item1), (SectorHeavyweightCandidate.AverageExitParameter, exit.Item2))));
            }

            stated.Add((kind, Of((IndexHeavyweightCandidate.DesignParameter, IndexHeavyweightCandidate.DesignB), (IndexHeavyweightCandidate.IndustriesParameter, 0), (IndexHeavyweightCandidate.WindowParameter, 63), (IndexHeavyweightCandidate.MembersParameter, 1))));
            stated.Add((kind, Of((IndexHeavyweightCandidate.DesignParameter, IndexHeavyweightCandidate.DesignB), (IndexHeavyweightCandidate.IndustriesParameter, 5), (IndexHeavyweightCandidate.WindowParameter, 126), (IndexHeavyweightCandidate.MembersParameter, 2))));
        }

        foreach (var valuation in new[] { 0.0, 1, 2 })
        {
            stated.Add((Core.Cards.RuleKind.Quality, Of((QualityCandidate.FloorParameter, 7), (QualityCandidate.ValuationParameter, valuation), (QualityCandidate.TrendParameter, 1), (QualityCandidate.BookParameter, 10), (QualityCandidate.SaleParameter, valuation == 1 ? 1 : 0))));
        }

        stated.Add((Core.Cards.RuleKind.Improving, Of((ImprovingCandidate.BuyParameter, 7), (ImprovingCandidate.FlagParameter, 1), (ImprovingCandidate.RiseParameter, 1), (ImprovingCandidate.FasterParameter, 1), (ImprovingCandidate.HoldParameter, 7), (ImprovingCandidate.LimitParameter, 126))));

        // The plan the test of its buy and sell points chose, as a live rule's hooks state it.
        stated.Add((Core.Cards.RuleKind.Pullback, new Dictionary<string, double>(PlanHook.ParametersOf(PlanPrices.Named("1.13 typical days under the close within 20 sessions, 4 typical days under the buy, the first band above, taking profit at 2 times the risk alone")!), StringComparer.Ordinal)));

        var chips = stated.SelectMany(one => Core.Cards.RuleChips.Of(one.Kind, one.Parameters))
            .Concat(new[] { "GSPC", "MID", "SML" }.SelectMany(index => Core.Families.DipRule.Provisional.KeyedChipsOn(index).Select(chip => new Core.Cards.RuleChip(chip.Key, chip.Words))))
            .ToArray();

        Assert.True(chips.Length > 60, $"only {chips.Length} chips were drawn");

        string[] unread = [.. chips.Where(chip => Api.Reading.RuleSentences.Of(chip) is null || Api.Reading.RuleSentences.Without(chip) is null).Select(chip => chip.Key + ": " + chip.Words).Distinct(StringComparer.Ordinal)];

        Assert.True(unread.Length == 0, "No sentence is written for:\n" + string.Join("\n", unread));
    }

    // A code revision splits the run of registrations it falls in, the settings the same, and one before the run began or
    // after the newest registration's first session runs from its own session.
    [Fact]
    public void ARevisionStartsAtAChangeInCodeAsAtAChangeInSettings()
    {
        (long, string, DateTimeOffset, string)[] lineage =
        [
            (1, "the live rule", new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero), "{\"a\": 1}"),
            (2, "the live rule", new DateTimeOffset(2026, 11, 15, 23, 27, 0, TimeSpan.Zero), "{\"a\": 1}"),
            (3, "the live rule", new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), "{\"a\": 1}"),
        ];
        var revisions = Core.Candidates.RuleRevisions.Of(lineage, [new CodeRevision("e", new DateOnly(2026, 11, 16), "Gains its own exit", "x")]);

        Assert.Equal(2, revisions.Count);
        Assert.Equal(new DateOnly(2026, 11, 13), revisions[0].Until);
        Assert.Equal(new DateOnly(2026, 11, 16), revisions[1].From);
        Assert.Equal("Gains its own exit", revisions[1].Code);
        Assert.Single(Core.Candidates.RuleRevisions.Of(lineage));
    }

    // A half the walk never reaches, at or above the plan's own target, is drawn nowhere: the whole sells at the target.
    [Fact]
    public void AHalfAtOrAboveTheTargetIsNeverItsExitAndOneBelowItSellsFirst()
    {
        var order = new PlanOrder("words", PlanPoints.NextOpen, null, 0, PlanPoints.RuleStop, 33.45, null, 1.5, 47.14, [], null, null, 63)
        {
            TakeProfit = 1.5,
            Half = true,
        };

        var over = TradeExit.Of(order, 40.64, 33.45);

        Assert.Null(over.Half);
        Assert.Equal(47.14, over.First!.Value, 6);
        Assert.False(over.FirstIsPart);

        var under = TradeExit.Of(order with { Target = 60 }, 40.64, 33.45);

        Assert.Equal(40.64 + (1.5 * (40.64 - 33.45)), under.Half!.Value, 6);
        Assert.Equal(under.Half, under.First);
        Assert.True(under.FirstIsPart);
    }
}
