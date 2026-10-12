using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.1: on the S&P 400 and 600 a swing rule may read no market check, the third value of its breadth
// setting beside the index's own breadth and the S&P 500's. It lists on a night its index's breadth closes the
// provisional rule's list; the form registers it with a pullback's own market dial off and refuses it with that dial on;
// a fourth value is refused; and the rule's words name it.
// see: An S&P 400 or 600 swing family may carry a variant with no market check, the check standing on its provisional rule
public partial class FixtureExpectations
{
    // The S&P 400 of the index families' store with ten more members, F1 to F10, each closing at 90 on the night under
    // the 200-day average its year near 99 holds: seven of seventeen members above their average, a breadth of 0.41 under
    // the floor of 0.45, so the index's market check is closed. None of the ten breaks out.
    static TemporaryStore ClosedIndexStore()
    {
        var store = IndexStore();

        foreach (var at in Enumerable.Range(1, 10))
        {
            var ticker = FormattableString.Invariant($"F{at}");

            StoreYear(store, ticker, IndexYear(90m, 1_000_000));
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
        }

        return store;
    }

    [Fact]
    public async Task ARuleReadingNoMarketCheckListsOnANightItsIndexsBreadthClosesTheProvisionalRulesList()
    {
        var breakout = IndexEvaluator("breakout-400");
        var provisional = IndexRules.Provisional(breakout);
        var off = new Dictionary<string, double>(provisional, StringComparer.Ordinal) { [IndexRuleCandidate.LargeBreadthParameter] = IndexRuleCandidate.NoMarketCheck };
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var store = ClosedIndexStore();
        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        var (names, income) = await IndexFamilies.InputsAsync(connection, "MID", night, default);
        var inputs = IndexNightRead.Prepare("MID", night, names, income)!;
        var levels = await IndexFamilies.LevelsAsync(connection, inputs, default);

        // Seven of seventeen above their average: the check closed.
        Assert.Equal(7.0 / 17.0, inputs.Breadth!.Value, 9);
        Assert.False(inputs.Open);

        IReadOnlyList<IndexAnswer> Answers(IReadOnlyDictionary<string, double> parameters) =>
            IndexNightRead.Answers(inputs, BreakoutRule.Name, IndexRules.Read(inputs, new IndexRule("rule", breakout, parameters, FreezeAt), levels));

        static string Fired(IReadOnlyList<IndexAnswer> answers) =>
            string.Join(",", answers.Where(answer => answer.Passed).OrderBy(answer => answer.Place).Select(answer => answer.Ticker));

        // The provisional rule lists nothing, every member failing at the check. The rule reading none lists what the
        // provisional rule lists on the open night, IA, IC, ID and IB in the order of their volume, 2.0, 1.8, 1.7 and 1.6
        // times, while FL fails under the floor, PR on its loss and NH and the ten with no setup.
        Assert.Equal(string.Empty, Fired(Answers(provisional)));
        Assert.All(Answers(provisional), answer => Assert.Equal(IndexNightRead.MarketClosed, answer.Reason));
        Assert.Equal("IA,IC,ID,IB", Fired(Answers(off)));
        Assert.Equal(
            (IndexNightRead.UnderTheFloors, IndexNightRead.NoProfit, IndexNightRead.NoSetup, IndexNightRead.NoSetup),
            (Why("FL"), Why("PR"), Why("NH"), Why("F1")));

        string? Why(string ticker) => Answers(off).Single(answer => answer.Ticker == ticker).Reason;
    }

    [Fact]
    public async Task TheFormRegistersNoMarketCheckWithAPullbacksOwnMarketDialOffAndRefusesItWithThatDialOnAndAFourthValue()
    {
        using var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        async Task<(int Code, string Said)> Variants(string family, string index, string variants, DateTimeOffset when) =>
            await RegisterVerbAt(store, when, RegisterVerb.IndexVariants, family, "--index", index, "--variants", variants);

        // The provisional pullback reads its own market dial at 0.45 over the index's breadth: reading no market check with
        // that dial on is refused, and a fourth value of the breadth on any family, each writing nothing.
        Assert.Equal(0.45, IndexRules.Provisional(IndexEvaluator("pullback-400"))[IndexPullbackCandidate.MarketParameter]);
        Assert.Contains("variant 1: a pullback reading no market check states its own market dial off, and 'market' is 0.45", (await Variants("pullback", "MID", "largeBreadth=2", at)).Said, StringComparison.Ordinal);
        Assert.Contains("variant 1: 'largeBreadth' is 3", (await Variants("breakout", "MID", "largeBreadth=3", at.AddSeconds(1))).Said, StringComparison.Ordinal);
        Assert.Contains("variant 1: 'largeBreadth' is 3", (await Variants("pullback", "SML", "largeBreadth=3,market=0", at.AddSeconds(2))).Said, StringComparison.Ordinal);
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // The six the remedy registers, a pullback, a breakout and a drift on each index, each the provisional rule with the
        // check off, the pullback's own market dial off with it, every one named for its index with the words ending on the
        // check off and none the live rule.
        foreach (var (index, minute) in new[] { ("MID", 1), ("SML", 2) })
        {
            Assert.Equal(0, (await Variants("pullback", index, "largeBreadth=2,market=0", at.AddMinutes(minute))).Code);
            Assert.Equal(0, (await Variants("breakout", index, "largeBreadth=2", at.AddMinutes(minute).AddSeconds(1))).Code);
            Assert.Equal(0, (await Variants("drift", index, "largeBreadth=2", at.AddMinutes(minute).AddSeconds(2))).Code);
        }

        var rows = TextRows(store, "SELECT candidate || '|' || evaluator FROM candidate_register ORDER BY id;");

        Assert.Equal(
            ["pullback-400", "breakout-400", "drift-400", "pullback-600", "breakout-600", "drift-600"],
            rows.Select(row => row.Split('|')[1]).ToArray());
        Assert.All(rows, row => Assert.EndsWith(", the market check off|" + row.Split('|')[1], row, StringComparison.Ordinal));
        Assert.All(rows, row => Assert.StartsWith("the S&P " + row.Split('|')[1][^3..] + " ", row, StringComparison.Ordinal));
        Assert.Contains("the market off and band strength", rows[0], StringComparison.Ordinal);

        // Each stands at the provisional setting with its moves and nothing else, and its own refusal reads it as one the
        // form admits.
        var register = await new CandidateRegistrar(FixedClock.At(at.AddMinutes(3), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

        foreach (var row in register)
        {
            var evaluator = IndexEvaluator(row.Evaluator);
            var expected = new Dictionary<string, double>(IndexRules.Provisional(evaluator), StringComparer.Ordinal) { [IndexRuleCandidate.LargeBreadthParameter] = IndexRuleCandidate.NoMarketCheck };

            if (evaluator is IndexPullbackCandidate)
            {
                expected[IndexPullbackCandidate.MarketParameter] = 0;
            }

            Assert.Equal(Sorted(expected), Sorted(CandidateEvaluator.Read(row.Parameters)));
            Assert.Null(IndexRules.Refusal(evaluator, CandidateEvaluator.Read(row.Parameters)));
        }

        // The words every index rule's registration states name the third value.
        Assert.Contains("the market check on the index's own breadth unless the S&P 500's or none is stated", IndexRules.RuleWords, StringComparison.Ordinal);
    }
}
