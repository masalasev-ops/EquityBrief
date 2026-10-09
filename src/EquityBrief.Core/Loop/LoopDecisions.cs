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
