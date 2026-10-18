using System.Text.RegularExpressions;

namespace EquityBrief.Core.Releases;

// The three probes the release reading is measured by, each over the probes' sample and changing nothing: the twice
// probe reads each release twice with its names and dates removed and asks whether the two answers are alike; the
// re-identification probe asks the model which company issued each release so read and whether it names the filer; and
// the distraction probe reads each release once with its names removed and once with them in, and asks whether the
// answers are alike. Each is put to the operator and none retires anything.
// see: A release is read with every name and date in it replaced by a neutral word, and product names kept
public static class ReleaseProbes
{
    public const string Twice = "twice";
    public const string Reidentify = "reidentify";
    public const string Distract = "distract";

    public static IReadOnlyList<string> Kinds { get; } = [Twice, Reidentify, Distract];

    // The re-identification probe's question, which asks for a name and never for a number, a date or an estimate.
    public const string ReidentifyInstruction =
        "You read a company's quarterly results release with its names and dates removed. Reply with the name of the " +
        "company you believe issued it and nothing else, or with the one word unknown.";

    // An answer names the filer where it holds one of its names whole, a name's first word that is no common word, or
    // one of its tickers as a word, whatever the case.
    public static bool Names(string? answer, ReleaseFiler filer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var said = answer.Trim();
        var words = new HashSet<string>(
            Regex.Matches(said, @"[\p{L}\p{N}&.\-]+", RegexOptions.CultureInvariant).Select(match => match.Value.Trim('.')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var variant in filer.Names.SelectMany(ReleaseAnonymiser.Variants))
        {
            var bare = variant.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? variant[4..] : variant;

            if (bare.Length > 2 && said.Contains(bare, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var first = bare.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim(',', '.') ?? string.Empty;

            if (first.Length > 2 && !ReleaseAnonymiser.CommonWords.Contains(Titled(first), StringComparer.Ordinal) && words.Contains(first))
            {
                return true;
            }
        }

        return filer.Tickers.Any(ticker => ticker.Length > 1 && words.Contains(ticker));
    }

    // A share of a count, none where the count is nought.
    public static double? Share(int part, int whole) => whole == 0 ? null : 1.0 * part / whole;

    static string Titled(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
}
