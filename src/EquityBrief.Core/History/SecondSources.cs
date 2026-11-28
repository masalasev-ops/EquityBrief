using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// One session as a second source states it: its prices as the source sends them, read from the text of each number, and
// the close it adjusts for later splits and dividends where it states one.
public sealed record SourceBar(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, long Volume, decimal? Adjusted);

// A session kept from a second source, at the store's scale: its prices the source's times the one ratio its closes hold
// to the store's, its close as traded beside them and its volume in the store's proportion.
public sealed record KeptBar(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, decimal RawClose, long Volume);

// What a second source's answer gives a hole: the sessions kept with the ratio and the volume's proportion they were
// scaled by, or none kept and why.
public sealed record FillJudgement(IReadOnlyList<KeptBar> Kept, decimal? Ratio, decimal? VolumeRatio, string Why);

// The holes inside a code's own prices filled from real traded prices a second source holds, never interpolated: Yahoo's
// daily chart, then Stooq's daily file, then Tiingo's daily prices, a source's sessions kept only where its closes hold one
// ratio to the store's over the sessions the store holds on each side of the hole and its volumes are equal or in one
// proportion.
// see: A hole inside a code's own prices is filled from a second source's traded prices where they hold one ratio to the store's on each side, never interpolated
public static class SecondSources
{
    public const string Yahoo = "yahoo";

    public const string Stooq = "stooq";

    public const string Tiingo = "tiingo";

    // The order the sources are asked in, the first holding a hole's sessions filling it.
    public static readonly string[] Order = [Yahoo, Stooq, Tiingo];

    // The sessions the store holds on each side of a hole that a source's closes are held to.
    public const int Side = 20;

    // How far apart the ratios of the closes, and the proportions of the volumes, may stand and still be one: 0.1%.
    public const decimal Band = 0.001m;

    // The calendar days asked on each side of a code's holes, wide enough to hold its twenty sessions on each side.
    public const int Margin = 45;

    static readonly Regex Suffix = new(@"_old\d*$", RegexOptions.Compiled);

    // The symbol a source files a code under: the store's code with its own suffix for an earlier holder taken off, a
    // class's dot written as a dash, upper case at Yahoo, lower case at Tiingo and with the market's at Stooq.
    public static string Symbol(string code, string source)
    {
        var plain = Suffix.Replace(code, string.Empty).Replace('.', '-');

        return source switch
        {
            Yahoo => plain.ToUpperInvariant(),
            Stooq => plain.ToLowerInvariant() + ".us",
            Tiingo => plain.ToLowerInvariant(),
            _ => throw new ArgumentException($"No second source is named {source}.", nameof(source)),
        };
    }

    // A code's member sessions with no price that fall inside its own prices, after its first priced session and before its
    // last; the sessions past its last price are a span's end or a gap, not a hole.
    public static IReadOnlyList<DateOnly> Holes(IReadOnlyList<SweepBar> stored, IReadOnlyList<DateOnly> missing)
    {
        if (stored.Count == 0)
        {
            return [];
        }

        var (first, last) = (stored[0].Session, stored[^1].Session);

        return [.. missing.Where(session => session > first && session < last).Order()];
    }

    // Sessions cut into runs back to back on the calendar, each run a hole judged on its own.
    public static IReadOnlyList<IReadOnlyList<DateOnly>> Runs(IReadOnlyList<DateOnly> sessions, IReadOnlyList<DateOnly> calendar)
    {
        var at = calendar.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);
        var runs = new List<IReadOnlyList<DateOnly>>();
        var run = new List<DateOnly>();

        foreach (var session in sessions.Order())
        {
            if (run.Count > 0 && (!at.TryGetValue(run[^1], out var before) || !at.TryGetValue(session, out var now) || now != before + 1))
            {
                runs.Add(run);
                run = [];
            }

            run.Add(session);
        }

        if (run.Count > 0)
        {
            runs.Add(run);
        }

        return runs;
    }

    // Whether a source's answer fills a hole: it must hold every session of the hole and every one of the store's sessions
    // on each side, up to twenty a side and at least one, its closes over the store's one ratio within 0.1% and its volumes
    // equal or in one proportion within the same; then each session is kept at the source's prices times that ratio, every
    // kept bar one that could have traded. A source's close adjusted for later splits and dividends is the one held to the
    // store's, its other prices brought to that adjustment first; its own close as traded is kept where it holds one ratio
    // to the store's traded closes, and otherwise the store's adjustment on the first session after the hole is taken off.
    public static FillJudgement Judge(IReadOnlyList<SweepBar> stored, IReadOnlyList<DateOnly> hole, IReadOnlyList<SourceBar> source)
    {
        if (hole.Count == 0)
        {
            return new FillJudgement([], null, null, "no hole");
        }

        var (first, last) = (hole.Min(), hole.Max());
        var before = stored.Where(bar => bar.Session < first).OrderBy(bar => bar.Session).TakeLast(Side).ToArray();
        var after = stored.Where(bar => bar.Session > last).OrderBy(bar => bar.Session).Take(Side).ToArray();

        if (before.Length == 0 || after.Length == 0)
        {
            return new FillJudgement([], null, null, "the store holds no session on one side of the hole");
        }

        var answered = source.GroupBy(bar => bar.Session).ToDictionary(group => group.Key, group => group.First());
        var held = hole.Count(answered.ContainsKey);

        if (held < hole.Count)
        {
            return new FillJudgement([], null, null, FormattableString.Invariant($"holds {held} of the hole's {hole.Count} session(s)"));
        }

        var window = before.Concat(after).ToArray();
        var around = window.Count(bar => answered.ContainsKey(bar.Session));

        if (around < window.Length)
        {
            return new FillJudgement([], null, null, FormattableString.Invariant($"holds {around} of the store's {window.Length} session(s) around the hole"));
        }

        if (window.Any(bar => bar.Close <= 0 || Adjusted(answered[bar.Session]) <= 0))
        {
            return new FillJudgement([], null, null, "a close around the hole is nought");
        }

        var ratios = window.Select(bar => bar.Close / Adjusted(answered[bar.Session])).ToArray();

        if (!One(ratios))
        {
            return new FillJudgement([], null, null, FormattableString.Invariant($"its closes stand {Spread(ratios) * 100:0.##}% apart over the store's, past the {Band * 100:0.#}% that makes one ratio"));
        }

        var volumes = new List<decimal>();

        foreach (var bar in window)
        {
            var theirs = answered[bar.Session].Volume;

            if (bar.Volume == 0 && theirs == 0)
            {
                continue;
            }

            if (bar.Volume == 0 || theirs == 0)
            {
                return new FillJudgement([], null, null, FormattableString.Invariant($"its volume on {bar.Session:yyyy-MM-dd} is {theirs} against the store's {bar.Volume}"));
            }

            volumes.Add(Shares(bar.Volume) / theirs);
        }

        if (volumes.Count > 0 && !One(volumes))
        {
            return new FillJudgement([], null, null, FormattableString.Invariant($"its volumes stand {Spread(volumes) * 100:0.##}% apart in proportion to the store's, past the {Band * 100:0.#}% that makes one"));
        }

        var ratio = decimal.Round(ratios.Average(), 10);
        var volumeRatio = volumes.Count == 0 ? 1m : decimal.Round(volumes.Average(), 10);
        var raws = window.Where(bar => bar.RawClose > 0).Select(bar => bar.RawClose / answered[bar.Session].Close).ToArray();
        decimal? rawRatio = raws.Length == window.Length && One(raws) ? decimal.Round(raws.Average(), 10) : null;
        var factor = after[0].RawClose > 0 ? after[0].RawClose / after[0].Close : 1m;
        var kept = new List<KeptBar>();

        foreach (var session in hole.Order())
        {
            var bar = answered[session];

            if (bar.Close <= 0)
            {
                return new FillJudgement([], ratio, volumeRatio, FormattableString.Invariant($"its close on {session:yyyy-MM-dd} is nought"));
            }

            var own = Adjusted(bar) / bar.Close * ratio;
            var close = PriceForm.Round(Adjusted(bar) * ratio);
            var one = new KeptBar(
                session,
                PriceForm.Round(bar.Open * own),
                PriceForm.Round(bar.High * own),
                PriceForm.Round(bar.Low * own),
                close,
                PriceForm.Round(rawRatio is { } traded ? bar.Close * traded : close * factor),
                (long)decimal.Round(bar.Volume * volumeRatio, 0, MidpointRounding.ToEven));

            if (one.Low <= 0 || one.Low > Math.Min(one.Open, one.Close) || one.High < Math.Max(one.Open, one.Close))
            {
                return new FillJudgement([], ratio, volumeRatio, FormattableString.Invariant($"its bar on {session:yyyy-MM-dd}, low {one.Low}, open {one.Open}, high {one.High}, close {one.Close}, could not have traded"));
            }

            kept.Add(one);
        }

        return new FillJudgement(kept, ratio, volumeRatio, FormattableString.Invariant($"kept at {ratio} the store's over {window.Length} session(s) around the hole"));
    }

    // What a kept row is: a session the store holds no price for, or one whose stored price a source corrected.
    public const string HoleKind = "hole";

    public const string CorrectionKind = "correction";

    // A code's series with its kept sessions read in: a hole's only where the series holds no price for the session, a
    // correction's in place of the stored one; the newest row of a session standing where it was kept twice.
    public static SweepBar[] Read(IReadOnlyList<SweepBar> series, IReadOnlyList<(KeptBar Bar, string Kind)> kept)
    {
        if (kept.Count == 0)
        {
            return [.. series];
        }

        var bySession = series.ToDictionary(bar => bar.Session);
        var stored = bySession.Keys.ToHashSet();

        foreach (var (bar, kind) in kept)
        {
            if (kind == CorrectionKind || (kind == HoleKind && !stored.Contains(bar.Session)))
            {
                bySession[bar.Session] = new SweepBar(bar.Session, bar.High, bar.Low, bar.Close, bar.Volume, bar.Open, bar.RawClose);
            }
        }

        return [.. bySession.Values.OrderBy(bar => bar.Session)];
    }

    static decimal Adjusted(SourceBar bar) => bar.Adjusted ?? bar.Close;

    // A count of shares as a decimal, which holds every count exactly.
    static decimal Shares(long count) => count;

    static bool One(IReadOnlyList<decimal> values) => Spread(values) <= Band;

    static decimal Spread(IReadOnlyList<decimal> values) => values.Min() <= 0 ? decimal.MaxValue : (values.Max() - values.Min()) / values.Min();

    // Yahoo's daily chart as it sends it: one result, its sessions' instants under `timestamp`, each price under
    // `indicators.quote[0]` adjusted for splits alone, and the close adjusted for splits and dividends under
    // `indicators.adjclose[0]`; a session it holds no price for written null and left out.
    public static IReadOnlyList<SourceBar> YahooChart(string json)
    {
        using var document = JsonDocument.Parse(json);

        var chart = document.RootElement.GetProperty("chart");

        if (chart.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null)
        {
            return [];
        }

        if (!chart.TryGetProperty("result", out var results) || results.ValueKind is not JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return [];
        }

        var result = results[0];

        if (!result.TryGetProperty("timestamp", out var instants) || instants.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var offset = result.GetProperty("meta").TryGetProperty("gmtoffset", out var gmt) && gmt.ValueKind is JsonValueKind.Number ? gmt.GetInt64() : 0;
        var indicators = result.GetProperty("indicators");
        var quote = indicators.GetProperty("quote")[0];
        JsonElement? adjusted = indicators.TryGetProperty("adjclose", out var adj) && adj.ValueKind is JsonValueKind.Array && adj.GetArrayLength() > 0
            && adj[0].TryGetProperty("adjclose", out var closes) ? closes : null;
        var bars = new List<SourceBar>();

        for (var at = 0; at < instants.GetArrayLength(); at++)
        {
            var (open, high, low, close) = (Number(quote, "open", at), Number(quote, "high", at), Number(quote, "low", at), Number(quote, "close", at));
            var volume = quote.GetProperty("volume")[at];

            if (open is null || high is null || low is null || close is null || volume.ValueKind is not JsonValueKind.Number)
            {
                continue;
            }

            var session = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(instants[at].GetInt64() + offset).UtcDateTime);
            decimal? scaled = adjusted is { } list && at < list.GetArrayLength() && list[at].ValueKind is JsonValueKind.Number ? list[at].GetDecimal() : null;

            bars.Add(new SourceBar(session, open.Value, high.Value, low.Value, close.Value, volume.GetInt64(), scaled));
        }

        return bars;
    }

    static decimal? Number(JsonElement quote, string name, int at) =>
        quote.TryGetProperty(name, out var values) && at < values.GetArrayLength() && values[at].ValueKind is JsonValueKind.Number ? values[at].GetDecimal() : null;

    // Stooq's daily file as it sends it: a header naming Date, Open, High, Low, Close and Volume, then a session a line, its
    // prices adjusted as Stooq adjusts them; an answer that is no such file, "No data" or a limit's words, holds none.
    public static IReadOnlyList<SourceBar> StooqDaily(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0 || !lines[0].StartsWith("Date,Open,High,Low,Close", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var bars = new List<SourceBar>();

        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');

            if (cells.Length < 5
                || !DateOnly.TryParseExact(cells[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session)
                || !decimal.TryParse(cells[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var open)
                || !decimal.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var high)
                || !decimal.TryParse(cells[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var low)
                || !decimal.TryParse(cells[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var close))
            {
                continue;
            }

            var volume = cells.Length > 5 && decimal.TryParse(cells[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var traded)
                ? (long)decimal.Round(traded, 0, MidpointRounding.ToEven)
                : 0L;

            bars.Add(new SourceBar(session, open, high, low, close, volume, null));
        }

        return bars;
    }

    // Tiingo's daily prices as it sends them: a list of sessions, each dated by `date`, its open, high, low, close and volume
    // as traded, and its close adjusted for splits and dividends under `adjClose`; anything else holds none.
    public static IReadOnlyList<SourceBar> TiingoPrices(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var bars = new List<SourceBar>();

        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("date", out var date) || date.GetString() is not { Length: >= 10 } stamp
                || !DateOnly.TryParseExact(stamp[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session)
                || Field(row, "open") is not { } open || Field(row, "high") is not { } high || Field(row, "low") is not { } low
                || Field(row, "close") is not { } close || Field(row, "volume") is not { } volume)
            {
                continue;
            }

            bars.Add(new SourceBar(session, open, high, low, close, (long)decimal.Round(volume, 0, MidpointRounding.ToEven), Field(row, "adjClose")));
        }

        return bars;
    }

    static decimal? Field(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number ? value.GetDecimal() : null;

    // Tiingo's meta of a symbol as it sends it: the first and last session it holds prices for, under `startDate` and
    // `endDate`; none where it holds the symbol under neither.
    public static (DateOnly First, DateOnly Last)? TiingoMeta(string json)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        return root.ValueKind is JsonValueKind.Object
            && Day(root, "startDate") is { } first
            && Day(root, "endDate") is { } last
                ? (first, last)
                : null;
    }

    static DateOnly? Day(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String && value.GetString() is { Length: >= 10 } text
            && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : null;
}

// Tiingo's limits on the operator's key, as the operator stated them: 50 requests an hour, 1,000 a day and 500 codes a
// month. Each ask is counted off those the store records, so a run stopped and started again keeps within them.
// see: A hole inside a code's own prices is filled from a second source's traded prices where they hold one ratio to the store's on each side, never interpolated
public static class TiingoAllowance
{
    public const int PerHour = 50;

    public const int PerDay = 1000;

    public const int CodesPerMonth = 500;

    // How long to wait before the next request for a code: none where it may go now, the time to the hour's or the day's
    // first free request where one of those is spent, and null where the month's codes are spent and this code is not among
    // them, so it waits for the next month. The day and the month are UTC's.
    public static TimeSpan? Wait(IReadOnlyList<(DateTimeOffset At, string Ticker)> asks, DateTimeOffset now, string ticker)
    {
        var month = asks.Where(ask => ask.At.UtcDateTime.Year == now.UtcDateTime.Year && ask.At.UtcDateTime.Month == now.UtcDateTime.Month).ToArray();

        if (!month.Any(ask => string.Equals(ask.Ticker, ticker, StringComparison.Ordinal)) && month.Select(ask => ask.Ticker).Distinct(StringComparer.Ordinal).Count() >= CodesPerMonth)
        {
            return null;
        }

        if (month.Count(ask => ask.At.UtcDateTime.Date == now.UtcDateTime.Date) >= PerDay)
        {
            return now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime;
        }

        var hour = asks.Where(ask => ask.At > now.AddHours(-1) && ask.At <= now).OrderBy(ask => ask.At).ToArray();

        return hour.Length >= PerHour ? hour[hour.Length - PerHour].At.AddHours(1) - now : TimeSpan.Zero;
    }
}
