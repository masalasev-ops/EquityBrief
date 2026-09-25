using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

public sealed record ShapeProposalOutcome(string Version, int Ordinary, bool Crossed, long? Proposal);

// The shape proposer, a night step after the swing filter. It reads the gate results the filter stored,
// the night's market readings and the listings' firing, and nothing a candidate's evaluator reads; once
// sixty ordinary nights are stored under the open filter version it writes one proposal for that
// version's window and states the crossed trigger on its run log row, and after a rejection the next
// waits on sixty more. It never opens a version: an acceptance is the operator's command.
// see: The shape proposer moves one setting a gate, nearest first, and never applies what it proposes
// see: The swing filter's shape is calibrated over its ordinary nights, and a night one cause floods is left out
public sealed class ShapeProposer : IComponent
{
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.MarketReading, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.ShapeProposal, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "shape-proposal";

    const string OpenVersion = "SELECT version, settings FROM filter_version WHERE closed_at IS NULL ORDER BY opened_at DESC LIMIT 1;";

    const string GateNights = @"
        SELECT session_date, MIN(version), COUNT(*),
               SUM(trend),
               SUM(trend * setup),
               SUM(trend * setup * trigger_pass),
               SUM(trend * setup * trigger_pass * trade),
               SUM(CASE WHEN trend = 1 AND setup = 1 AND trigger_pass = 1 AND trade = 1 AND exclusions = '[]' THEN 1 ELSE 0 END)
        FROM gate_result
        GROUP BY session_date
        ORDER BY session_date;
    ";

    const string MarketRatios = "SELECT session_date, median_volume_ratio FROM market_reading;";

    const string Listings = "SELECT session_date, reasons FROM listing;";

    const string ProposalsFor = "SELECT id, decision FROM shape_proposal WHERE version = $version ORDER BY id;";

    // Every stored row, in each name's session order, so each night's trigger event can be read beside the
    // one its name stored on the session before.
    const string Rows = @"
        SELECT ticker, session_date, version, trigger_event, ladder_reward_to_risk, ladder_stop_moves,
               swing_reward_to_risk, swing_stop_moves, exclusions, gates
        FROM gate_result
        ORDER BY ticker, session_date;
    ";

    const string Insert = @"
        INSERT INTO shape_proposal (
            proposed_at, session_date, version, ordinary, current_settings, settings, levers,
            list_now, list_proposed, findings)
        VALUES (
            $proposed_at, $session_date, $version, $ordinary, $current_settings, $settings, $levers,
            $list_now, $list_proposed, $findings);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public ShapeProposer(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<ShapeProposalOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var (version, settings) = await VersionAsync(connection, cancellation);
        var nights = await NightsAsync(connection, cancellation);
        var night = nights.Count > 0 ? nights[^1].Session : clock.SessionDateAt(startedAt);
        var firings = NightFirings.Of(await ListingRowsAsync(connection, cancellation));
        var state = ShapeClock.For(nights, firings, version, night);

        long? written = null;
        string detail;

        var (standing, rejected) = state.VersionOpen && state.Crossed
            ? await ProposedAsync(connection, version, cancellation)
            : (null, 0);

        if (!state.VersionOpen)
        {
            detail = FormattableString.Invariant($"no filter version is open, so no night counts toward the {state.Wanted}; {state.Ordinary} ordinary night(s) of {state.WindowNights} stored under section 17's proposed values, and nothing proposed");
        }
        else if (!state.Crossed)
        {
            detail = FormattableString.Invariant($"{state.Ordinary} of the {state.Wanted} ordinary nights under filter version {version}, and nothing proposed");
        }
        else if (standing is { } stands)
        {
            detail = FormattableString.Invariant($"shape calibration is due: {state.Ordinary} ordinary nights under filter version {version}, and proposal {stands} stands for it");
        }
        else if (state.Ordinary < state.Wanted * (rejected + 1))
        {
            detail = FormattableString.Invariant($"{state.Ordinary} ordinary nights under filter version {version}, {rejected} proposal(s) for it rejected, and the next is written at {state.Wanted * (rejected + 1)}");
        }
        else
        {
            var left = state.Events.Select(one => one.Session).ToHashSet();
            var (stored, unanswered) = await StoredAsync(connection, version, cancellation);
            var ordinary = stored.Where(one => !left.Contains(one.Session) && !unanswered.Contains(one.Session)).ToArray();
            var proposal = ShapeProposals.Propose(ordinary, settings);

            // A night whose rows were stored before the setup kept its two band answers cannot be recounted:
            // read as neither band, it would pass no setup under any setting. It is left out and named.
            var skipped = stored.Count(one => !left.Contains(one.Session) && unanswered.Contains(one.Session));

            if (skipped > 0)
            {
                proposal = proposal with
                {
                    Findings =
                    [
                        FormattableString.Invariant($"{skipped} ordinary night(s) under filter version {version} were stored before the setup kept its band answers and were not recounted"),
                        .. proposal.Findings,
                    ],
                };
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Insert + " SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$proposed_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$session_date", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$version", version);
                command.Parameters.AddWithValue("$ordinary", ordinary.Length);
                command.Parameters.AddWithValue("$current_settings", settings.Write());
                command.Parameters.AddWithValue("$settings", proposal.Settings.Write());
                command.Parameters.AddWithValue("$levers", JsonSerializer.Serialize(proposal.Levers));
                command.Parameters.AddWithValue("$list_now", proposal.ListNow is { } now ? now : DBNull.Value);
                command.Parameters.AddWithValue("$list_proposed", proposal.ListProposed is { } after ? after : DBNull.Value);
                command.Parameters.AddWithValue("$findings", JsonSerializer.Serialize(proposal.Findings));

                written = (long)(await command.ExecuteScalarAsync(cancellation))!;
            }

            await AppendAsync(
                connection,
                transaction,
                runId,
                startedAt,
                1,
                FormattableString.Invariant($"shape calibration is due: {ordinary.Length} ordinary nights under filter version {version}, and proposal {written} written with {proposal.Findings.Count} finding(s)"),
                cancellation);
            await transaction.CommitAsync(cancellation);

            return new ShapeProposalOutcome(version, ordinary.Length, true, written);
        }

        await AppendAsync(connection, null, runId, startedAt, 0, detail, cancellation);

        return new ShapeProposalOutcome(version, state.Ordinary, state.Crossed, written);
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction? transaction, string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static async Task<(string Version, FilterSettings Settings)> VersionAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = OpenVersion;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (reader.GetString(0), FilterSettings.Read(reader.GetString(1)))
            : (ShapeClock.NoVersion, FilterSettings.Proposed);
    }

    static async Task<IReadOnlyList<NightShape>> NightsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var ratios = new Dictionary<DateOnly, double?>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MarketRatios;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                ratios[Date(reader.GetString(0))] = reader.IsDBNull(1) ? null : reader.GetDouble(1);
            }
        }

        var nights = new List<NightShape>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = GateNights;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var session = Date(reader.GetString(0));

                nights.Add(new NightShape(
                    session,
                    reader.GetString(1),
                    reader.GetInt32(2),
                    [reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)],
                    reader.GetInt32(7),
                    ratios.TryGetValue(session, out var ratio) ? ratio : null));
            }
        }

        return nights;
    }

    static async Task<IReadOnlyList<(DateOnly, string)>> ListingRowsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Listings;

        var rows = new List<(DateOnly, string)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add((Date(reader.GetString(0)), reader.GetString(1)));
        }

        return rows;
    }

    // The version's proposal still waiting on a decision, and how many were rejected: a rejection leaves
    // the version open, and the next proposal for it waits on another sixty ordinary nights.
    static async Task<(long? Standing, int Rejected)> ProposedAsync(SqliteConnection connection, string version, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ProposalsFor;
        command.Parameters.AddWithValue("$version", version);

        long? standing = null;
        var rejected = 0;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (reader.IsDBNull(1))
            {
                standing = reader.GetInt64(0);
            }
            else if (reader.GetString(1) == "rejected")
            {
                rejected++;
            }
        }

        return (standing, rejected);
    }

    // Every night stored under the version, each member's stored answers read back, its trigger event
    // beside the one it stored on its session before, and the nights holding a row whose setup was read
    // with no band answers kept beside it.
    static async Task<(IReadOnlyList<StoredNight> Nights, IReadOnlySet<DateOnly> Unanswered)> StoredAsync(SqliteConnection connection, string version, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Rows;

        var nights = new Dictionary<DateOnly, List<StoredMember>>();
        var unanswered = new HashSet<DateOnly>();
        string? previousTicker = null;
        bool? previousEvent = null;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);
            var session = Date(reader.GetString(1));
            bool? triggerEvent = reader.IsDBNull(3) ? null : reader.GetInt32(3) == 1;
            bool? before = previousTicker == ticker ? previousEvent : null;

            previousTicker = ticker;
            previousEvent = triggerEvent;

            if (reader.GetString(2) != version)
            {
                continue;
            }

            double? Real(int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);

            var gates = Values(reader.GetString(9));

            if (gates.TryGetValue(SwingGates.Setup, out var setup) && setup.ContainsKey("depth") && !setup.ContainsKey(SwingGates.PullbackBandValue))
            {
                unanswered.Add(session);
            }

            if (!nights.TryGetValue(session, out var members))
            {
                nights[session] = members = [];
            }

            members.Add(new StoredMember(
                ticker,
                Text(gates, SwingGates.Trend, "trend state"),
                Number(gates, SwingGates.Trend, "strength"),
                Number(gates, SwingGates.Setup, "depth"),
                Number(gates, SwingGates.Setup, "dry-up"),
                Number(gates, SwingGates.Setup, "tightness"),
                Number(gates, SwingGates.Setup, "volume multiple"),
                Text(gates, SwingGates.Setup, SwingGates.PullbackBandValue) == "yes",
                Text(gates, SwingGates.Setup, SwingGates.BreakoutBandValue) == "yes",
                triggerEvent,
                before,
                Real(4),
                Real(5),
                Real(6),
                Real(7),
                reader.GetString(8) != "[]"));
        }

        return ([.. nights.OrderBy(pair => pair.Key).Select(pair => new StoredNight(pair.Key, pair.Value))], unanswered);
    }

    // Each gate's stored values, by the gate's name.
    static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Values(string gates)
    {
        using var document = JsonDocument.Parse(gates);

        return document.RootElement.GetProperty("gates").EnumerateArray().ToDictionary(
            gate => gate.GetProperty("gate").GetString()!,
            gate => (IReadOnlyDictionary<string, string>)gate.GetProperty("values").EnumerateObject()
                .ToDictionary(value => value.Name, value => value.Value.GetString() ?? string.Empty, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    static string? Text(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> gates, string gate, string name) =>
        gates.TryGetValue(gate, out var values) && values.TryGetValue(name, out var value) && value != "none" ? value : null;

    static double? Number(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> gates, string gate, string name) =>
        Text(gates, gate, name) is { } value && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}
