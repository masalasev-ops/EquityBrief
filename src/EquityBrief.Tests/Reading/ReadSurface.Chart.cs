using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.11: the chart page every link that opens a stock opens first, its candles rolled up in the core and
// worked by hand, its averages and returns as the name page's figures, the daily chart reading no pulled bar, the page
// asking for nothing but the delayed quote, and every page's time kept in memory and drawn on the Run page.
// see: Every link that opens a stock opens its chart page first, and the chart page links to its report
// see: Each request's route and server time is kept in memory for the read surface's life and drawn on the Run page, and no store holds it
public partial class ReadSurface
{
    // The rows 19.11 adds that this check reaches: the chart page's ten and the Run page's time of every page.
    internal static readonly string[] ChartRows =
    [
        .. new[] { "Masthead", "The report line", "The interval and the levels", "The chart", "The averages", "Volume", "The marks", "Tonight's plan", "Under the pointer", "The figures" }
            .Select(row => CheckReach.Key("15.22 Chart", row)),
        CheckReach.Key("15.10 Run", "Every page's time"),
    ];

    static DateOnly Day(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static SessionBar Session(string day, decimal open, decimal high, decimal low, decimal close, long volume) =>
        new(Day(day), open, high, low, close, volume);

    // A week from Monday to Friday and a calendar month each roll their sessions into one candle, the first session's
    // open, the highest high, the lowest low, the last close and the volume summed, worked by hand over sessions on both
    // sides of a holiday Monday; a stretch of weeks holding no session is named and a month holding one is not.
    [Fact]
    public void AWeekAndAMonthRollTheirSessionsIntoOneCandleAndAStretchHoldingNoneIsNamed()
    {
        SessionBar[] series =
        [
            Session("2026-09-02", 10m, 12m, 9m, 11m, 100),
            Session("2026-09-03", 11m, 13m, 10m, 12m, 200),
            Session("2026-09-04", 12m, 12.5m, 8m, 9m, 300),
            Session("2026-09-08", 9m, 10m, 8.5m, 9.5m, 400),
            Session("2026-09-09", 9.5m, 11m, 9m, 10.5m, 500),
            Session("2026-10-01", 10.5m, 11m, 10m, 10.8m, 600),
        ];

        Assert.Equal(6, ChartSeries.Candles(series, ChartInterval.Daily).Count);

        var weeks = ChartSeries.Candles(series, ChartInterval.Weekly);

        Assert.Equal(
            [
                new Candle(Day("2026-09-02"), Day("2026-09-04"), 10m, 13m, 8m, 9m, 600),
                new Candle(Day("2026-09-08"), Day("2026-09-09"), 9m, 11m, 8.5m, 10.5m, 900),
                new Candle(Day("2026-10-01"), Day("2026-10-01"), 10.5m, 11m, 10m, 10.8m, 600),
            ],
            weeks);
        Assert.Equal([new ChartHole(Day("2026-09-09"), Day("2026-10-01"))], ChartSeries.Holes(weeks, ChartInterval.Weekly));

        var months = ChartSeries.Candles(series, ChartInterval.Monthly);

        Assert.Equal(
            [
                new Candle(Day("2026-09-02"), Day("2026-09-09"), 10m, 13m, 8m, 10.5m, 1500),
                new Candle(Day("2026-10-01"), Day("2026-10-01"), 10.5m, 11m, 10m, 10.8m, 600),
            ],
            months);
        Assert.Empty(ChartSeries.Holes(months, ChartInterval.Monthly));
        Assert.Equal(ChartInterval.Daily, ChartSeries.Read(null));
        Assert.Equal(ChartInterval.Daily, ChartSeries.Read("hourly"));
        Assert.Equal(ChartInterval.Monthly, ChartSeries.Read("monthly"));
    }

    // The pulled sessions before the store's first come to the store's scale by the ratio of the two closes on that
    // session, rounded to the store's four places with the volume as pulled, and the store's bar is read wherever it
    // holds the session.
    [Fact]
    public void ThePulledSessionsComeToTheStoresScaleByTheCloseBothHoldAndTheStoreReadsWhereItHoldsTheSession()
    {
        SessionBar[] stored = [Session("2026-09-08", 50m, 52m, 49m, 51m, 1000), Session("2026-09-09", 51m, 53m, 50m, 52m, 1100)];
        SessionBar[] pulled =
        [
            Session("2026-09-03", 98m, 104m, 96m, 100m, 10),
            Session("2026-09-04", 100.0003m, 102m, 99m, 101m, 20),
            Session("2026-09-08", 100m, 104m, 98m, 102m, 30),
        ];

        var ratio = ChartSeries.Ratio(51m, 102m);

        Assert.Equal(0.5m, ratio);

        var joined = ChartSeries.Joined(stored, pulled, ratio);

        Assert.Equal([Day("2026-09-03"), Day("2026-09-04"), Day("2026-09-08"), Day("2026-09-09")], joined.Select(bar => bar.Session));
        Assert.Equal(Session("2026-09-03", 49m, 52m, 48m, 50m, 10), joined[0]);
        Assert.Equal(50.0002m, joined[1].Open);
        Assert.Equal(stored[0], joined[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartSeries.Ratio(51m, 0m));
        Assert.Empty(ChartSeries.Joined([], pulled, ratio));
    }

    // Each span is the newest close over the close it reaches back to, less one, worked by hand: the sessions spans over
    // the stored sessions, the year to date over the last close of the year before, one year over the store's own session
    // a year back where it holds one and over the month-end closes where it does not, three years over them, and five and
    // ten years none where they reach no further.
    [Fact]
    public void EachSpanIsTheNewestCloseOverTheCloseItReachesBackToAndNoneWhereTheStoredFiguresAreShort()
    {
        // 130 weekday sessions from 2025-12-01, the close a session its place plus 100.
        var stored = new List<SessionBar>();

        for (var day = Day("2025-12-01"); stored.Count < 130; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                stored.Add(new SessionBar(day, 100m + stored.Count, 100m + stored.Count, 100m + stored.Count, 100m + stored.Count, 1));
            }
        }

        var newest = stored[^1];
        (DateOnly, decimal)[] monthEnds =
        [
            (Day("2021-04-30"), 30m),
            (Day("2022-06-30"), 40m),
            (Day("2023-04-28"), 48m),
            (Day("2023-05-31"), 50m),
            (Day("2025-05-30"), 80m),
            (Day("2025-06-30"), 85m),
        ];

        var spans = ChartSeries.Performance(stored, monthEnds).ToDictionary(span => span.Span, StringComparer.Ordinal);
        var lastOf2025 = stored.Last(bar => bar.Session.Year == 2025);

        double Over(decimal from) => (double)((newest.Close / from) - 1);

        Assert.Equal(["week", "month", "quarter", "half year", "year to date", "one year", "three years", "five years", "ten years"], ChartSeries.Performance(stored, monthEnds).Select(span => span.Span));
        Assert.Equal(Over(224m), spans["week"].Return!.Value, 12);
        Assert.Equal(stored[124].Session, spans["week"].From);
        Assert.Equal(Over(208m), spans["month"].Return!.Value, 12);
        Assert.Equal(Over(166m), spans["quarter"].Return!.Value, 12);
        Assert.Equal(Over(103m), spans["half year"].Return!.Value, 12);
        Assert.Equal(Over(lastOf2025.Close), spans["year to date"].Return!.Value, 12);
        Assert.Equal(lastOf2025.Session, spans["year to date"].From);

        // The newest session is 2026-05-29. A year back the last month ended is April 2025's, which the month-ends do not
        // hold, May 2025's ending after the day: none for one year, and not the older 2023 close standing before it.
        // Three years back April 2023's is held, 2023-04-28; five years back April 2021's, 2021-04-30; ten none.
        Assert.Equal(Day("2026-05-29"), newest.Session);
        Assert.Null(spans["one year"].Return);
        Assert.Equal(Over(48m), spans["three years"].Return!.Value, 12);
        Assert.Equal(Day("2023-04-28"), spans["three years"].From);
        Assert.Equal(Over(30m), spans["five years"].Return!.Value, 12);
        Assert.Equal(Day("2021-04-30"), spans["five years"].From);
        Assert.Null(spans["ten years"].Return);
        Assert.Equal(Over(80m), ChartSeries.Performance(stored, [.. monthEnds, (Day("2025-04-30"), 80m)]).Single(span => span.Span == "one year").Return!.Value, 12);

        // A store holding the day a year back reads one year off its own close of that session, as the store keeps a year
        // to the day, and one starting the session after it reads the month-ends.
        var year = new List<SessionBar>();

        for (var day = Day("2025-05-29"); day <= Day("2026-05-29"); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                year.Add(new SessionBar(day, 100m + year.Count, 100m + year.Count, 100m + year.Count, 100m + year.Count, 1));
            }
        }

        var reached = ChartSeries.Performance(year, monthEnds).Single(span => span.Span == "one year");
        var after = ChartSeries.Performance(year.Skip(1).ToList(), [.. monthEnds, (Day("2025-04-30"), 80m)]).Single(span => span.Span == "one year");

        Assert.Equal(Day("2025-05-29"), reached.From);
        Assert.Equal((double)((year[^1].Close / year[0].Close) - 1), reached.Return!.Value, 12);
        Assert.Equal(Day("2025-04-30"), after.From);
        Assert.Equal((double)((year[^1].Close / 80m) - 1), after.Return!.Value, 12);
    }

    // The fixture's night: AAPL's chart page draws a week worked by hand from the daily bars it rolls up, its 50-session
    // average at the newest session worked by hand from the 50 newest closes, its quarter's return worked by hand, a
    // figure the store holds none of as not stored; it asks for nothing but the delayed quote; its figures are the name
    // page's own; and the daily chart reads no pulled bar while the weekly one reads them.
    [Fact]
    public async Task TheChartPageDrawsAFixtureNamesWeekAverageAndQuarterAsWorkedByHandAndTheNamePagesFigures()
    {
        var (store, code, _, _) = await FixtureReplay.NightAsync();

        using (store)
        {
            Assert.Equal(0, code);

            var bars = new List<(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, long Volume)>();

            using (var connection = store.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT session_date, open, high, low, close, volume FROM bar WHERE ticker = 'AAPL' ORDER BY session_date;";

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    bars.Add((
                        Day(reader.GetString(0)),
                        decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                        decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                        decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                        decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                        reader.GetInt64(5)));
                }
            }

            Assert.True(bars.Count > 100, $"The fixture holds {bars.Count} of AAPL's sessions.");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var daily = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/AAPL"));
            var weekly = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/AAPL?interval=weekly"));

            // A week of the stored year worked by hand from its daily bars: a whole week whose highest high and lowest low
            // fall on neither its first session nor its last, so a candle reading either end's figures for the week's is
            // another candle.
            static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

            var week = bars
                .GroupBy(bar => Monday(bar.Session))
                .Select(days => days.ToArray())
                .First(days => days.Length == 5
                    && days.Max(bar => bar.High) is var high && days[0].High != high && days[^1].High != high
                    && days.Min(bar => bar.Low) is var low && days[0].Low != low && days[^1].Low != low);

            Assert.Contains(
                FormattableString.Invariant($"data-first=\"{week[0].Session:yyyy-MM-dd}\" data-last=\"{week[^1].Session:yyyy-MM-dd}\" data-open=\"{week[0].Open}\" data-high=\"{week.Max(bar => bar.High)}\" data-low=\"{week.Min(bar => bar.Low)}\" data-close=\"{week[^1].Close}\" data-volume=\"{week.Sum(bar => bar.Volume)}\""),
                weekly,
                StringComparison.Ordinal);

            // The 50-session average at the newest session, the mean of the 50 newest closes.
            var mean = bars.TakeLast(50).Average(bar => (double)bar.Close);
            var drawn = double.Parse(Regex.Matches(daily, "<g class=\"candle\"[^>]*data-average50=\"([^\"]+)\"")[^1].Groups[1].Value, CultureInfo.InvariantCulture);

            Assert.Equal(mean, drawn, 6);

            // The quarter's return, the newest close over the close 63 sessions back, less one.
            var quarter = double.Parse(Regex.Match(daily, "<dt>Quarter</dt><dd[^>]*data-value=\"([^\"]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);

            Assert.Equal((double)((bars[^1].Close / bars[^64].Close) - 1), quarter, 12);

            // The fixture files no fact for AAPL's filer, so its shares say not stored.
            Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM filed_fact WHERE cik = (SELECT cik FROM company WHERE ticker = 'AAPL' AND cik IS NOT NULL ORDER BY fetched_at DESC LIMIT 1);"));
            Assert.Contains($"<dt>Shares outstanding</dt><dd class=\"degraded\">{SinglePageApp.NotStored}</dd>", daily, StringComparison.Ordinal);

            // The page asks for nothing but the delayed quote: no picture or script it loads, and no route on it but the
            // quote's, which the shell's script asks inside the session alone.
            Assert.DoesNotContain(" src=\"", daily, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", daily, StringComparison.Ordinal);
            Assert.All(Regex.Matches(daily, "data-([a-z-]+)-route=").Select(match => match.Groups[1].Value), route => Assert.Equal("quote", route));

            // Its figures are the name page's: the year's high and low, the market value and the relative strength.
            var name = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAPL"));

            string Fact(string attribute) => Regex.Match(name, $"<p class=\"fact-strip\"[^>]* {attribute}=\"([^\"]+)\"").Groups[1].Value;

            string Grid(string label) => Regex.Match(daily, $"<dt>{Regex.Escape(label)}</dt><dd[^>]*data-value=\"([^\"]+)\"").Groups[1].Value;

            Assert.Equal(Fact("data-year-high"), Grid("Year's high"));
            Assert.Equal(Fact("data-year-low"), Grid("Year's low"));

            // The fixture's capture of AAPL files no market value, which the name page states as none and the chart page
            // as not stored.
            Assert.Equal("none", Fact("data-market-capitalisation"));
            Assert.Contains($"<dt>Market value, the provider's</dt><dd class=\"degraded\">{SinglePageApp.NotStored}</dd>", daily, StringComparison.Ordinal);
            Assert.Contains(
                FormattableString.Invariant($"<dt>Relative strength</dt><dd>{double.Parse(Grid("Relative strength"), CultureInfo.InvariantCulture):0.0}</dd>"),
                Regex.Match(name, "<dl class=\"facts\">.*?</dl>", RegexOptions.Singleline).Value.Replace("<div>", string.Empty, StringComparison.Ordinal).Replace("</div>", string.Empty, StringComparison.Ordinal),
                StringComparison.Ordinal);

            // The name page's masthead links back to the chart, and the chart page opens the report or offers its request.
            Assert.Contains("<a class=\"m-chart\" href=\"#/chart/AAPL\">Its chart</a>", name, StringComparison.Ordinal);
            Assert.Matches("<p class=\"report-line\" data-report=\"written\"[^>]*><a class=\"btn open-report\" href=\"#/name/AAPL\">Open the report</a>|<div class=\"report-line\" data-report=\"none\">.*<form class=\"research-control ask\" method=\"post\" action=\"/passes/AAPL\"", daily);

            // The chart page's masthead is the name page's, drawn by the one function, less the name page's link back.
            static string Mast(string page) => Regex.Match(page, "<div class=\"screen-mast\".*?<span class=\"m-asof\">.*?</span></div>", RegexOptions.Singleline).Value;

            Assert.StartsWith("<div class=\"screen-mast\" data-title=\"AAPL\"", Mast(daily), StringComparison.Ordinal);
            Assert.Equal(Mast(name).Replace(" · <a class=\"m-chart\" href=\"#/chart/AAPL\">Its chart</a>", string.Empty, StringComparison.Ordinal), Mast(daily));

            // Each volume bar in the hue of its candle's close on the one before it, worked from the stored closes, the
            // first flat.
            Assert.Equal(
                bars.Select((bar, at) => at == 0 ? "flat" : bar.Close > bars[at - 1].Close ? "up" : bar.Close < bars[at - 1].Close ? "down" : "flat"),
                Regex.Matches(daily, "<rect class=\"volume vol-([a-z]+)\"").Select(match => match.Groups[1].Value));

            // Each report and ex-dividend mark on the candle holding its session, the marks the name page's chart draws
            // over the same sessions: AAPL's four reports and four ex-dividend dates in the fixture's stored year.
            var spans = Regex.Matches(daily, "<g class=\"candle\" data-at=\"([0-9]+)\" data-first=\"([0-9-]+)\" data-last=\"([0-9-]+)\"")
                .ToDictionary(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match => (First: Day(match.Groups[2].Value), Last: Day(match.Groups[3].Value)));
            var marks = Regex.Matches(daily, "<g class=\"ev-mark (ev-[a-z]+)\" data-kind=\"[^\"]+\" data-session=\"([0-9-]+)\" data-at=\"([0-9]+)\"")
                .Select(match => (Kind: match.Groups[1].Value, Session: Day(match.Groups[2].Value), At: int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
                .ToArray();

            Assert.Equal(4, marks.Count(mark => mark.Kind == "ev-report"));
            Assert.Equal(4, marks.Count(mark => mark.Kind == "ev-dividend"));
            Assert.All(marks, mark => Assert.InRange(mark.Session, spans[mark.At].First, spans[mark.At].Last));
            Assert.Equal(
                Regex.Matches(name, "<g class=\"ev-mark (ev-[a-z]+)\" data-kind=\"[^\"]+\" data-session=\"([0-9-]+)\"").Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value}").Order(StringComparer.Ordinal),
                marks.Select(mark => FormattableString.Invariant($"{mark.Kind} {mark.Session:yyyy-MM-dd}")).Order(StringComparer.Ordinal));

            // The line above the chart reads the newest candle, worked from its stored bar, as the candle's own reading
            // carries it; and the shell's script reads the candle under the pointer and the newest once the pointer leaves.
            var raw = await client.GetStringAsync("/screens/chart/AAPL");
            var newest = bars[^1];
            var reading = Regex.Match(raw, "<p class=\"chart-reading\" aria-live=\"polite\">(.*?)</p>").Groups[1].Value;

            Assert.StartsWith(FormattableString.Invariant($"{newest.Session:yyyy-MM-dd}: open {newest.Open:#,##0.00}, high {newest.High:#,##0.00}, low {newest.Low:#,##0.00}, close {newest.Close:#,##0.00}, volume {newest.Volume:N0}"), reading, StringComparison.Ordinal);
            Assert.Equal(reading, WebUtility.HtmlDecode(Regex.Matches(raw, "<g class=\"candle\"[^>]* data-reading=\"([^\"]*)\"")[^1].Groups[1].Value));

            var shell = await client.GetStringAsync("/");

            Assert.Contains("reading.innerHTML = candle.getAttribute('data-reading');", shell, StringComparison.Ordinal);
            Assert.Contains("event.target.closest('.candle-chart .candle')", shell, StringComparison.Ordinal);

            // The night's bands behind the candles, hidden until the page's toggle shows them in the browser.
            Assert.Equal(
                Scalar(store, "SELECT COUNT(*) FROM level WHERE ticker = 'AAPL' AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = 'AAPL');"),
                long.Parse(Regex.Match(daily, "<g class=\"level-bands\" data-bands=\"([0-9]+)\">").Groups[1].Value, CultureInfo.InvariantCulture));
            Assert.Contains("data-bands-toggle aria-pressed=\"false\">Show the night's levels</button>", daily, StringComparison.Ordinal);
            Assert.Contains(".candle-chart .level-bands{display:none} .chart-fig.bands-on .candle-chart .level-bands{display:inline}", shell, StringComparison.Ordinal);
            Assert.Contains("figure.classList.toggle('bands-on', on);", shell, StringComparison.Ordinal);

            // The daily chart reads no pulled bar: with sessions pulled before the store's first, the daily chart draws
            // the stored sessions alone and the weekly chart reads the pulled ones; with the pulled bars' table renamed
            // the daily chart still answers and the weekly one does not.
            var first = bars[0];

            for (var at = 1; at <= 30; at++)
            {
                var day = first.Session.AddDays(-at);

                if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                {
                    store.Execute(FormattableString.Invariant($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('AAPL', '{day:yyyy-MM-dd}', '{first.Close}', '{first.Close}', '{first.Close}', '{first.Close}', '{first.Close}', 1000, 'history-pull-chart');"));
                }
            }

            store.Execute(FormattableString.Invariant($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('AAPL', '{first.Session:yyyy-MM-dd}', '{first.Close}', '{first.Close}', '{first.Close}', '{first.Close}', '{first.Close}', 1000, 'history-pull-chart');"));

            var dailyAgain = await client.GetStringAsync("/screens/chart/AAPL?interval=daily");
            var monthly = await client.GetStringAsync("/screens/chart/AAPL?interval=monthly");

            Assert.Equal(bars.Count, Regex.Matches(dailyAgain, "<g class=\"candle\"").Count);
            Assert.Contains(FormattableString.Invariant($"<g class=\"candle\" data-at=\"0\" data-first=\"{first.Session:yyyy-MM-dd}\""), dailyAgain, StringComparison.Ordinal);
            Assert.True(
                Day(Regex.Match(monthly, "<g class=\"candle\" data-at=\"0\" data-first=\"([0-9-]+)\"").Groups[1].Value) < first.Session,
                "The monthly chart reads the sessions pulled before the store's first.");
            Assert.Contains("the pull history-pull-chart", WebUtility.HtmlDecode(monthly), StringComparison.Ordinal);

            store.Execute("ALTER TABLE pulled_bar RENAME TO pulled_bar_kept;");

            // A route the store cannot answer either fails its request or is refused, which the host may raise instead.
            async Task<bool> Answers(string route)
            {
                try
                {
                    return (await client.GetAsync(route)).StatusCode == HttpStatusCode.OK;
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    return false;
                }
            }

            Assert.True(await Answers("/screens/chart/AAPL"), "The daily chart answers with no pulled bars' table.");
            Assert.False(await Answers("/screens/chart/AAPL?interval=weekly"), "The weekly chart reads the pulled bars' table.");

            store.Execute("ALTER TABLE pulled_bar_kept RENAME TO pulled_bar;");
        }
    }

    // Every link that opens a stock opens its chart page: no stock link on Tonight, Universe, Researched, the Queue, the
    // Watch list, Past picks or paper trading opens the name page, the shell's search opens the chart page, and the
    // report links the selected name and a row carry still open the report.
    [Fact]
    public async Task EveryLinkThatOpensAStockOpensItsChartPageAndAReportLinkItsReport()
    {
        using var store = await FixtureExpectations.WithListings();

        // A name watched, one holding a written section and one waiting in the queue, so the Watch list, Researched and
        // the Queue each draw a stock.
        store.Execute("INSERT INTO watch_list (ticker, added_at) VALUES ('AAPL', '2026-09-08T22:00:00Z');");
        store.Execute($"INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES ('MSFT', '{MarkRenderer.TheRisks}', 1, '2026-09-08', 'a writer', 'accepted', 'A sentence.', '[]', NULL);");
        store.Execute("INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES ('NFLX', '2026-09-08T22:30:00Z', 'name', 'paid', 'outstanding');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var screens = new[] { "/screens/tonight", "/screens/universe", "/screens/researched", "/screens/queue", "/screens/watch", "/screens/picks", "/screens/paper" };
        var counts = new List<string>();

        foreach (var route in screens)
        {
            var markup = WebUtility.HtmlDecode(await client.GetStringAsync(route));

            Assert.DoesNotMatch("<a class=\"(name-link|tk|nm|peer-link)\" href=\"#/name/", markup);
            counts.Add($"{route} {Regex.Matches(markup, "href=\"#/chart/[A-Z.]+[\"?]").Count}");
        }

        // Tonight's four names and Universe's four, the one each of Researched, the Queue and the Watch list hold here;
        // Past picks and paper trading hold no trade in this store, and their tests read each trade's link.
        Assert.Equal(
            ["/screens/tonight 4", "/screens/universe 4", "/screens/researched 1", "/screens/queue 1", "/screens/watch 1", "/screens/picks 0", "/screens/paper 0"],
            counts);

        var shell = await client.GetStringAsync("/");

        Assert.Contains("location.hash = '#/chart/' + encodeURIComponent(ticker);", shell, StringComparison.Ordinal);
        Assert.Contains("const response = await fetch('/screens/chart/' + ticker + (query ? '?' + query : ''));", shell, StringComparison.Ordinal);

        // The report links on a row of the list keep opening the report.
        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/tonight"));

        Assert.DoesNotMatch("<a class=\"open[^\"]*\" href=\"#/chart/", tonight);
    }

    // Tonight's plan: where a rule lists the stock on the newest night, its buy, stop and target drawn as lines across the
    // chart with the rule's name, read off the night's decision cards over the universes' store: the S&P 400's breakout
    // listing M1 at a buy of 50 and a stop of 47 with a trail and no target, the S&P 600's pullback listing S2 at 50, 48
    // and 54, and a stock no rule lists drawing no line.
    [Fact]
    public async Task TheChartPageDrawsTonightsPlanAsLinesWithTheRulesNameAndAStockNoRuleListsNone()
    {
        using var store = UniversesStore();

        // The session before the night for every member, so each chart holds the two candles it draws from.
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "SELECT ticker, '2026-10-01', '49', '50', '48', '49', 900000, 'test', '2026-10-01T21:00:00Z', '49' FROM membership;");
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('SML', '{IndexNight}', 2, 0.5, 1, '{EquityBrief.Worker.Indices.IndexFamilies.Settings("SML")}', 0);");
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('SML', '{IndexNight}', 'S2', 'pullback', 1, 1, '50', '48', '54', NULL, 20, 2.0, NULL);");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('SML', '{IndexNight}', 'S2', 'pullback', 'listed', 1, '[]', NULL, NULL, NULL);");

        await new EquityBrief.Worker.Cards.DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string[]> Lines(string ticker) =>
        [
            .. Regex.Matches(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}")), "<line class=\"plan-line plan-([a-z]+)\" data-rule=\"([^\"]+)\" data-kind=\"[a-z]+\" data-price=\"([^\"]+)\"")
                .Select(match => $"{match.Groups[1].Value} {match.Groups[3].Value} {match.Groups[2].Value}"),
        ];

        var breakout = EquityBrief.Core.Families.SetupFamilies.Named("breakout")!.Label;
        var pullback = EquityBrief.Core.Families.SetupFamilies.Named("pullback")!.Label;

        Assert.Equal([$"buy 50 {breakout}", $"stop 47 {breakout}"], await Lines("M1"));
        Assert.Equal([$"buy 50 {pullback}", $"stop 48 {pullback}", $"target 54 {pullback}"], await Lines("S2"));
        Assert.Empty(await Lines("AAA"));
    }

    // The read surface keeps each route's times in memory: the median over an odd and an even count, the newest times
    // alone once a route holds more than it keeps, the chart page's route named by its interval, and the Run page drawing
    // the slowest routes with their counts, medians and slowest times since the surface started.
    [Fact]
    public async Task TheReadSurfaceTimesEveryRequestAndTheRunPageDrawsTheSlowestSinceItStarted()
    {
        Assert.Equal(2d, RouteTimes.Median([3d, 1d, 2d]));
        Assert.Equal(2.5d, RouteTimes.Median([4d, 1d, 2d, 3d]));

        var times = new RouteTimes(FixedClock.At(new DateTimeOffset(2026, 10, 16, 7, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        for (var at = 0; at < RouteTimes.KeptARoute + 5; at++)
        {
            times.Record("/screens/tonight/{night?}", at < 5 ? 10_000d : 1d);
        }

        times.Record("/screens/chart/{ticker} weekly", 40d);
        times.Record("/screens/chart/{ticker} weekly", 60d);

        var slowest = times.Slowest();

        Assert.Equal(
            [new RouteTime("/screens/chart/{ticker} weekly", 2, 50d, 60d), new RouteTime("/screens/tonight/{night?}", RouteTimes.KeptARoute, 1d, 1d)],
            slowest);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 7, 0, 0, TimeSpan.Zero), times.Since);

        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        await client.GetStringAsync("/screens/chart/AAPL?interval=monthly");
        await client.GetStringAsync("/screens/tonight");

        var replayed = Stamp(((IClock)FixedClock.At(Instant, SessionZones.UnitedStates)).SessionDateAt(Instant));
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{replayed}"));
        var table = Regex.Match(run, "<table class=\"list-table route-times\".*?</table>", RegexOptions.Singleline).Value;

        Assert.Matches("<tr data-route=\"/screens/chart/\\{ticker\\} monthly\" data-requests=\"1\" data-median=\"[0-9.]+\" data-slowest=\"[0-9.]+\">", table);
        Assert.Matches("<tr data-route=\"/screens/tonight/\\{night\\?\\}\" data-requests=\"1\"", table);
        Assert.Contains("Every page's time", run, StringComparison.Ordinal);
    }
}
