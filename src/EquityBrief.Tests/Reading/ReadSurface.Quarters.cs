using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Quarters;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Quarters;

namespace EquityBrief.Tests.Reading;

// read-surface: the state a member's reported quarters give it and where it is drawn, read off the rendered
// pages over constructed stores: tonight's list drawn state first from the first night whose readings are
// stored and in the filter's own order before it, the word beside the trend with what the numbers say inside
// it, the name page's numbers opening with what the numbers say, section 4's patterns held to the renderer's,
// the quarters step's line on the run page, and Past picks' state and order.
// see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
// see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
public partial class ReadSurface
{
    // The screens' claims the fundamentals item lands, which this check reaches and the phase 12 pair names.
    internal static string[] FundamentalsScreenClaims =>
    [
        CheckReach.Key("15.7 Tonight", "The list, the state-first order from the first night whose readings are stored and a night before it in the filter's own order"),
        CheckReach.Key("15.7 Tonight", "The list, the state its reported quarters give it beside the trend"),
        CheckReach.Key("15.7 Tonight", "The list, what the numbers say while the state is under the pointer or holds focus"),
        CheckReach.Key("15.9 Name", "What the numbers say, a heading carrying the state and the quarter it was read from"),
        CheckReach.Key("15.9 Name", "What the numbers say, one sentence per reading written from section 4's patterns"),
        CheckReach.Key("15.9 Name", "What the numbers say, a sentence saying so where a reading is absent or read over too few quarters"),
        CheckReach.Key("15.9 Name", "What the numbers say, the quarters the readings read with the dates each was filed and reported on"),
        CheckReach.Key("15.9 Name", "What the numbers say, the full numbers table folded beneath them"),
        CheckReach.Key("15.17 Past picks", "Every trade, within a night the order that night's list was drawn in, improving businesses first where it stored readings and the filter's own order where it stored none"),
        CheckReach.Key("15.17 Past picks", "Every trade, the state its reported quarters gave it on the night it was listed, or not read that night"),
    ];

    const string BeforeTheReadings = "2026-10-01";
    const string WithTheReadings = "2026-10-02";

    // The readings of each state, over constructed quarters: every figure given, the margin two points wider
    // and sales ten per cent up in each quarter unless the reading says otherwise.
    static Readings Improving() => QuarterReadings.Of(FixtureExpectations.Quarters(4), null, null);

    static Readings SteadyAwaiting() =>
        QuarterReadings.Of([FixtureExpectations.Quarter(0, margin: 0.27m), .. FixtureExpectations.Quarters(4)[1..]], null, new DateOnly(2026, 9, 30));

    static Readings Deteriorating() =>
        QuarterReadings.Of(
            [
                FixtureExpectations.Quarter(0, growth: -0.02m, margin: 0.20m, earlier: 0.25m),
                FixtureExpectations.Quarter(1, growth: -0.01m, margin: 0.22m, earlier: 0.23m),
                .. FixtureExpectations.Quarters(4)[2..],
            ],
            null,
            null);

    static Readings TooFewQuarters() => QuarterReadings.Of([FixtureExpectations.Quarter(0)], null, null);

    static Readings NoQuarter() => QuarterReadings.Of([], null, null);

    static string Stamp(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null";

    static void StoreReading(TemporaryStore store, string session, string ticker, Readings readings, string? fetchedAt = null) =>
        store.Execute(
            "INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) VALUES " +
            $"('{ticker}', '{session}', '{readings.State}', {Quoted(readings.ReadFrom is { } from ? Stamp(from) : null)}, {Quoted(fetchedAt)}, " +
            $"{Quoted(readings.Awaited is { } awaited ? Stamp(awaited) : null)}, '{readings.ToJson().Replace("'", "''", StringComparison.Ordinal)}');");

    // Two evenings the swing filter listed, the same five passing in the filter's order Q1 to Q5 on each, and
    // the readings stored on the second alone: Q1 deteriorating, Q2 steady with a newer quarter awaited, Q3
    // holding no quarter, Q4 improving and Q5 holding too few.
    static TemporaryStore ReadingsStore()
    {
        var store = new TemporaryStore().Migrated();

        Member[] five = [.. new[] { "Q1", "Q2", "Q3", "Q4", "Q5" }.Select((ticker, at) => new Member(ticker, Passed: true, Rank: at + 1, RewardToRisk: 3.0 - (0.2 * at)))];

        Evening(store, BeforeTheReadings, filter: true, five);
        Evening(store, WithTheReadings, filter: true, five);

        StoreReading(store, WithTheReadings, "Q1", Deteriorating());
        StoreReading(store, WithTheReadings, "Q2", SteadyAwaiting());
        StoreReading(store, WithTheReadings, "Q3", NoQuarter());
        StoreReading(store, WithTheReadings, "Q4", Improving(), "2026-09-15T23:40:00.000Z");
        StoreReading(store, WithTheReadings, "Q5", TooFewQuarters());

        return store;
    }

    // Q4's sentences, worked by hand from its quarters: sales ten per cent up in both, the same quarter a
    // year before five, the margin 30 against 28 in all four quarters, four quarters each meeting its
    // estimate, cash of 440 against profit of 400, and four multiples where a range needs eight.
    static readonly string[] ImprovingSentences =
    [
        "Sales rose 10.0% on a year earlier in the quarter to 2026-06-30, and rose 10.0% in the quarter to 2026-03-31.",
        "The newer quarter's growth was faster than the +5.0% the same quarter showed a year before.",
        "The operating margin was 30.0% against 28.0% a year earlier, wider for 4 quarter(s) in a row.",
        "Against the analysts' estimates over its last 4 quarters it beat 0, met 4 and missed 0, met meaning within a cent or 1% of the estimate.",
        "Operating cash flow over the last four quarters was 1.10 times net income: in line.",
        "4 of the 8 quarters a range needs carry a multiple, so the valuation position is not read.",
    ];

    [Fact]
    public async Task TonightsListDrawsImprovingBusinessesFirstFromTheFirstNightItsReadingsAreStoredAndTheFiltersOwnOrderBefore()
    {
        using var store = ReadingsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var withReadings = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{WithTheReadings}"));
        var before = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{BeforeTheReadings}"));
        var list = Assert.Single(Blocks(withReadings, "<section class=\"tonight-list\".*?</section>"));
        var earlier = Assert.Single(Blocks(before, "<section class=\"tonight-list\".*?</section>"));

        // Worked by hand: improving Q4, steady Q2, the two reading no state in the filter's order, Q3 then Q5,
        // and deteriorating Q1 last, whatever the filter's own rank; the night before the readings keeps the
        // filter's own order over the same gate rows.
        Assert.Equal(["Q4", "Q2", "Q3", "Q5", "Q1"], DrawnTickers(list));
        Assert.Equal(["Q1", "Q2", "Q3", "Q4", "Q5"], DrawnTickers(earlier));
        Assert.Contains("improving businesses first, then steady, then the names whose quarters read no state, then deteriorating", withReadings, StringComparison.Ordinal);
        Assert.Contains("in the filter's order, the trade's reward to risk first, then strength, then band strength", before, StringComparison.Ordinal);

        // Each row's state word stands in its trend cell as the night stored it, and the night before the
        // readings draws none.
        foreach (var (ticker, state) in new[] { ("Q1", FundamentalState.Deteriorating), ("Q2", FundamentalState.Steady), ("Q3", FundamentalState.NoFundamentalsYet), ("Q4", FundamentalState.Improving), ("Q5", FundamentalState.NotEnoughQuarters) })
        {
            Assert.Matches($"<td class=\"trend-state\">[^<]*<span class=\"business\" tabindex=\"0\" data-state=\"{Regex.Escape(state)}\">{Regex.Escape(state)}<span class=\"says\" role=\"tooltip\">", RowOf(list, ticker));
        }

        Assert.DoesNotContain("class=\"business\"", earlier, StringComparison.Ordinal);

        // What the numbers say, inside the word: Q4's sentences word for word, and Q3's saying it holds none.
        var says = Regex.Match(RowOf(list, "Q4"), "<span class=\"says\" role=\"tooltip\">(.*?)</span></span>").Groups[1].Value;

        Assert.Equal(string.Join(" ", ImprovingSentences), says);
        Assert.Contains("<span class=\"says\" role=\"tooltip\">No reported quarter is stored for it yet, so nothing is read.</span>", RowOf(list, "Q3"), StringComparison.Ordinal);

        // Shown while the word is under the pointer or holds focus, and hidden otherwise.
        Assert.Contains(".business .says{display:none;", EquityBrief.Web.App.Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".business:hover .says,.business:focus .says,.business:focus-within .says{display:block}", EquityBrief.Web.App.Stylesheet.Css, StringComparison.Ordinal);
    }

    // The name page's block of what the numbers say, from its opening tag to the folded part's close.
    static string NumbersSayOf(string page) => Assert.Single(Blocks(page, "<section class=\"numbers-say\".*?</details></section>"));

    static IReadOnlyList<string> SaidIn(string block) =>
        [.. Regex.Matches(Regex.Match(block, "<ul class=\"says\">(.*?)</ul>", RegexOptions.Singleline).Groups[1].Value, "<li>(.*?)</li>").Select(match => match.Groups[1].Value)];

    [Fact]
    public async Task TheNamePageOpensItsNumbersWithWhatTheNumbersSayWordForWord()
    {
        using var store = ReadingsStore();

        // The fetch Q4's readings read: its four quarters, and a fifth older one no reading read. The quarter
        // to 2025-12-31 carries no filing date.
        foreach (var (period, filed, reported) in new (string, string?, string?)[]
        {
            ("2026-06-30", "2026-07-30", "2026-07-25"), ("2026-03-31", "2026-04-30", "2026-04-25"),
            ("2025-12-31", null, "2026-01-25"), ("2025-09-30", "2025-10-30", "2025-10-25"), ("2025-06-30", "2025-07-30", "2025-07-25"),
        })
        {
            store.Execute(
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, report_date) VALUES " +
                $"('Q4', '2026-09-15T23:40:00.000Z', '2026-09-15', '{period}', {Quoted(filed)}, {Quoted(reported)});");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Q4: the heading carrying the state, the quarter read from beneath it, the six sentences worked by
        // hand, each one of section 4's patterns, and folded beneath them the four quarters read with the dates
        // each was filed and reported on and then the full numbers.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Q4/{WithTheReadings}"));
        var block = NumbersSayOf(page);

        Assert.StartsWith($"<section class=\"numbers-say\" data-state=\"{FundamentalState.Improving}\" data-read-from=\"2026-06-30\"><h4 class=\"says-heading\">What the numbers say: improving, read from sales and operating margin alone</h4>", block, StringComparison.Ordinal);
        Assert.Contains("<p class=\"read-from\">Read from the quarter to 2026-06-30.</p>", block, StringComparison.Ordinal);
        Assert.Equal(ImprovingSentences, SaidIn(block));
        Assert.All(SaidIn(block), sentence => Assert.NotNull(NumbersSay.PatternOf(sentence)));

        Assert.Equal(
            ["2026-06-30|2026-07-30|2026-07-25", "2026-03-31|2026-04-30|2026-04-25", "2025-12-31|not filed|2026-01-25", "2025-09-30|2025-10-30|2025-10-25"],
            Regex.Matches(block, "<tr data-quarter=\"([^\"]+)\"><td class=\"num\">[^<]+</td><td class=\"num\">(?:<span class=\"degraded\">)?([^<]+)(?:</span>)?</td><td class=\"num\">(?:<span class=\"degraded\">)?([^<]+)(?:</span>)?</td></tr>")
                .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}"));

        var folded = block.IndexOf("<details class=\"numbers-behind\">", StringComparison.Ordinal);

        Assert.True(folded > block.IndexOf("</ul>", StringComparison.Ordinal), "The folded part does not stand beneath the sentences.");
        Assert.True(block.IndexOf("<section class=\"numbers\"", StringComparison.Ordinal) > folded, "The numbers are not inside the folded part.");

        // Q2: a newer quarter awaited, which the first sentence names beside the quarter the readings read.
        Assert.Equal(
            "The quarter to 2026-09-30 is awaited, so these read the quarter to 2026-06-30.",
            SaidIn(NumbersSayOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Q2/{WithTheReadings}"))))[0]);

        // Q5: every reading read over too few quarters says so, word for word.
        var tooFew = NumbersSayOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Q5/{WithTheReadings}")));

        Assert.Contains("<h4 class=\"says-heading\">What the numbers say: not enough quarters, read from sales and operating margin alone</h4>", tooFew, StringComparison.Ordinal);
        Assert.Equal(
            [
                "Sales and the operating margin cannot be read against a year earlier over the two newest quarters.",
                "1 of the 4 quarters a record needs carry both an actual and an estimate, so no record is stated.",
                "The last four quarters do not all carry operating cash flow and net income, so earnings quality is not read.",
                "1 of the 8 quarters a range needs carry a multiple, so the valuation position is not read.",
            ],
            SaidIn(tooFew));

        // Q3: no quarter stored, one sentence saying so and no quarter to have read from.
        var none = NumbersSayOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Q3/{WithTheReadings}")));

        Assert.Equal(["No reported quarter is stored for it yet, so nothing is read."], SaidIn(none));
        Assert.Contains("data-read-from=\"none\"", none, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"read-from\"", none, StringComparison.Ordinal);

        // A night before the readings draws the numbers as they were, with no summary above them.
        var earlier = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/Q4/{BeforeTheReadings}"));

        Assert.DoesNotContain("class=\"numbers-say\"", earlier, StringComparison.Ordinal);
        Assert.Contains("<section class=\"numbers\"", earlier, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionFoursPatternTableAndTheRenderersPatternsAreOneListInBothDirections()
    {
        // Held word for word and in order, so a pattern the renderer writes and the document does not state
        // fails, and one the document states and nothing writes fails the same way.
        var table = Assert.Single(
            ArchitectureTables.In(File.ReadAllText(Repository.Architecture)),
            candidate => candidate.Heading == "What the numbers say, pattern by pattern");

        Assert.Equal(
            NumbersSay.Patterns.Select(entry => $"{entry.Name} | {entry.Pattern}"),
            table.Body.Select(row => $"{row[0]} | {row[1]}"));
        Assert.Equal(27, NumbersSay.Patterns.Count);

        // The heading pattern is the one the name page draws.
        Assert.Equal("What the numbers say: steady, read from sales and operating margin alone", NumbersSay.Heading(FundamentalState.Steady));
    }

    [Fact]
    public async Task TheRunPageCountsTheNightsQuarterAsksOnTheStepsLineAndDrawsNoRegionOfThem()
    {
        using var store = ReadingsStore();

        // The night's quarters step as its row holds it: Q2 asked the night after its report and its quarter
        // not yet posted, Q3 asked again and answered with nothing, Q4 filled, and one member of the fill
        // still owed. Worked by hand: three asked of three due, one each reporting, waiting and filled, one
        // each stored, not yet posted and returning nothing, twelve quarters and 31 weighted calls.
        var outcome = new QuartersOutcome(
            5,
            3,
            [
                new QuarterAsked("Q2", QuarterFetcher.Report, new DateOnly(2026, 9, 30), QuarterFetcher.NotYetPosted, 0, 10, "the answer's newest quarter ends 2026-06-30"),
                new QuarterAsked("Q3", QuarterFetcher.Waiting, null, QuarterFetcher.NothingReturned, 0, 10, null, 2),
                new QuarterAsked("Q4", QuarterFetcher.Fill, null, QuarterFetcher.Stored, 12, 11, null),
            ],
            0,
            0,
            1,
            4,
            31);
        var line = QuarterFetcher.Detail(outcome);

        Assert.Equal(
            "3 of 3 member(s) due asked: 1 reporting, 1 waiting, 0 joining, 1 filled; 1 stored, 1 not yet posted, 1 returning nothing, 0 refused; 12 quarter row(s), 31 weighted call(s); 1 member(s) of the fill still owed",
            line);

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            $"('night-quarters', '{QuarterFetcher.Stage}', '{WithTheReadings}T23:45:00.000Z', '{WithTheReadings}T23:46:00.000Z', 'ok', 12, 0, 4, '0', '{line}');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{WithTheReadings}"));

        // The step's line stands in the operational header, whole, and the page draws no region of the asks.
        Assert.Contains(line, page, StringComparison.Ordinal);
        Assert.DoesNotContain("The reported quarters the night asked for", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"fundamentals\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("The fill:", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PastPicksDrawsEachTradesStateOnItsListingNightAndEachNightInTheOrderItWasDrawnIn()
    {
        using var store = ReadingsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        // Newest night first; within the night the readings were stored the state-first order, and within the
        // night before them the filter's own.
        Assert.Equal(
            [
                ("Q4", WithTheReadings), ("Q2", WithTheReadings), ("Q3", WithTheReadings), ("Q5", WithTheReadings), ("Q1", WithTheReadings),
                ("Q1", BeforeTheReadings), ("Q2", BeforeTheReadings), ("Q3", BeforeTheReadings), ("Q4", BeforeTheReadings), ("Q5", BeforeTheReadings),
            ],
            Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\" data-night=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));

        // Each trade's state on its listing night, and the night before the readings not read.
        Assert.Contains("<td class=\"state-then\" data-state=\"improving\">improving</td>", PickRowOf(page, "Q4", WithTheReadings), StringComparison.Ordinal);
        Assert.Contains("<td class=\"state-then\" data-state=\"deteriorating\">deteriorating</td>", PickRowOf(page, "Q1", WithTheReadings), StringComparison.Ordinal);
        Assert.Contains("<td class=\"state-then\" data-state=\"no fundamentals yet\">no fundamentals yet</td>", PickRowOf(page, "Q3", WithTheReadings), StringComparison.Ordinal);

        foreach (var ticker in new[] { "Q1", "Q2", "Q3", "Q4", "Q5" })
        {
            Assert.Contains($"<td class=\"state-then\" data-state=\"none\"><span class=\"degraded\">{MarkRenderer.NotReadThatNight}</span></td>", PickRowOf(page, ticker, BeforeTheReadings), StringComparison.Ordinal);
        }

        Assert.Contains("<th>Business that night</th>", page, StringComparison.Ordinal);
    }
}
