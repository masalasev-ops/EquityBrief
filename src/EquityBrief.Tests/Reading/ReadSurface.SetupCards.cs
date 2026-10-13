using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.3: every setup card built one way. Each kind of rule's chips are its settings in the short words of its
// own table, worked by hand; every registered variant of the S&P 500's families differs from its live rule on a chip, so
// its tab names what it moves; the funnel's largest drop is the largest fall from one gate to the next, the earlier gate
// taking a tie; a card listing nothing says so in one line on both sides of its floors; each empty sector says why; and
// a heavyweight's standing tonight and the card's tiles are worked by hand at their edges.
// see: A variant's picks are shown on its card when chosen and its results only under its tests
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public partial class ReadSurface
{
    static string[] Words(RuleKind kind, IReadOnlyDictionary<string, double> parameters) =>
        [.. RuleChips.Of(kind, parameters).Select(chip => chip.Words)];

    [Fact]
    public void EachKindOfRulesChipsAreItsSettingsInTheShortWordsOfItsOwnTable()
    {
        // The S&P 500's pullback at a filter version's settings: its strength, depth, dry-up, arrival, report window,
        // reward to risk, stop, breadth and plan, the dials a variant may add drawn only where stated.
        Assert.Equal(
            ["stronger than 50%", "1 to 5 moves down", "volume under 1.5 times", "turns up within 3", "no report in 15 sessions", "1.5 times the risk", "stop 0.5 to 4 moves", "breadth at least 45%", "the swing plan"],
            Words(RuleKind.Pullback, SwingFilterRule.ParametersOf(new FilterSettings(0.45, 0.5, 1, 5, 1.5, 1.5, 0.5, 4, 15, 3, TradeInput.Swing))));
        Assert.Equal(
            ["stronger than 50%", "1 to 5 moves down", "no dry-up gate", "turns up within 3", "no report in 15 sessions", "1.5 times the risk", "stop 0.5 to 4 moves", "market check off", "the ladder's plan", "no deteriorating business", "best 3 a night", "estimates raised"],
            Words(RuleKind.Pullback, SwingFilterRule.ParametersOf(new FilterSettings(0.45, 0.5, 1, 5, double.PositiveInfinity, 1.5, 0.5, 4, 15, 3, TradeInput.Ladder), marketGate: false, skipDeteriorating: true, bestOf: 3, raisedEstimates: true)));

        // The breakout and the drift, each switch drawn only where it is read.
        Assert.Equal(
            ["126-session high", "volume 1.5 times", "ranges at 0.85", "stop 1.5 moves", "index above its 200-day"],
            Words(RuleKind.Breakout, new Dictionary<string, double> { ["highSessions"] = 126, ["volumeMultiple"] = 1.5, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5, ["indexAverageSessions"] = 200, ["vixLookbackSessions"] = 0 }));
        Assert.Equal(
            ["within 3 sessions", "up 0.5 moves", "volume 2 times", "target 2.5 times the risk", "stop at least 1 move", "VIX falling over 10"],
            Words(RuleKind.Drift, new Dictionary<string, double> { ["windowSessions"] = 3, ["reactionMoves"] = 0.5, ["volumeMultiple"] = 2, ["targetRiskMultiple"] = 2.5, ["stopFloorMoves"] = 1, ["indexAverageSessions"] = 0, ["vixLookbackSessions"] = 10 }));

        // The S&P 500's sector heavyweights at the live setting.
        Assert.Equal(
            ["10 largest", "251-session look-back", "2 leaders", "against the fund", "beta at least 1", "sold on losing the lead"],
            Words(RuleKind.Heavyweights, SectorHeavyweightCandidate.ParametersOf(HeavyweightRule.Live)));

        // An S&P 400 or 600 pullback, its levels after its dials and its own market dial beside the breadth it reads; with
        // the market check off the dial is no part of the chip.
        var pullback = new Dictionary<string, double>
        {
            ["band"] = 0, ["creditAverage"] = 0, ["creditChange"] = 0, ["depthHigh"] = 5, ["depthLow"] = 1, ["dryUp"] = 1.5, ["floors"] = 1, ["freshness"] = 3, ["fundOverSpy"] = 0,
            ["hold"] = 0, ["industryFall"] = 0, ["largeBreadth"] = 0, ["lowestClose"] = 5, ["market"] = 0.45, ["quality"] = 1, ["rewardToRisk"] = 2, ["stopHigh"] = 4, ["stopLow"] = 0.5, ["strength"] = 0.5,
        };

        Assert.Equal(
            ["strength 0.5", "1 to 5 moves down", "volume under 1.5 times", "fresh within 3", "2 times the risk", "stop 0.5 to 4 moves", "band strength 0", "dollar floor 1 times", "close at least $5", "the profit gate", "market check on its own breadth at 45%"],
            Words(RuleKind.IndexPullback, pullback));
        Assert.Equal(
            IndexRuleCandidate.MarketCheckOff,
            Words(RuleKind.IndexPullback, new Dictionary<string, double>(pullback) { ["largeBreadth"] = IndexRuleCandidate.NoMarketCheck, ["market"] = 0 })[^1]);
        Assert.Equal(
            "market check on the S&P 500's breadth, its own dial off",
            Words(RuleKind.IndexPullback, new Dictionary<string, double>(pullback) { ["largeBreadth"] = 1, ["market"] = 0 })[^1]);

        // An index drift with its window and stop floor, and the index heavyweights in each design.
        Assert.Equal(
            ["within 3 sessions", "up 0.5 moves", "volume 2 times", "target 2.5 times the risk", "dollar floor 1 times", "close at least $5", "the profit gate", "market check off"],
            Words(RuleKind.IndexDrift, new Dictionary<string, double> { ["windowSessions"] = 3, ["reactionMoves"] = 0.5, ["volumeMultiple"] = 2, ["targetMultiple"] = 2.5, ["stopFloor"] = 0, ["wideWindow"] = 0, ["peers"] = 0, ["floors"] = 1, ["lowestClose"] = 5, ["quality"] = 1, ["largeBreadth"] = 2 }));
        Assert.Equal(
            ["10 largest", "251-session look-back", "2 leaders", "beta at least 1", "sold on losing the lead", "dollar floor 1 times", "profit and cover"],
            Words(RuleKind.IndexHeavyweights, new Dictionary<string, double> { ["design"] = 0, ["floors"] = 1, ["highBeta"] = 1, ["industries"] = 0, ["largest"] = 10, ["leaders"] = 2, ["lookBack"] = 251, ["members"] = 0, ["quality"] = 2, ["soldOnLeading"] = 1, ["soldUnderAverage"] = 0, ["twelveOne"] = 0, ["window"] = 0 }));
        Assert.Equal(
            ["design (b)", "5 strongest industries", "over 21 sessions", "1 member an industry", "dollar floor 1 times", "the profit gate"],
            Words(RuleKind.IndexHeavyweights, new Dictionary<string, double> { ["design"] = 1, ["window"] = 21, ["industries"] = 5, ["members"] = 1, ["quality"] = 1, ["floors"] = 1 }));

        // A tab names each chip the variant differs on, and a setting the live rule states that the variant does not.
        var live = RuleChips.Of(RuleKind.Breakout, new Dictionary<string, double> { ["highSessions"] = 126, ["volumeMultiple"] = 1.5, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5, ["indexAverageSessions"] = 200 });
        var variant = RuleChips.Of(RuleKind.Breakout, new Dictionary<string, double> { ["highSessions"] = 251, ["volumeMultiple"] = 1.5, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5 });

        Assert.Equal("V2 · 251-session high · without index above its 200-day", RuleChips.TabWords(2, RuleChips.Differing(live, variant)));
        Assert.Equal("V1 · the same settings", RuleChips.TabWords(1, RuleChips.Differing(live, live)));
    }

    [Fact]
    public void EveryRegisteredVariantOfTheSAndP500sFamiliesDiffersFromItsLiveRuleOnAChip()
    {
        // The pullback's family as registered under a version whose strength floor stands under the top third, as version
        // 5's does at 0.5: under the proposed settings, already at the top third, that variant would be the live rule.
        IReadOnlyList<IReadOnlyList<Registration>> families =
        [
            TheSwingFamily.For("5", FilterSettings.Proposed with { StrengthFloor = 0.5 }),
            TheSetupFamilies.Breakouts,
            TheSetupFamilies.Drifts,
            TheSetupFamilies.Heavyweights,
        ];

        foreach (var family in families)
        {
            var kind = RuleScreen.KindOf(CandidateEvaluators.Find(family[0].Evaluator)!)!.Value;
            var live = RuleChips.Of(kind, family[0].Parameters);

            foreach (var registration in family.Skip(1))
            {
                var differing = RuleChips.Differing(live, RuleChips.Of(kind, registration.Parameters));

                Assert.True(differing.Count > 0, $"{registration.Candidate} draws the live rule's chips: its tab could not name what it moves.");
                Assert.DoesNotContain("the same settings", RuleChips.TabWords(1, differing), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheFunnelsLargestDropIsTheLargestFallFromOneGateToTheNextTheEarlierTakingATie()
    {
        Assert.Equal(new FunnelDrop("new high", 6, 10), RuleFunnelDrop.Largest([("market", 10), ("new high", 4), ("volume", 3), ("tightened", 2), ("trade", 2)]));
        Assert.Equal(new FunnelDrop("volume", 8, 9), RuleFunnelDrop.Largest([("market", 9), ("new high", 9), ("volume", 1), ("trade", 1)]));
        Assert.Equal(new FunnelDrop("b", 4, 10), RuleFunnelDrop.Largest([("a", 10), ("b", 6), ("c", 2)]));
        Assert.Null(RuleFunnelDrop.Largest([("a", 3), ("b", 3)]));
        Assert.Null(RuleFunnelDrop.Largest([("market", 0), ("setup", 0)]));
        Assert.Null(RuleFunnelDrop.Largest([("market", 5)]));
    }

    // A card listing nothing tonight, its funnel the volume gate stopping 8 of the 9 reaching it, at a stretch given.
    static string EmptyCard(StretchLineView stretch, string? empty = "No stock passed this setup tonight.")
    {
        var live = new RuleChoiceView("the live breakout rule", RuleScreenWords.LiveSlug, null, true, "Live");
        var rule = new RuleView(live, [live], [new RulePartView("A stock closes above its high", false)], null, [("market", 9), ("new high", 9), ("volume", 1), ("trade", 1)], stretch, null, true, null);

        return new MarkRenderer().FamilyCard(new FamilyCardView("breakout", "Breakout", "Breakouts to a new high", "Breakout from a base", "A stock closes above its high.", 2, 3, new DateOnly(2026, 1, 2), 0, [], [], empty, RuleChoice: rule));
    }

    [Fact]
    public void ACardListingNothingSaysSoInOneLineOnBothSidesOfItsFloors()
    {
        // Under the floors: no usual stretch yet, the completed stretches against the thirty the mark waits for.
        Assert.Contains(
            "<p class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">Nothing tonight · most stopped at the volume gate (8 of 9) · no usual stretch yet (29 of 30)</p>",
            EmptyCard(new StretchLineView(14, null, false, 29, 600)),
            StringComparison.Ordinal);

        // At the floors: the empty stretch against its usual longest, and past it flagged.
        Assert.Contains(
            "<p class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">Nothing tonight · most stopped at the volume gate (8 of 9) · empty 14 nights, its usual longest 48</p>",
            EmptyCard(new StretchLineView(14, 48, false, 30, 600)),
            StringComparison.Ordinal);
        Assert.Contains(
            "<p class=\"family-empty-line flagged\" data-picks=\"0\" data-flagged=\"true\">Nothing tonight · most stopped at the volume gate (8 of 9) · empty 49 nights, its usual longest 48</p>",
            EmptyCard(new StretchLineView(49, 48, true, 30, 600)),
            StringComparison.Ordinal);
        Assert.Contains("· empty 1 night, its usual longest 48</p>", EmptyCard(new StretchLineView(1, 48, false, 30, 600)), StringComparison.Ordinal);

        // A card listing nothing names no pick and draws no stretch line of its own, the one line carrying it.
        var card = EmptyCard(new StretchLineView(14, 48, false, 30, 600));

        Assert.DoesNotContain("card-picks", card, StringComparison.Ordinal);
        Assert.DoesNotContain("stretch-line", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded family-empty\" data-picks=\"0\">No stock passed this setup tonight.</p>", card, StringComparison.Ordinal);
    }

    [Fact]
    public void EachEmptySectorSaysWhyItsBookBoughtNothingThere()
    {
        var on = new DateOnly(2026, 10, 1);

        HeavyweightReadRow Row(string sector, double? lead, bool trend, double? beta) =>
            new(on, sector, 1, sector[..2].ToUpperInvariant() + "1", "CIK 1", 1000m, 0.2, 0.1, lead, trend, false, beta);

        IReadOnlyList<HeavyweightReadRow> read =
        [
            Row("Energy", 0, true, 1.2),
            Row("Energy", -0.01, true, 1.2),
            Row("Utilities", 0.02, false, 1.2),
            Row("Materials", 0.02, true, 0.99),
            Row("Industrials", 0.02, true, 1.0),
            Row("Real Estate", 0.02, true, null),
        ];

        Assert.Equal("None of its largest companies beat its fund at the rebalance of 2026-10-01.", TonightScreen.EmptySectorReason("Energy", read, fundReturn: true, highBeta: true));
        Assert.Equal("None of its largest companies beat its members' mean at the rebalance of 2026-10-01.", TonightScreen.EmptySectorReason("Energy", read, fundReturn: false, highBeta: true));
        Assert.Equal("Its leaders failed the trend gate at the rebalance of 2026-10-01.", TonightScreen.EmptySectorReason("Utilities", read, fundReturn: true, highBeta: true));
        Assert.Equal("Its leaders' beta sat under one at the rebalance of 2026-10-01.", TonightScreen.EmptySectorReason("Materials", read, fundReturn: true, highBeta: true));
        Assert.Equal("Its leaders' beta sat under one at the rebalance of 2026-10-01.", TonightScreen.EmptySectorReason("Real Estate", read, fundReturn: true, highBeta: true));
        Assert.Equal("What its rebalance of 2026-10-01 bought has been sold since.", TonightScreen.EmptySectorReason("Industrials", read, fundReturn: true, highBeta: true));
        Assert.Equal("What its rebalance of 2026-10-01 bought has been sold since.", TonightScreen.EmptySectorReason("Materials", read, fundReturn: true, highBeta: false));
        Assert.Equal("Its last rebalance read none of its companies.", TonightScreen.EmptySectorReason("Health Care", read, fundReturn: true, highBeta: true));
        Assert.Equal("No rebalance has been read yet.", TonightScreen.EmptySectorReason("Energy", [], fundReturn: true, highBeta: true));
        Assert.Equal("This book stores no reading for a sector it bought nothing in.", TonightScreen.StoresNoSectorReading);
    }

    [Fact]
    public void AHeavyweightsStandingTonightIsReadAtTheEdgesOfItsLeadAndItsAverage()
    {
        // A return of 25 per cent over the look-back: level with its sector's has lost its lead, a hair over it is still
        // leading.
        Assert.Equal(HeavyweightStandings.LostItsLead, HeavyweightStandings.Of(100, 80, 90, 0.25).Word);
        Assert.Equal(HeavyweightStandings.StillLeading, HeavyweightStandings.Of(100, 80, 90, 0.2499).Word);
        Assert.Equal(0.25, HeavyweightStandings.Of(100, 80, 90, 0.2499).Return!.Value, 12);

        // A close at its average is not under it; a close a cent under is, whatever its lead.
        Assert.Equal(HeavyweightStandings.StillLeading, HeavyweightStandings.Of(100, 80, 100, 0.1).Word);
        Assert.Equal(HeavyweightStandings.BelowItsAverage, HeavyweightStandings.Of(100, 80, 100.01, 0.1).Word);
        Assert.Equal(0.1 / 0.9, HeavyweightStandings.Of(100, 80, 90, 0.1).OverAverage!.Value, 12);

        // A close, the close a look-back before or the sector's return not held: not read.
        Assert.Equal(HeavyweightStandings.NotRead, HeavyweightStandings.Of(null, 80, 90, 0.1).Word);
        Assert.Equal(HeavyweightStandings.NotRead, HeavyweightStandings.Of(100, null, 90, 0.1).Word);
        Assert.Equal(HeavyweightStandings.NotRead, HeavyweightStandings.Of(100, 80, 90, null).Word);

        // A sector's members' mean leaves a member holding none out, and an equal share reads each return alike.
        Assert.Equal(0.2, HeavyweightStandings.MembersMean([0.1, null, 0.3])!.Value, 12);
        Assert.Null(HeavyweightStandings.MembersMean([null]));
        Assert.Equal(0.0, HeavyweightStandings.EqualWeighted([(110, 100), (45, 50), (null, 10)])!.Value, 12);
    }

    [Fact]
    public async Task AHoldingsCloseALookBackBeforeTheNightIsReadFromTheKeptBarsOnlyWhereNoRefetchHasWrittenItsYearSince()
    {
        // The store's year holds two sessions, 10-01 and 10-02, and the fetcher kept 09-30 on the night of 10-01; a look-back
        // of two sessions reaches 09-30. Written nightly, the kept close is read; with the year written again by a refetch
        // after the drop, it is not, the two standing on different bases.
        static async Task<StandingCloses> Read(string observed)
        {
            using var store = new TemporaryStore().Migrated();

            foreach (var (day, close, at) in new[] { ("2026-10-01", "102", observed), ("2026-10-02", "104", "2026-10-02T23:10:00Z") })
            {
                store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{day}', '{close}', '{close}', '{close}', '{close}', 1000, 'bulk', '{at}', '{close}');");
            }

            store.Execute("INSERT INTO kept_bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close, kept_on) VALUES ('AAA', '2026-09-30', '80', '80', '80', '80', 1000, 'bulk', '2026-09-30T23:10:00Z', '80', '2026-10-01');");

            var read = new ReadApi(store.DatabaseFile, EquityBrief.Core.Time.FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero), EquityBrief.Core.Time.SessionZones.UnitedStates));

            return (await read.StockStandingClosesAsync(["AAA"], new DateOnly(2026, 10, 2), 2, new DateOnly(2026, 10, 1)))["AAA"];
        }

        Assert.Equal(new StandingCloses(104m, 80m, 102m), await Read("2026-10-01T23:10:00Z"));
        Assert.Equal(new StandingCloses(104m, 80m, 102m), await Read("2026-10-02T23:59:59Z"));
        Assert.Equal(new StandingCloses(104m, null, 102m), await Read("2026-10-03T00:00:01Z"));
    }

    [Fact]
    public void AHeavyweightsCardsTilesAndSectorsAreWorkedByHand()
    {
        // Two holdings: A in Energy at 110 tonight, 100 at the rebalance and 88 a look-back before; B in Utilities at 45,
        // 50 and 50. XLE at 52.5, 50 and 42; XLU at 20, 20 and 25.
        var night = new DateOnly(2026, 10, 2);
        var next = new DateOnly(2026, 11, 3);
        var card = new HeavyweightCardView(
            "Sector heavyweights", "Largest companies leading their sectors", "the rule", 251, new DateOnly(2026, 10, 1), next,
            [
                new HeavyweightHoldingCell("A", null, "Energy", new DateOnly(2026, 10, 1), 0.05, new DateOnly(2026, 10, 1), 110m, 100, false),
                new HeavyweightHoldingCell("B", null, "Utilities", new DateOnly(2026, 9, 1), 0.02, new DateOnly(2026, 9, 1), 45m, 46, true),
            ],
            ["A"], [], null);
        var stocks = new Dictionary<string, StandingCloses> { ["A"] = new(110m, 88m, 100m), ["B"] = new(45m, 50m, 50m) };
        var funds = new Dictionary<string, StandingCloses> { ["XLE"] = new(52.5m, 42m, 50m), ["XLU"] = new(20m, 25m, 20m) };
        double? Fund(string sector) => sector == "Energy" ? 0.25 : sector == "Utilities" ? -0.2 : null;

        var drawn = TonightScreen.WithStanding(card, night, stocks, funds, new Dictionary<string, double?> { ["A"] = 1.1, ["B"] = null }, Fund, _ => "none bought", largeFunds: false);
        var tiles = drawn.Tiles!;

        // Two holdings in two of the eleven sectors; the book up 10 and down 10 per cent since the rebalance, nothing in
        // all, and its funds up 5 and level, 2.5 per cent; the sessions to November's first rebalance on the calendar.
        Assert.Equal((2, 2, 11), (tiles.Holdings, tiles.Sectors, tiles.Of));
        Assert.Equal(0.0, tiles.BookSince!.Value, 12);
        Assert.Equal(0.025, tiles.FundsSince!.Value, 12);
        Assert.Equal(["XLE", "XLU"], tiles.Funds);
        Assert.Equal(EquityBrief.Core.Bars.ExchangeClosures.SessionsUntil(night, next), tiles.SessionsToNext);

        // A's return over the look-back 25 per cent against its fund's 25: lost its lead; its growth since bought 10 per
        // cent. B under its average of 46: below its 200-day, its growth not carried to the night.
        var a = drawn.Holdings.Single(holding => holding.Ticker == "A");
        var b = drawn.Holdings.Single(holding => holding.Ticker == "B");

        Assert.Equal(HeavyweightStandings.LostItsLead, a.Standing!.Word);
        Assert.Equal(0.1, a.SinceBought!.Value, 12);
        Assert.Equal(HeavyweightStandings.BelowItsAverage, b.Standing!.Word);
        Assert.Null(b.SinceBought);

        // The eleven sectors in order, each fund's return over the look-back, and a sector holding none saying why.
        Assert.Equal(EquityBrief.Core.Families.GicsSectors.Eleven, drawn.Sectors!.Select(sector => sector.Sector));
        Assert.Equal(0.25, drawn.Sectors!.Single(sector => sector.Sector == "Energy").FundReturn!.Value, 12);
        Assert.Equal(-0.2, drawn.Sectors!.Single(sector => sector.Sector == "Utilities").FundReturn!.Value, 12);
        Assert.Null(drawn.Sectors!.Single(sector => sector.Sector == "Energy").Reason);
        Assert.Equal("none bought", drawn.Sectors!.Single(sector => sector.Sector == "Materials").Reason);

        // On an index card the funds are named as the S&P 500's.
        Assert.Equal("XLE, the S&P 500's fund,", TonightScreen.WithStanding(card, night, stocks, funds, new Dictionary<string, double?>(), Fund, _ => null, largeFunds: true).Sectors!.Single(sector => sector.Sector == "Energy").Fund);

        // A design (b) book reads no standing of its own.
        Assert.All(TonightScreen.WithStanding(card, night, stocks, funds, new Dictionary<string, double?>(), Fund, _ => null, designB: true).Holdings, holding => Assert.Equal(HeavyweightStandings.NotForThisDesign, holding.Standing!.Word));
    }
}
