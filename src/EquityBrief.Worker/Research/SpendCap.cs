using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// What one paid call came to: the answer and its price, or the refusal and why.
public sealed record PaidCall(ResearchAnswer? Answer, decimal Price, SpendVerdict Verdict, string? Failure)
{
    public bool Answered => Answer is not null;

    public bool Paused => Verdict.Paused;
}

// The spend cap. The one component that makes a paid call.
//
// It holds the research model, and the runners hold it rather than the model, so a call
// that would pass a cap is refused where every paid call is made rather than in each
// caller that remembered to ask. Before a call it reads what the run log says was spent
// today and this month and judges the call by the most it could cost; after it, it
// records what the call did cost on a run log row of its own. The ledger is therefore
// the store's, measured, and never a figure a caller kept.
// see: Every paid call is made through the spend cap, which holds the research model
// see: The spend cap counts a UTC day and a UTC month, and refuses a call that could take spend past either
// see: The spend cap is a stop, not an allowance
public sealed class SpendCap(
    IResearchModelFeed model,
    SpendCaps caps,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.ResearchModel]);

    // The stage each call's row carries, with the section after it, so a pass's calls
    // are rows of their own under the pass's run and one section's call is one row.
    public const string Stage = "research call";

    public const string Paid = "ok";
    public const string PausedOutcome = "paused";
    public const string Refused = "refused";
    public const string Unavailable = "unavailable";

    // Everything spent this UTC month, which is as far back as a verdict reads.
    const string SpentSince = @"
        SELECT started_at, spend
        FROM run_log
        WHERE started_at >= $since;
    ";

    const string AppendCall = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, $model_calls, $network_requests, $spend, $detail);
    ";

    public static string StageFor(string section) => Stage + ": " + section;

    public async Task<PaidCall> AskAsync(ModelRequest request, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var ceiling = model.Ceiling(request);
        var verdict = SpendRule.Judge(await LedgerAsync(connection, startedAt, cancellation), caps, startedAt, ceiling);

        if (verdict.Paused)
        {
            // Refused before the call, with the line the page states, so the run page
            // shows a pass that was stopped rather than a pass that did nothing.
            await RecordAsync(connection, runId, request, startedAt, PausedOutcome, 0, 0m, new
            {
                section = request.Section,
                paused = verdict.Line,
                cap = verdict.Cap,
                ceiling = Money(ceiling),
                resumesAt = verdict.ResumesAt!.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            }, cancellation);

            return new PaidCall(null, 0m, verdict, verdict.Line);
        }

        ResearchAnswer answer;

        try
        {
            answer = await model.CompleteAsync(request, cancellation).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is ResearchModelUnavailable or ProviderRefusal)
        {
            // Nothing arrived, so nothing was counted and nothing is priced. The row says
            // a call was attempted, which is what the run page is for.
            var outcome = failure is ResearchModelUnavailable ? Unavailable : Refused;

            await RecordAsync(connection, runId, request, startedAt, outcome, 1, 0m, new
            {
                section = request.Section,
                failed = failure.Message,
            }, cancellation);

            return new PaidCall(null, 0m, verdict, failure.Message);
        }

        var price = model.Price(answer);

        await RecordAsync(connection, runId, request, startedAt, Paid, 1, price, new
        {
            section = request.Section,
            model = model.Identity,
            answeredBy = answer.Model,
            created = answer.Created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            cacheHitTokens = answer.CacheHitTokens,
            cacheMissTokens = answer.CacheMissTokens,
            completionTokens = answer.CompletionTokens,
            reasoningTokens = answer.ReasoningTokens,
            price = Money(price),
        }, cancellation);

        return new PaidCall(answer, price, verdict, null);
    }

    // What the run log says was spent this UTC month, read as stored.
    public static async Task<SpendLedger> LedgerAsync(SqliteConnection connection, DateTimeOffset now, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SpentSince;
        command.Parameters.AddWithValue("$since", SpendLedger.MonthStart(now).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        var rows = new List<SpentRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new SpentRow(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                decimal.Parse(reader.GetString(1), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)));
        }

        return new SpendLedger(rows);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        ModelRequest request,
        DateTimeOffset startedAt,
        string outcome,
        int calls,
        decimal price,
        object detail,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendCall;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", StageFor(request.Section));
        command.Parameters.AddWithValue("$started_at", startedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$model_calls", calls);

        // A paid call is a request to a provider as well as a model call, which is what
        // tells it from a call to the operator's own runtime on the run page.
        command.Parameters.AddWithValue("$network_requests", calls);

        // Money as TEXT, written invariant and never rounded, so the ledger sums what
        // each call cost rather than what a page would draw of it.
        command.Parameters.AddWithValue("$spend", price.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(detail));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Money(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);
}
