using EquityBrief.Core.Families;

namespace EquityBrief.Core.Quotes;

// The market strip atop Tonight on every index and atop every page the copy holds: one row of tiles in this order, each
// read from the night's own store and, in the regular session, from Yahoo's delayed quote of its symbol, asked once for
// every tile and kept five minutes. The four series the night gains for it, QQQ's, DIA's and the dollar index's of the
// provider and WTI crude's of Yahoo, since the provider files no price of crude, are read by no rule, list or gate.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public static class MarketStrip
{
    public const string Nasdaq = "QQQ";

    public const string Dow = "DIA";

    public const string Dollar = "DXY";

    public const string Oil = "WTI";

    // The series the night asks the provider for beside its own, and the one it asks Yahoo for.
    public static IReadOnlyList<string> ProviderSeries { get; } = [Nasdaq, Dow, Dollar];

    public static IReadOnlyList<string> YahooSeries { get; } = [Oil];

    // How long the read surface keeps Yahoo's answer before it asks again.
    public const int CacheMinutes = 5;

    public const string Breadth = "above-average";

    public const string TenYear = "ten-year";

    public const string ThreeMonth = "three-month";

    public const string Vix = "vix";

    // Each tile: its key, its label, the series the store keeps its closes under, none for the breadth and the yields, which
    // the night stores elsewhere, and the symbol Yahoo answers it under, none where Yahoo answers nothing for it.
    public static IReadOnlyList<(string Key, string Label, string? Series, string? Yahoo)> Tiles { get; } =
    [
        ("index", "S&P 500", MarketCloses.Index, "^GSPC"),
        ("mid", "S&P 400 (IJH)", "IJH", "IJH"),
        ("small", "S&P 600 (IJR)", "IJR", "IJR"),
        ("nasdaq", "QQQ", Nasdaq, "QQQ"),
        ("dow", "DIA", Dow, "DIA"),
        (Breadth, "Breadth", null, null),
        (Vix, "VIX", MarketCloses.Vix, "^VIX"),
        (TenYear, "US 10Y", null, null),
        (ThreeMonth, "US 3M", null, null),
        ("credit", "Credit (HYG)", "HYG", "HYG"),
        ("oil", "Oil (WTI)", Oil, "CL=F"),
        ("dollar", "Dollar", Dollar, "DX-Y.NYB"),
    ];

    // The symbols Yahoo is asked for, one request for all of them.
    public static IReadOnlyList<string> YahooSymbols { get; } = [.. Tiles.Select(tile => tile.Yahoo).OfType<string>()];
}
