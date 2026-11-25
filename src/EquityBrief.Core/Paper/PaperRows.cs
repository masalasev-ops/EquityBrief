using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Paper;

// A live rule's trade kept under a plan that buys at a price, as of a night: the family and stock, the night that listed
// it and its place, the listing close and the rule's own stop, target, trail and limit, the plan's order, and what became
// of it by the night: filled at its price on a session, ended unfilled at its window's last session, or neither, waiting;
// and once filled, its end, result and sale where they came by the night.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
public sealed record PlannedTradeRow(
    string Owner,
    string Index,
    string Family,
    string Ticker,
    DateOnly Listed,
    int Place,
    decimal Entry,
    decimal? Stop,
    decimal? Target,
    decimal? Trail,
    int Cap,
    PlanOrder Order,
    DateOnly? FilledOn,
    decimal? Fill,
    bool NotFilled,
    DateOnly? EndedOn,
    double? Result,
    decimal? Sale)
{
    // Whether the rule keeping it is its family's live rule on its index, whose trades every page draws.
    public bool Live => Owner.StartsWith(Candidates.FamilyRecords.LivePrefix, StringComparison.Ordinal);

    // From the operator's correction of 2026-11-24 why it holds no fill as its fill row or its own table says, and the day that
    // was written: its window passed, its plan placed no price, or its revision was replaced; none where it holds a fill.
    public string? Why { get; init; }

    public DateOnly? WhyOn { get; init; }
}

// One trade a list or a book kept, in the one shape every paper account reads: the account it belongs to, by the
// registered rule's name or the page's own list, its family, its stock, the night it was listed on, its place that night,
// the close its plan was set at and its stop, none for a book's holding, the session it ended on, its result at its fill,
// in multiples of the risk its close put up for a swing trade and as a fraction of that close for a book's holding, its
// round trip as its table stores it, in multiples of its risk or as a fraction of its buy, its fill at the next session's
// open, the session of that fill, the open the bar store holds there now, and why it holds no fill. Paper trading's page
// reads besides its target, the distance its stop trails where its table stores one, the sessions it is given, which end
// of its plan its fill opened past and its edge as the fill stored them, a holding's sector and the reason its book sold
// it, and a swing trade's exit of the menu where its table names one, with its stop's distance in the night's typical
// moves.
public sealed record PaperRow(
    string Account,
    string Family,
    string Ticker,
    DateOnly Bought,
    int Place,
    decimal Entry,
    decimal? Stop,
    DateOnly? Ended,
    double? Result,
    bool Book,
    double? CostRisk,
    double? CostFraction,
    decimal? Fill = null,
    DateOnly? FilledOn = null,
    decimal? MarkedFrom = null,
    string? Unfilled = null,
    decimal? Target = null,
    decimal? Trail = null,
    int? Cap = null,
    string? Past = null,
    double? Edge = null,
    string? Sector = null,
    string? Reason = null,
    int? BookSize = null,
    int? Exit = null,
    double? RiskMoves = null,
    DateOnly? FiredOn = null);

// What paper trading reads of a stock on the night: its company as the membership names it, its typical move and its
// 200-session average as the night's indicators stored them, and its GICS sector on the night as the membership files it,
// each none where none is stored.
public sealed record PaperStockRow(string Ticker, string? Company, double? TypicalMove, double? Average200, string? Sector = null);

// One rule's verdict in an index's newest monthly per-rule test, as the tester stored it.
public sealed record RuleTestRow(
    string RunId,
    string Month,
    string Family,
    string Rule,
    string Standing,
    string Words,
    string Against,
    string Unit,
    int? Units,
    int? Blocks,
    double? Adjusted,
    bool? Gate,
    bool? Stable,
    int? Counted,
    int? Better,
    double? Trimmed,
    bool? Counts,
    bool Passed,
    string? Why);

// One test year of one rule in that test: its units and total, and the side it was read against's.
public sealed record RuleTestYearRow(string Family, string Rule, int Year, bool Complete, int Units, double Total, int ComparedUnits, double ComparedTotal);

// One row of the candidate register, as the pages and the paper accounts read it.
public sealed record CandidateRow(
    long Id,
    string Candidate,
    string Evaluator,
    string Event,
    string? Retires,
    DateTimeOffset RegisteredAt,
    string Parameters = "{}",
    string? Evidence = null,
    string EvaluatorVersion = "");

// A trade the operator took from a card, as the store holds it: the stock and when it was taken, the card it came from,
// the stock's sector, its fill and the session it is for, whether that fill is still the plan's buy awaiting the next
// session's open and whether the operator entered it, the plan it is managed by, and the exit the operator recorded.
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
public sealed record TakenTradeRow(
    string Ticker,
    string TakenAt,
    string Index,
    string Family,
    DateOnly Night,
    string? Sector,
    decimal Fill,
    DateOnly FillDate,
    bool Provisional,
    bool Entered,
    decimal? Stop,
    decimal? Target,
    decimal? Trail,
    int? Cap,
    decimal? ExitPrice,
    DateOnly? ExitDate,
    string? FollowedThrough,
    DateOnly? EndedOn = null,
    decimal? EndPrice = null,
    string? EndReason = null,
    double? Result = null)
{
    // Open while neither the operator's exit nor the night's follower has ended it.
    public bool IsOpen => ExitDate is null && EndedOn is null;
}

// One S&P 400 or 600 night as the index families stored it: the members read, the index's own breadth, whether its
// market check left its swing lists open, the settings its rules ran on and whether its sector heavyweights rebalanced.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
public sealed record IndexNightRow(string Index, DateOnly Session, int Members, double? Breadth, bool MarketOpen, string Settings, bool Rebalanced, string? Fault = null);
