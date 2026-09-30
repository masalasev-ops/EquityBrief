namespace EquityBrief.Core.Research;

// What a review is told after the section's own ask: the draft the pass wrote from the same facts and documents, and
// the checks each of its sentences is read against, and to answer with the whole section revised in the form the
// section asks for. The revision is read by the claim checker's own rules as any draft is.
// see: A review asks a section's model to check its own draft against the section's rules, behind a setting that ships off
public static class ReviewBrief
{
    public const string Checks =
        "Read the draft a sentence at a time and keep a sentence only where it gives a reason rather than restating a figure, "
        + "weighs the size of any change it names, and is specific to this company";

    public const string RiskCheck =
        ", and where what would confirm a risk is what that risk coming true would look like";

    public static string For(string section, string draft) =>
        "Here is a draft of this section written from the same facts and documents:\n"
        + draft.Trim()
        + "\n\n"
        + Checks
        + (RiskFields.IsRisks(section) ? RiskCheck : string.Empty)
        + ". Rewrite or remove every sentence that fails, and answer with the whole revised section, in the form the section asks for and under the same rules.";
}
