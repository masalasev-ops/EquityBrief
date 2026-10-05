using System.Text;

namespace EquityBrief.Core.Providers;

// A holding matched to the provider's symbols: the code it is read under, none where nothing matched, the key it was
// matched by, every code it stood between, so an ambiguity is stated rather than settled in silence, whether it was
// found by the wider reading of its name, and whether a code its name reads traded at the quarter's end at a price
// other than the one the fund valued the holding at, which leaves it matched to none.
public sealed record HoldingMatch(string? Ticker, string? By, IReadOnlyList<string> Between, bool Wider = false, bool PricedOut = false);

// A code's close on a snapshot's quarter end: as the provider sends it, and with the splits the provider files after
// that day undone, since the provider sends some codes' history already divided by a later split and others as traded.
public sealed record QuarterEndClose(decimal AsSent, decimal? SplitsUndone = null);

// Matches a fund's holdings to the provider's symbols, its common stock alone: by the holding's ISIN first, filed or made
// from its CUSIP, which names one security however its ticker was later reused, so the Bed Bath & Beyond a fund held in
// 2019 reads as the provider's BBBYQ and not as the company trading as BBBY today; and where no common stock carries that
// ISIN, by its name's key, the words that tell one company from another with the corporate suffixes and the punctuation
// taken out: the provider's names first, then the names the funds' own filings carry beside an ISIN, which read a company
// renamed since under the name it was held by, and, where neither finds one, the names sharing its first word and most of
// its words. A name names a company and not a security, so a match by it stands only where its code traded at the
// snapshot's quarter end at a close within a tolerance of the price the fund valued each share at, which tells common
// stock from a company's units or preferred lines and a company from another that took its code, and none is made where
// which codes traded is not known. Two codes for one ISIN are settled by which traded on that date where exactly one did;
// codes left standing, by a main exchange before another, a code still listed before a delisted one, and the code itself.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
// see: A holding matched by name is kept only where its code traded at the quarter's end
public sealed class HoldingMatcher
{
    public const string ByIsin = "isin";
    public const string ByName = "name";

    // How far the price the fund valued a share at may sit from a code's close on the quarter's end, as a share of the
    // close, for a match by name to stand.
    public const decimal ValueTolerance = 0.05m;

    // The share of a name's words, counted over both names, the wider reading needs in common with a symbol's, and how
    // many of the closest symbols it reads.
    public const double WiderOverlap = 0.5;
    public const int WiderRead = 5;

    // The words a company's name carries that say what kind of company it is and not which, a non-voting class's mark
    // among them.
    static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
    {
        "INC", "INCORPORATED", "CORP", "CORPORATION", "CO", "COMPANY", "LTD", "LIMITED", "PLC", "LLC", "LP", "NV", "SA",
        "THE", "CLASS", "CL", "HOLDINGS", "HOLDING", "GROUP", "A", "B", "COM", "ORD", "SHS", "NEW", "NVS",
    };

    // The exchanges a company's main listing trades on, read before an over-the-counter one.
    static readonly HashSet<string> Main = new(StringComparer.Ordinal) { "NYSE", "NASDAQ", "NYSE ARCA", "NYSE MKT", "AMEX", "BATS" };

    readonly ILookup<string, ListedSymbol> byIsin;
    readonly ILookup<string, ListedSymbol> byName;
    readonly ILookup<string, (string[] Words, ListedSymbol Symbol)> byFirstWord;
    readonly ILookup<string, string> byFiled;

    // The provider's symbols, and the names the funds' own filings carry beside an ISIN, each with the code its ISIN
    // matched.
    public HoldingMatcher(IEnumerable<ListedSymbol> symbols, IEnumerable<(string Name, string Code)>? filed = null)
    {
        var stocks = symbols.Where(symbol => symbol.Type == ProviderSymbols.CommonStock).ToArray();
        var keyed = stocks.Select(symbol => (Key: NameKey(symbol.Name), Symbol: symbol)).Where(pair => pair.Key.Length > 0).ToArray();

        byIsin = stocks.Where(symbol => symbol.Isin is not null).ToLookup(symbol => symbol.Isin!, StringComparer.Ordinal);
        byName = keyed.ToLookup(pair => pair.Key, pair => pair.Symbol, StringComparer.Ordinal);
        byFirstWord = keyed.ToLookup(pair => pair.Key.Split(' ')[0], pair => (pair.Key.Split(' '), pair.Symbol), StringComparer.Ordinal);
        byFiled = (filed ?? [])
            .Select(pair => (Key: NameKey(pair.Name), pair.Code))
            .Where(pair => pair.Key.Length > 0)
            .Distinct()
            .ToLookup(pair => pair.Key, pair => pair.Code, StringComparer.Ordinal);
    }

    // A holding's match, given each code's close on the snapshot's quarter end, none for a code that did not trade then.
    // A name match is made only where the closes are known, only to a code that traded at a close the holding's value
    // per share stands within the tolerance of, and by the wider reading of the name only where it is asked for.
    public HoldingMatch Match(FiledHolding holding, Func<string, QuarterEndClose?>? closeOn = null, bool wider = false)
    {
        if (FundSnapshots.IsinOf(holding) is { } isin && byIsin[isin].ToArray() is { Length: > 0 } onIsin)
        {
            return new(Choose(onIsin, closeOn is null ? null : code => closeOn(code) is not null), ByIsin, Codes(onIsin));
        }

        if (closeOn is null)
        {
            return new(null, null, []);
        }

        bool Stands(string code) => closeOn(code) is { } close && Priced(holding, close);

        var key = NameKey(holding.Name);

        if (key.Length > 0 && byName[key].Where(symbol => Stands(symbol.Code)).ToArray() is { Length: > 0 } onName)
        {
            return new(Choose(onName), ByName, Codes(onName));
        }

        if (key.Length > 0 && byFiled[key].Where(Stands).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() is { Length: > 0 } onFiled)
        {
            return new(onFiled[0], ByName, onFiled);
        }

        if (Cut(holding) is { } cut && cut.Where(symbol => Stands(symbol.Code)).ToArray() is { Length: > 0 } onCut)
        {
            return new(Choose(onCut), ByName, Codes(onCut));
        }

        if (wider && Wider(holding).Where(symbol => Stands(symbol.Code)).ToArray() is { Length: > 0 } onWider)
        {
            return new(Choose(onWider), ByName, Codes(onWider), Wider: true);
        }

        return new(null, null, [], PricedOut: NameCandidates(holding, wider).Any(code => closeOn(code) is not null));
    }

    // Every code a holding's name could be read as, whether or not it traded, which is what is asked about where the
    // store holds no bar of a code near the quarter's end; with the wider reading's where it is asked for.
    public IReadOnlyList<string> NameCandidates(FiledHolding holding, bool wider = false)
    {
        if (FundSnapshots.IsinOf(holding) is { } isin && byIsin[isin].Any())
        {
            return [];
        }

        var key = NameKey(holding.Name);

        return
        [
            .. (key.Length > 0 ? byName[key].Select(symbol => symbol.Code) : [])
                .Concat(key.Length > 0 ? byFiled[key] : [])
                .Concat((Cut(holding) ?? []).Select(symbol => symbol.Code))
                .Concat(wider ? Wider(holding).Select(symbol => symbol.Code) : [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    // Whether the price the fund valued a share at stands within the tolerance of a code's close, as sent or with the
    // later splits undone; a filing stating no shares or no value is not held to it.
    static bool Priced(FiledHolding holding, QuarterEndClose close) =>
        holding.ValuePerShare is not { } price
        || Within(price, close.AsSent)
        || (close.SplitsUndone is { } undone && Within(price, undone));

    static bool Within(decimal price, decimal close) => close > 0m && Math.Abs((price / close) - 1m) <= ValueTolerance;

    // A name the fund filed at the form's full width may have been cut inside its last word, so its words but that one
    // are read as the start of a name, where they start exactly one company's.
    IReadOnlyList<ListedSymbol>? Cut(FiledHolding holding) =>
        holding.Name.Length >= FiledNameWidth
        && NameKey(holding.Name) is var key && !byName[key].Any()
        && key.Split(' ') is { Length: > 1 } words
        && string.Join(' ', words[..^1]) is var start
        && byName.Where(group => group.Key == start || group.Key.StartsWith(start + " ", StringComparison.Ordinal)).ToArray() is { Length: 1 } cut
            ? [.. cut[0]]
            : null;

    // The wider reading of a name no symbol carries word for word: the symbols, listed and delisted, whose names share its
    // first word and at least half the words the two names hold between them, the closest first and no more than a few.
    IReadOnlyList<ListedSymbol> Wider(FiledHolding holding)
    {
        var words = NameKey(holding.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0 || byName[string.Join(' ', words)].Any())
        {
            return [];
        }

        return
        [
            .. byFirstWord[words[0]]
                .Select(pair => (pair.Symbol, Shared: Shared(words, pair.Words)))
                .Where(pair => pair.Shared >= WiderOverlap)
                .OrderByDescending(pair => pair.Shared)
                .ThenBy(pair => pair.Symbol.Code, StringComparer.Ordinal)
                .Take(WiderRead)
                .Select(pair => pair.Symbol),
        ];
    }

    // The words two names hold in common as a share of the words either holds.
    static double Shared(string[] one, string[] other)
    {
        var both = one.Intersect(other, StringComparer.Ordinal).Count();
        var either = one.Union(other, StringComparer.Ordinal).Count();

        return either == 0 ? 0.0 : 1.0 * both / either;
    }

    // The width the form files a holding's name at, past which it cuts the name.
    public const int FiledNameWidth = 30;

    // A company's name reduced to the words that tell it apart: what follows a slash, a place or a state the filing
    // names the company's listing by, taken off; upper case, an ampersand read as AND, every other mark a space, and the
    // corporate suffixes dropped wherever they stand.
    public static string NameKey(string name)
    {
        var text = new StringBuilder(name.Length);
        var slash = name.IndexOf('/', StringComparison.Ordinal);

        foreach (var character in (slash > 0 ? name[..slash] : name).ToUpperInvariant().Replace("&", " AND ", StringComparison.Ordinal))
        {
            text.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !Suffixes.Contains(word)));
    }

    static string Choose(IReadOnlyList<ListedSymbol> found, Func<string, bool>? tradedOn = null)
    {
        if (found.Count > 1 && tradedOn is not null && found.Where(symbol => tradedOn(symbol.Code)).ToArray() is { Length: 1 } traded)
        {
            return traded[0].Code;
        }

        // A main exchange first, since the indices admit only stocks listed on one, so another company's line over the
        // counter that shares the name and traded then is not read before the index's own delisted listing.
        return found
            .OrderBy(symbol => symbol.Exchange is { } exchange && Main.Contains(exchange) ? 0 : 1)
            .ThenBy(symbol => symbol.Delisted)
            .ThenBy(symbol => symbol.Code, StringComparer.Ordinal)
            .First()
            .Code;
    }

    static IReadOnlyList<string> Codes(IReadOnlyList<ListedSymbol> found) =>
        [.. found.Select(symbol => symbol.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
