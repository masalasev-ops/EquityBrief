using EquityBrief.Core.Cards;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.5: the AI judge's four accounts over constructed sessions, each worked by hand: a pick bought at
// the open after its night and walked under each take-profit from the price it paid; the random accounts drawn again from
// their seed, as many members a night as the judge picked; their median and middle band; the index held; a pick against the
// same plan bought on every member that night; and the money, sizing and costs read from the one account function alone.
// see: The readings for every stock on every session are built once before the reviewer, the judge's comparison reading the same plan on every stock until then
public partial class FixtureExpectations
{
    // The row of section 17 24.5 adds that this check reaches.
    internal static readonly string[] JudgeAccountClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The AI judge's four accounts"),
    ];

    static readonly DateOnly AccountNight = new(2026, 11, 19);

    static JudgeBars AccountBars(double[] opens, double[] closes) =>
        new([.. Enumerable.Range(0, opens.Length).Select(at => AccountNight.AddDays(at))], opens, closes);

    [Fact]
    public void APickIsBoughtAtTheOpenAfterItsNightAndWalkedUnderItsPlanFromThePriceItPaid()
    {
        // A week, the whole at the target: a typical move of 2 and 1 move of stop put up 2 under the fill of 101, a stop of
        // 99 and a target of 105; the session after the fill closes at 106 over it, so it sells at the next open, 107, 3 risks.
        var whole = JudgeAccounts.Trade("AAA", 1, "1 week", JudgeMenu.Whole, 2.0, AccountBars([100, 101, 103, 107, 108], [100, 103, 106, 107, 108]), buy => 0.1m)!;

        Assert.Equal((AccountNight.AddDays(1), 101m, 99m, AccountNight.AddDays(3), 107m, 0.1m, 105m, 5), (whole.Bought, whole.Buy, whole.Stop!.Value, whole.Ended!.Value, whole.Sale!.Value, whole.RoundTrip!.Value, whole.Target!.Value, whole.Cap!.Value));
        Assert.Equal(3.0, JudgeAccounts.ResultOf(whole));

        // Two weeks, half then the give-up price: 1.5 moves put up 3, a stop of 98 and the half at 107. The fourth session
        // closes at 108, so half sells at the next open, 109, and the stop moves to the buy; the highest close reaches 110
        // and the give-up price 107 behind it; a close of 106 under it sells the rest at the next open, 105. The two halves
        // came to 107, 2 risks.
        var half = JudgeAccounts.Trade("BBB", 2, "2 weeks", JudgeMenu.HalfThenGiveUp, 2.0, AccountBars([100, 101, 103, 105, 109, 111, 105, 104], [100, 102, 104, 108, 110, 106, 104, 104]), _ => null)!;

        Assert.Equal((AccountNight.AddDays(6), 107m, 98m, 107m, 3m), (half.Ended!.Value, half.Sale!.Value, half.Stop!.Value, half.Target!.Value, half.Trail!.Value));
        Assert.Equal(2.0, JudgeAccounts.ResultOf(half));

        // A walk that has not ended stays open; no session after the night, or no typical move, places no trade.
        Assert.Null(JudgeAccounts.Trade("CCC", 1, "3 months", JudgeMenu.Whole, 2.0, AccountBars([100, 101, 102], [100, 101, 102]), _ => null)!.Ended);
        Assert.Null(JudgeAccounts.Trade("DDD", 1, "1 week", JudgeMenu.Whole, 2.0, AccountBars([100], [100]), _ => null));
        Assert.Null(JudgeAccounts.Trade("EEE", 1, "1 week", JudgeMenu.Whole, null, AccountBars([100, 101], [100, 101]), _ => null));
    }

    [Fact]
    public void TheRandomAccountsRepeatFromTheirSeedAndPickAsManyMembersEachNightAsTheJudgeDid()
    {
        var members = Enumerable.Range(0, 40).Select(at => $"M{at:00}").ToArray();
        IReadOnlyList<(DateOnly, int, IReadOnlyList<string>)> nights = [(AccountNight, 3, members), (AccountNight.AddDays(1), 5, members), (AccountNight.AddDays(2), 0, members)];

        PaperTrade Placed(string ticker, DateOnly night, string hold, string shape, int place) =>
            new(ticker, night.AddDays(1), 10m, 9m, null, null, null, place, Listed: night, Cap: JudgeMenu.HoldOf(hold)!.Sessions);

        var first = JudgeAccounts.Draws(nights, Placed, 7, 50);
        var again = JudgeAccounts.Draws(nights, Placed, 7, 50);
        var other = JudgeAccounts.Draws(nights, Placed, 8, 50);

        Assert.Equal(50, first.Count);
        Assert.Equal(1000, JudgeAccounts.RandomDraws);
        Assert.Equal(8, JudgeAccounts.Plans.Count);
        Assert.Equal(first.Select(Keys), again.Select(Keys));
        Assert.NotEqual(first.Select(Keys), other.Select(Keys));

        // Each draw takes the judge's count on each night, no member twice a night.
        Assert.All(first, draw => Assert.Equal([3, 5], draw.GroupBy(trade => trade.ListedOn).OrderBy(group => group.Key).Select(group => group.Select(trade => trade.Ticker).Distinct().Count())));

        // A member its plan places no trade on is drawn past, so the count still holds.
        var sparse = JudgeAccounts.Draws(nights, (ticker, night, hold, shape, place) => ticker.EndsWith('0') ? null : Placed(ticker, night, hold, shape, place), 7, 20);

        Assert.All(sparse, draw => Assert.Equal(8, draw.Count));
        Assert.All(sparse, draw => Assert.DoesNotContain(draw, trade => trade.Ticker.EndsWith('0')));

        static string Keys(IReadOnlyList<PaperTrade> draw) => string.Join(",", draw.Select(trade => $"{trade.Ticker}:{trade.Cap}"));
    }

    [Fact]
    public void TheMedianAndTheBandAreReadOffTheRandomAccountsSortedAndTheIndexHeldGrowsWithItsFund()
    {
        PaperWalk Walk(params decimal[] values) =>
            new([.. values.Select((value, at) => (AccountNight.AddDays(at), value))], [], 0, 0, 0, null, null, null, null, null, []);

        // On the second session the eleven accounts stand at 100 to 200 by tens: the tenth in a hundred the second lowest,
        // 110, the median 150 and the ninetieth 190.
        var walks = Enumerable.Range(0, 11).Select(at => Walk(100m, 100m + (10m * at))).ToArray();
        var band = JudgeAccounts.Band(walks);

        Assert.Equal(new JudgeBand(AccountNight.AddDays(1), 110m, 150m, 190m), band[1]);
        Assert.Equal(new JudgeBand(AccountNight, 100m, 100m, 100m), band[0]);
        Assert.Empty(JudgeAccounts.Band([]));

        // The index held: 100,000 grown by a fund from 400 to 440 is 110,000, from the first session the fund holds.
        var fund = new Dictionary<DateOnly, decimal> { [AccountNight.AddDays(1)] = 400m, [AccountNight.AddDays(2)] = 440m };

        Assert.Equal(
            [(AccountNight.AddDays(1), 100000m), (AccountNight.AddDays(2), 110000m)],
            JudgeAccounts.IndexHeld(100000m, [AccountNight, AccountNight.AddDays(1), AccountNight.AddDays(2)], session => fund.TryGetValue(session, out var close) ? close : null));
    }

    [Fact]
    public void APickIsReadAgainstTheSamePlanBoughtOnEveryMemberThatNight()
    {
        // A week, the whole at 2 risks, on two members with a typical move of 1: the first fills at 50 with a target of 52, a
        // close of 53 selling at the next open, 54, 4 risks; the second fills at 30 with a stop of 29, a close of 28 selling
        // at 27.5, minus 2.5. Their mean is 0.75, so a pick that made 3 risks made 2.25 more.
        var first = (AccountBars([50, 50, 52, 54], [50, 51, 53, 54]), (double?)1.0);
        var second = (AccountBars([30, 30, 27.5], [30, 28, 27]), (double?)1.0);

        Assert.Equal(2.25, JudgeAccounts.AgainstEveryMember(3.0, "1 week", JudgeMenu.Whole, [first, second])!.Value, 9);
        Assert.Null(JudgeAccounts.AgainstEveryMember(null, "1 week", JudgeMenu.Whole, [first, second]));
        Assert.Null(JudgeAccounts.AgainstEveryMember(3.0, "1 week", JudgeMenu.Whole, [(AccountBars([30], [30]), 1.0)]));
    }

    [Fact]
    public void TheFourAccountsReadTheirMoneySizingAndCostsFromTheOneAccountFunction()
    {
        // The judge's account and a random account holding the same trade walk to the same values through the one function,
        // and the accounts' own code sizes and costs nothing of its own.
        var trade = JudgeAccounts.Trade("AAA", 1, "1 week", JudgeMenu.Whole, 2.0, AccountBars([100, 101, 103, 107, 108], [100, 103, 106, 107, 108]), _ => 0.1m)!;
        var sessions = Enumerable.Range(0, 5).Select(at => AccountNight.AddDays(at)).ToArray();
        decimal? Close(string ticker, DateOnly session) => new[] { 100m, 103m, 106m, 107m, 108m }[session.DayNumber - AccountNight.DayNumber];

        var judge = PaperAccount.Walk(AccountSettings.Example, [trade], sessions, Close);
        var random = PaperAccount.Walk(AccountSettings.Example, [trade with { Place = 2 }], sessions, Close);

        Assert.Equal(judge.Values, random.Values);
        Assert.Equal(1, judge.Taken);

        var source = SourceStatements.WithoutComments(File.ReadAllText(Path.Combine(Repository.Root, "src/EquityBrief.Core/Judge/JudgeAccounts.cs")));

        Assert.DoesNotContain("PositionSize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RiskPercent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TradeCost", source, StringComparison.Ordinal);
        Assert.Contains("PaperAccount.Averaged(", source, StringComparison.Ordinal);
    }
}
