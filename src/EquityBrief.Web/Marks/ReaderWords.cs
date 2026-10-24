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

    // Each step by the setup whose rule stores it and its name as stored, with what a stock one step short missed.
    public static IReadOnlyList<(string Setup, string Step, string Missed)> Steps { get; } =
    [
        ("pullback", "market", "the market check closed"),
        ("pullback", "trend and strength", "not in a strong enough uptrend"),
        ("pullback", "setup", "no dip to a bounce price"),
        ("pullback", "trigger", "has not turned back up"),
        ("pullback", "trade", "reward not worth the risk"),
        ("pullback", "floors", "price or trading too low"),
        ("pullback", "profit", "not profitable over its last four quarters"),
        ("pullback", "interest cover", "earnings do not cover its interest"),
        ("breakout", "market", "the market check closed"),
        ("breakout", "new high", "not at a new high"),
        ("breakout", "volume", "volume too light"),
        ("breakout", "tightened", "no quiet spell before it"),
        ("breakout", "trade", "no room for its stop"),
        ("breakout", "floors", "price or trading too low"),
        ("breakout", "profit", "not profitable over its last four quarters"),
        ("breakout", "setup", "not at a new high on heavy volume"),
        ("breakout", "index over its average", "the index below its average"),
        ("breakout", "VIX under its close before", "the VIX not lower than days before"),
        ("drift", "market", "the market check closed"),
        ("drift", "print", "no report in its last few trading days"),
        ("drift", "beat", "did not beat its estimate"),
        ("drift", "reaction", "did not jump on the report day"),
        ("drift", "volume", "volume too light"),
        ("drift", "held", "fell below the report day's low"),
        ("drift", "trade", "reward not worth the risk"),
        ("drift", "floors", "price or trading too low"),
        ("drift", "profit", "not profitable over its last four quarters"),
        ("drift", "revenue", "sales not up on a year earlier"),
        ("drift", "setup", "no fresh report it jumped on"),
        ("drift", "tone", "its results release not read better, or its outlook cut"),
        ("drift", "release read", "its results release not read"),
        ("drift", "index over its average", "the index below its average"),
        ("drift", "VIX under its close before", "the VIX not lower than days before"),
        ("fundamentals", "market", "the market check closed"),
        ("fundamentals", "setup", "not at the pullback's buy point"),
        ("fundamentals", "floors", "price or trading too low"),
        ("fundamentals", "readings", "its reported quarters not read tonight"),
        ("fundamentals", "profit", "not profitable over its last four quarters"),
        ("fundamentals", "revenue", "sales not growing fast enough"),
        ("fundamentals", "margin", "margins not wider than a year earlier"),
        ("fundamentals", "cash", "too little cash from operations against its income"),
        ("fundamentals", "trend", "not in an uptrend"),
        ("dip", "market", "the market check closed"),
        ("dip", "floors", "price or trading too low"),
        ("dip", "history", "too little unbroken history to read"),
        ("dip", "trend", "not in a long uptrend"),
        ("dip", "drop", "no sharp drop"),
        ("dip", "report", "a report too close"),
        ("dip", "typical move", "no room for its stop"),
    ];

    public static IReadOnlyList<(string Heading, string Higher)> Headings { get; } =
    [
        ("Momentum", "stronger momentum"),
        ("Valuation", "cheaper against its own past"),
        ("Analyst sentiment", "more positive analysts and insiders"),
        ("Volatility", "jumpier"),
        ("Size and liquidity", "larger and more traded"),
        ("Financial strength", "stronger finances"),
        ("Earnings quality", "better growth and margins"),
    ];

    // A missed step of a setup in the reader's words, none for a step the list does not hold.
    public static string? Missed(string setup, string? step) =>
        step is { Length: > 0 } named && Steps.FirstOrDefault(one => one.Setup == setup && one.Step == named) is { Missed: { } words } ? words : null;
}
