using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// The operator's word on a proposal, and when it is applied. A proposal is applied where the operator approved it, or,
// only with the adopt setting reading automatic, where it passed and no decision was written for it; the shipped
// setting reads on approval. A declined proposal is put again only where a later run read at least one more complete
// block than the run it was declined on, and it passed with that block included.
// see: An approved change is applied by the night's own build on the index it was approved on alone
// see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
public static class LoopDecisions
{
    public const string Approved = "approved";

    public const string Declined = "declined";

    public const string Applied = "applied";

    public const string Refused = "refused";

    // The setting that says whether a passing proposal waits for the operator's word, and its two values.
    public const string AdoptSetting = "EquityBrief:Loop:Adopt";

    public const string OnApproval = "on approval";

    public const string Automatic = "automatic";

    // The run a restore's decision is stored under in place of a tester run's, and the proposal it names.
    public const string RestoreRun = "restore";

    public static string RestoreProposal(long setting) => FormattableString.Invariant($"restore the setting before change {setting}");

    // Why a change is not applied.
    public const string BookRefused = "the S&P 500's sector heavyweights' books change their setting only by a freeze, so an approved change to one is not applied";

    public const string FamilyRefused = "the family has no setting an approval applies";

    // From 21.5 the fundamentals-first family, which the night computes no longer, takes no change.
    // see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
    public const string LeftTheNightRefused = "the fundamentals-first family is computed by the night no longer, so an approved change to it is not applied";

    // From 21.5 an approval changes a frozen swing rule's hooks alone, its setting standing where its freeze put it.
    // see: The engines test a frozen rule at the levels it states, and a frozen rule's setting moves only by a freeze
    public const string FrozenSettingRefused = "a frozen rule's setting moves only by a freeze, so an approval changes its hooks alone";

    // The families an approval changes on every index, the S&P 500's pullback by its hooks or a shape move from 17.14, and
    // from 21.5 the short-term dip, Quality at a fair price and the Fundamentals family on every index and the sector
    // heavyweights on the S&P 400 and 600, each by a registration of its live rule again at the change; the
    // fundamentals-first family, which the night computes no longer, is not among them.
    // see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
    // see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
    public static IReadOnlyList<string> Families { get; } =
        [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, DipRule.Name, QualityRule.Name, ImprovingRule.Name, HeavyweightRule.Name];

    // The S&P 500's families an approval changes by its stored settings and a registration together.
    public static IReadOnlyList<string> LargeFamilies { get; } = [BreakoutRule.Name, DriftRule.Name];

    // From 21.5 the seven families the monthly loop proposes for, in the order Tonight draws their cards: the pullback, the
    // breakout, the drift, the Fundamentals family, the short-term dip, the sector heavyweights and Quality at a fair
    // price; the fundamentals-first family, which the night computes no longer, is proposed for no more.
    // see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
    public static IReadOnlyList<string> LoopFamilies { get; } =
        [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, ImprovingRule.Name, DipRule.Name, HeavyweightRule.Name, QualityRule.Name];

    // Where a family stands in that order, counted from one, and past every family for a word it does not hold.
    public static int LoopOrderOf(string family)
    {
        for (var at = 0; at < LoopFamilies.Count; at++)
        {
            if (string.Equals(LoopFamilies[at], family, StringComparison.Ordinal))
            {
                return at + 1;
            }
        }

        return LoopFamilies.Count + 1;
    }

    // The sessions a rule a change replaced runs beside the change before the apply step retires it, a block, beside it
    // as a variant or, from 21.5, in the background where its family holds nine rules.
    // see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
    public const int ReplacedSessions = 63;

    // Why an approved change to a family on an index is not applied, none where it is.
    public static string? Refusal(string index, string family) =>
        family == HeavyweightRule.Name && index == MarketCloses.Index ? BookRefused
        : family == FundamentalsRule.Name ? LeftTheNightRefused
        : !Families.Contains(family, StringComparer.Ordinal) ? FamilyRefused
        : null;

    // From 21.5 why a change to a family keeping no stored setting is not applied where no live rule of it stands
    // registered on the index, a heavyweights book standing provisional or a dip or book before its freeze.
    public const string ProvisionalBookRefused = "a sector heavyweights' book standing provisional changes its setting only by its freeze, so an approved change to it is not applied";

    public const string NoRuleRefused = "the family holds no live rule registered on the index for a change to replace, so an approval waits for its freeze";

    // The same with whether the family's live rule stands registered on the index, which a family keeping no stored
    // setting changes by alone.
    public static string? Refusal(string index, string family, bool registered) =>
        Refusal(index, family)
        ?? (KeepsNoSetting(family) && !registered ? (family == HeavyweightRule.Name ? ProvisionalBookRefused : NoRuleRefused) : null);

    // From 21.5 the families keeping no stored setting, each changed by a registration of its live rule alone: the dip, the
    // two books and the sector heavyweights.
    public static bool KeepsNoSetting(string family) =>
        family is DipRule.Name or QualityRule.Name or ImprovingRule.Name or HeavyweightRule.Name;

    // From 21.5 a family as the register counts it on an index: the dip and the two books by their name on the index, the
    // S&P 500's other families by their own name, and the S&P 400's and 600's by their name on the index.
    public static string RegisterFamilyOf(string index, string family) =>
        family is DipRule.Name or QualityRule.Name or ImprovingRule.Name ? Candidates.OwnStepCandidate.FamilyOn(family, index)
        : index == MarketCloses.Index ? family
        : Candidates.IndexRuleCandidate.FamilyOn(family, index);

    // From 21.5 whether an approved change reaches its family through a registration of the live rule standing on its
    // index, other than the S&P 500's breakout and drift, which have their own path: a frozen swing or heavyweights family
    // on the S&P 400 or 600, and the dip and the two books on any index, each where a live rule of it stands registered.
    // see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
    public static bool RegistersOn(string index, string family, IReadOnlyList<Candidates.RegisterRow> standing) =>
        family != FundamentalsRule.Name
        && (index != MarketCloses.Index || family is DipRule.Name or QualityRule.Name or ImprovingRule.Name)
        && Candidates.FamilyRecords.LiveOf(Candidates.CandidateFamily.In(standing, RegisterFamilyOf(index, family))) is not null;

    // The one proposal of a family a run puts to the operator: of those that passed and state a change, leaving out one
    // an earlier run's decline holds back, the lowest adjusted p-value, a tie settled by the proposal's name; none where
    // none is left.
    public static string? PutOf(IEnumerable<(string Proposal, double? Adjusted, bool Passed, bool Changes, bool HeldBack)> family) =>
        family.Where(one => one.Passed && one.Changes && !one.HeldBack)
            .OrderBy(one => one.Adjusted ?? double.PositiveInfinity)
            .ThenBy(one => one.Proposal, StringComparer.Ordinal)
            .Select(one => one.Proposal)
            .FirstOrDefault();

    public static LoopProposalRow? PutOf(IEnumerable<LoopProposalRow> family, Func<LoopProposalRow, bool>? heldBack = null)
    {
        var rows = family.ToArray();
        var put = PutOf(rows.Select(one => (one.Proposal, one.Adjusted, one.Passed, one.Change is not null, heldBack?.Invoke(one) ?? false)));

        return rows.FirstOrDefault(one => one.Proposal == put);
    }

    // Whether a proposal whose change reads a release reading is held back from being put: its total less the rule's over
    // the test years from the local model's cutoff's year is not above nothing, which the years before the cutoff, whose
    // outcomes the model may have known, cannot make up. None is held back by it where no reading version's history read
    // is complete, since no change then reads a release reading; nothing is retired or hidden by it.
    // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
    public static bool HeldByCutoff(string? words, IEnumerable<(int Year, int Units, double Proposed, double Current)> years, DateOnly? cutoff)
    {
        if (cutoff is not { } day || !LedgerReleases.Reads(words))
        {
            return false;
        }

        var sides = LedgerReleases.Split(years, day);

        return sides.AfterProposed - sides.AfterCurrent <= 0;
    }

    // What a proposal the cutoff holds back says, on the Loop page and in the month's report.
    public static string HeldByCutoffWords(DateOnly cutoff) =>
        FormattableString.Invariant($"Held back: it reads a release reading, and its total less the rule's over the test years from the cutoff's year, {cutoff.Year}, is not above nothing, so it is not put to you.");

    // Whether a declined proposal is put to the operator again: a later run read more complete blocks than the run it
    // was declined on, and it passed with them.
    public static bool PutAgain(int blocksDeclined, int blocksNow, bool passedNow) => passedNow && blocksNow > blocksDeclined;

    // Whether a proposal is applied: approved, or passed and undecided where the setting reads automatic and never a
    // proposal the operator declined.
    public static bool Applies(string adopt, string? decision, bool passed) =>
        decision == Approved || (adopt == Automatic && decision is null && passed);

    // The adopt setting as the settings state it, the shipped value where none is stated; any other value is refused.
    public static string AdoptOf(string? stated) =>
        stated switch
        {
            null or "" or OnApproval => OnApproval,
            Automatic => Automatic,
            _ => throw new ArgumentException($"{AdoptSetting} reads '{stated}', and it takes '{OnApproval}' or '{Automatic}'.", nameof(stated)),
        };
}
