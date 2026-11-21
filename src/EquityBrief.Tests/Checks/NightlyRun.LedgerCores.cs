using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 24.9: on the operator's ruling of 2026-11-15 on item 5 the ledger's builds compute a chunk's sessions across the
// machine's cores again only behind a test building the same sessions both ways on each of the three indices, failing on any
// row that differs: every column of every history row and every night's count, read back and compared, and the comparison
// shown to fail on one planted difference. Where several sessions fail the earliest one's failure is raised, as one at a time.
// see: The ledger's builds compute a chunk's sessions across the machine's cores, every row the same as one at a time on each index
public partial class NightlyRun
{
    // Every column of every history row and of every night's row, in a fixed order, a row a line.
    static List<string> EveryHistoryRow(TemporaryStore store)
    {
        var rows = new List<string>();

        using var connection = store.Open();

        foreach (var table in new[] { "setup", "setup_night" })
        {
            using var command = connection.CreateCommand();

            command.CommandText = $"SELECT * FROM {table} WHERE source = 'history' ORDER BY 1, 2, 3, 4, 5;";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(table + "|" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? "null" : Convert.ToString(reader.GetValue(at), System.Globalization.CultureInfo.InvariantCulture))));
            }
        }

        return rows;
    }

    [Theory]
    [InlineData("GSPC")]
    [InlineData("MID")]
    [InlineData("SML")]
    public async Task TheHistoryBuiltAcrossTheCoresWritesEveryRowTheBuildOneSessionAtATimeWrites(string index)
    {
        using var once = ComparisonStore(index);
        using var across = once.Copy();
        var clock = FixedClock.At(new DateTimeOffset(2026, 7, 21, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Assert.Equal(0, await new SetupLedger(clock, once.DatabaseFile) { Cores = 1 }.BuildAsync(once.Root, index, LedgerSession(ComparisonFrom), LedgerSession(ComparisonThrough), new StringWriter()));
        Assert.Equal(0, await new SetupLedger(clock, across.DatabaseFile) { Cores = 8 }.BuildAsync(across.Root, index, LedgerSession(ComparisonFrom), LedgerSession(ComparisonThrough), new StringWriter()));

        var oneAtATime = EveryHistoryRow(once);
        var acrossTheCores = EveryHistoryRow(across);

        // The build wrote setups, so the comparison reads something.
        Assert.Contains(oneAtATime, row => row.StartsWith("setup|", StringComparison.Ordinal));
        Assert.Equal(oneAtATime, acrossTheCores);

        // One planted difference, a setup's result moved, is found.
        using (var connection = across.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE setup SET result = COALESCE(result, 0) + 0.001 WHERE rowid = (SELECT MIN(rowid) FROM setup WHERE source = 'history');";
            command.ExecuteNonQuery();
        }

        Assert.NotEqual(oneAtATime, EveryHistoryRow(across));
    }

    // No source a rule's version pins is one 24.9 writes, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheBuildAcrossTheCoresWrites()
    {
        string[] written = ["src/EquityBrief.Worker/Ledger/SetupLedger.cs", "src/EquityBrief.Worker/Ledger/SetupLedger.Long.cs", "src/EquityBrief.Worker/Program.cs"];
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(written, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(written.Intersect(pinned, StringComparer.Ordinal));
    }

    // Sessions computed across the cores each kept in its own place; where several fail, the earliest one's failure raised.
    [Fact]
    public void SessionsComputedAcrossTheCoresKeepTheirPlacesAndRaiseTheEarliestFailure()
    {
        Assert.Equal([.. Enumerable.Range(0, 500).Select(item => item * item)], SetupLedger.InParallel(500, item => item * item, 8));

        var failure = Assert.Throws<InvalidOperationException>(() => SetupLedger.InParallel(500, item => item % 97 == 13 ? throw new InvalidOperationException(FormattableString.Invariant($"item {item}")) : item, 8));

        Assert.Equal("item 13", failure.Message);
    }
}
