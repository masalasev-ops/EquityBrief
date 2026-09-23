using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Core.Volume;
using EquityBrief.Data;
using EquityBrief.Data.Swings;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Rules;

public sealed record VersionScoreOutcome(
    int Versions,
    int NamesScored,
    int RowsWritten,
    int RowsDropped,
    int NameNightsSkipped,
    int RowsKept = 0,
    int NamesNotComputed = 0,
    int Replayed = 0,
    int ReplayedMergeDistance = 0,
    int BlocksFrozen = 0);

// The rule version scorer. Replays tonight's name-nights under every open version
// of each ladder rule, from stored bars, and stores the plan each produced.
//
// Counterfactual and free. The bars are already stored, so scoring a version
// costs arithmetic and never a request: what it adds to the night is the ladder
// stage run again per version, and the level stage as well for a version of the
// merge distance, which is what the bound is arithmetic over.
//
// It stops the night at its own step where a live rule's parameters or code have
// moved while a window measuring it is open. That is not a note, because a
// measurement whose subject moved says nothing about either version: the scores
// already written under the window were computed against the old rule and
// tonight's would be computed against the new one, and a record holding both is a
// record of neither.
// see: Adding a candidate later restarts the clock
public sealed class RuleVersionScorer : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.RuleVersion, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.VersionScore, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.VersionBlock, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "rule-versions";

    public const string Ok = "ok";

    // A refused open or close is a row of its own under this outcome rather than
    // under ok with the refusal in the detail, so the run page and a reader of
    // the log can tell an attempt that was turned away from one that happened.
    public const string Refused = "refused";

    // The pin of every source the live ladder rules and their replay run through, less
    // the line below; `rule-versions-scored` derives the list from the compiled code.
    // see: The ladder rules' code version pins every source a live ladder rule or its replay runs through
    public const string CodeVersionDeclaration = "public const string CodeVersion =";

    public const string CodeVersion = "9c5b1776d336";

    public static IReadOnlyList<string> CodeVersionSources { get; } =
    [
        "src/EquityBrief.Core/Ladders/LadderSeries.cs",
        "src/EquityBrief.Core/Ladders/TrendSeries.cs",
        "src/EquityBrief.Core/Levels/LevelSeries.cs",
        "src/EquityBrief.Core/Prices/PriceForm.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Core/Rules/RuleVersions.cs",
        "src/EquityBrief.Core/Swings/SwingSeries.cs",
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Data/Swings/StoredSwings.cs",
        "src/EquityBrief.Worker/Ladders/LadderBuilder.cs",
        "src/EquityBrief.Worker/Levels/LevelBuilder.cs",
        "src/EquityBrief.Worker/Rules/RuleVersionScorer.cs",
    ];

    const string ReadVersions = @"
        SELECT rule, version, parameters, parameters_hash, code_version, opened_at, closed_at, replaced_by, evidence
        FROM rule_version
        ORDER BY rule, opened_at, version;
    ";

    const string OpenWindow = @"
        INSERT INTO rule_version (
            rule, version, parameters, parameters_hash, code_version, opened_at, closed_at, replaced_by)
        VALUES ($rule, $version, $parameters, $parameters_hash, $code_version, $opened_at, NULL, NULL);
    ";

    // The one change a version row ever takes, and it is the close: its instant,
    // the evidence it was closed on, and the version a replacement opened.
    const string CloseWindow = @"
        UPDATE rule_version
        SET closed_at = $closed_at, replaced_by = $replaced_by, evidence = $evidence
        WHERE rule = $rule AND version = $version AND opened_at = $opened_at AND closed_at IS NULL;
    ";

    // A night writes its set again, since it recomputes the bands it replays against.
    const string WriteScore = @"
        INSERT INTO version_score (ticker, session_date, rule, version, opened_at, plan, sample)
        VALUES ($ticker, $session_date, $rule, $version, $opened_at, $plan, $sample)
        ON CONFLICT (ticker, session_date, rule, version, opened_at) DO UPDATE SET
            plan = excluded.plan,
            sample = excluded.sample;
    ";

    // A backfill writes only the scores not stored yet and keeps the rest.
    // see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored
    const string KeepScore = @"
        INSERT INTO version_score (ticker, session_date, rule, version, opened_at, plan, sample)
        VALUES ($ticker, $session_date, $rule, $version, $opened_at, $plan, $sample)
        ON CONFLICT (ticker, session_date, rule, version, opened_at) DO NOTHING;
    ";

    // Kept while the bars a score was replayed from are, whatever night is scored.
    // see: Every computed table's writer is its own deleter
    const string DropOlderThan = @"
        DELETE FROM version_score WHERE session_date < $oldest;
    ";

    // The blocks of one window already frozen, with the origin the first of them
    // fixed. A frozen block is never recomputed: the rows it was computed from are
    // dropped a year back, and a sum that moved after a look would make that look's
    // boundary one found over an arrangement the record no longer has.
    const string FrozenBlocks = @"
        SELECT block, origin FROM version_block
        WHERE rule = $rule AND version = $version AND opened_at = $opened_at
        ORDER BY block;
    ";

    // The first session a window's score counts for, which is the session its blocks
    // are counted from. An in sample score is left out here as it is everywhere a
    // record is read, so a backfill over the nights before the window opened cannot
    // put block 0 behind the window itself.
    // see: A version's score counts only for a session after the New York date its window opened on
    const string FirstScoredSession = @"
        SELECT MIN(session_date) FROM version_score
        WHERE rule = $rule AND version = $version AND opened_at = $opened_at AND sample = $sample;
    ";

    // Every setup the live rule listed on a session one window's score counts for,
    // with the flag that says that version's label takes it away, and the outcome the
    // store already holds. No ticker is selected and none is needed: what a block
    // freezes to is a sum over setups and the sessions they were listed on.
    //
    // Keyed on the window's instant and not on the version's name alone, because a
    // version closed and opened again under one name is two windows, and a session
    // scored under both would otherwise be counted twice.
    // see: A version's record belongs to the window its scores were written under and never to the version's name
    const string ScoredSetups = @"
        SELECT v.session_date, f.outcome, f.null_win, f.null_win_at_sensitivity,
               f.break_even, f.return_pct, f.planned_risk, f.on_earnings,
               CASE WHEN json_extract(v.plan, '$.trend') = $downtrend AND d.trend_state <> $downtrend THEN 1 ELSE 0 END
        FROM version_score v
        JOIN ladder d ON d.ticker = v.ticker AND d.as_of = v.session_date
        JOIN forward_return f
            ON f.ticker = v.ticker AND f.session_date = v.session_date AND f.horizon = $horizon
        WHERE v.rule = $rule AND v.version = $version AND v.opened_at = $opened_at AND v.sample = $sample
        ORDER BY v.session_date;
    ";

    // A block written once and never again. The conflict keeps the frozen row rather
    // than replacing it, which is the second guard under the first: the sums are not
    // recomputed for a block already held, and a write that reached one anyway would
    // still leave it as it was.
    const string FreezeBlock = @"
        INSERT INTO version_block (
            rule, version, opened_at, block, origin,
            version_excess, version_setups, live_excess, live_setups,
            version_null_sum, version_null_spread, live_null_sum, live_null_spread, frozen_at)
        VALUES (
            $rule, $version, $opened_at, $block, $origin,
            $version_excess, $version_setups, $live_excess, $live_setups,
            $version_null_sum, $version_null_spread, $live_null_sum, $live_null_spread, $frozen_at)
        ON CONFLICT (rule, version, opened_at, block) DO NOTHING;
    ";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string BarsOn = "SELECT COUNT(*) FROM bar WHERE session_date = $session;";

    const string PlansOn = "SELECT COUNT(*) FROM ladder WHERE as_of = $session;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    const string NamesWithABarOn = @"
        SELECT DISTINCT ticker FROM bar WHERE session_date = $session ORDER BY ticker;
    ";

    // The bands and the trend the night being scored computed, and no other
    // night's: a session no night computed has none, so a name there is left out
    // rather than replayed against an earlier night's set.
    // Unordered, and put in price order once the edges are decimals, for the
    // reason the ladder builder's band query gives.
    // see: A stored price is chosen and ordered by its value and never by the text it is stored as
    const string BandsFor = @"
        SELECT low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members
        FROM level
        WHERE ticker = $ticker AND as_of = $session;
    ";

    const string TrendFor = @"
        SELECT trend_state FROM ladder WHERE ticker = $ticker AND as_of = $session;
    ";

    // The labels of the nights before the one being scored, newest first, which
    // is what a version that holds a name in downtrend after its label left
    // reads. The stored raw labels and never a version's own, because the hold
    // is stated over what the nights recorded.
    // see: The trend rule is a fifth ladder rule a version replays, and none of its three versions is live
    const string LabelsBefore = @"
        SELECT trend_state FROM ladder
        WHERE ticker = $ticker AND as_of < $session
        ORDER BY as_of DESC
        LIMIT $nights;
    ";

    const string ScaleOn = @"
        SELECT close, raw_close FROM bar WHERE ticker = $ticker AND session_date = $session;
    ";

    const string RecentFor = @"
        SELECT session_date, high, low, close FROM bar
        WHERE ticker = $ticker AND session_date <= $session
        ORDER BY session_date DESC
        LIMIT $window;
    ";

    // One stored reading at or before a session: the typical move the replay
    // scales its prices by, and the two averages a trend version reads.
    const string LatestIndicatorFor = @"
        SELECT value FROM indicator
        WHERE ticker = $ticker AND name = $name AND session_date <= $session
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public RuleVersionScorer(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The night's own step, which writes the night's set again where it is re-run.
    public Task<VersionScoreOutcome> RunAsync(
        DateOnly session,
        string runId,
        CancellationToken cancellation = default) =>
        ScoreAsync(session, runId, keepStored: false, cancellation);

    // A past night scored under the windows open now, keeping every score already stored.
    // see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored
    public Task<VersionScoreOutcome> BackfillAsync(
        DateOnly session,
        string runId,
        CancellationToken cancellation = default) =>
        ScoreAsync(session, runId, keepStored: true, cancellation);

    async Task<VersionScoreOutcome> ScoreAsync(
        DateOnly session,
        string runId,
        bool keepStored,
        CancellationToken cancellation)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        // The windows the store holds open when the step runs, so a night replayed after a close and a reopen reads the new ones.
        var open = (await VersionsAsync(connection, cancellation))
            .Where(row => row.ClosedAt is null)
            .OrderBy(row => row.Rule, StringComparer.Ordinal)
            .ThenBy(row => row.OpenedAt)
            .ToArray();

        // The night stops here where a live rule has moved inside an open window.
        // Before anything is scored, because a score written under a window whose
        // subject has moved is the thing being prevented rather than a row to be
        // corrected afterwards.
        if (RuleVersions.Drifted(open, HashesNow()) is { Count: > 0 } moved)
        {
            throw new InvalidOperationException(
                "a live ladder rule moved while a window measuring it is open: " + string.Join("; ", moved));
        }

        // The live version of each rule is what the night already computed, so
        // it is scored by having been run and is not replayed here.
        var replayed = open
            .Where(row => !string.Equals(row.Version, RuleVersions.Live, StringComparison.Ordinal))
            .ToArray();

        var mergeDistance = replayed.Count(row => LadderRules.ReplaysLevels(row.Rule));
        var names = await NamesAsync(connection, session, cancellation);
        var written = 0;
        var kept = 0;
        var skipped = 0;
        var notComputed = 0;

        // The scores, the drop and the row are one write, so a row that cannot be
        // written leaves nothing of the step behind it.
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        // No version to replay, no bands read: level rows written before member sources were stored cannot be read.
        foreach (var ticker in replayed.Length == 0 ? Array.Empty<string>() : names)
        {
            var inputs = await InputsAsync(connection, ticker, session, cancellation);

            if (inputs is null)
            {
                notComputed++;

                continue;
            }

            foreach (var version in replayed)
            {
                // Which member is an average or a touch cannot be read off a band set stored without sources.
                if (!inputs.Value.MemberSources && LadderRules.ReadsMembers(version.Rule))
                {
                    skipped++;

                    continue;
                }

                var plan = Replayed(version, inputs.Value);

                await using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = keepStored ? KeepScore : WriteScore;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session_date", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$rule", version.Rule);
                command.Parameters.AddWithValue("$version", version.Version);
                command.Parameters.AddWithValue("$opened_at", RuleVersions.Stored(version.OpenedAt));
                command.Parameters.AddWithValue("$plan", plan);
                command.Parameters.AddWithValue("$sample", RuleVersions.SampleOf(session, clock.SessionDateAt(version.OpenedAt)));

                var wrote = await command.ExecuteNonQueryAsync(cancellation);

                written += wrote;
                kept += 1 - wrote;
            }
        }

        // Before the drop, so a block that completes tonight is frozen off the rows
        // tonight still holds rather than off what the retention leaves behind.
        var frozen = await FreezeAsync(connection, transaction, replayed, cancellation);
        var dropped = await DroppedAsync(connection, transaction, cancellation);

        var detail =
            FormattableString.Invariant($"{open.Length} open version(s), {replayed.Length} replayed with {mergeDistance} of the merge distance, over {names.Count} name(s), ")
            + FormattableString.Invariant($"{written} score(s) written, {kept} already stored and kept, {notComputed} name(s) with no bands or plan of that session, ")
            + FormattableString.Invariant($"{skipped} name-night(s) skipped for a band set stored without member sources, {dropped} dropped, {frozen} block(s) frozen");

        await RecordAsync(connection, runId, startedAt, written, detail, cancellation, transaction: transaction);
        await transaction.CommitAsync(cancellation);

        return new VersionScoreOutcome(open.Length, names.Count, written, dropped, skipped, kept, notComputed, replayed.Length, mergeDistance, frozen);
    }

    // The blocks that have completed, frozen so a version's record outlives the rows
    // it was computed from.
    //
    // Only a rule whose versions can only take a setup away, because a version of any
    // other rule produces plans whose outcomes nothing computed, and only an open
    // window, because a closed one carries the evidence it was closed on in its own
    // row. A block already held is not recomputed and not rewritten, which is what
    // keeps a sum from moving after the look that read it, so a backfill that lands a
    // session inside a frozen block changes nothing.
    //
    // The as-of is the newest stored session and not the session being scored, so a
    // backfill of a past night and a night of its own agree on which blocks are whole.
    // see: A version's record is read from the blocks frozen as each completed
    async Task<int> FreezeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<RuleVersionRow> windows,
        CancellationToken cancellation)
    {
        if (await NewestStoredSessionAsync(connection, transaction, cancellation) is not { } asOf)
        {
            return 0;
        }

        var frozen = 0;

        foreach (var window in windows.Where(row => LadderRules.OnlyRemovesSetups(row.Rule)))
        {
            var held = await FrozenBlocksAsync(connection, transaction, window, cancellation);

            if (held.Blocks.Count >= Looks.Maximum)
            {
                continue;
            }

            // The stored origin wherever a block is held, which is what fixes it:
            // retention moves the earliest scored session forward, and an origin read
            // again from the store would re-cut the blocks under a record being read.
            var origin = held.Origin ?? await FirstScoredSessionAsync(connection, transaction, window, cancellation);

            if (origin is not { } first)
            {
                continue;
            }

            var setups = await ScoredSetupsAsync(connection, transaction, window, cancellation);
            var standing = held.Blocks.Count;

            var whole = setups
                .GroupBy(setup => Blocks.Of(first, setup.Setup.Session))
                .Where(block => !held.Blocks.Contains(block.Key))
                .Where(block => Blocks.Complete(first, block.Key, asOf))
                .OrderBy(block => block.Key);

            foreach (var block in whole)
            {
                // The last look is never extended, so a block past it is read by
                // nothing and is not written.
                if (standing >= Looks.Maximum)
                {
                    break;
                }

                var live = VersionRecord.Counted([.. block.Select(row => row.Setup)]);
                var mine = VersionRecord.Counted([.. block.Where(row => !row.Removed).Select(row => row.Setup)]);

                // A block holding no setup the store decided and computed a bar for is
                // not one the test is over.
                if (live.Count == 0)
                {
                    continue;
                }

                await WriteBlockAsync(connection, transaction, window, first, VersionRecord.Freeze(block.Key, live, mine), cancellation);

                standing++;
                frozen++;
            }
        }

        return frozen;
    }

    async Task<DateOnly?> NewestStoredSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static async Task<(IReadOnlySet<int> Blocks, DateOnly? Origin)> FrozenBlocksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RuleVersionRow window,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = FrozenBlocks;
        Window(command, window);

        var blocks = new HashSet<int>();
        DateOnly? origin = null;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            blocks.Add(reader.GetInt32(0));
            origin ??= DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return (blocks, origin);
    }

    static async Task<DateOnly?> FirstScoredSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RuleVersionRow window,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = FirstScoredSession;
        Window(command, window);
        command.Parameters.AddWithValue("$sample", RuleVersions.Scored);

        return await command.ExecuteScalarAsync(cancellation) is string first
            ? DateOnly.ParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static async Task<IReadOnlyList<(CandidateSetup Setup, bool Removed)>> ScoredSetupsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RuleVersionRow window,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = ScoredSetups;
        Window(command, window);
        command.Parameters.AddWithValue("$sample", RuleVersions.Scored);
        command.Parameters.AddWithValue("$horizon", ForwardReturnSeries.Setup);
        command.Parameters.AddWithValue("$downtrend", TrendState.Downtrend);

        var rows = new List<(CandidateSetup, bool)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add((
                new CandidateSetup(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    !reader.IsDBNull(7) && reader.GetInt64(7) == 1),
                reader.GetInt64(8) == 1));
        }

        return rows;
    }

    async Task WriteBlockAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RuleVersionRow window,
        DateOnly origin,
        VersionBlock block,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = FreezeBlock;
        Window(command, window);
        command.Parameters.AddWithValue("$block", block.Block);
        command.Parameters.AddWithValue("$origin", origin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$version_excess", block.VersionExcess);
        command.Parameters.AddWithValue("$version_setups", block.VersionSetups);
        command.Parameters.AddWithValue("$live_excess", block.LiveExcess);
        command.Parameters.AddWithValue("$live_setups", block.LiveSetups);
        command.Parameters.AddWithValue("$version_null_sum", block.VersionNullSum);
        command.Parameters.AddWithValue("$version_null_spread", block.VersionNullSpread);
        command.Parameters.AddWithValue("$live_null_sum", block.LiveNullSum);
        command.Parameters.AddWithValue("$live_null_spread", block.LiveNullSpread);
        command.Parameters.AddWithValue("$frozen_at", RuleVersions.Stored(clock.UtcNow));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // The three parameters every statement above keys a window by, in one place, so
    // a reader cannot key one by the version's name and reach two windows.
    static void Window(SqliteCommand command, RuleVersionRow window)
    {
        command.Parameters.AddWithValue("$rule", window.Rule);
        command.Parameters.AddWithValue("$version", window.Version);
        command.Parameters.AddWithValue("$opened_at", RuleVersions.Stored(window.OpenedAt));
    }

    // Why a past session may not be backfilled, or null: a day the exchange did
    // not trade, a session the store holds no bar for, and a session no night
    // computed, each of which has no night's plan to replay a version beside.
    // see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored
    public async Task<string?> BackfillRefusalAsync(DateOnly session, CancellationToken cancellation = default)
    {
        var named = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (session > ExchangeClosures.CoveredThrough || session < ExchangeClosures.CoveredFrom)
        {
            return FormattableString.Invariant(
                $"'{named}' is {(session > ExchangeClosures.CoveredThrough ? "past" : "before")} the exchange closure table, which covers ") +
                FormattableString.Invariant($"{ExchangeClosures.CoveredFrom:yyyy-MM-dd} through {ExchangeClosures.CoveredThrough:yyyy-MM-dd}, so whether it was a session is not known here");
        }

        if (!ExchangeClosures.IsSession(session))
        {
            return $"'{named}' is not a session: the exchange did not trade on it, so there is no night to score";
        }

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        if (await CountOnAsync(connection, BarsOn, named, cancellation) == 0)
        {
            await using var newest = connection.CreateCommand();

            newest.CommandText = NewestSession;

            var stored = await newest.ExecuteScalarAsync(cancellation) as string;

            return $"the store holds no bar for {named}, so no night has fetched it; the newest session it holds is {stored ?? "none"}";
        }

        return await CountOnAsync(connection, PlansOn, named, cancellation) == 0
            ? $"the store holds bars for {named} and no night's plan for it: it was fetched by a backfill or as a missed session and never computed as a night of its own, so a version replayed there would read another night's bands"
            : null;
    }

    static async Task<long> CountOnAsync(SqliteConnection connection, string sql, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$session", session);

        return (long)(await command.ExecuteScalarAsync(cancellation))!;
    }

    // Each live ladder rule's parameters as this build applies them. The merge
    // distance is a multiple of the typical move rather than a stored constant,
    // so what it carries is the multiple the level builder passes.
    //
    // One statement of them, read by the hash the night compares against and by
    // the refusal at the open: a version's parameters are named exactly as its
    // rule's are, because the replay reads each by name and falls back to the
    // live value where a name is missing, so a mistyped name would replay the
    // live rule under a version's name.
    public static IReadOnlyDictionary<string, double> LiveParameters(string rule)
    {
        var ladder = LadderRuleSet.Live.AsParameters;

        return rule switch
        {
            LadderRules.MergeDistance => new Dictionary<string, double>(StringComparer.Ordinal) { ["typicalMoveMultiple"] = Statistic.FromRatio(LevelSeries.MergeDistanceInTypicalMoves) },
            LadderRules.StopPlacement => new Dictionary<string, double>(StringComparer.Ordinal) { ["stopTrailsTheLastHigherLow"] = ladder["stopTrailsTheLastHigherLow"] },
            LadderRules.NearExitSkip => new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = ladder["nearExitInTypicalDays"] },
            LadderRules.ZoneEdgesFromNonAverageAnchors => new Dictionary<string, double>(StringComparer.Ordinal) { ["zoneEdgesFromNonAverageAnchorsOnly"] = ladder["zoneEdgesFromNonAverageAnchorsOnly"] },
            LadderRules.TrendRule => TrendRuleSet.Live.AsParameters,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "not a ladder rule this build carries"),
        };
    }

    // The hash of each live ladder rule as this build applies it.
    public static IReadOnlyDictionary<string, string> HashesNow() =>
        LadderRules.All.ToDictionary(
            rule => rule,
            rule => RuleVersions.Hash(LiveParameters(rule), CodeVersion),
            StringComparer.Ordinal);

    public readonly record struct ReplayInputs(
        IReadOnlyList<Level> Bands,
        decimal Close,
        decimal TypicalMove,
        IReadOnlyList<LadderBar> Recent,
        string TrendState,
        IReadOnlyList<LevelMember> Candidates,
        IReadOnlyList<LevelBar> Window,
        DateOnly AsOf,
        IReadOnlyList<decimal> SwingLows,
        bool MemberSources,
        decimal? ShortAverage = null,
        decimal? LongAverage = null,
        IReadOnlyList<string>? LabelsBefore = null);

    // The plan one version produces for one name-night.
    //
    // A merge distance version replays the level arithmetic first and then the
    // ladder arithmetic over the bands it produced, which is why it costs both
    // stages. Every other version reads the bands as the night computed them and
    // replays the ladder arithmetic alone.
    public static string Replayed(RuleVersionRow version, ReplayInputs inputs)
    {
        var parameters = RuleVersions.Read(version.Parameters);
        var bands = inputs.Bands;

        if (LadderRules.ReplaysLevels(version.Rule) && inputs.Candidates.Count > 0)
        {
            var multiple = parameters.GetValueOrDefault("typicalMoveMultiple", Statistic.FromRatio(LevelSeries.MergeDistanceInTypicalMoves));
            decimal distance;

            try
            {
                distance = inputs.TypicalMove * Statistic.ToPrice(multiple);
            }
            catch (OverflowException)
            {
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"'{version.Version}' of '{version.Rule}' replays a merge distance of {multiple} typical moves, which no price can hold; close it."));
            }

            bands = LevelSeries.For(inputs.Window, inputs.Candidates, inputs.Close, distance, inputs.AsOf);
        }

        var rules = new LadderRuleSet(
            NearExitInTypicalDays: (int)parameters.GetValueOrDefault("nearExitInTypicalDays", LadderSeries.NearExitInTypicalDays),
            StopTrailsTheLastHigherLow: parameters.GetValueOrDefault("stopTrailsTheLastHigherLow", 1) != 0,
            ZoneEdgesFromNonAverageAnchorsOnly: parameters.GetValueOrDefault("zoneEdgesFromNonAverageAnchorsOnly", 0) != 0);

        // The label the version applies, which the live values leave as the night
        // stored it. It is handed to the same builder the night ran rather than
        // built into it, because the trend rule selects the ladder's shape and
        // changes none of its arithmetic.
        var trend = TrendSeries.Applied(
            inputs.TrendState,
            inputs.Close,
            inputs.ShortAverage,
            inputs.LongAverage,
            inputs.LabelsBefore ?? [],
            new TrendRuleSet(
                DowntrendFromAverages: (int)parameters.GetValueOrDefault(TrendSeries.DowntrendFromAverages, TrendSeries.FromAveragesNever),
                NightsTheNewLabelHolds: (int)parameters.GetValueOrDefault(TrendSeries.NightsTheNewLabelHolds, TrendSeries.TheNightItIsRead)));

        // The swing lows the live stop trails, and no event, since a version's plan carries no second book.
        var plan = LadderSeries.For(bands, inputs.Close, inputs.TypicalMove, inputs.Recent, trend, inputs.SwingLows, rules: rules);

        return JsonSerializer.Serialize(new
        {
            tranches = plan.Tranches.Select(tranche => new
            {
                lowEdge = Money.ToStorage(tranche.LowEdge),
                highEdge = Money.ToStorage(tranche.HighEdge),
                condition = tranche.Condition.ToString(),
                stop = tranche.Stop is { } stop ? Money.ToStorage(stop) : null,
            }).ToArray(),
            exits = plan.Exits.Select(exit => new
            {
                lowEdge = Money.ToStorage(exit.LowEdge),
                traded = exit.Traded,
            }).ToArray(),
            invalidation = plan.Invalidation is { } low ? Money.ToStorage(low) : null,
            reason = plan.Reason,

            // The label the plan was built under, which is the stored one for
            // every rule but the trend rule. A version of that rule changes the
            // label and nothing else, so a score that kept only the tranches
            // could not be read against the label that produced them.
            trend,
        });
    }

    // ---- opening, replacing and closing a window ----

    // Why these parameters may not be a window of this rule, or null.
    //
    // Named apart from the write so a test puts each refusal to the same reader
    // the open does. A live window carries exactly the build's parameters,
    // since its hash is compared against the build's every night and a live
    // window opened at any other would stop the first night after it.
    // see: A version of a ladder rule is refused at values its replay would not apply as given, or at its rule's live values
    public static string? ParameterRefusal(
        string rule,
        string version,
        IReadOnlyDictionary<string, double> parameters)
    {
        if (!LadderRules.All.Contains(rule, StringComparer.Ordinal))
        {
            return null;
        }

        var live = LiveParameters(rule);

        if (!live.Keys.Order(StringComparer.Ordinal).SequenceEqual(parameters.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return
                $"'{rule}' is replayed from {string.Join(", ", live.Keys.Select(name => $"'{name}'"))} and was given " +
                $"{(parameters.Count == 0 ? "none" : string.Join(", ", parameters.Keys.Select(name => $"'{name}'")))}. " +
                "The replay reads each value by name and uses the live one where a name is missing, so a window " +
                "with any other names would measure the live rule under a version's name.";
        }

        if (parameters.Select(pair => ValueRefusal(pair.Key, pair.Value)).FirstOrDefault(refusal => refusal is not null) is { } unapplied)
        {
            return unapplied;
        }

        if (string.Equals(version, RuleVersions.Live, StringComparison.Ordinal))
        {
            return live.Any(pair => !parameters[pair.Key].Equals(pair.Value))
                ? $"a live window of '{rule}' carries the build's own parameters, " +
                  $"{RuleVersions.Write(live)}, and was given {RuleVersions.Write(parameters)}. A version with other " +
                  "values is opened under its own name beside the live one."
                : null;
        }

        return live.All(pair => parameters[pair.Key].Equals(pair.Value))
            ? $"'{version}' of '{rule}' carries the live values, {RuleVersions.Write(live)}, so it would store the " +
              "live rule's plans under a version's name."
            : null;
    }

    // Why the replay would not apply this value as given, or null.
    static string? ValueRefusal(string name, double value) => name switch
    {
        "typicalMoveMultiple" when !(value > 0 && AtPricePlaces(value)) => FormattableString.Invariant(
            $"'{name}' is a number of typical moves above 0 written to at most {PriceForm.Places} places, which is what the replay rounds it to, and was given {value}."),
        "nearExitInTypicalDays" when !(value >= 0 && value <= int.MaxValue && Math.Floor(value) == value) => FormattableString.Invariant(
            $"'{name}' is a whole number of typical days from 0, which is how the replay counts them, and was given {value}."),
        "stopTrailsTheLastHigherLow" or "zoneEdgesFromNonAverageAnchorsOnly" when value is not (0d or 1d) => FormattableString.Invariant(
            $"'{name}' is a flag of 1 or 0, which is all the replay reads it as, and was given {value}."),
        TrendSeries.DowntrendFromAverages when value is not (0d or 1d or 2d) => FormattableString.Invariant(
            $"'{name}' is {TrendSeries.FromAveragesNever} for the label as the night read it, {TrendSeries.FromAveragesBelowBoth} for a close below both averages read as a downtrend whatever the swings say, ")
            + FormattableString.Invariant($"or {TrendSeries.FromAveragesBelowBothUnderACross} for that reading only where the short average is below the long one, and was given {value}."),
        TrendSeries.NightsTheNewLabelHolds when !(value >= TrendSeries.TheNightItIsRead
            && value <= TrendSeries.MostNightsTheNewLabelHolds
            && Math.Floor(value) == value) => FormattableString.Invariant(
            $"'{name}' is a whole number of nights from {TrendSeries.TheNightItIsRead}, the night being scored among them, to {TrendSeries.MostNightsTheNewLabelHolds}, ")
            + FormattableString.Invariant($"which is the count of stored labels the replay reads behind the night, and was given {value}."),
        _ => null,
    };

    static bool AtPricePlaces(double value)
    {
        try
        {
            return double.IsFinite(value) && Statistic.FromRatio(Statistic.ToPrice(value)) == value;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public Task<string?> OpenLiveAsync(string rule, string runId, CancellationToken cancellation = default) =>
        OpenAsync(rule, RuleVersions.Live, LiveParameters(rule), runId, cancellation);

    // Each write below reads the windows, refuses or writes, and records the attempt
    // inside one transaction, so two commands racing for a rule's last window
    // admit one and a row that cannot be recorded takes its window write with it.
    // A refusal is recorded after the transaction is rolled back.
    public async Task<string?> OpenAsync(
        string rule,
        string version,
        IReadOnlyDictionary<string, double> parameters,
        string runId,
        CancellationToken cancellation = default)
    {
        var at = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        string? refusal;

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            var rows = await VersionsAsync(connection, cancellation, transaction);

            refusal = RuleVersions.Refusal(rows, rule, version, at) ?? ParameterRefusal(rule, version, parameters);

            if (refusal is null)
            {
                await OpenWindowAsync(connection, transaction, rule, version, parameters, at, cancellation);
                await RecordAsync(connection, runId, at, 1, $"opened '{version}' of '{rule}'", cancellation, transaction: transaction);
                await transaction.CommitAsync(cancellation);

                return null;
            }
        }

        await RecordAsync(connection, runId, at, 0, refusal, cancellation, Refused);

        return refusal;
    }

    // A change of version: the old window closed with the evidence that produced
    // the change and the name of the version replacing it, and that version opened,
    // in one write. The rule's cap is counted once the old window is closed, so a
    // rule at its cap can still be changed.
    // see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
    public async Task<string?> ReplaceAsync(
        string rule,
        string version,
        string with,
        IReadOnlyDictionary<string, double> parameters,
        string evidence,
        string runId,
        CancellationToken cancellation = default)
    {
        var at = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        string? refusal;

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            var rows = await VersionsAsync(connection, cancellation, transaction);
            var window = OpenWindowOf(rows, rule, version, at);

            refusal = string.IsNullOrWhiteSpace(evidence) ? NoEvidence
                : string.Equals(with, version, StringComparison.Ordinal) ? $"'{version}' cannot replace itself."
                : string.Equals(version, RuleVersions.Live, StringComparison.Ordinal) || string.Equals(with, RuleVersions.Live, StringComparison.Ordinal)
                    ? "a live window is replaced by closing it and opening the live window again, since it carries only the build's own values."
                : window is null ? $"'{version}' of '{rule}' has no open window to replace."
                : RuleVersions.Refusal([.. rows.Select(row => row == window ? row with { ClosedAt = at } : row)], rule, with, at)
                    ?? ParameterRefusal(rule, with, parameters);

            if (refusal is null)
            {
                await CloseWindowAsync(connection, transaction, window!, with, evidence, at, cancellation);
                await OpenWindowAsync(connection, transaction, rule, with, parameters, at, cancellation);
                await RecordAsync(
                    connection,
                    runId,
                    at,
                    2,
                    $"replaced '{version}' of '{rule}' with '{with}' {RuleVersions.Write(parameters)}, on: {evidence}",
                    cancellation,
                    transaction: transaction);
                await transaction.CommitAsync(cancellation);

                return null;
            }
        }

        await RecordAsync(connection, runId, at, 0, refusal, cancellation, Refused);

        return refusal;
    }

    // Closing keeps the row and every column it was opened with but the ones the
    // close writes, so the scores under it stay scores of the rule as it stood.
    //
    // The window closed is the one open now under that name, which there is at
    // most one of because a version is refused a second open window. A live
    // window is not closed while a version of its rule is open beside it, since
    // that would leave the version measured with nothing watching the code it
    // runs through; the versions are closed first. A close with nothing
    // replacing it takes its evidence as a replacement does, because ending a
    // measurement changes what is measured. Returns why nothing was closed, or null.
    // see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
    public async Task<string?> CloseAsync(
        string rule,
        string version,
        string evidence,
        string runId,
        CancellationToken cancellation = default)
    {
        var at = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        string? refusal;

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            var rows = await VersionsAsync(connection, cancellation, transaction);
            var open = RuleVersions.OpenAt(rows, at);
            var window = OpenWindowOf(rows, rule, version, at);

            refusal = string.IsNullOrWhiteSpace(evidence) ? NoEvidence
                : window is null ? $"'{version}' of '{rule}' has no open window to close."
                : string.Equals(version, RuleVersions.Live, StringComparison.Ordinal)
                    && open.Where(row => string.Equals(row.Rule, rule, StringComparison.Ordinal)
                        && !string.Equals(row.Version, RuleVersions.Live, StringComparison.Ordinal)).ToArray() is { Length: > 0 } beside
                    ? $"the live window of '{rule}' is not closed while {string.Join(", ", beside.Select(row => $"'{row.Version}'"))} " +
                      "is open beside it: a version with no live window beside it is measured with nothing watching the code " +
                      "it runs through. Close the versions first."
                    : null;

            if (refusal is null)
            {
                await CloseWindowAsync(connection, transaction, window!, null, evidence, at, cancellation);
                await RecordAsync(connection, runId, at, 1, $"closed '{version}' of '{rule}' with nothing replacing it, on: {evidence}", cancellation, transaction: transaction);
                await transaction.CommitAsync(cancellation);

                return null;
            }
        }

        await RecordAsync(connection, runId, at, 0, refusal, cancellation, Refused);

        return refusal;
    }

    const string NoEvidence = "a window is closed with the evidence that closed it, and the evidence given was blank.";

    // A refusal the verb reaches before the scorer does, recorded as the scorer's own are.
    public async Task RecordRefusalAsync(string runId, string refusal, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        await RecordAsync(connection, runId, clock.UtcNow, 0, refusal, cancellation, Refused);
    }

    static RuleVersionRow? OpenWindowOf(IReadOnlyList<RuleVersionRow> rows, string rule, string version, DateTimeOffset at) =>
        RuleVersions.OpenAt(rows, at).FirstOrDefault(row =>
            string.Equals(row.Rule, rule, StringComparison.Ordinal)
            && string.Equals(row.Version, version, StringComparison.Ordinal));

    static async Task OpenWindowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string rule,
        string version,
        IReadOnlyDictionary<string, double> parameters,
        DateTimeOffset at,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = OpenWindow;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$parameters", RuleVersions.Write(parameters));
        command.Parameters.AddWithValue("$parameters_hash", RuleVersions.Hash(parameters, CodeVersion));
        command.Parameters.AddWithValue("$code_version", CodeVersion);
        command.Parameters.AddWithValue("$opened_at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task CloseWindowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RuleVersionRow window,
        string? replacedBy,
        string evidence,
        DateTimeOffset at,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = CloseWindow;
        command.Parameters.AddWithValue("$closed_at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$replaced_by", (object?)replacedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$evidence", evidence);
        command.Parameters.AddWithValue("$rule", window.Rule);
        command.Parameters.AddWithValue("$version", window.Version);
        command.Parameters.AddWithValue("$opened_at", window.OpenedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    public async Task<IReadOnlyList<RuleVersionRow>> VersionsAsync(CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        return await VersionsAsync(connection, cancellation);
    }

    static async Task<IReadOnlyList<RuleVersionRow>> VersionsAsync(
        SqliteConnection connection,
        CancellationToken cancellation,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = ReadVersions;

        var rows = new List<RuleVersionRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new RuleVersionRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                Instant(reader.GetString(5)),
                reader.IsDBNull(6) ? null : Instant(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    static DateTimeOffset Instant(string text) => RuleVersions.At(text);

    static async Task<IReadOnlyList<string>> NamesAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NamesWithABarOn;
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public static async Task<ReplayInputs?> InputsAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly session,
        CancellationToken cancellation)
    {
        var on = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Bars, the swing lows and the typical move at the session's own scale, which its
        // stored bands were computed at: a refetch after a split or a dividend rescales them
        // and never the bands, and on the night itself the factor is one.
        // see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored
        decimal factor;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ScaleOn;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", on);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (!await reader.ReadAsync(cancellation) || reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                return null;
            }

            var raw = Money.FromStorage(reader.GetString(1));

            if (raw <= 0)
            {
                return null;
            }

            factor = Money.FromStorage(reader.GetString(0)) / raw;
        }

        decimal Scaled(decimal price) => factor == 1m ? price : PriceForm.Round(price / factor);

        var bands = new List<Level>();
        var memberSources = true;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = BandsFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", on);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var members = Members(reader.GetString(6));

                memberSources &= members is not null;

                bands.Add(new Level(
                    Money.FromStorage(reader.GetString(0)),
                    Money.FromStorage(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetInt32(3) != 0,
                    reader.GetInt32(4),
                    reader.GetInt32(5) != 0,
                    members ?? []));
            }

            bands.Sort((left, right) => left.LowEdge.CompareTo(right.LowEdge));
        }

        if (bands.Count == 0)
        {
            return null;
        }

        var recent = new List<LadderBar>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RecentFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", on);
            command.Parameters.AddWithValue("$window", VolumeProfileSeries.Window);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                recent.Add(new LadderBar(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Scaled(Money.FromStorage(reader.GetString(1))),
                    Scaled(Money.FromStorage(reader.GetString(2))),
                    Scaled(Money.FromStorage(reader.GetString(3)))));
            }
        }

        if (recent.Count == 0)
        {
            return null;
        }

        recent.Reverse();

        // The level window a merge distance version replays over, and the shorter one a condition is read over.
        var window = recent.Select(bar => new LevelBar(bar.SessionDate, bar.High, bar.Low, bar.Close)).ToArray();

        recent = [.. recent.TakeLast(LadderSeries.ConditionLookback)];

        double? typicalMove = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LatestIndicatorFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$name", Core.Indicators.IndicatorSeries.Atr14);
            command.Parameters.AddWithValue("$session", on);

            var value = await command.ExecuteScalarAsync(cancellation);

            typicalMove = value is null or DBNull ? null : Convert.ToDouble(value);
        }

        if (typicalMove is not { } move || move <= 0)
        {
            return null;
        }

        string? trendState = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TrendFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", on);

            trendState = await command.ExecuteScalarAsync(cancellation) as string;
        }

        if (trendState is null)
        {
            return null;
        }

        // The two averages the trend rule's versions read, at the session's own
        // scale as the typical move is, and the labels behind the night.
        var shortAverage = await AverageAsync(connection, ticker, on, Core.Indicators.IndicatorSeries.Sma50, cancellation);
        var longAverage = await AverageAsync(connection, ticker, on, Core.Indicators.IndicatorSeries.Sma200, cancellation);
        var labelsBefore = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LabelsBefore;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", on);
            command.Parameters.AddWithValue("$nights", TrendSeries.MostNightsTheNewLabelHolds - 1);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                labelsBefore.Add(reader.GetString(0));
            }
        }

        // The candidates and the window a merge distance version needs to replay
        // the level arithmetic. Taken from the bands the night already wrote:
        // every member the merge kept is in them, which is the candidate set less
        // the touches the scoring step added, and that is the set a different
        // merge distance would be applied to.
        var candidates = bands
            .SelectMany(band => band.Members)
            .Where(member => member.Source != MemberSource.Touch)
            .OrderBy(member => member.Price)
            .ToArray();

        var lows = StoredSwings.AsOf(connection, ticker, session)
            .Where(swing => swing.Direction == SwingSeries.Low)
            .OrderBy(swing => swing.SessionDate)
            .Select(swing => Scaled(swing.Price))
            .ToArray();

        return new ReplayInputs(
            bands,
            recent[^1].Close,
            Scaled(Statistic.ToPrice(move)),
            recent,
            trendState,
            candidates,
            window,
            session,
            lows,
            memberSources,
            shortAverage is { } shortMean ? Scaled(shortMean) : null,
            longAverage is { } longMean ? Scaled(longMean) : null,
            labelsBefore);
    }

    // One stored average at or before the session, as a price, or null where the
    // name has too short a history for it.
    static async Task<decimal?> AverageAsync(
        SqliteConnection connection,
        string ticker,
        string session,
        string name,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = LatestIndicatorFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$session", session);

        var value = await command.ExecuteScalarAsync(cancellation);

        return value is null or DBNull ? null : Statistic.ToPrice(Convert.ToDouble(value));
    }

    // Null for a band set stored before member sources were written.
    static IReadOnlyList<LevelMember>? Members(string json)
    {
        using var document = JsonDocument.Parse(json);

        var members = new List<LevelMember>();

        foreach (var member in document.RootElement.EnumerateArray())
        {
            if (!member.TryGetProperty("source", out var source))
            {
                return null;
            }

            members.Add(new LevelMember(
                Enum.Parse<MemberSource>(source.GetString()!, ignoreCase: true),
                member.GetProperty("kind").GetString()!,
                Money.FromStorage(member.GetProperty("price").GetString()!),
                DateOnly.ParseExact(member.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return members;
    }

    static async Task<int> DroppedAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellation)
    {
        await using var newest = connection.CreateCommand();

        newest.Transaction = transaction;
        newest.CommandText = NewestSession;

        if (await newest.ExecuteScalarAsync(cancellation) is not string stored)
        {
            return 0;
        }

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = DropOlderThan;
        command.Parameters.AddWithValue(
            "$oldest",
            DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .AddYears(-BarFetcher.RetentionYears)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int written,
        string detail,
        CancellationToken cancellation,
        string outcome = Ok,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", written);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
