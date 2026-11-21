using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.History;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// The fill's counts; with no archive to ask, a filer that filed after its answer was kept is read from it all the same and
// counted as kept before its newest filing.
public sealed record FallbackFill(int Filers, int FromKept, int Asked, int Refused, int Stored, int KeptBefore = 0, int NoAnswer = 0);

// From 24.12, by hand on the operator's word: fills the two fallback lines once for every member's filer, read from the
// SEC's answer the long facts pull kept where the filer filed nothing after it was kept, and otherwise asked of the SEC's
// archive again, free and at its pace; each period as first filed, a period already held left as it is. After it the
// filings refresh keeps the lines as it keeps the filed facts. It writes the fallback facts and its own run log row alone.
// see: Where operating income is not filed a reading takes income before taxes plus interest expense, else revenue less costs and expenses
public sealed class FallbackFactFill(IClock clock, string databaseFile)
{
    public const string Stage = "fallback-facts";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.FallbackFact, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.FilingsArchive]);

    const string Filers = @"
        SELECT DISTINCT c.cik FROM company c
        WHERE c.cik IS NOT NULL
          AND c.fetched_at = (SELECT MAX(fetched_at) FROM company d WHERE d.ticker = c.ticker)
          AND c.ticker IN (SELECT ticker FROM member_reading WHERE session_date = (SELECT MAX(session_date) FROM member_reading))
        ORDER BY c.cik;";

    const string Insert = @"
        INSERT INTO fallback_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id)
        VALUES ($cik, $concept, $period_start, $period_end, $dollars, $filed, $form, $accession, $run_id)
        ON CONFLICT (cik, concept, period_start, period_end) DO NOTHING;";

    const string NewestFiled = "SELECT MAX(filed) FROM filed_fact WHERE cik = $cik;";

    const string InsertRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows, 0, $requests, '0', $detail);";

    // The filer's kept answer where the newest filing it holds is on or after the filer's newest filing the store holds, else
    // none: from the 24.12 correction of the trusts read off the answer itself and never off its file's date, since an answer
    // copied or written after a filing need not hold it.
    public static bool KeptIsCurrent(DateOnly? keptThrough, DateOnly? newestFiled) => newestFiled is not { } filed || keptThrough is { } through && through >= filed;

    // The newest filing day any fact of an answer of the SEC's facts holds, none where it holds no fact.
    public static DateOnly? NewestFiledIn(string answer)
    {
        using var document = System.Text.Json.JsonDocument.Parse(answer);
        DateOnly? newest = null;

        if (!document.RootElement.TryGetProperty("facts", out var facts) || facts.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        foreach (var taxonomy in facts.EnumerateObject().Where(one => one.Value.ValueKind == System.Text.Json.JsonValueKind.Object))
        {
            foreach (var concept in taxonomy.Value.EnumerateObject())
            {
                if (!concept.Value.TryGetProperty("units", out var units) || units.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var unit in units.EnumerateObject().Where(one => one.Value.ValueKind == System.Text.Json.JsonValueKind.Array))
                {
                    foreach (var fact in unit.Value.EnumerateArray())
                    {
                        if (fact.TryGetProperty("filed", out var filed) && filed.ValueKind == System.Text.Json.JsonValueKind.String
                            && DateOnly.TryParseExact(filed.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                            && (newest is null || day > newest))
                        {
                            newest = day;
                        }
                    }
                }
            }
        }

        return newest;
    }

    public async Task<FallbackFill> FillAsync(string kept, IFilerFactsFeed? feed, string runId, Action<string> progress, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var filers = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Filers;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                filers.Add(reader.GetString(0));
            }
        }

        var (fromKept, asked, refused, stored, keptBefore, noAnswer) = (0, 0, 0, 0, 0, 0);

        foreach (var cik in filers)
        {
            DateOnly? newest = null;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = NewestFiled;
                command.Parameters.AddWithValue("$cik", cik);

                if (await command.ExecuteScalarAsync(cancellation) is string day)
                {
                    newest = DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }

            var file = LongFactPull.FileOf(kept, cik);
            var keptAnswer = File.Exists(file) ? LongFactPull.Read(file) : null;
            var current = keptAnswer is not null && KeptIsCurrent(NewestFiledIn(keptAnswer), newest);
            string answer;

            if (keptAnswer is not null && (feed is null || current))
            {
                answer = keptAnswer;
                fromKept++;
                keptBefore += current ? 0 : 1;
            }
            else if (feed is null)
            {
                noAnswer++;

                continue;
            }
            else
            {
                try
                {
                    answer = await feed.FactsAsync(cik, cancellation);
                    asked++;
                }
                catch (HttpRequestException refusal)
                {
                    refused++;
                    progress(FormattableString.Invariant($"{Stage}: CIK {cik} refused, nothing stored for it: {refusal.Message}"));

                    continue;
                }
            }

            var rows = IncomeDiagnosis.Stored(ConceptAnswers.FromFacts(answer, cik, IncomeDiagnosis.FallbackConcepts));

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            foreach (var row in rows)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = Insert;
                command.Parameters.AddWithValue("$cik", cik);
                command.Parameters.AddWithValue("$concept", row.Concept);
                command.Parameters.AddWithValue("$period_start", row.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$period_end", row.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$dollars", Money.ToStorage(row.Value));
                command.Parameters.AddWithValue("$filed", row.Filed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$form", row.Form);
                command.Parameters.AddWithValue("$accession", row.Accession);
                command.Parameters.AddWithValue("$run_id", runId);
                stored += await command.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);

            if ((fromKept + asked) % 200 == 0)
            {
                progress(FormattableString.Invariant($"{Stage}: {fromKept + asked} of {filers.Count} filer(s) read, {stored} row(s) stored"));
            }
        }

        var fill = new FallbackFill(filers.Count, fromKept, asked, refused, stored, keptBefore, noAnswer);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = InsertRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$outcome", fill.Refused == 0 ? "ok" : "partial");
            command.Parameters.AddWithValue("$rows", fill.Stored);
            command.Parameters.AddWithValue("$requests", fill.Asked + fill.Refused);
            command.Parameters.AddWithValue("$detail", Words(fill));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        return fill;
    }

    public static string Words(FallbackFill fill) =>
        FormattableString.Invariant($"{fill.Filers} filer(s): {fill.FromKept} read from the kept answers, {fill.KeptBefore} of them kept before the filer's newest filing, {fill.NoAnswer} with no kept answer and none asked, {fill.Asked} asked of the SEC, {fill.Refused} refused; {fill.Stored} fallback row(s) stored");
}
