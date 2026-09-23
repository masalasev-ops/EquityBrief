using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EquityBrief.Core.Rules;

// One version of one ladder rule, as the store holds it.
//
// `ClosedAt` is null while the window is open, and a closed window keeps its row
// rather than being replaced: a measurement whose subject moved says nothing
// about either version, so the only way to change a rule is to close the window
// measuring it and open another, with both rows standing afterwards.
// see: Adding a candidate later restarts the clock
public sealed record RuleVersionRow(
    string Rule,
    string Version,
    string Parameters,
    string ParametersHash,
    string CodeVersion,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string? ReplacedBy,
    string? Evidence = null);

// The five ladder rules that carry named versions.
//
// Five rather than every constant the builder holds, and the five are the ones a
// plan's shape actually turns on: where two bands become one zone, where the stop
// sits, whether a tranche too close to an exit is skipped, whether a zone's
// edges come from its non-average anchors alone, and which label the ladder is
// built under. The fourth arrives from 8.0's trace, which found a moving average
// widening 35 of 223 fired zones and three of them no longer holding the close if
// the average were dropped, and registered the alternative here rather than
// deciding it by taste. The fifth is the trend rule, which selects the ladder's
// whole shape, since a downtrend carries no tranches at all.
// see: A moving average may widen a band that a tranche sits on, and may never anchor one
// see: The trend rule is a fifth ladder rule a version replays, and none of its three versions is live
public static class LadderRules
{
    public const string MergeDistance = "merge distance";

    public const string StopPlacement = "where the stop sits";

    public const string NearExitSkip = "the near-exit skip";

    public const string ZoneEdgesFromNonAverageAnchors = "zone edges from non-average anchors only";

    public const string TrendRule = "the trend rule";

    public static IReadOnlyList<string> All { get; } =
    [
        MergeDistance,
        StopPlacement,
        NearExitSkip,
        ZoneEdgesFromNonAverageAnchors,
        TrendRule,
    ];

    // Which rules a version of this rule makes the night replay.
    //
    // The merge distance decides where a band's edges are, so a version of it
    // replays the level stage and then the ladder stage. The other three read
    // the bands as they stand and replay the ladder stage alone. That split is
    // the whole of the bound's arithmetic: a merge distance version costs a level
    // replay and a ladder replay, and every other version costs a ladder replay.
    public static bool ReplaysLevels(string rule) =>
        string.Equals(rule, MergeDistance, StringComparison.Ordinal);

    // Which rules a version of reads each band member's source: the merge distance
    // re-merges the anchors, and the zone edges leave out the averages and the touches.
    public static bool ReadsMembers(string rule) =>
        ReplaysLevels(rule) || string.Equals(rule, ZoneEdgesFromNonAverageAnchors, StringComparison.Ordinal);

    // Which rules a version of can only take a setup away. The trend rule selects the
    // ladder's whole shape and the label a version writes is the one that carries no
    // tranche, so a version's setups are the live rule's less the ones its label removes
    // and every outcome is one the store already holds. That is what lets a difference be
    // measured from stored outcomes at all, and a version of any other rule produces plans
    // whose outcomes nothing computed.
    // see: A trend version is judged by the candidates' test on its difference from the live rule
    public static bool OnlyRemovesSetups(string rule) =>
        string.Equals(rule, TrendRule, StringComparison.Ordinal);
}

// The bound, the windows and the drift check.
//
// The bound is proposed and its arithmetic is stated rather than assumed: the
// night of 2026-09-14 took 495 seconds over the steps before the close, its level stage 143
// seconds and its ladder stage 5 at 504 names. A merge distance version therefore
// costs 148 seconds and every other version 5. The caps are two windows of the
// merge distance and four of each other rule, each rule's live window among them,
// so the fullest register they admit replays one merge distance version and
// twelve others, 208 seconds, which puts the night at 703 against a deadline of
// 900. A projection is not a measurement, and the row that settles it reads the
// scorer's own nights.
// owes: The rule version bound set from nights the version scorer ran
// see: A ladder rule's version is measured beside that rule's live window, and both count against the bound
public static class RuleVersions
{
    // At most two windows of the merge distance, four of each other rule, and
    // eighteen at once, every window counted, live ones included.
    //
    // The merge distance has its own cap because it is the one rule whose
    // version replays the level stage, at 148 seconds against 5: four of it
    // would add three level replays and take the night past its deadline on its
    // own. The other caps stop one rule taking the whole budget and leaving the
    // others unversioned. Eighteen is the sum of the five caps, so no register
    // the caps admit exceeds it, and it stays a cap of its own for a register
    // written by anything other than the verb.
    public const int MostOfTheMergeDistance = 2;

    public const int MostPerRule = 4;

    public const int MostAtOnce = 18;

    // The live version of each rule is what the night already computes, so it
    // costs no replay. It is still a window, and it counts against the caps: a
    // version is opened only beside its rule's live window, because the live
    // window's hash is what stops the night when the code both of them run
    // through moves, and a version measured with nothing watching that code is a
    // measurement whose subject can move unseen.
    public const string Live = "live";

    public static int MostFor(string rule) =>
        LadderRules.ReplaysLevels(rule) ? MostOfTheMergeDistance : MostPerRule;

    public const string InSample = "in_sample";

    public const string Scored = "scored";

    // The form a window's instant is stored in. One statement of it, because a
    // score is keyed to the window by that instant and a reader spelling it a
    // second way would key a score to a window that does not exist.
    public const string Instant = "yyyy-MM-ddTHH:mm:ssZ";

    public static DateTimeOffset At(string stored) =>
        DateTimeOffset.ParseExact(
            stored,
            Instant,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static string Stored(DateTimeOffset at) =>
        at.ToUniversalTime().ToString(Instant, CultureInfo.InvariantCulture);

    // A score counts only for a session after the New York date its window opened on.
    // see: A version's score counts only for a session after the New York date its window opened on
    public static string SampleOf(DateOnly session, DateOnly windowOpenedOn) =>
        session > windowOpenedOn ? Scored : InSample;

    // The versions open at an instant, being those whose window opened at or
    // before it and has not closed by it.
    public static IReadOnlyList<RuleVersionRow> OpenAt(IEnumerable<RuleVersionRow> rows, DateTimeOffset at) =>
    [
        .. rows
            .Where(row => row.OpenedAt <= at)
            .Where(row => row.ClosedAt is null || row.ClosedAt > at)
            .OrderBy(row => row.Rule, StringComparer.Ordinal)
            .ThenBy(row => row.OpenedAt),
    ];

    // Why a version may not be opened, or null.
    //
    // Named apart from the write so the check reads the same reader the verb
    // does rather than a copy of it, and so each refusal can be put to it over
    // constructed rows.
    public static string? Refusal(IReadOnlyList<RuleVersionRow> rows, string rule, string version, DateTimeOffset at)
    {
        if (!LadderRules.All.Contains(rule, StringComparer.Ordinal))
        {
            return
                $"'{rule}' is not a ladder rule this build carries. A version of a rule nothing applies " +
                $"is a window measuring nothing. Carried: {string.Join(", ", LadderRules.All)}.";
        }

        var open = OpenAt(rows, at);

        if (open.Any(row => string.Equals(row.Rule, rule, StringComparison.Ordinal)
            && string.Equals(row.Version, version, StringComparison.Ordinal)))
        {
            return
                $"'{version}' of '{rule}' already has an open window. A version is opened once and closed " +
                "once, and re-opening one would put two windows on the same subject with nothing saying " +
                "which a score belongs to.";
        }

        var forThisRule = open.Count(row => string.Equals(row.Rule, rule, StringComparison.Ordinal));

        // A version beside nothing. The live window is what the drift check
        // reads, so without one a code change moves the version's own replay
        // with nothing to stop the night.
        if (!string.Equals(version, Live, StringComparison.Ordinal)
            && !open.Any(row => string.Equals(row.Rule, rule, StringComparison.Ordinal)
                && string.Equals(row.Version, Live, StringComparison.Ordinal)))
        {
            return
                $"'{rule}' has no open live window. A version is measured beside its rule's live window, " +
                "because that window's hash is what stops the night when the code both run through moves; " +
                "open the live window first.";
        }

        if (forThisRule >= MostFor(rule))
        {
            return FormattableString.Invariant(
                $"'{rule}' already has {forThisRule} open window(s), its live one included, which is the most for this rule of {MostFor(rule)}. ")
                + "Close one before opening another, because one rule taking the whole budget leaves the "
                + "others unversioned, and the merge distance's cap is lower because its versions replay the level stage.";
        }

        return open.Count >= MostAtOnce
            ? FormattableString.Invariant(
                $"{open.Count} window(s) are already open, which is the most at once of {MostAtOnce}. ")
                + "The night replays the ladder stage once per version and the level stage once per merge "
                + "distance version, and the bound is what keeps that inside the deadline."
            : null;
    }

    // The seconds a night of these windows is projected to add, from the stage
    // durations the night itself measured.
    //
    // A live window costs nothing, because the scorer replays only the versions
    // beside it and the night has already computed the live rule. Projected
    // rather than asserted: the figures are one night's at one index size, and
    // the row that settles the bound reads the scorer's own nights.
    public static double ProjectedSeconds(
        IReadOnlyList<RuleVersionRow> open,
        double levelStageSeconds,
        double ladderStageSeconds)
    {
        var replayed = open.Where(row => !string.Equals(row.Version, Live, StringComparison.Ordinal)).ToArray();
        var merge = replayed.Count(row => LadderRules.ReplaysLevels(row.Rule));
        var others = replayed.Length - merge;

        return (merge * (levelStageSeconds + ladderStageSeconds)) + (others * ladderStageSeconds);
    }

    // The most the caps let a night add: every rule at its cap, one window of
    // each being its live one. This is the figure the bound is for, since a bound
    // whose fullest register runs past the deadline is not a bound on the night.
    public static double WorstCaseSeconds(double levelStageSeconds, double ladderStageSeconds) =>
        LadderRules.All.Sum(rule =>
            (MostFor(rule) - 1) * (LadderRules.ReplaysLevels(rule) ? levelStageSeconds + ladderStageSeconds : ladderStageSeconds));

    // A rule's parameters and the code version, hashed, so a change to either is a
    // thing the night can notice; the source text is read by the code version's pin.
    public static string Hash(IReadOnlyDictionary<string, double> parameters, string codeVersion)
    {
        var written = string.Join(
            ";",
            parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => FormattableString.Invariant($"{pair.Key}={pair.Value}")));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(written + "|" + codeVersion));

        return Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }

    public static string Write(IReadOnlyDictionary<string, double> parameters) =>
        "{" + string.Join(
            ", ",
            parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => FormattableString.Invariant($"\"{pair.Key}\": {pair.Value}"))) + "}";

    public static IReadOnlyDictionary<string, double> Read(string parameters)
    {
        var read = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var part in parameters.Trim('{', '}', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOf(':', StringComparison.Ordinal);

            if (at < 0)
            {
                throw new FormatException(
                    $"'{part.Trim()}' in a version's parameters is not a name and a value. A version nobody " +
                    "can read back is a window nothing can be replayed under.");
            }

            read[part[..at].Trim().Trim('"')] = double.Parse(part[(at + 1)..].Trim(), CultureInfo.InvariantCulture);
        }

        return read;
    }

    // The live rules whose parameters or code have moved while a window measuring
    // them is open.
    //
    // This is what stops the night rather than a note on its row. A measurement
    // whose subject moved says nothing about either version: the scores already
    // written under the open window were computed against the old rule, the
    // scores tonight would be computed against the new one, and a record holding
    // both is a record of neither. The only way through is to close the window
    // and open a new one, which keeps the old rows and says what they were of.
    // see: Adding a candidate later restarts the clock
    public static IReadOnlyList<string> Drifted(
        IReadOnlyList<RuleVersionRow> open,
        IReadOnlyDictionary<string, string> hashesNow)
    {
        var moved = new List<string>();

        foreach (var row in open.Where(row => string.Equals(row.Version, Live, StringComparison.Ordinal)))
        {
            if (!hashesNow.TryGetValue(row.Rule, out var now))
            {
                moved.Add($"'{row.Rule}' has an open live window and this build applies no such rule.");

                continue;
            }

            if (!string.Equals(now, row.ParametersHash, StringComparison.Ordinal))
            {
                moved.Add(
                    $"'{row.Rule}' has an open window opened at hash {row.ParametersHash} and the build now " +
                    $"hashes to {now}. A window measuring a rule that moved says nothing about either " +
                    "version: close it and open a new one.");
            }
        }

        return moved;
    }
}
