using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Ledger;

// The point-in-time check: a setup's readings rebuilt from the history cut at its own session, holding only what stood
// then, and held to the readings the ledger stored for it. The cut keeps the calendar to the session, each name's bars to
// it and each market series to its day; it keeps the members' spans and the earnings calendar whole, since a member's
// span is dated and a report's date is announced before it; the quarters and the filed facts are read as filed before
// the session by the readings themselves. A reading that read anything past its session differs between the two.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public static class LedgerPointInTime
{
    // How far two readings may differ and still be one: a millionth of the larger.
    public const double Tolerance = 1e-6;

    public static SweepHistoryInputs Cut(SweepHistoryInputs inputs, DateOnly session) =>
        inputs with
        {
            Through = session,
            Sessions = [.. inputs.Sessions.Where(day => day <= session)],
            Names = [.. inputs.Names.Select(name => name with { Bars = [.. name.Bars.Where(bar => bar.Session <= session)] })],
        };

    public static LedgerMarket Cut(LedgerMarket market, DateOnly session) =>
        new(market.Series.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<(DateOnly Session, double Close)>)[.. pair.Value.Where(close => close.Session <= session)],
            StringComparer.Ordinal));

    // The readings of one stock on the last session of a cut history, none where the cut holds no bar of it there.
    public static double?[]? Rebuilt(
        string index,
        SweepHistoryInputs cut,
        LedgerMarket market,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        IReadOnlyDictionary<string, string?> sectors,
        IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>> facts,
        string ticker)
    {
        var calendar = cut.Sessions;

        if (calendar.Length == 0)
        {
            return null;
        }

        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[cut.Names.Count];

        Parallel.For(0, cut.Names.Count, name => series[name] = SweepColumns.Series(cut.Names[name], sessionAt));

        var name = Array.FindIndex(series, one => string.Equals(one.Name.Ticker, ticker, StringComparison.Ordinal));
        var at = calendar.Length - 1;

        if (name < 0)
        {
            return null;
        }

        var bar = IndexNightRead.BarOf(series[name], at);

        if (bar < 0)
        {
            return null;
        }

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);
        var closes = series[name].Bars.Select(held => Statistic.FromPrice(held.Close)).ToArray();

        return LedgerSetups.Readings(index, name, series[name], closes, bar, sessions[at], members, calendar, at, market, new LedgerContext(income, sectors, highs, lows, facts));
    }

    // The readings that differ between a stored row and its rebuild, each by its column: one none and the other a figure,
    // or two figures apart by more than a millionth of the larger.
    public static IReadOnlyList<(string Reading, double? Stored, double? Rebuilt)> Differences(IReadOnlyList<double?> stored, IReadOnlyList<double?> rebuilt)
    {
        var differ = new List<(string, double?, double?)>();

        for (var at = 0; at < LedgerReadings.Count; at++)
        {
            var (one, other) = (stored[at], rebuilt[at]);

            if (one is null != other is null
                || (one is { } a && other is { } b && Math.Abs(a - b) > Tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)))))
            {
                differ.Add((LedgerReadings.All[at].Column, one, other));
            }
        }

        return differ;
    }
}
