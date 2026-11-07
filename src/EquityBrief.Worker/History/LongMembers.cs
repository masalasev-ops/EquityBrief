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
public sealed class LongMembers : EquityBrief.Core.Components.IComponent
{
    public static EquityBrief.Core.Components.ComponentAccess Access => new(
        Stores:
        [
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.LongSnapshot, EquityBrief.Core.Components.Touch.Insert | EquityBrief.Core.Components.Touch.Delete),
            new EquityBrief.Core.Components.StoreTouch(EquityBrief.Core.Components.Store.LongHolding, EquityBrief.Core.Components.Touch.Insert | EquityBrief.Core.Components.Touch.Delete),
        ],
        Feeds: []);

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
        var filed = FiledCodes(folder);
        var matches = new Dictionary<string, IReadOnlyDictionary<(DateOnly, int), LongMatch>>(StringComparer.Ordinal);

        // The codes the symbol lists file as something other than a company's common stock, a fund, a note or a preferred
        // line, which the price key leaves out however their closes fall; a code the lists do not carry is read.
        var typed = symbols.GroupBy(symbol => symbol.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Any(symbol => symbol.Type == ProviderSymbols.CommonStock), StringComparer.Ordinal);

        foreach (var index in LongScheduleFilings.Funds.Keys)
        {
            matches[index] = LongMatching.Match([.. schedules.Where(schedule => schedule.Index == index)], closes, matcher, asked, names, name => Reported(filed, name), code => typed.TryGetValue(code, out var stock) && !stock);
        }

        return new Matched(schedules, matches, unread);
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
        var filed = FiledCodes(folder);

        foreach (var group in matched.Schedules
            .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (schedule.Index, schedule.Period, Place: place, Holding: holding)))
            .GroupBy(one => (one.Index, one.Holding.Name)))
        {
            if (group.All(one => matched.Matches[one.Index].ContainsKey((one.Period, one.Place))))
            {
                continue;
            }

            var (first, last) = (group.Min(one => one.Period), group.Max(one => one.Period));

            foreach (var code in LongMatching.Candidates(matcher, names, group.First().Holding).Concat(Reported(filed, group.Key.Name)).Distinct(StringComparer.Ordinal))
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
    static readonly System.Text.RegularExpressions.Regex Symbol = new(@"(?:under|by)\s+the\s+(?:ticker\s+|trading\s+)?symbols?\s*[""“'‘]?\s*([A-Z]{1,5}(?:\.[A-Z])?)\b[^.]{0,80}", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The filers a company search names, by CIK.
    static IReadOnlyList<string> FilersOf(string search) =>
        [.. CikOf.Matches(search).Select(one => one.Groups[1].Value.TrimStart('0')).Distinct(StringComparer.Ordinal)];

    // A filer's name and its former names as its filing index states them.
    static IReadOnlyList<string> NamesOf(string index)
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

        foreach (var (name, quarters, _) in Unmatched(Match(folder)).Take(top))
        {
            if (await ReadCodeAsync(filers, feed, name, quarters.Min(one => one.Period), quarters.Max(one => one.Period), cancellation) is { } read)
            {
                found.Add(read);
            }
        }

        await File.WriteAllTextAsync(Path.Combine(folder, FiledFile), System.Text.Json.JsonSerializer.Serialize(found, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellation);
        progress(FormattableString.Invariant($"{found.Count} name(s) read a code off their company's own annual report"));

        return found;
    }

    // A name's code as its company's own annual report from the years it was held names it, none where no filer of the name, no
    // such report or no sentence naming a code is found; every answer kept as sent, one already kept asked nothing again.
    public static async Task<FiledCode?> ReadCodeAsync(string filers, ITrustScheduleFeed feed, string name, DateOnly first, DateOnly last, CancellationToken cancellation = default)
    {
        {
            var words = SearchedAs(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            (string Cik, string Name, string Index)? filer = null;

            async Task<string> IndexOfAsync(string cik)
            {
                var indexFile = Path.Combine(filers, "submissions-" + cik + ".json");
                var index = File.Exists(indexFile) ? await File.ReadAllTextAsync(indexFile, cancellation) : await feed.IndexAsync(cik, cancellation);

                await File.WriteAllTextAsync(indexFile, index, cancellation);

                return index;
            }

            for (var kept = words.Length; kept >= 1 && filer is null; kept--)
            {
                var query = string.Join(' ', words[..kept]);
                var file = Path.Combine(filers, "search-" + string.Concat(query.Select(character => char.IsLetterOrDigit(character) ? character : '-')) + ".xml");
                var answer = File.Exists(file) ? await File.ReadAllTextAsync(file, cancellation) : await feed.CompaniesAsync(query, cancellation);

                await File.WriteAllTextAsync(file, answer, cancellation);

                var best = (Shared: 0.0, Filer: ((string, string, string)?)null);

                foreach (var cik in FilersOf(answer).Take(FilersRead))
                {
                    var filings = await IndexOfAsync(cik);

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

            var report = FilerFilings.Read([chosen.Index])
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

    // Whether a schedule's holding is matched and priced.
    public static Func<LongSchedule, int, bool> IsMatched(Matched matched) =>
        (schedule, place) => matched.Matches[schedule.Index].ContainsKey((schedule.Period, place));

    // Why the run stops over a capture, none where nothing does: each quarter end under a floor and each fall by half left out
    // that no check by hand has settled.
    public static IReadOnlyList<string> Stops(Matched matched, Func<LongFall, bool> checkedByHand) =>
        LongFloors.Stops(LongFloors.Coverage(matched.Schedules, IsMatched(matched)), LongFloors.Falls(matched.Schedules, IsMatched(matched)), checkedByHand);

    const string InsertSnapshot = @"
        INSERT INTO long_snapshot (index_code, period, accession, form, holdings, value_usd, run_id)
        VALUES ($index_code, $period, $accession, $form, $holdings, $value_usd, $run_id);";

    const string InsertHolding = @"
        INSERT INTO long_holding (index_code, period, holding, name, industry, shares, value_usd, ticker, matched_by, between, checked, run_id)
        VALUES ($index_code, $period, $holding, $name, $industry, $shares, $value_usd, $ticker, $matched_by, $between, $checked, $run_id);";

    const string ClearRun = "DELETE FROM long_holding; DELETE FROM long_snapshot;";

    // The run's schedules and their holdings written whole in one transaction, each holding with the code and the key it was
    // matched by or none, the run before's rows removed with them; nothing written where a stop stands.
    public static async Task<int> WriteAsync(string databaseFile, string runId, Matched matched, IReadOnlyDictionary<string, string> forms, IReadOnlyDictionary<(string Index, string Name), string> checks, CancellationToken cancellation = default)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(EquityBrief.Data.StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = connection.BeginTransaction();
        var written = 0;

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
                    ("$between", match is null ? null : System.Text.Json.JsonSerializer.Serialize(match.Between)),
                    ("$checked", checks.GetValueOrDefault((schedule.Index, holding.Name))),
                    ("$run_id", runId));
            }
        }

        await transaction.CommitAsync(cancellation);

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
