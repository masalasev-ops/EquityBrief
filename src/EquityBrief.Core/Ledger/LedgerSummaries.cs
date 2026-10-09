using System.Globalization;

namespace EquityBrief.Core.Ledger;

// One family's setups on an index in one year as the Ledger page draws them: how many, how many the live rule passes,
// how many the night wrote and how many of those its list picked, how many settled, the mean result and edge over the
// settled, and the nine cut points between the deciles of each.
public sealed record LedgerYear(
    string Index,
    string Family,
    int Year,
    int Setups,
    int LivePasses,
    int NightRows,
    int Picked,
    int Settled,
    double? ResultMean,
    double? EdgeMean,
    IReadOnlyList<double>? ResultDeciles,
    IReadOnlyList<double>? EdgeDeciles);

// One settled setup as the Ledger page lists it: its family, stock and session, whether the live rule passed it, how its
// path ended and on which session after how many, its result and edge, and its plan's prices.
public sealed record LedgerSetupRow(
    string Family,
    string Ticker,
    DateOnly Session,
    bool LivePass,
    string End,
    DateOnly? EndedOn,
    int? Sessions,
    double? Result,
    double? Edge,
    decimal Entry,
    decimal Stop,
    decimal? Target,
    decimal? Trail);

// A chosen setup and the closes the store holds for its stock from ten sessions before it to the session its path ended
// on, or to the newest it holds while the path is open: the bar store's year, and the kept bars beneath it.
public sealed record LedgerPath(LedgerSetupRow Setup, IReadOnlyList<(DateOnly Session, decimal Close)> Closes);

// The ledger's summary, which its writers refresh so the page draws counts and never computes them: a family's setups a
// year on each index, the share passed and picked, and the deciles of result and edge over the settled ones.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public static class LedgerSummaries
{
    // The cut points between deciles: the tenth, the fifth and on to nine tenths.
    public const int CutPoints = 9;

    // Each cut point the figure at that share of the figures sorted, by the nearest rank: the smallest figure at or past
    // the share of the count. None for no figure.
    public static IReadOnlyList<double>? Deciles(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();

        return [.. Enumerable.Range(1, CutPoints).Select(tenth => sorted[NearestRank(sorted.Length, tenth)])];
    }

    public static int NearestRank(int count, int tenth) => Math.Max(0, (int)Math.Ceiling(tenth * count / 10.0) - 1);

    // The cut points as stored, invariant and comma-separated.
    public static string Stored(IReadOnlyList<double> deciles) =>
        string.Join(",", deciles.Select(cut => cut.ToString("0.######", CultureInfo.InvariantCulture)));

    public static IReadOnlyList<double> Read(string stored) =>
        [.. stored.Split(',').Select(cut => double.Parse(cut, NumberStyles.Float, CultureInfo.InvariantCulture))];
}
