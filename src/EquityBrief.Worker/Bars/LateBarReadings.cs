using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Members;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// The readings and levels of a bar that arrived in the off-peak run, worked by the night's own unchanged components over a
// scratch store holding the stocks that arrived and what those components read, and only those stocks' rows of the night
// copied back. The scratch store then reads each stock's rules the same way, the bars of the rest of its index beside it
// for what the rules read across members, and names the rules it passes on the stock's ask row; nothing else of what ran
// over it comes back, so no pick, list, trade, record or ledger row moves.
// see: A late bar's readings are worked over a scratch store by the night's own components, and no pick or ledger row moves
public sealed class LateBarReadings : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read | Touch.Insert),
            new StoreTouch(Store.Swing, Touch.Read | Touch.Insert),
            new StoreTouch(Store.VolumeProfile, Touch.Read | Touch.Insert),
            new StoreTouch(Store.Level, Touch.Read | Touch.Insert),
            new StoreTouch(Store.Ladder, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.SwingReading, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.MemberReading, Touch.Read | Touch.Update),
            new StoreTouch(Store.LateBarAsk, Touch.Update),
            new StoreTouch(Store.SecTicker, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FamilyResult, Touch.Read),
            new StoreTouch(Store.IndexFamilyResult, Touch.Read),
            new StoreTouch(Store.DipResult, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    // What the components this runs read, left out where it is large and none of them reads it for a stock.
    static readonly HashSet<string> LeftOut = new(StringComparer.Ordinal)
    {
        "series_state", "run_log", "source_document", "rule_night", "setup_night", "loop_reference", "loop_link", "loop_test",
        "release_answer_edge", "release_probe", "release_day", "theme_section", "filed_fact_pull", "filing_day", "insider_read",
    };

    // A stock with no bar on the night holds none of these for it, and a swing it holds stands as it was found.
    const string CopyIndicators = "INSERT INTO indicator SELECT * FROM scratch.indicator WHERE ticker = $ticker AND session_date = $session ON CONFLICT DO NOTHING;";
    const string CopySwings = "INSERT INTO swing SELECT * FROM scratch.swing WHERE ticker = $ticker ON CONFLICT DO NOTHING;";
    const string CopyProfile = "INSERT INTO volume_profile SELECT * FROM scratch.volume_profile WHERE ticker = $ticker AND as_of = $session ON CONFLICT DO NOTHING;";
    const string CopyLevels = "INSERT INTO level SELECT * FROM scratch.level WHERE ticker = $ticker AND as_of = $session ON CONFLICT DO NOTHING;";

    // The night wrote these two for an S&P 500 member holding no bar, and the bar's replace them.
    const string CopyLadder = "INSERT OR REPLACE INTO ladder SELECT * FROM scratch.ladder WHERE ticker = $ticker AND as_of = $session;";
    const string CopyReading = "INSERT OR REPLACE INTO swing_reading SELECT * FROM scratch.swing_reading WHERE ticker = $ticker AND session_date = $session;";

    // The stock's own prices on its reading row of the night; the figures read across its peers stand as the night read them.
    const string CopyMember = @"
        UPDATE member_reading SET
            close = (SELECT s.close FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date),
            dollar_volume = (SELECT s.dollar_volume FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date),
            company_value = (SELECT s.company_value FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date),
            year_high = (SELECT s.year_high FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date),
            nearness = (SELECT s.nearness FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date),
            since_high = (SELECT s.since_high FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date),
            volume_ratio = (SELECT s.volume_ratio FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date)
        WHERE ticker = $ticker AND session_date = $session
          AND EXISTS (SELECT 1 FROM scratch.member_reading s WHERE s.index_code = member_reading.index_code AND s.ticker = member_reading.ticker AND s.session_date = member_reading.session_date);
    ";

    const string SetQualified = @"
        UPDATE late_bar_ask SET qualified = $qualified, detail = detail || $said
        WHERE ticker = $ticker AND session_date = $session AND stage = 'off-peak';
    ";

    // The scratch store's table of bars, which it takes from the store and writes nowhere but itself.
    const string ScratchBars = "bar";

    const string IndexOf = @"
        SELECT index_code FROM membership
        WHERE ticker = $ticker AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY joined DESC LIMIT 1;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    public const string Stage = "late-bar-readings";

    readonly IClock clock;
    readonly string databaseFile;

    public LateBarReadings(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // What it says on the step's row: nothing where all went through, and the cause where it did not, the bars standing.
    public async Task<string> WorkAsync(string runId, DateOnly session, IReadOnlyList<string> tickers, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var scratchRoot = Path.Combine(Path.GetTempPath(), "equitybrief-late-bars", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(scratchRoot);

        var scratch = Path.Combine(scratchRoot, "late-bars.db");

        try
        {
            MigrationRunner.Standard().Apply(scratch);

            var evening = new DateTimeOffset(session.Year, session.Month, session.Day, 21, 10, 0, TimeSpan.Zero);
            var night = FixedClock.At(evening, SessionZones.UnitedStates);
            var indexOf = await IndicesAsync(session, tickers, cancellation);

            await LoadAsync(scratch, session, tickers, cancellation);

            // The stocks' own readings, the steps reading the stocks' bars alone.
            await new IndicatorEngine(night, scratch).RunAsync("late-bars-indicators", cancellation);
            await new SwingFinder(night, scratch).RunAsync("late-bars-swings", cancellation);
            await new VolumeProfileBuilder(night, scratch).RunAsync("late-bars-profile", cancellation);
            await new LevelBuilder(night, scratch).RunAsync("late-bars-levels", cancellation);

            var large = indexOf.Where(pair => pair.Value == IndexFamilies.LargeIndex).Select(pair => pair.Key).ToArray();

            if (large.Length > 0)
            {
                await new LadderBuilder(night, scratch).RunAsync(IndexFamilies.LargeIndex, "late-bars-ladders", cancellation);
            }

            // The rest of each stock's index beside it, for what the rules and readings read across members.
            await AddMembersAsync(scratch, session, [.. indexOf.Values.Distinct(StringComparer.Ordinal)], tickers, cancellation);

            if (large.Length > 0)
            {
                await new SwingReader(night, scratch).RunAsync(IndexFamilies.LargeIndex, "late-bars-readings", cancellation);
            }

            await new MemberReader(night, scratch).RunAsync(IndexFamilies.LargeIndex, "late-bars-members", cancellation, IndexFamilies.Indices);
            await NightsMarketAsync(scratch, session, cancellation);
            await CopyBackAsync(scratch, session, tickers, cancellation);

            var rules = await RulesAsync(scratch, night, session, indexOf, large.Length > 0, cancellation);

            foreach (var ticker in tickers)
            {
                await SetQualifiedAsync(session, ticker, rules.GetValueOrDefault(ticker) ?? [], string.Empty, cancellation);
            }

            await AppendAsync(runId, started, tickers.Count, "ok", string.Join("; ", tickers.Select(ticker => ticker + ": " + (rules.GetValueOrDefault(ticker) is { Count: > 0 } passed ? LateBars.WouldHaveQualified(passed) : "no rule read passes it"))), cancellation);

            return string.Empty;
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            var cause = "; its readings or rules not worked: " + failure.Message;

            foreach (var ticker in tickers)
            {
                await SetQualifiedAsync(session, ticker, null, cause, cancellation);
            }

            await AppendAsync(runId, started, 0, "partial", failure.Message, cancellation);

            return cause;
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            try
            {
                Directory.Delete(scratchRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    async Task<IReadOnlyDictionary<string, string>> IndicesAsync(DateOnly session, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var indices = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var ticker in tickers)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = IndexOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", Stamp(session));

            if (await command.ExecuteScalarAsync(cancellation) is string index)
            {
                indices[ticker] = index;
            }
        }

        return indices;
    }

    // The scratch store's rows: every row of the stocks that arrived from each table keyed on a stock, those of their filers
    // from each keyed on a filer or a filing, and the tables holding no stock whole, less those no component here reads.
    async Task LoadAsync(string scratch, DateOnly session, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        await using var connection = await OpenWithLiveAsync(scratch, cancellation);

        await ExecuteAsync(connection, "CREATE TEMP TABLE late_names (ticker TEXT PRIMARY KEY);", cancellation);

        foreach (var ticker in tickers)
        {
            await using var name = connection.CreateCommand();

            name.CommandText = "INSERT INTO late_names VALUES ($ticker);";
            name.Parameters.AddWithValue("$ticker", ticker);

            await name.ExecuteNonQueryAsync(cancellation);
        }

        var tables = new List<string>();

        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT name FROM live.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

            await using var reader = await read.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables.Where(table => !LeftOut.Contains(table)))
        {
            var columns = await ColumnsAsync(connection, table, cancellation);
            var condition = columns.Contains("ticker", StringComparer.Ordinal) ? table is "membership" or "company" ? string.Empty : "WHERE ticker IN (SELECT ticker FROM late_names)"
                : columns.Contains("cik", StringComparer.Ordinal) ? "WHERE cik IN (SELECT cik FROM live.sec_ticker WHERE ticker IN (SELECT ticker FROM late_names))"
                : columns.Contains("accession", StringComparer.Ordinal) ? "WHERE accession IN (SELECT accession FROM live.release_document WHERE cik IN (SELECT cik FROM live.sec_ticker WHERE ticker IN (SELECT ticker FROM late_names)))"
                : string.Empty;
            var list = string.Join(", ", columns.Select(column => "\"" + column + "\""));

            await ExecuteAsync(connection, "INSERT OR REPLACE INTO main.\"" + table + "\" (" + list + ") SELECT " + list + " FROM live.\"" + table + "\" " + condition + ";", cancellation);
        }

        SqliteConnection.ClearPool(connection);
    }

    async Task AddMembersAsync(string scratch, DateOnly session, IReadOnlyList<string> indices, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        await using var connection = await OpenWithLiveAsync(scratch, cancellation);

        var columns = await ColumnsAsync(connection, ScratchBars, cancellation);
        var list = string.Join(", ", columns.Select(column => "\"" + column + "\""));

        foreach (var index in indices)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = "INSERT OR IGNORE INTO main.\"" + ScratchBars + "\" (" + list + ") SELECT " + list + " FROM live.\"" + ScratchBars + "\" WHERE ticker IN (SELECT ticker FROM live.membership WHERE index_code = $index AND (\"left\" IS NULL OR \"left\" > $session));";
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$session", Stamp(session));
            command.CommandTimeout = 600;

            await command.ExecuteNonQueryAsync(cancellation);
        }

        SqliteConnection.ClearPool(connection);
    }

    // The market's readings as the night stored them, over every member's indicators, which the readers above could read
    // only over the indicators the scratch store holds. The rules read the market through these.
    async Task NightsMarketAsync(string scratch, DateOnly session, CancellationToken cancellation)
    {
        await using var connection = await OpenWithLiveAsync(scratch, cancellation);

        foreach (var table in new[] { "market_reading", "switch_reading" })
        {
            await using var command = connection.CreateCommand();

            command.CommandText = "INSERT OR REPLACE INTO main." + table + " SELECT * FROM live." + table + " WHERE session_date = $session;";
            command.Parameters.AddWithValue("$session", Stamp(session));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        SqliteConnection.ClearPool(connection);
    }

    async Task<SqliteConnection> OpenWithLiveAsync(string scratch, CancellationToken cancellation)
    {
        var connection = new SqliteConnection(StoreConnection.For(scratch));

        await connection.OpenAsync(cancellation);

        await using var attach = connection.CreateCommand();

        attach.CommandText = "ATTACH DATABASE $live AS live;";
        attach.Parameters.AddWithValue("$live", databaseFile);

        await attach.ExecuteNonQueryAsync(cancellation);

        return connection;
    }

    // Only the stocks' rows of the night go back, in one transaction, so a page never reads half of them.
    async Task CopyBackAsync(string scratch, DateOnly session, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $scratch AS scratch;";
            attach.Parameters.AddWithValue("$scratch", scratch);

            await attach.ExecuteNonQueryAsync(cancellation);
        }

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var ticker in tickers)
            {
                foreach (var statement in new[] { CopyIndicators, CopySwings, CopyProfile, CopyLevels, CopyLadder, CopyReading, CopyMember })
                {
                    await using var command = connection.CreateCommand();

                    command.Transaction = transaction;
                    command.CommandText = statement;
                    command.Parameters.AddWithValue("$ticker", ticker);

                    if (statement != CopySwings)
                    {
                        command.Parameters.AddWithValue("$session", Stamp(session));
                    }

                    await command.ExecuteNonQueryAsync(cancellation);
                }
            }

            await transaction.CommitAsync(cancellation);
        }

        await using var detach = connection.CreateCommand();

        detach.CommandText = "DETACH DATABASE scratch;";

        await detach.ExecuteNonQueryAsync(cancellation);
    }

    // Each stock's rules through the night's own steps over the scratch store. The S&P 500's three families read its filter
    // and family steps, the S&P 400's and 600's the index families' step, and every index's dip the dip's own.
    async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> RulesAsync(string scratch, IClock night, DateOnly session, IReadOnlyDictionary<string, string> indexOf, bool large, CancellationToken cancellation)
    {
        var ledger = new SetupLedger(night, scratch);

        if (large)
        {
            await new ShortlistBuilder(night, scratch).RunAsync(IndexFamilies.LargeIndex, "late-bars-listings", night.UtcNow, cancellation);
            await new SwingFilter(night, scratch).RunAsync(IndexFamilies.LargeIndex, "late-bars-filter", null, cancellation);
            await new FamilyEvaluator(night, scratch).RunAsync("late-bars-families", FamilyRuleShadow.None, cancellation, token => ledger.ReadingsTonightAsync(IndexFamilies.LargeIndex, token));
        }

        var register = await new CandidateRegistrar(night, scratch).RowsAsync(cancellation);
        var indices = await new IndexFamilies(night, scratch).RunAsync("late-bars-index-families", cancellation, register, clock.UtcNow, ledger.ReadingsTonightAsync);
        var dips = await new DipFamily(night, scratch).RunAsync("late-bars-dips", cancellation, clock.UtcNow, ledger.ReadingsTonightAsync);

        // A step that could not read a stock's index is not a stock that passed nothing.
        foreach (var index in indexOf.Values.Distinct(StringComparer.Ordinal))
        {
            var fault = indices.Nights.FirstOrDefault(one => one.Index == index)?.Fault ?? dips.Indices.FirstOrDefault(one => one.Index == index)?.Fault;

            if (fault is not null)
            {
                throw new InvalidOperationException(fault);
            }
        }

        var passed = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        await using var connection = new SqliteConnection(StoreConnection.For(scratch));
        await connection.OpenAsync(cancellation);

        foreach (var (ticker, index) in indexOf)
        {
            var rows = new List<string>();

            async Task ReadAsync(string sql, string? fixedFamily)
            {
                await using var command = connection.CreateCommand();

                command.CommandText = sql;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session", Stamp(session));
                command.Parameters.AddWithValue("$index", index);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    rows.Add(fixedFamily ?? reader.GetString(0));
                }
            }

            if (index == IndexFamilies.LargeIndex)
            {
                await ReadAsync("SELECT 'pullback' FROM gate_result WHERE ticker = $ticker AND session_date = $session AND passed = 1 AND $index = $index;", SetupFamilies.Pullback);
                await ReadAsync("SELECT family FROM family_result WHERE ticker = $ticker AND session_date = $session AND passed = 1 AND $index = $index;", null);
            }
            else
            {
                await ReadAsync("SELECT family FROM index_family_result WHERE ticker = $ticker AND session_date = $session AND index_code = $index AND passed = 1;", null);
            }

            await ReadAsync("SELECT 'dip' FROM dip_result WHERE ticker = $ticker AND session_date = $session AND index_code = $index AND passed = 1;", DipRule.Name);

            passed[ticker] = [.. rows.Select(family => SetupFamilies.Named(family)?.Label ?? (family == DipRule.Name ? SetupFamilies.Dips.Label : family)).Distinct(StringComparer.Ordinal)];
        }

        return passed.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
    }

    async Task AppendAsync(string runId, DateTimeOffset started, int rows, string outcome, string detail, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task SetQualifiedAsync(DateOnly session, string ticker, IReadOnlyList<string>? rules, string said, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = SetQualified;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session", Stamp(session));
        command.Parameters.AddWithValue("$qualified", rules is null ? DBNull.Value : LateBars.QualifiedText(rules));
        command.Parameters.AddWithValue("$said", said);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<IReadOnlyList<string>> ColumnsAsync(SqliteConnection connection, string table, CancellationToken cancellation)
    {
        var columns = new List<string>();

        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT name FROM pragma_table_info($table, 'live');";
        command.Parameters.AddWithValue("$table", table);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.CommandTimeout = 600;

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
