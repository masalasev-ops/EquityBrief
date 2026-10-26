namespace EquityBrief.Web.Marks;

// The reader's words a redesigned page speaks in, declared once in section 15.23 and held to it word for word in both
// directions by the plain-words check: each term the build uses with the reader's words for it, which no covered page
// draws outside its glossary and its tooltips; each step a setup's rule stores with what it asks in the reader's words,
// which a page names a missed step by; and what higher means under each heading of what is behind a stock.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public static class ReaderWords
{
    public static IReadOnlyList<(string Term, string Words)> Terms { get; } =
    [
        ("print", "report"),
        ("reaction session", "report day"),
        ("typical move", "typical day's move"),
        ("held", "still above the report day's low"),
        ("trade gate", "a reward worth the risk"),
        ("stretch", "quiet nights"),
        ("mark", "the usual longest quiet run"),
        ("provisional", "not yet tested"),
        ("variant", "a version, named by what it changes"),
        ("risks", "per $1 risked"),
        ("reward to risk", "risk $1 to make $n"),
        ("breadth floor", "the market check"),
        ("breadth", "the share of the index's stocks above their 200-day average"),
        ("gate", "step"),
        ("near miss", "one step short"),
        ("session", "trading day"),
        ("ladder", "the levels plan"),
        ("tranche", "a zone of the levels plan"),
        ("trigger", "the signal to buy"),
        ("sweep", "the search over settings"),
        ("freeze", "fixing a setup's settings"),
        ("frozen", "fixed"),
        ("rebalance", "reshuffle"),
        ("trail", "a rising stop"),
        ("trailing", "rising"),
        ("its cap", "its time limit"),
        ("family", "setup"),
        ("families", "setups"),
        ("listing", "tonight's picks"),
        ("the list", "tonight's picks"),
        ("book", "holdings"),
        ("funnel", "how the stocks were narrowed"),
        ("window", "within so many trading days"),
        ("live rule", "the live setup"),
        ("nought", "zero"),
        ("one gate short", "one step short"),
    ];

    // Each step by the setup whose rule stores it and its name as stored, with what a stock that passed it did and what a
    // stock one step short missed.
    public static IReadOnlyList<(string Setup, string Step, string Passed, string Missed)> Steps { get; } =
    [
        ("pullback", "market", "the market check open", "the market check closed"),
        ("pullback", "trend and strength", "in a strong enough uptrend", "not in a strong enough uptrend"),
        ("pullback", "setup", "dipped to a bounce price", "no dip to a bounce price"),
        ("pullback", "trigger", "turned back up", "has not turned back up"),
        ("pullback", "trade", "a reward worth the risk", "reward not worth the risk"),
        ("pullback", "floors", "price and trading high enough", "price or trading too low"),
        ("pullback", "profit", "profitable over its last four quarters", "not profitable over its last four quarters"),
        ("pullback", "interest cover", "earnings covering its interest", "earnings do not cover its interest"),
        ("breakout", "market", "the market check open", "the market check closed"),
        ("breakout", "new high", "at a new high", "not at a new high"),
        ("breakout", "volume", "on heavy volume", "volume too light"),
        ("breakout", "tightened", "after a quiet spell", "no quiet spell before it"),
        ("breakout", "trade", "room for its stop", "no room for its stop"),
        ("breakout", "floors", "price and trading high enough", "price or trading too low"),
        ("breakout", "profit", "profitable over its last four quarters", "not profitable over its last four quarters"),
        ("breakout", "setup", "at a new high on heavy volume", "not at a new high on heavy volume"),
        ("breakout", "index over its average", "the index above its average", "the index below its average"),
        ("breakout", "VIX under its close before", "the VIX lower than days before", "the VIX not lower than days before"),
        ("drift", "market", "the market check open", "the market check closed"),
        ("drift", "print", "reported in its last few trading days", "no report in its last few trading days"),
        ("drift", "beat", "beat its estimate", "did not beat its estimate"),
        ("drift", "reaction", "jumped on the report day", "did not jump on the report day"),
        ("drift", "volume", "on heavy volume", "volume too light"),
        ("drift", "held", "still above the report day's low", "fell below the report day's low"),
        ("drift", "trade", "a reward worth the risk", "reward not worth the risk"),
        ("drift", "floors", "price and trading high enough", "price or trading too low"),
        ("drift", "profit", "profitable over its last four quarters", "not profitable over its last four quarters"),
        ("drift", "revenue", "sales up on a year earlier", "no quarter read with sales up on a year earlier"),
        ("drift", "setup", "a fresh report it jumped on", "no fresh report it jumped on"),
        ("drift", "tone", "its results release read better, its outlook not cut", "its results release not read better, or its outlook cut"),
        ("drift", "release read", "its results release read", "its results release not read"),
        ("drift", "index over its average", "the index above its average", "the index below its average"),
        ("drift", "VIX under its close before", "the VIX lower than days before", "the VIX not lower than days before"),
        ("fundamentals", "market", "the market check open", "the market check closed"),
        ("fundamentals", "setup", "at the pullback's buy point", "not at the pullback's buy point"),
        ("fundamentals", "floors", "price and trading high enough", "price or trading too low"),
        ("fundamentals", "readings", "its reported quarters read tonight", "its reported quarters not read tonight"),
        ("fundamentals", "profit", "profitable over its last four quarters", "not profitable over its last four quarters"),
        ("fundamentals", "revenue", "sales growing fast enough", "sales not growing fast enough"),
        ("fundamentals", "margin", "margins wider than a year earlier", "margins not wider than a year earlier"),
        ("fundamentals", "cash", "enough cash from operations against its income", "too little cash from operations against its income"),
        ("fundamentals", "trend", "in an uptrend", "not in an uptrend"),
        ("dip", "market", "the market check open", "the market check closed"),
        ("dip", "floors", "price and trading high enough", "price or trading too low"),
        ("dip", "history", "enough unbroken history to read", "too little unbroken history to read"),
        ("dip", "trend", "in a long uptrend", "not in a long uptrend"),
        ("dip", "drop", "a sharp drop", "no sharp drop"),
        ("dip", "report", "no report too close", "a report too close"),
        ("dip", "typical move", "room for its stop", "no room for its stop"),
    ];

    // Each heading of what is behind a stock as the core names it, with the name a pick's card gives it and what higher
    // means under it.
    public static IReadOnlyList<(string Heading, string Named, string Higher)> Headings { get; } =
    [
        ("Momentum", "Momentum", "stronger momentum"),
        ("Valuation", "Valuation", "cheaper against its own past"),
        ("Analyst sentiment", "Analysts and insiders", "more positive analysts and insiders"),
        ("Volatility", "Volatility", "jumpier"),
        ("Size and liquidity", "Size and trading", "larger and more traded"),
        ("Financial strength", "Financial strength", "stronger finances"),
        ("Earnings quality", "Earnings quality", "better growth and margins"),
    ];

    // A missed step of a setup in the reader's words, none for a step the list does not hold.
    public static string? Missed(string setup, string? step) =>
        step is { Length: > 0 } named && Steps.FirstOrDefault(one => one.Setup == setup && one.Step == named) is { Missed: { } words } ? words : null;

    // A step of a setup a stock passed, in the reader's words, none for a step the list does not hold.
    public static string? Passed(string setup, string? step) =>
        step is { Length: > 0 } named && Steps.FirstOrDefault(one => one.Setup == setup && one.Step == named) is { Passed: { } words } ? words : null;

    // A drift's revenue step is missed two ways, told apart by the words its night stored beside the step: both quarters
    // read and the sales not up, or a quarter not read before the night. One stock's line says which, and the step's own
    // words, true of either, name it over many stocks.
    public const string RevenueNotUp = "sales not up on a year earlier";

    public const string RevenueNotRead = "its quarter's sales not read yet";

    // A missed step of a setup for one stock, the words its night stored beside the step deciding a revenue step's.
    public static string? MissedBy(string setup, string? step, string? stored) =>
        setup == EquityBrief.Core.Families.DriftRule.Name && step == EquityBrief.Core.Cards.RuleRows.Revenue && stored is { } words
            ? RevenueRead(words) ? RevenueNotUp : RevenueNotRead
            : Missed(setup, step);

    // Whether a revenue step's stored words read both quarters: they open on the change measured between them.
    public static bool RevenueRead(string stored) =>
        System.Text.RegularExpressions.Regex.IsMatch(stored, @"^revenue [+\-]?\d", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // The build's phrasings a sentence the core or the night wrote may carry, each with the reader's words a page draws in
    // its place, in the order they are read: the longer phrase before any shorter one it holds.
    static readonly (System.Text.RegularExpressions.Regex Phrase, string Words)[] Rewrites =
    [
        .. new (string Phrase, string Words)[]
        {
            (@"\bits four newest quarters sum their net income above nothing\b", "it made a profit over its last four quarters"),
            (@"\bits four newest quarters sum their net income to nothing or less\b", "it made no profit over its last four quarters"),
            (@"\bits operating income is ([\d.,]+) times its interest\b", "it earns $1 times its interest bill"),
            (@"\bit traded ([\d.,]+) million dollars a session over the (\d+) sessions to the night\b", "it traded about $$$1 million a day over the $2 trading days to the night"),
            (@"\bits round trip at the published table is ([\d.]+) of the trade's risk\b", "buying and selling it costs $1 of the plan's risk at the published rates"),
            (@"\bits round trip at the published table is ([\d.]+)% of the buy, read in per cent since the rule sets no stop\b", "buying and selling it costs $1% of the buy at the published rates, said in per cent since the setup sets no stop"),
            (@"\bthe table's largest size band reads high for a company this large\b", "the published rates read high for a company this large"),
            (@"\bmoves the session 1 session after the night\b", "falls 1 trading day after the night"),
            (@"\bmoves the session (\d+) sessions after the night\b", "falls $1 trading days after the night"),
            (@"\bnot held\b", "not read"),
            (@"\binside the (\d+)-session window\b", "within $1 trading days"),
            (@"\bthe reaction session's\b", "the report day's"),
            (@"\bthe reaction session\b", "the report day"),
            (@"\breaction sessions?\b", "report day"),
            (@"\bon the reaction\b", "on the report day"),
            (@"\b(\d+)-session\b", "$1-day"),
            (@"\bsession\(s\)", "trading day(s)"),
            (@"\bsessions'", "trading days'"),
            (@"\bsession's\b", "trading day's"),
            (@"\bsessions\b", "trading days"),
            (@"\bsession\b", "trading day"),
            (@"\btypical moves\b", "typical days' moves"),
            (@"\btypical move\b", "typical day's move"),
            (@"\bthe triggers?\b", "the signal to buy"),
            (@"\btriggers?\b", "signal to buy"),
            (@"\bone gate short\b", "one step short"),
            (@"\bgates\b", "steps"),
            (@"\bgate\b", "step"),
            (@"\bprovisional settings\b", "starting settings"),
            (@"\bprovisional rule\b", "starting rule"),
            (@"\bprovisional\b", "not yet tested"),
            (@"\bvariants\b", "versions"),
            (@"\bvariant\b", "version"),
            (@"\buntil its freeze\b", "until its settings are fixed"),
            (@"\bbefore its freeze\b", "before its settings were fixed"),
            (@"\bat the freeze\b", "when its settings were fixed"),
            (@"\bits freeze\b", "the fixing of its settings"),
            (@"\bthe freeze\b", "the fixing of the settings"),
            (@"\bfreezes\b", "fixings of settings"),
            (@"\bfreeze\b", "fixing of settings"),
            (@"\bfrozen\b", "fixed"),
            (@"\brebalanced\b", "reshuffled"),
            (@"\brebalances\b", "reshuffles"),
            (@"\brebalance\b", "reshuffle"),
            (@"\bheld\b", "kept"),
            (@"\bthe listing close\b", "the close the night it was picked"),
            (@"\bthe listing\b", "the night it was picked"),
            (@"\blistings\b", "picks"),
            (@"\blisting\b", "picking"),
            (@"\bthe lists\b", "the setups' picks"),
            (@"\bthe list\b", "tonight's picks"),
            (@"\bprints\b", "reports"),
            (@"\bprint\b", "report"),
            (@"\btrailing\b", "rising"),
            (@"\btrails\b", "rises"),
            (@"\btrailed\b", "raised"),
            (@"\btrail\b", "rising stop"),
            (@"\bthe book\b", "its holdings"),
            (@"\bbooks\b", "holdings"),
            (@"\bbook\b", "holdings"),
            (@"\bfunnel\b", "narrowing"),
            (@"\bstretches\b", "quiet runs"),
            (@"\bstretch\b", "quiet run"),
            (@"\breward to risk\b", "reward against the risk"),
            (@"\bfamilies\b", "setups"),
            (@"\bfamily\b", "setup"),
            (@"\bthe breadth floor\b", "the market check's floor"),
            (@"\bbreadth\b", "share above the 200-day average"),
            (@"\bnought\b", "zero"),
            (@"\bwindows\b", "periods"),
            (@"\bwindow\b", "period"),
            (@"\bthe live rule\b", "the live setup"),
            (@"\blive rule\b", "live setup"),
            (@"\bladders\b", "levels plans"),
            (@"\bladder\b", "levels plan"),
            (@"\btranches\b", "zones"),
            (@"\btranche\b", "zone"),
            (@"\bsweeps\b", "searches over settings"),
            (@"\bsweep\b", "search over settings"),
            (@"\brisks\b", "times the risk"),
            (@"\bnear misses\b", "stocks one step short"),
            (@"\bnear miss\b", "one step short"),
        }.Select(rewrite => (new System.Text.RegularExpressions.Regex(rewrite.Phrase, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant), rewrite.Words)),
    ];

    // A sentence the core or the night wrote, said in the reader's words: each phrasing of the build's above rewritten in
    // any case, a phrase opening on a capital rewritten to words opening on one, and a word written all in capitals left
    // as it stands, since that is a ticker, HELD among them.
    public static string Plain(string sentence)
    {
        var said = sentence;

        foreach (var (phrase, words) in Rewrites)
        {
            said = phrase.Replace(said, match =>
            {
                if (match.Value.Length > 1 && match.Value.All(letter => !char.IsLetter(letter) || char.IsUpper(letter)))
                {
                    return match.Value;
                }

                var replaced = match.Result(words);

                return char.IsUpper(match.Value[0]) && replaced.Length > 0 ? char.ToUpperInvariant(replaced[0]) + replaced[1..] : replaced;
            });
        }

        return said;
    }
}
