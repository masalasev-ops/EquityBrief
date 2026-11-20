using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Ledger;

// A split a table holds with its shares as filed: one share of old shares became the new shares.
public sealed record SplitEntry(DateOnly Day, decimal NewShares, decimal OldShares)
{
    public StoredSplit Stored => new(Day, Statistic.FromRatio(NewShares / OldShares));
}

// A dividend a table holds, its amount as paid on the day it went ex.
public sealed record SplitDividend(DateOnly Day, decimal Amount);

// Why the split rule sets aside a step in a stock's closes no table holds a split of its size for, or a table's entry
// whose closes show no step of its size.
public enum SplitCause
{
    BesideEntry,
    Undone,
    SpecialDividend,
    UnheldSplit,
    NoCause,
    SmallSplit,
    Adjustment,
    BesideStep,
    NoStep,
}

// One step or entry the split rule sets aside: a step with its size and no entry, or an entry with no step.
public sealed record SplitSetAside(DateOnly Day, decimal? Step, SplitEntry? Entry, SplitCause Cause);

// From 24.11, read-only: each step and entry the split rule sets aside over a stock's closes, with its cause, read in the
// order the causes are listed, the first that holds. A step: beside a table's entry within the rule's days whose size is
// another; undone by a step within 20 days that brings it back within the rule's share; a dividend within the rule's days
// whose amount over the close before it makes the step within the rule's share; within 2 in 100 of the ratio of a split
// companies make, 3 for 2 or so many for one either way, no table holds; or no cause found. An entry: under the split step and of small whole numbers, a small split; under
// it otherwise, a stock dividend's or a spin-off's adjustment; beside a step of another size within the rule's days; or no
// step in the closes. It reads what the split rule reads and moves nothing the rule reads.
// see: A split table's entry is read only where the closes step by its own ratio, and an entry of another size is set aside and listed
public static class SplitCauses
{
    // The days within which a step undone by another is read as the provider's step undone.
    public const int UndoneWithinDays = 20;

    // How far a step may stand from a split's ratio and still be read as one, as a share.
    public const double SplitRatioWithin = 0.02;

    // The largest whole numbers a small split under the split step is filed in.
    public const int SmallestWhole = 10;

    // The ratios of the splits companies make at the split step or more, new shares over old: 3 for 2, and so many for
    // one; each read either way, a reverse split's the other way.
    static readonly double[] SplitRatios = [1.5, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 25, 30, 40, 50, 100];

    public static string Words(SplitCause cause) => cause switch
    {
        SplitCause.BesideEntry => "beside a table's entry of another size",
        SplitCause.Undone => "the provider's step undone within 20 days",
        SplitCause.SpecialDividend => "a special dividend",
        SplitCause.UnheldSplit => "a split's ratio no table holds",
        SplitCause.SmallSplit => "a small split under the split step",
        SplitCause.Adjustment => "a stock dividend's or a spin-off's adjustment",
        SplitCause.BesideStep => "beside a step of another size",
        SplitCause.NoStep => "no step in the closes",
        _ => "no cause found",
    };

    // The step of a session's close as traded over its close at the store's scale against the session before's, as the
    // split rule reads it.
    static decimal? Step(SweepBar before, SweepBar after) =>
        before.Close > 0m && before.RawClose > 0m && after.Close > 0m && after.RawClose > 0m
            ? after.RawClose / after.Close / (before.RawClose / before.Close)
            : null;

    static bool Near(DateOnly one, DateOnly other, int days) => Math.Abs(one.DayNumber - other.DayNumber) <= days;

    static bool Small(SplitEntry entry)
    {
        if (entry.NewShares != decimal.Truncate(entry.NewShares) || entry.OldShares != decimal.Truncate(entry.OldShares))
        {
            return false;
        }

        decimal a = entry.NewShares, b = entry.OldShares;

        while (b != 0m)
        {
            (a, b) = (b, a % b);
        }

        return a > 0m && entry.NewShares / a <= SmallestWhole && entry.OldShares / a <= SmallestWhole;
    }

    public static IReadOnlyList<SplitSetAside> Of(IReadOnlyList<SweepBar> bars, IReadOnlyList<SplitEntry> entries, IReadOnlyList<SplitDividend> dividends)
    {
        var held = entries.DistinctBy(entry => (entry.Day, entry.Stored.Ratio)).ToList();
        var stored = held.Select(entry => entry.Stored).ToList();
        var (unheld, unseen) = StoredSplits.Unmatched(bars, stored);
        var steps = new List<(int At, DateOnly Session, double Step)>();

        for (var at = 1; at < bars.Count; at++)
        {
            if (Step(bars[at - 1], bars[at]) is { } step && Statistic.FromRatio(step) is var ratio && StoredSplits.Steps(ratio))
            {
                steps.Add((at, bars[at].Session, ratio));
            }
        }

        SplitCause StepCause(DateOnly session, double ratio)
        {
            if (held.Any(entry => Near(entry.Day, session, StoredSplits.WithinDays)))
            {
                return SplitCause.BesideEntry;
            }

            if (steps.Any(other => other.Session != session && Near(other.Session, session, UndoneWithinDays) && Math.Abs((other.Step * ratio) - 1) <= StoredSplits.RatioWithin))
            {
                return SplitCause.Undone;
            }

            var at = steps.First(step => step.Session == session).At;
            var before = bars[at - 1].RawClose;

            if (before > 0m && dividends.Any(dividend =>
                    Near(dividend.Day, session, StoredSplits.WithinDays)
                    && dividend.Amount > 0m
                    && dividend.Amount < before
                    && Math.Abs((ratio / Statistic.FromRatio(1m - (dividend.Amount / before))) - 1) <= StoredSplits.RatioWithin))
            {
                return SplitCause.SpecialDividend;
            }

            return SplitRatios.Any(split => Math.Abs((ratio * split) - 1) <= SplitRatioWithin || Math.Abs((ratio / split) - 1) <= SplitRatioWithin)
                ? SplitCause.UnheldSplit
                : SplitCause.NoCause;
        }

        SplitCause EntryCause(SplitEntry entry)
        {
            if (!StoredSplits.Steps(entry.Stored.Ratio))
            {
                return Small(entry) ? SplitCause.SmallSplit : SplitCause.Adjustment;
            }

            return steps.Any(step => Near(step.Session, entry.Day, StoredSplits.WithinDays)) ? SplitCause.BesideStep : SplitCause.NoStep;
        }

        return
        [
            .. unheld.Select(step => new SplitSetAside(step.Session, step.Step, null, StepCause(step.Session, Statistic.FromRatio(step.Step)))),
            .. unseen.Select(split => held.First(entry => entry.Day == split.Day && entry.Stored.Ratio == split.Ratio)).Select(entry => new SplitSetAside(entry.Day, null, entry, EntryCause(entry))),
        ];
    }
}
