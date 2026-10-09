using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.6: winners against losers worked by hand over constructed listings. The null shuffles which
// listing carries which edge within each night, every arrangement read where the nights hold no more than 199: over two
// nights of a winner and a loser its mark is the best itself and nothing is proposed, over six it is two thirds and the
// reading that names the winner is; a reading that is the same for every listing of a night is held level by the null
// and proposes nothing; a fold reads no listing of its year or after it; each reading's spread is worked by hand; and a
// condition is stated as the hooks a rule's registration reads.
// see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
public partial class FixtureExpectations
{
    // The rows 17.6's engine adds that this check reaches: section 17's search and null and section 18's reading no
    // learning year holds.
    internal static readonly string[] ConditionClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The conditions' search"),
        CheckReach.Key(Scope.LimitsTable, "The conditions' null"),
        CheckReach.Key(Scope.FailureTable, "A reading no learning year holds"),
    ];

    // Every row 17.6's engine adds, named after phase 16's report until phase 17's own pair is checked: the readings'
    // store and the three above.
    internal static string[] ConditionRows =>
    [
        CheckReach.Key(Scope.StoresTable, "Loop readings"),
        .. ConditionClaims,
    ];

    static readonly int Rsi = LedgerReadings.All.Select((reading, at) => (reading, at)).Single(pair => pair.reading.Column == "rsi").at;

    static double?[] OneReading(int reading, double? value)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[reading] = value;

        return readings;
    }

    // Each night a winner reading 1 and a loser reading 0.
    static ConditionUnit[] WinnerAndLoser(int nights) =>
    [
        .. Enumerable.Range(0, nights).SelectMany(night => new[]
        {
            new ConditionUnit(night, 1, OneReading(Rsi, 1)),
            new ConditionUnit(night, -1, OneReading(Rsi, 0)),
        }),
    ];

    [Fact]
    public void TheNullIsWorkedByHandOverEveryArrangementAndAConditionGoesForwardOnlyAboveItsMark()
    {
        // The search over six nights: the reading at or above 1 keeps the six winners at +1, the reading at or under 0
        // the six losers at -1, and neither keeps every listing holding the reading; no second reading is held, so no
        // pair.
        var found = ConditionSearch.Search(WinnerAndLoser(6), 1);

        Assert.Equal([("rsi", true, 1.0, 1.0, 6), ("rsi", false, 0.0, -1.0, 6)], found.Select(one => (one.Conditions.Single().Column, one.Conditions.Single().Above, one.Conditions.Single().Level, one.Score, one.Kept)));

        // Two nights: four arrangements, each night swapped or not. Unswapped or both swapped the best is 1, the reading
        // naming the winners or the losers; one swapped, both conditions keep a winner and a loser and the best is 0. The
        // mark, the 95th percentile of 0, 0, 1 and 1 by its nearest rank, is 1, and the best does not stand above it.
        var two = ConditionSearch.Judge(WinnerAndLoser(2), 1);

        Assert.Equal((4, 1.0, 1.0), (two.Arrangements, two.Mark, two.Best));
        Assert.Empty(two.Passing);

        // Six nights: 64 arrangements, the best 2u - 6 over 6 in size for u nights unswapped: 0 on 20, a third on 30, two
        // thirds on 12 and 1 on 2. The 61st of 64 is two thirds, and the reading at or above 1 goes forward, the one
        // under it does not, its score under the rule's own of nothing.
        var six = ConditionSearch.Judge(WinnerAndLoser(6), 1);

        Assert.Equal(64, six.Arrangements);
        Assert.Equal(2.0 / 3, six.Mark!.Value, 12);
        Assert.Equal([("rsi", true, 1.0)], six.Passing.Select(one => (one.Conditions.Single().Column, one.Conditions.Single().Above, one.Conditions.Single().Level)));

        // Eight nights hold 256 arrangements, past 199, so the null reads 199 shuffles drawn at the fixed seed, the same
        // on every run.
        Assert.Equal(ConditionSearch.Shuffles, ConditionSearch.Judge(WinnerAndLoser(8), 1).Arrangements);
        Assert.Equal(ConditionSearch.Judge(WinnerAndLoser(8), 1).Mark, ConditionSearch.Judge(WinnerAndLoser(8), 1).Mark);
    }

    [Fact]
    public void AReadingTheSameForEveryListingOfANightIsHeldLevelByTheNullAndProposesNothing()
    {
        // Three nights of two winners reading 0 and three of two losers reading 1: the reading at or under 0 keeps every
        // winner at +1, but a night's two listings carry the same edge and the same reading, so no shuffle within a night
        // moves the search's best, the mark is 1 and nothing goes forward.
        ConditionUnit[] units =
        [
            .. Enumerable.Range(0, 6).SelectMany(night => new[]
            {
                new ConditionUnit(night, night < 3 ? 1 : -1, OneReading(Rsi, night < 3 ? 0 : 1)),
                new ConditionUnit(night, night < 3 ? 1 : -1, OneReading(Rsi, night < 3 ? 0 : 1)),
            }),
        ];
        var verdict = ConditionSearch.Judge(units, 1);

        Assert.Equal((1.0, 1.0, 64), (verdict.Best, verdict.Mark, verdict.Arrangements));
        Assert.Empty(verdict.Passing);
    }

    [Fact]
    public void AFoldReadsNoListingOfItsYearOrAfterItAndAReadingNoLearningYearHoldsProposesNothing()
    {
        // Weekdays from 2021 into 2022, and the fold testing 2022.
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2021, 1, 4); day <= new DateOnly(2022, 3, 31); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                calendar.Add(day);
            }
        }

        var fold = LoopFolds.Of(calendar, calendar[^1]).Single();
        var cut = calendar.IndexOf(fold.TestFrom);

        // Twelve listings a session apart learned on, ending two sessions after their buy, reading nothing; and twelve in
        // the test year, whose readings name every winner. Asking for a test year's readings would throw.
        RuleListing[] listings =
        [
            .. Enumerable.Range(0, 12).Select(at => new RuleListing(at, 0, cut - 40 + (at * 2), cut - 38 + (at * 2), at % 2 == 0 ? 1 : -1)),
            .. Enumerable.Range(0, 12).Select(at => new RuleListing(at, 0, cut + 1 + at, cut + 3 + at, at % 2 == 0 ? 1 : -1)),
        ];

        IReadOnlyList<double?> ReadingsOf(int at) =>
            listings[at].Session >= cut ? throw new InvalidOperationException("a test year's listing was read") : new double?[LedgerReadings.Count];

        var learning = ConditionProcedures.LearningUnits(fold, calendar, listings, ReadingsOf);

        Assert.Equal(12, learning.Count);
        Assert.All(learning, unit => Assert.True(unit.Night < cut));

        // No learning listing holds a reading, so the search finds nothing and the null reads nothing.
        var verdict = ConditionSearch.Judge(learning, 1);

        Assert.Empty(verdict.Passing);
        Assert.Equal((null, null, 0), (verdict.Mark, verdict.Best, verdict.Arrangements));
    }

    [Fact]
    public void EachReadingsSpreadIsWorkedByHandOverConstructedListings()
    {
        // Ten listings reading 1 to 10 with edges of the reading less 5.5: the winners read 6 to 10, their median 8, the
        // losers 1 to 5, their median 3; each tenth holds one listing, its edge -4.5 to +4.5; and a reading no listing
        // holds counts none.
        ConditionUnit[] units = [.. Enumerable.Range(1, 10).Select(value => new ConditionUnit(value, value - 5.5, OneReading(Rsi, value)))];
        var spreads = ConditionSearch.Spreads(units);
        var rsi = spreads.Single(one => one.Reading == Rsi);

        Assert.Equal((10, 5, 5, 8.0, 3.0), (rsi.Units, rsi.Winners, rsi.Losers, rsi.WinnersMedian, rsi.LosersMedian));
        Assert.Equal([-4.5, -3.5, -2.5, -1.5, -0.5, 0.5, 1.5, 2.5, 3.5, 4.5], rsi.Deciles.Select(one => one!.Value));
        Assert.Equal(LedgerReadings.Count, spreads.Count);
        Assert.All(spreads.Where(one => one.Reading != Rsi), one => Assert.Equal((0, null), (one.Units, one.WinnersMedian)));
    }

    [Fact]
    public void AConditionIsStatedAsTheHooksARulesRegistrationReads()
    {
        // A pair, the reading at or above 1 and the typical move's share at or under 0.05, stated as two hooks and read
        // back by the hooks' own reader as the same two conditions.
        var share = LedgerReadings.All.Select((reading, at) => (reading, at)).Single(pair => pair.reading.Column == "move_share").at;
        var pair = new ConditionFound([new AlsoRequires(Rsi, true, 1), new AlsoRequires(share, false, 0.05)], 0.4, 120);
        var hooks = RuleHooks.Of(pair.Parameters());

        Assert.Equal(["also_move_share_below", "also_rsi_above"], pair.Parameters().Keys.Order(StringComparer.Ordinal));
        Assert.Equal(pair.Conditions.OrderBy(one => one.Reading), hooks.Also.OrderBy(one => one.Reading));
        Assert.Equal("also requires rsi at or above 1 and move_share at or under 0.05", ConditionProcedures.ChangeOf(pair));
    }
}
