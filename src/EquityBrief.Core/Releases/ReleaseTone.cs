using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Releases;

// A print's release as a rule reading it is handed under one reading version: the release's four words where the
// version holds a reading of it, or why there is none.
public sealed record PrintRelease(long Version, ReleaseAnswer? Answer, string? Why);

// The drift's tone condition, read after the rule's gates as a market switch is: a member the rule fires on stays fired
// only where the release of the print its setup reacts to reads better than expected and its outlook was not lowered or
// withdrawn, under the reading version the registration names. A release with no reading held under the version, one
// whose reading was refused, one no window holds and a version whose history read is not complete each fail the gate
// and say why. A registration stating no version reads no condition.
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
public static class ReleaseTone
{
    // The gate's name, and the optional parameter naming the reading version a registration reads, off where nought.
    public const string Gate = "tone";

    public const string VersionParameter = "readingVersion";

    // The condition in the card's short words.
    public const string Words = "release better, outlook not cut";

    // Why a version reads no release of a print, beside the reader's own reasons.
    public const string HistoryOpen = "the reading version's history read is not complete";

    public const string NotHanded = "no release reading was handed for the print under the version";

    // The version a registration's parameters name, nought where they name none.
    public static long VersionOf(IReadOnlyDictionary<string, double> parameters) =>
        parameters.TryGetValue(VersionParameter, out var version) && version > 0 ? (long)version : 0;

    // The parameters a registration of a rule may state beside the ones every registration of it states, each off where
    // it is not stated: the reading version, which the S&P 500's drift and the S&P 400's and 600's drift read alone.
    public static IReadOnlyList<string> OptionalOf(Candidates.CandidateEvaluator? evaluator) =>
        evaluator is Candidates.DriftCandidate or Candidates.IndexDriftCandidate ? [VersionParameter] : [];

    // Whether an answer passes: results better, and an outlook anything but lowered or withdrawn, none given passing.
    public static bool Passes(ReleaseAnswer answer) =>
        answer.Results == "better" && answer.Outlook != "lowered or withdrawn";

    // The step a card's funnel counts before the tone gate, a member whose print's release is read under the version, so a
    // night holding no reading says where its members stopped; and whether a tone gate read a release.
    public const string ReadStep = "release read";

    public static bool IsRead(Gate gate) => gate.Values.ContainsKey("results");

    // The reasons an S&P 400 or 600 drift rule naming a version keeps a listed member off, its print's release holding no
    // reading under the version and its reading failing, which its card's funnel names as the two steps above.
    public const string NotHeldOnTheIndex = "no reading of its release held under its rule's reading version";

    public const string FailedOnTheIndex = "its release not read better with its outlook not cut";

    // What a night's releases held under one reading version, for the stage's row: the prints inside the drift's window
    // handed a release, how many of them were read, and how many hold none for each reason.
    public static string Said(long version, IReadOnlyList<PrintRelease> handed)
    {
        var none = handed
            .Where(release => release.Answer is null)
            .GroupBy(release => release.Why ?? NotHanded, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => FormattableString.Invariant($", {group.Count()} where {group.Key}"));

        return FormattableString.Invariant($"reading version {version}: {handed.Count} print(s) inside the window, {handed.Count(release => release.Answer is not null)} read") + string.Concat(none);
    }

    // The gate over the release a member's print is handed under the version.
    public static Gate GateOf(long version, PrintRelease? release)
    {
        if (release is not { Answer: { } answer })
        {
            return new Gate(Gate, false, $"no release reading under reading version {version}: {release?.Why ?? NotHanded}", FamilyRule.Values(("version", FamilyRule.Whole(version))));
        }

        var passed = Passes(answer);

        return new Gate(
            Gate,
            passed,
            passed
                ? $"the release reads results {answer.Results} and outlook {answer.Outlook}, under reading version {version}"
                : $"the release reads results {answer.Results} and outlook {answer.Outlook}, under reading version {version}, and the variant lists only results better with an outlook not lowered or withdrawn",
            FamilyRule.Values(("version", FamilyRule.Whole(version)), ("results", answer.Results), ("outlook", answer.Outlook)));
    }

    // A rule's answer with the condition read after its gates, the answer as it was where the registration names no
    // version.
    public static FamilyResult Read(FamilyResult result, long version, IReadOnlyList<PrintRelease>? releases) =>
        version > 0
            ? result with { Gates = [.. result.Gates, GateOf(version, releases?.FirstOrDefault(release => release.Version == version))] }
            : result;
}
