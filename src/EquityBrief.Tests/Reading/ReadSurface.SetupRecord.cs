using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 22.6 correction of 2026-11-09: a pick's card, on the stock page, beneath its row on Tonight, on Home and on the read-only
// copy, draws its setup's record over the long history beneath its entry check line, read off the newest run of the test of
// the buy and sell points on its index, or the year a filing-based setup's filings reach back to.
// see: A pick's card draws its setup's record over the long history under its entry check, the rule's record and never the stock's
public partial class ReadSurface
{
    internal static string[] SetupRecordRows =>
    [
        CheckReach.Key("15.18 The decision card", "The setup's record over the long history"),
        CheckReach.Key("15.24 Home", "An idea's record over the long history"),
        CheckReach.Key("15.7 Tonight", "A setup's record on its tile"),
    ];

    // The pullback's older run on the S&P 500 and its newer, lost; the breakout made; the dip over too few picks; the S&P 400's
    // drift written before the column was, and its breakout made, by a hair.
    static void SeedPlanPoints(TemporaryStore store) => store.Execute(@"
        INSERT INTO plan_point (run_id, index_code, family, through, setups, picks, traded_later_edge_per_pick, traded_later_picks) VALUES
            ('old', 'GSPC', 'pullback', '2026-11-05', 10, 5, 0.5, 999),
            ('new', 'GSPC', 'pullback', '2026-11-06', 10, 5, -0.124, 2933),
            ('new', 'GSPC', 'breakout', '2026-11-06', 10, 5, 0.186, 517),
            ('new', 'GSPC', 'dip', '2026-11-06', 10, 5, -0.04, 12),
            ('old', 'MID', 'drift', '2025-11-06', 10, 5, 0.3, NULL),
            ('mid', 'MID', 'breakout', '2026-11-06', 10, 5, 0.034, 4567);");

    static string Read(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", string.Empty));

    [Fact]
    public async Task TheReadHandsEachSetupItsNewestTradedRecordAndEachFilingSetupTheYearItsFilingsReachBackTo()
    {
        using var store = new TemporaryStore().Migrated();

        SeedPlanPoints(store);

        var records = await Api(store).SetupRecordsAsync();
        var pullback = records.Of("GSPC", SetupFamilies.Pullback)!;

        Assert.Equal((-0.124, 2933, 2019, 2026, "lost"), (pullback.EdgePerPick, pullback.Picks, pullback.From, pullback.Through, pullback.State));
        Assert.Equal("made", records.Of("GSPC", BreakoutRule.Name)!.State);
        Assert.Equal("short", records.Of("GSPC", DipRule.Name)!.State);
        Assert.Equal("unread", records.Of("MID", DriftRule.Name)!.State);
        Assert.Equal("made", records.Of("MID", BreakoutRule.Name)!.State);
        Assert.Null(records.Of("SML", SetupFamilies.Pullback));

        // A price family the run did not write reads none, the drift among them though 23.5 measured its first test year; each
        // filing-based family three learning years before the first test year 23.5 measured on its index, worked here by hand.
        Assert.Null(records.Of("GSPC", DriftRule.Name));
        Assert.Equal(
            [("GSPC", 2013), ("GSPC", 2020), ("GSPC", 2013), ("MID", 2012), ("SML", 2014)],
            new[] { ("GSPC", ImprovingRule.Name), ("GSPC", HeavyweightRule.Name), ("GSPC", QualityRule.Name), ("MID", HeavyweightRule.Name), ("SML", QualityRule.Name) }
                .Select(one => (one.Item1, records.Of(one.Item1, one.Item2)!.ReachesBack!.Value)));
    }

    [Fact]
    public void APicksCardDrawsItsSetupsRecordBeneathItsEntryCheckLineInTheWordsOfEachState()
    {
        var lost = new SetupLongRecord("GSPC", SetupFamilies.Pullback, -0.124, 2933, 2019, 2026);
        var row = new DecisionCardRow("GSPC", EntryNight, SetupFamilies.Pullback, "HAND", 1, 110m, 100m, 130m, "the rule", "{}", "[]", null, Cap: 60, EntryCheck: EntryFlagged());
        var view = CardScreen.View(row, CardContext.None with { LongRecords = new SetupRecords([lost]) });

        Assert.Same(lost, view.LongRecord);

        // The app's card and the read-only copy's: the heading, the figure in the loss's colour and the count worked by hand
        // from -0.124 over 2,933, and the rule's note, beneath the entry check line.
        foreach (var drawn in new[] { new MarkRenderer().DecisionCard(view), new MarkRenderer { Copying = true }.DecisionCard(view) })
        {
            var box = Regex.Match(drawn, "<div class=\"setup-record\".*?</div>", RegexOptions.Singleline).Value;

            Assert.Equal(
                "This setup's record on the S&P 500From 2019 to 2026, picks like this lost about $0.12 for every $1 risked, compared with buying the index's other stocks the same way, over 2,933 picks.The rule's record over every stock it bought, not this stock's chance.",
                Read(box));
            Assert.Contains("<b class=\"lost\">$0.12</b>", box, StringComparison.Ordinal);
            Assert.True(drawn.IndexOf("entry-card-line", StringComparison.Ordinal) < drawn.IndexOf("setup-record", StringComparison.Ordinal));
        }

        Assert.DoesNotContain("setup-record", new MarkRenderer().DecisionCard(CardScreen.View(row, CardContext.None)), StringComparison.Ordinal);

        // Made, rounded to the cent; a filing-based setup's year; too few picks drawn as its count against the minimum alone;
        // and nothing where the run wrote no count.
        Assert.Contains(
            "From 2019 to 2026, picks like this made about $0.19 more for every $1 risked than buying the index's other stocks the same way, over 517 picks.",
            Read(MarkRenderer.SetupRecordBox(new SetupLongRecord("GSPC", BreakoutRule.Name, 0.186, 517, 2019, 2026))),
            StringComparison.Ordinal);
        Assert.Contains("<b class=\"made\">$0.19</b>", MarkRenderer.SetupRecordBox(new SetupLongRecord("GSPC", BreakoutRule.Name, 0.186, 517, 2019, 2026)), StringComparison.Ordinal);
        Assert.Contains(
            "No long record yet: the filings this setup reads reach back only to 2013, so its record starts with its live trades.",
            Read(MarkRenderer.SetupRecordBox(SetupLongRecord.Filings("GSPC", ImprovingRule.Name))),
            StringComparison.Ordinal);
        Assert.Contains(
            "From 2019 to 2026, 12 picks like this, short of the 20 a record needs.",
            Read(MarkRenderer.SetupRecordBox(new SetupLongRecord("GSPC", DipRule.Name, -0.04, 12, 2019, 2026))),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.SetupRecordBox(new SetupLongRecord("MID", DriftRule.Name, 0.3, null, 2019, 2025)));
        Assert.Equal(string.Empty, MarkRenderer.SetupRecordBox(null));
        Assert.Null(SetupLongRecord.Filings("GSPC", DriftRule.Name));

        // Home's idea and a buy no plan sells on Tonight draw it beneath their entry check line too.
        var home = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [new HomeIdea(SetupFamilies.Pullback, "HAND", null, "A dip.", 100m, 90m, 125m, false, null, EntryCheck: EntryFlagged(), LongRecord: lost)], IdeasCount = 1 }, string.Empty);
        var card = Regex.Match(home, "<article class=\"home-idea.*?</article>", RegexOptions.Singleline).Value;

        Assert.Contains("over 2,933 picks.", Read(card), StringComparison.Ordinal);
        Assert.True(card.IndexOf("entry-card-line", StringComparison.Ordinal) < card.IndexOf("setup-record", StringComparison.Ordinal));

        var buys = new MarkRenderer().TomorrowsBuys([], null, night: EntryNight, planless: [new PlanlessBuyView(ImprovingRule.Name, ImprovingRule.Label, "HAND", null, "Bought.", "8 of 9", 100m, "on its filings", "#/tonight", EntryFlagged(), SetupLongRecord.Filings("GSPC", ImprovingRule.Name))]);
        var why = Regex.Match(buys, "<td class=\"why\">.*?</td>", RegexOptions.Singleline).Value;

        Assert.Contains("reach back only to 2013", Read(why), StringComparison.Ordinal);
        Assert.True(why.IndexOf("entry-card-line", StringComparison.Ordinal) < why.IndexOf("setup-record", StringComparison.Ordinal));
    }

    // The 22.6 correction of 2026-11-09's next: each setup's tile on Tonight ends on its record, in each state's words worked by
    // hand and read back off the S&P 400's tiles.
    // see: Each setup's tile on Tonight ends on its record over the long history with its plan as it stands
    [Fact]
    public async Task EachSetupsTileOnTonightEndsOnItsRecordInTheWordsOfEachState()
    {
        Assert.Equal(
            "2019 to 2026 on the S&P 500, with its plan as it stands: lost $0.12 per $1 risked against the index's stocks bought the same way.",
            Read(MarkRenderer.TileRecordLine(new SetupLongRecord("GSPC", SetupFamilies.Pullback, -0.124, 2933, 2019, 2026))));
        Assert.Contains("<b class=\"lost\">lost $0.12</b>", MarkRenderer.TileRecordLine(new SetupLongRecord("GSPC", SetupFamilies.Pullback, -0.124, 2933, 2019, 2026)), StringComparison.Ordinal);
        Assert.Equal(
            "2019 to 2026 on the S&P 500, with its plan as it stands: made $0.19 per $1 risked against the index's stocks bought the same way.",
            Read(MarkRenderer.TileRecordLine(new SetupLongRecord("GSPC", BreakoutRule.Name, 0.186, 517, 2019, 2026))));
        Assert.Contains("<b class=\"made\">made $0.19</b>", MarkRenderer.TileRecordLine(new SetupLongRecord("GSPC", BreakoutRule.Name, 0.186, 517, 2019, 2026)), StringComparison.Ordinal);
        Assert.Equal("No long record yet: the filings it reads reach back only to 2013.", Read(MarkRenderer.TileRecordLine(SetupLongRecord.Filings("GSPC", QualityRule.Name))));
        Assert.Equal("2019 to 2026 on the S&P 500: 12 picks, short of the 20 a record needs.", Read(MarkRenderer.TileRecordLine(new SetupLongRecord("GSPC", DipRule.Name, -0.04, 12, 2019, 2026))));
        Assert.Equal(string.Empty, MarkRenderer.TileRecordLine(new SetupLongRecord("MID", DriftRule.Name, 0.3, null, 2019, 2025)));

        using var store = UniversesStore();

        SeedPlanPoints(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var tonight = await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400");

        string Tile(string family) => Regex.Match(tonight, $"<button type=\"button\" class=\"setup-tile[^>]*data-family=\"{family}\".*?</button>", RegexOptions.Singleline).Value;

        // The breakout's run made money; the sector heavyweights' filings reach back to 23.5's year on the S&P 400 less three;
        // the drift's run wrote no count and the pullback's none at all, so neither tile draws a line.
        Assert.EndsWith("2019 to 2026 on the S&P 400, with its plan as it stands: made $0.03 per $1 risked against the index's stocks bought the same way.", Read(Tile(BreakoutRule.Name)), StringComparison.Ordinal);
        Assert.EndsWith("No long record yet: the filings it reads reach back only to 2012.", Read(Tile(HeavyweightRule.Name)), StringComparison.Ordinal);
        Assert.DoesNotContain("tile-record", Tile(DriftRule.Name), StringComparison.Ordinal);
        Assert.DoesNotContain("tile-record", Tile(SetupFamilies.Pullback), StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, Tile(SetupFamilies.Pullback));
    }

    [Fact]
    public async Task TheStockPageTonightsRowAndAMonthlyBooksBuyDrawTheRecordReadOffTheStore()
    {
        // The S&P 400's night: M1 listed under the breakout with its card stored, and M2 bought by the sector heavyweights.
        using var store = UniversesStore();

        SeedPlanPoints(store);
        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap) VALUES " +
            $"('MID', '{IndexNight}', 'breakout', 'M1', 1, '50', '47', NULL, 'the rule', '{{}}', '[]', NULL, 63), " +
            $"('MID', '{IndexNight}', 'heavyweight', 'M2', 1, '50', NULL, NULL, 'the book', '{{}}', '[]', NULL, NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        const string Made = "From 2019 to 2026, picks like this made about $0.03 more for every $1 risked than buying the index's other stocks the same way, over 4,567 picks.";

        var page = await client.GetStringAsync($"/screens/name/M1/{IndexNight}");

        Assert.Contains("This setup's record on the S&P 400", Read(page), StringComparison.Ordinal);
        Assert.Contains(Made, Read(page), StringComparison.Ordinal);

        var tonight = await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400");
        var fold = Regex.Match(tonight, "<tr class=\"fold-row\"[^>]*data-fold-for=\"M1\".*?</div></td></tr>", RegexOptions.Singleline).Value;

        Assert.Contains(Made, Read(fold), StringComparison.Ordinal);

        // The sector heavyweights' holding bought at the night's reshuffle, its card on the book's card: the year 23.5 measured
        // on the S&P 400 less three.
        var held = Regex.Match(tonight, "<div class=\"decision-card\"[^>]*data-family=\"heavyweight\" data-ticker=\"M2\".*?class=\"rec-sub\"", RegexOptions.Singleline).Value;

        Assert.Contains("No long record yet: the filings this setup reads reach back only to 2012, so its record starts with its live trades.", Read(held), StringComparison.Ordinal);
    }
}
