using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using LedgerHeadings = EquityBrief.Core.Ledger.LedgerHeadings;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// plain-words over Tonight, from 22.6: every card of the page on each index over constructed nights, the pick cards
// opened beneath their rows among them, and a pick's card drawn over constructed figures in every part it draws, each in
// the reader's words and every card opening on its summary sentence; the check failing a term planted in a pick's card,
// a card of the page opening on anything but its summary, and a card of a region the page's row does not name.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public partial class PlainWords
{
    // Every card of Tonight over every constructed night speaks in the reader's words and opens on its summary, and between
    // them the nights draw every region the page's row names and the pick cards beneath their rows.
    [Fact]
    public async Task EveryCardOfTonightSpeaksInTheReadersWordsOverEveryConstructedNight()
    {
        var terms = Terms();
        var pages = await Reading.ReadSurface.TonightPagesForPlainWords();

        Assert.Equal(9, pages.Count);
        Assert.All(pages, page => Assert.Empty(Faults(page.Page, RegionsOf(TonightPage), terms)));
        Assert.All(RegionsOf(TonightPage), region => Assert.Contains(pages, page => page.Page.Contains($"data-card=\"{region}\"", StringComparison.Ordinal)));
        Assert.True(pages.Count(page => page.Page.Contains("<div class=\"decision-card\"", StringComparison.Ordinal)) >= 3, "Fewer than three constructed nights drew a pick's card.");
    }

    // The steps each setup's first line names, as real nights stored them on each index, and every other line of the
    // checklist in each verdict the core writes it in.
    static IReadOnlyList<CardLineView> Lines(string family, string trend, bool sector = true)
    {
        var settings = CardSettings.Defaults;
        CardLine[] lines =
        [
            new(1, CardLines.TrendName, CardVerdict.Tick, trend),
            CardLines.Business(new BusinessReading(10m, 20.73m, false, false, 4), "steady", settings),
            CardLines.Market(new MarketReading(0.586, 0.45, true), sector ? new SectorPlace("Consumer Discretionary", -0.036, 8, 11, 47) : null, "S&P 500", settings),
            CardLines.Earnings(new ReportAhead(new DateOnly(2026, 11, 5), EventTiming.After, 10), 46, 63, settings),
            CardLines.Cost(374_500_000m, 0.005, family == HeavyweightRule.Name, true, settings),
            CardLines.Concentration("XYZ", "Industrials", [("A", "Industrials"), ("B", "Industrials"), ("C", "Energy")]),
        ];

        return [.. lines.Select(line => new CardLineView(line.Number, line.Name, line.Mark, line.Words))];
    }

    // Every other verdict of the checklist's lines the core writes, so each is read on one card or another.
    static IReadOnlyList<CardLineView> OtherLines()
    {
        var settings = CardSettings.Defaults;
        CardLine[] lines =
        [
            new(1, CardLines.TrendName, CardVerdict.Tick, "Passed every gate of the pullback on the S&P 500."),
            CardLines.Business(new BusinessReading(-1m, 0.5m, false, false, 4), "deteriorating", settings),
            CardLines.Business(new BusinessReading(null, null, false, true, 2), null, settings),
            CardLines.Business(new BusinessReading(5m, null, true, false, 4), "improving", settings),
            CardLines.Market(new MarketReading(null, 0.45, false, "S&P 500"), new SectorPlace("Utilities", -0.104, 11, 11, 31), "S&P 600", settings),
            CardLines.Market(new MarketReading(0.4497, 0.45, true), null, "S&P 600", settings),
            CardLines.Earnings(null, 15, 63, settings),
            CardLines.Earnings(new ReportAhead(new DateOnly(2027, 6, 5), EventTiming.Unstated, null), 15, 63, settings),
            CardLines.Earnings(new ReportAhead(new DateOnly(2026, 11, 24), EventTiming.Unstated, 24), 15, 63, settings),
            CardLines.Earnings(new ReportAhead(new DateOnly(2026, 11, 9), EventTiming.Before, 12), null, 10, settings),
            CardLines.Earnings(new ReportAhead(new DateOnly(2026, 12, 9), EventTiming.After, 31), null, 10, settings),
            CardLines.Earnings(new ReportAhead(new DateOnly(2026, 12, 9), EventTiming.After, 31), 15, null, settings),
            CardLines.Cost(null, null, false, false, settings),
            CardLines.Cost(24_300_000m, 0.031, false, false, settings),
            CardLines.Cost(291_800_000m, 0.07, true, false, settings),
            CardLines.Concentration("XYZ", null, []),
            CardLines.Concentration("XYZ", "Energy", [("A", "Energy")]),
        ];

        return [.. lines.Select(line => new CardLineView(line.Number, line.Name, line.Mark, line.Words))];
    }

    // The first lines real nights stored, word for word: the S&P 500's with the reasons its rows stored, the S&P 400's and
    // 600's as their nights' settings state their rules, the dip's in its own words and the sector heavyweights'.
    static readonly (string Family, string Index, string Words)[] StoredTrends =
    [
        (DriftRule.Name, "GSPC", "Passed every gate of the earnings drift on the S&P 500: market, breadth 58.6% at or above its floor of 45%; print, the print of 2026-10-22 reacted on 2026-10-22, 1 session(s) before tonight, inside the 3-session window; beat, the print of 2026-10-22 beat its estimate by 7.5%; reaction, the reaction session closed up 2.06 typical moves, at or above 0.5 up; volume, the reaction session's volume was 2.96 times its 50-session average, at or above 2.0; held, the close of 33.42 is above the reaction session's low of 29.9; trade, bought at the close of 33.42 with the stop at the reaction session's low, 29.9, and the target at 38.1297, the lowest band 2 typical moves or more above the close, a reward to risk of 1.34."),
        (SetupFamilies.Pullback, "GSPC", "Passed every gate of the pullback on the S&P 500: market, breadth 56.2% at or above its floor of 45%; trend and strength, an uptrend; strength 0.969 at or above its floor of 0.500; setup, a pullback into an anchored support band; trigger, the trigger fired tonight and not on 2026-10-20; trade, reward to risk 2.7411 at or above 2; the stop 0.97 typical moves below the entry, inside 0.5 to 4."),
        (BreakoutRule.Name, "MID", "Passed every gate of the breakout on the S&P 400: floors, a close of at least $5 and 10 million dollars a session over 50 sessions; its rule, breakout rule at a 251-session high, 1.5 times the volume, ranges at 1 and the stop 3 typical moves beneath, the profit gate, its market check on the S&P 500's breadth."),
        (DriftRule.Name, "SML", "Passed every gate of the earnings drift on the S&P 600: floors, a close of at least $5 and 5 million dollars a session over 50 sessions; its rule, drift rule within 3 sessions, up 0.5 typical moves on 1.25 times the volume, the target at 2.5 times the risk, the stop at least 1 typical move beneath, the profit gate, held 63 sessions, the market check off, its print's quarter's revenue above the same quarter's a year before, both as first filed and stored before the night."),
        (BreakoutRule.Name, "SML", "Passed every gate of the breakout on the S&P 600: floors, a close of at least $5 and 5 million dollars a session over 50 sessions; profit, the 4 newest quarters' net income above nothing; market, the index's breadth at or above 45%; its setting, high 126, volume 1.5, ceiling 0.85, stop 1.5."),
        (FundamentalsRule.Name, "SML", "Passed every gate of the fundamentals first on the S&P 600: floors, a close of at least $5 and 5 million dollars a session over 50 sessions; profit in the index's form, the 4 newest quarters' net income above nothing and the newest quarter's above nothing; market, the index's breadth at or above 45%; its setting, growth 0, margins either, cash 1.0; the business, revenue up more than 0 per cent on the year-earlier quarter and growing faster than the quarter before, the gross or the operating margin up on the year, cash from operations at least 1.0 times net income, closing above its 200-day average with the 50-day above it, at a pullback's buy point."),
        (DipRule.Name, "MID", "Passed every gate of the short-term dip on the S&P 400: floors, those the index families read on its index; trend, the close above its 200-session average; drop, its 2-session RSI under 10; report, none on the 3 sessions to the listing nor due before its 10 sessions end, the next on file on 2026-11-09; market, its own setting, none while it is provisional."),
        (HeavyweightRule.Name, "MID", "Passed every gate of the sector heavyweights on the S&P 400: it led Consumer Discretionary at the rebalance of 2026-10-20, its trend and its beta passing."),
    ];

    static readonly DateOnly CardNight = new(2026, 10, 23);

    // What could hit a trade in each form the night stores it.
    static readonly CardHitsView[] HitsDrawn =
    [
        new(new DateOnly(2027, 1, 21), 4, 1.75, 0.66, 1, new DateOnly(2026, 11, 23), false, 0.24m, 0.068, null, [(new DateOnly(2026, 10, 28), "FOMC decision"), (new DateOnly(2026, 11, 10), "CPI release")], ["CPI release"]),
        new(new DateOnly(2026, 11, 18), 0, null, null, 0, new DateOnly(2026, 10, 30), true, 0.35m, null, 0.4, [], []),
        new(new DateOnly(2026, 11, 6), 4, 0.57, null, 0, null, false, null, null, null, [(new DateOnly(2026, 10, 28), "FOMC decision")], [], DividendUnread: true),
        new(new DateOnly(2026, 11, 6), 2, null, null, 0, null, false, null, null, null, [], ["FOMC decision", "CPI release"]),
    ];

    // The chart of what is behind a stock with every heading read, one heading not read, the push drawn, and the push
    // whose stored text could not be read.
    static BehindItView Behind(bool pushed, bool unread = false) => new(
        "S&P 500",
        [.. LedgerHeadings.All.Select((heading, at) => new BehindHeadingView(heading.Name, at == 1 ? null : 0.1 * (at + 2), pushed ? 0.05 * (at - 3) : null))],
        pushed ? 0.12 : null,
        pushed ? 0.31 : null,
        unread ? "the stored text names no weight for a heading" : null);

    // A pick's card in each form it is drawn in, over constructed figures and the lines real nights stored: every setup's
    // first line on each index, the checklist in every verdict, the setup's record in risks and in per cent, below the
    // minimum, not replayed and standing at an approved change, survivors only, the setups like it matched both ways and
    // unmatched with the score's rank and without, the chart with and without its push and unread, the plan in money
    // capped and whole at risk and the account unset, the operator's trades with the setup's own on the same nights, and
    // each kind of trade taken.
    static IReadOnlyList<DecisionCardView> CardsDrawn()
    {
        var record = new CardRecordView("the drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the target at 2.5 times the risk, held 63 sessions, the profit gate", 2251, 0.338, 0.0214, "risks", 9, 25, 0.75, -0.976, new DateOnly(2019, 1, 2), new DateOnly(2026, 10, 15), "as it stood");
        var percent = new CardRecordView("heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold under its 200-day average, no quality gate", 335, null, 4.64, "percent", 76, 140, 0.75, -7.82, new DateOnly(2019, 1, 2), new DateOnly(2026, 10, 19), "survivors only");
        var matched = new CardSimilar(["breadth", "close_over_fifty", "close_over_long", "index_over_long", "return_half_year", "close_over_twenty"], 250, 0.34, 0.087, -0.263, 0.436, -0.079, 2245);
        var apart = new CardSimilar(["breadth", "liquidity", "high_ratio", "small_over_large", "fifty_over_long", "index_over_long"], 250, 0.46, 1.086, 0.693, 1.479, 0.548, 1139);
        var worse = new CardSimilar(["breadth"], 120, 0.5, -0.4, -0.6, -0.2, 0.1, 900);
        var unmatched = new CardSimilar(Unmatched: "the setups like it wait for the next ledger history build under this rule");
        var money = new CardMoneyView(120, 418.8m, 4010.4m, 0.04m, 3.21m, true, false, 1m);
        var whole = new CardMoneyView(40, 7948.8m, 7948.8m, 0.0795m, null, false, true, 1m);
        CardTakenView[] taken =
        [
            new("2026-10-24T12:00:00Z", 33.42m, new DateOnly(2026, 10, 26), true, null, null, false),
            new("2026-10-20T12:00:00Z", 30.1m, new DateOnly(2026, 10, 21), false, 31.5m, new DateOnly(2026, 10, 23), true),
            new("2026-10-10T12:00:00Z", 28.4m, new DateOnly(2026, 10, 13), false, null, null, true, new DateOnly(2026, 10, 22), "stop", 27.9m),
        ];
        var mine = new CardOperatorRecordView("risks", 3, 2, 6, 1, null, TakenWalk.RecordMinimum, false, new CardSameNightsView(4, 9, 3, 3, 7, null));
        var averaged = new CardOperatorRecordView("percent", 0, 0, 21, 2, 1.2, TakenWalk.RecordMinimum, true, new CardSameNightsView(12, 30, 0, 0, 28, 0.8));

        var cards = new List<DecisionCardView>();

        foreach (var (family, index, words) in StoredTrends)
        {
            var heavyweights = family == HeavyweightRule.Name;
            var dip = family == DipRule.Name;
            var trails = family == BreakoutRule.Name;
            decimal? stop = heavyweights ? null : 29.9m;
            decimal? target = heavyweights || dip || trails ? null : 38.13m;
            decimal? trail = trails ? 3.5m : null;
            int? cap = heavyweights ? null : dip ? 10 : 60;

            cards.Add(new DecisionCardView(
                index,
                CardNight,
                family,
                "XYZ",
                $"The {(SetupFamilies.Named(family)?.Label ?? "sector heavyweights").ToLowerInvariant()} on the {new IndexMembers(index, 0).Named}",
                33.42m,
                stop,
                target,
                Lines(family, words),
                heavyweights ? percent : record,
                dip ? Management.ForDip(stop, 5, cap) : Management.For(stop, target, trail, cap, heavyweights),
                heavyweights ? whole : money,
                false,
                taken,
                true,
                HitsDrawn[cards.Count % HitsDrawn.Length],
                heavyweights ? averaged : mine,
                new CardScoreView(cards.Count % 2 == 0 ? null : 37, (cards.Count % 4) switch { 0 => matched, 1 => apart, 2 => worse, _ => unmatched }),
                null,
                Behind(cards.Count % 2 == 1, cards.Count == 2),
                cap,
                trail));
        }

        // The checklist's every other verdict, a record below the minimum, a rule not replayed, a rule standing at an
        // approved change, and a card with the account not set and no trade taken.
        cards.Add(new DecisionCardView("GSPC", CardNight, SetupFamilies.Pullback, "XYZ", "The pullback on the S&P 500", 50m, 47m, 56m, OtherLines(), record with { Trades = 12 }, AccountUnset: true, Pressable: true, Cap: 63));
        cards.Add(new DecisionCardView("SML", CardNight, BreakoutRule.Name, "XYZ", "The breakout on the S&P 600", 50m, 47m, null, OtherLines(), null, Pressable: true, Trail: 3m, Cap: 63));
        cards.Add(new DecisionCardView("MID", CardNight, DriftRule.Name, "XYZ", "The earnings drift on the S&P 400", 50m, 47m, 58m, OtherLines(), null, Approved: "exit: the seventh", Cap: 63));

        return cards;
    }

    // A pick's card in every part and every form it draws speaks in the reader's words, opened beneath a row of tomorrow's
    // buys as Tonight draws it; and its plan is the setup's own alone, one stop and at most one target, with no levels plan.
    [Fact]
    public void APicksCardSpeaksInTheReadersWordsInEveryPartAndFormItDraws()
    {
        var terms = Terms();
        var marks = new MarkRenderer();
        var cards = CardsDrawn();

        Assert.Equal(StoredTrends.Length + 3, cards.Count);

        foreach (var card in cards)
        {
            var drawn = marks.DecisionCard(card, linked: true);
            var page = Cards.Computed("Tomorrow's buys", "<p class=\"card-summary\" data-summary>One stock to buy at tomorrow's open.</p>" + drawn, region: "buy-points");

            Assert.Empty(Faults(page, RegionsOf(TonightPage), terms));

            // One plan, the setup's own: one stop tile, at most one target, and no levels plan's tranches or bands.
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(drawn, "data-tile=\"stop\""));
            Assert.True(System.Text.RegularExpressions.Regex.Matches(drawn, "data-tile=\"target\"").Count <= 1);
            Assert.DoesNotContain("level-summary", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("selected-name", drawn, StringComparison.Ordinal);
            Assert.Contains(MarkRenderer.OnePlanSays(card.Family == HeavyweightRule.Name ? "sector heavyweights" : SetupFamilies.Named(card.Family)!.Label.ToLowerInvariant()), System.Net.WebUtility.HtmlDecode(drawn), StringComparison.Ordinal);
        }
    }

    // A word written all in capitals is a ticker and is never rewritten, though its letters spell a phrasing the list
    // rewrites, where the same word in a sentence is.
    [Fact]
    public void ATickerIsNeverRewrittenThoughItSpellsAPhrasingTheListRewrites()
    {
        Assert.Equal("HELD qualified again tonight, and its trade is kept.", ReaderWords.Plain("HELD qualified again tonight, and its trade is held."));
        Assert.Equal("Kept while it leads: sold at a later month's reshuffle.", ReaderWords.Plain("Held while it leads: sold at a later month's rebalance."));
    }

    // The sources 22.6 writes.
    static readonly string[] TonightSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/CardScreen.cs",
        "src/EquityBrief.Api/Reading/NameScreen.cs",
        "src/EquityBrief.Api/Reading/RuleScreen.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Families.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Heavyweights.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Indices.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Tonight.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Card.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.CloseToBuy.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Families.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Heavyweights.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Tonight.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.TonightTop.cs",
        "src/EquityBrief.Web/Marks/ReaderWords.cs",
        "src/EquityBrief.Web/Marks/RuleScreenWords.cs",
    ];

    // No source a rule's version pins is one Tonight's redesign writes, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTonightsRedesignWrites()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(TonightSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(TonightSources.Intersect(pinned, StringComparer.Ordinal));
    }

    // The check fails a term planted in a pick's card opened beneath its row on Tonight, a card of the page opening on
    // anything before its summary, and a card of a region the page's row does not name.
    [Fact]
    public async Task TheCheckFailsATermPlantedInAPicksCardOnTonightACardNotOpeningOnItsSummaryAndACardOfNoRegion()
    {
        var terms = Terms();
        var page = (await Reading.ReadSurface.TonightPagesForPlainWords()).First(one => one.Page.Contains("<div class=\"decision-card\"", StringComparison.Ordinal)).Page;

        Assert.Empty(Faults(page, RegionsOf(TonightPage), terms));

        var planted = page.Replace("<p class=\"card-summary\" data-summary>Picked by the", "<p class=\"card-summary\" data-summary>Its reaction session held. Picked by the", StringComparison.Ordinal);

        Assert.NotEqual(page, planted);
        Assert.Contains(Faults(planted, RegionsOf(TonightPage), terms), fault => fault.StartsWith("'held' in the card 'buy-points'", StringComparison.Ordinal));
        Assert.Contains(Faults(planted, RegionsOf(TonightPage), terms), fault => fault.StartsWith("'reaction session' in the card 'buy-points'", StringComparison.Ordinal));

        var before = page.Replace("<div class=\"card-b\"><div class=\"one-minute\"", "<div class=\"card-b\"><p>Before it</p><div class=\"one-minute\"", StringComparison.Ordinal);

        Assert.NotEqual(page, before);
        Assert.Contains(Faults(before, RegionsOf(TonightPage), terms), fault => fault == "The card 'one-minute' draws 'Before it' before its summary sentence.");

        var stray = page.Replace("data-card=\"words\"", "data-card=\"glossary\"", StringComparison.Ordinal);

        Assert.NotEqual(page, stray);
        Assert.Contains(Faults(stray, RegionsOf(TonightPage), terms), fault => fault == "A card of the region 'glossary' is drawn on the page, and the check reads no card of it.");
    }
}
