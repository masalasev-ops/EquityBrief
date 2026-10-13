using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.5: Universe. A tile a GICS sector counted by hand, the search on a ticker's opening letters and a
// company's name, each column's sort both ways with a row holding no value last, the rows a page, the near-level chips at
// one typical move and a hundredth past it with a close inside a band kept, the members one gate short worked from
// constructed rows, the readings' bars against the swing filter's ceiling, the phone's cards, and a correction's notice
// on both sides of its thirtieth day with the Run page's fold.
// see: Every page names a member's sector by the GICS sector its company fetch files
// see: A correction notice older than thirty days by the newest session it affects is folded onto the Run page
public partial class ReadSurface
{
    // The rows 19.5 adds that this check reaches: Universe's nine, the Run page's corrections and section 17's two.
    internal static readonly string[] UniverseRows =
    [
        .. new[] { "Sector tiles", "Search", "Sorting", "Rows a page", "Near a level", "One gate short", "The readings' bars", "On a phone", "Corrections" }
            .Select(row => CheckReach.Key("15.8 Universe", row)),
        CheckReach.Key("15.10 Run", "Corrections"),
        CheckReach.Key(Scope.LimitsTable, "Near a level"),
        CheckReach.Key(Scope.LimitsTable, "Correction notice age"),
    ];

    // The rows 19.5 retires: the sector strip's parts, which the tiles replace.
    internal static readonly string[] RetiredUniverseRows =
    [
        CheckReach.Key("15.8 Universe", "Sector strip, one line per sector"),
        CheckReach.Key("15.8 Universe", "Sector strip, how many are on tonight's list"),
        CheckReach.Key("15.8 Universe", "Sector strip, names"),
        CheckReach.Key("15.8 Universe", "Sector strip, how many are in an uptrend"),
    ];

    // A member as the universe reads it, with a close, its nearest support band and resistance band, and a typical move.
    static UniverseRow UniverseMember(string ticker, decimal close, decimal? supportLow = null, decimal? supportHigh = null, decimal? resistanceLow = null, decimal? resistanceHigh = null, double typical = 1.0, string? name = null, string sector = "Energy") =>
        new(ticker, sector, close, "uptrend", supportHigh, resistanceLow, typical, name, SupportFloor: supportLow, ResistanceCeiling: resistanceHigh);

    static UniverseScreen.Query Asking(string? search = null, string? near = null, bool oneShort = false, string? sort = null, bool descending = false) =>
        new(null, null, search, near, oneShort, sort, descending, null, null);

    [Fact]
    public void NearALevelKeepsACloseWithinOneTypicalMoveOfItsBandsEdgeAndOneInsideTheBand()
    {
        // On the support side: one typical move above the band's top edge, a hundredth past it, and a close inside a wide
        // band two moves under its top edge. On the resistance side the same beneath its bottom edge and inside its band.
        var cells = UniverseScreen.Rows(
        [
            UniverseMember("SAT", 101m, supportLow: 95m, supportHigh: 100m),
            UniverseMember("SPAST", 101.01m, supportLow: 95m, supportHigh: 100m),
            UniverseMember("SIN", 98m, supportLow: 90m, supportHigh: 100m),
            UniverseMember("RAT", 99m, resistanceLow: 100m, resistanceHigh: 105m),
            UniverseMember("RPAST", 98.99m, resistanceLow: 100m, resistanceHigh: 105m),
            UniverseMember("RIN", 103m, resistanceLow: 100m, resistanceHigh: 110m, typical: 0.5),
            UniverseMember("NONE", 50m),
        ]);

        Assert.Equal(2.0, cells.Single(cell => cell.Ticker == "SIN").ToSupport);
        Assert.Equal(6.0, cells.Single(cell => cell.Ticker == "RIN").ToResistance);
        Assert.Equal(["SAT", "SIN"], UniverseScreen.Kept(cells, Asking(near: UniverseScreen.NearSupport)).Select(cell => cell.Ticker).Order(StringComparer.Ordinal));
        Assert.Equal(["RAT", "RIN"], UniverseScreen.Kept(cells, Asking(near: UniverseScreen.NearResistance)).Select(cell => cell.Ticker).Order(StringComparer.Ordinal));
        Assert.Equal(cells.Count, UniverseScreen.Kept(cells, Asking()).Count);
        Assert.Equal(1.0, UniverseScreen.NearLevel);
    }

    [Fact]
    public void SearchKeepsATickersOpeningLettersOrAnyPartOfItsCompanysName()
    {
        // A ticker opening on the letters, a company's name holding them anywhere and in another case, and a ticker holding
        // them only in its middle, whose company's name holds none.
        var cells = UniverseScreen.Rows(
        [
            UniverseMember("ABCD", 10m, name: "Zeta Corp"),
            UniverseMember("XABC", 10m, name: "Alpha Inc"),
            UniverseMember("QQQQ", 10m, name: "Beta ABC Holdings"),
        ]);

        Assert.Equal(["ABCD", "QQQQ"], UniverseScreen.Kept(cells, Asking(search: "ab")).Select(cell => cell.Ticker).Order(StringComparer.Ordinal));
        Assert.Equal(["XABC"], UniverseScreen.Kept(cells, Asking(search: "alpha")).Select(cell => cell.Ticker));
        Assert.Empty(UniverseScreen.Kept(cells, Asking(search: "bc inc")));
        Assert.Equal(3, UniverseScreen.Kept(cells, Asking(search: "  ")).Count);
    }

    [Fact]
    public void EachColumnSortsBothWaysWithARowHoldingNoValueLastWhicheverWay()
    {
        UniverseCell Cell(string ticker, double? strength, decimal? close) =>
            new(ticker, "Energy", close, "uptrend", null, null, null, null, null, Strength: strength);

        UniverseCell[] rows = [Cell("BBB", 0.5, 20m), Cell("AAA", 0.9, null), Cell("CCC", null, 30m), Cell("DDD", 0.1, 10m), Cell("EEE", 0.5, 40m)];

        // A tie by the ticker, and the row holding no value last either way.
        Assert.Equal(["DDD", "BBB", "EEE", "AAA", "CCC"], UniverseScreen.Sorted(rows, "strength", false).Select(cell => cell.Ticker));
        Assert.Equal(["AAA", "BBB", "EEE", "DDD", "CCC"], UniverseScreen.Sorted(rows, "strength", true).Select(cell => cell.Ticker));
        Assert.Equal(["DDD", "BBB", "CCC", "EEE", "AAA"], UniverseScreen.Sorted(rows, "close", false).Select(cell => cell.Ticker));
        Assert.Equal(["EEE", "CCC", "BBB", "DDD", "AAA"], UniverseScreen.Sorted(rows, "close", true).Select(cell => cell.Ticker));

        // No sort, or one no column carries, keeps the projection's order.
        Assert.Equal(rows.Select(cell => cell.Ticker), UniverseScreen.Sorted(rows, null, false).Select(cell => cell.Ticker));
        Assert.Equal(rows.Select(cell => cell.Ticker), UniverseScreen.Sorted(rows, "weather", true).Select(cell => cell.Ticker));

        // Each heading but the strip's carries its link, the sorted column's turning it the other way and keeping the other keys.
        var state = new UniverseState(Search: "ab", Sort: "strength");
        var table = new MarkRenderer().UniverseTable(rows, new UniverseSorting(MarkRenderer.SortHref("uptrend", null, "400", state), state.Sort, state.Descending, null));
        var links = Regex.Matches(table, "<a class=\"sort\" data-sort=\"([^\"]+)\" aria-pressed=\"(true|false)\" href=\"([^\"]+)\"").Select(match => (Key: match.Groups[1].Value, Lit: match.Groups[2].Value, Href: WebUtility.HtmlDecode(match.Groups[3].Value))).ToArray();

        Assert.Equal(UniverseScreen.SortKeys, links.Select(link => link.Key));
        Assert.Equal("true", links.Single(link => link.Key == "strength").Lit);
        Assert.Equal("#/universe?page=1&trend=uptrend&universe=400&q=ab&sort=strength&dir=desc", links.Single(link => link.Key == "strength").Href);
        Assert.Equal("#/universe?page=1&trend=uptrend&universe=400&q=ab&sort=close", links.Single(link => link.Key == "close").Href);
        Assert.All(links.Where(link => link.Key != "strength"), link => Assert.Equal("false", link.Lit));
    }

    [Fact]
    public void RowsAPageOfferFiftyAHundredAndEveryRowAndTheChipsKeepTheOtherKeys()
    {
        IReadOnlyList<UniverseCell> kept = [.. Enumerable.Range(0, 120).Select(at => new UniverseCell(string.Create(CultureInfo.InvariantCulture, $"T{at:000}"), "Energy", 1m, null, null, null, null, null, null))];

        Assert.Equal((50, 1, 120), Shape(UniverseScreen.Paged(kept, null, null)));
        Assert.Equal((100, 1, 120), Shape(UniverseScreen.Paged(kept, "100", null)));
        Assert.Equal((20, 2, 120), Shape(UniverseScreen.Paged(kept, "100", 2)));
        Assert.Equal((120, 1, 120), Shape(UniverseScreen.Paged(kept, UniverseScreen.AllRows, 3)));
        Assert.Equal((20, 3, 120), Shape(UniverseScreen.Paged(kept, null, 9)));
        Assert.Equal((50, 1, 120), Shape(UniverseScreen.Paged(kept, "75", null)));
        Assert.Equal("T100", UniverseScreen.Paged(kept, null, 3).Page[0].Ticker);

        static (int Rows, int At, int Of) Shape(UniverseScreen.Shown shown) => (shown.Page.Count, shown.At, shown.Rows);

        // Each chip keeps every other key: the rows chips the search, the near chip and the sort, and the near chips the rows.
        var state = new UniverseState(Search: "ab", Near: UniverseScreen.NearSupport, Sort: "close", Descending: true, Rows: "100");
        var controls = WebUtility.HtmlDecode(new MarkRenderer().UniverseControls(state, null, "Energy", null, true, UniverseScreen.PageSizes, UniverseScreen.AllRows));

        string Href(string filter, string value) =>
            Regex.Match(controls, $"data-filter=\"{filter}\" data-value=\"{Regex.Escape(value)}\" aria-pressed=\"(?:true|false)\" href=\"([^\"]+)\"").Groups[1].Value;

        Assert.Equal("#/universe?page=1&sector=Energy&q=ab&near=support&sort=close&dir=desc&rows=all", Href("rows", "all"));
        Assert.Equal("#/universe?page=1&sector=Energy&q=ab&near=support&sort=close&dir=desc", Href("rows", "50"));
        Assert.Equal("#/universe?page=1&sector=Energy&q=ab&sort=close&dir=desc&rows=100", Href("near", "any"));
        Assert.Equal("#/universe?page=1&sector=Energy&q=ab&near=resistance&sort=close&dir=desc&rows=100", Href("near", "resistance"));
        Assert.Equal("#/universe?page=1&sector=Energy&q=ab&near=support&short=1&sort=close&dir=desc&rows=100", Href("short", "one gate short"));
        Assert.Contains("data-filter=\"rows\" data-value=\"100\" aria-pressed=\"true\"", controls, StringComparison.Ordinal);

        // The box's press is the shell's, which keeps its words in the link under the box's key and the page back to the first.
        Assert.Contains("<form class=\"universe-search\" role=\"search\" data-merge-key=\"q\">", controls, StringComparison.Ordinal);

        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains("!form.hasAttribute('data-merge-key')", shell, StringComparison.Ordinal);
        Assert.Contains("params.delete('page');", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMembersOneGateShortTakeThePullbacksGateAndThenTheFirstFamilyInTheOneOrder()
    {
        var night = new DateOnly(2026, 10, 9);

        GateResultRow Gates(string ticker, bool trend = true, bool setup = true, bool trigger = true, string[]? excluded = null) =>
            new(ticker, night, "3", true, trend, setup, SetupFamilies.Pullback, trigger, null, true, null, null, null, null, null, null, null, excluded ?? [], false, null, null, null, "{\"gates\":[]}");

        FamilyResultRow Family(string ticker, string family, string missed, int count = 1, string[]? excluded = null) =>
            new(night, ticker, family, false, count, null, null, null, null, null, excluded ?? [], FamilyRule.GatesJson(
            [
                new Gate(FamilyRule.Market, true, "open", FamilyRule.Values()),
                new Gate(missed, false, "missed", FamilyRule.Values()),
            ]));

        var shorts = UniverseScreen.OneShort(
            [
                // AAA misses the pullback's trigger alone and the breakout's volume: the pullback is first.
                Gates("AAA", trigger: false),
                // BBB misses two of the pullback's gates, and the drift's beat and the breakout's volume alone.
                Gates("BBB", setup: false, trigger: false),
                // CCC misses the pullback's trigger alone but an exclusion keeps it off, as one keeps its breakout off.
                Gates("CCC", trigger: false, excluded: ["earnings"]),
                // DDD passes nothing narrowly: its trend and setup both fail.
                Gates("DDD", trend: false, setup: false),
            ],
            [
                Family("AAA", BreakoutRule.Name, "volume"),
                Family("BBB", DriftRule.Name, "beat"),
                Family("BBB", BreakoutRule.Name, "volume"),
                Family("CCC", BreakoutRule.Name, "volume", excluded: ["gap"]),
                Family("DDD", DriftRule.Name, "beat", count: 2),
            ]);

        Assert.Equal(
            [("AAA", SetupFamilies.Pullback, SwingGates.Trigger), ("BBB", BreakoutRule.Name, "volume")],
            shorts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value.Family, pair.Value.Gate)));

        // The chip keeps them and no other member; the tag names the setup and the gate in the setup's tint.
        var cells = UniverseScreen.Rows([UniverseMember("AAA", 10m), UniverseMember("BBB", 10m), UniverseMember("CCC", 10m), UniverseMember("DDD", 10m)])
            .Select(cell => shorts.TryGetValue(cell.Ticker, out var shortOf) ? cell with { ShortFamily = shortOf.Family, ShortGate = shortOf.Gate } : cell)
            .ToArray();

        Assert.Equal(["AAA", "BBB"], UniverseScreen.Kept(cells, Asking(oneShort: true), shorts.Keys.ToHashSet(StringComparer.Ordinal)).Select(cell => cell.Ticker).Order(StringComparer.Ordinal));

        var table = WebUtility.HtmlDecode(new MarkRenderer().UniverseTable(cells));

        Assert.Contains("<span class=\"fam-tag short-tag\" data-family=\"breakout\" data-gate=\"volume\" title=\"One gate short of a breakout: the volume gate\"><span class=\"fam-swatch\" data-family=\"breakout\"></span>Breakout · volume</span>", table, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(table, "class=\"fam-tag short-tag\"").Count);

        // The S&P 400's and 600's nights store only the first part each member failed, which the chip says in its place.
        var degraded = new MarkRenderer().UniverseControls(new UniverseState(), null, null, "400", false, UniverseScreen.PageSizes, UniverseScreen.AllRows);

        Assert.Contains("one gate short: this index's night stores only the first part each member failed", degraded, StringComparison.Ordinal);
        Assert.DoesNotContain("data-value=\"one gate short\"", degraded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUniversePageDrawsATileAGicsSectorAndItsSearchSortAndReadingsBarsOffTheStore()
    {
        using var store = await FixtureExpectations.WithListings();

        FiledSectors(store);

        var night = NightIn(store);

        // XLK's closes: 110 on the night and 100 on the 252nd stored session back, the look-back's start, so the technology
        // tile's fund reads a tenth up; XLC holds none.
        store.Execute(
            "WITH RECURSIVE n(k) AS (SELECT 0 UNION ALL SELECT k + 1 FROM n WHERE k < 251) " +
            "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) " +
            $"SELECT 'XLK', date('{night}', '-' || k || ' days'), c, c, c, c, 'constructed' FROM (SELECT k, CASE k WHEN 0 THEN '110' WHEN 251 THEN '100' ELSE '105' END AS c FROM n);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe"));
        var uptrend = Rows(store, $"SELECT l.ticker FROM ladder l WHERE l.as_of = (SELECT MAX(a.as_of) FROM ladder a WHERE a.ticker = l.ticker AND a.as_of <= '{night}') AND l.trend_state = 'uptrend';")
            .Select(row => row[0]).ToHashSet(StringComparer.Ordinal);
        var listed = (await Api(store).ListingsAsync(DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .Where(listing => listing.IsListed).Select(listing => listing.Ticker).ToHashSet(StringComparer.Ordinal);

        // Three technology members and one communication services member, each tile's counts worked off the store.
        foreach (var (sector, members, fund, made) in new[] { ("Information Technology", new[] { "AAPL", "KEYS", "MSFT" }, "XLK", "0.1"), ("Communication Services", new[] { "NFLX" }, "XLC", "none") })
        {
            var tile = Regex.Match(page, $"<a class=\"sector-tile\" data-sector=\"{sector}\" data-names=\"(\\d+)\" data-uptrend=\"(\\d+)\" data-listed=\"(\\d+)\" data-fund=\"([^\"]+)\" data-fund-return=\"([^\"]+)\" aria-pressed=\"false\" href=\"([^\"]+)\">");

            Assert.True(tile.Success, $"no tile for {sector}");
            Assert.Equal(members.Length.ToString(CultureInfo.InvariantCulture), tile.Groups[1].Value);
            Assert.Equal(members.Count(uptrend.Contains).ToString(CultureInfo.InvariantCulture), tile.Groups[2].Value);
            Assert.Equal(members.Count(listed.Contains).ToString(CultureInfo.InvariantCulture), tile.Groups[3].Value);
            Assert.Equal(fund, tile.Groups[4].Value);
            Assert.Equal($"#/universe?page=1&sector={Uri.EscapeDataString(sector)}", tile.Groups[6].Value);

            if (made == "none")
            {
                Assert.Equal("none", tile.Groups[5].Value);
            }
            else
            {
                Assert.Equal(0.1, double.Parse(tile.Groups[5].Value, CultureInfo.InvariantCulture), 12);
            }
        }

        Assert.Contains("XLK <span class=\"gain\">+10.0%</span> over 251 sessions", page, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(page, "<a class=\"sector-tile\"").Count);

        // A lit tile lifts its filter on a second press.
        var lit = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe?sector=Information%20Technology"));

        Assert.Contains("data-sector=\"Information Technology\" data-names=\"3\"", lit, StringComparison.Ordinal);
        Assert.Matches("<a class=\"sector-tile\" data-sector=\"Information Technology\"[^>]*aria-pressed=\"true\" href=\"#/universe\\?page=1\">", lit);
        Assert.Equal(3, Regex.Matches(lit, "<tr data-ticker=").Count);

        // The search, read off the rows: Apple's name holds the words, and no other member's ticker or company does.
        var found = await client.GetStringAsync("/screens/universe?q=app");

        Assert.Equal(["AAPL"], Regex.Matches(found, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("value=\"app\"", found, StringComparison.Ordinal);

        // The sort, read off the closes the rows carry, highest first.
        var sorted = await client.GetStringAsync("/screens/universe?sort=close&dir=desc");
        var closes = Regex.Matches(sorted, "<td class=\"num\" data-close=\"([^\"]+)\"").Select(match => decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(4, closes.Length);
        Assert.Equal(closes.OrderDescending(), closes);
        Assert.Contains("data-sort=\"close\" data-dir=\"desc\"", sorted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDryUpsBarIsTickedAtTheNightsFilterCeilingAndTheTightnesssCarriesNone()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe"));

        // The night's gate rows name the version they were stored under, which holds no settings of its own where no
        // version is open, so the ceiling is the proposed one, over the bar's scale of two.
        var versions = SwingRows(store, $"SELECT DISTINCT version FROM gate_result WHERE session_date = '{SwingNight}';");
        var stored = SwingRows(store, $"SELECT settings FROM filter_version WHERE version = '{Assert.Single(versions)[0]}';");
        var ceiling = stored.Count == 0 ? FilterSettings.Proposed.DryUpCeiling : FilterSettings.Read(stored[0][0]).DryUpCeiling;
        var dryUps = Regex.Matches(page, "<td class=\"num swing\" data-dry-up=\"[^\"n]+\">.*?</td>").Select(match => match.Value).ToArray();
        var tightness = Regex.Matches(page, "<td class=\"num swing\" data-tightness=\"[^\"n]+\">.*?</td>").Select(match => match.Value).ToArray();

        Assert.Equal(3, dryUps.Length);
        Assert.All(dryUps, cell => Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"<span class=\"cell-tick\" style=\"left:{ceiling / 2 * 100:0.#}%\"></span>"), cell, StringComparison.Ordinal));
        Assert.Equal(3, tightness.Length);
        Assert.All(tightness, cell => Assert.Contains("<span class=\"cell-bar\"", cell, StringComparison.Ordinal));
        Assert.All(tightness, cell => Assert.DoesNotContain("cell-tick", cell, StringComparison.Ordinal));
    }

    [Fact]
    public void ATileSaysTheTrendIsNotReadOnTheSAndP400AndCountsItsFundsReturn()
    {
        UniverseCell Cell(string ticker, string sector, string? trend) =>
            new(ticker, sector, 1m, trend, null, null, null, null, null);

        IReadOnlyList<UniverseCell> cells = [Cell("AA", "Energy", "uptrend"), Cell("BB", "Energy", null), Cell("CC", UniverseScreen.SectorNotOnFile, "uptrend"), Cell("DD", "Utilities", "range")];
        var tiles = UniverseScreen.Tiles(cells, false, new HashSet<string>(["BB"], StringComparer.Ordinal), sector => sector == "Energy" ? ("XLE", -0.05) : null);

        Assert.Equal([("Energy", 2, 1, 1, "XLE"), ("Utilities", 1, 0, 0, (string?)null)], tiles.Select(tile => (tile.Sector, tile.Names, tile.InUptrend, tile.OnTheList, tile.Fund)));

        var drawn = WebUtility.HtmlDecode(new MarkRenderer().SectorTiles(tiles, null, null, "400", new UniverseState(), 1, 126));

        Assert.Equal(2, Regex.Matches(drawn, "<span class=\"tile-trend degraded\">the trend is not read on this index</span>").Count);
        Assert.Contains("XLE <span class=\"loss\">-5.0%</span> over 126 sessions", drawn, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tile-fund degraded\">no sector fund</span>", drawn, StringComparison.Ordinal);
        Assert.Contains("<p class=\"oneline\" data-not-filed=\"1\">1 member files no sector and is counted in none.</p>", drawn, StringComparison.Ordinal);
        Assert.Contains("href=\"#/universe?page=1&sector=Energy&universe=400\"", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void OnAPhoneEachUniverseRowIsACardOfItsNameCloseTrendAndDistancePicture()
    {
        var css = Stylesheet.Css;
        var block = css[css.IndexOf("@media (max-width:560px){\n .universe-table", StringComparison.Ordinal)..];

        block = block[..(block.IndexOf("\n}", StringComparison.Ordinal) + 2)];

        // The headings and every cell hidden, then the name, the close, the trend and the distance picture drawn again.
        Assert.Contains(".universe-table tr:first-child{display:none}", block, StringComparison.Ordinal);
        Assert.Contains(".universe-table td{display:none;", block, StringComparison.Ordinal);
        Assert.Equal(
            ["td.c-nm", "td:nth-child(3)", "td:nth-child(4)", "td:nth-child(5)"],
            Regex.Matches(block, @"\.universe-table (td[^{]*)\{display:block").Select(match => match.Groups[1].Value));

        // Those are the name, the close, the trend and the distance columns, in the table's own heading order.
        Assert.Equal(["Name", "Close", "Trend", "Distance"], new[] { 0, 2, 3, 4 }.Select(at => MarkRenderer.UniverseHeadings[at].Heading));
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public async Task ACorrectionsNoticeStandsOnUniverseToItsThirtiethDayAndIsFoldedOntoTheRunPageAfter(int days, bool folded)
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var before = DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
            $"SELECT ticker, '{before}', '{ReasonsBeforeTheCorrection}', 1, plan_at_listing, shadow_reasons FROM listing WHERE session_date = '{night}';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var universe = await client.GetStringAsync("/screens/universe");
        var run = await client.GetStringAsync($"/screens/run/{night}");

        Assert.Equal(!folded, universe.Contains("<p class=\"written-before-correction\"", StringComparison.Ordinal));
        Assert.Equal(folded, universe.Contains($"<p class=\"oneline corrections-link\" data-sessions=\"1\">A correction to listings more than 30 days old is listed under Corrections on <a href=\"{SinglePageApp.RunRoute}\">the Run page</a>.</p>", StringComparison.Ordinal));
        Assert.Equal(folded, run.Contains($"<details class=\"corrections\" data-sessions=\"{before}\"><summary>Corrections older than 30 days</summary><p class=\"written-before-correction\" data-sessions=\"{before}\">", StringComparison.Ordinal));
        Assert.Equal(SinglePageApp.CorrectionAgeDays < days, SinglePageApp.CorrectionIsOld([DateOnly.ParseExact(before, "yyyy-MM-dd", CultureInfo.InvariantCulture)], DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }
}
