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

        Assert.Contains($"<section class=\"behind-it card-compares\" data-index=\"S&P 500\" data-push=\"drawn\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains($"<li data-heading=\"Momentum\" data-push=\"{momentum.ToString("R", CultureInfo.InvariantCulture)}\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains($"<ul class=\"behind-push\" data-centre=\"2\" data-score=\"{(0.2 / 0.1 * 1.05).ToString("R", CultureInfo.InvariantCulture)}\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains("<li data-heading=\"Momentum\" data-percentile=\"0.75\">", rows["K1"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"behind-it card-compares\" data-index=\"S&P 500\" data-push=\"none\">", rows["F1"], StringComparison.Ordinal);
        Assert.Contains("<li data-heading=\"Momentum\" data-percentile=\"0.25\">", rows["F1"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"behind-it card-compares\" data-index=\"S&P 500\" data-push=\"none\">", rows["K2"], StringComparison.Ordinal);
        Assert.DoesNotContain("data-percentile=\"0", rows["K2"], StringComparison.Ordinal);

        // From 22.6 each heading's bar says what higher means under it, as section 15.23 declares: F1's momentum, a
        // quarter of the way up, reads 25 of 100.
        Assert.Contains("<span class=\"bh-name\">Momentum</span>", rows["F1"], StringComparison.Ordinal);
        Assert.Contains("<b class=\"bh-figure\">25</b> of 100; higher means stronger momentum</span>", rows["F1"], StringComparison.Ordinal);

        // No rule replayed on the S&P 500: each card's record is the outline under the setup's own heading.
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>How this setup has done</h5><p class=\"degraded\">No record yet", rows["K1"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>How this setup has done</h5><p class=\"degraded\">No record yet", rows["F1"], StringComparison.Ordinal);

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
            $"('MID', 'breakout', '{RuleReplay.ProvisionalWords("breakout")!.Replace("'", "''", StringComparison.Ordinal)}', 'high=126', '2026-10-02T12:00:00Z', '2019-01-02', '2026-10-01', 'as it stood', 'risks', 400, NULL, 0.21, 12, '[100,100,100,100]', -0.4);");

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        // M1 holds the S&P 400's readings on the night, which its card's chart reads among its own index's members.
        var m1 = new double?[LedgerReadings.Count];

        m1[LedgerReadings.IndexOf("close_over_twenty")] = 1.02;
        store.Execute($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('MID', '{IndexNight}', 'M1', '{SetupLedger.ReadingsText(m1)}', '{LedgerReadings.Version}');");

        var stored = StoredCards(store);

        Assert.Equal(
            ["MID breakout M1 recorded", "MID heavyweight M2 not recorded", "SML breakout S1 not recorded"],
            stored.Select(card => $"{card.Index} {card.Family} {card.Ticker} {(card.Recorded ? "recorded" : "not recorded")}"));

        // S1's one band the night stored, a support from 46 to 47.5 beneath its close of 50.
        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) VALUES " +
            $"('S1', '{IndexNight}', '46', '47.5', 'support', 1, 2, 1, '[]');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var rows = await CardsReadBack(client, stored, IndexNight);

        // From 22.6 the S&P 600's buy points fold each row's pick's card beneath it alone, closed where the link names no
        // row: S1's fold hidden, holding its card, whose one plan is its setup's own, and no levels plan, which stays on the
        // stock's full page, though the night stored a band for it beneath its close of 50.
        var points = BuyPointsOf(WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf("SML", IndexNight))));
        var s1 = Assert.Single(Folds(points));

        Assert.Equal(("fold-breakout-s1", "S1", true), (s1.Id, s1.Ticker, s1.Hidden));
        Assert.StartsWith("<div class=\"decision-card\" data-index=\"SML\" data-family=\"breakout\" data-ticker=\"S1\"", s1.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("level-summary", s1.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("selected-name", s1.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("data-low-edge=\"46\"", s1.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(" class=\"sel\"", points, StringComparison.Ordinal);

        // From 19.11 the row's stock opens its chart page, and its report link the report.
        Assert.Contains("<a class=\"name-link\" href=\"#/chart/S1\">S1</a>", points, StringComparison.Ordinal);
        Assert.Matches("<a class=\"open[^\"]*\" href=\"#/name/S1\"", points);

        // With S3 listed second and its card written, a link naming S3 opens its fold and no other, its row selected, and a
        // link naming a stock no row holds opens none.
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('SML', '{IndexNight}', 'S3', 'breakout', 1, 2, '40', '38', NULL, '2', 63, 1.9, NULL);");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('SML', '{IndexNight}', 'S3', 'breakout', 'listed', 2, '[]', NULL, NULL, NULL);");

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 55, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards-again");

        var named = BuyPointsOf(WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf("SML", IndexNight) + "&name=S3")));

        Assert.Equal(["S3"], Folds(named).Where(fold => !fold.Hidden).Select(fold => fold.Ticker));
        Assert.Contains("<tr data-ticker=\"S3\" data-family=\"breakout\" data-place=\"2\" data-fold=\"fold-breakout-s3\" class=\"sel\">", named, StringComparison.Ordinal);

        var unnamed = BuyPointsOf(WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf("SML", IndexNight) + "&name=ZZZ")));

        Assert.All(Folds(unnamed), fold => Assert.True(fold.Hidden, $"{fold.Ticker}'s fold is open for a name no row holds."));
        Assert.Equal(2, Folds(unnamed).Count);

        // M1's chart names its index and places it alone among its members, level with itself; no score on the S&P 400.
        Assert.Contains("<section class=\"behind-it card-compares\" data-index=\"S&P 400\" data-push=\"none\">", rows["M1"], StringComparison.Ordinal);
        Assert.Contains("<li data-heading=\"Momentum\" data-percentile=\"0.5\">", rows["M1"], StringComparison.Ordinal);

        // The S&P 400's breakout record under the setup's own heading, worked by hand off the record stored: 400 trades
        // from 2019 to 2026 with no target set, so no share reached one, an average of 0.21 of the risk, $21.00 for every
        // $100 risked, a median of 12 trading days and a median worst close of 0.4 of the risk, 40% of the way to the stop;
        // the lines that it is the setup's record and not the stock's and that its settings were chosen on those years; and
        // no word of the stock. A rule not replayed draws the outline under the setup's own heading.
        var record = Regex.Match(rows["M1"], "<section class=\"card-record\" data-trades=\"400\" data-unit=\"risks\">(.*?)</section>", RegexOptions.Singleline);

        Assert.True(record.Success, "M1's card draws no record.");
        Assert.StartsWith(
            "<h5>How this setup has done</h5><p class=\"record-said\" data-trades=\"400\" data-won=\"none\" data-average=\"0.21\">From 2019 to 2026 the breakout made 400 trades, and on average it made about $21.00 for every $100 risked, after costs. A trade was usually over in 12 trading days.</p>",
            record.Groups[1].Value,
            StringComparison.Ordinal);
        Assert.Contains("<div class=\"plan-tile\" data-tile=\"worst\" data-worst=\"-0.4\"><span class=\"tile-label\">Typical worst close</span><b>40% of the way to the stop</b>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<div class=\"plan-tile\" data-tile=\"held\" data-median=\"12\" data-held=\"", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"card-caution\">{MarkRenderer.RecordIsTheRules} {MarkRenderer.RecordReadsHigh}</p>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("M1", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>How this setup has done</h5><p class=\"degraded\">No record yet", rows["M2"], StringComparison.Ordinal);
        Assert.Contains("<section class=\"card-record outline\" data-trades=\"none\"><h5>How this setup has done</h5><p class=\"degraded\">No record yet", rows["S1"], StringComparison.Ordinal);

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

        // From 19.3 a row a table opens beneath one of its rows, a buy point's fold and a heavyweight's card, is held inside
        // the box the table scrolls in, read off that box's own width, whatever padding the card around the table carries:
        // the vw rule above measured the screen and ran a card past the box by the section's own padding.
        Assert.Contains(".tbl-wrap.opens-rows{container-type:inline-size}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".opens-rows .row-open{position:sticky;left:12px;max-width:calc(100cqw - 24px)}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".list-table tr.card-row>td{white-space:normal;background:var(--plot);padding:12px}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".buy-points tr.fold-row>td{white-space:normal;background:var(--plot);padding:12px}", Stylesheet.Css, StringComparison.Ordinal);

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
        // word for a reader that does not see it: from 22.6 the first line's on the step it passed, the business's among
        // the other checks, and the earnings line's on the next report among what could go wrong.
        Assert.Contains("<li class=\"card-tick\" data-step=\"\"><span class=\"card-mark\" data-verdict=\"tick\" role=\"img\" aria-label=\"tick\" title=\"tick\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><path d=\"M3.2 8.6l3 3 6.6-7\"/></svg></span> <span class=\"card-words\">Passed every step.</span></li>", drawn, StringComparison.Ordinal);
        Assert.Contains("<li class=\"card-line\" data-line=\"2\" data-verdict=\"note\"><span class=\"card-mark\" data-verdict=\"note\" role=\"img\" aria-label=\"note\" title=\"note\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><circle cx=\"8\" cy=\"8\" r=\"6.2\"/><path d=\"M8 7.3v4.2\"/><circle class=\"dot\" cx=\"8\" cy=\"4.9\" r=\"1\"/></svg></span> <b class=\"card-name\">The business</b>", drawn, StringComparison.Ordinal);
        Assert.Contains("<li data-hit=\"report\" data-line=\"3\" data-verdict=\"warning\"><span class=\"card-mark\" data-verdict=\"warning\" role=\"img\" aria-label=\"warning\" title=\"warning\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><path d=\"M8 1.9l6.6 11.6H1.4z\"/><path d=\"M8 6.4v3.6\"/><circle class=\"dot\" cx=\"8\" cy=\"11.7\" r=\"1\"/></svg></span><span class=\"hit-when\">Next report</span>", drawn, StringComparison.Ordinal);
        Assert.DoesNotMatch(">(?:tick|note|warning)<", drawn);

        Assert.Contains(".card-mark svg{width:16px;height:16px;fill:none;stroke:currentColor;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".card-mark svg .dot{fill:currentColor;stroke:none}", Stylesheet.Css, StringComparison.Ordinal);
        // Each mark in a colour of its own: a tick in the green a gain is drawn in, on its fill, a note in grey and a warning
        // in the caution's amber, each pair read at 4.5 to 1 or more in both palettes by the palette's own test.
        Assert.Contains(".card-mark[data-verdict='tick']{color:var(--up);background:var(--up-fill);border:1px solid var(--up)}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".card-mark[data-verdict='note']{color:var(--ink-2);background:var(--plot);", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".card-mark[data-verdict='warning']{color:var(--warn);background:var(--warn-fill);", Stylesheet.Css, StringComparison.Ordinal);
    }

    // The page each index's Tonight is read on.
    static string TonightOf(string index, string night) => index switch
    {
        "MID" => $"/screens/tonight/{night}?universe=400",
        "SML" => $"/screens/tonight/{night}?universe=600",
        _ => $"/screens/tonight/{night}",
    };

    // Each stored card read back off its index's Tonight and off its stock's page on the night: a buy point's in its fold
    // and a holding's in a row of its own after the holding's, each hidden until its control in the row before opens it,
    // holding each line the night stored with its verdict, name and words in the order stored; each index's page drawing
    // its own cards and no other index's; and the stock's page opening with the card. The cards' rows by ticker.
    static async Task<IReadOnlyDictionary<string, string>> CardsReadBack(HttpClient client, IReadOnlyList<StoredCard> stored, string night)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var index in stored.GroupBy(card => card.Index))
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf(index.Key, night)));
            var folded = new List<string>();

            foreach (var card in index)
            {
                // From the 19.2 correction of 2026-10-21 a buy point's card stands in its fold after its plan, the control
                // in its row's stock cell naming the fold; a holding's stands in a card row with its control in its row.
                var foldId = MarkRenderer.FoldId(card.Family, card.Ticker);
                var fold = Regex.Match(page, $"<tr class=\"fold-row\" id=\"{foldId}\" data-fold-for=\"{Regex.Escape(card.Ticker)}\" hidden><td colspan=\"\\d+\">(.*?)(?=<tr data-ticker=|</tbody></table></div><p class=\"buys-read\">)", RegexOptions.Singleline);
                var row = Regex.Match(page, $"<tr class=\"card-row\" id=\"{card.Id}\" hidden><td colspan=\"\\d+\">(.*?)</td></tr>", RegexOptions.Singleline);
                string body;

                if (fold.Success && fold.Groups[1].Value.Contains($"<div class=\"decision-card\" data-index=\"{card.Index}\" data-family=\"{card.Family}\" data-ticker=\"{card.Ticker}\"", StringComparison.Ordinal))
                {
                    var control = Regex.Matches(page, $"<button type=\"button\" class=\"card-toggle fold-toggle\" aria-expanded=\"false\" aria-controls=\"{foldId}\"");

                    Assert.True(control.Count == 1, $"The {index.Key} page draws {control.Count} controls for {card.Ticker}'s fold.");
                    Assert.True(control[0].Index < fold.Index, $"{card.Ticker}'s fold stands before the control opening it.");
                    Assert.False(row.Success, $"The {index.Key} page draws {card.Ticker}'s card twice.");
                    folded.Add(card.Id);
                    body = fold.Groups[1].Value;
                }
                else
                {
                    var control = Regex.Matches(page, $"<button type=\"button\" class=\"card-toggle\" aria-expanded=\"false\" aria-controls=\"{card.Id}\"");

                    Assert.True(control.Count == 1, $"The {index.Key} page draws {control.Count} controls for {card.Ticker}'s card.");
                    Assert.True(row.Success, $"The {index.Key} page draws no card row for {card.Ticker}.");
                    Assert.True(control[0].Index < row.Index, $"{card.Ticker}'s card row stands before the control opening it.");
                    body = row.Groups[1].Value;
                }

                // The lines the night stored, as 22.6 draws them: the first as a tick a step it passed, the earnings line
                // among what could go wrong with its own mark, and every other line among the other checks in its order,
                // and after them the sixth, which Tonight works out from the operator's open trades where it draws the card.
                LinesReadBack(card, body, pressed: true);

                rows[card.Ticker] = body;
            }

            // Every card no fold holds stands in a card row, and with no name in the link no fold is open.
            Assert.Equal(
                index.Select(card => card.Id).Where(id => !folded.Contains(id)).Order(StringComparer.Ordinal),
                Regex.Matches(page, "<tr class=\"card-row\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
            Assert.DoesNotMatch("<tr class=\"fold-row\" id=\"[^\"]+\" data-fold-for=\"[^\"]+\"><td", page);
        }

        foreach (var card in stored)
        {
            var named = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{card.Ticker}/{night}"));
            var top = Regex.Match(named, $"<section class=\"card name-card\" id=\"card\" data-role=\"buy\" data-family=\"{card.Family}\" data-index=\"{card.Index}\">(.*?)</div></section>", RegexOptions.Singleline);

            // The card is the page's third region, after the reading guide and the short version, its lines the night
            // stored drawn as on Tonight and no sixth, which reads the operator's own trades.
            Assert.True(top.Success, $"{card.Ticker}'s page draws no card as its third region.");
            Assert.Equal(
                ["how-to-read", "s-the-short-version", "card"],
                Regex.Matches(named, "<section class=\"(?:card[^\"]*|absent)\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Take(3));
            LinesReadBack(card, top.Groups[1].Value, pressed: false);
        }

        return rows;
    }

    // A card's stored lines read back off it as 22.6 draws them: the first line's steps each a tick naming its step as the
    // night stored it, every step the line names drawn and no other; the earnings line among what could go wrong, its mark
    // and its words kept; every other line among the other checks in its order with its mark and its words said plainly;
    // and where the card draws its presses, the sixth after them.
    static void LinesReadBack(StoredCard card, string body, bool pressed)
    {
        var first = card.Lines.Single(line => line.Number == 1);
        var why = Regex.Match(body, $"<section class=\"card-why\" data-line=\"1\" data-verdict=\"{first.Verdict}\"><h5>Why it qualified</h5><ul class=\"card-ticks\">(.*?)</ul></section>", RegexOptions.Singleline);

        Assert.True(why.Success, $"{card.Ticker}'s card draws no tick for the steps it passed.");

        var steps = Regex.Matches(why.Groups[1].Value, "<li class=\"card-tick\" data-step=\"([^\"]*)\">").Select(match => match.Groups[1].Value).ToArray();

        Assert.NotEmpty(steps);
        Assert.All(steps, step => Assert.Contains(step.Length == 0 ? "Passed every gate" : step + ", ", first.Words, StringComparison.Ordinal));

        var checks = Regex.Matches(body, "<li class=\"card-line\" data-line=\"(\\d+)\" data-verdict=\"([a-z]+)\"><span class=\"card-mark\" data-verdict=\"\\2\" role=\"img\" aria-label=\"\\2\" title=\"\\2\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\">.*?</svg></span> <b class=\"card-name\">([^<]*)</b> <span class=\"card-words\">(.*?)</span></li>")
            .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{CardWords(match.Groups[4].Value)}")
            .ToArray();
        var expected = card.Lines
            .Where(line => line.Number is not 1 and not 4)
            .Select(line => $"{line.Number}|{line.Verdict}|{ReaderWords.Plain(Said(line.Words))}")
            .ToArray();

        Assert.Equal(expected, pressed ? checks[..^1] : checks);

        if (pressed)
        {
            Assert.StartsWith("6|", checks[^1], StringComparison.Ordinal);
        }

        if (card.Lines.SingleOrDefault(line => line.Number == 4) is { } report)
        {
            var hit = Regex.Match(body, $"<li data-hit=\"report\" data-line=\"4\" data-verdict=\"{report.Verdict}\"><span class=\"card-mark\" data-verdict=\"{report.Verdict}\" role=\"img\"[^>]*><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\">.*?</svg></span><span class=\"hit-when\">Next report</span><span class=\"hit-what\">(.*?)<span class=\"hit-why\">");

            Assert.True(hit.Success, $"{card.Ticker}'s card draws its earnings line nowhere among what could go wrong.");
            Assert.Equal(ReaderWords.Plain(Said(report.Words)), CardWords(hit.Groups[1].Value));
        }
    }

    // A card line's words as stored, read off the markup with each signed figure's gain or loss span taken off and no
    // other markup, so a line drawing anything but the pair beside its words fails.
    static string CardWords(string drawn) =>
        Regex.Replace(drawn, "<span class=\"(?:gain|loss)\">([^<]*)</span>", "$1");

    // A sentence a rule stored as the page states it, by the test's own reading: a figure written past two places, or to
    // two at a thousand or more, at two places with its thousands marked, a count of one with its noun's singular where
    // the rule wrote "(s)" after it, and any other count with the noun's plural.
    static string Said(string stored) =>
        Regex.Replace(
            Regex.Replace(
                stored,
                "(?<![\\d.])(\\d+)\\.(\\d+)(?![\\d.])",
                figure => figure.Groups[2].Length > 2 || (figure.Groups[2].Length == 2 && figure.Groups[1].Length > 3)
                    ? Math.Round(decimal.Parse(figure.Value, CultureInfo.InvariantCulture), 2, MidpointRounding.AwayFromZero).ToString("#,##0.00", CultureInfo.InvariantCulture)
                    : figure.Value),
            "(\\d[\\d,]*) ([A-Za-z]+)\\(s\\)",
            count => count.Groups[1].Value == "1" ? $"1 {count.Groups[2].Value}" : $"{count.Groups[1].Value} {count.Groups[2].Value}s");

    [Fact]
    public void AFigureARuleStoredPastTwoPlacesIsDrawnAtThePlacesItIsReadAt()
    {
        // Worked by hand from TRV's card of 2026-10-16 and JBL's of 2026-10-13: the target stored at 441.815 reads 441.82
        // as the plan beside it does, a strength of 0.540 against its floor of 0.500 reads 0.54 against 0.50, a reward to
        // risk of 2.3085 reads 2.31 and a round trip of 0.013 of the risk 0.01, while a figure stored to two places, a
        // percentage and a whole count stay as written.
        Assert.Equal(
            "bought at the close of 389.54 with the stop at 368.63 and the target at 441.82, 2.5 times the risk; strength 0.54 at or above its floor of 0.50; reward to risk 2.31 at or above 2; breadth 55.0% over 50 sessions; its round trip is 0.01 of the trade's risk",
            MarkRenderer.StoredWords("bought at the close of 389.54 with the stop at 368.63 and the target at 441.815, 2.5 times the risk; strength 0.540 at or above its floor of 0.500; reward to risk 2.3085 at or above 2; breadth 55.0% over 50 session(s); its round trip is 0.013 of the trade's risk"));
        Assert.Equal(MarkRenderer.StoredWords("the target at 441.815 and 3046.47, 2 session(s)"), Said("the target at 441.815 and 3046.47, 2 session(s)"));

        // A pick's card, the name page's gates, the lines saying why the filter passed it and the gates beneath a row of
        // Tonight's list each read the stored words so.
        const string Trade = "reward to risk 2.3085 at or above 2; the target at 441.815, 1 session(s) before tonight";
        const string Read = "reward to risk 2.31 at or above 2; the target at 441.82, 1 session before tonight";
        var marks = new MarkRenderer();
        var card = marks.DecisionCard(new DecisionCardView("GSPC", new DateOnly(2026, 10, 16), "drift", "TRV", "the earnings drift", 389.54m, 368.63m, 441.82m, [new CardLineView(1, "Trend and strength", "tick", Trade)], null));
        var gates = marks.GatesTable("F1", new GatesView(new DateOnly(2026, 9, 5), "3", [new GateLine("trade", true, Trade)], null, null, null, null, null, null, null, null, null, [], [], true, null));
        var why = marks.WhyItPassed("F1", new FilterWhy(new DateOnly(2026, 9, 5), [new FilterGate("trade", true, Trade)], []));
        var list = marks.TonightList(
            [new ListingCell("F1", new DateOnly(2026, 9, 5), 0, 0, 389.54m, [], Filter: new FilterRow(1, "drift", "tonight", EquityBrief.Core.Filter.FilterSettings.SwingWord, "2.3085", "1.95", [new FilterGate("trade", true, Trade)]))],
            1,
            [],
            new ListRuleView(EquityBrief.Core.Shortlist.ListRules.Filter, true, 0.55, 0.45, [1, 1, 1, 1, 1]));

        // From 22.6 the card's tick says the line in the reader's words, its figures read the same way.
        Assert.Contains("<span class=\"card-words\">Reward against the risk 2.31 at or above 2; the target at 441.82, 1 trading day before tonight</span>", card, StringComparison.Ordinal);
        Assert.Contains($"<td>{Read}</td>", gates, StringComparison.Ordinal);
        Assert.Contains($"<b>trade</b>: {Read}</p>", why, StringComparison.Ordinal);
        Assert.Contains($"title=\"trade: {Read}\"", WebUtility.HtmlDecode(list).Replace("&#39;", "'", StringComparison.Ordinal), StringComparison.Ordinal);

        foreach (var drawn in new[] { card, gates, why, list })
        {
            Assert.DoesNotContain("441.815", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("2.3085", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("(s)", drawn, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ARefusedOrUnwrittenSectionsStoredReasonTakesItsCountsInWords()
    {
        // TRV's industry cycle of 2026-10-17 and ADM's of 2026-10-11, as their passes stored them, and a pass's own line.
        var drawn = new MarkRenderer().LeftOut(
            "TRV",
            [new LeftOutSection("TRV", "The two cases", "refused at its third retry: a figure no fact holds, 1 figure(s) named")],
            notWritten:
            [
                new LeftOutSection("TRV", "The industry cycle", "the theme could not be refreshed: declined for lack of industry sources: the searches found 5 page(s) about Insurance - Property & Casualty, from federalreserve.gov, spglobal.com and statista.com"),
                new LeftOutSection("ADM", "The industry cycle", "the theme could not be refreshed: declined for lack of industry sources: the searches found 1 page(s) about Farm Products, from statista.com"),
            ],
            pass: new ResearchPassLine("ok", new DateOnly(2026, 10, 17), "the newest research pass for this name ran on 2026-10-17 over 12 document(s)"));
        var read = WebUtility.HtmlDecode(drawn);

        Assert.Contains("The two cases is left out: refused at its third retry: a figure no fact holds, 1 figure named</p>", read, StringComparison.Ordinal);
        Assert.Contains("The industry cycle is not written: the theme could not be refreshed: declined for lack of industry sources: the searches found 5 pages about Insurance - Property & Casualty, from federalreserve.gov, spglobal.com and statista.com</p>", read, StringComparison.Ordinal);
        Assert.Contains("the searches found 1 page about Farm Products, from statista.com</p>", read, StringComparison.Ordinal);
        Assert.Contains("ran on 2026-10-17 over 12 documents</p>", read, StringComparison.Ordinal);
        Assert.DoesNotContain("(s)", read, StringComparison.Ordinal);
    }

    [Fact]
    public void ACountARuleStoredWithItsNounsSIsDrawnAsTheReaderSaysIt()
    {
        // Worked by hand: one session, none and two sessions, a count over a thousand, and a sentence holding no such count
        // left as stored.
        Assert.Equal("the print of 2026-10-16 reacted on 2026-10-16, 0 sessions before tonight, inside the 3-session window", MarkRenderer.CountsInWords("the print of 2026-10-16 reacted on 2026-10-16, 0 session(s) before tonight, inside the 3-session window"));
        Assert.Equal("the trigger first fired on 2026-09-04, 1 session before tonight", MarkRenderer.CountsInWords("the trigger first fired on 2026-09-04, 1 session(s) before tonight"));
        Assert.Equal("2 sessions and 1,250 calls", MarkRenderer.CountsInWords("2 session(s) and 1,250 call(s)"));
        Assert.Equal("its stop sits 0.85 typical moves below", MarkRenderer.CountsInWords("its stop sits 0.85 typical moves below"));

        // On a pick's card the line reads the same, and from 22.6 its step is said in the reader's words.
        var line = new CardLineView(1, "Trend and strength", "tick", "Passed every gate: print, the print of 2026-10-16 reacted on 2026-10-16, 0 session(s) before tonight.");
        var drawn = new MarkRenderer().DecisionCard(new DecisionCardView("GSPC", new DateOnly(2026, 10, 16), "drift", "TRV", "the earnings drift", 389.54m, 368.63m, 441.82m, [line], null));

        Assert.Contains("<li class=\"card-tick\" data-step=\"print\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<span class=\"card-words\">Reported in its last few trading days: the report of 2026-10-16 reacted on 2026-10-16, 0 trading days before tonight.</span>", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("(s)", drawn, StringComparison.Ordinal);

        // The name page's gates and the lines saying why the filter passed it read their stored reasons the same way.
        const string Trigger = "the trigger first fired on 2026-09-04, 1 session(s) before tonight, inside the 3-session window";
        var marks = new MarkRenderer();
        var gates = marks.GatesTable("F1", new GatesView(new DateOnly(2026, 9, 5), "3", [new GateLine("trigger", true, Trigger)], null, null, null, null, null, null, null, null, null, [], [], true, null));
        var why = marks.WhyItPassed("F1", new FilterWhy(new DateOnly(2026, 9, 5), [new FilterGate("trigger", true, Trigger)], []));

        Assert.Contains("<td>the trigger first fired on 2026-09-04, 1 session before tonight, inside the 3-session window</td>", gates, StringComparison.Ordinal);
        Assert.Contains("<b>trigger</b>: the trigger first fired on 2026-09-04, 1 session before tonight, inside the 3-session window</p>", why, StringComparison.Ordinal);
        Assert.DoesNotContain("(s)", gates + why, StringComparison.Ordinal);
    }

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
