using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Judge;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.3: the AI stock picker's facts of a constructed night worked by hand, the size bands on both sides of
// each edge, a field a member does not hold read as not on file, no name, ticker, industry or date in any text and the
// fields written the approved list's and no other; and the naming test passing and failing on each side of its bounds over
// constructed answers, the judge starting only under a passed test of its field list's version.
// see: The AI stock picker sees the fields the operator approved and nothing that names a company, a date or a price
// see: The AI stock picker starts only where the local model names no more than 2 in 100 of the companies behind a sample of its facts
public partial class FixtureExpectations
{
    // The rows of section 17 and 18 24.3 adds that this check reaches.
    internal static readonly string[] JudgeFactClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The AI stock picker's facts"),
        CheckReach.Key(Scope.LimitsTable, "The naming test"),
        CheckReach.Key(Scope.FailureTable, "A picker's fact a member does not hold, or a naming test that fails"),
    ];

    // Every row 24.3 adds: the fact writer's and the naming test's catalogue and matrix rows, their two stores, section 17's
    // two rows and section 18's row.
    internal static string[] JudgeFactRows => [.. ComponentAccess.JudgeRows, .. SchemaColumns.JudgeStoreRows, .. JudgeFactClaims];

    const string JudgeNight = "2026-11-19";

    // A night's ledger row: every reading none but those named.
    static string JudgeReadings(params (string Column, double Value)[] held)
    {
        var readings = new double?[LedgerReadings.Count];

        foreach (var (column, value) in held)
        {
            readings[LedgerReadings.IndexOf(column)] = value;
        }

        return JsonSerializer.Serialize(readings);
    }

    // The 252 weekdays ending on the night, oldest first.
    static IReadOnlyList<string> JudgeSessions()
    {
        var days = new List<DateOnly>();

        for (var day = DateOnly.ParseExact(JudgeNight, "yyyy-MM-dd", CultureInfo.InvariantCulture); days.Count < JudgeFacts.YearSessions; day = day.AddDays(-1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return [.. days.AsEnumerable().Reverse().Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))];
    }

    // A night of two members: AAA on the S&P 500 holding every source, its closes 100 but one of 150 a month before the
    // night and its highs a dollar above but one of 200; and BBB on the S&P 600 holding a ledger row alone.
    static TemporaryStore JudgeStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES "
            + $"('GSPC', '{JudgeNight}', 'AAA', '{JudgeReadings(("return_month", 0.054), ("return_quarter", 12.4), ("strength", 0.634), ("close_over_fifty", 1.024), ("close_over_long", 0.95), ("rsi", 61.6), ("volume_ratio", 1.26), ("volatility_month", 0.0234), ("surprise_percent", 8.2), ("surprise_sessions", 12), ("estimate_revision_month", 0.031), ("leverage_fall", 0.024), ("breadth", 0.524))}', 'pin'), "
            + $"('SML', '{JudgeNight}', 'BBB', '{JudgeReadings()}', 'pin');");
        store.Execute($"INSERT INTO member_reading (index_code, session_date, ticker, company_value) VALUES ('GSPC', '{JudgeNight}', 'AAA', '25000000000');");
        store.Execute($"INSERT INTO fundamental_score (index_code, session_date, ticker, financial, passed, applied, checks) VALUES ('GSPC', '{JudgeNight}', 'AAA', 0, 7, 9, '[]');");

        // The sector filed before the next morning, and one filed after it, which is not read.
        store.Execute("INSERT INTO company (ticker, fetched_at, sector) VALUES ('AAA', '2026-11-19T22:00:00Z', 'Information Technology'), ('AAA', '2026-11-20T15:00:00Z', 'Energy');");

        var sessions = JudgeSessions();
        var bars = sessions.Select((day, at) =>
        {
            var close = at == sessions.Count - 1 - JudgeFacts.MonthSessions ? "150" : "100";
            var high = at == 100 ? "200" : (decimal.Parse(close, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);

            return $"('AAA', '{day}', '{close}', '{high}', '{close}', '{close}', 1000, 'eod', '{day}T22:00:00Z')";
        });

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", bars) + ";");
        store.Execute($"INSERT INTO ladder (ticker, as_of, trend_state, plan) VALUES ('AAA', '{JudgeNight}', 'uptrend', '{{\"arithmetic\":{{\"firstReward\":\"30\",\"firstRisk\":\"10\"}}}}');");

        // Two good stories and a bad one in the 30 days, a good one before them and a good one labelled after the next
        // morning, neither read.
        store.Execute(
            "INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES "
            + "('AAA', 'a1', 'l1', 't', 's', '2026-11-18T12:00:00Z', 'x', 1, 'admitted', '2026-11-18'), "
            + "('AAA', 'a2', 'l2', 't', 's', '2026-11-10T12:00:00Z', 'x', 1, 'admitted', '2026-11-10'), "
            + "('AAA', 'a3', 'l3', 't', 's', '2026-11-01T12:00:00Z', 'x', 1, 'admitted', '2026-11-02'), "
            + "('AAA', 'a4', 'l4', 't', 's', '2026-10-10T12:00:00Z', 'x', 1, 'admitted', '2026-10-12'), "
            + "('AAA', 'a5', 'l5', 't', 's', '2026-11-19T12:00:00Z', 'x', 1, 'admitted', '2026-11-19');");
        store.Execute(
            "INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, direction, labelled_at, run_id) VALUES "
            + "('AAA', 'a1', 'local', 2, 'm', 'labelled', 'positive', '2026-11-19T04:00:00Z', 'r'), "
            + "('AAA', 'a2', 'local', 2, 'm', 'labelled', 'positive', '2026-11-11T04:00:00Z', 'r'), "
            + "('AAA', 'a3', 'local', 2, 'm', 'labelled', 'negative', '2026-11-03T04:00:00Z', 'r'), "
            + "('AAA', 'a4', 'local', 2, 'm', 'labelled', 'positive', '2026-10-13T04:00:00Z', 'r'), "
            + "('AAA', 'a5', 'local', 2, 'm', 'labelled', 'positive', '2026-11-20T15:00:00Z', 'r');");

        return store;
    }

    [Fact]
    public async Task ThePickersFactsOfANightAreWorkedByHandAndHoldNoNameTickerIndustryOrDate()
    {
        using var store = JudgeStore();
        var writer = new JudgeFactWriter(FixedClock.At(new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(2, await writer.WriteAsync(DateOnly.ParseExact(JudgeNight, "yyyy-MM-dd", CultureInfo.InvariantCulture), "judge-facts-test"));

        // AAA's short row worked by hand: the year's return 150 over 100, its close of 100 against the high of 200, its
        // reward of 30 over its risk of 10, and the two good and one bad of the stories read; from 24.24 AAA alone among the
        // S&P 500's members, so each figure it holds stands in the middle tenth, half of one member below it: 5.
        Assert.Equal(
            ["Sector: Information Technology; Size: large; Return over 1 month: +5% (tenth 5 of 10); Return over 3 months: +12% (tenth 5 of 10); Return over 12 months less the last: +50% (tenth 5 of 10); Strength against its index: above 63% of its index's stocks; Distance from its 52-week high: -50% (tenth 5 of 10); Against its 50-day average: +2% (tenth 5 of 10); Against its 200-day average: -5% (tenth 5 of 10); Momentum (RSI): 62 of 100 (tenth 5 of 10); Volume against its own average: 1.3 times (tenth 5 of 10); How much it usually moves in a day, this month: 2.3% (tenth 5 of 10); Business score: 7 of the 9 checks that apply (tenth 5 of 10); Last earnings against estimates, and days since: +8%, 12 trading days ago (tenth 5 of 10); Analysts' estimates over the last month: +3% (tenth 5 of 10)"],
            FamilyRows(store, "SELECT row_text FROM judge_fact WHERE ticker = 'AAA';"));

        var sheet = Assert.Single(FamilyRows(store, "SELECT sheet_text FROM judge_fact WHERE ticker = 'AAA';"));

        Assert.Contains("\nReward to the nearest resistance against the risk to support: 3.0 times the risk (tenth 5 of 10)\n", sheet, StringComparison.Ordinal);
        Assert.Contains("\nNews over 30 days, as the labeller read it: 2 good and 1 bad\n", sheet, StringComparison.Ordinal);
        Assert.Contains("\nDebt against assets, on a year before: debt fell 2.4 points of assets (tenth 5 of 10)\n", sheet, StringComparison.Ordinal);
        Assert.Contains("\nIts index's stocks above their 200-day average: 52%\n", sheet, StringComparison.Ordinal);

        // BBB holds a ledger row alone: every field but the news is not on file, and no figure of it is placed.
        var thin = ShownOf(Assert.Single(FamilyRows(store, "SELECT fields FROM judge_fact WHERE ticker = 'BBB';")));

        Assert.All(thin.Where(pair => pair.Key != "news"), pair => Assert.Equal(JudgeFacts.NotOnFile, pair.Value));
        Assert.Equal("0 good and 0 bad", thin["news"]);
        Assert.Equal("{}", System.Text.Json.Nodes.JsonNode.Parse(Assert.Single(FamilyRows(store, "SELECT fields FROM judge_fact WHERE ticker = 'BBB';")))![JudgeFactWriter.TenthsKey]!.ToJsonString());

        // The fields written are the approved list's and no other, on every member.
        Assert.All(FamilyRows(store, "SELECT fields FROM judge_fact;"), fields => Assert.Equal(
            JudgeFacts.All.Select(field => field.Key).Order(StringComparer.Ordinal),
            ShownOf(fields).Keys.Order(StringComparer.Ordinal)));

        // No text names a ticker, a company, an industry or a date, on every member.
        Assert.All(FamilyRows(store, "SELECT row_text || char(10) || sheet_text FROM judge_fact;"), text =>
        {
            Assert.DoesNotContain("AAA", text, StringComparison.Ordinal);
            Assert.DoesNotContain("BBB", text, StringComparison.Ordinal);
            Assert.DoesNotContain("industry", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"\b(19|20)\d\d\b|January|February|March|April|May|June|July|August|September|October|November|December", text);
        });

        // A night written again replaces its own rows.
        Assert.Equal(2, await writer.WriteAsync(DateOnly.ParseExact(JudgeNight, "yyyy-MM-dd", CultureInfo.InvariantCulture), "judge-facts-again"));
        Assert.Equal(["judge-facts-again", "judge-facts-again"], FamilyRows(store, "SELECT run_id FROM judge_fact;"));
    }

    // A member's stored fields' shown texts by key, the tenths 24.24 stores beside them left out.
    internal static IReadOnlyDictionary<string, string> ShownOf(string fields) =>
        System.Text.Json.Nodes.JsonNode.Parse(fields)!.AsObject()
            .Where(pair => pair.Value is System.Text.Json.Nodes.JsonValue)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);

    [Fact]
    public void EachSizeReadsItsBandOnBothSidesOfAnEdgeAndAShortYearReadsNone()
    {
        Assert.Equal(
            ["small", "mid-size", "mid-size", "large", "large", "very large", JudgeFacts.NotOnFile],
            new decimal?[] { 4_999_999_999m, 5_000_000_000m, 19_999_999_999m, 20_000_000_000m, 199_999_999_999m, 200_000_000_000m, null }.Select(JudgeFacts.SizeBand));

        // A year's return and high read none on a store holding fewer than the year's sessions.
        decimal[] short251 = [.. Enumerable.Repeat(100m, JudgeFacts.YearSessions - 1)];

        Assert.Null(JudgeFacts.ReturnTwelveLessOne(short251));
        Assert.Null(JudgeFacts.HighRatio(short251, short251));
        Assert.Equal(0, JudgeFacts.ReturnTwelveLessOne([.. short251, 100m]));
        Assert.Null(JudgeFacts.RewardToRisk(30, 0));
        Assert.Null(JudgeFactReader.PlanRewardToRisk("{\"arithmetic\":{\"absent\":\"no band\"}}"));
    }

    // A model that answers the naming test from the row or sheet it is shown: naming the company where the text is one of
    // those given, refusing where it is one of those given, and answering unknown otherwise.
    sealed class NamingFeed(IReadOnlySet<string> naming, IReadOnlySet<string> refusing) : IReadingModelFeed
    {
        public int Requests { get; private set; }

        public Task<bool> PrepareAsync(CancellationToken cancellation = default) => Task.FromResult(false);

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default) => Task.CompletedTask;

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            Requests++;

            var facts = request.Text[JudgeNaming.Prompt(string.Empty).Length..];

            if (refusing.Contains(facts))
            {
                throw new ReadingRefused("timed out", "no answer in time");
            }

            var ticker = naming.Contains(facts) ? Regex.Match(facts, "member (T[0-9]+)").Groups[1].Value : "unknown";

            return Task.FromResult(new ReadingReply("the model", $"{{\"company\":\"unknown\",\"ticker\":\"{ticker}\"}}", "stop", 10, 5, 0, "lm", "1", 0.1));
        }
    }

    // A night's facts of 120 members under the field list's version, each row and sheet naming its member only by a
    // marker the constructed model reads, which no real fact holds.
    static TemporaryStore NamingStore()
    {
        var store = new TemporaryStore().Migrated();
        var rows = Enumerable.Range(0, 120).Select(at => FormattableString.Invariant(
            $"('GSPC', '{JudgeNight}', 'T{at:000}', '{JudgeFacts.Version}', '{{}}', 'row of member T{at:000}', 'sheet of member T{at:000}', '2026-11-20T01:00:00Z', 'r')"));

        store.Execute("INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES " + string.Join(", ", rows) + ";");

        return store;
    }

    static async Task<JudgeNamingOutcome> NamingRun(TemporaryStore store, string run, IReadOnlySet<string> naming, IReadOnlySet<string> refusing)
    {
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 20, 2, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var settings = new LocalModelSettings("local", "http://127.0.0.1:1234/v1/", "the model", 60, 8192, 60);

        return await new JudgeNamingTest(new NamingFeed(naming, refusing), settings, clock, store.DatabaseFile, store.Root, (_, _) => Task.CompletedTask)
            .RunAsync(DateOnly.ParseExact(JudgeNight, "yyyy-MM-dd", CultureInfo.InvariantCulture), 7, clock.UtcNow.AddHours(6), run, "GSPC");
    }

    [Fact]
    public async Task TheNamingTestPassesAndFailsOnEachSideOfItsBoundsAndThePickerStartsOnlyUnderAPassedTest()
    {
        using var store = NamingStore();

        // The members the seed draws, read off the stored answers of a run in which nothing is named.
        var none = await NamingRun(store, "naming-none", new HashSet<string>(), new HashSet<string>());
        var drawn = FamilyRows(store, "SELECT ticker FROM judge_naming WHERE run_id = 'naming-none' AND round = 'row' ORDER BY rowid;");

        Assert.True(none.Passed);
        Assert.Equal(JudgeNaming.SampleSize, drawn.Count);
        Assert.Equal(drawn, JudgeNamingTest.Draw([.. Enumerable.Range(0, 120).Select(at => FormattableString.Invariant($"T{at:000}"))], 7));

        // Two of the drawn named in each round pass; three named in the sheets fail; six refused fail.
        string Row(int at) => $"row of member {drawn[at]}";
        string Sheet(int at) => $"sheet of member {drawn[at]}";

        var two = await NamingRun(store, "naming-two", new HashSet<string> { Row(0), Row(1), Sheet(0), Sheet(1) }, new HashSet<string>());

        Assert.Equal([("row", 100, 100, 2, true), ("sheet", 100, 100, 2, true)], two.Rounds.Select(round => (round.Round, round.Asked, round.Answered, round.Named, round.Passed)));
        Assert.True(two.Passed);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();
            Assert.True((await JudgeNamingTest.MayStartAsync(connection, JudgeFacts.Version, "GSPC")).May);
        }

        var three = await NamingRun(store, "naming-three", new HashSet<string> { Sheet(0), Sheet(1), Sheet(2) }, new HashSet<string>());

        Assert.False(three.Passed);
        Assert.Equal(3, three.Rounds.Single(round => round.Round == JudgeNaming.Sheet).Named);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            var (may, why) = await JudgeNamingTest.MayStartAsync(connection, JudgeFacts.Version, "GSPC");

            Assert.False(may);
            Assert.Contains("sheets 3 named of 100 answered of 100 asked", why, StringComparison.Ordinal);

            // A field list no test has run under starts no judge.
            Assert.False((await JudgeNamingTest.MayStartAsync(connection, "another0list", "GSPC")).May);
        }

        var refused = await NamingRun(store, "naming-refused", new HashSet<string>(), new HashSet<string>(Enumerable.Range(0, 6).Select(Row)));

        Assert.False(refused.Passed);
        Assert.Equal((100, 94), refused.Rounds.Single(round => round.Round == JudgeNaming.Row) is var row ? (row.Asked, row.Answered) : default);
        Assert.Equal(["timed out: no answer in time"], FamilyRows(store, "SELECT DISTINCT answer FROM judge_naming WHERE run_id = 'naming-refused' AND outcome = 'refused';"));
        Assert.True((await NamingRun(store, "naming-five-refused", new HashSet<string>(), new HashSet<string>(Enumerable.Range(0, 5).Select(Row)))).Passed);
    }

    [Fact]
    public void TheNamingQuestionSentToTheRuntimeHoldsItsAnswerToACompanyAndATickerUnderOneStructuredOutput()
    {
        // The body the feed sends wraps the naming test's schema once: an object of the company and the ticker, never a
        // structured output nested in another, which the runtime refuses.
        var request = new ReadingRequest("the model", JudgeNaming.System, JudgeNaming.Prompt("a row"), 7, JudgeNaming.AnswerTokens, JudgeNaming.Schema());

        using var body = JsonDocument.Parse(LmStudioReadingFeed.Body(request));

        var format = body.RootElement.GetProperty("response_format");
        var schema = format.GetProperty("json_schema").GetProperty("schema");

        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(["company", "ticker"], schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.False(schema.TryGetProperty("json_schema", out _));
    }

    [Fact]
    public void AnAnswerNamesTheCompanyByItsTickerOrTheFirstWordOfItsNameAndTheBoundIsTwoInAHundred()
    {
        Assert.True(JudgeNaming.Named("unknown", "aapl", "AAPL", "Apple Inc."));
        Assert.True(JudgeNaming.Named("Apple", "unknown", "AAPL", "Apple Inc."));
        Assert.True(JudgeNaming.Named("The Coca-Cola Company", null, "KO", "Coca-Cola Co"));
        Assert.True(JudgeNaming.Named("unknown", "BRK.B", "BRK-B", "Berkshire Hathaway Inc."));
        Assert.False(JudgeNaming.Named("Microsoft", "MSFT", "AAPL", "Apple Inc."));
        Assert.False(JudgeNaming.Named("unknown", "unknown", "KO", "Coca-Cola Co"));
        Assert.False(JudgeNaming.Named("a holding company", null, "BRK-B", "Berkshire Hathaway Inc."));

        Assert.True(JudgeNaming.Passed(2, 100));
        Assert.False(JudgeNaming.Passed(3, 100));
        Assert.False(JudgeNaming.Passed(0, 0));
    }
}
