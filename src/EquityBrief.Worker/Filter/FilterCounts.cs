using System.Globalization;
using System.Text;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Shortlist;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

// What a count left out and how closely its replay reproduces what the store keeps: the sessions of the
// year no breadth can be read on, and, over the nights the store keeps bands, trends and plans for,
// how many members the replay gives the same bands, trend and first tranche for.
public sealed record CountNotes(int Unreadable, DateOnly? FirstReadable, int Compared, int SameBands, int SameTrend, int SamePlan);

// One session's counts under one setting of the market gate and the trade gate's input.
public sealed record SessionCounts(
    DateOnly Session,
    string Source,
    double? Breadth,
    IReadOnlyList<int> Funnel,
    IReadOnlyList<int> Alone,
    IReadOnlyList<int> RelaxedAlone,
    int Pullbacks,
    int Breakouts);

// The shape counts the operator rules the filter's starting settings from: for each session, the
// members through each gate in order and what each removed, each gate alone, each gate relaxed with
// every other held, the trade gate read from the ladder's first tranche and from the swing trade's own
// plan, and the market gate at two floors. Over the nights the store keeps bands and plans for, those
// are read; over the rest of the stored year they are replayed as of each session.
//
// Shape only: it reads no outcome, scores nothing and writes nothing. The store is opened read-only.
// see: The swing filter's starting settings are ruled from shape counts before tonight's list switches to it
public sealed class FilterCounts : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.SeriesState, Touch.Read),
        ],
        Feeds: []);

    // The lower of the two floors the market gate is counted at, beside section 17's proposed one.
    public const double LowerBreadthFloor = 0.45;

    // The two floors the market gate is counted at.
    public static IReadOnlyList<double> MarketFloors { get; } = [LowerBreadthFloor, FilterSettings.ProposedBreadthFloor];

    public const string Stored = "stored";
    public const string Replayed = "replayed";

    static readonly string[] Needed = [IndicatorSeries.Atr14, IndicatorSeries.VolAvg50, IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200];

    readonly string databaseFile;

    public FilterCounts(string databaseFile) => this.databaseFile = databaseFile;

    // Every counted session under every setting, keyed by the setting's name.
    public CountNotes Notes { get; private set; } = new(0, null, 0, 0, 0, 0);

    // Each night the store keeps listings for, as the shape clock reads it at section 17's proposed
    // values: the counts through the gates with the market held open, the list, and the index's median
    // volume ratio; and each night's firing off its listings. What the classifier is run over.
    public IReadOnlyList<NightShape> Shapes { get; private set; } = [];

    public IReadOnlyList<NightFiring> Firings { get; private set; } = [];

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SessionCounts>>> CountAsync(
        string indexCode,
        bool year,
        Action<string>? progress = null,
        CancellationToken cancellation = default)
    {
        // Read-only, so nothing the counts do can reach the store they count.
        var builder = StoreConnection.Builder(databaseFile);
        builder.Mode = SqliteOpenMode.ReadOnly;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);

        var spans = await SpansAsync(connection, indexCode, cancellation);
        var bars = await BarsAsync(connection, cancellation);
        var indicators = await IndicatorsAsync(connection, cancellation);
        var prints = await EventsAsync(connection, "WHERE kind = 'earnings'", cancellation);
        var events = await EventsAsync(connection, string.Empty, cancellation);
        var suspects = await SuspectsAsync(connection, cancellation);
        var storedNights = await StoredNightsAsync(connection, cancellation);

        var sessions = TradingCalendar.Sessions([.. bars.Values.Select(series => (IReadOnlyCollection<DateOnly>)[.. series.Select(bar => bar.SessionDate)])]);
        // The year from the first session a return over the longer span can be read on, since before it no
        // member has a place and the trend gate passes nobody.
        var counted = year ? sessions.Skip(SwingReadings.ReturnLongSessions).ToArray() : [.. storedNights.Order()];

        var settings = Settings();
        var counts = settings.Keys.ToDictionary(name => name, _ => new List<SessionCounts>(), StringComparer.Ordinal);
        var eventsOn = settings.Keys.ToDictionary(name => name, _ => new Dictionary<DateOnly, IReadOnlyDictionary<string, bool?>>(), StringComparer.Ordinal);
        var earlierKept = settings.Values.Max(setting => setting.ArrivalSessions) - 1;

        var shapes = new List<NightShape>();
        var unreadable = 0;
        DateOnly? firstReadable = null;
        var compared = 0;
        var bandsAgree = 0;
        var trendsAgree = 0;
        var plansAgree = 0;

        foreach (var session in counted)
        {
            var members = spans
                .Where(span => (span.Joined is not { } joined || joined <= session) && (span.Left is not { } left || left > session))
                .Select(span => span.Ticker)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToArray();

            var stored = storedNights.Contains(session) ? await StoredAsync(connection, session, cancellation) : null;
            var inputs = await InputsAsync(connection, session, members, bars, indicators, prints, events, suspects, stored, earlierKept, cancellation);

            // A session no breadth can be read on is one the market gate and the trend classifier, which
            // both read the 200-day average, cannot answer for, so it is named and not counted.
            if (inputs is null || inputs.Breadth.Share is null)
            {
                unreadable++;

                continue;
            }

            firstReadable ??= session;

            if (year && stored is not null)
            {
                var (sameBands, sameTrend, samePlan) = await ParityAsync(connection, session, inputs, bars, indicators, events, stored, cancellation);

                compared += inputs.Rows.Count(row => row.Inputs.Close is not null);
                bandsAgree += sameBands;
                trendsAgree += sameTrend;
                plansAgree += samePlan;
            }

            foreach (var (name, setting) in settings)
            {
                var kept = eventsOn[name];

                var results = inputs.Rows
                    .Select(row => SwingGates.Evaluate(WithEvents(row.Inputs, kept) with { Breadth = inputs.Breadth }, setting))
                    .ToArray();

                counts[name].Add(new SessionCounts(
                    session,
                    stored is null ? Replayed : Stored,
                    inputs.Breadth.Share,
                    SwingFunnel.Of(results),
                    [.. Enumerable.Range(0, SwingGates.Order.Length).Select(gate => results.Count(result => result.Gates[gate].Passed))],
                    SwingFunnel.RelaxedAlone(results),
                    results.Count(result => result.Family == SwingGates.Pullback),
                    results.Count(result => result.Family == SwingGates.Breakout)));

                // The shape clock reads the proposed values with the trigger's arrival read off the night
                // before, as the night itself does, over the nights the store keeps listings for.
                if (stored is not null && setting == FilterSettings.Proposed)
                {
                    shapes.Add(ShapeOf(session, inputs, results));
                }

                kept[session] = results.ToDictionary(result => result.Ticker, result => result.TriggerEvent, StringComparer.Ordinal);
            }

            progress?.Invoke(FormattableString.Invariant($"{session:yyyy-MM-dd} {(stored is null ? Replayed : Stored)} {inputs.Rows.Count} member(s)"));
        }

        Notes = new CountNotes(unreadable, firstReadable, compared, bandsAgree, trendsAgree, plansAgree);
        Shapes = shapes;
        Firings = await FiringsAsync(connection, [.. shapes.Select(one => one.Session)], cancellation);

        return counts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<SessionCounts>)pair.Value, StringComparer.Ordinal);
    }

    // A night's shape off the results the night's evaluation gave, counted through the gates after the
    // market with the market held open.
    static NightShape ShapeOf(DateOnly session, SessionInputs inputs, IReadOnlyList<GateResult> results)
    {
        var ratios = inputs.Rows
            .Where(row => row.Inputs.Volume is > 0 && row.Inputs.VolumeAverage50 is > 0)
            .Select(row => EquityBrief.Core.Prices.Statistic.FromVolume(row.Inputs.Volume!.Value) / row.Inputs.VolumeAverage50!.Value)
            .ToArray();

        int Through(int last) => results.Count(result => result.Gates.Skip(1).Take(last + 1).All(gate => gate.Passed));

        return new NightShape(
            session,
            ShapeClock.NoVersion,
            results.Count,
            [Through(0), Through(1), Through(2), Through(3)],
            results.Count(result => result.Gates.Skip(1).All(gate => gate.Passed) && result.Exclusions.Count == 0),
            SwingReadings.Median(ratios));
    }

    static async Task<IReadOnlyList<NightFiring>> FiringsAsync(SqliteConnection connection, IReadOnlyList<DateOnly> sessions, CancellationToken cancellation)
    {
        var rows = new List<(DateOnly, string)>();

        foreach (var session in sessions)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = "SELECT reasons FROM listing WHERE session_date = $day;";
            command.Parameters.AddWithValue("$day", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                rows.Add((session, reader.GetString(0)));
            }
        }

        return NightFirings.Of(rows);
    }

    // The shape clock's readings over the nights given and its classification of each, as the operator
    // reads them: each gate's share through the funnel with the market held open, each reason's share,
    // the volume ratio, and whether the night is an event with what made it one.
    public static string ShapeReport(IReadOnlyList<NightShape> shapes, IReadOnlyList<NightFiring> firings)
    {
        var report = new StringBuilder();
        var events = ShapeClock.Events(shapes, firings).ToDictionary(one => one.Session);

        report.AppendLine();
        report.AppendLine("The shape clock over the nights the store keeps listings for, the market held open: each gate's share through the funnel, each reason's share, the volume ratio, and the night's class.");
        report.AppendLine("session     trend setup trigger trade | " + string.Join(" ", ShortlistSeries.Reasons.Select(reason => reason.Split(' ')[0])) + " | volume | class");

        foreach (var shape in shapes)
        {
            var firing = firings.FirstOrDefault(one => one.Session == shape.Session);
            var gates = string.Join(" ", shape.Through.Select(count => (count * 100.0 / Math.Max(1, shape.Members)).ToString("0.0", CultureInfo.InvariantCulture)));
            var reasons = string.Join(" ", ShortlistSeries.Reasons.Select(reason =>
                firing is not null && firing.Reasons.TryGetValue(reason, out var count) && count.Counted > 0
                    ? (count.Fired * 100.0 / count.Counted).ToString("0.0", CultureInfo.InvariantCulture)
                    : "n/a"));
            var ratio = shape.MedianVolumeRatio is { } held ? held.ToString("0.00", CultureInfo.InvariantCulture) : "n/a";
            var type = events.TryGetValue(shape.Session, out var flooded)
                ? "event: " + string.Join(" and ", flooded.Floods.Select(flood => flood.Measure).Concat(flooded.VolumeRatio is null ? [] : ["volume"]))
                : "ordinary";

            report.AppendLine(FormattableString.Invariant($"{shape.Session:yyyy-MM-dd}  {gates} | {reasons} | {ratio} | {type}"));
        }

        return report.ToString();
    }

    // On a night the store keeps bands, trends and plans for, how many members the replay gives the same
    // band set, the same trend and the same first tranche and target as the night stored.
    static async Task<(int Bands, int Trend, int Plan)> ParityAsync(
        SqliteConnection connection,
        DateOnly session,
        SessionInputs inputs,
        IReadOnlyDictionary<string, List<ReplayBar>> bars,
        IReadOnlyDictionary<(string, DateOnly), Dictionary<string, double>> indicators,
        IReadOnlyDictionary<string, List<DateOnly>> events,
        Night stored,
        CancellationToken cancellation)
    {
        var (sameBands, sameTrend, samePlan) = (0, 0, 0);
        var empty = new Dictionary<string, double>();

        foreach (var row in inputs.Rows.Where(row => row.Inputs.Close is not null))
        {
            var ticker = row.Inputs.Ticker;
            var held = bars[ticker].TakeWhile(bar => bar.SessionDate <= session).ToList();
            var nextEvent = events.TryGetValue(ticker, out var any) ? any.FirstOrDefault(date => date >= session) : default;
            var replayed = await SessionReplay.ForAsync(
                connection,
                ticker,
                held,
                indicators.GetValueOrDefault((ticker, session)) ?? empty,
                nextEvent == default ? null : nextEvent,
                cancellation);

            var replayedBands = replayed.Bands
                .Select(band => (band.LowEdge, band.HighEdge, band.Role, band.Strength, band.HasNonAverageAnchor))
                .Order()
                .ToArray();
            var storedBands = (stored.Bands.GetValueOrDefault(ticker) ?? [])
                .Select(band => (band.LowEdge, band.HighEdge, band.Role, band.Strength, band.HasNonAverageAnchor))
                .Order()
                .ToArray();

            sameBands += replayedBands.SequenceEqual(storedBands) ? 1 : 0;
            sameTrend += stored.Trends.TryGetValue(ticker, out var trend) && trend == replayed.Trend.State ? 1 : 0;

            var kept = stored.Plans.TryGetValue(ticker, out var plan) ? ListedTranche.Of(plan) : null;
            var again = SessionReplay.FirstTrancheOf(replayed.Plan);

            samePlan += kept is not null && kept.Entry == again.Entry && kept.Stop == again.Stop && kept.Target == again.Target ? 1 : 0;
        }

        return (sameBands, sameTrend, samePlan);
    }

    // The four settings counted: the market gate at each floor, each with the trade gate read from the
    // ladder's first tranche and from the swing trade's own plan, every other threshold section 17's.
    public static IReadOnlyDictionary<string, FilterSettings> Settings() =>
        MarketFloors
            .SelectMany(floor => new[] { TradeInput.Ladder, TradeInput.Swing }.Select(trade => (floor, trade)))
            .ToDictionary(
                pair => FormattableString.Invariant($"market {pair.floor * 100:0}%, trade from the {(pair.trade == TradeInput.Ladder ? "ladder" : "swing trade")}"),
                pair => FilterSettings.Proposed with { BreadthFloor = pair.floor, Trade = pair.trade },
                StringComparer.Ordinal);

    sealed record Span(string Ticker, DateOnly? Joined, DateOnly? Left);

    sealed record Night(
        IReadOnlyDictionary<string, IReadOnlyList<FilterBand>> Bands,
        IReadOnlyDictionary<string, string> Trends,
        IReadOnlyDictionary<string, string> Plans);

    sealed record Row(GateInputs Inputs);

    sealed record SessionInputs(Breadth Breadth, IReadOnlyList<Row> Rows);

    // Every member's inputs as of the session: its readings from its bars up to it, the breadth over the
    // members read, its bands, trend and plan read where the store keeps them for the session and
    // replayed where it does not, its next print, whether it is suspect as the store holds it now, and its gap.
    static async Task<SessionInputs?> InputsAsync(
        SqliteConnection connection,
        DateOnly session,
        IReadOnlyList<string> members,
        IReadOnlyDictionary<string, List<ReplayBar>> bars,
        IReadOnlyDictionary<(string, DateOnly), Dictionary<string, double>> indicators,
        IReadOnlyDictionary<string, List<DateOnly>> prints,
        IReadOnlyDictionary<string, List<DateOnly>> events,
        IReadOnlySet<string> suspects,
        Night? stored,
        int earlierKept,
        CancellationToken cancellation)
    {
        var readings = new Dictionary<string, SwingReading>(StringComparer.Ordinal);
        var series = new Dictionary<string, List<ReplayBar>>(StringComparer.Ordinal);
        var gaps = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        var empty = new Dictionary<string, double>();

        foreach (var ticker in members)
        {
            if (!bars.TryGetValue(ticker, out var all))
            {
                continue;
            }

            var upTo = all.TakeWhile(bar => bar.SessionDate <= session).ToList();

            if (upTo.Count == 0 || upTo[^1].SessionDate != session)
            {
                continue;
            }

            var checkedGaps = TradingCalendar.Check([.. upTo.Select(bar => bar.SessionDate)]);

            if (checkedGaps.Checked && checkedGaps.HasGap)
            {
                gaps[ticker] = checkedGaps.Earliest!.Value;

                continue;
            }

            var own = indicators.GetValueOrDefault((ticker, session)) ?? empty;

            series[ticker] = upTo;
            readings[ticker] = SwingReadings.Of(
                [.. upTo.Select(bar => new ReadingBar(bar.SessionDate, bar.High, bar.Low, bar.Close, bar.Volume))],
                own.TryGetValue(IndicatorSeries.Atr14, out var atr) ? atr : null,
                own.TryGetValue(IndicatorSeries.VolAvg50, out var volume) ? volume : null);
        }

        if (readings.Count == 0)
        {
            return null;
        }

        var placesShort = SwingReadings.Places(readings.Where(pair => pair.Value.ReturnShort is not null).ToDictionary(pair => pair.Key, pair => pair.Value.ReturnShort!.Value, StringComparer.Ordinal));
        var placesLong = SwingReadings.Places(readings.Where(pair => pair.Value.ReturnLong is not null).ToDictionary(pair => pair.Key, pair => pair.Value.ReturnLong!.Value, StringComparer.Ordinal));

        var breadth = SwingReadings.BreadthOf(
            members.Count,
            [
                .. readings.Keys
                    .Where(ticker => indicators.GetValueOrDefault((ticker, session))?.ContainsKey(IndicatorSeries.Sma200) == true)
                    .Select(ticker => (series[ticker][^1].Close, indicators[(ticker, session)][IndicatorSeries.Sma200])),
            ]);

        var rows = new List<Row>();

        foreach (var ticker in members)
        {
            var own = indicators.GetValueOrDefault((ticker, session)) ?? empty;
            var held = series.GetValueOrDefault(ticker);
            var reading = readings.GetValueOrDefault(ticker);
            var next = prints.TryGetValue(ticker, out var dated) ? dated.FirstOrDefault(date => date >= session) : default;
            DateOnly? nextPrint = next == default ? null : next;

            IReadOnlyList<FilterBand> bands = [];
            string? trend = null;
            FirstTranche? plan = null;

            if (held is not null && stored is not null)
            {
                bands = stored.Bands.GetValueOrDefault(ticker) ?? [];
                trend = stored.Trends.GetValueOrDefault(ticker);
                plan = stored.Plans.TryGetValue(ticker, out var kept) ? ListedTranche.Of(kept) : null;
            }
            else if (held is not null)
            {
                var nextEvent = events.TryGetValue(ticker, out var any) ? any.FirstOrDefault(date => date >= session) : default;
                var replayed = await SessionReplay.ForAsync(connection, ticker, held, own, nextEvent == default ? null : nextEvent, cancellation);

                bands = [.. replayed.Bands.Select(band => new FilterBand(band.LowEdge, band.HighEdge, band.Role, band.Strength, band.HasNonAverageAnchor))];
                trend = replayed.Trend.State;
                plan = SessionReplay.FirstTrancheOf(replayed.Plan);
            }

            double? strength = placesShort.TryGetValue(ticker, out var one) && placesLong.TryGetValue(ticker, out var two) ? (one + two) / 2 : null;

            rows.Add(new Row(new GateInputs(
                ticker,
                breadth,
                trend,
                strength,
                reading,
                held is null ? (gaps.TryGetValue(ticker, out var gapAt) ? FormattableString.Invariant($"the stored series has a gap at {gapAt:yyyy-MM-dd}") : "no bar for the session") : null,
                held?[^1].Close,
                held is { Count: > 1 } ? held[^2].Close : null,
                held is { Count: > 1 } ? held[^2].High : null,
                held?[^1].Volume,
                own.TryGetValue(IndicatorSeries.VolAvg50, out var average) ? average : null,
                own.TryGetValue(IndicatorSeries.Atr14, out var move) ? move : null,
                bands,
                plan,
                nextPrint,
                nextPrint is { } date ? ExchangeClosures.SessionsUntil(session, date) : null,
                suspects.Contains(ticker),
                gaps.TryGetValue(ticker, out var gap) ? gap : null,
                held is { Count: > 1 } ? held[^2].SessionDate : null,
                null,
                held is null ? [] : EarlierOf([.. held.Select(bar => bar.SessionDate)], earlierKept))));
        }

        return new SessionInputs(breadth, rows);
    }

    // The sessions a name's arrival window reads before its session before, newest first, off its own
    // bars up to the session counted, the newest of them being that session.
    public static IReadOnlyList<SessionEvent> EarlierOf(IReadOnlyList<DateOnly> held, int kept) =>
        held.Count > 2 ? [.. held.SkipLast(2).TakeLast(kept).Reverse().Select(day => new SessionEvent(day, null))] : [];

    // A row with the trigger's event on each session before it that this run counted, read by the session
    // and the name, and none where the run counted no such session.
    public static GateInputs WithEvents(GateInputs row, IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<string, bool?>> kept)
    {
        bool? FiredOn(DateOnly? day) =>
            day is { } on && kept.TryGetValue(on, out var stored) && stored.TryGetValue(row.Ticker, out var fired) ? fired : null;

        return row with
        {
            TriggerFiredTheSessionBefore = FiredOn(row.SessionBefore),
            Earlier = [.. (row.Earlier ?? []).Select(earlier => earlier with { Fired = FiredOn(earlier.Session) })],
        };
    }

    // The counts as the operator reads them: one line per session per setting, then each setting's
    // medians over the stored nights and over the replayed sessions.
    public static string Report(IReadOnlyDictionary<string, IReadOnlyList<SessionCounts>> counts, CountNotes notes)
    {
        var report = new StringBuilder();

        report.AppendLine("Shape counts only: no outcome is read and nothing is written. Each funnel is members, then through market, trend and strength, setup, trigger, trade, then passing after the exclusions.");
        report.AppendLine("Series state is read as the store holds it now, the calendar as it stands now, and a name no longer a member counts only on the sessions it still holds bars for. The first counted session reads no trigger arrival, having no session before it counted.");

        if (notes.Unreadable > 0)
        {
            report.AppendLine(FormattableString.Invariant($"{notes.Unreadable} session(s) left out: fewer than half the members hold a 200-day average on them in the stored year, so neither breadth nor the trend classifier can be read. Counted from {notes.FirstReadable:yyyy-MM-dd}."));
        }

        if (notes.Compared > 0)
        {
            report.AppendLine(FormattableString.Invariant($"The replay over the nights the store keeps bands, trends and plans for, {notes.Compared} member-night(s): the same bands for {notes.SameBands}, the same trend for {notes.SameTrend}, the same first tranche and target for {notes.SamePlan}."));
        }

        foreach (var (name, sessions) in counts)
        {
            report.AppendLine();
            report.AppendLine(name);
            report.AppendLine("session     source    breadth  funnel (attrition)                               alone: mkt trd set trg tra   relaxed alone: mkt trd set trg tra   pullbacks breakouts");

            foreach (var one in sessions)
            {
                var attrition = string.Join(" ", one.Funnel.Select((count, at) => at == 0 ? count.ToString(CultureInfo.InvariantCulture) : FormattableString.Invariant($"{count}(-{one.Funnel[at - 1] - count})")));

                report.AppendLine(FormattableString.Invariant($"{one.Session:yyyy-MM-dd}  {one.Source,-8}  {(one.Breadth is { } share ? (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a"),7}  {attrition,-48} {string.Join(" ", one.Alone.Select(count => count.ToString(CultureInfo.InvariantCulture).PadLeft(4)))}   {string.Join(" ", one.RelaxedAlone.Select(count => count.ToString(CultureInfo.InvariantCulture).PadLeft(4)))}   {one.Pullbacks,9} {one.Breakouts,9}"));
            }

            foreach (var source in new[] { Stored, Replayed })
            {
                var within = sessions.Where(one => one.Source == source).ToArray();

                if (within.Length == 0)
                {
                    continue;
                }

                report.AppendLine(FormattableString.Invariant($"median over {within.Length} {source} session(s): funnel {string.Join(" ", Enumerable.Range(0, within[0].Funnel.Count).Select(at => Median(within.Select(one => one.Funnel[at]))))}; alone {string.Join(" ", Enumerable.Range(0, within[0].Alone.Count).Select(at => Median(within.Select(one => one.Alone[at]))))}; relaxed alone {string.Join(" ", Enumerable.Range(0, within[0].RelaxedAlone.Count).Select(at => Median(within.Select(one => one.RelaxedAlone[at]))))}"));
            }
        }

        return report.ToString();
    }

    static string Median(IEnumerable<int> values) =>
        (SwingReadings.Median([.. values.Select(value => value * 1.0)]) ?? 0).ToString("0.#", CultureInfo.InvariantCulture);

    static async Task<IReadOnlyList<Span>> SpansAsync(SqliteConnection connection, string indexCode, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker, joined, \"left\" FROM membership WHERE index_code = $index;";
        command.Parameters.AddWithValue("$index", indexCode);

        var spans = new List<Span>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            spans.Add(new Span(reader.GetString(0), reader.IsDBNull(1) ? null : Date(reader.GetString(1)), reader.IsDBNull(2) ? null : Date(reader.GetString(2))));
        }

        return spans;
    }

    static async Task<IReadOnlyDictionary<string, List<ReplayBar>>> BarsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker, session_date, high, low, close, volume FROM bar ORDER BY ticker, session_date;";

        var bars = new Dictionary<string, List<ReplayBar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!bars.TryGetValue(ticker, out var series))
            {
                bars[ticker] = series = [];
            }

            series.Add(new ReplayBar(
                Date(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.GetInt64(5)));
        }

        return bars;
    }

    static async Task<IReadOnlyDictionary<(string, DateOnly), Dictionary<string, double>>> IndicatorsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker, session_date, name, value FROM indicator WHERE value IS NOT NULL AND name IN ($a, $b, $c, $d, $e);";

        for (var at = 0; at < Needed.Length; at++)
        {
            command.Parameters.AddWithValue("$" + (char)('a' + at), Needed[at]);
        }

        var values = new Dictionary<(string, DateOnly), Dictionary<string, double>>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var key = (reader.GetString(0), Date(reader.GetString(1)));

            if (!values.TryGetValue(key, out var named))
            {
                values[key] = named = new Dictionary<string, double>(StringComparer.Ordinal);
            }

            named[reader.GetString(2)] = reader.GetDouble(3);
        }

        return values;
    }

    static async Task<IReadOnlyDictionary<string, List<DateOnly>>> EventsAsync(SqliteConnection connection, string where, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker, event_date FROM calendar " + where + " ORDER BY ticker, event_date;";

        var events = new Dictionary<string, List<DateOnly>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!events.TryGetValue(ticker, out var dates))
            {
                events[ticker] = dates = [];
            }

            dates.Add(Date(reader.GetString(1)));
        }

        return events;
    }

    static async Task<IReadOnlySet<string>> SuspectsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker FROM series_state WHERE state = 'suspect';";

        var suspects = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            suspects.Add(reader.GetString(0));
        }

        return suspects;
    }

    // The nights the store keeps a band set, a trend and a plan for across the index: at least half as
    // many ladder rows as the biggest night holds, so a night a few stale names left rows on is not one.
    static async Task<IReadOnlySet<DateOnly>> StoredNightsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT as_of FROM ladder GROUP BY as_of
            HAVING COUNT(*) * 2 >= (SELECT MAX(held) FROM (SELECT COUNT(*) AS held FROM ladder GROUP BY as_of))
               AND as_of IN (SELECT DISTINCT session_date FROM listing)
               AND as_of IN (SELECT DISTINCT as_of FROM level);";

        var nights = new HashSet<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            nights.Add(Date(reader.GetString(0)));
        }

        return nights;
    }

    static async Task<Night> StoredAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var bands = new Dictionary<string, List<FilterBand>>(StringComparer.Ordinal);
        var trends = new Dictionary<string, string>(StringComparer.Ordinal);
        var plans = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ticker, low_edge, high_edge, role, strength, has_non_average_anchor FROM level WHERE as_of = $day;";
            command.Parameters.AddWithValue("$day", day);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var ticker = reader.GetString(0);

                if (!bands.TryGetValue(ticker, out var held))
                {
                    bands[ticker] = held = [];
                }

                held.Add(new FilterBand(Money.FromStorage(reader.GetString(1)), Money.FromStorage(reader.GetString(2)), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) == 1));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ticker, trend_state FROM ladder WHERE as_of = $day;";
            command.Parameters.AddWithValue("$day", day);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                trends[reader.GetString(0)] = reader.GetString(1);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ticker, plan_at_listing FROM listing WHERE session_date = $day AND plan_at_listing IS NOT NULL;";
            command.Parameters.AddWithValue("$day", day);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                plans[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return new Night(
            bands.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FilterBand>)pair.Value, StringComparer.Ordinal),
            trends,
            plans);
    }

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
