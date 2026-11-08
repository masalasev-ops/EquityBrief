using EquityBrief.Core.Loop;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// From 23.7 the monthly run's last step: the per-rule test over the long history on each index the month's runs do not hold
// one for yet, each family judged from its own first test year, then the verdict across the three indices over the same.
// Its sign vectors are cut to the fewest once it has used half its time, and it starts no index once its time has passed
// its cap, the indices after waiting for a second part run by hand the next day, which goes on from the indices it holds.
// It runs after the month's report, so a failure here holds nothing the report needs.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public sealed partial class WalkForwardTester
{
    public const string LongStep = "long";

    // From the 23.8 correction a long read with its index's own fund laid against its calendar from the long pull, the
    // second line every result over the long history reads beside its benchmark.
    // see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
    public static async Task<LoopRead> WithFundAsync(LoopRead read, History.LongHistoryReader reader, DateOnly through, CancellationToken cancellation = default)
    {
        if (FundLine.FundOf(read.Index) is not { } fund)
        {
            return read;
        }

        var series = await reader.SeriesOfAsync([fund], through, cancellation);

        return series.Count == 0 ? read : read with { Fund = FundLine.Aligned(read.Calendar, series[0].Closes) };
    }

    const string LongRunsOfTheMonth = "SELECT DISTINCT index_code FROM loop_rule WHERE month = $month AND history = 'long';";

    public async Task<(bool Held, string Words)> LongMonthAsync(string month, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var held = new HashSet<string>(StringComparer.Ordinal);

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            await using var command = connection.CreateCommand();
            command.CommandText = LongRunsOfTheMonth;
            command.Parameters.AddWithValue("$month", month);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                held.Add(reader.GetString(0));
            }
        }

        var said = new List<string>();
        var left = new List<string>();

        foreach (var index in MonthlyRun.Indices)
        {
            if (held.Contains(index))
            {
                said.Add($"the {Cards.DecisionCards.NameOf(index)}'s held already");

                continue;
            }

            var elapsed = clock.UtcNow - started;

            if (!LongTest.GoesOn(elapsed))
            {
                left.Add(index);

                continue;
            }

            var draws = LongTest.DrawsAt(elapsed);
            var at = clock.UtcNow;

            if (await RulesAsync(index, month, cancellation, longRun: true, draws: draws) != 0)
            {
                return (false, $"the long test on the {Cards.DecisionCards.NameOf(index)} did not finish its run");
            }

            said.Add(FormattableString.Invariant($"the {Cards.DecisionCards.NameOf(index)}'s in {(clock.UtcNow - at).TotalMinutes:0} min at {draws:N0} sign vectors"));
        }

        var spent = clock.UtcNow - started;

        if (left.Count > 0)
        {
            return (false, FormattableString.Invariant($"the long test reached its cap of {LongTest.Cap.TotalHours:0} hours in {spent.TotalMinutes:0} min with {string.Join(", ", left)} to run: {string.Join("; ", said)}; run 'loop-long --month {month}' by hand the next day to go on"));
        }

        var (pooled, words) = await new PooledTest(clock, databaseFile, dataRoot, output, pause).RunAsync(month, cancellation: cancellation, history: LongTest.Long);

        return (pooled, FormattableString.Invariant($"the long test in {(clock.UtcNow - started).TotalMinutes:0} min: {string.Join("; ", said)}; across the three, {words}"));
    }
}
