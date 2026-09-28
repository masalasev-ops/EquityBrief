using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Nights;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface, the Run page's first three regions: how last night went, the market and the funnel, each read
// back off the rendered page over a constructed store, the night's state worked by hand over constructed run log
// rows, and every table the page drew before read back inside a section folded shut beneath them.
// see: A night's state is read off its own run log rows, and the pages that state it read that one state
// see: The market on the Run page is named in one word by a stated rule that moves no gate
// see: Status is drawn in blues with violet for a wait and red for a failure alone
public partial class ReadSurface
{
    // The claims the 12.3 correction that opens the Run page on its pictures adds, which this check reaches and
    // the phase's pair names: the first three regions as the parts their rows state, the folded detail, the four
    // marks and section 17's market label. Declared before the reach that takes them in.
    internal static readonly string[] RunPageClaims =
    [
        CheckReach.Key("15.10 Run", "How last night went, a status mark and a headline naming the night's state as its own run log rows give it"),
        CheckReach.Key("15.10 Run", "How last night went, four headline figures being the stocks read and the provider requests with the research spend and the steps run again"),
        CheckReach.Key("15.10 Run", "How last night went, a time bar of the night's steps in six named groups showing where a stopped night stopped"),
        CheckReach.Key("15.10 Run", "The market, a one-word label read by a stated rule"),
        CheckReach.Key("15.10 Run", "The market, a gauge of the share of members above their 200-day average with the market gate's floor marked"),
        CheckReach.Key("15.10 Run", "The market, the share above the 50-day average and the index's volume against its fifty-day average"),
        CheckReach.Key("15.10 Run", "The market, a line of that share over the sixty sessions before the night that the store holds averages for"),
        CheckReach.Key("15.10 Run", "From the index to tonight's list, a funnel of how many members passed each of the filter's checks in turn down to those left after the exclusions"),
        CheckReach.Key("15.10 Run", "From the index to tonight's list, the count listed with a link opening tonight's list"),
        CheckReach.Key("15.10 Run", "The detail"),
        CheckReach.Key("15.5 The mark vocabulary", "Night status"),
        CheckReach.Key("15.5 The mark vocabulary", "Market gauge"),
        CheckReach.Key("15.5 The mark vocabulary", "Breadth line"),
        CheckReach.Key("15.5 The mark vocabulary", "Funnel bars"),
        CheckReach.Key(Scope.LimitsTable, "Market label"),
    ];

    static RunStageRow LogRow(string run, string stage, string from, string to, string outcome = "ok", int requests = 0, string spend = "0", string detail = "") =>
        new(run, stage, DateTimeOffset.Parse(from, CultureInfo.InvariantCulture), DateTimeOffset.Parse(to, CultureInfo.InvariantCulture), outcome, 0, 0, requests, spend, detail);

    const string EarlierRun = "night-20260910T230000Z";
    const string NightRun = "night-20260910T233000Z";

    // The night of 2026-09-10 as its run log gives it: an earlier run that stopped at the fetch, then the run read,
    // which fetched, computed its levels and closed, and whose overnight queue failed after the close; a pass of the
    // queue under an id of its own and a research pass on the same evening beside them.
    static readonly RunStageRow[] FinishedNight =
    [
        LogRow(EarlierRun, "membership", "2026-09-10T23:00:00Z", "2026-09-10T23:00:05Z", requests: 1),
        LogRow(EarlierRun, "fetch", "2026-09-10T23:00:05Z", "2026-09-10T23:00:35Z", "stopped", requests: 3, detail: "the day's file carried none of the index"),
        LogRow(NightRun, "membership", "2026-09-10T23:30:00Z", "2026-09-10T23:30:10Z", requests: 1),
        LogRow(NightRun, "fetch", "2026-09-10T23:30:10Z", "2026-09-10T23:30:20Z", requests: 2),
        LogRow(NightRun, "levels", "2026-09-10T23:31:00Z", "2026-09-10T23:35:00Z"),
        LogRow(NightRun, "listings", "2026-09-10T23:35:00Z", "2026-09-10T23:35:04Z"),
        LogRow(NightRun, "close", "2026-09-10T23:40:00Z", "2026-09-10T23:40:30Z", detail: "503 name(s) computed, 2 on the list, 9 reason(s) fired, 1 stale, 00:10:30"),
        LogRow(NightRun, RunScreen.QueueStage, "2026-09-10T23:40:30Z", "2026-09-10T23:41:00Z", "failed", detail: "database is locked"),
        LogRow(NightRun + "-queue-AAPL", "prose", "2026-09-10T23:40:40Z", "2026-09-10T23:40:50Z", "failed"),
        LogRow("research-20260910T235000Z-KEYS", "research call: The risks", "2026-09-10T23:50:00Z", "2026-09-10T23:51:00Z", spend: "0.012"),
    ];

    static readonly DateOnly TenthOfSeptember = new(2026, 9, 10);
    static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);

    [Fact]
    public void TheNightsStateIsReadOffItsOwnRunLogRowsAsTheRuleGivesIt()
    {
        var eleventh = new DateOnly(2026, 9, 11);
        var after = DateTimeOffset.Parse("2026-09-11T01:00:00Z", CultureInfo.InvariantCulture);

        // Finished: the newest run closed its arithmetic. Worked by hand: 503 stocks read off the close; seven
        // requests over both runs; 0.012 spent by the research pass that evening; one step stopped on the earlier
        // run; the arithmetic from 23:30:00 to the close's end at 23:40:30, 630 seconds; the queue's failure said
        // after the close and not unfinishing the night; and the queue's own pass no run of the night.
        var finished = RunScreen.Night(FinishedNight, TenthOfSeptember, eleventh, after, FifteenMinutes);

        Assert.Equal(NightStates.Finished, finished.State);
        Assert.Equal((503, 7, 0.012m, 1), (finished.StocksRead!.Value, finished.ProviderRequests, finished.ResearchSpend, finished.StepsRetried));
        Assert.Equal(630, finished.Seconds);
        Assert.Equal("overnight queue failed: database is locked", finished.AfterTheClose);
        Assert.Equal(
            [("Prices and calendar", 2, 20.0), ("Indicators and levels", 1, 240), ("Plans and moves", 0, 0), ("Readings and the list", 1, 4), ("Records", 1, 30), ("After the close", 1, 30)],
            finished.Groups.Select(group => (group.Name, group.Reached, group.Seconds)));
        Assert.DoesNotContain(finished.Groups, group => group.Stopped);
        Assert.Equal("Finished", MarkRenderer.NightHeadline(finished));
        Assert.Equal(
            "Started 23:30 UTC and closed its arithmetic in 10 min 30 s, inside its 15-minute deadline. Before this run, 1 step(s) of the night stopped and the night was run again. After the close, overnight queue failed: database is locked",
            MarkRenderer.NightSaid(finished));

        // Stopped: the newest run wrote a stop at the levels before any close, and the group holding it is marked.
        RunStageRow[] stopped =
        [
            LogRow(NightRun, "fetch", "2026-09-10T23:30:10Z", "2026-09-10T23:30:20Z"),
            LogRow(NightRun, "levels", "2026-09-10T23:31:00Z", "2026-09-10T23:45:00Z", "stopped", detail: "step 'levels' passed the night's deadline of 15 minute(s) and was stopped."),
        ];

        var stop = RunScreen.Night(stopped, TenthOfSeptember, eleventh, after, FifteenMinutes);

        Assert.Equal((NightStates.Stopped, "levels"), (stop.State, stop.StoppedAt));
        Assert.Equal("Stopped at levels", MarkRenderer.NightHeadline(stop));
        Assert.Equal("It stopped at levels: step 'levels' passed the night's deadline of 15 minute(s) and was stopped.", MarkRenderer.NightSaid(stop));
        Assert.Equal(["Indicators and levels"], stop.Groups.Where(group => group.Stopped).Select(group => group.Name));
        Assert.Equal("fail", MarkRenderer.NightTone(stop.State));

        // Neither a close nor a stop: running while its last row is inside the deadline, and left unfinished once
        // it is older, at the fifteen minutes exactly and a second past them.
        RunStageRow[] partway = [LogRow(NightRun, "fetch", "2026-09-10T23:30:10Z", "2026-09-10T23:30:20Z"), LogRow(NightRun, "levels", "2026-09-10T23:31:00Z", "2026-09-10T23:35:00Z")];

        Assert.Equal(NightStates.Running, RunScreen.Night(partway, TenthOfSeptember, TenthOfSeptember, DateTimeOffset.Parse("2026-09-10T23:50:00Z", CultureInfo.InvariantCulture), FifteenMinutes).State);
        Assert.Equal(NightStates.Unfinished, RunScreen.Night(partway, TenthOfSeptember, TenthOfSeptember, DateTimeOffset.Parse("2026-09-10T23:50:01Z", CultureInfo.InvariantCulture), FifteenMinutes).State);
        Assert.Equal("levels", RunScreen.Night(partway, TenthOfSeptember, eleventh, after, FifteenMinutes).StoppedAt);

        // No run of the night: not yet on the session the clock is in, never on one before it, and no session on a
        // Saturday; a queue's pass and a research pass are no run of the night.
        RunStageRow[] none = [.. FinishedNight.Where(row => !row.RunId.StartsWith("night-", StringComparison.Ordinal) || row.RunId.Contains("-queue-", StringComparison.Ordinal))];

        Assert.Equal(NightStates.NotYet, RunScreen.Night(none, TenthOfSeptember, TenthOfSeptember, after, FifteenMinutes).State);
        Assert.Equal(NightStates.NeverRan, RunScreen.Night(none, TenthOfSeptember, eleventh, after, FifteenMinutes).State);
        Assert.Equal(NightStates.NoSession, RunScreen.Night([], new DateOnly(2026, 9, 12), eleventh, after, FifteenMinutes).State);
        Assert.Equal(("wait", "fail", "quiet", "ok"), (MarkRenderer.NightTone(NightStates.NotYet), MarkRenderer.NightTone(NightStates.NeverRan), MarkRenderer.NightTone(NightStates.NoSession), MarkRenderer.NightTone(NightStates.Running)));
    }

    // Every stage a whole night writes is in exactly one of the six groups, read off the run log of the fixture's
    // night as the worker runs it, and the words a stop is written with are the night close's own.
    [Fact]
    public async Task EveryStageANightWritesIsInExactlyOneGroupAndAStopIsWrittenInTheNightsOwnWords()
    {
        var (store, code, _, error) = await FixtureReplay.NightAsync();

        using (store)
        {
            Assert.True(code == 0, error);

            var stages = new List<string>();

            using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
            {
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT DISTINCT stage FROM run_log WHERE run_id = '{FixtureReplay.NightRunId}';";

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    stages.Add(reader.GetString(0));
                }
            }

            Assert.True(stages.Count >= 20, $"The fixture's night wrote {stages.Count} stage(s), expected at least 20.");
            Assert.All(stages, stage => Assert.Single(RunScreen.StepGroups, group => group.Stages.Contains(stage)));
        }

        Assert.Equal(6, RunScreen.StepGroups.Count);
        Assert.Equal([NightClose.Failed, NightClose.Stopped, NightClose.Refused], RunScreen.StopOutcomes);
    }

    // The word the market is named by, worked by hand at each point of its rule and a hair either side.
    [Fact]
    public void TheMarketIsNamedByItsRuleAtTheFloorAndAtTheHealthyPoint()
    {
        Assert.Equal(MarketLabel.Weak, MarketLabel.For(0.4499, 0.45));
        Assert.Equal(MarketLabel.Mixed, MarketLabel.For(0.45, 0.45));
        Assert.Equal(MarketLabel.Mixed, MarketLabel.For(0.5999, 0.45));
        Assert.Equal(MarketLabel.Healthy, MarketLabel.For(0.60, 0.45));
        Assert.Equal(MarketLabel.Weak, MarketLabel.For(0.55, 0.60));
        Assert.Equal(MarketLabel.NotRead, MarketLabel.For(null, 0.45));
        Assert.Equal(0.60, MarketLabel.HealthyFrom);
    }

    // A night over five members with a version open at a 45% floor, its run log finished and its market read at
    // 44%, and two sessions of closes and 200-day averages behind it.
    static TemporaryStore RunTopStore()
    {
        var store = new TemporaryStore().Migrated();
        var settings = FilterSettings.Proposed with { BreadthFloor = 0.45 };

        store.Execute(
            "INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) " +
            $"VALUES ('2', '{settings.Write()}', '2026-09-01T00:00:00Z', NULL, 'the operator''s ruling');" +
            "INSERT INTO list_rule (session_date, rule) VALUES ('2026-09-10', 'filter');" +
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            "VALUES ('2026-09-10', 5, 5, 2, 0.44, 5, 3, 0.6, 5, 1.25);");

        // Each member passes a run of gates from the first: all five through the market, four through trend and
        // strength, three through the setup, two through the trigger and the trade, and one left after an
        // exclusion takes the other.
        (int Trend, int Setup, int Trigger, int Trade, string Exclusions, int Passed)[] members =
        [
            (1, 1, 1, 1, "[]", 1),
            (1, 1, 1, 1, "[\"earnings inside the holding window\"]", 0),
            (1, 1, 0, 0, "[]", 0),
            (1, 0, 0, 0, "[]", 0),
            (0, 0, 0, 0, "[]", 0),
        ];

        for (var at = 0; at < members.Length; at++)
        {
            var (trend, setup, trigger, trade, exclusions, passed) = members[at];

            store.Execute(
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, rank, gates) " +
                $"VALUES ('T{at}', '2026-09-10', '2', 'test', 1, {trend}, {setup}, {(setup == 1 ? "'pullback'" : "NULL")}, {trigger}, {trigger}, {trade}, '{exclusions}', {passed}, {(passed == 1 ? "1" : "NULL")}, '{{\"gates\":[],\"notes\":[]}}');");
        }

        // Two sessions of four names. On 2026-09-09 three of four close above their averages; on 2026-09-10 one of
        // four does, and a fifth name holds a close and no average, which the rule counts among the names and not
        // among those held: four of five hold both, more than half, so each session reads 0.75 and 0.25.
        foreach (var (session, closes) in new[] { ("2026-09-09", new[] { 11, 12, 13, 9 }), ("2026-09-10", new[] { 11, 9, 9, 9 }) })
        {
            for (var at = 0; at < closes.Length; at++)
            {
                store.Execute(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                    $"VALUES ('T{at}', '{session}', '10', '14', '8', '{closes[at]}', 1000, 'test', '{session}T21:00:00Z', '{closes[at]}');" +
                    "INSERT INTO indicator (ticker, session_date, name, value, bar_count) " +
                    $"VALUES ('T{at}', '{session}', 'sma200', 10.0, 200);");
            }

            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                $"VALUES ('T4', '{session}', '10', '14', '8', '10', 1000, 'test', '{session}T21:00:00Z', '10');");
        }

        foreach (var row in FinishedNight)
        {
            store.Execute(
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
                $"VALUES ('{row.RunId}', '{row.Stage}', '{Stamped(row.StartedAt)}', '{Stamped(row.EndedAt)}', '{row.Outcome}', 0, 0, {row.NetworkRequests}, '{row.Spend}', '{row.Detail.Replace("'", "''", StringComparison.Ordinal)}');");
        }

        return store;

        static string Stamped(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task TheRunPageOpensOnTheNightTheMarketAndTheFunnelWithEveryTableFoldedBeneath()
    {
        using var store = RunTopStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-10"));

        // The order: the night, the market and the funnel above the detail.
        int At(string marker)
        {
            var at = page.IndexOf(marker, StringComparison.Ordinal);

            Assert.True(at >= 0, $"The run page draws no {marker}.");

            return at;
        }

        Assert.True(At("data-card=\"night\"") < At("data-card=\"market-picture\"") && At("data-card=\"market-picture\"") < At("data-card=\"funnel-picture\"") && At("data-card=\"funnel-picture\"") < At("<section class=\"run-detail\">"));

        // How the night went, as the rule gives it over the same rows the store holds.
        var night = Assert.Single(Blocks(page, "<div class=\"night-status\".*?</ol></div></div>"));

        Assert.Contains("data-state=\"finished\" data-tone=\"ok\" data-session=\"2026-09-10\" data-stocks-read=\"503\" data-requests=\"7\" data-spend=\"0.012\" data-retried=\"1\"", night, StringComparison.Ordinal);
        Assert.Contains("<p class=\"ns-headline\">Finished</p>", night, StringComparison.Ordinal);
        Assert.Contains("closed its arithmetic in 10 min 30 s, inside its 15-minute deadline.", night, StringComparison.Ordinal);
        Assert.Equal(
            [.. RunScreen.StepGroups.Select(group => group.Name)],
            Regex.Matches(night, "<li data-group=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<li data-group=\"Plans and moves\" data-reached=\"0\"><b>Plans and moves</b> not reached</li>", night, StringComparison.Ordinal);
        Assert.Contains("class=\"sb-unreached\" data-group=\"Plans and moves\"", night, StringComparison.Ordinal);

        foreach (var (figure, value) in new[] { ("stocks read", "503"), ("provider requests", "7"), ("research spend", "$0.01"), ("steps retried", "1") })
        {
            Assert.Contains($"<div class=\"tile\" data-figure=\"{figure}\"><b>{value}</b>", night, StringComparison.Ordinal);
        }

        // The market: 44% against the version's 45% floor reads weak, the context and the volume beside it, and the
        // line over the two sessions worked by hand.
        var market = Assert.Single(Blocks(page, "<div class=\"market-picture\".*?</svg></div></div>"));

        Assert.Contains("data-breadth=\"0.44\" data-floor=\"0.45\" data-healthy-from=\"0.6\" data-label=\"weak\" data-line=\"2\"", market, StringComparison.Ordinal);
        Assert.Contains("<p class=\"mp-label\">A weak market</p>", market, StringComparison.Ordinal);
        Assert.Contains("<p data-part=\"context\"><b>60%</b> above their 50-day average</p>", market, StringComparison.Ordinal);
        Assert.Contains("<p data-part=\"volume\"><b>1.25×</b> the index's usual volume", market, StringComparison.Ordinal);
        Assert.Contains("Weak below 45%, the market gate's floor, where the list lists nobody; healthy from 60%; mixed between.", market, StringComparison.Ordinal);

        var line = await new ReadApi(store.DatabaseFile, FixedClock.At(Instant, EquityBrief.Core.Time.SessionZones.UnitedStates)).BreadthLineAsync(TenthOfSeptember, MarkRenderer.BreadthLineSessions);

        Assert.Equal([(new DateOnly(2026, 9, 9), 0.75, 4), (TenthOfSeptember, 0.25, 4)], line.Select(point => (point.Session, point.Share, point.Counted)));

        // The funnel: the counts through each check in turn, the one listed and the link to the night's list.
        var funnel = Assert.Single(Blocks(page, "<div class=\"funnel-picture\".*?</p></div>"));

        Assert.Equal(
            [("members", "5"), ("market", "5"), ("trend and strength", "4"), ("setup", "3"), ("trigger", "2"), ("trade", "2"), ("excluded", "1")],
            Regex.Matches(funnel, "<g data-step=\"([^\"]+)\" data-count=\"(\\d+)\">").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Contains("<b>1</b> stock(s) on this night's list. <a class=\"fp-open\" href=\"#/night/2026-09-10\">", funnel, StringComparison.Ordinal);

        // Every table the page drew before sits whole in a folded section beneath the pictures.
        var detail = page[At("<section class=\"run-detail\">")..];

        foreach (var card in new[] { "operational", "market", "funnel", "records", "overlap", "shadow", "stale", "queue", "harness" })
        {
            Assert.Matches($"<details class=\"fold\" data-fold=\"[a-z-]+\"><summary>[^<]+</summary>(?:(?!</details>).)*data-card=\"{card}\"", Regex.Replace(detail, "\\s+", " "));
        }
    }

    [Fact]
    public async Task AStoppedNightSaysWhereItStoppedAndWhyInRed()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            "('night-20260910T233000Z', 'fetch', '2026-09-10T23:30:10Z', '2026-09-10T23:30:20Z', 'ok', 0, 0, 1, '0', '')," +
            "('night-20260910T233000Z', 'levels', '2026-09-10T23:31:00Z', '2026-09-10T23:45:00Z', 'failed', 0, 0, 0, '0', 'step ''levels'' failed: SQLite Error 5: ''database is locked''.');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var night = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-10")), "<div class=\"night-status\".*?</ol></div></div>"));

        Assert.Contains("data-state=\"stopped\" data-tone=\"fail\"", night, StringComparison.Ordinal);
        Assert.Contains("<p class=\"ns-headline\">Stopped at levels</p>", night, StringComparison.Ordinal);
        Assert.Contains("It stopped at levels: step 'levels' failed: SQLite Error 5: 'database is locked'.", night, StringComparison.Ordinal);
        Assert.Contains("class=\"sb-stopped\" data-group=\"Indicators and levels\"", night, StringComparison.Ordinal);
        Assert.Contains("<li data-group=\"Indicators and levels\" data-reached=\"1\"><b>Indicators and levels</b> stopped here</li>", night, StringComparison.Ordinal);
    }

    // Status is drawn in the status tokens and never in a level's: every rule of the stylesheet drawing the Run
    // page's pictures names no level token, and every status token is used by those rules alone.
    [Fact]
    public void TheRunPagesPicturesDrawInTheStatusColoursAndNeverInALevels()
    {
        string[] pictures = [".night-status", ".status-mark", ".step-bar", ".gauge", ".breadth-line", ".funnel-bars", ".market-picture", ".trades-ring", ".fresh-bars", ".fr-key", ".spend-bar", ".pass-bars", ".worries", ".worry-mark", ".progress-bar", ".band-dot", ".edge-line", ".share-bar", ".compare-picture", ".overlap-rings", ".checkpoint-picture", ".ck-", ".tag-wait"];

        var rules = Regex.Matches(Stylesheet.Css, "([^{}]+)\\{([^{}]*)\\}")
            .Select(match => (Selector: match.Groups[1].Value.Trim(), Body: match.Groups[2].Value))
            .ToArray();

        var drawing = rules.Where(rule => pictures.Any(picture => rule.Selector.Contains(picture, StringComparison.Ordinal))).ToArray();

        Assert.True(drawing.Length >= 20, $"Read {drawing.Length} rule(s) drawing the Run page's pictures, expected at least 20.");
        Assert.All(drawing, rule => Assert.DoesNotMatch("var\\(--(?:sup|res|support|resistance)", rule.Body));

        foreach (var token in new[] { "--stat", "--stat-2", "--stat-fill", "--wait", "--wait-fill", "--fail", "--fail-fill" })
        {
            var users = rules.Where(rule => rule.Body.Contains($"var({token})", StringComparison.Ordinal)).ToArray();

            Assert.NotEmpty(users);
            Assert.All(users, rule => Assert.Contains(pictures.Append(".run-").Append(".ns-"), picture => rule.Selector.Contains(picture, StringComparison.Ordinal)));
        }
    }
}
