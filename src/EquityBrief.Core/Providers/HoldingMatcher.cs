using System.Text;

namespace EquityBrief.Core.Providers;

// A holding matched to the provider's symbols: the code it is read under, none where nothing matched, the key it was
// matched by, and every code it stood between, so an ambiguity is stated rather than settled in silence.
public sealed record HoldingMatch(string? Ticker, string? By, IReadOnlyList<string> Between);

// Matches a fund's holdings to the provider's symbols, its common stock alone: by the holding's ISIN first, filed or made
// from its CUSIP, which names one security however its ticker was later reused, so the Bed Bath & Beyond a fund held in
// 2019 reads as the provider's BBBYQ and not as the company trading as BBBY today; and where no common stock carries that
// ISIN, by its name's key, the words that tell one company from another with the corporate suffixes and the punctuation
// taken out. Two codes for one key are settled by which traded on the snapshot's date where exactly one did, and
// otherwise by a code still listed before a delisted one, a main exchange before another, and the code itself.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
public sealed class HoldingMatcher
{
    public const string ByIsin = "isin";
    public const string ByName = "name";

    // The words a company's name carries that say what kind of company it is and not which.
    static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
    {
        "INC", "INCORPORATED", "CORP", "CORPORATION", "CO", "COMPANY", "LTD", "LIMITED", "PLC", "LLC", "LP", "NV", "SA",
        "THE", "CLASS", "CL", "HOLDINGS", "HOLDING", "GROUP", "A", "B", "COM", "ORD", "SHS", "NEW",
    };

    // The exchanges a company's main listing trades on, read before an over-the-counter one.
    static readonly HashSet<string> Main = new(StringComparer.Ordinal) { "NYSE", "NASDAQ", "NYSE ARCA", "NYSE MKT", "AMEX", "BATS" };

    readonly ILookup<string, ListedSymbol> byIsin;
    readonly ILookup<string, ListedSymbol> byName;

    public HoldingMatcher(IEnumerable<ListedSymbol> symbols)
    {
        var stocks = symbols.Where(symbol => symbol.Type == ProviderSymbols.CommonStock).ToArray();

        byIsin = stocks.Where(symbol => symbol.Isin is not null).ToLookup(symbol => symbol.Isin!, StringComparer.Ordinal);
        byName = stocks.Select(symbol => (Key: NameKey(symbol.Name), Symbol: symbol)).Where(pair => pair.Key.Length > 0).ToLookup(pair => pair.Key, pair => pair.Symbol, StringComparer.Ordinal);
    }

    // A holding's match, given which codes traded on the snapshot's date where that is known.
    public HoldingMatch Match(FiledHolding holding, Func<string, bool>? tradedOn = null)
    {
        if (FundSnapshots.IsinOf(holding) is { } isin && byIsin[isin].ToArray() is { Length: > 0 } onIsin)
        {
            return new(Choose(onIsin, tradedOn), ByIsin, Codes(onIsin));
        }

        if (NameKey(holding.Name) is { Length: > 0 } key && byName[key].ToArray() is { Length: > 0 } onName)
        {
            return new(Choose(onName, tradedOn), ByName, Codes(onName));
        }

        // A name the fund filed at the form's full width may have been cut inside its last word, so its words but that
        // one are read as the start of a name, where they start exactly one company's.
        if (holding.Name.Length >= FiledNameWidth
            && NameKey(holding.Name).Split(' ') is { Length: > 1 } words
            && string.Join(' ', words[..^1]) is var start
            && byName.Where(group => group.Key == start || group.Key.StartsWith(start + " ", StringComparison.Ordinal)).ToArray() is { Length: 1 } cut)
        {
            return new(Choose([.. cut[0]], tradedOn), ByName, Codes([.. cut[0]]));
        }

        return new(null, null, []);
    }

    // The width the form files a holding's name at, past which it cuts the name.
    public const int FiledNameWidth = 30;

    // A company's name reduced to the words that tell it apart: upper case, an ampersand read as AND, every other mark
    // a space, and the corporate suffixes dropped wherever they stand.
    public static string NameKey(string name)
    {
        var text = new StringBuilder(name.Length);

        foreach (var character in name.ToUpperInvariant().Replace("&", " AND ", StringComparison.Ordinal))
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
