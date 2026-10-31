using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quotes;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.13: the market strip. The night asks the provider for QQQ's, DIA's and the dollar index's
// closes and Yahoo's chart for WTI crude's, one request a series, stating what it asks before its first request; the
// Treasury's 3-month is read beside its 10-year from the same answer; Yahoo's spark is read as sent from a capture; and the
// strip's tiles are worked by hand from constructed closes, in the session from Yahoo's quotes and outside it from the
// night's store, each change and arrow on both sides of nought, the VIX's words on both sides of its median, and a tile
// Yahoo answers nothing for drawing the close with words saying so.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public partial class FixtureExpectations
{
    // The shipped sources 21.13 writes or edits, its plain words and its strip alike.
    static readonly string[] PlainWordsAndStripSources =
    [
        "src/EquityBrief.Api/Cloudflare/SiteCopyCommand.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/MarketStripScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Strip.cs",
        "src/EquityBrief.Api/Reading/StripQuotes.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Quality.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Core/Providers/EodhdMarketSeriesFeed.cs",
        "src/EquityBrief.Core/Providers/ITreasuryYieldFeed.cs",
        "src/EquityBrief.Core/Providers/YahooMarketSeriesFeed.cs",
        "src/EquityBrief.Core/Providers/YahooNasdaqBarFeed.cs",
        "src/EquityBrief.Core/Providers/YahooSparkFeed.cs",
        "src/EquityBrief.Core/Quotes/MarketStrip.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Paper.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
        "src/EquityBrief.Web/Cloudflare/SiteCopy.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.BehindIt.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Card.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Families.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.LedgerWords.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.LoopSummary.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.LoopWords.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Paper.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.PaperWords.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Quality.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Strip.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Tonight.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.TonightTop.cs",
        "src/EquityBrief.Web/Marks/ReaderText.cs",
        "src/EquityBrief.Web/Marks/ReaderWords.cs",
        "src/EquityBrief.Worker/Bars/MarketSeriesFetcher.cs",
        "src/EquityBrief.Worker/Cards/DecisionCards.cs",
        "src/EquityBrief.Worker/NightFeeds.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Worker/Treasury/TreasuryReader.cs",
    ];

    [Fact]
    public void NoSourceARulesVersionPinsIsOne21Point13Writes()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(PlainWordsAndStripSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(PlainWordsAndStripSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Core/Families/FamilyRule.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    static string SparkCapture() =>
        File.ReadAllText(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05", "strip-yahoo-spark.json"));

    [Fact]
    public async Task TheNightAsksTheStripsFourSeriesOneRequestEachStatingItsCallsBeforeTheFirst()
    {
        using var store = BreakoutStore();

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 35, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var night = new DateOnly(2026, 10, 2);
        Dictionary<DateOnly, decimal> Two(decimal before, decimal on) => new() { [night.AddDays(-1)] = before, [night] = on };
        var provider = new ConstructedMarket(new() { [MarketStrip.Nasdaq] = Two(700m, 690m), [MarketStrip.Dow] = Two(520m, 522m), [MarketStrip.Dollar] = Two(101m, 102m) }, []);
        var oil = new ConstructedMarket(new() { [MarketStrip.Oil] = Two(102m, 103m) }, []);
        var said = new List<(string Line, int Requests)>();

        var outcome = await new MarketSeriesFetcher(provider, clock, store.DatabaseFile, oil, line => said.Add((line, provider.Requests + oil.Requests))).RunAsync("night-strip");

        // Stated once, before any request: the provider's twenty series at one weighted call each, and Yahoo's one, free.
        Assert.Equal([("asks 20 series of the provider at one weighted call each, 20 weighted call(s) in all, and 1 of Yahoo's chart, free", 0)], said);

        // Each of the four asked once over the window, the three the provider's after its own seventeen, crude Yahoo's.
        Assert.Equal([MarketStrip.Nasdaq, MarketStrip.Dow, MarketStrip.Dollar], MarketSeriesFetcher.Series.TakeLast(3));
        Assert.Equal(["QQQ 2025-08-28 2026-10-02", "DIA 2025-08-28 2026-10-02", "DXY 2025-08-28 2026-10-02"], provider.Asked.TakeLast(3));
        Assert.Equal(["WTI 2025-08-28 2026-10-02"], oil.Asked);
        Assert.Equal(21, outcome.Requests);

        // Their sessions stored apart from the members' bars under the night's run, crude among them.
        Assert.Equal(
            ["DIA|2026-10-01|520", "DIA|2026-10-02|522", "DXY|2026-10-01|101", "DXY|2026-10-02|102", "QQQ|2026-10-01|700", "QQQ|2026-10-02|690", "WTI|2026-10-01|102", "WTI|2026-10-02|103"],
            Query(store, "SELECT series || '|' || session_date || '|' || close FROM market_bar WHERE series IN ('QQQ', 'DIA', 'DXY', 'WTI') ORDER BY series, session_date;"));

        // The dollar index is filed under the provider's index exchange, the two funds under a listing's; QQQ and DIA are
        // funds whose closes the provider restates for a dividend, the dollar index and crude not.
        Assert.Equal(EodhdMarketSeriesFeed.IndexSuffix, EodhdMarketSeriesFeed.SuffixOf(MarketStrip.Dollar));
        Assert.Equal(EodhdMarketSeriesFeed.ListingSuffix, EodhdMarketSeriesFeed.SuffixOf(MarketStrip.Nasdaq));
        Assert.True(MarketSeriesFetcher.IsFund(MarketStrip.Dow));
        Assert.False(MarketSeriesFetcher.IsFund(MarketStrip.Dollar));
        Assert.Equal("CL=F", YahooMarketSeriesFeed.SymbolOf(MarketStrip.Oil));
    }

    [Fact]
    public void TheTreasurys3MonthIsReadBesideIts10YearFromTheSameAnswer()
    {
        var yields = TreasuryYields.Parse(File.ReadAllText(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05", "treasury-2026.csv")));
        var newest = yields.Single(yield => yield.Session == new DateOnly(2026, 10, 9));

        Assert.Equal((5.24, 4.25), (newest.TenYear, newest.ThreeMonth!.Value));

        // A table holding no 3-month column reads none for it and its 10-year as before.
        Assert.Null(Assert.Single(TreasuryYields.Parse("Date,\"10 Yr\"\n10/09/2026,5.24\n")).ThreeMonth);
    }

    [Fact]
    public void YahoosSparkIsReadAsSentAndAnAnswerHoldingNoResultIsRefused()
    {
        var quotes = YahooSpark.Parse(SparkCapture());

        Assert.Equal(MarketStrip.YahooSymbols.Order(StringComparer.Ordinal), quotes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new SparkQuote("^GSPC", 7580.07m, 7720.39m, DateTimeOffset.FromUnixTimeSeconds(1793390400)), quotes["^GSPC"]);
        Assert.Equal((102.95m, 102.0m), (quotes["CL=F"].Price, quotes["CL=F"].PreviousClose));

        // A symbol whose meta holds no price is left out; an answer with no spark result is refused.
        Assert.Empty(YahooSpark.Parse("{\"spark\":{\"result\":[{\"symbol\":\"X\",\"response\":[{\"meta\":{\"chartPreviousClose\":1}}]}]}}"));
        Assert.Throws<FormatException>(() => YahooSpark.Parse("{\"finance\":{\"error\":\"not found\"}}"));
    }

    // The strip's store on the night of 30 Oct, every series' close on the night against the session before; the VIX's year
    // at 20 before its close; the Treasury's two newest sessions.
    static StripStored StripOn(decimal vix)
    {
        var night = new DateOnly(2026, 10, 30);
        StripClose Close(string series, decimal before, decimal on) => new(series, night, on, before);

        return new StripStored(
            new Dictionary<string, StripClose>(StringComparer.Ordinal)
            {
                [MarketCloses.Index] = Close(MarketCloses.Index, 7500m, 7575m),
                ["IJH"] = Close("IJH", 74m, 73.26m),
                ["IJR"] = Close("IJR", 140m, 140m),
                [MarketStrip.Nasdaq] = Close(MarketStrip.Nasdaq, 700m, 707m),
                [MarketStrip.Dow] = Close(MarketStrip.Dow, 520m, 520m),
                [MarketCloses.Vix] = Close(MarketCloses.Vix, 18m, vix),
                ["HYG"] = Close("HYG", 78m, 77.22m),
                [MarketStrip.Oil] = Close(MarketStrip.Oil, 100m, 102m),
                [MarketStrip.Dollar] = Close(MarketStrip.Dollar, 102m, 101.49m),
            },
            [.. Enumerable.Repeat(20m, 252)],
            new StripYields(night, 4.30, 4.20, 4.25, 4.22));
    }

    [Fact]
    public void TheStripsTilesAreWorkedByHandFromTheNightsClosesOutsideTheSession()
    {
        var night = new DateOnly(2026, 10, 30);
        var view = MarketStripScreen.View(StripOn(25m), new MarketStripScreen.Breadth("S&P 500", 0.523, 0.45), night, null, null, NewYork);
        var tile = view.Tiles.ToDictionary(one => one.Key, StringComparer.Ordinal);

        Assert.Equal(["index", "mid", "small", "nasdaq", "dow", MarketStrip.Breadth, MarketStrip.Vix, MarketStrip.TenYear, MarketStrip.ThreeMonth, "credit", "oil", "dollar"], view.Tiles.Select(one => one.Key));
        Assert.Equal("Prices as of the close of 30 Oct", view.Line);
        Assert.False(view.Quoted);

        // The index up a per cent from 7,500 to 7,575; the S&P 400's fund down a per cent; the S&P 600's flat; each drawn
        // at its close.
        Assert.Equal(("7,575.00", 1.0), (tile["index"].Level, Math.Round(tile["index"].Change!.Value, 6)));
        Assert.Equal(("73.26", -1.0), (tile["mid"].Level, Math.Round(tile["mid"].Change!.Value, 6)));
        Assert.Equal(0.0, tile["small"].Change);

        // The VIX at 25 over its median of 20 over the year before; the yields in points, the 10-year up 0.05 and the 3-month
        // down 0.02; the share above the 200-day with the floor the night read.
        Assert.Equal("above its 1-year median", tile[MarketStrip.Vix].Words);
        Assert.Equal(("4.30%", 0.05), (tile[MarketStrip.TenYear].Level, Math.Round(tile[MarketStrip.TenYear].Change!.Value, 6)));
        Assert.Equal(("4.20%", -0.02), (tile[MarketStrip.ThreeMonth].Level, Math.Round(tile[MarketStrip.ThreeMonth].Change!.Value, 6)));
        Assert.Equal(("52.3%", "of S&P 500 stocks above their 200-day average", "setups buy at 45% or more"), (tile[MarketStrip.Breadth].Level, tile[MarketStrip.Breadth].Words, tile[MarketStrip.Breadth].Beneath));
        Assert.All(view.Tiles, one => Assert.False(one.PriorClose));
        Assert.Equal("The S&P 500 rose 1.0% on the day; the VIX is above its 1-year median.", view.Summary);

        // At 15 the VIX is under its median, and on an S&P 400 page the share reads with each setup's own floor.
        var under = MarketStripScreen.View(StripOn(15m), new MarketStripScreen.Breadth("S&P 400", 0.41, null), night, null, null, NewYork);

        Assert.Equal("below its 1-year median", under.Tiles.Single(one => one.Key == MarketStrip.Vix).Words);
        Assert.Equal("each setup on this index reads its own floor", under.Tiles.Single(one => one.Key == MarketStrip.Breadth).Beneath);

        // Drawn, a rise is green with an arrow up, a fall red with an arrow down, a flat change neither.
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().MarketStrip(view));

        Assert.Contains("<div class=\"strip-tile\" data-tile=\"index\" data-change=\"1\" data-quoted=\"false\" data-prior=\"false\"><span class=\"st-label\">S&P 500</span><b class=\"st-level\">7,575.00</b><span class=\"st-change gain\"><span class=\"st-arrow\" aria-label=\"up\">▲</span> +1.00%</span></div>", drawn, StringComparison.Ordinal);
        Assert.Contains("<span class=\"st-change loss\"><span class=\"st-arrow\" aria-label=\"down\">▼</span> -1.00%</span>", drawn, StringComparison.Ordinal);
        Assert.Contains("<span class=\"st-change flat\">0.00%</span>", drawn, StringComparison.Ordinal);
        Assert.Contains("<span class=\"st-change gain\"><span class=\"st-arrow\" aria-label=\"up\">▲</span> +0.05</span>", drawn, StringComparison.Ordinal);
        Assert.StartsWith("<p class=\"card-summary\" data-summary>The S&P 500 rose 1.0% on the day; the VIX is above its 1-year median.</p><p class=\"strip-line\" data-quoted=\"false\">Prices as of the close of 30 Oct</p>", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void InTheSessionTheStripDrawsYahoosQuotesAndATileItDidNotAnswerDrawsTheCloseSayingSo()
    {
        var night = new DateOnly(2026, 10, 30);

        // Asked at 14:35 New York on the capture's quotes, HYG's left out as Yahoo answering nothing for it.
        var asked = new DateTimeOffset(2026, 10, 30, 18, 35, 0, TimeSpan.Zero);
        var quotes = YahooSpark.Parse(SparkCapture()).Where(pair => pair.Key != "HYG").ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var view = MarketStripScreen.View(StripOn(25m), new MarketStripScreen.Breadth("S&P 500", 0.523, 0.45), night, quotes, asked, NewYork);
        var tile = view.Tiles.ToDictionary(one => one.Key, StringComparer.Ordinal);

        Assert.Equal("Delayed prices, 2:35 PM ET", view.Line);
        Assert.True(view.Quoted);

        // The index at Yahoo's 7,580.07 against its 7,720.39 before, down 1.82 per cent; crude at 102.95 against 102.
        Assert.Equal(("7,580.07", -1.82), (tile["index"].Level, Math.Round(tile["index"].Change!.Value, 2)));
        Assert.True(tile["index"].Quoted);
        Assert.Equal(("102.95", 0.93), (tile["oil"].Level, Math.Round(tile["oil"].Change!.Value, 2)));

        // HYG, which Yahoo did not answer, draws the night's close and its change, saying it is the prior close only; the
        // yields, which Yahoo is not asked for, the same; the share above the 200-day says nothing of either.
        Assert.Equal(("77.22", -1.0, true, false), (tile["credit"].Level, Math.Round(tile["credit"].Change!.Value, 6), tile["credit"].PriorClose, tile["credit"].Quoted));
        Assert.True(tile[MarketStrip.TenYear].PriorClose);
        Assert.False(tile[MarketStrip.Breadth].PriorClose);
        Assert.Contains("<span class=\"st-beneath st-prior\">prior close only</span>", new MarkRenderer().MarketStrip(view), StringComparison.Ordinal);
        Assert.Equal("The S&P 500 fell 1.8% on the day; the VIX is below its 1-year median.", view.Summary);

        // A session Yahoo answered nothing in reads as the closes, every tile it is asked for saying so.
        var none = MarketStripScreen.View(StripOn(25m), new MarketStripScreen.Breadth("S&P 500", 0.523, 0.45), night, new Dictionary<string, SparkQuote>(), asked, NewYork);

        Assert.Equal("Prices as of the close of 30 Oct", none.Line);
        Assert.True(none.Tiles.Single(one => one.Key == "index").PriorClose);

        // The strip's card speaks in the reader's words and opens on its sentence.
        var card = EquityBrief.Web.App.Cards.Computed("Markets", new MarkRenderer().MarketStrip(view), title: "The markets", region: MarkRenderer.StripRegion);

        Assert.Empty(PlainWords.Faults(card, MarkRenderer.StripRegion, PlainWords.Terms()));
    }
}
