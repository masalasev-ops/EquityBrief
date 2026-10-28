using System.Text.RegularExpressions;

namespace EquityBrief.Core.News;

// Whether a story is about a member: the feed tags a story with every listing it names, and one ticker can be another
// company's on another exchange, so a story is the member's only where it carries the member's American listing and its
// title or the opening of its text names the company, by its name or by its ticker beside an American exchange or a
// ticker's own marks. A story naming the company only further in, as a holding, a peer or a parent, is about something
// else, and a ticker standing in another exchange's listing, as Roche's beside SIX, is not the member's.
// see: A story is a member's where its title or its opening names the company by its name or its American listing, and no other story is drawn or counted
public static class StoryMatch
{
    // The feed's mark for a listing on an American exchange, the one exchange every index the night reads trades on.
    public const string AmericanListing = "US";

    // How much of a story's text is its opening, read with its title for the company's name.
    public const int Opening = 600;

    // A symbol the feed tags a story with is the member's where it is its ticker bare or on the American exchange.
    public static bool American(string symbol)
    {
        var dot = symbol.LastIndexOf('.');

        return dot < 0 || string.Equals(symbol[(dot + 1)..], AmericanListing, StringComparison.OrdinalIgnoreCase);
    }

    // The words a company's own name is found by: the name less a leading "The" and its legal and share-class words, and
    // its first word alone where that word is three letters or more and not one many companies open on.
    public static IReadOnlyList<string> NameKeys(string? company)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            return [];
        }

        var words = Plain(company)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => Regex.Replace(word.Trim(',', '.'), @"\.com$", string.Empty, RegexOptions.IgnoreCase))
            .Where(word => word.Length > 0)
            .ToList();

        while (words.Count > 0 && string.Equals(words[0], "The", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }

        while (words.Count > 1 && Legal.Contains(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count == 0)
        {
            return [];
        }

        var keys = new List<string> { string.Join(' ', words) };

        if (words.Count > 1 && words[0].Length >= 3 && !Common.Contains(words[0]))
        {
            keys.Add(words[0]);
        }
        else if (words.Count > 2)
        {
            keys.Add(string.Join(' ', words.Take(2)));
        }

        return keys;
    }

    // Whether a story's title or text names the member: a key of its name as a whole word in any case, a name its
    // company trades under that its legal name does not carry, or its ticker inside brackets, after an American exchange's
    // name and a colon, after a dollar sign, with the American listing's mark, or alone in capitals where it is three
    // letters or more and no other exchange's colon or mark stands against it.
    public static bool IsAbout(string title, string text, string ticker, string? company)
    {
        var story = ForeignListing.Replace(Plain(title + " " + (text.Length > Opening ? text[..Opening] : text)), " ");

        foreach (var key in NameKeys(company).Concat(TradesAs.TryGetValue(ticker, out var names) ? names : []))
        {
            if (Regex.IsMatch(story, $@"(?<![A-Za-z0-9]){Regex.Escape(key)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        var symbol = Regex.Escape(ticker);

        return Regex.IsMatch(story, $@"(?:\(|\b(?:NYSE|NASDAQ|Nasdaq|NYSE American|NYSEAMERICAN|Cboe|CBOE)\s*:\s*|\$){symbol}(?![A-Za-z0-9])")
            || Regex.IsMatch(story, $@"(?<![A-Za-z0-9]){symbol}\.US(?![A-Za-z0-9])", RegexOptions.IgnoreCase)
            || (ticker.Length >= 3 && Regex.IsMatch(story, $@"(?<![A-Za-z0-9:.]){symbol}(?![A-Za-z0-9]|\.[A-Za-z])"));
    }

    // The stories a page draws and counts: the member's alone, where its company's name is known; a name no index holds
    // carries no company to hold its stories to, and keeps them as stored.
    public static IReadOnlyList<T> Members<T>(IReadOnlyList<T> stories, Func<T, (string Title, string Text)> read, string ticker, string? company) =>
        string.IsNullOrWhiteSpace(company)
            ? stories
            : [.. stories.Where(story => read(story) is var (title, text) && IsAbout(title, text, ticker, company))];

    static string Plain(string text) => text.Replace('’', '\'').Replace('‘', '\'').Replace("&amp;", "&", StringComparison.Ordinal);

    // A bracketed listing naming an exchange other than an American one, every ticker in it that exchange's; a bracket
    // naming the ticker or the symbol in capitals names no exchange.
    static readonly Regex ForeignListing = new(@"\([^()]*\b(?!(?:NYSE|NASDAQ|Cboe|CBOE|NYSEAMERICAN|NYSEARCA|NYSEMKT|AMEX|ARCA|BATS|IEX|TICKER|SYMBOL)\b)[A-Z]{2,10}\s*:[^()]*\)", RegexOptions.CultureInvariant);

    // The legal and share-class words a company's name closes on, which no story names it by.
    static readonly HashSet<string> Legal = new(
        ["Inc", "Incorporated", "Corp", "Corporation", "Co", "Company", "Companies", "Ltd", "Limited", "plc", "LLC", "LP", "L.P", "N.V", "NV", "S.A", "SA", "AG", "SE", "Holdings", "Holding", "Group", "Class", "A", "B", "C", "Trust", "REIT", "&"],
        StringComparer.OrdinalIgnoreCase);

    // First words many companies' names open on, which find a company only with the word after them.
    static readonly HashSet<string> Common = new(
        ["First", "General", "American", "United", "International", "National", "Universal", "Public", "Southern", "Northern", "Eastern", "Western", "Advanced", "Applied", "Global", "Digital", "Federal", "Royal", "Pacific", "Atlantic", "Consolidated", "Allied", "Old", "New", "Best", "Live", "Real", "Republic", "Equity", "Host", "Bank", "Energy", "Home", "Health", "Texas", "Life"],
        StringComparer.OrdinalIgnoreCase);

    // The names a company trades under that its legal name does not carry, where the stories name it by them alone.
    static readonly IReadOnlyDictionary<string, string[]> TradesAs = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["GOOGL"] = ["Google"],
        ["GOOG"] = ["Google"],
        ["META"] = ["Facebook"],
    };
}
