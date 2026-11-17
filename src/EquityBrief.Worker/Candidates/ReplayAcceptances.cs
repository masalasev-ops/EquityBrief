using EquityBrief.Core.Candidates;

namespace EquityBrief.Worker.Candidates;

// One acceptance the operator gave in writing: its name, the ruling it cites, the rules it accepts, read off the register as
// the standing live rules its test names, and the tables their difference over the replay may lie in.
public sealed record ReplayAcceptance(string Name, string Ruling, Func<RegisterRow, bool> Accepts, IReadOnlySet<string> Tables);

// The acceptances `register --moved --accepting <name>` reads. An acceptance is only ever given by the operator, in writing,
// for a named rule and a named change, and is written here as an entry of its own citing the ruling word for word; nothing
// else makes a moved live rule whose replay differs keep its record inside the freeze.
// see: A live rule's record never restarts inside the rule freeze, so register refuses a moved live rule its replay does not reproduce unless the operator accepted its change in writing
public static class ReplayAcceptances
{
    public const string Option = "--accepting";

    // The tables a heavyweights rule's own book and its fills are compared in, as the replay's record names them.
    static readonly IReadOnlySet<string> OwnBookAndFills = new HashSet<string>(["index_heavyweight_rule_holding", "trade_fill"], StringComparer.Ordinal);

    public static IReadOnlyList<ReplayAcceptance> Given { get; } =
    [
        new(
            "heavyweights-rule-exits",
            "the operator's written acceptance of 2026-11-17: \"This is my written acceptance for the live S&P 400 and S&P 600 sector heavyweights rules: their 11 open holdings take a stop and a take-profit from their own buy price by the book's chosen exit, as I ruled on 17 Nov. Their records carry on.\"",
            row => FamilyRecords.IsLive(row.Candidate) && CandidateEvaluators.Find(row.Evaluator) is IndexHeavyweightCandidate { Index: "MID" or "SML" },
            OwnBookAndFills),
    ];

    public static ReplayAcceptance? Named(string name) => Given.FirstOrDefault(acceptance => string.Equals(acceptance.Name, name, StringComparison.Ordinal));
}
