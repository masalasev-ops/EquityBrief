using System.Globalization;
using EquityBrief.Core.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// The Loop page's reads: the months an index's tester runs are for, the newest run for a month, and that run's
// proposals and test years. The page draws them and computes none of them.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
public sealed partial class ReadApi
{
    const string LoopMonthsOf = "SELECT DISTINCT month FROM loop_run WHERE index_code = $index ORDER BY month DESC;";

    // The newest run by the order the runs were written, as every read of the newest run is taken.
    const string LoopRunOf = @"
        SELECT run_id, month, index_code, through, started_at, folds FROM loop_run
        WHERE index_code = $index AND month = $month ORDER BY rowid DESC LIMIT 1;";

    const string LoopProposalsOf = @"
        SELECT family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding
        FROM loop_proposal WHERE run_id = $run_id AND index_code = $index ORDER BY family, proposal;";

    const string LoopFindingsOf = @"
        SELECT family, figure, value, trades, words
        FROM loop_finding WHERE run_id = $run_id AND index_code = $index ORDER BY family, rowid;";

    const string LoopTestsOf = @"
        SELECT family, proposal, year, complete, chosen, current_units, proposed_units, current_total, proposed_total
        FROM loop_test WHERE run_id = $run_id AND index_code = $index ORDER BY family, proposal, year;";

    // Each reading's spread a run stored, in the order it wrote them, which is the catalogue's.
    const string LoopReadingsOf = @"
        SELECT family, reading, units, winners, losers, winners_median, losers_median, deciles
        FROM loop_reading WHERE run_id = $run_id AND index_code = $index ORDER BY family, rowid;";

    public async Task<IReadOnlyList<LoopReadingRow>> LoopReadingsAsync(LoopRunRow run)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopReadingsOf;
        command.Parameters.AddWithValue("$run_id", run.RunId);
        command.Parameters.AddWithValue("$index", run.Index);

        var rows = new List<LoopReadingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopReadingRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                Optional(reader, 5),
                Optional(reader, 6),
                System.Text.Json.JsonSerializer.Deserialize<double?[]>(reader.GetString(7)) ?? []));
        }

        return rows;
    }

    // Each family's learned score a run stored, the one fitted on all finished data, cut after the run's last session.
    const string LoopModelsOf = @"
        SELECT m.family, m.learned_before, m.setups, m.hash, m.words
        FROM loop_model m JOIN loop_run r ON r.run_id = m.run_id
        WHERE m.run_id = $run_id AND m.learned_before > r.through ORDER BY m.family;";

    public async Task<IReadOnlyList<LoopModelRow>> LoopModelsAsync(LoopRunRow run)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopModelsOf;
        command.Parameters.AddWithValue("$run_id", run.RunId);

        var rows = new List<LoopModelRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopModelRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    // Each card a night stored on an index for a family a score reaches, with its rank, none until the score passed.
    const string LoopRanksOf = @"
        SELECT family, ticker, score_rank FROM decision_card
        WHERE index_code = $index AND session_date = $night AND similar IS NOT NULL ORDER BY family, place, ticker;";

    public async Task<IReadOnlyList<LoopRankRow>> LoopRanksAsync(string index, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopRanksOf;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<LoopRankRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopRankRow(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<string>> LoopMonthsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopMonthsOf;
        command.Parameters.AddWithValue("$index", index);

        var months = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            months.Add(reader.GetString(0));
        }

        return months;
    }

    public async Task<LoopRunRow?> LoopRunAsync(string index, string month)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopRunOf;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$month", month);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new LoopRunRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(4),
                reader.GetInt32(5))
            : null;
    }

    public async Task<IReadOnlyList<LoopProposalRow>> LoopProposalsAsync(LoopRunRow run)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopProposalsOf;
        command.Parameters.AddWithValue("$run_id", run.RunId);
        command.Parameters.AddWithValue("$index", run.Index);

        var rows = new List<LoopProposalRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopProposalRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                Optional(reader, 7),
                reader.GetInt64(8) == 1,
                reader.GetInt64(9) == 1,
                reader.GetInt32(10),
                reader.GetInt32(11),
                Optional(reader, 12),
                reader.GetInt64(13) == 1,
                Optional(reader, 14),
                reader.GetInt32(15),
                reader.GetInt64(16) == 1,
                reader.IsDBNull(17) ? null : reader.GetString(17)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<LoopFindingRow>> LoopFindingsAsync(LoopRunRow run)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopFindingsOf;
        command.Parameters.AddWithValue("$run_id", run.RunId);
        command.Parameters.AddWithValue("$index", run.Index);

        var rows = new List<LoopFindingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopFindingRow(reader.GetString(0), reader.GetString(1), Optional(reader, 2), reader.GetInt32(3), reader.GetString(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<LoopTestRow>> LoopTestsAsync(LoopRunRow run)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LoopTestsOf;
        command.Parameters.AddWithValue("$run_id", run.RunId);
        command.Parameters.AddWithValue("$index", run.Index);

        var rows = new List<LoopTestRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LoopTestRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt64(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetDouble(7),
                reader.GetDouble(8)));
        }

        return rows;
    }

    static double? Optional(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);
}
