using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Candidates;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The swap a promotion makes, written by the night's own build at its start, before anything reads the register. Each press
// the night has not answered is answered once, oldest first: inside the rule freeze refused; Keep recorded and nothing
// changed; a Promote undone before it ran answered undone; a Promote retiring the variant's row and the running revision's
// and registering, at one instant, the variant's settings under the running revision's name and the running revision's
// settings as a variant, each carrying its own record; and an Undo of an applied swap the same swap back, each record carried
// back. Every register write goes through the registrar. It reads no market data, makes no request and calls no model.
// see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
public sealed class PromotionSwap(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.PromotionPress, Touch.Read),
            new StoreTouch(Store.PromotionSwap, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "promotion-swap";

    public const string NoVariant = "the variant no longer stands registered on its index, so nothing was promoted";

    public const string NoRevision = "its setup holds no running revision on its index, so nothing was promoted";

    public const string NotApplied = "the promotion it undoes never ran, so there is nothing to undo";

    const string Unanswered = @"
        SELECT press.id, press.index_code, press.family, press.variant, press.press, press.undoes, press.month, press.pressed_at
        FROM promotion_press AS press LEFT JOIN promotion_swap AS answer ON answer.press_id = press.id
        WHERE answer.press_id IS NULL ORDER BY press.id;";

    const string InsertAnswer = @"
        INSERT INTO promotion_swap (press_id, index_code, family, applied, words, revision, promoted, running, replaced, carries, night, swapped_at, run_id)
        VALUES ($press_id, $index_code, $family, $applied, $words, $revision, $promoted, $running, $replaced, $carries, $night, $swapped_at, $run_id);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, 0, 0, '0', $detail);";

    // Every press the night has not answered, answered for the session given, and the instant its first registration was
    // written from, none where it wrote none.
    public async Task<(IReadOnlyList<PromotionSwapRow> Answers, DateTimeOffset? WroteFrom)> NightAsync(string runId, DateOnly session, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var reads = new PaperReads(databaseFile);
        var (presses, answers) = await reads.PromotionSwapsAsync();
        var unanswered = presses.Where(press => answers.All(answer => answer.PressId != press.Id)).ToArray();
        var written = new List<PromotionSwapRow>();
        DateTimeOffset? wroteFrom = null;

        foreach (var press in unanswered)
        {
            var answer = await AnswerAsync(press, presses, [.. answers, .. written], session, runId, reads, cancellation);

            if (answer.Applied && wroteFrom is null)
            {
                wroteFrom = clock.UtcNow;
            }

            await InsertAsync(answer, runId, cancellation);
            written.Add(answer);
        }

        await RecordAsync(runId, started, written, cancellation);

        return (written, wroteFrom is null ? null : started);
    }

    async Task<PromotionSwapRow> AnswerAsync(PromotionPressRow press, IReadOnlyList<PromotionPressRow> presses, IReadOnlyList<PromotionSwapRow> answers, DateOnly session, string runId, PaperReads reads, CancellationToken cancellation)
    {
        PromotionSwapRow Refused(string words) => new(press.Id, press.Index, press.Family, false, words, null, null, null, null, [], session, clock.UtcNow);

        if (RuleFreezeDates.Holds(session))
        {
            return Refused("refused: " + RuleFreezeDates.ApprovalRefused);
        }

        var large = press.Index == Indices.IndexFamilies.LargeIndex;
        var indexName = OwnStepCandidate.NameOf(press.Index);
        var register = await reads.RegisteredCandidatesAsync();
        var standing = RuleRegister.Standing(press.Family, large, indexName, register, session, clock.UtcNow);
        var (live, variants) = RuleRegister.LiveAndVariants(standing);
        var lineage = RuleRegister.Registrations(press.Family, large, indexName, register).Where(row => FamilyRecords.IsLive(row.Candidate)).ToArray();
        var revisions = PaperBooks.RevisionsOf(lineage);

        if (press.Press == PromotionSwaps.Keep)
        {
            return Refused(PromotionSwaps.Kept(revisions.Count > 0 ? revisions[^1].Number : 1));
        }

        if (press.Press == PromotionSwaps.Promote)
        {
            if (presses.Any(one => one.Press == PromotionSwaps.Undo && one.Undoes == press.Id))
            {
                return Refused(PromotionSwaps.UndoWaiting(session));
            }

            if (live is null || revisions.Count == 0)
            {
                return Refused(NoRevision);
            }

            if (variants.FirstOrDefault(row => RuleRegister.Slug(row.Candidate) == press.Variant) is not { } variant)
            {
                return Refused(NoVariant);
            }

            var running = revisions[^1];
            var replaced = PromotionSwaps.ReplacedName(live.Candidate, running.Number);
            PromotionCarry[] carries =
            [
                new(live.Candidate, variant.Candidate, FamilyRecords.FirstSession(variant.RegisteredAt), session),
                new(replaced, live.Candidate, running.From, session),
            ];

            return await SwapAsync(press, live, variant.Candidate, variant, replaced, live, carries, running.Number + 1, session, runId,
                $"promoted on the operator's press {press.Id.ToString(CultureInfo.InvariantCulture)} at the monthly run of {press.Month}: the variant becomes revision {(running.Number + 1).ToString(CultureInfo.InvariantCulture)} and revision {running.Number.ToString(CultureInfo.InvariantCulture)} runs on as a variant",
                PromotionSwaps.Applied(running.Number + 1, running.Number, session), cancellation);
        }

        // An Undo of a swap the night applied: the same swap back.
        if (answers.FirstOrDefault(answer => answer.PressId == press.Undoes && answer.Applied) is not { Promoted: { } promoted, Replaced: { } replacedName } applied || live is null)
        {
            return Refused(press.Undoes is { } undone && answers.Any(answer => answer.PressId == undone) ? NotApplied : PromotionSwaps.UndoWaiting(session));
        }

        if (standing.FirstOrDefault(row => row.Candidate == replacedName) is not { } replacedRow)
        {
            return Refused(NoVariant);
        }

        var revisionNow = revisions.Count > 0 ? revisions[^1] : null;
        // Each record goes back to the name it was kept under, and the trades listed since the swap to the rule that listed them
        // as it stands again.
        PromotionCarry[] back =
        [
            .. applied.Carries.Select(carry => new PromotionCarry(carry.From, carry.From, carry.Since, carry.Until)),
            new(live.Candidate, replacedName, applied.Night, session),
            new(promoted, live.Candidate, applied.Night, session),
        ];

        return await SwapAsync(press, live, replacedName, replacedRow, promoted, live, back, (revisionNow?.Number ?? 1) + 1, session, runId,
            $"undone on the operator's press {press.Id.ToString(CultureInfo.InvariantCulture)}: the promotion of press {applied.PressId.ToString(CultureInfo.InvariantCulture)} swapped back, each record carried back",
            PromotionSwaps.UndoApplied(session), cancellation);
    }

    // The swap at one instant through the registrar: the row whose settings become the running revision's and the running
    // revision's own retired, the first's settings registered under the running revision's name and the second's under the
    // variant's name given, or nothing where the registrar refuses.
    async Task<PromotionSwapRow> SwapAsync(PromotionPressRow press, CandidateRow live, string risingName, CandidateRow rising, string fallingName, CandidateRow falling, IReadOnlyList<PromotionCarry> carries, int revision, DateOnly session, string runId, string evidence, string words, CancellationToken cancellation)
    {
        var registrar = new CandidateRegistrar(clock, databaseFile);
        var rows = await registrar.RowsAsync(cancellation);
        var risingRow = rows.First(row => row.Id == rising.Id);
        var fallingRow = rows.First(row => row.Id == falling.Id);
        Registration[] registrations =
        [
            new(live.Candidate, risingRow.Rule, risingRow.Test, risingRow.Evaluator, CandidateEvaluator.Read(risingRow.Parameters)),
            new(fallingName, fallingRow.Rule, fallingRow.Test, fallingRow.Evaluator, CandidateEvaluator.Read(fallingRow.Parameters)),
        ];
        var outcome = await registrar.ReplaceTogetherAsync(CandidateFamily.SetupFamilyOf(live.Evaluator), [risingName, live.Candidate], registrations, evidence, runId, cancellation);

        return outcome.Outcome == CandidateRegistrar.Refused
            ? new PromotionSwapRow(press.Id, press.Index, press.Family, false, "refused: " + outcome.Detail, null, null, null, null, [], session, clock.UtcNow)
            : new PromotionSwapRow(press.Id, press.Index, press.Family, true, words, revision, risingName, live.Candidate, fallingName, carries, session, clock.UtcNow);
    }

    async Task InsertAsync(PromotionSwapRow answer, string runId, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = InsertAnswer;
        command.Parameters.AddWithValue("$press_id", answer.PressId);
        command.Parameters.AddWithValue("$index_code", answer.Index);
        command.Parameters.AddWithValue("$family", answer.Family);
        command.Parameters.AddWithValue("$applied", answer.Applied ? 1 : 0);
        command.Parameters.AddWithValue("$words", answer.Words);
        command.Parameters.AddWithValue("$revision", (object?)answer.Revision ?? DBNull.Value);
        command.Parameters.AddWithValue("$promoted", (object?)answer.Promoted ?? DBNull.Value);
        command.Parameters.AddWithValue("$running", (object?)answer.Running ?? DBNull.Value);
        command.Parameters.AddWithValue("$replaced", (object?)answer.Replaced ?? DBNull.Value);
        command.Parameters.AddWithValue("$carries", answer.Carries.Count == 0 ? DBNull.Value : PromotionSwaps.WriteCarries(answer.Carries));
        command.Parameters.AddWithValue("$night", answer.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$swapped_at", Instant(answer.SwappedAt));
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(string runId, DateTimeOffset started, IReadOnlyList<PromotionSwapRow> written, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId + "-" + Stage);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Instant(started));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", Cards.DecisionCards.Ok);
        command.Parameters.AddWithValue("$rows_written", written.Count);
        command.Parameters.AddWithValue("$detail", written.Count == 0
            ? "no press waiting"
            : FormattableString.Invariant($"{written.Count(one => one.Applied)} swap(s) applied and {written.Count(one => !one.Applied)} press(es) answered without one: ") + string.Join("; ", written.Select(one => one.Words)));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
