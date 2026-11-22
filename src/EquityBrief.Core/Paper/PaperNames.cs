using EquityBrief.Core.Families;

namespace EquityBrief.Core.Paper;

// A variant's name in Paper trading's words, worked once for every page and for the monthly run's stored cases: the setting
// it was registered to try, which is any it differs in but the plan where it differs in more, as a plain sentence.
// see: The Improvement loop names a setup's revision and its variants in Paper trading's words
public static class PaperNames
{
    // The one change a variant is named by.
    public static ChangeLine? NamedChange(PaperAccountView variant) =>
        variant.Differences.FirstOrDefault(change => change.Key != "hooks") ?? variant.Differences.FirstOrDefault();

    // A variant named by its one change as a sentence.
    public static string VariantName(PaperAccountView variant) =>
        NamedChange(variant)?.Sentence ?? Capitalised((variant.Changes ?? variant.Name).Replace(" · ", "; ", StringComparison.Ordinal));

    // The settings a variant differs in beside the one it is named by.
    public static IReadOnlyList<ChangeLine> AlsoDiffers(PaperAccountView variant) =>
        NamedChange(variant) is { } named ? [.. variant.Differences.Where(change => !ReferenceEquals(change, named))] : [];

    static string Capitalised(string line) =>
        line.Length == 0 ? line : RuleWording.ScreenName(char.ToUpperInvariant(line[0]) + line[1..]);
}
