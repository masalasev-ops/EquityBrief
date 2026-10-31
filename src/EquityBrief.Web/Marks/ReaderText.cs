using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EquityBrief.Web.Marks;

// The words Paper trading, the Loop page and the Ledger draw from what the night, the tester and the rules stored, put
// into the reader's words as each page is drawn. A stored proposal's change, a finding's sentence, a rule's own words and
// the pages' own templates all reach the reader through these phrases, in this order, the longer and the more particular
// first, so a term of the word list keeps its meaning where it has two: an upper edge of a tenth is its top and a rule's
// edge is its gain over the easy alternative. Only the text between tags is read, never an attribute, a script or a
// style, and a word written all in capitals is a ticker and stands as it is. The stored rows and the rules' sources are
// not touched, so no rule's version moves.
// see: Paper trading, the Loop page and the Ledger draw every stored word through one table of phrases into the reader's words
public static class ReaderText
{
    // Each phrase and what the reader reads in its place. A replacement keeps a capital the phrase opened on.
    public static IReadOnlyList<(string Phrase, string Words)> Phrases { get; } =
    [
        (@"upper edge of its tenth", "top of its tenth"),
        (@"the gate: an adjusted p-value of", "could this be luck: a chance of"),
        (@"an adjusted p-value of", "a chance it is luck of"),
        (@"an adjusted p at or", "a chance it is luck at or"),
        (@"adjusted p-values?", "chance it is luck"),
        (@"adjusted p", "chance it is luck"),
        (@"step-down", "judging together with the other changes tried"),
        (@"winners against losers", "what the winning picks had in common"),
        (@"the idea generator", "an idea the local model put"),
        (@"idea generator", "idea the local model put"),
        (@"the learned score", "the score fitted to past setups"),
        (@"learned scores?", "score fitted to past setups"),
        (@"the autopsy's exits?", "the exit its trades' paths suggested"),
        (@"the autopsy's", "the trade review's"),
        (@"autops(?:y|ies)", "look at what its trades did between buy and sale"),
        (@"hooks", "settings a change can set"),
        (@"benchmarks?", "easy alternative"),
        (@"(\d+) folds", "$1 years it had never seen"),
        (@"folds", "years it had never seen"),
        (@"fold's", "year's"),
        (@"fold", "year it had never seen"),
        (@"blocks of (\d+) sessions", "runs of $1 trading days"),
        (@"weekly blocks", "weekly runs"),
        (@"blocks", "runs of 63 trading days"),
        (@"block", "run of 63 trading days"),
        (@"edge, risks", "gain over the easy alternative, per $$1 risked"),
        (@"edges", "gains over the easy alternative"),
        (@"edge", "gain over the easy alternative"),
        (@"result, risks", "result, per $$1 risked"),
        (@"in risks a trade", "per $$1 risked a trade"),
        (@"(\d[\d,]*(?:\.\d+)?) risks (above|under|up|below|over the buy)", "$1 times its risk $2"),
        (@"([+\-−]?)(\d[\d,]*(?:\.\d+)?) risks", "$1$$$2 per $$1 risked"),
        (@"risks", "per $$1 risked"),
        (@"reward to risk (?:of )?at least (\d[\d.]*)", "risk $$1 to make at least $$$1"),
        (@"reward to risk", "reward for the risk"),
        (@"breadth at least (\d[\d.]*%)", "at least $1 of the index's stocks above their 200-day average"),
        (@"breadth floors?", "market check"),
        (@"breadth", "share of stocks above their 200-day average"),
        (@"live rules", "live setups"),
        (@"live rule", "live setup"),
        (@"the proposal's", "the change's"),
        (@"proposal's", "change's"),
        (@"the proposals", "the changes tried"),
        (@"proposals", "changes tried"),
        (@"the proposal", "the change tried"),
        (@"a proposal's", "a change tried's"),
        (@"a proposal", "a change tried"),
        (@"proposal", "change tried"),
        (@"live and provisional", "live and not-yet-tested"),
        (@"live or provisional", "live or not-yet-tested"),
        (@"provisional", "not yet tested"),
        (@"variants", "versions"),
        (@"variant's", "version's"),
        (@"variant", "version"),
        (@"held back", "set aside"),
        (@"not held", "not met"),
        (@"held on closes to", "kept on closes up to"),
        (@"setups held it", "setups had it"),
        (@"held enough", "had enough"),
        (@"held at", "stayed at"),
        (@"held (\d+) sessions", "kept $1 trading days"),
        (@"better; held", "better; met"),
        (@"held", "kept"),
        (@"one gate short", "one step short"),
        (@"the gate detects", "the test detects"),
        (@"trade gates?", "reward worth the risk"),
        (@"gates", "steps"),
        (@"gate", "step"),
        (@"(\d+)-session", "$1-day"),
        (@"session by session", "day by day"),
        (@"sessions", "trading days"),
        (@"session's", "trading day's"),
        (@"session", "trading day"),
        (@"typical moves", "typical days' moves"),
        (@"typical move", "typical day's move"),
        (@"it trails", "its stop rises"),
        (@"trailing", "rising"),
        (@"trails", "rising stops"),
        (@"a trail", "a rising stop"),
        (@"trail", "rising stop"),
        (@"a book's", "the holdings'"),
        (@"a book is", "holdings are"),
        (@"a book", "holdings"),
        (@"each book", "each set of holdings"),
        (@"books", "holdings"),
        (@"book's", "holdings'"),
        (@"book", "holdings"),
        (@"families", "setups"),
        (@"family's", "setup's"),
        (@"family", "setup"),
        (@"the list", "tonight's picks"),
        (@"listings", "picks"),
        (@"listing's", "pick's"),
        (@"listing", "pick"),
        (@"reaction sessions?", "report day"),
        (@"prints", "reports"),
        (@"print", "report"),
        (@"expected values?", "average made by the past setups most like it after costs"),
        (@"near miss(?:es)?", "one step short"),
        (@"its caps?", "its time limit"),
        (@"triggers", "signals to buy"),
        (@"trigger", "signal to buy"),
        (@"sweeps", "searches over settings"),
        (@"sweep", "search over settings"),
        (@"frozen", "fixed"),
        (@"freezes", "fixings of a setup's settings"),
        (@"freeze", "fixing of a setup's settings"),
        (@"rebalances", "reshuffles"),
        (@"rebalance", "reshuffle"),
        (@"tranches", "zones of the levels plan"),
        (@"tranche", "zone of the levels plan"),
        (@"ladders", "levels plans"),
        (@"ladder", "levels plan"),
        (@"funnel", "narrowing of the stocks"),
        (@"windows", "spans of trading days"),
        (@"window", "span of trading days"),
        (@"stretches", "quiet spells"),
        (@"stretch", "quiet spell"),
        (@"marks", "usual longest quiet runs"),
        (@"mark", "usual longest quiet run"),
        (@"nought", "zero"),
    ];

    static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.CultureInvariant);

    static readonly Regex Opening = new(@"^<(script|style)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Closing = new(@"^</(script|style)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly (Regex Pattern, string Words)[] Rules =
        [.. Phrases.Select(phrase => (new Regex(@"(?<![\w'’-])" + phrase.Phrase + @"(?![\w'’-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), phrase.Words))];

    // The markup with every text between its tags in the reader's words, the tags as they were.
    public static string Page(string markup)
    {
        var drawn = new StringBuilder(markup.Length);
        var from = 0;
        var inside = false;

        foreach (Match tag in Tag.Matches(markup))
        {
            var text = markup[from..tag.Index];

            drawn.Append(inside ? text : Text(text));
            drawn.Append(tag.Value);
            inside = Opening.IsMatch(tag.Value) || (inside && !Closing.IsMatch(tag.Value));
            from = tag.Index + tag.Length;
        }

        var rest = markup[from..];

        return drawn.Append(inside ? rest : Text(rest)).ToString();
    }

    // One text between tags, read as the reader sees it and written back escaped where it changed.
    static string Text(string text)
    {
        if (text.Length == 0 || !text.Any(char.IsLetter))
        {
            return text;
        }

        var read = text.Contains('&', StringComparison.Ordinal) ? WebUtility.HtmlDecode(text) : text;
        var said = Words(read);

        return said == read ? text : WebUtility.HtmlEncode(said);
    }

    // A sentence in the reader's words.
    public static string Words(string text)
    {
        foreach (var (pattern, words) in Rules)
        {
            text = pattern.Replace(text, match =>
            {
                if (Ticker(match.Value))
                {
                    return match.Value;
                }

                var replaced = match.Result(words);

                return char.IsUpper(match.Value[0]) && replaced.Length > 0 && char.IsLower(replaced[0])
                    ? char.ToUpperInvariant(replaced[0]) + replaced[1..]
                    : replaced;
            });
        }

        return text;
    }

    static bool Ticker(string word) => word.Length > 1 && word.All(letter => !char.IsLetter(letter) || char.IsUpper(letter));
}
