using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.EntryCheck;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// The entry check's roster rows, one a reading, a fix, the wait plan and the card's line, over seven stocks of a real night copied whole
// from the store into a committed fixture. Each expected value was worked by hand from the order's rule text before the code ran, by a
// script sharing no code with the tool, and every figure is from the store. The reader runs over the fixture's rows as a night runs it.
// see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
public class EntryCheckFixture
{
    const int CaseCount = 7;

    static readonly string Folder = Path.Combine(Repository.Root, "fixtures", "entry-check-2026-11-04");

    static readonly JsonElement Cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "expectations", "entry-check.json"))).RootElement.GetProperty("cases");

    static readonly Lazy<Task<IReadOnlyDictionary<string, EntryReading>>> Read = new(ReadAsync);

    static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    static string? TextOrNone(JsonElement element, string name) =>
        element.TryGetProperty(name, out var said) && said.ValueKind == JsonValueKind.String ? said.GetString() : null;

    static decimal? Price(JsonElement element, string name) =>
        TextOrNone(element, name) is { } said ? decimal.Parse(said, CultureInfo.InvariantCulture) : null;

    static DateOnly? Day(JsonElement element, string name) =>
        TextOrNone(element, name) is { } said ? DateOnly.ParseExact(said, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    static async Task<IReadOnlyDictionary<string, EntryReading>> ReadAsync()
    {
        using var store = new TemporaryStore().Migrated();

        await using (var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile)))
        {
            await connection.OpenAsync();

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

            foreach (var one in Cases.EnumerateObject())
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Folder, one.Name + ".json")));

                await LoadAsync(connection, transaction, document.RootElement);
            }

            await transaction.CommitAsync();
        }

        var outcome = await new EntryReader(FixedClock.At(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("fixture");

        Assert.Equal((new DateOnly(2026, 11, 4), CaseCount, 0), (outcome.Night!.Value, outcome.Read, outcome.WithoutABar));

        return await Reading.ReadSurface.ApiOf(store).EntryReadingsOnAsync(outcome.Night.Value);
    }

    static async Task LoadAsync(SqliteConnection connection, SqliteTransaction transaction, JsonElement row)
    {
        var ticker = Text(row, "ticker");
        var session = Text(row, "session");

        async Task Insert(string sql, params (string Name, object? Value)[] values)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$ticker", ticker);

            foreach (var (name, value) in values)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync();
        }

        static object? Of(JsonElement element, string name) => element.GetProperty(name) switch
        {
            { ValueKind: JsonValueKind.String } said => said.GetString(),
            { ValueKind: JsonValueKind.Number } said when said.TryGetInt64(out var whole) => whole,
            { ValueKind: JsonValueKind.Number } said => said.GetDouble(),
            _ => null,
        };

        await Insert("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', $ticker, NULL, NULL, '2026-11-04T00:00:00Z');");

        foreach (var bar in row.GetProperty("bars").EnumerateArray())
        {
            await Insert(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $session, $open, $high, $low, $close, $volume, 'fixture', '2026-11-04T00:00:00Z', $raw);",
                ("$session", Of(bar, "session_date")), ("$open", Of(bar, "open")), ("$high", Of(bar, "high")), ("$low", Of(bar, "low")), ("$close", Of(bar, "close")), ("$volume", Of(bar, "volume")), ("$raw", Of(bar, "raw_close")));
        }

        foreach (var bar in row.GetProperty("pulled").EnumerateArray())
        {
            await Insert(
                "INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ($ticker, $session, $open, $high, $low, $close, $raw, $volume, $pull);",
                ("$session", Of(bar, "session_date")), ("$open", Of(bar, "open")), ("$high", Of(bar, "high")), ("$low", Of(bar, "low")), ("$close", Of(bar, "close")), ("$raw", Of(bar, "raw_close")), ("$volume", Of(bar, "volume")), ("$pull", Of(row, "pull")));
        }

        foreach (var one in row.GetProperty("indicators").EnumerateArray())
        {
            await Insert(
                "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ($ticker, $session, $name, $value, $count);",
                ("$session", session), ("$name", Of(one, "name")), ("$value", Of(one, "value")), ("$count", Of(one, "bar_count")));
        }

        foreach (var one in row.GetProperty("levels").EnumerateArray())
        {
            await Insert(
                "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) VALUES ($ticker, $as_of, $low, $high, $role, $immediate, $strength, $anchored, '[]');",
                ("$as_of", Of(row, "levels_as_of")), ("$low", Of(one, "low_edge")), ("$high", Of(one, "high_edge")), ("$role", Of(one, "role")), ("$immediate", Of(one, "immediate")), ("$strength", Of(one, "strength")), ("$anchored", Of(one, "has_non_average_anchor")));
        }

        foreach (var one in row.GetProperty("swings").EnumerateArray())
        {
            await Insert(
                "INSERT INTO swing (ticker, session_date, direction, price, confirmed_on) VALUES ($ticker, $session, $direction, $price, $confirmed);",
                ("$session", Of(one, "session_date")), ("$direction", Of(one, "direction")), ("$price", Of(one, "price")), ("$confirmed", Of(one, "confirmed_on")));
        }

        foreach (var one in row.GetProperty("calendar").EnumerateArray())
        {
            await Insert(
                "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ($ticker, $on, $kind, $timing, $detail, $observed);",
                ("$on", Of(one, "event_date")), ("$kind", Of(one, "kind")), ("$timing", Of(one, "timing")), ("$detail", Of(one, "detail")), ("$observed", Of(one, "observed_at")));
        }

        await Insert("INSERT INTO company (ticker, fetched_at, cik) VALUES ($ticker, '2026-11-04T00:00:00Z', $cik);", ("$cik", Of(row, "cik")));

        if (row.GetProperty("release") is { ValueKind: JsonValueKind.Object } release)
        {
            await Insert(
                "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id, release) VALUES ($ticker, $cik, '8-K', $filed, $accepted, 'e', 'u', $text, 's', 1, 'fixture', 1);",
                ("$cik", Of(row, "cik")), ("$filed", Text(release, "accepted_at")[..10]), ("$accepted", Of(release, "accepted_at")), ("$text", ResultsReleases.Compressed(Text(release, "text")).Gzip));
        }
    }

    public static TheoryData<string> Tickers()
    {
        var data = new TheoryData<string>();

        foreach (var one in Cases.EnumerateObject())
        {
            data.Add(one.Name);
        }

        return data;
    }

    static async Task<(EntryReading Reading, JsonElement Expected)> CaseAsync(string ticker) =>
        ((await Read.Value)[ticker], Cases.GetProperty(ticker));

    [Fact]
    public void TheFixtureHoldsTheCasesTheOrderNames()
    {
        Assert.Equal(CaseCount, Cases.EnumerateObject().Count());

        // SWK gapped and its release names an item; SN has no gap; AIZ's release matches no phrase; COR's targets predate its report.
        Assert.False(Cases.GetProperty("SN").GetProperty("gapHigh").ValueKind == JsonValueKind.String);
        Assert.True(Cases.GetProperty("SWK").GetProperty("oneOff").ValueKind == JsonValueKind.Object);
        Assert.True(Cases.GetProperty("AIZ").GetProperty("oneOff").ValueKind == JsonValueKind.Null);
        Assert.StartsWith("2026-11-03", Text(Cases.GetProperty("COR"), "analystAnswered"), StringComparison.Ordinal);
        Assert.Equal("2026-11-04", Text(Cases.GetProperty("COR"), "report"));

        // The manifest declares every case, and no credential is in any of them.
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "manifest.json")));

        Assert.Equal(
            Cases.EnumerateObject().Select(one => one.Name + ".json").Order(StringComparer.Ordinal),
            manifest.RootElement.GetProperty("inputs").EnumerateArray().Select(one => Text(one, "file")).Order(StringComparer.Ordinal));
    }

    // entry-stretch
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheStretchReadsAsWorkedByHand(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);

        Assert.Equal(decimal.Parse(Text(expected, "close"), CultureInfo.InvariantCulture), reading.Close);
        Assert.Equal(expected.GetProperty("typicalDay").GetDouble(), reading.TypicalDay!.Value, 0.000001);
        Assert.Equal(expected.GetProperty("stretchPercent").GetDouble(), reading.StretchPercent!.Value, 0.001);
        Assert.Equal(expected.GetProperty("stretchDays").GetDouble(), reading.StretchDays!.Value, 0.001);
        Assert.Equal(expected.GetProperty("stretchShare").GetDouble(), reading.StretchShare!.Value, 0.001);
    }

    // entry-hot-run
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheHotRunReadsAsWorkedByHand(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);

        Assert.Equal(expected.GetProperty("gainTwenty").GetDouble(), reading.GainTwenty!.Value, 0.001);
        Assert.Equal(expected.GetProperty("rsi").GetDouble(), reading.Rsi!.Value, 0.000001);
        Assert.Equal(expected.GetProperty("volumeRatio").GetDouble(), reading.VolumeRatio!.Value, 0.001);
        Assert.Equal(expected.GetProperty("largestGain").GetBoolean(), reading.LargestGain);
    }

    // entry-gap
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheGapReadsAsWorkedByHand(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);

        Assert.Equal((Price(expected, "gapHigh"), Price(expected, "gapLow")), (reading.GapHigh, reading.GapLow));
    }

    [Fact]
    public async Task SwksGapAndItsDriftStopReadAsWorkedByHand()
    {
        var (reading, _) = await CaseAsync("SWK");

        // The drift's stop of 110.04 is the night's low, the gap's top: 8.29 under the buy of 118.33 at a typical day of 3.9764 is 2.1 days.
        Assert.Equal(
            "Tonight's low of $110.04 is above the prior session's high of $107.13, a gap of $2.92. The earnings drift rule's stop is $110.04, 2.1 typical days under the buy. A fall back into the gap would reach it.",
            EntryWords.Gap(reading, 118.33m, 110.04m, "earnings drift"));
    }

    // entry-highest-close
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheHighestCloseReadsAsWorkedByHand(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);

        Assert.Equal((Day(expected, "highestSince"), Day(expected, "historyStart")), (reading.HighestSince, reading.HistoryStart));
    }

    // entry-older-resistance
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheOlderResistanceReadsAsWorkedByHand(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);

        if (Price(expected, "olderLow") is not { } low)
        {
            Assert.Null(reading.OlderLow);
            Assert.Equal("Older resistance: " + EntryWords.NoOlderBand + ".", EntryWords.Older(reading));

            return;
        }

        Assert.InRange(reading.OlderLow!.Value, low - 0.001m, low + 0.001m);
        Assert.InRange(reading.OlderHigh!.Value, Price(expected, "olderHigh")!.Value - 0.001m, Price(expected, "olderHigh")!.Value + 0.001m);
        Assert.Equal(Day(expected, "olderOn"), reading.OlderOn);
        Assert.Equal(expected.GetProperty("olderDays").GetDouble(), reading.OlderDays!.Value, 0.001);
    }

    // entry-one-off
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheOneOffReadsAsAPersonReadTheRelease(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);

        Assert.Equal(Day(expected, "releaseOn"), reading.ReleaseOn);

        if (expected.GetProperty("oneOff") is not { ValueKind: JsonValueKind.Object } item)
        {
            Assert.Null(reading.OneOffAmount);
            Assert.Contains(EntryWords.NoItemFound, EntryWords.OneOff(reading), StringComparison.Ordinal);

            return;
        }

        Assert.Equal(Price(item, "amount"), reading.OneOffAmount);
        Assert.Equal(item.GetProperty("tied").GetBoolean(), reading.OneOffTied);
        Assert.Equal(item.GetProperty("direction").GetInt32(), reading.OneOffDirection);
        Assert.Equal(Price(item, "beat"), reading.Beat);
        Assert.Contains(Text(item, "sentence"), EntryWords.OneOff(reading), StringComparison.Ordinal);
    }

    // entry-wait-plan
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheWaitPlanReadsAsWorkedByHand(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);
        var plan = expected.GetProperty("wait");

        Assert.Equal(TextOrNone(plan, "why"), reading.WaitWhy);
        Assert.Equal((Price(plan, "low"), Price(plan, "high"), Price(plan, "stop"), Price(plan, "exit")), (reading.WaitLow, reading.WaitHigh, reading.WaitStop, reading.WaitExit));

        if (plan.TryGetProperty("zone_days", out var days))
        {
            Assert.Equal(days.GetDouble(), reading.ZoneDays!.Value, 0.001);
        }

        if (plan.TryGetProperty("reward", out var reward) && reward.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(reward.GetDouble(), reading.WaitReward!.Value, 0.001);
        }
        else
        {
            Assert.Null(reading.WaitReward);
        }

        Assert.StartsWith(EntryWords.Provisional, EntryWords.Wait(reading), StringComparison.Ordinal);
    }

    // entry-card-line
    [Theory]
    [MemberData(nameof(Tickers))]
    public async Task TheCardsLineReadsAsWorkedByHand(string ticker)
    {
        var (reading, expected) = await CaseAsync(ticker);

        Assert.Equal(Text(expected, "cardLine"), EntryWords.CardLine(reading));
        Assert.Contains(Text(expected, "cardLine"), System.Net.WebUtility.HtmlDecode(MarkRenderer.EntryCheckLine(reading, ticker)), StringComparison.Ordinal);
    }

    // entry-overhead-fix
    [Fact]
    public async Task ASwkWithNoResistanceInTheYearNamesItsOlderBand()
    {
        var (reading, expected) = await CaseAsync("SWK");
        var drawn = System.Net.WebUtility.HtmlDecode(MarkRenderer.OlderBandsTable(
            [new Core.Report.OlderBand("resistance", reading.OlderLow!.Value, reading.OlderHigh!.Value, reading.OlderOn!.Value)],
            reading.Close));

        Assert.Equal(new DateOnly(2019, 9, 12), Day(expected, "olderOn"));
        Assert.Contains("Prices above this traded on 2019-09-12. The plan's levels read one year, so they do not use these.", drawn, StringComparison.Ordinal);
        Assert.Contains("from 2019.", drawn, StringComparison.Ordinal);
    }

    // entry-stale-fix
    [Fact]
    public void AnAnalystsAnswerFromBeforeTheReportSaysSoWithItsDay()
    {
        static DateOnly Answered(string ticker) => DateOnly.ParseExact(Text(Cases.GetProperty(ticker), "analystAnswered")[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // COR's answer of 2026-11-03 predates its report of 2026-11-04, and SWK's of 2026-11-05 does not predate its report of 2026-11-04.
        Assert.Contains("Set before the 2026-11-04 report", MarkRenderer.SetBefore(Answered("COR"), Day(Cases.GetProperty("COR"), "report")), StringComparison.Ordinal);
        Assert.Contains("fetched on 2026-11-03, before the 2026-11-04 report", MarkRenderer.FetchedBefore(Answered("COR"), Day(Cases.GetProperty("COR"), "report")), StringComparison.Ordinal);
        Assert.Empty(MarkRenderer.SetBefore(Answered("SWK"), Day(Cases.GetProperty("SWK"), "report")));
        Assert.Empty(MarkRenderer.FetchedBefore(Answered("SWK"), Day(Cases.GetProperty("SWK"), "report")));
    }

    // Five pullbacks listed on the 21st of 30 sessions, a benchmark of 0.875.
    // AAA closes that night at 104 over an average of (19 x 100 + 104) / 20 = 100.2, its stop 96, a risk of 8. It opens at 102 then runs to 109:
    // bought at 102 it reaches its target of 108 and sells at the next open of 109, (109 - 102) / 8 = 0.875. Its low of 101 never comes back to
    // 100.2, so the average's entry misses it.
    // BBB, the one picked, flat at 50, stop 46, opens at 45 under its stop and closes back to 45: bought there it sells at 45, 0; the average's
    // fill would stand under the stop, so it is not taken.
    // CCC, flat at 100, stop 96, opens at 101, its low of 99.5 reaches its average of 100, and it runs to 109: at the next open
    // (109 - 101) / 4 = 2, and at the average (109 - 100) / 4 = 2.25, 9% on the fill.
    // DDD, the same plan, opens at 101, its low of 95 passes its average of 100 and it closes at 95 under its stop on that very session, so it
    // sells the next session at 90 though that session closes back at 105: at the next open (90 - 101) / 4 = -2.75, and at the average
    // (90 - 100) / 4 = -2.5, -10% on the fill.
    // EEE, the same plan, opens at 99 under its average of 100, so it fills at that open and not the level, and runs to 109: (109 - 99) / 4 =
    // 2.5 either way, 10.1% on the fill.
    // Thirty sessions hold no year, so no band and no wait plan. The cost at no company value is 0.129% each way at these prices. The edges at
    // the next open, each its result less its cost less the benchmark: AAA -0.017334, BBB -0.891125, CCC 1.09146, DDD -3.65548, EEE 1.59114,
    // their mean -0.37627; at the average CCC 1.3413, DDD -3.40564, EEE 1.59114, their mean -0.1577.
    [Fact]
    public async Task TheMeasurementOverAConstructedLedgerStatesTheFiguresWorkedByHandAndWritesNoStoreRow()
    {
        var days = new List<DateOnly>();

        for (var day = new DateOnly(2026, 1, 5); days.Count < 30; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                days.Add(day);
            }
        }

        string Stamp(int at) => days[at].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        static (decimal Open, decimal Close, decimal Low, decimal High) Bar(string ticker, int at) => (ticker, at) switch
        {
            ("AAA", 20) => (100, 104, 99, 105),
            ("AAA", 21) => (102, 103, 101, 104),
            ("CCC", 21) => (101, 100.5m, 99.5m, 102),
            ("DDD", 21) => (101, 95, 95, 102),
            ("DDD", 22) => (90, 105, 89, 106),
            ("DDD", >= 23) => (105, 105, 104, 106),
            ("EEE", 21) => (99, 99, 98, 100),
            ("BBB", 21) => (45, 47, 44, 48),
            ("BBB", >= 22) => (45, 45, 44, 46),
            ("BBB", _) => (50, 50, 49, 51),
            (_, >= 22) => (109, 109, 108, 110),
            _ => (100, 100, 99, 101),
        };

        using var store = new TemporaryStore().Migrated();

        foreach (var ticker in new[] { "AAA", "BBB", "CCC", "DDD", "EEE" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-02-13T21:10:00Z');");

            for (var at = 0; at < days.Count; at++)
            {
                var (open, close, low, high) = Bar(ticker, at);

                store.Execute(FormattableString.Invariant($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{Stamp(at)}', '{open}', '{high}', '{low}', '{close}', 1000000, 'constructed', '2026-02-13T21:10:00Z', '{close}');"));
            }

            var (entry, stop, target, result, end, picked) = ticker switch
            {
                "AAA" => (104, 96, 108, "0.875", "target", 0),
                "BBB" => (50, 46, 58, "0.0", "stop", 1),
                "DDD" => (100, 96, 108, "-2.75", "stop", 0),
                "EEE" => (100, 96, 108, "2.5", "target", 0),
                _ => (100, 96, 108, "2.0", "target", 0),
            };

            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, cap, result, benchmark, sessions, end, ended_on, settled, source, pin) "
                + $"VALUES ('GSPC', 'pullback', '{ticker}', '{Stamp(20)}', 'the pullback', 1, {picked}, '{entry}', '{stop}', '{target}', 10, {result}, 0.875, 3, '{end}', '{Stamp(23)}', 1, 'history', 'p');");
        }

        var runs = store.Open();

        await using (runs)
        {
            await using var count = runs.CreateCommand();

            count.CommandText = "SELECT COUNT(*) FROM run_log;";

            var before = (long)(await count.ExecuteScalarAsync())!;

            using var output = new StringWriter();
            var tester = new EquityBrief.Worker.Loop.WalkForwardTester(FixedClock.At(new DateTimeOffset(2026, 2, 16, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile, store.Root, output);

            Assert.Equal(0, await tester.EntryCheckAsync("GSPC"));
            Assert.Equal(2, await tester.EntryCheckAsync("NOPE"));

            SqliteConnection.ClearPool(new SqliteConnection(EquityBrief.Worker.Sweep.SweepHistory.ConnectionString(store.DatabaseFile)));

            Assert.Contains("registered nothing and wrote no store row", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("name an index with '--index'", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(before, (long)(await count.ExecuteScalarAsync())!);
        }

        using var figures = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(store.Root, "entry-check-GSPC.json", SearchOption.AllDirectories).Single()).TrimStart('﻿'));
        var root = figures.RootElement;

        Assert.Equal((5, 0, 5), (root.GetProperty("setups").GetInt32(), root.GetProperty("unread").GetInt32(), root.GetProperty("walked").GetInt32()));

        JsonElement Entry(string name) => root.GetProperty("entries").EnumerateArray().Single(one => Text(one, "Entry") == name && Text(one, "Era") == EntryMeasure.FromRecent && Text(one, "Family") == "pullback");
        static double Figure(JsonElement element, string name) => element.GetProperty(name).GetDouble();

        var bought = Entry("next open");

        Assert.Equal((5, 5), (bought.GetProperty("Picks").GetInt32(), bought.GetProperty("Filled").GetProperty("Trades").GetInt32()));
        Assert.Equal(0.525, Figure(bought.GetProperty("Filled"), "MeanResult"), 0.001);
        Assert.Equal(-0.37627, Figure(bought.GetProperty("Filled"), "MeanEdge"), 0.001);
        Assert.Equal(((0.875 * 8 / 102) + 0 + (2d * 4 / 101) + (-2.75 * 4 / 101) + (2.5 * 4 / 99)) * 100 / 5, Figure(bought.GetProperty("Filled"), "MeanPercent"), 0.001);
        Assert.Equal(60, Figure(bought.GetProperty("Filled"), "TargetRate"), 0.001);
        Assert.Equal(40, Figure(bought.GetProperty("Filled"), "StopRate"), 0.001);

        var average = Entry("20-session average, within 3");

        Assert.Equal((5, 0, 3, 2), (average.GetProperty("Picks").GetInt32(), average.GetProperty("NoPlan").GetInt32(), average.GetProperty("Filled").GetProperty("Trades").GetInt32(), average.GetProperty("Missed").GetProperty("Trades").GetInt32()));
        Assert.Equal(60, Figure(average, "FillRate"), 0.001);
        Assert.Equal(0.75, Figure(average.GetProperty("Filled"), "MeanResult"), 0.001);
        Assert.Equal(0.75, Figure(average.GetProperty("Filled"), "MeanOwnRisk"), 0.001);
        Assert.Equal((9 - 10 + (10d / 99 * 100)) / 3, Figure(average.GetProperty("Filled"), "MeanPercent"), 0.001);
        Assert.Equal(-0.1577, Figure(average.GetProperty("Filled"), "MeanEdge"), 0.001);
        Assert.Equal(0.45, Figure(average, "ResultPerPick"), 0.001);
        Assert.Equal((9 - 10 + (10d / 99 * 100)) / 5, Figure(average, "PercentPerPick"), 0.001);
        Assert.Equal((2 - 2.75 + 2.5) / 3, Figure(average.GetProperty("NextOpenOnFilled"), "MeanResult"), 0.001);
        Assert.Equal(0.4375, Figure(average.GetProperty("Missed"), "MeanResult"), 0.001);

        var zone = Entry("wait plan's zone, within 5");

        Assert.Equal((5, 5, 0), (zone.GetProperty("Picks").GetInt32(), zone.GetProperty("NoPlan").GetInt32(), zone.GetProperty("Filled").GetProperty("Trades").GetInt32()));
        Assert.Equal(0.525, Figure(zone.GetProperty("Missed"), "MeanResult"), 0.001);

        // The readings in bands: every trade in the family's whole, and in one band of each reading.
        var bands = root.GetProperty("bands").EnumerateArray().Where(one => Text(one, "Era") == EntryMeasure.FromRecent).ToArray();

        Assert.Equal(5, bands.Single(one => Text(one, "Reading") == EntryMeasure.AllTrades).GetProperty("Group").GetProperty("Trades").GetInt32());
        Assert.Equal(5, bands.Where(one => Text(one, "Reading") == EntryMeasure.GapSize).Sum(one => one.GetProperty("Group").GetProperty("Trades").GetInt32()));
        Assert.Equal(EntryMeasure.NoRelease, Text(bands.Single(one => Text(one, "Reading") == EntryMeasure.OneOffShare), "Band"));

        // BBB alone was picked, on the 21st session: it flat, with no gap and last this high the session before, so nothing flags.
        var night = root.GetProperty("flags").EnumerateArray().Single(one => Text(one, "Night") == Stamp(20));

        Assert.Equal((1, 1, 0), (night.GetProperty("Picks").GetInt32(), night.GetProperty("NothingFlagged").GetInt32(), night.GetProperty("Gap").GetInt32()));
        Assert.Equal(20, root.GetProperty("flags").GetArrayLength());
        Assert.Equal(0, root.GetProperty("flags").EnumerateArray().Where(one => Text(one, "Night") != Stamp(20)).Sum(one => one.GetProperty("Picks").GetInt32()));
    }
}
