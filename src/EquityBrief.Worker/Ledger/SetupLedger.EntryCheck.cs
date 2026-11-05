using System.Collections.Concurrent;
using System.Globalization;
using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Volume;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.EntryCheck;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// The entry check's measurement by hand, over one index's stored years, the tester's entry point handing it the index. Each live pass's
// readings are read as of its session from the history's bars, split in bands with the stored result and its edge after costs, and each
// live pullback, drift and breakout is walked at two wait entries beside the next-open buy, through the ledger's own pricing. It reads the
// store and writes nothing to it, its answer an HTML page with its figures beside it under the data root's loop folder.
// see: The entry check's measurement is read-only over the stored years and registers nothing
public sealed partial class SetupLedger
{
    public const string EntryCheckStage = "loop-entry-check";

    // The stored nights the flags of each night's picks are counted over.
    public const int FlagNights = 20;

    public const string NextOpenEntry = "next open";
    public const string ZoneEntry = "wait plan's zone";
    public const string AverageEntry = "20-session average";

    const string LivePasses = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, risk_moves, result, benchmark, cost, edge_after_cost, ""end"", company_value, picked, settled, live_pass
        FROM setup WHERE index_code = $index AND (live_pass = 1 OR picked = 1) ORDER BY session_date, family, ticker;";

    const string EntryFilers = "SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;";

    const string StoredReleases = "SELECT cik, accepted_at, text_gzip FROM release_document WHERE release = 1 ORDER BY accepted_at;";

    const string StoredBeats = "SELECT ticker, event_date, eps_actual, eps_estimate FROM pulled_surprise WHERE eps_actual IS NOT NULL AND eps_estimate IS NOT NULL ORDER BY event_date;";

    sealed record EntryRow(string Family, string Ticker, DateOnly Session, double Entry, double Stop, double? Target, double? Trail, int Cap, double? RiskMoves, double? Result, double? Benchmark, double? Cost, double? EdgeAfterCost, string End, double? CompanyValue, bool Picked, bool Settled, bool LivePass);

    sealed record ReleaseRead(DateOnly On, OneOffItem? Item, decimal? Beat);

    sealed record EntryWalk(string Label, int Window, EntryTrade? Filled, bool Open, bool NoPlan);

    sealed record WalkedPick(string Family, string Era, EntryTrade Bought, IReadOnlyList<EntryWalk> Walks);

    // One entry on a family and era, every pick counted: filled or not, the next-open buy of the same picks beside it.
    public sealed record EntryRowFigures(
        string Family,
        string Era,
        string Entry,
        int Picks,
        int NoPlan,
        EntryGroup Filled,
        double? FillRate,
        double? ResultPerPick,
        double? EdgePerPick,
        double? PercentPerPick,
        EntryGroup NextOpenOnFilled,
        EntryGroup Missed);

    public sealed record BandFigures(string Family, string Era, string Reading, string Band, EntryGroup Group, double? EdgeOverFamily);

    public sealed record NightFlags(string Night, int Picks, int Stretched, int LargestGain, int Gap, int HighestClose, int OneOff, int NothingFlagged);

    public async Task<int> EntryCheckAsync(string index, string dataRoot, TextWriter output, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
        {
            output.WriteLine($"{EntryCheckStage}: the bar store holds no session, so nothing was read");

            return 0;
        }

        var rows = await EntryRowsAsync(connection, index, cancellation);

        if (rows.Count == 0)
        {
            output.WriteLine($"{EntryCheckStage}: the ledger holds no live pass on the {DecisionCards.NameOf(index)}, so nothing was read");

            return 0;
        }

        var prepared = await PrepareAsync(connection, index, Date(newest), output, cancellation, heavyweightsRead: false);
        var releases = await ReleasesReadAsync(connection, cancellation);

        output.WriteLine(FormattableString.Invariant($"{EntryCheckStage}: read {rows.Count} row(s) of the {DecisionCards.NameOf(index)} and the releases of {releases.Count} stock(s); reading each as of its session"));

        var readings = new ConcurrentDictionary<(int Name, int Bar), EntryReading?>();
        var plans = new ConcurrentDictionary<(int Name, int Bar), WaitPlan?>();
        var lows = new ConcurrentDictionary<int, double[]>();
        var highs = new ConcurrentDictionary<int, double[]>();
        var bands = new ConcurrentBag<(string Family, string Era, string Reading, string Band, EntryTrade Trade)>();
        var walked = new ConcurrentBag<WalkedPick>();
        var unread = 0;
        var done = 0;

        double[] LowsOf(int name) => lows.GetOrAdd(name, key => [.. prepared.Series[key].Bars.Select(bar => Statistic.FromPrice(bar.Low))]);
        double[] HighsOf(int name) => highs.GetOrAdd(name, key => [.. prepared.Series[key].Bars.Select(bar => Statistic.FromPrice(bar.High))]);

        EntryReading? ReadingOf(int name, int bar) => readings.GetOrAdd((name, bar), key => ReadingAsOf(prepared.Series[key.Name], key.Bar, releases));

        Parallel.ForEach(rows.Where(row => row.LivePass && row.Settled && row.Result is not null), new ParallelOptions { CancellationToken = cancellation }, row =>
        {
            if (Interlocked.Increment(ref done) % 20_000 == 0)
            {
                lock (output)
                {
                    output.WriteLine(FormattableString.Invariant($"{EntryCheckStage}: {done} setup(s) read"));
                }
            }

            var at = Array.BinarySearch(prepared.Calendar, row.Session);

            if (at < 0 || !prepared.Places.TryGetValue(row.Ticker, out var name) || IndexNightRead.BarOf(prepared.Series[name], at) is not (>= 0 and var bar) || ReadingOf(name, bar) is not { } reading)
            {
                Interlocked.Increment(ref unread);

                return;
            }

            var book = BookSetups.IsBook(row.Family);
            var result = book ? row.Result!.Value * 100 : row.Result!.Value;
            var edge = book || row.EdgeAfterCost is null ? EdgeOf(row, row.Result) : row.EdgeAfterCost;
            var era = EntryMeasure.EraOf(row.Session);

            foreach (var (one, band) in EntryMeasure.BandsOf(reading, StopDays(row, prepared.Series[name].Atr[bar], prepared.Closes[name][bar])))
            {
                bands.Add((row.Family, era, one, band, new EntryTrade(result, edge, row.End)));
            }

            if (row.Family is SetupFamilies.Pullback or BreakoutRule.Name or DriftRule.Name && WalkOf(prepared, row, name, bar, reading, plans, LowsOf(name), HighsOf(name)) is { } pick)
            {
                walked.Add(pick);
            }
        });

        var flags = FlagsOf(rows, prepared, newest, ReadingOf);
        var bandFigures = BandTable(bands);
        var entryFigures = EntryTable(walked);
        var lines = new List<string>
        {
            FormattableString.Invariant($"{rows.Count(row => row.LivePass && row.Settled && row.Result is not null)} settled live pass(es) on the {DecisionCards.NameOf(index)} over the history read through {newest}, {unread} whose stock or session the history does not hold left out; {walked.Count} of them a live pullback, drift or breakout walked at the wait entries."),
            "Each reading is read as of the setup's session, from the bars and the swings confirmed by then. A result and an edge are in risks, the plan's stop under the buy, and a book family's in percentage points. An edge is the result less the stored benchmark and the cost, the stored cost where the night stored one and the published spread for its size and price otherwise.",
            "A wait entry fills on the first session after the pick that trades into its level within its window, at that open where it opens at or under the level and at the level otherwise, and is sold as the ledger sells. The average's level is the 20-session average on the pick's night and its plan the rule's own stop, target, trail and cap. The zone's level is the top of the wait plan's zone on that night and its plan the wait plan's stop and first exit, to the rule's cap where it draws no exit. A pick no session fills in the window, or whose level or fill stands at or under its stop, or whose wait plan draws none, is counted as not taken. Results are stated in the risk the rule's own plan set at the pick's close, so entries are compared on the same number of shares. The zone's own stop is usually further away than the rule's, so the same number of shares puts more at risk there: read its result in its own risk and its return on the fill beside it, and not its result in the rule's risk alone. The benchmark is the stored one, every member bought at the next open under the rule's plan and sold as the trade is, so it is the like measure for the next-open buy and the average's entry and only a rough one for the zone's.",
            "These bands' edges are provisional and pick a starting point. Nothing here changes a pick, a plan, a record, a paper account or a family's setting.",
        };

        foreach (var line in lines)
        {
            output.WriteLine($"{EntryCheckStage}: {line}");
        }

        var (report, figures) = CheckReports.Write(
            dataRoot,
            FormattableString.Invariant($"entry-check-{index}"),
            $"The entry check's measurement on the {DecisionCards.NameOf(index)}",
            lines,
            [
                new CheckReports.Table(
                    "Each night's picks and the flags each fires on, the newest 20 stored nights",
                    ["Night", "Stocks picked", "Stretched", "Largest gain", "Gap", "Highest close over a year", "One-off item", "Nothing flagged"],
                    [.. flags.Select(night => (IReadOnlyList<string>)[night.Night, CheckReports.Cell(night.Picks), CheckReports.Cell(night.Stretched), CheckReports.Cell(night.LargestGain), CheckReports.Cell(night.Gap), CheckReports.Cell(night.HighestClose), CheckReports.Cell(night.OneOff), CheckReports.Cell(night.NothingFlagged)])]),
                new CheckReports.Table(
                    "Each entry against the next-open buy, by family and span",
                    ["Family", "Span", "Entry", "Picks", "No plan", "Filled", "Fill rate", "Mean result", "Result in its own risk", "Return on the fill, %", "Target rate", "Stop rate", "Trail rate", "Mean edge after costs", "Next open's result, same picks", "Next open's return, same picks, %", "Next open's edge, same picks", "Result per pick", "Edge per pick", "Return per pick, %", "Missed", "Next open's result, missed", "Next open's edge, missed"],
                    [.. entryFigures.Select(one => (IReadOnlyList<string>)[one.Family, one.Era, one.Entry, CheckReports.Cell(one.Picks), CheckReports.Cell(one.NoPlan), CheckReports.Cell(one.Filled.Trades), CheckReports.Cell(one.FillRate), CheckReports.Cell(one.Filled.MeanResult, signed: true), CheckReports.Cell(one.Filled.MeanOwnRisk, signed: true), CheckReports.Cell(one.Filled.MeanPercent, signed: true), CheckReports.Cell(one.Filled.TargetRate), CheckReports.Cell(one.Filled.StopRate), CheckReports.Cell(one.Filled.TrailRate), CheckReports.Cell(one.Filled.MeanEdge, signed: true), CheckReports.Cell(one.NextOpenOnFilled.MeanResult, signed: true), CheckReports.Cell(one.NextOpenOnFilled.MeanPercent, signed: true), CheckReports.Cell(one.NextOpenOnFilled.MeanEdge, signed: true), CheckReports.Cell(one.ResultPerPick, signed: true), CheckReports.Cell(one.EdgePerPick, signed: true), CheckReports.Cell(one.PercentPerPick, signed: true), CheckReports.Cell(one.Missed.Trades), CheckReports.Cell(one.Missed.MeanResult, signed: true), CheckReports.Cell(one.Missed.MeanEdge, signed: true)])]),
                new CheckReports.Table(
                    "Each reading in bands, by family and span",
                    ["Family", "Span", "Reading", "Band", "Trades", "Mean result", "Target rate", "Stop rate", "Trail rate", "Mean edge after costs", "Edge against the family's"],
                    [.. bandFigures.Select(one => (IReadOnlyList<string>)[one.Family, one.Era, one.Reading, one.Band, CheckReports.Cell(one.Group.Trades), CheckReports.Cell(one.Group.MeanResult, signed: true), CheckReports.Cell(one.Group.TargetRate), CheckReports.Cell(one.Group.StopRate), CheckReports.Cell(one.Group.TrailRate), CheckReports.Cell(one.Group.MeanEdge, signed: true), CheckReports.Cell(one.EdgeOverFamily, signed: true)])]),
            ],
            new { index, through = newest, setups = rows.Count, unread, walked = walked.Count, flags, entries = entryFigures, bands = bandFigures },
            clock.UtcNow);

        output.WriteLine($"{EntryCheckStage}: written to {report} with its figures at {figures}; registered nothing and wrote no store row");

        return 0;
    }

    static async Task<List<EntryRow>> EntryRowsAsync(SqliteConnection connection, string index, CancellationToken cancellation)
    {
        var rows = new List<EntryRow>();

        await using var command = Command(connection, null, LivePasses, [("$index", index)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            double? Price(int column) => reader.IsDBNull(column) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(column)));
            double? Real(int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);

            rows.Add(new EntryRow(
                reader.GetString(0),
                reader.GetString(1),
                Date(reader.GetString(2)),
                Price(3) ?? 0,
                Price(4) ?? 0,
                Price(5),
                Price(6),
                reader.GetInt32(7),
                Real(8),
                Real(9),
                Real(10),
                Real(11),
                Real(12),
                reader.GetString(13),
                Real(14),
                !reader.IsDBNull(15) && reader.GetInt64(15) == 1,
                reader.GetInt64(16) == 1,
                reader.GetInt64(17) == 1));
        }

        return rows;
    }

    // Each stock's stored releases, oldest first, with the item each names and the beat stored for its report.
    static async Task<IReadOnlyDictionary<string, IReadOnlyList<ReleaseRead>>> ReleasesReadAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var filers = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var command = Command(connection, null, EntryFilers, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                filers[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var beats = new Dictionary<string, List<(DateOnly On, decimal Beat)>>(StringComparer.Ordinal);

        await using (var command = Command(connection, null, StoredBeats, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                if (decimal.TryParse(reader.GetString(2), NumberStyles.Number, CultureInfo.InvariantCulture, out var actual)
                    && decimal.TryParse(reader.GetString(3), NumberStyles.Number, CultureInfo.InvariantCulture, out var estimate))
                {
                    if (!beats.TryGetValue(reader.GetString(0), out var held))
                    {
                        beats[reader.GetString(0)] = held = [];
                    }

                    held.Add((Date(reader.GetString(1)), actual - estimate));
                }
            }
        }

        var byFiler = new Dictionary<string, List<(DateOnly On, OneOffItem? Item)>>(StringComparer.Ordinal);

        await using (var command = Command(connection, null, StoredReleases, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                if (!byFiler.TryGetValue(reader.GetString(0), out var held))
                {
                    byFiler[reader.GetString(0)] = held = [];
                }

                held.Add((Date(reader.GetString(1)[..10]), OneOffItems.Of(ResultsReleases.Decompressed((byte[])reader[2]))));
            }
        }

        var read = new Dictionary<string, IReadOnlyList<ReleaseRead>>(StringComparer.Ordinal);

        foreach (var (ticker, cik) in filers)
        {
            if (byFiler.TryGetValue(cik, out var held))
            {
                var own = beats.GetValueOrDefault(ticker) ?? [];

                read[ticker] = [.. held.Select(one =>
                {
                    var near = own.Where(beat => Math.Abs(beat.On.DayNumber - one.On.DayNumber) <= EntryReader.ReportReachDays).ToArray();

                    return new ReleaseRead(one.On, one.Item, near.Length > 0 ? near[^1].Beat : null);
                })];
            }
        }

        return read;
    }

    // The reading of a stock as of a bar, the stored year then being the year the night held, the bars before it the older history, and
    // its newest release accepted by then.
    static EntryReading? ReadingAsOf(SweepSeries series, int bar, IReadOnlyDictionary<string, IReadOnlyList<ReleaseRead>> releases)
    {
        static EntryBar Read(SweepBar one) => new(one.Session, one.High, one.Low, one.Close, one.Volume);

        var start = series.WindowStart[bar];
        var reading = EntryReadings.Of(
            series.Name.Ticker,
            [.. series.Bars[start..(bar + 1)].Select(Read)],
            [.. series.Bars[..start].Select(Read)],
            Finite(series.Atr[bar]),
            Finite(series.Rsi[bar]),
            Finite(series.Volume50[bar]));

        if (reading is null || !releases.TryGetValue(series.Name.Ticker, out var own) || own.LastOrDefault(one => one.On <= reading.Session) is not { } release)
        {
            return reading;
        }

        return reading with
        {
            ReleaseOn = release.On,
            OneOffSentence = release.Item?.Sentence,
            OneOffAmount = release.Item?.Amount,
            OneOffTied = release.Item?.TiedToEarnings,
            OneOffDirection = release.Item?.Direction,
            Beat = release.Beat,
        };
    }

    static double? Finite(double value) => double.IsFinite(value) ? value : null;

    static double? StopDays(EntryRow row, double typical, double close)
    {
        if (row.RiskMoves is { } moves)
        {
            return moves;
        }

        return row.Entry > row.Stop && row.Entry > 0 && typical > 0 ? (row.Entry - row.Stop) * (close / row.Entry) / typical : null;
    }

    // The wait plan as of a bar, from the bands and the trend the night's own functions read over the year it held.
    static WaitPlan? PlanAsOf(SweepSeries series, int bar, EntryReading reading)
    {
        var from = Math.Max(0, bar - VolumeProfileSeries.Window + 1);
        var bars = series.Bars[from..(bar + 1)].Select(one => new ReplayBar(one.Session, one.High, one.Low, one.Close, one.Volume)).ToArray();
        var indicators = new Dictionary<string, double>(StringComparer.Ordinal);

        void Hold(string key, double value)
        {
            if (double.IsFinite(value) && value > 0)
            {
                indicators[key] = value;
            }
        }

        Hold(IndicatorSeries.Atr14, series.Atr[bar]);
        Hold(IndicatorSeries.Sma20, series.Sma20[bar]);
        Hold(IndicatorSeries.Sma50, series.Sma50[bar]);
        Hold(IndicatorSeries.Sma200, series.Sma200[bar]);

        var confirmed = series.Swings.Take(series.Confirmed[bar]).ToArray();
        var close = series.Bars[bar].Close;
        decimal? Average(string key) => indicators.TryGetValue(key, out var value) ? Statistic.ToPrice(value) : null;
        var trend = TrendSeries.For(close, Average(IndicatorSeries.Sma50), Average(IndicatorSeries.Sma200), confirmed);

        return WaitPlans.Of(
            close,
            reading.GapHigh,
            reading.GapLow,
            reading.TypicalDay,
            SessionReplay.BandsOver(bars, indicators, confirmed),
            trend.State,
            [.. confirmed.Where(swing => swing.Direction == SwingSeries.Low).OrderBy(swing => swing.SessionDate).Select(swing => swing.Price)]);
    }

    static double? EdgeOf(EntryRow row, double? result)
    {
        var book = BookSetups.IsBook(row.Family);
        var form = book ? ChanceForm.Book : row.Trail is null ? ChanceForm.Target : ChanceForm.Trailing;
        var after = LikeSetups.AfterCost(form, result, book ? null : row.Cost, row.Entry, row.Stop, row.CompanyValue);

        return after is { } held && row.Benchmark is { } benchmark ? held - (book ? benchmark * 100 : benchmark) : null;
    }

    // One pick at the next open and at each wait entry and window, each in the risk the rule's plan set at its close.
    static WalkedPick? WalkOf(PreparedHistory prepared, EntryRow row, int name, int bar, EntryReading reading, ConcurrentDictionary<(int Name, int Bar), WaitPlan?> plans, double[] lows, double[] highs)
    {
        var closes = prepared.Closes[name];
        var opens = prepared.Opens[name];
        var series = prepared.Series[name];
        var factor = row.Entry > 0 ? closes[bar] / row.Entry : 1;
        var anchor = new SetupAnchor(row.Session, closes[bar], row.Stop * factor, row.Target * factor, row.Trail * factor, row.Cap, row.RiskMoves);
        double? open = bar + 1 < opens.Length && bar + 1 < closes.Length ? opens[bar + 1] : null;

        if (ExitPricings.Price(closes, opens, highs, bar, anchor, open)?.AtNextOpen is not { Result: { } result } bought)
        {
            return null;
        }

        var plan = plans.GetOrAdd((name, bar), key => PlanAsOf(series, key.Bar, reading));
        var walks = new List<EntryWalk>();

        foreach (var window in EntryMeasure.Windows)
        {
            walks.Add(Walk(AverageEntry, window, series.Sma20[bar], anchor));

            if (plan is { ZoneHigh: { } top, Stop: { } stop })
            {
                var zone = new SetupAnchor(row.Session, closes[bar], Statistic.FromPrice(stop), plan.FirstExit is { } exit ? Statistic.FromPrice(exit) : double.PositiveInfinity, null, row.Cap, row.RiskMoves);

                walks.Add(Walk(ZoneEntry, window, Statistic.FromPrice(top), zone));
            }
            else
            {
                walks.Add(new EntryWalk(ZoneEntry, window, null, false, true));
            }
        }

        return new WalkedPick(row.Family, EntryMeasure.EraOf(row.Session), new EntryTrade(result, EdgeOf(row, result), bought.End, open > 0 ? result * anchor.Risk / open * 100 : null, result), walks);

        EntryWalk Walk(string label, int window, double level, SetupAnchor plan2)
        {
            if (!double.IsFinite(level) || EntryMeasure.FillOf(opens, lows, bar, level, plan2.Stop, window) is not { } fill)
            {
                return new EntryWalk(label, window, null, false, false);
            }

            var priced = ExitPricings.Price(closes, opens, highs, fill.Bar - 1, plan2, fill.Price)?.AtNextOpen;

            if (priced?.Result is not { } made)
            {
                return new EntryWalk(label, window, null, true, false);
            }

            var inRulesRisk = EntryMeasure.InTheRulesRisk(made, plan2.Risk, anchor.Risk);

            return new EntryWalk(label, window, new EntryTrade(inRulesRisk, EdgeOf(row, inRulesRisk), priced.End, made * plan2.Risk / fill.Price * 100, made), false, false);
        }
    }

    // How many of each of the newest nights' picked stocks each flag fires on, a stock two families list counted once, as its card is.
    static IReadOnlyList<NightFlags> FlagsOf(IReadOnlyList<EntryRow> rows, PreparedHistory prepared, string newest, Func<int, int, EntryReading?> readingOf)
    {
        var nights = prepared.Calendar.Where(day => day <= Date(newest)).TakeLast(FlagNights).ToArray();
        var flags = new List<NightFlags>();

        foreach (var night in nights)
        {
            var at = Array.BinarySearch(prepared.Calendar, night);
            var read = rows
                .Where(row => row.Picked && row.Session == night)
                .Select(row => row.Ticker)
                .Distinct(StringComparer.Ordinal)
                .Select(ticker => prepared.Places.TryGetValue(ticker, out var name) && IndexNightRead.BarOf(prepared.Series[name], at) is >= 0 and var bar ? readingOf(name, bar) : null)
                .OfType<EntryReading>()
                .ToArray();

            flags.Add(new NightFlags(
                night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                read.Length,
                read.Count(EntryReadings.Stretched),
                read.Count(one => one.LargestGain == true),
                read.Count(one => one.GapHigh is not null),
                read.Count(EntryReadings.HighestLongSince),
                read.Count(one => one.OneOffAmount is not null),
                read.Count(one => EntryWords.Flags(one).Count == 0)));
        }

        return flags;
    }

    static IReadOnlyList<BandFigures> BandTable(IEnumerable<(string Family, string Era, string Reading, string Band, EntryTrade Trade)> bands)
    {
        var all = bands.ToArray();
        var figures = new List<BandFigures>();

        foreach (var family in all.GroupBy(one => one.Family, StringComparer.Ordinal).OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            foreach (var era in new[] { EntryMeasure.BeforeRecent, EntryMeasure.FromRecent })
            {
                var inEra = family.Where(one => one.Era == era).ToArray();
                var own = inEra.GroupBy(one => one.Reading, StringComparer.Ordinal).FirstOrDefault()?.ToArray() ?? [];
                var whole = EntryMeasure.GroupOf([.. own.Select(one => one.Trade)]);

                figures.Add(new BandFigures(family.Key, era, EntryMeasure.AllTrades, EntryMeasure.AllTrades, whole, null));

                foreach (var group in inEra.GroupBy(one => (one.Reading, one.Band)).OrderBy(one => one.Key.Reading, StringComparer.Ordinal).ThenBy(one => one.Key.Band, StringComparer.Ordinal))
                {
                    var read = EntryMeasure.GroupOf([.. group.Select(one => one.Trade)]);

                    figures.Add(new BandFigures(family.Key, era, group.Key.Reading, group.Key.Band, read, read.MeanEdge - whole.MeanEdge));
                }
            }
        }

        return figures;
    }

    static IReadOnlyList<EntryRowFigures> EntryTable(IEnumerable<WalkedPick> walked)
    {
        var all = walked.ToArray();
        var figures = new List<EntryRowFigures>();

        foreach (var family in all.GroupBy(one => one.Family, StringComparer.Ordinal).OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            foreach (var era in new[] { EntryMeasure.BeforeRecent, EntryMeasure.FromRecent })
            {
                var picks = family.Where(one => one.Era == era).ToArray();

                if (picks.Length == 0)
                {
                    continue;
                }

                var nextOpen = EntryMeasure.GroupOf([.. picks.Select(one => one.Bought)]);

                figures.Add(new EntryRowFigures(family.Key, era, NextOpenEntry, picks.Length, 0, nextOpen, 100, nextOpen.MeanResult, nextOpen.MeanEdge, nextOpen.MeanPercent, nextOpen, EntryMeasure.GroupOf([])));

                foreach (var label in new[] { ZoneEntry, AverageEntry })
                {
                    foreach (var window in EntryMeasure.Windows)
                    {
                        figures.Add(EntryFigure(family.Key, era, label, window, picks));
                    }
                }
            }
        }

        return figures;
    }

    // A pick whose path was still open when the history ended has no result yet, so it counts neither as filled nor as missed.
    static EntryRowFigures EntryFigure(string family, string era, string label, int window, IReadOnlyList<WalkedPick> picks)
    {
        var read = picks.Select(pick => (Pick: pick, Walk: pick.Walks.Single(one => one.Label == label && one.Window == window))).Where(one => !one.Walk.Open).ToArray();
        var filled = read.Where(one => one.Walk.Filled is not null).ToArray();
        var missed = read.Where(one => one.Walk.Filled is null).ToArray();
        var group = EntryMeasure.GroupOf([.. filled.Select(one => one.Walk.Filled!.Value)]);

        return new EntryRowFigures(
            family,
            era,
            FormattableString.Invariant($"{label}, within {window}"),
            read.Length,
            read.Count(one => one.Walk.NoPlan),
            group,
            read.Length == 0 ? null : 100d * filled.Length / read.Length,
            read.Length == 0 ? null : filled.Sum(one => one.Walk.Filled!.Value.Result) / read.Length,
            read.Length == 0 ? null : filled.Sum(one => one.Walk.Filled!.Value.Edge ?? 0) / read.Length,
            read.Length == 0 ? null : filled.Sum(one => one.Walk.Filled!.Value.Percent ?? 0) / read.Length,
            EntryMeasure.GroupOf([.. filled.Select(one => one.Pick.Bought)]),
            EntryMeasure.GroupOf([.. missed.Select(one => one.Pick.Bought)]));
    }
}
