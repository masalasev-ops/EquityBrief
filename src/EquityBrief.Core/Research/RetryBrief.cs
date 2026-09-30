using EquityBrief.Core.Facts;

namespace EquityBrief.Core.Research;

// What a section's one retry is told: each thing the checker refused in the first draft, a line each, with
// what to do about it, and never the refused draft itself.
//
// A retry told only the checker's reason wrote a second draft carrying new figures in place of the one it was
// refused for, taken from other sections' refused drafts, and was left out. So the refused draft is read again
// by the checker's own rules over the same facts file and the same source list, which gives the same findings
// the checker stored, and each finding becomes a line naming the figure, date, citation or sentence and what a
// second draft does with it: a figure or a date removed or replaced by one the facts file lists, a sentence
// citing nothing removed or ended on the marker of the document that states it.
// see: A retry names each thing the check refused, and a second draft repeating one is left out
public static class RetryBrief
{
    public const string Opening =
        "Your previous draft of this section was refused by the checker. Each line below names one thing it refused and what to do about it. Your new draft must contain none of these:";

    public const string Closing = "Write the section again.";

    // The brief for a refused draft, read again over what it was checked against. Where the rules find
    // nothing the checker's stored reason is stated whole, since a brief naming nothing would tell a retry
    // nothing.
    public static string For(
        string section,
        string refusedProse,
        IReadOnlyList<Fact> facts,
        IReadOnlyList<StoredDocument?> sources,
        DateOnly? night,
        string? storedReason,
        string? parts = null)
    {
        var findings = ClaimRules.Check(section, refusedProse, facts, sources, night, parts).Findings;

        return findings.Count > 0
            ? For(findings)
            : Opening + "\n- " + (storedReason ?? "a draft the checker refused") + ". Write it so that it is not.\n" + Closing;
    }

    public static string For(IReadOnlyList<ClaimFinding> findings) =>
        Opening + "\n" + string.Join("\n", findings.Select(Line).Distinct(StringComparer.Ordinal)) + "\n" + Closing;

    // One refused thing: what it is, the rule that refused it, and what a second draft does with it.
    public static string Line(ClaimFinding finding) =>
        "- " + What(finding) + ": " + finding.Reason + ". " + Do(finding.Reason);

    // The thing refused as a writer finds it again: a sentence the rule refused whole by its own words, and
    // anything the rule took out of a sentence with the sentence it sits in.
    static string What(ClaimFinding finding) =>
        finding.Sentence.Length == 0
            ? finding.Offending
            : string.Equals(finding.Offending, finding.Sentence, StringComparison.Ordinal)
                ? "the sentence \"" + finding.Sentence + "\""
                : "\"" + finding.Offending + "\" in the sentence \"" + finding.Sentence + "\"";

    public static string Do(string rule) => rule switch
    {
        ClaimRules.UnmatchedFigure => "Remove it, or replace it with a figure listed under Facts, copied or rounded.",
        ClaimRules.UnmatchableFigure => "Remove it, or write it in digits as a figure listed under Facts.",
        ClaimRules.UnknownWindow => "Remove it, or name a window the facts listed carry.",
        ClaimRules.UnknownDate => "Remove it, or replace it with a date listed under Facts.",
        ClaimRules.FigureNamedForAnotherPeriod => "Name the period the fact is listed for, or remove the figure.",
        ClaimRules.Uncited => "Remove it, or end it with the marker of the listed document that states it.",
        ClaimRules.CitationOutOfRange => "Cite only the documents listed, by their markers.",
        ClaimRules.NotStored => "Cite only the documents listed, by their markers.",
        ClaimRules.RefusedSource => "Cite a listed document that states it, or remove the sentence.",
        ClaimRules.CauseNamingNoMove => "Begin it with the session a listed move ended on, or remove it.",
        ClaimRules.CauseOutsideItsMove => "Cite a document published inside that move, or remove the sentence.",
        ClaimRules.DateNoCitedDocumentCarries => "Use the date the cited document states, or remove the item.",
        ClaimRules.DateNotAfterTheNight => "Remove the item, since only events after the session are listed.",
        ClaimRules.ClaimOfCandour => "Write the sentence without calling any statement candid or frank.",
        ClaimRules.EmDashed => "Write the sentence without an em dash.",
        ClaimRules.TwoCasesWithoutSides => "Write the bull case as a paragraph opening \"" + ClaimRules.CaseFor + "\" and the bear case as a paragraph opening \"" + ClaimRules.CaseAgainst + "\".",
        RiskFields.NotFields => "Answer with the JSON object the section asks for and nothing else.",
        RiskFields.FactNotListed => "Name a fact exactly as it is listed under Facts, or confirm the risk by an event of one kind.",
        RiskFields.DirectionNotNamed => "Write the direction as \"" + RiskFields.RisesAbove + "\" or \"" + RiskFields.FallsBelow + "\".",
        RiskFields.FactWithoutLevel => "Give the fact a direction and a level listed under Facts.",
        RiskFields.LevelNotHeld => "Write the level as a figure listed under Facts, copied or rounded.",
        RiskFields.KindNotListed => "Give the event one kind of " + string.Join(", ", RiskFields.Kinds) + ".",
        RiskFields.EventWithLevel => "Give an event no direction and no level.",
        RiskFields.SharedFact => "Confirm each risk by a fact no other risk names, or join the risks that share it.",
        RiskFields.SharedKind => "Give each event risk a kind no other event risk has, or join the risks that share it.",
        _ => "Write it so that it is not.",
    };
}
