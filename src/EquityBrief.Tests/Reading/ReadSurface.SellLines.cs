using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Report;
using EquityBrief.Core.Tiles;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 22.42 correction: each card's line under its sell price worded from its family's own exit rule on Home,
// Tonight and the read-only copy, and the three parts of the entry check's fixes the operator's read-back of 2026-11-05 found
// not done: an analysts' figure fetched before the newest report labelled on every page drawing it, no sentence saying no
// resistance band stands above, and the chart page's market value the stock page's and never the provider's.
// see: A card's line under its sell price is worded from its family's own exit rule
public partial class ReadSurface
{
    static readonly string FundamentalsCopyLine = "No price is set in advance: it sells when a new filing weakens its score, or at its time limit.";

    // One idea of a family buying at 100 with its give-up price at 90, as Home is handed it.
    static HomeIdea SellIdea(string family, decimal? target = 125m, bool rises = false, decimal? trail = null) =>
        new(family, "AAA", "Alpha Inc", "Why it was picked.", 100m, 90m, target, rises, null, trail);

    static string OneHomeCard(HomeIdea idea, bool copying) =>
        new SinglePageApp().HomeRegion(new MarkRenderer { Copying = copying }, Day() with { Ideas = [idea], IdeasCount = 1 }, string.Empty);

    // The families whose rule sets a give-up price that never moves, each on the rule its code states: the pullback's and
    // the drift's stop and target, the fundamentals-first family on the pullback's plan and the dip's stop and exit.
    public static TheoryData<string, decimal?> FixedStopFamilies => new()
    {
        { SetupFamilies.Pullback, 125m },
        { DriftRule.Name, 125m },
        { FundamentalsRule.Name, 125m },
        { DipRule.Name, null },
    };

    [Theory]
    [MemberData(nameof(FixedStopFamilies))]
    public void AHomeCardOfAFamilyWhoseGiveUpPriceNeverMovesSaysItStaysWhereItIs(string family, decimal? target)
    {
        foreach (var copying in new[] { false, true })
        {
            var page = OneHomeCard(SellIdea(family, target), copying);

            Assert.Contains("<div class=\"home-odds\" data-sell-line>The give-up price stays where it is.</div>", page, StringComparison.Ordinal);
            Assert.DoesNotContain("rises as the stock climbs", page, StringComparison.Ordinal);
            Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(page));
        }
    }

    // On Home the breakout's one line replaces "Breakouts earn most" alone: the record line and the no-loss line stay.
    [Fact]
    public void AHomeBreakoutSaysItsGiveUpPriceRisesAndKeepsItsRecordAndNoLossLines()
    {
        var idea = SellIdea(BreakoutRule.Name, null, rises: true, trail: 4m) with { Stop = 96m, Record = new EquityBrief.Core.Cards.PaperWinners(3, 300, null) };

        foreach (var copying in new[] { false, true })
        {
            var page = OneHomeCard(idea, copying);

            Assert.Contains($"<div class=\"home-odds\" data-sell-line>{SinglePageApp.RisesLine}</div>", page, StringComparison.Ordinal);
            Assert.Contains("The trade can no longer lose once the stock reaches about $104.00.", page, StringComparison.Ordinal);
            Assert.Contains("The breakout rule has 3 of the 300 finished trades its record needs.", page, StringComparison.Ordinal);
            Assert.DoesNotContain("Breakouts earn most", page, StringComparison.Ordinal);
            Assert.DoesNotContain(MarkRenderer.StaysWords, page, StringComparison.Ordinal);
            Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(page));
        }
    }

    // A plan its rule sells on a rising give-up price whose distance is not read says so all the same, and however a target
    // came to be handed to it draws no bar against one.
    [Fact]
    public void AHomeCardOfARisingRuleSaysSoWithNoDistanceReadAndDrawsNoBar()
    {
        var page = OneHomeCard(SellIdea(BreakoutRule.Name, 125m, rises: true), copying: false);

        Assert.Contains($"<div class=\"home-odds\" data-sell-line>{SinglePageApp.RisesLine}</div>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("home-rr", page, StringComparison.Ordinal);
        Assert.Contains("class=\"home-rr\"", OneHomeCard(SellIdea(SetupFamilies.Pullback), copying: false), StringComparison.Ordinal);
    }

    // Home reads each index's own Fundamentals rule off its night: the S&P 400's here, held at 6 of 9 for at most 252 trading days.
    [Fact]
    public async Task HomesFundamentalsCardReadsTheSettingItsIndexsRuleStandsAt()
    {
        using var store = UniversesStore();

        ImprovingNightOn(store, setting: new ImprovingSweepSetting(8, false, true, true, 6, 252));

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));
        var card = Regex.Match(page, "<article class=\"home-idea fam-edge\" data-family=\"improving\" data-ticker=\"M1\">.*?</article>", RegexOptions.Singleline).Value;

        Assert.Contains("it sells when a new filing drops its score below 6 in every 9 of the checks that apply, or after 252 trading days.", card, StringComparison.Ordinal);
        Assert.Contains("its filings weaken", card, StringComparison.Ordinal);
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(page));
    }

    // The tiles the page's script draws again at a newer quote carry the label as the page's own do.
    [Fact]
    public async Task TheLiveTilesLabelAConsensusFetchedBeforeTheNewestReport()
    {
        using var store = UniversesStore();

        store.Execute("INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, eps_average, eps_year_ago, eps_analysts) VALUES ('S1', '2026-09-01T22:00:00Z', '0y', '2026-12-31', '7.00', '6.00', 10);");
        store.Execute("INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('S1', '2026-10-01', 'earnings', 'before', '{}', '2026-10-01T22:00:00Z');");
        store.Execute(
            "INSERT INTO live_quote (ticker, asked_at, answered_at, quoted_at, price, previous_close, change, change_pct, night, distances) VALUES " +
            "('S1', '2026-10-07T14:55:00Z', '2026-10-07T14:55:04Z', '2026-10-07T14:43:00Z', '50', '50', '0', 0, '2026-10-02', '[]');");

        using var session = new PassHost(store.Root) { Clock = EquityBrief.Core.Time.FixedClock.At(InTheSession, EquityBrief.Core.Time.SessionZones.UnitedStates) };
        using var live = session.CreateClient();
        using var reply = System.Text.Json.JsonDocument.Parse(await live.GetStringAsync(SinglePageApp.QuoteRoute + "S1"));

        Assert.Contains("Set before the 1 Oct report", WebUtility.HtmlDecode(reply.RootElement.GetProperty("tiles").GetString() ?? string.Empty), StringComparison.Ordinal);
    }

    // OGS's card: the Fundamentals family sets no price and sells on its filings or its time limit, at the setting its index's
    // rule stands at, whose figures the read-only copy leaves off.
    [Fact]
    public void AHomeFundamentalsCardSaysItSellsOnItsFilingsAndNamesNoPriceToFallTo()
    {
        var idea = new HomeIdea(ImprovingRule.Name, "OGS", "ONE Gas", "Its newest filing shows the business getting stronger.", 80m, null, null, false, null, Setting: new ImprovingSweepSetting(9, true, true, true, 6, 252));
        var page = Regex.Match(OneHomeCard(idea, copying: false), "<article.*?</article>", RegexOptions.Singleline).Value;

        Assert.Contains("<div><span>Sell when</span><b class=\"stop\">its filings weaken</b></div>", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"home-odds\" data-sell-line>No price is set in advance: it sells when a new filing drops its score below 6 in every 9 of the checks that apply, or after 252 trading days.</div>", page, StringComparison.Ordinal);
        Assert.Contains("<b class=\"aim\">No fixed target</b>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Sell if it falls to", page, StringComparison.Ordinal);
        Assert.DoesNotContain("200-day", page, StringComparison.Ordinal);
        Assert.DoesNotContain("give-up price", page, StringComparison.Ordinal);
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(page));

        // A rule an approval sold under its 200-day average says so among its exits, and a card handed no setting reads the
        // family's starting one.
        Assert.Contains(
            "below 6 in every 9 of the checks that apply, on a close under its 200-day average, or after 252 trading days.",
            OneHomeCard(idea with { Setting = idea.Setting! with { SoldUnderAverage = true } }, copying: false),
            StringComparison.Ordinal);
        Assert.Contains(
            "below 7 in every 9 of the checks that apply, or after 126 trading days.",
            OneHomeCard(idea with { Setting = null }, copying: false),
            StringComparison.Ordinal);

        var copied = OneHomeCard(idea, copying: true);

        Assert.Contains($"<div class=\"home-odds\" data-sell-line>{FundamentalsCopyLine}</div>", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("252", copied, StringComparison.Ordinal);
        Assert.Contains("its filings weaken", copied, StringComparison.Ordinal);
    }

    static DecisionCardView SellCard(string family, decimal? stop = 46m, decimal? target = 58m, decimal? trail = null, string? approved = null) =>
        new("GSPC", new DateOnly(2026, 10, 30), family, "AAA", "the live rule", 50m, stop, target, [], null, Cap: 21, Trail: trail, Approved: approved);

    [Theory]
    [MemberData(nameof(FixedStopFamilies))]
    public void APickCardOfAFamilyWhoseGiveUpPriceNeverMovesSaysItStaysOnTonightAndTheCopy(string family, decimal? target)
    {
        var card = new MarkRenderer().DecisionCard(SellCard(family, target: target is null ? null : 58m));

        Assert.Contains(">Sell if it closes below</span><b class=\"loss\">46.00</b><span class=\"tile-under\">The give-up price stays where it is. Sold at the next day's open.</span>", card, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.RisingWords, card, StringComparison.Ordinal);
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(card));

        var copy = new MarkRenderer { Copying = true }.DecisionCard(SellCard(family, target: target is null ? null : 58m));

        Assert.Contains("<span class=\"tile-under\">The give-up price stays where it is.</span>", copy, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.RisingWords, copy, StringComparison.Ordinal);
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(copy));
    }

    // Tonight keeps its words of a rising give-up price for the breakout alone: another family a change on the Loop page gave
    // a trail says how it rises without them or the breakout's reason, and an exit that moves the stop draws no line that it stays.
    [Fact]
    public void TonightKeepsItsRisingWordsForTheBreakoutAlone()
    {
        var breakout = new MarkRenderer().DecisionCard(SellCard(BreakoutRule.Name, target: null, trail: 4m));

        Assert.Contains(MarkRenderer.RisingWords, breakout, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.RunsWords, breakout, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.StaysWords, breakout, StringComparison.Ordinal);
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(breakout));

        var trailed = new MarkRenderer().DecisionCard(SellCard(SetupFamilies.Pullback, target: null, trail: 4m));

        Assert.DoesNotContain(MarkRenderer.RisingWords, trailed, StringComparison.Ordinal);
        Assert.DoesNotContain("Breakouts earn most", trailed, StringComparison.Ordinal);
        Assert.Contains("raised after each higher close, never lowered, and sold at the next day's open", trailed, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.StaysWords, trailed, StringComparison.Ordinal);

        var raised = ExitMenu.Swing.First(choice => choice.Kind == ExitKind.BreakEven).Words;
        var changed = new MarkRenderer().DecisionCard(SellCard(SetupFamilies.Pullback, approved: raised));

        Assert.Contains("<span class=\"tile-under\">sold at the next day's open</span>", changed, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.StaysWords, changed, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.StaysWords, new MarkRenderer { Copying = true }.DecisionCard(SellCard(SetupFamilies.Pullback, approved: raised)), StringComparison.Ordinal);
        Assert.DoesNotContain("data-sell-line", OneHomeCard(SellIdea(SetupFamilies.Pullback) with { StopMoves = true }, copying: false), StringComparison.Ordinal);

        // A change that moves no stop leaves the line standing.
        var timed = ExitMenu.Swing.First(choice => choice.Kind == ExitKind.TimeStop).Words;

        Assert.False(MarkRenderer.ApprovedExitMovesTheStop(timed));
        Assert.False(MarkRenderer.ApprovedExitMovesTheStop(null));
        Assert.Contains(MarkRenderer.StaysWords, new MarkRenderer().DecisionCard(SellCard(SetupFamilies.Pullback, approved: timed)), StringComparison.Ordinal);

        foreach (var kind in new[] { ExitKind.BreakEven, ExitKind.HalfBreakEven, ExitKind.Trail, ExitKind.HalfTarget })
        {
            Assert.True(MarkRenderer.ApprovedExitMovesTheStop(ExitMenu.Swing.First(choice => choice.Kind == kind).Words), $"{kind} moves the stop.");
        }

        // The sector heavyweights set no price, and their tile says so as it did.
        var book = new MarkRenderer().DecisionCard(SellCard(HeavyweightRule.Name, stop: null, target: null));

        Assert.Contains("nothing sells it at a price set in advance", book, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.StaysWords, book, StringComparison.Ordinal);
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(book));
    }

    // The check fails PTEN's and OGS's cards as they read on 2026-11-05, and each other way a line can contradict its rule,
    // wherever the card stands, on Home or as a pick's card, and passes the line each family's rule gives.
    [Fact]
    public void ACardWhoseLineContradictsItsFamilysExitRuleIsFound()
    {
        static string Home(string family, string ticker, string lines) => $"<article class=\"home-idea fam-edge\" data-family=\"{family}\" data-ticker=\"{ticker}\">{lines}</article>";
        static string Pick(string family, string ticker, string lines) => $"<div class=\"decision-card plain\" data-index=\"GSPC\" data-family=\"{family}\" data-ticker=\"{ticker}\" data-night=\"2026-11-05\">{lines}</div>";

        Assert.Equal(
            [$"dip PTEN: {SinglePageApp.RisesLine[..43]}"],
            MarkRenderer.SellLinesContradictingTheirRule(Home(DipRule.Name, "PTEN", $"<div class=\"home-odds\">{SinglePageApp.RisesLine}</div>")));
        Assert.Equal(
            [$"improving OGS: {SinglePageApp.RisesLine[..43]}", "improving OGS: Sell if it falls to"],
            MarkRenderer.SellLinesContradictingTheirRule(Home(ImprovingRule.Name, "OGS", $"<span>Sell if it falls to</span><div class=\"home-odds\">{SinglePageApp.RisesLine}</div>")));
        Assert.Equal(
            [$"pullback AAA: {MarkRenderer.RisingWords[..35]}"],
            MarkRenderer.SellLinesContradictingTheirRule(Pick(SetupFamilies.Pullback, "AAA", $"<span>{MarkRenderer.RisingWords}</span>")));
        Assert.Equal(
            [$"breakout BBB: {MarkRenderer.StaysWords}"],
            MarkRenderer.SellLinesContradictingTheirRule(Pick(BreakoutRule.Name, "BBB", $"<span>{MarkRenderer.StaysWords}</span>")));
        Assert.Equal(
            [$"heavyweight CCC: {MarkRenderer.StaysWords}"],
            MarkRenderer.SellLinesContradictingTheirRule(Pick(HeavyweightRule.Name, "CCC", $"<span>{MarkRenderer.StaysWords}</span>")));

        // Each card is read alone, so a line on one card says nothing of the next.
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule(
            Home(BreakoutRule.Name, "BBB", $"<div>{SinglePageApp.RisesLine}</div>") + Home(DipRule.Name, "PTEN", $"<div>{MarkRenderer.StaysWords}</div>")));
        Assert.Empty(MarkRenderer.SellLinesContradictingTheirRule($"<p>{SinglePageApp.RisesLine}</p>"));
    }

    // SWK on 2026-11-05: a fetch of 14 Oct and a report of 4 Nov, its filing not yet stored. A figure fetched on the report's day
    // or after it says nothing.
    [Fact]
    public void AnAnalystsFigureFetchedBeforeTheNewestReportIsLabelledOnTheBriefAndTheTile()
    {
        var report = new DateOnly(2026, 11, 4);

        Assert.Equal("Set before the 4 Nov report", MarkRenderer.SetBeforeOf(new DateOnly(2026, 10, 14), report));
        Assert.Null(MarkRenderer.SetBeforeOf(report, report));
        Assert.Null(MarkRenderer.SetBeforeOf(new DateOnly(2026, 11, 5), report));
        Assert.Null(MarkRenderer.SetBeforeOf(new DateOnly(2026, 10, 14), null));
        Assert.Null(MarkRenderer.SetBeforeOf(null, report));

        // The brief's line, its fetch of 20 Oct against a later report and an earlier one.
        static string Line(DateOnly? newest) =>
            BriefScreen.Of(BriefOf(BriefMember()) with { NewestReport = newest }).Panels.SelectMany(panel => panel.Lines).Single(line => line.Key == "analysts").Words;

        Assert.EndsWith(" Set before the 4 Nov report.", Line(report), StringComparison.Ordinal);
        Assert.DoesNotContain("Set before", Line(new DateOnly(2026, 10, 19)), StringComparison.Ordinal);
        Assert.DoesNotContain("Set before", Line(null), StringComparison.Ordinal);

        // The stock page's tile of the analysts' consensus.
        static string Tiles(DateOnly? newest) => new MarkRenderer().LeadTiles(new TilesView(null, new GrowthTile(GrowthTile.Consensus, 19.9, new DateOnly(2026, 12, 31), 14, new DateOnly(2026, 10, 14)), null, null, null, false, false, newest));

        Assert.Contains("as fetched on 2026-10-14. Set before the 4 Nov report", WebUtility.HtmlDecode(Tiles(report)), StringComparison.Ordinal);
        Assert.DoesNotContain("Set before", Tiles(new DateOnly(2026, 10, 14)), StringComparison.Ordinal);
    }

    // Read off the pages the fixture's AAPL draws: a report on the calendar the night before, its quarter not stored, after the
    // fetch that kept its analysts' figures; and once a fetch comes on the report's day neither page says so.
    [Fact]
    public async Task TheStockAndChartPagesLabelTheAnalystsFiguresFetchedBeforeAReportWhoseQuarterIsNotStored()
    {
        var (store, code, _, _) = await FixtureReplay.NightAsync();

        using (store)
        {
            Assert.Equal(0, code);

            var night = Assert.Single(Rows(store, "SELECT MAX(session_date) FROM bar WHERE ticker = 'AAPL';"))[0];
            var reported = Day(night).AddDays(-1);
            var reportedOn = reported.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var fetchedOn = reported.AddDays(-21).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var periodEnd = Day(night).AddMonths(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var label = "Set before the " + reported.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture) + " report";

            store.Execute($"DELETE FROM estimate_trend WHERE ticker = 'AAPL';");
            store.Execute(
                "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, eps_average, eps_year_ago, eps_analysts, eps_now, eps_thirty_days_ago) VALUES " +
                $"('AAPL', '{fetchedOn}T22:00:00Z', '0y', '{periodEnd}', '7.00', '6.00', 10, '7.00', '6.50'), " +
                $"('AAPL', '{fetchedOn}T22:00:00Z', '0q', '{periodEnd}', '1.80', '1.50', 10, '1.80', '1.70');");
            store.Execute($"UPDATE company SET target_price = '200' WHERE ticker = 'AAPL';");
            store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('AAPL', '{reportedOn}', 'earnings', 'AfterMarket', '{{}}', '{reportedOn}T22:00:00Z');");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var chart = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/AAPL"));
            var name = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAPL"));

            // The chart page: the analysts' section, the next quarter's estimate on the earnings graph and the brief's line.
            Assert.Contains(label, Regex.Match(chart, "<section class=\"chart-graph\" data-graph=\"analysts\".*?</section>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
            Assert.Contains(label, Regex.Match(chart, "<section class=\"chart-graph\" data-graph=\"earnings\".*?</section>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
            Assert.Contains(label, BriefOfPage(chart).Lines["analysts"], StringComparison.Ordinal);

            // The stock page: its tile of the analysts' consensus, and the analysts' card with its consensus and mean target.
            Assert.Contains(label, Regex.Match(name, "<div class=\"lead-tile\" data-tile=\"growth\">.*?</div>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
            Assert.Contains(label, Regex.Match(name, "id=\"analysts\".*?class=\"consensus-table\"", RegexOptions.Singleline).Value, StringComparison.Ordinal);

            store.Execute($"UPDATE estimate_trend SET fetched_at = '{reportedOn}T22:00:00Z' WHERE ticker = 'AAPL';");

            Assert.DoesNotContain(label, WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAPL")), StringComparison.Ordinal);
        }
    }

    // SWK's stock page under "What would turn it up", and the ladder's figure beside it: bands are read over the stored year,
    // so each says how far it looked. No shipped source keeps the old sentence.
    [Fact]
    public void NoSentenceSaysNoResistanceBandStandsAboveAndEachSaysTheLastYearOfBars()
    {
        var lines = NameScreen.RiskLinesAt([new LadderBand(100m, 102m, "support", 1.2, 2, new DateOnly(2026, 9, 1), null)], 118.33m, invalidation: null);

        Assert.Equal("No resistance level in the last year of bars, so no close above one is read.", lines.Up);
        Assert.Contains(
            "data-side=\"above\">No resistance level in the last year of bars, so the plan has nothing above it to sell into.</text>",
            new MarkRenderer().LadderFigure("SWK", [new LadderBand(100m, 102m, "support", 1.2, 2, new DateOnly(2026, 9, 1), "tranche 1 buys here")], 118.33m, live: false),
            StringComparison.Ordinal);

        var old = new Regex("No (resistance )?band stands above");
        var shipped = Repository.SourceFiles()
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();

        Assert.True(shipped.Length > 100, $"Read {shipped.Length} shipped source files, expected more than 100.");
        Assert.DoesNotContain(shipped, file => old.IsMatch(File.ReadAllText(file)));
        Assert.Matches(old, "No resistance band stands above the price, so no close above one is read.");
        Assert.Matches(old, "No band stands above the price, so the plan has nothing above it to sell into.");
    }

    // SWK's chart page drew the provider's $13.8B where its stock page drew $18.0B. Both read the stored close times the filed
    // shares, with the shares' date, and a stock holding no such value draws none on the chart page rather than the provider's.
    [Fact]
    public async Task TheChartPagesMarketValueIsTheStockPagesWithTheSharesDateAndNeverTheProviders()
    {
        var (store, code, _, _) = await FixtureReplay.NightAsync();

        using (store)
        {
            Assert.Equal(0, code);

            var night = Assert.Single(Rows(store, "SELECT MAX(session_date) FROM member_reading WHERE ticker = 'AAPL';"))[0];

            store.Execute($"UPDATE member_reading SET company_value = '18000000000' WHERE ticker = 'AAPL' AND session_date = '{night}';");
            store.Execute("UPDATE reported_quarter SET shares = '100000000', filing_date = '2026-01-15' WHERE ticker = 'AAPL' AND shares IS NOT NULL;");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var chart = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/AAPL"));
            var name = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAPL"));

            Assert.Contains("<dt>Market value</dt>", chart, StringComparison.Ordinal);
            Assert.Contains("$18.0B, the shares filed on 2026-01-15 times the close", Regex.Replace(TextOf(chart), "\\s+", " "), StringComparison.Ordinal);
            Assert.DoesNotContain("Market value, the provider's", chart, StringComparison.Ordinal);
            Assert.Contains("$18.0B", Regex.Match(name, "<dt>Market value</dt>.*?</dd>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
            Assert.Contains("the shares filed on 2026-01-15", Regex.Match(name, "<dt>Market value</dt>.*?</dd>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
            Assert.Contains("the shares filed on 2026-01-15", Regex.Match(name, "<tr data-reading=\"company-value\">.*?</tr>", RegexOptions.Singleline).Value, StringComparison.Ordinal);

            store.Execute("UPDATE member_reading SET company_value = NULL WHERE ticker = 'AAPL';");

            var none = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/AAPL"));

            Assert.Contains($"<dt>Market value</dt><dd class=\"degraded\">{SinglePageApp.NotStored}</dd>", none, StringComparison.Ordinal);
        }
    }
}
