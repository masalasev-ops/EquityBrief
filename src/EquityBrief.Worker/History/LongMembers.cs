using System.Globalization;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;

namespace EquityBrief.Worker.History;

// One quarter end's figures of one fund's matching: its holdings and their value, and those each key matched with their value,
// the rest matched to none.
public sealed record LongQuarter(string Index, DateOnly Period, int Holdings, decimal Value, IReadOnlyDictionary<string, (int Count, decimal Value)> ByKey)
{
    public int Matched => ByKey.Values.Sum(one => one.Count);

    public decimal MatchedValue => ByKey.Values.Sum(one => one.Value);
}

// The long history's membership read off a capture's folder: each fund's complete schedules, the provider's day files at each
// quarter end and its symbol lists, each holding matched by the keys the matching reads.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
public static class LongMembers
{
    public sealed record Matched(IReadOnlyList<LongSchedule> Schedules, IReadOnlyDictionary<string, IReadOnlyDictionary<(DateOnly Period, int Place), LongMatch>> Matches, IReadOnlyList<string> Unread);

    // Each quarter end's day file read, at the last session on or before it within the days the capture looked in.
    public static IReadOnlyDictionary<DateOnly, QuarterCloses> DayFiles(string folder)
    {
        var days = Path.Combine(folder, LongDayFiles.DaysFolder);
        var kept = Directory.Exists(days)
            ? Directory.GetFiles(days, "*.json").Select(file => (Session: DateOnly.ParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture), File: file)).ToArray()
            : [];
        var read = new Dictionary<DateOnly, QuarterCloses>();

        foreach (var quarter in LongDayFiles.Quarters)
        {
            if (kept.Where(one => one.Session <= quarter && one.Session > quarter.AddDays(-LongDayFiles.SessionDays)).OrderByDescending(one => one.Session).FirstOrDefault() is { File: not null } day)
            {
                read[quarter] = new QuarterCloses(LongDayFiles.ClosesOf(File.ReadAllText(day.File)));
            }
        }

        return read;
    }

    public const string AskedFolder = "asked";

    // The provider's listed and delisted symbols as the capture kept them.
    public static IReadOnlyList<ListedSymbol> Symbols(string folder) =>
    [
        .. ProviderSymbols.Parse(File.ReadAllText(Path.Combine(folder, LongDayFiles.ListedFile)), false),
        .. ProviderSymbols.Parse(File.ReadAllText(Path.Combine(folder, LongDayFiles.DelistedFile)), true),
    ];

    // Each code's close at each quarter end off its own prices as the provider answered an ask, the close of its last session in
    // the days to the quarter end; none where no ask was answered or it holds no such session.
    public static Func<DateOnly, string, decimal?> Asked(string folder)
    {
        var asked = Path.Combine(folder, AskedFolder);
        var read = new Dictionary<string, SortedDictionary<DateOnly, decimal>>(StringComparer.Ordinal);

        foreach (var file in Directory.Exists(asked) ? Directory.GetFiles(asked, "*.json") : [])
        {
            read[Path.GetFileNameWithoutExtension(file)] = PricesOf(File.ReadAllText(file));
        }

        return (period, code) => read.TryGetValue(code, out var prices)
            ? prices.Where(pair => pair.Key <= period && pair.Key > period.AddDays(-LongDayFiles.SessionDays)).Select(pair => (decimal?)pair.Value).LastOrDefault()
            : null;
    }

    // A code's daily closes as the provider answered them.
    public static SortedDictionary<DateOnly, decimal> PricesOf(string answer)
    {
        var prices = new SortedDictionary<DateOnly, decimal>();

        using var document = System.Text.Json.JsonDocument.Parse(answer);

        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return prices;
        }

        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("date", out var date) && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && row.TryGetProperty("close", out var close) && close.ValueKind == System.Text.Json.JsonValueKind.Number && close.TryGetDecimal(out var price))
            {
                prices[day] = price;
            }
        }

        return prices;
    }

    // Every fund's schedules matched over the capture's day files, the closes asked and the symbol lists.
    public static Matched Match(string folder)
    {
        var (schedules, unread) = LongScheduleFilings.Read(folder);
        var closes = DayFiles(folder);
        var symbols = Symbols(folder);
        var matcher = new HoldingMatcher(symbols);
        var names = new LongNames(symbols);
        var asked = Asked(folder);
        var matches = new Dictionary<string, IReadOnlyDictionary<(DateOnly, int), LongMatch>>(StringComparer.Ordinal);

        foreach (var index in LongScheduleFilings.Funds.Keys)
        {
            matches[index] = LongMatching.Match([.. schedules.Where(schedule => schedule.Index == index)], closes, matcher, asked, names);
        }

        return new Matched(schedules, matches, unread);
    }

    // The codes to ask the provider's own prices of: every code a name still matched to none at some quarter end could be read
    // as that no day file carries at any of its quarter ends and no ask has answered, each with the span from the first of the
    // name's quarter ends to its last.
    public static IReadOnlyList<(string Code, DateOnly From, DateOnly Through)> ToAsk(string folder, Matched matched)
    {
        var closes = DayFiles(folder);
        var symbols = Symbols(folder);
        var matcher = new HoldingMatcher(symbols);
        var names = new LongNames(symbols);
        var answered = Directory.Exists(Path.Combine(folder, AskedFolder))
            ? Directory.GetFiles(Path.Combine(folder, AskedFolder), "*.json").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal)
            : [];
        var spans = new Dictionary<string, (DateOnly From, DateOnly Through)>(StringComparer.Ordinal);

        foreach (var group in matched.Schedules
            .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (schedule.Index, schedule.Period, Place: place, Holding: holding)))
            .GroupBy(one => (one.Index, one.Holding.Name)))
        {
            if (group.All(one => matched.Matches[one.Index].ContainsKey((one.Period, one.Place))))
            {
                continue;
            }

            var (first, last) = (group.Min(one => one.Period), group.Max(one => one.Period));

            foreach (var code in LongMatching.Candidates(matcher, names, group.First().Holding))
            {
                if (answered.Contains(code) || group.Any(one => closes.TryGetValue(one.Period, out var day) && day.CloseOf(code) is not null))
                {
                    continue;
                }

                spans[code] = spans.TryGetValue(code, out var span) ? (span.From < first ? span.From : first, span.Through > last ? span.Through : last) : (first, last);
            }
        }

        return [.. spans.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value.From, pair.Value.Through))];
    }

    // Each code asked of the provider once, its answer kept as sent; a code already answered asked no more.
    public static async Task<int> AskAsync(string folder, ILongHistoryFeed feed, IReadOnlyList<(string Code, DateOnly From, DateOnly Through)> codes, Action<string> progress, CancellationToken cancellation = default)
    {
        var asked = Path.Combine(folder, AskedFolder);
        var kept = 0;

        Directory.CreateDirectory(asked);

        foreach (var (code, from, through) in codes)
        {
            var file = Path.Combine(asked, code + ".json");

            if (File.Exists(file))
            {
                continue;
            }

            try
            {
                await File.WriteAllTextAsync(file, await feed.PricesAsync(code + "." + LongDayFiles.Exchange, from.AddDays(-LongDayFiles.SessionDays), through, cancellation), cancellation);
                kept++;
            }
            catch (ProviderRefusal refused)
            {
                await File.WriteAllTextAsync(file, "[]", cancellation);
                progress($"{code}: {refused.Message}");
            }

            if (kept % 100 == 0)
            {
                progress(FormattableString.Invariant($"{kept} of {codes.Count} asked"));
            }
        }

        return kept;
    }

    // Each quarter end's figures of each fund's matching.
    public static IReadOnlyList<LongQuarter> Quarters(Matched matched) =>
    [
        .. matched.Schedules.OrderBy(schedule => schedule.Index, StringComparer.Ordinal).ThenBy(schedule => schedule.Period).Select(schedule =>
        {
            var byKey = new Dictionary<string, (int Count, decimal Value)>(StringComparer.Ordinal);

            for (var place = 0; place < schedule.Holdings.Count; place++)
            {
                if (matched.Matches[schedule.Index].TryGetValue((schedule.Period, place), out var match))
                {
                    var (count, value) = byKey.GetValueOrDefault(match.By);

                    byKey[match.By] = (count + 1, value + (schedule.Holdings[place].Value ?? 0m));
                }
            }

            return new LongQuarter(schedule.Index, schedule.Period, schedule.Holdings.Count, schedule.Holdings.Sum(holding => holding.Value ?? 0m), byKey);
        }),
    ];
}
