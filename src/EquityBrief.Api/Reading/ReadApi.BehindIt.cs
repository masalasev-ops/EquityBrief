using System.Text.Json;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;

namespace EquityBrief.Api.Reading;

// What the chart of what is behind a stock reads: every member's ledger readings on a night on an index, and a family's
// learned score on the index as the newest tester run there fitted it on all finished data, with whether any of its
// proposals passed, read as the night's cards read it.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
public sealed partial class ReadApi
{
    const string LedgerReadingsOn = "SELECT ticker, readings FROM ledger_reading WHERE index_code = $index AND session_date = $night;";

    const string NewestLoopRunOn = "SELECT run_id FROM loop_run WHERE index_code = $index AND through <= $night ORDER BY rowid DESC LIMIT 1;";

    const string RidgeScoreOf = @"
        SELECT m.parameters FROM loop_model m JOIN loop_run r ON r.run_id = m.run_id
        WHERE m.run_id = $run AND m.family = $family AND m.kind = 'ridge' AND m.learned_before > r.through;
    ";

    const string RidgePassed = "SELECT COUNT(*) FROM loop_proposal WHERE run_id = $run AND family = $family AND proposal LIKE $prefix AND passed = 1;";

    // Every member's readings on the night, by ticker, the catalogue's in its order; none for a night the ledger stored none of.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<double?>>> LedgerReadingsAsync(string index, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LedgerReadingsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var read = new Dictionary<string, IReadOnlyList<double?>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (JsonSerializer.Deserialize<double?[]>(reader.GetString(1)) is { Length: var length } readings && length == LedgerReadings.Count)
            {
                read[reader.GetString(0)] = readings;
            }
        }

        return read;
    }

    // A family's learned score on an index from the newest tester run there over data to the night and whether any of its
    // proposals passed, so a page for an earlier night reads no score fitted after it; no model where the run fitted none
    // for the family, and none with why where its stored text cannot be read.
    public async Task<(RidgeModel? Model, bool Passed, string? Unread)> RidgeScoreAsync(string index, string family, DateOnly night)
    {
        await using var connection = Open();

        async Task<object?> Scalar(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var scalar = await command.ExecuteScalarAsync();

            return scalar is DBNull ? null : scalar;
        }

        if (await Scalar(NewestLoopRunOn, ("$index", index), ("$night", night.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))) is not string run)
        {
            return (null, false, null);
        }

        var passed = await Scalar(RidgePassed, ("$run", run), ("$family", family), ("$prefix", RidgeScore.Proposal + "%")) is long count && count > 0;

        if (await Scalar(RidgeScoreOf, ("$run", run), ("$family", family)) is not string parameters)
        {
            return (null, passed, null);
        }

        try
        {
            return (RidgeModel.Parse(parameters), passed, null);
        }
        catch (Exception failure) when (failure is KeyNotFoundException or FormatException or OverflowException or IndexOutOfRangeException or ArgumentException)
        {
            return (null, passed, failure.Message);
        }
    }
}
