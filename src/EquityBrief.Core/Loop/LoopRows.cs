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
    bool Passed);

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
