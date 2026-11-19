using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EquityBrief.Web.Marks;

// The words Paper trading, the Loop page and the Ledger draw from what the night, the tester and the rules stored, put
// into the reader's words as each page is drawn. A stored proposal's change, a finding's sentence, a rule's own words and
// the pages' own templates all reach the reader through these phrases, in this order, the longer and the more particular
// first, so a term of the word list keeps its meaning where it has two: an upper edge of a tenth is its top and a rule's
// edge is its gain over the easy alternative. Only the text between tags is read, never an attribute, a script or a
// style, and a word written all in capitals is a ticker and stands as it is. A company's name and anything else a filer
// or a provider wrote is drawn inside an element marked as filed, and the text inside it stands as it was written, so
// Block, Inc. is never "Run of 63 trading days, Inc". The stored rows and the rules' sources are not touched, so no rule's
// version moves.
// see: Paper trading, the Loop page and the Ledger draw every stored word through one table of phrases into the reader's words
public static class ReaderText
{
    // The attribute an element carries where its text is a company's name or other words a filer or a provider wrote.
    public const string AsFiled = "data-as-filed";

    // The attribute as an element's opening tag writes it.
    public const string AsFiledMark = " " + AsFiled;

    // The attribute an element carries where its text is a sentence the operator worded, which the page draws as worded:
    // "Each position risks 1%" uses risks as a verb, never the tester's unit.
    // see: Each rule's paper account sizes a position by its risk and caps it at a tenth of the account, whatever a pick's card is capped at
    public const string AsWorded = "data-as-worded";

    public const string AsWordedMark = " " + AsWorded;

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
        (@"pooled", "all three indexes together"),
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
        (@"live rule", "running revision"),
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
        (@"live setups'", "running revisions'"),
        (@"live setups", "running revisions"),
        (@"live setup's", "running revision's"),
        (@"live setup", "running revision"),
        (@"versions", "revisions"),
        (@"version's", "revision's"),
        (@"version", "revision"),
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

    static readonly Regex Filed = new(@"^<([a-z][a-z0-9]*)\b[^>]*\s(?:" + AsFiled + "|" + AsWorded + @")(?:[\s=/>])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Named = new(@"^<(/?)([a-z][a-z0-9]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly (Regex Pattern, string Words)[] Rules =
        [.. Phrases.Select(phrase => (new Regex(@"(?<![\w'’-])" + phrase.Phrase + @"(?![\w'’-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), phrase.Words))];

    // The revision words alone, for a page whose other words stand as they are: Past picks and the Run page's filter name a
    // rule's revisions and variants, and no version, live setup or provisional rule. The longer phrase first.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    public static IReadOnlyList<(string Phrase, string Words)> RevisionPhrases { get; } =
    [
        (@"the live list recommended", "the rules picked"),
        (@"the live list recommends", "a rule picks"),
        (@"the live list drew", "the rules picked"),
        (@"the live list picked", "the rules picked"),
        (@"the live lists?", "the running revisions' picks"),
        (@"lists recommended", "rules picked"),
        (@"lists kept", "rules kept"),
        (@"left its list open", "left its picks open"),
        (@"on the (S&P \d+)'s list", "on the $1's picks"),
        (@"their rules' own lists", "their rules' own picks"),
        (@"this table's list held", "this table's rule held"),
        (@"the index's list in place", "the index's page in place"),
        (@"its list drew it", "its rule picked it"),
        (@"Filters live in the address", "Filters are kept in the address"),
        (@"went live", "started running"),
        (@"go live", "run"),
        (@"under every version", "under every revision"),
        (@"this version's", "the running revision's"),
        (@"provisional settings", "starting settings"),
        (@"live and provisional", "running and not-yet-tested"),
        (@"live or provisional", "running or not-yet-tested"),
        (@"provisional", "not yet tested"),
        (@"live setups'", "running revisions'"),
        (@"live setups", "running revisions"),
        (@"live setup's", "running revision's"),
        (@"live setup", "running revision"),
        (@"live rules", "running revisions"),
        (@"live rule's", "running revision's"),
        (@"live rule", "running revision"),
        (@"live (pullback|breakout|drift|dip|heavyweights|quality|Fundamentals) rule", "running $1 revision"),
        (@"(?<!reading |short )versions", "revisions"),
        (@"(?<!reading |short )version's", "revision's"),
        (@"(?<!reading |short )version", "revision"),
    ];

    static readonly (Regex Pattern, string Words)[] RevisionRules =
        [.. RevisionPhrases.Select(phrase => (new Regex(@"(?<![\w'’-])" + phrase.Phrase + @"(?![\w'’-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), phrase.Words))];

    // The markup with the revision words alone put into the reader's words, as Page puts every word, an element marked as
    // filed or as worded standing as it was written.
    public static string Revisions(string markup) => Drawn(markup, text => Replaced(text, RevisionRules));

    // The markup with every text between its tags in the reader's words, the tags as they were, and the text inside an
    // element marked as filed as it was written, however deep its own elements nest.
    public static string Page(string markup) => Drawn(markup, Words);

    static string Drawn(string markup, Func<string, string> words)
    {
        string Text(string text)
        {
            if (text.Length == 0 || !text.Any(char.IsLetter))
            {
                return text;
            }

            var read = text.Contains('&', StringComparison.Ordinal) ? WebUtility.HtmlDecode(text) : text;
            var said = words(read);

            return said == read ? text : WebUtility.HtmlEncode(said);
        }

        var drawn = new StringBuilder(markup.Length);
        var from = 0;
        var inside = false;
        string? kept = null;
        var depth = 0;

        foreach (Match tag in Tag.Matches(markup))
        {
            var text = markup[from..tag.Index];

            drawn.Append(inside || kept is not null ? text : Text(text));
            drawn.Append(tag.Value);
            inside = Opening.IsMatch(tag.Value) || (inside && !Closing.IsMatch(tag.Value));

            if (kept is null)
            {
                if (Filed.Match(tag.Value) is { Success: true } filed && !tag.Value.EndsWith("/>", StringComparison.Ordinal))
                {
                    kept = filed.Groups[1].Value;
                    depth = 1;
                }
            }
            else if (Named.Match(tag.Value) is { Success: true } named && string.Equals(named.Groups[2].Value, kept, StringComparison.OrdinalIgnoreCase))
            {
                depth += named.Groups[1].Value.Length == 0 ? 1 : -1;
                kept = depth == 0 ? null : kept;
            }

            from = tag.Index + tag.Length;
        }

        var rest = markup[from..];

        return drawn.Append(inside || kept is not null ? rest : Text(rest)).ToString();
    }

    // A sentence in the reader's words.
    public static string Words(string text) => Replaced(FieldWords.Words(text), Rules);

    static string Replaced(string text, IReadOnlyList<(Regex Pattern, string Words)> rules)
    {
        foreach (var (pattern, words) in rules)
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
