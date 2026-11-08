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

    // From the 23.8 correction the dip's own sweep's reading of the long history with its live rule's setting, levels and
    // words, which the long search walks its grid over; none, with why, where no live dip rule stands on the index or it
    // stands at hooks the grid's walk reads none of.
    // see: A search over the long history re-runs every third test year with its setting carried between
    public sealed record LongDip(Sweep.IndexSweepRunner.DipRead Read, int[] Setting, IReadOnlyList<Sweep.DialLevel<Sweep.FamilyListing>> Levels, string Words);

    public static async Task<(LongDip? Dip, string? Why)> LongDipAsync(string databaseFile, LoopRead read, History.LongHistoryReader longHistory, Sweep.SweepHistory history, DateOnly through, DateTimeOffset at, Action<string> progress, CancellationToken cancellation = default)
    {
        var standing = Core.Candidates.ShadowColumn.StandingAt(await RegisterAsync(databaseFile, cancellation), at);

        if (LiveOn(standing, read.Index, Core.Families.DipRule.Name) is not { } live)
        {
            return (null, "no live dip rule stands on the index");
        }

        var parameters = Core.Candidates.DipCandidate.Renamed(Core.Candidates.CandidateEvaluator.Read(live.Parameters));
        var evaluator = Core.Candidates.CandidateEvaluators.Find(live.Evaluator)!;

        if (LoopRegistrations.HooksOf(evaluator, parameters).Count > 0)
        {
            return (null, "its live rule stands at hooks the grid's walk reads none of");
        }

        progress("reading the dip's own sweep over the long history");

        var large = read.Index == global::EquityBrief.Worker.Indices.IndexFamilies.LargeIndex
            ? read.Inputs
            : await longHistory.ReadAsync(global::EquityBrief.Worker.Indices.IndexFamilies.LargeIndex, through, null, cancellation);
        var market = await LongSeriesAsync(longHistory, history, [Sweep.IndexSweepRunner.DipFund(read.Index), Core.Families.MarketCloses.Vix], through, cancellation);
        var dipRead = Sweep.IndexSweepRunner.DipRead.Of(read, large, market);
        var (setting, levels) = dipRead.SettingOf(parameters);

        return (new LongDip(dipRead, setting, levels, "the dip rule at " + Dips.DipRules.Short(((Core.Candidates.DipCandidate)evaluator).SettingOf(parameters), read.Index)), null);
    }

    // The named market series over the long history: the long pull's closes, then the standing pull's after its last.
    public static async Task<IReadOnlyList<Sweep.SweepMarketSeries>> LongSeriesAsync(History.LongHistoryReader longHistory, Sweep.SweepHistory history, IReadOnlyList<string> named, DateOnly through, CancellationToken cancellation = default)
    {
        var longs = await longHistory.SeriesOfAsync(named, through, cancellation);
        var standing = await history.SeriesOfAsync(named, through, cancellation);
        var joined = new List<Sweep.SweepMarketSeries>();

        foreach (var series in named)
        {
            var older = longs.FirstOrDefault(one => one.Series == series);
            var newer = standing.FirstOrDefault(one => one.Series == series);

            if (older is null || older.Closes.Count == 0)
            {
                if (newer is not null)
                {
                    joined.Add(newer);
                }

                continue;
            }

            var last = older.Closes[^1].Session;

            joined.Add(older with { Closes = [.. older.Closes, .. (newer?.Closes ?? []).Where(one => one.Session > last)] });
        }

        return joined;
    }

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
