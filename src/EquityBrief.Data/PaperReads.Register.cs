using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Paper;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Data;

public sealed partial class PaperReads
{
    // The candidate register whole: what stands is a question about an instant, and the answer is arithmetic over every row,
    // a retirement a row like any other.
    const string RegisteredCandidates = @"
        SELECT id, candidate, evaluator, event, retires, registered_at, parameters, evidence, evaluator_version
        FROM candidate_register
        ORDER BY id;
    ";

    // Every replay of a family rule, oldest first, which where each rule's record counts from is read off.
    const string FamilyReplays = "SELECT outcome, detail, started_at FROM run_log WHERE stage LIKE $stages ORDER BY rowid;";

    const string IndexNightOn = @"
        SELECT index_code, session_date, members, breadth, market_open, settings, rebalanced, fault FROM index_family_night
        WHERE index_code = $index AND session_date = $on;
    ";

    const string NewestCutoff = "SELECT cutoff FROM reading_pin WHERE history_completed_at IS NOT NULL ORDER BY id DESC LIMIT 1;";

    // The operator's taken trades' columns, in the order every reader of them reads.
    public const string TakenColumns = "ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through, ended_on, end_price, end_reason, result";

    const string EveryTaken = "SELECT " + TakenColumns + " FROM taken_trade ORDER BY taken_at, ticker;";

    public async Task<IReadOnlyList<CandidateRow>> RegisteredCandidatesAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RegisteredCandidates;

        var rows = new List<CandidateRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CandidateRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.ParseExact(
                    reader.GetString(5),
                    "yyyy-MM-ddTHH:mm:ssZ",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                reader.IsDBNull(6) ? "{}" : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8)));
        }

        return rows;
    }

    // Every replay of a family rule the run log holds, each read back as where the rule's record counts from.
    // see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
    public async Task<IReadOnlyList<FamilyReplayRow>> FamilyReplaysAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FamilyReplays;
        command.Parameters.AddWithValue("$stages", FamilyRecords.ReplayStages);

        var rows = new List<FamilyReplayRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var at = DateTimeOffset.ParseExact(reader.GetString(2), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            if (FamilyRecords.ReplayOf(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), at) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    // An index's night as its families stored it, with the failure that stopped it where its part of the night failed,
    // and none for a night they did not read.
    public async Task<IndexNightRow?> IndexNightAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexNightOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new IndexNightRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.GetInt64(4) == 1,
                reader.GetString(5),
                reader.GetInt64(6) == 1,
                reader.IsDBNull(7) ? null : reader.GetString(7))
            : null;
    }

    // The local model's cutoff the newest reading version whose history read is complete states.
    public async Task<DateOnly?> ModelCutoffAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestCutoff;

        return await command.ExecuteScalarAsync() is string day ? DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    // Every taken trade, which the accounts read the operator's own from.
    public async Task<IReadOnlyList<TakenTradeRow>> TakenTradesAsync()
    {
        await using var connection = Open();

        return await TakenAsync(connection, EveryTaken, []);
    }

    // The taken trades a statement reads, each in the columns every reader of them reads.
    public static async Task<IReadOnlyList<TakenTradeRow>> TakenAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, string Value)> parameters)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<TakenTradeRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            decimal? Price(int column) =>
                reader.IsDBNull(column) ? null : decimal.Parse(reader.GetString(column), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

            DateOnly Day(int column) => DateOnly.ParseExact(reader.GetString(column), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            rows.Add(new TakenTradeRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Day(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                Price(6)!.Value,
                Day(7),
                reader.GetInt64(8) == 1,
                reader.GetInt64(9) == 1,
                Price(10),
                Price(11),
                Price(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
                Price(14),
                reader.IsDBNull(15) ? null : Day(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : Day(17),
                Price(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetDouble(20)));
        }

        return rows;
    }
}
