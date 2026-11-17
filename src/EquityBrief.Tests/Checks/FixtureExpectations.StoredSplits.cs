using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.10 correction of 2026-11-17: a step in a stock's close as traded over its close at the
// store's scale is read as a split only where a split table holds a split within five days of it, so a special dividend's
// step or a provider's flipped adjustment moves no share check; a split a table holds whose closes show no step is read as
// none; both kinds are listed; and a ledger's dollar volume reads each session's close as traded times the stored volume
// brought back to the shares that traded that day by the held splits after it, worked by hand.
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
        Assert.Equal(0.5, FundamentalScorecard.SplitsBetween(NinePrices(split: true) with { StoredSplits = [NineSplit.AddDays(-3)] }, from, to), 12);

        // A step no table holds within five days, a special dividend's or a provider's flip, is no split.
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(NinePrices(split: true, held: false), from, to), 12);
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(NinePrices(split: true) with { StoredSplits = [NineSplit.AddDays(-StoredSplits.WithinDays - 1)] }, from, to), 12);

        // A split the table holds with no step in the closes moves nothing.
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(NinePrices() with { StoredSplits = [NineSplit] }, from, to), 12);

        // The share check: two hundred shares after a step no table holds stand as an offering doubling the shares.
        var unheld = FundamentalScorecard.Read(NineFacts(sharesNow: 200m), NineSession, false, NinePrices(split: true, held: false));

        Assert.Equal(CheckAnswer.Failed, NineAnswer(unheld, 7));
        Assert.Equal(CheckAnswer.Passed, NineAnswer(Nine(NineFacts(sharesNow: 200m), split: true), 7));
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

        var (unheld, unseen) = StoredSplits.Unmatched(bars, [quiet]);

        Assert.Equal([(stepped, 0.5m)], unheld);
        Assert.Equal([quiet], unseen);

        // Held, the step is listed nowhere and the split it is read as is listed nowhere either.
        var (none, nothing) = StoredSplits.Unmatched(bars, [stepped.AddDays(2)]);

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

        var held = StoredSplits.SharesTraded(bars, [split]);
        var unheld = StoredSplits.SharesTraded(bars, []);

        Assert.Equal(7_650_000m, MemberReadings.DollarVolume(StoredSplits.TradedDollars(bars, held, 0, bars.Length - 1)));
        Assert.Equal(12_750_000m, MemberReadings.DollarVolume(StoredSplits.TradedDollars(bars, unheld, 0, bars.Length - 1)));
        Assert.Equal(0.5m, held[0]);
        Assert.Equal(1m, held[30]);
        Assert.Equal(1m, held[^1]);
    }
}
