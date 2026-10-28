using System.Globalization;
using System.Text.Json;
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

// The rule's record a card draws, by hand: each rule a card can name on each index, each family's live rule on the S&P 500,
// and on the S&P 400 and 600 each family's live rule where one stands as the night reads it and its provisional one
// otherwise, replayed at its one setting over the pulled history with its sweep's own walk and each trade after its cost,
// its figures stored one row an index and family over what an earlier run stored. A family's live rule on the S&P 400 or
// 600 is replayed by its sweep's own reading of the index's history at the next open, its row naming the rule in its
// registration's words and the setting by its sweep's key; one whose settings that reading cannot read writes no row and
// says why. It does not start while the night holds the store or inside the night's window, makes no request and calls
// no model, and writes one row of its own to the run log.
// see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
// see: A frozen rule on the S&P 400 or 600 is replayed at its own settings by its sweep's own code, and its card draws that record alone
public sealed class RuleRecorder(IClock clock, string databaseFile, string dataRoot, TextWriter output) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
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
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "rule-record";

    public const string Stage = "rule-record";

    // The `--open-floor` form's own stage, its flag and the floor it replays at: the reward to risk the next open has to
    // leave for the drift's listing to be bought.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    public const string OpenFloorStage = "rule-record-open-floor";

    public const string OpenFloorFlag = "--open-floor";

    public const double OpenFloor = 1.0;

    // The words the variant is named by, the live drift rule's with the one change.
    public static string OpenFloorWords { get; } =
        "the " + TheSetupFamilies.Words(DriftRule.Live) + FormattableString.Invariant($", skipping a fill whose reward to risk at the open is under {OpenFloor:0.#}");

    const string OpenFloorRuns = "SELECT detail FROM run_log WHERE stage = $stage ORDER BY rowid;";

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
        var written = 0;

        // The S&P 400's and 600's rules as the night reads them as of the run, each family's live rule among them where
        // one stands; one whose evaluator moved or whose settings left its grid is read by no night, and its family keeps
        // its provisional replay.
        var (rules, _) = IndexFamilies.StandingRules(await new CandidateRegistrar(clock, databaseFile).RowsAsync(cancellation), startedAt);

        foreach (var index in indices)
        {
            var replayed = await RuleReplay.IndexAsync(databaseFile, index, output.WriteLine, cancellation, live: rules);
            var indexName = DecisionCards.NameOf(index);

            foreach (var rule in replayed)
            {
                if (rule.Refused is { } why)
                {
                    var refusal = $"{indexName} {rule.Family}: its live rule, the {rule.Rule}, is not replayed, since {why}, and no record is written for it";

                    lines.Add(refusal);
                    output.WriteLine(refusal);

                    continue;
                }

                var figures = RuleRecordFigures.Of(rule.Trades);
                var median = Whole(figures.MedianSessions);
                var frozen = index != IndexFamilies.LargeIndex && rules.Any(one => one.Live && one.Index == index && one.Family == rule.Family);

                await WriteAsync(index, rule, figures, startedAt, cancellation);
                written++;

                var line = FormattableString.Invariant($"{indexName} {rule.Family}{(frozen ? ", its live rule at " + rule.Settings : string.Empty)}: {figures.Trades} trade(s) from {rule.From:yyyy-MM-dd} to {rule.Through:yyyy-MM-dd}, {rule.Membership}, average {Number(figures.Average)} {rule.Unit}, won {Number(figures.Won)}, median {median} session(s) held");

                lines.Add(line);
                output.WriteLine(line);
            }
        }

        await AppendAsync(runId, startedAt, written, string.Join("; ", lines), cancellation);

        return 0;
    }

    // The `--nights` form: each standing rule's listed count on every scored session of the pulled history, replayed with
    // the sweep's own walk and written as the rule's history for the cards' stretch lines, through the cards' stage,
    // which carries the history on night by night. The S&P 500's live swing filter, breakout and drift rules, the S&P
    // 400's and 600's live rules as the night reads them, each replayed at its own settings by its sweep's own reading of
    // the index's history and written under its name, or the provisional rule where none stands, and the breakout's and
    // the drift's registered variants on the S&P 500 whose settings sit on their family's grid and read no market switch;
    // a variant off the grid, reading a switch or of the swing filter's own, and every other rule on the S&P 400 and 600,
    // starts its history at its registration, and the sector heavyweights carry no stretch line.
    // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
    // see: A frozen rule on the S&P 400 or 600 is replayed at its own settings by its sweep's own code, and its card draws that record alone
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
        var (rules, _) = IndexFamilies.StandingRules(register, startedAt);
        var cards = new RuleCards(clock, databaseFile);
        var lines = new List<string>();
        var rows = 0;

        foreach (var index in indices)
        {
            var large = index == IndexFamilies.LargeIndex;
            var variants = large ? Variants(standing) : [];
            var replayed = await RuleReplay.IndexAsync(databaseFile, index, output.WriteLine, cancellation, [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name], variants, large ? null : rules);

            foreach (var rule in replayed)
            {
                if (rule.Refused is { } why)
                {
                    var refusal = $"{DecisionCards.NameOf(index)} {rule.Family}: its live rule, the {rule.Rule}, is not replayed, since {why}, so its history is not written";

                    lines.Add(refusal);
                    output.WriteLine(refusal);

                    continue;
                }

                // On the S&P 400 and 600 the rule the night reads a family's list by, its live rule where one stands, the
                // rule the replay walked.
                var name = large
                    ? rule.Family == SetupFamilies.Pullback ? LiveName(standing, row => CandidateEvaluators.Find(row.Evaluator) is SwingFilterRule) : rule.Rule
                    : rules.FirstOrDefault(one => one.Live && one.Index == index && one.Family == rule.Family)?.Candidate ?? RuleCards.ProvisionalRule;

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

    // The `--open-floor` form: the S&P 500's live drift rule walked at its one setting over the pulled history through the
    // newest session the store holds, as its card's record is walked, and walked again skipping a listing whose next open
    // leaves a reward to risk under the floor, both written beside each other on the run log row it writes with the
    // listings the variant skipped; the trades listed after the first such run's last session counted apart as the record
    // followed forward. It writes no record row, registers nothing and moves no rule, list or paper account.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    public async Task<int> OpenFloorAsync(bool print = false, CancellationToken cancellation = default)
    {
        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb} {OpenFloorFlag}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb} {OpenFloorFlag}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var startedAt = clock.UtcNow;
        var runId = FormattableString.Invariant($"{OpenFloorStage}-{startedAt:yyyyMMddTHHmmssZ}");
        var setting = IndexNightRead.DriftAsFrozen.ToArray();
        RuleReplay.SwingVariant[] variants =
        [
            new(FamilyRecords.LivePrefix + TheSetupFamilies.Words(DriftRule.Live), DriftRule.Name, setting, 0),
            new(OpenFloorWords, DriftRule.Name, setting, 0, OpenFloor),
        ];
        var replayed = await RuleReplay.IndexAsync(databaseFile, IndexFamilies.LargeIndex, output.WriteLine, cancellation, [DriftRule.Name], variants);
        var live = replayed.Single(rule => rule.Rule == variants[0].Name);
        var floor = replayed.Single(rule => rule.Rule == variants[1].Name);
        var forwardFrom = await ForwardFromAsync(cancellation) ?? live.Through;

        var detail = JsonSerializer.Serialize(new
        {
            index = IndexFamilies.LargeIndex,
            floor = OpenFloor,
            from = live.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            through = live.Through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            forwardFrom = forwardFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            live = Walked(live, forwardFrom),
            variant = Walked(floor, forwardFrom),
            skipped = floor.Skipped,
        });

        var line = FormattableString.Invariant($"{Verb} {OpenFloorFlag}: from {live.From:yyyy-MM-dd} to {live.Through:yyyy-MM-dd}, the live drift rule {live.Trades.Count} trade(s), average {Number(RuleRecordFigures.Of(live.Trades).Average)} risks; skipping a fill under {OpenFloor:0.#} at the open, {floor.Trades.Count} trade(s), average {Number(RuleRecordFigures.Of(floor.Trades).Average)} risks, {floor.Skipped} listing(s) skipped; followed forward from {forwardFrom:yyyy-MM-dd}");

        output.WriteLine(line);

        // With '--print' the figures are printed and nothing is written.
        if (print)
        {
            output.WriteLine(detail);

            return 0;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", OpenFloorStage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", DecisionCards.Ok);
        command.Parameters.AddWithValue("$rows_written", 0);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);

        return 0;

        static object Walked(RuleReplay.Replayed rule, DateOnly forwardFrom)
        {
            var figures = RuleRecordFigures.Of(rule.Trades);
            var forward = RuleRecordFigures.Of([.. rule.Trades.Where((_, at) => rule.Listed[at] > forwardFrom)]);

            return new
            {
                rule = rule.Rule,
                trades = figures.Trades,
                won = figures.Won,
                average = figures.Average,
                medianSessions = figures.MedianSessions,
                forwardTrades = forward.Trades,
                forwardAverage = forward.Average,
            };
        }
    }

    // The last session the first `--open-floor` run read through, which the record is followed forward from; none where no
    // run has written one.
    async Task<DateOnly?> ForwardFromAsync(CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = OpenFloorRuns;
        command.Parameters.AddWithValue("$stage", OpenFloorStage);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            using var document = JsonDocument.Parse(reader.GetString(0));

            if (document.RootElement.TryGetProperty("through", out var through) && DateOnly.TryParseExact(through.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return day;
            }
        }

        return null;
    }

    // The registered breakout and drift rules the sweep's walk can express: each at its place on its family's grid, the
    // drift's stop floor beside it, and none reading a market switch or off the grid.
    public static IReadOnlyList<RuleReplay.SwingVariant> Variants(IReadOnlyList<RegisterRow> standing)
    {
        var variants = new List<RuleReplay.SwingVariant>();

        foreach (var row in standing)
        {
            // The breakout's and the drift's rows alone, read before their parameters are, since the swing filter's and
            // the heavyweights' state no market switch and the reading throws on a key they lack.
            if (CandidateEvaluators.Find(row.Evaluator) is not (BreakoutCandidate or DriftCandidate))
            {
                continue;
            }

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

    // A family's live rule, the newest registration of it carrying the live rule's name.
    // see: An approved change reaches the S&P 500's breakout and drift through the stored settings and a registration, and the rule it replaces runs beside it for 63 sessions
    static string? LiveName(IReadOnlyList<RegisterRow> standing, Func<RegisterRow, bool> ofTheRule) =>
        FamilyRecords.LiveOf(standing.Where(ofTheRule))?.Candidate;

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
