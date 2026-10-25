using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Candidates;

// A rule of a family its own step keeps on its index, as a registered candidate: the family on its index, so the S&P
// 500's family evaluator, which reads its own families by name, never reads it, the family rules' recorder, replay and
// cards pass it over as they pass over an S&P 400's or 600's rule, and its correction for luck counts its own index's
// rules of the family alone, at most nine. It fires on no member through the family evaluator; its family's step reads
// its settings off its registration and keeps its record under its name.
// see: A quality rule is registered as the family on its index and kept by the family's own step alone
public abstract class OwnStepCandidate(string index) : FamilyRuleEvaluator
{
    // The index the rule reads, by its code in the store.
    public string Index { get; } = index;

    // The setup family the rule is a rule of, by the word the store keeps it under.
    public abstract string SetupFamily { get; }

    public override string Name => SetupFamily + "-" + WordOf(Index);

    public override string Family => FamilyOn(SetupFamily, Index);

    // The family on an index, which the register groups the index's rules of the family under.
    public static string FamilyOn(string family, string index) => family + " on the " + NameOf(index);

    // The name a reader knows each index by, and the word the evaluator's name carries.
    public static string NameOf(string index) => index switch
    {
        FundHoldings.MidCapIndex => "S&P 400",
        FundHoldings.SmallCapIndex => "S&P 600",
        _ => "S&P 500",
    };

    public static string WordOf(string index) => index switch
    {
        FundHoldings.MidCapIndex => "400",
        FundHoldings.SmallCapIndex => "600",
        _ => "500",
    };

    // Never evaluated over a member through the family evaluator, and fires on none.
    public override FamilyResult EvaluateMember(FamilyMember member, IReadOnlyDictionary<string, double> parameters) =>
        new(member.Breakout.Ticker, Family, [], null, null, null, null, []);

    public override double? TypicalMoveOf(FamilyMember member) => null;
}
