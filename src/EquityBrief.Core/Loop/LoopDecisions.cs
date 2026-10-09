using EquityBrief.Core.Families;

namespace EquityBrief.Core.Loop;

// The operator's word on a proposal, and when it is applied. A proposal is applied where the operator approved it, or,
// only with the adopt setting reading automatic, where it passed and no decision was written for it; the shipped
// setting reads on approval. A declined proposal is put again only where a later run read at least one more complete
// block than the run it was declined on, and it passed with that block included.
// see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
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
    public const string LargeIndexRefused = "an S&P 500 rule's page is drawn by its family's own code, so an approved change there waits on the operator's ruling of how it reaches the page";

    public const string BookRefused = "a sector heavyweights' book changes its setting only by a freeze, so an approved change to it is not applied";

    public const string FamilyRefused = "the family has no setting an approval applies";

    public const string FrozenRefused = "the family's live rule stands registered on the index and draws its page, and a registered rule changes only by a registration, which waits on the operator's ruling of how an approval registers one";

    // The swing families an approval changes on the S&P 400 and 600.
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name];

    // Why an approved change to a family on an index is not applied, none where it is.
    public static string? Refusal(string index, string family) =>
        index == MarketCloses.Index ? LargeIndexRefused
        : family == HeavyweightRule.Name ? BookRefused
        : Families.Contains(family, StringComparer.Ordinal) ? null
        : FamilyRefused;

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
