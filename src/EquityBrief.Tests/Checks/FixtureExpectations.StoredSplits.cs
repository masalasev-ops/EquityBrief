using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.10 corrections of 2026-11-17: a step in a stock's close as traded over its close at the
// store's scale is read as a split only where a split table holds a split within five days of it whose ratio is the step's
// own, so a special dividend's step, a provider's flipped adjustment or a spin-off the provider files as a split moves no
// share check; a split a table holds whose closes show no step of its size is read as none; both kinds are listed; and a
// ledger's dollar volume reads each session's close as traded times the stored volume brought back to the shares that traded
// that day by the held splits after it, worked by hand.
// see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
// see: A ledger's dollar volume floor reads the dollars that traded each session, so no later split or dividend moves a setup's existence
public partial class FixtureExpectations
{
    [Fact]
    public void AStepInTheClosesIsReadAsASplitOnlyWhereASplitTableHoldsOneWithinFiveDays()
    {
        DateOnly from = new(2025, 7, 25), to = new(2026, 7, 25);

        // Held on its own session, and three days before it: the older count is halved.
        Assert.Equal(0.5, FundamentalScorecard.SplitsBetween(NinePrices(split: true), from, to), 12);
        Assert.Equal(0.5, FundamentalScorecard.SplitsBetween(NinePrices(split: true) with { StoredSplits = [new(NineSplit.AddDays(-3), 2)] }, from, to), 12);

        // A step no table holds within five days, a special dividend's or a provider's flip, is no split.
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(NinePrices(split: true, held: false), from, to), 12);
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(NinePrices(split: true) with { StoredSplits = [new(NineSplit.AddDays(-StoredSplits.WithinDays - 1), 2)] }, from, to), 12);

        // A split the table holds with no step in the closes moves nothing.
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(NinePrices() with { StoredSplits = [new(NineSplit, 2)] }, from, to), 12);

        // The share check: two hundred shares after a step no table holds stand as an offering doubling the shares.
        var unheld = FundamentalScorecard.Read(NineFacts(sharesNow: 200m), NineSession, false, NinePrices(split: true, held: false));

        Assert.Equal(CheckAnswer.Failed, NineAnswer(unheld, 7));
        Assert.Equal(CheckAnswer.Passed, NineAnswer(Nine(NineFacts(sharesNow: 200m), split: true), 7));
    }

    // The operator's instruction of 2026-11-17 on the ledger builds' rule: a table's entry counts only where the closes show a
    // jump of its own size. MSGK's distribution of October 2026 stepped its closes by 0.506 a day from an entry the provider
    // files as 1,328 for 1,000, whose step would be 0.753: neither is read.
    // see: A split table's entry is read only where the closes step by its own ratio, and an entry of another size is set aside and listed
    [Fact]
    public void AStepIsReadAsASplitOnlyWhereTheHeldSplitIsOfItsOwnSize()
    {
        DateOnly from = new(2025, 7, 25), to = new(2026, 7, 25);
        var halved = NinePrices(split: true);

        // A held split of the step's size within ten per cent, a split on a dividend's ex-date among them, is read.
        Assert.Equal(0.5, FundamentalScorecard.SplitsBetween(halved with { StoredSplits = [new(NineSplit, 2.1)] }, from, to), 12);
        Assert.Equal(0.5, FundamentalScorecard.SplitsBetween(halved with { StoredSplits = [new(NineSplit, 1.85)] }, from, to), 12);

        // One of another size is not: a spin-off's 1,328 for 1,000, and a split the other way.
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(halved with { StoredSplits = [new(NineSplit.AddDays(1), 1.328)] }, from, to), 12);
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(halved with { StoredSplits = [new(NineSplit, 0.5)] }, from, to), 12);
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(halved with { StoredSplits = [new(NineSplit, 2 * (1 + StoredSplits.RatioWithin + 0.01))] }, from, to), 12);

        // Both are listed: the step as no split, the entry as a split its closes do not show.
        SweepBar[] bars =
        [
            new(NineSplit.AddDays(-1), 0m, 0m, 196.5952m, 584453, RawClose: 388.63m),
            new(NineSplit, 0m, 0m, 289.4503m, 347006, RawClose: 289.4503m),
        ];
        var (steps, entries) = StoredSplits.Unmatched(bars, [new(NineSplit, 1.328)]);

        Assert.Single(steps);
        Assert.Equal(0.506, (double)steps[0].Step, 3);
        Assert.Equal([new StoredSplit(NineSplit, 1.328)], entries);
    }

    [Fact]
    public void AStepNoTableHoldsAndAHeldSplitTheClosesDoNotShowAreBothListed()
    {
        DateOnly first = new(2026, 3, 2), stepped = new(2026, 3, 4), quiet = new(2026, 3, 20);
        SweepBar[] bars =
        [
            new(first, 0m, 0m, 50m, 100, RawClose: 100m),
            new(first.AddDays(1), 0m, 0m, 50m, 100, RawClose: 100m),
            new(stepped, 0m, 0m, 51m, 100, RawClose: 51m),
            new(quiet, 0m, 0m, 52m, 100, RawClose: 52m),
            new(quiet.AddDays(1), 0m, 0m, 53m, 100, RawClose: 53m),
        ];

        var (unheld, unseen) = StoredSplits.Unmatched(bars, [new(quiet, 2)]);

        Assert.Equal([(stepped, 0.5m)], unheld);
        Assert.Equal([new StoredSplit(quiet, 2)], unseen);

        // Held, the step is listed nowhere and the split it is read as is listed nowhere either.
        var (none, nothing) = StoredSplits.Unmatched(bars, [new(stepped.AddDays(2), 2)]);

        Assert.Empty(none);
        Assert.Empty(nothing);
    }

    [Fact]
    public void ALedgersDollarVolumeIsTheDollarsThatTradedWorkedByHand()
    {
        // Fifty sessions: the first twenty-five before a split of two for one on the twenty-sixth, traded at $102 with the
        // provider's restated volume of 200,000 standing for the 100,000 shares that traded, their close at the store's
        // scale $50 after the split and a later dividend of two per cent; the next fourteen traded at $51 on 100,000, $50 at
        // the store's scale; and from the fortieth, after the dividend, $51 both ways, a step of 0.98 that is no split. The
        // dollars that traded are 25 x $10.2m and 25 x $5.1m: $7.65m a session. Read across no split, the provider's volume
        // makes the first twenty-five $20.4m a session and the mean $12.75m.
        var start = new DateOnly(2026, 1, 5);
        var bars = Enumerable.Range(0, MemberReadings.DollarVolumeSessions).Select(at =>
            at < 25 ? new SweepBar(start.AddDays(at), 0m, 0m, 50m, 200_000, RawClose: 102m)
            : at < 39 ? new SweepBar(start.AddDays(at), 0m, 0m, 50m, 100_000, RawClose: 51m)
            : new SweepBar(start.AddDays(at), 0m, 0m, 51m, 100_000, RawClose: 51m)).ToArray();
        var split = start.AddDays(25);

        var held = StoredSplits.SharesTraded(bars, [new(split, 2)]);
        var unheld = StoredSplits.SharesTraded(bars, []);

        Assert.Equal(7_650_000m, MemberReadings.DollarVolume(StoredSplits.TradedDollars(bars, held, 0, bars.Length - 1)));
        Assert.Equal(12_750_000m, MemberReadings.DollarVolume(StoredSplits.TradedDollars(bars, unheld, 0, bars.Length - 1)));
        Assert.Equal(0.5m, held[0]);
        Assert.Equal(1m, held[30]);
        Assert.Equal(1m, held[^1]);

        // A held entry of another size undoes nothing.
        Assert.Equal(1m, StoredSplits.SharesTraded(bars, [new(split, 1.328)])[0]);
    }
}
