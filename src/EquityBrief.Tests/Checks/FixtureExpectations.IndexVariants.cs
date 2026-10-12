using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.11: a family's variants on the S&P 400 or 600 registered with no live rule, its provisional
// rule staying the live one, each the provisional setting with its moves; and the sector heavyweights' twelve months'
// strength skipping the latest month as a night reads it, from the oldest close the night's year holds, 251 sessions
// back, to 21 back.
// see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
public partial class FixtureExpectations
{
    // A registration's parameters in the order of their names, so two can be compared as text.
    static string Sorted(IReadOnlyDictionary<string, double> parameters) =>
        string.Join(",", parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => FormattableString.Invariant($"{pair.Key}={pair.Value}")));

    [Fact]
    public void TheTwelveMonthStrengthReadsFromTheOldestCloseTheNightsYearHoldsToTheCloseAMonthBack()
    {
        // A year of 252 closes, the night the newest: the strength reads the oldest, 251 back, against the one 21 back.
        var closes = Enumerable.Repeat(150.0, 252).ToArray();

        closes[0] = 100.0;
        closes[230] = 120.0;

        Assert.Equal(251, TwelveMonthStrength.From);
        Assert.Equal(21, TwelveMonthStrength.Skipped);
        Assert.Equal(0.2, TwelveMonthStrength.Of(closes, 251)!.Value, 12);

        // The newest close moves nothing, since the latest month is skipped.
        closes[251] = 1_000.0;
        Assert.Equal(0.2, TwelveMonthStrength.Of(closes, 251)!.Value, 12);

        // A session one short of the year's reach reads none, as does a member holding no oldest close.
        Assert.Null(TwelveMonthStrength.Of(closes, 250));
        closes[0] = double.NaN;
        Assert.Null(TwelveMonthStrength.Of(closes, 251));
        closes[0] = 0;
        Assert.Null(TwelveMonthStrength.Of(closes, 251));
    }

    [Fact]
    public void ADesignAVariantMayReadTheTwelveMonthStrengthAndADesignBRegistrationMayNot()
    {
        var heavyweights = HeavyweightIndexEvaluator("heavyweight-600");
        var provisional = IndexRules.Provisional(heavyweights);

        Assert.Equal(0, provisional[IndexHeavyweightCandidate.TwelveOneParameter]);
        Assert.Null(IndexRules.Refusal(heavyweights, new Dictionary<string, double>(provisional) { [IndexHeavyweightCandidate.TwelveOneParameter] = 1 }));
        Assert.Contains(
            "'twelveOne' is 2",
            IndexRules.Refusal(heavyweights, new Dictionary<string, double>(provisional) { [IndexHeavyweightCandidate.TwelveOneParameter] = 2 }),
            StringComparison.Ordinal);

        // Design (b) states every design (a) dial as nought, the twelve-month strength among them.
        var followers = new Dictionary<string, double>(provisional)
        {
            [IndexHeavyweightCandidate.DesignParameter] = IndexHeavyweightCandidate.DesignB,
            [IndexHeavyweightCandidate.LargestParameter] = 0,
            [IndexHeavyweightCandidate.LookBackParameter] = 0,
            [IndexHeavyweightCandidate.LeadersParameter] = 0,
            [IndexHeavyweightCandidate.BetaParameter] = 0,
            [IndexHeavyweightCandidate.LeadingExitParameter] = 0,
            [IndexHeavyweightCandidate.AverageExitParameter] = 0,
            [IndexHeavyweightCandidate.WindowParameter] = 21,
            [IndexHeavyweightCandidate.IndustriesParameter] = 5,
            [IndexHeavyweightCandidate.MembersParameter] = 1,
        };

        Assert.Null(IndexRules.Refusal(heavyweights, followers));
        Assert.Contains(
            "'twelveOne' is 1, a dial of the other design",
            IndexRules.Refusal(heavyweights, new Dictionary<string, double>(followers) { [IndexHeavyweightCandidate.TwelveOneParameter] = 1 }),
            StringComparison.Ordinal);

        // The rule's words name the strength it leads on.
        Assert.Contains(
            "leading it on twelve months' strength skipping the latest month, from 251 sessions back to 21",
            IndexRules.Words(heavyweights, new Dictionary<string, double>(provisional) { [IndexHeavyweightCandidate.TwelveOneParameter] = 1 }),
            StringComparison.Ordinal);
        Assert.Contains("leading it over 251 sessions", IndexRules.Words(heavyweights, provisional), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIndexsVariantsAreRegisteredWithNoLiveRuleAtOneInstantOrNoneAndRefusedWhereARuleOfTheFamilyStands()
    {
        using var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        async Task<(int Code, string Said)> Variants(string index, string variants, DateTimeOffset when) =>
            await RegisterVerbAt(store, when, RegisterVerb.IndexVariants, "heavyweight", "--index", index, "--variants", variants);

        IReadOnlyList<string> Standing() =>
            TextRows(store, "SELECT candidate || '|' || evaluator || '|' || event FROM candidate_register ORDER BY id;");

        // A value off its sweep's grid, a variant stating the provisional rule's own settings, two stating one setting,
        // nine variants and no variant are each refused with nothing written.
        Assert.Equal(1, (await Variants("SML", "twelveOne=2", at)).Code);
        Assert.Contains("variant 1: 'twelveOne' is 2", (await Variants("SML", "twelveOne=2", at.AddSeconds(1))).Said, StringComparison.Ordinal);
        Assert.Contains("variant 1 states the provisional rule's settings", (await Variants("SML", "twelveOne=0", at.AddSeconds(2))).Said, StringComparison.Ordinal);
        Assert.Contains("variant 2 states the provisional rule's settings or a variant's", (await Variants("SML", "quality=2;quality=2", at.AddSeconds(3))).Said, StringComparison.Ordinal);
        Assert.Contains("9 variants were given", (await Variants("SML", string.Join(";", Enumerable.Repeat("quality=2", 9)), at.AddSeconds(4))).Said, StringComparison.Ordinal);
        Assert.Empty(Standing());

        // The S&P 600's two books, the profit gate with the interest cover and the twelve-month strength, registered at one
        // instant with no live rule, each the provisional setting with its one dial moved.
        Assert.Equal(0, (await Variants("SML", "quality=2;twelveOne=1", at.AddMinutes(1))).Code);

        var rows = Standing();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.StartsWith("the S&P 600 heavyweights rule of each sector's", row, StringComparison.Ordinal));
        Assert.All(rows, row => Assert.EndsWith("|heavyweight-600|registered", row, StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.StartsWith(FamilyRecords.LivePrefix, StringComparison.Ordinal));
        Assert.Contains("on twelve months' strength skipping the latest month", rows[1], StringComparison.Ordinal);
        Assert.Single(TextRows(store, "SELECT DISTINCT registered_at FROM candidate_register;"));

        var register = await new CandidateRegistrar(FixedClock.At(at.AddMinutes(2), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var parameters = register.Select(row => Sorted(CandidateEvaluator.Read(row.Parameters))).ToArray();
        var provisional = IndexRules.Provisional(HeavyweightIndexEvaluator("heavyweight-600"));

        Assert.Equal(Sorted(new Dictionary<string, double>(provisional) { [IndexRuleCandidate.QualityParameter] = 2 }), parameters[0]);
        Assert.Equal(Sorted(new Dictionary<string, double>(provisional) { [IndexHeavyweightCandidate.TwelveOneParameter] = 1 }), parameters[1]);

        // With rules of the family standing on the S&P 600 the form is refused, and the S&P 400 stands apart.
        Assert.Contains("rules of the heavyweight on the S&P 600 already stand registered", (await Variants("SML", "leaders=1", at.AddMinutes(3))).Said, StringComparison.Ordinal);
        Assert.Equal(0, (await Variants("MID", "leaders=1", at.AddMinutes(4))).Code);
        Assert.Equal(3, Standing().Count);
    }

    [Fact]
    public async Task ATwelveMonthBookRegisteredBeforeAFreezeKeepsABookOfItsOwnLedOnItsOwnStrength()
    {
        var (store, closes) = HeavyweightRuleStore();

        using (store)
        {
            var registeredAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

            // Two books beside the S&P 400's provisional rule: the twelve-month strength, and the provisional setting with the
            // interest cover. Registered 1 and 2.
            Assert.Equal(0, (await RegisterVerbAt(store, registeredAt, RegisterVerb.IndexVariants, "heavyweight", "--index", "MID", "--variants", "twelveOne=1;quality=2")).Code);

            var register = await new CandidateRegistrar(FixedClock.At(registeredAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

            await new IndexFamilies(FixedClock.At(new DateTimeOffset(2026, 10, 1, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
                .RunAsync("first", default, register, registeredAt.AddHours(1));

            // The night is the store's 261st session, 2026-10-01: the twelve-month strength reads each member's close 251
            // sessions back, the store's tenth, against its close 21 back, its 240th, and the provisional rule's look-back
            // the tenth against the night's. Each lead is the member's figure less the mean of the three, worked here from
            // the closes the store holds.
            double Strength(string ticker) => Statistic.FromRatio(closes[ticker][239] / closes[ticker][9]) - 1.0;
            double LookBack(string ticker) => Statistic.FromRatio(closes[ticker][260] / closes[ticker][9]) - 1.0;

            var tickers = new[] { "L1", "L2", "L3" };
            var strengths = tickers.ToDictionary(ticker => ticker, Strength, StringComparer.Ordinal);
            var lookBacks = tickers.ToDictionary(ticker => ticker, LookBack, StringComparer.Ordinal);

            double Stored(int rule, string ticker) => double.Parse(
                TextRows(store, $"SELECT printf('%.12f', h.lead) FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate WHERE r.id = {rule} AND h.ticker = '{ticker}';").Single(),
                CultureInfo.InvariantCulture);

            // The twelve-month book buys L1 and L2, the two with a beta of at least 1 leading the mean, each with its lead on
            // the twelve-month strength and not on the look-back; the cover's book buys L1 alone, L2's coverage failing.
            Assert.Equal(
                ["1|L1", "1|L2", "2|L1"],
                TextRows(store, "SELECT r.id || '|' || h.ticker FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate ORDER BY r.id, h.ticker;"));

            foreach (var ticker in new[] { "L1", "L2" })
            {
                Assert.Equal(strengths[ticker] - strengths.Values.Average(), Stored(1, ticker), 9);
                Assert.NotEqual(Math.Round(lookBacks[ticker] - lookBacks.Values.Average(), 6), Math.Round(Stored(1, ticker), 6));
            }

            // The cover's book reads the members clearing its quality alone, so L1 leads the mean of L1 and L3.
            Assert.Equal(lookBacks["L1"] - ((lookBacks["L1"] + lookBacks["L3"]) / 2), Stored(2, "L1"), 9);

            // The provisional rule draws the index's own book as before, no live rule standing.
            Assert.Equal(["L1", "L2"], TextRows(store, "SELECT ticker FROM index_heavyweight_holding WHERE index_code = 'MID' ORDER BY ticker;"));
        }
    }
}
