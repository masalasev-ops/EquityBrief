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

    // src/EquityBrief.Core/Ledger/LedgerReadings.cs
    // From 23.10's second part the profit part's words over the quarters as the rules read them, each from its filing and then
    // its results release.
    public static class ReadQuarters
    {
        public static string QuartersRead(int read, int quarters) =>
            FormattableString.Invariant($"{read} of the {quarters} quarters the profit check sums read with their net income from a filing or a results release before tonight");

        public static string NetIncomeNotAbove(decimal netIncome, int quarters) =>
            FormattableString.Invariant($"net income of {netIncome:#,0} over its {quarters} newest quarters as read from their filings and releases, not above nothing");
    }

    public static class LedgerReadingsWords
    {
        public const string CloseOverTwenty = "the close over its 20-session average";
        public const string CloseOverFifty = "the close over its 50-session average";
        public const string CloseOverLong = "the close over its 200-session average";
        public const string FiftyOverLong = "the 50-session average over the 200-session average";
        public const string MoveShare = "the typical move over the close";
        public const string Rsi = "the relative strength index on the session";
        public const string RsiUp = "1 where the relative strength index rose on the session, 0 where it did not";
        public const string VolumeRatio = "the session's volume over the mean of the 50 sessions before it";
        public const string ReturnQuarter = "the return over 63 sessions";
        public const string ReturnHalfYear = "the return over 126 sessions";
        public const string ReturnTwelveLessOne = "the return over the 231 sessions ending 21 sessions before";
        public const string Strength = "the mean of the member's places among the members' returns over 63 and 126 sessions, the live measure";
        public const string StrengthTwelveLessOne = "the member's place among the members' returns over the 231 sessions ending 21 sessions before";
        public const string HighRatio = "the close over the highest high of the 252 sessions ending on the session";
        public const string SinceHigh = "the sessions since the highest high of the 20 sessions before";
        public const string Depth = "the pullback from that high in typical moves";
        public const string DryUp = "the volume while it came down over the 50-session average";
        public const string GapDown = "the largest gap down inside that pullback in typical moves";
        public const string RsiLow = "the lowest relative strength index since that high";
        public const string Tightness = "the mean daily range of the 20 sessions before over that of the 20 before them";
        public const string Liquidity = "the base-10 logarithm of the mean of close times volume over the 50 sessions to the session";
        public const string EarningsSessions = "the weekdays to the next report on file, the report's day counted";
        public const string SurpriseSessions = "the sessions since the newest surprise's reaction session";
        public const string SurprisePercent = "that surprise in per cent";
        public const string RewardToRisk = "the plan's target distance over its stop distance";
        public const string Freshness = "for a pullback the sessions since its trigger first fired, for a drift the sessions since its reaction";
        public const string BandStrength = "for a pullback the strength of the band holding the close";
        public const string VolumeMultiple = "for a breakout the session's volume over its 50-session average, for a drift the reaction session's";
        public const string RangeRatio = "for a breakout the mean range of the 20 sessions before over that of the 20 before them";
        public const string ReactionMoves = "for a drift the reaction session's rise in typical moves";
        public const string Breadth = "the share of the index's members closing above their 200-session average";
        public const string HighsLessLows = "the members at a 252-session high less those at a 252-session low, over the members held";
        public const string IndexOverLong = "the index's own series' close over its 200-session average, the S&P 500 by its index and the 400 and 600 by their funds";
        public const string Vix = "the VIX's close";
        public const string VixChange = "the VIX's close over its close 10 sessions before";
        public const string MidOverLarge = "IJH's return over SPY's across 63 sessions";
        public const string SmallOverLarge = "IJR's return over SPY's across 63 sessions";
        public const string CreditOverFifty = "HYG's close over its 50-session average";
        public const string Profit = "1 where the four newest quarters filed before the session sum their net income above nothing, 0 otherwise";
        public const string Coverage = "1 where those quarters' operating income is at least twice their interest expense or the company is a financial one, 0 otherwise";
        public const string RevenueGrowth = "the newest quarter's revenue as first filed before the session over the same quarter's a year before, less one";
        public const string GrowthChange = "that growth less the quarter before's growth on its own year before";
        public const string GrossMarginChange = "the newest quarter's gross profit over its revenue less the same quarter's a year before";
        public const string OperatingMarginChange = "the newest quarter's operating income over its revenue less the same quarter's a year before";
        public const string CashOverIncome = "the newest fiscal year's cash from operations over its net income, both as first filed before the session, where that income is above nothing";
        public const string ValuePlace = "the four newest quarters' net income as first filed before the session over the company's value, the share count its filings' covers state times the close, as the share of the same figure at the ends of its own twelve quarters before it that it stands above, a tie counting half";

        public const string ReturnWeek = "the return over 5 sessions";
        public const string ReturnMonth = "the return over 21 sessions";
        public const string LargestReturn = "the largest daily return of the 21 sessions to the session, after Bali, Cakici and Whitelaw (2011)";
        public const string VolatilityMonth = "the sample standard deviation of the daily returns of the 21 sessions to the session";
        public const string ResidualVolatility = "the sample standard deviation of the residuals of the daily returns of the 63 sessions to the session on the index's own series' over the same days, after Ang, Hodrick, Xing and Zhang (2006)";
        public const string Beta = "the slope of the daily returns of the 251 sessions to the session on the index's own series' over the same days, the 252 closes of the year the night holds, after Frazzini and Pedersen (2014)";
        public const string Skewness = "the skewness of the daily returns of the 63 sessions to the session, their mean cubed deviation over their population standard deviation cubed";
        public const string ClosePlace = "the mean over the 10 sessions to the session of the close's place in the session's range, close less low over high less low, a session whose high is its low left out";
        public const string UpVolumeShare = "the share of the 20 sessions' volume traded on sessions closing above the close before";
        public const string PriceVolumeCorrelation = "the correlation of the daily close change with the daily volume over the 20 sessions to the session";
        public const string Illiquidity = "the base-10 logarithm of the mean over the 21 sessions to the session of the absolute daily return over the close times the volume, a session trading nothing left out, after Amihud (2002)";
        public const string OvernightLessIntraday = "the sum over the 21 sessions to the session of the overnight returns, the open over the close before less one, less the sum of the intraday returns, the close over the open less one, after Lou, Polk and Skouras (2019)";
        public const string SectorMonth = "the return over 21 sessions less its GICS sector fund's between the same two days, the sector its company files as it stood on the session";
        public const string SectorQuarter = "the return over 63 sessions less its GICS sector fund's between the same two days";
        public const string LowRatio = "the close over the lowest low of the 252 sessions ending on the session";
        public const string Streak = "the signed count of the consecutive sessions to the session closing the same way on the close before, plus for rises and minus for falls, nought where the session closed level";
        public const string VolumeSlope = "the slope of on-balance volume over the 20 sessions to the session, each session's volume added on a rise and taken off on a fall, over the mean volume of those sessions";
        public const string AlphaOne = "Kakushadze's Alpha#1, rank(Ts_ArgMax(SignedPower(((returns < 0) ? stddev(returns, 20) : close), 2.), 5)) - 0.5, rank a member's place among the index's members on the session and Ts_ArgMax the place of the largest of the 5 sessions to it, 1 for the oldest";
        public const string AlphaTwo = "Kakushadze's Alpha#2, -1 * correlation(rank(delta(log(volume), 2)), rank(((close - open) / open)), 6), each rank a member's place among the index's members on each of the 6 sessions";
        public const string AlphaThree = "Kakushadze's Alpha#3, -1 * correlation(rank(open), rank(volume), 10), each rank a member's place among the index's members on each of the 10 sessions";
        public const string AlphaFour = "Kakushadze's Alpha#4, -1 * Ts_Rank(rank(low), 9), Ts_Rank the place of the session's own among the 9 sessions' places, 1 for the lowest";
        public const string AlphaSix = "Kakushadze's Alpha#6, -1 * correlation(open, volume, 10)";

        public const string InsiderNetBuy = "the dollars of the insiders' open-market purchases, code P, less those of their open-market sales, code S, filed in the 90 days before the session, over the company's value as the valuation reading works it, after Lakonishok and Lee (2001)";
        public const string InsiderBuyers = "the insiders with an open-market purchase filed in those 90 days, counted once each";
        public const string InsiderOfficerBuy = "1 where an officer bought in the open market in those 90 days, 0 where none did";
        public const string InsiderUnplannedSale = "the dollars of the open-market sales filed in those 90 days whose filing does not tick the Rule 10b5-1 box, over the company's value, none where the days reach before 2023-04-01, after Cohen, Malloy and Pomorski (2012)";

        public const string ReleaseResults = "the quarter against what was expected in the newest results release readable on the session and accepted within the 95 days before it, as the local model read it with its names and dates removed: 1 better, 0 in line, -1 worse, none not stated";
        public const string ReleaseOutlook = "that release's outlook: 1 raised, 0 kept, -1 lowered or withdrawn, none where none is given";
        public const string ReleaseTone = "the tone of management's own words in that release: 1 confident, 0 neutral, -1 cautious";
        public const string ReleaseDemand = "demand or orders in that release: 1 stronger, 0 steady, -1 weaker, none not stated";

        public const string RsiTwo = "the 2-session relative strength index on the session, Wilder's, the short-term dip's drop";
        public const string DropFall = "the fall over the 3 sessions to the session, the close 3 sessions before less the session's, in the session's typical moves";
        public const string DropLargest = "the largest fall of a single one of those 3 sessions, the close before it less its own, in the typical move standing before it";
        public const string DropVolume = "the mean volume of those 3 sessions over the mean of the 50 sessions before them";
        public const string DropAgainstSector = "the fall over those 3 sessions less the median of the same fall of the members of its GICS sector the index held that session, each in its own typical moves, none where fewer than two of them read one";
        public const string LargeBreadth = "on an S&P 400 or 600 row the S&P 500's breadth on the session, none on an S&P 500 row";
        public const string FundOverLong = "the index's own fund's close over its 200-session average, SPY's on an S&P 500 row and IJH's and IJR's on the S&P 400's and 600's";
        public const string VixOverMedian = "the VIX's close over the median of its closes on the 252 sessions of the exchange before the session, a session being a day the S&P 500's index closed";
        public const string CompanyValue = "the base-10 logarithm of the company's value on the session as the valuation reading works it, the newest share count its filings' covers state for a day before the session times the session's close on that count's basis";

        public const string FundamentalsScore = "the fundamentals scorecard's checks passed over the checks that applied and were read, none where fewer than five were read, after Piotroski (2000)";
        public const string GrossProfitability = "the four newest quarters' gross profit as first filed before the session over the total assets at the newest quarter's end, after Novy-Marx (2013)";
        public const string ReturnOnAssets = "the four newest quarters' net income over the total assets at the start of the four quarters";
        public const string ReturnOnAssetsChange = "that return less the same a year before";
        public const string LeverageFall = "the long-term debt over the year's average total assets a year before less the same at the newest quarter's end, higher where the debt fell";
        public const string CurrentRatioChange = "the current assets over the current liabilities at the newest quarter's end less the same a year before";
        public const string ShareFall = "one less the newest share count the covers state over the one standing a year before it, brought across each split between them, higher where the shares fell";
        public const string TurnoverChange = "the four newest quarters' sales over the total assets at their start less the same a year before";
        public const string GrossMarginYearChange = "the four newest quarters' gross profit over their sales less the same a year before";

        public const string RelativeStrengthHigh = "the close over the index's own series' close on the session, over the highest such ratio of the 252 sessions to it, 1 at a new high; none under 252 sessions or where the series holds no close by one of their days";
        public const string InsiderClusterBuyers = "the distinct insiders buying in the open market over the 21 sessions to the session, from the Form 4 filings filed before it; none where the store's insider history does not hold those days";
        public const string RatingActionsNet = "Yahoo's rating upgrades less downgrades dated over the 21 sessions before the session, from the stock's newest stored answer; none where the store holds no answer or the session is after the answer's ask";
        public const string EstimateRevisionMonth = "the current fiscal year's consensus earnings a share over its figure 30 days before, less 1, as the newest quarters fetch on or before the session states them; none before the first fetch or where either is missing";
        public const string ShortInterestShare = "the shares short FINRA last reported before the session over the newest share count the filings' covers state; none while no store holds the reports";

        public const string BusinessStateFiled = "the business's state off the SEC's facts as first filed before the session, as the night's place for a state: 0 improving, where both of the two newest filed quarters grew their sales on the same quarter a year earlier and widened their operating margin, 3 deteriorating, where both shrank and narrowed, 1 steady otherwise; none where fewer than two quarters hold the four figures";

        // From 23.10's second part the entry check's readings, each as the entry check reads it on the night.
        public const string StretchDays = "the close over its 20-session average, in typical days, the stored year's sessions held; none where fewer than 20 or no typical move";

        public const string StretchShare = "the share of the stored year's sessions holding the 20-session average that stood as far above it as the session or further, in per cent";

        public const string GainTwenty = "the gain over the 20 sessions to the session, in per cent; none where the stored year holds fewer";

        public const string LargestGain = "1 where the session's one-day gain is a rise larger than each of the 249 before it, 0 where it is not; none where fewer than 250 are held";

        public const string NightVolumeRatio = "the session's volume over the mean of the 50 sessions to it, the session's own among them, as the entry check reads it";

        public const string GapDays = "the gap up to the session, its low over the session before's high, in typical days, 0 where its low does not stand above that high; none where no typical move is held";

        public const string HighestSinceYears = "the years since an earlier session last closed at or above the session's close, or since the history's first session where none did";

        public const string OlderDays = "the nearest band above the close from the sessions before the stored year, its low over the close in typical days; none where no band stands above or no typical move is held";

        public const string OneOffShare = "the largest one-off item the newest results release accepted by the session names, its amount a share over the beat on the estimate, where the release ties it to earnings per share and says which way and a beat above nought is stored, 0 where the release names no item; none where no release is stored, or an item's share is not read";
    }

    // src/EquityBrief.Core/Ledger/FundamentalScorecard.cs
    public static class FundamentalScorecard
    {
        public const string NetIncomeLine = "net income";
        public const string CashLine = "cash from operations";
        public const string AssetsLine = "total assets";
        public const string DebtLine = "long-term debt";
        public const string CurrentAssetsLine = "current assets";
        public const string CurrentLiabilitiesLine = "current liabilities";
        public const string SharesLine = "share count";
        public const string SalesLine = "sales";
        public const string GrossProfitLine = "gross profit";
    }

    // src/EquityBrief.Core/Ledger/TradeFills.cs
    public static class TradeFills
    {
        public const string Waiting = "buys at the next open";

        public const string NoNextSession = "no next session";

        public const string NoOpen = "no open on its next session";

        public const string NoRisk = "no risk at its close";
    }

    // DipRule.cs: the short-term dip's chips, rule sentence, market levels and gate reasons.
    public static class DipRule
    {
        public const string DollarFloorChip = "dollar floor 1 times";

        public static string CloseChip(decimal lowestPrice) => FormattableString.Invariant($"close at least ${lowestPrice:0.##}");

        public static string TrendChip(int trendSessions) => FormattableString.Invariant($"above its {trendSessions}-session average");

        public static string FiftyChip(int fiftySessions) => FormattableString.Invariant($"its {fiftySessions}-session above it");

        public static string FallChip(double fall, int dropFallSessions) => FormattableString.Invariant($"down {fall:0.##} moves in {dropFallSessions} sessions");

        public static string RsiChip(int rsiSessions, double rsiUnder) => FormattableString.Invariant($"{rsiSessions}-session RSI under {rsiUnder:0.##}");

        public static string VolumeChip(double volume, int volumeSessions) =>
            FormattableString.Invariant($"drop volume under {volume:0.##} times its {volumeSessions}-session average");

        public static string ReportChip(int dropSessions) => FormattableString.Invariant($"no report in {dropSessions} sessions or ahead");

        public static string StopChip(double stopMoves) => FormattableString.Invariant($"stop {stopMoves:0.##} moves");

        public static string RsiExitChip(int rsiSessions, int rsi) => FormattableString.Invariant($"sold when its {rsiSessions}-session RSI closes over {rsi}");

        public const string UpCloseExitChip = "sold on its first up close";

        public static string AverageExitChip(int exitAverage) => FormattableString.Invariant($"sold over its {exitAverage}-session average");

        public static string LimitChip(int timeLimit) => FormattableString.Invariant($"{timeLimit} sessions at most");

        public const string MarketOffChip = "market setting off";

        public static string MarketChip(string words) => "market setting " + words;

        public static string ClosingAbove(int trendSessions) => FormattableString.Invariant($"A stock closing above its {trendSessions}-session average");

        public static string FiftyAbove(int fiftySessions) => FormattableString.Invariant($", its {fiftySessions}-session average above that,");

        public static string FellAtLeast(double fall, int dropFallSessions) =>
            FormattableString.Invariant($" that fell at least {fall:0.##} typical moves over the {dropFallSessions} sessions to the listing");

        public static string RsiClosesUnder(int rsiSessions, double rsiUnder) =>
            FormattableString.Invariant($" whose {rsiSessions}-session relative strength index closes under {rsiUnder:0.##} after a sharp drop");

        public static string OnAMeanVolume(double volume, int volumeSessions) =>
            FormattableString.Invariant($" on a mean volume under {volume:0.##} times its {volumeSessions}-session average");

        public static string NoReportIn(int dropSessions) =>
            FormattableString.Invariant($", with no report on any of the {dropSessions} sessions to the listing nor one due before its time limit ends");

        public static string ListedOnlyOn(string condition) => ", listed only on a session " + condition;

        public static string BoughtAtTheOpen(double stopMoves) =>
            FormattableString.Invariant($". Bought at the next session's open, its stop {stopMoves:0.##} typical moves under the listing close and no target; sold at the next open after ");

        public static string RsiClosesBackAbove(int rsiSessions, int rsi) => FormattableString.Invariant($"its {rsiSessions}-session RSI first closes back above {rsi}");

        public const string FirstUpClose = "its first close above the close before";

        public static string FirstCloseAboveAverage(int exitAverage) => FormattableString.Invariant($"the first close above its {exitAverage}-session average of closes");

        public static string OrAStop(int timeLimit) =>
            FormattableString.Invariant($" or a close under its stop, and at the close of its {timeLimit}th session where neither came.");

        public const string OffWords = "no market setting";

        public const string OffCondition = "of any market";

        public static string OwnBreadthWords(double floor) => FormattableString.Invariant($"the index's own breadth at {floor * 100:0} per cent");

        public static string OwnBreadthCondition(double floor) => FormattableString.Invariant($"the index's own breadth stands at {floor * 100:0} per cent or above");

        public static string LargeBreadthWords(double largeBreadthFloor) => FormattableString.Invariant($"the S&P 500's breadth at {largeBreadthFloor * 100:0} per cent");

        public static string LargeBreadthCondition(double largeBreadthFloor) =>
            FormattableString.Invariant($"the S&P 500's breadth stands at {largeBreadthFloor * 100:0} per cent or above");

        public static string FundWords(string fund, int fundTrendSessions) => FormattableString.Invariant($"{fund} above its {fundTrendSessions}-session average");

        public static string FundCondition(string fund, int fundTrendSessions) => FormattableString.Invariant($"{fund} closes above its {fundTrendSessions}-session average");

        public static string VixWords(int vixMedianSessions) => FormattableString.Invariant($"the VIX above its own median of the {vixMedianSessions} sessions before");

        public static string VixCondition(int vixMedianSessions) => FormattableString.Invariant($"the VIX closes above its median of the {vixMedianSessions} sessions before");

        public static string MarketSettingClosed(string words, string read) => FormattableString.Invariant($"its market setting, {words}, closed: {read}");

        public const string MarketClosed = "the market setting closed";

        public const string UnderTheFloors = "under the price or dollar volume floor";

        public const string ShortHistory = "fewer than 200 sessions stored";

        public const string Gapped = "a session missing from its stored year";

        public const string SuspectSeries = "its series marked suspect";

        public const string NoTrend = "not above its 200-session average";

        public const string NoDrop = "the 2-session RSI not under its floor";

        public const string ReportNear = "a report in the drop or before the time limit";

        public const string NoMove = "no typical move to place its stop by";

        public const string StopUnderNothing = "its stop at or under nothing";

        public const string NoMarketSetting = "the dip reads no market setting";

        public const string AverageNotRead = "its average not read";

        public static string ClosedAgainst(double? close, int trendSessions, double? average) =>
            FormattableString.Invariant($"closed {close:0.##} against its {trendSessions}-session average of {average:0.##}");

        public static string StopFallsUnderNothing(double stopMoves, double? move, double? close) =>
            FormattableString.Invariant($"a stop {stopMoves:0.##} typical moves of {move:0.##} under its close of {close:0.##} falls at or under nothing");

        public const string MoveNotRead = "its typical move not read";

        public const string RsiNotRead = "its 2-session RSI not read";

        public static string RsiNotUnder(double? rsi, double rsiUnder) => FormattableString.Invariant($"its 2-session RSI of {rsi:0.#} not under {rsiUnder:0.##}");

        public const string FallNotRead = "its fall over the drop not read";

        public static string FallUnder(double? fall, int dropFallSessions, double floor) =>
            FormattableString.Invariant($"a fall of {fall:0.##} typical moves over its {dropFallSessions} sessions, under {floor:0.##}");

        public const string VolumeNotRead = "its drop's volume not read";

        public static string VolumeNotUnder(double? volume, int volumeSessions, double? volumeUnder) =>
            FormattableString.Invariant($"its drop's mean volume at {volume:0.##} times its {volumeSessions}-session average, not under {volumeUnder:0.##}");

        public static string MissingSession(DateOnly missing) =>
            "its stored year missing the session of " + missing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", so nothing is read across it";

        public const string MarkedSuspect = "its series marked suspect, its corporate action's refetch not stored, so nothing is read from it";

        public static string FewerThan(int stored, int trendSessions) => FormattableString.Invariant($"{stored} sessions stored, fewer than the {trendSessions} its average reads");

        public const string ReportOnTheNight = "a report on the night";

        public static string ReportBack(int inside) =>
            FormattableString.Invariant($"a report {-inside} session{(inside == -1 ? string.Empty : "s")} back, inside the drop");

        public static string ReportDue(int inside) =>
            FormattableString.Invariant($"a report due in {inside} session{(inside == 1 ? string.Empty : "s")}, before its time limit ends");
    }

    // DipFamily.cs: the words of the dip's step, its run row, its settings and its market gate.
    public static class DipFamily
    {
        public const string Moved = "its evaluator's code moved since it was registered, so it is kept no further until it is registered again";

        public const string NoSession = "no session: the store holds no bar";

        public static string StoppedOn(string fault) => "; the step stopped on " + fault;

        public static string NotComputedTonight(string index, string fault) => $"{index}: not computed tonight, {fault}";

        public static string OnTheIndex(string index, int members, int passed, int listed, int heldByATrade, int underAnother, int tradesKept, int tradesEnded, int rules) =>
            FormattableString.Invariant(
                $"{index}: {members} member(s) read, {passed} passed, {listed} listed, {heldByATrade} held back by a trade still open, {underAnother} listed under another family, {tradesKept} trade(s) kept, {tradesEnded} ended{(rules > 1 ? FormattableString.Invariant($", {rules} rules kept") : string.Empty)}");

        public const string Floors = "the price and dollar volume floors the index families read";

        public static string NoLevel(string key) => $"its market setting {key} is no level the index reads";

        public static string OwnBreadthRead(string breadth) => "the index's own breadth at " + breadth;

        public static string LargeBreadthRead(string breadth) => "the S&P 500's breadth at " + breadth;

        public static string FundOnTheNight(string fund, bool above) => $"{fund} {(above ? "above" : "not above")} its average on the night";

        public const string NoSessionInTheSeries = "no session of the night in the market series";

        public static string VixOnTheNight(bool over) => $"the VIX {(over ? "above" : "not above")} its median on the night";
    }

    // FamilyLister.cs: the family lister's run row.
    public static class FamilyLister
    {
        public const string NoSession = "no session is stored, so no list was drawn";

        public static string NoFilterResult(DateOnly session) =>
            FormattableString.Invariant($"the swing filter stored no result for {session:yyyy-MM-dd}, so no list was drawn");

        public static string ListedFor(int listed, DateOnly? night) =>
            FormattableString.Invariant($"{listed} listed for {night:yyyy-MM-dd}: ");

        public static string FamilyPassed(int listed, int qualified, string family) =>
            FormattableString.Invariant($"{listed} of the {qualified} the {family} family passed");

        public static string HeldBack(int openTrade, int underAnother, int pastFive, int listedANight) =>
            FormattableString.Invariant($"; {openTrade} held back by a trade still open, {underAnother} listed under another family, {pastFive} past a family's {listedANight}");

        public static string IndexTradesUnread(string fault) => $"; the S&P 400's and 600's trades could not be read, so none held a stock back: {fault}";
    }

    // SetupFamilies.cs: each family's card heading, line above it and rule, and a provisional rule's status.
    public static class SetupFamilies
    {
        public const string PullbacksHeading = "Pullbacks to support";

        public const string PullbacksEyebrow = "Pullback in an uptrend";

        public const string PullbacksRule = "A strong stock in an uptrend dips to a support band and turns back up. Stop below the band, target at the next band above.";

        public const string BreakoutsHeading = "Breakouts to a new high";

        public const string BreakoutsEyebrow = "Breakout from a base";

        public const string BreakoutsRule = "A stock closes above its highest price of the past six months on heavy volume, after its daily ranges narrowed. Stop one and a half typical moves below, raised as the price climbs and never lowered; no target.";

        public const string EarningsDriftEyebrow = "After a strong report";

        public const string EarningsDriftRule = "A company beats its estimate and the stock closes up at least half a typical day's move on twice its usual volume. Bought within three sessions while it holds above that day's low. Stop at that day's low, target at the next band above or 2.5 times the risk, whichever is nearer.";

        public const string SectorLeadersEyebrow = "Strongest stocks of the strongest sectors";

        public const string SectorLeadersRule = "A stock in the top quarter of one of the three strongest sectors, at a pullback's buy point. Stop below the band, target at the next band above, as a pullback's.";

        public const string FundamentalsFirstHeading = "Improving businesses in an uptrend";

        public const string FundamentalsFirstEyebrow = "Business first, then the chart";

        public const string FundamentalsFirstRule = "A company whose revenue grew on the year-earlier quarter and faster than the quarter before, whose gross or operating margin widened on the year, whose cash from operations covered its net income and whose four newest quarters and newest quarter were profitable, closing above its 200-day average with the 50-day above it, bought at a pullback's buy point. Stop below the band, target at its reward to risk, as the pullback's base.";

        public const string DipsHeading = "Short dips in an uptrend";

        public const string DipsEyebrow = "Sharp drop in an uptrend";

        public const string SectorHeavyweightsEyebrow = "Largest companies leading their sectors";

        public const string SectorHeavyweightsRule = "On each month's first session, or the first after it whose stored year holds the closes its readings need, among each sector's ten largest companies, the two whose twelve-month returns beat the sector fund's by the most, where they beat it at all, their close is above their 50-day average and that above their 200-day, and their beta is at least one, bought at the next session's open. Held while it leads: sold at the close of a later month's rebalance where the rule would no longer buy it.";

        public const string Provisional = "provisional: not yet frozen; its record starts at the freeze";

        public const string ProvisionalStatus = "Provisional: not yet frozen";
    }

    // OpenTrades.cs: the exclusion the filter writes for a stock whose trade is open, compared by its opening.
    public static class OpenTrades
    {
        public const string ExclusionPrefix = "an open trade from ";
    }

    // RuleRows.cs: the reasons a stored funnel names its parts by, each a key matched against a stored reason.
    public static class RuleRows
    {
        public const string RevenueNotAbove = "its print's revenue not above the same quarter's a year before";

        public const string MarketCheckClosed = "the market check closed";

        public const string UnderTheFloors = "under the price or dollar volume floor";

        public const string ProfitCheck = "the profit check";

        public const string CoverCheck = "the interest cover check";
    }

    // src/EquityBrief.Core/Filter/SwingGates.cs
    public static class SwingGates
    {
        public const string Trend = "trend and strength";

        public const string EarningsExclusion = "earnings inside the holding window";

        public const string PullbackBandValue = "inside an anchored support band";

        public static string BreadthNotAvailable(int counted, int members) =>
            FormattableString.Invariant($"breadth is not available: {counted} of the {members} members hold a close and a 200-day average, fewer than half");

        public const string NoMarketReading = "no market reading is stored for the night";

        public static string BreadthAgainstFloor(double share, bool passed, double floor) =>
            FormattableString.Invariant($"breadth {share * 100:0.0}% {(passed ? "at or above" : "below")} its floor of {floor * 100:0.#}%");

        public const string NoTrendState = "no trend state is stored for the night";

        public static string NoPlace(int bars) =>
            FormattableString.Invariant($"no place among the members' returns: {bars} bars, too few for a return over both spans");

        public static string TrendAndStrength(bool up, string trendState, double strength, bool strong, double floor) =>
            FormattableString.Invariant($"{(up ? "an uptrend" : trendState.Replace('_', ' ') + ", not an uptrend")}; strength {strength:0.000} {(strong ? "at or above" : "below")} its floor of {floor:0.000}");

        public const string NoReadings = "no swing readings are stored for the night";

        public const string PullbackIntoTheBand = "a pullback into an anchored support band";

        public static string DepthOutside(double depth, double low, double high) =>
            FormattableString.Invariant($"depth {depth:0.00} outside {low:0.##} to {high:0.##} typical moves");

        public static string DryUpNotBelow(double dryUp, double ceiling) =>
            FormattableString.Invariant($"dry-up {dryUp:0.00} not below {ceiling:0.##}");

        public const string HighOnTheNight = "the high was made on the night";

        public const string InsideNoBand = "the close is inside no anchored support band";

        public const string TheSessionBefore = "the session before";

        public static string SessionsBeforeTonight(int back) =>
            FormattableString.Invariant($"the session {back} before tonight");

        public const string EventTheSessionBefore = "event the session before";

        public static string FiredTonight(string before) =>
            $"the trigger fired tonight and not on {before}";

        public static string FirstFired(string session, int arrived, int window) =>
            FormattableString.Invariant($"the trigger first fired on {session}, {arrived} session(s) before tonight, inside the {window}-session window");

        public const string NoPreviousHigh = "no previous session's high to read the trigger against";

        public static string NoResultFor(string session) =>
            $"no gate result is stored for {session}, so the trigger's arrival cannot be read";

        public static string FiredBeforeToo(string before) =>
            $"the trigger fired on {before} too, so tonight is not its arrival";

        public const string NoEvent = "the close is not above the previous session's high and did not come back into the band";

        public static string FiredOnEverySession(string back, int window) =>
            FormattableString.Invariant($"the trigger fired on every session back to {back}, so it did not arrive in the last {window} sessions");

        public static string DidNotArrive(int window) =>
            FormattableString.Invariant($"the trigger did not arrive in the last {window} sessions");

        public const string NoPlan = "no plan is stored for the night";

        public const string PlanComputesNoRatio = "the plan's arithmetic computes no reward to risk";

        public const string NoClose = "no close is stored for the night";

        public const string NoSetupBand = "no setup band to set a stop at";

        public const string NoTypicalMove = "no typical move to place the stop and the target by";

        public const string NoBandBeneath = "the setup band's low edge is less than a typical move below the entry and no support band sits beneath it to set a stop at";

        public const string NoBandClearAbove = "no band sits two typical moves or more above the close to set a target at";

        public const string NoBandAbove = "no band above the close to set a target at";

        public const string StopNotBelow = "the stop is not below the entry";

        public const string RewardToRiskValue = "reward to risk";

        public const string StopInMovesValue = "stop in typical moves";

        public const string NoRewardToRisk = "no reward to risk";

        public const string NoStopDistance = "no typical move to read the stop's distance in";

        public static string RewardAndStop(decimal rewardToRisk, bool enough, double floor, double moves, bool placed, double low, double high) =>
            FormattableString.Invariant($"reward to risk {rewardToRisk} {(enough ? "at or above" : "below")} {floor:0.##}; the stop {moves:0.00} typical moves below the entry, {(placed ? "inside" : "outside")} {low:0.##} to {high:0.##}");

        public const string NoEarningsDate = "no earnings date is on file, so none is excluded for one";

        public const string EarningsPastTheTable = "the next earnings date is past the end of the closure table, so its sessions are not counted";
    }

    // src/EquityBrief.Worker/Filter/SwingFilter.cs
    public static class SwingFilter
    {
        public const string NoReadings = "no swing readings are stored for the night";

        public static string FiledStates(int read, int members, int deteriorating) =>
            FormattableString.Invariant($"; business states read off the SEC's facts for {read} of {members} member(s), {deteriorating} deteriorating");

        public static string EstimatesRead(int asked) =>
            FormattableString.Invariant($"; estimates read for {asked} member(s) a rule reading them passed on everything else, ");

        public const string NoneAsked = ": none asked for, since the night handed nothing to ask with";
    }

    // src/EquityBrief.Worker/Filter/SwingReader.cs
    public static class SwingReader
    {
        public const string NoBarStored = "no bar is stored for the name";

        public const string NoBarThisSessionOpening = "no bar for this session";

        public const string GapOpening = "the stored series has a gap at";

        public const string LastSessionStored = "; the last session stored for the name is ";

        public const string GapEnding = ", so nothing is read across it";

        public static string Counts(int members, int read, int stale, int gapped, int none, int dropped) =>
            FormattableString.Invariant($"{members} member(s), {read} read, {stale} with no bar for the session, {gapped} gapped, {none} holding no bar, {dropped} dropped");

        public static string BreadthNotAvailable(int counted, int members) =>
            FormattableString.Invariant($"not available, {counted} of {members} members holding a close and a 200-day average");
    }

    // src/EquityBrief.Core/Filter/SwingFunnel.cs
    public static class SwingFunnel
    {
        public const string NoFilterVersion = "no filter version open, so section 17's proposed values";
    }

    // src/EquityBrief.Worker/Bars/SeriesGapStop.cs
    public static class SeriesGapStop
    {
        public const string StoppedAtAGap = " stopped at a gap (";

        public const string NotCheckedForGaps = " not checked for gaps";
    }

    // src/EquityBrief.Core/Candidates/ShadowColumn.cs
    public static class ShadowColumn
    {
        public static string NoEvaluator(string evaluator) =>
            $"the code carries no evaluator named '{evaluator}'";

        public static string VersionMoved(string evaluator, string registered, string carried) =>
            $"registered under {evaluator} at {registered} and the code carries " +
            $"{carried}, so a score would be about a rule the register does not name";

        public static string NotComputed(string missing, string ticker) =>
            $"the night computed no '{missing}' for {ticker}";
    }

    // The words of src/EquityBrief.Worker/Indices/IndexFamilies.cs.
    public static class IndexFamilies
    {
        public const string NoSession = "no session: the store holds no bar";

        public const string StepStoppedOn = "; the step stopped on ";

        public const string NoneToSellAgain = "no holding sold on leaving the index stands at a close other than its stock's on the session it was sold";

        public const string SoldAgainOpening = "sold again at its stock's close on the session it was sold: ";

        public static string NotComputedTonight(string index, string fault) =>
            $"{index}: not computed tonight, {fault}";

        public static string NightRead(string index, int members, double? breadth, bool marketOpen, IReadOnlyDictionary<string, int> passed, int listed, int heldByATrade, int tradesKept, int tradesEnded, bool rebalanced, string? waits, int entered, int ended, int held) =>
            FormattableString.Invariant(
                $"{index}: {members} member(s) read, breadth {(breadth is { } value ? value.ToString("0.00", CultureInfo.InvariantCulture) : "not read")}, the market check {(marketOpen ? "open" : "closed")}, {string.Join(", ", passed.Select(pair => $"{pair.Value} passed by the {pair.Key}"))}, {listed} listed, {heldByATrade} held back by a trade still open, {tradesKept} trade(s) kept, {tradesEnded} ended; the sector heavyweights {(rebalanced ? "rebalanced" : "carried")}{(waits is { } waiting ? ", " + waiting : string.Empty)}, {entered} bought, {ended} sold, {held} held");

        public static string RulesKept(int rules, int trades, int ended, int benchmarked) =>
            FormattableString.Invariant($"; {rules} registered rule(s) kept {trades} trade(s) on their own lists, {ended} ended and {benchmarked} benchmarked");

        public static string RuleBooks(bool rebalanced, int entered, int ended, int held, string? waits) =>
            FormattableString.Invariant($"; the registered heavyweights rules' books {(rebalanced ? "rebalanced" : "carried")}, {entered} bought, {ended} sold, {held} held{(waits is { } ruleWaits ? "; " + ruleWaits : string.Empty)}");

        public static string EvaluatorMoved(string candidate, string evaluatorVersion, string version) =>
            $"'{candidate}', its evaluator moved from {evaluatorVersion} to {version}";

        public const string CostsPaid = "the published spread";

        public static string BeforeTheYear(int held, int sessionsReadTo, int read) =>
            FormattableString.Invariant($"the store's year holds {held} of the {sessionsReadTo} sessions the year's high and the heavyweights' closes read, the sessions before it read from the pulled history for {read} member(s)");

        public static string TooFew(string[] few) =>
            FormattableString.Invariant($", which holds too few for {few.Length}, each reading none of them: {string.Join(", ", few)}");

        public const string NoLongerTheLeader = "no longer the leader";

        public const string LeftTheIndex = "left the index";

        public static string NoMemberClose(DateOnly night) =>
            FormattableString.Invariant($"the rebalance waits, since no member of the index holds a close on {night:yyyy-MM-dd}");

        public const string UnderTheAverage = "a close under its 200-day average";

        public const string NoLongerBought = "no longer among those it buys";

        public const string RuleRetired = "its rule retired";

        public static string Resold(string index, string ticker, DateOnly entered, DateOnly sold, string exit, string close) =>
            FormattableString.Invariant($"{index} {ticker} bought {entered:yyyy-MM-dd} and sold {sold:yyyy-MM-dd}, at {exit} and now at {close}");
    }

    // The words of src/EquityBrief.Worker/Indices/IndexNightRead.cs.
    public static class IndexNightRead
    {
        public const string MarketClosed = "the market check closed";

        public const string UnderTheFloors = "under the price or dollar volume floor";

        public const string NoProfit = "the profit check";

        public const string NoCover = "the interest cover check";

        public const string NotReadWithoutASetup = "not read: no setup tonight";

        public const string OwnBreadth = "the index's own breadth";

        public const string LargeBreadth = "the S&P 500's breadth";

        public static string Market(string reading, string percent, double share, double floor) =>
            FormattableString.Invariant($"{reading} {percent}% {(share >= floor ? "at or above" : "below")} its floor of {floor * 100:0.#}%");

        public static string NotReadTonight(string reading) =>
            $"{reading} not read tonight";

        public const string NoMarketCheck = "its rule reads no market check";

        public const string NotByItsHooks = "a condition or a score floor its approved setting adds";

        public static string Exposure(int picks, string breadth, double floor, string over, int vixMedianSessions) =>
            FormattableString.Invariant($"the exposure allows {picks} pick(s) tonight: the index's breadth {breadth} against its floor of {floor:0.##}, and the VIX at {over} of its median of the {vixMedianSessions} sessions before, at or under 1 holding");

        public const string VixOverMedian = "vix over median";

        public static string Traded(decimal mean) =>
            FormattableString.Invariant($"{mean:#,0} a session over the 50 sessions to tonight");

        public const string FewerThanFifty = "fewer than 50 sessions of volume to tonight";

        public static string AndItsFloor(decimal held, decimal floors) =>
            FormattableString.Invariant($" and its floor of {held * floors:#,0}");

        public static string ClosedAt(decimal traded, string traded50, decimal least, string floorWords) =>
            FormattableString.Invariant($"closed at {traded:0.00} with {traded50}, against the least close of {least:0.00}{floorWords}");

        public static string QuartersFiled(int filed, int quarters) =>
            FormattableString.Invariant($"{filed} of the {quarters} quarters the profit check sums filed with their net income before tonight");

        public static string NetIncomeNotAbove(decimal netIncome, int quarters) =>
            FormattableString.Invariant($"net income of {netIncome:#,0} over its {quarters} newest quarters as filed, not above nothing");

        public const string CoverNotRead = "its interest cover not read for tonight";

        public const string CoverUnderItsFloor = "its interest cover under its floor";
    }

    // The words of src/EquityBrief.Worker/Sweep/SweepIdeas.cs.
    public static class SweepIdeas
    {
        public const string BreadthRising = "the share of members above their own 200-day average higher than ten sessions before";

        public const string BreadthRisingEvidence = "Zaremba and others (2021): country indices with high breadth beat those with low breadth by 1.19% a month, a comparison of markets that times none. Weak evidence.";

        public const string HighsOverLows = "more members making a new 252-session high than a new 252-session low";

        public const string HighsOverLowsEvidence = "Practitioner use only, from Fosback (1979); no peer-reviewed support was found. Judgement.";

        public const string IndexOverFifty = "the index closing above its 50-session average";

        public const string IndexOverFiftyEvidence = "No study was found for the shorter average. Judgement.";

        public const string IndexOverTwoHundred = "the index closing above its 200-session average";

        public const string IndexOverTwoHundredEvidence = "Faber (2007): a 10-month average rule cut the worst fall from 83.66% to 42.24% with a similar return. Cooper, Gutierrez and Hameed (2004): momentum made 0.93% a month after up markets and lost 0.37% after down markets, 1929 to 1995. Evidence.";

        public const string VixUnderTwenty = "the VIX closing under 20";

        public const string VixUnderTwentyEvidence = "Wang and Xu (2015): momentum profits are larger when market volatility is low, 1929 to 2009. Daniel and Moskowitz (2016): momentum crashes follow market falls when volatility is high. Barroso and Santa-Clara (2015): scaling by realised volatility raised the Sharpe ratio from 0.53 to 0.97. All three read realised volatility and not the VIX. Evidence for the direction; the level of 20 is judgement.";

        public const string VixFalling = "the VIX closing under its close ten sessions before";

        public const string VixFallingEvidence = "The same three studies of realised volatility as a5. Evidence for the direction; the ten sessions are judgement.";

        public const string TouchedStop = "after the buy, a session opening under the stop sells at its open and one whose low touches the stop sells at the stop, the stop read before the target, which stays on closes";

        public const string TouchedStopEvidence = "Han, Zhou and Zhu (2016): a 10% stop on momentum stocks, sold when the open or the close crossed it, cut the worst month from -49.79% to -11.36% and more than doubled the Sharpe ratio, 1926 to 2013. Kaminski and Lo (2014): stops add where returns persist and subtract under a random walk. Lo and Remorov (2017): tight stops underperform on US stocks after costs. Evidence for a stop; a touch at the day's low is judgement.";

        public const string BestOfThree = "only the first three of the night's list, after a stock's open trade has kept it off, in the list's own order: reward to risk, then strength, then band strength, then ticker";

        public const string BestOfThreeEvidence = "No study was found. Judgement.";

        public const string TrailTwo = "no target: the stop the higher of the plan's stop and the highest close since the buy less 2 typical moves, never lowered, sold at the first close under it, 63 sessions at most";

        public const string TrailTwoEvidence = "Dai, Marshall, Nguyen and Visaltanachoti (2021): trailing stops give a lower average return than holding and cut downside risk, and wider ones survive costs. Evidence, which predicts a lower average; the 2 typical moves are judgement.";

        public const string TrailThree = "the same trailing stop at 3 typical moves";

        public const string TrailThreeEvidence = "The same study as d2. Evidence, which predicts a lower average; the 3 typical moves are judgement.";

        public const string StopAtLeastAMove = "the stop no closer than one typical move: the stop setting of 1 to 4";

        public const string StopAtLeastAMoveEvidence = "Lo and Remorov (2017), on tight stops, loosely. Judgement.";

        public const string HoldTen = "the base's own exit held at most 10 sessions";

        public const string HoldTwenty = "the base's own exit held at most 20 sessions";

        public const string ShortHoldEvidence = "No study of so short a hold was looked up. Judgement, the operator's idea of 2026-10-01.";

        public const string TogetherOpening = "the passing switches together: ";

        public const string TogetherEvidence = "Each as its own idea states.";

        public const string TheStartingPoint = "the starting point";

        public const string BaseVariant = "the reward-to-risk floor at 2";

        public const string OnTheStartingPoint = " on the starting point";

        public const string MarketVariant = "the market floor at 50%";

        public const string MarketVariantOnTheStartingPoint = "market 50% on the starting point";

        public const string DepthVariant = "the pullback's depth from 1.5 typical moves";

        public const string DepthVariantOnTheStartingPoint = "depth 1.5 on the starting point";
    }

    // The words of src/EquityBrief.Core/Sweep/SweepAxes.cs.
    public static class SweepAxes
    {
        public static string RewardToRisk(string number) =>
            $"reward to risk {number}";
    }

    // The words of src/EquityBrief.Core/Families/HeavyweightRule.cs.
    public static class HeavyweightRuleWords
    {
        public static string WaitsForSessions(int sessionsNeeded, int held, DateOnly night) =>
            FormattableString.Invariant($"the rebalance waits for a night the store's year and the pulled history before it hold the {sessionsNeeded} sessions its readings need, since they hold {held} to {night:yyyy-MM-dd}");

        public static string NoLead(DateOnly night) =>
            FormattableString.Invariant($"the rebalance waits, since no sector's largest companies read a lead on {night:yyyy-MM-dd}");

        public static string NoBeta(DateOnly night) =>
            FormattableString.Invariant($"the rebalance waits, since no sector's largest companies read a beta on {night:yyyy-MM-dd}");
    }

    // The words of src/EquityBrief.Worker/Families/HeavyweightBook.cs.
    public static class HeavyweightBook
    {
        public const string NoSession = "the store holds no session";

        public static string ReadLater(string newest, DateOnly night) =>
            FormattableString.Invariant($"the book has read {newest}, a later session than {night:yyyy-MM-dd}, and reads no earlier one");

        public static string NoEvaluator(string evaluator) =>
            $"the code carries no evaluator named '{evaluator}'";

        public static string EvaluatorMoved(string evaluator, string evaluatorVersion, string version) =>
            $"registered under {evaluator} at {evaluatorVersion} and the code carries {version}";

        public static string WaitsForCloses(string[] missing, DateOnly night) =>
            FormattableString.Invariant($"the rebalance waits for a night the store holds a close for {string.Join(", ", missing)} on, since none is stored for {night:yyyy-MM-dd}");

        public static string BeforeTheYear(int held, int need, int read) =>
            FormattableString.Invariant($"the store's year holds {held} of the {need} sessions the heavyweights' closes read, the sessions before it read from the pulled history for {read} member(s)");

        public static string TooFew(List<string> few) =>
            FormattableString.Invariant($", which holds too few for {few.Count}, each reading no return and no beta: {string.Join(", ", few)}");

        public const string TheRegisteredRules = "; the registered rules: ";

        public static string KeptByNoBook(string candidate, string fault) =>
            $"'{candidate}' FAILURE: kept by no book, {fault}";

        // From 23.10's second part a rule whose conditions or score read the catalogue's readings on a night that supplied none.
        public const string HooksUnread = "the rebalance waits for a night that reads the catalogue's readings its conditions or its score read";
    }

    // The words of src/EquityBrief.Core/Families/FamilyRule.cs.
    public static class FamilyRuleWords
    {
        public const string IndexGate = "index over its average";

        public const string VixGate = "VIX under its close before";

        public static string OnlyAboveItsAverage(int indexAverageSessions) =>
            FormattableString.Invariant($", only on nights the index closes above its {indexAverageSessions}-session average");

        public static string OnlyUnderItsCloseBefore(int vixLookbackSessions) =>
            FormattableString.Invariant($", only on nights the VIX closes under its close {vixLookbackSessions} sessions before");

        public static string IndexClosed(double held, double mean, int count) =>
            FormattableString.Invariant($"the index closed {held:0.##} {(held > mean ? "above" : "at or below")} the average of its {count} closes to the night, {mean:0.##}");

        public static string IndexClosesShort(int count) =>
            FormattableString.Invariant($"the index's stored closes do not hold all {count} sessions to the night");

        public static string VixClosed(double held, double earlier, int count) =>
            FormattableString.Invariant($"the VIX closed {held:0.##} {(held < earlier ? "under" : "at or above")} its close {count} sessions before, {earlier:0.##}");

        public static string VixClosesShort(int count) =>
            FormattableString.Invariant($"the VIX's stored closes do not hold the night and the session {count} before it");

        public const string RewardToRiskValue = "reward to risk";

        public const string NoMarketCheck = "the swing filter stored no market check for the night";
    }

    // QualityRule.cs
    public static class QualityRule
    {
        public static string Words(int scoreFloorInNine, string valuationWords, bool trend, int holdings, bool sellOnFiling, bool soldUnderAverage) =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"a score of at least {scoreFloorInNine} in 9 of the checks that apply, {valuationWords}{(trend ? ", the close above its 200-day average" : string.Empty)}, the top {holdings} kept, each sold at {(sellOnFiling ? "a reshuffle or on a filing dropping its score" : "a reshuffle")} where it no longer qualifies{(soldUnderAverage ? " and at a close under its 200-day average" : string.Empty)}");

        public const string ValuationOff = "its price over earnings not read";

        public const string ValuationAtOrBelowMedian = "its price over earnings at or below its own median of 12 quarters";

        public const string ValuationCheapestThird = "its price over earnings in the cheapest third of its own 12 quarters";

        public const string Label = "Quality at a fair price";

        public const string Description = "Holds the companies with the strongest fundamentals that are not expensive against their own history, reshuffled monthly.";

        public const string NoWord = "too few checks read for a score";

        public const string UnderTheFloor = "a score under the floor";

        public const string NotValued = "no price over earnings read";

        public const string TooDear = "dearer than its own median";

        public const string NotCheapest = "not in the cheapest third of its own range";

        public const string UnderTheAverage = "a close under its 200-day average";
    }

    // QualityFamily.cs
    public static class QualityFamily
    {
        public const string NoLongerQualifies = "no longer qualifies";

        public const string LeftTheIndex = "left the index";

        public const string OnAFiling = "a filing dropped its score";

        public const string UnderItsAverage = "under its 200-day average";

        public const string Moved = "its evaluator's code moved since it was registered, so it is kept no further until it is registered again";

        public const string NoSession = "no session: the store holds no bar";

        public const string StoppedOn = "; the step stopped on ";

        public static string NotComputedTonight(string index, string fault) =>
            $"{index}: not computed tonight, {fault}";

        public static string IndexRead(string index, int members, int scored, int qualified, bool reshuffled, int chosen, int sold, int held, int waiting, int rules) =>
            FormattableString.Invariant(
                $"{index}: {members} member(s) read, {scored} scored, {qualified} qualified, {(reshuffled ? "reshuffled" : "no reshuffle")}, {chosen} chosen, {sold} sold, {held} held, {waiting} waiting for their next session{(rules > 1 ? FormattableString.Invariant($", {rules} rules kept") : string.Empty)}");
    }

    // ImprovingRule.cs
    public static class ImprovingRule
    {
        public static string Words(int buyInNine, int holdInNine, int sessions) =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"a newly read filing lifting its score to at least {buyInNine} in 9 of the checks that apply and above its previous filing's, with its sales growth over a year faster than the quarter before; bought at the next open, held while its score stays at {holdInNine} in 9 or better, and sold at the close of the night a new filing drops it below that, at the close of its {sessions}th session or at its last close as a member");

        public const string Description = "Finds companies whose business is strong or getting stronger, read from their filings with the SEC and their results releases.";

        public const string Looks = "A company whose business is strong or getting stronger, read from its filings and results release.";

        public const string LeftTheIndex = "left the index";

        public const string UnderItsAverage = "under its 200-day average";

        public const string NoWord = "too few checks read for a score";

        public const string NotHigher = "no higher than its previous filing's score";

        public const string UnderTheBar = "a score under the bar";

        public const string GrowthNotRead = "its sales growth not read";

        public const string NotFaster = "its sales growth not faster than the quarter before";

        public const string NoFlagBefore = "no flag stood the night before";

        public const string ReadingsCaveat = "Its release readings before February 2025 were read by a model trained after them, so its history may flatter it; its record from today is what counts.";
    }

    // ImprovingGrid.cs
    public static class ImprovingGrid
    {
        public static string Words(int buyInNine, bool rise, bool faster, bool flagFirst, int holdInNine, int sessions, bool soldUnderAverage) =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"a newly read filing lifting its score to at least {buyInNine} in 9{(rise ? " and above its previous filing's" : string.Empty)}{(faster ? ", its sales growth faster than the quarter before's" : string.Empty)}{(flagFirst ? ", a flag standing the night before" : string.Empty)}; held while its score stays at {holdInNine} in 9 or better, at most {sessions} sessions{(soldUnderAverage ? ", and sold at a close under its 200-day average" : string.Empty)}");
    }

    // ImprovingFamily.cs
    public static class ImprovingFamily
    {
        public const string Moved = "its evaluator's code moved since it was registered, so it is kept no further until it is registered again";

        public const string NoSession = "no session: the store holds no bar";

        public const string StoppedOn = "; the step stopped on ";

        public static string NotComputedTonight(string index, string fault) =>
            $"{index}: not computed tonight, {fault}";

        public static string IndexRead(string index, int members, int scored, int firstRead, int improved, int bought, int flagged, int sold, int held, int waiting) =>
            FormattableString.Invariant(
                $"{index}: {members} member(s) read, {scored} scored, {firstRead} newly read filing(s), {improved} raising the score, {bought} bought, {flagged} flagged, {sold} sold, {held} held, {waiting} waiting for their next session");
    }

    // ReleaseAnswers.cs
    public static class ReleaseAnswers
    {
        public const string NotOneObject = "not one object";

        public const string MissingAnAnswer = "missing an answer";

        public const string WordNotListed = "a word not listed";

        public const string RuntimeRefused = "the runtime refused";

        public const string NotAnsweredInTime = "not answered in time";

        public const string NotThePins = "not the pin's model";
    }

    // ReleaseReadings.cs
    public static class ReleaseReadings
    {
        public const string NotPlaced = "the calendar places no session before the print's report date";

        public const string NoRelease = "no release the SEC accepted from the print's window is readable on the night";

        public const string NotRead = "the release is not yet read under the reading version";

        public const string RefusedWhy = "the release's reading was refused";

        public const string NoFiler = "the store matches no filer to the stock";

        public const string NoCauseStated = "no cause stated";
    }

    // ExitMenu.cs
    public static class ExitMenu
    {
        public static string BreakEven(double k) =>
            FormattableString.Invariant($"the stop raised to the buy once a close is {k:0.##} risks up");

        public static string TrailOnceUp(double k, double j) =>
            FormattableString.Invariant($"a trail {k:0.##} typical moves under the highest close once a close is {j:0.##} risks up, with no target");

        public static string TrailFromTheBuy(double k) =>
            FormattableString.Invariant($"a trail {k:0.##} typical moves under the highest close from the buy, with no target");

        public static string Target(double k) =>
            FormattableString.Invariant($"a target at {k:0.##} risks");

        public static string TimeUnlessUp(int n, double x) =>
            FormattableString.Invariant($"sold at the close {n} sessions in unless it is {x:0.##} risks up");

        public static string TimeUnlessAboveTheBuy(int n) =>
            FormattableString.Invariant($"sold at the close {n} sessions in unless it is above the buy");

        public static string TimeStop(int n) =>
            FormattableString.Invariant($"sold at the close {n} sessions in, whatever the trade stands at");

        public static string HalfBreakEven(double k) =>
            FormattableString.Invariant($"half sold once a close is {k:0.##} risks up, the stop raised to the buy on the rest");

        public const string Standing = "the target standing as a sell order from the buy";

        public static string HalfTarget(double k) =>
            FormattableString.Invariant($"half sold at the target and the rest trailed {k:0.##} typical moves under the highest close");

        public const string StandingWords = "Sells at the target the moment the price touches it during the day, or at the open if the stock opens above it. Stops still sell at the next open after a close below them.";
    }

    // RuleHooks.cs
    public static class RuleHooks
    {
        public const string NotABooks = "a book reads conditions on the catalogue's readings and an order by a score alone";

        public static string AlsoRequires(string column, bool above, double level) =>
            FormattableString.Invariant($"also requires {column} {(above ? "at or above" : "at or under")} {level.ToString("0.####", CultureInfo.InvariantCulture)}");

        public static string ScoreFloor(double least) =>
            FormattableString.Invariant($", a score under {least.ToString("0.####", CultureInfo.InvariantCulture)} left off");

        public static string OrderedByTrees(long key) =>
            FormattableString.Invariant($"ordered by the learned trees {key:x12}");

        public const string OrderedByAScoreOver = "ordered by a score over ";

        public static string RankedByScore(int rankingPicks) =>
            FormattableString.Invariant($"ranked over the family's setups at the ledger's loose gates by the index's newest learned score, the best {rankingPicks}");

        public static string RankedByExpected(int rankingPicks) =>
            FormattableString.Invariant($"ranked over the family's setups at the ledger's loose gates by the expected value of the setups like each, the best {rankingPicks}");

        public const string OrderedByExpected = "ordered by the expected value of the setups like each, highest first";

        public static string Exposure(int exposureFull, double breadth, int vixMedianSessions, int exposureHalf) =>
            FormattableString.Invariant($"exposure: {exposureFull} picks where the index's breadth is at or above {breadth.ToString("0.####", CultureInfo.InvariantCulture)} and the VIX at or under its median of the {vixMedianSessions} sessions before, {exposureHalf} where one holds and none where neither, in the market check's place");
    }

    // TreeScore.cs
    public static class TreeScore
    {
        public const string Proposal = "the learned trees";

        public const string SplitMostOn = "the trees split most on ";

        public static string Fitted(double businessShare, int trees, int rows) =>
            FormattableString.Invariant($"; the business readings take {businessShare * 100:0} per cent of the splits; {trees} trees fitted over {rows} finished setups across the three indices");

        public static string Clipped(double labelLow, double labelHigh) =>
            FormattableString.Invariant($", their edge clipped at {labelLow:+0.00;-0.00} and {labelHigh:+0.00;-0.00} moves");
    }

    // LadderSeries.cs
    public static class LadderSeries
    {
        public const string TrendDown = "the trend is down, so no tranche is placed";

        public const string TrendNotClassified = "the trend state could not be classified, so no tranche is placed";

        public const string NoSupportBelow = "no support band sits below the price";

        public const string NoAnchoredSupportBelow = "no support band below the price has an anchor other than a moving average";

        public static string CloserThan(int nearExitInTypicalDays) =>
            FormattableString.Invariant($"closer than {nearExitInTypicalDays} typical days' moves to the blended entry");

        public const string BreakoutBeforeThePrint = "a breakout before the print";

        public static string BreakoutTrigger(decimal highEdge) =>
            FormattableString.Invariant($"a daily close above {highEdge} on volume above its fifty-day average, with the print still ahead");

        public const string FlushAfterThePrint = "a flush after the print";

        public static string FlushTrigger(int nearExitInTypicalDays, decimal lowEdge, decimal highEdge) =>
            FormattableString.Invariant($"the session after the print closes down more than {nearExitInTypicalDays} typical days and inside {lowEdge} to {highEdge}, and the next session holds its low");

        public const string GapUpAfterThePrint = "a gap up after the print";

        public static string GapTrigger(decimal highEdge) =>
            FormattableString.Invariant($"the session after the print opens above {highEdge} and closes above its own open");

        public const string NothingToSize = "no tranche is placed, so there is nothing to size";

        public const string NoRewardToMeasure = "no exit is traded, so there is no reward to measure";

        public const string NoRiskToMeasure = "the first tranche has no stop, so there is no risk to measure";
    }

    // TrendSeries.cs
    public static class TrendSeries
    {
        public const string NoShortAverage = "no 50-day average";

        public const string NoLongAverage = "no 200-day average";

        public static string FewerSwings(int swingsCompared) =>
            $"fewer than {swingsCompared} swing lows and fewer than {swingsCompared} swing highs";

        public static string FewerSwingLows(int swingsCompared) =>
            $"fewer than {swingsCompared} swing lows";

        public static string FewerSwingHighs(int swingsCompared) =>
            $"fewer than {swingsCompared} swing highs";
    }

    // TakeProfit.cs, from the operator's order of 2026-11-14: the reasons a book's holding or a Fundamentals trade ends on its
    // stop or its take-profit, stored as its end, and the words a plan's sale carries its take-profit multiple in.
    public static class TakeProfit
    {
        public const string UnderItsStop = "a close under its stop";

        public const string TookProfit = "took profit";

        public const string TakingOpening = "taking profit at ";

        public const string TakingClosing = " times the risk";

        public static string Taking(double multiple) => FormattableString.Invariant($"{TakingOpening}{multiple:0.##}{TakingClosing}");
    }
}
