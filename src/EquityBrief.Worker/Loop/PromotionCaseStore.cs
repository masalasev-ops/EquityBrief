using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Paper;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The monthly run's cases: on each index, Paper trading's accounts worked by the core over the one reader as of the newest
// night, the case for promotion's rows read off them, and each case standing stored with its figures, whether or not the
// page is read, a month's case stored once. It writes nothing else but its run's row.
// see: The monthly run stores each case standing from Paper trading's accounts in Core, and the local run writes its words
public sealed class PromotionCaseStore(IClock clock, string databaseFile, string dataRoot) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.PromotionCase, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Step = "cases";

    public const string Stage = "promotion-cases";

    const string NewestNight = "SELECT MAX(session_date) FROM listing;";

    const string InsertCase = @"
        INSERT INTO promotion_case (month, index_code, family, variant, night, figures, figures_key, stored_at, run_id)
        VALUES ($month, $index_code, $family, $variant, $night, $figures, $figures_key, $stored_at, $run_id)
        ON CONFLICT (month, index_code, family, variant) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, 0, 0, '0', $detail);
    ";

    // The cases standing on each index as of the newest night, stored under the month; the step's words say how many on each.
    public async Task<(bool Held, string Words)> StoreAsync(string month, string runId, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        DateOnly night;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestNight;

            if (await command.ExecuteScalarAsync(cancellation) is not string newest)
            {
                return (true, "no night is stored, so no case was worked");
            }

            night = DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var reads = new PaperReads(databaseFile);
        var settings = PaperFiguresRun.SettingsOf(dataRoot);
        var cases = new List<PromotionCaseView>();
        var said = new List<string>();

        foreach (var index in PaperFiguresRun.Indices)
        {
            var name = Core.Candidates.OwnStepCandidate.NameOf(index);
            var worked = await PaperWork.WorkAsync(reads, index, name, index == PaperFiguresRun.Indices[0], night, settings);
            var (_, longYears) = await reads.RuleTestsAsync(index, LongTest.Long);
            var standing = PromotionCases.Standing(PromotionCases.Rows(index, name, worked.Sources, worked.Views, worked.Sessions, longYears, night));

            cases.AddRange(standing);
            said.Add(FormattableString.Invariant($"{standing.Count} on the {name}"));
            await output.WriteLineAsync(FormattableString.Invariant($"cases: {standing.Count} case(s) standing on the {name} as of {night:yyyy-MM-dd}"));
        }

        var stored = 0;
        await using var transaction = connection.BeginTransaction();
        var at = Instant(clock.UtcNow);

        foreach (var one in cases)
        {
            var figures = PromotionCases.Figures(one);

            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = InsertCase;
            insert.Parameters.AddWithValue("$month", month);
            insert.Parameters.AddWithValue("$index_code", one.Index);
            insert.Parameters.AddWithValue("$family", one.Family);
            insert.Parameters.AddWithValue("$variant", one.Variant);
            insert.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$figures", figures);
            insert.Parameters.AddWithValue("$figures_key", PromotionCases.FiguresKey(figures));
            insert.Parameters.AddWithValue("$stored_at", at);
            insert.Parameters.AddWithValue("$run_id", runId);

            stored += await insert.ExecuteNonQueryAsync(cancellation);
        }

        var words = FormattableString.Invariant($"{cases.Count} case(s) standing as of {night:yyyy-MM-dd}, {string.Join(", ", said)}; {stored} stored for {month}");

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId + "-" + Step);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", Instant(started));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", Cards.DecisionCards.Ok);
            command.Parameters.AddWithValue("$rows_written", stored);
            command.Parameters.AddWithValue("$detail", words);

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return (true, words);
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
