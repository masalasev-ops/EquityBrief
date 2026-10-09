namespace EquityBrief.Core.Loop;

// A tester run as the Loop page reads it: its run, the month it is for, its index, the newest session it read, when it
// started and how many folds it tested.
public sealed record LoopRunRow(string RunId, string Month, string Index, DateOnly Through, string StartedAt, int Folds);

// A proposal as the run stored it: its family and name, the change in words or none, the rule today in words, its unit
// and units, its blocks, its adjusted p-value and whether it is at or under the bar, the stability screen's counted and
// better years and whether it passed, the paired total with the largest left out, whether its units are enough, the
// smallest difference a unit the gate detects four times in five, the folds choosing within a step, and whether it
// passed the tester.
public sealed record LoopProposalRow(
    string Family,
    string Proposal,
    string? Words,
    string Current,
    string Unit,
    int Units,
    int Blocks,
    double? Adjusted,
    bool Gate,
    bool Stable,
    int Counted,
    int Better,
    double? Trimmed,
    bool Counts,
    double? Detectable,
    int StableFolds,
    bool Passed,
    string? Finding = null);

// One figure the autopsy stated of a family's finished trades in a run: the family, the figure, its value, the trades
// it was read over and its words.
public sealed record LoopFindingRow(string Family, string Figure, double? Value, int Trades, string Words);

// One reading's spread over a family's finished listings in a run: the listings holding it, the winners and the losers
// among them, each side's median and the mean edge of each tenth in the reading's order.
public sealed record LoopReadingRow(string Family, string Reading, int Units, int Winners, int Losers, double? WinnersMedian, double? LosersMedian, IReadOnlyList<double?> Deciles);

// A family's learned score as a run stored it, the one fitted on all finished data: the setups ended before the session
// it was cut at, how many it was fitted over, its hash and its weight in words.
public sealed record LoopModelRow(string Family, DateOnly LearnedBefore, int Setups, string Hash, string Words);

// A card's rank under its index's learned score as the night stored it, none until the score passed on the index.
public sealed record LoopRankRow(string Family, string Ticker, int? Rank);

// A proposal's test year as the run stored it: the setting the fold's learning years chose or none, and each side's
// units and total edge after costs.
public sealed record LoopTestRow(
    string Family,
    string Proposal,
    int Year,
    bool Complete,
    string? Chosen,
    int CurrentUnits,
    int ProposedUnits,
    double CurrentTotal,
    double ProposedTotal);
