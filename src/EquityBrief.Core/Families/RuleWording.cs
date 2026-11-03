using System.Globalization;
using static System.FormattableString;

namespace EquityBrief.Core.Families;

// The words a page or the run page draws for a rule, held here because no rule's version pins this file: a wording
// fix made here moves no version and restarts no rule. The files that still hold their rule's words are listed where
// the check reads them.
// see: A rule's screen wording sits outside the sources its version pins and a file still holding it waits for a restart already happening
public static class RuleWording
{
    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The sentence an earnings drift's target is named by, and the phrase its registered name states it in, which stays
    // as it was registered since that name is the rule's key in the store.
    public static string DriftTarget(string multiple) =>
        $"the nearer of the next price band about two typical moves up, or {multiple} times the risk";

    public const string DriftTargetAsStored = @"\bthe target at (\d+(?:\.\d+)?) times the risk\b";

    // A rule's registered name as a page draws it, wherever a page draws the name without the reader's other words.
    public static string ScreenName(string registered) =>
        System.Text.RegularExpressions.Regex.Replace(registered, DriftTargetAsStored, match => DriftTarget(match.Groups[1].Value));

    public static class Breakout
    {
        public static string TooFewForTheHigh(int stored, int sessions) =>
            Invariant($"not available: {stored} sessions are stored, and a close is read against the {sessions} before it");

        public static string NewHigh(decimal close, bool passed, int sessions, decimal high) =>
            Invariant($"the close of {close} is {(passed ? "above" : "not above")} the highest high of the {sessions} sessions before it, {high}");

        public const string NoVolumeAverage = "not available: no 50-session average volume is stored for the night";

        public static string Volume(double multiple, bool passed, double floor) =>
            Invariant($"volume {multiple:0.00} times its 50-session average, {(passed ? "at or above" : "below")} {floor:0.0#}");

        public static string TooFewForTheRanges(int stored, int sessions) =>
            Invariant($"not available: {stored} sessions are stored, and the ranges are read over the {sessions} before tonight");

        public const string NoCloseBefore = "not available: a session before tonight holds no close above zero";

        public static string NoRange(int sessions) =>
            Invariant($"not available: the {sessions} sessions the newer ranges are read against held no range");

        public static string Ranges(int sessions, double newer, bool passed, double ceiling, double older) =>
            Invariant($"the mean daily range of the {sessions} sessions before tonight is {newer * 100:0.00}% of the close, {(passed ? "no wider than" : "wider than")} {ceiling:0.0#} of the {older * 100:0.00}% of the {sessions} before them");

        public const string NoClose = "no close is stored for the night";

        public const string NoTypicalMove = "no typical move is stored for the night to place the stop by";

        public static string NoStop(double moves, double move, decimal close) =>
            Invariant($"{moves:0.#} typical moves of {move:0.00} beneath the close of {close} leave no stop below it and above zero");

        public static string Bought(decimal close, decimal stop, double moves, double move) =>
            Invariant($"bought at the close of {close} with the stop at {stop}, {moves:0.#} typical moves of {move:0.00} beneath, trailing the highest close since");
    }

    public static class Drift
    {
        public const string NoReaction = "no print's reaction is stored for the name";

        public static string BarsShort(DateOnly reaction) =>
            Invariant($"not available: the stored bars do not reach the reaction session of {Day(reaction)} and the close before it");

        public static string Reacted(DateOnly report, DateOnly reaction, int back, bool inside, int window) =>
            Invariant($"the print of {Day(report)} reacted on {Day(reaction)}, {back} session(s) before tonight, {(inside ? "inside" : "outside")} the {window}-session window");

        public const string NoPrint = "no print inside the window to read";

        public static string NoActual(DateOnly report) =>
            Invariant($"the calendar carries no actual for the print of {Day(report)} yet, so no surprise is read");

        public static string Beat(DateOnly report, bool passed, double surprise) =>
            passed
                ? Invariant($"the print of {Day(report)} beat its estimate by {surprise:0.0#}%")
                : Invariant($"the print of {Day(report)} did not beat its estimate: a surprise of {surprise:0.0#}%");

        public const string NoMoveBefore = "not available: no typical move is stored for the session before the reaction's";

        public static string Reaction(bool up, double moves, bool passed, double floor) =>
            Invariant($"the reaction session closed {(up ? "up" : "down")} {moves:0.00} typical moves, {(passed ? "at or above" : "below")} {floor:0.0#} up");

        public const string NoVolumeAverage = "not available: no 50-session average volume is stored for the reaction session";

        public static string Volume(double multiple, bool passed, double floor) =>
            Invariant($"the reaction session's volume was {multiple:0.00} times its 50-session average, {(passed ? "at or above" : "below")} {floor:0.0#}");

        public static string Held(decimal close, bool passed, decimal low) =>
            Invariant($"the close of {close} is {(passed ? "above" : "not above")} the reaction session's low of {low}");

        public const string NoTypicalMove = "no typical move is stored for the night to hold the stop's floor by";

        public static string NoStop(decimal low, decimal close) =>
            Invariant($"the reaction session's low of {low} leaves no stop below the close of {close} and above zero");

        public static string BoughtOnTheFloor(decimal close, decimal stop, double moves, decimal low, decimal target) =>
            Invariant($"bought at the close of {close} with the stop at {stop}, {moves:0.#} typical moves under the close and below the reaction session's low of {low}, and the target at {target}, ");

        public static string BoughtOnTheLow(decimal close, decimal stop, decimal target) =>
            Invariant($"bought at the close of {close} with the stop at the reaction session's low, {stop}, and the target at {target}, ");

        public static string TargetFromBand(double moves) =>
            Invariant($"the lowest band {moves:0.#} typical moves or more above the close");

        public static string TargetFromRisk(double multiple) =>
            Invariant($"{multiple:0.0#} times the risk above the close");

        public static string RewardToRisk(decimal reward) =>
            Invariant($", a reward to risk of {reward:0.00}");
    }

    public static class Tone
    {
        public const string Words = "release better, outlook not cut";

        public const string HistoryOpen = "the reading version's history read is not complete";

        public const string NotHanded = "no release reading was handed for the print under the version";

        public const string NotHeldOnTheIndex = "no reading of its release held under its rule's reading version";

        public const string FailedOnTheIndex = "its release not read better with its outlook not cut";

        public static string Handed(long version, int prints, int read) =>
            Invariant($"reading version {version}: {prints} print(s) inside the window, {read} read");

        public static string NoReading(long version, string why) =>
            Invariant($"no release reading under reading version {version}: {why}");

        public static string Reads(string results, string outlook, long version, bool passed) =>
            passed
                ? Invariant($"the release reads results {results} and outlook {outlook}, under reading version {version}")
                : Invariant($"the release reads results {results} and outlook {outlook}, under reading version {version}, and the variant lists only results better with an outlook not lowered or withdrawn");
    }

    public static class Estimate
    {
        public const string NoTrend = "the answer files no earnings trend";

        public const string NoEstimate = "the answer files no estimate for the current fiscal year";

        public static string Missing(string missing) =>
            $"the answer files the current fiscal year's estimate {missing} as none";
    }

    public static class Shadow
    {
        public const string NoBar = "no bar is stored for the night's session";

        public const string Gapped = "the stored series has a gap, so nothing is computed across it";

        public static string GappedAt(string at) => "the stored series has a gap at " + at + ", so nothing is computed across it";

        public const string NoFamilyCandidate = "; no family candidate stands registered, so nothing was evaluated in shadow";

        public const string NoSwingCandidate = "; no swing family candidate stands registered, so nothing was evaluated in shadow";

        public const string NoCandidate = "; no candidate stands registered, so nothing was evaluated in shadow";

        public static string SwingWritten(int standing, int evaluated) =>
            Invariant($"; {standing} swing family candidate(s) registered, {evaluated} shadow evaluation(s) written, ");

        public static string FamilyWritten(int standing, int evaluated) =>
            Invariant($"; {standing} family candidate(s) registered, {evaluated} shadow evaluation(s) written, ");

        public static string Written(int standing, int evaluated) =>
            Invariant($"; {standing} candidate(s) registered, {evaluated} shadow evaluation(s) written, ");

        public static string Skipped(int skipped, int stale, int gapped) =>
            Invariant($"{skipped} skipped on a member without the readings: {stale} stale, {gapped} gapped");

        public static string SkippedANameNight(int skipped, int stale) =>
            Invariant($"{skipped} skipped on a name-night without the readings: {stale} stale, ");

        public static string GappedOrUnread(int gapped, int notAvailable) =>
            Invariant($"{gapped} gapped, {notAvailable} with a reading not available");

        public static string FailedOnEveryMember(int faults) =>
            Invariant($"; FAILURE: {faults} registered candidate(s) skipped on every member, the code carrying no evaluator by its name or a moved one: ");

        public static string FailedOnEveryName(int faults) =>
            Invariant($"; FAILURE: {faults} registered candidate(s) skipped on every name, the code carrying no evaluator by its name or a moved one: ");
    }

    public static class Evaluation
    {
        public const string NoSession = "no session is stored, so no family was evaluated";

        public static string NoFilterResult(string session) =>
            $"the swing filter stored no result for {session}, so no family was evaluated";

        public static string Passed(string family, int passed, int members, int oneShort) =>
            Invariant($"the {family} family passed {passed} of {members} members, {oneShort} one gate short");

        public const string TheSwitches = "; the market switches: ";

        public const string TheReadings = "; the release readings: ";
    }

    public static class Ladder
    {
        public const string NoBarOpening = "no bar for this session; the last session stored for the name is ";

        public const string GapOpening = "the stored series has a gap at ";

        public const string GapEnding = ", so nothing is computed for this name until that session arrives";

        public const string NoBars = "no stored bars";

        public const string NoBands = "no bands are stored for this name";

        public const string NoTypicalMove = "no typical daily move is stored for this name";

        public const string NoTranche = "no tranche is placed";

        public const string NoReportDated = "no report is dated on the calendar, so the second book is empty";

        public const string SecondBookProposes = "every figure in the second book is a proposal";

        public static string NotClassifiedAndBare(int notClassified, int withoutBars) =>
            Invariant($"{notClassified} not classified, {withoutBars} with no stored bars, ");
    }

    public static class Shortlist
    {
        public const string GapEnding = ", so nothing is computed across it";

        public const string BeyondTheCalendarOpening = " name(s) with a dated event beyond the exchange calendar, which ends ";

        public const string BeyondTheCalendarEnding = ", not counted and not fired: ";

        public const string Failed = "ok, with a registered candidate's evaluator missing or moved";

        public const string NoBarOpening = "no bar for this session; the last session stored for the name is ";

        public const string GapOpening = "the stored series has a gap at ";

        public const string NoBarStored = "no bar is stored for the name";

        public const string NoLadderRow = "no ladder row for this name tonight";
    }

    public static class Versions
    {
        public static string Scored(int open, int replayed, int mergeDistance, int names, int written, int kept, int notComputed, int skipped, int dropped, int frozen) =>
            Invariant($"{open} open version(s), {replayed} replayed with {mergeDistance} of the merge distance, over {names} name(s), ")
            + Invariant($"{written} score(s) written, {kept} already stored and kept, {notComputed} name(s) with no bands or plan of that session, ")
            + Invariant($"{skipped} name-night(s) skipped for a band set stored without member sources, {dropped} dropped, {frozen} block(s) frozen");
    }

    // What a stock's row says on an S&P 400's or 600's rule that did not list it.
    public static class IndexRule
    {
        public const string MarketCheckOff = "market check off";

        public const string IndustryFell = "its industry fell";

        public const string SwitchClosed = "a switch its rule reads closed";

        public const string NotNearItsHigh = "not near enough its year's high";

        public const string HighNotAsStated = "its year's high not as recent as its rule states";

        public const string NoYearHigh = "not read: no year's high tonight";

        public const string VolumeUnder = "volume under its rule's multiple";

        public const string PeersNotAbove = "its peers' surprise not above nothing";

        public const string RevenueNotAbove = "its print's revenue not above the same quarter's a year before";
    }
}
