using EquityBrief.Core.Providers;

namespace EquityBrief.Core.History;

// A long schedule's holding matched to the provider's code: the code, the key it was matched by, and every code it stood
// between, so an ambiguity is stated rather than settled in silence.
public sealed record LongMatch(string Ticker, string By, IReadOnlyList<string> Between);

// The provider's closes at one quarter end as its day file carries them, each code's close as traded, and the codes by their
// close in cents, which the price key reads.
public sealed class QuarterCloses(IReadOnlyDictionary<string, decimal> closes)
{
    readonly ILookup<long, string> byCent = closes.ToLookup(pair => (long)Math.Round(pair.Value * 100m, MidpointRounding.AwayFromZero), pair => pair.Key);

    public decimal? CloseOf(string code) => closes.TryGetValue(code, out var close) ? close : null;

    // Every code whose close equals a value a share within half a cent.
    public IReadOnlyList<string> AtTheCent(decimal valuePerShare)
    {
        var cents = (long)Math.Round(valuePerShare * 100m, MidpointRounding.AwayFromZero);

        return
        [
            .. new[] { cents - 1, cents, cents + 1 }
                .SelectMany(near => byCent[near])
                .Where(code => Math.Abs(closes[code] - valuePerShare) < HalfACent)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    const decimal HalfACent = 0.005m;
}

// The provider's common stocks by the words their names hold, which read a name the symbol lists carry in another form: the
// codes whose names share at least half a holding's words, one of them a word few names carry.
public sealed class LongNames
{
    // A word carried by no more names than this is one that tells a company apart.
    public const int RareAt = 25;

    public const double SharedAtLeast = 0.5;

    readonly Dictionary<string, List<string>> byWord = new(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<string>> wordsOf = new(StringComparer.Ordinal);
    readonly HashSet<string> codes = new(StringComparer.Ordinal);

    // The forms the provider files a code's earlier holder under once the code passed to another: the code with _old, then
    // _old1 and on.
    public const int OlderForms = 4;

    public LongNames(IEnumerable<ListedSymbol> symbols)
    {
        ListedSymbol[] all = [.. symbols];

        codes.UnionWith(all.Select(symbol => symbol.Code));

        foreach (var symbol in all.Where(symbol => symbol.Type == ProviderSymbols.CommonStock))
        {
            var words = Words(symbol.Name);

            if (words.Count == 0 || !wordsOf.TryAdd(symbol.Code, words))
            {
                continue;
            }

            foreach (var word in words)
            {
                if (!byWord.TryGetValue(word, out var codes))
                {
                    byWord[word] = codes = [];
                }

                codes.Add(symbol.Code);
            }
        }
    }

    // A code's older forms the symbol lists carry, whatever name they file each under.
    public IReadOnlyList<string> Older(string code)
    {
        var plain = code.Contains("_old", StringComparison.Ordinal) ? code[..code.IndexOf("_old", StringComparison.Ordinal)] : code;

        return [.. new[] { plain + "_old" }.Concat(Enumerable.Range(1, OlderForms - 1).Select(form => FormattableString.Invariant($"{plain}_old{form}"))).Where(codes.Contains)];
    }

    static HashSet<string> Words(string name) =>
        [.. HoldingMatcher.NameKey(LongMatching.InOrder(name)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 1)];

    // The codes whose names share at least half a name's words, one of them rare, in the codes' order.
    public IReadOnlyList<string> Related(string name)
    {
        var words = Words(name);

        if (words.Count == 0)
        {
            return [];
        }

        return
        [
            .. words
                .Where(word => byWord.TryGetValue(word, out var codes) && codes.Count <= RareAt)
                .SelectMany(word => byWord[word])
                .Distinct(StringComparer.Ordinal)
                .Where(code => 1.0 * words.Count(word => wordsOf[code].Contains(word)) / words.Count >= SharedAtLeast)
                .Order(StringComparer.Ordinal),
        ];
    }
}

// Matches one fund's older schedules, which carry no identifier, to the provider's codes, each key stored. A holding's name
// first, as the members pull reads a name, a code kept only where it traded at the quarter end at a close the value a share
// stands within a tolerance of; then the price key, one code alone whose close equals the value a share within a cent at two
// adjacent quarter ends both holding the name, widening to every code the day files carry; then the code the next quarter's
// holding of the identical name matched, carried back where the closes move in step across the two; and every code held to
// the fund's values a share over the quarters it matched, a code whose closes do not move in step matched to none there.
// see: A holding of a schedule filed with no identifier carries the code the next coded quarter's holding of the identical name matched, where the closes move in step across them
// see: A renamed company is matched to a code its fund held by ISIN within a year where its close equals the value a share to the cent at two quarter ends
// see: A holding matched by name is kept only where its code traded at the quarter's end
// see: A holding's code is kept only where its closes move in step with the fund's values a share, and a match by name is held to the level at each quarter end as well
public static class LongMatching
{
    public const string ByName = HoldingMatcher.ByName;

    public const string ByCarried = HoldingMatcher.ByCarried;

    public const string ByPrice = "price";

    public const string ByOlder = "older";

    public const string ByHand = "hand";

    // The key a holding is matched by where its code is the one its company's own annual report names, its closes held to the
    // fund's values a share as a name's are.
    public const string ByFiling = "filing";

    // How many adjacent quarter ends the price key reads a value a share at, and the fewest quarter ends in a row a run at one
    // ratio holds.
    public const int PriceQuarters = 2;

    // How many times longer than any other code's the run settling a quarter end held by several must be.
    public const int LongestRun = 2;

    // Every holding of one fund's schedules matched, keyed by its quarter end and its place in the schedule; a holding no key
    // matched stands in none.
    public static IReadOnlyDictionary<(DateOnly Period, int Place), LongMatch> Match(
        IReadOnlyList<LongSchedule> schedules,
        IReadOnlyDictionary<DateOnly, QuarterCloses> closes,
        HoldingMatcher matcher,
        Func<DateOnly, string, decimal?>? asked = null,
        LongNames? names = null,
        Func<string, IReadOnlyList<string>>? annualReport = null)
    {
        LongSchedule[] ordered = [.. schedules.OrderBy(schedule => schedule.Period)];
        var matches = new Dictionary<(DateOnly Period, int Place), LongMatch>();

        // A code's close at a quarter end off the day file, or where the day file carries none, as the provider answered an
        // ask of the code's own prices.
        QuarterEndClose? CloseOn(DateOnly period, string code) =>
            closes.TryGetValue(period, out var day) && day.CloseOf(code) is { } close ? new QuarterEndClose(close)
            : asked?.Invoke(period, code) is { } answered ? new QuarterEndClose(answered)
            : null;

        // The name, as the members pull reads one, the wider reading where the plain one finds none.
        foreach (var schedule in ordered)
        {
            for (var place = 0; place < schedule.Holdings.Count; place++)
            {
                var filed = Filed(schedule.Holdings[place]);
                var inOrder = filed with { Name = InOrder(filed.Name) };
                var match = matcher.Match(filed, code => CloseOn(schedule.Period, code));

                if (match.Ticker is null && match.By is null && inOrder.Name != filed.Name)
                {
                    match = matcher.Match(inOrder, code => CloseOn(schedule.Period, code));
                }

                if (match.Ticker is null && match.By is null)
                {
                    match = matcher.Match(filed, code => CloseOn(schedule.Period, code), wider: true);
                }

                if (match.Ticker is { } code)
                {
                    // Where several codes stand on the name, the one whose close sits nearest the value a share: a company's own
                    // code before another's that took its name since.
                    if (match.Between.Count > 1 && filed.ValuePerShare is { } price)
                    {
                        code = match.Between
                            .Select(one => (Code: one, Close: CloseOn(schedule.Period, one)))
                            .Where(one => one.Close is not null)
                            .OrderBy(one => Math.Abs(one.Close!.AsSent - price))
                            .ThenBy(one => one.Code == code ? 0 : 1)
                            .Select(one => one.Code)
                            .FirstOrDefault() ?? code;
                    }

                    matches[(schedule.Period, place)] = new LongMatch(code, ByName, match.Between);
                }
            }
        }

        // The price key: one code alone at the cent at two adjacent quarter ends holding the identical name.
        for (var at = 0; at < ordered.Length; at++)
        {
            for (var place = 0; place < ordered[at].Holdings.Count; place++)
            {
                if (matches.ContainsKey((ordered[at].Period, place)) || ordered[at].Holdings[place].ValuePerShare is not { } price || !closes.TryGetValue(ordered[at].Period, out var here))
                {
                    continue;
                }

                foreach (var near in new[] { at - 1, at + 1 }.Where(near => near >= 0 && near < ordered.Length))
                {
                    var other = Place(ordered[near], ordered[at].Holdings[place].Name);

                    if (other is not { } beside || ordered[near].Holdings[beside].ValuePerShare is not { } price2 || !closes.TryGetValue(ordered[near].Period, out var there))
                    {
                        continue;
                    }

                    // Codes carrying one close at both quarter ends are one series under two codes, a company's old code and its
                    // new kept side by side, read as the first of them.
                    string[] both =
                    [
                        .. here.AtTheCent(price).Intersect(there.AtTheCent(price2), StringComparer.Ordinal)
                            .GroupBy(code => (here.CloseOf(code), there.CloseOf(code)))
                            .Select(series => series.Order(StringComparer.Ordinal).First()),
                    ];

                    if (both.Length == 1)
                    {
                        matches[(ordered[at].Period, place)] = new LongMatch(both[0], ByPrice, both);
                        matches.TryAdd((ordered[near].Period, beside), new LongMatch(both[0], ByPrice, both));

                        break;
                    }
                }
            }
        }

        // A name whose one code holds one ratio to the fund's values a share at two or more of its quarter ends in a row, the
        // provider's own closes divided by the splits and spin-offs after them: each quarter end in such a run matched, where no
        // other code the name reads holds a run there.
        foreach (var name in ordered.SelectMany(schedule => schedule.Holdings.Select(holding => holding.Name)).Distinct(StringComparer.Ordinal))
        {
            var held = ordered
                .Select(schedule => (Period: schedule.Period, Place: Place(schedule, name)))
                .Where(pair => pair.Place is not null)
                .Select(pair => (pair.Period, Place: pair.Place!.Value, Holding: ordered.First(schedule => schedule.Period == pair.Period).Holdings[pair.Place!.Value]))
                .ToArray();

            if (held.All(one => matches.ContainsKey((one.Period, one.Place))))
            {
                continue;
            }

            var runs = new Dictionary<(DateOnly Period, int Place), List<(string Code, int Length)>>();

            // Codes carrying one close at every one of the name's quarter ends are one series, read as the first of them; with the
            // codes the company's own annual report names for the name.
            var read = Candidates(matcher, names, held[0].Holding);
            var reported = annualReport?.Invoke(name) ?? [];
            var series = read.Concat(reported).Distinct(StringComparer.Ordinal)
                .GroupBy(code => string.Join('|', held.Select(one => CloseOn(one.Period, code)?.AsSent.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")), StringComparer.Ordinal)
                .Select(group => group.Order(StringComparer.Ordinal).First());

            foreach (var code in series)
            {
                foreach (var run in Runs(code, held, CloseOn))
                {
                    foreach (var key in run)
                    {
                        if (!runs.TryGetValue(key, out var codes))
                        {
                            runs[key] = codes = [];
                        }

                        codes.Add((code, run.Count));
                    }
                }
            }

            // Where several codes hold a run at a quarter end, the code whose run there is the longest, at least twice as long
            // as any other's: another company's price moving with the holding's for a quarter or two against the holding's own
            // code divided by its splits over years.
            foreach (var (key, codes) in runs)
            {
                var longest = codes.OrderByDescending(one => one.Length).ThenBy(one => one.Code, StringComparer.Ordinal).ToArray();

                if (!matches.ContainsKey(key) && (longest.Length == 1 || longest[0].Length >= LongestRun * longest[1].Length))
                {
                    var by = reported.Contains(longest[0].Code, StringComparer.Ordinal) && !read.Contains(longest[0].Code, StringComparer.Ordinal) ? ByFiling : ByName;

                    matches[key] = new LongMatch(longest[0].Code, by, [.. codes.Select(one => one.Code).Distinct(StringComparer.Ordinal)]);
                }
            }
        }

        // The code the next quarter's holding of the identical name matched, carried back where the closes move in step.
        for (var at = ordered.Length - 2; at >= 0; at--)
        {
            for (var place = 0; place < ordered[at].Holdings.Count; place++)
            {
                var holding = ordered[at].Holdings[place];

                if (matches.ContainsKey((ordered[at].Period, place)) || Place(ordered[at + 1], holding.Name) is not { } next
                    || !matches.TryGetValue((ordered[at + 1].Period, next), out var later))
                {
                    continue;
                }

                var steps = InStep(later.Ticker, [(ordered[at].Period, holding), (ordered[at + 1].Period, ordered[at + 1].Holdings[next])], CloseOn);

                if (steps == true)
                {
                    matches[(ordered[at].Period, place)] = new LongMatch(later.Ticker, ByCarried, [later.Ticker]);
                }
            }
        }

        // Every code held to the fund's values a share over the quarters a holding of its name matched it.
        foreach (var name in ordered.SelectMany(schedule => schedule.Holdings.Select(holding => holding.Name)).Distinct(StringComparer.Ordinal))
        {
            var held = ordered
                .Select(schedule => (Schedule: schedule, Place: Place(schedule, name)))
                .Where(pair => pair.Place is not null)
                .Select(pair => (Key: (pair.Schedule.Period, pair.Place!.Value), Holding: pair.Schedule.Holdings[pair.Place!.Value]))
                .ToArray();

            foreach (var code in held.Select(one => matches.TryGetValue(one.Key, out var match) ? match.Ticker : null).OfType<string>().Distinct(StringComparer.Ordinal).ToArray())
            {
                var under = held.Where(one => matches.TryGetValue(one.Key, out var match) && match.Ticker == code).ToArray();

                if (InStep(code, [.. under.Select(one => (one.Key.Period, one.Holding))], CloseOn) == false)
                {
                    foreach (var one in under)
                    {
                        matches.Remove(one.Key);
                    }
                }
            }
        }

        return matches;
    }

    // A holding as the members pull's matcher reads one: a company's common stock, its name, shares and value as filed and no
    // identifier.
    public static FiledHolding Filed(LongHolding holding) =>
        new(holding.Name, null, null, FundSnapshots.CommonEquity, holding.Shares, holding.Value);

    static readonly System.Text.RegularExpressions.Regex Bracketed = new(@"^(?<lead>[^(]+?)\s*\((?<inner>[^)]+)\)(?<rest>.*)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The trust's own form of a name, a part set in brackets after the word it goes before, read in order: Schwab (Charles)
    // Corp. as Charles Schwab Corp., Du Pont (E.I.) de Nemours as E.I. Du Pont de Nemours; the name itself where it holds none.
    public static string InOrder(string name)
    {
        var plain = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(The\)", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        return Bracketed.Match(plain) is { Success: true } bracketed
            ? $"{bracketed.Groups["inner"].Value.Trim()} {bracketed.Groups["lead"].Value.Trim()}{bracketed.Groups["rest"].Value}".Trim()
            : plain;
    }

    // Every code a holding's name could be read as: its name's and its name in order's as the matcher reads them more widely,
    // and the codes whose names share most of its words.
    public static IReadOnlyList<string> Candidates(HoldingMatcher matcher, LongNames? names, LongHolding holding)
    {
        var filed = Filed(holding);
        string[] read =
        [
            .. matcher.NameCandidates(filed, wider: true)
                .Concat(matcher.NameCandidates(filed with { Name = InOrder(filed.Name) }, wider: true))
                .Concat(names?.Related(holding.Name) ?? []),
        ];

        // And each code the name reads in its older forms, the earlier holder of a code passed on.
        return [.. read.Concat(read.SelectMany(code => names?.Older(code) ?? [])).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    // A holding's place in a schedule by its name as filed, none where the schedule holds no holding of that name.
    static int? Place(LongSchedule schedule, string name)
    {
        for (var place = 0; place < schedule.Holdings.Count; place++)
        {
            if (string.Equals(schedule.Holdings[place].Name, name, StringComparison.Ordinal))
            {
                return place;
            }
        }

        return null;
    }

    // The runs of a name's quarter ends in a row at which a code's close stands at one ratio to the value a share, each two or
    // more long, the ratio held within half a per cent from one to the next.
    static IEnumerable<IReadOnlyList<(DateOnly Period, int Place)>> Runs(string code, IReadOnlyList<(DateOnly Period, int Place, LongHolding Holding)> held, Func<DateOnly, string, QuarterEndClose?> closeOn)
    {
        var run = new List<(DateOnly Period, int Place)>();
        decimal? before = null;

        foreach (var (period, place, holding) in held)
        {
            decimal? ratio = holding.ValuePerShare is { } price && price > 0m && closeOn(period, code) is { AsSent: > 0m } close ? price / close.AsSent : null;

            if (ratio is { } now && before is { } then && Math.Abs((now / then) - 1m) <= HoldingMatcher.RatioTolerance)
            {
                run.Add((period, place));
            }
            else
            {
                if (run.Count >= PriceQuarters)
                {
                    yield return run;
                }

                run = ratio is null ? [] : [(period, place)];
            }

            before = ratio;
        }

        if (run.Count >= PriceQuarters)
        {
            yield return run;
        }
    }

    // Whether a code's closes move in step with a holding's values a share over the quarter ends given.
    static bool? InStep(string code, IReadOnlyList<(DateOnly Period, LongHolding Holding)> over, Func<DateOnly, string, QuarterEndClose?> closeOn) =>
        HoldingMatcher.Tracks(
        [
            .. over
                .Select(one => (Price: one.Holding.ValuePerShare, Close: closeOn(one.Period, code)))
                .Where(pair => pair.Price is not null && pair.Close is not null)
                .Select(pair => (pair.Price!.Value, pair.Close!)),
        ]);
}
