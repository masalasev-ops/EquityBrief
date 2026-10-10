using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.News;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.6: the news region on a name's page, tonight's counts beside each row, and the Run page's
// labeller line with its count toward the nights its limits settle from, each read off the rendered page over
// a constructed store whose counts are worked by hand.
// see: The news labels alone name the model that wrote them
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
public partial class ReadSurface
{
    internal static readonly string[] NewsSurfaceClaims =
    [
        CheckReach.Key("15.9 Name", "News"),
        CheckReach.Key("15.7 Tonight", "The list, the positive and negative stories of the thirty days before the night as the name page's bar counts them"),
        CheckReach.Key("15.10 Run", "Research and spend, the news labeller's line with what the night's labelling cost and the month's against its limit and the articles labelled and the unreadable answers by cause and what stopped it"),
        CheckReach.Key("15.10 Run", "What else is waiting on a count, the nights the news labeller ran against twenty with its measured duration and its month's spend and the share of answers refused for a digit"),
    ];

    const string NewsNight = "2026-09-08";
    const string NewsEarlier = "2026-09-04";
    const string NewsRun = "label-news-20260908T230000Z";
    const string NewsCard = "<section class=\"card\" id=\"news\".*?</section>";

    static string SqlText(string? value) => value is null ? "NULL" : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    // Two evenings the swing filter listed, AG on both and BS on the second with CE one gate short of it, and the
    // articles naming them: of AG's eight, seven fall in the thirty days before the second night, one of them an
    // opinion piece, one answered unreadably, one refused by admissibility and one holding no label; the newest
    // of the two labels one article holds is the one the page draws. The labeller's run for the second night
    // reached both listed names.
    static TemporaryStore NewsStore()
    {
        var store = new TemporaryStore().Migrated();

        OpenCloseVersion(store);
        Evening(store, NewsEarlier, filter: true, [new Member("AG", Passed: true, Rank: 1)]);
        Evening(store, NewsNight, filter: true,
        [
            new Member("AG", Passed: true, Rank: 1),
            new Member("BS", Passed: true, Rank: 2),
            new Member("CE", Trend: false),
        ]);

        void Article(string ticker, string id, string published, string admissibility, string title) =>
            store.Execute(
                "INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES " +
                $"('{ticker}', '{id}', 'https://example.invalid/{id}', '{title}', 'wire.example', '{published}', 'The company said so.', 20, '{admissibility}', '{published[..10]}');");

        void Label(string ticker, string id, string profile, string model, string outcome, string? cause, string? kind, string? direction, string? reason, string at) =>
            store.Execute(
                "INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, cause, kind, direction, reason, labelled_at, run_id) VALUES " +
                $"('{ticker}', '{id}', '{profile}', 1, '{model}', '{outcome}', {SqlText(cause)}, {SqlText(kind)}, {SqlText(direction)}, {SqlText(reason)}, '{at}', '{NewsRun}');");

        Article("AG", "a0", "2026-08-20T12:00:00Z", Admissibility.Accepted, "AG wins a contract");
        Article("AG", "a1", "2026-09-07T12:00:00Z", Admissibility.Accepted, "AG beats");
        Article("AG", "a2", "2026-09-06T12:00:00Z", Admissibility.Accepted, "AG guides down");
        Article("AG", "a3", "2026-09-05T12:00:00Z", Admissibility.Accepted, "AG names a chief");
        Article("AG", "a4", "2026-09-05T09:00:00Z", Admissibility.Accepted, "Why AG is a buy");
        Article("AG", "a5", "2026-09-04T12:00:00Z", Admissibility.Accepted, "AG sales up");
        Article("AG", "a6", "2026-09-03T12:00:00Z", Admissibility.PriceForecast, "AG to double, says a forecaster");
        Article("AG", "a7", "2026-08-01T12:00:00Z", Admissibility.Accepted, "AG from before the window");
        Article("BS", "b1", "2026-09-08T10:00:00Z", Admissibility.Accepted, "BS beats");
        Article("CE", "c1", "2026-09-02T10:00:00Z", Admissibility.Accepted, "CE names a chief");

        Label("AG", "a1", "claude-haiku", "claude-haiku-4-5-20251001", NewsLabelling.Labelled, null, "results", "negative", "The quarter fell short of what the company had guided.", "2026-09-08T23:00:00Z");
        Label("AG", "a1", "deepseek", "deepseek-flash", NewsLabelling.Labelled, null, "results", "positive", "Quarterly sales came in ahead of what the company had guided.", "2026-09-08T23:10:00Z");
        Label("AG", "a2", "deepseek", "deepseek-flash", NewsLabelling.Labelled, null, "guidance", "negative", "The company lowered what it expects for the year.", "2026-09-08T23:10:10Z");
        Label("AG", "a3", "deepseek", "deepseek-flash", NewsLabelling.Labelled, null, "management", "neutral", "A chief was named.", "2026-09-08T23:10:20Z");
        Label("AG", "a4", "deepseek", "deepseek-flash", NewsLabelling.Labelled, null, NewsInstruction.Opinion, "positive", "A columnist argues the shares are cheap.", "2026-09-08T23:10:30Z");
        Label("AG", "a5", "deepseek", "deepseek-flash", NewsLabelling.Unreadable, NewsInstruction.DigitInTheReason, null, null, null, "2026-09-08T23:10:40Z");
        Label("AG", "a7", "deepseek", "deepseek-flash", NewsLabelling.Labelled, null, "deal", "positive", "The company agreed to buy a smaller rival.", "2026-09-08T23:10:50Z");
        Label("BS", "b1", "deepseek", "deepseek-flash", NewsLabelling.Labelled, null, "results", "positive", "Sales came in ahead of expectations.", "2026-09-08T23:11:00Z");

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            $"('{NewsRun}', 'research call: news label, AG a1', '2026-09-08T23:00:10Z', '2026-09-08T23:00:20Z', 'ok', 0, 1, 1, '0.0004', '{{}}'), " +
            $"('{NewsRun}', '{NewsLabelling.Stage}', '2026-09-08T23:00:00Z', '2026-09-08T23:04:30Z', 'ok', 6, 7, 7, '0.0012', " +
            "'{\"session\":\"2026-09-08\",\"profile\":\"deepseek\",\"model\":\"deepseek-flash\",\"instructionVersion\":1,\"monthLimit\":\"5\",\"timeLimitMinutes\":20,\"names\":2,\"reached\":2,\"labelled\":5,\"unreadable\":{\"a digit in the reason\":1},\"refusedByAdmissibility\":1,\"alreadyLabelled\":0,\"cost\":\"0.0012\",\"monthCost\":\"0.0034\",\"stop\":\"every name reached\",\"labelsDropped\":0}');");

        return store;
    }

    static async Task<string> Page(HttpClient client, string path) => WebUtility.HtmlDecode(await client.GetStringAsync(path));

    [Fact]
    public async Task ANamesPageDrawsItsNewsAfterItsGroupByPriceUnderTabsWithTheBarAndTheModelNamedOnce()
    {
        using var store = NewsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await Page(client, "/screens/name/AG");
        var cards = Regex.Matches(page, "<section class=\"card\"[^>]* data-card=\"([^\"]+)\">").Select(card => card.Groups[1].Value).ToList();
        var before = new[] { "peers", "rules" }.First(cards.Contains);

        Assert.Equal("news", cards[cards.IndexOf(before) + 1]);

        var card = Assert.Single(Blocks(page, NewsCard));

        // Worked by hand over the store: seven of AG's eight articles fall in the thirty days before the night,
        // six of them under the all tab since the opinion piece sits in its own; one positive, the article labelled
        // twice reading its newer label, one negative and one neutral, with one answered unreadably, one refused
        // by admissibility and one holding no label.
        Assert.Equal(
            " data-ticker=\"AG\" data-articles=\"7\" data-all=\"6\" data-positive=\"1\" data-negative=\"1\" data-neutral=\"1\" data-opinion=\"1\"",
            Regex.Match(card, "<div class=\"news-region\"([^>]*)>").Groups[1].Value);
        Assert.Contains("aria-label=\"1 positive against 1 negative over the thirty days before\"", card, StringComparison.Ordinal);
        Assert.Equal(
            ["all|6", "positive|1", "negative|1", "neutral|1", "opinion|1"],
            Regex.Matches(card, "<label class=\"chip\" for=\"news-tab-([a-z]+)\" data-tab=\"[a-z]+\" data-count=\"(\\d+)\">").Select(chip => chip.Groups[1].Value + "|" + chip.Groups[2].Value));

        var matches = Regex.Matches(card, "<li class=\"news-row\" data-article=\"([^\"]+)\" data-state=\"([^\"]+)\" data-tabs=\"([^\"]+)\"[^>]*>(.*?)</li>", RegexOptions.Singleline);
        var rows = matches.ToDictionary(row => row.Groups[1].Value, row => (State: row.Groups[2].Value, Tabs: row.Groups[3].Value, Markup: row.Groups[4].Value), StringComparer.Ordinal);

        // Newest first, and nothing from before the window.
        Assert.Equal(["a1", "a2", "a3", "a4", "a5", "a6", "a0"], matches.Select(row => row.Groups[1].Value));
        Assert.Equal(("labelled", "all positive"), (rows["a1"].State, rows["a1"].Tabs));
        Assert.Contains("<span class=\"tag kind\">results</span><span class=\"tag dir\">positive</span>", rows["a1"].Markup, StringComparison.Ordinal);
        Assert.Contains("<span class=\"news-why\" tabindex=\"0\">why<span class=\"says\" role=\"tooltip\">Quarterly sales came in ahead of what the company had guided.</span></span>", rows["a1"].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<span class=\"says\"", rows["a5"].Markup, StringComparison.Ordinal);
        Assert.Contains("<a class=\"news-title\" href=\"https://example.invalid/a1\" rel=\"noopener\" target=\"_blank\">AG beats</a>", rows["a1"].Markup, StringComparison.Ordinal);
        Assert.Contains("<span class=\"news-meta\">wire.example, 2026-09-07</span>", rows["a1"].Markup, StringComparison.Ordinal);
        Assert.Equal(("labelled", "all negative"), (rows["a2"].State, rows["a2"].Tabs));
        Assert.Equal(("labelled", "all neutral"), (rows["a3"].State, rows["a3"].Tabs));
        Assert.Equal(("labelled", "opinion"), (rows["a4"].State, rows["a4"].Tabs));
        Assert.Equal(("unreadable", "all"), (rows["a5"].State, rows["a5"].Tabs));
        Assert.Contains("<span class=\"tag state\">unreadable: a digit in the reason</span>", rows["a5"].Markup, StringComparison.Ordinal);
        Assert.Equal(("refused", "all"), (rows["a6"].State, rows["a6"].Tabs));
        Assert.Contains("<span class=\"tag state\">refused: algorithmic price forecast</span>", rows["a6"].Markup, StringComparison.Ordinal);
        Assert.Equal(("unlabelled", "all"), (rows["a0"].State, rows["a0"].Tabs));

        // The model that wrote the newest label, named once and in small type, and the older label's not at all.
        Assert.Single(Regex.Matches(card, "deepseek-flash"));
        Assert.Contains("<p class=\"news-model\">Labelled by deepseek-flash, the news job's profile deepseek.</p>", card, StringComparison.Ordinal);
        Assert.DoesNotContain("claude-haiku", card, StringComparison.Ordinal);
        Assert.DoesNotContain("news-unlabelled", card, StringComparison.Ordinal);

        // The tabs are inputs the stylesheet reads, and the region carries no script.
        Assert.Equal(5, Regex.Matches(card, "<input type=\"radio\" class=\"news-tab\" name=\"news-tab\"").Count);
        Assert.DoesNotContain("<script", card, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameHoldingNoLabelForTheNightSaysWhyInOneLine()
    {
        using var store = NewsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // CE holds an article and was not on the night's list, which is all the labeller reads.
        var ce = Assert.Single(Blocks(await Page(client, "/screens/name/CE"), NewsCard));

        Assert.Contains("<p class=\"news-unlabelled\" data-why=\"not-listed\">CE was not on the list this night, and the labeller reads the names on the list alone.</p>", ce, StringComparison.Ordinal);
        Assert.DoesNotContain("news-model", ce, StringComparison.Ordinal);

        // AG on the earlier night, whose window holds its unreadable article and the one holding no label: no run
        // is recorded for that night.
        Assert.Contains("data-why=\"not-run\"", Assert.Single(Blocks(await Page(client, $"/screens/name/AG/{NewsEarlier}"), NewsCard)), StringComparison.Ordinal);

        // A run refused for that night before it asked anything, its key missing.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            $"('label-news-20260904T230000Z', '{NewsLabelling.Stage}', '2026-09-04T23:00:00Z', '2026-09-04T23:00:00Z', 'refused', 0, 0, 0, '0', " +
            "'{\"refused\":\"The News job uses the profile ''claude-haiku'', whose key ''Claude'' the secrets file does not hold.\",\"session\":\"2026-09-04\"}');");

        var refused = Assert.Single(Blocks(await Page(client, $"/screens/name/AG/{NewsEarlier}"), NewsCard));

        Assert.Contains("data-why=\"key\"", refused, StringComparison.Ordinal);
        Assert.Contains("whose key 'Claude' the secrets file does not hold", refused, StringComparison.Ordinal);

        // A later run for that night that stopped at the month limit before any name.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            $"('label-news-20260904T231500Z', '{NewsLabelling.Stage}', '2026-09-04T23:15:00Z', '2026-09-04T23:15:01Z', 'ok', 0, 0, 0, '0', " +
            "'{\"session\":\"2026-09-04\",\"profile\":\"deepseek\",\"model\":\"deepseek-flash\",\"names\":1,\"reached\":0,\"labelled\":0,\"unreadable\":{},\"refusedByAdmissibility\":0,\"cost\":\"0\",\"monthCost\":\"5\",\"monthLimit\":\"5\",\"stop\":\"the labeller''s month limit\"}');");

        Assert.Contains("<p class=\"news-unlabelled\" data-why=\"month-limit\">The labeller's month limit was reached before this name, so its articles wait for next month.</p>", Assert.Single(Blocks(await Page(client, $"/screens/name/AG/{NewsEarlier}"), NewsCard)), StringComparison.Ordinal);

        // A name with nothing stored in the window says so and draws no tabs.
        var bs = Assert.Single(Blocks(await Page(client, $"/screens/name/BS/{NewsEarlier}"), NewsCard));

        Assert.Contains("<p class=\"news-none\" data-why=\"none\">No article naming BS was stored in the thirty days before this night.</p>", bs, StringComparison.Ordinal);
        Assert.DoesNotContain("news-tabs", bs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TonightCountsEachRowsPositiveAndNegativeStoriesAsTheNamePagesBarCountsThemAndTheSecondListCarriesNone()
    {
        using var store = NewsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await Page(client, $"/screens/tonight/{NewsNight}");
        var listed = Assert.Single(Blocks(page, "<section class=\"tonight-list\" data-list=\"listed\".*?</section>"));

        Assert.Matches("<thead>.*?News.*?</thead>", listed);

        // AG: one positive and one negative, the opinion piece and the article before the window in neither;
        // BS: one positive.
        Assert.Equal(
            ["AG|1|1", "BS|1|0"],
            Regex.Matches(listed, "<tr data-ticker=\"([A-Z]+)\"[^>]*>.*?<td class=\"news-counts\" data-positive=\"([^\"]+)\" data-negative=\"([^\"]+)\">", RegexOptions.Singleline)
                .Select(row => row.Groups[1].Value + "|" + row.Groups[2].Value + "|" + row.Groups[3].Value));

        var close = Assert.Single(Blocks(page, "<section class=\"tonight-list close-list\".*?</section>"));

        Assert.Contains("data-ticker=\"CE\"", close, StringComparison.Ordinal);
        Assert.DoesNotContain("news-counts", close, StringComparison.Ordinal);
        Assert.DoesNotMatch("<thead>.*?News.*?</thead>", close);
        Assert.Contains("The news labeller reads the list above alone, so a row here carries no news counts.", page, StringComparison.Ordinal);

        // A listed name the labeller has no label for says so in its cell.
        store.Execute("DELETE FROM news_label WHERE ticker = 'BS';");

        Assert.Contains("<tr data-ticker=\"BS\"", await Page(client, $"/screens/tonight/{NewsNight}"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">no label</span></td>", await Page(client, $"/screens/tonight/{NewsNight}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageDrawsTheLabellersLineForTheNightAndCountsItsNightsTowardTwenty()
    {
        using var store = NewsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var run = await Page(client, $"/screens/run/{NewsNight}");
        var line = Regex.Match(run, "<p class=\"rp-labeller\"([^>]*)>(.*?)</p>", RegexOptions.Singleline);

        Assert.True(line.Success, "no labeller line on the run page");
        Assert.Equal(
            " data-session=\"2026-09-08\" data-ran=\"true\" data-cost=\"0.0012\" data-month=\"0.0034\" data-month-limit=\"5\" data-labelled=\"5\" data-unreadable=\"1\" data-refused=\"1\" data-reached=\"2\" data-names=\"2\" data-stop=\"every name reached\"",
            line.Groups[1].Value);
        Assert.Contains("The news labeller on deepseek (deepseek-flash) cost", line.Groups[2].Value, StringComparison.Ordinal);
        Assert.Contains("5 article(s) labelled, 1 unreadable (a digit in the reason 1), 1 refused by admissibility, 2 of 2 name(s) reached, stopped by every name reached.", line.Groups[2].Value, StringComparison.Ordinal);

        var triggers = Regex.Matches(run, "<li data-trigger=\"([^\"]+)\" data-count=\"(\\d+)\" data-of=\"(\\d+)\">(.*?)</li>")
            .ToDictionary(match => match.Groups[1].Value, match => (Count: match.Groups[2].Value, Of: match.Groups[3].Value, Text: match.Groups[4].Value), StringComparer.Ordinal);

        Assert.Equal(("1", "20"), (triggers["news labeller"].Count, triggers["news labeller"].Of));
        Assert.Contains("4.5 minute(s) a run at the median", triggers["news labeller"].Text, StringComparison.Ordinal);
        Assert.Contains("1 of 6 answer(s) refused for a digit", triggers["news labeller"].Text, StringComparison.Ordinal);

        // A night the labeller has no run for says so.
        var earlier = Regex.Match(await Page(client, $"/screens/run/{NewsEarlier}"), "<p class=\"rp-labeller\"([^>]*)>(.*?)</p>", RegexOptions.Singleline);

        Assert.True(earlier.Success, "no labeller line on the earlier night's run page");
        Assert.Equal(" data-ran=\"none\"", earlier.Groups[1].Value);
        Assert.Equal("The news labeller has no run recorded for this night.", earlier.Groups[2].Value);
    }

    [Fact]
    public void ARefusedLabellerRunEndsItsLineWithTheRefusalsOwnPeriodAndNoSecond()
    {
        static string Line(string? refusal) => Regex.Match(
            WebUtility.HtmlDecode(new EquityBrief.Web.Marks.MarkRenderer().ResearchRegion(new EquityBrief.Web.Marks.ResearchPicture(
                new EquityBrief.Web.Marks.NightSpend(0m, 0m, 10m, 50m),
                [],
                new EquityBrief.Web.Marks.LabellerLine(new DateOnly(2026, 10, 1), false, null, null, 0m, 0m, null, 0, new Dictionary<string, int>(), 0, 0, 0, null, refusal)))),
            "<p class=\"rp-labeller\"[^>]*>(.*?)</p>",
            RegexOptions.Singleline).Groups[1].Value;

        // The refusal of 2026-10-01 began as the labeller wrote it, a sentence closing on its own period, which
        // the run page drew with a second one after it.
        Assert.Equal(
            "The news labeller was refused before it asked anything: The News job uses the profile 'claude-haiku', whose key 'Claude' the secrets file does not hold.",
            Line("The News job uses the profile 'claude-haiku', whose key 'Claude' the secrets file does not hold."));

        // A refusal written with no period of its own, or none recorded, is closed by one.
        Assert.Equal("The news labeller was refused before it asked anything: no answer came.", Line("no answer came"));
        Assert.Equal("The news labeller was refused before it asked anything: no reason recorded.", Line(null));
    }
}
