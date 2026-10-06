using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;

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

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var rows = await CardsReadBack(client, stored, TheSwitch);

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

        var stored = StoredCards(store);

        Assert.Equal(
            ["MID breakout M1 recorded", "MID heavyweight M2 not recorded", "SML breakout S1 not recorded"],
            stored.Select(card => $"{card.Index} {card.Family} {card.Ticker} {(card.Recorded ? "recorded" : "not recorded")}"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var rows = await CardsReadBack(client, stored, IndexNight);

        // The S&P 400's breakout record under the rule's own heading naming its index, opening with the line that it is the
        // rule's record and not the stock's, its figures as the record stored them and no word of the stock; a rule not
        // replayed draws the outline under its own heading, named for its index.
        var record = Regex.Match(rows["M1"], "<section class=\"card-record\" data-trades=\"400\" data-unit=\"risks\">(.*?)</section>", RegexOptions.Singleline);

        Assert.True(record.Success, "M1's card draws no record.");
        Assert.StartsWith($"<h5>The record of the breakout on the S&P 400</h5><p class=\"card-caution\">{MarkRenderer.RecordIsTheRules}</p>", record.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<dd data-average=\"0.21\">0.21 of the risk after each trade's cost</dd>", record.Groups[1].Value, StringComparison.Ordinal);
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
        Assert.Contains(".card-line{display:grid;grid-template-columns:78px 150px minmax(0,1fr);", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".decision-card{position:sticky;left:0;max-width:min(860px,calc(100vw - 2*var(--gutter) - 24px));", Stylesheet.Css, StringComparison.Ordinal);
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

            foreach (var card in index)
            {
                var control = Regex.Matches(page, $"<button type=\"button\" class=\"card-toggle\" aria-expanded=\"false\" aria-controls=\"{card.Id}\"");
                var row = Regex.Match(page, $"<tr class=\"card-row\" id=\"{card.Id}\" hidden><td colspan=\"\\d+\">(.*?)</td></tr>", RegexOptions.Singleline);

                Assert.True(control.Count == 1, $"The {index.Key} page draws {control.Count} controls for {card.Ticker}'s card.");
                Assert.True(row.Success, $"The {index.Key} page draws no card row for {card.Ticker}.");
                Assert.True(control[0].Index < row.Index, $"{card.Ticker}'s card row stands before the control opening it.");

                // The lines the night stored, in their order, and after them the sixth, which Tonight works out from the
                // operator's open trades where it draws the card.
                var drawn = Regex.Matches(row.Groups[1].Value, "<li class=\"card-line\" data-line=\"(\\d+)\" data-verdict=\"([a-z]+)\"><span class=\"card-mark\" data-verdict=\"\\2\">\\2</span> <b class=\"card-name\">([^<]*)</b> <span class=\"card-words\">([^<]*)</span></li>")
                    .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}|{match.Groups[4].Value}")
                    .ToArray();

                Assert.Equal(card.Lines.Select(line => $"{line.Number}|{line.Verdict}|{line.Name}|{line.Words}"), drawn[..^1]);
                Assert.StartsWith($"6|", drawn[^1], StringComparison.Ordinal);
                Assert.Contains($"|{EquityBrief.Core.Cards.CardLines.ConcentrationName}|", drawn[^1], StringComparison.Ordinal);

                rows[card.Ticker] = row.Groups[1].Value;
            }

            Assert.Equal(
                index.Select(card => card.Id).Order(StringComparer.Ordinal),
                Regex.Matches(page, "<tr class=\"card-row\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        }

        foreach (var card in stored)
        {
            var named = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{card.Ticker}/{night}"));
            var top = Regex.Match(named, $"<section class=\"name-card\" data-family=\"{card.Family}\" data-index=\"{card.Index}\">(.*?)</div></section>", RegexOptions.Singleline);

            Assert.True(top.Success, $"{card.Ticker}'s page draws no card at its top.");
            Assert.Equal(
                card.Lines.Select(line => line.Words),
                Regex.Matches(top.Groups[1].Value, "<span class=\"card-words\">([^<]*)</span>").Select(match => match.Groups[1].Value));
        }

        return rows;
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
