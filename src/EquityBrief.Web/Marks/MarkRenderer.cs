using System.Globalization;
using EquityBrief.Core.Spending;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Shortlist;

namespace EquityBrief.Web.Marks;

// One session, as a mark is given it. The renderer's own shape rather than the
// read API's, because the mark renderer is in the project the API references
// and not the other way round.
public sealed record ChartBar(
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

// One moving average over the same sessions as the bars, as a mark is given it.
//
// Values are nullable and in session order, one per bar, so the line breaks
// where the average has no value rather than joining across the absence. A
// 200-day average has no value for the first 199 sessions of a stored year, and
// a line drawn straight from the first value it does have would claim the
// average was flat over sessions it did not exist for.
//
// Double rather than decimal, because an average of prices is a statistic and
// the two worlds do not mix. It arrives already across that boundary.
public sealed record ChartAverage(string Name, IReadOnlyList<double?> Values);

// One band of the volume profile, as a mark is given it.
//
// Edges decimal because they are prices, shares long, and the share of the
// period double because it is a fraction of a count. The mark draws the count
// and states the share, which is the pair the report reads out loud.
public sealed record ProfileBand(decimal Low, decimal High, long Shares, double ShareOfPeriod);

// One level band, as a mark is given it.
//
// The role is a word rather than a flag, because it is drawn as a word as well
// as a hue: hue is never the only channel that carries a meaning, and a reader
// who cannot separate green from orange still reads the band.
public sealed record ChartBand(decimal LowEdge, decimal HighEdge, string Role, bool Immediate, int Strength);

// One momentum reading, as a mark is given it.
//
// Neutral is the value the reading means nothing without. An RSI of 53 is a
// number; an RSI of 53 against a rule at 50 is a statement. Floor and Ceiling
// bound the axis where the reading has a fixed range and are absent where it
// does not: an RSI runs 0 to 100 whatever the stock does, and a MACD is in the
// stock's own money and has no bounds but its own.
public sealed record MomentumReading(
    string Name,
    IReadOnlyList<double?> Values,
    double Neutral,
    double? Floor,
    double? Ceiling);

// One row of the level summary table, as the surface is given it.
//
// Members arrive parsed, because the table's whole content is each band's
// members and their dates and a string would have to be read to draw them.
public sealed record SummaryMember(string Kind, decimal Price, DateOnly Date);

public sealed record SummaryBand(
    decimal LowEdge,
    decimal HighEdge,
    string Role,
    bool Immediate,
    int Strength,
    bool HasNonAverageAnchor,
    IReadOnlyList<SummaryMember> Members,
    double? AwayInTypicalDays = null);

// One row of the plan column: a price, what happens there, and how it reads.
//
// `Kind` is what the reader is being told at that price, and the mark draws each
// kind differently: a purchase below the marker, a sale above it, a stop as a
// horizontal rule and the invalidation as the lowest rule of all. `Detail` is
// what the row says in words, because hue is never the only channel. A tranche
// also carries its condition in words and its stop apart, which is what the
// tranche table draws a column each.
public sealed record PlanRow(
    decimal LowEdge,
    decimal HighEdge,
    string Kind,
    string Detail,
    bool Traded,
    string? BuyOn = null,
    decimal? Stop = null);

// The kinds a plan row takes, named once so the mark and the tables agree.
public static class PlanKind
{
    public const string Tranche = "tranche";
    public const string Exit = "exit";
    public const string Stop = "stop";
    public const string Invalidation = "invalidation";
}

// An average that anchors no band, with the count that explains it.
//
// Section 18's row says a name with fewer than two hundred bars records its long
// average as not available with the bar count, and that what a reader sees is
// "not available, nn bars". An average with no value cannot be a band member, so
// without this the table would simply not mention it, and an absence with no
// statement beside it is the failure that row describes.
public sealed record AbsentAverage(string Name, int BarCount);

// One row of the universe screen, already projected.
//
// `Nearest` is the smaller of the two distances and is what the screen orders
// on. Every nullable field is a name the night computed nothing for, and each is
// drawn as an absence rather than as a zero.
public sealed record UniverseCell(
    string Ticker,
    string Sector,
    decimal? Close,
    string? TrendState,
    decimal? NearestSupport,
    decimal? NearestResistance,
    double? ToSupport,
    double? ToResistance,
    double? Nearest,
    // The listing halves, which arrived at 5.4 with the store that feeds them.
    // `LastListed` is null for a name that has never been on the list, which is
    // an absence rather than a date nobody has.
    DateOnly? LastListed = null,
    IReadOnlyList<bool>? Evenings = null,
    // The sessions-until-earnings half, which arrived at 5.8 with the rest of
    // the parts section 15 states and the pages did not draw. Null for a name
    // with no dated event ahead of it, and for one whose event is past the end
    // of the exchange closure table, which are two absences and are stated as
    // two: a count nobody can take is not the same as no event to count to.
    // owes: The exchange closure table extended before the nights reach its end
    int? SessionsUntilEarnings = null,
    DateOnly? NextEvent = null,
    bool EventBeyondTheTable = false,
    // The company's name as the membership row holds it, drawn under the ticker, and
    // null for a row that carries none.
    string? Name = null,
    // The day the name's newest researched section was written, drawn under its name, and
    // null for a name holding none.
    DateOnly? Researched = null,
    // The swing readings the universe table draws: the mean of the two places among the members'
    // returns, the pullback in typical days, the volume while it came down and the range's
    // tightness, each as the swing reader stored it and null where it stored none.
    double? Strength = null,
    double? Depth = null,
    double? DryUp = null,
    double? Tightness = null,
    // The name's stored closes, which the table draws as its year line beside the ticker while the
    // pointer is over it, and null where none were read for it.
    PeerYear? Year = null);

// One trade the live list recommended, as the Past picks screen and a name's own page draw it: the night
// it was listed, the name, the plan's buy, stop and target, what became of it as of the night drawn, the
// sessions it was held, its result in multiples of the risk it took once it finished, where the price
// stood against the buy as a ratio of it, the session it finished on or the close it stood at, and the
// bar its plan set.
public sealed record PickCell(
    string Ticker,
    string? Company,
    DateOnly Night,
    string Plan,
    decimal? Buy,
    decimal? Stop,
    decimal? Target,
    string Status,
    int? Sessions,
    double? Result,
    double? Along,
    DateOnly? EndedOn,
    double? ReturnPct,
    DateOnly? NowOn,
    decimal? NowClose,
    double? BreakEven,
    // The state the member's reported quarters gave it on its listing night, and null on a night that
    // stored no readings, which the row says it was not read on.
    string? State = null);

// What became of a trade, in the words its status cell and its filter chip draw, and the one value each
// is filtered by. A trade whose outcome row is missing is its own status and in no filter but all.
public static class PickStatus
{
    public const string Open = "open";
    public const string Target = "target";
    public const string Stopped = "stopped";
    public const string Time = "time";
    public const string Missing = "missing";

    public static IReadOnlyList<string> Filters { get; } = [Open, Target, Stopped, Time];

    public static string Words(string status) => status switch
    {
        Open => "Open",
        Target => "Reached target",
        Stopped => "Stopped out",
        Time => "Ran out of time",
        _ => "No outcome stored",
    };

    public static string Chip(string status) => status == Open ? "Still open" : Words(status);
}

// The trades the live list recommended, counted: how many were listed and over how many nights, how many
// stand in each status, the trades decided at their target or their stop that set a bar and the nights
// they were listed on, the minimum both counts wait on, and the share of those that reached the target,
// the share they needed to break even and the average result over every finished trade, each null until
// both minimums are met. A trade that ran out of time is finished and in no share.
public sealed record PicksSummary(
    int Listed,
    int Nights,
    int Open,
    int Target,
    int Stopped,
    int Time,
    int Missing,
    int Decided,
    int DecidedNights,
    int MinimumDecided,
    int MinimumNights,
    double? TargetShare,
    double? BreakEven,
    double? AverageResult)
{
    public int Finished => Target + Stopped + Time;

    public int Of(string status) => status switch
    {
        PickStatus.Open => Open,
        PickStatus.Target => Target,
        PickStatus.Stopped => Stopped,
        PickStatus.Time => Time,
        _ => Missing,
    };
}

// One name's swing readings on a night as the swing reader stored them, and the reason it read
// nothing where it did.
public sealed record SwingReadingsView(
    DateOnly Session,
    int Bars,
    double? ReturnShort,
    double? ReturnLong,
    double? PlaceShort,
    double? PlaceLong,
    decimal? RecentHigh,
    DateOnly? HighSession,
    int? PullbackSessions,
    double? Depth,
    double? DryUp,
    double? Tightness,
    string? Note);

// One operating obligation's count against its trigger, as the Calibration region states it.
public sealed record TriggerLine(string Obligation, int Count, int Trigger, string Says);

// The newest shape proposal as the run page draws it: its number, the night and version it was written
// for, the ordinary nights it read, each gate's lever, the list's median held and proposed, the gates no
// threshold brings inside their bands, its decision, and what accepting it restarts, being the live
// filter's candidate, the acceptances already taken while one stood and the blocks its clock has run.
public sealed record ProposalView(
    long Id,
    DateOnly Session,
    string Version,
    int Ordinary,
    IReadOnlyList<Lever> Levers,
    double? ListNow,
    double? ListProposed,
    IReadOnlyList<string> Findings,
    string? Decision,
    string? Reason,
    string? Opened,
    string? Live,
    int AcceptedWhileLive,
    int Blocks);

// One step of the swing filter's funnel: the gate, how many members passed it and every gate before it,
// and how many it removed.
public sealed record FunnelStep(string Gate, int Passed, int Removed);

// The run page's funnel for a night: the members, each gate in order, the setup's two families, what
// the exclusions removed, and how many pass, with the version the night ran under.
public sealed record FunnelView(
    DateOnly Session,
    string Version,
    int Members,
    IReadOnlyList<FunnelStep> Steps,
    int Pullbacks,
    int Breakouts,
    IReadOnlyList<(string Exclusion, int Count)> Exclusions,
    int Excluded,
    int Passing,
    string Rule = ListRules.Reasons);

// One gate's answer as a name's page draws it.
public sealed record GateLine(string Gate, bool Passed, string Reason);

// A name's swing filter result on a night as its page draws it: the five gates with their reasons,
// the family and the trigger, the trade read three ways with the plan the trade gate read, the
// exclusions and the notes, and its rank where it passed. The plan clear of the noise enters at the same
// close as the plan at the nearest bands.
public sealed record GatesView(
    DateOnly Session,
    string Version,
    IReadOnlyList<GateLine> Gates,
    string? Family,
    bool? TriggerEvent,
    double? LadderRewardToRisk,
    double? LadderStopMoves,
    decimal? SwingEntry,
    decimal? SwingStop,
    decimal? SwingTarget,
    double? SwingRewardToRisk,
    double? SwingStopMoves,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> Notes,
    bool Passed,
    int? Rank,
    string Rule = ListRules.Reasons,
    decimal? ClearStop = null,
    decimal? ClearTarget = null,
    double? ClearRewardToRisk = null,
    double? ClearStopMoves = null,
    string? Input = null);

// The night's market reading as the swing reader stored it: the members, the breadth over the ones
// read with how many it was counted over, the same over the shorter average as context, and the
// median of the members' volume against their fifty-day average.
public sealed record MarketView(
    DateOnly Session,
    int Members,
    int Counted,
    int Above,
    double? Breadth,
    int CountedContext,
    int AboveContext,
    double? BreadthContext,
    int VolumeCounted,
    double? MedianVolumeRatio);

// One session's share of the members holding a close and a 200-day average that closed above it, read
// off the stored averages, which is what the Run page's breadth line draws.
public sealed record BreadthPoint(DateOnly Session, double Share, int Counted);

// The market as the Run page pictures it: the night's stored reading, the floor of the market gate the
// night ran under, and the breadth over the sessions before it that the store holds averages for.
// see: The market on the Run page is named in one word by a stated rule that moves no gate
public sealed record MarketPicture(DateOnly Session, MarketView? Night, double Floor, IReadOnlyList<BreadthPoint> Line);

// The states a night can be in, as the Run page and the notice on tonight's page name them.
// see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
public static class NightStates
{
    public const string Finished = "finished";
    public const string Running = "running";
    public const string Waiting = "waiting to try again";
    public const string Unfinished = "left unfinished";
    public const string NotYet = "not yet run";
    public const string NeverRan = "never ran";
    public const string NoSession = "no session";
}

// One group of the night's steps as the Run page's time bar draws it: the steps in it, how many of
// them the night reached, the seconds they took, and whether the night stopped inside it.
public sealed record StepGroupView(string Name, int Steps, int Reached, double Seconds, bool Stopped);

// One try of a night: its number, counted from one, the run it wrote under, and the step it stopped at
// with the reason it wrote, where it stopped.
public sealed record NightTry(int Number, string RunId, string? StoppedAt, string? Reason);

// How a night went, read off its own run log rows alone: its state, where it stopped and why, when it
// started and how long its arithmetic took against its deadline, the four headline figures, its steps
// in groups, every try it made, and while it waits the instant its next try starts. The notice on
// tonight's page reads the same state.
// see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
public sealed record NightView(
    DateOnly Session,
    string State,
    string? StoppedAt,
    string? Reason,
    DateTimeOffset? Started,
    DateTimeOffset? LastWritten,
    double? Seconds,
    double DeadlineMinutes,
    int? StocksRead,
    int ProviderRequests,
    decimal ResearchSpend,
    int StepsRetried,
    string? AfterTheClose,
    IReadOnlyList<StepGroupView> Groups,
    IReadOnlyList<NightTry>? Tries = null,
    DateTimeOffset? NextTry = null)
{
    public IReadOnlyList<NightTry> TriesMade => Tries ?? [];
}

// One evening of the list as the Run page's freshness bars draw it: the names it listed and how many of them
// were on the list the evening before, each evening read by the rule that listed it.
public sealed record FreshNight(DateOnly Session, int Listed, int Repeated)
{
    public int New => Listed - Repeated;
}

// One night of research as the Run page draws it: the passes the paid model wrote that night and the drafts
// the overnight queue wrote.
public sealed record ResearchNight(DateOnly Session, int PaidPasses, int Drafts);

// Research and spend as the Run page pictures them: the night's spend against the caps and the seven nights
// up to it.
public sealed record ResearchPicture(NightSpend Spend, IReadOnlyList<ResearchNight> Nights);

// How one section of one report came out, as the Run page draws it: passed first time or on retry, left out
// with why, or not warranted where it stood from an earlier day; what its own calls cost, a trial's left out;
// how many drafts the pass wrote of it; and for the two cases whether any of those drafts carried a figure on
// both sides.
// see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports
public sealed record ReportCell(string Section, string Outcome, string? Why, decimal Cost, int Drafts, bool BothSides);

// One report: the pass's run, its stock and the day it wrote for, what its calls cost, a trial's left out, and a
// cell for each section in figure 12.2's order.
public sealed record ReportRow(string RunId, string Ticker, DateOnly Day, decimal Cost, IReadOnlyList<ReportCell> Cells);

// A section's rates over the newest reports that warranted it: how many were read, and how many of them passed
// first time and were left out.
public sealed record SectionRate(string Section, int Reports, int FirstTime, int LeftOut);

// One side of a section a trial or a review asked for: the model, how it came out, its rounds and what they cost,
// each round's draft, and which side it is.
public sealed record TrialSide(string Model, string Outcome, int Rounds, decimal Cost, IReadOnlyList<string> Drafts, string Side = TrialSide.OfTrial)
{
    public const string OfPass = "pass";
    public const string OfTrial = "trial";
    public const string OfReview = "review";
}

// One section a trial or a review asked for beside a report: the pass's own side, and the trial's and the review's
// where each asked for it, in that order.
// see: A review asks a section's model to check its own draft against the section's rules, behind a setting that ships off
public sealed record TrialRow(string RunId, string Ticker, DateOnly Day, string Section, TrialSide Pass, IReadOnlyList<TrialSide> Asked)
{
    public TrialSide? Trial => Asked.FirstOrDefault(side => side.Side == TrialSide.OfTrial);

    public TrialSide? Review => Asked.FirstOrDefault(side => side.Side == TrialSide.OfReview);
}

// The reports of the seven nights up to a night, each section's rates over the newest reports that warranted it,
// how many of the newest reports' two cases drafts carried a figure on both sides out of how many were drafted,
// and every section a trial asked for up to the night.
public sealed record ReportsView(
    IReadOnlyList<ReportRow> Reports,
    int Held,
    IReadOnlyList<SectionRate> Rates,
    int BothSides,
    int TwoCases,
    IReadOnlyList<TrialRow> Trials)
{
    public const string FirstTime = "passed first time";
    public const string OnRetry = "passed on retry";
    public const string LeftOut = "left out";
    public const string NotWarranted = "not warranted";

    // How many of the newest reports a section's rates and the two cases' count are read over.
    // see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports
    public const int RateWindow = 20;
}

// A version running beside the live list, as the Run page's learning region draws it: what it changes in
// plain words, the stocks it has picked so far, the share of them the live list also picked, and the
// evidence it has gathered against the floor its first look is read at. Picks only: nothing here is read
// from how its trades turned out.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
public sealed record VersionLine(string Candidate, string Slug, string Changes, int Picks, double? Shared, int Blocks, int Floor, bool Live);

// A stock in a comparison of the night's picks, with the setting that sent it one way where the stored gate
// results say which.
public sealed record ComparedName(string Ticker, string? Why);

// Tonight's picks compared with one background version's: the names only the live list picked, the names
// both picked, and the names only the version picked, each only-name with its reason, and over the last
// twenty evenings the version's picks, the share of them the live list also picked and the evenings it
// picked a name the live list did not.
public sealed record CompareView(
    DateOnly Night,
    IReadOnlyList<VersionLine> Versions,
    VersionLine? Chosen,
    bool Evaluated,
    IReadOnlyList<ComparedName> OnlyLive,
    IReadOnlyList<string> Both,
    IReadOnlyList<ComparedName> OnlyVersion,
    int Evenings,
    int Picked,
    double? SharedShare,
    int EveningsAhead);

// One version at a checkpoint: before its first look a locked row with its trades and blocks so far, and
// from then the share of its trades that reached the target, the break-even they needed, what a version
// with no skill scored from the same starts, how far luck alone could move it, and the verdict in words.
public sealed record CheckpointRow(
    string Candidate,
    bool Live,
    int Setups,
    int Blocks,
    int Floor,
    double? Share,
    double? BreakEven,
    double? NullShare,
    double? SmallestExcess,
    string Verdict)
{
    public bool Unlocked => Share is not null && NullShare is not null;
}

// One item of the Run page's checklist: what it asks, whether it held, and where it failed or could not be
// read, why.
public sealed record WorryItem(string Item, string State, string? Why)
{
    public const string Held = "held";
    public const string Failed = "failed";
    public const string NotRead = "not read";
}

// One of a name's biggest moves, as the table is given it. `Cause` is the text of
// the accepted cause section that names this move, and null where no sentence of
// it does: a researched claim, which arrives with the pass that writes it.
public sealed record MoveCell(DateOnly SessionDate, int Sessions, double ChangePct, int Rank, string? Cause = null, MoveGroup? Group = null);

// The group a move is read against: its industry or its sector, named, how many other members
// it holds and how many of them held both closes, and their median move over the same
// sessions, none where no member did. `NotAMemberOn` is the night the group was read on where the
// index did not hold the name on it, which the cell names in place of a group.
// see: A name's group is its industry where at least five other members share it on the session, and its sector otherwise, and every surface that uses it says which and how many
public sealed record MoveGroup(string Kind, string? Name, int Members, int Counted, double? Median, DateOnly? NotAMemberOn = null);

// A name's peers table as the page is given it: the group the name's moves are read against,
// none where the store holds no readings for the name, and a row for the name itself and for each
// member of it the night chose, in the order it chose them. `Kept` says the readings are the newest
// night's alone, which a page drawn for an earlier night states rather than drawing them. `Others`
// is how many other members the group holds, of which the rows are the ones the night chose, and
// `Chosen` is false on a row written before the night chose any.
// see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
public sealed record PeersView(string? GroupKind, string? GroupName, IReadOnlyList<PeerCell> Rows, bool Kept = true, bool Member = true, int Others = 0, bool Chosen = true);

// One row of a peers table: a member of the name's group, or the name itself, marked, with its
// close and trend state as the universe table holds them, the two readings the annotator stored
// for it, the cell the distance row mark is drawn from, how closely its daily moves followed the
// name's with whether it shares the name's industry, the company's name, and its stored closes.
public sealed record PeerCell(
    string Ticker,
    bool Own,
    decimal? Close,
    string? TrendState,
    PeerFigures? Readings,
    UniverseCell? Distance,
    PeerLikeness? Likeness = null,
    string? Company = null,
    PeerYear? Year = null);

// How closely a member's daily moves followed the name's, as the annotator stored it: none where the
// two share fewer daily returns than the floor, with how many they share.
public sealed record PeerLikeness(bool SameIndustry, double? Value, int Sessions);

// A member's stored closes in session order, from the first session to the last.
public sealed record PeerYear(DateOnly From, DateOnly To, IReadOnlyList<decimal> Closes);

// The two readings as the annotator stored them for a name, with the bars they were read over.
public sealed record PeerFigures(DateOnly Session, decimal YearHigh, double BelowHighPct, double? ReturnPct, int Bars);

// One print of a name's earnings reaction record as the page is given it: the report date, when in
// the session it was reported, the session it moved on, the estimate and the actual as filed and
// null where the provider filed none, the provider's surprise, none beside no estimate, and the
// session's move.
public sealed record ReactionCell(DateOnly ReportDate, string Timing, DateOnly Session, string? Estimate, string? Actual, double? SurprisePct, double MovePct);

// Where the causes in a moves table came from: the date the accepted cause section
// was written on and the model that wrote it.
public sealed record CauseSource(DateOnly AsOf, string Model);

// What research spent on a night's UTC day and in its month to the end of that day,
// beside the two caps it is held to.
public sealed record NightSpend(decimal OnTheDay, decimal MonthToDate, decimal DayCap, decimal MonthCap);

// A night's research prose, as tonight's header states it: of the names whose report
// carries a written section as of the night, how many carry one written on the night
// and how many carry only sections written before it.
public sealed record NightProse(int Fresh, int Reused, int Names);

// A pause as a name page draws it: which cap stopped research, when it resumes, and
// the line the spend cap itself states.
public sealed record ResearchPausedLine(string Cap, DateTimeOffset ResumesAt, string Line);

// The paid calls the run log carries a recorded cost for, the passes they were made
// in, and what they came to.
public sealed record PricedCalls(int Count, int Passes, decimal Total, int AtPeak = 0);

// One row of tonight's list, already projected.
//
// `Fired` carries the same reasons as `Reasons` with the values that made each
// true, which is 15.7's reasons-per-row half and arrived at 5.6 with the record
// that sits beside them. It is optional because a caller that only needs the
// names of what fired, the watch list among them, should not have to carry the
// values to say so.
public sealed record ListingCell(
    string Ticker,
    DateOnly SessionDate,
    int FiredCount,
    int Strength,
    decimal? Close,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<FiredReason>? Fired = null,
    // The three section 15.7 states beside the name, the close and the reasons,
    // drawn from 5.8. Each is absent rather than zero for a name the night
    // computed nothing for. `Distance` carries the mark's own input rather than
    // a copy of its three numbers, because the mark takes a universe cell and a
    // second shape holding the same values is a second place they can disagree.
    double? DayChangePct = null,
    string? TrendState = null,
    UniverseCell? Distance = null,
    // Where the name's stored series is suspect, which the row says beside the name
    // from the 7.0 ruling, and null for a name whose series is trusted.
    SuspectPrices? Suspect = null,
    // The day the name's newest researched section was written, and null for a name
    // holding none. The key under each figure is not one of them, so most of the index
    // is null here even though the overnight queue writes that key for every name.
    // see: A researched name is one holding an accepted section besides the key under each figure
    DateOnly? ResearchedOn = null,
    // The reward to risk the night's plan computes from its first tranche, which breaks a tie in
    // the fired count, and where it computes none the plan's own words for why. Exactly one of
    // the two is set on a row the list draws.
    // see: Tonight's list breaks a tie in fired count by the plan's reward to risk, and a row with none is drawn after every row with one and says why
    decimal? RewardToRisk = null,
    string? NoRewardToRisk = null,
    // What the queue holds for the name, where it holds a request nobody has settled: queued
    // with the instant it will start, or being written since the instant its pass started.
    // Null for a name the queue holds nothing waiting for.
    // see: The queue page states when each request will be written
    QueueState? Queue = null,
    // Where the swing filter drew the row: its rank and the gates that decided it.
    FilterRow? Filter = null,
    // The state the member's reported quarters gave it on the night and what the numbers say, and null
    // on a night that stored no readings.
    // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
    NumbersRow? Business = null,
    // Where the row is close to a buy point: the one gate it missed, what it had against the bar it needed
    // in plain words, how far that is as a share of the bar, and the trade's entry, stop and target where
    // one exists. Null on every row of tonight's list itself.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    EquityBrief.Core.Filter.MissedGate? Missed = null);

// The state a member's reported quarters gave it on a night and the sentences its readings say, which
// tonight's row draws beside the trend word, the sentences showing while the word is under the pointer
// or has focus.
public sealed record NumbersRow(string State, IReadOnlyList<string> Sentences);

// "What the numbers say" as a name's page opens its numbers with it: the heading carrying the state, the
// quarter the readings were read from, one sentence per reading, and the quarters behind them with the
// dates each was filed and reported on.
public sealed record NumbersSayView(string State, string Heading, DateOnly? ReadFrom, IReadOnlyList<string> Sentences, IReadOnlyList<QuarterFiled> Quarters);

public sealed record QuarterFiled(DateOnly PeriodEnd, DateOnly? FilingDate, DateOnly? ReportDate);

// A row the swing filter drew: its rank, the setup's family, the session its trigger arrived on, the plan
// the trade gate read with its reward to risk and the stop's distance in typical moves, and each gate with
// whether it passed and why.
// see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
public sealed record FilterRow(int Rank, string? Family, string Arrived, string Input, string RewardToRisk, string StopMoves, IReadOnlyList<FilterGate> Gates);

public sealed record FilterGate(string Name, bool Passed, string Reason);

// A name the operator watches as its page draws it: the night's row for it, its company, the day it was
// added, what the swing filter said of it that night, and whether that was a place on the list.
public sealed record WatchCell(ListingCell Row, string? Company, DateOnly Added, string Filter, bool Listed);

// Why a name the swing filter listed is on the list: the evening, each gate with why it passed, and the
// reasons that fired on it, as context.
public sealed record FilterWhy(DateOnly Evening, IReadOnlyList<FilterGate> Gates, IReadOnlyList<string> Reasons);

// The list from night to night: of the night's names, how many were on the list the evening before, and how
// many at least once over the five and the twenty evenings before, each evening read by the rule that
// listed it.
// The Calibration region's edge half: each swing family candidate's record read over its own setups, with
// the sessions its first look and its earliest promotion wait on.
public sealed record EdgeView(DateOnly Night, IReadOnlyList<EdgeCandidate> Candidates, int FirstLookSessions, int PromotionSessions);

// The near misses over the rows the open filter version stored, from the first night it stored.
public sealed record NearMissView(DateOnly Night, string? Version, DateOnly? From, IReadOnlyList<NearMissGroup> Groups);

public sealed record OverlapView(DateOnly Night, int Names, DateOnly? LastNight, int OnLastNight, int FiveHeld, int OnFive, int TwentyHeld, int OnTwenty);

// The rule the night's list was drawn by, and on a night the swing filter drew it, whether the market gate
// was open with the breadth and its floor, and how many members reached each gate after it.
public sealed record ListRuleView(string Rule, bool MarketOpen, double? Breadth, double? Floor, IReadOnlyList<int> Reached);

// A request the queue holds for a name and has not settled, as a row of tonight's list and the
// selected name's region state it: `queued` or `writing`, the instant it starts or started as
// the store spells an instant where one is known, and the words the page states it in.
public sealed record QueueState(string State, string? At, string Words)
{
    public const string Queued = "queued";
    public const string Writing = "writing";
}

// A name whose stored series may not reflect a dividend or split, as a page states it:
// when its refetch was last asked for and the reason it failed, both as the store holds
// them.
public sealed record SuspectPrices(string LastAskedAt, string Reason);

// A member the backfill asked for a year for and got none: the nights it was asked for, the
// session it was last asked for on, and the session it is next asked for on, null where
// that is the next night.
public sealed record NoYear(int Nights, DateOnly? Last, DateOnly? Next);

// One reason that fired for a name, with the values that made it true.
public sealed record FiredReason(string Name, IReadOnlyDictionary<string, string> Values);

// One reason and how many of tonight's names it fired on.
public sealed record ReasonTotal(string Reason, int Names);

// One reason's record as a page draws it. `Resolved` counts every win and loss, and `Scored` the ones
// that set a bar, which is the set the share, the bar, both floors and the verdict are taken over.
// see: An unresolved setup is never a win
// see: A condition is judged against the break-even its own plan demands
// see: A reason's share, verdict and both floors are counted over the resolved setups that set a bar
//
// `Nights` is the listing sessions whose rows count toward this record, null
// where a caller built the record by hand.
public sealed record ReasonRecord(
    string Reason,
    int Fired,
    int Won,
    int Lost,
    int Unresolved,
    int Minimum,
    int NeverEntered = 0,
    int Scored = 0,
    double? Share = null,
    double? BreakEven = null,
    int Sessions = 0,
    int SessionMinimum = 0,
    bool? Cleared = null,
    double? PValue = null,
    double Threshold = 0,
    int Divisor = 0,
    int? Nights = null,
    string Withheld = ReasonVerdict.BelowTheResolvedMinimum,
    double Significance = 0)
{
    // A setup that has done nothing is neither right nor wrong, so it is in
    // neither half of this, and one whose price never reached the entry the plan
    // named is not a trade at all, so it is in neither either.
    // see: A setup is scored from its entry, and a target reached before the entry is never a win
    public int Resolved => Won + Lost;

    // The verdict's own answer, naming no floor of its own, so a page draws a
    // verdict on exactly the populations the test was run over and on no others.
    // see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
    public bool HasEarnedAVerdict => Withheld == ReasonVerdict.Shown;
}

// One row of the reason track: section 15.5's three states out of one
// denominator.
//
// `ResolvedUnsplit` is the resolved setups of a reason that has not earned a
// verdict, drawn as one segment rather than as a win segment beside a loss one.
// 15.11 gates the record column on the minimum, and a split drawn below it is
// the same figure through a second channel.
public sealed record ReasonTrackRow(
    string Reason,
    int Won,
    int Lost,
    int Unresolved,
    int ResolvedUnsplit = 0)
{
    public int Total => Won + Lost + Unresolved + ResolvedUnsplit;
}

// One window's universe base rate, as the run page pins it.
//
// `Rate` is null before the first fill has anything matured to count, which is a
// window nothing has measured rather than a rate of zero.
public sealed record BaseRateLine(string Window, double? Rate);

// The shadow candidates region: how many candidate conditions stand registered,
// the maximum family that number is held to and the family's divisor, and beside
// the divisor the distinct trials the level is shared across, the level each starts
// at and what a candidate's looks release of it, first to last.
//
// Numbers and no names. The region says how hard the correction is and that
// every candidate's own record is withheld until it is promoted, and it carries
// nothing a reader could read a candidate's performance off, because the
// register is only a pre-registration for as long as nobody can see how a
// candidate is doing before deciding whether to keep it.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
// see: Holm's level passes between the candidates by a graph fixed when they are registered, and its first step is 0.05 over the distinct trials read at a look or still running
public sealed record ShadowRegion(
    int Registered,
    int Divisor,
    int Maximum,
    int Trials,
    double? Level,
    IReadOnlyList<double> Releases,
    bool FirstLookPromotes);

// One order of tonight's list as the run page measures it: its name and whether it is the benchmark,
// the setups among the rows it would have drawn, how many of those have had their whole outcome
// window, and how many blocks hold at least one of those.
public sealed record OrderMeasured(string Key, string Name, bool Benchmark, int Setups, int Closed, int Blocks);

// The three orders over the nights that recorded what each reads, from the first of them, with the
// row count each is measured over, the block length and the floor below which nothing is compared.
public sealed record OrderComparison(IReadOnlyList<OrderMeasured> Orders, DateOnly? From, int Nights, int Drawn, int BlockSessions, int Floor);

// One registered candidate as the record region draws it: what it was registered with, whether it
// still stands, the level the graph gives it and the step that level stands at, and its record.
public sealed record CandidateRecordRow(
    string Candidate,
    string Proposed,
    bool Standing,
    bool Crossed,
    double Level,
    int Step,
    Measured Record);

// The candidates' records, with the count of distinct trials beside them, the looks a verdict is
// read at, the block length and floor, and the round trip the bars carry.
public sealed record CandidateRegion(
    IReadOnlyList<CandidateRecordRow> Candidates,
    int Registered,
    int Standing,
    int Trials,
    double Significance,
    int BlockSessions,
    int Floor,
    IReadOnlyList<int> LooksAt,
    double Cost,
    double Sensitivity);

// One open version of the trend rule as the versions region draws it: the labels it gave the
// night's names, the difference between them and the live rule's, and its record where it has one.
//
// The labels are counts and never names, for the reason a candidate's record carries none: a
// version is a rule being measured, and a screen that named the stocks it moved would be showing
// a list nobody chose to show.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
public sealed record TrendVersionRow(
    string Version,
    string Parameters,
    DateTimeOffset OpenedAt,
    IReadOnlyList<LabelCount> Labels,
    int Moved,
    VersionMeasured? Record);

// One trend label and how many of the night's names carried it under a version.
public sealed record LabelCount(string Label, int Names);

// How often a label flipped from one night to the next and how often the old one came back,
// which is the reading the confirmation version's nights are settled from.
public sealed record LabelReturns(int Pairs, int Flipped, int ReturnedTheNextNight, int ReturnedWithinTwo);

// The trend rule's versions, the labels the live rule gave the same night, and the flip-backs the
// stored labels show. The A-over-B margin and the Reality Check stand with them, because the
// better of several versions is not read against the level one version is read at.
// owes: The trend confirmation's nights settled from flip-backs
// see: A trend version is judged by the candidates' test on its difference from the live rule
public sealed record TrendVersionRegion(
    IReadOnlyList<TrendVersionRow> Versions,
    IReadOnlyList<LabelCount> Live,
    LabelReturns Returns,
    DateOnly Night,
    int Nights,
    int MostAtOnce,
    int Open,
    double Margin,
    RealityCheck.Checked? Best);

// The four verdict counts of the last phase report.
//
// Four fields and no total. Out of scope is counted apart from unexamined and
// only one of them is a defect, so a record that summed them would make the run
// page report a build that has not reached a claim as one that failed to check
// it.
public sealed record HarnessCounts(int Passed, int Failed, int Unexamined, int OutOfScope);

// One stage of a night, as the operational header draws it.
// `StartedAt` is the instant, not the duration, and the two are different
// questions. The duration answers how long the night took; the instant answers
// what time of day a stage ran, which is the only thing that can bound the hour
// the provider posts the day's bulk file. The row carried the duration alone
// until the phase 5 sign-off, so the surface two operating obligations name
// could answer one of them and not the other.
// owes: The provider's posting hour for the day's bulk file, measured from live fetches
public sealed record StageRow(
    string Stage,
    DateTimeOffset StartedAt,
    double Seconds,
    int RowsWritten,
    int ModelCalls,
    int NetworkRequests,
    string Spend,
    string Outcome,
    string Detail,
    bool ByHand = false);

// The overnight queue as the run page draws it for one night.
//
// `Outcome` is null where the queue wrote no row for the night, and the counts are then
// zero. `NotRun` is every traded session with no queue row, from the one after the newest
// earlier night the queue ran on up to and including this night, and `NeverRan` says the
// store holds no queue row on or before this night at all, which is stated rather than
// read as a night it failed.
// see: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar
public sealed record QueueNight(
    DateOnly Night,
    string? Outcome,
    int Queued,
    int Completed,
    int Left,
    double LimitHours,
    string? Reason,
    string? Awake,
    IReadOnlyList<DateOnly> NotRun,
    bool NeverRan);

// One refused document, as the run page draws it: the category that refused it,
// what it was called, and where it is.
//
// No body and no date. The store holds no body for a refusal, which is the point
// of the row rather than a gap in it, and one class of refusal is that the
// document carried no publish date, so a date drawn beside each row would be
// blank for exactly the rows whose reason is the blank.
public sealed record RefusedDocument(string Category, string Title, string Url);

// One section left out, as a page draws it: which section, whose, and the reason
// the checker stored. `Subject` is a ticker on the run page and empty on a name's
// own page, where the name is the page.
public sealed record LeftOutSection(string Subject, string Section, string Reason);

// Where a name's research stands, as the page draws it: missing, stands or stale,
// and the one line that says so in the judge's own words.
public sealed record ResearchStateLine(string State, string Line);

// One written section as a page draws it: the section, the prose as stored, the date it
// was written on, the model that wrote it, and the ids its markers resolve to, in the
// order its source list holds them, so [D1] is the first.
public sealed record WrittenCell(string Section, string Prose, DateOnly AsOf, string Model, IReadOnlyList<string> SourceIds);

// One document a written section cites, as a page draws it.
public sealed record SourceCell(string Id, string Title, string Url, DateOnly? PublishedOn);

// One dated event the calendar holds for a name, as the dates-and-sources region draws it.
public sealed record DateCell(DateOnly Date, string Kind, string Timing);

// What the newest pass for a name came to, as the research region states it: its
// outcome, the session it ran on, and the one line that says what happened.
public sealed record ResearchPassLine(string Outcome, DateOnly AsOf, string Line);

// A control that starts a research pass for the name, and what it asks for.
public sealed record ResearchControl(string Kind, string Label, bool Refresh, bool PaidForLocal);

// Where a pass the page started stands: starting before its first row lands, running while it
// works with the step it is on in the reader's words, and ended when its own row lands, with
// the count of sections the name holds so the page knows when one more has arrived.
public sealed record PassProgress(string State, string Step, int Sections);

// What research has cost, stated beside a control before it is pressed: the passes the
// run log has priced, what they came to, the most one came to, and the line saying where
// research stands against the caps now.
public sealed record ResearchCost(int Passes, decimal Total, decimal Most, string Verdict);

// One line of the sector strip.
public sealed record SectorLine(string Sector, int Names, int InUptrend, int OnTheList);

// The price scale a chart drew, so another mark can draw against it.
//
// Section 15.5 says the volume profile is drawn against the same price axis as
// the chart beside it. That is a claim about two pictures agreeing, and the only
// way to make it hold by construction rather than by coincidence is to compute
// the scale once and hand it to both. A profile that took its own low and high
// from its own bands would be a picture whose rows line up with nothing, and it
// would look entirely reasonable on its own.
public sealed record PriceAxis(double Low, double High)
{
    // The span the scale is drawn over. A flat series has none and is drawn
    // through the middle rather than refused, which is the rule the chart
    // already applies to its candles.
    public double Range => High - Low > 0 ? High - Low : 1;
}

// How a chart is drawn on a page. `Scale` gives the picture a width and a height of its
// own, so a chart and the profile beside it drawn at one scale line up price for price,
// and no scale draws it the width of whatever holds it. `Markers` are the sessions a
// table beside the chart numbers.
// One session a table beside the chart numbers: the session, what its own row says, and
// where that row is. A circle drawn with a number and nothing else is a number a reader has
// to go looking for the meaning of, so it carries both.
public sealed record ChartMarker(DateOnly Session, string Says, string Href);

public sealed record ChartFrame(double? Scale = null, IReadOnlyList<ChartMarker>? Markers = null);

// One entry of a page's contents: where it sits as a reader counts down the page, what the
// card calls itself, and the card's own id, which is what the entry links to.
public sealed record ContentsEntry(int At, string Title, string Id);

// The marks, as SVG strings written server side.
//
// This is the level chart mark with one of its four elements absent. Section
// 15.5 names four: candles, the level bands, the moving averages and a volume
// pane. Candles and the volume pane are drawn here at 1.3 and the moving
// averages at 3.1. The bands arrive at 3.4 with the level builder, drawn into
// this file rather than into a second one.
//
// That is the whole reason this is not a temporary chart. A temporary chart
// becomes the second renderer, and one renderer for both the app and the export
// is what makes the two carry the same pictures from the same numbers.
// see: Marks are defined once and every screen draws from that list
//
// It touches no store and computes no figure, which is what its blank
// matrix cells claim. Geometry is not a figure: nothing here is reported to a
// reader as a number, and every price drawn arrives already computed.
public sealed class MarkRenderer : IComponent
{
    // The empty declaration, which is a claim and not an omission. Section
    // 15.4 puts the marks on the server, and the seam between rendering and
    // reading only means something if a check asserts the renderer reaches no
    // store of its own.
    public static ComponentAccess Access => ComponentAccess.Nothing;

    // Below this a chart says what it has rather than drawing through nothing.
    // Two, because one session has no range to scale against and a chart of one
    // candle is a picture of nothing. Section 15.5's note is the rule: a mark
    // degrades by stating its bar count, never by drawing a sparse series as a
    // quiet one.
    public const int FewestBars = 2;

    // The plot's own width, which sets the column: a picture is drawn at the size it is
    // read at, so the pair of the chart and the profile beside it fills the card at the
    // column's widest and no picture is ever drawn larger than it was made.
    const int Width = 1376;
    const int ProfileWidth = 150;
    const int PriceHeight = 340;
    const int VolumeHeight = 90;
    const int Gap = 18;
    const int Margin = 8;

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // The same culture as a function, for the places a fragment is built
    // rather than appended. StringBuilder takes the provider directly; a
    // string does not.
    static string Formatted(FormattableString text) => text.ToString(Invariant);

    // The one place a price becomes a plot coordinate.
    //
    // Prices are decimal and statistics are double, and nothing crosses between
    // them implicitly. A coordinate is neither: it is a position on a surface,
    // computed in double because that is what geometry is, and it never travels
    // back. This helper is named for the crossing so the boundary is visible at
    // every call rather than hidden in an expression, which is what the money
    // rule in CLAUDE.md asks of anything that crosses it.
    static double PlotValue(decimal price) => (double)price;

    static string Number(double value) => value.ToString("0.##", Invariant);

    // A price as a picture prints it, to two places with a thousands separator. The stored value
    // stays whole on the mark's own data attributes, which is where anything reading the mark
    // takes it from.
    static string Price(decimal value) => Figures.Price(value);

    // Five places, and below them the bound a tail lies under, since no tail over setups that can
    // lose is zero.
    const double SmallestDrawnProbability = 0.00001;

    static string Probability(double value) =>
        value < SmallestDrawnProbability
            ? Formatted($"below {SmallestDrawnProbability.ToString("0.#####", Invariant)}")
            : Formatted($"of {value.ToString("0.#####", Invariant)}");

    static string VerdictWord(ReasonRecord record) => record.Cleared is true ? "cleared" : "not cleared";

    // 15.11's three figures together, over the one set each of them was computed over.
    static string ShareOfTheScored(ReasonRecord record) =>
        Formatted($"{Number(record.Share ?? 0)} per cent of {record.Scored} resolved setups that set a bar reached target before stop, against the {Number(record.BreakEven ?? 0)} per cent those setups demanded");

    // The count a withheld verdict waits on, against the floor the verdict named as short.
    static string CountAgainstTheFloors(ReasonRecord record) =>
        record.Withheld == ReasonVerdict.BelowTheSessionMinimum
            ? Formatted($"{record.Scored} of {record.Minimum} resolved setups that set a bar, over {record.Sessions} of {record.SessionMinimum} listing session(s)")
            : Formatted($"{record.Scored} of {record.Minimum} resolved setups that set a bar");

    // The price scale the chart draws, computed here so the profile beside it
    // can be given the same one.
    //
    // It takes in the averages as well as the candles, for the reason stated
    // below: a 200-day average sits well under the price after a year of rising,
    // and a scale drawn from the candles alone pushes it off the bottom of the
    // pane where it reads as absent rather than as low. Neither the profile nor
    // the level bands widen it. A profile band lies inside the window's own high
    // and low by construction, and every level candidate but the averages does
    // too: a swing, a touch, a retracement and a shelf are all prices from
    // inside the window, and the averages are already taken in here. A scale
    // that stretched to fit a mark would make the two pictures disagree about
    // where a price is, which is the whole thing this method exists to prevent.
    public PriceAxis AxisFor(IReadOnlyList<ChartBar> bars, IReadOnlyList<ChartAverage>? averages = null)
    {
        var high = bars.Max(bar => PlotValue(bar.High));
        var low = bars.Min(bar => PlotValue(bar.Low));

        var drawn = (averages ?? []).SelectMany(line => line.Values).Where(value => value is not null).ToArray();

        if (drawn.Length > 0)
        {
            high = Math.Max(high, drawn.Max()!.Value);
            low = Math.Min(low, drawn.Min()!.Value);
        }

        return new PriceAxis(low, high);
    }

    // Where a price sits in the price pane, given the axis. One definition, used
    // by the candles, the averages and the profile, because two mappings on one
    // scale is a picture that lies about where the price is.
    static double At(PriceAxis axis, double value) =>
        PriceHeight - Margin - ((value - axis.Low) / axis.Range * (PriceHeight - (2 * Margin)));

    // The plan column: one vertical price axis with the current price marked in
    // the middle of it.
    //
    // Everything above the marker is a sale and everything below is a purchase,
    // which is legible without reading a caption, and it is one column rather
    // than two facing sides because a reader should not have to learn a
    // convention before reading it.
    // see: The plan figure is one vertical price column with the current price marked in it
    //
    // It draws nothing the ladder does not carry. Every row handed in is a
    // stored value, and the only arithmetic here is the axis, which is where a
    // price sits on a scale rather than what the price is.
    // see: A screen reads and renders, and computes nothing
    public string PlanColumn(string ticker, decimal close, IReadOnlyList<PlanRow> rows)
    {
        if (rows.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"0\">" +
                $"{Escaped(ticker)} has no plan to draw, which is what a name with no eligible " +
                $"band gets.</p>";
        }

        // The price now sits in the middle of the column and the scale reaches the row
        // furthest from it on either side, so above the marker and below it are halves
        // of one picture rather than whatever the prices happened to span.
        var now = PlotValue(close);
        var reach = rows
            .SelectMany(row => new[] { PlotValue(row.LowEdge), PlotValue(row.HighEdge) })
            .Select(price => Math.Abs(price - now))
            .Max();
        var span = reach > 0 ? reach * 1.08 : Math.Max(Math.Abs(now) * 0.05, 1);
        var half = (PlanHeight - (2 * PlanEdge)) / 2.0;
        var middle = PlanEdge + half;

        double Y(decimal price) => middle - ((PlotValue(price) - now) / span * half);

        var axis = new PriceAxis(now - span, now + span);
        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg class=\"plan-column\" viewBox=\"0 0 {PlanWidth} {PlanHeight}\" width=\"{PlanWidth}\" height=\"{PlanHeight}\" ");
        svg.Append(Invariant, $"role=\"img\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{rows.Count}\" ");
        svg.Append(Invariant, $"data-close=\"{close}\" data-axis-low=\"{axis.Low}\" data-axis-high=\"{axis.High}\">");

        svg.Append(Invariant, $"<title>{Escaped(ticker)} plan column</title>");
        svg.Append(Invariant, $"<desc>One vertical price axis. Everything above the price marker is a sale, everything below is a purchase, stops are horizontal rules and the invalidation is the lowest.</desc>");

        svg.Append("<text class=\"m-head\" x=\"0\" y=\"14\">ABOVE THE PRICE: SALES</text>");
        svg.Append(Invariant, $"<text class=\"m-head\" x=\"0\" y=\"{PlanHeight - 6}\">BELOW THE PRICE: PURCHASES</text>");
        svg.Append(Invariant, $"<line class=\"m-axisline\" x1=\"{PlanAxis}\" y1=\"{PlanEdge}\" x2=\"{PlanAxis}\" y2=\"{PlanHeight - PlanEdge}\"/>");

        // Where each row's words go. Zones are named to the right of the column and the
        // rules to the left of it, each side pushed apart until no two labels share a
        // line, with a leader from a label that moved to the price it names.
        var right = new List<(int Row, double Wanted, string[] Lines)>();
        var left = new List<(int Row, double Wanted, string[] Lines)>();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            switch (row.Kind)
            {
                case PlanKind.Tranche:
                    right.Add((index, Y(row.HighEdge) + 10, [Formatted($"Buy {Zone(row)}"), .. Clauses(row.Detail)]));
                    break;
                case PlanKind.Exit:
                    right.Add((index, Y(row.LowEdge) - 2, [Formatted($"Sell at {Zone(row)}"), .. Clauses(row.Detail)]));
                    break;
                case PlanKind.Invalidation:
                    left.Add((index, Y(row.LowEdge) - 5, [Formatted($"Invalidation {Price(row.LowEdge)}")]));
                    break;
                default:
                    left.Add((index, Y(row.LowEdge) - 5, [Formatted($"Stop {Price(row.LowEdge)}")]));
                    break;
            }
        }

        var rightAt = Spread(right, fixedLines: []);
        var leftAt = Spread(left, fixedLines: [(middle - 14, middle + 14)]);

        foreach (var (row, index) in rows.Select((row, index) => (row, index)))
        {
            var top = Y(row.HighEdge);
            var bottom = Y(row.LowEdge);
            var height = Math.Max(bottom - top, 3);

            svg.Append(Invariant, $"<g class=\"plan-row\" data-kind=\"{Escaped(row.Kind)}\" ");
            svg.Append(Invariant, $"data-low-edge=\"{row.LowEdge}\" data-high-edge=\"{row.HighEdge}\" ");
            svg.Append(Invariant, $"data-traded=\"{(row.Traded ? "true" : "false")}\">");

            // A stop and the invalidation are rules rather than zones, because
            // each is one price a close is measured against. A tranche and an
            // exit are the band they sit on, which has width. A rule runs from its
            // label across the column and stops there, so it never runs under the
            // words set to the right of the column.
            if (row.Kind is PlanKind.Stop or PlanKind.Invalidation)
            {
                var heavy = row.Kind == PlanKind.Invalidation;

                svg.Append(Invariant, $"<line class=\"{row.Kind}-rule\" x1=\"{PlanAxis - 10}\" y1=\"{Number(bottom)}\" x2=\"{PlanRuleEnd}\" y2=\"{Number(bottom)}\" ");
                svg.Append(Invariant, $"stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"{(heavy ? "2.5" : "1")}\"{(heavy ? string.Empty : " stroke-dasharray=\"4 3\"")}/>");
            }
            else if (row.Kind == PlanKind.Tranche)
            {
                svg.Append(Invariant, $"<rect class=\"tranche-zone\" x=\"{PlanAxis + 1}\" y=\"{Number(top)}\" width=\"20\" height=\"{Number(height)}\" ");
                svg.Append(Invariant, $"fill=\"{SupportHue}\" fill-opacity=\"{(row.Traded ? 0.85 : 0.3)}\"/>");
            }
            else
            {
                svg.Append(Invariant, $"<rect class=\"exit-zone\" x=\"{PlanAxis - 10}\" y=\"{Number(top)}\" width=\"20\" height=\"{Number(height)}\" ");
                svg.Append(Invariant, $"fill=\"{ResistanceHue}\" fill-opacity=\"{(row.Traded ? 0.3 : 0.12)}\"/>");
                svg.Append(Invariant, $"<line class=\"m-sale\" x1=\"{PlanAxis}\" y1=\"{Number(bottom)}\" x2=\"{PlanAxis + 22}\" y2=\"{Number(bottom)}\"/>");
            }

            // The row in words, because hue is never the only channel and a
            // reader who cannot separate the two loses nothing.
            var onTheRight = rightAt.TryGetValue(index, out var placedRight);
            var (wanted, labelY, lines) = onTheRight ? placedRight : leftAt[index];
            var x = onTheRight ? PlanAxis + 28 : PlanAxis - 14;
            var anchor = onTheRight ? "start" : "end";

            if (Math.Abs(labelY - wanted) > 3)
            {
                var from = onTheRight ? PlanAxis + 22 : PlanAxis - 10;

                svg.Append(Invariant, $"<line class=\"m-leader\" x1=\"{from}\" y1=\"{Number(wanted - 4)}\" x2=\"{x + (onTheRight ? -3 : 3)}\" y2=\"{Number(labelY - 4)}\"/>");
            }

            for (var line = 0; line < lines.Length; line++)
            {
                var style = line == 0 ? (row.Kind is PlanKind.Stop ? "m-rownote" : "m-row") : "m-rownote";
                var kind = line == 0 ? "plan-label " : string.Empty;

                svg.Append(Invariant, $"<text class=\"{kind}{style}\" x=\"{x}\" y=\"{Number(labelY + (line * 13))}\" text-anchor=\"{anchor}\">{Escaped(lines[line])}</text>");
            }

            svg.Append("</g>");
        }

        // A plan with nothing to buy says so where its purchases would be, rather than
        // leaving the lower half of the column empty.
        if (rows.All(row => row.Kind != PlanKind.Tranche))
        {
            var y0 = middle + 24;
            var exits = rows.Count(row => row.Kind == PlanKind.Exit);

            svg.Append(Invariant, $"<g class=\"no-purchase\"><rect class=\"m-absent\" x=\"{PlanAxis + 10}\" y=\"{Number(y0)}\" width=\"{PlanWidth - PlanAxis - 12}\" height=\"{Number(PlanHeight - PlanEdge - y0)}\"/>");
            svg.Append(Invariant, $"<text class=\"m-absent-t\" x=\"{PlanAxis + 22}\" y=\"{Number(y0 + 24)}\">No support band below the price.</text>");
            svg.Append(Invariant, $"<text class=\"m-absent-s\" x=\"{PlanAxis + 22}\" y=\"{Number(y0 + 42)}\">No purchase and no invalidation are drawn.</text>");
            svg.Append(Invariant, $"<text class=\"m-absent-s\" x=\"{PlanAxis + 22}\" y=\"{Number(y0 + 57)}\">{exits} exit zone(s), all above the price.</text></g>");
        }

        // The price marker last, so it is drawn over the zones rather than under
        // them: it is the one thing the whole figure is read against. Its line ends
        // where the rules do, clear of the words beside the column.
        svg.Append(Invariant, $"<g class=\"price-marker\" data-close=\"{close}\">");
        svg.Append(Invariant, $"<line class=\"m-nowline\" x1=\"0\" y1=\"{Number(middle)}\" x2=\"{PlanRuleEnd}\" y2=\"{Number(middle)}\"/>");
        svg.Append(Invariant, $"<rect class=\"m-nowtag\" x=\"0\" y=\"{Number(middle - 11)}\" width=\"{PlanAxis - 24}\" height=\"22\" rx=\"2\"/>");
        svg.Append(Invariant, $"<text class=\"m-nowtag-t\" x=\"7\" y=\"{Number(middle + 4)}\">Price now {Price(close)}</text>");
        svg.Append("</g>");

        svg.Append("</svg>");

        return svg.ToString();
    }

    // The plan column's drawing: its size, the band its heads sit in, and where the axis runs.
    const int PlanWidth = 440;
    const int PlanHeight = 440;
    const int PlanEdge = 40;
    const int PlanAxis = 150;

    // Where a rule across the column ends: past the zones drawn on it and short of the
    // words set to its right, which start at the axis and 28.
    const int PlanRuleEnd = PlanAxis + 24;

    // A zone's prices as its label says them, and a zone of one price as that price.
    static string Zone(PlanRow row) =>
        row.LowEdge == row.HighEdge ? Price(row.LowEdge) : Formatted($"{Price(row.LowEdge)} to {Price(row.HighEdge)}");

    // A row's sentence as the lines it is drawn on, one clause to a line.
    static string[] Clauses(string detail) =>
        [.. detail.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    // Each label's line, pushed down from the one above it until none overlap, and the
    // whole run moved up where it would leave the picture. Bands a label may not sit in,
    // such as the price marker's tag, are stepped over.
    static Dictionary<int, (double Wanted, double At, string[] Lines)> Spread(
        List<(int Row, double Wanted, string[] Lines)> labels,
        IReadOnlyList<(double From, double To)> fixedLines)
    {
        var placed = new Dictionary<int, (double Wanted, double At, string[] Lines)>();
        var floor = PlanEdge - 4.0;

        foreach (var (row, wanted, lines) in labels.OrderBy(label => label.Wanted))
        {
            var at = Math.Max(wanted, floor + 12);
            var height = lines.Length * 13;

            foreach (var (from, to) in fixedLines)
            {
                if (at + height - 10 > from && at - 10 < to)
                {
                    at = wanted < (from + to) / 2 ? from - height + 8 : to + 12;
                }
            }

            placed[row] = (wanted, at, lines);
            floor = at + height - 12 + 4;
        }

        var overflow = placed.Values.Select(label => label.At + (label.Lines.Length * 13) - 12).DefaultIfEmpty(0).Max() - (PlanHeight - 22);

        if (overflow > 0)
        {
            foreach (var row in placed.Keys.ToArray())
            {
                placed[row] = placed[row] with { At = placed[row].At - overflow };
            }
        }

        return placed;
    }

    // The tranche table and the exit table, which are what the plan column's
    // figure is read beside, each under its own heading. A tranche's zone, what it
    // is bought on and its stop are a column each, and the stop the invalidation
    // sits at says so. Every cell is a stored value, drawn at the places it is read
    // at with the stored edges on its row.
    public string PlanTables(string ticker, IReadOnlyList<PlanRow> rows)
    {
        var tranches = rows.Where(row => row.Kind == PlanKind.Tranche).ToArray();
        var exits = rows.Where(row => row.Kind == PlanKind.Exit).ToArray();
        var invalidation = rows.FirstOrDefault(row => row.Kind == PlanKind.Invalidation)?.LowEdge;

        var html = new StringBuilder();

        html.Append("<div class=\"sub plan-sub\">Entries</div><div class=\"tbl-wrap\">");
        html.Append(Invariant, $"<table class=\"tranche-table\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{tranches.Length}\">");
        html.Append("<tr><th>Zone</th><th>Buy on</th><th>Stop on</th></tr>");

        foreach (var row in tranches)
        {
            html.Append(Invariant, $"<tr data-low-edge=\"{row.LowEdge}\" data-high-edge=\"{row.HighEdge}\"><td class=\"num\">{Zone(row)}</td>");

            if (row.BuyOn is { } buyOn)
            {
                var stop = row.Stop is { } at
                    ? Formatted($"a daily close below {Price(at)}") + (at == invalidation ? ", where the whole position is wrong" : string.Empty)
                    : "no stop beneath";

                html.Append(Invariant, $"<td>{Escaped(buyOn)}</td><td>{Escaped(stop)}</td></tr>");
            }
            else
            {
                html.Append(Invariant, $"<td colspan=\"2\">{Escaped(row.Detail)}</td></tr>");
            }
        }

        html.Append("</table></div>");

        html.Append("<div class=\"sub plan-sub\">Exits</div><div class=\"tbl-wrap\">");
        html.Append(Invariant, $"<table class=\"exit-table\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{exits.Length}\">");
        html.Append("<tr><th>Zone</th><th>Action</th></tr>");

        foreach (var row in exits)
        {
            html.Append(Invariant, $"<tr data-low-edge=\"{row.LowEdge}\" data-high-edge=\"{row.HighEdge}\" data-traded=\"{(row.Traded ? "true" : "false")}\">");
            html.Append(Invariant, $"<td class=\"num\">{Zone(row)}</td><td>{Escaped(row.Detail)}</td></tr>");
        }

        html.Append("</table></div>");

        return html.ToString();
    }

    // The volume profile, drawn horizontally against a price axis it is given.
    //
    // The width of a row is its share of the busiest band rather than of the
    // period, because a profile whose rows were scaled to the period would be
    // twenty short stubs on a name whose volume is evenly spread. The share of
    // the period is on the row as a number instead, which is the figure the
    // report quotes and the one the shelf threshold is read against.
    public string VolumeProfile(
        string ticker,
        IReadOnlyList<ProfileBand> bands,
        PriceAxis axis,
        IReadOnlyList<ChartBand>? levels = null,
        double? scale = null)
    {
        if (bands.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"0\">" +
                $"{Escaped(ticker)} has no volume profile, which is what a name with fewer than " +
                $"sixty stored sessions gets.</p>";
        }

        foreach (var band in bands)
        {
            if (band.High <= band.Low)
            {
                throw new ArgumentException(
                    $"A profile band runs from {band.Low} to {band.High}, which is not a band. A row " +
                    "of no height would draw nothing and would take its share of the period with it.",
                    nameof(bands));
            }
        }

        var loudest = bands.Max(band => band.Shares);
        var busiest = loudest > 0 ? loudest : 1;

        // Its own width and height rather than the width of what holds it, so the
        // picture is the size of the chart's price pane beside it and never stretched
        // across the page.
        // The legend row is carried too, empty, because the chart beside this one has one
        // and the two are anchored at the top: without it every price here would sit a
        // legend's height above the same price in the chart.
        const int Drawn = LegendRow + PriceHeight + ProfileCaption;

        var size = scale is { } at
            ? Formatted($"width=\"{Number(ProfileWidth * at)}\" height=\"{Number(Drawn * at)}\"")
            : Formatted($"width=\"{ProfileWidth}\" height=\"{Drawn}\"");

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {ProfileWidth} {Drawn}\" {size} ");
        svg.Append(Invariant, $"role=\"img\" class=\"volume-profile\" data-ticker=\"{Escaped(ticker)}\" ");
        svg.Append(Invariant, $"data-bands=\"{bands.Count}\" data-axis-low=\"{Number(axis.Low)}\" data-axis-high=\"{Number(axis.High)}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, shares traded in {bands.Count} price bands</title>");
        svg.Append("<desc>Shares traded in each price band, drawn against the price axis of the chart beside it.</desc>");

        svg.Append(Invariant, $"<g class=\"m-body\" transform=\"translate(0,{LegendRow})\">");
        svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"0\" y=\"0\" width=\"{ProfileWidth}\" height=\"{PriceHeight}\"/>");

        // The chart's level bands carried across, so a shelf and the band it sits in
        // read as one price.
        foreach (var level in levels ?? [])
        {
            var top = Math.Max(0, At(axis, PlotValue(level.HighEdge)));
            var bottom = Math.Min(PriceHeight, At(axis, PlotValue(level.LowEdge)));

            if (bottom > top)
            {
                svg.Append(Invariant, $"<rect class=\"m-band-{(level.Role == "support" ? "sup" : "res")}\" x=\"0\" y=\"{Number(top)}\" width=\"{ProfileWidth}\" height=\"{Number(bottom - top)}\"/>");
            }
        }

        foreach (var band in bands)
        {
            var top = At(axis, PlotValue(band.High));
            var bottom = At(axis, PlotValue(band.Low));
            var width = (double)band.Shares / busiest * (ProfileWidth - (2 * Margin));

            // A band whose whole span sits outside the axis draws nothing rather
            // than being clamped to an edge, where it would read as a band at a
            // price it is not at.
            var height = bottom - top;

            svg.Append(Invariant, $"<rect class=\"band\" data-band-low=\"{band.Low.ToString(Invariant)}\" ");
            svg.Append(Invariant, $"data-band-high=\"{band.High.ToString(Invariant)}\" data-shares=\"{band.Shares}\" ");
            svg.Append(Invariant, $"data-share-of-period=\"{band.ShareOfPeriod.ToString("0.#####", Invariant)}\" ");
            svg.Append(Invariant, $"x=\"{Margin}\" y=\"{Number(top)}\" width=\"{Number(Math.Max(0, width))}\" ");
            svg.Append(Invariant, $"height=\"{Number(Math.Max(0, height))}\" fill=\"var(--muted, #6a6a6a)\"/>");
        }

        // Where a band would reach if every band had traded the same, and twice that, as
        // two rules to read the bars against. They are positions on the picture and no
        // band is marked by them.
        var even = bands.Average(band => band.Shares);
        var evenX = Margin + (even / busiest * (ProfileWidth - (2 * Margin)));

        svg.Append(Invariant, $"<line class=\"m-evenrule\" x1=\"{Number(evenX)}\" y1=\"0\" x2=\"{Number(evenX)}\" y2=\"{PriceHeight}\"/>");
        svg.Append(Invariant, $"<text class=\"m-tick\" x=\"{Number(evenX + 3)}\" y=\"{PriceHeight - 4}\">even</text>");

        if (2 * even <= busiest)
        {
            var twiceX = Margin + (2 * even / busiest * (ProfileWidth - (2 * Margin)));

            svg.Append(Invariant, $"<line class=\"m-evenrule2\" x1=\"{Number(twiceX)}\" y1=\"0\" x2=\"{Number(twiceX)}\" y2=\"{PriceHeight}\"/>");
            svg.Append(Invariant, $"<text class=\"m-tick\" x=\"{Number(twiceX + 3)}\" y=\"11\">twice even</text>");
        }

        svg.Append(Invariant, $"<line class=\"m-axisline\" x1=\"0\" y1=\"{PriceHeight}\" x2=\"{ProfileWidth}\" y2=\"{PriceHeight}\"/>");
        svg.Append(Invariant, $"<text class=\"m-tick\" x=\"0\" y=\"{PriceHeight + 14}\">Shares traded by price</text>");

        svg.Append("</g>");
        svg.Append("</svg>");

        return svg.ToString();
    }

    // The row beneath the profile's pane its caption sits in.
    const int ProfileCaption = 20;

    // Support is green and resistance is orange, and this is the one place in
    // the whole system those two hues are used. Every other mark is neutral ink
    // or one hue in steps.
    // see: Support and resistance own two hues and nothing else uses them
    const string SupportHue = "var(--support, #2f7d4f)";
    const string ResistanceHue = "var(--resistance, #b5651d)";

    const int PaneTitle = 22;
    const int PaneLine = 16;
    const int PaneGap = 22;

    // The momentum panel. Relative strength on one small axis, and the convergence line
    // with its signal line and the gap between them on a second, each pane headed by what
    // it reads, what it means and where it stood at the last session drawn.
    //
    // The rules are the point of the mark rather than decoration. Section 5 says an RSI
    // near 50 is balanced, above 70 stretched upward and below 30 stretched downward, so
    // its pane draws and names all three; a reading drawn without them is a line whose
    // height means nothing, which a reader has to bring their own conventions to.
    //
    // The three convergence readings share an axis because they are one quantity read
    // three ways: the signal line is read where the line crosses it and the bars are the
    // gap between the two, so drawn apart the crossing is on no pane at all. Relative
    // strength is a score out of a hundred and the line is in the stock's money, so the
    // two keep an axis each, since one shared scale would flatten whichever has the
    // smaller numbers into a straight line.
    public string MomentumPanel(string ticker, IReadOnlyList<MomentumReading> readings)
    {
        if (readings.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-readings=\"0\">" +
                $"{Escaped(ticker)} has no momentum readings stored.</p>";
        }

        var panes = readings.GroupBy(reading => PaneOf(reading.Name), StringComparer.Ordinal).ToArray();

        // A pane's heading is its title and the lines saying what it means, and its plot is
        // taller where the three convergence readings share it, so a crossing has room.
        int Head(string pane) => PaneTitle + (PaneMeaning(pane).Length * PaneLine) + 6;
        int Plot(IEnumerable<MomentumReading> pane) => pane.All(reading => reading.Floor is not null && reading.Ceiling is not null) ? 116 : 124;

        var height = panes.Sum(pane => Head(pane.Key) + Plot(pane)) + ((panes.Length - 1) * PaneGap);
        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {height}\" ");
        // At the width of the chart's plot above it rather than of whatever holds it,
        // so a session is at one distance across the two and nothing here is drawn
        // larger than it was made.
        svg.Append(Invariant, $"width=\"{Width}\" height=\"{height}\" role=\"img\" class=\"momentum-panel\" data-ticker=\"{Escaped(ticker)}\" ");
        svg.Append(Invariant, $"data-readings=\"{readings.Count}\" data-panes=\"{panes.Length}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, {readings.Count} momentum reading(s) on {panes.Length} axis(es)</title>");
        svg.Append("<desc>Relative strength on its own axis, and the convergence line with its signal line and the gap between them on a second, each with its rules drawn and named.</desc>");

        var top = 0;

        foreach (var pane in panes)
        {
            var members = pane.ToArray();
            var plotTop = top + Head(pane.Key);
            var plotHeight = Plot(members);
            var sessions = members.Max(reading => reading.Values.Count);
            var neutral = members[0].Neutral;
            var drawn = members.SelectMany(reading => reading.Values).OfType<double>().ToArray();
            var bounded = members.All(reading => reading.Floor is not null && reading.Ceiling is not null);

            // The axis takes in the neutral rule as well as the values, because a rule
            // outside the scale is a rule drawn off the pane, and a reading that never
            // crossed its rule is exactly the case a reader most wants to see.
            var low = bounded ? members.Min(reading => reading.Floor!.Value) : Math.Min(drawn.Length > 0 ? drawn.Min() : neutral, neutral);
            var high = bounded ? members.Max(reading => reading.Ceiling!.Value) : Math.Max(drawn.Length > 0 ? drawn.Max() : neutral, neutral);
            var span = high - low > 0 ? high - low : 1;

            double Y(double value) => plotTop + plotHeight - 3 - ((value - low) / span * (plotHeight - 6));

            var slot = (double)(Width - (2 * Margin)) / Math.Max(sessions, 1);

            svg.Append(Invariant, $"<g class=\"pane\" data-pane=\"{Escaped(pane.Key)}\" data-neutral=\"{Number(neutral)}\">");

            // The heading: what the pane reads and each reading's value at the last session
            // drawn, then what the pane means in words.
            var last = members
                .Where(reading => reading.Values.Count > 0 && reading.Values[^1] is not null)
                .Select(reading => (ReadingPart(reading.Name) + " " + Number(reading.Values[^1]!.Value)).Trim())
                .ToArray();
            var stood = last.Length switch
            {
                0 => "no reading at the last session drawn",
                1 => last[0] + " at the last session drawn",
                _ => string.Join(", ", last[..^1]) + " and " + last[^1] + " at the last session drawn",
            };

            svg.Append(Invariant, $"<text class=\"m-pane-h\" x=\"{Margin}\" y=\"{top + 14}\">{Escaped(TitleOf(pane.Key))}: {Escaped(stood)}</text>");

            var line = top + PaneTitle + 10;

            foreach (var said in PaneMeaning(pane.Key))
            {
                svg.Append(Invariant, $"<text class=\"m-pane-c\" x=\"{Margin}\" y=\"{line}\">{Escaped(said)}</text>");
                line += PaneLine;
            }

            svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"{Margin}\" y=\"{plotTop}\" width=\"{Width - (2 * Margin)}\" height=\"{plotHeight}\"/>");

            // A pane on a fixed scale carries the range its reading usually sits in and the
            // two edges section 5 names, which are reading conventions the panel draws and
            // nothing computes with.
            // see: The momentum panel is context a reader weighs, and nothing computes with it
            var rules = new List<(double At, string Named)>();

            if (bounded)
            {
                var usualLow = low + (span * 0.3);
                var usualHigh = low + (span * 0.7);

                svg.Append(Invariant, $"<rect class=\"m-neutral\" x=\"{Margin}\" y=\"{Number(Y(usualHigh))}\" width=\"{Width - (2 * Margin)}\" height=\"{Number(Y(usualLow) - Y(usualHigh))}\"/>");

                foreach (var (edge, named) in new[] { (usualHigh, "stretched upward"), (usualLow, "stretched downward") })
                {
                    svg.Append(Invariant, $"<line class=\"m-edge-rule\" x1=\"{Margin}\" y1=\"{Number(Y(edge))}\" x2=\"{Width - Margin}\" y2=\"{Number(Y(edge))}\"/>");
                    rules.Add((edge, Number(edge) + ", " + named));
                }
            }

            // The neutral rule, once for the pane, since every reading on it is read against
            // the same value, and before the readings so they are drawn over it.
            svg.Append(Invariant, $"<line class=\"neutral-rule\" x1=\"{Margin}\" y1=\"{Number(Y(neutral))}\" ");
            svg.Append(Invariant, $"x2=\"{Width - Margin}\" y2=\"{Number(Y(neutral))}\" ");
            svg.Append("stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\" stroke-dasharray=\"3 3\"/>");
            rules.Add((neutral, Number(neutral) + (bounded ? ", balanced" : string.Empty)));

            // The bars first, so the two lines are drawn over the gap between them.
            foreach (var reading in members.OrderBy(reading => reading.Name == "macd_hist" ? 0 : 1))
            {
                var values = reading.Values.OfType<double>().Count();

                svg.Append(Invariant, $"<g class=\"reading\" data-name=\"{Escaped(reading.Name)}\" ");
                svg.Append(Invariant, $"data-neutral=\"{Number(reading.Neutral)}\" data-values=\"{values}\">");

                if (reading.Name == "macd_hist")
                {
                    // The gap between the two lines, as bars either side of the rule: above
                    // is strengthening and below is weakening.
                    for (var at = 0; at < reading.Values.Count; at++)
                    {
                        if (reading.Values[at] is { } bar)
                        {
                            var y = Y(bar);
                            var zero = Y(reading.Neutral);

                            svg.Append(Invariant, $"<rect class=\"m-hist\" x=\"{Number(Margin + (slot * at) + (slot * 0.18))}\" y=\"{Number(Math.Min(y, zero))}\" width=\"{Number(slot * 0.64)}\" height=\"{Number(Math.Abs(zero - y))}\"/>");
                        }
                    }
                }
                else
                {
                    // One path per unbroken run, for the reason the averages break: a
                    // reading has no value until its warm-up ends. The signal line is
                    // dashed, so the two lines read apart where they cross.
                    var style = reading.Name == "macd_signal" ? "m-mom-2" : "m-mom";
                    var run = new StringBuilder();

                    for (var at = 0; at <= reading.Values.Count; at++)
                    {
                        var value = at < reading.Values.Count ? reading.Values[at] : null;

                        if (value is { } point)
                        {
                            run.Append(run.Length == 0 ? 'M' : 'L')
                                .Append(Number(Margin + (slot * at) + (slot / 2)))
                                .Append(' ')
                                .Append(Number(Y(point)))
                                .Append(' ');

                            continue;
                        }

                        if (run.Length > 0)
                        {
                            svg.Append(Invariant, $"<path class=\"{style}\" d=\"{run.ToString().Trim()}\"/>");
                            run.Clear();
                        }
                    }

                    if (reading.Values.Count > 0 && reading.Values[^1] is { } newest)
                    {
                        svg.Append(Invariant, $"<circle class=\"m-last\" cx=\"{Number(Margin + (slot * (reading.Values.Count - 1)) + (slot / 2))}\" cy=\"{Number(Y(newest))}\" r=\"2.6\"/>");
                    }
                }

                svg.Append("</g>");
            }

            // Each rule named at the pane's left edge, over the oldest sessions, so no name is
            // written across the newest, which are the ones read against the bands tonight.
            foreach (var (at, named) in rules)
            {
                svg.Append(Invariant, $"<text class=\"m-rule-t\" x=\"{Margin + 4}\" y=\"{Number(Y(at) - 3)}\">{Escaped(named)}</text>");
            }

            // Sessions no reading of the pane has a value for are a dashed box saying how
            // many, never a stretch of pane that reads as a quiet reading.
            // see: Not yet measured is drawn as a dashed outline, never as a pale value
            var gapStart = -1;

            for (var at = 0; at <= sessions; at++)
            {
                var missing = at < sessions && members.All(reading => at >= reading.Values.Count || reading.Values[at] is null);

                if (missing && gapStart < 0)
                {
                    gapStart = at;
                }
                else if (!missing && gapStart >= 0)
                {
                    var from = Margin + (slot * gapStart);
                    var wide = slot * (at - gapStart);

                    svg.Append(Invariant, $"<g class=\"not-computed\" data-sessions=\"{at - gapStart}\"><rect class=\"m-absent\" x=\"{Number(from + 0.6)}\" y=\"{plotTop + 1}\" width=\"{Number(Math.Max(wide - 1.2, 1))}\" height=\"{plotHeight - 2}\"/>");

                    if (wide > 190)
                    {
                        svg.Append(Invariant, $"<text class=\"m-absent-s\" x=\"{Number(from + 8)}\" y=\"{plotTop + (plotHeight / 2) + 4}\">{at - gapStart} of {sessions} sessions: not yet computed</text>");
                    }

                    svg.Append("</g>");
                    gapStart = -1;
                }
            }

            svg.Append("</g>");

            top = plotTop + plotHeight + PaneGap;
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    // The axis a reading is drawn on: relative strength on its own, and the convergence line,
    // its signal line and the gap between them on one.
    static string PaneOf(string name) => name switch
    {
        "rsi14" => "strength",
        "macd" or "macd_signal" or "macd_hist" => "convergence",
        _ => name,
    };

    // A pane's name as a reader says it, with the names the store holds its readings under.
    static string TitleOf(string pane) => pane switch
    {
        "strength" => "Relative strength over 14 sessions (rsi14)",
        "convergence" => "Trend momentum (macd, the solid line) and its signal line (macd_signal, dashed)",
        _ => pane,
    };

    // What a pane's readings mean, in the words section 5 reads them by, a line of the pane's
    // heading each so none runs past the picture's width.
    static string[] PaneMeaning(string pane) => pane switch
    {
        "strength" => ["It runs from 0 to 100. Above 70 the stock has risen fast and is stretched upward, below 30 it has fallen fast and is stretched downward, and the shaded middle is where it usually sits."],
        "convergence" =>
        [
            "The solid line is the gap between a fast and a slow average of the price, and the dashed line is a slower average of that gap.",
            "The bars (macd_hist) are the distance between the two lines: above zero the move is strengthening, below zero it is weakening, and a crossing is where the bars change side.",
        ],
        _ => [],
    };

    // How a reading's value at the last session is named in its pane's heading.
    static string ReadingPart(string name) => name switch
    {
        "rsi14" => string.Empty,
        "macd" => "the line",
        "macd_signal" => "its signal line",
        "macd_hist" => "the gap",
        _ => name,
    };

    // The level summary table. Each band with its members and their dates.
    //
    // A table rather than a mark, and that is section 15.5's own arithmetic: it
    // states seven marks and this is not one of them. It is a region of the name
    // screen, listed in 15.9 beside the chart, and it is written here because
    // the marks and the regions that read them are drawn by the same server.
    //
    // Highest price first, with the close as a row between the resistance bands
    // and the support bands, so the table reads in the direction the plan column
    // beside it does.
    public string LevelSummary(
        string ticker,
        IReadOnlyList<SummaryBand> bands,
        IReadOnlyList<AbsentAverage>? absent = null,
        decimal? close = null)
    {
        var missing = absent ?? [];

        if (bands.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"0\">" +
                $"{Escaped(ticker)} has no level bands stored.</p>";
        }

        var table = new StringBuilder();
        var closeDrawn = close is null;

        table.Append("<div class=\"tbl-wrap\">");
        table.Append(Invariant, $"<table class=\"level-summary\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"{bands.Count}\">");
        table.Append("<caption>Level summary, each band with its members and their dates</caption>");
        table.Append("<thead><tr><th>Band</th><th>Role</th><th>Away</th><th>Strength</th><th>Members</th></tr></thead><tbody>");

        foreach (var band in bands
            .OrderBy(band => band.Role == LevelSeries.Resistance ? 0 : 1)
            .ThenByDescending(band => band.LowEdge)
            .ThenByDescending(band => band.HighEdge))
        {
            if (!closeDrawn && band.Role != LevelSeries.Resistance)
            {
                table.Append(CloseRow(close!.Value));
                closeDrawn = true;
            }

            // A band of one price is written as one price rather than as a range
            // from a number to itself, because the second reads as a mistake.
            var edges = band.LowEdge == band.HighEdge
                ? Price(band.LowEdge)
                : $"{Price(band.LowEdge)} to {Price(band.HighEdge)}";

            var role = band.Immediate ? $"{band.Role}, immediate" : band.Role;

            table.Append(Invariant, $"<tr class=\"band\" data-low-edge=\"{band.LowEdge.ToString(Invariant)}\" data-high-edge=\"{band.HighEdge.ToString(Invariant)}\" ");
            table.Append(Invariant, $"data-role=\"{Escaped(band.Role)}\" data-immediate=\"{(band.Immediate ? 1 : 0)}\" ");
            table.Append(Invariant, $"data-members=\"{band.Members.Count}\" data-anchored=\"{(band.HasNonAverageAnchor ? 1 : 0)}\">");
            // How far the nearer edge of the band sits from tonight's close, counted in the
            // moves this name usually makes in a session, which is how every distance on
            // these pages is stated. A name whose chart has not moved has none rather than
            // a distance divided by nothing.
            // see: Distances are stated as typical days' moves
            var away = band.AwayInTypicalDays is { } days
                ? Formatted($"{days:0.0} typical days")
                : "not measured";

            table.Append(Invariant, $"<td>{Escaped(edges)}</td><td>{Escaped(role)}</td>");
            table.Append(Invariant, $"<td class=\"away\" data-away=\"{(band.AwayInTypicalDays is { } value ? value.ToString(Invariant) : "none")}\">{Escaped(away)}</td>");
            // The strength with a bar of a fixed length a point beside it, so two bands
            // compare at a glance and two names' tables compare the same way.
            table.Append(Invariant, $"<td class=\"strength\" data-strength=\"{band.Strength}\"><span class=\"str-bar\" style=\"width:{band.Strength * StrengthBarPerPoint}px\" aria-hidden=\"true\"></span>{band.Strength}</td><td>");

            // The members one disclosure down, under a line saying what the evidence
            // is, since a band can rest on a dozen pieces of it.
            if (band.Members.Count > 0)
            {
                table.Append(Invariant, $"<details><summary>{Escaped(EvidenceLine(band.Members))}</summary>");
            }

            table.Append("<ul>");

            foreach (var member in band.Members)
            {
                table.Append(Invariant, $"<li class=\"member\" data-kind=\"{Escaped(member.Kind)}\" data-date=\"{member.Date:yyyy-MM-dd}\" data-price=\"{member.Price.ToString(Invariant)}\">");
                table.Append(Invariant, $"{Escaped(MemberLine(member))}</li>");
            }

            table.Append(band.Members.Count > 0 ? "</ul></details></td></tr>" : "</ul></td></tr>");
        }

        if (!closeDrawn)
        {
            table.Append(CloseRow(close!.Value));
        }

        table.Append("</tbody>");

        // The averages that anchor nothing, each saying why. Section 18's row
        // asks for the string and this is the surface it is read on: an average
        // with no value cannot be a member of any band above, so without this
        // row it would be absent from the table with nothing saying so.
        if (missing.Count > 0)
        {
            table.Append(Invariant, $"<tfoot data-absent=\"{missing.Count}\">");

            foreach (var average in missing)
            {
                table.Append(Invariant, $"<tr class=\"absent-average\" data-name=\"{Escaped(average.Name)}\" ");
                table.Append(Invariant, $"data-bar-count=\"{average.BarCount}\"><td>{Escaped(average.Name)}</td>");
                table.Append(Invariant, $"<td colspan=\"3\">not available, {average.BarCount} bars</td></tr>");
            }

            table.Append("</tfoot>");
        }

        table.Append("</table></div>");
        table.Append(Invariant, $"<p class=\"level-key\" data-recent=\"{LevelSeries.RecentSessions}\"><b>Strength.</b> One point for each piece of evidence in a band: each swing, each visit the price paid it, and each average, retracement and volume shelf inside it. One more where any of it came in the last {LevelSeries.RecentSessions} sessions, one where a retracement and a swing agree, and one where a heavy volume shelf sits in it. The bar beside each number is one step a point.</p>");

        return table.ToString();
    }

    // The length a point of strength adds to the bar beside it, in pixels.
    const int StrengthBarPerPoint = 3;

    // The close, drawn as a row between the resistance bands above it and the
    // support bands below it.
    static string CloseRow(decimal close) =>
        string.Create(CultureInfo.InvariantCulture, $"<tr class=\"close-row\" data-close=\"{close}\"><td>{Price(close)}</td><td colspan=\"4\">the close</td></tr>");

    // What a band's evidence is, in one line. The turns first, being the swings
    // and the visits the price paid it, counted by kind with the first and last
    // dates, or the one date where they are the same; then the averages, the
    // retracements and the shelves by kind, which carry no date of their own
    // worth stating because each is recomputed every night.
    // see: A member's date is the session its evidence occurred on, and a figure recomputed nightly has none of its own
    internal static string EvidenceLine(IReadOnlyList<SummaryMember> members)
    {
        var turns = members.Where(member => member.Kind is "swing high" or "swing low" or "touch").ToArray();
        var parts = new List<string>();

        if (turns.Length > 0)
        {
            var kinds = new[] { ("swing high", "swing highs"), ("swing low", "swing lows"), ("touch", "touches") }
                .Select(kind => (Count: turns.Count(member => member.Kind == kind.Item1), One: kind.Item1, Many: kind.Item2))
                .Where(kind => kind.Count > 0)
                .Select(kind => string.Create(CultureInfo.InvariantCulture, $"{kind.Count} {(kind.Count == 1 ? kind.One : kind.Many)}"));
            var first = turns.Min(member => member.Date);
            var last = turns.Max(member => member.Date);
            var when = first == last
                ? FormattableString.Invariant($"on {first:yyyy-MM-dd}")
                : FormattableString.Invariant($"first {first:yyyy-MM-dd}, last {last:yyyy-MM-dd}");
            var times = turns.Length switch
            {
                1 => "once",
                2 => "twice",
                _ => string.Create(CultureInfo.InvariantCulture, $"{turns.Length} times"),
            };

            parts.Add($"turned the price {times}: {string.Join(", ", kinds)}; {when}");
        }

        var averages = members
            .Where(member => member.Kind.StartsWith("sma", StringComparison.Ordinal))
            .Select(member => "the " + AverageName(member.Kind))
            .Distinct(StringComparer.Ordinal);
        var retracements = members
            .Where(member => member.Kind.StartsWith("retracement ", StringComparison.Ordinal))
            .Select(member => member.Kind["retracement ".Length..] + "%")
            .ToArray();
        var others = averages.ToList();

        if (retracements.Length > 0)
        {
            others.Add(retracements.Length == 1
                ? $"the {retracements[0]} retracement"
                : $"the {Joined(retracements)} retracements");
        }

        if (members.Any(member => member.Kind == "shelf"))
        {
            others.Add("a heavy volume shelf");
        }

        if (others.Count > 0)
        {
            parts.Add((turns.Length > 0 ? "also " : string.Empty) + Joined(others));
        }

        return string.Join("; ", parts);
    }

    // One member as the disclosure lists it: a swing or a visit at its price on
    // its session, a retracement at its price with the session the move it is
    // drawn across ended on, and an average or a shelf by kind at its price alone.
    internal static string MemberLine(SummaryMember member) => member.Kind switch
    {
        "swing high" or "swing low" or "touch" => FormattableString.Invariant($"{member.Kind} at {Price(member.Price)} on {member.Date:yyyy-MM-dd}"),
        "shelf" => $"a heavy volume shelf at {Price(member.Price)}",
        _ when member.Kind.StartsWith("sma", StringComparison.Ordinal) => $"the {AverageName(member.Kind)} at {Price(member.Price)}",
        _ when member.Kind.StartsWith("retracement ", StringComparison.Ordinal) =>
            FormattableString.Invariant($"the {member.Kind["retracement ".Length..]}% retracement at {Price(member.Price)}, of the move that ended {member.Date:yyyy-MM-dd}"),
        _ => FormattableString.Invariant($"{member.Kind} at {Price(member.Price)} on {member.Date:yyyy-MM-dd}"),
    };

    // A list read aloud: one item, two joined by "and", or more with commas and a
    // final "and".
    static string Joined(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    public string LevelChart(
        string ticker,
        IReadOnlyList<ChartBar> bars,
        IReadOnlyList<ChartAverage>? averages = null,
        IReadOnlyList<ChartBand>? bands = null,
        ChartFrame? frame = null)
    {
        if (bars.Count < FewestBars)
        {
            return Degraded(ticker, bars.Count);
        }

        // An average whose length does not match the bars is refused rather
        // than drawn against the wrong sessions. A line one session short would
        // draw every point one slot to the left and look entirely plausible,
        // which is the failure that reports green.
        var lines = averages ?? [];
        var shading = bands ?? [];

        foreach (var band in shading)
        {
            if (band.HighEdge < band.LowEdge)
            {
                throw new ArgumentException(
                    $"A level band runs from {band.LowEdge} to {band.HighEdge}, which is inverted. Drawn " +
                    "as given it would be a rectangle of negative height, which renders as nothing at " +
                    "all rather than as a fault.",
                    nameof(bands));
            }
        }

        foreach (var line in lines)
        {
            if (line.Values.Count != bars.Count)
            {
                throw new ArgumentException(
                    $"The average '{line.Name}' carries {line.Values.Count} values against " +
                    $"{bars.Count} sessions. A mark draws one value per session, and a line of a " +
                    "different length would be drawn against the wrong dates rather than refused.",
                    nameof(averages));
            }
        }

        // The one scale, computed by the method the profile beside this chart is
        // given. A flat series has no span and is drawn through the middle
        // rather than refused, which PriceAxis.Range carries.
        var axis = AxisFor(bars, lines);
        var loudest = bars.Max(bar => bar.Volume);
        var busiest = loudest > 0 ? loudest : 1;

        var slot = (double)(Width - (2 * Margin)) / bars.Count;
        var body = Math.Max(1, Math.Min(11, slot * 0.62));

        var size = frame?.Scale is { } scale
            ? Formatted($"width=\"{Number(ChartWidth * scale)}\" height=\"{Number(ChartHeight * scale)}\"")
            : Formatted($"width=\"{ChartWidth}\" height=\"{ChartHeight}\"");

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {ChartWidth} {ChartHeight}\" ");
        svg.Append(Invariant, $"{size} role=\"img\" class=\"level-chart\" data-ticker=\"{Escaped(ticker)}\" data-sessions=\"{bars.Count}\" ");
        svg.Append(Invariant, $"data-axis-low=\"{Number(axis.Low)}\" data-axis-high=\"{Number(axis.High)}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, {bars.Count} sessions from {bars[0].SessionDate:yyyy-MM-dd} to {bars[^1].SessionDate:yyyy-MM-dd}</title>");

        // Stated for a reader who cannot see the picture, and it says which
        // elements are here rather than describing the finished mark.
        var drawn = lines.Count > 0
            ? $"with {lines.Count} moving average(s) "
            : "with no moving average given ";

        var shaded = shading.Count > 0
            ? $"{shading.Count} level band(s) shaded behind them, "
            : "no level bands given, ";

        svg.Append(Invariant, $"<desc>Daily candles {drawn}over a volume pane on a shared time axis, with {shaded}support below the price and resistance above it.</desc>");

        // What the lines and the two hues are, above the picture rather than written
        // across it. Each average's swatch is drawn with that average's own stroke, so
        // the match is made by eye rather than by remembering an order.
        svg.Append("<g class=\"m-legend\">");

        double legendAt = Margin;

        for (var line = 0; line < lines.Count; line++)
        {
            var name = AverageName(lines[line].Name);

            svg.Append(Invariant, $"<line class=\"m-legend-swatch\" x1=\"{Number(legendAt)}\" y1=\"12\" x2=\"{Number(legendAt + 20)}\" y2=\"12\" ");
            svg.Append(Invariant, $"stroke=\"var(--ink, #1c1c1c)\" stroke-opacity=\"{Number(0.34 + (0.22 * Math.Min(line, 3)))}\" stroke-width=\"1.4\"/>");
            svg.Append(Invariant, $"<text class=\"m-legend-t\" x=\"{Number(legendAt + 26)}\" y=\"16\">{Escaped(name)}</text>");

            legendAt += 26 + (name.Length * LegendCharacter) + 22;
        }

        // The two hues, named where the words that used to name them inside the plot
        // can be read. Each entry is set out by the length of the one before it, as the
        // averages are, because a fixed step sets them at whatever the font measures.
        foreach (var (side, words) in new[] { ("sup", "nearest support"), ("res", "nearest resistance") })
        {
            if (!shading.Any(band => band.Immediate))
            {
                break;
            }

            svg.Append(Invariant, $"<text class=\"m-legend-t m-legend-{side}\" x=\"{Number(legendAt)}\" y=\"16\">{words}</text>");

            legendAt += (words.Length * LegendCharacter) + 22;
        }

        svg.Append("</g>");

        // Everything else sits below the legend, at the coordinates it is computed at,
        // so a price is placed against the axis and never against the row above it.
        svg.Append(Invariant, $"<g class=\"m-body\" transform=\"translate(0,{LegendRow})\">");

        svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"{Margin}\" y=\"0\" width=\"{Width - (2 * Margin)}\" height=\"{PriceHeight}\"/>");

        // The prices the right-hand column names: the close, and every band edge. Each
        // is a stored price, so the column states nothing the store does not hold, and
        // an edge of the nearest band on either side carries that side so the column
        // draws it in the band's own hue.
        var named = new List<(double Y, string Text, string? Nearest)>();

        // The bands first, so everything else reads on top of them. A band drawn
        // over the candles hides the price it is a statement about, which is the
        // one thing the picture exists to show.
        //
        // Full width, because a band is a price and not an event: it holds for
        // the whole chart rather than for the sessions that happened to touch it.
        if (shading.Count > 0)
        {
            svg.Append("<g class=\"level-bands\">");

            var labelled = new List<double>();

            foreach (var band in shading)
            {
                var top = At(axis, PlotValue(band.HighEdge));
                var bottom = At(axis, PlotValue(band.LowEdge));
                var support = band.Role == "support";
                var hue = support ? SupportHue : ResistanceHue;
                var side = support ? "sup" : "res";

                // A zero-width band is a real band: a single price with one
                // member. It becomes a rule rather than a rectangle nothing
                // draws, the same repair a zero-height candle body gets.
                var height = Math.Abs(bottom - top);

                svg.Append(Invariant, $"<rect class=\"level-band\" data-role=\"{Escaped(band.Role)}\" ");
                svg.Append(Invariant, $"data-low-edge=\"{band.LowEdge.ToString(Invariant)}\" data-high-edge=\"{band.HighEdge.ToString(Invariant)}\" ");
                svg.Append(Invariant, $"data-immediate=\"{(band.Immediate ? 1 : 0)}\" data-strength=\"{band.Strength}\" ");
                svg.Append(Invariant, $"x=\"{Margin}\" y=\"{Number(Math.Min(top, bottom))}\" width=\"{Width - (2 * Margin)}\" ");
                svg.Append(Invariant, $"height=\"{Number(Math.Max(height, 1))}\" fill=\"{hue}\" fill-opacity=\"{Number(band.Immediate ? 0.22 : 0.12)}\"/>");

                // The band's edges, so where a band starts and stops is a line and
                // not the fade of a tint.
                svg.Append(Invariant, $"<line class=\"m-edge-{side}\" x1=\"{Margin}\" y1=\"{Number(top)}\" x2=\"{Width - Margin}\" y2=\"{Number(top)}\"/>");
                svg.Append(Invariant, $"<line class=\"m-edge-{side}\" x1=\"{Margin}\" y1=\"{Number(bottom)}\" x2=\"{Width - Margin}\" y2=\"{Number(bottom)}\"/>");

                // The band in words is not written here. It was, at the plot's left
                // edge, where it sat over the oldest sessions and was crossed by its
                // own edge line; and hue is never the only channel, so the words are
                // where they can be read: the column on the right names every edge and
                // draws the nearest band's in that band's hue, the legend says which
                // hue is which, and the table beneath names every band with its role,
                // its strength and the sessions that reached it.
                named.Add((top, Price(band.HighEdge), band.Immediate ? side : null));

                if (band.LowEdge != band.HighEdge)
                {
                    named.Add((bottom, Price(band.LowEdge), band.Immediate ? side : null));
                }
            }

            svg.Append("</g>");
        }

        double Centre(int index) => Margin + (slot * index) + (slot / 2);

        // The averages are drawn before the candles so the price reads on top of
        // them. They carry no hue of their own: green and orange belong to
        // support and resistance on every screen, and a set of averages is a
        // magnitude rather than a set of categories, so it is one hue in steps
        // with the longer average darker, which is section 15.6's rule that
        // magnitude is one ramp and never a rainbow. That rule has no decision
        // behind it and is cited by section rather than by name, because the
        // architecture states it once with its reasoning and a decision would be
        // a second place holding one fact.
        // see: Support and resistance own two hues and nothing else uses them
        for (var line = 0; line < lines.Count; line++)
        {
            var average = lines[line];
            var shade = 0.34 + (0.22 * Math.Min(line, 3));

            svg.Append(Invariant, $"<g class=\"moving-average\" data-average=\"{Escaped(average.Name)}\" ");
            svg.Append(Invariant, $"data-values=\"{average.Values.Count(value => value is not null)}\">");

            // One path per unbroken run of values. A 200-day average has no
            // value for the first 199 sessions of a stored year, and joining
            // across an absence would draw a line over sessions the average did
            // not exist for.
            var run = new StringBuilder();

            for (var index = 0; index <= average.Values.Count; index++)
            {
                var value = index < average.Values.Count ? average.Values[index] : null;

                if (value is { } point)
                {
                    run.Append(run.Length == 0 ? 'M' : 'L')
                        .Append(Number(Centre(index)))
                        .Append(' ')
                        .Append(Number(At(axis, point)))
                        .Append(' ');

                    continue;
                }

                if (run.Length > 0)
                {
                    svg.Append(Invariant, $"<path d=\"{run.ToString().Trim()}\" fill=\"none\" ");
                    svg.Append(Invariant, $"stroke=\"var(--ink, #1c1c1c)\" stroke-opacity=\"{Number(shade)}\" stroke-width=\"1.4\"/>");
                    run.Clear();
                }
            }

            // The line is named in the legend above the picture rather than where it
            // ends. An average ends at the newest session, so a name written there sat
            // over the sessions a reader came to look at, and three of them ending
            // close together sat over each other as well.
            svg.Append("</g>");
        }

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var centre = Margin + (slot * index) + (slot / 2);

            double Y(decimal price) => At(axis, PlotValue(price));

            var top = Y(bar.High);
            var bottom = Y(bar.Low);
            var openY = Y(bar.Open);
            var closeY = Y(bar.Close);

            // Hollow for a close above the open and filled for below, in
            // neutral ink. Green and orange belong to support and resistance on
            // every screen and section 15.6 names a candle as the case it
            // forbids them in.
            // see: Support and resistance own two hues and nothing else uses them
            var rising = bar.Close > bar.Open;
            var fill = rising ? "none" : "var(--ink, #1c1c1c)";

            svg.Append(Invariant, $"<g class=\"candle\" data-session=\"{bar.SessionDate:yyyy-MM-dd}\">");
            svg.Append(Invariant, $"<line x1=\"{Number(centre)}\" y1=\"{Number(top)}\" x2=\"{Number(centre)}\" y2=\"{Number(bottom)}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>");

            // A session that opened and closed at one price has no body, and a
            // zero-height rectangle draws nothing, so it becomes a rule.
            var height = Math.Abs(openY - closeY);
            var left = centre - (body / 2);

            if (height < 1)
            {
                svg.Append(Invariant, $"<line x1=\"{Number(left)}\" y1=\"{Number(closeY)}\" x2=\"{Number(left + body)}\" y2=\"{Number(closeY)}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>");
            }
            else
            {
                svg.Append(Invariant, $"<rect x=\"{Number(left)}\" y=\"{Number(Math.Min(openY, closeY))}\" width=\"{Number(body)}\" height=\"{Number(height)}\" fill=\"{fill}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>");
            }

            svg.Append("</g>");
        }

        // The sessions a table beside the chart numbers, each marked with its number
        // above that session's candle.
        if (frame?.Markers is { Count: > 0 } markers)
        {
            for (var mark = 0; mark < markers.Count; mark++)
            {
                var at = -1;

                for (var index = 0; index < bars.Count; index++)
                {
                    if (bars[index].SessionDate == markers[mark].Session)
                    {
                        at = index;
                    }
                }

                if (at < 0)
                {
                    continue;
                }

                var y = Math.Max(10, At(axis, PlotValue(bars[at].High)) - 14);

                // Wrapped in a link to its own row and carrying what that row says, so a
                // circle answers what it is where it is drawn and reaches the rest.
                svg.Append(Invariant, $"<a href=\"{Escaped(markers[mark].Href)}\"><g class=\"move-mark\" data-session=\"{markers[mark].Session:yyyy-MM-dd}\">");
                svg.Append(Invariant, $"<title>{Escaped(markers[mark].Says)}</title>");
                svg.Append(Invariant, $"<circle class=\"m-mark\" cx=\"{Number(Centre(at))}\" cy=\"{Number(y)}\" r=\"9\"/>");
                svg.Append(Invariant, $"<text class=\"m-mark-t\" x=\"{Number(Centre(at))}\" y=\"{Number(y + 4)}\" text-anchor=\"middle\">{mark + 1}</text></g></a>");
            }
        }

        // The last close as a rule across the pane and a tag in the column, since it is
        // the price every band is read against.
        var now = At(axis, PlotValue(bars[^1].Close));

        svg.Append(Invariant, $"<line class=\"m-now\" x1=\"{Margin}\" y1=\"{Number(now)}\" x2=\"{Width - Margin}\" y2=\"{Number(now)}\" stroke-dasharray=\"4 3\"/>");

        svg.Append(Invariant, $"<g class=\"price-column\" data-close=\"{bars[^1].Close.ToString(Invariant)}\">");
        svg.Append(Invariant, $"<line class=\"m-axisline\" x1=\"{Width + 1}\" y1=\"0\" x2=\"{Width + 1}\" y2=\"{PriceHeight}\"/>");

        var placed = new List<double> { now };

        svg.Append(Invariant, $"<rect class=\"m-nowtag\" x=\"{Width + 4}\" y=\"{Number(now - 11)}\" width=\"{AxisWidth - 6}\" height=\"22\" rx=\"2\"/>");
        svg.Append(Invariant, $"<text class=\"m-nowtag-t\" x=\"{Width + 9}\" y=\"{Number(now + 5)}\">{Price(bars[^1].Close)}</text>");

        foreach (var (y, text, nearest) in named.OrderBy(price => Math.Abs(price.Y - now)))
        {
            if (y < 8 || y > PriceHeight - 4 || placed.Any(other => Math.Abs(other - y) < 18))
            {
                continue;
            }

            placed.Add(y);

            // An edge of the nearest band on either side is drawn in that band's hue,
            // which is what the words inside the plot used to say.
            var hue = nearest is null ? string.Empty : $" m-tick-{nearest}";

            svg.Append(Invariant, $"<line class=\"m-axisline\" x1=\"{Width + 1}\" y1=\"{Number(y)}\" x2=\"{Width + 5}\" y2=\"{Number(y)}\"/>");
            svg.Append(Invariant, $"<text class=\"m-tick{hue}\" x=\"{Width + 9}\" y=\"{Number(y + 5)}\">{Escaped(text)}</text>");
        }

        svg.Append("</g>");

        var volumeTop = PriceHeight + Gap;

        svg.Append(Invariant, $"<g class=\"volume-pane\" data-sessions=\"{bars.Count}\">");
        svg.Append(Invariant, $"<line x1=\"{Margin}\" y1=\"{volumeTop}\" x2=\"{Width - Margin}\" y2=\"{volumeTop}\" stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\"/>");

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var centre = Margin + (slot * index) + (slot / 2);
            var height = (double)bar.Volume / busiest * (VolumeHeight - Margin);

            svg.Append(Invariant, $"<rect class=\"volume\" data-session=\"{bar.SessionDate:yyyy-MM-dd}\" ");
            svg.Append(Invariant, $"x=\"{Number(centre - (body / 2))}\" y=\"{Number(volumeTop + VolumeHeight - Margin - height)}\" ");
            svg.Append(Invariant, $"width=\"{Number(body)}\" height=\"{Number(height)}\" fill=\"var(--muted, #6a6a6a)\"/>");
        }

        // In the gap above the bars rather than inside the pane, where it was drawn
        // across whichever sessions traded most.
        svg.Append(Invariant, $"<text class=\"m-cap\" x=\"{Margin}\" y=\"{volumeTop - 6}\">Volume, the tallest bar {loudest.ToString("N0", Invariant)} shares</text>");
        svg.Append("</g>");

        // The shared time axis, which is what makes the two panes one picture: the
        // first and last sessions and three between, each a stored session date.
        svg.Append("<g class=\"time-axis\">");

        var dateY = PriceHeight + Gap + VolumeHeight + DateRow - 3;
        var ticks = new[] { 0, bars.Count / 4, bars.Count / 2, bars.Count * 3 / 4, bars.Count - 1 }.Distinct().ToArray();

        foreach (var tick in ticks)
        {
            var anchor = tick == 0 ? "start" : tick == bars.Count - 1 ? "end" : "middle";
            var x = tick == 0 ? Margin : tick == bars.Count - 1 ? Width - Margin : Centre(tick);

            svg.Append(Invariant, $"<text class=\"m-tick\" x=\"{Number(x)}\" y=\"{dateY}\" text-anchor=\"{anchor}\">{bars[tick].SessionDate:yyyy-MM-dd}</text>");
        }

        svg.Append("</g>");

        svg.Append("</g>");
        svg.Append("</svg>");

        return svg.ToString();
    }

    // The chart's whole drawing: the price pane, the volume pane beneath it, a row of
    // dates, and the column on the right where the prices it is read against are named.
    const int AxisWidth = 86;
    const int DateRow = 16;

    // The row above the panes that names what the lines and the two hues are. It is
    // outside the plot because a name written inside one is written over the price it
    // is about, and the newest sessions, which is where an average ends, are the ones
    // a reader is looking at.
    const int LegendRow = 26;

    // What a character of the legend measures at the size the stylesheet sets it, taken
    // off the drawn row rather than assumed, so each entry is set out past the one before
    // it and two never sit on each other.
    const double LegendCharacter = 7.2;

    const int ChartWidth = Width + AxisWidth;
    const int ChartHeight = LegendRow + PriceHeight + Gap + VolumeHeight + DateRow;

    // An average's name as a reader says it.
    static string AverageName(string name) => name switch
    {
        "sma20" => "20-day average",
        "sma50" => "50-day average",
        "sma200" => "200-day average",
        _ => name,
    };

    // The line a name's page opens with where its stored series is suspect, section
    // 15.9's region from the 7.0 ruling.
    //
    // Above everything the page draws from those prices, since each of them is computed
    // over a series that may not carry a dividend's or a split's adjustment, and the page
    // draws them all the same rather than withholding them. The instant and the reason are
    // the row's, drawn as stored. A name whose series is trusted draws nothing here.
    // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
    public string PricesSuspect(string ticker, SuspectPrices? suspect) =>
        suspect is null
            ? string.Empty
            : $"<p class=\"prices-suspect\" data-ticker=\"{Escaped(ticker)}\" data-last-asked-at=\"{Escaped(suspect.LastAskedAt)}\">" +
              $"{Escaped(ticker)}'s prices may not reflect a recent dividend or split: downloading its year of prices again failed. " +
              $"Last tried {Escaped(suspect.LastAskedAt)}, because {Escaped(suspect.Reason)}. " +
              "The figures on this page are computed from the prices as stored.</p>";

    // The line a name's page opens with where the backfill asked for its year and none came
    // back: the nights it was asked for, and when it is asked next.
    // see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
    public string NoYearServed(string ticker, NoYear? noYear)
    {
        if (noYear is null)
        {
            return string.Empty;
        }

        var line = new StringBuilder();

        line.Append(Invariant, $"<p class=\"no-year\" data-ticker=\"{Escaped(ticker)}\" data-nights=\"{noYear.Nights}\">");
        line.Append(Invariant, $"No year of prices came back for {Escaped(ticker)}: the provider was asked for one on {noYear.Nights} night(s)");

        if (noYear.Last is { } last)
        {
            line.Append(Invariant, $", the last for the session of {last:yyyy-MM-dd}");
        }

        if (noYear.Next is { } next)
        {
            line.Append(Invariant, $", and it is asked again on the first night on or after {next:yyyy-MM-dd}.</p>");
        }
        else
        {
            line.Append(", and it is asked again on the next night.</p>");
        }

        return line.ToString();
    }

    // What a mark returns instead of a drawing. It states the count rather than
    // apologising, because the reader's next question is how many there were.
    // Why it is here, section 15.9's region that is present only when the name
    // is on tonight's list.
    //
    // Each reason in a full sentence rather than a label, with the values that
    // made it true. A label is what the list row shows; a sentence is what the
    // name page owes, because this is the page a reader acts from.
    // see: Every figure carries a plain-language key
    public string WhyItIsHere(string ticker, IReadOnlyList<FiredReason> reasons)
    {
        var why = new StringBuilder();

        why.Append(Invariant, $"<section class=\"why-it-is-here\" data-ticker=\"{Escaped(ticker)}\" data-reasons=\"{reasons.Count}\">");

        if (reasons.Count == 0)
        {
            why.Append("<p class=\"degraded\" data-listed=\"false\">this name is not on tonight's list</p></section>");

            return why.ToString();
        }

        foreach (var reason in reasons)
        {
            why.Append(Invariant, $"<p class=\"reason\" data-reason=\"{Escaped(reason.Name)}\">");
            why.Append(Invariant, $"{Escaped(Sentence(reason.Name))}");
            why.Append(Invariant, $" <span class=\"values\" data-values=\"{Escaped(string.Join(", ", reason.Values.Select(value => $"{value.Key} {value.Value}")))}\">");
            why.Append(Invariant, $"{Escaped(string.Join(", ", reason.Values.Select(value => $"{value.Key} {Figures.Read(value.Value)}")))}</span></p>");
        }

        why.Append("</section>");

        return why.ToString();
    }

    // Why a name the swing filter listed is on the list: each gate with why it passed, and the reasons
    // that fired on it as context, so the page states the rule that listed the evening.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    public string WhyItPassed(string ticker, FilterWhy why)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"why-it-is-here\" data-ticker=\"{Escaped(ticker)}\" data-rule=\"{ListRules.Filter}\" data-gates=\"{why.Gates.Count}\">");
        region.Append(Invariant, $"<p class=\"list-rule\">{Escaped(ticker)} was {Escaped(ListRules.ByFilter)} on {why.Evening:yyyy-MM-dd}.</p>");

        foreach (var gate in why.Gates)
        {
            region.Append(Invariant, $"<p class=\"gate\" data-gate=\"{Escaped(gate.Name)}\" data-passed=\"{(gate.Passed ? "true" : "false")}\"><b>{Escaped(gate.Name)}</b>: {Escaped(gate.Reason)}</p>");
        }

        region.Append(Invariant, $"<p class=\"context\" data-reasons=\"{why.Reasons.Count}\">");
        region.Append(why.Reasons.Count == 0
            ? "None of the six reasons fired on it that evening; they are context and no longer choose the list."
            : "As context, the reasons that fired on it that evening: " + Escaped(string.Join(", ", why.Reasons)) + ".");
        region.Append("</p></section>");

        return region.ToString();
    }

    // Why a name one gate short is close to a buy point: the gate it missed, what it had against the bar it
    // needed and how far short that is, and the trade its plan states, beneath the line that it is not a pick.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public string WhyItIsClose(string ticker, DateOnly evening, EquityBrief.Core.Filter.MissedGate missed)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"why-it-is-here\" data-ticker=\"{Escaped(ticker)}\" data-rule=\"{ListRules.Filter}\" data-missed=\"{Escaped(missed.Gate)}\" data-distance=\"{missed.Distance.ToString("R", Invariant)}\">");
        region.Append(Invariant, $"<p class=\"list-rule\">{Escaped(ticker)} passed every gate of the swing filter on {evening:yyyy-MM-dd} but one, and nothing excluded it, so it is close to a buy point and not on the list.</p>");
        region.Append(Formatted($"<p class=\"gate\" data-gate=\"{Escaped(missed.Gate)}\" data-passed=\"false\"><b>{Escaped(missed.Gate)}</b>: {Escaped(missed.Words)}; {missed.Distance * 100:0}% short of its bar.</p>"));

        if (missed.Trade is { } trade)
        {
            region.Append(Invariant, $"<p class=\"missed-trade\">The trade its plan states: {Escaped(trade)}.</p>");
        }

        region.Append("<p class=\"context\">It is not a pick: it did not pass, and nothing counts it as one.</p></section>");

        return region.ToString();
    }

    // The list from night to night, the run page's overlap.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    public string Overlap(OverlapView? overlap)
    {
        if (overlap is null)
        {
            return "<section class=\"overlap\" data-names=\"none\"><p class=\"degraded\">no list is stored for the night</p></section>";
        }

        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"overlap\" data-names=\"{overlap.Names}\" data-last-night=\"{overlap.OnLastNight}\" data-five=\"{overlap.OnFive}\" data-twenty=\"{overlap.OnTwenty}\">");

        if (overlap.Names == 0)
        {
            region.Append(Invariant, $"<p class=\"degraded\">No name is on the list on {overlap.Night:yyyy-MM-dd}, so nothing carries over from the evenings before.</p></section>");

            return region.ToString();
        }

        region.Append(Invariant, $"<p>Of the {overlap.Names} name(s) on the list on {overlap.Night:yyyy-MM-dd}: ");
        region.Append(overlap.LastNight is { } before
            ? "on the list the evening before, " + before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", " + overlap.OnLastNight.ToString(CultureInfo.InvariantCulture) + "; "
            : "on the list the evening before, none, since the store holds no evening before it; ");
        region.Append(Invariant, $"on it at least once over the last {overlap.FiveHeld} evening(s) before it, {overlap.OnFive}; over the last {overlap.TwentyHeld}, {overlap.OnTwenty}");
        region.Append(overlap.TwentyHeld < 20
            ? Formatted($", the store holding {overlap.TwentyHeld} evening(s) before it.</p></section>")
            : ".</p></section>");

        return region.ToString();
    }

    // Each reason as a sentence. Every reason the shortlist carries has its own
    // arm and anything else throws, for the reason the plan column's mapping
    // does: a sentence a reader acts on that was produced by a value nobody
    // wrote is what a catch-all arm makes invisible.
    static string Sentence(string reason) => reason switch
    {
        "at entry zone" => "tonight's close is inside a tranche zone, so the plan's first step is available at tonight's price.",
        "crossed a level" => "the close moved through a band edge it was on the other side of yesterday, so the level either held or failed today.",
        "breakout on volume" => "the close is above a band that sat at or above last night's close, on volume above the fifty-day average.",
        "trend state changed" => "tonight's trend label differs from last night's, so the ladder changes shape and the whole plan is different from yesterday's.",
        "unusual volume" => "volume is above twice the fifty-day average, so something happened the price may not have shown yet.",
        "earnings soon" => "the next dated event is inside the twenty-session horizon, which is a calendar fact rather than a setup.",
        _ => throw new InvalidOperationException(
            $"The stored listing carries the reason '{reason}', which this mapping has no sentence for. " +
            "A sentence a reader acts on that was produced by a value nobody wrote is what a catch-all arm " +
            "makes invisible, so the page fails rather than rendering a default."),
    };

    // The contents, section 15.9's head: every card the page drew, in the order it drew them
    // and numbered from where a reader starts, each a link to the card itself.
    //
    // Built from what was drawn rather than from a roster kept beside the page, so a card
    // added without an entry cannot happen and an entry naming a card the page does not carry
    // cannot be written. A name holding no research draws a shorter contents for that reason
    // rather than links to sections that are not there.
    public string Contents(string ticker, IReadOnlyList<ContentsEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var nav = new StringBuilder();

        nav.Append(Invariant, $"<nav class=\"contents\" aria-label=\"What is on this page\" data-ticker=\"{Escaped(ticker)}\" data-entries=\"{entries.Count}\"><ol>");

        foreach (var entry in entries)
        {
            nav.Append(Invariant, $"<li><a href=\"#{Escaped(entry.Id)}\"><span class=\"c-n\">{entry.At}</span>{Escaped(entry.Title)}</a></li>");
        }

        nav.Append("</ol></nav>");

        return nav.ToString();
    }

    // The walk, section 15.9's last region: previous and next on tonight's list,
    // so an evening's reading is one pass through with no return to the list.
    //
    // A name that is not on the list has no neighbours and says so, rather than
    // linking to the ends of a list it is not in.
    //
    // A page about an earlier night walks that night's list: each neighbour is linked on the
    // night the page is about, so a pass through an evening stays in that evening.
    public string Walk(string ticker, string? previous, string? next, DateOnly? night = null)
    {
        var walk = new StringBuilder();
        var on = night is { } evening ? FormattableString.Invariant($"/{evening:yyyy-MM-dd}") : string.Empty;
        var list = night is null ? "tonight's list" : "that evening's list";

        walk.Append(Invariant, $"<nav class=\"walk\" data-ticker=\"{Escaped(ticker)}\" ");
        walk.Append(Invariant, $"data-previous=\"{Escaped(previous ?? "none")}\" data-next=\"{Escaped(next ?? "none")}\">");

        if (previous is null)
        {
            walk.Append(Invariant, $"<span class=\"degraded\">no previous name on {list}</span>");
        }
        else
        {
            walk.Append(Invariant, $"<a href=\"#/name/{Escaped(previous)}{on}\">previous: {Escaped(previous)}</a>");
        }

        if (next is null)
        {
            walk.Append(Invariant, $"<span class=\"degraded\">no next name on {list}</span>");
        }
        else
        {
            walk.Append(Invariant, $"<a href=\"#/name/{Escaped(next)}{on}\">next: {Escaped(next)}</a>");
        }

        walk.Append("</nav>");

        return walk.ToString();
    }


    // The listing strip, section 15.5's mark: the evenings a name was on the
    // list over a window.
    //
    // It says nothing about index membership, which every name in the table it
    // sits in has by definition. That sentence is in section 15.8's note because
    // the columns were misread that way once.
    public string ListingStrip(string ticker, IReadOnlyList<bool> evenings)
    {
        const int Cell = 4;
        const int Height = 14;

        var strip = new StringBuilder();

        strip.Append(Invariant, $"<svg class=\"listing-strip\" role=\"img\" viewBox=\"0 0 {Math.Max(evenings.Count, 1) * Cell} {Height}\" ");
        strip.Append(Invariant, $"width=\"{Math.Max(evenings.Count, 1) * Cell}\" height=\"{Height}\" ");
        strip.Append(Invariant, $"data-ticker=\"{Escaped(ticker)}\" data-evenings=\"{evenings.Count}\" ");
        strip.Append(Invariant, $"data-listed=\"{evenings.Count(listed => listed)}\">");

        for (var at = 0; at < evenings.Count; at++)
        {
            // A listed evening is inked and a quiet one is a rule, so the strip
            // reads as a pattern rather than as two colours a reader has to
            // learn. Hue is never the only channel that carries a meaning.
            if (evenings[at])
            {
                strip.Append(Invariant, $"<rect x=\"{at * Cell}\" y=\"2\" width=\"{Cell - 1}\" height=\"{Height - 4}\" fill=\"var(--ink, #1c1c1c)\" />");
            }
            else
            {
                strip.Append(Invariant, $"<rect x=\"{at * Cell}\" y=\"{Height / 2}\" width=\"{Cell - 1}\" height=\"1\" fill=\"var(--rule, #d8d8d8)\" />");
            }
        }

        strip.Append(Invariant, $"<title>{Escaped(ticker)}: on the list on {evenings.Count(listed => listed)} of {evenings.Count} evening(s)</title>");
        strip.Append("</svg>");

        return strip.ToString();
    }

    // What a row with no reward to risk says where it carries no reason of its own.
    public const string NoRewardToRiskStated = "the plan states no reward to risk";

    // Tonight's list, section 15.7's third region.
    //
    // One row per name on the list, in the order the rows arrive in, at most
    // twenty drawn, each numbered by its place in that order. The true count is
    // the header's headline, because a page that shows twenty every night cannot
    // tell you how busy the night was, and the line above the rows states it again
    // beside how many are drawn, because a list that does not say how long it is
    // cannot tell a reader which of its rows they are on.
    // see: The page shows twenty and states the true count
    //
    // Drawn as "Close to a buy point" it holds the members one gate short, each with the gate it missed in
    // place of the gates it passed, beneath a count of its own, and on a night the market gate closed its
    // rows beneath one line saying they would qualify if the market turned.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public string TonightList(
        IReadOnlyList<ListingCell> rows,
        int drawn,
        IReadOnlyList<ReasonRecord>? records = null,
        ListRuleView? rule = null,
        bool oneGateShort = false)
    {
        var shown = rows.Take(drawn).ToArray();
        var list = new StringBuilder();
        var byFilter = oneGateShort || rule is { Rule: ListRules.Filter };

        list.Append(Invariant, $"<section class=\"tonight-list{(oneGateShort ? " close-list" : string.Empty)}\" data-list=\"{(oneGateShort ? "close" : "listed")}\" data-fired=\"{rows.Count}\" data-drawn=\"{shown.Length}\" data-rule=\"{Escaped(rule?.Rule ?? ListRules.Reasons)}\">");

        // The rule the evening was listed by, so a row read from an evening before the switch is not
        // taken for one the swing filter drew.
        // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
        if (!oneGateShort)
        {
            list.Append(Invariant, $"<p class=\"list-rule\" data-rule=\"{Escaped(rule?.Rule ?? ListRules.Reasons)}\">This evening was {Escaped(ListRules.EveningSaid(rule?.Rule ?? ListRules.Reasons))}.</p>");
        }

        // A closed market fails every member at its first gate, so every row here missed the market alone, and
        // what it needed is said once above them rather than on each.
        var marketClosed = oneGateShort && rule is { MarketOpen: false };

        if (marketClosed && rows.Count > 0)
        {
            list.Append(Invariant, $"<p class=\"market-turned\" data-market=\"closed\" data-breadth=\"{(rule!.Breadth is { } share ? share.ToString("R", Invariant) : "none")}\">");
            list.Append(rule.Breadth is { } breadth && rule.Floor is { } floor
                ? Formatted($"The market gate closed tonight, so these would qualify if the market turned: {breadth * 100:0.0}% of the members closed above their 200-day average, and it needs {floor * 100:0.#}%.")
                : "The market gate closed tonight, so these would qualify if the market turned; the night's breadth is not available.");
            list.Append("</p>");
        }

        if (!oneGateShort && byFilter && rule is { MarketOpen: false } closed)
        {
            list.Append(Invariant, $"<p class=\"degraded\" data-market=\"closed\" data-breadth=\"{(closed.Breadth is { } share ? share.ToString("R", Invariant) : "none")}\">");
            list.Append(closed.Breadth is { } breadth && closed.Floor is { } floor
                ? Formatted($"The market gate closed tonight: {breadth * 100:0.0}% of the members closed above their 200-day average, below its floor of {floor * 100:0.#}%, so no name is listed.")
                : "The market gate closed tonight: the night's breadth is not available, so no name is listed.");
            list.Append("</p></section>");

            return list.ToString();
        }

        if (oneGateShort && rows.Count == 0)
        {
            list.Append("<p class=\"degraded\" data-close=\"0\">No member missed exactly one gate tonight with nothing excluding it.</p></section>");

            return list.ToString();
        }

        if (rows.Count == 0)
        {
            list.Append(byFilter && rule is { Reached.Count: 4 } none
                ? Formatted($"<p class=\"degraded\" data-fired=\"0\" data-passed=\"0\">No name passed the swing filter tonight: {none.Reached[0]} passed the trend and strength gate, {none.Reached[1]} the setup, {none.Reached[2]} the trigger and {none.Reached[3]} the trade, and none of those past the exclusions.</p></section>")
                : "<p class=\"degraded\" data-fired=\"0\">no name fired a reason tonight</p></section>");

            return list.ToString();
        }

        // One column per reason, always in the same place, so an evening that is one thing
        // happening to many names reads as one dark stripe down one column. A reason the
        // store holds that is not one of the six still gets a column of its own after them.
        var columns = ShortlistSeries.Reasons
            .Concat(shown.SelectMany(row => row.Reasons).Where(reason => !ShortlistSeries.Reasons.Contains(reason, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var byReason = records?.ToDictionary(record => record.Reason, StringComparer.Ordinal);

        // How many rows the list draws of how many fired, above the rows it counts, and where
        // the rows leave a name out, where every name is.
        list.Append(Invariant, $"<p class=\"list-count\" data-drawn=\"{shown.Length}\" data-undrawn=\"{rows.Count - shown.Length}\">");
        var named = oneGateShort ? "one gate short" : byFilter ? "the swing filter listed" : "that fired";

        // The two lists share twenty rows, the first list's drawn first, so a night whose first list fills
        // them draws none of this one and says how many it holds.
        list.Append(shown.Length == 0
            ? Formatted($"The list above fills every row the page draws, so none of the {rows.Count} names {named} is drawn. <a href=\"#/universe\">See every name on the universe page</a>")
            : rows.Count > shown.Length
            ? Formatted($"Showing {shown.Length} of the {rows.Count} names {named}. <a href=\"#/universe\">See every name on the universe page</a>")
            : rows.Count == 1
                ? $"Showing the one name {named}."
                : Formatted($"Showing all {rows.Count} names {named}."));
        list.Append("</p>");

        if (shown.Length == 0)
        {
            list.Append("</section>");

            return list.ToString();
        }

        list.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table\" data-rows=\"{shown.Length}\">");
        // Each heading carries what its column holds, a reason's column under its short name with its
        // full name in the sentence.
        list.Append("<thead><tr>");

        foreach (var (heading, says) in TonightHeadings(byFilter, oneGateShort))
        {
            list.Append(TippedHeading(heading, says, heading switch
            {
                "#" => "place",
                "Close" or "Day" or "Reward to risk" => "r",
                "Distance to levels" => "c",
                _ => null,
            }));
        }

        foreach (var column in columns)
        {
            list.Append(TippedHeading(column, ReasonSays(column), "rz", Head(column)));
        }

        list.Append("</tr></thead><tbody>");

        foreach (var (row, place) in shown.Select((row, at) => (row, at + 1)))
        {
            // The row is what selects it, anywhere on it but its links. Section 15.7's selected-name
            // region is for whichever row is selected, and a row a reader cannot select is a row the
            // region can never be about. The address it selects carries the night as well as the name,
            // so a selected view of an earlier night is a link like every other view.
            // see: Selecting a row draws its plan beneath the list and is no navigation
            list.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-fired-count=\"{row.FiredCount}\" data-strength=\"{row.Strength}\" ");
            list.Append(Invariant, $"data-day-change=\"{Change(row.DayChangePct)}\" data-trend-state=\"{Escaped(row.TrendState ?? NotClassified)}\" ");
            list.Append(Invariant, $"data-selects=\"{Escaped(row.Ticker)}\" data-select-href=\"#/night/{row.SessionDate:yyyy-MM-dd}?name={Uri.EscapeDataString(row.Ticker)}\">");

            // The row's place in the order the rows are drawn in, counted from one.
            list.Append(Invariant, $"<td class=\"place\" data-place=\"{place}\">{place}</td>");

            // The name, a link opening the name's own page, which is what a reader following a ticker
            // expects.
            list.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>");
            // What the link opens is a report where one was written and the name's
            // page where none was, so it is drawn with the words of whichever it is:
            // a link calling itself a report for a name holding none is the page
            // promising something it does not have.
            // see: A researched name is one holding an accepted section besides the key under each figure
            var researched = row.ResearchedOn is not null;

            list.Append(Invariant, $" <a class=\"open{(researched ? string.Empty : " unwritten")}\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\" ");
            list.Append(Invariant, $"data-report-state=\"{ReportState(row)}\" data-researched=\"{(researched ? "true" : "false")}\"");

            if (row.ResearchedOn is { } on)
            {
                list.Append(Invariant, $" data-researched-on=\"{on:yyyy-MM-dd}\" title=\"open the report, written {on:yyyy-MM-dd}\">report</a>");
            }
            else
            {
                list.Append(" title=\"open the name, whose researched sections are not written\">not written</a>");

                // Asked for from the row, which is where the operator is when they see the
                // name holds none and the queue holds nothing for it. It writes a request and
                // starts the worker's drain, and a row whose name is already waiting is refused
                // by the store rather than by the page reading the queue first and racing itself.
                // see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
                if (row.Queue is null)
                {
                    list.Append(AskForAReport(row.Ticker));
                }
            }

            // What the queue holds for the name, beside the report it holds or in place of the
            // control asking for one, so a name already queued or being written says when.
            // see: The queue page states when each request will be written
            if (row.Queue is { } queued)
            {
                list.Append(Invariant, $"<span class=\"report-state\" data-report-state=\"{Escaped(queued.State)}\" data-at=\"{Escaped(queued.At ?? string.Empty)}\">{Escaped(queued.Words)}</span>");
            }

            if (row.Distance?.Name is { Length: > 0 } company)
            {
                list.Append(Invariant, $"<span class=\"co\">{Escaped(company)}</span>");
            }

            // Beside the name, where its stored series is suspect, so a row read from the
            // list does not pass for one whose prices carry every action's adjustment. The
            // instant and the reason are the name page's to state in full; the row carries
            // them as its title.
            // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
            list.Append(row.Suspect is { } suspect
                ? $" <span class=\"prices-suspect\" data-last-asked-at=\"{Escaped(suspect.LastAskedAt)}\" title=\"last tried {Escaped(suspect.LastAskedAt)}, because {Escaped(suspect.Reason)}\">prices may not reflect a dividend or split</span></td>"
                : "</td>");

            list.Append(Invariant, $"<td class=\"r num\">{(row.Close is { } close ? close.ToString(Invariant) : "not computed")}</td>");

            // The day's change, signed and in words as well as by its sign. The
            // two hues are support's and resistance's and a day is not allowed
            // either of them, so the direction is the sign on the number and
            // nothing else carries it.
            // see: Support and resistance own two hues and nothing else uses them
            list.Append(Invariant, $"<td class=\"day-change\">{ChangeReads(row.DayChangePct, row.Close)}</td>");

            // The trend state in a word, read off the ladder row rather than
            // worked out here, and a name with no row says so rather than
            // showing an empty cell. Beside it, the state the member's reported
            // quarters gave it on the night, with what its readings say.
            list.Append(Invariant, $"<td class=\"trend-state\">{Escaped((row.TrendState ?? NotClassified).Replace('_', ' '))}{BusinessWord(row.Business)}</td>");

            // The distance row mark, the same mark the universe table draws, so
            // a shape means one thing on both screens.
            list.Append(Invariant, $"<td class=\"c\">{(row.Distance is { } cell ? DistanceRow(cell) : "<span class=\"degraded\" data-distance=\"none\">no bands stored for this name</span>")}</td>");

            // The figure that breaks a tie in the fired count, and where the plan computes none the
            // plan's own words for why, so a row drawn after its neighbours says what put it there.
            // It is a fact about the chart and not a probability of anything.
            // see: Tonight's list breaks a tie in fired count by the plan's reward to risk, and a row with none is drawn after every row with one and says why
            list.Append(row.RewardToRisk is { } ratio
                ? Formatted($"<td class=\"r num\" data-reward-to-risk=\"{ratio.ToString(Invariant)}\">{Figures.Ratio(ratio)}</td>")
                : $"<td class=\"reward-to-risk\"><span class=\"degraded\" data-reward-to-risk=\"none\">{Escaped(row.NoRewardToRisk ?? NoRewardToRiskStated)}</span></td>");

            // The gates that put the row on the list, each with why, the setup's family, the session its
            // trigger arrived on and the trade the gate read, its figures to the hundredth as the reward to
            // risk column and the name page draw them rather than as the gate stored them.
            // On the second list, the one gate the row missed in place of the gates, with what it had against
            // the bar it needed, how far short that is as a share of the bar, and the trade its plan states.
            // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
            if (oneGateShort)
            {
                list.Append(row.Missed is { } missed
                    ? Formatted($"<td class=\"missed\" data-gate=\"{Escaped(missed.Gate)}\" data-distance=\"{missed.Distance.ToString("R", Invariant)}\"><b>{Escaped(missed.Gate)}</b>{(marketClosed && missed.Gate == "market" ? string.Empty : ": " + Escaped(missed.Words))}; {missed.Distance * 100:0}% short")
                        + (missed.Trade is { } trade ? $"<span class=\"missed-trade\">{Escaped(trade)}</span>" : string.Empty)
                        + "</td>"
                    : "<td class=\"missed\"><span class=\"degraded\">no gate result stored</span></td>");
            }
            else if (byFilter)
            {
                list.Append(row.Filter is { } gates
                    ? Formatted($"<td class=\"gates\" data-rank=\"{gates.Rank}\" data-family=\"{Escaped(gates.Family ?? "none")}\" data-arrived=\"{Escaped(gates.Arrived)}\" data-input=\"{Escaped(gates.Input)}\" title=\"{Escaped(string.Join("; ", gates.Gates.Select(gate => gate.Name + ": " + gate.Reason)))}\">{Escaped(gates.Family ?? "no family")}, arrived {Escaped(gates.Arrived)}; {Escaped(PlanWords(gates.Input))}, reward to risk {Hundredths(gates.RewardToRisk)}, its stop {Hundredths(gates.StopMoves)} typical moves below the entry</td>")
                    : "<td class=\"gates\"><span class=\"degraded\">no gate result stored</span></td>");
            }

            list.Append(ReasonsForRow(row, columns, byReason));
            list.Append("</tr>");
        }

        list.Append("</tbody>");

        // Each reason's record once, at the foot of its own column: a property of the
        // reason across every name it has fired for, and never of a row's name.
        // see: A reason's record is displayed, beside the reason and never beside the name
        if (byReason is not null)
        {
            list.Append(Invariant, $"<tfoot><tr><td colspan=\"{(byFilter ? 8 : 7)}\" class=\"rec-lab\">Each reason's record across every name it has fired for. ");
            list.Append("Solid: the share that reached target before stop, of how many resolved, against the break-even they needed. ");
            list.Append("Dashed: not enough setups have finished to say anything yet, shown as how many have finished against the number needed.</td>");

            foreach (var column in columns)
            {
                list.Append("<td class=\"rz\">");

                if (byReason.TryGetValue(column, out var record))
                {
                    list.Append(Invariant, $"<span class=\"reason\" data-reason=\"{Escaped(column)}\">");
                    list.Append(record.HasEarnedAVerdict
                        ? Formatted($"<span class=\"record-foot\" data-verdict=\"{VerdictWord(record)}\"><b>{Number(record.Share ?? 0)}%</b> of {record.Scored}, needs {Number(record.BreakEven ?? 0)}%</span>")
                        : Formatted($"<span class=\"record-foot not-measured\" data-outline=\"dashed\">{record.Scored} of {record.Minimum}</span>"));
                    list.Append("</span>");
                }

                list.Append("</td>");
            }

            list.Append("</tr></tfoot>");
        }

        list.Append("</table></div>");
        list.Append("</section>");

        return list.ToString();
    }

    // Which of the four states a name's report is in: being written or queued where the queue
    // holds a request for it, and otherwise written or not written by whether it holds one.
    public static string ReportState(ListingCell row) =>
        row.Queue?.State ?? (row.ResearchedOn is not null ? "written" : "unwritten");

    // The control asking for a report on a name holding none, as tonight's list draws it on
    // a row and in the selected name's region, both on the list's page. It writes a request
    // and starts the worker's drain.
    // see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
    public static string AskForAReport(string ticker) =>
        Formatted($"<form class=\"research-control ask\" method=\"post\" action=\"/passes/{Uri.EscapeDataString(ticker)}\" ")
        + Formatted($"data-kind=\"ask\" data-asks=\"{Escaped(ticker)}\" data-from=\"list\">")
        + "<input type=\"hidden\" name=\"from\" value=\"list\">"
        + "<button type=\"submit\" title=\"put this name in the queue and start the worker on it\">ask for a report</button></form>";

    // A reason's column head, one word, with the reason's full name on the head's own
    // title.
    static string Head(string reason) => reason switch
    {
        ShortlistSeries.AtEntryZone => "entry",
        ShortlistSeries.CrossedALevel => "crossed",
        ShortlistSeries.BreakoutOnVolume => "breakout",
        ShortlistSeries.TrendStateChanged => "trend",
        ShortlistSeries.UnusualVolume => "volume",
        ShortlistSeries.EarningsSoon => "earnings",
        _ => reason,
    };

    // The reasons on one row of tonight's list, section 15.7's fourth region: a cell
    // per reason column, holding the reason where it fired and nothing where it did not.
    //
    // Each reason named, with its measured record beside it under 15.11 and the
    // values that made it true on hover. The record is the reason's and not the
    // name's: it says how this reason has done across every name it ever fired
    // for, and it is not a statement about the name in this row. That is why it
    // is drawn inside the reason's own span rather than in a column of its own,
    // where a reader would take it for a property of the row.
    // see: A reason's record is displayed, beside the reason and never beside the name
    static string ReasonsForRow(ListingCell row, IReadOnlyList<string> columns, IReadOnlyDictionary<string, ReasonRecord>? byReason)
    {
        var cells = new StringBuilder();

        // The values arrive with `Fired` and the names without it, so a caller
        // that has only the names still draws the reasons rather than nothing.
        var fired = row.Fired
            ?? [.. row.Reasons.Select(name => new FiredReason(name, new Dictionary<string, string>(StringComparer.Ordinal)))];

        foreach (var column in columns)
        {
            if (fired.FirstOrDefault(reason => string.Equals(reason.Name, column, StringComparison.Ordinal)) is not { } reason)
            {
                cells.Append("<td class=\"rz\"></td>");

                continue;
            }

            cells.Append(Invariant, $"<td class=\"rz\"><div class=\"reason\" data-reason=\"{Escaped(reason.Name)}\" tabindex=\"0\">");
            cells.Append(Invariant, $"<span class=\"r-head\">{Escaped(Head(reason.Name))}</span><div class=\"why\">");
            cells.Append(Invariant, $"<p class=\"why-fired\">{Escaped(reason.Name)}</p>");

            // What the night measured this reason over, drawn as the page's own markup rather
            // than held in an attribute, which put it in a native tooltip a touch screen cannot
            // reach. The values are the strings the reason wrote, so they are drawn as stored
            // and no figure on the row is rounded twice.
            // see: A figure is drawn at the places it is read at, and its element carries the stored value whole
            if (reason.Values.Count == 0)
            {
                cells.Append("<p class=\"no-values\">no values stored for this reason</p>");
            }
            else
            {
                cells.Append(Invariant, $"<dl class=\"reason-values\" data-values=\"{reason.Values.Count}\">");

                foreach (var value in reason.Values.OrderBy(one => one.Key, StringComparer.Ordinal))
                {
                    cells.Append(Invariant, $"<dt>{Escaped(value.Key)}</dt><dd>{Escaped(value.Value)}</dd>");
                }

                cells.Append("</dl>");
            }

            if (byReason is not null && byReason.TryGetValue(reason.Name, out var record))
            {
                // 15.11's two states on this surface as on the run page: the share, the
                // count and the bar in one span, or the count against the floor that is short.
                cells.Append(record.HasEarnedAVerdict
                    ? Formatted($"<span class=\"record\" data-verdict=\"{VerdictWord(record)}\" data-share=\"{Number(record.Share ?? 0)}\" data-scored=\"{record.Scored}\" data-break-even=\"{Number(record.BreakEven ?? 0)}\">{ShareOfTheScored(record)}</span>")
                    : Formatted($"<span class=\"record not-measured\" data-outline=\"dashed\" data-verdict=\"none\" data-short=\"{record.Withheld}\" data-scored=\"{record.Scored}\" data-minimum=\"{record.Minimum}\">{CountAgainstTheFloors(record)}</span>"));
            }

            cells.Append("</div></div></td>");
        }

        return cells.ToString();
    }

    // The reason track, section 15.5's sixth mark.
    //
    // Per reason, out of one denominator: setups resolved as a win, resolved as
    // a loss, and unresolved. The third state is a dashed outline and never a
    // third colour, because unresolved is not a smaller amount of losing and
    // must not read as one.
    // see: Not yet measured is drawn as a dashed outline, never as a pale value
    //
    // One denominator means one across the whole mark rather than one per row.
    // Scaled per row every bar would be full width and the mark would say
    // nothing about which reason fired on more names, which is the question it
    // exists to answer.
    //
    // Hue is not a channel here at all. The three states are one ink at two
    // steps plus an outline, and each row carries its counts in words on its own
    // title, so a reader who cannot separate two greys loses nothing.
    // see: Support and resistance own two hues and nothing else uses them
    public string ReasonTrack(IReadOnlyList<ReasonTrackRow> rows)
    {
        const int Row = 18;
        const int Width = 220;
        const int Label = 4;

        var most = rows.Count == 0 ? 0 : rows.Max(row => row.Total);
        var height = Math.Max(rows.Count, 1) * Row;
        var track = new StringBuilder();

        track.Append(Invariant, $"<svg class=\"reason-track\" role=\"img\" viewBox=\"0 0 {Width} {height}\" ");
        track.Append(Invariant, $"width=\"{Width}\" height=\"{height}\" data-reasons=\"{rows.Count}\" data-denominator=\"{most}\">");

        for (var at = 0; at < rows.Count; at++)
        {
            var row = rows[at];
            var top = (at * Row) + 3;

            track.Append(Invariant, $"<g data-reason=\"{Escaped(row.Reason)}\" data-total=\"{row.Total}\" ");
            track.Append(Invariant, $"data-won=\"{row.Won}\" data-lost=\"{row.Lost}\" ");
            track.Append(Invariant, $"data-resolved-unsplit=\"{row.ResolvedUnsplit}\" data-unresolved=\"{row.Unresolved}\">");

            // A reason that fired on nothing draws its rule rather than nothing,
            // so a reason that never fires is visible as one that never fires
            // rather than absent from the picture.
            if (row.Total == 0 || most == 0)
            {
                track.Append(Invariant, $"<rect x=\"{Label}\" y=\"{top + (Row / 2)}\" width=\"{Width - Label - 2}\" height=\"1\" fill=\"var(--rule, #d8d8d8)\" />");
            }

            var span = most == 0 ? 0d : (double)(Width - Label - 2) / most;
            var left = (double)Label;

            left = Segment(track, "won", row.Won, left, top, span, "var(--ink, #1c1c1c)", 1);
            left = Segment(track, "lost", row.Lost, left, top, span, "var(--ink, #1c1c1c)", 0.45);
            left = Segment(track, "resolved", row.ResolvedUnsplit, left, top, span, "var(--ink, #1c1c1c)", 0.7);

            // The unresolved segment, drawn as an outline with nothing inside
            // it. Its own segment of the track and never folded into the rate.
            if (row.Unresolved > 0 && span > 0)
            {
                track.Append(Invariant, $"<rect data-state=\"unresolved\" data-count=\"{row.Unresolved}\" ");
                track.Append(Invariant, $"x=\"{left:0.##}\" y=\"{top}\" width=\"{row.Unresolved * span:0.##}\" height=\"{Row - 6}\" ");
                track.Append(Invariant, $"fill=\"none\" stroke=\"var(--ink, #1c1c1c)\" stroke-dasharray=\"3 2\" />");
            }

            track.Append(Invariant, $"<title>{Escaped(row.Reason)}: {Words(row)}</title>");
            track.Append("</g>");
        }

        track.Append("</svg>");

        return track.ToString();
    }

    static double Segment(
        StringBuilder track,
        string state,
        int count,
        double left,
        int top,
        double span,
        string fill,
        double opacity)
    {
        const int Row = 18;

        if (count <= 0 || span <= 0)
        {
            return left;
        }

        track.Append(Invariant, $"<rect data-state=\"{state}\" data-count=\"{count}\" ");
        track.Append(Invariant, $"x=\"{left:0.##}\" y=\"{top}\" width=\"{count * span:0.##}\" height=\"{Row - 6}\" ");
        track.Append(Invariant, $"fill=\"{fill}\" fill-opacity=\"{Number(opacity)}\" />");

        return left + (count * span);
    }

    // The counts in words, so the picture is never the only channel.
    static string Words(ReasonTrackRow row) =>
        row.Total == 0
            ? "no setup on this reason yet"
            : row.ResolvedUnsplit > 0
                ? Formatted($"{row.ResolvedUnsplit} resolved and {row.Unresolved} unresolved, of {row.Total}")
                : Formatted($"{row.Won} won, {row.Lost} lost and {row.Unresolved} unresolved, of {row.Total}");

    // The reasons whose records stand on fewer of the nights than the listings do, each
    // group with its own count, so the line above the table is true of every row beneath it.
    static string FewerNights(IReadOnlyList<ReasonRecord> records, int nights) =>
        string.Concat(records
            .Where(record => record.Nights is { } own && own < nights)
            .GroupBy(record => record.Nights!.Value)
            .OrderByDescending(group => group.Key)
            .Select(group => Formatted(
                $", and {Escaped(string.Join(" and ", group.Select(record => record.Reason)))} on {group.Key} of them, {FewerNightsText}")));

    public const string FewerNightsText =
        "the rows written before a correction to a reason's rule not counting toward its record";

    // The reason records, section 15.10's second region, in the state this build
    // is in for its first year.
    //
    // A reason below the minimum shows a dashed outline carrying its resolved
    // count against that minimum, and no rate. A pale number reads as a small
    // one, and a rate over a handful of cases reads as evidence and is not.
    // see: Not yet measured is drawn as a dashed outline, never as a pale value
    // see: The record column stays empty until it has earned a number
    // see: A reason's record is displayed, beside the reason and never beside the name
    //
    // The base rate is the pinned first row rather than a figure beside one of
    // them, which is what makes it impossible to read a forward-return figure on
    // this page without it.
    // see: Every forward-return figure is shown against the universe base rate
    public string ReasonRecords(
        IReadOnlyList<ReasonRecord> records,
        IReadOnlyList<ReasonTrackRow> tracks,
        IReadOnlyList<BaseRateLine> baseRates,
        int nights)
    {
        // The guard that makes the rule a property of the code rather than a
        // habit of this method. A region that can be drawn with no base rate is
        // one that will be, on the evening somebody passes an empty list, and
        // the figure that appears without it looks exactly like one that has it.
        // see: Every forward-return figure is shown against the universe base rate
        if (baseRates.Count == 0)
        {
            throw new InvalidOperationException(
                "The reason records were asked for with no base rate line at all. Every " +
                "forward-return figure on this page is shown against the universe base rate " +
                "for the same window, so a region with no pinned line is a region drawing " +
                "figures a reader cannot judge.");
        }

        var table = new StringBuilder();

        table.Append(Invariant, $"<section class=\"reason-records\" data-reasons=\"{records.Count}\" ");
        table.Append(Invariant, $"data-nights=\"{nights}\" data-base-rates=\"{baseRates.Count}\" ");
        table.Append(Invariant, $"data-minimum=\"{(records.Count == 0 ? 0 : records[0].Minimum)}\">");

        // The base rates, pinned above every row rather than beside one of them,
        // one per window that has one. The population is stated in the same
        // breath: every name-night the store holds, and not the nights a reason
        // fired, which would compare a signal against itself.
        foreach (var line in baseRates)
        {
            table.Append(Invariant, $"<p class=\"base-rate\" data-pinned=\"true\" data-window=\"{Escaped(line.Window)}\" ");
            table.Append(Invariant, $"data-base-rate=\"{(line.Rate is { } rate ? Number(rate) : "none")}\">");

            table.Append(line.Rate is { } shown
                ? Formatted($"the universe base rate over the {Escaped(line.Window)} session window is {Number(shown)} per cent, ")
                : Formatted($"the universe base rate over the {Escaped(line.Window)} session window is not yet measured, "));

            table.Append("computed over every name-night the store holds rather than over the nights a reason fired</p>");
        }

        // The setup horizon has no universe figure of the same kind, and the
        // page says why rather than leaving a window without a line and letting
        // a reader wonder which of the three is missing.
        // see: The `setup` horizon has no universe base rate, and the column is null for it
        table.Append("<p class=\"base-rate\" data-window=\"setup\" data-base-rate=\"none by rule\">");
        table.Append("the setup horizon has no universe base rate: target before stop is a question about a plan, ");
        table.Append("and a name with no plan that night has no answer to it. A setup is judged against the ");
        table.Append("break-even its own plan demanded</p>");

        table.Append(Invariant, $"<p class=\"nights\" data-nights=\"{nights}\">the record below stands on {nights} night(s) of listings{FewerNights(records, nights)}</p>");

        table.Append("<div class=\"tbl-wrap\">");
        table.Append("<table class=\"records-table\">");
        table.Append("<tr><th>Reason</th><th>Track</th><th>Fired</th><th>Resolved</th><th>Never entered</th><th>Record</th></tr>");

        var byReason = tracks.ToDictionary(track => track.Reason, StringComparer.Ordinal);

        foreach (var record in records)
        {
            table.Append(Invariant, $"<tr data-reason=\"{Escaped(record.Reason)}\" data-fired=\"{record.Fired}\" ");
            table.Append(Invariant, $"data-resolved=\"{record.Resolved}\" data-minimum=\"{record.Minimum}\" data-nights=\"{record.Nights ?? nights}\">");
            table.Append(Invariant, $"<td>{Escaped(record.Reason)}</td>");
            table.Append(Invariant, $"<td>{(byReason.TryGetValue(record.Reason, out var track) ? ReasonTrack([track]) : string.Empty)}</td>");
            table.Append(Invariant, $"<td>{record.Fired}</td>");
            table.Append(Invariant, $"<td>{record.Resolved}</td>");

            // The setups whose price never came back to the entry the plan named,
            // in their own column: never a win, never a loss and never in a rate.
            // see: A setup is scored from its entry, and a target reached before the entry is never a win
            table.Append(Invariant, $"<td class=\"never-entered\" data-never-entered=\"{record.NeverEntered}\">{record.NeverEntered}</td>");

            // Section 15.11's three states.
            //
            // Below either floor: a dashed outline carrying the count against
            // the floor that is short, and no rate. The count is what makes the
            // absence readable, and naming which floor is short is what makes it
            // actionable: a reader who cannot tell whether they are waiting for
            // rows or for nights cannot tell how long they are waiting.
            //
            // At or above both: the share, the number resolved and the
            // break-even those setups demanded, always the three together, with
            // the verdict and the divisor that corrected it. A share without its
            // denominator hides how much was checked; a share without the
            // break-even hides whether it was any good; and a verdict without
            // its divisor hides how hard the test actually was.
            // see: Not yet measured is drawn as a dashed outline, never as a pale value
            // see: The record column stays empty until it has earned a number
            // see: The significance threshold is divided by the family size, and the divisor is shown
            if (!record.HasEarnedAVerdict)
            {
                table.Append(Invariant, $"<td class=\"not-measured\" data-outline=\"dashed\" data-verdict=\"none\" ");
                table.Append(Invariant, $"data-short=\"{record.Withheld}\" data-scored=\"{record.Scored}\" ");
                table.Append(Invariant, $"data-sessions=\"{record.Sessions}\" data-session-minimum=\"{record.SessionMinimum}\">");

                table.Append(CountAgainstTheFloors(record));

                table.Append("</td>");
            }
            else
            {
                table.Append(Invariant, $"<td data-verdict=\"{VerdictWord(record)}\" ");
                table.Append(Invariant, $"data-share=\"{(record.Share is { } share ? Number(share) : "none")}\" ");
                table.Append(Invariant, $"data-break-even=\"{(record.BreakEven is { } bar ? Number(bar) : "none")}\" ");
                table.Append(Invariant, $"data-p-value=\"{(record.PValue is { } p ? p.ToString("R", Invariant) : "none")}\" ");
                table.Append(Invariant, $"data-divisor=\"{record.Divisor}\" data-threshold=\"{record.Threshold.ToString("0.#####", Invariant)}\" ");
                table.Append(Invariant, $"data-sessions=\"{record.Sessions}\">");

                table.Append(ShareOfTheScored(record));
                table.Append(Formatted($", over {record.Sessions} listing session(s). "));
                table.Append(Formatted(
                    $"{(record.Cleared is true ? "Clears" : "Does not clear")} at {record.Threshold.ToString("0.#####", Invariant)}, "));
                table.Append(Formatted(
                    $"which is {record.Significance.ToString("0.#####", Invariant)} divided by a family of {record.Divisor}"));
                table.Append(record.PValue is { } shownP
                    ? Formatted($", on an exact one-sided p {Probability(shownP)}</td>")
                    : "</td>");
            }

            table.Append("</tr>");
        }

        table.Append("</table></div>");
        table.Append("<p data-verdicts=\"withheld\">no rate and no verdict is shown for a reason below either floor, because a rate over a handful of resolved setups is consistent with almost any truth, and a count of rows alone can be filled by a handful of nights of one market move</p>");
        table.Append("</section>");

        return table.ToString();
    }

    // The operational header, section 15.10's first region: what ran, how long
    // each stage took, model calls, network requests, spend and the outcome.
    //
    // Per stage rather than in one total, because a night that landed inside its
    // limit by one step doing nothing is legible only if the steps are apart.
    public string OperationalHeader(DateOnly night, IReadOnlyList<StageRow> stages, PricedCalls? priced = null)
    {
        var header = new StringBuilder();

        header.Append(Invariant, $"<header class=\"operational\" data-night=\"{night:yyyy-MM-dd}\" data-stages=\"{stages.Count}\">");

        if (stages.Count == 0)
        {
            header.Append(Invariant, $"<p class=\"degraded\" data-stages=\"0\">the run log carries no stage for {night:yyyy-MM-dd}</p></header>");

            return header.ToString();
        }

        header.Append("<div class=\"tbl-wrap\">");
        header.Append("<table class=\"stage-table\">");
        header.Append("<tr><th>Stage</th><th>Started (UTC)</th><th>Took</th><th>Rows</th><th>Model calls</th><th>Requests</th><th>Spend</th><th>Outcome</th><th>Detail</th></tr>");

        foreach (var stage in stages)
        {
            // The instant in UTC and stated as UTC in the column heading, for
            // the reason every schedule in this repository is written that way:
            // a local rendering walks an hour against the provider twice a year,
            // and the figure this column exists to bound is the provider's.
            // The cell carries the clock time and the attribute the whole
            // instant, because a night that starts after the close in New York
            // carries tomorrow's UTC date and a bare time would hide it.
            header.Append(Invariant, $"<tr data-stage=\"{Escaped(stage.Stage)}\" {(stage.ByHand ? "data-by-hand=\"1\" " : string.Empty)}");
            header.Append(Invariant, $"data-started=\"{stage.StartedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\" data-seconds=\"{Number(stage.Seconds)}\" ");
            header.Append(Invariant, $"data-rows=\"{stage.RowsWritten}\" data-model-calls=\"{stage.ModelCalls}\" ");
            header.Append(Invariant, $"data-requests=\"{stage.NetworkRequests}\" data-spend=\"{Escaped(stage.Spend)}\" ");
            header.Append(Invariant, $"data-outcome=\"{Escaped(stage.Outcome)}\">");
            header.Append(Invariant, $"<td>{Escaped(stage.Stage)}{(stage.ByHand ? " (run by hand)" : string.Empty)}</td>");
            header.Append(Invariant, $"<td>{stage.StartedAt.UtcDateTime:HH:mm:ss}</td>");
            header.Append(Invariant, $"<td>{Number(stage.Seconds)}s</td><td>{stage.RowsWritten}</td>");
            header.Append(Invariant, $"<td>{stage.ModelCalls}</td><td>{stage.NetworkRequests}</td>");
            header.Append(Invariant, $"<td>{Escaped(stage.Spend)}</td><td>{Escaped(stage.Outcome)}</td>");

            // What the stage said about itself, in a cell rather than a hover.
            // Until the phase 5 sign-off it was the stage cell's title, which a
            // person reads only by pointing at it and a phone cannot show, so the
            // names a night could not compare, the rows the fetch passed over and
            // the members a file carried nothing for were on the page and not
            // readable on it.
            header.Append(Invariant, $"<td class=\"detail\">{Escaped(stage.Detail)}</td></tr>");
        }

        header.Append("</table></div>");

        // The night's own total beneath the steps rather than above them, so the
        // steps are what is read first. A command run by hand that day is counted
        // apart and never in the night's stage time.
        var ofTheNight = stages.Where(stage => !stage.ByHand).ToArray();
        var byHand = stages.Count - ofTheNight.Length;

        header.Append(Invariant, $"<p class=\"total\" data-seconds=\"{Number(ofTheNight.Sum(stage => stage.Seconds))}\" data-by-hand=\"{byHand}\">");
        header.Append(Invariant, $"{ofTheNight.Length} stage(s) of the night, {Number(ofTheNight.Sum(stage => stage.Seconds))} second(s) of stage time");
        header.Append(byHand > 0 ? Formatted($", and {byHand} command(s) run by hand</p>") : "</p>");

        // Every paid call the run log carries a recorded cost for, over every night
        // rather than this one, the research passes they were made in, and what they
        // came to. It is the surface the operating row that settles the two caps is
        // read on, and that row fires at twenty passes, so passes are stated beside
        // calls rather than left to be counted off them.
        // owes: The spend cap set from the passes the ledger has priced
        if (priced is { } calls)
        {
            header.Append(Invariant, $"<p class=\"priced-calls\" data-calls=\"{calls.Count}\" data-passes=\"{calls.Passes}\" data-spend=\"{calls.Total}\" data-at-peak=\"{calls.AtPeak}\">");
            header.Append(Invariant, $"paid calls with a recorded cost: {calls.Count} over {calls.Passes} research pass(es), costing {SpendVerdict.Money(calls.Total)} in all, {calls.AtPeak} of them answered inside a peak window</p>");
        }

        header.Append("</header>");

        return header.ToString();
    }

    // Stale and failed, section 15.10's fourth region: names carrying
    // yesterday's bars, and what failed in which component.
    //
    // The names are listed rather than counted. A page that says four names are
    // stale and does not say which is a page nobody can act on. The research
    // halves, being the sections that fell back and the documents refused by
    // admissibility, arrive with the pass that produces them and are stated as
    // absent rather than drawn as empty lists.
    public string StaleAndFailed(
        IReadOnlyList<string> stale,
        IReadOnlyList<StageRow> failed,
        IReadOnlyList<RefusedDocument> refused,
        IReadOnlyList<LeftOutSection> fellBack)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"stale-and-failed\" data-stale=\"{stale.Count}\" data-failed=\"{failed.Count}\">");

        region.Append(stale.Count == 0
            ? "<p data-stale=\"none\">no name is carrying yesterday's bars</p>"
            : Formatted($"<p data-stale=\"{stale.Count}\">{stale.Count} name(s) carrying yesterday's bars: {Escaped(string.Join(", ", stale))}</p>"));

        region.Append(FailedStages(failed));
        region.Append(Refused(refused));
        region.Append(FellBack(fellBack));
        region.Append("</section>");

        return region.ToString();
    }

    // What failed, in which component: the count and each stage that failed. Apart from
    // the rest of its region so a page over a store it cannot read further draws this
    // half alone.
    public string FailedStages(IReadOnlyList<StageRow> failed)
    {
        var region = new StringBuilder();

        region.Append(failed.Count == 0
            ? "<p data-failed=\"none\">no stage of this night failed</p>"
            : Formatted($"<p data-failed=\"{failed.Count}\">{failed.Count} stage(s) failed</p>"));

        foreach (var stage in failed)
        {
            region.Append(Invariant, $"<p class=\"failed\" data-stage=\"{Escaped(stage.Stage)}\" data-outcome=\"{Escaped(stage.Outcome)}\">");
            region.Append(Invariant, $"{Escaped(stage.Stage)}: {Escaped(stage.Outcome)}. {Escaped(stage.Detail)}</p>");
        }

        return region.ToString();
    }

    // The sections that fell back on this night, whose they were and why.
    //
    // Listed rather than counted, for the reason the refusals are, and the reason
    // drawn whole, because the reason is the offending text: a figure the facts
    // file does not hold, a sentence naming no document, or no admissible source.
    // A night with none says so, which is the ordinary case.
    string FellBack(IReadOnlyList<LeftOutSection> fellBack)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"fell-back\" data-fell-back=\"{fellBack.Count}\">");

        if (fellBack.Count == 0)
        {
            region.Append("<p data-fell-back=\"none\">no section fell back on this night</p>");
        }
        else
        {
            region.Append(Formatted($"<p data-fell-back=\"{fellBack.Count}\">{fellBack.Count} section(s) fell back</p>"));

            foreach (var section in fellBack)
            {
                region.Append(Invariant, $"<p class=\"fell-back-section\" data-subject=\"{Escaped(section.Subject)}\" data-section=\"{Escaped(section.Section)}\">");
                region.Append(Invariant, $"{Escaped(section.Subject)}, {Escaped(section.Section)}: {Escaped(section.Reason)}</p>");
            }
        }

        region.Append("</section>");

        return region.ToString();
    }

    // A name's own sections that were left out, each with one line saying why.
    //
    // Section 18's two rows about a section the checker could not accept say the
    // same thing a reader sees: the section is absent with one line saying why,
    // whether it was refused twice or had no admissible source to be written from.
    // The line is the reason the checker stored, so the page and the run log
    // cannot describe one refusal two ways.
    //
    // Only the sections left out are drawn here. A written section is drawn in its
    // own place on the page with its date and model beneath it, and a section still
    // waiting on its retry is neither written nor left out, so this says nothing of it.
    //
    // From 6.6 it also draws the sections the newest pass did not write, each with the
    // reason the pass recorded: the machine could not hold it, the local model was
    // unavailable, the pass was handed nothing for it, or from 6.8 the spend cap
    // stopped the call. Section 18 says those are absent with their reason, and a
    // section absent with nothing beside it reads as one nobody tried.
    //
    // From 6.8 it carries what the newest pass came to and the controls that start
    // one, with what research has cost stated beside them before they are pressed.
    // Where a pass the page started stands, drawn beside the control that started it. The
    // page reads this while the pass runs and redraws the name's sections each time one more
    // has landed, which is what section 15.12's last step asks for.
    // see: A pass the page starts is watched until it ends and the page redraws as each section lands
    public string PassProgressLine(string ticker, PassProgress progress) =>
        Formatted($"<p class=\"pass-progress\" role=\"status\" data-ticker=\"{Escaped(ticker)}\" data-state=\"{Escaped(progress.State)}\" data-sections=\"{progress.Sections}\">{Escaped(progress.Step)}, and the name holds {progress.Sections} written section(s)</p>");

    public string LeftOut(
        string ticker,
        IReadOnlyList<LeftOutSection> leftOut,
        ResearchStateLine? state = null,
        IReadOnlyList<LeftOutSection>? notWritten = null,
        ResearchPausedLine? paused = null,
        ResearchPassLine? pass = null,
        IReadOnlyList<ResearchControl>? controls = null,
        ResearchCost? cost = null)
    {
        var region = new StringBuilder();
        var unwritten = notWritten ?? [];

        // A control is drawn only with its cost, because a control whose cost is not
        // stated before it is pressed is the one section 15.9 says the page does not draw.
        var offered = cost is null ? [] : controls ?? [];

        region.Append(Invariant, $"<section class=\"research\" data-ticker=\"{Escaped(ticker)}\" data-left-out=\"{leftOut.Count}\" data-not-written=\"{unwritten.Count}\" data-controls=\"{offered.Count}\">");

        // Where the research stands, first, because it is the answer to the
        // question a reader opens the page with. Section 15.9's research-state
        // rows: missing with one line saying the sections have not been written,
        // or stale with one line naming which of the four triggers fired.
        if (state is not null)
        {
            region.Append(Invariant, $"<p class=\"research-state\" data-state=\"{Escaped(state.State)}\">{Escaped(state.Line)}</p>");
        }

        // What the newest pass came to, where it says something the state does not: a
        // research model that did not answer, a cap that stopped the pass, a pass that
        // ran today. In the words the page projection assembles from the pass's own row.
        if (pass is not null)
        {
            region.Append(Invariant, $"<p class=\"research-pass\" data-outcome=\"{Escaped(pass.Outcome)}\" data-as-of=\"{pass.AsOf:yyyy-MM-dd}\">{Escaped(pass.Line)}</p>");
        }

        // Research paused, section 15.9's state, from 6.7: one line saying research is
        // paused and when it resumes, in the words the spend cap refuses a call with,
        // so the page and the run log cannot describe one pause two ways.
        // see: The spend cap is a stop, not an allowance
        if (paused is not null)
        {
            region.Append(Invariant, $"<p class=\"research-paused\" data-cap=\"{Escaped(paused.Cap)}\" data-resumes-at=\"{paused.ResumesAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\">");
            region.Append(Invariant, $"{Escaped(paused.Line)}</p>");
        }

        foreach (var section in leftOut)
        {
            region.Append(Invariant, $"<p class=\"left-out\" data-section=\"{Escaped(section.Section)}\">");
            region.Append(Invariant, $"{Escaped(section.Section)} is left out: {Escaped(section.Reason)}</p>");
        }

        foreach (var section in unwritten)
        {
            region.Append(Invariant, $"<p class=\"not-written\" data-section=\"{Escaped(section.Section)}\">");
            region.Append(Invariant, $"{Escaped(section.Section)} is not written: {Escaped(section.Reason)}</p>");
        }

        // The controls, each a form the page's own script sends, with what it asks for on
        // the form so a test reads what a press would send rather than the label beside it.
        // The cost is stated once, before any of them, because it is the same statement
        // for each: the local lane costs nothing and every other call goes through the cap.
        // see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
        if (offered.Count > 0 && cost is not null)
        {
            // Before the controls, so it is read before any of them is pressed.
            region.Append(Invariant, $"<p class=\"research-cost\" data-passes=\"{cost.Passes}\" data-spend=\"{cost.Total}\" data-most=\"{cost.Most}\">");
            region.Append(Escaped(CostLine(cost))).Append("</p>");
        }

        foreach (var control in offered)
        {
            region.Append(Invariant, $"<form class=\"research-control\" method=\"post\" action=\"/passes/{Uri.EscapeDataString(ticker)}\" ");
            region.Append(Invariant, $"data-kind=\"{Escaped(control.Kind)}\" data-refresh=\"{Flag(control.Refresh)}\" data-paid-for-local=\"{Flag(control.PaidForLocal)}\">");
            region.Append(Invariant, $"<input type=\"hidden\" name=\"refresh\" value=\"{Flag(control.Refresh)}\">");
            region.Append(Invariant, $"<input type=\"hidden\" name=\"paidForLocal\" value=\"{Flag(control.PaidForLocal)}\">");
            region.Append(Invariant, $"<button type=\"submit\">{Escaped(control.Label)}</button></form>");
        }

        region.Append("</section>");

        return region.ToString();
    }

    // What research has cost, in one sentence. Every figure is the run log's: the passes
    // it priced and what they came to. A pass's price is known only once it has been
    // made, so what is stated before a press is what the passes before it cost and the
    // cap that stops the next one, rather than an estimate nothing measured.
    // see: The spend cap is a stop, not an allowance
    public static string CostLine(ResearchCost cost) =>
        cost.Passes == 0
            ? "A pass writes the local lane's sections for nothing and asks the paid model for the rest through the spend cap, which refuses a call that could take spend past a cap. No research pass has a recorded cost yet. " + Capitalised(cost.Verdict) + "."
            : Formatted($"A pass writes the local lane's sections for nothing and asks the paid model for the rest through the spend cap, which refuses a call that could take spend past a cap. The {cost.Passes} research pass(es) the run log has priced cost {SpendVerdict.Money(cost.Total)} in all, and the most one cost was {SpendVerdict.Money(cost.Most)}. ") + Capitalised(cost.Verdict) + ".";

    static string Flag(bool value) => value ? "true" : "false";

    static string Capitalised(string line) =>
        line.Length == 0 ? line : char.ToUpperInvariant(line[0]) + line[1..];

    // One written section, under its own heading, with the date it was written on beneath
    // the prose and the model that wrote it on the element, and the documents its markers
    // resolve to.
    //
    // The prose is drawn as stored, a paragraph at each blank line, and as text: a model's
    // words are quoted rather than rendered, so markup inside them is shown rather than
    // run. Each marker is listed with the document it names, because a sentence resting on
    // [D2] is checkable only where [D2] says which document it is.
    // see: A research record is written and dated per section, not as a whole
    // see: Every researched claim must name a stored source document
    public string WrittenSection(string ticker, WrittenCell section, IReadOnlyList<SourceCell> documents, string dated = "written on")
    {
        var drawn = new StringBuilder();

        drawn.Append(Invariant, $"<section class=\"written-section\" data-ticker=\"{Escaped(ticker)}\" data-section=\"{Escaped(section.Section)}\" ");
        drawn.Append(Invariant, $"data-as-of=\"{section.AsOf:yyyy-MM-dd}\" data-model=\"{Escaped(section.Model)}\">");
        drawn.Append(Invariant, $"<h3>{Escaped(section.Section)}</h3>");

        var paragraphs = section.Prose.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (TwoCases(section.Section, paragraphs) is { } cases)
        {
            foreach (var (label, rows) in cases)
            {
                if (label is not null)
                {
                    drawn.Append(Invariant, $"<div class=\"case\" data-case=\"{Escaped(label)}\"><h4>{Escaped(label)}</h4>");
                }

                ClaimRows(drawn, rows);

                if (label is not null)
                {
                    drawn.Append("</div>");
                }
            }
        }
        else if (RiskParts(section.Section, paragraphs) is { } parts && parts.Select(RiskAndWhatWouldConfirmIt).ToArray() is var split && split.All(part => part.Confirmation is not null))
        {
            // Every risk states what would confirm it apart, so each is a row of two columns: the
            // risk, and what would confirm it.
            drawn.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"risks-table\" data-parts=\"{split.Length}\"><tr><th>Risk</th><th>What would confirm it</th></tr>");

            foreach (var (risk, confirmation) in split)
            {
                drawn.Append("<tr class=\"risk\"><td><p class=\"prose\">").Append(Escaped(risk)).Append("</p></td>");
                drawn.Append("<td><p class=\"prose confirms\">").Append(Escaped(confirmation!)).Append("</p></td></tr>");
            }

            drawn.Append("</table></div>");
        }
        else if (RiskParts(section.Section, paragraphs) is { } risks)
        {
            drawn.Append(Invariant, $"<ul class=\"risks\" data-parts=\"{risks.Count}\">");

            foreach (var part in risks)
            {
                var (risk, confirmation) = RiskAndWhatWouldConfirmIt(part);

                drawn.Append("<li class=\"risk\"><p class=\"prose\">").Append(Escaped(risk)).Append("</p>");

                if (confirmation is not null)
                {
                    drawn.Append("<p class=\"prose confirms\">").Append(Escaped(confirmation)).Append("</p>");
                }

                drawn.Append("</li>");
            }

            drawn.Append("</ul>");
        }
        else if (string.Equals(section.Section, TheRisks, StringComparison.Ordinal) && paragraphs.Length > 0)
        {
            ClaimRows(drawn, Claims(paragraphs));
        }
        else
        {
            foreach (var paragraph in paragraphs)
            {
                drawn.Append("<p class=\"prose\">").Append(Escaped(paragraph)).Append("</p>");
            }
        }

        drawn.Append(Invariant, $"<p class=\"written-by\">{Escaped(dated)} {section.AsOf:yyyy-MM-dd}</p>");

        if (section.SourceIds.Count > 0)
        {
            drawn.Append(Invariant, $"<ol class=\"section-sources\" data-cites=\"{section.SourceIds.Count}\">");

            for (var at = 0; at < section.SourceIds.Count; at++)
            {
                var id = section.SourceIds[at];
                var document = documents.FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.Ordinal));

                drawn.Append(Invariant, $"<li data-marker=\"D{at + 1}\" data-document=\"{Escaped(id)}\">[D{at + 1}] ");
                drawn.Append(document is null ? "a document the store does not hold" : Link(document));
                drawn.Append("</li>");
            }

            drawn.Append("</ol>");
        }

        drawn.Append("</section>");

        return drawn.ToString();
    }

    // The section a reader reads first, and the one a model's draft is most often left out of.
    public const string TheShortVersion = "The short version";

    // Why a short version written by code stands where the model's would, each read from the record the
    // page already reads the section's state from: the checker left the model's draft out, the pass named
    // it not written, or no research has been written for the name, which implies no model at all.
    public const string ShortVersionRefused = "refused";
    public const string ShortVersionNotWritten = "not written";
    public const string ShortVersionNoResearch = "no research";

    public static string ShortVersionHeading(string reason) => reason switch
    {
        ShortVersionRefused => "Written by code: the model's short version was refused by the claim check, so this summary states only what the night computed.",
        ShortVersionNoResearch => "Written by code: no research has been written for this name, so this summary states only what the night computed.",
        _ => "Written by code: the model's short version was not written, so this summary states only what the night computed.",
    };

    // The short version where no accepted one stands, written by code from computed parts alone: why the
    // name is or is not on the list, the state its reported quarters give it with the heading its numbers
    // open on, and the entry, stop and target of the trade the swing filter's trade gate read. No model is
    // asked and nothing is stored, so it follows the night the page draws. The list is the one the page's
    // night drew, dated where the page is for an earlier night and called tonight's where it is not, as the
    // page's reasons are, since the newest bar can be a night the list has not yet been drawn for.
    // see: The short version is written last from the sections that passed, and one left out is replaced by a summary code writes
    public string ShortVersionByCode(
        string ticker,
        string reason,
        DateOnly? earlierNight,
        FilterWhy? passed,
        (DateOnly Evening, EquityBrief.Core.Filter.MissedGate Gate)? missed,
        IReadOnlyList<FiredReason> fired,
        NumbersSayView? says,
        GatesView? gates)
    {
        var list = earlierNight is { } on ? "the list on " + on.ToString("yyyy-MM-dd", Invariant) : "tonight's list";

        var why = passed is { } through
            ? FormattableString.Invariant($"On the list on {through.Evening:yyyy-MM-dd}: the swing filter passed it on all five gates{(gates?.Family is { Length: > 0 } family ? ", a " + family + " setup" : string.Empty)}.")
            : missed is { } near
                ? FormattableString.Invariant($"Close to a buy point on {near.Evening:yyyy-MM-dd}: one gate short, the {near.Gate.Gate} gate, {near.Gate.Words}. It is not a pick.")
                : fired.Count > 0
                    ? FormattableString.Invariant($"On {list} for {fired.Count} reason(s): {string.Join(", ", fired.Select(one => one.Name))}.")
                    : FormattableString.Invariant($"Not on {list}.");

        var business = says is null
            ? "No reading of its reported quarters is stored for this night."
            : says.Heading.TrimEnd('.') + ".";

        var (entry, stop, target) = gates?.Input == FilterSettings.ClearWord
            ? (gates.SwingEntry, gates.ClearStop, gates.ClearTarget)
            : (gates?.SwingEntry, gates?.SwingStop, gates?.SwingTarget);

        var plan = gates is not null && entry is { } buy && stop is { } exit && target is { } sell
            ? Formatted($"{Capitalised(PlanWords(gates.Input == FilterSettings.ClearWord ? FilterSettings.ClearWord : FilterSettings.SwingWord))}{(gates.Input is FilterSettings.ClearWord or FilterSettings.SwingWord ? ", the plan the trade gate read" : string.Empty)}: in at {Price(buy)}, stop {Price(exit)}, target {Price(sell)}.")
            : "No plan with an entry, a stop and a target is stored for this night.";

        var drawn = new StringBuilder();

        drawn.Append(Invariant, $"<section class=\"code-summary\" data-ticker=\"{Escaped(ticker)}\" data-section=\"{Escaped(TheShortVersion)}\" data-reason=\"{Escaped(reason)}\" data-state=\"{Escaped(says?.State ?? "none")}\">");
        drawn.Append(Invariant, $"<h3>{Escaped(TheShortVersion)}</h3>");
        drawn.Append(Invariant, $"<p class=\"code-heading\">{Escaped(ShortVersionHeading(reason))}</p>");
        drawn.Append("<ul class=\"code-parts\">");
        drawn.Append("<li data-summary-part=\"why\">").Append(Escaped(why)).Append("</li>");
        drawn.Append("<li data-summary-part=\"business\">").Append(Escaped(business)).Append("</li>");
        drawn.Append(Invariant, $"<li data-summary-part=\"plan\" data-entry=\"{(entry is { } e ? e.ToString(Invariant) : "none")}\" data-stop=\"{(stop is { } s ? s.ToString(Invariant) : "none")}\" data-target=\"{(target is { } t ? t.ToString(Invariant) : "none")}\">").Append(Escaped(plan)).Append("</li>");
        drawn.Append("</ul></section>");

        return drawn.ToString();
    }

    // The section holding the case for a name and the case against it.
    public const string TheTwoCases = "The two cases";

    // The two cases a claim to a row, under the case each belongs to. A row is a sentence as the
    // claim checker reads it, which is the unit the checker accepted and ends on the marker of the
    // document it rests on, and each paragraph is read on its own so a paragraph's end is always
    // a row's. A reader looking for the case against a name should not have to find where a run
    // of prose stops being the case for it.
    //
    // The labels are read off the prose: where it opens on the bull case, the case against starts
    // at the first sentence opening on the bear case, whichever paragraph that sentence is in, and
    // runs to the end, so what the writer set after it is read in the case it was set in. Where
    // the prose says neither, the rows are drawn with no label, because a boundary this file
    // guessed at would put one case's words under the other's.
    // see: A written section is drawn a claim to a row, and the two cases and the risks are asked for in the parts the page draws
    static IReadOnlyList<(string? Label, IReadOnlyList<string> Rows)>? TwoCases(string section, IReadOnlyList<string> paragraphs)
    {
        if (!string.Equals(section, TheTwoCases, StringComparison.Ordinal) || paragraphs.Count == 0)
        {
            return null;
        }

        var rows = Claims(paragraphs);

        return EquityBrief.Core.Research.ClaimRules.CaseSides(rows) is { } sides
            ? [("The case for", sides.For), ("The case against", sides.Against)]
            : [(null, rows)];
    }

    // A section's claims in the order they were written: each sentence as the claim checker reads
    // it, which ends on the marker of the document it rests on, each paragraph read on its own so a
    // paragraph's end is always a claim's. Cut and never edited, so joined back up they are the
    // section as it was written.
    static string[] Claims(IReadOnlyList<string> paragraphs) =>
        [.. paragraphs.SelectMany(paragraph => EquityBrief.Core.Research.ClaimRules.Sentences(paragraph)).Select(sentence => sentence.Text)];

    // Claims drawn a claim to a row, the rows ruled apart as a table's are.
    static void ClaimRows(StringBuilder drawn, IReadOnlyList<string> rows)
    {
        drawn.Append(Invariant, $"<ul class=\"claim-rows\" data-rows=\"{rows.Count}\">");

        foreach (var row in rows)
        {
            drawn.Append("<li><p class=\"prose\">").Append(Escaped(row)).Append("</p></li>");
        }

        drawn.Append("</ul>");
    }

    // The section holding the risks, which the writer is asked for as a risk and then what
    // would confirm that risk, one risk after another.
    public const string TheRisks = "The risks, each with what would confirm it";

    // Where one risk ends and the next begins, as the prose states it: a sentence opening on
    // an ordinal and the word risk.
    static readonly Regex RiskOpens = new(
        @"(?:^|(?<=\.\s))(?:The|A)\s(?<ordinal>first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth)\srisk\b",
        RegexOptions.CultureInvariant);

    // How a part opens what would confirm the risk it states, in the words the writer is asked
    // for.
    const string ConfirmationOpens = "That risk would be confirmed";

    // The risks as one part each, where the prose says where the parts are, and nothing where
    // it does not. Two shapes are read: a paragraph per risk, which is what a writer that broke
    // them up gives, and a sentence opening on an ordinal risk, which is what one that ran them
    // together gives. Where neither is there the section is drawn a claim to a row rather than a
    // risk to a row, because a boundary this file guessed at would put one risk's words under
    // another's.
    //
    // The parts are the prose cut and never edited, so joined back up they are the section as it
    // was written, which is the property the surface reads them against. What stands before the
    // first ordinal is read by that ordinal. Where it numbers the first risk, what stands before
    // it is an introduction and opens the first part rather than being dropped. Where it numbers
    // the second, what stands before it is the first risk, a part of its own, since reading it as
    // an introduction would set the second risk beneath the first one's confirmation. Where it
    // numbers any later one, the risks before it are stated without saying where they part, so
    // the section is drawn a claim to a row.
    //
    // The order is the order they were written in. Ordering them by how severe each one is would
    // rank them on a judgement no model stated and no code computed.
    // see: A written section is drawn a claim to a row, and the two cases and the risks are asked for in the parts the page draws
    // see: Code owns every number
    static IReadOnlyList<string>? RiskParts(string section, IReadOnlyList<string> paragraphs)
    {
        if (!string.Equals(section, TheRisks, StringComparison.Ordinal) || paragraphs.Count == 0)
        {
            return null;
        }

        if (paragraphs.Count > 1)
        {
            return paragraphs;
        }

        var whole = paragraphs[0];
        var opens = RiskOpens.Matches(whole).ToList();

        if (opens.Count == 0)
        {
            return null;
        }

        IEnumerable<Match>? cuts = opens[0].Groups["ordinal"].Value switch
        {
            "first" => opens.Skip(1),
            "second" => opens,
            _ => null,
        };

        if (cuts is null)
        {
            return null;
        }

        var edges = new List<int> { 0 };

        edges.AddRange(cuts.Select(one => one.Index).Where(at => at > 0));
        edges.Add(whole.Length);

        return edges.Count < 3
            ? null
            : [.. Enumerable.Range(0, edges.Count - 1).Select(at => whole[edges[at]..edges[at + 1]].Trim())];
    }

    // A part as the risk and what would confirm it, where the part opens its confirmation in the
    // words the writer was asked for, and as one run where it does not.
    static (string Risk, string? Confirmation) RiskAndWhatWouldConfirmIt(string part)
    {
        var at = part.IndexOf(". " + ConfirmationOpens, StringComparison.Ordinal);

        return at < 0 ? (part, null) : (part[..(at + 1)], part[(at + 2)..]);
    }

    // The one section dated by the close it explains rather than by the day it was written.
    public const string KeySection = "The key under each figure";

    // Where the key under each figure would be, when the one on file explains another
    // night's figures than the page draws: which night it was written for, and why it is
    // not drawn beside these.
    // see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
    public string KeyForAnotherNight(string ticker, string section, DateOnly writtenFor, DateOnly? session) =>
        FormattableString.Invariant($"<p class=\"key-elsewhere\" data-ticker=\"{Escaped(ticker)}\" data-section=\"{Escaped(section)}\" data-written-for=\"{writtenFor:yyyy-MM-dd}\">")
        + (session is { } on
            ? FormattableString.Invariant($"Not drawn: the newest key explains the figures of {writtenFor:yyyy-MM-dd}, and the figures on this page are {on:yyyy-MM-dd}'s.")
            : FormattableString.Invariant($"Not drawn: the newest key explains the figures of {writtenFor:yyyy-MM-dd}, and no session is stored for this name."))
        + "</p>";

    // Dates and sources, section 15.9's region and the one section 4 calls what the research read: the
    // calendar, the dated items a pass read out of the documents, and every document the
    // written sections cite with its date and link.
    //
    // The calendar is the provider's and the dated items are a model's, and the two are
    // drawn apart for that reason: an earnings date is filed and a conference date is a
    // sentence resting on a document, and a reader has to be able to tell which is which.
    // A region with nothing in a part says so rather than drawing it empty.
    public string DatesAndSources(string ticker, IReadOnlyList<DateCell> dates, WrittenCell? items, IReadOnlyList<SourceCell> documents)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"dates-and-sources\" data-ticker=\"{Escaped(ticker)}\" data-events=\"{dates.Count}\" data-documents=\"{documents.Count}\">");

        if (dates.Count == 0)
        {
            region.Append("<p class=\"degraded\" data-events=\"none\">the calendar holds no dated event for this name from its newest session</p>");
        }
        else
        {
            region.Append("<div class=\"tbl-wrap\">");
            region.Append("<table class=\"calendar-dates\"><tr><th>Date</th><th>Event</th><th>Timing</th></tr>");

            foreach (var date in dates)
            {
                region.Append(Invariant, $"<tr data-date=\"{date.Date:yyyy-MM-dd}\" data-kind=\"{Escaped(date.Kind)}\">");
                region.Append(Invariant, $"<td>{date.Date:yyyy-MM-dd}</td><td>{Escaped(date.Kind)}</td><td>{Escaped(date.Timing)}</td></tr>");
            }

            region.Append("</table></div>");
        }

        if (items is not null)
        {
            region.Append(WrittenSection(ticker, items, documents));
        }

        if (documents.Count == 0)
        {
            region.Append("<p class=\"degraded\" data-documents=\"none\">no written section cites a document</p>");
        }
        else
        {
            region.Append("<ol class=\"sources\">");

            foreach (var document in documents)
            {
                region.Append(Invariant, $"<li data-document=\"{Escaped(document.Id)}\" data-published-on=\"{Published(document)}\" data-url=\"{Escaped(document.Url)}\">");
                region.Append(Link(document)).Append("</li>");
            }

            region.Append("</ol>");
        }

        region.Append("</section>");

        return region.ToString();
    }

    // A document as a link with the date it was published beside it, or the words saying
    // no date is on file, which admissibility refuses a document for, so a cited one
    // carries a date unless its row is older than the rule.
    static string Link(SourceCell document) =>
        "<a href=\"" + Escaped(document.Url) + "\">" + Escaped(document.Title) + "</a>"
        + (document.PublishedOn is not null ? ", published on " + Published(document) : ", with no publish date on file");

    static string Published(SourceCell document) =>
        document.PublishedOn is { } on ? on.ToString("yyyy-MM-dd", Invariant) : "none";

    // The documents admissibility refused, with the category that refused each.
    //
    // Listed rather than counted, and the address drawn, because the address is
    // the thing a person can act on: a region saying four documents were refused
    // says nothing a reader can follow up. The count per category is stated above
    // the list, so a search returning marketing reads as one thing rather than as
    // four unrelated refusals.
    //
    // A night with no refusals says so. It is the ordinary case, and drawn as an
    // empty list it would read as a region that failed to load.
    string Refused(IReadOnlyList<RefusedDocument> refused)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"refused-documents\" data-refused=\"{refused.Count}\">");

        if (refused.Count == 0)
        {
            region.Append("<p data-refused=\"none\">no document was refused by admissibility on this night</p>");
            region.Append("</section>");

            return region.ToString();
        }

        // Counted off the same list this draws, so the header and the rows cannot
        // disagree about how many of a kind there were.
        var byCategory = refused
            .GroupBy(document => document.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (Category: group.Key, Documents: group.Count()))
            .ToArray();

        region.Append(Formatted($"<p data-categories=\"{byCategory.Length}\">{refused.Count} document(s) refused: "));
        region.Append(Escaped(string.Join(", ", byCategory.Select(group =>
            group.Category + " " + group.Documents.ToString(CultureInfo.InvariantCulture)))));
        region.Append("</p>");

        foreach (var document in refused)
        {
            region.Append(Invariant, $"<p class=\"refused\" data-category=\"{Escaped(document.Category)}\" ");
            region.Append(Invariant, $"data-url=\"{Escaped(document.Url)}\">");
            region.Append(Invariant, $"{Escaped(document.Category)}: {Escaped(document.Title)}, {Escaped(document.Url)}</p>");
        }

        region.Append("</section>");

        return region.ToString();
    }

    // The overnight queue, section 15.10's fifth region: whether the queue ran after the
    // night's arithmetic, how many queued passes completed and how many were left for the
    // next night, and every traded session since it last ran on which it did not.
    //
    // A night it did not run is a line of its own naming the night, because a queue that
    // silently failed looks identical to a quiet night and the absence has to be read off
    // the page rather than inferred from a region with nothing in it.
    // see: The overnight run holds the machine awake and reports whether it ran
    public string OvernightQueue(QueueNight queue)
    {
        var region = new StringBuilder();
        var ran = queue.Outcome is not null;

        region.Append(Invariant, $"<section class=\"overnight-queue\" data-night=\"{queue.Night:yyyy-MM-dd}\" data-queue=\"{(ran ? "ran" : queue.NeverRan ? "never" : "not run")}\" ");
        region.Append(Invariant, $"data-outcome=\"{Escaped(queue.Outcome ?? "none")}\" data-queued=\"{queue.Queued}\" data-completed=\"{queue.Completed}\" data-left=\"{queue.Left}\" data-not-run=\"{queue.NotRun.Count}\">");

        if (ran && queue.Outcome == "failed")
        {
            region.Append(Invariant, $"<p data-outcome=\"failed\">the overnight queue failed on {queue.Night:yyyy-MM-dd}: {Escaped(queue.Reason ?? "the run log states no reason")}</p>");
        }
        else if (ran)
        {
            region.Append(Invariant, $"<p data-queue=\"ran\">the overnight queue ran on {queue.Night:yyyy-MM-dd}: {queue.Completed} of {queue.Queued} queued pass(es) completed, {queue.Left} left for the next night</p>");

            if (queue.Outcome == "limit")
            {
                region.Append(Invariant, $"<p data-outcome=\"limit\">it started no pass once its limit of {queue.LimitHours.ToString("0.##", CultureInfo.InvariantCulture)} hour(s) had passed</p>");
            }
            else if (queue.Outcome == "unavailable")
            {
                region.Append(Invariant, $"<p data-outcome=\"unavailable\">it could not run: {Escaped(queue.Reason ?? "the local model is unavailable")}</p>");
            }

            if (queue.Awake is { Length: > 0 } awake)
            {
                region.Append(Invariant, $"<p data-awake=\"{Escaped(awake)}\">the machine was {Escaped(awake)}</p>");
            }
        }
        else if (queue.NeverRan)
        {
            region.Append(Invariant, $"<p data-queue=\"never\">the overnight queue has not run on any night this store holds, up to {queue.Night:yyyy-MM-dd}</p>");
        }

        foreach (var night in queue.NotRun)
        {
            region.Append(Invariant, $"<p class=\"not-run\" data-night=\"{night:yyyy-MM-dd}\">the overnight queue did not run on {night:yyyy-MM-dd}</p>");
        }

        region.Append("</section>");

        return region.ToString();
    }

    // The shadow candidates region, section 15.10's third.
    //
    // How many candidate conditions are registered, the correction divisor that
    // number sets, and one line saying each candidate's record is withheld until
    // it is promoted. No candidate's pick of a name appears here, and the reason is
    // worth the sentence: the region exists to say how hard the test is, and a
    // region that also said how a candidate was doing would let the decision to
    // keep it be taken on the result, which is what registering in advance is
    // for.
    //
    // The count and the divisor are drawn from two figures computed apart, so a register and a
    // correction that disagree show it on the page rather than agreeing by construction.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    // see: The significance threshold is divided by the family size, and the divisor is shown
    // The run page's Calibration region, section 15.10's rows: what the two clocks can do, in the one
    // sentence section 13 states; the shape half, being the ordinary nights under the open filter
    // version against the sixty the calibration waits on, every event night with what made it one, each
    // gate's median count and the list's against their bands, drawn as not yet measured until the
    // trigger, and each reason's share as context; and a line for each operating obligation no other
    // surface counts, its count against its trigger.
    // see: The swing filter's shape is calibrated over its ordinary nights, a night one cause pushes past a quarter and twice its usual share is left out, and each band spans a third to three times what the ruled filter passes
    public string Calibration(ShapeState shape, IReadOnlyList<TriggerLine> triggers)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"calibration\" data-night=\"{shape.Night:yyyy-MM-dd}\" data-version=\"{Escaped(shape.Version)}\" ");
        region.Append(Invariant, $"data-window=\"{shape.WindowNights}\" data-ordinary=\"{shape.Ordinary}\" data-wanted=\"{shape.Wanted}\" data-crossed=\"{(shape.Crossed ? "true" : "false")}\">");
        region.Append(Invariant, $"<p class=\"timeline\">{Escaped(ShapeClock.Timeline)}</p>");

        region.Append("<h4>The shape clock</h4>");
        region.Append(Invariant, $"<p class=\"shape-window\" data-ordinary=\"{shape.Ordinary}\">");
        region.Append(shape.VersionOpen
            ? Formatted($"{shape.Ordinary} of the {shape.Wanted} ordinary nights under filter version {Escaped(shape.Version)} are stored, of {shape.WindowNights} night(s) under it.")
            : Formatted($"No filter version is open, so no night counts toward the {shape.Wanted} yet: {shape.Ordinary} ordinary night(s) of {shape.WindowNights} are stored under section 17's proposed values."));
        region.Append(shape.Crossed ? " The trigger is crossed, and shape calibration is due." : string.Empty);
        region.Append("</p>");

        region.Append(Invariant, $"<p class=\"event-line\" data-event=\"{(shape.Tonight is null ? "false" : "true")}\">");
        if (shape.Tonight is { } flooded)
        {
            region.Append(Invariant, $"The night of {shape.Night:yyyy-MM-dd} is an event night: {Causes(flooded)}. It is counted, and no median below reads it.");
        }
        else
        {
            region.Append(Invariant, $"The night of {shape.Night:yyyy-MM-dd} is an ordinary night, or one the filter stored nothing for.");
        }

        region.Append("</p>");

        region.Append("<div class=\"tbl-wrap\"><table class=\"shape-table\"><tr><th>Through</th><th>Median over the ordinary nights</th><th>Band, proposed</th></tr>");

        foreach (var banded in shape.Gates.Append(shape.List))
        {
            region.Append(Invariant, $"<tr data-measure=\"{Escaped(banded.Measure)}\" data-median=\"{(banded.Median is { } median ? median.ToString("R", Invariant) : "none")}\" data-low=\"{banded.Low}\" data-high=\"{banded.High}\" data-measured=\"{(shape.Crossed ? "true" : "false")}\">");
            region.Append(Invariant, $"<td>{Escaped(banded.Measure)}</td>");
            region.Append(banded.Median is { } drawn
                ? Formatted($"<td class=\"{(shape.Crossed ? "num" : "num not-yet")}\">{drawn:0.#}{(shape.Crossed ? string.Empty : ", not yet measured")}</td>")
                : "<td class=\"not-yet\">no ordinary night yet</td>");
            region.Append(Invariant, $"<td class=\"num\">{banded.Low} to {banded.High}</td></tr>");
        }

        region.Append("</table></div>");

        region.Append(Invariant, $"<p class=\"event-sessions\" data-events=\"{shape.Events.Count}\">");
        if (shape.Events.Count == 0)
        {
            region.Append("No event night among the nights in the window.");
        }
        else
        {
            region.Append("Event nights, left out of every median: ");

            for (var at = 0; at < shape.Events.Count; at++)
            {
                region.Append(Invariant, $"{(at == 0 ? string.Empty : "; ")}{shape.Events[at].Session:yyyy-MM-dd}, {Causes(shape.Events[at])}");
            }

            region.Append('.');
        }

        region.Append("</p>");

        region.Append("<div class=\"tbl-wrap\"><table class=\"reason-context\"><tr><th>Reason, as context</th><th>On the night</th><th>Median over the ordinary nights</th></tr>");

        foreach (var reason in shape.Reasons)
        {
            region.Append(Invariant, $"<tr data-reason=\"{Escaped(reason.Reason)}\" data-share=\"{(reason.Tonight is { } share ? share.ToString("R", Invariant) : "none")}\" data-median=\"{(reason.Median is { } median ? median.ToString("R", Invariant) : "none")}\">");
            region.Append(Invariant, $"<td>{Escaped(reason.Reason)}</td>");
            region.Append(reason.Tonight is { } tonight ? $"<td>{Figures.Share(tonight)}</td>" : "<td>not evaluated under its current rule</td>");
            region.Append(reason.Median is { } typical ? $"<td>{Figures.Share(typical)}</td>" : "<td>no ordinary night yet</td>");
            region.Append("</tr>");
        }

        region.Append("</table></div>");

        region.Append("<h4>What else is waiting on a count</h4><ul class=\"triggers\">");

        foreach (var line in triggers)
        {
            region.Append(Invariant, $"<li data-trigger=\"{Escaped(line.Obligation)}\" data-count=\"{line.Count}\" data-of=\"{line.Trigger}\">{line.Count} of {line.Trigger}: {Escaped(line.Says)}</li>");
        }

        region.Append("</ul></section>");

        return region.ToString();
    }

    // The Calibration region's edge half, section 15.10's row: each swing family candidate standing, the
    // live filter first, with the filter version it was defined against and the settings the live filter
    // has moved since, its non-empty blocks against the floor its first look is read at, its resolved
    // setups, and, from the floor on, its share against its planned break-even and its calibrated null;
    // below the floor no figure is drawn. What the first look can do and when a promotion can first come
    // are stated once for all of them.
    // owes: The swing family's first look
    // see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
    public string Edge(EdgeView? edge)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"edge-clock\" data-candidates=\"{edge?.Candidates.Count ?? 0}\">");
        region.Append("<h4>The edge clock</h4>");

        if (edge is null || edge.Candidates.Count == 0)
        {
            region.Append("<p class=\"degraded\" data-edge=\"none\">No swing family candidate stands registered, so the edge clock has not started.</p></section>");

            return region.ToString();
        }

        region.Append(Invariant, $"<p class=\"looks\" data-first-look=\"{edge.FirstLookSessions}\" data-promotion=\"{edge.PromotionSessions}\">");
        region.Append(Invariant, $"Each candidate's first look is read at {Looks.At[0]} non-empty blocks, no earlier than {edge.FirstLookSessions} sessions after its first night, and it can retire the candidate or leave it and cannot promote it; a promotion can come no earlier than the look at {Looks.At[1]} blocks, {edge.PromotionSessions} sessions after its first night.</p>");
        region.Append("<div class=\"tbl-wrap\"><table class=\"edge-table\"><tr><th>Candidate</th><th>Defined against</th><th>Sessions run</th><th class=\"num\">Blocks</th><th class=\"num\">Resolved setups</th><th>Against its null</th></tr>");

        foreach (var candidate in edge.Candidates)
        {
            var record = candidate.Record;
            var resolved = record.Setups + record.NotYetInABlock;

            region.Append(Invariant, $"<tr data-candidate=\"{Escaped(candidate.Candidate)}\" data-live=\"{(candidate.Live ? "true" : "false")}\" data-defined=\"{Escaped(candidate.DefinedAgainst)}\" data-moved=\"{Escaped(string.Join(",", candidate.Moved))}\" ");
            region.Append(Invariant, $"data-sessions=\"{candidate.SessionsRun}\" data-blocks=\"{record.Blocks}\" data-floor=\"{record.Floor}\" data-resolved=\"{resolved}\" data-withheld=\"{(record.Blocks < record.Floor ? "true" : "false")}\">");
            region.Append(Invariant, $"<td>{Escaped(candidate.Candidate)}</td>");
            region.Append(candidate.Live
                ? Formatted($"<td>version {Escaped(candidate.DefinedAgainst)}, the live settings</td>")
                : candidate.Moved.Count == 0
                    ? Formatted($"<td>version {Escaped(candidate.DefinedAgainst)}, and the live filter has not moved since</td>")
                    : Formatted($"<td>version {Escaped(candidate.DefinedAgainst)}; the live filter has since moved {Escaped(string.Join(", ", candidate.Moved))}</td>"));
            region.Append(candidate.First is { } first
                ? "<td>" + candidate.SessionsRun.ToString(CultureInfo.InvariantCulture) + " since its first night, " + first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "</td>"
                : "<td>no night has evaluated it yet</td>");
            region.Append(Invariant, $"<td class=\"num\">{record.Blocks} of {record.Floor}</td><td class=\"num\">{resolved}</td>");
            region.Append(record.Blocks < record.Floor || record.Share is null
                ? Formatted($"<td class=\"not-yet\">withheld until {record.Floor} non-empty blocks</td>")
                : Formatted($"<td>{EdgeClock.Figure(record.Share.Value)}% reached target before stop, against a planned break-even of {(record.PlannedBreakEven is { } even ? EdgeClock.Figure(even) + "%" : "none stored")} and a calibrated null of {(record.NullShare is { } bar ? EdgeClock.Figure(bar) + "%" : "none stored")}</td>"));
            region.Append("</tr>");
        }

        region.Append("</table></div></section>");

        return region.ToString();
    }

    // The near misses, section 15.10's row: beside each gate and each exclusion the setups it alone
    // rejected, every other gate passing, and the setups the filter admitted, each group read against its
    // own planned break-even and calibrated null, and withheld below the block floor.
    // see: A gate's near misses are the setups it alone rejected, each group read against its own break-even and null and withheld below the block floor
    public string NearMisses(NearMissView? view)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"near-misses\" data-version=\"{Escaped(view?.Version ?? "none")}\" data-groups=\"{view?.Groups.Count ?? 0}\">");
        region.Append("<h4>Near misses</h4>");

        if (view is null || view.Version is null)
        {
            region.Append("<p class=\"degraded\">No filter version is open, so no near miss is read.</p></section>");

            return region.ToString();
        }

        region.Append(Invariant, $"<p>Over the rows filter version {Escaped(view.Version)} has stored{(view.From is { } from ? " from " + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty)}: the setups the filter admitted, and beside each gate and each exclusion the setups it alone rejected, every other gate passing and no other exclusion applying. Each group is its own population, read against its own planned break-even and calibrated null, and no figure is drawn below {Blocks.Floor} non-empty blocks. A member the setup gate rejects has no band to stop below, so it has no plan to score.</p>");
        region.Append("<div class=\"tbl-wrap\"><table class=\"near-miss-table\"><tr><th>Setups</th><th class=\"num\">Rows</th><th class=\"num\">Resolved</th><th class=\"num\">Blocks</th><th>Against its break-even and null</th></tr>");

        foreach (var group in view.Groups)
        {
            var record = group.Record;
            var resolved = record.Setups + record.NotYetInABlock;
            var name = group.Kind switch
            {
                EdgeClock.Admitted => "admitted by the filter",
                EdgeClock.Gate => "rejected by " + group.Group + " alone",
                _ => "removed by " + group.Group + " alone",
            };

            region.Append(Invariant, $"<tr data-group=\"{Escaped(group.Group)}\" data-kind=\"{Escaped(group.Kind)}\" data-rows=\"{group.Rows}\" data-resolved=\"{resolved}\" data-blocks=\"{record.Blocks}\" data-withheld=\"{(record.Blocks < record.Floor ? "true" : "false")}\">");
            region.Append(Invariant, $"<td>{Escaped(name)}</td><td class=\"num\">{group.Rows}</td><td class=\"num\">{resolved}</td><td class=\"num\">{record.Blocks} of {record.Floor}</td>");
            region.Append(record.Blocks < record.Floor || record.Share is null
                ? Formatted($"<td class=\"not-yet\">withheld until {record.Floor} non-empty blocks</td>")
                : Formatted($"<td>{EdgeClock.Figure(record.Share.Value)}% reached target before stop, against a planned break-even of {(record.PlannedBreakEven is { } even ? EdgeClock.Figure(even) + "%" : "none stored")} and a calibrated null of {(record.NullShare is { } bar ? EdgeClock.Figure(bar) + "%" : "none stored")}</td>"));
            region.Append("</tr>");
        }

        region.Append("</table></div></section>");

        return region.ToString();
    }

    // The newest shape proposal beneath the shape clock: what it proposes gate by gate, the list's median
    // held and proposed, the gates it names as findings rather than forcing, its decision, and beside it
    // the blocks accepting it would restart, the count the shape command holds a later acceptance to.
    // see: The shape proposer moves one setting a gate, nearest first, and never applies what it proposes
    // see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts
    public string Proposal(ProposalView? proposal)
    {
        var region = new StringBuilder();

        region.Append("<section class=\"shape-proposal\"><h4>The shape proposal</h4>");

        if (proposal is null)
        {
            region.Append("<p class=\"proposal\" data-proposal=\"none\">No shape proposal is stored. The proposer writes one once the shape clock's trigger is crossed, and nothing moves until you accept it.</p></section>");

            return region.ToString();
        }

        var decided = proposal.Decision switch
        {
            null => Formatted($"waiting on your decision: shape --accept {proposal.Id} opens it as the next filter version, and shape --reject {proposal.Id} --reason records why not"),
            "accepted" => $"accepted, opening filter version {Escaped(proposal.Opened ?? "none")}",
            _ => $"rejected: {Escaped(proposal.Reason ?? string.Empty)}",
        };

        region.Append(Invariant, $"<p class=\"proposal\" data-proposal=\"{proposal.Id}\" data-version=\"{Escaped(proposal.Version)}\" data-ordinary=\"{proposal.Ordinary}\" data-decision=\"{Escaped(proposal.Decision ?? "none")}\">");
        region.Append(Invariant, $"Proposal {proposal.Id}, written on the night of {proposal.Session:yyyy-MM-dd} over {proposal.Ordinary} ordinary nights under filter version {Escaped(proposal.Version)}, is {decided}.</p>");

        region.Append("<div class=\"tbl-wrap\"><table class=\"proposal-levers\"><tr><th>Through</th><th>Setting</th><th>Held</th><th>Proposed</th><th>Median held</th><th>Median proposed</th><th>Band</th></tr>");

        foreach (var lever in proposal.Levers)
        {
            region.Append(Invariant, $"<tr data-gate=\"{Escaped(lever.Gate)}\" data-setting=\"{Escaped(lever.Setting ?? "none")}\" data-current=\"{Raw(lever.Current)}\" data-proposed=\"{Raw(lever.Proposed)}\" data-median-now=\"{Raw(lever.MedianNow)}\" data-median-proposed=\"{Raw(lever.MedianProposed)}\">");
            region.Append(Invariant, $"<td>{Escaped(lever.Gate)}</td>");
            region.Append(lever.Setting is { } setting
                ? $"<td>{Escaped(setting)}</td><td class=\"num\">{Setting(lever.Current)}</td><td class=\"num\">{(lever.Proposed is { } moved ? Setting(moved) : "none brings it inside")}</td>"
                : "<td>no threshold</td><td></td><td></td>");
            region.Append(Invariant, $"<td class=\"num\">{Median(lever.MedianNow)}</td><td class=\"num\">{Median(lever.MedianProposed)}</td><td class=\"num\">{lever.Low} to {lever.High}</td></tr>");
        }

        region.Append(Invariant, $"<tr data-gate=\"the list\" data-median-now=\"{Raw(proposal.ListNow)}\" data-median-proposed=\"{Raw(proposal.ListProposed)}\"><td>the list</td><td></td><td></td><td></td>");
        region.Append(Invariant, $"<td class=\"num\">{Median(proposal.ListNow)}</td><td class=\"num\">{Median(proposal.ListProposed)}</td><td class=\"num\">{ShapeClock.ListLow} to {ShapeClock.ListHigh}</td></tr>");
        region.Append("</table></div>");

        if (proposal.Findings.Count == 0)
        {
            region.Append("<p class=\"proposal-findings\" data-findings=\"0\">No finding: every gate with a threshold has a value in its range that brings its median inside its band.</p>");
        }
        else
        {
            region.Append(Invariant, $"<ul class=\"proposal-findings\" data-findings=\"{proposal.Findings.Count}\">");

            foreach (var finding in proposal.Findings)
            {
                region.Append(Invariant, $"<li>{Escaped(finding)}</li>");
            }

            region.Append("</ul>");
        }

        region.Append(Invariant, $"<p class=\"restarts\" data-live=\"{Escaped(proposal.Live ?? "none")}\" data-accepted-while-live=\"{proposal.AcceptedWhileLive}\" data-blocks=\"{proposal.Blocks}\">");
        region.Append(proposal switch
        {
            { Live: null } => "No live filter candidate is registered, so accepting restarts nothing.",
            { Live: { } live, AcceptedWhileLive: 0 } => Formatted($"Accepting it restarts '{Escaped(live)}', which has run {proposal.Blocks} non-empty block(s) of the {Blocks.Floor} its first look reads. It is the first acceptance while the list is live, which costs its restart and states nothing more."),
            { Live: { } live } => Formatted($"Accepting it restarts the {proposal.Blocks} non-empty block(s) '{Escaped(live)}' has run, of the {Blocks.Floor} its first look reads. Shape is frozen after one acceptance while the list is live, so the command states them: shape --accept {proposal.Id} --restarts {proposal.Blocks}."),
        });
        region.Append("</p></section>");

        return region.ToString();
    }

    static string Raw(double? value) => value is { } some ? some.ToString("R", Invariant) : "none";

    static string Setting(double? value) => value is { } some ? some.ToString("0.00", Invariant) : "none";

    static string Median(double? value) => value is { } some ? some.ToString("0.#", Invariant) : "no ordinary night";

    // The line at the top of the run page once the shape clock's trigger is crossed.
    public string ShapeDue(ShapeState shape) =>
        shape.Crossed
            ? Formatted($"<p class=\"due\" data-due=\"shape\">Shape calibration is due: {shape.Ordinary} ordinary nights under filter version {Escaped(shape.Version)} are stored, against the {shape.Wanted} it waits on.</p>")
            : string.Empty;

    static string Causes(EventNight night) =>
        string.Join(
            " and ",
            night.Floods
                .Select(flood => $"{Escaped(flood.Measure)} at {Figures.Share(flood.Share)} of the index against its median of {Figures.Share(flood.Median)}")
                .Concat(night.VolumeRatio is { } ratio ? [Formatted($"the index trading at {ratio:0.00} times its fifty-day volume")] : []));

    public string ShadowCandidates(ShadowRegion shadow)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"shadow-candidates\" data-shadow=\"{shadow.Registered}\" data-divisor=\"{shadow.Divisor}\" data-maximum=\"{shadow.Maximum}\">");

        region.Append(shadow is { Registered: 0, Divisor: 0 }
            ? Formatted($"<p data-shadow=\"none\">no candidate condition is registered as this page is read, so the family's divisor is none; the candidate family may hold at most {shadow.Maximum}</p>")
            : Formatted($"<p data-shadow=\"{shadow.Registered}\">{shadow.Registered} candidate condition(s) registered as this page is read, of at most {shadow.Maximum}, and the family's divisor is {shadow.Divisor}</p>"));

        // Beside the divisor, the count the level is actually shared across and the bar it sets, with what
        // each look releases of it, because a verdict is read against the bar and not against the divisor.
        // see: Holm's level passes between the candidates by a graph fixed when they are registered, and its first step is 0.05 over the distinct trials read at a look or still running
        if (shadow.Level is { } level && shadow.Releases.Count >= 2)
        {
            region.Append(Invariant, $"<p data-trials=\"{shadow.Trials}\" data-level=\"{level:0.#####}\" data-releases=\"{string.Join(", ", shadow.Releases.Select(release => release.ToString("0.#####", Invariant)))}\">");
            region.Append(Invariant, $"{shadow.Trials} distinct trial(s) counted, the rules still running and the rules a look has read, so the level at Holm's first step is 0.05 over {shadow.Trials}, {level:0.#####}, ");
            region.Append(Invariant, $"which a candidate's looks release as {shadow.Releases[0]:0.#####} by the first, {shadow.Releases[1]:0.#####} by the second and {shadow.Releases[^1]:0.#####} by the last");
            region.Append(shadow.FirstLookPromotes
                ? "</p>"
                : ", the first below the 1 in 256 that eight blocks can reach, so it can retire a candidate and never promote one</p>");
        }
        else
        {
            region.Append("<p data-trials=\"0\">no distinct trial is counted, since no rule is running and no look has read one, so no level is divided</p>");
        }

        region.Append("<p data-withheld=\"true\">each candidate's own record is withheld until it is promoted, and its picks are drawn in the comparison of tonight's picks alone</p>");

        region.Append("</section>");

        return region.ToString();
    }

    // The order tonight's list is drawn in, measured against the order it replaced.
    //
    // Each order's counts over the rows it would have drawn, the old order named as the benchmark,
    // and no comparison until every order holds the floor of blocks: a comparison drawn before then
    // is a reading taken on too little to mean anything, and one read nightly and acted on when it
    // looks good is the choice made on the figure the rule exists to keep out of it.
    // see: The order tonight's list is drawn in is compared against the order it replaces, declared before any record is read
    // see: A listing records the band strength the old order read, and the three orders are compared over the nights that recorded it
    public string TonightsOrder(OrderComparison comparison)
    {
        var region = new StringBuilder();
        var fewest = comparison.Orders.Count == 0 ? 0 : comparison.Orders.Min(order => order.Blocks);
        var due = comparison.Orders.Count > 0 && fewest >= comparison.Floor;

        region.Append(Invariant, $"<section class=\"tonights-order\" data-from=\"{(comparison.From is { } from ? from.ToString("yyyy-MM-dd", Invariant) : "none")}\" ");
        region.Append(Invariant, $"data-nights=\"{comparison.Nights}\" data-drawn=\"{comparison.Drawn}\" data-block-sessions=\"{comparison.BlockSessions}\" data-floor=\"{comparison.Floor}\" data-compared=\"{(due ? "due" : "false")}\">");

        if (comparison.From is not { } first)
        {
            region.Append("<p data-orders=\"none\">no night's listings record the band strength the old order reads yet, so no order is measured</p></section>");

            return region.ToString();
        }

        region.Append(Invariant, $"<p>Measured over the {comparison.Nights} night(s) from {first:yyyy-MM-dd} whose listings record what each order reads, the first {comparison.Drawn} rows each order would have drawn on each of them.</p>");
        region.Append("<div class=\"tbl-wrap\"><table class=\"orders\"><thead><tr><th>Order</th><th class=\"r\">Setups drawn</th><th class=\"r\">Whole window closed</th>");
        region.Append(Invariant, $"<th class=\"r\">Blocks of {comparison.BlockSessions} sessions holding one</th></tr></thead><tbody>");

        foreach (var order in comparison.Orders)
        {
            region.Append(Invariant, $"<tr data-order=\"{Escaped(order.Key)}\" data-benchmark=\"{(order.Benchmark ? "true" : "false")}\" data-setups=\"{order.Setups}\" data-closed=\"{order.Closed}\" data-blocks=\"{order.Blocks}\">");
            region.Append(Invariant, $"<td>{Escaped(order.Name)}{(order.Benchmark ? " <b>(the benchmark)</b>" : string.Empty)}</td>");
            region.Append(Invariant, $"<td class=\"r num\">{order.Setups}</td><td class=\"r num\">{order.Closed}</td><td class=\"r num\">{order.Blocks} of {comparison.Floor}</td></tr>");
        }

        region.Append("</tbody></table></div>");

        region.Append(due
            ? Formatted($"<p data-compared=\"due\">every order holds at least {comparison.Floor} blocks with a setup whose whole window has closed, so the comparison against the benchmark is due; switching needs it to reject over both challengers at 0.05</p>")
            : Formatted($"<p data-compared=\"false\">no comparison is drawn: it needs every order to hold {comparison.Floor} blocks of {comparison.BlockSessions} sessions with a setup whose whole window has closed, and the fewest any order holds is {fewest}</p>"));

        region.Append("<p data-measure=\"true\">each order is measured by the wins among those setups over what the calibrated null says a setup with no edge wins, block by block, the same excess a candidate is judged on</p>");
        region.Append("</section>");

        return region.ToString();
    }

    // What each registered candidate's record has come to, and what its next look waits for.
    //
    // A record and never a name: the counts are over setups and the sessions they were listed on,
    // and how a candidate's pick of a name turned out reaches no screen until a look reads it.
    // The running figure is drawn as monitoring and the verdict field beside it changes only at a
    // look, which is the whole of the arrangement: a figure read every night and acted on when it
    // looks good is the choice the looks exist to keep out of the decision.
    // see: The nightly running figure is monitoring and never the verdict
    // see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    // The trend rule's versions: what each labelled the night's names, how far each stands from
    // the live rule, and how often a label the stored nights show went away and came back.
    //
    // The returns are drawn whether or not a version is open, because they are the reading the
    // confirmation version's own number is settled from and that reading is about the stored
    // labels rather than about any version of them.
    // owes: The trend confirmation's nights settled from flip-backs
    // see: A trend version is judged by the candidates' test on its difference from the live rule
    public string TrendVersions(TrendVersionRegion region)
    {
        var drawn = new StringBuilder();

        drawn.Append(Invariant, $"<section class=\"trend-versions\" data-open=\"{region.Open}\" data-most-at-once=\"{region.MostAtOnce}\" ");
        drawn.Append(Invariant, $"data-nights=\"{region.Nights}\" data-margin=\"{region.Margin:0.#}\">");

        drawn.Append(Invariant, $"<p data-labels=\"live\">On {region.Night:yyyy-MM-dd} the live rule labelled ");
        drawn.Append(Labels(region.Live));
        drawn.Append(".</p>");

        drawn.Append(Invariant, $"<p data-returns=\"true\" data-pairs=\"{region.Returns.Pairs}\" data-flipped=\"{region.Returns.Flipped}\" ");
        drawn.Append(Invariant, $"data-returned-next=\"{region.Returns.ReturnedTheNextNight}\" data-returned-within-two=\"{region.Returns.ReturnedWithinTwo}\">");
        drawn.Append(Invariant, $"Over {region.Nights} stored night(s), a label changed on {region.Returns.Flipped} of {region.Returns.Pairs} night-to-night pairs, ");
        drawn.Append(Invariant, $"and the old label came back the next night {region.Returns.ReturnedTheNextNight} time(s) and within two nights {region.Returns.ReturnedWithinTwo}. ");
        drawn.Append("A version that holds the new label for a night or two is measured against that and settled by no other reading.</p>");

        if (region.Versions.Count == 0)
        {
            drawn.Append(Invariant, $"<p data-versions=\"none\">no version of the trend rule has an open window, of the {region.MostAtOnce} windows the bound allows at once</p></section>");

            return drawn.ToString();
        }

        foreach (var version in region.Versions)
        {
            // The instant and not the date it falls on, here and in the prose, because a window
            // closed and opened again under one name can open twice on one day and the two are
            // two records.
            // see: A version's record belongs to the window its scores were written under and never to the version's name
            drawn.Append(Invariant, $"<article class=\"trend-version\" data-version=\"{Escaped(version.Version)}\" data-parameters=\"{Escaped(version.Parameters)}\" ");
            drawn.Append(Invariant, $"data-opened=\"{Opened(version.OpenedAt)}\" data-moved=\"{version.Moved}\">");
            drawn.Append(Invariant, $"<h4>{Escaped(version.Version)}</h4>");
            drawn.Append(Invariant, $"<p data-labels=\"version\">Opened {Opened(version.OpenedAt)} at {Escaped(version.Parameters)}. It labelled ");
            drawn.Append(Labels(version.Labels));
            drawn.Append(Invariant, $", which moves {version.Moved} name(s) off the live rule's label.</p>");

            if (version.Record is { } record)
            {
                drawn.Append(Invariant, $"<p data-field=\"verdict\" data-blocks=\"{record.Blocks}\" data-floor=\"{record.Floor}\" ");
                drawn.Append(Invariant, $"data-live-setups=\"{record.LiveSetups}\" data-version-setups=\"{record.VersionSetups}\" ");
                drawn.Append(Invariant, $"data-excess=\"{Figure(record.Excess)}\" data-p=\"{Figure(record.PValue)}\" data-verdict=\"{Escaped(record.Verdict)}\">");
                drawn.Append(Invariant, $"{Escaped(record.Verdict)}. {record.VersionSetups} setup(s) against the live rule's {record.LiveSetups}, ");
                drawn.Append(Invariant, $"over {record.Blocks} block(s) of the {record.Floor} a verdict is read at.</p>");
            }

            drawn.Append("</article>");
        }

        if (region.Best is { } best)
        {
            drawn.Append(Invariant, $"<p data-reality-check=\"true\" data-best=\"{Escaped(best.Best)}\" data-best-opened=\"{Opened(best.BestOpenedAt)}\" ");
            drawn.Append(Invariant, $"data-p=\"{best.PValue:0.#####}\" data-challengers=\"{best.Challengers}\">");
            drawn.Append(Invariant, $"Of {best.Challengers} version(s) read against the live rule as the benchmark, the largest difference is '{Escaped(best.Best)}' opened {Opened(best.BestOpenedAt)}, ");
            drawn.Append(Invariant, $"at {best.PValue:0.#####} over every sign vector its blocks allow, which is the figure the best of several is read at ");
            drawn.Append(Invariant, $"and never its own. Two that both cross keep the narrower, unless the wider is ahead by {region.Margin:0.#} point(s).</p>");
        }
        else
        {
            drawn.Append(Invariant, $"<p data-reality-check=\"none\">nothing is compared until every open version holds the same {Blocks.Floor} whole blocks, ");
            drawn.Append("because the best of several is read against the benchmark over one set of blocks and not each over its own</p>");
        }

        drawn.Append("</section>");

        return drawn.ToString();
    }

    // A window's instant as every surface draws it, which is the form the store holds it in.
    static string Opened(DateTimeOffset at) => RuleVersions.Stored(at);

    static string Labels(IReadOnlyList<LabelCount> labels) =>
        labels.Count == 0
            ? "no name at all"
            : string.Join(", ", labels.Select(label => Formatted($"{label.Names} {Escaped(label.Label)}")));

    public string CandidateRecords(CandidateRegion region)
    {
        var drawn = new StringBuilder();
        var looks = string.Join(", ", region.LooksAt.Select(look => look.ToString(Invariant)));

        drawn.Append(Invariant, $"<section class=\"candidate-records\" data-registered=\"{region.Registered}\" data-standing=\"{region.Standing}\" ");
        drawn.Append(Invariant, $"data-looks=\"{looks}\" data-floor=\"{region.Floor}\" data-block-sessions=\"{region.BlockSessions}\" ");
        drawn.Append(Invariant, $"data-cost=\"{region.Cost:0.#}\" data-sensitivity=\"{region.Sensitivity:0.#}\">");

        if (region.Candidates.Count == 0)
        {
            drawn.Append("<p data-records=\"none\">no candidate condition has been registered, so there is no record to read</p></section>");

            return drawn.ToString();
        }

        // The count of distinct trials beside every verdict, which is what the level at the graph's first
        // step is divided by: the rules still running and the rules a look has read, so the names ever
        // registered, retirements taken unread among them, are not what a verdict is judged against.
        // see: Holm's level passes between the candidates by a graph fixed when they are registered, and its first step is 0.05 over the distinct trials read at a look or still running
        drawn.Append(Invariant, $"<p data-trials=\"{region.Trials}\">{region.Registered} candidate condition(s) have ever been registered, which are {region.Trials} distinct trial(s) counted, ");
        drawn.Append(Invariant, $"the rules still running and the rules a look has read, and the level at the graph's first step is {region.Significance:0.##} over them. ");
        drawn.Append(Invariant, $"A verdict is read at {looks} non-empty blocks of {region.BlockSessions} sessions and at no other time, ");
        drawn.Append(Invariant, $"over the setups whose whole outcome window has closed, against a bar simulated from each setup's own plan at a round trip of {region.Cost:0.#} basis points, with {region.Sensitivity:0.#} shown beside it.</p>");

        foreach (var candidate in region.Candidates)
        {
            var record = candidate.Record;

            drawn.Append(Invariant, $"<article class=\"candidate\" data-candidate=\"{Escaped(candidate.Candidate)}\" data-standing=\"{(candidate.Standing ? "true" : "false")}\" ");
            drawn.Append(Invariant, $"data-level=\"{candidate.Level:0.######}\" data-step=\"{candidate.Step}\" data-blocks=\"{record.Blocks}\" data-floor=\"{record.Floor}\" ");
            drawn.Append(Invariant, $"data-setups=\"{record.Setups}\" data-withheld=\"{Escaped(record.Withheld)}\" data-verdict=\"{Escaped(record.Verdict)}\">");
            drawn.Append(Invariant, $"<h4>{Escaped(candidate.Candidate)}{(candidate.Standing ? string.Empty : " <b>(retired)</b>")}</h4>");

            // The verdict field: what the last look read, how many looks are left, and what the
            // next one waits for. It is the field a nightly reading never moves.
            drawn.Append(Invariant, $"<p data-field=\"verdict\">{Escaped(record.Verdict)}. ");
            drawn.Append(Invariant, $"Step {candidate.Step} of the graph, at a level of {candidate.Level:0.#####} of the {region.Significance:0.##} the family is tested at. ");
            drawn.Append(record.NextLookAt is { } next
                ? Formatted($"{record.LooksRemaining} look(s) remain, the next at {next} non-empty blocks, and {record.Blocks} of {record.Floor} stand.</p>")
                : Formatted($"No look remains, and {record.Blocks} block(s) stand.</p>"));

            if (record.Withheld != CandidateRecord.Shown)
            {
                drawn.Append(Invariant, $"<p class=\"degraded\" data-withheld=\"{Escaped(record.Withheld)}\">no verdict is read below {record.Floor} non-empty blocks, and {record.Blocks} stand</p>");
            }

            drawn.Append(Invariant, $"<p data-monitoring=\"true\" data-share=\"{Figure(record.Share)}\" data-null=\"{Figure(record.NullShare)}\" ");
            drawn.Append(Invariant, $"data-excess=\"{Figure(record.Excess)}\" data-p=\"{Figure(record.PValue)}\" data-poisson-binomial=\"{Figure(record.PoissonBinomial)}\">");
            drawn.Append(record.Share is { } share && record.NullShare is { } bar
                ? Formatted($"Monitoring, not the verdict: {record.Setups} setup(s) over {record.Blocks} whole block(s), winning {share:0.0}% against the {bar:0.0}% a setup with no edge wins")
                : Formatted($"Monitoring, not the verdict: no setup has had its whole outcome window inside a whole block yet"));
            drawn.Append(record.PValue is { } running ? Formatted($", sign-flip {running:0.0000}") : string.Empty);
            drawn.Append(record.PoissonBinomial is { } beside ? Formatted($", and {beside:0.0000} on the Poisson binomial, which assumes the setups are independent and decides nothing") : string.Empty);
            drawn.Append(Invariant, $". {record.NotYetInABlock} closed setup(s) sit in a block that is not whole yet.</p>");

            if (record.Looks.Count > 0)
            {
                drawn.Append("<div class=\"tbl-wrap\"><table class=\"looks\"><thead><tr><th>Look</th><th class=\"r\">Setups</th><th class=\"r\">Won</th>");
                drawn.Append("<th class=\"r\">The bar</th><th class=\"r\">Sign-flip</th><th class=\"r\">Level spent</th><th class=\"r\">Smallest excess it could detect</th><th>Read</th></tr></thead><tbody>");

                foreach (var look in record.Looks)
                {
                    drawn.Append(Invariant, $"<tr data-look=\"{look.Blocks}\" data-setups=\"{look.Setups}\" data-share=\"{look.Share:0.0}\" data-null=\"{look.NullShare:0.0}\" ");
                    drawn.Append(Invariant, $"data-p=\"{look.PValue:0.0000}\" data-spends=\"{look.Spends:0.######}\" data-crossed=\"{(look.Crossed ? "true" : "false")}\" ");
                    drawn.Append(Invariant, $"data-futile=\"{(look.Futile ? "true" : "false")}\" data-smallest-excess=\"{Figure(look.SmallestExcess)}\">");
                    drawn.Append(Invariant, $"<td>{look.Blocks} blocks</td><td class=\"r num\">{look.Setups}</td><td class=\"r num\">{look.Share:0.0}%</td>");
                    drawn.Append(Invariant, $"<td class=\"r num\">{look.NullShare:0.0}%</td><td class=\"r num\">{look.PValue:0.0000}</td><td class=\"r num\">{look.Spends:0.#####}</td>");
                    drawn.Append(look.SmallestExcess is { } smallest
                        ? Formatted($"<td class=\"r num\">{smallest:0.0} points</td>")
                        : "<td class=\"r\"><span class=\"degraded\">none stated</span></td>");
                    drawn.Append(look.Crossed
                        ? "<td>crossed its boundary</td>"
                        : look.Futile ? "<td>below its own bar, which the futility guideline reads</td>" : "<td>did not cross</td>");
                    drawn.Append("</tr>");
                }

                drawn.Append("</tbody></table></div>");
                drawn.Append("<p data-approximation=\"true\">the smallest excess a look could detect is a normal approximation over the setups its blocks hold, widened by the design effect, where every other figure here is counted or enumerated whole</p>");
            }

            drawn.Append(Invariant, $"<p data-reported=\"true\" data-design-effect=\"{Figure(record.DesignEffect)}\" data-realized-loss=\"{Figure(record.RealizedLoss)}\" ");
            drawn.Append(Invariant, $"data-realized-loss-high=\"{Figure(record.RealizedLossHigh)}\" data-realized-break-even=\"{Figure(record.RealizedBreakEven)}\" ");
            drawn.Append(Invariant, $"data-planned-break-even=\"{Figure(record.PlannedBreakEven)}\" data-same-session=\"{record.SameSession}\" data-earnings=\"{record.EarningsStopOuts}\">");
            drawn.Append("Reported and tested nowhere: ");
            drawn.Append(record.DesignEffect is { } effect
                ? Formatted($"a design effect of {effect:0.00}, noisy near the floor of blocks; ")
                : "no design effect yet; ");
            drawn.Append(record.RealizedLoss is { } lost && record.RealizedLossHigh is { } worst
                ? Formatted($"a loss costing {lost:0.00} times the planned risk on average and {worst:0.00} at the ninetieth of them; ")
                : "no realized loss yet; ");
            drawn.Append(record.RealizedBreakEven is { } realized && record.PlannedBreakEven is { } planned
                ? Formatted($"a realized break-even of {realized:0.0}% against the {planned:0.0}% the plans stated; ")
                : "no realized break-even yet; ");
            drawn.Append(Invariant, $"{record.SameSession} setup(s) entered and stopped on one session, counted as losses against the bar the worst fill in the zone sets; ");
            drawn.Append(Invariant, $"and {record.EarningsStopOuts} stopped out on a session the name reported on.</p>");

            drawn.Append(Invariant, $"<p data-proposed=\"{Escaped(candidate.Proposed)}\">registered with {Escaped(candidate.Proposed)}, which is what it is being judged at: a changed number is a new registration with a window of its own and never a change to this one</p>");
            drawn.Append("</article>");
        }

        drawn.Append("<p data-withheld=\"true\">no candidate's pick of a name is drawn here, and no record is drawn beside a ticker</p>");
        drawn.Append("</section>");

        return drawn.ToString();
    }

    static string Figure(double? value) => value is { } figure ? figure.ToString("0.######", Invariant) : "none";

    // The harness, section 15.10's last region: the verdict counts from the last
    // phase report, each separately.
    //
    // Separately because out of scope is counted apart from unexamined and only
    // one of them is a defect, and a page that summed them would report a build
    // that has not reached a claim as one that failed to check it.
    public string HarnessVerdicts(HarnessCounts? counts)
    {
        if (counts is not { } read)
        {
            return "<section class=\"harness\" data-report=\"none\">" +
                "<p class=\"degraded\">no phase report has been written on this machine</p></section>";
        }

        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"harness\" data-passed=\"{read.Passed}\" data-failed=\"{read.Failed}\" ");
        region.Append(Invariant, $"data-unexamined=\"{read.Unexamined}\" data-out-of-scope=\"{read.OutOfScope}\">");
        region.Append(Invariant, $"<p>{read.Passed} passed, {read.Failed} failed, {read.Unexamined} unexamined, ");
        region.Append(Invariant, $"{read.OutOfScope} out of scope</p>");
        region.Append("<p class=\"degraded\">out of scope is counted apart from unexamined and never added to it, because only unexamined is a defect</p>");
        region.Append("</section>");

        return region.ToString();
    }

    // Tonight's reason totals, section 15.7's last region: the reason track
    // across tonight's fired names, which says whether the evening is one thing
    // happening to many names or many things happening to a few.
    public string ReasonTotals(IReadOnlyList<ReasonTrackRow> tracks, int fired = 0)
    {
        var region = new StringBuilder();
        var names = tracks.Sum(track => track.Total);
        var denominator = fired > 0 ? fired : Math.Max(1, tracks.Count == 0 ? 1 : tracks.Max(track => track.Total));

        region.Append(Invariant, $"<section class=\"reason-totals\" data-reasons=\"{tracks.Count}\" ");
        region.Append(Invariant, $"data-names=\"{names}\">");

        // Tonight's counts, each out of tonight's fired names with the count written on
        // its bar. A setup listed tonight has no outcome yet, so the counts are what the
        // evening says and a reason's record over time is the run page's.
        // see: Tonight's reason totals are counts, and a reason's record is the run page's
        region.Append("<div class=\"tbl-wrap\">");
        region.Append("<table class=\"totals-table\"><tr><th>Reason</th><th>Names</th></tr>");

        foreach (var track in tracks)
        {
            const int Wide = 420;
            var bar = (double)track.Total / denominator * Wide;
            var inside = bar >= 64;
            var words = Formatted($"{track.Total} of {denominator}");

            region.Append(Invariant, $"<tr data-reason=\"{Escaped(track.Reason)}\" data-names=\"{track.Total}\">");
            region.Append(Invariant, $"<td><a href=\"#/run/?reason={Uri.EscapeDataString(track.Reason)}\">{Escaped(track.Reason)}</a></td><td>");
            region.Append(Invariant, $"<svg class=\"reason-count\" role=\"img\" viewBox=\"0 0 {Wide} 20\" width=\"{Wide}\" height=\"20\" data-count=\"{track.Total}\" data-of=\"{denominator}\" aria-label=\"{Escaped(track.Reason)}: {words} of tonight's fired names\">");
            region.Append(Invariant, $"<rect class=\"m-trk\" x=\"0\" y=\"2\" width=\"{Wide}\" height=\"16\"/>");
            region.Append(Invariant, $"<rect class=\"m-won\" x=\"0\" y=\"2\" width=\"{Number(bar)}\" height=\"16\"/>");
            region.Append(Invariant, $"<text class=\"{(inside ? "m-barlab-in" : "m-barlab-out")}\" x=\"{Number(inside ? bar - 6 : bar + 6)}\" y=\"14\" text-anchor=\"{(inside ? "end" : "start")}\">{words}</text>");
            region.Append("</svg></td></tr>");
        }

        region.Append("</table></div>");
        region.Append("<p class=\"degraded\" data-unresolved=\"all\">every name that fired tonight is a setup nothing has scored yet, so these are counts and not outcomes; each reason's record over time is on the run page</p>");
        region.Append("</section>");

        return region.ToString();
    }

    // The night header, section 15.7's first region.
    //
    // The fired count is the headline, because it is the market's mood and it is
    // the one number the twenty drawn rows cannot tell you. The quantities phase
    // 6 supplies are absent and say so rather than being drawn as zero, which
    // would read as a night that spent nothing because it did nothing.
    public string NightHeader(DateOnly night, int index, int fired, string? duration, HarnessCounts? harness, NightSpend? spend = null, NightProse? prose = null, MarketView? market = null, int? listed = null)
    {
        var header = new StringBuilder();

        header.Append(Invariant, $"<header class=\"night-header\" data-night=\"{night:yyyy-MM-dd}\" ");
        header.Append(Invariant, $"data-index=\"{index}\" data-fired=\"{fired}\" data-listed=\"{(listed is { } count ? count.ToString(Invariant) : "none")}\"><div class=\"night\">");

        // The headline, large, with the index it is out of beneath it: on a night the swing filter
        // listed, the names it passed, and the fired count as context beside it; before the switch, the
        // fired count.
        // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
        header.Append(listed is { } passed
            ? Formatted($"<div class=\"headline\" aria-hidden=\"true\"><div class=\"big\">{passed}</div><div class=\"cap\">names the swing filter passed<span>out of {index} in the index</span></div></div>")
            : Formatted($"<div class=\"headline\" aria-hidden=\"true\"><div class=\"big\">{fired}</div><div class=\"cap\">names fired<span>out of {index} in the index</span></div></div>"));
        header.Append("<div class=\"ops\">");

        if (listed is { } onTheList)
        {
            header.Append(Invariant, $"<p class=\"listed\" data-listed=\"{onTheList}\">{onTheList} of {index} name(s) passed the swing filter on {night:yyyy-MM-dd}</p>");
        }

        header.Append(Invariant, $"<p class=\"fired\">{fired} of {index} name(s) fired on {night:yyyy-MM-dd}{(listed is null ? string.Empty : ", as context")}</p>");
        header.Append(BreadthLine(market));
        header.Append(Invariant, $"<p class=\"duration\" data-duration=\"{Escaped(duration ?? "not recorded")}\">the night took {Escaped(duration ?? "a time the run log does not record")}</p>");

        // The harness verdict, which section 15.7 states in this header and
        // which stood only on the run page until 5.8. It is the same four
        // counts from the same report, drawn here in one line rather than in a
        // region of its own: this header answers how the evening went, and
        // whether the build that produced it is checked is part of that answer.
        //
        // Four counts and no total, for the reason the run page's own region
        // gives: out of scope is counted apart from unexamined and only one of
        // them is a defect.
        if (harness is { } counts)
        {
            header.Append(Invariant, $"<p class=\"harness-verdict\" data-passed=\"{counts.Passed}\" data-failed=\"{counts.Failed}\" ");
            header.Append(Invariant, $"data-unexamined=\"{counts.Unexamined}\" data-out-of-scope=\"{counts.OutOfScope}\">");
            header.Append(Invariant, $"harness: {counts.Passed} passed, {counts.Failed} failed, {counts.Unexamined} unexamined, ");
            header.Append(Invariant, $"{counts.OutOfScope} out of scope</p>");
        }
        else
        {
            header.Append("<p class=\"harness-verdict degraded\" data-report=\"none\">harness: no phase report has been written on this machine</p>");
        }

        // What research spent, from 6.7, which is where anything first spends: the
        // night's UTC day and its month to the end of that day, each beside its cap,
        // read off the run log's own rows. A night that spent nothing says so in the
        // same words, because nothing spent is a figure and not an absence.
        // see: The spend cap counts a UTC day and a UTC month, and refuses a call that could take spend past either
        if (spend is { } spent)
        {
            header.Append(Invariant, $"<p class=\"night-spend\" data-spent-day=\"{spent.OnTheDay}\" data-spent-month=\"{spent.MonthToDate}\" ");
            header.Append(Invariant, $"data-day-cap=\"{spent.DayCap}\" data-month-cap=\"{spent.MonthCap}\">");
            header.Append(Invariant, $"research spent {SpendVerdict.Money(spent.OnTheDay)} of the {SpendVerdict.Money(spent.DayCap)} day cap on {night:yyyy-MM-dd}, ");
            header.Append(Invariant, $"and {SpendVerdict.Money(spent.MonthToDate)} of the {SpendVerdict.Money(spent.MonthCap)} month cap in its month to that day</p>");
        }
        else
        {
            header.Append("<p class=\"degraded\" data-spend=\"absent\">what research spent is not read on this page</p>");
        }

        // Reports carrying fresh prose against reused, from 6.8, which is where a pass
        // first writes a section a reader is shown. Fresh is a report with a section
        // written on the night, reused is one whose every section predates it and was
        // shown again for nothing, and the population is stated beside the two: the names
        // with any written section as of the night, which is every report that carries
        // prose at all.
        // see: Deciding not to spend must not cost anything
        if (prose is { } written)
        {
            header.Append(Invariant, $"<p class=\"night-prose\" data-fresh=\"{written.Fresh}\" data-reused=\"{written.Reused}\" data-names=\"{written.Names}\">");
            header.Append(Invariant, $"research prose: {written.Fresh} report(s) carry prose written on {night:yyyy-MM-dd} and {written.Reused} carry only prose written before it, ");
            header.Append(Invariant, $"of the {written.Names} name(s) with a written section</p>");
        }
        else
        {
            header.Append("<p class=\"degraded\" data-prose=\"absent\">fresh prose against reused is not read on this page</p>");
        }

        header.Append("</div></div></header>");

        return header.ToString();
    }

    // The how-it-got-here table's rows, section 15.9's second region.
    //
    // The biggest moves of the stored year, largest first, each saying how many
    // sessions it spans so a five-day run reads as one and not as a day that
    // moved twelve per cent.
    //
    // The cause column, from 6.6. Where no cause section has been accepted for the
    // name the column is absent and the table says so once, rather than drawn as an
    // empty cell in every row, because an absence stated and an absence drawn as
    // emptiness are different things and only the first is readable. Where one has,
    // each row carries the sentences that name its move, and a row no sentence names
    // says so: the section rests only on documents published inside a move, so a
    // move no document fell inside is one nothing was written about.
    // see: A cause of a move rests only on a document published inside that move
    public string MovesTable(string ticker, IReadOnlyList<MoveCell> moves, IReadOnlyList<ChartBar> year, CauseSource? cause = null)
    {
        var table = new StringBuilder();

        table.Append(Invariant, $"<section class=\"how-it-got-here\" data-ticker=\"{Escaped(ticker)}\" data-moves=\"{moves.Count}\" ");
        table.Append(Invariant, $"data-picture-sessions=\"{year.Count}\">");

        // The twelve-month picture, which section 15.9 puts in this region above
        // the table of the biggest moves. It is the level chart mark given the
        // year and no bands and no averages, rather than a drawing of its own:
        // nothing on any screen is a one-off drawing, and what this region asks
        // is what the year did, not where the levels are. The chart region below
        // is the one about levels, and it draws the same mark with them.
        // see: Marks are defined once and every screen draws from that list
        //
        // It degrades the way every mark does, by saying what it has: a name
        // with fewer sessions than a chart needs gets the sentence stating the
        // count rather than a picture drawn through nothing.
        //
        // It sits in the box the stylesheet scales every chart to its column in, so a window
        // narrower than the picture shows the whole year scaled rather than cutting off its newest
        // months, where most of the numbered moves are.
        table.Append(Invariant, $"<figure class=\"twelve-months\" data-sessions=\"{year.Count}\">");
        table.Append("<div class=\"fig\">");
        table.Append(LevelChart(ticker, year, [], [], new ChartFrame(Markers: [.. moves.Select(move => new ChartMarker(move.SessionDate, Says(move), "#" + RowId(ticker, move)))])));
        table.Append("</div>");
        table.Append(Invariant, $"<figcaption>the twelve months to {(year.Count > 0 ? year[^1].SessionDate.ToString("yyyy-MM-dd", Invariant) : "no stored session")}</figcaption>");
        table.Append("</figure>");

        if (moves.Count == 0)
        {
            table.Append("<p class=\"degraded\" data-moves=\"none\">no moves are stored for this name yet</p></section>");

            return table.ToString();
        }

        table.Append("<div class=\"tbl-wrap\">");
        if (cause is null)
        {
            table.Append(Invariant, $"<table class=\"moves-table\" data-rows=\"{moves.Count}\" data-cause-column=\"absent\">");
            table.Append("<tr><th>Session</th><th>Over</th><th>Change</th><th>Its group</th></tr>");
        }
        else
        {
            table.Append(Invariant, $"<table class=\"moves-table\" data-rows=\"{moves.Count}\" data-cause-column=\"written\" ");
            table.Append(Invariant, $"data-cause-as-of=\"{cause.AsOf:yyyy-MM-dd}\" data-cause-model=\"{Escaped(cause.Model)}\">");
            table.Append("<tr><th>Session</th><th>Over</th><th>Change</th><th>Its group</th><th>Cause</th></tr>");
        }

        foreach (var move in moves)
        {
            table.Append(Invariant, $"<tr id=\"{RowId(ticker, move)}\" data-session-date=\"{move.SessionDate:yyyy-MM-dd}\" data-sessions=\"{move.Sessions}\" ");
            table.Append(Invariant, $"data-change-pct=\"{Number(move.ChangePct)}\" data-rank=\"{move.Rank}\">");
            table.Append(Invariant, $"<td>{move.SessionDate:yyyy-MM-dd}</td>");
            table.Append(Invariant, $"<td>{(move.Sessions == 1 ? "one session" : $"{move.Sessions} sessions")}</td>");
            table.Append(Invariant, $"<td>{Number(move.ChangePct)}%</td>");
            table.Append(GroupCell(move.Group));

            if (cause is not null)
            {
                if (move.Cause is { Length: > 0 } written)
                {
                    table.Append("<td class=\"cause\" data-cause=\"written\">").Append(Escaped(written)).Append("</td>");
                }
                else
                {
                    table.Append("<td class=\"cause\" data-cause=\"none\">no cause was written for this move</td>");
                }
            }

            table.Append("</tr>");
        }

        table.Append("</table></div>");

        if (cause is null)
        {
            table.Append("<p class=\"degraded\" data-cause=\"absent\">the cause of each move has not been written for this name</p>");
        }

        table.Append("</section>");

        return table.ToString();
    }

    // Where a move's row sits, which its circle on the picture links to. The ticker is in it
    // because an exported report holds one name and the app draws one at a time, and a
    // bare rank would collide the day a page carries two of these tables.
    static string RowId(string ticker, MoveCell move) =>
        Formatted($"move-{Escaped(ticker)}-{move.Rank}");

    // What a move's circle says when a reader asks it, in the words its own row uses.
    static string Says(MoveCell move) =>
        Formatted($"{move.Rank}: {(move.ChangePct < 0 ? "down" : "up")} {Number(Math.Abs(move.ChangePct))}% over ") +
        (move.Sessions == 1 ? "one session" : Formatted($"{move.Sessions} sessions")) +
        ", ending " + move.SessionDate.ToString("yyyy-MM-dd", Invariant);

    // A move's group beside it: the median move of the name's group over the same sessions,
    // named as an industry or a sector with how many members it was taken over, and a group
    // that holds nobody, or nobody holding both closes, says so rather than drawing a figure.
    // The median is the annotator's and is drawn as stored; nothing here works it out.
    // see: A large move is shown beside its group's median move over the same sessions
    // see: A screen reads and renders, and computes nothing
    static string GroupCell(MoveGroup? group)
    {
        if (group is null)
        {
            return "<td class=\"group-median\" data-group=\"none\">no group median is stored for this move</td>";
        }

        // A name the index did not hold on the night the group was read has no group read for it,
        // so the cell names that night rather than reading the membership row it no longer had as a
        // group of one. The moves are the newest night's on a page for any night, so the night named
        // is the one they were read on and not the page's own.
        if (group.NotAMemberOn is { } readOn)
        {
            return FormattableString.Invariant($"<td class=\"group-median\" data-group=\"not-a-member\" data-read-on=\"{readOn:yyyy-MM-dd}\">not a member of the index on {readOn:yyyy-MM-dd}, the night its moves' groups were read, so no group is read for this move</td>");
        }

        var named = group.Name is { Length: > 0 } name ? $"the {Escaped(name)} {Escaped(group.Kind)}" : $"a {Escaped(group.Kind)} its membership row does not name";
        var attributes = Formatted($"data-group-kind=\"{Escaped(group.Kind)}\" data-group-name=\"{Escaped(group.Name ?? string.Empty)}\" data-group-members=\"{group.Members}\" data-group-counted=\"{group.Counted}\"");

        if (group.Members == 0)
        {
            return Formatted($"<td class=\"group-median\" {attributes} data-group-median=\"\">{named} holds no other member, so no median is drawn</td>");
        }

        if (group.Median is not { } median)
        {
            return Formatted($"<td class=\"group-median\" {attributes} data-group-median=\"\">none of the {group.Members} other members of {named} held a close on both sessions</td>");
        }

        var missing = group.Members - group.Counted;

        return Formatted($"<td class=\"group-median\" {attributes} data-group-median=\"{Number(median)}\">{Number(median)}%, the median of {group.Counted} of the {group.Members} other members of {named}")
            + (missing > 0 ? Formatted($", {missing} holding no close on one of the two sessions") : string.Empty)
            + "</td>";
    }

    // Section 2's peers table: the name's own row, marked, then the members of its group the night
    // chose, ten at most, those sharing its industry first and then by how closely each one's daily
    // moves followed the name's, each with that likeness, its close, how far it sits below the stored
    // year's high, its return over the window, its trend state and the distance row mark. Each ticker
    // opens its own page and draws its year beside it while the pointer is over it or it has focus.
    // The order is one of likeness, which says nothing of which company is the better, and every
    // figure is drawn as the store holds it: the order is the one the rows arrive in and nothing here
    // sorts, filters or works a figure out.
    // see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
    // see: A screen reads and renders, and computes nothing
    public string PeersTable(string ticker, PeersView peers)
    {
        var table = new StringBuilder();
        var shown = peers.Rows.Count(row => !row.Own);
        var others = Math.Max(peers.Others, shown);

        table.Append(Invariant, $"<div class=\"peers\" data-ticker=\"{Escaped(ticker)}\" data-group-kind=\"{Escaped(peers.GroupKind ?? string.Empty)}\" data-group-name=\"{Escaped(peers.GroupName ?? string.Empty)}\" data-others=\"{others}\">");

        if (!peers.Kept)
        {
            table.Append("<p class=\"degraded\" data-peers=\"not-kept\">The peers' readings are kept for the newest night alone, so none is drawn for an earlier one.</p></div>");

            return table.ToString();
        }

        if (!peers.Member)
        {
            table.Append(Invariant, $"<p class=\"degraded\" data-peers=\"not-a-member\">{Escaped(ticker)} is not a member of the index on the night, so it has no group and no peers are drawn.</p></div>");

            return table.ToString();
        }

        if (peers.GroupKind is not { } kind)
        {
            table.Append(Invariant, $"<p class=\"degraded\" data-peers=\"none\">No readings are stored for {Escaped(ticker)}, so its group and its peers are not drawn.</p></div>");

            return table.ToString();
        }

        var named = peers.GroupName is { Length: > 0 } name
            ? $"the {Escaped(name)} {Escaped(kind)}"
            : $"a {Escaped(kind)} its membership row does not name";

        var followed = Formatted($"how closely each one's daily moves followed {Escaped(ticker)}'s over the sessions both hold");

        table.Append(others == 0
            ? $"<p class=\"peers-group\" data-peers=\"alone\">{named} holds no other member, so the table holds {Escaped(ticker)} alone.</p>"
            : !peers.Chosen
                ? Formatted($"<p class=\"peers-group\" data-peers=\"not-chosen\">{named} holds {others} other members, and the night has not yet chosen the ones this table draws, which it does from its next run.</p>")
                : shown == others
                    ? Formatted($"<p class=\"peers-group\" data-peers=\"group\">{Escaped(ticker)} and the {others} other members of {named}, those sharing its industry first, then by {followed}.</p>")
                    : Formatted($"<p class=\"peers-group\" data-peers=\"group\">{Escaped(ticker)} and {shown} of the {others} other members of {named}: those sharing its industry first, then the ones whose daily moves followed {Escaped(ticker)}'s most closely over the sessions both hold.</p>"));

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"peers-table\" data-rows=\"{peers.Rows.Count}\" data-others=\"{others}\">");
        table.Append("<tr>");

        foreach (var (heading, says) in PeersHeadings)
        {
            table.Append(TippedHeading(heading, says, heading == "Moved with it" ? "r" : null));
        }

        table.Append("</tr>");

        foreach (var row in peers.Rows)
        {
            var company = row.Company is { Length: > 0 } called ? Formatted($" <span class=\"co\">{Escaped(called)}</span>") : string.Empty;
            var industry = row.Likeness is { SameIndustry: true } ? " <span class=\"pill same-industry\">same industry</span>" : string.Empty;

            table.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-own=\"{(row.Own ? "true" : "false")}\">");
            table.Append(row.Own
                ? Formatted($"<td class=\"peer own\"><b>{Escaped(row.Ticker)}</b> <span class=\"own-mark\">this name</span>{company}</td>")
                : Formatted($"<td class=\"peer\"><a class=\"peer-link\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{company}{industry}<span class=\"peer-pop\" role=\"tooltip\">{YearLine(row.Ticker, row.Year, row.Distance?.NearestSupport, row.Distance?.NearestResistance)}</span></td>"));
            table.Append(row.Likeness switch
            {
                null => "<td class=\"r num likeness\" data-likeness=\"\" data-sessions=\"\"></td>",
                { Value: { } value } alike => Formatted($"<td class=\"r num likeness\" data-likeness=\"{value.ToString("R", Invariant)}\" data-sessions=\"{alike.Sessions}\">{value.ToString("0.00;-0.00", Invariant)}</td>"),
                { } alike => Formatted($"<td class=\"r num likeness\" data-likeness=\"\" data-sessions=\"{alike.Sessions}\"><span class=\"degraded\">too few sessions shared, {alike.Sessions}</span></td>"),
            });
            table.Append(row.Close is { } close
                ? Formatted($"<td class=\"r num\" data-close=\"{close.ToString(Invariant)}\">{Figures.Price(close)}</td>")
                : "<td class=\"r num\" data-close=\"\">not computed</td>");

            if (row.Readings is { } read)
            {
                table.Append(Formatted($"<td class=\"below-high\" data-below-high=\"{Number(read.BelowHighPct)}\" data-year-high=\"{read.YearHigh.ToString(Invariant)}\" data-bars=\"{read.Bars}\">{Number(read.BelowHighPct)}% below {Figures.Price(read.YearHigh)}, the high of {read.Bars} bars</td>"));
                table.Append(read.ReturnPct is { } back
                    ? Formatted($"<td class=\"peer-return\" data-return=\"{Number(back)}\" data-bars=\"{read.Bars}\">{back.ToString("+0.##;-0.##;0", Invariant)}%</td>")
                    : Formatted($"<td class=\"peer-return\" data-return=\"\" data-bars=\"{read.Bars}\"><span class=\"degraded\">not available, {read.Bars} bars</span></td>"));
            }
            else
            {
                table.Append("<td class=\"below-high\" data-below-high=\"\" data-bars=\"\"><span class=\"degraded\">no readings stored</span></td>");
                table.Append("<td class=\"peer-return\" data-return=\"\" data-bars=\"\"><span class=\"degraded\">no readings stored</span></td>");
            }

            table.Append(Invariant, $"<td class=\"trend-state\">{Escaped((row.TrendState ?? NotClassified).Replace('_', ' '))}</td>");
            table.Append(Invariant, $"<td class=\"c\">{(row.Distance is { } cell ? DistanceRow(cell) : "<span class=\"degraded\" data-distance=\"none\">no bands stored for this name</span>")}</td>");
            table.Append("</tr>");
        }

        table.Append("</table></div></div>");

        return table.ToString();
    }

    const int YearWidth = 380;
    const int YearHeight = 150;
    const int YearTop = 30;
    const int YearRight = 118;
    const int YearLabelGap = 13;

    // The year line: a name's stored closes as one line, with its nearest support and its nearest
    // resistance drawn across it in their own hues and named at the right with their prices, and the
    // last close marked. It is what the peers table and the universe table draw beside a ticker while the
    // pointer is over it or it has focus, so the name's year is read without leaving the page. The scale takes
    // in both bands as well as the closes, since a band drawn off the picture is the one thing the
    // reader looked for. A name holding fewer closes than a line needs says so and draws nothing,
    // which is section 15.5's rule for every mark.
    // see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
    public string YearLine(string ticker, PeerYear? year, decimal? support, decimal? resistance)
    {
        if (year is null || year.Closes.Count < FewestBars)
        {
            return Formatted($"<span class=\"degraded year-line-none\" data-ticker=\"{Escaped(ticker)}\" data-closes=\"{year?.Closes.Count ?? 0}\">{Escaped(ticker)} holds {year?.Closes.Count ?? 0} stored close(s), too few to draw its year.</span>");
        }

        var prices = year.Closes.Select(PlotValue).ToList();

        prices.AddRange(new[] { support, resistance }.Where(band => band is not null).Select(band => PlotValue(band!.Value)));

        var low = prices.Min();
        var high = prices.Max();
        var span = high - low > 0 ? high - low : 1;
        double plotWidth = YearWidth - YearRight - 4;
        var step = plotWidth / (year.Closes.Count - 1);

        double Y(decimal price) => YearTop + (YearHeight - YearTop - 6) * (1 - ((PlotValue(price) - low) / span));

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {YearWidth} {YearHeight}\" width=\"{YearWidth}\" height=\"{YearHeight}\" role=\"img\" class=\"year-line\" data-ticker=\"{Escaped(ticker)}\" data-closes=\"{year.Closes.Count}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, {year.Closes.Count} stored closes from {year.From:yyyy-MM-dd} to {year.To:yyyy-MM-dd}</title>");
        svg.Append(Invariant, $"<text class=\"m-pane-h\" x=\"4\" y=\"14\">{Escaped(ticker)}, {year.Closes.Count} closes to {year.To:yyyy-MM-dd}</text>");
        svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"4\" y=\"{YearTop - 4}\" width=\"{plotWidth}\" height=\"{YearHeight - YearTop}\"/>");

        // Each band's rule across the plot and its name at the right, the two names set at least a line
        // apart where the bands sit close, so neither is written over the other.
        var labels = new List<(double At, string Side, string Named)>();

        foreach (var (price, side, named) in new[] { (resistance, "res", "resistance"), (support, "sup", "support") })
        {
            if (price is { } band)
            {
                svg.Append(Invariant, $"<line class=\"m-edge-{side}\" data-{named}=\"{band.ToString(Invariant)}\" x1=\"4\" y1=\"{Number(Y(band))}\" x2=\"{4 + plotWidth}\" y2=\"{Number(Y(band))}\"/>");
                labels.Add((Y(band) + 4, side, Formatted($"{named} {Price(band)}")));
            }
        }

        if (labels.Count == 2 && labels[1].At - labels[0].At < YearLabelGap)
        {
            var middle = (labels[0].At + labels[1].At) / 2;

            labels[0] = labels[0] with { At = middle - (YearLabelGap / 2.0) };
            labels[1] = labels[1] with { At = middle + (YearLabelGap / 2.0) };
        }

        foreach (var (at, side, named) in labels)
        {
            svg.Append(Invariant, $"<text class=\"m-legend-t m-legend-{side}\" x=\"{8 + plotWidth}\" y=\"{Number(at)}\">{named}</text>");
        }

        var line = new StringBuilder();

        for (var at = 0; at < year.Closes.Count; at++)
        {
            line.Append(at == 0 ? 'M' : 'L').Append(Number(4 + (step * at))).Append(' ').Append(Number(Y(year.Closes[at]))).Append(' ');
        }

        svg.Append(Invariant, $"<path class=\"m-mom\" d=\"{line.ToString().Trim()}\"/>");
        svg.Append(Invariant, $"<circle class=\"m-last\" data-close=\"{year.Closes[^1].ToString(Invariant)}\" cx=\"{Number(4 + plotWidth)}\" cy=\"{Number(Y(year.Closes[^1]))}\" r=\"2.6\"/>");
        svg.Append("</svg>");

        return svg.ToString();
    }

    // Section 4's earnings reaction record, beside the earnings setups: one row per print over the
    // calendar's year behind, each with its report date, its timing, the session it moved on, the
    // estimate, the actual, the provider's surprise and that session's move, drawn as stored. A print
    // with no filed estimate says so and draws no surprise, so it is never read as having met one.
    // see: Each print's reaction is read from the nightly calendar and the stored bars, and reaches no reason, gate or plan
    // see: A screen reads and renders, and computes nothing
    public string ReactionsTable(string ticker, IReadOnlyList<ReactionCell> prints)
    {
        var table = new StringBuilder();

        table.Append(Invariant, $"<div class=\"reactions\" data-ticker=\"{Escaped(ticker)}\" data-prints=\"{prints.Count}\">");

        if (prints.Count == 0)
        {
            table.Append(Invariant, $"<p class=\"degraded\" data-reactions=\"none\">No print over the calendar's year behind is stored for {Escaped(ticker)} with a session the stored bars reach.</p></div>");

            return table.ToString();
        }

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"reactions-table\" data-rows=\"{prints.Count}\">");
        table.Append("<tr><th>Reported</th><th>When</th><th>Moved on</th><th>Estimate</th><th>Actual</th><th>Surprise</th><th>That session</th></tr>");

        foreach (var print in prints)
        {
            var when = print.Timing switch
            {
                "before" => "before the open",
                "after" => "after the close",
                _ => "timing not filed",
            };

            table.Append(Invariant, $"<tr data-report-date=\"{print.ReportDate:yyyy-MM-dd}\" data-timing=\"{Escaped(print.Timing)}\" data-session=\"{print.Session:yyyy-MM-dd}\">");
            table.Append(Invariant, $"<td>{print.ReportDate:yyyy-MM-dd}</td><td>{when}</td><td>{print.Session:yyyy-MM-dd}</td>");
            table.Append(print.Estimate is { } estimate
                ? Formatted($"<td class=\"r num\" data-estimate=\"{Escaped(estimate)}\">{Escaped(Figures.Read(estimate))}</td>")
                : "<td class=\"estimate\" data-estimate=\"\"><span class=\"degraded\">none was filed</span></td>");
            table.Append(print.Actual is { } actual
                ? Formatted($"<td class=\"r num\" data-actual=\"{Escaped(actual)}\">{Escaped(Figures.Read(actual))}</td>")
                : "<td class=\"actual\" data-actual=\"\"><span class=\"degraded\">not filed</span></td>");
            table.Append(print.Estimate is null
                ? "<td class=\"surprise\" data-surprise=\"\"><span class=\"degraded\">none, with no estimate to measure against</span></td>"
                : print.SurprisePct is { } surprise
                    ? Formatted($"<td class=\"surprise\" data-surprise=\"{Number(surprise)}\">{surprise.ToString("+0.##;-0.##;0", Invariant)}%</td>")
                    : "<td class=\"surprise\" data-surprise=\"\"><span class=\"degraded\">not filed</span></td>");
            table.Append(Formatted($"<td class=\"reaction-move\" data-move=\"{Number(print.MovePct)}\">{print.MovePct.ToString("+0.##;-0.##;0", Invariant)}%</td>"));
            table.Append("</tr>");
        }

        table.Append("</table></div></div>");

        return table.ToString();
    }

    // The distance row, section 15.5's mark for a table cell.
    //
    // A name's close between its nearest support and its nearest resistance,
    // with the distances in typical days. It turns a column of numbers into a
    // shape, so a scan down five hundred rows shows which names are near an edge
    // without reading any of them
    // (see: Distances are stated as typical days' moves).
    //
    // The two hues are the ones support and resistance own everywhere else, and
    // nothing else on this mark uses them
    // (see: Support and resistance own two hues and nothing else uses them).
    //
    // A name with neither edge draws a rule and says so. An absence drawn as a
    // shape at one end is a shape a reader will read.
    public string DistanceRow(UniverseCell row)
    {
        // The close fixed at the centre, the nearest support a block to its left and
        // below the line, the nearest resistance a block to its right and above it, one
        // tick per typical day. A block against the centre is a name at an edge.
        const int Half = 40;
        const int Spare = 3;
        const int Pad = 30;
        const int Wide = (2 * Half) + (2 * Spare);
        const int Height = 26;
        const int Middle = 13;
        const double Centre = Wide / 2.0;

        // The scale is fixed across every row rather than fitted to each, which
        // is the whole point of a mark meant to be scanned down a column: a
        // shape that means one thing on one row and another on the next says
        // nothing about the column. Four typical days either side, clamped, so a
        // name far from everything sits at the edge rather than off it.
        const double Span = DistanceSpan;

        var mark = new StringBuilder();

        // Appended fragment by fragment with the provider on each, rather than
        // concatenated and formatted once. Two interpolated strings joined with
        // a plus are formatted in the current culture before anything sees them,
        // which is the coercion the compiler refuses here and the one this
        // repository bans everywhere else.
        mark.Append(Invariant, $"<svg class=\"distance-row\" role=\"img\" viewBox=\"{-Pad} 0 {Wide + (2 * Pad)} {Height}\" width=\"{Wide + (2 * Pad)}\" height=\"{Height}\" ");
        mark.Append(Invariant, $"data-ticker=\"{Escaped(row.Ticker)}\" ");
        mark.Append(Invariant, $"data-to-support=\"{Days(row.ToSupport)}\" data-to-resistance=\"{Days(row.ToResistance)}\" ");
        mark.Append(Invariant, $"data-nearest=\"{Days(row.Nearest)}\">");

        mark.Append(Invariant, $"<line class=\"m-track\" x1=\"{Centre - Half}\" y1=\"{Middle}\" x2=\"{Centre + Half}\" y2=\"{Middle}\"/>");

        for (var day = 1; day < Span; day++)
        {
            var q = day / Span * Half;

            mark.Append(Invariant, $"<line class=\"m-track\" x1=\"{Number(Centre - q)}\" y1=\"{Middle - 2}\" x2=\"{Number(Centre - q)}\" y2=\"{Middle + 2}\"/>");
            mark.Append(Invariant, $"<line class=\"m-track\" x1=\"{Number(Centre + q)}\" y1=\"{Middle - 2}\" x2=\"{Number(Centre + q)}\" y2=\"{Middle + 2}\"/>");
        }

        void Side(double? days, int direction)
        {
            var support = direction < 0;
            var side = support ? "sup" : "res";

            if (days is not { } value)
            {
                // No band on this side: said in words inside a dashed box, never drawn
                // as a block at an end, which is a shape a reader would read.
                var boxWidth = Half + Pad - 8;
                var left = support ? Centre - 4 - boxWidth : Centre + 4;

                mark.Append(Invariant, $"<rect class=\"m-absent\" x=\"{Number(left + 0.5)}\" y=\"2.5\" width=\"{boxWidth}\" height=\"{Height - 5}\"/>");
                mark.Append(Invariant, $"<text class=\"m-absent-s\" x=\"{Number(left + (boxWidth / 2.0))}\" y=\"{Middle + 4}\" text-anchor=\"middle\">{(support ? "none below" : "none above")}</text>");

                return;
            }

            var end = Centre + (direction * Math.Min(value, Span) / Span * Half);
            var linkY = support ? Middle + 3 : Middle - 3;

            mark.Append(Invariant, $"<line class=\"{(support ? "support" : "resistance")}-edge\" x1=\"{Number(Centre)}\" y1=\"{linkY}\" x2=\"{Number(end)}\" y2=\"{linkY}\" ");
            mark.Append(Invariant, $"stroke=\"{(support ? SupportHue : ResistanceHue)}\" stroke-width=\"1.5\" />");

            // Past the scale the block becomes an arrow, so a distant band reads as
            // beyond the picture rather than at its edge.
            if (value > Span)
            {
                mark.Append(Invariant, $"<polyline class=\"m-link-{side}\" points=\"{Number(end - (direction * 6))},{(support ? Middle : Middle - 8)} {Number(end)},{(support ? Middle + 4 : Middle - 4)} {Number(end - (direction * 6))},{(support ? Middle + 8 : Middle)}\"/>");
            }
            else
            {
                mark.Append(Invariant, $"<rect class=\"m-dist-{side}\" x=\"{Number(support ? end - 6 : end)}\" y=\"{(support ? Middle : Middle - 12)}\" width=\"6\" height=\"12\"/>");
            }

            mark.Append(Invariant, $"<text class=\"m-dnum\" x=\"{Number(support ? Centre - Half - Spare - 4 : Centre + Half + Spare + 4)}\" y=\"{Middle + 4}\" text-anchor=\"{(support ? "end" : "start")}\">");
            mark.Append(support ? Formatted($"S {value:0.0}") : Formatted($"{value:0.0} R"));
            mark.Append("</text>");
        }

        Side(row.ToSupport, -1);
        Side(row.ToResistance, 1);

        // The close, always drawn, because the mark is about where the price
        // sits between the two and a row with no marker is a row with no
        // subject.
        mark.Append(Invariant, $"<rect class=\"close\" x=\"{Number(Centre - 1)}\" y=\"{Middle - 12}\" width=\"2\" height=\"24\" fill=\"var(--ink, #1c1c1c)\" />");

        mark.Append(row.ToSupport is null && row.ToResistance is null
            ? Formatted($"<title>{Escaped(row.Ticker)}: no band on either side yet</title>")
            : Formatted($"<title>{Escaped(row.Ticker)}: {Reads(row.ToSupport, "support")}, {Reads(row.ToResistance, "resistance")}</title>"));

        mark.Append("</svg>");

        return mark.ToString();
    }

    static string Days(double? days) =>
        days is { } value ? value.ToString("0.##", CultureInfo.InvariantCulture) : "none";

    // A day's change as an attribute, and as the words a reader takes it from.
    // The sign is always drawn, because the sign is the only channel the
    // direction has: hue is spoken for by support and resistance, and a change
    // written without its sign is a magnitude.
    static string Change(double? change) =>
        change is { } value ? value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "none";

    // Two absences, and they are stated as two. A name with no bar on the night
    // has no close and no change to draw; a name with a close and no session
    // before it has a first stored session. Drawn the same way they read as one
    // thing, and the second is rare while the first is every stale member on
    // every night, which is the pair the sixth phase 5 sign-off review found
    // collapsed into a change of exactly 0.00.
    static string ChangeReads(double? change, decimal? close) =>
        change is { } value
            ? Formatted($"{value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}%")
            : close is null
                ? "<span class=\"degraded\" data-absence=\"no-bar\">no bar for this session</span>"
                : "<span class=\"degraded\" data-absence=\"no-earlier-close\">no earlier close stored</span>";

    static string Reads(double? days, string side) =>
        days is { } value
            ? Formatted($"{value:0.#} typical days to {side}")
            : Formatted($"no {side} band");

    // The sector strip, section 15.8's first region.
    //
    // One line per sector with how many names it holds and how many are in an
    // uptrend. The count of names on tonight's list is the half this cannot draw
    // until 5.4 creates the store it would read, and it is absent rather than
    // shown as zero: a zero here would say nothing fired tonight.
    public string SectorStrip(IReadOnlyList<SectorLine> lines)
    {
        var strip = new StringBuilder();

        strip.Append(Formatted($"<section class=\"sector-strip\" data-sectors=\"{lines.Count}\">"));

        foreach (var line in lines)
        {
            strip.Append(Invariant, $"<div class=\"sector\" data-sector=\"{Escaped(line.Sector)}\" ");
            strip.Append(Invariant, $"data-names=\"{line.Names}\" data-uptrend=\"{line.InUptrend}\" data-listed=\"{line.OnTheList}\">");
            strip.Append(Invariant, $"{Escaped(line.Sector)}: {line.Names} name(s), {line.OnTheList} on the list, {line.InUptrend} in an uptrend</div>");
        }

        strip.Append("</section>");

        return strip.ToString();
    }

    // What a trend column holds, said alike wherever a table draws one.
    public const string TrendSays = "The chart's trend in a word. Uptrend: the close is above its 50-day average, the 50-day is above the 200-day, and the latest swing low is above the one before it. Downtrend: the mirror of that. Range: every input is there and neither holds. Not classified: too little history to read one.";

    // What a column drawing the distance row holds, said alike wherever a table draws one.
    public static string DistanceSays { get; } = Formatted($"How far the close sits from its nearest support below and its nearest resistance above, in typical days' moves, a typical day's move being the stock's average true range over {EquityBrief.Core.Indicators.IndicatorSeries.Wilder} sessions. S is the gap between the close and the top of the support band, R the gap between the close and the bottom of the resistance band, so 0.0 means the close is at that band's edge. In the picture the close is the centre line, support is green to the left and resistance orange to the right, one tick per typical day, and an arrow marks a band more than {DistanceSpan} away.");

    // A column heading carrying what its column holds, which the stylesheet shows while the pointer is over
    // the heading or it has the focus. The words the heading shows are its name unless it is drawn short.
    static string TippedHeading(string heading, string says, string? classes = null, string? shown = null) =>
        Formatted($"<th class=\"{(classes is null ? string.Empty : classes + " ")}tipped\" tabindex=\"0\" data-heading=\"{Escaped(heading)}\"><span class=\"th-t\">{Escaped(shown ?? heading)}</span><span class=\"head-tip\" role=\"tooltip\">{Escaped(says)}</span></th>");

    // What each of the universe table's columns holds, in the order the columns are drawn, said in the
    // column's heading so a reader learns how to read a column without leaving the table. The windows
    // are the swing reader's and the indicator engine's own, read from where each keeps them.
    public static IReadOnlyList<(string Heading, string Says)> UniverseHeadings { get; } =
    [
        ("Name", "The stock's ticker, which opens its own page, with the company beneath it. Hold the pointer over the name to see its year of closes with its nearest support and resistance."),
        ("Sector", "The sector the index files the company under."),
        ("Close", "The stock's closing price on the night the table is drawn for."),
        ("Trend", TrendSays),
        ("Distance", DistanceSays),
        ("Sessions to earnings", "Trading sessions from the night to the company's next dated earnings report on the calendar."),
        ("Strength", Formatted($"How the stock's returns compare with the rest of the index: the average of where its return over the last {SwingReadings.ReturnShortSessions} sessions and over the last {SwingReadings.ReturnLongSessions} sits among every other member's. 90% means that across the two it beat nine in ten of them.")),
        ("Pullback", Formatted($"How far the close sits below the highest high of the last {SwingReadings.HighWindow} sessions, in typical days' moves.")),
        ("Dry-up", "The median daily volume since that high against the stock's 50-day average volume. Below 1 means trading thinned out on the way down."),
        ("Tightness", Formatted($"The mean true range, a day's full span of movement, of the last {SwingReadings.TightShortSessions} sessions against the last {SwingReadings.TightLongSessions}. Below 1 means the price has been moving in a narrower range lately.")),
        ("Last on the list", "The last evening the stock was on tonight's list, or never."),
        ("Sixty evenings", "One column for each of the last sixty sessions: a solid bar on an evening the stock was on the list and a thin line on one it was not. Before the swing filter's first night an evening's list held every stock any reason fired for, which was most of the index."),
    ];

    // What each of tonight's list's columns holds before its reason columns, in the order they are drawn,
    // the gates' column among them on an evening the swing filter listed.
    public static IReadOnlyList<(string Heading, string Says)> TonightHeadings(bool byFilter, bool oneGateShort = false) =>
    [
        ("#", "The row's place in the order the list is drawn in, counted from one."),
        ("Name", "The ticker selects the row and draws its plan and levels beneath the list. Beside it, report opens the stock's written report, or not written opens its page where no report is written yet, and the company is named beneath."),
        ("Close", "The stock's closing price on the evening."),
        ("Day", "How far the close moved on the day against the close before it, in per cent."),
        ("Trend", TrendSays + " Beside it, where the night read one, the state the company's reported quarters gave it, with what its numbers say under the pointer."),
        ("Distance to levels", DistanceSays),
        ("Reward to risk", "How far the plan's target sits above its buy against how far its stop sits below it, so 2.00 means twice as much to gain as to lose. It is a fact about the chart and not a chance of anything. Where the plan states none, the row says why."),
        .. oneGateShort
            ? new[] { ("Gate missed", GateMissedSays) }
            : byFilter
            ? new[] { ("Gates", "How the swing filter passed the stock: the setup's family, the session its trigger arrived on, and the plan the trade gate read with its reward to risk and how far its stop sits below the entry in typical days' moves. Hold the pointer on the cell for each gate's reason.") }
            : Array.Empty<(string, string)>(),
    ];

    // What the second list's gate column holds.
    public const string GateMissedSays =
        "The one gate of the five the stock did not pass, what it had against the bar it needed, and how far short that is as a share of that bar, so ten per cent short reads the same on every gate. A condition not met at all is a whole bar, one hundred per cent. Beneath it, the trade its plan states, where it states one.";

    // What a reason column of tonight's list holds: the reason's full name and when it fires, as the
    // architecture's table of reasons states it, what a mark in the column means, and that on an evening the
    // swing filter listed the reasons are context. A reason this file does not know is named with the rest.
    public static string ReasonSays(string reason)
    {
        var fires = reason switch
        {
            ShortlistSeries.AtEntryZone => " fires when the close is inside one of the plan's buying zones.",
            ShortlistSeries.CrossedALevel => " fires when the close moved through a band's edge it was on the other side of the session before.",
            ShortlistSeries.BreakoutOnVolume => " fires when the close is above a band that sat at or above the previous close, on volume above the 50-day average.",
            ShortlistSeries.TrendStateChanged => " fires when the trend's word differs from the night before.",
            ShortlistSeries.UnusualVolume => Formatted($" fires when the day's volume is above {ShortlistSeries.UnusualVolumeMultiple:0.##} times the 50-day average."),
            ShortlistSeries.EarningsSoon => Formatted($" fires when the next earnings report is within {ShortlistSeries.EarningsHorizonSessions} sessions on the exchange's calendar."),
            _ => ".",
        };

        return char.ToUpperInvariant(reason[0]) + reason[1..] + fires + " A mark on a row means it fired for the stock that evening, and holding the pointer on the mark shows the values it fired on. Where the swing filter listed the evening, the reasons are context and chose no row.";
    }

    // What each of a peers table's columns holds, in the order they are drawn.
    public static IReadOnlyList<(string Heading, string Says)> PeersHeadings { get; } =
    [
        ("Name", "The first row is this page's stock, then at most ten of its group, those sharing its industry first. Each ticker opens its own page, and holding the pointer over it draws its year of closes with its nearest support and resistance."),
        ("Moved with it", Formatted($"How closely the stock's daily moves followed this page's stock over the sessions both hold: 1 is in step every day, 0 is no relation, and a negative figure moved the other way. A pair sharing fewer than {EquityBrief.Core.Moves.PeerPicks.FewestSessions} sessions says how many instead.")),
        ("Close", "The stock's last stored close."),
        ("Below the year's high", "How far the close sits below the highest price among the bars the store holds for it, in per cent, with that high and how many bars it was read over."),
        (Formatted($"Return over {EquityBrief.Core.Moves.PeerReadings.ReturnWindow} sessions"), Formatted($"The change in the close over the last {EquityBrief.Core.Moves.PeerReadings.ReturnWindow} sessions, in per cent.")),
        ("Trend", TrendSays),
        ("Distance", DistanceSays),
    ];

    // What each of a Past picks table's columns holds, in the order they are drawn: the stock's column only
    // where the table holds more than one name's trades, and the night linking to the name's page for that
    // night where it does not.
    public static IReadOnlyList<(string Heading, string Says)> PicksHeadings(bool named) =>
    [
        ("Night listed", named
            ? "The evening the live list recommended the trade. It is taken as bought at that evening's close."
            : "The evening the live list recommended the trade. It is taken as bought at that evening's close, and the date opens this stock's page as it stood that night."),
        .. named ? new[] { ("Stock", "The stock, which opens its page as it stood on the night listed.") } : Array.Empty<(string, string)>(),
        ("Business that night", "The state the company's reported quarters gave it on the evening it was listed, or not read that night where none was stored yet."),
        ("Buy", "The price the plan buys at."),
        ("Stop", "The price the plan sells at to cut the loss."),
        ("Target", "The price the plan takes its gain at."),
        ("Trade", "The line runs from the stop on the left, in green, to the target on the right, in orange, with the buy marked between them. The dot is where the price is now, hollow while the trade is open and filled where it finished."),
        ("Status", "What became of the trade: open, reached target, stopped out, or ran out of time where its holding limit passed before either."),
        ("Sessions held", "Trading sessions from the night listed to the session it finished on, or to the night drawn while it is still open."),
        ("Result", "What the trade came to in multiples of the risk it took, the fall from the buy to the stop: +2.00 made twice that risk and -1.00 lost it, and a close through the stop can read below -1. Open while the trade runs."),
    ];

    // How many typical days either side of the close the distance row draws before a band becomes an arrow.
    public const int DistanceSpan = 4;

    // The universe table, section 15.8's second region.
    //
    // Every name in the index, in the order the projection put them, which is by
    // distance to the nearest level ascending. The columns the listings store
    // carries, being the evening a name was last on the list and the listing
    // strip, are absent rather than blank, and the table says so once rather
    // than in every row.
    public string UniverseTable(IReadOnlyList<UniverseCell> rows)
    {
        var table = new StringBuilder();

        table.Append("<div class=\"tbl-wrap\">");
        table.Append(Formatted($"<table class=\"universe-table\" data-rows=\"{rows.Count}\">"));

        // Each heading carries what its column holds, which the stylesheet shows while the pointer is
        // over the heading or it has the focus.
        table.Append("<tr>");

        foreach (var (heading, says) in UniverseHeadings)
        {
            table.Append(TippedHeading(heading, says));
        }

        table.Append("</tr>");

        foreach (var row in rows)
        {
            table.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-sector=\"{Escaped(row.Sector)}\" ");
            table.Append(Invariant, $"data-trend-state=\"{Escaped(row.TrendState ?? NotClassified)}\">");

            // The ticker is the way to the name's page, with the company's name beneath it, the
            // day its research was written where it holds any, and its year line with its nearest
            // bands, which the stylesheet shows while the pointer is over the cell or it has focus.
            table.Append(Invariant, $"<td class=\"c-nm\"><a class=\"tk\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>");
            table.Append(row.Name is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty);
            table.Append(row.Researched is { } written
                ? $"<span class=\"researched-on\" data-researched=\"{written.ToString("yyyy-MM-dd", Invariant)}\">researched {written.ToString("yyyy-MM-dd", Invariant)}</span>"
                : string.Empty);
            table.Append(Formatted($"<span class=\"peer-pop\" role=\"tooltip\">{YearLine(row.Ticker, row.Year, row.NearestSupport, row.NearestResistance)}</span></td>"));
            table.Append(Formatted($"<td>{Escaped(row.Sector)}</td>"));
            // The close, drawn at the places a price is read at with the stored value on the
            // cell. A name the night computed nothing for says so rather than showing a zero.
            table.Append(Invariant, $"<td class=\"num\" data-close=\"{(row.Close is { } held ? held.ToString(Invariant) : "none")}\">{(row.Close is { } close ? Price(close) : "not computed")}</td>");
            table.Append(Formatted(
                $"<td>{Escaped((row.TrendState ?? NotClassified).Replace('_', ' '))}</td>"));
            table.Append(Formatted($"<td>{DistanceRow(row)}</td>"));

            // The sessions until the name's next dated event, which section 15.8
            // states as a column of this table. Two absences and they are stated
            // as two: a name the calendar holds nothing for has no event to
            // count to, and one whose event is past the end of the exchange
            // closure table has an event nobody can count the sessions to. A
            // column that drew both the same way would say the table had ended
            // in the voice of a name with no earnings date.
            // owes: The exchange closure table extended before the nights reach its end
            table.Append(Invariant, $"<td class=\"to-earnings\" data-sessions=\"{(row.SessionsUntilEarnings is { } until ? until.ToString(Invariant) : "none")}\" ");
            table.Append(Invariant, $"data-next-event=\"{(row.NextEvent is { } dated ? dated.ToString("yyyy-MM-dd", Invariant) : "none")}\">");
            table.Append(row.SessionsUntilEarnings is { } sessions
                ? Formatted($"{sessions}")
                : row.EventBeyondTheTable
                    ? "<span class=\"degraded\">past the end of the closure table</span>"
                    : "<span class=\"degraded\">no dated event</span>");
            table.Append("</td>");

            // The swing readings, each as the swing reader stored it and drawn whole on its
            // cell: the mean of the two places among the members' returns, the pullback in
            // typical days, the volume while it came down and the range's tightness.
            table.Append(SwingCell("strength", row.Strength, held => Formatted($"{held * 100:0}%")));
            table.Append(SwingCell("depth", row.Depth, held => Formatted($"{held:0.00}")));
            table.Append(SwingCell("dry-up", row.DryUp, held => Formatted($"{held:0.00}")));
            table.Append(SwingCell("tightness", row.Tightness, held => Formatted($"{held:0.00}")));

            // The two right-hand columns count evenings a name appeared on the
            // list. They say nothing about index membership, which every name in
            // this table has by definition.
            table.Append(Invariant, $"<td data-last-listed=\"{(row.LastListed is { } listed ? listed.ToString("yyyy-MM-dd", Invariant) : "never")}\">");
            table.Append(Invariant, $"{(row.LastListed is { } shown ? shown.ToString("yyyy-MM-dd", Invariant) : "never")}</td>");
            table.Append(Formatted($"<td>{ListingStrip(row.Ticker, row.Evenings ?? [])}</td>"));
            table.Append("</tr>");
        }

        table.Append("</table></div>");

        return table.ToString();
    }

    // What a name with no ladder row is shown as. Its own value rather than an
    // empty cell, so it can be filtered for and counted.
    const string NotClassified = "not classified";

    // One swing reading in a universe cell, the stored value whole on the element and a word where none was stored.
    static string SwingCell(string attribute, double? value, Func<double, string> shown) =>
        Formatted($"<td class=\"num swing\" data-{attribute}=\"{Whole(value)}\">") +
        (value is { } held ? shown(held) : "<span class=\"degraded\">not read</span>") + "</td>";

    // A stored statistic whole, for the element a test reads it back off, and a word where none was stored.
    static string Whole(double? value) => value is { } held ? held.ToString("R", Invariant) : "none";

    // A gate's stored figure to the hundredth, and the gate's own word where it stored none.
    static string Hundredths(string stored) =>
        double.TryParse(stored, NumberStyles.Float, Invariant, out var value) ? value.ToString("0.00", Invariant) : Escaped(stored);

    // The night's breadth line for tonight's header: the share of the members read closing above
    // their own long average, with how many it was counted over and the shorter average beside it
    // as context, and a line saying so where the night stored none or too few members to read.
    static string BreadthLine(MarketView? market)
    {
        if (market is null)
        {
            return "<p class=\"breadth degraded\" data-breadth=\"none\">breadth: no market reading is stored for this night</p>";
        }

        var head = Formatted($"<p class=\"breadth\" data-breadth=\"{Whole(market.Breadth)}\" data-counted=\"{market.Counted}\" data-members=\"{market.Members}\" data-breadth-context=\"{Whole(market.BreadthContext)}\">");

        return market.Breadth is { } share
            ? head + Formatted($"breadth: {share * 100:0.0}% of the {market.Counted} members read close above their own {SwingReadings.BreadthAverageSessions}-day average") +
                (market.BreadthContext is { } context ? Formatted($", and {context * 100:0.0}% above their {SwingReadings.ContextAverageSessions}-day average, as context") : string.Empty) + "</p>"
            : head + Formatted($"breadth: not available, {market.Counted} of the {market.Members} members hold a close and a {SwingReadings.BreadthAverageSessions}-day average, fewer than half</p>");
    }

    // The run page's funnel, section 15.10's row: how many members each gate passed in order and how
    // many it removed, the setup's two families, what the exclusions removed and how many pass, and
    // the version the night ran under, each count whole on its row.
    public string Funnel(FunnelView? funnel)
    {
        if (funnel is null)
        {
            return "<p class=\"degraded\" data-funnel=\"none\">no swing filter results are stored for this night</p>";
        }

        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"funnel-table\" data-session=\"{funnel.Session:yyyy-MM-dd}\" data-members=\"{funnel.Members}\" data-version=\"{Escaped(funnel.Version)}\">");
        region.Append("<tr><th>Step</th><th>Passed</th><th>Removed</th></tr>");
        region.Append(Invariant, $"<tr data-step=\"members\" data-passed=\"{funnel.Members}\" data-removed=\"0\"><td>Members of the index</td><td class=\"num\">{funnel.Members}</td><td class=\"num\"></td></tr>");

        foreach (var step in funnel.Steps)
        {
            region.Append(Invariant, $"<tr data-step=\"{Escaped(step.Gate)}\" data-passed=\"{step.Passed}\" data-removed=\"{step.Removed}\"><td>{Escaped(char.ToUpperInvariant(step.Gate[0]) + step.Gate[1..])}");

            // A night stored before the filter read pullbacks alone may hold breakouts, and draws them.
            if (step.Gate == "setup" && funnel.Breakouts > 0)
            {
                region.Append(Invariant, $" <span class=\"families\" data-pullbacks=\"{funnel.Pullbacks}\" data-breakouts=\"{funnel.Breakouts}\">({funnel.Pullbacks} pullback(s), {funnel.Breakouts} breakout(s))</span>");
            }

            region.Append(Invariant, $"</td><td class=\"num\">{step.Passed}</td><td class=\"num\">{step.Removed}</td></tr>");
        }

        region.Append(Invariant, $"<tr data-step=\"excluded\" data-passed=\"{funnel.Passing}\" data-removed=\"{funnel.Excluded}\"><td>Not excluded");

        if (funnel.Exclusions.Count > 0)
        {
            region.Append(" <span class=\"exclusions\">(");
            region.Append(string.Join(", ", funnel.Exclusions.Select(pair => Formatted($"<span data-exclusion=\"{Escaped(pair.Exclusion)}\" data-count=\"{pair.Count}\">{pair.Count} {Escaped(pair.Exclusion)}</span>"))));
            region.Append(")</span>");
        }

        region.Append(Invariant, $"</td><td class=\"num\">{funnel.Passing}</td><td class=\"num\">{funnel.Excluded}</td></tr>");
        region.Append("</table></div>");
        region.Append(Invariant, $"<p class=\"funnel-version\" data-version=\"{Escaped(funnel.Version)}\">");
        region.Append(funnel.Version == "none"
            ? "No filter version is open, so the night ran on section 17's proposed values."
            : Formatted($"The night ran under filter version {Escaped(funnel.Version)}."));
        // Whether these counts drew the evening's list is the evening's rule.
        // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
        region.Append(Invariant, $" {funnel.Passing} of {funnel.Members} member(s) pass.");
        region.Append(funnel.Rule == ListRules.Filter
            ? " The names passing are this evening's list.</p>"
            : " The six reasons drew this evening's list, and these counts decided nothing on it.</p>");

        return region.ToString();
    }

    // The plan a trade gate read, in the words the pages use for it.
    // see: The swing filter's trade gate reads section 10's plan for the swing trade, and the plan at the nearest bands is the variant in the reward to risk variant's place
    public static string PlanWords(string? input) => input switch
    {
        FilterSettings.LadderWord => "the ladder's first tranche",
        FilterSettings.SwingWord => "the swing trade at the nearest bands",
        FilterSettings.ClearWord => "the swing trade clear of the noise",
        _ => "the plan named " + (input ?? "none"),
    };

    // A name's gates, section 15.9's row: each of the five with whether it passed and why, the setup's
    // family and the trigger, the trade read from the ladder's first tranche, from the swing trade at the
    // nearest bands and from the swing trade clear of the noise with the one the trade gate read marked,
    // and the exclusions and notes, each whole on its element as the store holds it.
    public string GatesTable(string ticker, GatesView view)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"gates\" data-ticker=\"{Escaped(ticker)}\" data-session=\"{view.Session:yyyy-MM-dd}\" data-version=\"{Escaped(view.Version)}\" data-passed=\"{(view.Passed ? "yes" : "no")}\" data-rank=\"{(view.Rank is { } at ? at.ToString(Invariant) : "none")}\">");
        region.Append("<div class=\"tbl-wrap\"><table class=\"gates-table\"><tr><th>Gate</th><th>Passed</th><th>Why</th></tr>");

        foreach (var gate in view.Gates)
        {
            region.Append(Invariant, $"<tr data-gate=\"{Escaped(gate.Gate)}\" data-passed=\"{(gate.Passed ? "yes" : "no")}\"><td>{Escaped(gate.Gate)}</td><td>{(gate.Passed ? "passed" : "failed")}</td><td>{Escaped(gate.Reason)}</td></tr>");
        }

        region.Append("</table></div>");

        region.Append(Invariant, $"<p class=\"family\" data-family=\"{Escaped(view.Family ?? "none")}\" data-trigger-event=\"{(view.TriggerEvent is { } happened ? (happened ? "yes" : "no") : "none")}\">");
        region.Append(view.Family is { } family ? Formatted($"The setup is a {Escaped(family)}") : "No setup passed");
        region.Append(view.TriggerEvent switch
        {
            true => "; the trigger's event happened on the night.",
            false => "; the trigger's event did not happen on the night.",
            null => "; the night's bars cannot say whether the trigger's event happened.",
        });
        region.Append("</p>");

        region.Append("<div class=\"tbl-wrap\"><table class=\"trade-table\"><tr><th>Plan</th><th>Reward to risk</th><th>Stop below the entry</th></tr>");
        region.Append(Invariant, $"<tr data-plan=\"{FilterSettings.LadderWord}\" data-read=\"{Read(FilterSettings.LadderWord)}\" data-reward-to-risk=\"{Whole(view.LadderRewardToRisk)}\" data-stop-moves=\"{Whole(view.LadderStopMoves)}\"><td>{Capitalised(PlanWords(FilterSettings.LadderWord))}{Marked(FilterSettings.LadderWord)}</td><td class=\"num\">{Ratio(view.LadderRewardToRisk)}</td><td class=\"num\">{Moves(view.LadderStopMoves)}</td></tr>");
        region.Append(Invariant, $"<tr data-plan=\"{FilterSettings.SwingWord}\" data-read=\"{Read(FilterSettings.SwingWord)}\" data-entry=\"{Plain(view.SwingEntry)}\" data-stop=\"{Plain(view.SwingStop)}\" data-target=\"{Plain(view.SwingTarget)}\" data-reward-to-risk=\"{Whole(view.SwingRewardToRisk)}\" data-stop-moves=\"{Whole(view.SwingStopMoves)}\"><td>{Capitalised(PlanWords(FilterSettings.SwingWord))}: in at {Entry()}, stop {Level(view.SwingStop)}, target {Level(view.SwingTarget)}{Marked(FilterSettings.SwingWord)}</td><td class=\"num\">{Ratio(view.SwingRewardToRisk)}</td><td class=\"num\">{Moves(view.SwingStopMoves)}</td></tr>");
        region.Append(Invariant, $"<tr data-plan=\"{FilterSettings.ClearWord}\" data-read=\"{Read(FilterSettings.ClearWord)}\" data-entry=\"{Plain(view.SwingEntry)}\" data-stop=\"{Plain(view.ClearStop)}\" data-target=\"{Plain(view.ClearTarget)}\" data-reward-to-risk=\"{Whole(view.ClearRewardToRisk)}\" data-stop-moves=\"{Whole(view.ClearStopMoves)}\"><td>{Capitalised(PlanWords(FilterSettings.ClearWord))}: in at {Entry()}, stop {Level(view.ClearStop)}, target {Level(view.ClearTarget)}{Marked(FilterSettings.ClearWord)}</td><td class=\"num\">{Ratio(view.ClearRewardToRisk)}</td><td class=\"num\">{Moves(view.ClearStopMoves)}</td></tr>");
        region.Append("</table></div>");

        string Read(string plan) => view.Input == plan ? "yes" : "no";

        string Marked(string plan) => view.Input == plan ? " <span class=\"plan-read\">(the plan the trade gate read)</span>" : string.Empty;

        string Entry() => view.SwingEntry is { } entry ? Price(entry) : "no close";

        static string Level(decimal? value) => value is { } held ? Price(held) : "none";

        static string Capitalised(string words) => char.ToUpperInvariant(words[0]) + words[1..];

        region.Append(Invariant, $"<p class=\"exclusions\" data-exclusions=\"{Escaped(string.Join(",", view.Exclusions))}\">");
        region.Append(view.Exclusions.Count == 0 ? "No exclusion applies." : "Excluded: " + Escaped(string.Join(", ", view.Exclusions)) + ".");

        foreach (var note in view.Notes)
        {
            region.Append(' ').Append(Escaped(char.ToUpperInvariant(note[0]) + note[1..])).Append('.');
        }

        region.Append("</p></div>");

        return region.ToString();

        static string Ratio(double? value) => value is { } held ? held.ToString("0.00", Invariant) : "<span class=\"degraded\">none</span>";

        static string Moves(double? value) => value is { } held ? held.ToString("0.00", Invariant) + " typical moves" : "<span class=\"degraded\">none</span>";

        static string Plain(decimal? value) => value is { } held ? held.ToString(Invariant) : "none";
    }

    // The run page's market reading, section 15.10's row: the breadth with how many members it was
    // counted over, the share above the shorter average as context, and the index's median volume
    // against its fifty-day average, each drawn whole as the store holds it.
    public string MarketReading(MarketView? market)
    {
        if (market is null)
        {
            return "<p class=\"degraded\" data-market=\"none\">no market reading is stored for this night</p>";
        }

        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"market-table\" data-session=\"{market.Session:yyyy-MM-dd}\" data-members=\"{market.Members}\">");
        region.Append("<tr><th>Reading</th><th>On the night</th></tr>");
        region.Append(Invariant, $"<tr data-part=\"breadth\" data-breadth=\"{Whole(market.Breadth)}\" data-counted=\"{market.Counted}\" data-above=\"{market.Above}\"><td>Breadth</td><td>");
        region.Append(market.Breadth is { } share
            ? Formatted($"{share * 100:0.0}% of the {market.Counted} members read close above their own {SwingReadings.BreadthAverageSessions}-day average, of {market.Members} in the index")
            : Formatted($"not available: {market.Counted} of the {market.Members} members hold a close and a {SwingReadings.BreadthAverageSessions}-day average, fewer than half"));
        region.Append("</td></tr>");
        region.Append(Invariant, $"<tr data-part=\"context\" data-breadth-context=\"{Whole(market.BreadthContext)}\" data-counted-context=\"{market.CountedContext}\"><td>Above the {SwingReadings.ContextAverageSessions}-day average, as context</td><td>");
        region.Append(market.BreadthContext is { } context
            ? Formatted($"{context * 100:0.0}% of the {market.CountedContext} members read")
            : "<span class=\"degraded\">not available</span>");
        region.Append("</td></tr>");
        region.Append(Invariant, $"<tr data-part=\"volume\" data-median-volume-ratio=\"{Whole(market.MedianVolumeRatio)}\" data-volume-counted=\"{market.VolumeCounted}\"><td>The index's median volume against its fifty-day average</td><td>");
        region.Append(market.MedianVolumeRatio is { } ratio
            ? Formatted($"{ratio:0.00} over the {market.VolumeCounted} members trading")
            : "<span class=\"degraded\">not available</span>");
        region.Append("</td></tr></table></div>");

        return region.ToString();
    }

    // A name's swing readings, section 15.9's row: each return with its place among the members'
    // returns, the recent high and the pullback from it, the volume while it came down and the
    // range's tightness, each drawn whole on its element as the store holds it, and a line naming
    // why where the night read nothing for the name.
    public string SwingTable(string ticker, SwingReadingsView view)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"swing-readings\" data-ticker=\"{Escaped(ticker)}\" data-session=\"{view.Session:yyyy-MM-dd}\" data-bars=\"{view.Bars}\">");

        if (view.Note is { } why)
        {
            region.Append(Invariant, $"<p class=\"degraded\" data-reading=\"none\">no swing readings for this night: {Escaped(why)}</p></div>");

            return region.ToString();
        }

        region.Append("<div class=\"tbl-wrap\"><table class=\"swing-table\"><tr><th>Reading</th><th>On the night</th></tr>");

        string ReturnCell(double? value, double? place, int sessions) =>
            value is { } made
                ? Formatted($"{made:+0.00;-0.00;0.00}% over {sessions} sessions") +
                    (place is { } share ? Formatted($", above {share * 100:0.0}% of the other members' returns") : ", with no other member's return to place it among")
                : Formatted($"<span class=\"degraded\">not available, {view.Bars} bars</span>");

        region.Append(Invariant, $"<tr data-reading=\"return-short\"><td>Return over {SwingReadings.ReturnShortSessions} sessions</td><td data-value=\"{Whole(view.ReturnShort)}\" data-place=\"{Whole(view.PlaceShort)}\">");
        region.Append(ReturnCell(view.ReturnShort, view.PlaceShort, SwingReadings.ReturnShortSessions)).Append("</td></tr>");
        region.Append(Invariant, $"<tr data-reading=\"return-long\"><td>Return over {SwingReadings.ReturnLongSessions} sessions</td><td data-value=\"{Whole(view.ReturnLong)}\" data-place=\"{Whole(view.PlaceLong)}\">");
        region.Append(ReturnCell(view.ReturnLong, view.PlaceLong, SwingReadings.ReturnLongSessions)).Append("</td></tr>");

        region.Append(Invariant, $"<tr data-reading=\"recent-high\"><td>Highest high of the last {SwingReadings.HighWindow} sessions</td>");
        region.Append(Invariant, $"<td data-value=\"{(view.RecentHigh is { } high ? high.ToString(Invariant) : "none")}\" data-session=\"{(view.HighSession is { } made ? made.ToString("yyyy-MM-dd", Invariant) : "none")}\" data-since=\"{(view.PullbackSessions is { } since ? since.ToString(Invariant) : "none")}\">");
        if (view.RecentHigh is { } top && view.HighSession is { } on && view.PullbackSessions is { } after)
        {
            region.Append(Invariant, $"{Price(top)} on {on:yyyy-MM-dd}, {after} session(s) ago");
        }
        else
        {
            region.Append(Formatted($"<span class=\"degraded\">not available, {view.Bars} bars</span>"));
        }

        region.Append("</td></tr>");

        region.Append(Invariant, $"<tr data-reading=\"depth\"><td>How far the close sits below it</td><td data-value=\"{Whole(view.Depth)}\">");
        region.Append(view.Depth is { } depth ? Formatted($"{depth:0.00} typical days' moves") : "<span class=\"degraded\">not available</span>");
        region.Append("</td></tr>");

        region.Append(Invariant, $"<tr data-reading=\"dry-up\"><td>Volume since the high against its fifty-day average</td><td data-value=\"{Whole(view.DryUp)}\">");
        region.Append(view.DryUp is { } dry
            ? Formatted($"{dry:0.00}")
            : view.PullbackSessions == 0
                ? "<span class=\"degraded\">the high was made on the night, so no session has come down from it</span>"
                : "<span class=\"degraded\">not available</span>");
        region.Append("</td></tr>");

        region.Append(Invariant, $"<tr data-reading=\"tightness\"><td>True range of the last {SwingReadings.TightShortSessions} sessions against the last {SwingReadings.TightLongSessions}</td><td data-value=\"{Whole(view.Tightness)}\">");
        region.Append(view.Tightness is { } tight ? Formatted($"{tight:0.00}") : Formatted($"<span class=\"degraded\">not available, {view.Bars} bars</span>"));
        region.Append("</td></tr></table></div></div>");

        return region.ToString();
    }

    // The filters, section 15.8's third region: trend state and sector as chips,
    // in the hash so a filtered view is a link.
    //
    // Drawn from the rows rather than from a list written here, so a state or a
    // sector the store holds and this file has never heard of still gets a chip.
    // The paging nav, section 15.8's "paged".
    //
    // The page is in the hash beside the filters, so a page of a filtered view
    // is a link, and each link carries the filters it was drawn under rather
    // than dropping them: a next-page link that cleared the chips would take a
    // reader from a filtered page 1 to an unfiltered page 2 and look like paging.
    //
    // It states which page of how many over how many rows. A nav that drew only
    // arrows says nothing about where a reader is or how much is left, which on
    // five hundred rows is the only question paging raises. The count comes first,
    // beside its own word, and the page after it, so no figure follows another
    // across a comma, which a reader takes for one number with its thousands set off.
    public string UniversePaging(int rows, int page, int pageSize, string? trend, string? sector)
    {
        var pages = Math.Max(1, (rows + pageSize - 1) / pageSize);
        var at = Math.Clamp(page, 1, pages);

        var nav = new StringBuilder();

        nav.Append(Invariant, $"<nav class=\"universe-paging\" data-page=\"{at}\" data-pages=\"{pages}\" ");
        nav.Append(Invariant, $"data-rows=\"{rows}\" data-page-size=\"{pageSize}\">");

        string Link(int to, string label, string rel) =>
            Formatted($"<a class=\"page\" rel=\"{rel}\" data-page=\"{to}\" href=\"{Query(to, trend, sector)}\">{Escaped(label)}</a>");

        nav.Append(at > 1
            ? Link(at - 1, "previous", "prev")
            : "<span class=\"page degraded\" data-page=\"none\" rel=\"prev\">previous</span>");

        nav.Append(Invariant, $"<span class=\"page-of\">{rows} {(rows == 1 ? "name" : "names")}, page {at} of {pages}</span>");

        nav.Append(at < pages
            ? Link(at + 1, "next", "next")
            : "<span class=\"page degraded\" data-page=\"none\" rel=\"next\">next</span>");

        nav.Append("</nav>");

        return nav.ToString();
    }

    // The universe route's hash, with the filters it was drawn under kept.
    static string Query(int page, string? trend, string? sector)
    {
        var parts = new List<string> { Formatted($"page={page}") };

        if (trend is { Length: > 0 })
        {
            parts.Add(Formatted($"trend={Uri.EscapeDataString(trend)}"));
        }

        if (sector is { Length: > 0 })
        {
            parts.Add(Formatted($"sector={Uri.EscapeDataString(sector)}"));
        }

        return "#/universe?" + string.Join("&amp;", parts);
    }

    public string UniverseFilters(IReadOnlyList<UniverseCell> rows, string? trend = null, string? sector = null)
    {
        var states = rows
            .Select(row => row.TrendState ?? NotClassified)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(state => state, StringComparer.Ordinal)
            .ToArray();

        var sectors = rows
            .Select(row => row.Sector)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sector => sector, StringComparer.Ordinal)
            .ToArray();

        var filters = new StringBuilder();

        filters.Append(Invariant, $"<nav class=\"universe-filters\" data-states=\"{states.Length}\" data-sector-chips=\"{sectors.Length}\">");

        // Two rows of chips, each with an "all" chip, and the lit chip marked. A chip keeps
        // the other row's filter, so lighting a trend does not clear a sector.
        filters.Append("<span class=\"chips-label\">Trend</span>");
        filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"trend\" data-value=\"all\" aria-pressed=\"{Flag(trend is null)}\" href=\"{Query(1, null, sector)}\">all</a>");

        foreach (var state in states)
        {
            filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"trend\" data-value=\"{Escaped(state)}\" aria-pressed=\"{Flag(state == trend)}\" ");
            filters.Append(Invariant, $"href=\"#/universe?trend={Uri.EscapeDataString(state)}{(sector is { Length: > 0 } kept ? "&amp;sector=" + Uri.EscapeDataString(kept) : string.Empty)}\">{Escaped(state.Replace('_', ' '))}</a>");
        }

        filters.Append("<span class=\"chip-break\"></span><span class=\"chips-label\">Sector</span>");
        filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"sector\" data-value=\"all\" aria-pressed=\"{Flag(sector is null)}\" href=\"{Query(1, trend, null)}\">all</a>");

        foreach (var named in sectors)
        {
            filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"sector\" data-value=\"{Escaped(named)}\" aria-pressed=\"{Flag(named == sector)}\" ");
            filters.Append(Invariant, $"href=\"#/universe?sector={Uri.EscapeDataString(named)}{(trend is { Length: > 0 } kept ? "&amp;trend=" + Uri.EscapeDataString(kept) : string.Empty)}\">{Escaped(named)}</a>");
        }

        filters.Append("</nav>");

        return filters.ToString();
    }

    // The trade line, section 15.5's eighth mark: one trade from its stop at the left to its target at the
    // right, the buy a short tick between them, and a dot for the price, hollow while the trade is open and
    // filled where it finished. The stop and the target keep the two hues a level below and above the price
    // own, and nothing else on the mark uses them; a price past either end sits at that end rather than off
    // the line. It is placed by ratios to the buy, so a split since the listing moves no part of it.
    //
    // It degrades by saying what it has: a plan missing a price, or whose prices are out of order, draws no
    // line, and a trade whose outcome row is missing or an open one with no close to place draws the line
    // and no dot, each saying why.
    // see: Support and resistance own two hues and nothing else uses them
    // see: Marks are defined once and every screen draws from that list
    public string TradeLine(PickCell pick)
    {
        const double Left = 10;
        const double Right = 150;
        const int Wide = 160;
        const int Height = 30;
        const int Middle = 15;

        var mark = new StringBuilder();

        mark.Append(Invariant, $"<svg class=\"trade-line\" role=\"img\" viewBox=\"0 0 {Wide} {Height}\" width=\"{Wide}\" height=\"{Height}\" ");
        mark.Append(Invariant, $"data-ticker=\"{Escaped(pick.Ticker)}\" data-night=\"{DayOf(pick.Night)}\" data-status=\"{pick.Status}\" ");
        mark.Append(Invariant, $"data-buy=\"{StoredPrice(pick.Buy)}\" data-stop=\"{StoredPrice(pick.Stop)}\" data-target=\"{StoredPrice(pick.Target)}\" data-along=\"{Whole(pick.Along)}\"");

        if (pick.Buy is not { } buy || pick.Stop is not { } stop || pick.Target is not { } target || stop >= buy || buy >= target)
        {
            var missing = pick.Stop is null
                ? "no stop stored"
                : pick.Target is null
                    ? "no target stored"
                    : pick.Buy is null ? "no buy stored" : "the stop, buy and target are not in order";

            mark.Append(" data-dot=\"none\">");
            mark.Append(Invariant, $"<rect class=\"m-absent\" x=\"0.5\" y=\"2.5\" width=\"{Wide - 1}\" height=\"{Height - 5}\"/>");
            mark.Append(Invariant, $"<text class=\"tl-say\" x=\"{Wide / 2}\" y=\"{Middle + 4}\" text-anchor=\"middle\">{missing}</text>");
            mark.Append(Invariant, $"<title>{Escaped(pick.Ticker)}, listed {DayOf(pick.Night)}: {missing}</title></svg>");

            return mark.ToString();
        }

        var low = EquityBrief.Core.Prices.Statistic.FromRatio(stop / buy);
        var high = EquityBrief.Core.Prices.Statistic.FromRatio(target / buy);

        double At(double ratio) => Left + (Math.Clamp((ratio - low) / (high - low), 0, 1) * (Right - Left));

        var dot = pick.Status == PickStatus.Missing || pick.Along is null
            ? "none"
            : pick.Status == PickStatus.Open ? "open" : "filled";

        mark.Append(Invariant, $" data-dot=\"{dot}\">");
        mark.Append(Invariant, $"<line class=\"tl-track\" x1=\"{Left}\" y1=\"{Middle}\" x2=\"{Right}\" y2=\"{Middle}\"/>");
        mark.Append(Invariant, $"<line class=\"tl-stop\" x1=\"{Left}\" y1=\"6\" x2=\"{Left}\" y2=\"24\"/>");
        mark.Append(Invariant, $"<line class=\"tl-target\" x1=\"{Right}\" y1=\"6\" x2=\"{Right}\" y2=\"24\"/>");
        mark.Append(Invariant, $"<line class=\"tl-buy\" x1=\"{Number(At(1))}\" y1=\"10\" x2=\"{Number(At(1))}\" y2=\"20\"/>");

        var plan = Formatted($"Stop {Price(stop)}, buy {Price(buy)}, target {Price(target)}");

        switch (dot)
        {
            case "none":
                var absent = pick.Status == PickStatus.Missing ? "no outcome stored" : "no close stored to place";

                mark.Append(Invariant, $"<text class=\"tl-say\" x=\"{Wide / 2}\" y=\"29\" text-anchor=\"middle\">{absent}</text>");
                mark.Append(Invariant, $"<title>{plan}; {absent}</title>");
                break;

            case "open":
                mark.Append(Invariant, $"<circle class=\"tl-open\" cx=\"{Number(At(pick.Along!.Value))}\" cy=\"{Middle}\" r=\"4.5\"/>");
                mark.Append(Invariant, $"<title>{plan}; the close of {(pick.NowOn is { } on ? DayOf(on) : "none")}, {(pick.NowClose is { } now ? Price(now) : "none")}</title>");
                break;

            default:
                mark.Append(Invariant, $"<circle class=\"tl-done\" cx=\"{Number(At(pick.Along!.Value))}\" cy=\"{Middle}\" r=\"4.5\"/>");
                mark.Append(Invariant, $"<title>{plan}; {PickStatus.Words(pick.Status).ToLowerInvariant()} on {(pick.EndedOn is { } ended ? DayOf(ended) : "none")}, {(pick.ReturnPct is { } made ? made.ToString("+0.00;-0.00;0.00", Invariant) : "none")}% from the buy</title>");
                break;
        }

        mark.Append("</svg>");

        return mark.ToString();
    }

    // The state word a row draws beside the trend word, focusable so the sentences show from the keyboard
    // as from the pointer, and nothing on a night that stored no readings.
    // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
    static string BusinessWord(NumbersRow? business) =>
        business is null
            ? string.Empty
            : $" <span class=\"business\" tabindex=\"0\" data-state=\"{Escaped(business.State)}\">{Escaped(business.State)}"
                + $"<span class=\"says\" role=\"tooltip\">{string.Join(" ", business.Sentences.Select(Escaped))}</span></span>";

    // "What the numbers say", the block a name's numbers open with: the heading carrying the state, the
    // quarter the readings were read from, one sentence per reading, and folded beneath them the quarters
    // behind them with the dates each was filed and reported on, then the full numbers table.
    // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
    public string NumbersSay(NumbersSayView view, string numbers)
    {
        var block = new StringBuilder();

        block.Append(Invariant, $"<section class=\"numbers-say\" data-state=\"{Escaped(view.State)}\" data-read-from=\"{(view.ReadFrom is { } from ? DayOf(from) : "none")}\">");
        block.Append(Invariant, $"<h4 class=\"says-heading\">{Escaped(view.Heading)}</h4>");

        if (view.ReadFrom is { } read)
        {
            block.Append(Invariant, $"<p class=\"read-from\">Read from the quarter to {DayOf(read)}.</p>");
        }

        block.Append("<ul class=\"says\">");

        foreach (var sentence in view.Sentences)
        {
            block.Append(Invariant, $"<li>{Escaped(sentence)}</li>");
        }

        block.Append("</ul>");
        block.Append("<details class=\"numbers-behind\"><summary>The quarters these read, and the full numbers</summary>");

        if (view.Quarters.Count > 0)
        {
            block.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"quarters-read\" data-quarters=\"{view.Quarters.Count}\"><thead><tr><th>Quarter to</th><th>Filed</th><th>Reported</th></tr></thead><tbody>");

            foreach (var quarter in view.Quarters)
            {
                block.Append(Invariant, $"<tr data-quarter=\"{DayOf(quarter.PeriodEnd)}\"><td class=\"num\">{DayOf(quarter.PeriodEnd)}</td>");
                block.Append(Invariant, $"<td class=\"num\">{(quarter.FilingDate is { } filed ? DayOf(filed) : "<span class=\"degraded\">not filed</span>")}</td>");
                block.Append(Invariant, $"<td class=\"num\">{(quarter.ReportDate is { } reported ? DayOf(reported) : "<span class=\"degraded\">not stated</span>")}</td></tr>");
            }

            block.Append("</tbody></table></div>");
        }

        block.Append(numbers);
        block.Append("</details></section>");

        return block.ToString();
    }

    // What a Past picks row says of a night that stored no readings of the reported quarters.
    public const string NotReadThatNight = "not read that night";

    // The trades as a table, newest first: the night listed, the stock where the table holds more than one
    // name's, the buy, the stop and the target each whole on its cell, the trade line, the status in words,
    // the sessions held and the result as a signed multiple of the risk, or open. A table on a name's own
    // page leaves the stock out and links the night to that night's page instead.
    public string PicksTable(IReadOnlyList<PickCell> rows, bool named)
    {
        var table = new StringBuilder();

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"picks-table\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in PicksHeadings(named))
        {
            table.Append(TippedHeading(heading, says, heading is "Buy" or "Stop" or "Target" or "Sessions held" or "Result" ? "r" : null));
        }

        table.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            var page = Formatted($"#/name/{Uri.EscapeDataString(row.Ticker)}/{DayOf(row.Night)}");

            table.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-night=\"{DayOf(row.Night)}\" data-status=\"{row.Status}\" data-plan=\"{Escaped(row.Plan)}\">");
            table.Append(named
                ? Formatted($"<td class=\"num\">{DayOf(row.Night)}</td><td class=\"c-nm\"><a class=\"nm\" href=\"{page}\"><span class=\"tk\">{Escaped(row.Ticker)}</span>{(row.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</a></td>")
                : Formatted($"<td class=\"num\"><a href=\"{page}\">{DayOf(row.Night)}</a></td>"));

            // The state the trade carried on its listing night, and a night before the readings existed says
            // it was not read then rather than drawing an empty cell.
            // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
            table.Append(row.State is { } state
                ? Formatted($"<td class=\"state-then\" data-state=\"{Escaped(state)}\">{Escaped(state)}</td>")
                : $"<td class=\"state-then\" data-state=\"none\"><span class=\"degraded\">{NotReadThatNight}</span></td>");

            foreach (var (name, price) in new[] { ("buy", row.Buy), ("stop", row.Stop), ("target", row.Target) })
            {
                table.Append(Invariant, $"<td class=\"r num\" data-{name}=\"{StoredPrice(price)}\">{(price is { } held ? Price(held) : "<span class=\"degraded\">none</span>")}</td>");
            }

            table.Append(Invariant, $"<td>{TradeLine(row)}</td>");
            table.Append(Invariant, $"<td class=\"status\">{PickStatus.Words(row.Status)}</td>");
            table.Append(Invariant, $"<td class=\"r num\" data-sessions=\"{(row.Sessions is { } counted ? counted.ToString(Invariant) : "none")}\">{(row.Sessions is { } sessions ? sessions.ToString(Invariant) : "<span class=\"degraded\">not counted</span>")}</td>");
            table.Append(row.Status == PickStatus.Open
                ? "<td class=\"r res open\" data-result=\"open\">open</td>"
                : row.Result is { } result
                    ? Formatted($"<td class=\"r res\" data-result=\"{result.ToString("R", Invariant)}\">{result.ToString("+0.00;-0.00;0.00", Invariant)} &#215;</td>")
                    : "<td class=\"r res\" data-result=\"none\"><span class=\"degraded\">none</span></td>");
            table.Append("</tr>");
        }

        table.Append("</tbody></table></div>");

        return table.ToString();
    }

    // The Past picks screen's counts: the trades listed over their nights, how many stand open, finished,
    // reached target, stopped out and ran out of time, and a line for trades whose outcome row is missing.
    // Below the minimum a dashed outline states the finished trades and their nights against the numbers
    // needed and no share; at or above it, the finished trades as a bar in three steps of one neutral hue
    // with a line at their average break-even, and the share that reached its target, the share needed to
    // break even and the average result, always together.
    // see: Not yet measured is drawn as a dashed outline, never as a pale value
    // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
    public string PicksCounts(PicksSummary summary)
    {
        var counts = new StringBuilder();

        counts.Append(Invariant, $"<dl class=\"counts\" data-listed=\"{summary.Listed}\" data-nights=\"{summary.Nights}\" data-open=\"{summary.Open}\" data-finished=\"{summary.Finished}\" ");
        counts.Append(Invariant, $"data-target=\"{summary.Target}\" data-stopped=\"{summary.Stopped}\" data-time=\"{summary.Time}\" data-missing=\"{summary.Missing}\">");
        counts.Append(Invariant, $"<div><dt>Trades listed</dt><dd>{summary.Listed}<span class=\"grp\">over {summary.Nights} night{(summary.Nights == 1 ? string.Empty : "s")}</span></dd></div>");

        foreach (var (words, count) in new[] { ("Still open", summary.Open), ("Finished", summary.Finished), ("Reached target", summary.Target), ("Stopped out", summary.Stopped), ("Ran out of time", summary.Time) })
        {
            counts.Append(Invariant, $"<div><dt>{words}</dt><dd>{count}</dd></div>");
        }

        counts.Append("</dl>");

        if (summary.Missing > 0)
        {
            counts.Append(Invariant, $"<p class=\"degraded\" data-missing=\"{summary.Missing}\">{summary.Missing} trade{(summary.Missing == 1 ? " has" : "s have")} no outcome row stored, so {(summary.Missing == 1 ? "it counts" : "they count")} as listed and in no status.</p>");
        }

        if (summary.TargetShare is not { } share)
        {
            counts.Append(Invariant, $"<p class=\"not-yet-rate\" data-decided=\"{summary.Decided}\" data-needed=\"{summary.MinimumDecided}\" data-decided-nights=\"{summary.DecidedNights}\" data-nights-needed=\"{summary.MinimumNights}\">");
            counts.Append(Invariant, $"<b>{summary.Decided}</b> trade{(summary.Decided == 1 ? string.Empty : "s")} decided at the target or the stop of the <b>{summary.MinimumDecided}</b> needed, over <b>{summary.DecidedNights}</b> of the <b>{summary.MinimumNights}</b> listing nights needed. ");
            counts.Append("The share that reached its target is drawn once both are met, the same minimums the run page's records wait on, and a trade that ran out of time is never in it. Until then the trades below are the whole story.</p>");

            return counts.ToString();
        }

        // The bar: the finished trades in three steps of one neutral hue, those that reached the target, then
        // those stopped out, then those that ran out of time, and the line at the decided trades' average
        // break-even across the part of the bar they fill, so the first step reaching past it is the share
        // clearing the bar its trades set.
        const int Wide = 620;

        var finished = Math.Max(1, summary.Finished);
        var reached = 1.0 * Wide * summary.Target / finished;
        var stopped = 1.0 * Wide * summary.Stopped / finished;
        double? even = summary.BreakEven is { } bar ? Math.Clamp(bar, 0, 100) * (reached + stopped) / 100 : null;

        counts.Append(Invariant, $"<div class=\"rate\" data-share=\"{share.ToString("R", Invariant)}\" data-break-even=\"{Whole(summary.BreakEven)}\" data-average-result=\"{Whole(summary.AverageResult)}\">");
        counts.Append(Invariant, $"<svg class=\"rate-bar\" viewBox=\"0 0 {Wide} 46\" role=\"img\" aria-label=\"{summary.Finished} finished trades: {summary.Target} reached target, {summary.Stopped} stopped out, {summary.Time} ran out of time\">");
        counts.Append(Invariant, $"<rect class=\"rb-t\" x=\"0\" y=\"8\" width=\"{Number(reached)}\" height=\"18\"/>");
        counts.Append(Invariant, $"<rect class=\"rb-s\" x=\"{Number(reached)}\" y=\"8\" width=\"{Number(stopped)}\" height=\"18\"/>");
        counts.Append(Invariant, $"<rect class=\"rb-o\" x=\"{Number(reached + stopped)}\" y=\"8\" width=\"{Number(Wide - reached - stopped)}\" height=\"18\"/>");

        if (even is { } line)
        {
            counts.Append(Invariant, $"<line class=\"rb-even\" x1=\"{Number(line)}\" y1=\"2\" x2=\"{Number(line)}\" y2=\"32\"/>");
            counts.Append(Invariant, $"<text class=\"rb-lab\" x=\"{Number(Math.Clamp(line, 50, Wide - 50))}\" y=\"44\" text-anchor=\"middle\">break-even {summary.BreakEven!.Value.ToString("0.0", Invariant)}%</text>");
        }

        counts.Append("</svg>");
        counts.Append(Invariant, $"<div class=\"rb-legend\"><span><i class=\"rb-key-t\"></i>reached target {summary.Target}</span><span><i class=\"rb-key-s\"></i>stopped out {summary.Stopped}</span><span><i class=\"rb-key-o\"></i>ran out of time {summary.Time}</span><span>of {summary.Finished} finished</span></div>");
        counts.Append("<dl class=\"three\">");
        counts.Append(Invariant, $"<div><dt>Reached target first</dt><dd>{share.ToString("0.0", Invariant)}%</dd><small>{summary.Target} of the {summary.Decided} decided at the target or the stop</small></div>");
        counts.Append(Invariant, $"<div><dt>Needed to break even</dt><dd>{(summary.BreakEven is { } needed ? needed.ToString("0.0", Invariant) + "%" : "none")}</dd><small>the mean of each decided trade's own bar</small></div>");
        counts.Append(Invariant, $"<div><dt>Average result</dt><dd>{(summary.AverageResult is { } average ? average.ToString("+0.00;-0.00;0.00", Invariant) + " &#215; risk" : "none")}</dd><small>per finished trade</small></div>");
        counts.Append("</dl></div>");

        return counts.ToString();
    }

    // The Past picks screen's filters, one chip a status with the trades it holds, and all. None by setup.
    public string PicksFilters(PicksSummary summary, string? status)
    {
        var filters = new StringBuilder();
        var lit = status is { } asked && PickStatus.Filters.Contains(asked, StringComparer.Ordinal) ? asked : null;

        filters.Append("<nav class=\"universe-filters picks-filters\" aria-label=\"Filter the trades\"><span class=\"chips-label\">Status</span>");
        filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"status\" data-value=\"all\" aria-pressed=\"{Flag(lit is null)}\" href=\"#/picks\">All<span class=\"n\">{summary.Listed}</span></a>");

        foreach (var each in PickStatus.Filters)
        {
            filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"status\" data-value=\"{each}\" aria-pressed=\"{Flag(each == lit)}\" href=\"#/picks?status={each}\">{PickStatus.Chip(each)}<span class=\"n\">{summary.Of(each)}</span></a>");
        }

        filters.Append("</nav>");

        return filters.ToString();
    }

    // A name's own page, after the plan: how many times the live list picked it before the night drawn and
    // how each group ended, then a row per earlier listing. A name never picked draws none of it.
    public string OnTheListBefore(string ticker, IReadOnlyList<PickCell> earlier)
    {
        static string Times(int count) => count switch
        {
            1 => "once",
            2 => "twice",
            _ => count.ToString(Invariant) + " times",
        };

        var groups = new[] { PickStatus.Open, PickStatus.Target, PickStatus.Stopped, PickStatus.Time, PickStatus.Missing }
            .Select(status => (Status: status, Count: earlier.Count(pick => pick.Status == status)))
            .Where(group => group.Count > 0)
            .Select(group => (group.Status == PickStatus.Open ? "still open" : PickStatus.Words(group.Status).ToLowerInvariant()) + " " + Times(group.Count));

        return Formatted($"<p class=\"picked\" data-ticker=\"{Escaped(ticker)}\" data-picked=\"{earlier.Count}\">Picked {Times(earlier.Count)}: {string.Join(", ", groups)}.</p>")
            + PicksTable(earlier, named: false);
    }

    static string DayOf(DateOnly day) => day.ToString("yyyy-MM-dd", Invariant);

    static string StoredPrice(decimal? price) => price is { } held ? held.ToString(Invariant) : "none";

    public string Degraded(string ticker, int bars) =>
        $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-sessions=\"{bars}\">" +
        $"{Escaped(ticker)} has {bars} stored session{(bars == 1 ? string.Empty : "s")}, " +
        $"and a chart needs at least {FewestBars}.</p>";

    // The headline each state of a night is named by, in the words the Run page and tonight's notice draw.
    public static string NightHeadline(NightView night) => night.State switch
    {
        NightStates.Finished => "Finished",
        NightStates.Running => "Running",
        NightStates.Waiting => "Waiting to try again",
        NightStates.Unfinished => night.StoppedAt is { } step ? "Left unfinished at " + step : "Left unfinished",
        NightStates.NotYet => "Not run yet",
        NightStates.NeverRan => "Never ran",
        _ => "No session",
    };

    // The line beneath the headline, saying what the state rests on in plain words.
    public static string NightSaid(NightView night)
    {
        var day = night.Session.ToString("ddd yyyy-MM-dd", Invariant);
        var started = night.Started is { } at ? at.UtcDateTime.ToString("HH:mm", Invariant) + " UTC" : string.Empty;
        var written = night.LastWritten is { } last ? last.UtcDateTime.ToString("HH:mm", Invariant) + " UTC" : string.Empty;

        var next = night.NextTry is { } due ? due.UtcDateTime.ToString("HH:mm", Invariant) + " UTC" : string.Empty;

        var said = night.State switch
        {
            NightStates.Finished => Formatted($"Started {started} and closed its arithmetic in {Span(night.Seconds ?? 0)}, inside its {night.DeadlineMinutes:0}-minute deadline."),
            NightStates.Running => Formatted($"Started {started}; the last step written was {night.StoppedAt} at {written}. Until it finishes, tonight's list is the one before it."),
            NightStates.Waiting => Formatted($"Try {night.TriesMade.Count} stopped at {night.StoppedAt}, and try {night.TriesMade.Count + 1} starts from that step at {next}. Until it finishes, tonight's list is the one before it."),
            NightStates.Unfinished when night.Reason is { } reason => Formatted($"It stopped at {night.StoppedAt}: {reason}"),
            NightStates.Unfinished => Formatted($"Its last step written was {night.StoppedAt} at {written}, and no step recorded a stop, so the night ended without saying why, a machine asleep or a task ended among the causes."),
            NightStates.NotYet => Formatted($"No night has run for {day} yet."),
            NightStates.NeverRan => Formatted($"No night ran for {day}: the run log holds none of its steps."),
            _ => Formatted($"The exchange did not trade on {day}, so the night fetched nothing and exited clean."),
        };

        if (night.StepsRetried > 0 && night.State is NightStates.Finished or NightStates.Running)
        {
            said += Formatted($" It took {night.StepsRetried} more tr{(night.StepsRetried == 1 ? "y" : "ies")} after a step stopped.");
        }

        if (night.AfterTheClose is { } after)
        {
            said += " After the close, " + after;
        }

        return said;

        static string Span(double seconds) =>
            seconds >= 60
                ? Formatted($"{(int)(seconds / 60)} min {(int)(seconds % 60)} s")
                : Formatted($"{(int)seconds} s");
    }

    // The kind of status a night's state is drawn as: blue for a night that finished or is running,
    // violet for one that is waiting, red for a failure alone, and the page's own ink for a day with no
    // session. Green and orange are the levels' and never a status.
    // see: Status is drawn in blues with violet for a wait and red for a failure alone
    public static string NightTone(string state) => state switch
    {
        NightStates.Finished or NightStates.Running => "ok",
        NightStates.NotYet or NightStates.Waiting => "wait",
        NightStates.Unfinished or NightStates.NeverRan => "fail",
        _ => "quiet",
    };

    // Every try a night made that stopped, with the step it stopped at and the reason it wrote, drawn while
    // the night waits to try again or was left unfinished, and nothing otherwise.
    // see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own
    public static string NightTries(NightView night)
    {
        var stopped = night.TriesMade.Where(attempt => attempt.StoppedAt is not null).ToArray();

        if (night.State is not (NightStates.Waiting or NightStates.Unfinished) || stopped.Length == 0)
        {
            return string.Empty;
        }

        var list = new StringBuilder();

        list.Append(Invariant, $"<ol class=\"night-tries\" data-tries=\"{night.TriesMade.Count}\">");

        foreach (var attempt in stopped)
        {
            list.Append(Invariant, $"<li data-try=\"{attempt.Number}\" data-step=\"{Escaped(attempt.StoppedAt!)}\"><b>Try {attempt.Number}</b> stopped at {Escaped(attempt.StoppedAt!)}: {Escaped(attempt.Reason ?? "it wrote no reason")}</li>");
        }

        list.Append("</ol>");

        return list.ToString();
    }

    // The notice at the top of tonight's page: the same state, headline and line the Run page's first
    // region draws, with every try that stopped while the night waits or was left unfinished, and one line
    // where it finished.
    // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
    public string NightNotice(NightView night)
    {
        var tone = NightTone(night.State);

        if (night.State == NightStates.Finished)
        {
            var closed = night.LastWritten is { } at ? at.UtcDateTime.ToString("HH:mm", Invariant) + " UTC" : "its close";

            return $"<p class=\"night-notice\" data-state=\"{Escaped(night.State)}\" data-tone=\"{tone}\" data-session=\"{night.Session.ToString("yyyy-MM-dd", Invariant)}\">The night of {night.Session.ToString("ddd yyyy-MM-dd", Invariant)} finished; its last step was written at {closed}.</p>";
        }

        var notice = new StringBuilder();

        notice.Append(Invariant, $"<div class=\"night-notice\" data-state=\"{Escaped(night.State)}\" data-tone=\"{tone}\" data-session=\"{night.Session:yyyy-MM-dd}\">");
        notice.Append(Invariant, $"<p class=\"nn-headline\"><b>The night of {night.Session:ddd yyyy-MM-dd}: {Escaped(NightHeadline(night))}</b></p>");
        notice.Append(Invariant, $"<p class=\"nn-said\">{Escaped(NightSaid(night))}</p>");
        notice.Append(NightTries(night));
        notice.Append("</div>");

        return notice.ToString();
    }

    // How last night went, section 15.10's first region: the status mark and the headline for the state
    // the night's run log gives it, the line saying what that rests on, the four headline figures and the
    // time bar of the night's steps in their six groups, with where a stopped night stopped.
    // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
    public string NightStatus(NightView night)
    {
        var tone = NightTone(night.State);
        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"night-status\" data-state=\"{Escaped(night.State)}\" data-tone=\"{tone}\" data-session=\"{night.Session:yyyy-MM-dd}\" ");
        region.Append(Invariant, $"data-stocks-read=\"{(night.StocksRead is { } read ? read.ToString(Invariant) : "none")}\" data-requests=\"{night.ProviderRequests}\" ");
        region.Append(Invariant, $"data-spend=\"{night.ResearchSpend.ToString(Invariant)}\" data-retried=\"{night.StepsRetried}\">");

        region.Append("<div class=\"ns-head\">");
        region.Append(Invariant, $"<svg class=\"status-mark\" viewBox=\"0 0 64 64\" width=\"64\" height=\"64\" role=\"img\" aria-label=\"{Escaped(NightHeadline(night))}\">");
        region.Append("<circle cx=\"32\" cy=\"32\" r=\"29\" class=\"sm-ring\"></circle>");
        region.Append(tone switch
        {
            "ok" when night.State == NightStates.Finished => "<path class=\"sm-glyph\" d=\"M19 33 L28 42 L45 23\"></path>",
            "ok" or "wait" => "<path class=\"sm-glyph\" d=\"M32 16 V33 L42 39\"></path>",
            "fail" => "<path class=\"sm-glyph\" d=\"M32 16 V36 M32 45 V47\"></path>",
            _ => "<path class=\"sm-glyph\" d=\"M20 32 H44\"></path>",
        });
        region.Append("</svg><div>");
        region.Append(Invariant, $"<div class=\"lbl\">The night of {night.Session:ddd yyyy-MM-dd}</div>");
        region.Append(Invariant, $"<p class=\"ns-headline\">{Escaped(NightHeadline(night))}</p>");
        region.Append(Invariant, $"<p class=\"ns-said\">{Escaped(NightSaid(night))}</p>");
        region.Append(NightTries(night));
        region.Append("</div></div>");

        region.Append("<div class=\"ns-figures\">");
        region.Append(Tile("stocks read", night.StocksRead is { } stocks ? stocks.ToString(Invariant) : "none", "stocks read"));
        region.Append(Tile("provider requests", night.ProviderRequests.ToString(Invariant), "provider requests"));
        region.Append(Tile("research spend", SpendVerdict.Money(night.ResearchSpend), "spent on research"));
        region.Append(Tile("steps retried", night.StepsRetried.ToString(Invariant), "steps run again"));
        region.Append("</div>");

        region.Append(StepBar(night));
        region.Append("</div>");

        return region.ToString();

        static string Tile(string figure, string value, string words) =>
            Formatted($"<div class=\"tile\" data-figure=\"{figure}\"><b>{Escaped(value)}</b><span>{words}</span></div>");
    }

    // The time bar of the night's steps: one segment for each group the night reached, as wide as its
    // share of the seconds the night's steps took, a group it did not reach as a dashed outline, and the
    // group a stopped night stopped in outlined as a failure; each group named with its seconds beneath.
    static string StepBar(NightView night)
    {
        const double Wide = 1000;
        const double Unreached = 70;
        const double Least = 18;

        var reached = night.Groups.Where(group => group.Reached > 0).ToArray();
        var left = Wide - ((night.Groups.Count - reached.Length) * Unreached);
        var seconds = reached.Sum(group => group.Seconds);
        var bar = new StringBuilder();

        bar.Append(Invariant, $"<div class=\"step-time\"><div class=\"caption\">Where the time went</div>");
        bar.Append(Invariant, $"<svg class=\"step-bar\" viewBox=\"0 0 {Wide} 24\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"The night's steps in {night.Groups.Count} groups\">");

        var at = 0.0;
        var index = 0;

        foreach (var group in night.Groups)
        {
            var width = group.Reached == 0
                ? Unreached
                : Math.Max(Least, seconds > 0 ? left * group.Seconds / seconds : left / reached.Length);

            width = Math.Min(width, Wide - at);

            var kind = group.Stopped ? "stopped" : group.Reached == 0 ? "unreached" : index % 2 == 0 ? "ran" : "ran-2";

            bar.Append(Invariant, $"<rect class=\"sb-{kind}\" data-group=\"{Escaped(group.Name)}\" data-seconds=\"{Number(group.Seconds)}\" x=\"{Number(at + 2)}\" y=\"2\" width=\"{Number(Math.Max(width - 4, 1))}\" height=\"20\" rx=\"3\"></rect>");
            at += width;
            index++;
        }

        bar.Append("</svg><ol class=\"step-groups\">");

        foreach (var group in night.Groups)
        {
            var said = group.Stopped
                ? "stopped here"
                : group.Reached == 0
                    ? "not reached"
                    : Formatted($"{Number(group.Seconds)} s");

            bar.Append(Invariant, $"<li data-group=\"{Escaped(group.Name)}\" data-reached=\"{group.Reached}\"><b>{Escaped(group.Name)}</b> {said}</li>");
        }

        bar.Append("</ol></div>");

        return bar.ToString();
    }

    // The market, section 15.10's second region: the one-word label its rule gives the night's breadth,
    // a gauge of that breadth with the market gate's floor marked, the share above the shorter average and
    // the index's volume against its own, the rule in words, and the breadth line over the sessions up to
    // the night that the store holds averages for.
    // see: The market on the Run page is named in one word by a stated rule that moves no gate
    public string MarketRegion(MarketPicture picture)
    {
        var night = picture.Night;
        var breadth = night?.Breadth;
        var label = MarketLabel.For(breadth, picture.Floor);
        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"market-picture\" data-session=\"{picture.Session:yyyy-MM-dd}\" data-breadth=\"{Whole(breadth)}\" data-floor=\"{picture.Floor.ToString("R", Invariant)}\" data-healthy-from=\"{MarketLabel.HealthyFrom.ToString("R", Invariant)}\" data-label=\"{label}\" data-line=\"{picture.Line.Count}\">");
        region.Append(Invariant, $"<p class=\"mp-label\">{(label == MarketLabel.NotRead ? "The market was not read" : "A " + label + " market")}</p>");
        region.Append("<div class=\"mp-body\">");
        region.Append(Gauge(breadth, picture.Floor));
        region.Append("<div class=\"mp-figures\">");
        region.Append(Invariant, $"<p data-part=\"context\"><b>{(night?.BreadthContext is { } context ? Formatted($"{context * 100:0}%") : "none")}</b> above their {SwingReadings.ContextAverageSessions}-day average</p>");
        region.Append(Invariant, $"<p data-part=\"volume\"><b>{(night?.MedianVolumeRatio is { } ratio ? Formatted($"{ratio:0.00}&#215;") : "none")}</b> the index's usual volume, its median against the fifty-day average</p>");
        region.Append("</div></div>");
        region.Append(Invariant, $"<p class=\"mp-rule\">Weak below {picture.Floor * 100:0}%, the market gate's floor, where the list lists nobody; healthy from {MarketLabel.HealthyFrom * 100:0}%; mixed between.</p>");
        region.Append(BreadthLine(picture));
        region.Append("</div>");

        return region.ToString();

        static string Gauge(double? share, double floor)
        {
            const double Cx = 130;
            const double Cy = 132;
            const double Radius = 98;

            (double X, double Y) At(double fraction, double radius) =>
                (Cx + (radius * Math.Cos(Math.PI * (1 - fraction))), Cy - (radius * Math.Sin(Math.PI * (1 - fraction))));

            var gauge = new StringBuilder();
            var (tickX1, tickY1) = At(floor, Radius - 14);
            var (tickX2, tickY2) = At(floor, Radius + 14);
            var (textX, textY) = At(floor, Radius + 26);

            gauge.Append(Invariant, $"<svg class=\"gauge\" viewBox=\"0 0 260 170\" width=\"260\" height=\"170\" role=\"img\" aria-label=\"{(share is { } read ? Formatted($"{read * 100:0.0}%") : "no share")} of the members above their {SwingReadings.BreadthAverageSessions}-day average, the market gate's floor at {floor * 100:0}%\">");
            gauge.Append(Invariant, $"<path class=\"g-track\" d=\"M{Number(Cx - Radius)} {Number(Cy)} A{Number(Radius)} {Number(Radius)} 0 0 1 {Number(Cx + Radius)} {Number(Cy)}\"></path>");

            if (share is { } value)
            {
                var (x, y) = At(Math.Clamp(value, 0.001, 1), Radius);

                gauge.Append(Invariant, $"<path class=\"g-value\" d=\"M{Number(Cx - Radius)} {Number(Cy)} A{Number(Radius)} {Number(Radius)} 0 0 1 {Number(x)} {Number(y)}\"></path>");
                gauge.Append(Invariant, $"<text class=\"g-figure\" x=\"{Number(Cx)}\" y=\"{Number(Cy - 12)}\" text-anchor=\"middle\">{value * 100:0}%</text>");
            }
            else
            {
                gauge.Append(Invariant, $"<text class=\"g-none\" x=\"{Number(Cx)}\" y=\"{Number(Cy - 12)}\" text-anchor=\"middle\">not read</text>");
            }

            gauge.Append(Invariant, $"<line class=\"g-floor\" x1=\"{Number(tickX1)}\" y1=\"{Number(tickY1)}\" x2=\"{Number(tickX2)}\" y2=\"{Number(tickY2)}\"></line>");
            gauge.Append(Invariant, $"<text class=\"g-floor-text\" x=\"{Number(textX)}\" y=\"{Number(Math.Max(textY + 4, 10))}\" text-anchor=\"middle\">{floor * 100:0}%</text>");
            gauge.Append(Invariant, $"<text class=\"g-caption\" x=\"{Number(Cx)}\" y=\"{Number(Cy + 22)}\" text-anchor=\"middle\">above their {SwingReadings.BreadthAverageSessions}-day average</text>");
            gauge.Append("</svg>");

            return gauge.ToString();
        }
    }

    // The breadth line: the share above the 200-day average on each session up to the night that the
    // store holds averages for, the market gate's floor dashed across it, and the newest point marked; a
    // line over fewer sessions than the sixty it draws says how many it holds.
    static string BreadthLine(MarketPicture picture)
    {
        const double Wide = 520;
        const double High = 70;

        var line = new StringBuilder();
        var points = picture.Line;

        line.Append(Invariant, $"<div class=\"breadth-line-wrap\"><div class=\"caption\">The last {points.Count} night(s) the store holds averages for");
        line.Append(points.Count < BreadthLineSessions && points.Count > 0
            ? ", from " + points[0].Session.ToString("yyyy-MM-dd", Invariant) + Formatted($", of the {BreadthLineSessions} the line draws</div>")
            : "</div>");

        if (points.Count < 2)
        {
            line.Append("<p class=\"degraded\" data-breadth-line=\"none\">too few sessions hold a 200-day average to draw a line</p></div>");

            return line.ToString();
        }

        // The scale spans the shares drawn and the floor, a tenth beyond each end, so a line that moves by
        // a few points is not drawn flat against a scale from nought to one.
        var low = Math.Max(0, Math.Min(picture.Floor, points.Min(point => point.Share)) - 0.1);
        var high = Math.Min(1, Math.Max(picture.Floor, points.Max(point => point.Share)) + 0.1);

        double X(int at) => at * Wide / (points.Count - 1);
        double Y(double share) => High - 4 - ((share - low) / (high - low) * (High - 8));

        line.Append(Invariant, $"<svg class=\"breadth-line\" viewBox=\"0 0 {Wide} {High}\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"The share above the {SwingReadings.BreadthAverageSessions}-day average over {points.Count} sessions\">");
        line.Append(Invariant, $"<line class=\"bl-floor\" x1=\"0\" y1=\"{Number(Y(picture.Floor))}\" x2=\"{Wide}\" y2=\"{Number(Y(picture.Floor))}\"></line>");
        line.Append(Invariant, $"<polyline class=\"bl-line\" points=\"{string.Join(' ', points.Select((point, at) => Number(X(at)) + "," + Number(Y(point.Share))))}\"></polyline>");
        line.Append(Invariant, $"<circle class=\"bl-last\" cx=\"{Number(X(points.Count - 1))}\" cy=\"{Number(Y(points[^1].Share))}\" r=\"4\"></circle>");
        line.Append("</svg></div>");

        return line.ToString();
    }

    // How many sessions the breadth line draws at most.
    public const int BreadthLineSessions = 60;

    // The words a gate is named by in the Run page's funnel.
    public static string FunnelWords(string gate) => gate switch
    {
        SwingGates.Market => "The market gate open",
        SwingGates.Trend => "In an uptrend and strong enough",
        SwingGates.Setup => "A pullback into support",
        SwingGates.Trigger => "A fresh buy signal",
        SwingGates.Trade => "A trade worth taking",
        _ => gate,
    };

    // From the index to tonight's list, section 15.10's third region: a bar for the whole index, one for
    // the members through each gate in order, and one for those left after the exclusions, each with its
    // count, the names listed in words, and a link opening tonight's list for the night.
    public string FunnelPicture(FunnelView? funnel, string tonight)
    {
        if (funnel is null)
        {
            return "<p class=\"degraded\" data-funnel-picture=\"none\">no swing filter results are stored for this night, so there is no funnel to draw</p>";
        }

        const double Words = 230;
        const double Wide = 260;
        const double Row = 34;

        var bars = new List<(string Step, string Words, int Count)> { ("members", "The whole index", funnel.Members) };

        bars.AddRange(funnel.Steps.Select(step => (step.Gate, FunnelWords(step.Gate), step.Passed)));
        bars.Add(("excluded", "Not excluded", funnel.Passing));

        var picture = new StringBuilder();

        picture.Append(Invariant, $"<div class=\"funnel-picture\" data-session=\"{funnel.Session:yyyy-MM-dd}\" data-members=\"{funnel.Members}\" data-passing=\"{funnel.Passing}\" data-rule=\"{Escaped(funnel.Rule)}\">");
        picture.Append(Invariant, $"<svg class=\"funnel-bars\" viewBox=\"0 0 {Words + Wide + 60} {bars.Count * Row}\" role=\"img\" aria-label=\"How many members passed each check, from {funnel.Members} to {funnel.Passing}\">");

        for (var at = 0; at < bars.Count; at++)
        {
            var (step, words, count) = bars[at];
            var width = funnel.Members == 0 ? 0 : Math.Max(3, Wide * count / funnel.Members);
            var y = at * Row;

            picture.Append(Invariant, $"<g data-step=\"{Escaped(step)}\" data-count=\"{count}\">");
            picture.Append(Invariant, $"<text class=\"fb-words\" x=\"0\" y=\"{Number(y + (Row / 2) + 5)}\">{Escaped(words)}</text>");
            picture.Append(Invariant, $"<rect class=\"fb-bar\" x=\"{Words}\" y=\"{Number(y + 5)}\" width=\"{Number(width)}\" height=\"{Number(Row - 10)}\" rx=\"3\"></rect>");
            picture.Append(Invariant, $"<text class=\"fb-count\" x=\"{Number(Words + Wide + 56)}\" y=\"{Number(y + (Row / 2) + 5)}\" text-anchor=\"end\">{count}</text></g>");
        }

        picture.Append("</svg>");
        picture.Append(Invariant, $"<p class=\"fp-listed\"><b>{funnel.Passing}</b> ");
        picture.Append(funnel.Rule == ListRules.Filter
            ? Formatted($"stock(s) on this night's list. <a class=\"fp-open\" href=\"{Escaped(tonight)}\">Open the list &#8594;</a></p>")
            : Formatted($"stock(s) passed, and the six reasons drew this evening's list. <a class=\"fp-open\" href=\"{Escaped(tonight)}\">Open the list &#8594;</a></p>"));
        picture.Append("</div>");

        return picture.ToString();
    }

    // How the list's trades are going, section 15.10's fourth region: the live list's trades by where each
    // stands, a ring of the trades decided at the target or the stop against the minimum a share waits on,
    // drawn as a dashed outline until it is met, and from then the share, the break-even and the average
    // result together as Past picks draws them, with a link to Past picks.
    // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
    public string TradesRegion(PicksSummary summary, string picks)
    {
        const double Radius = 56;
        var circumference = 2 * Math.PI * Radius;
        var decided = Math.Min(summary.Decided, summary.MinimumDecided);
        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"trades-picture\" data-listed=\"{summary.Listed}\" data-open=\"{summary.Open}\" data-target=\"{summary.Target}\" data-stopped=\"{summary.Stopped}\" data-time=\"{summary.Time}\" data-decided=\"{summary.Decided}\" data-needed=\"{summary.MinimumDecided}\" data-met=\"{(summary.TargetShare is null ? "false" : "true")}\">");
        region.Append("<div class=\"tp-body\">");
        region.Append(Invariant, $"<svg class=\"trades-ring\" viewBox=\"0 0 150 150\" width=\"150\" height=\"150\" role=\"img\" aria-label=\"{summary.Decided} of the {summary.MinimumDecided} trades decided that a share waits on\">");
        region.Append(Invariant, $"<circle class=\"{(summary.TargetShare is null ? "tr-track tr-unmet" : "tr-track")}\" cx=\"75\" cy=\"75\" r=\"{Number(Radius)}\"></circle>");
        region.Append(Invariant, $"<circle class=\"tr-done\" cx=\"75\" cy=\"75\" r=\"{Number(Radius)}\" stroke-dasharray=\"{Number(circumference * decided / Math.Max(1, summary.MinimumDecided))} {Number(circumference)}\" transform=\"rotate(-90 75 75)\"></circle>");
        region.Append(Invariant, $"<text class=\"tr-figure\" x=\"75\" y=\"74\" text-anchor=\"middle\">{summary.Decided}</text>");
        region.Append(Invariant, $"<text class=\"tr-caption\" x=\"75\" y=\"94\" text-anchor=\"middle\">of {summary.MinimumDecided} decided</text></svg>");
        region.Append("<div class=\"tp-tiles\">");

        foreach (var (status, count, words) in new[]
        {
            (PickStatus.Open, summary.Open, "still open"),
            (PickStatus.Target, summary.Target, "reached target"),
            (PickStatus.Stopped, summary.Stopped, "stopped out"),
            (PickStatus.Time, summary.Time, "ran out of time"),
        })
        {
            region.Append(Invariant, $"<div class=\"tile\" data-status=\"{status}\"><b>{count}</b><span>{words}</span></div>");
        }

        region.Append("</div></div>");
        region.Append(summary.TargetShare is { } share
            ? Formatted($"<p class=\"tp-rate\" data-share=\"{share.ToString("R", Invariant)}\"><b>{share:0.0}%</b> reached the target against <b>{summary.BreakEven ?? 0:0.0}%</b> needed to break even, and the average trade came to <b>{summary.AverageResult ?? 0:0.00}</b> of its risk.</p>")
            : Formatted($"<p class=\"tp-rate\" data-share=\"none\">The share that reached the target is drawn once {summary.MinimumDecided} trades are decided over {summary.MinimumNights} listing nights: {summary.Decided} and {summary.DecidedNights} so far.</p>"));
        region.Append(Invariant, $"<p class=\"tp-open\"><a href=\"{Escaped(picks)}\">Every trade on Past picks &#8594;</a></p></div>");

        return region.ToString();
    }

    // Is the list finding new stocks, section 15.10's fifth region: a bar for each evening up to the night, the
    // part new that evening and the part also on the list the evening before, and the night's split in words.
    public string FreshBars(IReadOnlyList<FreshNight> evenings, DateOnly night)
    {
        if (evenings.Count == 0)
        {
            return "<p class=\"degraded\" data-fresh=\"none\">no evening of the list is stored up to this night</p>";
        }

        const double Wide = 520;
        const double High = 130;

        var most = Math.Max(1, evenings.Max(evening => evening.Listed));
        var step = Wide / Math.Max(evenings.Count, 10);
        var bars = new StringBuilder();

        bars.Append(Invariant, $"<div class=\"fresh-picture\" data-evenings=\"{evenings.Count}\">");
        bars.Append(Invariant, $"<svg class=\"fresh-bars\" viewBox=\"0 0 {Wide} {High + 4}\" role=\"img\" aria-label=\"New and repeated names on each of the last {evenings.Count} evenings\">");
        bars.Append(Invariant, $"<line class=\"fr-base\" x1=\"0\" y1=\"{High}\" x2=\"{Wide}\" y2=\"{High}\"></line>");

        for (var at = 0; at < evenings.Count; at++)
        {
            var evening = evenings[at];
            var repeated = High * evening.Repeated / most;
            var fresh = High * evening.New / most;
            var x = (at * step) + 2;

            bars.Append(Invariant, $"<g data-session=\"{evening.Session:yyyy-MM-dd}\" data-listed=\"{evening.Listed}\" data-new=\"{evening.New}\" data-repeated=\"{evening.Repeated}\">");
            bars.Append(Invariant, $"<rect class=\"fr-repeated\" x=\"{Number(x)}\" y=\"{Number(High - repeated)}\" width=\"{Number(step - 4)}\" height=\"{Number(repeated)}\"></rect>");
            bars.Append(Invariant, $"<rect class=\"fr-new\" x=\"{Number(x)}\" y=\"{Number(High - repeated - fresh)}\" width=\"{Number(step - 4)}\" height=\"{Number(fresh)}\"></rect></g>");
        }

        bars.Append("</svg>");
        bars.Append("<p class=\"fr-key\"><span class=\"sw sw-new\"></span>new that evening <span class=\"sw sw-repeated\"></span>also on the list the evening before</p>");

        var tonight = evenings.LastOrDefault(evening => evening.Session == night);
        var listed = evenings.Sum(evening => evening.Listed);

        bars.Append(tonight is { } drawn
            ? Formatted($"<p class=\"fr-said\">On this night: {drawn.New} new and {drawn.Repeated} also on the list the evening before. Over these {evenings.Count} evening(s), {(listed == 0 ? 0 : 100.0 * evenings.Sum(evening => evening.New) / listed):0}% of the names listed were new.</p>")
            : "<p class=\"fr-said\">This night listed nobody.</p>");
        bars.Append("</div>");

        return bars.ToString();
    }

    // Research and spend, section 15.10's ninth region: the month's spend against the month cap, the passes
    // the paid model wrote on each of the seven nights up to the night, and the reports and the overnight
    // drafts written over them.
    // see: The spend cap counts a UTC day and a UTC month, and refuses a call that could take spend past either
    public string ResearchRegion(ResearchPicture picture)
    {
        const double Wide = 520;
        const double High = 70;

        var spent = picture.Spend;
        var share = spent.MonthCap <= 0 ? 0 : EquityBrief.Core.Prices.Statistic.FromRatio(Math.Min(1m, spent.MonthToDate / spent.MonthCap));
        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"research-picture\" data-month=\"{spent.MonthToDate}\" data-month-cap=\"{spent.MonthCap}\" data-nights=\"{picture.Nights.Count}\">");
        region.Append(Invariant, $"<p class=\"rp-spend\"><b>{SpendVerdict.Money(spent.MonthToDate)}</b> spent this month, of the {SpendVerdict.Money(spent.MonthCap)} month cap</p>");
        region.Append(Invariant, $"<svg class=\"spend-bar\" viewBox=\"0 0 {Wide} 14\" role=\"img\" aria-label=\"{SpendVerdict.Money(spent.MonthToDate)} of the {SpendVerdict.Money(spent.MonthCap)} month cap\">");
        region.Append(Invariant, $"<rect class=\"sp-track\" x=\"0\" y=\"1\" width=\"{Wide}\" height=\"12\" rx=\"6\"></rect>");
        region.Append(Invariant, $"<rect class=\"sp-spent\" x=\"0\" y=\"1\" width=\"{Number(Math.Max(share * Wide, spent.MonthToDate > 0 ? 6 : 0))}\" height=\"12\" rx=\"6\"></rect></svg>");

        var most = Math.Max(1, picture.Nights.Count == 0 ? 1 : picture.Nights.Max(night => night.PaidPasses));
        var step = Wide / 7;

        region.Append(Invariant, $"<div class=\"caption\">Reports the paid model wrote, on each of the last {picture.Nights.Count} night(s)</div>");
        region.Append(Invariant, $"<svg class=\"pass-bars\" viewBox=\"0 0 {Wide} {High + 18}\" role=\"img\" aria-label=\"Reports the paid model wrote on each night\">");

        for (var at = 0; at < picture.Nights.Count; at++)
        {
            var night = picture.Nights[at];
            var height = High * night.PaidPasses / most;

            region.Append(Invariant, $"<g data-session=\"{night.Session:yyyy-MM-dd}\" data-paid=\"{night.PaidPasses}\" data-drafts=\"{night.Drafts}\">");
            region.Append(Invariant, $"<rect class=\"pb-bar\" x=\"{Number((at * step) + 8)}\" y=\"{Number(High - height)}\" width=\"{Number(step - 16)}\" height=\"{Number(Math.Max(height, 1))}\" rx=\"3\"></rect>");
            region.Append(Invariant, $"<text class=\"pb-day\" x=\"{Number((at * step) + (step / 2))}\" y=\"{High + 14}\" text-anchor=\"middle\">{night.Session:ddd}</text></g>");
        }

        region.Append("</svg><div class=\"rp-tiles\">");
        region.Append(Invariant, $"<div class=\"tile\" data-figure=\"paid reports\"><b>{picture.Nights.Sum(night => night.PaidPasses)}</b><span>reports written by the paid model</span></div>");
        region.Append(Invariant, $"<div class=\"tile\" data-figure=\"overnight drafts\"><b>{picture.Nights.Sum(night => night.Drafts)}</b><span>drafts written overnight on this machine</span></div>");
        region.Append("</div></div>");

        return region.ToString();
    }

    // How each report did, in section 15.10's detail: one numbered row for each report of the seven nights with its
    // stock, its day and what its calls cost, and a cell for each section saying how it came out with what its own
    // calls cost, the two cases' cell marked where a draft carried a figure on both sides; each section left out
    // with why beneath, numbered as its cell is; the count of the newest reports' two cases carrying a figure on
    // both sides; and each section's rates over the newest twenty reports that warranted it.
    // see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports
    public string ReportsRegion(ReportsView view, string nameRoute)
    {
        // A store holding no report says so rather than drawing a table of rates read over nothing.
        if (view.Held == 0)
        {
            return "<div class=\"reports\" data-reports=\"0\" data-held=\"0\"><p class=\"degraded\">No report the paid model wrote is held yet, so there is nothing to read section by section.</p></div>";
        }

        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"reports\" data-reports=\"{view.Reports.Count}\" data-held=\"{view.Held}\">");
        region.Append(Invariant, $"<p class=\"list-count\" data-shown=\"{view.Reports.Count}\" data-held=\"{view.Held}\">Showing {view.Reports.Count} of {view.Held} report{(view.Held == 1 ? string.Empty : "s")}, those written over the last seven nights</p>");

        var why = new List<(int Place, string Line)>();

        if (view.Reports.Count > 0)
        {
            region.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table report-table\" data-rows=\"{view.Reports.Count}\"><thead><tr><th class=\"place\">#</th><th>Stock</th><th>Day</th><th class=\"r\">Cost</th>");

            foreach (var section in view.Reports[0].Cells.Select(cell => cell.Section))
            {
                region.Append(Invariant, $"<th class=\"c\" data-section=\"{Escaped(section)}\" title=\"{Escaped(section)}\">{Escaped(SectionHead(section))}</th>");
            }

            region.Append("</tr></thead><tbody>");

            foreach (var (report, place) in view.Reports.Select((report, at) => (report, at + 1)))
            {
                region.Append(Invariant, $"<tr data-run=\"{Escaped(report.RunId)}\" data-ticker=\"{Escaped(report.Ticker)}\"><td class=\"place\">{place}</td>");
                region.Append(Invariant, $"<td><a href=\"{Escaped(nameRoute + report.Ticker)}\">{Escaped(report.Ticker)}</a></td><td>{report.Day:yyyy-MM-dd}</td><td class=\"r\" data-cost=\"{report.Cost}\">{ReportCost(report.Cost)}</td>");

                foreach (var cell in report.Cells)
                {
                    var word = cell.Outcome switch
                    {
                        ReportsView.FirstTime => "first",
                        ReportsView.OnRetry => "retry",
                        ReportsView.LeftOut => "out",
                        _ => "earlier",
                    };

                    var note = string.Empty;

                    if (cell.Outcome == ReportsView.LeftOut)
                    {
                        why.Add((place, Formatted($"{report.Ticker}, {SectionHead(cell.Section)}: {cell.Why ?? "not written"}")));
                        note = Formatted($" <sup>{place}</sup>");
                    }

                    region.Append(Invariant, $"<td class=\"c rc rc-{word}\" data-section=\"{Escaped(cell.Section)}\" data-outcome=\"{Escaped(cell.Outcome)}\" data-cost=\"{cell.Cost}\" data-drafts=\"{cell.Drafts}\" data-both-sides=\"{(cell.BothSides ? "true" : "false")}\">{word}{note}");
                    region.Append(cell.Outcome == ReportsView.NotWarranted ? string.Empty : "<small>" + ReportCost(cell.Cost) + "</small>");
                    region.Append(cell.BothSides ? "<small class=\"both\">both sides</small>" : string.Empty);
                    region.Append("</td>");
                }

                region.Append("</tr>");
            }

            region.Append("</tbody></table></div>");
        }

        region.Append("<p class=\"report-key\">first: passed the claim check at its first draft; retry: passed on its one retry; out: left out, with why below; earlier: stood from an earlier day, so the pass did not write it. Each cost is that section's own calls, a trial's left out.</p>");

        if (why.Count > 0)
        {
            region.Append("<ol class=\"report-why\">");

            foreach (var (place, line) in why)
            {
                region.Append(Invariant, $"<li data-place=\"{place}\">{place}. {Escaped(line)}</li>");
            }

            region.Append("</ol>");
        }

        region.Append(Invariant, $"<p class=\"both-sides\" data-both-sides=\"{view.BothSides}\" data-drafted=\"{view.TwoCases}\">{view.BothSides} of the newest {view.TwoCases} reports' two cases carried a figure on both sides, first draft or retry.</p>");

        region.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table rate-table\" data-window=\"{ReportsView.RateWindow}\"><thead><tr><th class=\"place\">#</th><th>Section</th><th class=\"r\">Passed first time</th><th class=\"r\">Left out</th></tr></thead><tbody>");

        foreach (var (rate, place) in view.Rates.Select((rate, at) => (rate, at + 1)))
        {
            region.Append(Invariant, $"<tr data-section=\"{Escaped(rate.Section)}\" data-reports=\"{rate.Reports}\" data-first=\"{rate.FirstTime}\" data-left-out=\"{rate.LeftOut}\"><td class=\"place\">{place}</td><td>{Escaped(rate.Section)}</td>");
            region.Append(Invariant, $"<td class=\"r\">{rate.FirstTime} of {rate.Reports}</td><td class=\"r\">{rate.LeftOut} of {rate.Reports}</td></tr>");
        }

        region.Append(Invariant, $"</tbody></table></div><p class=\"report-key\">Each section's rates are read over the newest {ReportsView.RateWindow} reports that warranted it, and over as many as there are where fewer have been written.</p></div>");

        return region.ToString();
    }

    // Each section a trial asked for beside a report, in section 15.10's detail: one numbered row a section with the
    // pass's model and the trial's, each with how it came out, its rounds and what they cost, and the two models'
    // drafts side by side folded beneath it, in two columns on a wide screen and one on a narrow one. Nothing is
    // drawn while no trial has written a row.
    // see: A trial asks a second profile for named sections after a report and records its drafts beside the report, never in it
    public string TrialsRegion(IReadOnlyList<TrialRow> trials, string nameRoute)
    {
        if (trials.Count == 0)
        {
            return string.Empty;
        }

        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"trials\" data-trials=\"{trials.Count}\"><p class=\"list-count\" data-shown=\"{trials.Count}\">Showing all {trials.Count} section{(trials.Count == 1 ? string.Empty : "s")} a trial or a review asked for</p><ol class=\"trial-list\">");

        foreach (var (trial, place) in trials.Select((trial, at) => (trial, at + 1)))
        {
            TrialSide[] sides = [trial.Pass with { Side = TrialSide.OfPass }, .. trial.Asked];

            region.Append(Invariant, $"<li class=\"trial\" data-run=\"{Escaped(trial.RunId)}\" data-section=\"{Escaped(trial.Section)}\" data-sides=\"{sides.Length}\"><p class=\"trial-head\">{place}. <a href=\"{Escaped(nameRoute + trial.Ticker)}\">{Escaped(trial.Ticker)}</a>, {trial.Day:yyyy-MM-dd}, {Escaped(trial.Section)}</p>");
            region.Append("<div class=\"trial-sides\">");
            region.Append(string.Concat(sides.Select(TrialSideLine)));
            region.Append(Invariant, $"</div><details class=\"trial-drafts\"><summary>The {sides.Length} drafts side by side</summary><div class=\"trial-pair\">");
            region.Append(string.Concat(sides.Select(TrialDrafts)));
            region.Append("</div></details></li>");
        }

        region.Append("</ol></div>");

        return region.ToString();

        static string Named(TrialSide drawn) => drawn.Side == TrialSide.OfReview ? drawn.Model + ", reviewing its draft" : drawn.Model;

        static string TrialSideLine(TrialSide drawn) =>
            Formatted($"<p class=\"trial-side\" data-side=\"{drawn.Side}\" data-model=\"{Escaped(drawn.Model)}\" data-outcome=\"{Escaped(drawn.Outcome)}\" data-rounds=\"{drawn.Rounds}\" data-cost=\"{drawn.Cost}\"><b>{Escaped(Named(drawn))}</b>: {Escaped(drawn.Outcome)}, {drawn.Rounds} round{(drawn.Rounds == 1 ? string.Empty : "s")}, {ReportCost(drawn.Cost)}</p>");

        static string TrialDrafts(TrialSide drawn)
        {
            var column = new StringBuilder();

            column.Append(Invariant, $"<div class=\"trial-column\" data-side=\"{drawn.Side}\"><h4>{Escaped(Named(drawn))}</h4>");

            foreach (var (draft, round) in drawn.Drafts.Select((draft, at) => (draft, at + 1)))
            {
                column.Append(Invariant, $"<div class=\"trial-draft\" data-round=\"{round}\"><p class=\"trial-round\">Round {round}</p>");
                column.Append(draft.Length == 0 ? "<p class=\"degraded\">no draft</p>" : string.Concat(draft.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(paragraph => "<p>" + Escaped(paragraph) + "</p>")));
                column.Append("</div>");
            }

            column.Append("</div>");

            return column.ToString();
        }
    }

    // A report's cost to the ten-thousandth of a dollar, since a section's call is often less than a cent.
    static string ReportCost(decimal cost) => "$" + cost.ToString("0.0000", CultureInfo.InvariantCulture);

    // A section's heading in the report table, short enough for a column, its whole name in the heading's title.
    static string SectionHead(string section) => section switch
    {
        "The cause of each large move" => "Moves",
        "What the company sells" => "Business",
        "The segment commentary" => "Segments",
        "The key under each figure" => "Key",
        "The industry cycle" => "Cycle",
        "The dated calendar items" => "Calendar",
        "The two cases" => "Two cases",
        "The risks, each with what would confirm it" => "Risks",
        "The short version" => "Short version",
        _ => section,
    };

    // Anything to worry about, section 15.10's tenth region: each item of the checklist in plain words, held,
    // turned red with its reason where it failed, or a dashed outline where the night stored nothing it could
    // be read from, and the four counts of the last phase report beneath.
    // see: Status is drawn in blues with violet for a wait and red for a failure alone
    public string WorryRegion(IReadOnlyList<WorryItem> items, HarnessCounts? harness)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"worry-picture\" data-failed=\"{items.Count(item => item.State == WorryItem.Failed)}\"><ul class=\"worries\">");

        foreach (var item in items)
        {
            var glyph = item.State switch
            {
                WorryItem.Held => "<path d=\"M7 12.5 L10.5 16 L17 8.5\"></path>",
                WorryItem.Failed => "<path d=\"M8 8 L16 16 M16 8 L8 16\"></path>",
                _ => "<path d=\"M8 12 H16\"></path>",
            };

            region.Append(Invariant, $"<li data-item=\"{Escaped(item.Item)}\" data-state=\"{Escaped(item.State)}\"><svg class=\"worry-mark\" viewBox=\"0 0 24 24\" width=\"24\" height=\"24\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"12\" r=\"11\"></circle>{glyph}</svg>");
            region.Append(Invariant, $"<span>{Escaped(item.Item)}{(item.Why is { } why ? ": <b class=\"worry-why\">" + Escaped(why) + "</b>" : string.Empty)}</span></li>");
        }

        region.Append("</ul>");
        region.Append(harness is { } counts
            ? Formatted($"<div class=\"caption\">The checks on the code, from the last phase report</div><div class=\"wr-tiles\"><div class=\"tile\" data-count=\"passed\"><b>{counts.Passed}</b><span>passed</span></div><div class=\"tile\" data-count=\"failed\"><b>{counts.Failed}</b><span>failed</span></div><div class=\"tile\" data-count=\"unexamined\"><b>{counts.Unexamined}</b><span>not examined</span></div><div class=\"tile\" data-count=\"out of scope\"><b>{counts.OutOfScope}</b><span>out of scope</span></div></div>")
            : "<p class=\"degraded\" data-report=\"none\">no phase report has been written on this machine, so the checks on the code are not counted here</p>");
        region.Append("</div>");

        return region.ToString();
    }

    // How the system learns, section 15.10's sixth region: the shape clock as progress toward the ordinary
    // nights it waits on with each check's median count against its band, dashed until measured; the edge
    // clock as a line from today to its two checkpoints with the blocks gathered; and every version running
    // beside the live list, what it changes, its picks, the share the live list also picked and its evidence,
    // with the checkpoint that unlocks its results and a link comparing tonight's picks.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    public string LearningRegion(ShapeState? shape, EdgeView edge, IReadOnlyList<VersionLine> versions, string compare)
    {
        var region = new StringBuilder();

        region.Append("<div class=\"learning-picture\"><div class=\"lp-clocks\">");

        // The shape clock.
        region.Append("<div class=\"lp-shape\"><p class=\"lp-head\">Tuning how many stocks pass</p>");

        if (shape is { } clock)
        {
            var done = clock.Wanted == 0 ? 0 : Math.Min(1.0, 1.0 * clock.Ordinary / clock.Wanted);

            region.Append(Invariant, $"<svg class=\"progress-bar\" viewBox=\"0 0 380 16\" role=\"img\" data-ordinary=\"{clock.Ordinary}\" data-wanted=\"{clock.Wanted}\" aria-label=\"{clock.Ordinary} of {clock.Wanted} ordinary nights\">");
            region.Append("<rect class=\"pg-track\" x=\"0\" y=\"2\" width=\"380\" height=\"12\" rx=\"6\"></rect>");
            region.Append(Invariant, $"<rect class=\"pg-done\" x=\"0\" y=\"2\" width=\"{Number(Math.Max(done * 380, clock.Ordinary > 0 ? 6 : 0))}\" height=\"12\" rx=\"6\"></rect></svg>");
            region.Append(Invariant, $"<p class=\"lp-said\">{clock.Ordinary} of {clock.Wanted} ordinary nights under {(clock.VersionOpen ? "filter version " + Escaped(clock.Version) : "no open filter version")}. Each check's typical pass count against the range it should fall in:</p>");

            foreach (var band in clock.Gates.Append(clock.List))
            {
                var top = Math.Max(band.High, band.Median ?? 0) * 1.15;
                double X(double at) => 300 * at / Math.Max(top, 1);

                region.Append(Invariant, $"<div class=\"band-row\" data-measure=\"{Escaped(band.Measure)}\" data-median=\"{Whole(band.Median)}\" data-low=\"{band.Low}\" data-high=\"{band.High}\"><span>{Escaped(char.ToUpperInvariant(band.Measure[0]) + band.Measure[1..])}</span>");
                region.Append("<svg class=\"band-dot\" viewBox=\"0 0 300 22\" role=\"img\" aria-label=\"" + Escaped(band.Measure) + "\">");
                region.Append("<line class=\"bd-axis\" x1=\"0\" y1=\"11\" x2=\"300\" y2=\"11\"></line>");
                region.Append(Invariant, $"<rect class=\"{(clock.Crossed && band.Median is not null ? "bd-band" : "bd-band bd-unmeasured")}\" x=\"{Number(X(band.Low))}\" y=\"4\" width=\"{Number(X(band.High) - X(band.Low))}\" height=\"14\" rx=\"7\"></rect>");
                region.Append(band.Median is { } median && clock.Crossed
                    ? Formatted($"<circle class=\"bd-dot\" cx=\"{Number(X(median))}\" cy=\"11\" r=\"6\"></circle>")
                    : string.Empty);
                region.Append("</svg></div>");
            }

            region.Append("<p class=\"lp-note\">The range is dashed and holds no dot until the sixty ordinary nights are stored; then the page proposes settings for you to accept or reject.</p>");
        }
        else
        {
            region.Append("<p class=\"degraded\" data-shape=\"none\">the shape clock is not read on this page</p>");
        }

        region.Append("</div>");

        // The edge clock.
        var live = edge.Candidates.FirstOrDefault(candidate => candidate.Live) ?? edge.Candidates.FirstOrDefault();
        var run = live?.SessionsRun ?? 0;
        var end = Math.Max(edge.PromotionSessions, 1);
        double At(double sessions) => 10 + (500 * Math.Min(sessions, end) / end);

        region.Append("<div class=\"lp-edge\"><p class=\"lp-head\">Judging whether the picks make money</p>");
        region.Append(Invariant, $"<svg class=\"edge-line\" viewBox=\"0 0 520 96\" role=\"img\" data-sessions=\"{run}\" data-first-look=\"{edge.FirstLookSessions}\" data-promotion=\"{edge.PromotionSessions}\" aria-label=\"{run} sessions run of the {edge.FirstLookSessions} the first checkpoint waits on\">");
        region.Append("<line class=\"el-track\" x1=\"10\" y1=\"46\" x2=\"510\" y2=\"46\"></line>");
        region.Append(Invariant, $"<line class=\"el-done\" x1=\"10\" y1=\"46\" x2=\"{Number(At(run))}\" y2=\"46\"></line>");
        region.Append(Invariant, $"<circle class=\"el-today\" cx=\"{Number(At(run))}\" cy=\"46\" r=\"8\"></circle><text class=\"el-text\" x=\"{Number(Math.Max(At(run), 24))}\" y=\"22\" text-anchor=\"middle\">today</text>");
        region.Append(Invariant, $"<circle class=\"el-check\" cx=\"{Number(At(edge.FirstLookSessions))}\" cy=\"46\" r=\"8\"></circle><text class=\"el-text\" x=\"{Number(At(edge.FirstLookSessions))}\" y=\"76\" text-anchor=\"middle\">1</text>");
        region.Append(Invariant, $"<circle class=\"el-check\" cx=\"510\" cy=\"46\" r=\"8\"></circle><text class=\"el-text\" x=\"510\" y=\"76\" text-anchor=\"middle\">2</text>");
        region.Append("</svg>");
        region.Append(Invariant, $"<p class=\"lp-said\" data-first-look=\"{edge.FirstLookSessions}\" data-promotion=\"{edge.PromotionSessions}\">Checkpoint 1, {edge.FirstLookSessions} sessions in, about two years: it can drop a version and never promote one. Checkpoint 2, {edge.PromotionSessions} sessions in, about three years: it can promote one.</p>");
        region.Append(Invariant, $"<p class=\"lp-said\">Block {live?.Record.Blocks ?? 0} of {live?.Record.Floor ?? 8} gathered by the live list. Trades that overlap in time share one market, so evidence is counted in blocks of {EquityBrief.Core.Returns.Blocks.Sessions} sessions and not in trades.</p>");
        region.Append("</div></div>");

        // The versions.
        region.Append("<div class=\"lp-versions\"><p class=\"lp-head\">Versions running in the background</p>");
        region.Append("<p class=\"lp-note\">Picks only: how any version's trades turn out stays hidden until a checkpoint, so none wins on a lucky month.</p>");
        region.Append("<div class=\"tbl-wrap\"><table class=\"versions-table\"><tr><th>Version</th><th>What it changes</th><th class=\"num\">Picks so far</th><th>Also on the live list</th><th>Evidence</th><th>Results</th></tr>");

        foreach (var version in versions)
        {
            region.Append(Invariant, $"<tr data-version=\"{Escaped(version.Slug)}\" data-picks=\"{version.Picks}\" data-shared=\"{Whole(version.Shared)}\" data-blocks=\"{version.Blocks}\" data-live=\"{(version.Live ? "true" : "false")}\">");
            region.Append(Invariant, $"<td><b>{Escaped(version.Candidate)}</b></td><td>{Escaped(version.Changes)}</td><td class=\"num\">{version.Picks}</td>");
            region.Append(version.Live
                ? "<td>is the live list</td>"
                : version.Shared is { } shared
                    ? Formatted($"<td><svg class=\"share-bar\" viewBox=\"0 0 90 12\" width=\"90\" height=\"12\" aria-hidden=\"true\"><rect class=\"sh-track\" x=\"0\" y=\"1\" width=\"90\" height=\"10\" rx=\"5\"></rect><rect class=\"sh-done\" x=\"0\" y=\"1\" width=\"{Number(90 * shared)}\" height=\"10\" rx=\"5\"></rect></svg> {shared * 100:0}% of its picks</td>")
                    : "<td><span class=\"degraded\">no pick yet</span></td>");
            region.Append(Invariant, $"<td>block {version.Blocks} of {version.Floor}</td><td><span class=\"tag-wait\">at checkpoint 1</span></td></tr>");
        }

        region.Append("</table></div>");
        region.Append(Invariant, $"<p class=\"lp-open\"><a href=\"{Escaped(compare)}\">Compare tonight's picks &#8594;</a></p></div></div>");

        return region.ToString();
    }

    // Compare tonight's picks, section 15.10's seventh region: a choice of the versions running beside the
    // live list, kept in the link, and for the one chosen the names only the live list picked, the names both
    // picked and the names only it picked, each only-name with the setting that made the difference, drawn
    // with the three counts overlapping, and its picks over the last twenty evenings. Picks only.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    public string CompareRegion(CompareView view, string route, string names)
    {
        var region = new StringBuilder();

        if (view.Chosen is not { } chosen)
        {
            return "<p class=\"degraded\" data-compare=\"none\">no version runs beside the live list, so there is nothing to compare</p>";
        }

        region.Append(Invariant, $"<div class=\"compare-picture\" data-version=\"{Escaped(chosen.Slug)}\" data-evaluated=\"{(view.Evaluated ? "true" : "false")}\" data-only-live=\"{view.OnlyLive.Count}\" data-both=\"{view.Both.Count}\" data-only-version=\"{view.OnlyVersion.Count}\">");
        region.Append("<div class=\"cp-choose\"><label for=\"compare-version\">Compare the live list with</label>");
        region.Append(Invariant, $"<select id=\"compare-version\" data-compare=\"{Escaped(route)}\">");

        foreach (var version in view.Versions.Where(version => !version.Live))
        {
            region.Append(Invariant, $"<option value=\"{Escaped(version.Slug)}\"{(version.Slug == chosen.Slug ? " selected" : string.Empty)}>{Escaped(version.Candidate)}</option>");
        }

        region.Append("</select></div>");
        region.Append(Invariant, $"<p class=\"cp-changes\"><b>{Escaped(chosen.Candidate)}:</b> {Escaped(chosen.Changes)}.</p>");

        if (!view.Evaluated)
        {
            region.Append("<p class=\"degraded\" data-evaluated=\"false\">This version was not evaluated on this night, so it picked nothing here.</p>");
        }

        region.Append("<div class=\"cp-body\">");
        region.Append(Invariant, $"<svg class=\"overlap-rings\" viewBox=\"0 0 300 200\" width=\"300\" height=\"200\" role=\"img\" aria-label=\"{view.OnlyLive.Count} only on the live list, {view.Both.Count} on both, {view.OnlyVersion.Count} only this version\">");
        region.Append("<circle class=\"or-live\" cx=\"110\" cy=\"100\" r=\"80\"></circle><circle class=\"or-version\" cx=\"190\" cy=\"100\" r=\"80\"></circle>");
        region.Append(Invariant, $"<text class=\"or-count\" x=\"70\" y=\"108\" text-anchor=\"middle\">{view.OnlyLive.Count}</text><text class=\"or-count\" x=\"150\" y=\"108\" text-anchor=\"middle\">{view.Both.Count}</text><text class=\"or-count\" x=\"230\" y=\"108\" text-anchor=\"middle\">{view.OnlyVersion.Count}</text>");
        region.Append("<text class=\"or-name\" x=\"70\" y=\"194\" text-anchor=\"middle\">live list</text><text class=\"or-name\" x=\"230\" y=\"194\" text-anchor=\"middle\">this version</text></svg>");
        region.Append("<div class=\"cp-groups\">");
        region.Append(Group("only-live", "Only the live list", view.OnlyLive));
        region.Append(Group("both", "Both", [.. view.Both.Select(ticker => new ComparedName(ticker, null))]));
        region.Append(Group("only-version", "Only this version", view.OnlyVersion));
        region.Append("</div></div>");
        region.Append("<div class=\"cp-tiles\">");
        region.Append(Invariant, $"<div class=\"tile\" data-figure=\"picked\"><b>{view.Picked}</b><span>stocks this version picked over the last {view.Evenings} evening(s)</span></div>");
        region.Append(Invariant, $"<div class=\"tile\" data-figure=\"shared\"><b>{(view.SharedShare is { } share ? Formatted($"{share * 100:0}%") : "none")}</b><span>of them also on the live list</span></div>");
        region.Append(Invariant, $"<div class=\"tile\" data-figure=\"ahead\"><b>{view.EveningsAhead}</b><span>evenings it picked a stock the live list did not</span></div>");
        region.Append("</div><p class=\"lp-note\">These are picks, not results. A version's picks never appear on tonight's list, a stock's page or Past picks, and how its trades turn out stays hidden until its checkpoint.</p></div>");

        return region.ToString();

        string Group(string kind, string heading, IReadOnlyList<ComparedName> tickers)
        {
            var group = new StringBuilder();

            group.Append(Invariant, $"<div class=\"cp-group\" data-group=\"{kind}\"><p class=\"cp-heading\">{heading}</p>");
            group.Append(tickers.Count == 0 ? "<p class=\"cp-none\">none on this night</p>" : "<ul>");

            foreach (var name in tickers)
            {
                group.Append(Invariant, $"<li data-ticker=\"{Escaped(name.Ticker)}\"><a href=\"{Escaped(names + name.Ticker)}\">{Escaped(name.Ticker)}</a>{(name.Why is { } why ? " <span class=\"cp-why\">" + Escaped(why) + "</span>" : string.Empty)}</li>");
            }

            group.Append(tickers.Count == 0 ? "</div>" : "</ul></div>");

            return group.ToString();
        }
    }

    // At a checkpoint, section 15.10's eighth region: one row per version on a scale from nought to a
    // hundred. Before its first look a row is a dashed, locked outline with its trades and blocks so far; from
    // its first look the share of its trades that reached the target, the break-even they needed, what no
    // skill scored from the same starts and how far luck alone could move it, with the verdict in words.
    // see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow
    public string CheckpointRegion(IReadOnlyList<CheckpointRow> rows)
    {
        const double Wide = 560;
        double X(double percent) => Wide * Math.Clamp(percent, 0, 100) / 100;

        var region = new StringBuilder();

        region.Append(Invariant, $"<div class=\"checkpoint-picture\" data-rows=\"{rows.Count}\" data-unlocked=\"{rows.Count(row => row.Unlocked)}\">");
        region.Append("<p class=\"ck-key\"><span class=\"ck-k ck-k-share\"></span>the share of its trades that reached the target <span class=\"ck-k ck-k-even\"></span>the share needed to break even <span class=\"ck-k ck-k-null\"></span>what no skill scored from the same starts <span class=\"ck-k ck-k-luck\"></span>how far luck alone could move it</p>");

        foreach (var row in rows)
        {
            region.Append(Invariant, $"<div class=\"ck-row\" data-candidate=\"{Escaped(row.Candidate)}\" data-unlocked=\"{(row.Unlocked ? "true" : "false")}\" data-setups=\"{row.Setups}\" data-blocks=\"{row.Blocks}\">");
            region.Append(Invariant, $"<span class=\"ck-name\">{Escaped(row.Candidate)}</span>");

            if (row.Unlocked)
            {
                var share = row.Share!.Value;
                var zero = row.NullShare!.Value;
                var luck = row.SmallestExcess ?? 0;

                region.Append(Invariant, $"<svg class=\"ck-scale\" viewBox=\"0 0 {Wide} 34\" role=\"img\" aria-label=\"{share:0.0}% reached the target\">");
                region.Append(Invariant, $"<line class=\"ck-axis\" x1=\"0\" y1=\"17\" x2=\"{Wide}\" y2=\"17\"></line>");
                region.Append(Invariant, $"<rect class=\"ck-luck\" x=\"{Number(X(zero))}\" y=\"11\" width=\"{Number(X(zero + luck) - X(zero))}\" height=\"12\" rx=\"6\"></rect>");
                region.Append(Invariant, $"<circle class=\"ck-null\" cx=\"{Number(X(zero))}\" cy=\"17\" r=\"6\"></circle>");
                region.Append(row.BreakEven is { } even ? Formatted($"<line class=\"ck-even\" x1=\"{Number(X(even))}\" y1=\"3\" x2=\"{Number(X(even))}\" y2=\"31\"></line>") : string.Empty);
                region.Append(Invariant, $"<circle class=\"ck-share\" cx=\"{Number(X(share))}\" cy=\"17\" r=\"8\"></circle></svg>");
                region.Append(Invariant, $"<span class=\"ck-words\">{share:0.0}% reached the target against {(row.BreakEven is { } needed ? Formatted($"{needed:0.0}%") : "no stored")} needed</span><span class=\"ck-verdict\">{Escaped(row.Verdict)}</span>");
            }
            else
            {
                region.Append(Invariant, $"<span class=\"ck-locked\">unlocks at checkpoint 1, after block {row.Floor} of {row.Floor}</span>");
                region.Append(Invariant, $"<span class=\"ck-words\">{row.Setups} trade(s) so far, block {row.Blocks} of {row.Floor}</span><span class=\"tag-wait\">at checkpoint 1</span>");
            }

            region.Append("</div>");
        }

        region.Append("</div>");

        return region.ToString();
    }

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
