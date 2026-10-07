using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Checks;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// The sweep against a store: what it reads, what it leaves a writer, what it reads of the live rule's own design
// against what a night stored over the committed fixture, and what a run started again goes on from.
public partial class FixtureExpectations
{
    // A Tuesday noon, far from the night's pause.
    static readonly DateTimeOffset SweepNoon = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    static IReadOnlyList<string> SweepRows(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? string.Empty : Convert.ToString(reader.GetValue(at), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    // The sweep's own pool for a store, emptied so the store's folder can go.
    static void SweepRelease(TemporaryStore store)
    {
        using var connection = new SqliteConnection(SweepHistory.ConnectionString(store.DatabaseFile));

        SqliteConnection.ClearPool(connection);
    }

    [Fact]
    public async Task ASweepReadNeverFailsAWriteAndAStatementHeldOpenPastTheWritersWaitWould()
    {
        // The store's journal mode is DELETE: a reader holds a shared lock for as long as one statement runs,
        // and a writer's commit waits for it. The sweep reads one short statement a name, so a writer waits at
        // most for that one statement. Shown over 100 names of 300 sessions each, 30,000 bars, with a writer
        // committing one bar at a time the whole while and waiting 2 seconds at most, where the store's own
        // connections wait 600.
        const int Names = 100;
        const int Sessions = 300;

        using var store = new TemporaryStore().Migrated();

        Assert.Equal(["delete"], SweepRows(store, "PRAGMA journal_mode;"));

        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2024, 1, 1); sessions.Count < Sessions; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(day);
            }
        }

        var through = sessions[^1];

        using (var connection = store.Open())
        {
            using var transaction = connection.BeginTransaction();
            using var member = connection.CreateCommand();
            member.CommandText = "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', $ticker, NULL, NULL, '2025-07-01T21:10:00Z');";
            member.Parameters.Add("$ticker", SqliteType.Text);

            using var bar = connection.CreateCommand();
            bar.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $session, '100', '101', '99', '100.5', 1000, 'bulk', '2025-07-01T21:10:00Z', '100.5');";
            bar.Parameters.Add("$ticker", SqliteType.Text);
            bar.Parameters.Add("$session", SqliteType.Text);

            for (var name = 0; name < Names; name++)
            {
                var ticker = FormattableString.Invariant($"N{name:000}");

                member.Parameters["$ticker"].Value = ticker;
                member.ExecuteNonQuery();

                foreach (var session in sessions)
                {
                    bar.Parameters["$ticker"].Value = ticker;
                    bar.Parameters["$session"].Value = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    bar.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        var writer = StoreConnection.Builder(store.DatabaseFile);

        writer.DefaultTimeout = 2;

        var reading = new TaskCompletionSource();
        var committed = new List<(long At, int Name)>();
        var failures = new List<string>();
        var clock = Stopwatch.StartNew();

        var writes = Task.Run(async () =>
        {
            var name = 0;
            var next = through.AddDays(1);

            while (!reading.Task.IsCompleted)
            {
                try
                {
                    await using var connection = new SqliteConnection(writer.ConnectionString);
                    await connection.OpenAsync();
                    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $session, '100', '101', '99', '100.5', 1000, 'bulk', '2025-07-01T21:10:00Z', '100.5');";
                    command.Parameters.AddWithValue("$ticker", FormattableString.Invariant($"N{name % Names:000}"));
                    command.Parameters.AddWithValue("$session", next.AddDays(name / Names).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    await command.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();

                    lock (committed)
                    {
                        committed.Add((clock.ElapsedTicks, name));
                    }

                    name++;

                    // Ten milliseconds between commits, many times the rate any writer the store has commits at,
                    // so the reader's own waits for a commit stay short and the test runs in seconds.
                    await Task.Delay(10);
                }
                catch (SqliteException refused)
                {
                    lock (failures)
                    {
                        failures.Add(refused.Message);
                    }

                    return;
                }
            }
        });

        // A write made from inside the read, once it says it has read its names, by the same writer: it commits before
        // the read returns where the read holds no statement open across its names, whatever the machine's speed.
        var inside = new List<string>();
        SweepHistoryInputs inputs;

        void WriteInside(string said)
        {
            if (said != FormattableString.Invariant($"read {Names} of {Names} name(s)"))
            {
                return;
            }

            try
            {
                using var connection = new SqliteConnection(writer.ConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'INSIDE', NULL, NULL, '2025-07-01T21:10:00Z');";
                command.ExecuteNonQuery();
                inside.Add("committed");
            }
            catch (SqliteException refused)
            {
                inside.Add(refused.Message);
            }
        }

        try
        {
            // The writer has begun before the read starts, and runs until it ends.
            while (true)
            {
                lock (committed)
                {
                    if (committed.Count > 0)
                    {
                        break;
                    }
                }

                await Task.Delay(10);
            }

            inputs = await new SweepHistory(store.DatabaseFile).ReadAsync(through, WriteInside);
        }
        finally
        {
            reading.SetResult();
            await writes;
            SweepRelease(store);
        }

        Assert.Empty(failures);
        Assert.Equal(["committed"], inside);

        // The read holds every name's sessions and none a writer added after the end it was handed.
        Assert.Equal(Names, inputs.Names.Count);
        Assert.All(inputs.Names, name => Assert.Equal(sessions, name.Bars.Select(one => one.Session)));
        Assert.Equal(sessions, inputs.Sessions);

        // A statement held open past the writer's wait fails the write with the store locked, which is the
        // failure the reads being short keeps away: the same writer, a sweep connection stepping one row and
        // holding it.
        await using (var reader = new SqliteConnection(SweepHistory.ConnectionString(store.DatabaseFile)))
        {
            await reader.OpenAsync();
            await using var held = reader.CreateCommand();
            held.CommandText = "SELECT ticker FROM bar;";
            await using var rows = await held.ExecuteReaderAsync();

            Assert.True(await rows.ReadAsync());

            await using var connection = new SqliteConnection(writer.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'LOCKED', NULL, NULL, '2025-07-01T21:10:00Z');";

            var locked = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

            Assert.Equal(5, locked.SqliteErrorCode);
            Assert.Contains("database is locked", locked.Message, StringComparison.Ordinal);
        }

        SweepRelease(store);

        // And the sweep's connection is read-only and never immutable: it refuses a write of its own, and it
        // reads rows a writer committed after it opened.
        await using (var own = new SqliteConnection(SweepHistory.ConnectionString(store.DatabaseFile)))
        {
            await own.OpenAsync();
            await using var command = own.CreateCommand();
            command.CommandText = "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'SWEEP', NULL, NULL, '2025-07-01T21:10:00Z');";

            Assert.Equal(8, (await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync())).SqliteErrorCode);

            store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'LATER', NULL, NULL, '2025-07-01T21:10:00Z');");
            command.CommandText = "SELECT COUNT(*) FROM membership WHERE ticker = 'LATER';";

            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }

        Assert.DoesNotContain("immutable", SweepHistory.ConnectionString(store.DatabaseFile), StringComparison.OrdinalIgnoreCase);

        SweepRelease(store);
    }

    [Fact]
    public async Task TheSweepReadsTheLiveRulesDesignAsTheNightStoredItOverTheFixture()
    {
        // Over the fixture's two nights, every member the filter stored a row for, 4 names on each of 2 nights:
        // the sweep's readings of the live design are the ones the night stored, each read through the sweep's
        // own path from the bars the store holds: the pullback's depth and dry-up at the 20-session high, the
        // strength, the breadth, the classifier's uptrend, every band the level builder drew, the anchored
        // support band and its strength, the trigger's event, section 10's plan, the plan at the nearest bands
        // and the ladder's first tranche.
        using var store = await FixtureExpectations.WithTwoNights();

        var inputs = await new SweepHistory(store.DatabaseFile).ReadAsync(FixtureNight);

        SweepRelease(store);

        var sessionAt = inputs.Sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var sessions = SweepColumns.Sessions(series, inputs.Sessions);
        var compared = 0;

        static double? Held(double value) => double.IsNaN(value) ? null : value;

        static void Within(string stored, double? read, string what)
        {
            if (stored.Length == 0)
            {
                Assert.True(read is null, $"{what}: the night stored none and the sweep read {read}.");
            }
            else
            {
                Assert.True(read is { } value && Math.Abs(value - double.Parse(stored, CultureInfo.InvariantCulture)) < 1e-9, $"{what}: the night stored {stored} and the sweep read {read}.");
            }
        }

        static decimal? Price(string stored) => stored.Length == 0 ? null : decimal.Parse(stored, CultureInfo.InvariantCulture);

        foreach (var night in new[] { EarlierNight, FixtureNight })
        {
            var day = night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var session = sessionAt[night];

            Within(SweepRows(store, $"SELECT IFNULL(breadth, '') FROM market_reading WHERE session_date = '{day}';").Single(), sessions[session].Breadth, "breadth");

            foreach (var ticker in SweepRows(store, $"SELECT ticker FROM gate_result WHERE session_date = '{day}' ORDER BY ticker;"))
            {
                var at = inputs.Names.ToList().FindIndex(name => name.Ticker == ticker);
                var one = series[at];
                var bar = Array.IndexOf(one.SessionAt, session);

                Assert.True(bar > 0, $"{ticker} holds no bar on {day}.");

                var reading = SweepRows(store, $"SELECT IFNULL(depth, ''), IFNULL(dry_up, ''), IFNULL(strength, '') FROM swing_reading WHERE ticker = '{ticker}' AND session_date = '{day}';").Single().Split('|');

                Within(reading[0], Held(one.Depth[(bar * SweepAxes.ReferenceHighs.Count) + 1]), $"{ticker} {day} depth");
                Within(reading[1], Held(one.DryUp[(bar * SweepAxes.ReferenceHighs.Count) + 1]), $"{ticker} {day} dry-up");
                Within(reading[2], sessions[session].Strength[(int)StrengthMeasure.ThreeAndSixMonths].TryGetValue(at, out var place) ? (double?)place : null, $"{ticker} {day} strength");

                var state = SweepRows(store, $"SELECT trend_state FROM ladder WHERE ticker = '{ticker}' AND as_of = '{day}';").Single();

                Assert.Equal(state == SwingGates.Uptrend, (one.Uptrend[bar] & (1 << (int)UptrendRule.Classifier)) != 0);
                Assert.Equal(state, one.Label[bar]);

                // Every band, edge for edge.
                var levels = SweepCandidates.LevelsOn(one, bar);
                var stored = SweepRows(store, $"SELECT low_edge, high_edge, role, strength, has_non_average_anchor FROM level WHERE ticker = '{ticker}' AND as_of = '{day}' ORDER BY low_edge;")
                    .Select(row => row.Split('|'))
                    .Select(cells => (decimal.Parse(cells[0], CultureInfo.InvariantCulture), decimal.Parse(cells[1], CultureInfo.InvariantCulture), cells[2], int.Parse(cells[3], CultureInfo.InvariantCulture), cells[4] == "1"))
                    .ToArray();

                Assert.True(stored.Length > 0, $"{ticker} {day} stored no band.");
                Assert.Equal(stored, levels.OrderBy(level => level.LowEdge).Select(level => (level.LowEdge, level.HighEdge, level.Role, level.Strength, level.HasNonAverageAnchor)).ToArray());

                var bands = levels.Select(level => new FilterBand(level.LowEdge, level.HighEdge, level.Role, level.Strength, level.HasNonAverageAnchor)).ToArray();
                var close = one.Bars[bar].Close;
                var band = SweepReadings.BandHolding(bands, close, anchoredOnly: true);
                var row = SweepRows(
                    store,
                    "SELECT IFNULL(band_strength, ''), IFNULL(trigger_event, ''), IFNULL(clear_stop, ''), IFNULL(clear_target, ''), IFNULL(clear_reward_to_risk, ''), IFNULL(swing_stop, ''), IFNULL(swing_target, ''), " +
                    "IFNULL(swing_reward_to_risk, ''), IFNULL(ladder_reward_to_risk, ''), IFNULL(ladder_stop_moves, ''), IFNULL(clear_stop_moves, '') " +
                    $"FROM gate_result WHERE ticker = '{ticker}' AND session_date = '{day}';").Single().Split('|');

                Assert.Equal(row[0], band is null ? string.Empty : band.Strength.ToString(CultureInfo.InvariantCulture));

                var fired = SweepReadings.Event(TriggerKind.AbovePreviousHigh, one.Bars[bar], one.Bars[bar - 1], band);

                Assert.Equal(row[1], fired is { } happened ? (happened ? "1" : "0") : string.Empty);

                double? move = double.IsNaN(one.Atr[bar]) ? null : one.Atr[bar];
                var clear = SweepReadings.Clear(bands, close, band, null, move);

                // The night stores section 10's stop where it has no target; the sweep's plan is the whole plan
                // or none, so a stored target is what it is held to.
                Assert.Equal(Price(row[3]), clear?.Target);

                if (clear is { } plan)
                {
                    Assert.Equal(Price(row[2]), plan.Stop);
                    Within(row[4], Statistic.FromRatio(plan.RewardToRisk), $"{ticker} {day} section 10's reward to risk");
                    Within(row[10], plan.StopMoves, $"{ticker} {day} section 10's stop in typical moves");
                }

                var nearest = band is null ? null : SweepReadings.Nearest(bands, close, band.LowEdge, move);

                Assert.Equal(Price(row[6]), nearest?.Target);

                if (nearest is { } swing)
                {
                    Assert.Equal(Price(row[5]), swing.Stop);
                    Within(row[7], Statistic.FromRatio(swing.RewardToRisk), $"{ticker} {day} the nearest bands' reward to risk");
                }

                // The ladder's first tranche on a night the classifier read an uptrend, which is where the sweep
                // builds it.
                if (state == SwingGates.Uptrend)
                {
                    var ladder = SweepCandidates.LadderPlan(one, bar, levels);

                    Within(row[8], ladder is { } first ? Statistic.FromRatio(first.RewardToRisk) : null, $"{ticker} {day} the ladder's reward to risk");

                    if (ladder is { } tranche)
                    {
                        Within(row[9], tranche.StopMoves, $"{ticker} {day} the ladder's stop in typical moves");
                    }
                }

                compared++;
            }
        }

        Assert.Equal(8, compared);
    }

    // A log that cancels the run once it writes a line holding the marker, so a test can stop a run at a
    // chunk's end the way a machine that stops does.
    sealed class SweepStoppingLog(string marker, CancellationTokenSource stop) : StringWriter(CultureInfo.InvariantCulture)
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);

            if (value is not null && value.Contains(marker, StringComparison.Ordinal))
            {
                stop.Cancel();
            }
        }
    }

    static async Task<string> SweepRunUntil(TemporaryStore store, string folder, string marker)
    {
        using var stop = new CancellationTokenSource();
        using var log = new SweepStoppingLog(marker, stop);

        var runner = new SweepRunner(FixedClock.At(SweepNoon, SessionZones.UnitedStates), store.Root, store.DatabaseFile, folder, log);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(stop.Token));

        SweepRelease(store);

        return log.ToString();
    }

    [Fact]
    public async Task ARunStoppedAfterAChunkGoesOnFromTheNextAndHoldsWhatOneRunUnstoppedHolds()
    {
        // The fixture's store, its 4 names over its year of sessions: the candidates in 4 chunks, the first 20
        // sessions and then 100 at a time. One run stopped after its first chunk and started again holds the
        // same candidates as one run stopped only once they are all held.
        using var store = await FixtureExpectations.WithTwoNights();

        var stopped = Path.Combine(store.Root, "stopped");
        var whole = Path.Combine(store.Root, "whole");

        var first = await SweepRunUntil(store, stopped, "candidates chunk 1 of");
        var runner = new SweepRunner(FixedClock.At(SweepNoon, SessionZones.UnitedStates), store.Root, store.DatabaseFile, stopped, TextWriter.Null);

        Assert.Contains("candidates chunk 1 of 4 ", first, StringComparison.Ordinal);
        Assert.Equal(1, runner.Load().CandidateChunks);
        Assert.False(runner.Load().CandidatesDone);
        Assert.Equal(FixtureNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), runner.Load().Through);

        var second = await SweepRunUntil(store, stopped, "candidate(s) held");

        Assert.DoesNotContain("candidates chunk 1 of", second, StringComparison.Ordinal);
        Assert.Contains("candidates chunk 2 of 4 ", second, StringComparison.Ordinal);
        Assert.Contains("candidates chunk 4 of 4 ", second, StringComparison.Ordinal);
        Assert.True(runner.Load().CandidatesDone);

        var once = await SweepRunUntil(store, whole, "candidate(s) held");
        var held = System.Text.RegularExpressions.Regex.Match(once, @"(\d+) candidate\(s\) held").Groups[1].Value;

        Assert.True(int.Parse(held, CultureInfo.InvariantCulture) > 0, "the fixture's run held no candidate.");
        Assert.Contains($" {held} candidate(s) held", second, StringComparison.Ordinal);

        // The chunk files hold the same bytes either way.
        foreach (var chunk in Directory.GetFiles(Path.Combine(whole, SweepFolder.CandidatesFolder)).Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(whole, SweepFolder.CandidatesFolder, chunk!)), File.ReadAllBytes(Path.Combine(stopped, SweepFolder.CandidatesFolder, chunk!)));
        }

        // Nothing was written to the store: the run log holds only the rows the two nights wrote.
        Assert.DoesNotContain(SweepRows(store, "SELECT run_id FROM run_log;"), run => run.Contains("sweep", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AWholeRunOverTheFixtureRunsEveryStageWritesItsReportAndTouchesNeitherTheStoreNorTheFirstRun()
    {
        // The fixture's store, its 4 names over a year: a run from the candidates through the point-in-time
        // check, stage 1, the two condition steps and stage 2 to the report, in a run folder of its own under
        // the sweep's folder, with the root's own report left as it was. Few candidates meet any floor over
        // four names, so the report proposes nothing and says so, and every stage is recorded in the state.
        using var store = await FixtureExpectations.WithTwoNights();

        var root = Path.Combine(store.Root, SweepFolder.Name);
        var run = Path.Combine(root, SweepFolder.RunName(SweepNoon));

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, SweepFolder.ReportFile), "the first run's report");
        Directory.CreateDirectory(run);

        var log = new StringWriter(CultureInfo.InvariantCulture);
        var runner = new SweepRunner(FixedClock.At(SweepNoon, SessionZones.UnitedStates), store.Root, store.DatabaseFile, run, log);
        var exit = await runner.RunAsync();

        SweepRelease(store);

        var state = runner.Load();

        Assert.True(exit == 0, log.ToString());
        Assert.True(state.Finished);
        Assert.True(state.CandidatesDone && state.PointInTimeDone && state.RanksDone && state.TrialsDone && state.CrossDone);
        Assert.Equal(0, state.PointInTimeDifferences);
        Assert.Equal(SweepRunner.BuildId, state.Build);
        Assert.Equal(["read", "series", "candidates", "point-in-time", "stage1", "step-b", "step-c"], state.Seconds.Keys.Take(7));
        Assert.True(File.Exists(Path.Combine(run, "point-in-time.json")));
        Assert.True(File.Exists(Path.Combine(run, "conditions.json")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(run, "cross"), "chunk-*.json"));

        var report = File.ReadAllText(Path.Combine(run, SweepFolder.ReportFile));

        Assert.Contains("7. The point-in-time result", report, StringComparison.Ordinal);
        Assert.Contains("3. The seven conditions", report, StringComparison.Ordinal);
        Assert.Contains("No difference.", report, StringComparison.Ordinal);
        Assert.Equal("the first run's report", File.ReadAllText(Path.Combine(root, SweepFolder.ReportFile)));
        Assert.Equal([SweepFolder.RunName(SweepNoon)], SweepFolder.Runs(root));
        Assert.Equal(Path.Combine(run, SweepFolder.ReportFile), SweepFolder.NewestReport(root));

        // A finished run is never written again.
        Assert.Equal(2, await runner.RunAsync());
        Assert.DoesNotContain(SweepRows(store, "SELECT run_id FROM run_log;"), row => row.Contains("sweep", StringComparison.OrdinalIgnoreCase));
    }
}
