using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Volume;
using EquityBrief.Data.Swings;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Levels;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

// One session's bar as the replay holds it.
public readonly record struct ReplayBar(DateOnly SessionDate, decimal High, decimal Low, decimal Close, long Volume);

// What one name's bands, trend and plan were as of an earlier session, recomputed through the same
// public functions the level builder, the trend classifier and the ladder builder call, over the bars
// and the indicators the store holds as of that session and the swings it holds confirmed by then.
// The shape counts read it for the sessions the store keeps no bands or plans for.
//
// Read-only: it opens nothing and writes nothing, and every query it makes goes through the connection
// its caller opened.
public static class SessionReplay
{
    public sealed record AsOf(IReadOnlyList<Level> Bands, Trend Trend, Ladder Plan);

    // The bands, trend and plan as of the session, the series given being the name's bars up to and
    // including it and the indicators its own values on that session.
    public static async Task<AsOf> ForAsync(
        SqliteConnection connection,
        string ticker,
        IReadOnlyList<ReplayBar> series,
        IReadOnlyDictionary<string, double> indicators,
        DateOnly? nextEvent,
        CancellationToken cancellation)
    {
        var session = series[^1].SessionDate;
        var close = series[^1].Close;
        var bands = Bands(connection, ticker, series, indicators);
        var trend = await TrendClassifier.ForAsync(connection, ticker, session, close, cancellation);

        if (!trend.Classified)
        {
            return new AsOf(bands, trend, new Ladder([], [], null, trend.Reason, []));
        }

        if (bands.Count == 0)
        {
            return new AsOf(bands, trend, new Ladder([], [], null, "no bands are stored for this name", []));
        }

        if (!indicators.TryGetValue(IndicatorSeries.Atr14, out var atr))
        {
            return new AsOf(bands, trend, new Ladder([], [], null, "no typical daily move is stored for this name", []));
        }

        var recent = series
            .Skip(Math.Max(0, series.Count - LadderSeries.ConditionLookback))
            .Select(bar => new LadderBar(bar.SessionDate, bar.High, bar.Low, bar.Close))
            .ToArray();

        var lows = StoredSwings.AsOf(connection, ticker, session)
            .Where(swing => swing.Direction == SwingSeries.Low)
            .OrderBy(swing => swing.SessionDate)
            .Select(swing => swing.Price)
            .ToArray();

        return new AsOf(bands, trend, LadderSeries.For(bands, close, Statistic.ToPrice(atr), recent, trend.State, lows, nextEvent));
    }

    // The level builder's band set as of the session: the swings confirmed by it inside the window, the
    // three averages, the volume shelves over the window and the retracement between the window's last
    // swing high and low, merged at half the typical move. None where the window is short of sixty
    // sessions or the session holds no typical move, as the level builder skips such a name.
    static IReadOnlyList<Level> Bands(
        SqliteConnection connection,
        string ticker,
        IReadOnlyList<ReplayBar> series,
        IReadOnlyDictionary<string, double> indicators)
    {
        if (series.Count < VolumeProfileSeries.Window || !indicators.TryGetValue(IndicatorSeries.Atr14, out var atr))
        {
            return [];
        }

        var session = series[^1].SessionDate;
        var window = series
            .Skip(series.Count - VolumeProfileSeries.Window)
            .Select(bar => new LevelBar(bar.SessionDate, bar.High, bar.Low, bar.Close))
            .ToArray();

        var swings = StoredSwings.AsOf(connection, ticker, session)
            .Where(swing => swing.SessionDate >= window[0].SessionDate)
            .Select(swing => new LevelMember(MemberSource.Swing, $"swing {swing.Direction}", swing.Price, swing.SessionDate))
            .ToArray();

        var averages = new[] { IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200 }
            .Where(indicators.ContainsKey)
            .Select(name => new LevelMember(MemberSource.Average, name, Statistic.ToPrice(indicators[name]), session));

        var shelves = VolumeProfileSeries.For([.. series.Select(bar => new ProfileBar(bar.SessionDate, bar.High, bar.Low, bar.Volume))])
            .Where(band => band.ShareOfPeriod >= LevelBuilder.ShelfThreshold)
            .Select(band => new LevelMember(MemberSource.Shelf, "shelf", PriceForm.Round((band.Low + band.High) / 2, Statistic.Places), session))
            .OrderBy(shelf => shelf.Price);

        var candidates = new List<LevelMember>();

        candidates.AddRange(swings);
        candidates.AddRange(averages);
        candidates.AddRange(shelves);
        candidates.AddRange(LevelSeries.RetracementsBetween(Last(swings, SwingSeries.High), Last(swings, SwingSeries.Low)));

        return LevelSeries.For(window, candidates, window[^1].Close, Statistic.ToPrice(atr) * LevelSeries.MergeDistanceInTypicalMoves, session);
    }

    static (decimal Price, DateOnly Date)? Last(IReadOnlyList<LevelMember> swings, string direction)
    {
        var last = swings
            .Where(member => member.Kind == $"swing {direction}")
            .OrderBy(member => member.Date)
            .LastOrDefault();

        return last.Kind is null ? null : (last.Price, last.Date);
    }

    // The first tranche and the first traded target of a plan, as the listing keeps them, read as the
    // trade gate reads them.
    public static FirstTranche FirstTrancheOf(Ladder plan)
    {
        var arithmetic = LadderSeries.ArithmeticFor(plan);
        var first = plan.Tranches.Count > 0 ? plan.Tranches[0] : null;
        var target = plan.Exits.FirstOrDefault(exit => exit.Traded)?.LowEdge;

        return new FirstTranche(
            first is null ? null : LadderSeries.Midpoint(first),
            first?.Stop,
            target,
            arithmetic.FirstRewardToRisk is { } ratio ? Math.Round(ratio, PriceForm.Places, MidpointRounding.AwayFromZero) : null,
            arithmetic.Absent);
    }
}
