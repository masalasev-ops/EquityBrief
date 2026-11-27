using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction on the operator's orders of 2026-11-27: the long test reads its cap before
// each family, a family walked before it keeps every verdict and case its judgement gave, each line of a family after it
// is written by name as not reached, and the next month's long test takes those indices and families first; the books
// and the families holding a registered variant are walked before the rest, the smallest index first.
// see: The long test reads its cap before each family and keeps every verdict judged before it, each line it did not reach named and walked first the month after
public partial class FixtureExpectations
{
    static List<object?[]> LongCapRows(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();

        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];

            for (var at = 0; at < row.Length; at++)
            {
                row[at] = reader.IsDBNull(at) ? null : reader.GetValue(at);
            }

            rows.Add(row);
        }

        return rows;
    }

    // The order worked by hand: the family the month before did not reach first, then the two books in the page's order,
    // then the one family holding a variant, then the rest in the page's order; the indices smallest first, one holding a
    // line not reached taken first.
    [Fact]
    public void TheLongTestWalksTheFamiliesNotReachedFirstThenTheBooksThenTheFamiliesHoldingAVariant()
    {
        Assert.Equal(
            [DriftRule.Name, QualityRule.Name, ImprovingRule.Name, BreakoutRule.Name, "pullback", HeavyweightRule.Name, DipRule.Name],
            LongTest.FamilyOrder(RuleTests.FamilyNames, [DriftRule.Name], family => family == BreakoutRule.Name));
        Assert.Equal(["SML", "MID", "GSPC"], LongTest.IndexOrder([]));
        Assert.Equal(["MID", "SML", "GSPC"], LongTest.IndexOrder([("MID", DriftRule.Name)]));
        Assert.Equal(["SML", "MID", "GSPC"], WalkForwardTester.Indices.OrderBy(index => LongTest.Indices.ToList().IndexOf(index)));
    }

    // A tiny cap of two minutes, each family's walk taking one: the two families walked before it keep the verdicts their
    // judgement gave, and the third and fourth are kept line by line as not reached, never walked.
    [Fact]
    public void ATinyCapKeepsEveryVerdictJudgedBeforeItAndKeepsTheFamiliesAfterItAsNotReached()
    {
        var clock = new SweepClock(new DateTimeOffset(2026, 12, 5, 13, 0, 0, TimeSpan.Zero));
        var cap = clock.UtcNow.AddMinutes(2);
        var walkedFamilies = new List<string>();

        (string Family, Func<IReadOnlyList<RuleUnderTest>> Walk) Part(string family) => (family, () =>
        {
            walkedFamilies.Add(family);
            clock.UtcNow = clock.UtcNow.AddMinutes(1);

            return [new RuleUnderTest(family, family + "-live", RuleTests.Live, family + " in words", LoopProcedures.Risks, 63)];
        });

        var parts = new[] { Part("a"), Part("b"), Part("c"), Part("d") };
        var (walked, tested) = RuleTests.WalkInOrder(
            parts,
            ["b", "a", "d", "c"],
            () => clock.UtcNow < cap,
            part => part(),
            rules => [.. rules.Select(rule => new RuleTested(rule, "judged " + rule.Family, null, null, null))],
            family => [new RuleTested(new RuleUnderTest(family, family + "-live", RuleTests.Live, family + " in words", LoopProcedures.Risks, 63, Why: RuleTests.NotReached), RuleTests.Benchmark, null, null, RuleTests.NotReached)]);

        Assert.Equal(["b", "a"], walkedFamilies);
        Assert.Equal(["b", "a"], walked.Select(rule => rule.Family));
        Assert.Equal(["judged b", "judged a"], tested.Where(one => one.Why is null).Select(one => one.Against));
        Assert.Equal(["d-live", "c-live"], tested.Where(one => one.Why == RuleTests.NotReached).Select(one => one.Rule.Rule));
        Assert.Equal(["b", "a", "d", "c"], tested.Select(one => one.Rule.Family));
    }

    // The month's step under a cap of nothing over registered lines on the S&P 400 and 500: no history is read, every
    // registered line is written by name as not reached on the long history, the words name each, and the next month takes
    // the two indices holding those lines first and the S&P 600, holding none, after them.
    [Fact]
    public async Task TheMonthsLongTestUnderACapOfNothingWritesEveryLineByNameAndTheNextMonthTakesThemFirst()
    {
        using var store = new TemporaryStore().Migrated();
        var frozenAt = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('A', '2026-11-27', '10', '11', '9', '10', 1000, 'constructed', '2026-11-27T22:00:00Z', '10');");
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt, [RegisterVerb.IndexFamily, BreakoutRule.Name, "--index", "MID", .. MidBreakoutFrozen])).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(1), RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", FamilyApprovalDip)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(2), RegisterVerb.IndexFamily, QualityRule.Name, "--index", "GSPC", "--parameters", QualityFrozen)).Code);

        var standing = await StandingRowsAsync(store, frozenAt.AddMinutes(3));

        async Task<(bool Held, string Words, IReadOnlyList<string> Order)> MonthAsync(string month, DateTimeOffset at)
        {
            var tester = new WalkForwardTester(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile, store.Root, TextWriter.Null);
            var (held, words) = await tester.LongMonthAsync(month, cap: TimeSpan.Zero);
            var order = LongCapRows(store, FormattableString.Invariant($"SELECT run_id FROM run_log WHERE stage = '{WalkForwardTester.RulesVerb}' AND run_id LIKE '%{at:yyyyMMddTHHmmssZ}' ORDER BY rowid;"))
                .Select(row => ((string)row[0]!).Split('-')[3])
                .ToArray();

            return (held, words, order);
        }

        var november = await MonthAsync("2026-11", new DateTimeOffset(2026, 11, 7, 13, 0, 0, TimeSpan.Zero));
        var rows = LongCapRows(store, "SELECT index_code, family, rule, history, why, units FROM loop_rule WHERE month = '2026-11' ORDER BY rowid;");

        Assert.False(november.Held);
        Assert.Equal(["SML", "MID", "GSPC"], november.Order);
        Assert.Equal(standing.Count, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(LongTest.Long, row[3]);
            Assert.Equal(RuleTests.NotReached, row[4]);
            Assert.Null(row[5]);
        });
        Assert.Equal(standing.Select(row => row.Candidate).Order(StringComparer.Ordinal), rows.Select(row => (string)row[2]!).Order(StringComparer.Ordinal));
        Assert.Equal([BreakoutRule.Name, QualityRule.Name, DipRule.Name], rows.Select(row => (string)row[1]!).Distinct());
        Assert.Contains($"{rows.Count} line(s) not reached, each written by name and walked first at the next monthly run", november.Words, StringComparison.Ordinal);

        foreach (var row in rows)
        {
            Assert.Contains($"{row[2]}", november.Words, StringComparison.Ordinal);
        }

        // A second try of the month holds the S&P 400 and 500 already, runs again only the S&P 600, which holds no line and
        // so no long row, and still sums no verdict across the three over a month holding a line not reached.
        var again = await MonthAsync("2026-11", new DateTimeOffset(2026, 11, 8, 13, 0, 0, TimeSpan.Zero));

        Assert.False(again.Held);
        Assert.Equal(["SML"], again.Order);
        Assert.Contains("sums no verdict across the three", again.Words, StringComparison.Ordinal);
        Assert.Equal(0L, LongCapRows(store, "SELECT COUNT(*) FROM loop_pooled;")[0][0]);

        var december = await MonthAsync("2026-12", new DateTimeOffset(2026, 12, 5, 13, 0, 0, TimeSpan.Zero));

        Assert.Equal(["MID", "GSPC", "SML"], december.Order);
    }
}
