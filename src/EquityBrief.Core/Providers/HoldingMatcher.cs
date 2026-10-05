using System.Text;

namespace EquityBrief.Core.Providers;

// A holding matched to the provider's symbols: the code it is read under, none where nothing matched, the key it was
// matched by, and every code it stood between, so an ambiguity is stated rather than settled in silence.
public sealed record HoldingMatch(string? Ticker, string? By, IReadOnlyList<string> Between);

// Matches a fund's holdings to the provider's symbols, its common stock alone: by the holding's ISIN first, filed or made
// from its CUSIP, which names one security however its ticker was later reused, so the Bed Bath & Beyond a fund held in
// 2019 reads as the provider's BBBYQ and not as the company trading as BBBY today; and where no common stock carries that
// ISIN, by its name's key, the words that tell one company from another with the corporate suffixes and the punctuation
// taken out: the provider's names first, then the names the funds' own filings carry beside an ISIN, which read a company
// renamed since under the name it was held by. A name names a company and not a security, so a match by it is kept only
// where its code traded at the snapshot's quarter end, and none is made where which codes traded is not known. Two codes
// for one ISIN are settled by which traded on that date where exactly one did; codes left standing, by a code still
// listed before a delisted one, a main exchange before another, and the code itself.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
// see: A holding matched by name is kept only where its code traded at the quarter's end
public sealed class HoldingMatcher
{
    public const string ByIsin = "isin";
    public const string ByName = "name";

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
    readonly ILookup<string, string> byFiled;

    // The provider's symbols, and the names the funds' own filings carry beside an ISIN, each with the code its ISIN
    // matched.
    public HoldingMatcher(IEnumerable<ListedSymbol> symbols, IEnumerable<(string Name, string Code)>? filed = null)
    {
        var stocks = symbols.Where(symbol => symbol.Type == ProviderSymbols.CommonStock).ToArray();

        byIsin = stocks.Where(symbol => symbol.Isin is not null).ToLookup(symbol => symbol.Isin!, StringComparer.Ordinal);
        byName = stocks.Select(symbol => (Key: NameKey(symbol.Name), Symbol: symbol)).Where(pair => pair.Key.Length > 0).ToLookup(pair => pair.Key, pair => pair.Symbol, StringComparer.Ordinal);
        byFiled = (filed ?? [])
            .Select(pair => (Key: NameKey(pair.Name), pair.Code))
            .Where(pair => pair.Key.Length > 0)
            .Distinct()
            .ToLookup(pair => pair.Key, pair => pair.Code, StringComparer.Ordinal);
    }

    // A holding's match, given which codes traded on the snapshot's quarter end. A name match is made only where that is
    // known and only to a code that traded.
    public HoldingMatch Match(FiledHolding holding, Func<string, bool>? tradedOn = null)
    {
        if (FundSnapshots.IsinOf(holding) is { } isin && byIsin[isin].ToArray() is { Length: > 0 } onIsin)
        {
            return new(Choose(onIsin, tradedOn), ByIsin, Codes(onIsin));
        }

        if (tradedOn is null)
        {
            return new(null, null, []);
        }

        var key = NameKey(holding.Name);

        if (key.Length > 0 && byName[key].Where(symbol => tradedOn(symbol.Code)).ToArray() is { Length: > 0 } onName)
        {
            return new(Choose(onName, tradedOn), ByName, Codes(onName));
        }

        if (key.Length > 0 && byFiled[key].Where(tradedOn).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() is { Length: > 0 } onFiled)
        {
            return new(onFiled[0], ByName, onFiled);
        }

        if (Cut(holding) is { } cut && cut.Where(symbol => tradedOn(symbol.Code)).ToArray() is { Length: > 0 } onCut)
        {
            return new(Choose(onCut, tradedOn), ByName, Codes(onCut));
        }

        return new(null, null, []);
    }

    // Every code a holding's name could be read as, whether or not it traded, which is what is asked about where the
    // store holds no bar of a code near the quarter's end.
    public IReadOnlyList<string> NameCandidates(FiledHolding holding)
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
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

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

    static string Choose(IReadOnlyList<ListedSymbol> found, Func<string, bool>? tradedOn)
    {
        if (found.Count > 1 && tradedOn is not null && found.Where(symbol => tradedOn(symbol.Code)).ToArray() is { Length: 1 } traded)
        {
            return traded[0].Code;
        }

        return found
            .OrderBy(symbol => symbol.Delisted)
            .ThenBy(symbol => symbol.Exchange is { } exchange && Main.Contains(exchange) ? 0 : 1)
            .ThenBy(symbol => symbol.Code, StringComparer.Ordinal)
            .First()
            .Code;
    }

    static IReadOnlyList<string> Codes(IReadOnlyList<ListedSymbol> found) =>
        [.. found.Select(symbol => symbol.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
