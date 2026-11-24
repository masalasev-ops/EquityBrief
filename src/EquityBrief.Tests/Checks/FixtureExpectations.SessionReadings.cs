using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ledger;
using EquityBrief.Tests.Harness;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: every member's readings on every session, worked by hand over a constructed long history.
// see: The readings for every stock on every session are built once before the reviewer, the picker's comparison reading the same plan on every stock until then
public partial class FixtureExpectations
{
    internal static string[] SessionReadingRows => [.. SchemaColumns.SessionReadingStoreRows, .. Reading.ReadSurface.SessionCoverageRows];

    static readonly DateOnly SessionReadingsStart = new(2005, 1, 3);

    // Sixty weekdays from the first session of 2005, the long history's calendar.
    static DateOnly[] SessionReadingsDays()
    {
        var days = new List<DateOnly>();

        for (var day = SessionReadingsStart; days.Count < 60; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return [.. days];
    }

    static string SessionDay(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task EveryMembersReadingsOnEverySessionAreWrittenOnTheWordAloneTheNightsOwnCopiedAndEachHeldToItsRebuildAtItsSession()
    {
        using var store = new TemporaryStore().Migrated();
        var days = SessionReadingsDays();
        var left = new DateOnly(2005, 3, 1);

        // AAA a member throughout, its close rising a tenth a session from 10; BBB a member until it leaves, falling five
        // hundredths a session from 20.
        store.Execute($"INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('GSPC', 'AAA', '2004-12-31', NULL, 'fund', 'r'), ('GSPC', 'BBB', '2004-12-31', '{SessionDay(left)}', 'fund', 'r');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days.Select((day, at) => SessionBar("AAA", day, 10m + (0.1m * at))).Concat(days.Select((day, at) => SessionBar("BBB", day, 20m - (0.05m * at))))) + ";");

        // A night stored its own row of AAA on the 46th session, after BBB left.
        var night = days[45];
        var nightsOwn = "[" + string.Join(",", Enumerable.Repeat("null", LedgerReadings.Count - 1)) + ",7]";

        store.Execute($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('GSPC', '{SessionDay(night)}', 'AAA', '{nightsOwn}', '{LedgerReadings.Version}');");

        // A Saturday afternoon, outside any night's window.
        var ledger = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

        // Without the word: the count and the time over the 59 sessions to work, the night's one copied, and nothing written.
        var refused = new StringWriter();

        Assert.Equal(2, await ledger.SessionReadingsAsync(store.Root, "GSPC", word: false, refused, NoWait));
        Assert.Contains("60 session(s) of the S&P 500 to write of the 60 from 2005-01-03 to 2005-03-25, 59 worked from the long history and 1 copied from the nights' own rows", refused.ToString(), StringComparison.Ordinal);
        Assert.Contains("session-readings: measured on 59 session(s) from 2005-01-03, ", refused.ToString(), StringComparison.Ordinal);
        Assert.Contains("minute(s) of reading before the writes, and about ", refused.ToString(), StringComparison.Ordinal);
        Assert.Contains("writes only on the operator's word, given as '--word'; nothing was written", refused.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM session_reading;"));

        // With it: AAA on each of the 60 sessions, the night's own row copied; BBB on each session before it left, counted from
        // the calendar rather than read off the build.
        var output = new StringWriter();
        var bbbSessions = days.Count(day => day < left);

        Assert.Equal(0, await ledger.SessionReadingsAsync(store.Root, "GSPC", word: true, output, NoWait));
        Assert.Equal(41, bbbSessions);
        Assert.Equal([$"AAA|{SetupLedger.BuildSource}|59", "AAA|night|1", $"BBB|{SetupLedger.BuildSource}|41"],
            Query(store, "SELECT ticker || '|' || source || '|' || COUNT(*) FROM session_reading GROUP BY ticker, source ORDER BY ticker, source;"));
        Assert.Equal(SessionDay(days[bbbSessions - 1]), Text(store, "SELECT MAX(session_date) FROM session_reading WHERE ticker = 'BBB';"));
        Assert.Equal(nightsOwn, Text(store, $"SELECT readings FROM session_reading WHERE ticker = 'AAA' AND session_date = '{SessionDay(night)}';"));
        Assert.Equal([LedgerReadings.Version], Query(store, "SELECT DISTINCT pin FROM session_reading;"));

        // AAA's close over its 20-session average on its 31st session, worked by hand: 13.0 over the mean of 11.1 to 13.0, 12.05.
        var thirtyFirst = System.Text.Json.JsonSerializer.Deserialize<double?[]>(Text(store, $"SELECT readings FROM session_reading WHERE ticker = 'AAA' AND session_date = '{SessionDay(days[30])}';"))!;

        Assert.Equal(LedgerReadings.Count, thirtyFirst.Length);
        Assert.Equal(13.0 / 12.05, thirtyFirst[LedgerReadings.IndexOf("close_over_twenty")]!.Value, 9);

        // The year counted: 101 rows over its 60 sessions, one a night stored.
        Assert.Equal(["GSPC|2005|101|60|1|2005-01-03|2005-03-25"], Query(store, "SELECT index_code || '|' || year || '|' || rows || '|' || sessions || '|' || night_sessions || '|' || first_session || '|' || last_session FROM session_reading_year;"));
        Assert.Equal([SetupLedger.Ok], Query(store, $"SELECT outcome FROM run_log WHERE stage = '{SetupLedger.SessionReadingsStage}';"));
        Assert.StartsWith("101 row(s) of every member's readings on the S&P 500 over 2005-01-03 to 2005-03-25, 59 session(s) worked from the long history and 1 copied from the nights' own rows", Text(store, $"SELECT detail FROM run_log WHERE stage = '{SetupLedger.SessionReadingsStage}';"), StringComparison.Ordinal);

        // A second build writes nothing.
        var second = new StringWriter();

        Assert.Equal(0, await ledger.SessionReadingsAsync(store.Root, "GSPC", word: true, second, NoWait));
        Assert.Contains("every session is written under the catalogue's version now", second.ToString(), StringComparison.Ordinal);
        Assert.Equal(101, Scalar(store, "SELECT COUNT(*) FROM session_reading;"));

        // A long setup of AAA on its 31st session holding its row's readings and a family's own reading of its own: the check
        // holds every sampled session's row to its rebuild and the setup to its stock's row, and finds nothing.
        var columns = LedgerReadings.All.Select(reading => reading.Column).ToArray();
        var values = thirtyFirst.Select((value, at) => at == LedgerReadings.IndexOf("reward_to_risk") ? "2.5" : value is { } held ? held.ToString("R", CultureInfo.InvariantCulture) : "NULL");

        store.Execute(
            "INSERT INTO long_setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, cap, end, settled, source, pin, last_end, last_settled, run_id, " + string.Join(", ", columns) + ") " +
            $"VALUES ('GSPC', 'pullback', 'AAA', '{SessionDay(days[30])}', 'provisional', 0, 0, '13', '12', 63, 'open', 0, 'history', '{LedgerReadings.Version}', 'open', 0, 'ledger-build-long', " + string.Join(", ", values) + ");");

        var checking = new StringWriter();

        Assert.Equal(0, await ledger.SessionCheckAsync("GSPC", checking, perYear: 60));
        Assert.Contains("60 sampled session(s) on the S&P 500, 60 a year by seed 19: 59 row(s) rebuilt from the long history cut at their session, 1 of the ledger's setup(s) held to their stock's row, 1 of the nights' own row(s) held to the table; every reading the same", checking.ToString(), StringComparison.Ordinal);

        // AAA's 31st row planted with BBB's readings: its rebuild and the setup each name the close over its average.
        store.Execute($"UPDATE session_reading SET readings = (SELECT readings FROM session_reading WHERE ticker = 'BBB' AND session_date = '{SessionDay(days[30])}') WHERE ticker = 'AAA' AND session_date = '{SessionDay(days[30])}';");

        var planted = new StringWriter();

        var later = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 11, 21, 16, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(1, await later.SessionCheckAsync("GSPC", planted, perYear: 60));
        Assert.Contains($"AAA {SessionDay(days[30])}: close_over_twenty stored ", planted.ToString(), StringComparison.Ordinal);
        Assert.Contains($"pullback AAA {SessionDay(days[30])}: close_over_twenty the ledger's ", planted.ToString(), StringComparison.Ordinal);
        Assert.Equal([SetupLedger.Ok, SetupLedger.NotComputed], Query(store, $"SELECT outcome FROM run_log WHERE stage = '{SetupLedger.SessionCheckStage}' ORDER BY rowid;"));
    }

    // The fixture's replayed night copies its own rows into every member's readings on every session unchanged, and each
    // index's year counts them, counted here from the rows themselves.
    [Fact]
    public async Task TheFixturesNightCopiesItsOwnReadingsIntoEveryMembersReadingsOnEverySessionAndCountsTheirYear()
    {
        var expected = Expected("every-session");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["session_reading", "session_reading_year"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));
        Assert.Equal(SetupLedger.NightCopyDays, expected.GetProperty("copiedDays").GetInt32());
        Assert.Equal(SetupLedger.NightSource, expected.GetProperty("source").GetString());

        using var store = await FixtureReplay.ReplayedAsync();

        var nights = Query(store, "SELECT index_code || '|' || session_date || '|' || ticker || '|' || readings || '|' || pin FROM ledger_reading ORDER BY 1;");

        Assert.NotEmpty(nights);
        Assert.Equal(nights, Query(store, $"SELECT index_code || '|' || session_date || '|' || ticker || '|' || readings || '|' || pin FROM session_reading WHERE source = '{SetupLedger.NightSource}' ORDER BY 1;"));
        Assert.Equal(0, Scalar(store, $"SELECT COUNT(*) FROM session_reading WHERE source <> '{SetupLedger.NightSource}';"));

        var counted = nights
            .Select(row => row.Split('|'))
            .GroupBy(row => (Index: row[0], Year: row[1][..4]))
            .OrderBy(group => group.Key.Index, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Year, StringComparer.Ordinal)
            .Select(group => $"{group.Key.Index}|{group.Key.Year}|{group.Count()}|{group.Select(row => row[1]).Distinct().Count()}|{group.Select(row => row[1]).Distinct().Count()}|{group.Min(row => row[1])}|{group.Max(row => row[1])}");

        Assert.Equal(counted, Query(store, "SELECT index_code || '|' || year || '|' || rows || '|' || sessions || '|' || night_sessions || '|' || first_session || '|' || last_session FROM session_reading_year ORDER BY index_code, year;"));
    }

    static string SessionBar(string ticker, DateOnly day, decimal close) =>
        FormattableString.Invariant($"('{ticker}', '{SessionDay(day)}', '{close}', '{close + 0.05m}', '{close - 0.05m}', '{close}', '{close}', 1000000, 'r')");

    // A setup's row and its stock's differ only where a reading not its family's own differs: the family's reward against
    // risk and a rating actions reading one of them holds none of pass, and a close over its average apart is named.
    [Fact]
    public void ASetupsRowIsHeldToItsStocksOnEveryReadingButItsFamilysOwn()
    {
        var member = new double?[LedgerReadings.Count];
        var setup = new double?[LedgerReadings.Count];
        var own = SetupLedger.FamilyOwnReadings.Select(LedgerReadings.IndexOf).ToHashSet();

        member[LedgerReadings.IndexOf("close_over_twenty")] = 1.02;
        setup[LedgerReadings.IndexOf("close_over_twenty")] = 1.02;
        setup[LedgerReadings.IndexOf("reward_to_risk")] = 2.5;
        setup[LedgerReadings.IndexOf("rating_actions_net")] = 3;

        Assert.Empty(SetupLedger.SetupDifferences(setup, member, own));

        member[LedgerReadings.IndexOf("close_over_twenty")] = 1.05;

        Assert.Equal(["close_over_twenty"], SetupLedger.SetupDifferences(setup, member, own).Select(differ => differ.Reading));
        Assert.Equal(["reward_to_risk", "freshness", "band_strength", "volume_multiple", "range_ratio", "reaction_moves"], SetupLedger.FamilyOwnReadings);
    }

    // No night, page, rule or check but the setup ledger's own reads the table: the shipped source naming it is the setup
    // ledger's, the migration's and from the 24.29 correction of 2026-11-24 the widths test's, run by hand, alone; no shipped
    // component but the setup ledger touches it and the widths test reads it alone, and only the read surface reads its years; and no source
    // the build lives in is one a rule's version pins.
    [Fact]
    public void NoNightPageOrRuleReadsEveryMembersReadingsOnEverySessionAndNoRulesVersionMoves()
    {
        var naming = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(file => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file), @"\bsession_reading\b(?!_)"))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["RangeWidthTest.cs", "SchemaMigrations.cs", "SetupLedger.cs"], naming);

        var components = Harness.ShippedComponents.All();

        Assert.Equal(["SetupLedger"], components.Where(component => component.Access.Stores.Any(touch => touch.Store == Core.Components.Store.SessionReading)).Select(component => component.Name));
        Assert.Equal(Core.Components.Touch.Read, Worker.Loop.RangeWidthTest.Access.Stores.Single(touch => touch.Store == Core.Components.Store.SessionReading).Touch);
        Assert.Equal(["ReadApi", "SetupLedger"], components.Where(component => component.Access.Stores.Any(touch => touch.Store == Core.Components.Store.SessionReadingYear)).Select(component => component.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Core.Components.Touch.Read, components.Single(component => component.Name == "ReadApi").Access.Stores.Single(touch => touch.Store == Core.Components.Store.SessionReadingYear).Touch);

        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.DoesNotContain("src/EquityBrief.Worker/Ledger/SetupLedger.SessionReadings.cs", pinned);
        Assert.DoesNotContain("src/EquityBrief.Worker/Ledger/SetupLedger.cs", pinned);
        Assert.Contains("src/EquityBrief.Core/Ledger/LedgerReadings.cs", pinned);
    }
}
