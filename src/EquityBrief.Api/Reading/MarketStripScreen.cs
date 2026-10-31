using System.Globalization;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quotes;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The market strip as a page draws it, worked from what the night stored and, while the regular session is open, the
// quotes Yahoo answered. Each tile's level is its close on the night and its change on the day its close against the
// session's before; in the session a tile Yahoo answered draws Yahoo's price against its own previous close, and one it
// did not draws the close with the words saying so. The VIX's words read its close against the median of its closes over
// the 252 sessions before, as the rules read it; the yields' changes are in points. Nothing here is stored.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public static class MarketStripScreen
{
    // The page's own breadth with the floor its market check reads, or none where it reads several.
    public sealed record Breadth(string Index, double? Share, double? Floor);

    public static MarketStripView View(StripStored stored, Breadth breadth, DateOnly night, IReadOnlyDictionary<string, SparkQuote>? quotes, DateTimeOffset? asked, TimeZoneInfo zone)
    {
        var inSession = asked is not null;
        var tiles = new List<StripTileView>();

        foreach (var (key, label, series, yahoo) in MarketStrip.Tiles)
        {
            if (key == MarketStrip.Breadth)
            {
                tiles.Add(new StripTileView(
                    key,
                    "Above 200-day",
                    breadth.Share is { } share ? (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : null,
                    null,
                    $"of {breadth.Index} stocks above their 200-day average",
                    breadth.Floor is { } floor
                        ? FormattableString.Invariant($"setups buy at {floor * 100:0.##}% or more")
                        : "each setup on this index reads its own floor",
                    false,
                    false));

                continue;
            }

            if (key is MarketStrip.TenYear or MarketStrip.ThreeMonth)
            {
                var yields = stored.Yields;
                var (now, then) = key == MarketStrip.TenYear ? (yields?.TenYear, yields?.TenYearBefore) : (yields?.ThreeMonth, yields?.ThreeMonthBefore);

                tiles.Add(new StripTileView(
                    key,
                    label,
                    now is { } held ? held.ToString("0.00", CultureInfo.InvariantCulture) + "%" : null,
                    now is { } a && then is { } b ? a - b : null,
                    null,
                    null,
                    false,
                    inSession && now is not null,
                    Points: true));

                continue;
            }

            var close = series is null ? null : stored.Closes.GetValueOrDefault(series);
            var quote = yahoo is null ? null : quotes?.GetValueOrDefault(yahoo);
            decimal? level = quote?.Price ?? close?.Close;
            double? change = quote is { PreviousClose: > 0 } quoted
                ? Statistic.FromRatio((quoted.Price - quoted.PreviousClose) / quoted.PreviousClose) * 100
                : close is { Before: > 0 } held2 ? Statistic.FromRatio((held2.Close - held2.Before!.Value) / held2.Before.Value) * 100 : null;
            var vixWords = key == MarketStrip.Vix && level is { } vixLevel && Median(stored.VixYear) is { } median
                ? vixLevel > median ? "above its 1-year median" : "below its 1-year median"
                : null;

            tiles.Add(new StripTileView(
                key,
                label,
                level is { } shown ? Level(shown) : null,
                change,
                vixWords,
                null,
                quote is not null,
                inSession && quote is null && close is not null));
        }

        var line = inSession && quotes is { Count: > 0 }
            ? "Delayed prices, " + TimeZoneInfo.ConvertTime(asked!.Value, zone).ToString("h:mm tt", CultureInfo.InvariantCulture) + " ET"
            : "Prices as of the close of " + night.ToString("d MMM", CultureInfo.InvariantCulture);

        return new MarketStripView(Summary(tiles), line, inSession && quotes is { Count: > 0 }, tiles);
    }

    // The strip's sentence: how the S&P 500 moved and where the VIX stands against its year.
    static string Summary(IReadOnlyList<StripTileView> tiles)
    {
        var index = tiles.First(tile => tile.Key == MarketStrip.Tiles[0].Key);
        var vix = tiles.First(tile => tile.Key == MarketStrip.Vix);

        if (index.Change is not { } moved)
        {
            return "No close of the S&P 500 is stored for this night yet.";
        }

        var said = Math.Round(moved, 2) switch
        {
            > 0 => FormattableString.Invariant($"The S&P 500 rose {moved:0.0}% on the day"),
            < 0 => FormattableString.Invariant($"The S&P 500 fell {-moved:0.0}% on the day"),
            _ => "The S&P 500 was flat on the day",
        };

        return said + (vix.Words is { } words ? "; the VIX is " + words + "." : ".");
    }

    // The median of the closes, the mean of the middle two where they are even, none where there are none.
    public static decimal? Median(IReadOnlyList<decimal> closes)
    {
        if (closes.Count == 0)
        {
            return null;
        }

        var sorted = closes.Order().ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    static string Level(decimal level) => level.ToString("#,##0.00", CultureInfo.InvariantCulture);

    // The strip's card for a page on an index's night: the S&P 500's breadth with the floor the night's swing filter read,
    // and an S&P 400's or 600's with none, since each of its setups reads its own.
    public static async Task<string> DrawAsync(ReadApi read, MarkRenderer marks, EquityBrief.Web.App.UniverseChoice reading, DateOnly night, IReadOnlyDictionary<string, SparkQuote>? quotes, DateTimeOffset? asked, TimeZoneInfo zone)
    {
        var stored = await read.StripAsync(night, Series, VixSessions);
        Breadth breadth;

        if (reading.Code == EquityBrief.Web.App.Universes.Large.Code)
        {
            var funnel = RunScreen.Funnel(await read.GateResultsAsync(night), night, await read.ListRuleAsync(night));
            var settings = funnel is { } ran ? await read.FilterSettingsAsync(ran.Version) : null;

            breadth = new Breadth(reading.Name, (await read.MarketReadingAsync(night))?.Breadth, settings is null ? null : EquityBrief.Core.Filter.FilterSettings.Read(settings).BreadthFloor);
        }
        else
        {
            breadth = new Breadth(reading.Name, (await read.IndexNightAsync(reading.Code, night))?.Breadth, null);
        }

        return EquityBrief.Web.App.Cards.Computed(
            "Markets",
            marks.MarketStrip(View(stored, breadth, night, quotes, asked, zone)),
            title: "The markets",
            region: MarkRenderer.StripRegion);
    }

    // The series the strip's tiles read from the store.
    public static IReadOnlyList<string> Series { get; } = [.. MarketStrip.Tiles.Select(tile => tile.Series).OfType<string>()];

    public static int VixSessions => EquityBrief.Core.Ledger.LedgerReadings.VixMedianSessions;
}
