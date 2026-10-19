using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Ledger;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Reading;

// read-surface, 16.1: each pick's card drawn in place beneath its row on Tonight, and at the top of the stock's page, read
// back off the rendered page against the rows the night stored on every index: its control in the row, each line's
// verdict and words, and the rule's record under the rule's own heading naming its index and never the stock, or the
// outline saying the rule has not been replayed; and the card stacked to one column on a phone's screen.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: A rule's record is drawn under the rule's own heading, beside its reason or on a pick's card and never as the stock's own
public partial class ReadSurface
{
    sealed record StoredCardLine(int Number, string Name, string Verdict, string Words);

    sealed record StoredCard(string Index, string Family, string Ticker, IReadOnlyList<StoredCardLine> Lines, bool Recorded)
    {
        public string Id => $"card-{Index}-{Family}-{Ticker}".ToLowerInvariant();
    }

    // The S&P 500's families night: the pullback lists F1 to F5 and holds HELD back, and the breakout lists K1 and K2 with
    // K3 a gate short. The night's cards over it as the night writes them.
    [Fact]
    public async Task EachSAndP500PicksCardIsDrawnInPlaceBeneathItsRowAndAtTheTopOfItsPageAsTheNightStoredIt()
    {
        using var store = await FamilyPagesStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        var stored = StoredCards(store);

        Assert.Equal(
            ["GSPC breakout K1", "GSPC breakout K2", "GSPC pullback F1", "GSPC pullback F2", "GSPC pullback F3", "GSPC pullback F4", "GSPC pullback F5"],
            stored.Select(card => $"{card.Index} {card.Family} {card.Ticker}"));

        // From 19.2 the chart of what is behind each pick: K1 and F1 hold the night's readings, K1's close 1.05 times its
        // 20-session average and F1's 0.95, and K2 none; the breakout's learned score passed on the S&P 500 and the
        // pullback has none.
        // see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
        foreach (var (ticker, overTwenty) in new[] { ("K1", 1.05), ("F1", 0.95) })
        {
            var held = new double?[LedgerReadings.Count];

            held[LedgerReadings.IndexOf("close_over_twenty")] = overTwenty;
            store.Execute($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('GSPC', '{TheSwitch}', '{ticker}', '{SetupLedger.ReadingsText(held)}', '{LedgerReadings.Version}');");
        }

        ScoreRun(store, "loop-test-GSPC-1", passed: true);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var rows = await CardsReadBack(client, stored, TheSwitch);

        // K1's card pushes by its momentum alone, 0.2 times 1.05 less the mean of 1 over the spread of 0.1, from a setup at
        // the means scoring 2 to its own 2.1; F1's, whose family has no score, and K2's, holding no readings, draw the
        // readings alone, K2's none read.
        var momentum = 0.2 * (1.05 - 1.0) / 0.1;

        Assert.Contains($"<section class=\"behind-it\" data-index=\"S&P 500\" data-push=\"drawn\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains($"<li data-heading=\"Momentum\" data-push=\"{momentum.ToString("R", CultureInfo.InvariantCulture)}\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains($"<ul class=\"behind-push\" data-centre=\"2\" data-score=\"{(0.2 / 0.1 * 1.05).ToString("R", CultureInfo.InvariantCulture)}\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains("<li data-heading=\"Momentum\" data-percentile=\"0.75\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"behind-it\" data-index=\"S&P 500\" data-push=\"none\">", rows["F1"], StringComparison.Ordinal);
        Assert.Contains("<li data-heading=\"Momentum\" data-percentile=\"0.25\">", rows["F1"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"behind-it\" data-index=\"S&P 500\" data-push=\"none\">", rows["K2"], StringComparison.Ordinal);
        Assert.DoesNotContain("data-percentile=\"0", rows["K2"], StringComparison.Ordinal);

        // No rule replayed on the S&P 500: each card's record is the outline under the rule's own heading.
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>The record of the breakout on the S&P 500</h5><p class=\"degraded\">Not replayed yet", rows["K1"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>The record of the pullback on the S&P 500</h5><p class=\"degraded\">Not replayed yet", rows["F1"], StringComparison.Ordinal);

        // A stock no family listed has no card on its page: HELD, held back, and K3, a gate short.
        foreach (var ticker in new[] { "HELD", "K3" })
        {
            Assert.DoesNotContain("class=\"name-card\"", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}/{TheSwitch}")), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EachSAndP400And600PicksCardIsDrawnInPlaceBeneathItsRowAndAtTheTopOfItsPageAsTheNightStoredIt()
    {
        // The universes' store: the S&P 400's breakout listed M1, its pullback held M3 back for its trade on the S&P 500's
        // list, and its sector heavyweights bought M2. Here the S&P 600's breakout listed S1 on the same night, and the
        // S&P 400's breakout has a record.
        using var store = UniversesStore();

        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('SML', '{IndexNight}', 2, 0.5, 1, '{IndexFamilies.Settings("SML")}', 0);");
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('SML', '{IndexNight}', 'S1', 'breakout', 1, 1, '50', '47', NULL, '3', 63, 2.4, NULL), " +
            $"('SML', '{IndexNight}', 'S2', 'breakout', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no setup');");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('SML', '{IndexNight}', 'S1', 'breakout', 'listed', 1, '[]', NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO rule_record (index_code, family, rule, settings, recorded_at, first_session, last_session, membership, unit, trades, won, average, median_sessions, ended_by, worst_close) VALUES " +
            "('MID', 'breakout', 'the breakout at high=126', 'high=126', '2026-10-02T12:00:00Z', '2019-01-02', '2026-10-01', 'as it stood', 'risks', 400, NULL, 0.21, 12, '[100,100,100,100]', -0.4);");

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        // M1 holds the S&P 400's readings on the night, which its card's chart reads among its own index's members.
        var m1 = new double?[LedgerReadings.Count];

        m1[LedgerReadings.IndexOf("close_over_twenty")] = 1.02;
        store.Execute($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('MID', '{IndexNight}', 'M1', '{SetupLedger.ReadingsText(m1)}', '{LedgerReadings.Version}');");

        var stored = StoredCards(store);

        Assert.Equal(
            ["MID breakout M1 recorded", "MID heavyweight M2 not recorded", "SML breakout S1 not recorded"],
            stored.Select(card => $"{card.Index} {card.Family} {card.Ticker} {(card.Recorded ? "recorded" : "not recorded")}"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var rows = await CardsReadBack(client, stored, IndexNight);

        // From 19.2 the S&P 600's buy points select their first row where the link names none, drawing S1's card beneath
        // it, and each row's link keeps the index.
        var points = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf("SML", IndexNight))), "<table class=\"list-table buy-points\".*?</table>"));

        Assert.Contains($"data-select-href=\"#/night/{IndexNight}?name=S1&universe=600\" class=\"sel\" aria-selected=\"true\">", points, StringComparison.Ordinal);
        Assert.Matches("<tr class=\"plan-row\" data-plan-for=\"S1\"><td colspan=\"9\"><div class=\"row-open\"><div class=\"decision-card\" data-index=\"SML\" data-family=\"breakout\" data-ticker=\"S1\"", points);

        // From 19.11 the row's stock opens its chart page, and its report link the report.
        Assert.Contains("<a class=\"name-link\" href=\"#/chart/S1\">S1</a>", points, StringComparison.Ordinal);
        Assert.Matches("<a class=\"open[^\"]*\" href=\"#/name/S1\"", points);

        // With S3 listed second, a link naming S3 selects its row and draws its plan's row beneath it and no other, and a
        // link naming a stock no row holds selects the first.
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('SML', '{IndexNight}', 'S3', 'breakout', 1, 2, '40', '38', NULL, '2', 63, 1.9, NULL);");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('SML', '{IndexNight}', 'S3', 'breakout', 'listed', 2, '[]', NULL, NULL, NULL);");

        var named = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf("SML", IndexNight) + "&name=S3")), "<table class=\"list-table buy-points\".*?</table>"));

        Assert.Contains($"data-select-href=\"#/night/{IndexNight}?name=S3&universe=600\" class=\"sel\" aria-selected=\"true\">", named, StringComparison.Ordinal);
        Assert.Equal(["S3"], Regex.Matches(named, "<tr class=\"plan-row\" data-plan-for=\"([^\"]+)\">").Select(match => match.Groups[1].Value));

        var unnamed = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf("SML", IndexNight) + "&name=ZZZ")), "<table class=\"list-table buy-points\".*?</table>"));

        Assert.Equal(["S1"], Regex.Matches(unnamed, "<tr class=\"plan-row\" data-plan-for=\"([^\"]+)\">").Select(match => match.Groups[1].Value));

        // M1's chart names its index and places it alone among its members, level with itself; no score on the S&P 400.
        Assert.Contains("<section class=\"behind-it\" data-index=\"S&P 400\" data-push=\"none\">", rows["M1"], StringComparison.Ordinal);
        Assert.Contains("<li data-heading=\"Momentum\" data-percentile=\"0.5\">", rows["M1"], StringComparison.Ordinal);

        // The S&P 400's breakout record under the rule's own heading naming its index, opening with the line that it is the
        // rule's record and not the stock's, its figures as the record stored them and no word of the stock; a rule not
        // replayed draws the outline under its own heading, named for its index.
        var record = Regex.Match(rows["M1"], "<section class=\"card-record\" data-trades=\"400\" data-unit=\"risks\">(.*?)</section>", RegexOptions.Singleline);

        Assert.True(record.Success, "M1's card draws no record.");
        Assert.StartsWith($"<h5>The record of the breakout on the S&P 400</h5><p class=\"card-caution\">{MarkRenderer.RecordIsTheRules}</p>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<dd data-average=\"0.21\"><span class=\"gain\">+0.21</span> of the risk after each trade's cost</dd>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<dd data-worst=\"-0.4\">a median <span class=\"loss\">-0.40</span> of the risk from the buy before the end</dd>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<dd data-won=\"none\">none read: the rule sets no target, so a trade has no win</dd>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<dd data-median=\"12\" data-held=\"", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"card-caution\">{MarkRenderer.RecordReadsHigh}</p>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("M1", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>The record of the sector heavyweights on the S&P 400</h5><p class=\"degraded\">Not replayed yet", rows["M2"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>The record of the breakout on the S&P 600</h5><p class=\"degraded\">Not replayed yet", rows["S1"], StringComparison.Ordinal);

        // The S&P 500's Tonight, whose families drew nothing on the night, draws no card, and M3, which no family listed,
        // has none on its page.
        Assert.DoesNotContain("class=\"card-row\"", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}")), StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"name-card\"", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M3/{IndexNight}")), StringComparison.Ordinal);
    }

    [Fact]
    public void APicksCardStandsInOneColumnOnAPhonesScreen()
    {
        // On a phone's screen each line and each figure of the record stand in one column, the mark above its words, and
        // the card is no wider than the screen less the page's gutters, however wide the table holding it scrolls.
        var narrow = Regex.Match(Stylesheet.Css, "@media \\(max-width:640px\\)\\{\n(.*?)\n\\}", RegexOptions.Singleline);

        Assert.True(narrow.Success, "The stylesheet states no rules for a phone's screen.");
        Assert.Contains(" .card-line,.card-figures{grid-template-columns:1fr}", narrow.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains(" .card-mark{justify-self:start}", narrow.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains(".card-line{display:grid;grid-template-columns:24px 150px minmax(0,1fr);", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".decision-card{position:sticky;left:0;max-width:min(860px,calc(100vw - 2*var(--gutter) - 24px));", Stylesheet.Css, StringComparison.Ordinal);

        // From 19.3 a row a table opens beneath one of its rows, the buy points' plan and card and a heavyweight's card, is
        // held inside the box the table scrolls in, read off that box's own width, whatever padding the card around the
        // table carries: the vw rule above measured the screen and ran a card past the box by the section's own padding.
        Assert.Contains(".tbl-wrap.opens-rows{container-type:inline-size}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".opens-rows .row-open{position:sticky;left:12px;max-width:calc(100cqw - 24px)}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".list-table tr.card-row>td{white-space:normal;background:var(--plot);padding:12px}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".buy-points tr.plan-row>td{white-space:normal;background:var(--plot);padding:12px}", Stylesheet.Css, StringComparison.Ordinal);

        var marks = new MarkRenderer();
        var card = new DecisionCardView("GSPC", new DateOnly(2026, 10, 1), "pullback", "F1", "the pullback", 50m, 47.3m, 55.4m, [], null);

        Assert.StartsWith("<tr class=\"card-row\" id=\"card-gspc-pullback-f1\" hidden><td colspan=\"9\"><div class=\"row-open\"><div class=\"decision-card\"", marks.CardRow(card, 9), StringComparison.Ordinal);
        Assert.EndsWith("</div></div></td></tr>", marks.CardRow(card, 9), StringComparison.Ordinal);
    }

    [Fact]
    public void EachLineOfAPicksCardIsMarkedByATickANoteSignOrAWarningSignAndNotByItsWord()
    {
        var card = new DecisionCardView(
            "GSPC",
            new DateOnly(2026, 10, 16),
            "drift",
            "TRV",
            "the earnings drift",
            389.54m,
            368.63m,
            441.82m,
            [
                new CardLineView(1, "Trend and strength", "tick", "Passed every gate."),
                new CardLineView(2, "Business", "note", "Its cover is not read."),
                new CardLineView(3, "Earnings in the hold", "warning", "No report date is on file for it."),
            ],
            null);
        var drawn = new MarkRenderer().DecisionCard(card);

        // A tick is a check's two strokes; a note a circle around an i, its stroke and its dot; a warning a triangle
        // around an exclamation, its stroke and its dot. Each is drawn in its mark's own colour and named by its verdict's
        // word for a reader that does not see it.
        Assert.Contains("<li class=\"card-line\" data-line=\"1\" data-verdict=\"tick\"><span class=\"card-mark\" data-verdict=\"tick\" role=\"img\" aria-label=\"tick\" title=\"tick\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><path d=\"M3.2 8.6l3 3 6.6-7\"/></svg></span> <b class=\"card-name\">Trend and strength</b>", drawn, StringComparison.Ordinal);
        Assert.Contains("<span class=\"card-mark\" data-verdict=\"note\" role=\"img\" aria-label=\"note\" title=\"note\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><circle cx=\"8\" cy=\"8\" r=\"6.2\"/><path d=\"M8 7.3v4.2\"/><circle class=\"dot\" cx=\"8\" cy=\"4.9\" r=\"1\"/></svg></span> <b class=\"card-name\">Business</b>", drawn, StringComparison.Ordinal);
        Assert.Contains("<span class=\"card-mark\" data-verdict=\"warning\" role=\"img\" aria-label=\"warning\" title=\"warning\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><path d=\"M8 1.9l6.6 11.6H1.4z\"/><path d=\"M8 6.4v3.6\"/><circle class=\"dot\" cx=\"8\" cy=\"11.7\" r=\"1\"/></svg></span> <b class=\"card-name\">Earnings in the hold</b>", drawn, StringComparison.Ordinal);
        Assert.DoesNotMatch(">(?:tick|note|warning)<", drawn);

        Assert.Contains(".card-mark svg{width:16px;height:16px;fill:none;stroke:currentColor;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".card-mark svg .dot{fill:currentColor;stroke:none}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".card-mark[data-verdict='tick']{color:var(--ink);", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".card-mark[data-verdict='warning']{color:var(--warn);background:var(--warn-fill);", Stylesheet.Css, StringComparison.Ordinal);
    }

    // The page each index's Tonight is read on.
    static string TonightOf(string index, string night) => index switch
    {
        "MID" => $"/screens/tonight/{night}?universe=400",
        "SML" => $"/screens/tonight/{night}?universe=600",
        _ => $"/screens/tonight/{night}",
    };

    // Each stored card read back off its index's Tonight and off its stock's page on the night: its control once in its
    // pick's row and its card in a row of its own after it, hidden until opened, holding each line the night stored with
    // its verdict, name and words in the order stored; each index's page drawing its own cards and no other index's; and
    // the stock's page opening with the card. The card rows by ticker.
    static async Task<IReadOnlyDictionary<string, string>> CardsReadBack(HttpClient client, IReadOnlyList<StoredCard> stored, string night)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var index in stored.GroupBy(card => card.Index))
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf(index.Key, night)));
            var open = new List<string>();

            foreach (var card in index)
            {
                var control = Regex.Matches(page, $"<button type=\"button\" class=\"card-toggle\" aria-expanded=\"false\" aria-controls=\"{card.Id}\"");
                var row = Regex.Match(page, $"<tr class=\"card-row\" id=\"{card.Id}\" hidden><td colspan=\"\\d+\">(.*?)</td></tr>", RegexOptions.Singleline);

                // From 19.3 the buy point selected, the first where the link names none, draws its card open in its plan's
                // row beneath it with no control; every other card stands hidden beneath its row with its control in it.
                var plan = Regex.Match(page, $"<tr class=\"plan-row\" data-plan-for=\"{Regex.Escape(card.Ticker)}\"><td colspan=\"\\d+\">(.*?)(?=<tr data-ticker=|</tbody>)", RegexOptions.Singleline);
                string body;

                if (plan.Success && plan.Groups[1].Value.Contains($"<div class=\"decision-card\" data-index=\"{card.Index}\" data-family=\"{card.Family}\" data-ticker=\"{card.Ticker}\"", StringComparison.Ordinal))
                {
                    Assert.True(control.Count == 0, $"The {index.Key} page draws a control for {card.Ticker}'s card, open in its plan's row.");
                    Assert.False(row.Success, $"The {index.Key} page draws {card.Ticker}'s card twice.");
                    open.Add(card.Id);
                    body = plan.Groups[1].Value;
                }
                else
                {
                    Assert.True(control.Count == 1, $"The {index.Key} page draws {control.Count} controls for {card.Ticker}'s card.");
                    Assert.True(row.Success, $"The {index.Key} page draws no card row for {card.Ticker}.");
                    Assert.True(control[0].Index < row.Index, $"{card.Ticker}'s card row stands before the control opening it.");
                    body = row.Groups[1].Value;
                }

                // The lines the night stored, in their order, and after them the sixth, which Tonight works out from the
                // operator's open trades where it draws the card.
                var drawn = Regex.Matches(body, "<li class=\"card-line\" data-line=\"(\\d+)\" data-verdict=\"([a-z]+)\"><span class=\"card-mark\" data-verdict=\"\\2\" role=\"img\" aria-label=\"\\2\" title=\"\\2\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\">.*?</svg></span> <b class=\"card-name\">([^<]*)</b> <span class=\"card-words\">(.*?)</span></li>")
                    .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}|{CardWords(match.Groups[4].Value)}")
                    .ToArray();

                Assert.Equal(card.Lines.Select(line => $"{line.Number}|{line.Verdict}|{line.Name}|{line.Words}"), drawn[..^1]);
                Assert.StartsWith($"6|", drawn[^1], StringComparison.Ordinal);
                Assert.Contains($"|{EquityBrief.Core.Cards.CardLines.ConcentrationName}|", drawn[^1], StringComparison.Ordinal);

                rows[card.Ticker] = body;
            }

            Assert.Equal(
                index.Select(card => card.Id).Where(id => !open.Contains(id)).Order(StringComparer.Ordinal),
                Regex.Matches(page, "<tr class=\"card-row\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
            Assert.True(open.Count <= 1, $"The {index.Key} page opens {open.Count} cards.");
        }

        foreach (var card in stored)
        {
            var named = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{card.Ticker}/{night}"));
            var top = Regex.Match(named, $"<section class=\"card name-card\" id=\"card\" data-role=\"buy\" data-family=\"{card.Family}\" data-index=\"{card.Index}\">(.*?)</div></section>", RegexOptions.Singleline);

            // The card is the page's third region, after the reading guide and the short version.
            Assert.True(top.Success, $"{card.Ticker}'s page draws no card as its third region.");
            Assert.Equal(
                ["how-to-read", "s-the-short-version", "card"],
                Regex.Matches(named, "<section class=\"(?:card[^\"]*|absent)\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Take(3));
            Assert.Equal(
                card.Lines.Select(line => line.Words),
                Regex.Matches(top.Groups[1].Value, "<span class=\"card-words\">(.*?)</span></li>").Select(match => CardWords(match.Groups[1].Value)));
        }

        return rows;
    }

    // A card line's words as stored, read off the markup with each signed figure's gain or loss span taken off and no
    // other markup, so a line drawing anything but the pair beside its words fails.
    static string CardWords(string drawn) =>
        Regex.Replace(drawn, "<span class=\"(?:gain|loss)\">([^<]*)</span>", "$1");

    // The cards the night stored, each line read off the stored lines by the test's own reading.
    static IReadOnlyList<StoredCard> StoredCards(TemporaryStore store)
    {
        var cards = new List<StoredCard>();

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT index_code, family, ticker, lines, record FROM decision_card ORDER BY index_code, family, ticker;";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            using var lines = JsonDocument.Parse(reader.GetString(3));

            cards.Add(new StoredCard(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                [
                    .. lines.RootElement.EnumerateArray().Select(line => new StoredCardLine(
                        line.GetProperty("line").GetInt32(),
                        line.GetProperty("name").GetString()!,
                        line.GetProperty("verdict").GetString()!,
                        line.GetProperty("words").GetString()!)),
                ],
                !reader.IsDBNull(4)));
        }

        return cards;
    }
}
