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
// see: A holding's code may be read off its company's own SEC filings and held to the fund's values a share as any code is
// see: The holdings the long history leaves out are split by why they left with their share of value each year
public sealed class LongMembers : EquityBrief.Core.Components.IComponent
{
    public static EquityBrief.Core.Components.ComponentAccess Access => new(
        Stores:
        [
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.LongSnapshot, EquityBrief.Core.Components.Touch.Insert | EquityBrief.Core.Components.Touch.Delete),
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.LongHolding, EquityBrief.Core.Components.Touch.Read | EquityBrief.Core.Components.Touch.Insert | EquityBrief.Core.Components.Touch.Delete),
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.LongSpan, EquityBrief.Core.Components.Touch.Insert | EquityBrief.Core.Components.Touch.Delete),
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.PulledHolding, EquityBrief.Core.Components.Touch.Read),
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.Membership, EquityBrief.Core.Components.Touch.Read),
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.RunLog, EquityBrief.Core.Components.Touch.Insert),
        ],
        Feeds: [EquityBrief.Core.Components.Feed.FilingsArchive, EquityBrief.Core.Components.Feed.HistoricalPrice]);

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

        decimal? LastWithin(IReadOnlyDictionary<DateOnly, decimal> prices, DateOnly period)
        {
            decimal? last = null;

            foreach (var pair in prices.Where(one => one.Key <= period && one.Key > period.AddDays(-LongDayFiles.SessionDays)))
            {
                last = pair.Value;
            }

            return last;
        }

        return (period, code) => read.TryGetValue(code, out var prices) ? LastWithin(prices, period) : null;
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
        var fromFilings = FromFilings(folder, symbols);
        var matches = new Dictionary<string, IReadOnlyDictionary<(DateOnly, int), LongMatch>>(StringComparer.Ordinal);

        // The codes the symbol lists file as something other than a company's common stock, a fund, a note or a preferred
        // line, which the price key leaves out however their closes fall; a code the lists do not carry is read.
        var typed = symbols.GroupBy(symbol => symbol.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Any(symbol => symbol.Type == ProviderSymbols.CommonStock), StringComparer.Ordinal);

        foreach (var index in LongScheduleFilings.Funds.Keys)
        {
            matches[index] = LongMatching.Match([.. schedules.Where(schedule => schedule.Index == index)], closes, matcher, asked, names, fromFilings, code => typed.TryGetValue(code, out var stock) && !stock, LongHandMatches.Of(index), (name, code) => LongHandMatches.IsRefused(index, name, code));
        }

        return new Matched(schedules, matches, unread);
    }

    // The codes a name's company's own SEC filings point to: the code its annual report names with its older forms, and every
    // common stock the symbol lists file under exactly a name its filer took, its current name or a former one, which reads a
    // company renamed after it left the fund.
    public static Func<string, IReadOnlyList<string>> FromFilings(string folder, IReadOnlyList<ListedSymbol> symbols)
    {
        var filed = FiledCodes(folder);
        var chosen = ChosenFilers(folder);
        var read = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var byName = symbols
            .Where(symbol => symbol.Type == ProviderSymbols.CommonStock)
            .Select(symbol => (Key: HoldingMatcher.NameKey(symbol.Name), symbol.Code))
            .Where(pair => pair.Key.Length > 0)
            .ToLookup(pair => pair.Key, pair => pair.Code, StringComparer.Ordinal);

        return name =>
        {
            if (!read.TryGetValue(name, out var codes))
            {
                var index = chosen.GetValueOrDefault(name)?.Cik is { } cik ? Path.Combine(folder, FilersFolder, "submissions-" + cik + ".json") : null;
                var filerNames = index is not null && File.Exists(index) ? NamesOf(File.ReadAllText(index)) : [];

                read[name] = codes = [.. Reported(filed, name).Concat(filerNames.SelectMany(filer => byName[HoldingMatcher.NameKey(filer)])).Distinct(StringComparer.Ordinal)];
            }

            return codes;
        };
    }

    // The code a name's company's own annual report names, and its older forms, which the provider files the company under
    // once its code passed to another.
    public static IReadOnlyList<string> Reported(IReadOnlyDictionary<string, FiledCode> filed, string name) =>
        filed.TryGetValue(name, out var code)
            ? [code.Code, .. Enumerable.Range(0, LongNames.OlderForms).Select(form => form == 0 ? code.Code + "_old" : FormattableString.Invariant($"{code.Code}_old{form}"))]
            : [];

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
        var fromFilings = FromFilings(folder, symbols);

        foreach (var group in matched.Schedules
            .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (schedule.Index, schedule.Period, Place: place, Holding: holding)))
            .GroupBy(one => (one.Index, one.Holding.Name)))
        {
            if (group.All(one => matched.Matches[one.Index].ContainsKey((one.Period, one.Place))))
            {
                continue;
            }

            var (first, last) = (group.Min(one => one.Period), group.Max(one => one.Period));

            foreach (var code in LongMatching.Candidates(matcher, names, group.First().Holding).Concat(fromFilings(group.Key.Name)).Distinct(StringComparer.Ordinal))
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

    public const string FilersFolder = "filers";

    public const string NamedFolder = "named";

    // The companies the plan names in 2008 and 2009, by the words a fund's holding of each carries.
    public static IReadOnlyList<(string Named, string Words)> Named { get; } =
    [
        ("Bear Stearns", "Bear Stearns"),
        ("Lehman", "Lehman Brothers"),
        ("Merrill", "Merrill Lynch"),
        ("Wachovia", "Wachovia"),
        ("Washington Mutual", "Washington Mutual"),
        ("Countrywide", "Countrywide"),
        ("National City", "National City"),
        ("Fannie Mae", "Fannie Mae|Federal National Mortgage"),
        ("Freddie Mac", "Freddie Mac|Federal Home Loan Mortgage"),
        ("CIT", "CIT Group"),
        ("General Motors", "General Motors Corp"),
    ];

    // How long after its last quarter end held a named company's prices are asked for.
    public const int NamedYears = 3;

    // One named company as the long history holds it: the fund, the name as filed, its last quarter end held, the code and key
    // it was matched by there or none, and its last session with a volume and that session's close as the provider answers.
    public sealed record NamedCompany(string Named, string Index, string Name, DateOnly Last, string? Code, string? By, DateOnly? LastSession, decimal? LastClose);

    // Each named company's last holding in each fund, its code asked of the provider from that quarter end for three years, the
    // answer kept as sent.
    public static async Task<IReadOnlyList<NamedCompany>> NamedAsync(string folder, ILongHistoryFeed feed, Matched matched, CancellationToken cancellation = default)
    {
        var kept = Path.Combine(folder, NamedFolder);
        var named = new List<NamedCompany>();

        Directory.CreateDirectory(kept);

        foreach (var (title, words) in Named)
        {
            var pattern = new System.Text.RegularExpressions.Regex(words, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var last in matched.Schedules
                .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (schedule.Index, schedule.Period, Place: place, holding.Name)))
                .Where(one => pattern.IsMatch(one.Name))
                .GroupBy(one => one.Index, StringComparer.Ordinal)
                .Select(group => group.MaxBy(one => one.Period))
                .OrderBy(one => one.Index, StringComparer.Ordinal))
            {
                var match = matched.Matches[last.Index].GetValueOrDefault((last.Period, last.Place));
                DateOnly? session = null;
                decimal? close = null;

                if (match is not null)
                {
                    var file = Path.Combine(kept, match.Ticker + ".json");
                    var answer = File.Exists(file) ? await File.ReadAllTextAsync(file, cancellation) : await feed.PricesAsync(match.Ticker + "." + LongDayFiles.Exchange, last.Period, last.Period.AddYears(NamedYears), cancellation);

                    await File.WriteAllTextAsync(file, answer, cancellation);

                    using var document = System.Text.Json.JsonDocument.Parse(answer);

                    foreach (var row in document.RootElement.EnumerateArray())
                    {
                        if (row.TryGetProperty("volume", out var volume) && volume.ValueKind == System.Text.Json.JsonValueKind.Number && volume.GetDecimal() > 0m
                            && DateOnly.TryParseExact(row.GetProperty("date").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                        {
                            (session, close) = (day, row.GetProperty("close").GetDecimal());
                        }
                    }
                }

                named.Add(new NamedCompany(title, last.Index, last.Name, last.Period, match?.Ticker, match?.By, session, close));
            }
        }

        return named;
    }

    // Each name a fund's schedules hold that some quarter end still matches to none, with the quarter ends and the value left
    // out, the largest first.
    public static IReadOnlyList<(string Name, IReadOnlyList<(string Index, DateOnly Period)> Quarters, decimal Value)> Unmatched(Matched matched) =>
    [
        .. matched.Schedules
            .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (schedule.Index, schedule.Period, Place: place, Holding: holding)))
            .Where(one => !matched.Matches[one.Index].ContainsKey((one.Period, one.Place)))
            .GroupBy(one => one.Holding.Name, StringComparer.Ordinal)
            .Select(group => (group.Key, (IReadOnlyList<(string, DateOnly)>)[.. group.Select(one => (one.Index, one.Period))], group.Sum(one => one.Holding.Value ?? 0m)))
            .OrderByDescending(one => one.Item3)
            .ThenBy(one => one.Key, StringComparer.Ordinal),
    ];

    // The words a name is looked for under in the archive's company browser.
    public static string SearchedAs(string name) => HoldingMatcher.NameKey(LongMatching.InOrder(name)).ToLowerInvariant();

    // The file a name's search is kept in.
    public static string FilerFile(string folder, string name) =>
        Path.Combine(folder, FilersFolder, string.Concat(SearchedAs(name).Select(character => char.IsLetterOrDigit(character) ? character : '-')) + ".xml");

    // A name's code as its company's own annual report names it: the filer, the report read and the sentence naming the code.
    public sealed record FiledCode(string Name, string Cik, string Filer, string Accession, string Code, string Sentence);

    public const string FiledFile = "filed-codes.json";

    static readonly System.Text.RegularExpressions.Regex CikOf = new(@"<cik>\s*(\d+)\s*</cik>", System.Text.RegularExpressions.RegexOptions.Compiled);

    // How many of the filers a company search names are read for their names.
    public const int FilersRead = 6;
    // A report's sentence naming its code: under or by the symbol, or a ticker or trading symbol and a colon, the mark around
    // the code any one sign, since a page filed in an older encoding comes back with its quotes unread.
    static readonly System.Text.RegularExpressions.Regex Symbol = new(@"(?:(?:under|by)\s+the\s+(?:ticker\s+|trading\s+)?symbols?|(?:ticker|trading)\s+symbol\s*:)\s*[^\w\s]?\s*([A-Z]{1,5}(?:\.[A-Z])?)\b[^.]{0,80}", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The filers a company search names, by CIK.
    static IReadOnlyList<string> FilersOf(string search) =>
        [.. CikOf.Matches(search).Select(one => one.Groups[1].Value.TrimStart('0')).Distinct(StringComparer.Ordinal)];

    // A filer's name and its former names as its filing index states them.
    public static IReadOnlyList<string> NamesOf(string index)
    {
        using var document = System.Text.Json.JsonDocument.Parse(index);
        var names = new List<string>();

        if (document.RootElement.TryGetProperty("name", out var name) && name.GetString() is { } current)
        {
            names.Add(current);
        }

        if (document.RootElement.TryGetProperty("formerNames", out var former) && former.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            names.AddRange(former.EnumerateArray().Select(one => one.TryGetProperty("name", out var was) ? was.GetString() : null).OfType<string>());
        }

        return names;
    }

    // Each name still matched to none at some quarter end looked for in the archive: its company search by its words and then
    // by fewer of them, the filer whose name holds most of its words, one of that filer's annual reports from the years held,
    // and the code the report names; every answer kept as sent, a name already read asked nothing again.
    public static async Task<IReadOnlyList<FiledCode>> FindFilersAsync(string folder, ITrustScheduleFeed feed, int top, Action<string> progress, CancellationToken cancellation = default)
    {
        var filers = Path.Combine(folder, FilersFolder);
        var found = new List<FiledCode>();

        Directory.CreateDirectory(filers);

        var chosen = new List<ChosenFiler>();
        var unmatched = Unmatched(Match(folder)).Take(top).ToArray();

        foreach (var (name, quarters, _) in unmatched)
        {
            var filer = await FilerAsync(filers, feed, name, cancellation);

            chosen.Add(new ChosenFiler(name, filer?.Cik, filer?.Name));

            if (filer is not null && await ReadCodeAsync(filers, feed, name, quarters.Min(quarter => quarter.Period), quarters.Max(quarter => quarter.Period), cancellation) is { } read)
            {
                found.Add(read);
            }

            if (chosen.Count % 50 == 0)
            {
                progress(FormattableString.Invariant($"{chosen.Count} of {unmatched.Length} name(s) looked for, {found.Count} code(s) read"));
            }
        }

        // A name looked for before and matched since keeps what was found for it, which its match may rest on.
        var looked = chosen.Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        FiledCode[] allFound = [.. FiledCodes(folder).Values.Where(one => !looked.Contains(one.Name)), .. found];
        ChosenFiler[] allChosen = [.. ChosenFilers(folder).Values.Where(one => !looked.Contains(one.Name)), .. chosen];

        await File.WriteAllTextAsync(Path.Combine(folder, FiledFile), System.Text.Json.JsonSerializer.Serialize(allFound.OrderBy(one => one.Name, StringComparer.Ordinal), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, ChosenFile), System.Text.Json.JsonSerializer.Serialize(allChosen.OrderBy(one => one.Name, StringComparer.Ordinal), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellation);
        progress(FormattableString.Invariant($"{chosen.Count(one => one.Cik is not null)} of {chosen.Count} name(s) found a filer, {found.Count} read a code off their company's own annual report"));

        return found;
    }

    // The filer a name was looked for under, none where no filer's name held enough of its words.
    public sealed record ChosenFiler(string Name, string? Cik, string? Filer);

    public const string ChosenFile = "chosen-filers.json";

    // The filers each name was looked for under, as the lookup kept them.
    public static IReadOnlyDictionary<string, ChosenFiler> ChosenFilers(string folder)
    {
        var file = Path.Combine(folder, ChosenFile);

        return File.Exists(file)
            ? (System.Text.Json.JsonSerializer.Deserialize<List<ChosenFiler>>(File.ReadAllText(file)) ?? []).GroupBy(one => one.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            : new Dictionary<string, ChosenFiler>(StringComparer.Ordinal);
    }

    // A filer's filings off its index and every older page it names, as the lookup kept them; none where a page was not kept.
    public static IReadOnlyList<FilerFiling>? KeptFilings(string filers, string cik)
    {
        var indexFile = Path.Combine(filers, "submissions-" + cik + ".json");

        if (!File.Exists(indexFile))
        {
            return null;
        }

        var index = File.ReadAllText(indexFile);
        var pages = FilerFilings.Pages(index).Select(page => Path.Combine(filers, page)).ToArray();

        return pages.All(File.Exists) ? FilerFilings.Read([index, .. pages.Select(File.ReadAllText)]) : null;
    }

    // The filer whose name or former name holds most of a name's words, searched by its words and then by fewer of them, and
    // where none is found by its words with a part set in brackets left out, a place the trust names a company by; with its
    // index and every older page it names, each kept as sent; none where no filer's name holds half its words.
    public static async Task<(string Cik, string Name, IReadOnlyList<string> Pages)?> FilerAsync(string filers, ITrustScheduleFeed feed, string name, CancellationToken cancellation = default)
    {
        var unbracketed = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)", string.Empty).Trim();

        return await FilerByAsync(filers, feed, SearchedAs(name), cancellation)
            ?? (unbracketed != name && unbracketed.Length > 0 ? await FilerByAsync(filers, feed, SearchedAs(unbracketed), cancellation) : null);
    }

    static async Task<(string Cik, string Name, IReadOnlyList<string> Pages)?> FilerByAsync(string filers, ITrustScheduleFeed feed, string searched, CancellationToken cancellation)
    {
        var words = searched.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        (string Cik, string Name, string Index)? filer = null;

        async Task<string> KeptAsync(string file, Func<Task<string>> ask)
        {
            var answer = File.Exists(file) ? await File.ReadAllTextAsync(file, cancellation) : await ask();

            await File.WriteAllTextAsync(file, answer, cancellation);

            return answer;
        }

        for (var kept = words.Length; kept >= 1 && filer is null; kept--)
        {
            var query = string.Join(' ', words[..kept]);
            var answer = await KeptAsync(Path.Combine(filers, "search-" + string.Concat(query.Select(character => char.IsLetterOrDigit(character) ? character : '-')) + ".xml"), () => feed.CompaniesAsync(query, cancellation));
            var best = (Shared: 0.0, Filer: ((string, string, string)?)null);

            foreach (var cik in FilersOf(answer).Take(FilersRead))
            {
                var filings = await KeptAsync(Path.Combine(filers, "submissions-" + cik + ".json"), () => feed.IndexAsync(cik, cancellation));

                foreach (var filed in NamesOf(filings))
                {
                    var shared = Shared(words, HoldingMatcher.NameKey(filed).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

                    if (shared > best.Shared)
                    {
                        best = (shared, (cik, filed, filings));
                    }
                }
            }

            if (best.Shared >= LongNames.SharedAtLeast)
            {
                filer = best.Filer;
            }
        }

        if (filer is not { } chosen)
        {
            return null;
        }

        var pages = new List<string> { chosen.Index };

        foreach (var page in FilerFilings.Pages(chosen.Index))
        {
            pages.Add(await KeptAsync(Path.Combine(filers, page), () => feed.PageAsync(page, cancellation)));
        }

        return (chosen.Cik, chosen.Name, pages);
    }

    // A name's code as its company's own annual report from the years it was held names it, none where no filer of the name, no
    // such report or no sentence naming a code is found; every answer kept as sent, one already kept asked nothing again.
    public static async Task<FiledCode?> ReadCodeAsync(string filers, ITrustScheduleFeed feed, string name, DateOnly first, DateOnly last, CancellationToken cancellation = default)
    {
        if (await FilerAsync(filers, feed, name, cancellation) is not { } chosen)
        {
            return null;
        }

        var report = FilerFilings.Read(chosen.Pages)
            .Where(filing => filing.Form is "10-K" or "10-K405" && filing.Filed >= first.AddYears(-1) && filing.Filed <= last.AddYears(1))
            .OrderBy(filing => Math.Abs(filing.Filed.DayNumber - first.AddDays((last.DayNumber - first.DayNumber) / 2).DayNumber))
            .FirstOrDefault();

        if (report is null)
        {
            return null;
        }

        var documentFile = Path.Combine(filers, report.Accession + "-" + report.PrimaryDocument);
        var document = File.Exists(documentFile) ? await File.ReadAllTextAsync(documentFile, cancellation) : await feed.DocumentAsync(chosen.Cik, report.Accession, report.PrimaryDocument, cancellation);

        await File.WriteAllTextAsync(documentFile, document, cancellation);

        var text = System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(document, "<[^>]+>", " ")), @"\s+", " ");

        return Symbol.Match(text) is { Success: true } named
            ? new FiledCode(name, chosen.Cik, chosen.Name, report.Accession, named.Groups[1].Value.Replace('.', '-'), named.Value.Trim())
            : null;
    }

    // One match checked against its company's own annual report: the fund, the name and the code it was matched to with its
    // key, the quarter ends matched, whether it was matched at one alone, and the code the report names with its sentence.
    public sealed record HandCheck(string Index, string Name, string Code, string By, int Quarters, bool Single, string? Filed, string? Sentence, string? Accession)
    {
        public string Verdict => Filed is null ? NotRead : Plain(Code) == Plain(Filed) ? Agrees : Differs;
    }

    public const string Agrees = "agrees";

    public const string Differs = "differs";

    public const string NotRead = "no report read";

    // How many matches a key a fund are sampled for the check, at what seed, and how many of them may differ before the key
    // stops the run.
    public const int SampleOf = 30;

    public const int SampleSeed = 20261107;

    public const int WrongAtMost = 2;

    public const string ChecksFile = "checks.json";

    // A code as its company files it: its older forms' mark and a class's separator set aside.
    static string Plain(string code) =>
        System.Text.RegularExpressions.Regex.Replace(code, "_old\\d*$", string.Empty).Replace(".", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    // The matches checked against the SEC's filings: every one matched at a single quarter end, and 30 a key a fund drawn at a
    // fixed seed from the rest.
    public static IReadOnlyList<(string Index, string Name, string Code, string By, IReadOnlyList<DateOnly> Quarters, bool Single)> ToCheck(Matched matched)
    {
        var listed = new List<(string, string, string, string, IReadOnlyList<DateOnly>, bool)>();

        foreach (var index in LongScheduleFilings.Funds.Keys)
        {
            var groups = matched.Schedules
                .Where(schedule => schedule.Index == index)
                .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (schedule.Period, holding.Name, Match: matched.Matches[index].GetValueOrDefault((schedule.Period, place)))))
                .Where(one => one.Match is not null)
                .GroupBy(one => (one.Name, one.Match!.Ticker, one.Match.By))
                .Select(group => (Index: index, group.Key.Name, Code: group.Key.Ticker, group.Key.By, Quarters: (IReadOnlyList<DateOnly>)[.. group.Select(one => one.Period).Order()]))
                .OrderBy(group => group.Name, StringComparer.Ordinal)
                .ThenBy(group => group.Code, StringComparer.Ordinal)
                .ToArray();

            listed.AddRange(groups.Where(group => group.Quarters.Count == 1).Select(group => (group.Index, group.Name, group.Code, group.By, group.Quarters, true)));

            foreach (var key in groups.Where(group => group.Quarters.Count > 1).GroupBy(group => group.By, StringComparer.Ordinal).OrderBy(key => key.Key, StringComparer.Ordinal))
            {
                var random = new Random(SampleSeed);

                listed.AddRange(key.OrderBy(_ => random.Next()).Take(SampleOf).Select(group => (group.Index, group.Name, group.Code, group.By, group.Quarters, false)));
            }
        }

        return listed;
    }

    // Each match the check lists read against its company's own annual report, written to the capture's folder.
    public static async Task<IReadOnlyList<HandCheck>> CheckAsync(string folder, ITrustScheduleFeed feed, Matched matched, CancellationToken cancellation = default)
    {
        var filers = Path.Combine(folder, FilersFolder);
        var checks = new List<HandCheck>();

        Directory.CreateDirectory(filers);

        foreach (var (index, name, code, by, quarters, single) in ToCheck(matched))
        {
            var read = await ReadCodeAsync(filers, feed, name, quarters[0], quarters[^1], cancellation);

            checks.Add(new HandCheck(index, name, code, by, quarters.Count, single, read?.Code, read?.Sentence, read?.Accession));
        }

        await File.WriteAllTextAsync(Path.Combine(folder, ChecksFile), System.Text.Json.JsonSerializer.Serialize(checks, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellation);

        return checks;
    }

    static double Shared(string[] one, string[] other)
    {
        var both = one.Intersect(other, StringComparer.Ordinal).Count();
        var either = one.Union(other, StringComparer.Ordinal).Count();

        return either == 0 ? 0.0 : 1.0 * both / either;
    }

    // The codes each name's company's annual report named, as the lookup kept them.
    public static IReadOnlyDictionary<string, FiledCode> FiledCodes(string folder)
    {
        var file = Path.Combine(folder, FiledFile);

        return File.Exists(file)
            ? (System.Text.Json.JsonSerializer.Deserialize<List<FiledCode>>(File.ReadAllText(file)) ?? []).GroupBy(one => one.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            : new Dictionary<string, FiledCode>(StringComparer.Ordinal);
    }

    // What each checked match was checked against, by fund and name: a hand match's reading, else the sampled check's verdict
    // and the report it read.
    public static IReadOnlyDictionary<(string Index, string Name), string> CheckedAs(string folder)
    {
        var file = Path.Combine(folder, ChecksFile);
        var checkedAs = new Dictionary<(string Index, string Name), string>();

        foreach (var check in File.Exists(file) ? System.Text.Json.JsonSerializer.Deserialize<List<HandCheck>>(File.ReadAllText(file)) ?? [] : [])
        {
            checkedAs[(check.Index, check.Name)] = check.Filed is null
                ? check.Verdict
                : FormattableString.Invariant($"{check.Verdict}: the annual report, accession {check.Accession}, names {check.Filed}, \"{check.Sentence}\"");
        }

        foreach (var hand in LongHandMatches.All)
        {
            checkedAs[(hand.Index, hand.Name)] = "matched by hand: " + hand.Read;
        }

        return checkedAs;
    }

    // Why each name a fund left out at some quarter end left, read off its filer's kept filings around its last quarter end in
    // that fund, by fund and name.
    public static IReadOnlyDictionary<(string Index, string Name), LongLeft> LeftAs(string folder, Matched matched)
    {
        var chosen = ChosenFilers(folder);
        var filers = Path.Combine(folder, FilersFolder);
        var filings = new Dictionary<string, IReadOnlyList<FilerFiling>?>(StringComparer.Ordinal);
        var left = new Dictionary<(string Index, string Name), LongLeft>();

        foreach (var group in matched.Schedules
            .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (schedule.Index, schedule.Period, Place: place, holding.Name)))
            .GroupBy(one => (one.Index, one.Name)))
        {
            if (group.All(one => matched.Matches[one.Index].ContainsKey((one.Period, one.Place))))
            {
                continue;
            }

            var cik = chosen.GetValueOrDefault(group.Key.Name)?.Cik;
            var kept = cik is null ? null : filings.TryGetValue(cik, out var read) ? read : filings[cik] = KeptFilings(filers, cik);

            left[group.Key] = LongLeaving.Of(group.Key.Name, kept, group.Max(one => one.Period));
        }

        return left;
    }

    // Whether a schedule's holding is matched and priced.
    public static Func<LongSchedule, int, bool> IsMatched(Matched matched) =>
        (schedule, place) => matched.Matches[schedule.Index].ContainsKey((schedule.Period, place));

    // Why the run stops over a capture, none where nothing does: each quarter end under the stop and each fall by half the
    // operator has not left out by name.
    public static IReadOnlyList<string> Stops(Matched matched) =>
        LongFloors.Stops(LongFloors.Coverage(matched.Schedules, IsMatched(matched)), LongFloors.Falls(matched.Schedules, IsMatched(matched)));

    const string InsertSnapshot = @"
        INSERT INTO long_snapshot (index_code, period, accession, form, holdings, value_usd, run_id)
        VALUES ($index_code, $period, $accession, $form, $holdings, $value_usd, $run_id);";

    const string InsertHolding = @"
        INSERT INTO long_holding (index_code, period, holding, name, industry, shares, value_usd, ticker, matched_by, stood_between, checked, left_as, left_read, run_id)
        VALUES ($index_code, $period, $holding, $name, $industry, $shares, $value_usd, $ticker, $matched_by, $stood_between, $checked, $left_as, $left_read, $run_id);";

    const string InsertSpan = @"
        INSERT INTO long_span (index_code, ticker, joined, left, read_from, run_id)
        VALUES ($index_code, $ticker, $joined, $left, $read_from, $run_id);";

    const string ClearRun = "DELETE FROM long_span; DELETE FROM long_holding; DELETE FROM long_snapshot;";

    public const string Stage = "long-members";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    const string SelectPulledHoldings = "SELECT index_code, period, ticker FROM pulled_holding WHERE ticker IS NOT NULL;";

    const string SelectProviderSpans = "SELECT ticker, joined, left, name FROM membership WHERE index_code = 'GSPC';";

    // The run's schedules and their holdings written whole in one transaction, each holding with the code and the key it was
    // matched by or why it was left out, and every code's spans of membership read over these schedules, the members pull's
    // later ones and the provider's dates of the S&P 500, the run before's rows removed with them; nothing written where a stop
    // stands.
    public static async Task<int> WriteAsync(string databaseFile, string runId, Matched matched, IReadOnlyDictionary<string, string> forms, IReadOnlyDictionary<(string Index, string Name), string> checks, IReadOnlyDictionary<(string Index, string Name), LongLeft> left, EquityBrief.Core.Time.IClock clock, DateTimeOffset startedAt, CancellationToken cancellation = default)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(EquityBrief.Data.StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = connection.BeginTransaction();
        var written = 0;
        var snapshots = new Dictionary<string, Dictionary<DateOnly, HashSet<string>>>(StringComparer.Ordinal);
        var provider = new List<ProviderSpan>();

        void Hold(string index, DateOnly period, string ticker)
        {
            if (!snapshots.TryGetValue(index, out var periods))
            {
                snapshots[index] = periods = [];
            }

            if (!periods.TryGetValue(period, out var codes))
            {
                periods[period] = codes = new HashSet<string>(StringComparer.Ordinal);
            }

            codes.Add(ticker);
        }

        static DateOnly? Day(object value) =>
            value is string text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = SelectPulledHoldings;

            await using var rows = await read.ExecuteReaderAsync(cancellation);

            while (await rows.ReadAsync(cancellation))
            {
                if (Day(rows.GetString(1)) is { } period)
                {
                    Hold(rows.GetString(0), period, rows.GetString(2));
                }
            }
        }

        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = SelectProviderSpans;

            await using var rows = await read.ExecuteReaderAsync(cancellation);

            while (await rows.ReadAsync(cancellation))
            {
                provider.Add(new ProviderSpan(rows.GetString(0), Day(rows.GetValue(1)), Day(rows.GetValue(2)), rows.IsDBNull(3) ? null : rows.GetString(3)));
            }
        }

        async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            written += await command.ExecuteNonQueryAsync(cancellation);
        }

        await ExecuteAsync(ClearRun);
        written = 0;

        foreach (var schedule in matched.Schedules.OrderBy(one => one.Index, StringComparer.Ordinal).ThenBy(one => one.Period))
        {
            var period = schedule.Period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            await ExecuteAsync(InsertSnapshot,
                ("$index_code", schedule.Index),
                ("$period", period),
                ("$accession", schedule.Accession),
                ("$form", forms.GetValueOrDefault(schedule.Accession, string.Empty)),
                ("$holdings", schedule.Holdings.Count),
                ("$value_usd", schedule.Holdings.Sum(holding => holding.Value ?? 0m).ToString(CultureInfo.InvariantCulture)),
                ("$run_id", runId));

            for (var place = 0; place < schedule.Holdings.Count; place++)
            {
                var holding = schedule.Holdings[place];
                var match = matched.Matches[schedule.Index].GetValueOrDefault((schedule.Period, place));
                var why = match is null ? left.GetValueOrDefault((schedule.Index, holding.Name)) : null;

                if (match is not null)
                {
                    Hold(schedule.Index, schedule.Period, match.Ticker);
                }

                await ExecuteAsync(InsertHolding,
                    ("$index_code", schedule.Index),
                    ("$period", period),
                    ("$holding", place),
                    ("$name", holding.Name),
                    ("$industry", holding.Industry),
                    ("$shares", holding.Shares?.ToString(CultureInfo.InvariantCulture)),
                    ("$value_usd", holding.Value?.ToString(CultureInfo.InvariantCulture)),
                    ("$ticker", match?.Ticker),
                    ("$matched_by", match?.By),
                    ("$stood_between", match is null ? null : System.Text.Json.JsonSerializer.Serialize(match.Between)),
                    ("$checked", match is null ? null : checks.GetValueOrDefault((schedule.Index, holding.Name))),
                    ("$left_as", why?.As),
                    ("$left_read", why is null ? null : why.Accession is null ? why.Read : FormattableString.Invariant($"{why.Read}, accession {why.Accession} of {why.Filed:yyyy-MM-dd}")),
                    ("$run_id", runId));
            }
        }

        var firstHeldByName = matched.Schedules
            .Where(schedule => schedule.Index == "GSPC")
            .SelectMany(schedule => schedule.Holdings.Select(holding => (Key: HoldingMatcher.NameKey(LongMatching.InOrder(holding.Name)), schedule.Period)))
            .Where(one => one.Key.Length > 0)
            .GroupBy(one => one.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(one => one.Period), StringComparer.Ordinal);
        var spans = LongSpans.Of(
            snapshots.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>)pair.Value.ToDictionary(one => one.Key, one => (IReadOnlySet<string>)one.Value), StringComparer.Ordinal),
            provider,
            firstHeldByName);

        foreach (var span in spans)
        {
            await ExecuteAsync(InsertSpan,
                ("$index_code", span.Index),
                ("$ticker", span.Ticker),
                ("$joined", span.Joined.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$left", span.Left?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$read_from", span.ReadFrom),
                ("$run_id", runId));
        }

        var holdings = matched.Schedules.Sum(schedule => schedule.Holdings.Count);
        var priced = matched.Schedules.Sum(schedule => Enumerable.Range(0, schedule.Holdings.Count).Count(place => matched.Matches[schedule.Index].ContainsKey((schedule.Period, place))));
        var writtenRows = written;

        await ExecuteAsync(AppendRun,
            ("$run_id", runId),
            ("$stage", Stage),
            ("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$rows", writtenRows),
            ("$detail", FormattableString.Invariant($"{matched.Schedules.Count} schedule(s) and {holdings} holding(s) written, {priced} matched and {holdings - priced} left out, {spans.Count} span(s)")));

        await transaction.CommitAsync(cancellation);

        return writtenRows;
    }

    const string SelectLongHoldings = "SELECT index_code, period, ticker, name FROM long_holding;";

    const string ClearSpans = "DELETE FROM long_span;";

    // The spans read again from what the store holds: the long history's matched holdings, the members pull's later ones and the
    // provider's dates of the S&P 500 from the day given, as the run that wrote the holdings read them.
    // see: The S&P 500 before 2012 is read from IVV's filed holdings and from 2012 from the provider's dates
    public static async Task<IReadOnlyList<LongSpan>> SpansAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, DateOnly providerFrom, CancellationToken cancellation = default)
    {
        var snapshots = new Dictionary<string, Dictionary<DateOnly, HashSet<string>>>(StringComparer.Ordinal);
        var provider = new List<ProviderSpan>();
        var firstHeldByName = new Dictionary<string, DateOnly>(StringComparer.Ordinal);

        static DateOnly? Day(object value) =>
            value is string text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

        void Hold(string index, DateOnly period, string ticker)
        {
            if (!snapshots.TryGetValue(index, out var periods))
            {
                snapshots[index] = periods = [];
            }

            if (!periods.TryGetValue(period, out var codes))
            {
                periods[period] = codes = new HashSet<string>(StringComparer.Ordinal);
            }

            codes.Add(ticker);
        }

        async Task ReadAsync(string sql, Action<Microsoft.Data.Sqlite.SqliteDataReader> row)
        {
            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = sql;

            await using var rows = await read.ExecuteReaderAsync(cancellation);

            while (await rows.ReadAsync(cancellation))
            {
                row(rows);
            }
        }

        await ReadAsync(SelectPulledHoldings, rows =>
        {
            if (Day(rows.GetString(1)) is { } period)
            {
                Hold(rows.GetString(0), period, rows.GetString(2));
            }
        });
        await ReadAsync(SelectLongHoldings, rows =>
        {
            if (Day(rows.GetString(1)) is not { } period)
            {
                return;
            }

            if (!rows.IsDBNull(2))
            {
                Hold(rows.GetString(0), period, rows.GetString(2));
            }

            var key = HoldingMatcher.NameKey(LongMatching.InOrder(rows.GetString(3)));

            if (rows.GetString(0) == "GSPC" && key.Length > 0 && (!firstHeldByName.TryGetValue(key, out var held) || period < held))
            {
                firstHeldByName[key] = period;
            }
        });
        await ReadAsync(SelectProviderSpans, rows => provider.Add(new ProviderSpan(rows.GetString(0), Day(rows.GetValue(1)), Day(rows.GetValue(2)), rows.IsDBNull(3) ? null : rows.GetString(3))));

        return LongSpans.Of(
            snapshots.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>)pair.Value.ToDictionary(one => one.Key, one => (IReadOnlySet<string>)one.Value), StringComparer.Ordinal),
            provider,
            firstHeldByName,
            providerFrom);
    }

    // The spans written again whole inside a transaction a later run holds, the rows before removed with them, and the rows written.
    public static async Task<int> RewriteSpansAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction, IReadOnlyList<LongSpan> spans, string runId, CancellationToken cancellation = default)
    {
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = ClearSpans;
            await clear.ExecuteNonQueryAsync(cancellation);
        }

        var written = 0;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertSpan;

        var (index, ticker, joined, left, readFrom, run) = (insert.Parameters.Add("$index_code", Microsoft.Data.Sqlite.SqliteType.Text), insert.Parameters.Add("$ticker", Microsoft.Data.Sqlite.SqliteType.Text), insert.Parameters.Add("$joined", Microsoft.Data.Sqlite.SqliteType.Text), insert.Parameters.Add("$left", Microsoft.Data.Sqlite.SqliteType.Text), insert.Parameters.Add("$read_from", Microsoft.Data.Sqlite.SqliteType.Text), insert.Parameters.Add("$run_id", Microsoft.Data.Sqlite.SqliteType.Text));

        foreach (var span in spans)
        {
            (index.Value, ticker.Value, joined.Value, left.Value, readFrom.Value, run.Value) = (span.Index, span.Ticker, span.Joined.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), span.Left?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value, span.ReadFrom, runId);
            written += await insert.ExecuteNonQueryAsync(cancellation);
        }

        return written;
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
