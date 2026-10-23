using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Analysts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data.Analysts;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.13: a chart page's ask for its analysts and its press for TipRanks' ratings, each written only under
// the page's own header, Yahoo's not inside the minutes between two asks or over an answer of the New York day and
// TipRanks' not over an answer of the day or a spent month until its count starts again; the chart page drawing Yahoo's
// targets as a range, its counts by month and each firm's action, ten shown and the rest behind a control, and the
// provider's own figures where Yahoo answered nothing, saying so; the ratings drawn with the month's calls used and
// left; and the routes reading each answer back as the section the page swaps in.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
// see: TipRanks' analyst ratings are asked only on a press and never past the month's limit, a stock again only 15 days after its last answer
public partial class ReadSurface
{
    // The rows 20.13 adds that this check reaches: section 15.5's target range, section 15.22's firm actions and the
    // analysts by name, and the interval between two asks of Yahoo.
    internal static string[] AnalystPageRows =>
    [
        CheckReach.Key("15.5 The mark vocabulary", "Target range"),
        CheckReach.Key("15.22 Chart", "The firm actions"),
        CheckReach.Key("15.22 Chart", "The analysts by name"),
        CheckReach.Key(Scope.LimitsTable, "Analyst ask interval"),
    ];

    // Friday 2026-10-23 at 11:00 in New York.
    static readonly DateTimeOffset ChartOpened = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    static async Task<(HttpStatusCode Status, string Body)> AnalystPress(Dictionary<DateTimeOffset, PassHost> hosts, TemporaryStore store, RecordingLauncher launcher, string route, DateTimeOffset at, bool fromThePage)
    {
        if (!hosts.TryGetValue(at, out var host))
        {
            hosts[at] = host = new PassHost(store.Root) { Clock = FixedClock.At(at, SessionZones.UnitedStates), Launcher = launcher };
        }

        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, route);

        if (fromThePage)
        {
            request.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);
        }

        using var reply = await client.SendAsync(request);

        return (reply.StatusCode, await reply.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnAnalystsAskIsWrittenUnderThePagesHeaderOnceInTenMinutesAndNeverOverAnAnswerOfTheNewYorkDay()
    {
        using var store = await FixtureExpectations.WithListings();

        var name = FiredNamesOn(store, NightIn(store))[0];
        var launcher = new RecordingLauncher();
        var hosts = new Dictionary<DateTimeOffset, PassHost>();

        Task<(HttpStatusCode Status, string Body)> Press(DateTimeOffset at, bool fromThePage = true, string? ticker = null) =>
            AnalystPress(hosts, store, launcher, SinglePageApp.AnalystsRoute + (ticker ?? name), at, fromThePage);

        long Asks() => Count(store, "analyst_request");

        // Without the page's own header, and for a stock no index holds: refused, nothing written, nothing started.
        Assert.Equal(HttpStatusCode.Forbidden, (await Press(ChartOpened, fromThePage: false)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Press(ChartOpened, ticker: "ZZZZ")).Status);
        Assert.Equal((0L, 0), (Asks(), launcher.Analysts.Count));

        // Under the header: the ask written as Yahoo's and the analyst job started with the stock and the instant.
        var asked = await Press(ChartOpened);

        Assert.Equal(HttpStatusCode.Accepted, asked.Status);
        Assert.Contains("data-asked=\"true\" data-started=\"true\"", asked.Body, StringComparison.Ordinal);
        Assert.Equal([$"{name}|2026-10-23T15:00:00Z|yahoo"], Rows(store, "SELECT ticker, asked_at, source FROM analyst_request;").Select(row => string.Join("|", row)));
        Assert.Equal((name, ChartOpened), launcher.Analysts.Single());

        // Nine minutes and fifty-nine seconds later the ask stands and nothing is asked; ten minutes later, again.
        Assert.Equal(HttpStatusCode.OK, (await Press(ChartOpened.AddSeconds(599))).Status);
        Assert.Equal((1L, 1), (Asks(), launcher.Analysts.Count));
        Assert.Equal(HttpStatusCode.Accepted, (await Press(ChartOpened.AddMinutes(10))).Status);
        Assert.Equal((2L, 2), (Asks(), launcher.Analysts.Count));

        // An answer Yahoo gave on the New York day stands, and nothing is asked over it until the next day's first second.
        store.Execute($"INSERT INTO analyst_view (ticker, asked_at, answered_at, currency, target_low, target_mean, target_median, target_high, analysts, recommendation, months, actions) VALUES ('{name}', '2026-10-23T15:10:00Z', '2026-10-23T15:10:04Z', 'USD', '19', '27', '29', '33', 17, 'hold', '[]', '[]');");

        var answered = await Press(ChartOpened.AddMinutes(30));

        Assert.Equal(HttpStatusCode.OK, answered.Status);
        Assert.Contains($"Yahoo answered for {name} at 2026-10-23T15:10:00Z, on this New York day, so nothing was asked.", answered.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Press(new DateTimeOffset(2026, 10, 24, 3, 59, 59, TimeSpan.Zero))).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await Press(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero))).Status);
        Assert.Equal((3L, 3), (Asks(), launcher.Analysts.Count));

        foreach (var host in hosts.Values)
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task ARatingsPressIsRefusedInsideAStocksFifteenDaysAndOverASpentMonthUntilEachEnds()
    {
        using var store = await FixtureExpectations.WithListings();

        var names = Rows(store, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;").Select(row => row[0]).ToArray();
        var launcher = new RecordingLauncher();
        var hosts = new Dictionary<DateTimeOffset, PassHost>();

        Task<(HttpStatusCode Status, string Body)> Press(string ticker, DateTimeOffset at, bool fromThePage = true) =>
            AnalystPress(hosts, store, launcher, SinglePageApp.RatingsRoute + ticker, at, fromThePage);

        long Asks() => Count(store, "analyst_request");

        Assert.Equal(HttpStatusCode.Forbidden, (await Press(names[0], ChartOpened, fromThePage: false)).Status);
        Assert.Equal((0L, 0), (Asks(), launcher.Ratings.Count));

        // Under the header: written as TipRanks' and the ratings job started.
        Assert.Equal(HttpStatusCode.Accepted, (await Press(names[0], ChartOpened)).Status);
        Assert.Equal([$"{names[0]}|tipranks"], Rows(store, "SELECT ticker, source FROM analyst_request;").Select(row => string.Join("|", row)));
        Assert.Equal((names[0], ChartOpened), launcher.Ratings.Single());

        // An answer TipRanks gave for the stock stands for 15 days from the press it answered, and no call is made over it.
        store.Execute($"INSERT INTO analyst_rating (ticker, asked_at, answered_at, as_of, total, counts, ratings) VALUES ('{names[0]}', '2026-10-23T15:00:00Z', '2026-10-23T15:00:06Z', '2026-10-23', 0, '[]', '[]');");

        var again = await Press(names[0], ChartOpened.AddMinutes(5));

        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Contains($"TipRanks answered for {names[0]} at 2026-10-23T15:00:00Z, and its press opens again at 2026-11-07T15:00:00Z, so no call was made.", again.Body, StringComparison.Ordinal);

        // The month's 100 calls spent as the newest run read them: refused for any stock until the count starts again on
        // 2026-11-01, a second before it refused and at it asked.
        store.Execute($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('{AnalystLimits.RatingsStage}-spent', '{AnalystLimits.RatingsStage}', '2026-10-23T15:20:00Z', '2026-10-23T15:20:02Z', 'limit reached', 0, 0, 3, '0', '{{\"ticker\":\"{names[0]}\",\"askedAt\":\"2026-10-23T15:20:00Z\",\"detail\":\"x\",\"used\":100,\"limit\":100,\"resetsAt\":\"2026-11-01T00:00:00Z\",\"readAt\":\"2026-10-23T15:20:01Z\"}}');");

        var spent = await Press(names[1], ChartOpened.AddMinutes(30));

        Assert.Equal(HttpStatusCode.Conflict, spent.Status);
        Assert.Contains("The month&#39;s 100 TipRanks calls are used until 2026-11-01T00:00:00Z, so no call was made.", spent.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await Press(names[1], new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero))).Status);
        Assert.Equal((1L, 1), (Asks(), launcher.Ratings.Count));
        Assert.Equal(HttpStatusCode.Accepted, (await Press(names[1], new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero))).Status);
        Assert.Equal((2L, 2), (Asks(), launcher.Ratings.Count));

        // The first stock's press opens at the 15th day's instant and not a second before.
        Assert.Equal(HttpStatusCode.Conflict, (await Press(names[0], new DateTimeOffset(2026, 11, 7, 14, 59, 59, TimeSpan.Zero))).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await Press(names[0], new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero))).Status);
        Assert.Equal((3L, 3), (Asks(), launcher.Ratings.Count));

        foreach (var host in hosts.Values)
        {
            host.Dispose();
        }
    }

    // Twelve constructed actions a day apart from 2026-10-12 back, each raising its firm's target by one from 20 up.
    static IReadOnlyList<YahooAction> TwelveActions =>
    [
        .. Enumerable.Range(0, 12).Select(at => new YahooAction(
            new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero).AddDays(-at),
            "Firm " + (at + 1),
            at == 0 ? "up" : "main",
            at == 0 ? "Hold" : "Buy",
            "Buy",
            20m + at,
            21m + at,
            "Raises")),
    ];

    static IReadOnlyList<YahooMonth> FourMonths =>
    [
        new("0m", 1, 4, 10, 2, 0),
        new("-1m", 1, 3, 10, 3, 0),
        new("-2m", 0, 3, 11, 3, 0),
        new("-3m", 0, 2, 12, 3, 1),
    ];

    [Fact]
    public async Task TheChartPageDrawsYahoosAnswerAndTheRatingsWithTheCallsLeftAndTheProvidersFiguresWhereYahooAnsweredNothing()
    {
        using var store = await FixtureExpectations.WithListings();

        var name = FiredNamesOn(store, NightIn(store))[0];
        var close = Rows(store, $"SELECT close FROM bar WHERE ticker = '{name}' ORDER BY session_date DESC LIMIT 1;").Single()[0];

        // Before Yahoo answers: the provider's mean target and rating bars, the section asking Yahoo, and the press with no
        // count read yet.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(ChartOpened, SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            var page = await client.GetStringAsync($"/screens/chart/{name}");

            Assert.Contains($"<section class=\"chart-graph\" data-graph=\"analysts\" data-analysts-route=\"{SinglePageApp.AnalystsRoute}{name}\" data-ask=\"yes\"><h3>The analysts</h3>", page, StringComparison.Ordinal);
            Assert.Contains("data-mark=\"target-marker\"", page, StringComparison.Ordinal);
            Assert.Contains($"<p class=\"mark-note analysts-said\" data-yahoo=\"asking\">Yahoo is asked for {name}'s targets, counts by month and firm actions as the page opens, and they are drawn here once it answers.</p>", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
            Assert.Contains($"<section class=\"chart-graph\" data-graph=\"ratings\" data-ratings-route=\"{SinglePageApp.RatingsRoute}{name}\"><h3>The analysts by name</h3>", page, StringComparison.Ordinal);
            Assert.Contains("<button type=\"submit\" class=\"btn-2\">Get analyst ratings</button> <span class=\"ratings-calls\" data-calls=\"none\">TipRanks: no call read yet this month</span>", page, StringComparison.Ordinal);
            Assert.Contains($"No TipRanks rating is stored for {name}. A press asks TipRanks once, which spends one of the month's calls.", page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-mark=\"target-range\"", page, StringComparison.Ordinal);

            using var none = JsonDocument.Parse(await client.GetStringAsync(SinglePageApp.AnalystsRoute + name));

            Assert.False(none.RootElement.GetProperty("answered").GetBoolean());
        }

        // Yahoo's answer of the day, TipRanks' HPQ ratings stored for the stock and the month's calls as last read.
        store.Execute(
            "INSERT INTO analyst_view (ticker, asked_at, answered_at, currency, target_low, target_mean, target_median, target_high, analysts, recommendation, months, actions) VALUES " +
            $"('{name}', '2026-10-23T14:00:00Z', '2026-10-23T14:00:04Z', 'USD', '19.0', '27.51765', '29.0', '33.8', 17, 'hold', '{StoredAnalysts.Months(FourMonths)}', '{StoredAnalysts.Actions(TwelveActions)}');");

        var hpq = TipRanksAnswers.Ratings(File.ReadAllText(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05", "analysts-tipranks-HPQ.json")))!;

        store.Execute($"INSERT INTO analyst_rating (ticker, asked_at, answered_at, as_of, total, counts, ratings) VALUES ('{name}', '2026-10-23T14:30:00Z', '2026-10-23T14:30:05Z', '2026-10-23', 16, '{StoredAnalysts.Counts(hpq.Counts)}', '{StoredAnalysts.Ratings(hpq.Ratings).Replace("'", "''", StringComparison.Ordinal)}');");
        store.Execute($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('{AnalystLimits.RatingsStage}-one', '{AnalystLimits.RatingsStage}', '2026-10-23T14:30:00Z', '2026-10-23T14:30:06Z', 'answered', 1, 0, 4, '0', '{{\"ticker\":\"{name}\",\"askedAt\":\"2026-10-23T14:30:00Z\",\"detail\":\"x\",\"used\":3,\"limit\":100,\"resetsAt\":\"2026-11-01T00:00:00Z\",\"readAt\":\"2026-10-23T14:30:06Z\"}}');");

        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(ChartOpened.AddMinutes(1), SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            var page = await client.GetStringAsync($"/screens/chart/{name}");

            // The day's answer stands, so the page asks nothing; the range at the price the page draws, the newest close.
            Assert.Contains($"data-analysts-route=\"{SinglePageApp.AnalystsRoute}{name}\" data-ask=\"no\" data-answered=\"2026-10-23T14:00:00Z\"", page, StringComparison.Ordinal);
            Assert.Contains($"data-mark=\"target-range\" data-low=\"19.0\" data-mean=\"27.51765\" data-high=\"33.8\" data-median=\"29.0\" data-price=\"{close}\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-mark=\"target-marker\"", page, StringComparison.Ordinal);

            // Yahoo's four months as the rating bars draw them, July to October 2026 oldest first, each as fetched on the
            // day it was asked.
            var analysts = Regex.Match(page, "<section class=\"chart-graph\" data-graph=\"analysts\".*?</section>", RegexOptions.Singleline).Value;

            Assert.Contains("data-mark=\"rating-bars\" data-months=\"4\"", analysts, StringComparison.Ordinal);
            Assert.Equal(["2026-07", "2026-08", "2026-09", "2026-10"], Regex.Matches(analysts, "data-month=\"(\\d{4}-\\d{2})\"").Select(match => match.Groups[1].Value).Distinct());
            Assert.Contains("<title>Oct 26: 4 buy as fetched on 2026-10-23</title>", page, StringComparison.Ordinal);

            // Twelve actions, ten shown newest first and numbered, two behind the control.
            Assert.Contains("<div class=\"firm-actions\" data-mark=\"firm-actions\" data-actions=\"12\" data-shown=\"10\"><p class=\"muted\">Showing 10 of 12 firm actions, newest first, as Yahoo answered on 2026-10-23.</p>", page, StringComparison.Ordinal);
            Assert.Contains("<details class=\"more-actions\"><summary>Show the other 2</summary>", page, StringComparison.Ordinal);
            Assert.Contains("<tr data-graded=\"2026-10-12\" data-firm=\"Firm 1\" data-action=\"up\" data-target=\"21\"><td class=\"r num\">1</td><td class=\"nw\">2026-10-12</td><td>Firm 1</td><td>Upgrade</td><td>Hold to Buy</td><td class=\"r num gain\">20.00 to 21.00</td></tr>", page, StringComparison.Ordinal);
            Assert.Contains("<td class=\"r num\">12</td><td class=\"nw\">2026-10-01</td><td>Firm 12</td><td>Maintained</td><td>Buy</td>", page, StringComparison.Ordinal);

            // TipRanks' sixteen ratings with the calls used and left as the newest run read them.
            Assert.Contains("<span class=\"ratings-calls\" data-used=\"3\" data-left=\"97\" data-limit=\"100\">TipRanks: 3 used, 97 left this month, starting again on 2026-11-01</span>", page, StringComparison.Ordinal);

            // TipRanks answered for the stock at 14:30 UTC, so its press is drawn closed with the day it opens again.
            Assert.Contains("<button type=\"submit\" class=\"btn-2\" disabled data-opens=\"2026-11-07T14:30:00Z\">Get analyst ratings</button> <span class=\"ratings-wait\">TipRanks answered on 2026-10-23; the press opens again on 2026-11-07.</span>", page, StringComparison.Ordinal);
            Assert.Contains("<div class=\"tipranks-ratings\" data-mark=\"tipranks-ratings\" data-asked=\"2026-10-23\" data-ratings=\"16\">", page, StringComparison.Ordinal);
            Assert.Contains("<span data-span=\"last90Days\" data-analysts=\"13\">90 days: 13 analysts, 1 buy, 7 hold, 5 sell</span>", page, StringComparison.Ordinal);
            Assert.Contains("<td class=\"r num\">4.4</td><td class=\"r num\">65.7%</td><td class=\"r num gain\">+27.6%</td>", page, StringComparison.Ordinal);

            // Each route reads its answer back as the section the page swaps in.
            using var yahoo = JsonDocument.Parse(await client.GetStringAsync(SinglePageApp.AnalystsRoute + name));

            Assert.True(yahoo.RootElement.GetProperty("answered").GetBoolean());
            Assert.Equal("2026-10-23T14:00:00Z", yahoo.RootElement.GetProperty("answeredAt").GetString());
            Assert.StartsWith("<section class=\"chart-graph\" data-graph=\"analysts\"", yahoo.RootElement.GetProperty("html").GetString(), StringComparison.Ordinal);
            Assert.Contains("data-mark=\"target-range\"", yahoo.RootElement.GetProperty("html").GetString(), StringComparison.Ordinal);

            using var tipranks = JsonDocument.Parse(await client.GetStringAsync(SinglePageApp.RatingsRoute + name));

            Assert.Equal("2026-10-23T14:30:00Z", tipranks.RootElement.GetProperty("answeredAt").GetString());
            Assert.Equal("answered", tipranks.RootElement.GetProperty("run").GetProperty("outcome").GetString());
            Assert.Contains("data-used=\"3\" data-left=\"97\"", tipranks.RootElement.GetProperty("html").GetString(), StringComparison.Ordinal);
        }
    }

    // TipRanks sends some words with an ampersand already written as an entity and others with it bare, as its first
    // answer for PG did, and each is drawn as its characters escaped once: the analyst, the article's title and the firm.
    [Fact]
    public void TipRanksWordsSentWithAnEntityAreDrawnAsTheirCharactersEscapedOnce()
    {
        var sent = new TipRanksRating("Ana &amp; Lee", "Smith &amp; Co", "Buy", "upgraded", new DateOnly(2026, 10, 23), 176m, "USD", 2.5, 0.857, 8.4, 3, 40, "Procter &amp; Gamble upgraded to Buy from Hold");
        var bare = sent with { Analyst = "Bo & Al", Firm = "Jones & Co", Article = "Procter & Gamble (NYSE: PG) and Nestlé SA" };
        var drawn = new MarkRenderer().TipRanksRatings("PG", new TipRanksView(new DateTimeOffset(2026, 10, 23, 18, 29, 48, TimeSpan.Zero), new TipRanksAnswer(new DateOnly(2026, 10, 23), 2, [], [sent, bare])));

        Assert.Contains("<tr data-analyst=\"Ana &amp; Lee\" data-target=\"176\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<td>Ana &amp; Lee<span class=\"muted tr-article\"> Procter &amp; Gamble upgraded to Buy from Hold</span></td><td>Smith &amp; Co</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("<tr data-analyst=\"Bo &amp; Al\" data-target=\"176\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<td>Bo &amp; Al<span class=\"muted tr-article\"> Procter &amp; Gamble (NYSE: PG) and Nestlé SA</span></td><td>Jones &amp; Co</td>", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;amp;", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTargetRangeIsWorkedByHandAndTheRatingMonthsAreYahoosOwnLabelsOldestFirst()
    {
        var marks = new MarkRenderer();
        var hpq = new YahooAnalystView(
            new DateTimeOffset(2026, 10, 23, 4, 20, 25, TimeSpan.Zero),
            new YahooAnalystAnswer("USD", 19.0m, 27.51765m, 29.0m, 33.8m, 17, "hold", FourMonths, TwelveActions));
        var svg = marks.TargetRange("HPQ", hpq, 33.76m);

        // Worked by hand: 19.00 and 33.80 padded by 8 per cent of their span, 17.816 to 34.984 over the 520 from 46, so the
        // low at 81.86, the high at 530.14, the mean at 339.85, the median at 384.75 and the price at 528.93; the mean
        // 18.49 per cent under the price, in the fall hue.
        Assert.Contains("<rect class=\"tr-range\" x=\"81.86\" y=\"42\" width=\"448.28\" height=\"12\" rx=\"3\"><title>Yahoo's targets as asked on 2026-10-23: low 19.00, high 33.80</title></rect>", svg, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"tr-mean\" x=\"337.85\" y=\"36\" width=\"4\" height=\"24\"><title>The mean target as asked on 2026-10-23: 27.52</title></rect>", svg, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"tr-median\" x=\"380.75\" y=\"44\" width=\"8\" height=\"8\" transform=\"rotate(45 384.75 48)\">", svg, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tm-price\" cx=\"528.93\" cy=\"48\" r=\"6\"><title>The price drawn: 33.76</title></circle>", svg, StringComparison.Ordinal);
        Assert.Contains(">Mean 27.52</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Low 19.00</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">High 33.80</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Price 33.76</text>", svg, StringComparison.Ordinal);
        Assert.Matches("<text class=\"tm-against loss\" x=\"626\" y=\"52\" text-anchor=\"end\" data-against=\"-18\\.49[0-9]*\">-18\\.5%</text>", svg);
        Assert.EndsWith("<p class=\"mark-note\" data-asked=\"2026-10-23\">The targets 17 analysts set, as Yahoo answered on 2026-10-23, their median 29.00; Yahoo's word for their view is hold.</p>", WebUtility.HtmlDecode(svg), StringComparison.Ordinal);

        // An answer with no high, and no price, each say what the range lacks.
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"target-range\">Yahoo's answer of 2026-10-23 holds no low, mean and high target for HPQ, so no range is drawn.</p>",
            marks.TargetRange("HPQ", hpq with { Answer = hpq.Answer with { TargetHigh = null } }, 33.76m));
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"target-range\">No price is stored for HPQ to set the analysts' targets against.</p>",
            marks.TargetRange("HPQ", hpq, null));

        // "0m" is the month of the UTC day asked and "-3m" three before it, drawn oldest first, each as fetched that day; a
        // label that is no month back is passed over.
        var months = MarkRenderer.YahooMonths(hpq with { Answer = hpq.Answer with { Months = [.. FourMonths, new YahooMonth("+1m", 1, 1, 1, 1, 1), new YahooMonth("now", 1, 1, 1, 1, 1)] } });

        Assert.Equal(["2026-07 0/2/12/3/1", "2026-08 0/3/11/3/0", "2026-09 1/3/10/3/0", "2026-10 1/4/10/2/0"], months.Months.Select(month => $"{month.Year}-{month.Month:00} {month.Fetch!.StrongBuy}/{month.Fetch.Buy}/{month.Fetch.Hold}/{month.Fetch.Sell}/{month.Fetch.StrongSell}"));
        Assert.All(months.Months, month => Assert.Equal(new DateOnly(2026, 10, 23), month.Fetch!.FetchedOn));

        // A store holding no action for the stock says so, and a firm stating no target and no grade says that.
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"firm-actions\">Yahoo's answer of 2026-10-23 files no firm's rating action for HPQ.</p>",
            marks.FirmActions("HPQ", hpq with { Answer = hpq.Answer with { Actions = [] } }));
        Assert.Contains(
            "<td>Reiterated</td><td>none stated</td><td class=\"r num flat\">none stated</td>",
            marks.FirmActions("HPQ", hpq with { Answer = hpq.Answer with { Actions = [new YahooAction(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "Firm", "reit", null, null, null, null, null)] } }),
            StringComparison.Ordinal);
    }
}
