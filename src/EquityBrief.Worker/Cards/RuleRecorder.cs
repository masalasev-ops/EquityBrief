using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Cards;

// The rule's record a card draws, by hand: each rule a card can name on each index, each family's live rule on the S&P 500
// and its provisional one on the S&P 400 and 600, replayed at its one setting over the pulled history with the sweep's own
// walk and each trade after its cost, its figures stored one row an index and family over what an earlier run stored. It
// does not start while the night holds the store or inside the night's window, makes no request and calls no model, and
// writes one row of its own to the run log.
// see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
public sealed class RuleRecorder(IClock clock, string databaseFile, string dataRoot, TextWriter output) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.PulledMarketBar, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.PulledShares, Touch.Read),
            new StoreTouch(Store.PulledSplit, Touch.Read),
            new StoreTouch(Store.PulledRevenue, Touch.Read),
            new StoreTouch(Store.PulledMember, Touch.Read),
            new StoreTouch(Store.PulledIncome, Touch.Read),
            new StoreTouch(Store.PulledSnapshot, Touch.Read),
            new StoreTouch(Store.PulledHolding, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.RuleRecord, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "rule-record";

    public const string Stage = "rule-record";

    // How long a run is allowed for before the night's window, which it does not start inside.
    public static readonly TimeSpan Expected = TimeSpan.FromHours(1);

    const string Upsert = @"
        INSERT INTO rule_record (
            index_code, family, rule, settings, recorded_at, first_session, last_session, membership, unit,
            trades, won, average, median_sessions, ended_by, worst_close)
        VALUES (
            $index, $family, $rule, $settings, $recorded_at, $first, $last, $membership, $unit,
            $trades, $won, $average, $median, $ended_by, $worst)
        ON CONFLICT (index_code, family) DO UPDATE SET
            rule = excluded.rule, settings = excluded.settings, recorded_at = excluded.recorded_at,
            first_session = excluded.first_session, last_session = excluded.last_session, membership = excluded.membership,
            unit = excluded.unit, trades = excluded.trades, won = excluded.won, average = excluded.average,
            median_sessions = excluded.median_sessions, ended_by = excluded.ended_by, worst_close = excluded.worst_close;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    // The indices named, each one the night reads, or every one where none is named.
    public async Task<int> RunAsync(IReadOnlyList<string> named, CancellationToken cancellation = default)
    {
        var indices = named.Count == 0 ? DecisionCards.Indices : named;
        var unknown = indices.Where(index => !DecisionCards.Indices.Contains(index, StringComparer.Ordinal)).ToArray();

        if (unknown.Length > 0)
        {
            output.WriteLine($"{Verb}: name an index with '--index', one of {string.Join(", ", DecisionCards.Indices)}, or none for all; not {string.Join(", ", unknown)}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var startedAt = clock.UtcNow;
        var runId = FormattableString.Invariant($"{Verb}-{startedAt:yyyyMMddTHHmmssZ}");
        var lines = new List<string>();

        foreach (var index in indices)
        {
            var replayed = await RuleReplay.IndexAsync(databaseFile, index, output.WriteLine, cancellation);

            foreach (var rule in replayed)
            {
                var figures = RuleRecordFigures.Of(rule.Trades);
                var indexName = DecisionCards.NameOf(index);
                var median = Whole(figures.MedianSessions);

                await WriteAsync(index, rule, figures, startedAt, cancellation);

                var line = FormattableString.Invariant($"{indexName} {rule.Family}: {figures.Trades} trade(s) from {rule.From:yyyy-MM-dd} to {rule.Through:yyyy-MM-dd}, {rule.Membership}, average {Number(figures.Average)} {rule.Unit}, won {Number(figures.Won)}, median {median} session(s) held");

                lines.Add(line);
                output.WriteLine(line);
            }
        }

        await AppendAsync(runId, startedAt, lines.Count, string.Join("; ", lines), cancellation);

        return 0;
    }

    // The `--nights` form: each standing rule's listed count on every scored session of the pulled history, replayed with
    // the sweep's own walk and written as the rule's history for the cards' stretch lines, through the cards' stage,
    // which carries the history on night by night. The S&P 500's live swing filter, breakout and drift rules, the S&P
    // 400's and 600's live or provisional rules, and the breakout's and the drift's registered variants whose settings
    // sit on their family's grid and read no market switch; a variant off the grid, reading a switch or of the swing
    // filter's own starts its history at its registration, and the sector heavyweights carry no stretch line.
    // see: A card's stretch line counts its mark over past empty nights and draws none under 30 completed stretches
    public async Task<int> NightsAsync(IReadOnlyList<string> named, CancellationToken cancellation = default)
    {
        var indices = named.Count == 0 ? DecisionCards.Indices : named;
        var unknown = indices.Where(index => !DecisionCards.Indices.Contains(index, StringComparer.Ordinal)).ToArray();

        if (unknown.Length > 0)
        {
            output.WriteLine($"{Verb}: name an index with '--index', one of {string.Join(", ", DecisionCards.Indices)}, or none for all; not {string.Join(", ", unknown)}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected + Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var startedAt = clock.UtcNow;
        var runId = FormattableString.Invariant($"{Verb}-nights-{startedAt:yyyyMMddTHHmmssZ}");
        var register = await new CandidateRegistrar(clock, databaseFile).RowsAsync(cancellation);
        var standing = CandidateFamily.StandingBefore(register, startedAt);
        var cards = new RuleCards(clock, databaseFile);
        var lines = new List<string>();
        var rows = 0;

        foreach (var index in indices)
        {
            var large = index == IndexFamilies.LargeIndex;
            var variants = large ? Variants(standing) : [];
            var replayed = await RuleReplay.IndexAsync(databaseFile, index, output.WriteLine, cancellation, [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name], variants);

            foreach (var rule in replayed)
            {
                var name = large
                    ? rule.Family == SetupFamilies.Pullback ? LiveName(standing, row => CandidateEvaluators.Find(row.Evaluator) is SwingFilterRule) : rule.Rule
                    : LiveName(standing, row => CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate own && own.Index == index && own.SetupFamily == rule.Family) ?? RuleCards.ProvisionalRule;

                if (name is null)
                {
                    output.WriteLine($"{DecisionCards.NameOf(index)} {rule.Family}: no live rule stands registered, so its history is not written");

                    continue;
                }

                var written = await cards.WriteHistoryAsync(index, rule.Family, name, rule.Nights, cancellation);
                var reading = RuleStretch.Read([.. rule.Nights.Select(night => night.Listed)]);
                var line = FormattableString.Invariant($"{DecisionCards.NameOf(index)} {rule.Family}, {name}: {written} session(s) from {rule.From:yyyy-MM-dd} to {rule.Through:yyyy-MM-dd}, {rule.Nights.Count(night => night.Listed > 0)} listing, {reading.CompletedStretches} stretch(es) completed, mark {(reading.Mark is { } mark ? mark.ToString(CultureInfo.InvariantCulture) : "none")}");

                rows += written;
                lines.Add(line);
                output.WriteLine(line);
            }
        }

        await AppendAsync(runId, startedAt, rows, string.Join("; ", lines), cancellation);

        return 0;
    }

    // The registered breakout and drift rules the sweep's walk can express: each at its place on its family's grid, the
    // drift's stop floor beside it, and none reading a market switch or off the grid.
    public static IReadOnlyList<RuleReplay.SwingVariant> Variants(IReadOnlyList<RegisterRow> standing)
    {
        var variants = new List<RuleReplay.SwingVariant>();

        foreach (var row in standing)
        {
            var parameters = CandidateEvaluator.Read(row.Parameters);

            if (MarketSwitches.Of(parameters).Any)
            {
                continue;
            }

            int[] setting;
            var stopFloor = 0.0;
            string family;

            switch (CandidateEvaluators.Find(row.Evaluator))
            {
                case BreakoutCandidate:
                    var breakout = BreakoutCandidate.SettingsOf(parameters);
                    setting = IndexNightRead.Places(BreakoutSweep.Grid, [breakout.HighSessions, breakout.VolumeMultiple, breakout.RangeCeiling, breakout.StopMoves]);
                    family = BreakoutRule.Name;
                    break;
                case DriftCandidate:
                    var drift = DriftCandidate.SettingsOf(parameters);
                    setting = IndexNightRead.Places(DriftSweep.Grid, [drift.WindowSessions, drift.ReactionMoves, drift.VolumeMultiple, drift.TargetRiskMultiple]);
                    stopFloor = drift.StopFloorMoves;
                    family = DriftRule.Name;
                    break;
                default:
                    continue;
            }

            if (setting.All(place => place >= 0))
            {
                variants.Add(new RuleReplay.SwingVariant(row.Candidate, family, setting, stopFloor));
            }
        }

        return variants;
    }

    static string? LiveName(IReadOnlyList<RegisterRow> standing, Func<RegisterRow, bool> ofTheRule) =>
        standing.FirstOrDefault(row => ofTheRule(row) && FamilyRecords.IsLive(row.Candidate))?.Candidate;

    async Task WriteAsync(string index, RuleReplay.Replayed rule, RuleRecordFigures figures, DateTimeOffset at, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = Upsert;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", rule.Family);
        command.Parameters.AddWithValue("$rule", rule.Rule);
        command.Parameters.AddWithValue("$settings", rule.Settings);
        command.Parameters.AddWithValue("$recorded_at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$first", rule.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$last", rule.Through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$membership", rule.Membership);
        command.Parameters.AddWithValue("$unit", rule.Unit);
        command.Parameters.AddWithValue("$trades", figures.Trades);
        command.Parameters.AddWithValue("$won", (object?)figures.Won ?? DBNull.Value);
        command.Parameters.AddWithValue("$average", (object?)figures.Average ?? DBNull.Value);
        command.Parameters.AddWithValue("$median", (object?)figures.MedianSessions ?? DBNull.Value);
        command.Parameters.AddWithValue("$ended_by", RuleRecordFigures.EndedByJson(figures.EndedBy));
        command.Parameters.AddWithValue("$worst", (object?)figures.WorstClose ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task AppendAsync(string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", DecisionCards.Ok);
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Number(double? value) => value is { } read ? read.ToString("0.000", CultureInfo.InvariantCulture) : "none";

    static string Whole(int? value) => value is { } read ? read.ToString(CultureInfo.InvariantCulture) : "none";
}
