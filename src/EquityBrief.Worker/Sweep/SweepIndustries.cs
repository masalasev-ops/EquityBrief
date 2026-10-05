using System.Collections.Concurrent;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// Each GICS industry's return from one session to a later one among the S&P 500's members as they stood on the later:
// each member's own return between the two, weighted by its company's value on the first on the count filed before
// it, a member with no industry, no value or no close on either session left out and an industry left with none
// reading none. Read once a pair of sessions. The followers' design reads each industry's lead over SPY from it, and
// a swing family's industry fall reads whether it fell.
// see: A 400 or 600 swing pick may be kept off where its industry's S&P 500 heavyweights fell, a dial its sweeps test
public sealed class SweepIndustries(SweepHistoryInputs large, HeavyweightHistory companies, IReadOnlyDictionary<string, string> industries)
{
    readonly ConcurrentDictionary<(DateOnly Start, DateOnly Day), IReadOnlyDictionary<string, double>> read = new();

    public IReadOnlyDictionary<string, string> Industries => industries;

    public IReadOnlyDictionary<string, double> Between(DateOnly start, DateOnly day) => read.GetOrAdd((start, day), key =>
    {
        var sums = new Dictionary<string, (double Weighted, double Weight)>(StringComparer.Ordinal);

        foreach (var name in large.Names)
        {
            if (!name.MemberOn(key.Day) || !industries.TryGetValue(name.Ticker, out var industry)
                || BarOn(name, key.Day) is not { } now || BarOn(name, key.Start) is not { } then || then.Close <= 0m
                || CompanyValue.On(new SessionClose(then.Session, then.Close, then.RawClose > 0m ? then.RawClose : then.Close), companies.Counts.GetValueOrDefault(name.Ticker) ?? [], companies.Splits.GetValueOrDefault(name.Ticker) ?? []) is not { } value
                || value <= 0m)
            {
                continue;
            }

            var weight = Statistic.FromPrice(value);
            var held = sums.GetValueOrDefault(industry);

            sums[industry] = (held.Weighted + (weight * (Statistic.FromRatio(now.Close / then.Close) - 1.0)), held.Weight + weight);
        }

        return sums.Where(pair => pair.Value.Weight > 0).ToDictionary(pair => pair.Key, pair => pair.Value.Weighted / pair.Value.Weight, StringComparer.Ordinal);
    });

    // A name's bar on a session, none where it holds none.
    static SweepBar? BarOn(SweepName name, DateOnly day)
    {
        var (low, high) = (0, name.Bars.Length - 1);

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var session = name.Bars[middle].Session;

            if (session == day)
            {
                return name.Bars[middle];
            }

            (low, high) = session < day ? (middle + 1, high) : (low, middle - 1);
        }

        return null;
    }
}
