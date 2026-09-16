using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Rules;

public sealed record VersionScoreOutcome(int Versions, int NamesScored, int RowsWritten, int RowsDropped);

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
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.RuleVersion, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.VersionScore, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "rule-versions";

    public const string Ok = "ok";

    // A refused open or close is a row of its own under this outcome rather than
    // under ok with the refusal in the detail, so the run page and a reader of
    // the log can tell an attempt that was turned away from one that happened.
    public const string Refused = "refused";

    // The build's version of the ladder rules' own code, hashed into every
    // window's `parameters_hash`: the pin of the sources a replay runs through,
    // being the level arithmetic, the ladder arithmetic and this file, less the
    // line below. `rule-versions-scored` recomputes it from those files and fails
    // where they disagree, so a change to any of them is a new code version, and
    // a live window opened under the old one stops the first night after it.
    // A constant somebody raised by hand would have let the code move under an
    // open window with the night going on scoring it, which is what 8.6 shipped.
    public const string CodeVersionDeclaration = "public const string CodeVersion =";

    public const string CodeVersion = "526e7c8f9175";

    // The sources the pin is taken over, from the repository root.
    public static IReadOnlyList<string> CodeVersionSources { get; } =
    [
        "src/EquityBrief.Core/Levels/LevelSeries.cs",
        "src/EquityBrief.Core/Ladders/LadderSeries.cs",
        "src/EquityBrief.Worker/Rules/RuleVersionScorer.cs",
    ];

    // One year retained, as every computed table is.
    // see: Every computed table's writer is its own deleter
    public const int RetentionDays = 365;

    const string ReadVersions = @"
        SELECT rule, version, parameters, parameters_hash, code_version, opened_at, closed_at, replaced_by
        FROM rule_version
        ORDER BY rule, opened_at, version;
    ";

    const string OpenWindow = @"
        INSERT INTO rule_version (
            rule, version, parameters, parameters_hash, code_version, opened_at, closed_at, replaced_by)
        VALUES ($rule, $version, $parameters, $parameters_hash, $code_version, $opened_at, NULL, NULL);
    ";

    // The one field a version row ever changes, and it is the close.
    const string CloseWindow = @"
        UPDATE rule_version
        SET closed_at = $closed_at, replaced_by = $replaced_by
        WHERE rule = $rule AND version = $version AND opened_at = $opened_at AND closed_at IS NULL;
    ";

    const string WriteScore = @"
        INSERT INTO version_score (ticker, session_date, rule, version, opened_at, plan, sample)
        VALUES ($ticker, $session_date, $rule, $version, $opened_at, $plan, $sample)
        ON CONFLICT (ticker, session_date, rule, version, opened_at) DO UPDATE SET
            plan = excluded.plan,
            sample = excluded.sample;
    ";

    const string DropOlderThan = @"
        DELETE FROM version_score WHERE session_date < $oldest;
    ";

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

    // The bands and the trend as they stood on the night being scored. On the
    // night itself that is the newest set; on a backfill it is that night's, and
    // reading the newest would replay a past close against tonight's bands.
    const string BandsFor = @"
        SELECT low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members
        FROM level
        WHERE ticker = $ticker AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = $ticker AND as_of <= $session)
        ORDER BY low_edge;
    ";

    const string TrendFor = @"
        SELECT trend_state FROM ladder
        WHERE ticker = $ticker AND as_of <= $session
        ORDER BY as_of DESC
        LIMIT 1;
    ";

    const string RecentFor = @"
        SELECT session_date, high, low, close FROM bar
        WHERE ticker = $ticker AND session_date <= $session
        ORDER BY session_date DESC
        LIMIT $window;
    ";

    const string TypicalMoveFor = @"
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

    public async Task<VersionScoreOutcome> RunAsync(
        DateOnly session,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var versions = await VersionsAsync(connection, cancellation);
        var open = RuleVersions.OpenAt(versions, startedAt);

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

        var names = await NamesAsync(connection, session, cancellation);
        var written = 0;

        await using (var transaction = await connection.BeginTransactionAsync(cancellation))
        {
            // No version to replay, no bands read: level rows written before member sources were stored cannot be read.
            foreach (var ticker in replayed.Length == 0 ? Array.Empty<string>() : names)
            {
                var inputs = await InputsAsync(connection, ticker, session, cancellation);

                if (inputs is null)
                {
                    continue;
                }

                foreach (var version in replayed)
                {
                    var plan = Replayed(version, inputs.Value);

                    // A score for a night before its window opened is the
                    // backfill's, and it counts toward nothing. Read off the
                    // window's own instant against the night rather than from a
                    // caller's claim about itself.
                    var inSample = session < DateOnly.FromDateTime(version.OpenedAt.UtcDateTime);

                    await using var command = connection.CreateCommand();

                    command.Transaction = (SqliteTransaction)transaction;
                    command.CommandText = WriteScore;
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$session_date", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$rule", version.Rule);
                    command.Parameters.AddWithValue("$version", version.Version);
                    command.Parameters.AddWithValue("$opened_at", version.OpenedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$plan", plan);
                    command.Parameters.AddWithValue("$sample", inSample ? RuleVersions.InSample : RuleVersions.Scored);

                    written += await command.ExecuteNonQueryAsync(cancellation);
                }
            }

            await transaction.CommitAsync(cancellation);
        }

        var dropped = await DroppedAsync(connection, session.AddDays(-RetentionDays), cancellation);

        var detail =
            FormattableString.Invariant($"{open.Count} open version(s), {replayed.Length} replayed over {names.Count} name(s), ")
            + FormattableString.Invariant($"{written} score(s) written, {dropped} dropped");

        await RecordAsync(connection, runId, startedAt, written, detail, cancellation);

        return new VersionScoreOutcome(open.Count, names.Count, written, dropped);
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
            LadderRules.MergeDistance => new Dictionary<string, double>(StringComparer.Ordinal) { ["typicalMoveMultiple"] = 0.5 },
            LadderRules.StopPlacement => new Dictionary<string, double>(StringComparer.Ordinal) { ["stopTrailsTheLastHigherLow"] = ladder["stopTrailsTheLastHigherLow"] },
            LadderRules.NearExitSkip => new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = ladder["nearExitInTypicalDays"] },
            LadderRules.ZoneEdgesFromNonAverageAnchors => new Dictionary<string, double>(StringComparer.Ordinal) { ["zoneEdgesFromNonAverageAnchorsOnly"] = ladder["zoneEdgesFromNonAverageAnchorsOnly"] },
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
        DateOnly AsOf);

    // The plan one version produces for one name-night.
    //
    // A merge distance version replays the level arithmetic first and then the
    // ladder arithmetic over the bands it produced, which is why it costs both
    // stages. Every other version reads the bands as the night computed them and
    // replays the ladder arithmetic alone.
    public static string Replayed(RuleVersionRow version, ReplayInputs inputs)
    {
        var parameters = RuleVersions.Read(version.Parameters);

        var bands = LadderRules.ReplaysLevels(version.Rule) && inputs.Candidates.Count > 0
            ? LevelSeries.For(
                inputs.Window,
                inputs.Candidates,
                inputs.Close,
                inputs.TypicalMove * Statistic.ToPrice(parameters.GetValueOrDefault("typicalMoveMultiple", 0.5)),
                inputs.AsOf)
            : inputs.Bands;

        var rules = new LadderRuleSet(
            NearExitInTypicalDays: (int)parameters.GetValueOrDefault("nearExitInTypicalDays", LadderSeries.NearExitInTypicalDays),
            StopTrailsTheLastHigherLow: parameters.GetValueOrDefault("stopTrailsTheLastHigherLow", 1) != 0,
            ZoneEdgesFromNonAverageAnchorsOnly: parameters.GetValueOrDefault("zoneEdgesFromNonAverageAnchorsOnly", 0) != 0);

        var plan = LadderSeries.For(bands, inputs.Close, inputs.TypicalMove, inputs.Recent, inputs.TrendState, rules: rules);

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
        });
    }

    // ---- opening and closing a window ----

    // Why these parameters may not be a window of this rule, or null.
    //
    // Named apart from the write so a test puts each refusal to the same reader
    // the open does. A live window carries exactly the build's parameters,
    // since its hash is compared against the build's every night and a live
    // window opened at any other would stop the first night after it.
    public static string? ParameterRefusal(string rule, string version, IReadOnlyDictionary<string, double> parameters)
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

        if (string.Equals(version, RuleVersions.Live, StringComparison.Ordinal)
            && live.Any(pair => !parameters[pair.Key].Equals(pair.Value)))
        {
            return
                $"a live window of '{rule}' carries the build's own parameters, " +
                $"{RuleVersions.Write(live)}, and was given {RuleVersions.Write(parameters)}. A version with other " +
                "values is opened under its own name beside the live one.";
        }

        return null;
    }

    public Task<string?> OpenLiveAsync(string rule, string runId, CancellationToken cancellation = default) =>
        OpenAsync(rule, RuleVersions.Live, LiveParameters(rule), runId, cancellation);

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

        var rows = await VersionsAsync(connection, cancellation);

        if ((RuleVersions.Refusal(rows, rule, version, at) ?? ParameterRefusal(rule, version, parameters)) is { } refusal)
        {
            await RecordAsync(connection, runId, at, 0, refusal, cancellation, Refused);

            return refusal;
        }

        await using var command = connection.CreateCommand();

        command.CommandText = OpenWindow;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$parameters", RuleVersions.Write(parameters));
        command.Parameters.AddWithValue("$parameters_hash", RuleVersions.Hash(parameters, CodeVersion));
        command.Parameters.AddWithValue("$code_version", CodeVersion);
        command.Parameters.AddWithValue("$opened_at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellation);
        await RecordAsync(connection, runId, at, 1, $"opened '{version}' of '{rule}'", cancellation);

        return null;
    }

    // Closing keeps the row and every column it was opened with but the two the
    // close writes, so the scores under it stay scores of the rule as it stood.
    //
    // The window closed is the one open now under that name, which there is at
    // most one of because a version is refused a second open window. A live
    // window is not closed while a version of its rule is open beside it, since
    // that would leave the version measured with nothing watching the code it
    // runs through; the versions are closed first. Returns why nothing was
    // closed, or null.
    public async Task<string?> CloseAsync(
        string rule,
        string version,
        string? replacedBy,
        string runId,
        CancellationToken cancellation = default)
    {
        var at = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var open = RuleVersions.OpenAt(await VersionsAsync(connection, cancellation), at);

        var window = open.FirstOrDefault(row =>
            string.Equals(row.Rule, rule, StringComparison.Ordinal)
            && string.Equals(row.Version, version, StringComparison.Ordinal));

        var refusal = window is null
            ? $"'{version}' of '{rule}' has no open window to close."
            : string.Equals(version, RuleVersions.Live, StringComparison.Ordinal)
                && open.Where(row => string.Equals(row.Rule, rule, StringComparison.Ordinal)
                    && !string.Equals(row.Version, RuleVersions.Live, StringComparison.Ordinal)).ToArray() is { Length: > 0 } beside
                ? $"the live window of '{rule}' is not closed while {string.Join(", ", beside.Select(row => $"'{row.Version}'"))} " +
                  "is open beside it: a version with no live window beside it is measured with nothing watching the code " +
                  "it runs through. Close the versions first."
                : null;

        if (refusal is not null)
        {
            await RecordAsync(connection, runId, at, 0, refusal, cancellation, Refused);

            return refusal;
        }

        await using var command = connection.CreateCommand();

        command.CommandText = CloseWindow;
        command.Parameters.AddWithValue("$closed_at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$replaced_by", (object?)replacedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$opened_at", window!.OpenedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellation);

        await RecordAsync(
            connection,
            runId,
            at,
            1,
            $"closed '{version}' of '{rule}'" + (replacedBy is null ? string.Empty : $", replaced by '{replacedBy}'"),
            cancellation);

        return null;
    }

    public async Task<IReadOnlyList<RuleVersionRow>> VersionsAsync(CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        return await VersionsAsync(connection, cancellation);
    }

    static async Task<IReadOnlyList<RuleVersionRow>> VersionsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

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
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows;
    }

    static DateTimeOffset Instant(string text) =>
        DateTimeOffset.ParseExact(
            text,
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

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

    static async Task<ReplayInputs?> InputsAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly session,
        CancellationToken cancellation)
    {
        var bands = new List<Level>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = BandsFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                bands.Add(new Level(
                    Money.FromStorage(reader.GetString(0)),
                    Money.FromStorage(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetInt32(3) != 0,
                    reader.GetInt32(4),
                    reader.GetInt32(5) != 0,
                    Members(reader.GetString(6))));
            }
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
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$window", LadderSeries.ConditionLookback);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                recent.Add(new LadderBar(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Money.FromStorage(reader.GetString(1)),
                    Money.FromStorage(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3))));
            }
        }

        if (recent.Count == 0)
        {
            return null;
        }

        recent.Reverse();

        double? typicalMove = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TypicalMoveFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$name", Core.Indicators.IndicatorSeries.Atr14);
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

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
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            trendState = await command.ExecuteScalarAsync(cancellation) as string;
        }

        if (trendState is null)
        {
            return null;
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

        var window = recent
            .Select(bar => new LevelBar(bar.SessionDate, bar.High, bar.Low, bar.Close))
            .ToArray();

        return new ReplayInputs(
            bands,
            recent[^1].Close,
            Statistic.ToPrice(move),
            recent,
            trendState,
            candidates,
            window,
            session);
    }

    static IReadOnlyList<LevelMember> Members(string json)
    {
        using var document = JsonDocument.Parse(json);

        return
        [
            .. document.RootElement.EnumerateArray().Select(member => new LevelMember(
                Enum.Parse<MemberSource>(member.GetProperty("source").GetString()!, ignoreCase: true),
                member.GetProperty("kind").GetString()!,
                Money.FromStorage(member.GetProperty("price").GetString()!),
                DateOnly.ParseExact(member.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
        ];
    }

    static async Task<int> DroppedAsync(SqliteConnection connection, DateOnly oldest, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = DropOlderThan;
        command.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int written,
        string detail,
        CancellationToken cancellation,
        string outcome = Ok)
    {
        await using var command = connection.CreateCommand();

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
