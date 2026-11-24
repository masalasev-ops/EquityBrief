using EquityBrief.Core.Families;
using EquityBrief.Core.Insiders;
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
// the session by the readings themselves. The history cut is read through the session the build read it through, since
// the pulled years are scaled onto the stored closes by the median ratio over the sessions both hold through that end,
// so a history read to another end prices them a rounding apart. A reading that read anything past its session differs
// between the two, a family's own readings among them.
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

    // The readings every setup carries of one stock on the last session of a cut history, none where the cut holds no
    // bar of it there.
    public static double?[]? Rebuilt(
        string index,
        SweepHistoryInputs cut,
        LedgerMarket market,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        IReadOnlyDictionary<string, string?> sectors,
        IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>> facts,
        string ticker,
        LedgerInsiders? insiders = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>? releases = null,
        IReadOnlyDictionary<DateOnly, double>? largeBreadth = null,
        IReadOnlyDictionary<string, string?>? gics = null,
        IReadOnlyDictionary<string, LedgerActions>? actions = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerTrend>>? trends = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerShortInterest>>? shortInterest = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerOneOff>>? oneOffs = null,
        bool table = false,
        IReadOnlyDictionary<string, SetupLedger.ReportFiled>? reportFilings = null)
    {
        if (Read(cut, income, sectors, facts, ticker, insiders, releases, largeBreadth, gics, actions, trends, shortInterest, oneOffs) is not { } read)
        {
            return null;
        }

        var readings = LedgerSetups.Readings(index, read.Name, read.Series[read.Name], Closes(read.Series[read.Name]), read.Bar, read.Sessions[read.At], read.Members, read.Calendar, read.At, market, read.Context, read.Series);

        // From the 24.19 correction a row of the every-session table is rebuilt as the table reads its seven of its own.
        if (table)
        {
            TableOwnReadings.Apply(readings, new TradedRanks(read.Series, read.Members), read.Name, read.Series[read.Name], read.Bar, reportFilings is { } filings ? SetupLedger.ReportDaysOf(filings, read.Series[read.Name]) : null);
        }

        return readings;
    }

    // One stock's setup of a family on the last session of a cut history as the build reads it: a swing family's from the
    // family's own loose gates over the cut, its own readings beside every setup's, and a heavyweights' setup's the
    // readings every setup carries, since the rule reads none of its own. Held is false where the cut holds no bar of the
    // stock there, and Readings none where the family's gates pass no setup of it on that session.
    public static (bool Held, double?[]? Readings) Setup(
        string index,
        SweepHistoryInputs cut,
        LedgerMarket market,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        IReadOnlyDictionary<string, string?> sectors,
        IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>> facts,
        string family,
        string ticker,
        LedgerInsiders? insiders = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>? releases = null,
        IReadOnlyDictionary<DateOnly, double>? largeBreadth = null,
        IReadOnlyDictionary<string, string?>? gics = null,
        IReadOnlyDictionary<string, LedgerActions>? actions = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerTrend>>? trends = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerShortInterest>>? shortInterest = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerOneOff>>? oneOffs = null)
    {
        if (Read(cut, income, sectors, facts, ticker, insiders, releases, largeBreadth, gics, actions, trends, shortInterest, oneOffs) is not { } read)
        {
            return (false, null);
        }

        if (family == HeavyweightRule.Name)
        {
            return (true, LedgerSetups.Readings(index, read.Name, read.Series[read.Name], Closes(read.Series[read.Name]), read.Bar, read.Sessions[read.At], read.Members, read.Calendar, read.At, market, read.Context, read.Series));
        }

        var row = LedgerSetups.On(index, read.Series, read.Sessions, read.Members, read.Calendar, read.At, market, read.Context)
            .Where(setups => setups.Family == family)
            .SelectMany(setups => setups.Rows)
            .FirstOrDefault(setup => string.Equals(setup.Ticker, ticker, StringComparison.Ordinal));

        return (true, row?.Readings);
    }

    // From the operator's order of 2026-11-24, item 4, a cut whose members on its last sessions given are those a set holds on
    // each, a code a member where the set names it and none where it does not, the cut's own spans before them and on a
    // session the set does not hold.
    public static SweepHistoryInputs OverMembers(SweepHistoryInputs cut, IReadOnlyDictionary<DateOnly, HashSet<string>> membersOn, int sessions)
    {
        var window = cut.Sessions.TakeLast(sessions).ToArray();

        if (window.Length == 0)
        {
            return cut;
        }

        var from = window[0];

        return cut with
        {
            Names =
            [
                .. cut.Names.Select(name => name with
                {
                    Spans =
                    [
                        .. name.Spans
                            .Where(span => span.Joined is not { } joined || joined < from)
                            .Select(span => (span.Joined, (DateOnly?)(span.Left is { } left && left < from ? left : from))),
                        .. window
                            .Where(day => membersOn.TryGetValue(day, out var set) ? set.Contains(name.Ticker) : name.MemberOn(day))
                            .Select(day => ((DateOnly?)day, (DateOnly?)day.AddDays(1))),
                    ],
                }),
            ],
        };
    }

    // The rows of the tickers given on the last session of a cut history, read once over the cut as the ledger's own build
    // reads them, each with the third and fourth alphas at the traded price over the same members beside it; a ticker the
    // cut holds no bar of there left out.
    public static IReadOnlyDictionary<string, (double?[] Readings, double? Alpha3Traded, double? Alpha4Traded)> RebuiltMany(
        string index,
        SweepHistoryInputs cut,
        LedgerMarket market,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        IReadOnlyDictionary<string, string?> sectors,
        IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>> facts,
        IEnumerable<string> tickers,
        LedgerInsiders? insiders = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>? releases = null,
        IReadOnlyDictionary<DateOnly, double>? largeBreadth = null,
        IReadOnlyDictionary<string, string?>? gics = null,
        IReadOnlyDictionary<string, LedgerActions>? actions = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerTrend>>? trends = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerShortInterest>>? shortInterest = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerOneOff>>? oneOffs = null)
    {
        var rows = new Dictionary<string, (double?[] Readings, double? Alpha3Traded, double? Alpha4Traded)>(StringComparer.Ordinal);

        if (Read(cut, income, sectors, facts, null, insiders, releases, largeBreadth, gics, actions, trends, shortInterest, oneOffs) is not { } read)
        {
            return rows;
        }

        var traded = new TradedRanks(read.Series, read.Members);

        foreach (var ticker in tickers.Distinct(StringComparer.Ordinal))
        {
            var name = Array.FindIndex(read.Series, one => string.Equals(one.Name.Ticker, ticker, StringComparison.Ordinal));
            var bar = name < 0 ? -1 : IndexNightRead.BarOf(read.Series[name], read.At);

            if (bar < 0)
            {
                continue;
            }

            var readings = LedgerSetups.Readings(index, name, read.Series[name], Closes(read.Series[name]), bar, read.Sessions[read.At], read.Members, read.Calendar, read.At, market, read.Context, read.Series);

            rows[ticker] = (readings, traded.Alpha3(name, bar), traded.Alpha4(name, bar));
        }

        return rows;
    }

    sealed record CutRead(SweepSeries[] Series, IReadOnlyList<SweepColumns.Session> Sessions, SweepBenchmark.Members Members, DateOnly[] Calendar, int At, int Name, int Bar, LedgerContext Context);

    // A cut read once, and the ticker given placed in it; none given places none.
    static CutRead? Read(
        SweepHistoryInputs cut,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        IReadOnlyDictionary<string, string?> sectors,
        IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>> facts,
        string? ticker,
        LedgerInsiders? insiders,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>? releases,
        IReadOnlyDictionary<DateOnly, double>? largeBreadth,
        IReadOnlyDictionary<string, string?>? gics,
        IReadOnlyDictionary<string, LedgerActions>? actions = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerTrend>>? trends = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerShortInterest>>? shortInterest = null,
        IReadOnlyDictionary<string, IReadOnlyList<LedgerOneOff>>? oneOffs = null)
    {
        var calendar = cut.Sessions;

        if (calendar.Length == 0)
        {
            return null;
        }

        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[cut.Names.Count];

        Parallel.For(0, cut.Names.Count, name => series[name] = SweepColumns.Series(cut.Names[name], sessionAt));

        var name = ticker is null ? -1 : Array.FindIndex(series, one => string.Equals(one.Name.Ticker, ticker, StringComparison.Ordinal));
        var at = calendar.Length - 1;

        if (ticker is not null && name < 0)
        {
            return null;
        }

        var bar = name < 0 ? -1 : IndexNightRead.BarOf(series[name], at);

        if (ticker is not null && bar < 0)
        {
            return null;
        }

        var members = SweepBenchmark.On(series, calendar.Length);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);

        return new CutRead(series, SweepColumns.Sessions(series, calendar), members, calendar, at, name, bar, new LedgerContext(income, sectors, highs, lows, facts)
        {
            Ranks = new FactorRanks(series, members),
            Insiders = insiders?.Rows,
            InsiderSpans = insiders?.Spans ?? InsiderCoverage.None,
            Releases = releases,
            LargeBreadth = largeBreadth,
            Gics = gics,
            Actions = actions,
            Trends = trends,
            ShortInterest = shortInterest,
            OneOffs = oneOffs,
        });
    }

    static double[] Closes(SweepSeries series) => [.. series.Bars.Select(held => Statistic.FromPrice(held.Close))];

    // From the 23.6 correction the readings the long build holds none of by design: the analysts' rating actions, which the
    // long build reads for no setup. Its check reads such a reading only where the long build stored one, and says how many
    // it left unread.
    // see: The long ledger's point-in-time check leaves unread a reading the long build holds none of by design, and says so
    public static IReadOnlyList<string> HeldNoneByTheLongBuild { get; } = ["rating_actions_net"];

    // The differences a long setup's check reads, and how many readings it left unread for the long build storing none of a
    // reading it holds none of by design.
    public static (IReadOnlyList<(string Reading, double? Stored, double? Rebuilt)> Read, int Unread) LongDifferences(IReadOnlyList<double?> stored, IReadOnlyList<double?> rebuilt)
    {
        var unread = 0;
        var kept = new double?[stored.Count];

        for (var at = 0; at < stored.Count; at++)
        {
            kept[at] = rebuilt[at];

            if (stored[at] is null && HeldNoneByTheLongBuild.Contains(LedgerReadings.All[at].Column, StringComparer.Ordinal))
            {
                kept[at] = null;
                unread++;
            }
        }

        return (Differences(stored, kept), unread);
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
