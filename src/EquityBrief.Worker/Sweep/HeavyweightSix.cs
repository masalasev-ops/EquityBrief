using EquityBrief.Core.Families;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Worker.Sweep;

// One of the sector heavyweights' six settings a design on the S&P 400 or 600, pre-registered before its run: its place
// and words, whether it reads twelve months' strength skipping the latest, its quality, whether it rebalances on the
// first session of each quarter, and design (a)'s setting or design (b)'s members an industry.
public sealed record HeavyweightSixSetting(int Place, string Words, bool TwelveOne, IndexQuality Quality, bool Quarterly, HeavyweightSetting? Leaders = null, int PerIndustry = 1)
{
    public string Key => Place.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + Words;
}

// The sector heavyweights' six settings a design on the S&P 400 and 600, read as the ruling of 2026-10-08 registered
// them before their run, and only those: design (a) walked over the members as the index's own book reads them, design
// (b) over the S&P 500's industries' leads; each holding scored as the sweep scores it.
// see: The S&P 400's and 600's sector heavyweights are searched over six settings a design registered before the run
public static class HeavyweightSix
{
    // Twelve months' strength skipping the latest: the return from this many sessions back to the newer one.
    public const int TwelveMonths = 252;

    public const int SkippedMonth = 21;

    // Design (b)'s base: the window an industry's lead and a member's own return are read over, and the industries kept.
    public const int BaseWindow = 63;

    public const int BaseIndustries = 10;

    public const int Settings = 6;

    // Design (a), the index's own provisional book and the five moves from it.
    public static IReadOnlyList<HeavyweightSixSetting> DesignA { get; } =
    [
        new(1, "the provisional book", false, IndexQuality.Profit, false, IndexHeavyweights.Provisional),
        new(2, "twelve months' strength skipping the latest", true, IndexQuality.Profit, false, IndexHeavyweights.Provisional),
        new(3, "the profit gate and the interest cover", false, IndexQuality.Cover, false, IndexHeavyweights.Provisional),
        new(4, "rebalanced quarterly", false, IndexQuality.Profit, true, IndexHeavyweights.Provisional),
        new(5, "the beta floor off", false, IndexQuality.Profit, false, IndexHeavyweights.Provisional with { HighBeta = false }),
        new(6, "the second, third and fourth together", true, IndexQuality.Cover, true, IndexHeavyweights.Provisional),
    ];

    // Design (b), its base and the five moves from it.
    public static IReadOnlyList<HeavyweightSixSetting> DesignB { get; } =
    [
        new(1, "the base", false, IndexQuality.Profit, false),
        new(2, "twelve months' strength skipping the latest", true, IndexQuality.Profit, false),
        new(3, "the profit gate and the interest cover", false, IndexQuality.Cover, false),
        new(4, "rebalanced quarterly", false, IndexQuality.Profit, true),
        new(5, "two members an industry", false, IndexQuality.Profit, false, PerIndustry: 2),
        new(6, "the second, third and fourth together", true, IndexQuality.Cover, true),
    ];

    // How many of the six luck alone would put above nothing in the floor's years, were they independent.
    public static double Luck => HeavyweightSweep.Luck(Settings);

    // A close's return from twelve months back to a month back on the calendar, none where either close is missing or
    // the calendar does not reach back that far.
    public static double? TwelveOne(double[] close, int session)
    {
        var (from, to) = (session - TwelveMonths, session - SkippedMonth);

        return from >= 0 && close[from] > 0 && !double.IsNaN(close[to]) ? (close[to] / close[from]) - 1.0 : null;
    }

    // The sessions of the months a quarterly book rebalances on: each month's first that opens January, April, July or
    // October.
    public static IReadOnlyList<int> Quarters(IReadOnlyList<DateOnly> calendar, IReadOnlyList<int> months) =>
        [.. months.Where(session => calendar[session].Month % 3 == 1)];

    // The sessions a setting rebalances on.
    public static IReadOnlyList<int> RebalancesOf(HeavyweightSixSetting setting, IReadOnlyList<DateOnly> calendar, IReadOnlyList<int> months) =>
        setting.Quarterly ? Quarters(calendar, months) : months;

    // Design (a)'s rebalance on a session at a setting: the members clearing its quality and the floors, read by the
    // night's own rule over the look-back the book reads or, where the setting asks, over twelve months skipping the
    // latest, each sector's leaders and its size cut by name.
    public static HeavyweightRebalance ReadA(
        HeavyweightTape tape,
        HeavyweightSession session,
        HeavyweightSixSetting setting,
        IReadOnlyDictionary<string, int> names,
        Func<int, int, IndexQuality, bool> clears)
    {
        var kept = session.Members.Where(candidate => clears(candidate.Name, session.Session, setting.Quality)).ToArray();
        var leaders = setting.Leaders!;

        if (!setting.TwelveOne)
        {
            return HeavyweightSweep.Read(session with { Members = kept }, leaders, names);
        }

        var members = kept.Select(candidate => candidate.Member with { Return = TwelveOne(tape.Close[candidate.Name], session.Session) }).ToArray();

        return new([.. HeavyweightRule.Read(members, leaders.Reading).Select(sector => (sector.Sector, sector.Leaders.Select(leader => names[leader]).ToArray(), sector.Largest.Select(ranked => names[ranked.Ticker]).ToArray()))]);
    }

    // Design (b)'s rebalance on a session at a setting: the industries leading SPY over the window or over twelve months
    // skipping the latest, the strongest first and the base's count of them kept, and in each the index's members
    // clearing the quality and the floors by their own return over the same sessions, the strongest bought, grouped by
    // their sector with that sector's members in the index as the holding's benchmark.
    public static HeavyweightRebalance ReadB(
        HeavyweightTape tape,
        int session,
        HeavyweightSixSetting setting,
        Func<int, int, IReadOnlyList<string>> leading,
        Func<int, string?> industryOf,
        Func<int, string?> sectorOf,
        Func<int, int, IndexQuality, bool> clears)
    {
        var (from, to) = setting.TwelveOne ? (session - TwelveMonths, session - SkippedMonth) : (session - BaseWindow, session);

        if (from < 0)
        {
            return new([]);
        }

        var bought = new List<int>();

        foreach (var industry in leading(from, to).Take(BaseIndustries))
        {
            bought.AddRange(Enumerable.Range(0, tape.Tickers.Length)
                .Where(name => tape.Member[name][session] && industryOf(name) == industry
                    && tape.Close[name][from] > 0 && !double.IsNaN(tape.Close[name][to]) && !double.IsNaN(tape.Close[name][session])
                    && clears(name, session, setting.Quality))
                .OrderByDescending(name => (tape.Close[name][to] / tape.Close[name][from]) - 1.0)
                .ThenBy(name => tape.Tickers[name], StringComparer.Ordinal)
                .Take(setting.PerIndustry));
        }

        return new([
            .. bought
                .GroupBy(name => sectorOf(name) ?? "none", StringComparer.Ordinal)
                .OrderBy(sector => sector.Key, StringComparer.Ordinal)
                .Select(sector => (
                    sector.Key,
                    sector.ToArray(),
                    Enumerable.Range(0, tape.Tickers.Length).Where(name => tape.Member[name][session] && (sectorOf(name) ?? "none") == sector.Key).ToArray())),
        ]);
    }
}
