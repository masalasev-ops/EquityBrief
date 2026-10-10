using EquityBrief.Core.News;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Quotes;
using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// One stored bar, handed over exactly as the store holds it.
//
// The prices are decimal because the money rule is decimal in code and TEXT in
// storage, and they cross that boundary through Money and nowhere else. Volume
// is a long because the column is INTEGER.
public sealed record BarRow(
    string Ticker,
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

// One stored indicator, handed over exactly as the store holds it.
//
// Value is a nullable double because the column is REAL and nullable, and an
// indicator whose window is longer than the history behind its session has no
// value. BarCount is what makes that null legible, and it is served rather than
// dropped: a reader who sees no long average is owed the count that explains it.
public sealed record IndicatorRow(
    string Ticker,
    DateOnly SessionDate,
    string Name,
    double? Value,
    int BarCount);

// One of a name's chart averages over the sessions its indicator rows leave empty, as the night stored it: the values by
// session, the pull the sessions before the year came from, and why none was read where none was.
public sealed record ChartAverageRow(
    string Name,
    DateOnly Night,
    IReadOnlyList<(DateOnly Session, double Value)> Values,
    string? Pull,
    string? Reason);

// One stored level band, handed over exactly as the store holds it.
//
// The members arrive as the JSON string the column holds rather than parsed
// into a shape of this project's own. Parsing here would put a second reading of
// that column beside the level builder's writing of it, and the surface that
// draws the summary table is the one that has to understand it.
public sealed record LevelRow(
    string Ticker,
    DateOnly AsOf,
    decimal LowEdge,
    decimal HighEdge,
    string Role,
    bool Immediate,
    int Strength,
    bool HasNonAverageAnchor,
    string Members);

// One band of a name's volume profile, as stored.
public sealed record ProfileRow(
    string Ticker,
    DateOnly AsOf,
    decimal BandLow,
    decimal BandHigh,
    long Shares,
    double ShareOfPeriod);

// One dated event the calendar holds for a name, as stored.
public sealed record CalendarRow(string Ticker, DateOnly EventDate, string Kind, string Timing, string Detail);

// A name's ladder row for one night: the trend state and the plan as stored.
// The plan is handed over as the stored JSON rather than parsed, because
// parsing it here would make this surface the second place the plan's shape is
// stated.
public sealed record LadderRow(string Ticker, DateOnly AsOf, string TrendState, string Plan);

// A name whose stored series the corporate action check marked suspect, as its row holds
// it: the reason its last refetch failed for, the instant it was last asked for, and how
// many nights after the one that marked it it has been asked for again. Every field is
// the stored column, and the instant stays the text the check wrote.
public sealed record SuspectSeriesRow(string Ticker, string? Reason, string CheckedAt, int Retries);

// A member the backfill asked for a year for and got none, as the newest of its rows naming
// the member says: the nights it was asked for, the session it was last asked for on, and
// the session it is next asked for on, null where that is the next night.
public sealed record NoYearRow(string Ticker, int Nights, DateOnly? Last, DateOnly? Next);

// One stored filing, as the store holds it. The payload and the source are handed
// over as written rather than unpacked here, because the read surface hands back
// stored values unchanged and a reader that picked figures out of the JSON would
// be deciding which of them the page may have.
public sealed record FilingRow(string Ticker, DateOnly FilingDate, string Payload, string Source);

// The newest version of one of a name's sections, and where the checker left it.
//
// The prose is not carried. What the page draws from this at 6.4 is whether a
// section was left out and why, and the written sections are drawn from 6.8,
// where each carries its own date and model beside it.
public sealed record SectionStateRow(string Section, int Version, DateOnly AsOf, string Status, string? Reason);

// The version of one of a name's sections a reader is shown: the newest the
// claim checker accepted, with the date it was written on and the model that
// wrote it. A newer version still waiting on its retry, or refused, does not take
// its place, because what a reader is shown is what passed.
//
// The prose and the source list are handed back as stored. What 6.6 draws from
// them is the cause of each move, in the moves table, and the provenance footer's
// date and model; the sections themselves are drawn from 6.8.
public sealed record WrittenSectionRow(string Section, int Version, DateOnly AsOf, string Model, string Prose, string SourceIds);

// One document a written section cites, as stored, less the body: the dates-and-sources
// region draws what it was, when it was published and where it is, and a reader follows
// the link for the text.
public sealed record CitedDocumentRow(string Id, string Url, string Title, DateOnly? PublishedOn, string Admissibility);

// The date one of a name's accepted sections was written on, as of a night, which is
// what tonight's header counts fresh prose against reused from.
public sealed record WrittenOnRow(string Ticker, string Section, DateOnly AsOf);

// One section that fell back on a night, from either store. `Subject` is a
// ticker for a name's section and a theme for a theme's, which is what the run
// page names beside the section.
public sealed record FellBackRow(string Subject, string Section, int Version, string? Reason);

// The high and the low of the sessions one move spans, which is what section
// 15.9's fact strip states. A name with no annotated move has none.
public sealed record YearExtremes(string Ticker, decimal High, DateOnly HighOn, decimal Low, DateOnly LowOn, int Sessions);

// One listing row, as the store holds it.
//
// `Reasons` and `PlanAtListing` arrive as the JSON the builder wrote, because
// the read surface hands back stored values unchanged and parsing one into a
// shape would be deriving.
//
// `BandStrength` is null on a row written before the listing recorded it, which is a row the
// comparison of tonight's orders does not read.
// see: A listing records the band strength the old order read, and the three orders are compared over the nights that recorded it
// Whether the evening listed the name, read by the rule that listed that evening: a reason firing
// before the switch and the swing filter passing it from the switch; and that rule.
// see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
public sealed record ListingRow(
    string Ticker,
    DateOnly SessionDate,
    string Reasons,
    int FiredCount,
    string PlanAtListing,
    int? BandStrength = null,
    bool? Listed = null,
    string? Rule = null)
{
    public bool IsListed => Listed ?? FiredCount > 0;

    public string ListedBy => Rule ?? EquityBrief.Core.Shortlist.ListRules.Reasons;
}

// One stage of one run, as the run log holds it.
//
// The two instants arrive parsed rather than as text, because the only thing
// this row is read for is a page that states how long each stage took, and a
// surface that parsed them would be the second place the log's time format is
// stated.
public sealed record RunStageRow(
    string RunId,
    string Stage,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Outcome,
    int RowsWritten,
    int ModelCalls,
    int NetworkRequests,
    string Spend,
    string Detail);

// One version a research pass or its theme pass wrote, as the store holds it now: the name or the theme it was
// written for, the section, the version, the status the checker gave it, its prose and why it was refused.
public sealed record WrittenVersion(string Owner, string Section, int Version, string Status, string Prose, string? RejectReason);

// One row of a pass as it runs: the component that wrote it, what it came to, and whether it
// has ended. A row with no end is the step the pass is on.
public sealed record PassStageRow(string Stage, string Outcome, DateTimeOffset StartedAt, bool Ended);

// One document a pass fetched and did not store, as the store holds it.
//
// The row exists because the refusal would otherwise be invisible: nothing else
// records that a document was fetched and refused, and the run page is the
// surface that shows it. So the body is null by rule here rather than by absence,
// and the column that would hold it is not read at all.
//
// `PublishedOn` is null on the refusal that is about the date being missing,
// which is why the page states the category beside every row rather than a date
// beside each: one class of refusal has no date to show.
public sealed record RefusedDocumentRow(
    string Id,
    string Url,
    string Title,
    DateOnly? PublishedOn,
    DateTimeOffset FetchedAt,
    string Category);

// One filled forward return, as the store holds it.
//
// `Outcome` is null while the horizon has not matured, and it is null rather
// than a word for it, because an unresolved setup is never a win and a word
// would put it in the same column as one. `BaseRate` sits on the row rather
// than beside the horizon, which is the grain the store already chose so the
// row a page reads carries the figure it must be read against.
// see: An unresolved setup is never a win
// see: Every forward-return figure is shown against the universe base rate
public sealed record ForwardReturnRow(
    string Ticker,
    DateOnly SessionDate,
    string Horizon,
    string? Outcome,
    DateOnly? ResolvedOn,
    double? ReturnPct,
    double? BaseRate,
    double? BreakEven = null);

// One row of the candidate register, as the run page reads it.
//
// The rule, the test, the parameters and the evidence are not carried, because
// the region states how many candidates are registered and the divisor that
// number sets and nothing else: a candidate's own record is withheld until it is
// promoted, and a row a page never draws is a row the page has no business
// holding.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
// One candidate's answer on one name-night, as the shadow column stored it, beside whether the live list
// picked the name: whether the candidate fired and each of its gate's answers with its plan.
public sealed record ShadowPickRow(DateOnly Session, string Ticker, bool LivePassed, string Candidate, bool Fired, string Values);

public sealed record CandidateRow(
    long Id,
    string Candidate,
    string Evaluator,
    string Event,
    string? Retires,
    DateTimeOffset RegisteredAt,
    string Parameters = "{}",
    string? Evidence = null,
    string EvaluatorVersion = "");

// One name-night a candidate fired on, with what its setup came to.
//
// The name is not carried and no surface could draw one from this: what a record is over is a
// count of setups and the sessions they were listed on, and how a candidate's pick of a name
// turned out is the thing the shadow exists to keep off every screen until a look reads it.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
public sealed record CandidateSetupRow(
    string Candidate,
    DateOnly SessionDate,
    string? Outcome,
    double? Null,
    double? NullAtSensitivity,
    double? BreakEven,
    double? ReturnPct,
    double? PlannedRisk,
    bool OnEarnings);

// One night and one candidate it evaluated or skipped, which is one candidate standing when that
// night started.
public sealed record CandidateNightRow(DateOnly SessionDate, string Candidate);

// One open window of a ladder rule, as the versions region reads it.
//
// The instant and not the date it falls on. A window is closed and opened again under the same
// name when a pinned source moves, so two windows of one name can open on one day, and a region
// keyed on the date could not tell a score of one from a score of the other.
// see: A version's record belongs to the window its scores were written under and never to the version's name
public sealed record OpenVersionRow(string Version, string Parameters, DateTimeOffset OpenedAt);

// One label a version gave the night's names under one window, how many carried it, and how many
// of those are not the label the night itself stored.
public sealed record VersionLabelRow(string Version, DateTimeOffset OpenedAt, string Label, int Names, int Moved);

// One label the night's own rule gave, and how many names carried it. It belongs to no window,
// which is why it is not a version's row with the window left blank.
public sealed record LiveLabelRow(string Label, int Names);

// One completed block of one window's record, as the scorer froze it. No name is carried and no
// surface could draw one from it.
// see: A version's record is read from the blocks frozen as each completed
public sealed record VersionBlockRow(string Version, DateTimeOffset OpenedAt, VersionBlock Block);

// One of a name's biggest moves, as the store holds it.
//
// No cause. It is a researched claim and lives in `research_section` with its
// source, so the how-it-got-here table's cause column is explicitly absent until
// phase 6 rather than blank.
//
// The group the annotator read the move against, and its median over the same sessions,
// null on a row written before 11.5 wrote them.
public sealed record MoveRow(
    string Ticker,
    DateOnly SessionDate,
    int Sessions,
    double ChangePct,
    int Rank,
    string? GroupKind = null,
    string? GroupName = null,
    int? GroupMembers = null,
    int? GroupCounted = null,
    double? GroupMedian = null);

// A name's two readings for the peers table as the annotator stored them: the session they were
// taken at, the group the name's moves are read against, the year's high and the distance below
// it, the return over the window, null where the name holds too few bars for it, how many bars
// both were read over, and the members of its group its peers table draws, as the JSON the
// annotator wrote, null on a row written before the annotator chose them.
public sealed record PeerReadingRow(
    string Ticker,
    DateOnly SessionDate,
    string GroupKind,
    string? GroupName,
    decimal YearHigh,
    double BelowHighPct,
    double? ReturnPct,
    int Bars,
    string? Peers = null);

// One name's swing readings on a night as the swing reader stored them, and the reason it read
// nothing where it did.
public sealed record SwingReadingRow(
    string Ticker,
    DateOnly SessionDate,
    int Bars,
    double? ReturnShort,
    double? ReturnLong,
    double? PlaceShort,
    double? PlaceLong,
    double? Strength,
    decimal? RecentHigh,
    DateOnly? HighSession,
    int? PullbackSessions,
    double? Depth,
    double? DryUp,
    double? Tightness,
    string? Note);

// One member's readings on a night as the member reader stored them: its index, its close as traded, its dollar
// volume, its company's value, a round trip at its close in percent at the published table and at double, the profit
// gate, the coverage, none where a quarter it reads was fetched without its interest expense, its quarters' state, its
// year's high with the sessions since it and the close's nearness to it, its volume over its average, and its industry
// with that industry's S&P 500 members' return over a month and a quarter and their mean surprise before the night.
public sealed record MemberReadingRow(
    string IndexCode,
    string Ticker,
    DateOnly SessionDate,
    decimal? Close,
    decimal? DollarVolume,
    decimal? CompanyValue,
    double? Cost,
    double? CostDouble,
    bool Profit,
    bool? Coverage,
    string? State,
    decimal? YearHigh,
    double? Nearness,
    int? SinceHigh,
    double? VolumeRatio,
    string? Industry,
    double? IndustryMonth,
    double? IndustryQuarter,
    double? PeerSurprise);

// A company's analysts' rating counts as one fetch filed them: the day of the fetch, the five counts and their total,
// each none where the fetch filed none or was made before the counts were stored.
public sealed record RatingsRow(DateOnly Fetched, int? StrongBuy, int? Buy, int? Hold, int? Sell, int? StrongSell, int? Total, bool BeforeCounts = false);

// One night's swing filter results counted: the version it ran under, the members, how many passed
// each gate after the market and every one before it, the market held open, and how many of those no
// exclusion removed.
public sealed record GateNightRow(DateOnly Session, string Version, int Members, int Trend, int Setup, int Trigger, int Trade, int Listed);

// What the calibration region's trigger lines read off the run log and the rule versions: the nights
// run for the session the clock fell on that closed, the version step's line on each of them, and the
// nights of trend labels since the version holding a new label for more than one night opened, with
// its name, none where no such version is open.
// The newest shape proposal as the proposer stored it, with its decision where one was taken.
public sealed record ShapeProposalRow(
    long Id,
    DateOnly Session,
    string Version,
    int Ordinary,
    IReadOnlyList<EquityBrief.Core.Filter.Lever> Levers,
    double? ListNow,
    double? ListProposed,
    IReadOnlyList<string> Findings,
    string? Decision,
    string? Reason,
    string? Opened);

public sealed record TriggerReads(int? ConfirmationNights, string? ConfirmationVersion, int WiderMembers = 0, int RatedFourTimes = 0);

// A stored article of a name with the newest label written for it under any profile, or none.
public sealed record NewsArticleRow(
    string ArticleId,
    string Title,
    string Source,
    string PublishedAt,
    string Link,
    string Admissibility,
    string? Outcome,
    string? Cause,
    string? Kind,
    string? Direction,
    string? Reason,
    string? Model,
    string? Profile);

// One of the news labeller's own rows: its run, its outcome, when it ran and the detail naming the night.
public sealed record LabellerRunRow(string RunId, string Outcome, string StartedAt, string EndedAt, string Detail);

// One attempt of the store's copy, as its own row records it.
public sealed record StoreBackupRow(string RunId, string Outcome, string StartedAt, string EndedAt, string Detail);

// One member's swing filter result on a night as the filter stored it: each gate's pass, the family
// and the trigger, the trade read three ways, the exclusions, the rank among the names passing, and
// the gates' reasons and values as stored. The plan clear of the noise enters at the same close as the
// plan at the nearest bands, and a row written before it was stored carries none of it.
public sealed record GateResultRow(
    string Ticker,
    DateOnly SessionDate,
    string Version,
    bool Market,
    bool Trend,
    bool Setup,
    string? Family,
    bool Trigger,
    bool? TriggerEvent,
    bool Trade,
    double? LadderRewardToRisk,
    double? LadderStopMoves,
    decimal? SwingEntry,
    decimal? SwingStop,
    decimal? SwingTarget,
    double? SwingRewardToRisk,
    double? SwingStopMoves,
    IReadOnlyList<string> Exclusions,
    bool Passed,
    int? Rank,
    double? Strength,
    int? BandStrength,
    string Gates,
    decimal? ClearStop = null,
    decimal? ClearTarget = null,
    double? ClearRewardToRisk = null,
    double? ClearStopMoves = null);

// One trade the live list recommended, as the store holds it: the name the swing filter passed on a
// night it listed, the plan that night's trade gate read, its entry, stop and target, the forward return
// that plan was scored under with whether a row is stored at all, the company's name, the close the
// name's bars hold now for its listing night, and the newest close at or before the night asked for.
public sealed record PickRow(
    string Ticker,
    DateOnly Night,
    string Plan,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    bool OutcomeStored,
    string? Outcome,
    DateOnly? ResolvedOn,
    double? ReturnPct,
    double? BreakEven,
    string? Company,
    decimal? ListingClose,
    DateOnly? NowOn,
    decimal? NowClose,
    // The state the member's reported quarters gave it on its listing night, and none on a night that
    // stored no readings.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    string? State = null,
    // The setup family the page listed the trade under, and the other families the stock qualified under
    // that night, which a night the families did not draw holds none of.
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    string Family = EquityBrief.Core.Families.SetupFamilies.Pullback,
    IReadOnlyList<string>? Also = null,
    // What the plan put at risk from its fill, as the filler stored it beside the outcome.
    double? PlannedRisk = null);

// A pick's card as the night stored it: its index, night, family and stock, its place, the plan's prices, the rule it
// names, the card's values it was read with, its lines and the rule's record as the card read it, none where the rule had
// not been replayed; and what its plan in money and its management are worked from, the stock's sector, the trail and
// the cap, the round trip a share and the most holdings a book with no stop can hold.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
public sealed record DecisionCardRow(
    string Index,
    DateOnly Night,
    string Family,
    string Ticker,
    int Place,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    string Rule,
    string Settings,
    string Lines,
    string? Record,
    string? Sector = null,
    decimal? Trail = null,
    int? Cap = null,
    decimal? RoundTrip = null,
    int? BookHoldings = null,
    string? Hits = null,
    int? ScoreRank = null,
    string? Similar = null,
    string? Approved = null);

// The operator's own record of one family on one index as the night's follower wrote it: the unit its results are read
// in, the trades won at the target and lost at the stop where the rule sets a target, every trade ended, those open, and
// the average result once enough have ended.
// see: The operator's own record states its average result once twenty of its trades in a family and index have ended
public sealed record TakenRecordRow(string Index, string Family, string Unit, int Won, int Lost, int Ended, int Open, double? Average, int SameNights = 0, int RuleListed = 0, int RuleWon = 0, int RuleLost = 0, int RuleEnded = 0, double? RuleAverage = null);

// A trade the operator took from a card, as the store holds it: the stock and when it was taken, the card it came from,
// the stock's sector, its fill and the session it is for, whether that fill is still the plan's buy awaiting the next
// session's open and whether the operator entered it, the plan it is managed by, and the exit the operator recorded.
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
public sealed record TakenTradeRow(
    string Ticker,
    string TakenAt,
    string Index,
    string Family,
    DateOnly Night,
    string? Sector,
    decimal Fill,
    DateOnly FillDate,
    bool Provisional,
    bool Entered,
    decimal? Stop,
    decimal? Target,
    decimal? Trail,
    int? Cap,
    decimal? ExitPrice,
    DateOnly? ExitDate,
    string? FollowedThrough,
    DateOnly? EndedOn = null,
    decimal? EndPrice = null,
    string? EndReason = null,
    double? Result = null)
{
    // Open while neither the operator's exit nor the night's follower has ended it.
    public bool IsOpen => ExitDate is null && EndedOn is null;
}

// One stock under one family on a night the families drew the page's list, as the store holds it: whether
// the page lists it, its place down the page and the other families it qualified under, or why it is held
// back, a trade still open naming the family and the night that listed it.
public sealed record FamilyPickRow(
    DateOnly SessionDate,
    string Ticker,
    string Family,
    string State,
    int? Place,
    IReadOnlyList<string> Also,
    string? HeldFamily,
    DateOnly? HeldNight,
    string? HeldIndex = null);

// One S&P 400 or 600 night as the index families stored it: the members read, the index's own breadth, whether its
// market check left its swing lists open, the settings its rules ran on and whether its sector heavyweights rebalanced.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
public sealed record IndexNightRow(string Index, DateOnly Session, int Members, double? Breadth, bool MarketOpen, string Settings, bool Rebalanced, string? Fault = null);

// One member's answer under one of an index's families on a night: whether the family's provisional rule passed it,
// its place among those it passed and its trade, or the first part of the rule it failed.
public sealed record IndexResultRow(string Ticker, string Family, bool Passed, int? Place, decimal? Entry, decimal? Stop, decimal? Target, decimal? Trail, int? Cap, double? OrderBy, string? Reason);

// One trade an index's list kept: where it ended by the night, its result before its cost and its cost, each in
// multiples of its risk, none while open on the night.
public sealed record IndexTradeRow(string Index, string Family, string Ticker, DateOnly Listed, int Place, decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, DateOnly? EndedOn, double? Result, double? Cost);

// One holding an index's sector heavyweights kept, as of a night: where it ended by the night, why, its result, its
// size cut's and its cost, each a fraction of its buy and none while it is held on the night, and the lead it was bought
// on, none where its book stored none.
public sealed record IndexHoldingRow(string Index, string Ticker, DateOnly EnteredOn, string Sector, decimal EntryClose, DateOnly? EndedOn, decimal? ExitClose, string? Reason, double? Result, double? CutReturn, double? Cost, double? Lead = null);

// One sector heavyweight holding as the book stored it as of a night: the stock, the session it was bought on, its
// sector and company, its buy close, and where it had ended by the night, its sale close, why, its result and its
// size cut's return over the same sessions, each none for a holding still open on the night.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
public sealed record HeavyweightHoldingRow(
    string Ticker,
    DateOnly EnteredOn,
    string Sector,
    string Company,
    decimal EntryClose,
    DateOnly? EndedOn,
    decimal? ExitClose,
    string? Reason,
    double? Result,
    double? CutReturn);

// One of a sector's largest companies as the book read it at a rebalance: its place by value, its value, its
// return over the look-back, the sector's, its lead over the sector's, whether it passed the trend gate and
// whether the rule bought it as the sector's leader.
public sealed record HeavyweightReadRow(
    DateOnly Session,
    string Sector,
    int Place,
    string Ticker,
    string Company,
    decimal CompanyValue,
    double? LookBack,
    double? SectorReturn,
    double? Lead,
    bool Trend,
    bool Leader);

// A holding's close on a night beside its 200-session average there, each none where the store holds none.
public sealed record HeavyweightCloseRow(string Ticker, decimal? Close, double? Average200);

// One member's answer under one setup family but the pullback on a night, as the family evaluator stored
// it: whether it passed, the gates it missed, its place among the names the family passed, the trade it is
// bought on, and its gates with their reasons and values.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public sealed record FamilyResultRow(
    DateOnly SessionDate,
    string Ticker,
    string Family,
    bool Passed,
    int Missed,
    int? Place,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    double? OrderBy,
    IReadOnlyList<string> Exclusions,
    string Gates);

// One member's readings of its reported quarters on a night, as the fundamental reader stored them.
// see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
public sealed record FundamentalReadingRow(
    string Ticker,
    DateOnly SessionDate,
    string State,
    DateOnly? ReadFrom,
    string? FetchedAt,
    DateOnly? Awaited,
    string Readings);

// A quarter one fetch stored, with the dates it was filed and reported on.
public sealed record QuarterDatesRow(DateOnly PeriodEnd, DateOnly? FilingDate, DateOnly? ReportDate);

// The night's market reading as the swing reader stored it.
public sealed record MarketReadingRow(
    DateOnly SessionDate,
    int Members,
    int Counted,
    int Above,
    double? Breadth,
    int CountedContext,
    int AboveContext,
    double? BreadthContext,
    int VolumeCounted,
    double? MedianVolumeRatio);

// One print's earnings reaction as the annotator stored it: the report date, when in the session,
// the session it moved on, the estimate and the actual as the provider filed them, null where it
// filed none, the provider's surprise, null beside no estimate, and the session's move.
public sealed record ReactionRow(
    string Ticker,
    DateOnly ReportDate,
    string Timing,
    DateOnly Session,
    string? Estimate,
    string? Actual,
    double? SurprisePct,
    double MovePct);

// One name's close on one session, as the day change is read from.
//
// A close and the session it is the close of, because a day change needs two of
// these and a pair with no dates on it cannot say which is the earlier. The
// subtraction is not here: this hands back the stored column and the projection
// that draws the column works out the change, which is the seam
// `UniverseScreen` already names for the distance.
// see: A screen reads and renders, and each figure it works out has one function in the core
public sealed record CloseRow(string Ticker, DateOnly SessionDate, decimal Close);

// One row of the universe screen, and every field is a stored column.
//
// Nothing here is derived, which is what keeps the read surface's own claim
// true. The distance the screen sorts on is worked out from these values by the
// projection, in the seam `NameScreen` names, and not here and not in the page.
//
// Every nullable field is a name the night computed nothing for: a joiner with
// no bars has no close, a name with no ladder row has no trend state, and a name
// whose chart has no band on one side has no edge there. Each is drawn as an
// absence rather than as a zero.
// A name the operator watches, and the day it was added.
public sealed record WatchedRow(string Ticker, DateOnly AddedOn);

public sealed record UniverseRow(
    string Ticker,
    string? Sector,
    decimal? Close,
    string? TrendState,
    decimal? NearestSupport,
    decimal? NearestResistance,
    double? TypicalMove,
    // The company's name and its industry, as the membership row holds them, which a
    // page states beside the ticker. Null for a row that carries none.
    string? Name = null,
    string? Industry = null,
    // The day the name's newest researched section was written, and null for a name
    // holding none.
    DateOnly? Researched = null);

// A current member as the masthead's search offers it: its ticker, its company's name, and
// the day its newest researched section was written, null where it holds none.
public sealed record FindableRow(string Ticker, string? Name, DateOnly? Researched);

// A name holding researched sections: the day its newest one was written and how many
// sections it holds.
public sealed record ResearchedRow(string Ticker, string? Name, string? Sector, DateOnly Written, int Sections);

// The read surface. Serves what the nightly run stored, and nothing else.
//
// It computes nothing and fetches nothing, which section 7's row states and
// this checkpoint's done condition requires be proved rather than asserted.
// Both halves are meant literally. No value leaving here is derived from
// another: every field is the stored column, converted between the storage form
// and the code form and not otherwise touched. And the project has no feed, no
// client and no reference to the worker, which api-isolation reads from the
// compiled dependency file rather than from the project file.
//
// The seam matters more than it looks. A read surface that computes is a second
// place the arithmetic lives, and the day it disagrees with the nightly run
// nothing says which one is the system.
// see: Code owns every number
// see: A screen reads and renders, and each figure it works out has one function in the core
public sealed partial class ReadApi : IComponent
{
    // Reads every store but the pulled history and appends to the run log, which
    // is section 7's row for this component and the R cells plus one W in its
    // matrix row. The pulled history is the one column it leaves blank, because
    // no screen draws a pulled row and a pull is removed whole by its id.
    //
    // Every store means every store the matrix has a column for, and from 8.4
    // that includes the candidate register: the run page states how many
    // candidates are registered and the divisor that number sets, and a count
    // drawn on a page is a count something read. It reads the register, and a
    // candidate's picks only for the run page's comparison of tonight's picks.
    // Series state was the one column the row left blank until the 7.0 ruling,
    // which has the name page and tonight's list say where a name's prices may
    // not reflect a dividend or split.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.KeptBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.ChartAverage, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Move, Touch.Read),
            new StoreTouch(Store.PeerReading, Touch.Read),
            new StoreTouch(Store.SwingReading, Touch.Read),
            new StoreTouch(Store.MarketReading, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.ShapeProposal, Touch.Read),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.ListRule, Touch.Read),
            new StoreTouch(Store.FamilyResult, Touch.Read),
            new StoreTouch(Store.FamilyNight, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read),
            new StoreTouch(Store.HeavyweightRuleNight, Touch.Read),
            new StoreTouch(Store.HeavyweightRuleHolding, Touch.Read),
            new StoreTouch(Store.IndexFamilyNight, Touch.Read),
            new StoreTouch(Store.IndexFamilyResult, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.IndexFamilyTrade, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read),
            new StoreTouch(Store.IndexRuleTrade, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightRuleNight, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightRuleHolding, Touch.Read),
            new StoreTouch(Store.DecisionCard, Touch.Read),
            new StoreTouch(Store.RuleNight, Touch.Read),
            new StoreTouch(Store.RulePick, Touch.Read),
            new StoreTouch(Store.FormingRow, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.LedgerSummary, Touch.Read),
            new StoreTouch(Store.LoopRun, Touch.Read),
            new StoreTouch(Store.LoopProposal, Touch.Read),
            new StoreTouch(Store.LoopTest, Touch.Read),
            new StoreTouch(Store.LoopFinding, Touch.Read),
            new StoreTouch(Store.LoopReading, Touch.Read),
            new StoreTouch(Store.LoopModel, Touch.Read),
            new StoreTouch(Store.LoopDecision, Touch.Read | Touch.Insert),
            new StoreTouch(Store.LoopApplied, Touch.Read),
            new StoreTouch(Store.ProvisionalSetting, Touch.Read),
            new StoreTouch(Store.LoopAlarm, Touch.Read),
            new StoreTouch(Store.SweepAnswer, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.EstimateReading, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.Fundamentals, Touch.Read),
            new StoreTouch(Store.FundamentalsSnapshot, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.DividendReading, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Read),
            new StoreTouch(Store.NewsPulse, Touch.Read),
            new StoreTouch(Store.NewsArticle, Touch.Read),
            new StoreTouch(Store.NewsLabel, Touch.Read),
            new StoreTouch(Store.ResearchSection, Touch.Read),
            new StoreTouch(Store.ThemeSection, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.RuleVersion, Touch.Read),
            new StoreTouch(Store.VersionScore, Touch.Read),
            new StoreTouch(Store.VersionBlock, Touch.Read),
            new StoreTouch(Store.SeriesState, Touch.Read),
            new StoreTouch(Store.ResearchRequest, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.QuoteRequest, Touch.Read | Touch.Insert),
            new StoreTouch(Store.LiveQuote, Touch.Read),
            new StoreTouch(Store.WatchList, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.TakenTrade, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.TakenRecord, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "read-api";

    // The state the corporate action check marks a name whose refetch failed with. The
    // worker's own constant cannot be referenced from here, so it is stated and
    // `read-surface` asserts the two agree.
    public const string SuspectState = "suspect";

    readonly string databaseFile;
    readonly IClock clock;

    public ReadApi(string databaseFile, IClock clock)
    {
        this.databaseFile = databaseFile;
        this.clock = clock;
    }

    // The stage and outcome the spend cap writes a paid call under. The worker's own
    // constants cannot be referenced from here, so they are stated and `read-surface`
    // asserts the two agree.
    public const string PaidCallStage = "research call";
    public const string PaidOutcome = "ok";

    // What the spend cap writes for a call that cost nothing, which is money as the
    // invariant culture writes a zero.
    public const string NothingSpent = "0";

    // What the run log says was spent between two instants, as stored.
    const string SpentBetween = @"
        SELECT started_at, spend
        FROM run_log
        WHERE started_at >= $from AND started_at < $to;
    ";

    // Every paid call the log carries a cost for: the spend cap's rows, by the stage's
    // own prefix, that carry a price. That is every answered call and, from 6.8, every
    // call the provider answered with nothing usable and billed all the same, which is a
    // refusal carrying a cost. A call refused before it was made, or never answered,
    // carries none.
    // The instant each answered paid call came back, which is the instant its price was read at.
    const string PaidCallAnswers = @"
        SELECT json_extract(detail, '$.created')
        FROM run_log
        WHERE substr(stage, 1, length($prefix)) = $prefix AND spend != $nothing AND json_valid(detail);
    ";

    // The stages a report is read from beside the pass's own: the theme pass under the same run and a section a
    // trial asked for. The worker's own constants cannot be referenced from here, so the words are stated and
    // `read-surface` asserts they agree.
    public const string ThemeStage = "theme research";
    public const string TrialStage = "section trial";
    public const string ReviewStage = "section review";

    // Every row of every research pass the run page and the comparison command read reports from: the pass's own
    // row, the theme pass's, each paid call and each section a trial or a review asked for.
    // see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports, and no trial's drafts
    const string ReportRows = @"
        SELECT run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail
        FROM run_log
        WHERE substr(run_id, 1, length($pass)) = $pass
          AND (stage IN ($research, $theme)
            OR substr(stage, 1, length($call)) = $call
            OR substr(stage, 1, length($trial)) = $trial
            OR substr(stage, 1, length($review)) = $review)
        ORDER BY rowid;
    ";

    // Each version a research pass or its theme pass says it wrote, as the stores hold it now.
    const string ReportVersions = @"
        SELECT r.ticker, r.section, r.version, r.status, r.prose, r.reject_reason
        FROM run_log l, json_each(l.detail, '$.written') w
        JOIN research_section r
          ON r.ticker = json_extract(l.detail, '$.ticker')
         AND r.section = json_extract(w.value, '$.section')
         AND r.version = json_extract(w.value, '$.version')
        WHERE l.stage = $research AND substr(l.run_id, 1, length($pass)) = $pass AND json_valid(l.detail)
        UNION
        SELECT t.theme, t.section, t.version, t.status, t.prose, t.reject_reason
        FROM run_log l, json_each(l.detail, '$.written') w
        JOIN theme_section t
          ON t.theme = json_extract(l.detail, '$.theme')
         AND t.section = json_extract(w.value, '$.section')
         AND t.version = json_extract(w.value, '$.version')
        WHERE l.stage = $theme AND substr(l.run_id, 1, length($pass)) = $pass AND json_valid(l.detail);
    ";

    // A trial's calls and a review's are left out, being spend beside a report rather than on it.
    // see: A trial asks a second profile for named sections beside a report, and ships naming none
    // see: A review asks a section's model to check its own draft against the section's rules, beside a stated number of reports
    const string PaidCallSpends = @"
        SELECT run_id, spend
        FROM run_log
        WHERE substr(stage, 1, length($prefix)) = $prefix AND spend != $nothing AND instr(stage, $trial) = 0 AND instr(stage, $review) = 0;
    ";

    // The newest accepted version of each of a name's sections, a stored key under each figure
    // left out as it is from every read the pages draw.
    // see: The key under each figure is retired with the overnight queue that wrote it, and its stored rows are drawn nowhere
    const string WrittenSectionsForName = @"
        SELECT r.section, r.version, r.as_of, r.model, r.prose, r.source_ids
        FROM research_section r
        WHERE r.ticker = $ticker
          AND r.section <> $retired
          AND r.status = 'accepted'
          AND r.as_of <= $on
          AND r.version = (
              SELECT MAX(s.version) FROM research_section s
              WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted' AND s.as_of <= $on)
        ORDER BY r.section;
    ";

    // The industry cycle a name reads, which is its theme's: the newest version the checker
    // accepted for the industry the index last named for the member. A name's cycle is
    // never one of its own rows, so it is read off the theme store and not the research one.
    // see: A theme is the industry the index names for a member, and one theme pass serves every member it names
    const string ThemeCycleForName = @"
        SELECT t.section, t.version, t.as_of, t.model, t.prose, t.source_ids
        FROM theme_section t
        WHERE t.theme = (
                SELECT m.industry FROM membership m
                WHERE m.ticker = $ticker AND m.industry IS NOT NULL
                ORDER BY m.observed_at DESC
                LIMIT 1)
          AND t.section = $section
          AND t.status = 'accepted'
        ORDER BY t.version DESC
        LIMIT 1;
    ";

    // The stages the prose writer and the research runner record themselves under.
    // The worker's own constants cannot be referenced from here, since the read
    // surface holds no reference to the worker, so the words are stated and
    // `read-surface` asserts the two agree.
    public const string ProseStage = "prose";
    public const string ResearchStage = "research";

    // What a research pass came to, as its stage's outcome column holds it, stated for
    // the reason the stages are.
    public const string PassWritten = "ok";
    public const string PassNotWarranted = "not warranted";
    public const string PassUnavailable = "unavailable";
    public const string PassPaused = "paused";
    public const string PassAlreadyRunning = "already running";
    public const string PassNoFactsFile = "no facts file";

    // The newest pass the run log holds for one name, as its stage recorded it: a
    // research pass, or a prose pass the local lane ran on its own. A research pass
    // writes its prose stage's row before its own, so its own is the newer and
    // carries what the whole pass did. The name sits inside the detail rather than in
    // a column, so it is read with the store's own JSON function, and newest by the
    // order the rows were written, which is the ordering the run page takes for the
    // reason 6.0 gave. A detail that is not JSON is passed over rather than read,
    // since every other stage writes a sentence there.
    //
    // A press that found nothing to do and one refused because a pass was running are
    // passed over too. Neither wrote or tried anything, and read as the newest they
    // would take the lines of the pass that did off the page.
    const string NewestPassForName = @"
        SELECT detail FROM run_log
        WHERE stage IN ($prose, $research)
          AND outcome NOT IN ($not_warranted, $already_running)
          AND CASE WHEN json_valid(detail) THEN json_extract(detail, '$.ticker') END = $ticker
        ORDER BY rowid DESC
        LIMIT 1;
    ";

    // The documents a set of sections cite, newest published first. The ids arrive as
    // one JSON array, read by the store's own function, so the statement is one text
    // however many a page cites. No body: a page links to a document rather than
    // reprinting it.
    const string CitedDocuments = @"
        SELECT id, url, title, published_on, admissibility
        FROM source_document
        WHERE id IN (SELECT value FROM json_each($ids))
        ORDER BY published_on DESC, id;
    ";

    // Every dated event the calendar holds for a name on or after a date, which is the
    // calendar half of the dates-and-sources region.
    const string EventsForName = @"
        SELECT ticker, event_date, kind, timing, detail
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $on_or_after
        ORDER BY event_date, kind;
    ";

    // The date each name's accepted sections were written on, the newest accepted
    // version of each on or before a night, which is what tonight's header reads a
    // report's prose as fresh or reused from.
    const string WrittenOnOrBefore = @"
        SELECT r.ticker, r.section, r.as_of
        FROM research_section r
        WHERE r.status = 'accepted'
          AND r.as_of <= $night
          AND r.version = (
              SELECT MAX(s.version) FROM research_section s
              WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted' AND s.as_of <= $night)
        ORDER BY r.ticker, r.section;
    ";

    // The newest version of each of a name's sections, on or before the night the
    // page is about, so an earlier night's page shows what the report said then.
    const string SectionStatesForName = @"
        SELECT r.section, r.version, r.as_of, r.status, r.reject_reason
        FROM research_section r
        WHERE r.ticker = $ticker
          AND r.section <> $retired
          AND r.as_of <= $night
          AND r.version = (
              SELECT MAX(s.version) FROM research_section s
              WHERE s.ticker = r.ticker AND s.section = r.section AND s.as_of <= $night)
        ORDER BY r.section;
    ";

    // A name's earnings events, every one the calendar holds, which the staleness
    // rules read for the newest print on or before the night.
    const string EarningsForName = @"
        SELECT event_date, timing FROM calendar
        WHERE ticker = $ticker AND kind = 'earnings'
        ORDER BY event_date;
    ";

    // A name's news pulse, every session the table keeps.
    const string PulseForName = @"
        SELECT session_date, article_count FROM news_pulse
        WHERE ticker = $ticker
        ORDER BY session_date;
    ";

    // The newest night the store computed a facts file for this name, which is what
    // the staleness verdict is as of on the page as it is in the judge.
    const string NewestFactsNight = @"
        SELECT MAX(session_date) FROM facts WHERE ticker = $ticker;
    ";

    // The sections that fell back on one night, from both stores. By the date
    // each was written, which is a date rather than an instant and needs no clock.
    const string FellBackOnNight = @"
        SELECT ticker, section, version, reject_reason
        FROM research_section
        WHERE status = 'fallback' AND as_of = $night AND section <> $retired
        UNION ALL
        SELECT theme, section, version, reject_reason
        FROM theme_section
        WHERE status = 'fallback' AND as_of = $night
        ORDER BY 1, 2, 3;
    ";

    // Newest filing first, which is the order the numbers section draws in and the
    // order a restatement arrives in: a later filing about an earlier quarter is a
    // later row, so ordering on the filing date is what puts what is now known at
    // the top.
    const string FilingsForName = @"
        SELECT ticker, filing_date, payload, source
        FROM fundamentals
        WHERE ticker = $ticker AND filing_date <= $on
        ORDER BY filing_date DESC;
    ";

    // Every copy of the parts a fetch stores as of itself, newest first. Read whole rather than
    // bounded here, because the night a copy belongs to is the session date of the instant it was
    // fetched at, which is the clock's to say.
    const string SnapshotsForName = @"
        SELECT fetched_at, payload
        FROM fundamentals_snapshot
        WHERE ticker = $ticker
        ORDER BY fetched_at DESC;
    ";

    // The bars of the stored year up to the night, whose highest high and lowest
    // low the fact strip states with the session each was made on.
    //
    // The bars are handed back and the two extremes are chosen from them as
    // prices. Choosing which stored value to hand back is selection the read
    // surface permits, as the `MAX(as_of)` the level query uses is, but a price
    // is stored as text and the store compares text by its characters, so the
    // choice is made where the values are decimals.
    // see: A stored price is chosen and ordered by its value and never by the text it is stored as
    // see: The fact strip states the year's high and low with the sessions they were made on
    const string YearForName = @"
        SELECT session_date, high, low
        FROM bar
        WHERE ticker = $ticker AND session_date <= $on
        ORDER BY session_date;
    ";

    // Ordered by session so the caller does not have to sort, which is the one
    // thing this does beyond selecting. Ordering is not computation: it changes
    // which row comes first and never what a row says.
    const string BarsForName = @"
        SELECT ticker, session_date, open, high, low, close, volume
        FROM bar
        WHERE ticker = $ticker AND session_date >= $from AND session_date <= $to
        ORDER BY session_date;
    ";

    // Ordered by session and then by name, for the reason the bars query is
    // ordered: the caller does not have to sort and ordering says nothing about
    // what a row holds. The null value is served as a null and never as a zero,
    // because a zero is a reading and an absent indicator is not one.
    // Every band for one name at its latest as-of date. The latest rather than
    // all of them, because the level table on the name page is tonight's map and
    // a page holding two nights of bands would be a page holding two maps.
    //
    // Unordered here and put in price order by the reader, because an edge is
    // stored as text and the store would order it by its characters, which reads
    // a band at 87 as sitting above one at 117.
    // see: A stored price is chosen and ordered by its value and never by the text it is stored as
    const string LevelsForName = @"
        SELECT ticker, as_of, low_edge, high_edge, role, immediate, strength,
               has_non_average_anchor, members
        FROM level
        WHERE ticker = $ticker
              AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = $ticker AND as_of <= $on);
    ";

    // The latest night alone, for the same reason the level query binds as_of to
    // the maximum: a page holding two nights of profile bands is a page holding
    // two histograms of different periods, and unordered for the reason that
    // query is, the histogram being drawn from the bottom band up.
    // see: A stored price is chosen and ordered by its value and never by the text it is stored as
    const string ProfileForName = @"
        SELECT ticker, as_of, band_low, band_high, share_count, share_of_period
        FROM volume_profile
        WHERE ticker = $ticker
              AND as_of = (SELECT MAX(as_of) FROM volume_profile WHERE ticker = $ticker AND as_of <= $on);
    ";

    // The next event on or after a date, which is what the fact strip states and
    // what the earnings-soon condition reads. A name with no row answers with
    // nothing, and nothing is what the strip says rather than a guessed date.
    const string NextEventForName = @"
        SELECT ticker, event_date, kind, timing, detail
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $on_or_after
        ORDER BY event_date
        LIMIT 1;
    ";

    // The next event on or after a date for every name that has one, which is
    // what the universe table's sessions-until-earnings column reads.
    //
    // One query rather than one per name. The per-name form above answers the
    // fact strip, which is about the one name a reader opened; this answers a
    // column over five hundred rows, and five hundred round trips to draw one
    // column is the shape that makes a page too slow to open. The window
    // function picks each name's earliest event on or after the date, so the
    // row this returns is the row the per-name query would return.
    const string NextEventForEveryName = @"
        SELECT ticker, event_date, kind, timing, detail
        FROM (
            SELECT ticker, event_date, kind, timing, detail,
                   ROW_NUMBER() OVER (PARTITION BY ticker ORDER BY event_date) AS seen
            FROM calendar
            WHERE event_date >= $on_or_after)
        WHERE seen = 1
        ORDER BY ticker;
    ";

    // Every name's two newest stored sessions at or before a night, which is what
    // a day change is made of.
    //
    // Both legs from one read, and both bounded by the night. They were two reads
    // at 5.8 and only one of them was bounded: the close came from
    // `Universe`, whose close column is the name's newest bar whatever night is
    // asked for, and the previous close was the newest bar before the night. On
    // any past night that subtracts two sessions that are not consecutive and are
    // not the night's, and for a name with no bar on the night it subtracts one
    // session from itself and draws exactly 0.00, which is the absence this
    // surface states everywhere else. The sixth phase 5 sign-off review found
    // both shapes.
    //
    // At or before rather than on the night, because whether the name traded that
    // session is the question the caller has to answer and this hands back what
    // the store holds. The projection draws a change only where the newer of the
    // two is the night itself.
    //
    // Two rather than one, because the session before a Monday is the Friday and
    // no table here holds that relation. A name whose series starts on the night
    // has one row here and no change to draw.
    const string ClosesToTheNight = @"
        SELECT ticker, session_date, close
        FROM (
            SELECT ticker, session_date, close,
                   ROW_NUMBER() OVER (PARTITION BY ticker ORDER BY session_date DESC) AS seen
            FROM bar
            WHERE session_date <= $session)
        WHERE seen <= 2
        ORDER BY ticker, session_date DESC;
    ";

    const string LadderForName = @"
        SELECT ticker, as_of, trend_state, plan
        FROM ladder
        WHERE ticker = $ticker AND as_of <= $on
        ORDER BY as_of DESC
        LIMIT 1;
    ";

    const string IndicatorsForName = @"
        SELECT ticker, session_date, name, value, bar_count
        FROM indicator
        WHERE ticker = $ticker AND session_date >= $from AND session_date <= $to
        ORDER BY session_date, name;
    ";

    // A name's biggest moves, largest first, which is the order the
    // how-it-got-here table is read down.
    const string MovesForName = @"
        SELECT ticker, session_date, sessions, change_pct, rank,
               group_kind, group_name, group_members, group_counted, group_median
        FROM move
        WHERE ticker = $ticker AND session_date <= $on
        ORDER BY rank;
    ";

    // Every name's two readings, which a name's peers table draws for the members of its group.
    const string EveryPeerReading = @"
        SELECT ticker, session_date, group_kind, group_name, year_high, below_high_pct, return_pct, bars, peers
        FROM peer_reading
        ORDER BY ticker;
    ";

    // Every stored close of the names given, in session order, which the picture drawn beside each
    // member of a peers table reads.
    const string ClosesOfNames = @"
        SELECT ticker, session_date, close
        FROM bar
        WHERE ticker IN (SELECT value FROM json_each($tickers))
        ORDER BY ticker, session_date;
    ";

    // A name's earnings reaction record, oldest print first, as of the night the page shows.
    const string ReactionsForName = @"
        SELECT ticker, report_date, timing, reaction_session, estimate, actual, surprise_pct, move_pct
        FROM earnings_reaction
        WHERE ticker = $ticker AND report_date <= $on
        ORDER BY report_date;
    ";

    // The newest night the listings hold, so the front page resolves to it
    // without a date being asked for.
    const string NewestNight = "SELECT MAX(session_date) FROM listing WHERE session_date <= $on;";

    const string HeldNights = "SELECT DISTINCT session_date FROM listing ORDER BY session_date;";

    // The newest run the log carries a stage for, which is the night the run
    // page opens on. The read surface's own row and a night on a day with no
    // session are not nights that ran: the first is this process starting and
    // the second fetched nothing, and opening on either would hide the evening
    // before it.
    //
    // Ordered by the order the rows were written and not by the instant they
    // carry, which is 6.0's repair for what the phase 5 sign-off found. A replay
    // stamps `started_at` from 21:10Z on the session it was given, so a night
    // replayed for an older session after tonight's ran carries the older
    // instant and this query would name it the newest run. The page would then
    // open on a night whose list the store does not hold. The write order is the
    // rowid, which is the one thing here that cannot be stamped.
    //
    // A command a person runs by hand is not a night, so its run ids are left out, one
    // clause per prefix the run screen reads as by hand. GLOB, because it is case sensitive.
    static string NewestRun =>
        "SELECT started_at FROM run_log WHERE stage != $read_api AND outcome != $no_session"
        + string.Concat(RunScreen.RunsByHand.Select((_, at) => FormattableString.Invariant($" AND run_id NOT GLOB $by_hand_{at}")))
        + " ORDER BY rowid DESC LIMIT 1;";

    // Whether a listing's name was on its night's list, by the rule that drew that night's list: listed
    // by a family where the families drew it, passed by the swing filter on a night it listed before
    // them, and fired by a reason before that.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    static readonly string WasListed = EquityBrief.Core.Families.FamilyList.OnTheList(
        "l.ticker", "l.session_date", "CASE WHEN r.rule = 'filter' THEN IFNULL(g.passed, 0) ELSE l.fired_count > 0 END");

    // Every listing for one night, fired and quiet alike, because the page's own
    // header states the true fired count over the whole index and the twenty
    // drawn rows cannot tell you it.
    static readonly string ListingsForNight = @"
        SELECT l.ticker, l.session_date, l.reasons, l.fired_count, l.plan_at_listing, l.band_strength,
               " + WasListed + @",
               IFNULL(r.rule, 'reasons')
        FROM listing l
        LEFT JOIN list_rule r ON r.session_date = l.session_date
        LEFT JOIN gate_result g ON g.ticker = l.ticker AND g.session_date = l.session_date
        WHERE l.session_date = $session_date
        ORDER BY l.ticker;
    ";

    // Every listing the store holds, which is what a reason's record is counted
    // over.
    //
    // Every night rather than tonight, because a record is a property of the
    // reason across every name it ever fired for, and a record over one evening
    // would be a statement about that evening wearing the clothes of a verdict.
    // It is a scan of the table, and it is one scan an evening on a page nobody
    // reloads: the reasons are JSON on the row, so a count per reason cannot be
    // asked of the store.
    static readonly string EveryListing = @"
        SELECT l.ticker, l.session_date, l.reasons, l.fired_count, l.plan_at_listing, l.band_strength,
               " + WasListed + @",
               IFNULL(r.rule, 'reasons')
        FROM listing l
        LEFT JOIN list_rule r ON r.session_date = l.session_date
        LEFT JOIN gate_result g ON g.ticker = l.ticker AND g.session_date = l.session_date
        ORDER BY l.session_date, l.ticker;
    ";

    // A name's own listing history, which is what the universe screen's two
    // right-hand columns count and what the listing strip draws.
    static readonly string ListingsForName = @"
        SELECT l.ticker, l.session_date, l.reasons, l.fired_count, l.plan_at_listing, l.band_strength,
               " + WasListed + @",
               IFNULL(r.rule, 'reasons')
        FROM listing l
        LEFT JOIN list_rule r ON r.session_date = l.session_date
        LEFT JOIN gate_result g ON g.ticker = l.ticker AND g.session_date = l.session_date
        WHERE l.ticker = $ticker AND l.session_date <= $on
        ORDER BY l.session_date DESC
        LIMIT $sessions;
    ";

    const string ListRuleOn = "SELECT rule FROM list_rule WHERE session_date = $on;";

    const string FirstFilterNight = "SELECT MIN(session_date) FROM list_rule WHERE rule = $filter;";

    const string Watched = "SELECT ticker, added_at FROM watch_list ORDER BY added_at, ticker;";

    const string WatchedCount = "SELECT COUNT(*) FROM watch_list;";

    const string WatchName = "INSERT INTO watch_list (ticker, added_at) VALUES ($ticker, $added_at) ON CONFLICT (ticker) DO NOTHING;";

    const string UnwatchName = "DELETE FROM watch_list WHERE ticker = $ticker;";

    // Every row a filter version stored, each gate's answer, its exclusions and whether it passed, with
    // what its own plan came to where one was scored: the population the near misses are read over.
    // see: A gate's near misses are the setups it alone rejected, each group read against its own break-even and null and withheld below the block floor
    // A version's rows with each setup scored on the plan the version's trade gate reads, the plan at the
    // nearest bands where the version reads another or no version was open.
    // see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
    const string NearMissRows = @"
        SELECT g.session_date, g.market, g.trend, g.setup, g.trigger_pass, g.trade, g.exclusions, g.passed,
               f.outcome, f.null_win, f.null_win_at_sensitivity, f.break_even, f.return_pct, f.planned_risk, f.on_earnings
        FROM gate_result g
        LEFT JOIN filter_version v ON v.version = g.version
        LEFT JOIN forward_return f
            ON f.ticker = g.ticker AND f.session_date = g.session_date
            AND f.horizon = CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN $clear ELSE $swing END
        WHERE g.version = $version
        ORDER BY g.session_date, g.ticker;
    ";

    // Every trade the live list recommended up to a night: each name the swing filter passed on a night
    // it listed, on the plan that night's trade gate read, with the forward return it was scored under,
    // newest first in the list's own order. The names passing and no other, so a member failing one gate
    // is not a trade the list recommended whatever plan its row carries; and a night the reasons listed
    // holds no filter row to read. The plan is chosen as the near misses choose it. On a night the families
    // drew the page's list, the pullback's trades are the stocks the page listed under it, in the page's
    // order, each with the other families it qualified under, and every other family's trades are the
    // stocks the page listed under it, each on the trade its family's stored answer holds and the outcome
    // scored under its family's horizon.
    // see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
    // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    static readonly string Picks = @"
        SELECT g.ticker, g.session_date,
               CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN $clear ELSE $swing END,
               g.swing_entry,
               CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN g.clear_stop ELSE g.swing_stop END,
               CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN g.clear_target ELSE g.swing_target END,
               f.horizon IS NOT NULL, f.outcome, f.resolved_on, f.return_pct, f.break_even,
               (SELECT m.name FROM membership m WHERE m.ticker = g.ticker
                ORDER BY m.observed_at DESC, m.rowid DESC LIMIT 1),
               (SELECT b.close FROM bar b WHERE b.ticker = g.ticker AND b.session_date = g.session_date),
               (SELECT b.session_date FROM bar b WHERE b.ticker = g.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               (SELECT b.close FROM bar b WHERE b.ticker = g.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               s.state,
               $pullback,
               (SELECT fp.also FROM family_pick fp
                WHERE fp.session_date = g.session_date AND fp.ticker = g.ticker AND fp.family = $pullback AND fp.state = $listed),
               f.planned_risk,
               IFNULL(" + EquityBrief.Core.Families.FamilyList.PlaceOn("g.ticker", "g.session_date") + @", 0),
               " + EquityBrief.Core.Quarters.FundamentalState.PlaceIn("s.state") + @",
               g.rank
        FROM gate_result g
        JOIN list_rule r ON r.session_date = g.session_date AND r.rule = $filter
        LEFT JOIN filter_version v ON v.version = g.version
        LEFT JOIN forward_return f
            ON f.ticker = g.ticker AND f.session_date = g.session_date
            AND f.horizon = CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN $clear ELSE $swing END
        LEFT JOIN fundamental_reading s ON s.ticker = g.ticker AND s.session_date = g.session_date
        WHERE g.session_date <= $on AND ($ticker IS NULL OR g.ticker = $ticker)
          AND CASE WHEN " + EquityBrief.Core.Families.FamilyList.HasPicks("g.session_date") + @"
                   THEN EXISTS (SELECT 1 FROM family_pick fp
                                WHERE fp.session_date = g.session_date AND fp.ticker = g.ticker AND fp.family = $pullback AND fp.state = $listed)
                   ELSE g.passed = 1 END
        UNION ALL
        SELECT p.ticker, p.session_date,
               " + EquityBrief.Core.Families.SetupFamilies.HorizonIn("p.family", "pv.settings") + @",
               pr.entry, pr.stop, pr.target,
               pf.horizon IS NOT NULL, pf.outcome, pf.resolved_on, pf.return_pct, pf.break_even,
               (SELECT m.name FROM membership m WHERE m.ticker = p.ticker
                ORDER BY m.observed_at DESC, m.rowid DESC LIMIT 1),
               (SELECT b.close FROM bar b WHERE b.ticker = p.ticker AND b.session_date = p.session_date),
               (SELECT b.session_date FROM bar b WHERE b.ticker = p.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               (SELECT b.close FROM bar b WHERE b.ticker = p.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               ps.state,
               p.family,
               p.also,
               pf.planned_risk,
               IFNULL(p.place, 0),
               0,
               0
        FROM family_pick p
        JOIN family_result pr ON pr.session_date = p.session_date AND pr.ticker = p.ticker AND pr.family = p.family
        LEFT JOIN gate_result pg ON pg.ticker = p.ticker AND pg.session_date = p.session_date
        LEFT JOIN filter_version pv ON pv.version = pg.version
        LEFT JOIN forward_return pf
            ON pf.ticker = p.ticker AND pf.session_date = p.session_date
            AND pf.horizon = " + EquityBrief.Core.Families.SetupFamilies.HorizonIn("p.family", "pv.settings") + @"
        LEFT JOIN fundamental_reading ps ON ps.ticker = p.ticker AND ps.session_date = p.session_date
        WHERE p.state = $listed AND p.family <> $pullback
          AND p.session_date <= $on AND ($ticker IS NULL OR p.ticker = $ticker)
        ORDER BY 2 DESC, 20, 21, 22, 1;
    ";

    // Every member's readings on one night.
    const string ReadingsOn = @"
        SELECT ticker, session_date, state, read_from, fetched_at, awaited, readings
        FROM fundamental_reading
        WHERE session_date = $on
        ORDER BY ticker;
    ";

    // One member's readings on the newest night at or before the one asked for.
    const string ReadingForName = @"
        SELECT ticker, session_date, state, read_from, fetched_at, awaited, readings
        FROM fundamental_reading
        WHERE ticker = $ticker AND session_date <= $on
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    // The quarters one fetch stored, newest first, with the dates each was filed and reported on.
    const string QuartersOfAFetch = @"
        SELECT period_end, filing_date, report_date
        FROM reported_quarter
        WHERE ticker = $ticker AND fetched_at = $fetched_at
        ORDER BY period_end DESC;
    ";

    // Every current member of the index, with what the night computed for it.
    //
    // Each figure is the newest at or before `$on`, so a page about an earlier
    // night states the close, the trend, the typical move and the bands that
    // night held rather than the newest the store holds.
    //
    // The population is the index rather than the names with bars, which is the
    // same population the ladder builder writes over and the same reason: a name
    // the night computed nothing for is a row saying so, and a name quietly
    // absent would make a count wrong in the direction nobody looks.
    //
    // Left joins throughout. The bands each row carries are read by the query
    // below rather than here, because they are prices and the store would choose
    // between two of them by their characters.
    //
    // One row a ticker. A provider that re-dates a member's span leaves two open
    // spans for one ticker, and the screen draws the one it listed most recently.
    const string Universe = @"
        SELECT m.ticker,
               m.sector,
               (SELECT b.close FROM bar b WHERE b.ticker = m.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               (SELECT l.trend_state FROM ladder l WHERE l.ticker = m.ticker AND l.as_of <= $on
                ORDER BY l.as_of DESC LIMIT 1),
               (SELECT i.value FROM indicator i WHERE i.ticker = m.ticker AND i.name = $typical
                    AND i.session_date <= $on
                ORDER BY i.session_date DESC LIMIT 1),
               m.name,
               m.industry,
               " + ResearchedOn + @"
        FROM membership m
        WHERE " + CurrentMember + @"
        ORDER BY m.ticker;
    ";

    // The bands the universe screen states, which are the immediate ones the
    // level builder already marked, at each name's latest as-of date at or
    // before the night asked for, for the reason the level query binds it: a
    // screen holding two nights of bands is a screen holding two charts.
    //
    // Every marked band of every name in one read, with the nearest on each side
    // chosen from them as prices. The builder marks one band a side, so the
    // choice stands on a set of one until a night writes two, which is when a
    // choice made on the text would begin answering with the wrong band.
    // see: A stored price is chosen and ordered by its value and never by the text it is stored as
    const string ImmediateBands = @"
        SELECT v.ticker, v.role, v.low_edge, v.high_edge
        FROM level v
        WHERE v.immediate = 1
              AND v.as_of = (SELECT MAX(a.as_of) FROM level a WHERE a.ticker = v.ticker AND a.as_of <= $on);
    ";

    // The current members of the index on a session, one row a ticker, for the reason the
    // universe query states.
    const string CurrentMember = @"
          m.index_code = $index_code
          AND (m.joined IS NULL OR m.joined <= $session)
          AND (m.""left"" IS NULL OR m.""left"" > $session)
          AND m.rowid = (
              SELECT o.rowid FROM membership o
              WHERE o.index_code = m.index_code
                AND o.ticker = m.ticker
                AND (o.joined IS NULL OR o.joined <= $session)
                AND (o.""left"" IS NULL OR o.""left"" > $session)
              ORDER BY o.observed_at DESC, o.rowid DESC
              LIMIT 1)";

    // The day a member's newest researched section was written. A researched section is an
    // accepted one a research pass wrote, and a stored key under each figure is not one: it
    // was written for every name each night, so counting it would call most of the index
    // researched.
    // see: A researched name is one holding an accepted section besides the key under each figure
    const string ResearchedOn = @"
               (SELECT MAX(r.as_of) FROM research_section r
                WHERE r.ticker = m.ticker AND r.status = 'accepted' AND r.section <> $retired)";

    // Every current member with its company's name and the day its newest researched
    // section was written, which is what the masthead's search offers.
    const string Findable = @"
        SELECT m.ticker, m.name, " + ResearchedOn + @"
        FROM membership m
        WHERE " + CurrentMember + @"
        ORDER BY m.ticker;
    ";

    // Every name holding a researched section, newest first, with the name and sector its
    // newest membership row carries and how many sections it holds.
    // see: A researched name is one holding an accepted section besides the key under each figure
    const string Researched = @"
        SELECT r.ticker,
               (SELECT m.name FROM membership m WHERE m.ticker = r.ticker
                ORDER BY m.observed_at DESC, m.rowid DESC LIMIT 1),
               (SELECT m.sector FROM membership m WHERE m.ticker = r.ticker
                ORDER BY m.observed_at DESC, m.rowid DESC LIMIT 1),
               MAX(r.as_of),
               COUNT(DISTINCT r.section)
        FROM research_section r
        WHERE r.status = 'accepted' AND r.section <> $retired
        GROUP BY r.ticker
        ORDER BY MAX(r.as_of) DESC, r.ticker;
    ";

    // Every stage of every run that started inside a window of UTC days, which
    // is what the run page's operational header draws.
    //
    // The window is wide and the night is decided afterwards, by the clock. The
    // log has no session column, and it cannot have one it would agree with:
    // the run starts after the close in New York, so the UTC date it carries is
    // the session's on some evenings and the next day's on others, and which it
    // is depends on the offset that evening. The clock is the one thing allowed
    // to answer that question, and it answers it here rather than in SQL.
    // see: Nothing is written against one operating system
    // Where a pass a page started stands, which is every row of its run in the order they
    // were written.
    //
    // A pass writes a row per component as it goes and its own row last, so these rows are
    // what a page watching a pass reads. The run is the newest of this name's that started
    // at or after the instant the page was handed when it pressed: an earlier pass is a
    // different pass, and before the first row lands there is no run and the page says the
    // pass is starting.
    // see: A pass the page starts is watched until it ends and the page redraws as each section lands
    const string PassRowsForName = @"
        SELECT stage, outcome, started_at, IFNULL(ended_at, '')
        FROM run_log
        WHERE run_id = (
            SELECT run_id FROM run_log
            WHERE run_id LIKE $like AND started_at >= $since
            ORDER BY started_at DESC, rowid DESC
            LIMIT 1)
        ORDER BY rowid;
    ";

    const string RunLogInWindow = @"
        SELECT run_id, stage, started_at, ended_at, outcome,
               rows_written, model_calls, network_requests, spend, detail
        FROM run_log
        WHERE started_at >= $from AND started_at < $to
        ORDER BY started_at, stage;
    ";

    // Every document refused by admissibility inside a window of UTC days, which
    // is what the run page's stale-and-failed region draws.
    //
    // The window and then the clock, exactly as the run log's own read works and
    // for the same reason: this table has no session column either, a pass runs
    // when a name is opened, and which session an instant belongs to is a
    // question only the clock may answer.
    //
    // The body is not selected. A refused row carries none, and a query that
    // asked for it would read as a page that could show one.
    const string RefusedDocumentsInWindow = @"
        SELECT id, url, title, published_on, fetched_at, admissibility
        FROM source_document
        WHERE admissibility != $accepted
          AND fetched_at >= $from AND fetched_at < $to
        ORDER BY fetched_at DESC, id;
    ";

    // Every filled forward return, which is what the run page's reason records
    // count over and where the base rate is read from.
    const string ForwardReturns = @"
        SELECT ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even
        FROM forward_return
        ORDER BY session_date, ticker, horizon;
    ";

    // One name's forward returns, newest listing first, which its listing history reads.
    const string ForwardReturnsForName = @"
        SELECT ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even
        FROM forward_return
        WHERE ticker = $ticker
        ORDER BY session_date DESC, horizon;
    ";

    // The candidate register, for the run page's count and its divisor. The
    // columns the region draws from and no others.
    const string RegisteredCandidates = @"
        SELECT id, candidate, evaluator, event, retires, registered_at, parameters, evidence, evaluator_version
        FROM candidate_register
        ORDER BY id;
    ";

    // Every replay of a family rule, oldest first, which where each rule's record counts from is read off.
    const string FamilyReplays = "SELECT outcome, detail, started_at FROM run_log WHERE stage LIKE $stages ORDER BY rowid;";

    // Every name-night a candidate fired on, with what the setup listed that night came to, the
    // bar its own plan set and the bar the calibration set for it.
    //
    // The fired ones alone. A candidate's record is over the setups it produced, and a name-night
    // it did not fire on produced none; the quiet rows are what the base rate is over and are read
    // by the query that reads them. With each, the name, the session its setup resolved on and
    // whether an outcome row is stored, which the open trade walk reads and nothing hands on.
    // see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
    const string CandidateSetups = @"
        SELECT json_extract(c.value, '$.candidate'), l.session_date, f.outcome,
               f.null_win, f.null_win_at_sensitivity, f.break_even, f.return_pct, f.planned_risk, f.on_earnings,
               l.ticker, f.resolved_on, f.ticker IS NOT NULL, NULL, NULL, NULL
        FROM listing l, json_each(COALESCE(l.shadow_reasons, '{}'), '$.candidates') c
        LEFT JOIN forward_return f
            ON f.ticker = l.ticker AND f.session_date = l.session_date AND f.horizon = $horizon
        WHERE json_extract(c.value, '$.fired') = 1
        UNION ALL
        SELECT json_extract(c.value, '$.candidate'), g.session_date, f.outcome,
               f.null_win, f.null_win_at_sensitivity, f.break_even, f.return_pct, f.planned_risk, f.on_earnings,
               g.ticker, f.resolved_on, f.ticker IS NOT NULL,
               CASE json_extract(c.value, '$.values.plan')
                   WHEN $clearPlan THEN g.clear_reward_to_risk
                   WHEN $ladderPlan THEN g.ladder_reward_to_risk
                   ELSE g.swing_reward_to_risk
               END,
               g.strength, g.band_strength
        FROM gate_result g, json_each(COALESCE(g.shadow, '{}'), '$.candidates') c
        LEFT JOIN forward_return f
            ON f.ticker = g.ticker AND f.session_date = g.session_date
            AND f.horizon = CASE json_extract(c.value, '$.values.plan') WHEN $clearPlan THEN $clear ELSE $swing END
        WHERE json_extract(c.value, '$.fired') = 1
        ORDER BY 1, 2;
    ";

    // Which candidates each night evaluated, evaluated or skipped, which is the set standing when
    // that night started. A candidate's first such night is the night its window opened, and the
    // candidates that night held are the family its level is divided by.
    const string CandidateNights = @"
        SELECT DISTINCT l.session_date, json_extract(c.value, '$.candidate')
        FROM listing l, json_each(COALESCE(l.shadow_reasons, '{}'), '$.candidates') c
        UNION
        SELECT DISTINCT l.session_date, json_extract(s.value, '$.candidate')
        FROM listing l, json_each(COALESCE(l.shadow_reasons, '{}'), '$.skipped') s
        UNION
        SELECT DISTINCT g.session_date, json_extract(c.value, '$.candidate')
        FROM gate_result g, json_each(COALESCE(g.shadow, '{}'), '$.candidates') c
        UNION
        SELECT DISTINCT g.session_date, json_extract(s.value, '$.candidate')
        FROM gate_result g, json_each(COALESCE(g.shadow, '{}'), '$.skipped') s
        ORDER BY 1, 2;
    ";

    const string OpenVersions = @"
        SELECT version, parameters, opened_at
        FROM rule_version
        WHERE rule = $rule AND closed_at IS NULL
        ORDER BY opened_at, version;
    ";

    // What each version labelled the night's names, beside the label the night stored for the same
    // name, so the count of names a version moves is read here rather than computed on a page.
    //
    // Grouped by the window and not by the version's name, because a name closed and opened again
    // carries rows of both windows and a session scored under each would otherwise be counted twice.
    //
    // Every score, whatever its sample flag. This is a description of tonight rather than evidence:
    // on the night a window opens every score under it is in sample, and a region filtered on the
    // flag would draw nothing on the one night it is first read. The record is where the flag
    // decides, and it is applied there.
    // see: A version's record belongs to the window its scores were written under and never to the version's name
    const string VersionLabels = @"
        SELECT v.version, v.opened_at, json_extract(v.plan, '$.trend'), COUNT(*),
               SUM(CASE WHEN json_extract(v.plan, '$.trend') <> d.trend_state THEN 1 ELSE 0 END)
        FROM version_score v
        JOIN ladder d ON d.ticker = v.ticker AND d.as_of = v.session_date
        WHERE v.rule = $rule AND v.session_date = $session
        GROUP BY v.version, v.opened_at, json_extract(v.plan, '$.trend')
        ORDER BY v.version, v.opened_at, json_extract(v.plan, '$.trend');
    ";

    const string LiveLabels = @"
        SELECT trend_state, COUNT(*) FROM ladder WHERE as_of = $session GROUP BY trend_state ORDER BY trend_state;
    ";

    const string NightsOfLabels = "SELECT COUNT(DISTINCT as_of) FROM ladder;";

    // Every night-to-night pair of one name's stored labels, the ones where the label changed, and
    // the ones where the label before it came back the next night or the night after.
    //
    // Read over the stored labels and never over a version's, because what the confirmation
    // version's own number is settled from is how often the night's own rule changed its mind.
    // owes: The trend confirmation's nights settled from flip-backs
    const string LabelFlips = @"
        WITH labelled AS (
            SELECT trend_state,
                   LAG(trend_state, 1) OVER name AS before,
                   LEAD(trend_state, 1) OVER name AS next,
                   LEAD(trend_state, 2) OVER name AS after
            FROM ladder
            WINDOW name AS (PARTITION BY ticker ORDER BY as_of)
        )
        SELECT SUM(CASE WHEN before IS NOT NULL THEN 1 ELSE 0 END),
               SUM(CASE WHEN before IS NOT NULL AND trend_state <> before THEN 1 ELSE 0 END),
               SUM(CASE WHEN before IS NOT NULL AND trend_state <> before AND next = before THEN 1 ELSE 0 END),
               SUM(CASE WHEN before IS NOT NULL AND trend_state <> before AND (next = before OR after = before) THEN 1 ELSE 0 END)
        FROM labelled;
    ";

    // The blocks of every window of a rule, as the scorer froze each when it completed.
    //
    // No join to `version_score` and none to `ladder`. Both are dropped one year back from the
    // newest stored session while a record is read at 8 blocks and again at 16, about four years
    // of nights, so a record computed from them could never hold more than three whole blocks
    // against a floor of eight and its verdict would be withheld for as long as the window stayed
    // open. What the reader reads is the frozen row, which the retention does not reach.
    // see: A version's record is read from the blocks frozen as each completed
    const string VersionBlocks = @"
        SELECT version, opened_at, block,
               version_excess, version_setups, live_excess, live_setups,
               version_null_sum, version_null_spread, live_null_sum, live_null_spread
        FROM version_block
        WHERE rule = $rule
        ORDER BY version, opened_at, block;
    ";

    // The current members whose stored series does not end on the newest session
    // anyone has, which is the stale region's population. The same query the
    // night's closing stage counts over, returning the names rather than the
    // count, because a page that says four names are stale and does not say
    // which is a page nobody can act on.
    //
    // Both this and the universe read the index on the session the page is read
    // in, being joined by it where the join date is known and not left by it.
    // `left IS NULL` until the phase 5 sign-off, which read an announced change
    // as effective the night it was announced.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string StaleNames = @"
        SELECT DISTINCT m.ticker
        FROM membership m
        WHERE m.index_code = $index
          AND (m.joined IS NULL OR m.joined <= $session)
          AND (m.""left"" IS NULL OR m.""left"" > $session)
          AND IFNULL((SELECT MAX(b.session_date) FROM bar b WHERE b.ticker = m.ticker), '')
              < (SELECT MAX(session_date) FROM bar)
        ORDER BY m.ticker;
    ";

    // One row per process start, stage read-api, which is the grain SCHEMA
    // declares for the run log: one row per run per stage. A row per served
    // request would break that key and would grow the operational record by
    // something that is not an operation.
    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $started_at, 'started',
            0, 0, 0, '0', $detail);
    ";

    // The store's schema and this checkout's, where the store is behind, and null where it is not.
    public Task<(int Store, int Checkout)?> SchemaBehindAsync()
    {
        using var connection = Open();

        var at = EquityBrief.Data.Migrations.MigrationRunner.AppliedVersion(connection);

        return Task.FromResult<(int Store, int Checkout)?>(
            at < EquityBrief.Data.Migrations.SchemaMigrations.LatestVersion ? (at, EquityBrief.Data.Migrations.SchemaMigrations.LatestVersion) : null);
    }

    SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();

        return connection;
    }

    public async Task<IReadOnlyList<BarRow>> BarsAsync(string ticker, DateOnly from, DateOnly to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = BarsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var bars = new List<BarRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bars.Add(new BarRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                Money.FromStorage(reader.GetString(5)),
                reader.GetInt64(6)));
        }

        return bars;
    }

    public async Task<IReadOnlyList<IndicatorRow>> IndicatorsAsync(string ticker, DateOnly from, DateOnly to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<IndicatorRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new IndicatorRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                await reader.IsDBNullAsync(3) ? null : reader.GetDouble(3),
                reader.GetInt32(4)));
        }

        return rows;
    }

    // A name's chart averages over the sessions its indicator rows leave empty, each with the pull the sessions before
    // its year were read from, or why none was.
    // see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step only the chart reads
    const string ChartAveragesOf = "SELECT name, night, sessions, pull, reason FROM chart_average WHERE ticker = $ticker ORDER BY name;";

    public async Task<IReadOnlyList<ChartAverageRow>> ChartAveragesAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ChartAveragesOf;
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<ChartAverageRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            using var sessions = System.Text.Json.JsonDocument.Parse(reader.GetString(2));

            rows.Add(new ChartAverageRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                [.. sessions.RootElement.EnumerateArray().Select(pair => (DateOnly.ParseExact(pair[0].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), pair[1].GetDouble()))],
                await reader.IsDBNullAsync(3) ? null : reader.GetString(3),
                await reader.IsDBNullAsync(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<LevelRow>> LevelsAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LevelsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", On(asOf));

        var rows = new List<LevelRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LevelRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt32(5) == 1,
                reader.GetInt32(6),
                reader.GetInt32(7) == 1,
                reader.GetString(8)));
        }

        return [.. rows.OrderBy(row => row.LowEdge)];
    }

    public async Task<IReadOnlyList<ProfileRow>> ProfileAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ProfileForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", On(asOf));

        var rows = new List<ProfileRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ProfileRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                reader.GetInt64(4),
                reader.GetDouble(5)));
        }

        return [.. rows.OrderBy(row => row.BandLow)];
    }

    // Every stage of the runs that belong to one night, in the order they ran.
    //
    // The night is decided by the clock over each row's own start, for the
    // reason the query states: a run that starts at half past eight in New York
    // carries tomorrow's UTC date and belongs to tonight. The window handed to
    // SQL is a day either side, so the filter has something to filter and the
    // whole log is not read to draw one evening.
    // Every row of the pass a page started, in the order the pass wrote them.
    public async Task<IReadOnlyList<PassStageRow>> PassRowsAsync(string ticker, DateTimeOffset since)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = PassRowsForName;
        command.Parameters.AddWithValue("$like", PassRun.Like(ticker));
        command.Parameters.AddWithValue("$since", since.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        var rows = new List<PassStageRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new PassStageRow(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                reader.GetString(3).Length > 0));
        }

        return rows;
    }

    public async Task<IReadOnlyList<RunStageRow>> RunLogAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RunLogInWindow;
        command.Parameters.AddWithValue("$from", night.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", night.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<RunStageRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var started = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);

            if (clock.SessionDateAt(started) != night)
            {
                continue;
            }

            rows.Add(new RunStageRow(
                reader.GetString(0),
                reader.GetString(1),
                started,
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9)));
        }

        return rows;
    }

    // The documents one night's passes refused, newest first.
    //
    // A pass is on demand rather than nightly, so what this bounds is the day the
    // page is showing: the refusals of the evening a reader is looking at, which
    // is the same period every other region of that page is about. The night is
    // decided by the clock over each row's own fetch instant, as the run log's is.
    public async Task<IReadOnlyList<RefusedDocumentRow>> RefusedDocumentsAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RefusedDocumentsInWindow;
        command.Parameters.AddWithValue("$accepted", Admissibility.Accepted);
        command.Parameters.AddWithValue("$from", night.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", night.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<RefusedDocumentRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var fetched = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);

            if (clock.SessionDateAt(fetched) != night)
            {
                continue;
            }

            rows.Add(new RefusedDocumentRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3)
                    ? null
                    : DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                fetched,
                reader.GetString(5)));
        }

        return rows;
    }

    // How long the night took, read from the run log rather than measured here.
    // A night the log does not carry says so rather than showing nothing, which
    // is the same rule the fact strip follows for a date not on file.
    //
    // The night is what selects the rows, which it did not until 5.6. The query
    // took every run carrying a listings stage and spanned the lot, so the date
    // in the parameter changed nothing and two stored nights reported one
    // duration covering both. The span is over the run that wrote that night's
    // list, so a re-run of an earlier evening is its own duration rather than a
    // widening of tonight's.
    //
    // One run and not every run that reached the list. Until the phase 5
    // sign-off a night that wrote its list and stopped at the next step, and
    // then was run again, spanned both runs: the two by-hand runs of
    // 2026-09-10 each wrote a list, and the header measured from the first's
    // start to the second's end. The run is the one whose list the store holds,
    // which is the last to write a listings row for the night. Last by the
    // order the rows were written and not by the instant they carry, for the
    // reason `NewestRun` states: a night run again for a named session stamps
    // its stages from 21:10Z on that session, earlier than the run it follows.
    //
    // A listings row is proof of a list only where the stage wrote it. A step
    // that fails, passes the deadline or is stopped before it starts records a
    // stop under the stage's name, and its list, if it began one, rolled back,
    // so the store holds the list of the run before it. The stage's own row
    // carries one of the outcomes it writes and is written in the transaction
    // that writes its list, so it stands exactly where the list committed; a
    // stop carries one of the night's stop outcomes. Where no run wrote a list
    // for the night, the span is the last run to reach the stage.
    public async Task<string?> NightDurationAsync(DateOnly night)
    {
        var rows = await RunLogAsync(night);
        var run = await LastListingsRunAsync(night);

        // The arithmetic's span, which the wall clock row bounds. Every step after the close sits
        // under the same run, each under a limit of its own, and the night's copy of the store waits
        // for the labeller it started, so a span over them would read their hours against a limit of
        // minutes.
        // see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own
        var afterTheClose = RunScreen.StepGroups[^1].Stages;
        var stages = rows.Where(row => row.RunId == run && !afterTheClose.Contains(row.Stage)).ToArray();

        if (stages.Length == 0)
        {
            return null;
        }

        var started = stages.Min(stage => stage.StartedAt);
        var ended = stages.Max(stage => stage.EndedAt);

        return (ended - started).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    // The run that wrote a night's list last, and where none did the run that wrote its
    // listings row last, the night decided by the clock over each row's own start as
    // `RunLogAsync` decides it.
    async Task<string?> LastListingsRunAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ListingsRunsInWindow;
        command.Parameters.AddWithValue("$ok", ListingsStageOutcomes[0]);
        command.Parameters.AddWithValue("$shadow_fault", ListingsStageOutcomes[1]);
        command.Parameters.AddWithValue("$from", night.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", night.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (clock.SessionDateAt(DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture)) == night)
            {
                return reader.GetString(0);
            }
        }

        return null;
    }

    // The outcomes the listings stage writes on its own row: its list written, and its list
    // written with a registered candidate unevaluated in shadow. Stated here because the read
    // surface holds no reference to the worker; `read-surface` asserts they are the stage's
    // own and that no stop writes either.
    public static IReadOnlyList<string> ListingsStageOutcomes { get; } = ["ok", "ok, with a registered candidate's evaluator missing or moved"];

    const string ListingsRunsInWindow = @"
        SELECT run_id, started_at
        FROM run_log
        WHERE stage = 'listings' AND started_at >= $from AND started_at < $to
        ORDER BY outcome IN ($ok, $shadow_fault) DESC, rowid DESC;
    ";

    public async Task<IReadOnlyList<ForwardReturnRow>> ForwardReturnsAsync(string? ticker = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ticker is null ? ForwardReturns : ForwardReturnsForName;

        if (ticker is not null)
        {
            command.Parameters.AddWithValue("$ticker", ticker);
        }

        var rows = new List<ForwardReturnRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ForwardReturnRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4)
                    ? null
                    : DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7)));
        }

        return rows;
    }

    // `night` is the night the page shows. The index is read on that night and not
    // on today's: until the phase 5 sign-off both reads below bound membership to
    // the day the page was opened, so every page about a past night read today's
    // index, and a page opened on 2026-09-21 before that night ran would have
    // dropped the three leavers from 2026-09-18's list and drawn the first-ranked
    // one's plan at a close of zero. Only a caller with no night falls back to
    // today's session.
    public async Task<IReadOnlyList<string>> StaleNamesAsync(string indexCode, DateOnly? night = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = StaleNames;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", OnNight(night));

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    // The night the run page shows when none is asked for: the one the newest
    // run belongs to, whether or not it wrote a list.
    //
    // Until the phase 5 sign-off the page opened on the newest night the
    // listings hold, and then kept only that night's rows, so a night that
    // stopped before its list, at the fetch or anywhere before the listings step, was
    // absent from the page a person opens: the reviewer stopped a night at the
    // fetch on 2026-09-09 and the default page showed 2026-09-08 saying no stage
    // of this night failed. The night is decided by the clock over the run's
    // own start, for the reason `RunLogAsync` states. A store whose log holds no
    // night falls back to the listings, which is what a store written before
    // the log existed has.
    public async Task<DateOnly?> RunNightAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestRun;
        command.Parameters.AddWithValue("$read_api", Stage);
        command.Parameters.AddWithValue("$no_session", RunScreen.NoSession);

        for (var at = 0; at < RunScreen.RunsByHand.Count; at++)
        {
            command.Parameters.AddWithValue(FormattableString.Invariant($"$by_hand_{at}"), RunScreen.RunsByHand[at] + "*");
        }

        return await command.ExecuteScalarAsync() is string started
            ? clock.SessionDateAt(DateTimeOffset.Parse(started, CultureInfo.InvariantCulture))
            : await NewestNightAsync();
    }

    // The newest night the listings hold, or the newest on or before a date, which is the
    // evening a page asked for an earlier one draws: a date the exchange did not trade on,
    // or one a night never ran for, answers with the evening before it rather than with
    // nothing, and the page says which evening it drew.
    // see: A name's page for an earlier night draws what the store held that night and nothing it learned after
    public async Task<DateOnly?> NewestNightAsync(DateOnly? onOrBefore = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestNight;
        command.Parameters.AddWithValue("$on", On(onOrBefore));

        return await command.ExecuteScalarAsync() is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    // Every night the listings hold, oldest first, which is what a dated screen's calendar offers.
    public async Task<IReadOnlyList<DateOnly>> NightsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = HeldNights;

        var nights = new List<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            nights.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return nights;
    }

    public async Task<IReadOnlyList<ListingRow>> ListingsAsync(DateOnly sessionDate)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ListingsForNight;
        command.Parameters.AddWithValue("$session_date", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await ListingsAsync(command);
    }

    public async Task<IReadOnlyList<ListingRow>> ListingsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = EveryListing;

        return await ListingsAsync(command);
    }

    public async Task<IReadOnlyList<ListingRow>> ListingsAsync(string ticker, int sessions, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ListingsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$sessions", sessions);
        command.Parameters.AddWithValue("$on", On(asOf));

        return await ListingsAsync(command);
    }

    static async Task<IReadOnlyList<ListingRow>> ListingsAsync(SqliteCommand command)
    {
        var rows = new List<ListingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ListingRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetInt64(6) == 1,
                reader.GetString(7)));
        }

        return rows;
    }

    // The rule the evening's list was drawn by, the reasons where the store records none.
    public async Task<IReadOnlyList<EquityBrief.Core.Filter.NearMissRow>> NearMissRowsAsync(string version)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NearMissRows;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$swing", EquityBrief.Core.Returns.ForwardReturnSeries.Swing);
        command.Parameters.AddWithValue("$clear", EquityBrief.Core.Returns.ForwardReturnSeries.Clear);
        command.Parameters.AddWithValue("$clearPlan", EquityBrief.Core.Filter.FilterSettings.ClearWord);

        var rows = new List<EquityBrief.Core.Filter.NearMissRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var session = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            rows.Add(new EquityBrief.Core.Filter.NearMissRow(
                session,
                reader.GetInt64(1) == 1,
                reader.GetInt64(2) == 1,
                reader.GetInt64(3) == 1,
                reader.GetInt64(4) == 1,
                reader.GetInt64(5) == 1,
                JsonSerializer.Deserialize<string[]>(reader.GetString(6)) ?? [],
                reader.GetInt64(7) == 1,
                reader.IsDBNull(8)
                    ? null
                    : new EquityBrief.Core.Candidates.CandidateSetup(
                        session,
                        reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetDouble(9),
                        reader.IsDBNull(10) ? null : reader.GetDouble(10),
                        reader.IsDBNull(11) ? null : reader.GetDouble(11),
                        reader.IsDBNull(12) ? null : reader.GetDouble(12),
                        reader.IsDBNull(13) ? null : reader.GetDouble(13),
                        !reader.IsDBNull(14) && reader.GetInt64(14) == 1)));
        }

        return rows;
    }

    // The most names the watch list holds.
    public const int WatchLimit = 20;

    // The names the operator watches, oldest first.
    // see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
    public async Task<IReadOnlyList<WatchedRow>> WatchedAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Watched;

        var rows = new List<WatchedRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new WatchedRow(
                reader.GetString(0),
                DateOnly.FromDateTime(DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture).UtcDateTime)));
        }

        return rows;
    }

    // A name put on the watch list by the operator's press: a current member of the index, not already on
    // it, while the list holds fewer than its limit, and otherwise nothing written and the line says why.
    // see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
    public async Task<RequestWritten> WatchAsync(string ticker, IReadOnlySet<string> members)
    {
        var asked = ticker.Trim().ToUpperInvariant();

        if (!members.Contains(asked))
        {
            return new RequestWritten(false, $"{asked} was not added: it is not a member of the index tonight.");
        }

        await using var connection = Open();
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync();
        await using var counting = connection.CreateCommand();

        counting.Transaction = transaction;
        counting.CommandText = WatchedCount;

        if (Convert.ToInt32(await counting.ExecuteScalarAsync(), CultureInfo.InvariantCulture) >= WatchLimit)
        {
            return new RequestWritten(false, $"{asked} was not added: the watch list holds {WatchLimit}, its limit, so take one out first.");
        }

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = WatchName;
        command.Parameters.AddWithValue("$ticker", asked);
        command.Parameters.AddWithValue("$added_at", clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));

        var written = await command.ExecuteNonQueryAsync() == 1;

        await transaction.CommitAsync();

        return written
            ? new RequestWritten(true, $"{asked} is on the watch list.")
            : new RequestWritten(false, $"{asked} was already on the watch list.");
    }

    // A name taken off the watch list by the operator's press.
    // see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
    public async Task<RequestWritten> UnwatchAsync(string ticker)
    {
        var asked = ticker.Trim().ToUpperInvariant();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = UnwatchName;
        command.Parameters.AddWithValue("$ticker", asked);

        return await command.ExecuteNonQueryAsync() == 1
            ? new RequestWritten(true, $"{asked} is off the watch list.")
            : new RequestWritten(false, $"{asked} was not on the watch list.");
    }

    // The first night the swing filter listed, from which the dated screens open, or none on a store it
    // has never listed.
    // see: The dated screens open from the swing filter's first night, and no evening before it is drawn
    public async Task<DateOnly?> FirstFilterNightAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FirstFilterNight;
        command.Parameters.AddWithValue("$filter", EquityBrief.Core.Shortlist.ListRules.Filter);

        return await command.ExecuteScalarAsync() is string first
            ? DateOnly.ParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    public async Task<string> ListRuleAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ListRuleOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync() as string ?? EquityBrief.Core.Shortlist.ListRules.Reasons;
    }

    public async Task<IReadOnlyList<MoveRow>> MovesAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = MovesForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", On(asOf));

        var rows = new List<MoveRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new MoveRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(2),
                reader.GetDouble(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9)));
        }

        return rows;
    }

    // A name's earnings reaction record, oldest print first, as of the night the page shows.
    // A name's stored articles of the thirty days before a night, newest first, each with the newest label
    // written for it under any profile, which is the one the page draws.
    // see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
    const string NewsForName = @"
        SELECT a.article_id, a.title, a.source, a.published_at, a.link, a.admissibility,
               l.outcome, l.cause, l.kind, l.direction, l.reason, l.model, l.profile
        FROM news_article a
        LEFT JOIN news_label l ON l.rowid = (
            SELECT n.rowid FROM news_label n
            WHERE n.ticker = a.ticker AND n.article_id = a.article_id
            ORDER BY n.labelled_at DESC, n.rowid DESC LIMIT 1)
        WHERE a.ticker = $ticker AND a.published_at >= $from AND a.published_at < $to
        ORDER BY a.published_at DESC, a.article_id;
    ";

    // Every name's positive and negative stories of the thirty days before a night, counted as the name page's
    // bar counts them: the newest label of each article, labelled, opinion pieces left out.
    const string NewsCountsOn = @"
        SELECT a.ticker,
               SUM(CASE WHEN l.direction = 'positive' THEN 1 ELSE 0 END),
               SUM(CASE WHEN l.direction = 'negative' THEN 1 ELSE 0 END)
        FROM news_article a
        JOIN news_label l ON l.rowid = (
            SELECT n.rowid FROM news_label n
            WHERE n.ticker = a.ticker AND n.article_id = a.article_id
            ORDER BY n.labelled_at DESC, n.rowid DESC LIMIT 1)
        WHERE a.published_at >= $from AND a.published_at < $to AND l.outcome = $labelled AND l.kind <> $opinion
        GROUP BY a.ticker;
    ";

    // The store's copies, each attempt's own row, newest first.
    // The copies' own rows, each copy's start and its end, newest first.
    const string StoreBackupRows = @"
        SELECT run_id, outcome, started_at, ended_at, detail FROM run_log
        WHERE stage IN ($stage, $start)
        ORDER BY rowid DESC
        LIMIT 40;
    ";

    // The news labeller's own row of each of its runs, newest first; the night each labelled is in its detail.
    const string LabellerRunRows = @"
        SELECT run_id, outcome, started_at, ended_at, detail FROM run_log
        WHERE stage = $stage AND run_id LIKE $prefix
        ORDER BY rowid DESC;
    ";

    public async Task<IReadOnlyList<NewsArticleRow>> NewsAsync(string ticker, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", NewsLabelling.WindowFrom(night));
        command.Parameters.AddWithValue("$to", NewsLabelling.WindowTo(night));

        var rows = new List<NewsArticleRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new NewsArticleRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return rows;
    }

    public async Task<IReadOnlyDictionary<string, (int Positive, int Negative)>> NewsCountsAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewsCountsOn;
        command.Parameters.AddWithValue("$from", NewsLabelling.WindowFrom(night));
        command.Parameters.AddWithValue("$to", NewsLabelling.WindowTo(night));
        command.Parameters.AddWithValue("$labelled", NewsLabelling.Labelled);
        command.Parameters.AddWithValue("$opinion", NewsInstruction.Opinion);

        var counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = (reader.GetInt32(1), reader.GetInt32(2));
        }

        return counts;
    }

    public async Task<IReadOnlyList<LabellerRunRow>> LabellerRunsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LabellerRunRows;
        command.Parameters.AddWithValue("$stage", NewsLabelling.Stage);
        command.Parameters.AddWithValue("$prefix", NewsLabelling.RunPrefix + "%");

        var rows = new List<LabellerRunRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LabellerRunRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        return rows;
    }

    // The store's copies as their own rows record them, newest first.
    // see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
    public async Task<IReadOnlyList<StoreBackupRow>> StoreBackupsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = StoreBackupRows;
        command.Parameters.AddWithValue("$stage", EquityBrief.Core.Configuration.StoreCopies.Stage);
        command.Parameters.AddWithValue("$start", EquityBrief.Core.Configuration.StoreCopies.StartStage);

        var rows = new List<StoreBackupRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new StoreBackupRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ReactionRow>> ReactionsAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ReactionsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", On(asOf));

        var rows = new List<ReactionRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ReactionRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.GetDouble(7)));
        }

        return rows;
    }

    // The swing readings' columns, in the order every read of them takes them.
    const string SwingColumns = @"
        ticker, session_date, bars, return_short, return_long, place_short, place_long, strength,
        recent_high, high_session, pullback_sessions, depth, dry_up, tightness, note";

    // A name's swing readings for the night a page is drawn for, or its newest where the page is tonight's.
    const string SwingReadingOn = "SELECT " + SwingColumns + " FROM swing_reading WHERE ticker = $ticker AND session_date = $on;";

    const string NewestSwingReading = "SELECT " + SwingColumns + " FROM swing_reading WHERE ticker = $ticker ORDER BY session_date DESC LIMIT 1;";

    // Every member's swing readings for one night, which the universe table draws.
    const string SwingReadingsOn = "SELECT " + SwingColumns + " FROM swing_reading WHERE session_date = $on;";

    // The night's market reading, for that night and no other: a night that stored none says so.
    const string MarketReadingOn = @"
        SELECT session_date, members, counted, above, breadth, counted_context, above_context, breadth_context,
               volume_counted, median_volume_ratio
        FROM market_reading
        WHERE session_date = $on;
    ";

    // A name's swing readings for a night, or its newest where no night is named, and none where the
    // swing reader stored none.
    public async Task<SwingReadingRow?> SwingReadingAsync(string ticker, DateOnly? on = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = on is null ? NewestSwingReading : SwingReadingOn;
        command.Parameters.AddWithValue("$ticker", ticker);

        if (on is { } night)
        {
            command.Parameters.AddWithValue("$on", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? SwingRow(reader) : null;
    }

    // Every member's swing readings for one night.
    public async Task<IReadOnlyList<SwingReadingRow>> SwingReadingsAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SwingReadingsOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<SwingReadingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(SwingRow(reader));
        }

        return rows;
    }

    // The member readings' columns, in the order every read of them takes them.
    const string MemberColumns = @"
        index_code, ticker, session_date, close, dollar_volume, company_value, cost, cost_double, profit, coverage, state,
        year_high, nearness, since_high, volume_ratio, industry, industry_month, industry_quarter, peer_surprise";

    // A member's readings for the night a page is drawn for, or its newest where the page is tonight's. A member holds
    // one index on a session, so the index orders nothing but a row the store should not hold.
    const string MemberReadingOn = "SELECT " + MemberColumns + " FROM member_reading WHERE ticker = $ticker AND session_date = $on ORDER BY index_code LIMIT 1;";

    const string NewestMemberReading = "SELECT " + MemberColumns + " FROM member_reading WHERE ticker = $ticker ORDER BY session_date DESC, index_code LIMIT 1;";

    // A member's readings for a night, or its newest where no night is named, and none where the member reader stored
    // none for it.
    public async Task<MemberReadingRow?> MemberReadingAsync(string ticker, DateOnly? on = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = on is null ? NewestMemberReading : MemberReadingOn;
        command.Parameters.AddWithValue("$ticker", ticker);

        if (on is { } night)
        {
            command.Parameters.AddWithValue("$on", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        decimal? Price(int column) => reader.IsDBNull(column) ? null : Money.FromStorage(reader.GetString(column));

        double? Figure(int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);

        return new MemberReadingRow(
            reader.GetString(0),
            reader.GetString(1),
            DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Price(3),
            Price(4),
            Price(5),
            Figure(6),
            Figure(7),
            !reader.IsDBNull(8) && reader.GetInt64(8) == 1,
            reader.IsDBNull(9) ? null : reader.GetInt64(9) == 1,
            reader.IsDBNull(10) ? null : reader.GetString(10),
            Price(11),
            Figure(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            Figure(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            Figure(16),
            Figure(17),
            Figure(18));
    }

    // A company's newest fetch made on or before a night, with its five rating counts and their total, none where the
    // fetch filed none, and whether the fetch was made before the counts were stored: the counts came with the quarters'
    // interest expense, so a fetch whose quarters read no interest expense read no counts either.
    // see: Analyst coverage is stored from each fetch and waits for dated counts before any rule tests it
    const string RatingsOn = @"
        SELECT c.fetched_at, c.strong_buy, c.buy, c.hold, c.sell, c.strong_sell,
               CASE WHEN COALESCE(c.strong_buy, c.buy, c.hold, c.sell, c.strong_sell) IS NULL THEN NULL
                    ELSE IFNULL(c.strong_buy, 0) + IFNULL(c.buy, 0) + IFNULL(c.hold, 0) + IFNULL(c.sell, 0) + IFNULL(c.strong_sell, 0) END,
               EXISTS (SELECT 1 FROM reported_quarter q WHERE q.ticker = c.ticker AND q.fetched_at = c.fetched_at AND q.interest_read = 0)
        FROM company c
        WHERE c.ticker = $ticker AND substr(c.fetched_at, 1, 10) <= $on
        ORDER BY c.fetched_at DESC
        LIMIT 1;
    ";

    // A company's rating counts as its newest fetch on or before a night filed them, or its newest fetch's where no
    // night is named, and none where no fetch is stored.
    public async Task<RatingsRow?> RatingsAsync(string ticker, DateOnly? on = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RatingsOn;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", (on ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        int? Count(int column) => reader.IsDBNull(column) ? null : reader.GetInt32(column);

        return new RatingsRow(
            DateOnly.ParseExact(reader.GetString(0)[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Count(1),
            Count(2),
            Count(3),
            Count(4),
            Count(5),
            Count(6),
            reader.GetInt64(7) == 1);
    }

    static SwingReadingRow SwingRow(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetDouble(4),
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.IsDBNull(8) ? null : Money.FromStorage(reader.GetString(8)),
            reader.IsDBNull(9) ? null : DateOnly.ParseExact(reader.GetString(9), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetDouble(11),
            reader.IsDBNull(12) ? null : reader.GetDouble(12),
            reader.IsDBNull(13) ? null : reader.GetDouble(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));

    // Every night's swing filter results counted, the market gate held open.
    const string GateNights = @"
        SELECT session_date, MIN(version), COUNT(*),
               SUM(trend),
               SUM(trend * setup),
               SUM(trend * setup * trigger_pass),
               SUM(trend * setup * trigger_pass * trade),
               SUM(CASE WHEN trend = 1 AND setup = 1 AND trigger_pass = 1 AND trade = 1 AND exclusions = '[]' THEN 1 ELSE 0 END)
        FROM gate_result
        GROUP BY session_date
        ORDER BY session_date;
    ";

    const string MarketRatios = "SELECT session_date, median_volume_ratio FROM market_reading;";

    const string OpenFilterVersion = "SELECT version FROM filter_version WHERE closed_at IS NULL ORDER BY opened_at DESC LIMIT 1;";

    const string LatestShapeProposal = @"
        SELECT id, session_date, version, ordinary, levers, list_now, list_proposed, findings, decision, reason, opened
        FROM shape_proposal
        ORDER BY id DESC
        LIMIT 1;
    ";

    const string OpenTrendVersions = @"
        SELECT version, parameters, opened_at
        FROM rule_version
        WHERE rule = $rule AND closed_at IS NULL;
    ";

    const string VersionNights = @"
        SELECT COUNT(DISTINCT session_date)
        FROM version_score
        WHERE rule = $rule AND version = $version AND opened_at = $opened_at AND sample = 'scored';
    ";

    public async Task<IReadOnlyList<GateNightRow>> GateNightsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = GateNights;

        var rows = new List<GateNightRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new GateNightRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7)));
        }

        return rows;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double?>> MarketRatiosAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = MarketRatios;

        var ratios = new Dictionary<DateOnly, double?>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            ratios[DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture)] = reader.IsDBNull(1) ? null : reader.GetDouble(1);
        }

        return ratios;
    }

    // The newest shape proposal, or none where the proposer has written none.
    public async Task<ShapeProposalRow?> LatestShapeProposalAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LatestShapeProposal;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ShapeProposalRow(
            reader.GetInt64(0),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(2),
            reader.GetInt32(3),
            JsonSerializer.Deserialize<List<EquityBrief.Core.Filter.Lever>>(reader.GetString(4)) ?? [],
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : reader.GetDouble(6),
            JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? [],
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    // The open filter version's name, and none where none is open.
    public async Task<string?> OpenFilterVersionAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = OpenFilterVersion;

        return await command.ExecuteScalarAsync() as string;
    }

    public async Task<TriggerReads> TriggerReadsAsync()
    {
        await using var connection = Open();

        // The trend version whose new label has to hold more than one night, read off its stored parameters.
        (string Version, string OpenedAt)? confirmation = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = OpenTrendVersions;
            command.Parameters.AddWithValue("$rule", LadderRules.TrendRule);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                using var parameters = JsonDocument.Parse(reader.GetString(1));

                if (parameters.RootElement.TryGetProperty(EquityBrief.Core.Ladders.TrendSeries.NightsTheNewLabelHolds, out var nights)
                    && nights.ValueKind == JsonValueKind.Number
                    && nights.GetDouble() > 1)
                {
                    confirmation = (reader.GetString(0), reader.GetString(2));
                }
            }
        }

        int? confirmationNights = null;

        if (confirmation is { } version)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = VersionNights;
            command.Parameters.AddWithValue("$rule", LadderRules.TrendRule);
            command.Parameters.AddWithValue("$version", version.Version);
            command.Parameters.AddWithValue("$opened_at", version.OpenedAt);
            confirmationNights = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        // The S&P 400's and 600's members the newest night read, and how many of them hold four storing fetches that filed
        // the analysts' rating counts, each dated by its fetch.
        var (wider, rated) = (0, 0);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RatedFourTimes;

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                (wider, rated) = (reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetInt32(1));
            }
        }

        return new TriggerReads(confirmationNights, confirmation?.Version, wider, rated);
    }

    const string RatedFourTimes = @"
        SELECT COUNT(*),
               SUM(CASE WHEN (SELECT COUNT(*) FROM company c
                              WHERE c.ticker = r.ticker
                                AND (c.strong_buy IS NOT NULL OR c.buy IS NOT NULL OR c.hold IS NOT NULL OR c.sell IS NOT NULL OR c.strong_sell IS NOT NULL)) >= 4
                        THEN 1 ELSE 0 END)
        FROM member_reading r
        WHERE r.index_code IN ('MID', 'SML') AND r.session_date = (SELECT MAX(session_date) FROM member_reading);
    ";

    // The swing filter's columns, in the order every read of them takes them.
    const string GateColumns = @"
        ticker, session_date, version, market, trend, setup, family, trigger_pass, trigger_event, trade,
        ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, swing_reward_to_risk,
        swing_stop_moves, exclusions, passed, rank, strength, band_strength, gates,
        clear_stop, clear_target, clear_reward_to_risk, clear_stop_moves";

    const string GateResultOn = "SELECT " + GateColumns + " FROM gate_result WHERE version <> '" + EquityBrief.Core.Filter.ReplayedResults.Version + "' AND ticker = $ticker AND session_date = $on;";

    const string NewestGateResult = "SELECT " + GateColumns + " FROM gate_result WHERE version <> '" + EquityBrief.Core.Filter.ReplayedResults.Version + "' AND ticker = $ticker ORDER BY session_date DESC LIMIT 1;";

    // Every member's result for one night, which the run page's funnel counts.
    const string GateResultsOn = "SELECT " + GateColumns + " FROM gate_result WHERE version <> '" + EquityBrief.Core.Filter.ReplayedResults.Version + "' AND session_date = $on ORDER BY ticker;";

    // A name's swing filter result for a night, or its newest where no night is named, and none where
    // the filter stored none.
    public async Task<GateResultRow?> GateResultAsync(string ticker, DateOnly? on = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = on is null ? NewestGateResult : GateResultOn;
        command.Parameters.AddWithValue("$ticker", ticker);

        if (on is { } night)
        {
            command.Parameters.AddWithValue("$on", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? GateRow(reader) : null;
    }

    // Every member's swing filter result for one night.
    public async Task<IReadOnlyList<GateResultRow>> GateResultsAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = GateResultsOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<GateResultRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(GateRow(reader));
        }

        return rows;
    }

    // Every trade the live list recommended on a night up to the one given, newest first, or one name's
    // alone where a ticker is given.
    // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
    public async Task<IReadOnlyList<PickRow>> PicksAsync(DateOnly on, string? ticker = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Picks;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ticker", (object?)ticker ?? DBNull.Value);
        command.Parameters.AddWithValue("$filter", EquityBrief.Core.Shortlist.ListRules.Filter);
        command.Parameters.AddWithValue("$clearPlan", EquityBrief.Core.Filter.FilterSettings.ClearWord);
        command.Parameters.AddWithValue("$clear", EquityBrief.Core.Returns.ForwardReturnSeries.Clear);
        command.Parameters.AddWithValue("$swing", EquityBrief.Core.Returns.ForwardReturnSeries.Swing);
        command.Parameters.AddWithValue("$pullback", EquityBrief.Core.Families.SetupFamilies.Pullback);
        command.Parameters.AddWithValue("$listed", EquityBrief.Core.Families.FamilyList.Listed);

        var rows = new List<PickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

            DateOnly? Day(int at) => reader.IsDBNull(at) ? null : DateOnly.ParseExact(reader.GetString(at), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            rows.Add(new PickRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                Price(3),
                Price(4),
                Price(5),
                reader.GetInt32(6) == 1,
                reader.IsDBNull(7) ? null : reader.GetString(7),
                Day(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                Price(12),
                Day(13),
                Price(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.GetString(16),
                reader.IsDBNull(17) ? [] : System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(17)) ?? [],
                reader.IsDBNull(18) ? null : reader.GetDouble(18)));
        }

        return rows;
    }

    const string FamilyNightOn = "SELECT families FROM family_night WHERE session_date = $on;";

    // The families on the page on a night they drew its list, in the page's order, and nothing on a night
    // they did not draw, which is every night before them.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public async Task<IReadOnlyList<string>?> FamilyNightAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FamilyNightOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync() is string families
            ? System.Text.Json.JsonSerializer.Deserialize<string[]>(families) ?? []
            : null;
    }

    const string FamilyPicksOn = @"
        SELECT session_date, ticker, family, state, place, also, held_family, held_night, held_index
        FROM family_pick
        WHERE session_date = $on
        ORDER BY place IS NULL, place, family, ticker;
    ";

    // The page's list on a night the families drew it: every stock a family passed, listed or held back,
    // the listed ones first in the page's order. A night the families did not draw holds none.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public async Task<IReadOnlyList<FamilyPickRow>> FamilyPicksAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FamilyPicksOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<FamilyPickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new FamilyPickRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : DateOnly.ParseExact(reader.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    // ---- the decision cards ----
    // see: A pick's card advises on the trade and removes no pick, and code computes every figure on it

    const string CardColumns = "index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, sector, trail, cap, round_trip, book_holdings, hits, score_rank, similar, approved";

    const string DecisionCardsOn = "SELECT " + CardColumns + @" FROM decision_card
        WHERE index_code = $index AND session_date = $on
        ORDER BY family, place, ticker;";

    const string BoughtCardsTo = "SELECT " + CardColumns + @" FROM decision_card
        WHERE index_code = $index AND family = $family AND session_date <= $on
        ORDER BY session_date, ticker;";

    const string CardsOfName = "SELECT " + CardColumns + @" FROM decision_card
        WHERE ticker = $ticker AND session_date = $on
        ORDER BY index_code, family;";

    // Every card an index's night stored, the families' picks and its book's buys.
    public Task<IReadOnlyList<DecisionCardRow>> DecisionCardsAsync(string index, DateOnly on) =>
        CardsAsync(DecisionCardsOn, [("$index", index), ("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);

    // The cards a sector heavyweights' book drew for what it bought on each night through a night, which each holding's
    // row opens from the night it was bought.
    public Task<IReadOnlyList<DecisionCardRow>> BoughtCardsAsync(string index, DateOnly on) =>
        CardsAsync(BoughtCardsTo, [("$index", index), ("$family", EquityBrief.Core.Families.HeavyweightRule.Name), ("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);

    // A stock's cards on a night, under whichever family and index listed it.
    public Task<IReadOnlyList<DecisionCardRow>> DecisionCardsOfAsync(string ticker, DateOnly on) =>
        CardsAsync(CardsOfName, [("$ticker", ticker), ("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);

    async Task<IReadOnlyList<DecisionCardRow>> CardsAsync(string sql, IReadOnlyList<(string Name, string Value)> parameters)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<DecisionCardRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            decimal? Price(int column) =>
                reader.IsDBNull(column) ? null : decimal.Parse(reader.GetString(column), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

            rows.Add(new DecisionCardRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                Price(5),
                Price(6),
                Price(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                Price(13),
                reader.IsDBNull(14) ? null : reader.GetInt32(14),
                Price(15),
                reader.IsDBNull(16) ? null : reader.GetInt32(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetInt32(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20)));
        }

        return rows;
    }

    // ---- the operator's taken trades, written by a card's presses alone ----

    const string TakenColumns = "ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through, ended_on, end_price, end_reason, result";

    const string OpenTaken = "SELECT " + TakenColumns + " FROM taken_trade WHERE exit_date IS NULL AND ended_on IS NULL ORDER BY taken_at, ticker;";

    const string TakenOfStock = "SELECT " + TakenColumns + " FROM taken_trade WHERE ticker = $ticker ORDER BY taken_at;";

    const string EveryTaken = "SELECT " + TakenColumns + " FROM taken_trade ORDER BY taken_at, ticker;";

    const string CardTaken = "SELECT " + CardColumns + @" FROM decision_card
        WHERE index_code = $index AND session_date = $night AND family = $family AND ticker = $ticker;";

    const string OpenOfStock = "SELECT COUNT(*) FROM taken_trade WHERE ticker = $ticker AND exit_date IS NULL AND ended_on IS NULL;";

    const string InsertTaken = @"
        INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through)
        VALUES ($ticker, $taken_at, $index, $family, $night, $sector, $fill, $fill_date, $provisional, $entered, $stop, $target, $trail, $cap, NULL, NULL, NULL);
    ";

    // The operator's word on a loop proposal or a restore, the Loop page's presses' one write.
    // see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
    const string InsertDecision = @"
        INSERT INTO loop_decision (run_id, index_code, family, proposal, decision, reason, decided_at)
        VALUES ($run_id, $index, $family, $proposal, $decision, $reason, $decided_at);";

    const string DeleteUnfollowed = @"
        DELETE FROM taken_trade
        WHERE ticker = $ticker AND taken_at = $taken_at AND followed_through IS NULL AND exit_date IS NULL;
    ";

    const string RecordExit = @"
        UPDATE taken_trade SET exit_price = $exit_price, exit_date = $exit_date
        WHERE ticker = $ticker AND taken_at = $taken_at AND exit_date IS NULL;
    ";

    // Every trade the operator holds open, which a card's concentration line counts.
    public Task<IReadOnlyList<TakenTradeRow>> OpenTakenTradesAsync() => TakenAsync(OpenTaken, []);

    // A stock's taken trades, open and ended, which its cards draw.
    public Task<IReadOnlyList<TakenTradeRow>> TakenTradesOfAsync(string ticker) => TakenAsync(TakenOfStock, [("$ticker", ticker)]);

    // Every taken trade, which the cards a page draws read theirs from.
    public Task<IReadOnlyList<TakenTradeRow>> TakenTradesAsync() => TakenAsync(EveryTaken, []);

    const string TakenRecords = @"
        SELECT index_code, family, unit, won, lost, ended, open_trades, average,
               same_nights, rule_listed, rule_won, rule_lost, rule_ended, rule_average
        FROM taken_record ORDER BY index_code, family;
    ";

    // The operator's record of each family on each index, as the night's follower last wrote it.
    public async Task<IReadOnlyList<TakenRecordRow>> TakenRecordsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = TakenRecords;

        var rows = new List<TakenRecordRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new TakenRecordRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetDouble(13)));
        }

        return rows;
    }

    // A trade taken from a card the night stored, read from the store and never from the press: its fill the price the
    // operator entered or, where none was, the plan's buy marked provisional until the next session's open replaces it,
    // dated the session after the pick's night unless the operator entered another. Refused for a card the store does
    // not hold, a stock already holding an open taken trade, a fill at or under the stop and a card stating no buy.
    // see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
    public async Task<RequestWritten> TakeAsync(string index, DateOnly night, string family, string ticker, decimal? price, DateOnly? date)
    {
        var cards = await CardsAsync(CardTaken, [("$index", index), ("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$family", family), ("$ticker", ticker)]);

        if (cards.Count == 0)
        {
            return new RequestWritten(false, $"{ticker} was not taken: no card of the {family} on {index} for {night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is stored.");
        }

        var card = cards[0];

        if ((price ?? card.Entry) is not { } fill || fill <= 0m)
        {
            return new RequestWritten(false, $"{ticker} was not taken: its card states no buy, so enter the price you filled at.");
        }

        if (card.Stop is { } stop && fill <= stop)
        {
            return new RequestWritten(false, $"{ticker} was not taken: a fill of {fill.ToString(CultureInfo.InvariantCulture)} is at or under the stop of {stop.ToString(CultureInfo.InvariantCulture)}.");
        }

        await using var connection = Open();
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync();
        await using var counting = connection.CreateCommand();

        counting.Transaction = transaction;
        counting.CommandText = OpenOfStock;
        counting.Parameters.AddWithValue("$ticker", ticker);

        if (Convert.ToInt32(await counting.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
        {
            return new RequestWritten(false, $"{ticker} was not taken: you already hold an open trade in it, so record its exit first.");
        }

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = InsertTaken;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$taken_at", clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$index", card.Index);
        command.Parameters.AddWithValue("$family", card.Family);
        command.Parameters.AddWithValue("$night", card.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sector", (object?)card.Sector ?? DBNull.Value);
        command.Parameters.AddWithValue("$fill", fill.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$fill_date", (date ?? SessionAfter(card.Night)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$provisional", price is null ? 1 : 0);
        command.Parameters.AddWithValue("$entered", price is null ? 0 : 1);
        command.Parameters.AddWithValue("$stop", (object?)card.Stop?.ToString(CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$target", (object?)card.Target?.ToString(CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$trail", (object?)card.Trail?.ToString(CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$cap", (object?)card.Cap ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        return new RequestWritten(true, price is null
            ? $"{ticker} is taken at the plan's buy of {fill.ToString(CultureInfo.InvariantCulture)}, provisional until the next session's open is stored."
            : $"{ticker} is taken at {fill.ToString(CultureInfo.InvariantCulture)}.");
    }

    // "Not taken": a taken trade removed, only before its first night has followed it and while it holds no exit.
    public async Task<RequestWritten> NotTakenAsync(string ticker, string takenAt) =>
        await ExecuteAsync(DeleteUnfollowed, [("$ticker", ticker), ("$taken_at", takenAt)]) == 1
            ? new RequestWritten(true, $"{ticker} is no longer taken.")
            : new RequestWritten(false, $"{ticker} was not removed: a night has followed it or its exit is recorded, so record its exit instead.");

    // The operator's own exit of an open taken trade, its price and its date.
    public async Task<RequestWritten> ExitAsync(string ticker, string takenAt, decimal price, DateOnly date) =>
        price <= 0m
            ? new RequestWritten(false, $"{ticker}'s exit was not recorded: an exit price must be above nothing.")
            : await ExecuteAsync(RecordExit, [("$ticker", ticker), ("$taken_at", takenAt), ("$exit_price", price.ToString(CultureInfo.InvariantCulture)), ("$exit_date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]) == 1
                ? new RequestWritten(true, $"{ticker}'s exit at {price.ToString(CultureInfo.InvariantCulture)} on {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is recorded.")
                : new RequestWritten(false, $"{ticker}'s exit was not recorded: it holds no open taken trade of that time.");

    // The session after a night on the exchange's calendar.
    static DateOnly SessionAfter(DateOnly night)
    {
        var day = night.AddDays(1);

        while (!EquityBrief.Core.Bars.ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }

    async Task<int> ExecuteAsync(string sql, IReadOnlyList<(string Name, string Value)> parameters)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    async Task<IReadOnlyList<TakenTradeRow>> TakenAsync(string sql, IReadOnlyList<(string Name, string Value)> parameters)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<TakenTradeRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            decimal? Price(int column) =>
                reader.IsDBNull(column) ? null : decimal.Parse(reader.GetString(column), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

            DateOnly Day(int column) => DateOnly.ParseExact(reader.GetString(column), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            rows.Add(new TakenTradeRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Day(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                Price(6)!.Value,
                Day(7),
                reader.GetInt64(8) == 1,
                reader.GetInt64(9) == 1,
                Price(10),
                Price(11),
                Price(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
                Price(14),
                reader.IsDBNull(15) ? null : Day(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : Day(17),
                Price(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetDouble(20)));
        }

        return rows;
    }

    // ---- the S&P 400's and 600's nights, read one index at a time ----
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index

    // How many members each index held on a session, which the Universe selector states beside each choice.
    const string MembersByIndexOn = @"
        SELECT index_code, COUNT(DISTINCT ticker) FROM membership
        WHERE (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        GROUP BY index_code;
    ";

    public async Task<IReadOnlyDictionary<string, int>> MembersByIndexAsync(DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = MembersByIndexOn;
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    const string IndexNightsOf = "SELECT session_date FROM index_family_night WHERE index_code = $index ORDER BY session_date;";

    // The nights an index's families stored, oldest first.
    public async Task<IReadOnlyList<DateOnly>> IndexNightsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexNightsOf;
        command.Parameters.AddWithValue("$index", index);

        var nights = new List<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            nights.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return nights;
    }

    const string IndexNightOn = @"
        SELECT index_code, session_date, members, breadth, market_open, settings, rebalanced, fault FROM index_family_night
        WHERE index_code = $index AND session_date = $on;
    ";

    const string IndexLastRebalanceOn = @"
        SELECT MAX(session_date) FROM index_family_night WHERE index_code = $index AND rebalanced = 1 AND session_date <= $on;
    ";

    // An index's night as its families stored it, with the failure that stopped it where its part of the night failed,
    // and none for a night they did not read.
    public async Task<IndexNightRow?> IndexNightAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexNightOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new IndexNightRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.GetInt64(4) == 1,
                reader.GetString(5),
                reader.GetInt64(6) == 1,
                reader.IsDBNull(7) ? null : reader.GetString(7))
            : null;
    }

    // The last session an index's sector heavyweights rebalanced on, on or before a night.
    public async Task<DateOnly?> IndexLastRebalanceAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexLastRebalanceOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync() is string last ? DateOnly.ParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    const string RuleNightsOn = @"
        SELECT index_code, session_date, family, rule, evaluated, listed, gates, stretch, mark, flagged, completed, sessions, source
        FROM rule_night
        WHERE index_code = $index AND session_date = $on
        ORDER BY family, rule;
    ";

    // Every standing rule's row on a night: how many it listed, its funnel and its stretch against its mark.
    // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
    public async Task<IReadOnlyList<RuleNightRow>> RuleNightsAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RuleNightsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<RuleNightRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RuleNightRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4) == 1,
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.GetInt32(9) == 1,
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.GetString(12)));
        }

        return rows;
    }

    // The picks every variant kept on a night: the swing filter's variants' own, and each registered breakout, drift or
    // index rule's list as its recorder kept it, each with its plan.
    // see: A variant's picks are shown on its card when chosen and its results only under its tests
    const string RulePicksOn = @"
        SELECT rule, ticker, session_date, place, entry, stop, target, reward_to_risk, why FROM rule_pick
        WHERE index_code = $index AND session_date = $on
        UNION ALL
        SELECT candidate, ticker, session_date, place, entry, stop, target, reward_to_risk, '{}' FROM family_trade
        WHERE $index = 'GSPC' AND session_date = $on
        UNION ALL
        SELECT candidate, ticker, session_date, place, entry, stop, target, reward_to_risk, '{}' FROM index_rule_trade
        WHERE index_code = $index AND session_date = $on
        ORDER BY 1, 4;
    ";

    public async Task<IReadOnlyList<RulePickRow>> RulePicksAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RulePicksOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<RulePickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RulePickRow(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(3),
                Money.FromStorage(reader.GetString(4)),
                Money.FromStorage(reader.GetString(5)),
                reader.IsDBNull(6) ? null : Money.FromStorage(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetString(8)));
        }

        return rows;
    }

    const string FormingRowsOn = @"
        SELECT rule, place, ticker, close, high, moves_under, volume_needed, volume, range_ratio, missing, next_earnings, forming
        FROM forming_row
        WHERE index_code = $index AND session_date = $on
        ORDER BY rule, place;
    ";

    // The members forming a breakout under each breakout rule on a night.
    // see: The forming list advises and never lists a stock
    public async Task<IReadOnlyList<FormingRow>> FormingRowsAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FormingRowsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<FormingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new FormingRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetDouble(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : DateOnly.ParseExact(reader.GetString(10), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(11)));
        }

        return rows;
    }

    const string IndexPicksOn = @"
        SELECT session_date, ticker, family, state, place, also, held_family, held_night, held_index
        FROM index_family_pick
        WHERE index_code = $index AND session_date = $on
        ORDER BY place IS NULL, place, family, ticker;
    ";

    // An index's list on a night: every stock a family passed, listed or held back, the listed ones first in the page's
    // order.
    public async Task<IReadOnlyList<FamilyPickRow>> IndexPicksAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexPicksOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<FamilyPickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new FamilyPickRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : DateOnly.ParseExact(reader.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    const string IndexResultsOn = @"
        SELECT ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason
        FROM index_family_result
        WHERE index_code = $index AND session_date = $on
        ORDER BY family, place IS NULL, place, ticker;
    ";

    // Every member's answer under each of an index's families on a night.
    public async Task<IReadOnlyList<IndexResultRow>> IndexResultsAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexResultsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<IndexResultRow>();

        await using var reader = await command.ExecuteReaderAsync();

        decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

        while (await reader.ReadAsync())
        {
            rows.Add(new IndexResultRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) == 1,
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Price(4),
                Price(5),
                Price(6),
                Price(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return rows;
    }

    // Every trade an index's list kept on or before a night, its end read only where it ended by the night.
    const string IndexTradesUpTo = @"
        SELECT index_code, family, ticker, session_date, place, entry, stop, target, trail, cap,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN cost END
        FROM index_family_trade
        WHERE index_code = $index AND session_date <= $on
        ORDER BY session_date DESC, place, ticker;
    ";

    public async Task<IReadOnlyList<IndexTradeRow>> IndexTradesAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexTradesUpTo;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<IndexTradeRow>();

        await using var reader = await command.ExecuteReaderAsync();

        decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

        while (await reader.ReadAsync())
        {
            rows.Add(new IndexTradeRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(4),
                Money.FromStorage(reader.GetString(5)),
                Money.FromStorage(reader.GetString(6)),
                Price(7),
                Price(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : DateOnly.ParseExact(reader.GetString(10), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12)));
        }

        return rows;
    }

    // Every holding an index's sector heavyweights bought on or before a night, its end read only where it ended by the
    // night.
    const string IndexHoldingsUpTo = @"
        SELECT index_code, ticker, entered_on, sector, entry_close,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN exit_close END,
               CASE WHEN ended_on <= $on THEN reason END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN cut_return END,
               CASE WHEN ended_on <= $on THEN cost END,
               lead
        FROM index_heavyweight_holding
        WHERE index_code = $index AND entered_on <= $on
        ORDER BY entered_on, sector, ticker;
    ";

    public Task<IReadOnlyList<IndexHoldingRow>> IndexHoldingsAsync(string index, DateOnly on) =>
        HoldingRowsAsync(IndexHoldingsUpTo, ("$index", index), on);

    // Every holding a registered heavyweights rule of an index bought on or before a night, in its own book, its end read
    // only where it ended by the night.
    // see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
    const string IndexRuleHoldingsUpTo = @"
        SELECT index_code, ticker, entered_on, sector, entry_close,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN exit_close END,
               CASE WHEN ended_on <= $on THEN reason END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN cut_return END,
               CASE WHEN ended_on <= $on THEN cost END,
               lead
        FROM index_heavyweight_rule_holding
        WHERE candidate = $candidate AND entered_on <= $on
        ORDER BY entered_on, sector, ticker;
    ";

    public Task<IReadOnlyList<IndexHoldingRow>> IndexRuleHoldingsAsync(string candidate, DateOnly on) =>
        HoldingRowsAsync(IndexRuleHoldingsUpTo, ("$candidate", candidate), on);

    // The last session a registered heavyweights rule's own book rebalanced on, on or before a night.
    const string IndexRuleLastRebalanceOn = "SELECT MAX(session_date) FROM index_heavyweight_rule_night WHERE candidate = $candidate AND session_date <= $on;";

    public async Task<DateOnly?> IndexRuleLastRebalanceAsync(string candidate, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexRuleLastRebalanceOn;
        command.Parameters.AddWithValue("$candidate", candidate);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync() is string last ? DateOnly.ParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    async Task<IReadOnlyList<IndexHoldingRow>> HoldingRowsAsync(string sql, (string Name, string Value) owner, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue(owner.Name, owner.Value);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<IndexHoldingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new IndexHoldingRow(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(3),
                Money.FromStorage(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? null : Money.FromStorage(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11)));
        }

        return rows;
    }

    // Each of an index's holdings open on a night, its close there and its 200-session average there.
    const string IndexHoldingClosesOn = @"
        SELECT h.ticker, b.close, i.value
        FROM (SELECT DISTINCT ticker FROM index_heavyweight_holding WHERE index_code = $index AND entered_on <= $on AND (ended_on IS NULL OR ended_on > $on)) h
        LEFT JOIN bar b ON b.ticker = h.ticker AND b.session_date = $on
        LEFT JOIN indicator i ON i.ticker = h.ticker AND i.session_date = $on AND i.name = $average
        ORDER BY h.ticker;
    ";

    public Task<IReadOnlyList<HeavyweightCloseRow>> IndexHoldingClosesAsync(string index, DateOnly on) =>
        HoldingClosesAsync(IndexHoldingClosesOn, ("$index", index), on);

    // Each of a registered heavyweights rule's holdings open on a night in its own book, its close there and its
    // 200-session average there.
    const string IndexRuleHoldingClosesOn = @"
        SELECT h.ticker, b.close, i.value
        FROM (SELECT DISTINCT ticker FROM index_heavyweight_rule_holding WHERE candidate = $candidate AND entered_on <= $on AND (ended_on IS NULL OR ended_on > $on)) h
        LEFT JOIN bar b ON b.ticker = h.ticker AND b.session_date = $on
        LEFT JOIN indicator i ON i.ticker = h.ticker AND i.session_date = $on AND i.name = $average
        ORDER BY h.ticker;
    ";

    public Task<IReadOnlyList<HeavyweightCloseRow>> IndexRuleHoldingClosesAsync(string candidate, DateOnly on) =>
        HoldingClosesAsync(IndexRuleHoldingClosesOn, ("$candidate", candidate), on);

    async Task<IReadOnlyList<HeavyweightCloseRow>> HoldingClosesAsync(string sql, (string Name, string Value) owner, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue(owner.Name, owner.Value);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$average", EquityBrief.Core.Indicators.IndicatorSeries.Sma200);

        var rows = new List<HeavyweightCloseRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new HeavyweightCloseRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : Money.FromStorage(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        }

        return rows;
    }

    // Each member of an index on a night with the company its membership row names, and its close on the night.
    const string IndexMembersOn = @"
        SELECT m.ticker,
               (SELECT n.name FROM membership n WHERE n.ticker = m.ticker AND n.name IS NOT NULL ORDER BY n.joined DESC LIMIT 1),
               b.close
        FROM (SELECT DISTINCT ticker FROM membership
              WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)) m
        LEFT JOIN bar b ON b.ticker = m.ticker AND b.session_date = $session
        ORDER BY m.ticker;
    ";

    public async Task<IReadOnlyList<(string Ticker, string? Company, decimal? Close)>> IndexMembersAsync(string index, DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexMembersOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<(string, string?, decimal?)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : Money.FromStorage(reader.GetString(2))));
        }

        return rows;
    }

    // Every trade a registered family rule kept up to a night, with its result where it had ended by the night,
    // its benchmark where written and the cap its benchmark waits on; and every holding a registered sector
    // heavyweights rule kept, read at the session it ended where it ended by the night, since its holding counts in
    // the block it ends in, and at its buy while open, with its size cut's return as its benchmark and no cap. The
    // name is not read: a rule's record is over its trades' edges and the sessions they count on.
    // see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
    const string FamilyTradesUpTo = @"
        SELECT candidate, session_date,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN benchmark END,
               cap,
               CASE WHEN ended_on <= $on THEN cost END
        FROM family_trade
        WHERE session_date <= $on
        UNION ALL
        SELECT candidate, CASE WHEN ended_on <= $on THEN ended_on ELSE entered_on END,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN cut_return END,
               0,
               NULL
        FROM heavyweight_rule_holding
        WHERE entered_on <= $on
        ORDER BY 1, 2;
    ";

    // The trades every registered family rule kept up to a night, which each rule's record is read over, a trade
    // that ended after the night read as still open on it. A benchmark is read only once the trade's cap's
    // sessions after its listing have passed by the night, which is when the recorder writes it, so a trade
    // stopped out before the night while its cap still ran is not decided on that night's page whatever a later
    // night wrote.
    // see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
    public async Task<IReadOnlyList<EquityBrief.Core.Candidates.FamilyTradeRow>> FamilyTradesAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FamilyTradesUpTo;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<EquityBrief.Core.Candidates.FamilyTradeRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var listed = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            rows.Add(new EquityBrief.Core.Candidates.FamilyTradeRow(
                reader.GetString(0),
                listed,
                reader.IsDBNull(2) ? null : DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) || !EquityBrief.Core.Returns.Blocks.Closed(listed, on, reader.GetInt32(5)) ? null : reader.GetDouble(4),
                reader.IsDBNull(6) ? null : reader.GetDouble(6)));
        }

        return rows;
    }

    // Each registered heavyweights rule's holdings beside them, read as the S&P 500's are: by the session a holding ended
    // on, or bought on while open, its size cut's return its benchmark and no cap.
    const string IndexRuleTradesUpTo = @"
        SELECT candidate, session_date,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN benchmark END,
               cap,
               CASE WHEN ended_on <= $on THEN cost END
        FROM index_rule_trade
        WHERE index_code = $index AND session_date <= $on
        UNION ALL
        SELECT candidate, CASE WHEN ended_on <= $on THEN ended_on ELSE entered_on END,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN cut_return END,
               0,
               CASE WHEN ended_on <= $on THEN cost END
        FROM index_heavyweight_rule_holding
        WHERE index_code = $index AND entered_on <= $on
        ORDER BY 1, 2;
    ";

    // The trades every registered rule of an index's swing families kept up to a night, each one's result read after its
    // own round trip, the one figure such a rule's record is read by, and a trade that ended after the night read as
    // still open on it; a benchmark read only once the trade's cap's sessions have passed by the night, when the index
    // families write it.
    // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
    public async Task<IReadOnlyList<EquityBrief.Core.Candidates.FamilyTradeRow>> IndexRuleTradesAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexRuleTradesUpTo;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<EquityBrief.Core.Candidates.FamilyTradeRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var listed = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            double? result = reader.IsDBNull(3) ? null : reader.GetDouble(3) - (reader.IsDBNull(6) ? 0 : reader.GetDouble(6));

            rows.Add(new EquityBrief.Core.Candidates.FamilyTradeRow(
                reader.GetString(0),
                listed,
                reader.IsDBNull(2) ? null : DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                result,
                reader.IsDBNull(4) || !EquityBrief.Core.Returns.Blocks.Closed(listed, on, reader.GetInt32(5)) ? null : reader.GetDouble(4)));
        }

        return rows;
    }

    // Every holding the sector heavyweights bought on or before a night, its end read only where it ended by the
    // night, so a holding that ended after it is read as open on it.
    const string HeavyweightHoldingsUpTo = @"
        SELECT ticker, entered_on, sector, company, entry_close,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN exit_close END,
               CASE WHEN ended_on <= $on THEN reason END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN cut_return END
        FROM heavyweight_holding
        WHERE entered_on <= $on
        ORDER BY entered_on, sector, ticker;
    ";

    // The sector heavyweights' holdings as of a night, which tonight's card, a name's page and Past picks read.
    // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
    // A registered heavyweights rule's own book as of a night, read as the page's book is, which its card draws when the
    // card's selector chooses it.
    // see: A variant's picks are shown on its card when chosen and its results only under its tests
    const string HeavyweightRuleHoldingsUpTo = @"
        SELECT ticker, entered_on, sector, company, entry_close,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN exit_close END,
               CASE WHEN ended_on <= $on THEN reason END,
               CASE WHEN ended_on <= $on THEN result END,
               CASE WHEN ended_on <= $on THEN cut_return END
        FROM heavyweight_rule_holding
        WHERE candidate = $candidate AND entered_on <= $on
        ORDER BY entered_on, sector, ticker;
    ";

    const string HeavyweightRuleReadOn = @"
        SELECT session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader
        FROM heavyweight_rule_night
        WHERE candidate = $candidate AND session_date = (SELECT MAX(session_date) FROM heavyweight_rule_night WHERE candidate = $candidate AND session_date <= $on)
        ORDER BY sector, place;
    ";

    const string HeavyweightRuleClosesOn = @"
        SELECT h.ticker, b.close, i.value
        FROM (SELECT DISTINCT ticker FROM heavyweight_rule_holding WHERE candidate = $candidate AND entered_on <= $on AND (ended_on IS NULL OR ended_on > $on)) h
        LEFT JOIN bar b ON b.ticker = h.ticker AND b.session_date = $on
        LEFT JOIN indicator i ON i.ticker = h.ticker AND i.session_date = $on AND i.name = $average
        ORDER BY h.ticker;
    ";

    public async Task<IReadOnlyList<HeavyweightHoldingRow>> HeavyweightHoldingsAsync(DateOnly on, string? candidate = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = candidate is null ? HeavyweightHoldingsUpTo : HeavyweightRuleHoldingsUpTo;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (candidate is not null)
        {
            command.Parameters.AddWithValue("$candidate", candidate);
        }

        var rows = new List<HeavyweightHoldingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new HeavyweightHoldingRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                Money.FromStorage(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? null : Money.FromStorage(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9)));
        }

        return rows;
    }

    // The book's reading at its last rebalance on or before a night, a sector at a time in order of value.
    const string HeavyweightReadOn = @"
        SELECT session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader
        FROM heavyweight_night
        WHERE session_date = (SELECT MAX(session_date) FROM heavyweight_night WHERE session_date <= $on)
        ORDER BY sector, place;
    ";

    // The sector heavyweights' last rebalance on or before a night: each sector's largest companies as the book read
    // them, and nothing where the book read none by the night.
    public async Task<IReadOnlyList<HeavyweightReadRow>> HeavyweightReadAsync(DateOnly on, string? candidate = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = candidate is null ? HeavyweightReadOn : HeavyweightRuleReadOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (candidate is not null)
        {
            command.Parameters.AddWithValue("$candidate", candidate);
        }

        var rows = new List<HeavyweightReadRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new HeavyweightReadRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                Money.FromStorage(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.GetInt32(9) == 1,
                reader.GetInt32(10) == 1));
        }

        return rows;
    }

    // Each holding open on a night, its close there and its 200-session average there.
    const string HeavyweightClosesOn = @"
        SELECT h.ticker, b.close, i.value
        FROM (SELECT DISTINCT ticker FROM heavyweight_holding WHERE entered_on <= $on AND (ended_on IS NULL OR ended_on > $on)) h
        LEFT JOIN bar b ON b.ticker = h.ticker AND b.session_date = $on
        LEFT JOIN indicator i ON i.ticker = h.ticker AND i.session_date = $on AND i.name = $average
        ORDER BY h.ticker;
    ";

    // Where each holding open on a night closed against its 200-session average, which its row on the card states.
    public async Task<IReadOnlyList<HeavyweightCloseRow>> HeavyweightClosesAsync(DateOnly on, string? candidate = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = candidate is null ? HeavyweightClosesOn : HeavyweightRuleClosesOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$average", EquityBrief.Core.Indicators.IndicatorSeries.Sma200);

        if (candidate is not null)
        {
            command.Parameters.AddWithValue("$candidate", candidate);
        }

        var rows = new List<HeavyweightCloseRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new HeavyweightCloseRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : Money.FromStorage(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        }

        return rows;
    }

    const string FamilyResultsOn = @"
        SELECT session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates
        FROM family_result
        WHERE session_date = $on
        ORDER BY family, place IS NULL, place, ticker;
    ";

    // Every member's answer under each setup family but the pullback on one night, a family at a time and
    // the names it passed first in its own order. A night the family evaluator did not run for holds none.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public async Task<IReadOnlyList<FamilyResultRow>> FamilyResultsAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FamilyResultsOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<FamilyResultRow>();

        await using var reader = await command.ExecuteReaderAsync();

        decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

        while (await reader.ReadAsync())
        {
            rows.Add(new FamilyResultRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3) == 1,
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Price(6),
                Price(7),
                Price(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(10)) ?? [],
                reader.GetString(11)));
        }

        return rows;
    }

    // Every member's readings of its reported quarters on one night, and none on a night the readings did
    // not run for.
    // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
    public async Task<IReadOnlyList<FundamentalReadingRow>> FundamentalReadingsAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ReadingsOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<FundamentalReadingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(ReadingRow(reader));
        }

        return rows;
    }

    // One member's readings on the newest night at or before the one asked for, or its newest.
    public async Task<FundamentalReadingRow?> FundamentalReadingAsync(string ticker, DateOnly? on = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ReadingForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", (on ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadingRow(reader) : null;
    }

    static FundamentalReadingRow ReadingRow(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        DateOnly? Day(int at) => reader.IsDBNull(at) ? null : DateOnly.ParseExact(reader.GetString(at), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new FundamentalReadingRow(
            reader.GetString(0),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(2),
            Day(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            Day(5),
            reader.GetString(6));
    }

    // The quarters one fetch stored for a member, newest first, with the dates each was filed and reported on.
    public async Task<IReadOnlyList<QuarterDatesRow>> QuartersOfAFetchAsync(string ticker, string fetchedAt)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = QuartersOfAFetch;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$fetched_at", fetchedAt);

        var rows = new List<QuarterDatesRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            DateOnly? Day(int at) => reader.IsDBNull(at) ? null : DateOnly.ParseExact(reader.GetString(at), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            rows.Add(new QuarterDatesRow(Day(0)!.Value, Day(1), Day(2)));
        }

        return rows;
    }

    static GateResultRow GateRow(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        double? Real(int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);

        decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

        return new GateResultRow(
            reader.GetString(0),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(2),
            reader.GetInt32(3) == 1,
            reader.GetInt32(4) == 1,
            reader.GetInt32(5) == 1,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt32(7) == 1,
            reader.IsDBNull(8) ? null : reader.GetInt32(8) == 1,
            reader.GetInt32(9) == 1,
            Real(10),
            Real(11),
            Price(12),
            Price(13),
            Price(14),
            Real(15),
            Real(16),
            System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(17)) ?? [],
            reader.GetInt32(18) == 1,
            reader.IsDBNull(19) ? null : reader.GetInt32(19),
            Real(20),
            reader.IsDBNull(21) ? null : reader.GetInt32(21),
            reader.GetString(22),
            Price(23),
            Price(24),
            Real(25),
            Real(26));
    }

    // The night's market reading, and none where the night stored none.
    public async Task<MarketReadingRow?> MarketReadingAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = MarketReadingOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new MarketReadingRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9))
            : null;
    }

    // Each name's close and 200-day average on the sessions up to a night, newest first, over as
    // many sessions as the line draws: the rows the breadth line is counted from.
    // Every name holding a close on the session, with its average where it holds one, so a session on
    // which fewer than half hold an average reads none, by the swing readings' own rule.
    const string AverageAndCloseUpTo = @"
        SELECT b.session_date, b.close, i.value
        FROM bar b
        LEFT JOIN indicator i ON i.ticker = b.ticker AND i.session_date = b.session_date AND i.name = $average
        WHERE b.session_date IN (
              SELECT DISTINCT session_date FROM indicator
              WHERE name = $average AND value IS NOT NULL AND session_date <= $on
              ORDER BY session_date DESC LIMIT $sessions)
        ORDER BY b.session_date;
    ";

    const string SettingsOfVersion = "SELECT settings FROM filter_version WHERE version = $version;";

    // Each candidate's answer on each name-night up to a night where it picked the name or the live list
    // did, with the answer's gate values: the rows the Run page's comparison and its versions' counts read.
    // A replayed row carries no shadow and is read by nothing here.
    const string ShadowPicksUpTo = @"
        SELECT g.session_date, g.ticker, g.passed, json_extract(c.value, '$.candidate'), json_extract(c.value, '$.fired'), json_extract(c.value, '$.values')
        FROM gate_result g, json_each(COALESCE(g.shadow, '{}'), '$.candidates') c
        WHERE g.session_date <= $on AND (json_extract(c.value, '$.fired') = 1 OR g.passed = 1)
        ORDER BY g.session_date, g.ticker;
    ";

    const string EvaluatedOn = @"
        SELECT DISTINCT json_extract(c.value, '$.candidate')
        FROM gate_result g, json_each(COALESCE(g.shadow, '{}'), '$.candidates') c
        WHERE g.session_date = $on;
    ";

    // Every candidate's picks and the live list's up to a night, as the shadow column stored them.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    public async Task<IReadOnlyList<ShadowPickRow>> ShadowPicksAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ShadowPicksUpTo;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<ShadowPickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ShadowPickRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetInt32(2) == 1,
                reader.GetString(3),
                !reader.IsDBNull(4) && reader.GetInt32(4) == 1,
                reader.IsDBNull(5) ? "{}" : reader.GetString(5)));
        }

        return rows;
    }

    // The candidates a night evaluated, which a comparison names before it says a version picked nobody.
    public async Task<IReadOnlySet<string>> EvaluatedOnAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = EvaluatedOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var evaluated = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            evaluated.Add(reader.GetString(0));
        }

        return evaluated;
    }

    // The breadth over the sessions up to a night that the store holds 200-day averages for, each
    // counted by the swing readings' own rule over the names holding a close and the average that
    // session, oldest first; fewer than asked for where the store holds fewer.
    // see: The market on the Run page is named in one word by a stated rule that moves no gate
    public async Task<IReadOnlyList<BreadthPoint>> BreadthLineAsync(DateOnly on, int sessions)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = AverageAndCloseUpTo;
        command.Parameters.AddWithValue("$average", "sma" + EquityBrief.Core.Filter.SwingReadings.BreadthAverageSessions.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sessions", sessions);

        var held = new SortedDictionary<DateOnly, (int Closes, List<(decimal Close, double Average)> Pairs)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var session = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var (closes, pairs) = held.TryGetValue(session, out var found) ? found : (0, new List<(decimal Close, double Average)>());

            if (!reader.IsDBNull(2))
            {
                pairs.Add((Money.FromStorage(reader.GetString(1)), reader.GetDouble(2)));
            }

            held[session] = (closes + 1, pairs);
        }

        return
        [
            .. held
                .Select(pair => (pair.Key, Breadth: EquityBrief.Core.Filter.SwingReadings.BreadthOf(pair.Value.Closes, pair.Value.Pairs)))
                .Where(point => point.Breadth.Share is not null)
                .Select(point => new BreadthPoint(point.Key, point.Breadth.Share!.Value, point.Breadth.Counted)),
        ];
    }

    // The settings a filter version holds, and none for a version the register never opened, which is
    // how a night run on section 17's proposed values and a replayed session read.
    public async Task<string?> FilterSettingsAsync(string version)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SettingsOfVersion;
        command.Parameters.AddWithValue("$version", version);

        return await command.ExecuteScalarAsync() as string;
    }

    // Whether a member's trigger event happened on each session up to a night, newest first, off every result
    // the store holds for it, the replayed sessions among them, since those are what the trigger's arrival
    // reads: what "Close to a buy point" reads how many sessions before the night a missed trigger first
    // fired from. A session whose bars could not say is none.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public async Task<IReadOnlyList<bool?>> TriggerEventsAsync(string ticker, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = TriggerEventsUpTo;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var events = new List<bool?>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            events.Add(reader.IsDBNull(0) ? null : reader.GetInt64(0) == 1);
        }

        return events;
    }

    const string TriggerEventsUpTo = "SELECT trigger_event FROM gate_result WHERE ticker = $ticker AND session_date <= $on ORDER BY session_date DESC;";

    // Every name's two readings for the peers table, one row per name, as the annotator last
    // wrote them.
    public async Task<IReadOnlyList<PeerReadingRow>> PeerReadingsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = EveryPeerReading;

        var rows = new List<PeerReadingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new PeerReadingRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Money.FromStorage(reader.GetString(4)),
                reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    // The stored closes of the names given, each name's in session order.
    public async Task<IReadOnlyList<CloseRow>> ClosesAsync(IReadOnlyList<string> tickers)
    {
        if (tickers.Count == 0)
        {
            return [];
        }

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ClosesOfNames;
        command.Parameters.AddWithValue("$tickers", System.Text.Json.JsonSerializer.Serialize(tickers));

        var rows = new List<CloseRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CloseRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2))));
        }

        return rows;
    }

    // The index on the night the page shows, for the reason `StaleNamesAsync`
    // gives.
    string OnNight(DateOnly? night) =>
        (night ?? clock.SessionDateAt(clock.UtcNow)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<UniverseRow>> UniverseAsync(string indexCode, DateOnly? night = null)
    {
        await using var connection = Open();

        var support = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var resistance = new Dictionary<string, decimal>(StringComparer.Ordinal);

        await using (var bands = connection.CreateCommand())
        {
            bands.CommandText = ImmediateBands;
            bands.Parameters.AddWithValue("$on", On(night));

            await using var marked = await bands.ExecuteReaderAsync();

            while (await marked.ReadAsync())
            {
                var name = marked.GetString(0);

                // The nearest support is the highest of them and the nearest
                // resistance the lowest, which is the rule the builder marked
                // them by, applied here to the prices rather than to their text.
                if (string.Equals(marked.GetString(1), LevelSeries.Support, StringComparison.Ordinal))
                {
                    var edge = Money.FromStorage(marked.GetString(3));

                    if (!support.TryGetValue(name, out var held) || edge > held)
                    {
                        support[name] = edge;
                    }
                }
                else
                {
                    var edge = Money.FromStorage(marked.GetString(2));

                    if (!resistance.TryGetValue(name, out var held) || edge < held)
                    {
                        resistance[name] = edge;
                    }
                }
            }
        }

        await using var command = connection.CreateCommand();

        command.CommandText = Universe;
        command.Parameters.AddWithValue("$index_code", indexCode);
        command.Parameters.AddWithValue("$session", OnNight(night));
        command.Parameters.AddWithValue("$on", On(night));
        command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);
        command.Parameters.AddWithValue("$retired", ClaimRules.RetiredKey);

        var rows = new List<UniverseRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var ticker = reader.GetString(0);

            rows.Add(new UniverseRow(
                ticker,
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : Money.FromStorage(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                support.TryGetValue(ticker, out var below) ? below : null,
                resistance.TryGetValue(ticker, out var above) ? above : null,
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : Day(reader.GetString(7))));
        }

        return rows;
    }

    // Every current member as the masthead's search offers it.
    public async Task<IReadOnlyList<FindableRow>> FindableAsync(string indexCode, DateOnly? night = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Findable;
        command.Parameters.AddWithValue("$index_code", indexCode);
        command.Parameters.AddWithValue("$session", OnNight(night));
        command.Parameters.AddWithValue("$retired", ClaimRules.RetiredKey);

        var rows = new List<FindableRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new FindableRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : Day(reader.GetString(2))));
        }

        return rows;
    }

    // Every name holding a researched section, newest first.
    public async Task<IReadOnlyList<ResearchedRow>> ResearchedAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Researched;
        command.Parameters.AddWithValue("$retired", ClaimRules.RetiredKey);

        var rows = new List<ResearchedRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ResearchedRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                Day(reader.GetString(3)),
                reader.GetInt32(4)));
        }

        return rows;
    }

    static DateOnly Day(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The night a read is about. A page about tonight asks for the last date there is
    // rather than for no bound, so one statement serves both and a night's page and
    // tonight's differ in the date they hand over and in nothing else.
    // see: A name's page for an earlier night draws what the store held that night and nothing it learned after
    static string On(DateOnly? asOf) =>
        (asOf ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<CalendarRow?> NextEventAsync(string ticker, DateOnly onOrAfter)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NextEventForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on_or_after", onOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new CalendarRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4))
            : null;
    }

    // Every name's next event on or after a date, in one read.
    public async Task<IReadOnlyList<CalendarRow>> NextEventsAsync(DateOnly onOrAfter)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NextEventForEveryName;
        command.Parameters.AddWithValue("$on_or_after", onOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<CalendarRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CalendarRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    // Every name's two newest stored sessions at or before a night, newest first
    // within each name.
    public async Task<IReadOnlyList<CloseRow>> ClosesToTheNightAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ClosesToTheNight;
        command.Parameters.AddWithValue("$session", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<CloseRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CloseRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2))));
        }

        return rows;
    }

    public async Task<LadderRow?> LadderAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LadderForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", On(asOf));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new LadderRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3))
            : null;
    }

    // Every filing one name holds, newest first. All of them rather than the five
    // the numbers section draws, because the store holds twelve and which of them
    // a section shows is the section's statement rather than this one's
    // (see: Twelve filings are stored and five are shown). The payload and the
    // source are handed back as stored, since a read surface that unpacked the JSON
    // would be deciding which figures exist.
    //
    // The newest row's parts that are as of a fetch are the newest copy's, of the copies fetched on
    // or before the night the read is about, so tonight's page draws the day's figures and an
    // earlier night's draws what the store held that night.
    // see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
    // see: A name's page for an earlier night draws what the store held that night and nothing it learned after
    public async Task<IReadOnlyList<FilingRow>> FundamentalsAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();

        var rows = new List<FilingRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FilingsForName;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", On(asOf));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows.Add(new FilingRow(
                    reader.GetString(0),
                    DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }

        if (rows.Count == 0)
        {
            return rows;
        }

        string? snapshot = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SnapshotsForName;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync();

            while (snapshot is null && await reader.ReadAsync())
            {
                var fetchedAt = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

                if (asOf is not { } night || clock.SessionDateAt(fetchedAt) <= night)
                {
                    snapshot = reader.GetString(1);
                }
            }
        }

        rows[0] = rows[0] with { Payload = FundamentalsSnapshot.Over(rows[0].Payload, snapshot) };

        return rows;
    }

    // Where each of a name's sections stands, newest version first per section.
    public async Task<IReadOnlyList<SectionStateRow>> SectionStatesAsync(string ticker, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SectionStatesForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$retired", ClaimRules.RetiredKey);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<SectionStateRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SectionStateRow(
                reader.GetString(0),
                reader.GetInt32(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    // The spend rows between two instants, handed back as stored. What they come to,
    // and whether a cap has stopped research, is judged by the screen with the rule the
    // spend cap judges a call by, so the page and the refusal are one rule.
    // see: The spend cap counts a UTC day and a UTC month, and refuses a call that could take spend past either
    public async Task<IReadOnlyList<SpentRow>> SpentRowsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SpentBetween;
        command.Parameters.AddWithValue("$from", from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        var rows = new List<SpentRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SpentRow(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                decimal.Parse(reader.GetString(1), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    // What every answered paid call cost, one amount per call with the run it was made
    // under, as stored.
    public async Task<IReadOnlyList<(string RunId, decimal Spend)>> PaidCallSpendsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = PaidCallSpends;
        command.Parameters.AddWithValue("$prefix", PaidCallStage + ":");
        command.Parameters.AddWithValue("$nothing", NothingSpent);
        command.Parameters.AddWithValue("$trial", ", " + EquityBrief.Core.Research.TrialCalls.Round);
        command.Parameters.AddWithValue("$review", ", " + EquityBrief.Core.Research.TrialCalls.ReviewRound);

        var spends = new List<(string, decimal)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            spends.Add((reader.GetString(0), decimal.Parse(reader.GetString(1), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)));
        }

        return spends;
    }

    // Every run log row the run page reads its reports from, in the order they were written.
    public async Task<IReadOnlyList<RunStageRow>> ReportRowsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ReportRows;
        ReportParameters(command);
        command.Parameters.AddWithValue("$call", PaidCallStage + ":");
        command.Parameters.AddWithValue("$trial", TrialStage + ":");
        command.Parameters.AddWithValue("$review", ReviewStage + ":");

        var rows = new List<RunStageRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RunStageRow(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9)));
        }

        return rows;
    }

    // Each version the research passes and their theme passes wrote.
    public async Task<IReadOnlyList<WrittenVersion>> ReportVersionsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ReportVersions;
        ReportParameters(command);

        var versions = new List<WrittenVersion>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            versions.Add(new WrittenVersion(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return versions;
    }

    static void ReportParameters(SqliteCommand command)
    {
        command.Parameters.AddWithValue("$pass", EquityBrief.Core.Research.PassRun.Prefix);
        command.Parameters.AddWithValue("$research", ResearchStage);
        command.Parameters.AddWithValue("$theme", ThemeStage);
    }

    // The instant every answered paid call came back, as the run log carries it.
    public async Task<IReadOnlyList<DateTimeOffset>> PaidCallAnswersAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = PaidCallAnswers;
        command.Parameters.AddWithValue("$prefix", PaidCallStage + ":");
        command.Parameters.AddWithValue("$nothing", NothingSpent);

        var answers = new List<DateTimeOffset>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0)
                && DateTimeOffset.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var answered))
            {
                answers.Add(answered);
            }
        }

        return answers;
    }

    // A name's written sections, one per section, each the newest the checker
    // accepted, with its industry cycle among them, which is its theme's rather than a row
    // of its own.
    public async Task<IReadOnlyList<WrittenSectionRow>> WrittenSectionsAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = WrittenSectionsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$retired", ClaimRules.RetiredKey);
        command.Parameters.AddWithValue("$on", On(asOf));

        var rows = new List<WrittenSectionRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new WrittenSectionRow(
                reader.GetString(0),
                reader.GetInt32(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        if (await ThemeCycleAsync(ticker) is { } cycle)
        {
            rows.Add(cycle);
        }

        return rows;
    }

    // The name's industry cycle, being its theme's newest accepted version, or none where
    // its industry has none or the index names no industry for it.
    public async Task<WrittenSectionRow?> ThemeCycleAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ThemeCycleForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$section", ClaimRules.CycleSection);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new WrittenSectionRow(
                reader.GetString(0),
                reader.GetInt32(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5))
            : null;
    }

    // The detail of the newest pass for a name, as stored, or none where no pass has
    // run for it. Handed back unread, for the reason a filing's payload is.
    public async Task<string?> NewestPassAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestPassForName;
        command.Parameters.AddWithValue("$prose", ProseStage);
        command.Parameters.AddWithValue("$research", ResearchStage);
        command.Parameters.AddWithValue("$not_warranted", PassNotWarranted);
        command.Parameters.AddWithValue("$already_running", PassAlreadyRunning);
        command.Parameters.AddWithValue("$ticker", ticker);

        return await command.ExecuteScalarAsync() as string;
    }

    // The documents the given ids name, as stored. An id no row holds is not returned,
    // and the page says so for it rather than drawing a link to nothing.
    public async Task<IReadOnlyList<CitedDocumentRow>> CitedDocumentsAsync(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = CitedDocuments;
        command.Parameters.AddWithValue("$ids", System.Text.Json.JsonSerializer.Serialize(ids));

        var rows = new List<CitedDocumentRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CitedDocumentRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(4)));
        }

        return rows;
    }

    // Every dated event the calendar holds for one name on or after a date.
    public async Task<IReadOnlyList<CalendarRow>> EventsAsync(string ticker, DateOnly onOrAfter)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = EventsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on_or_after", onOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<CalendarRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CalendarRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    // The date every name's accepted sections were written on, as of a night.
    public async Task<IReadOnlyList<WrittenOnRow>> WrittenOnOrBeforeAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = WrittenOnOrBefore;
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<WrittenOnRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new WrittenOnRow(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    // Every name whose stored series is suspect, as the rows hold them. The name page and
    // tonight's list say so beside the name's figures, which are computed over a series
    // that may not carry a dividend's or a split's adjustment. The whole store's rather
    // than one name's, since a night holds none or a few and tonight's list asks about
    // twenty names at once.
    // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
    public async Task<IReadOnlyList<SuspectSeriesRow>> SuspectSeriesAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SuspectSeries;
        command.Parameters.AddWithValue("$suspect", SuspectState);

        var rows = new List<SuspectSeriesRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SuspectSeriesRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return rows;
    }

    const string SuspectSeries = @"
        SELECT ticker, reason, checked_at, retries
        FROM series_state
        WHERE state = $suspect
        ORDER BY ticker;
    ";

    // The backfill's stage, stated here because the read surface holds no reference to the
    // worker; `nightly-run` asserts the two agree.
    public const string BackfillStage = "backfill";

    // Where the backfill asked for a name's year and none came back, as the newest of its rows
    // naming the name says, and nothing where the newest that asked for it served it.
    // see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
    public async Task<NoYearRow?> NoYearAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = BackfillRows;
        command.Parameters.AddWithValue("$stage", BackfillStage);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            using var detail = JsonDocument.Parse(reader.GetString(0));
            var root = detail.RootElement;

            if (root.TryGetProperty("unserved", out var unserved) && unserved.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in unserved.EnumerateArray())
                {
                    if (string.Equals(name.GetProperty("ticker").GetString(), ticker, StringComparison.Ordinal))
                    {
                        return new NoYearRow(ticker, name.GetProperty("nights").GetInt32(), NamedDate(name, "last"), NamedDate(name, "next"));
                    }
                }
            }

            if (root.TryGetProperty("asked", out var asked)
                && asked.ValueKind == JsonValueKind.Array
                && asked.EnumerateArray().Any(entry => string.Equals(entry.GetString(), ticker, StringComparison.Ordinal)))
            {
                return null;
            }
        }

        return null;
    }

    static DateOnly? NamedDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    const string BackfillRows = @"
        SELECT detail FROM run_log
        WHERE stage = $stage AND detail LIKE '{%'
        ORDER BY started_at DESC;
    ";

    // Whether the index holds a name today, which is what the name page's control is
    // refused on before anything is started for it.
    public async Task<bool> IsMemberAsync(string indexCode, string ticker) =>
        (await UniverseAsync(indexCode)).Any(member => string.Equals(member.Ticker, ticker, StringComparison.Ordinal));

    // Whether a name's research stands, read here by the rules the judge applies,
    // so the page and the judge's run log reach one verdict from one store. The
    // judge writes the run log and nothing else, which is why this is derived on
    // read rather than read back from a stored state.
    // see: Research goes stale on an event dated after the section was written, and a news spike is dated by the session it began
    public async Task<StalenessVerdict> StalenessAsync(string ticker)
    {
        await using var connection = Open();

        var sections = (await SectionStatesAsync(ticker, DateOnly.MaxValue))
            .Select(state => new SectionStanding(state.Section, state.AsOf, state.Status))
            .ToArray();

        var earnings = new List<EarningsEvent>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EarningsForName;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                earnings.Add(new EarningsEvent(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.GetString(1)));
            }
        }

        var pulse = new List<PulseSession>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PulseForName;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                pulse.Add(new PulseSession(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.GetInt32(1)));
            }
        }

        DateOnly night;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestFactsNight;
            command.Parameters.AddWithValue("$ticker", ticker);

            night = await command.ExecuteScalarAsync() is string stored
                ? DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : clock.SessionDateAt(clock.UtcNow);
        }

        var filings = await FundamentalsAsync(ticker);

        return Staleness.Judge(
            night,
            sections,
            filings.Count == 0 ? null : filings.Max(filing => filing.FilingDate),
            earnings,
            pulse,
            refresh: false);
    }

    // The sections that fell back on one night, a name's and a theme's together.
    public async Task<IReadOnlyList<FellBackRow>> FellBackAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FellBackOnNight;
        command.Parameters.AddWithValue("$retired", ClaimRules.RetiredKey);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<FellBackRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new FellBackRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }

    // The candidate register, whole, for the run page's shadow region.
    //
    // Whole rather than filtered to what stands, because what stands is a
    // question about an instant and the answer is arithmetic over every row: a
    // retirement is a row like any other, and a query that returned only the
    // registrations would be a reader that could not see one.
    public async Task<IReadOnlyList<CandidateRow>> RegisteredCandidatesAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RegisteredCandidates;

        var rows = new List<CandidateRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CandidateRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.ParseExact(
                    reader.GetString(5),
                    "yyyy-MM-ddTHH:mm:ssZ",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                reader.IsDBNull(6) ? "{}" : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8)));
        }

        return rows;
    }

    // The sweep answers recorded for an index before an instant, every run's, which a card reads to say its family's
    // sweep found no setting that passed the floors.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    public async Task<IReadOnlyList<RecordedAnswer>> SweepAnswersAsync(string indexCode, DateTimeOffset before)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT run, index_code, family, design, answer, recorded_at FROM sweep_answer WHERE index_code = $index AND recorded_at < $before;";
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$before", before.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

        var rows = new List<RecordedAnswer>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RecordedAnswer(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.ParseExact(reader.GetString(5), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)));
        }

        return rows;
    }

    // Every replay of a family rule the run log holds, each read back as where the rule's record counts from.
    // see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
    public async Task<IReadOnlyList<EquityBrief.Core.Candidates.FamilyReplayRow>> FamilyReplaysAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FamilyReplays;
        command.Parameters.AddWithValue("$stages", EquityBrief.Core.Candidates.FamilyRecords.ReplayStages);

        var rows = new List<EquityBrief.Core.Candidates.FamilyReplayRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var at = DateTimeOffset.ParseExact(reader.GetString(2), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            if (EquityBrief.Core.Candidates.FamilyRecords.ReplayOf(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), at) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    // Every setup a registered candidate produced, for the run page's record region.
    public async Task<IReadOnlyList<CandidateSetupRow>> CandidateSetupsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = CandidateSetups;
        command.Parameters.AddWithValue("$horizon", EquityBrief.Core.Returns.ForwardReturnSeries.Setup);

        // A swing family candidate fires on a swing filter row, and its setup is the row's plan its own
        // verdict names, the plan at the nearest bands where a verdict written before any other was stored
        // names none.
        // see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
        command.Parameters.AddWithValue("$swing", EquityBrief.Core.Returns.ForwardReturnSeries.Swing);
        command.Parameters.AddWithValue("$clear", EquityBrief.Core.Returns.ForwardReturnSeries.Clear);
        command.Parameters.AddWithValue("$clearPlan", EquityBrief.Core.Filter.FilterSettings.ClearWord);
        command.Parameters.AddWithValue("$ladderPlan", EquityBrief.Core.Filter.FilterSettings.LadderWord);

        var read = new List<(CandidateSetupRow Row, EquityBrief.Core.Filter.OpenTradeListing Listing, EquityBrief.Core.Filter.ListOrder Order)>();

        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var session = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var outcome = reader.IsDBNull(2) ? null : reader.GetString(2);

                read.Add((
                    new CandidateSetupRow(
                        reader.GetString(0),
                        session,
                        outcome,
                        reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        reader.IsDBNull(6) ? null : reader.GetDouble(6),
                        reader.IsDBNull(7) ? null : reader.GetDouble(7),
                        !reader.IsDBNull(8) && reader.GetInt64(8) == 1),
                    new EquityBrief.Core.Filter.OpenTradeListing(
                        reader.GetString(9),
                        session,
                        reader.GetInt64(11) == 1,
                        outcome,
                        reader.IsDBNull(10) ? null : DateOnly.ParseExact(reader.GetString(10), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        EquityBrief.Core.Returns.ForwardReturnSeries.SetupSessionCap),
                    new EquityBrief.Core.Filter.ListOrder(
                        reader.IsDBNull(12) ? null : reader.GetDouble(12),
                        reader.IsDBNull(13) ? null : reader.GetDouble(13),
                        reader.IsDBNull(14) ? null : reader.GetInt32(14))));
            }
        }

        return CandidateSetupsKept(read, KeptANight(await RegisteredCandidatesAsync()));
    }

    // The most a night each candidate's list keeps, read off the registration it stands by, for the candidates
    // whose registration states a count; a candidate stating none keeps every member it fires on.
    // see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
    public static IReadOnlyDictionary<string, int> KeptANight(IReadOnlyList<CandidateRow> register) =>
        register
            .Where(row => row.Event == EquityBrief.Core.Candidates.CandidateFamily.Registered)
            .GroupBy(row => row.Candidate, StringComparer.Ordinal)
            .Select(candidate => (candidate.Key, Count: (int)EquityBrief.Core.Candidates.CandidateEvaluator.Read(candidate.OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id).Last().Parameters).GetValueOrDefault(EquityBrief.Core.Candidates.SwingFilterRule.BestOfParameter)))
            .Where(candidate => candidate.Count > 0)
            .ToDictionary(candidate => candidate.Key, candidate => candidate.Count, StringComparer.Ordinal);

    // A candidate's setups less every one listed while its own trade on the stock was still open: each rule
    // walks its own open trades, so its record counts the trades it alone would have made and never one move
    // twice, and a rule keeping the night's first so many counts those alone, in the list's own order once its
    // own open trades have kept a stock off. The walk reads the name and nothing after it hands the name on.
    // see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
    // see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
    public static IReadOnlyList<CandidateSetupRow> CandidateSetupsKept(
        IReadOnlyList<(CandidateSetupRow Row, EquityBrief.Core.Filter.OpenTradeListing Listing, EquityBrief.Core.Filter.ListOrder Order)> read,
        IReadOnlyDictionary<string, int> keptANight) =>
    [
        .. read
            .GroupBy(one => one.Row.Candidate, StringComparer.Ordinal)
            .SelectMany(candidate =>
            {
                var walked = keptANight.TryGetValue(candidate.Key, out var first)
                    ? EquityBrief.Core.Filter.OpenTrades.WalkTheFirst(candidate.Select(one => (one.Listing, one.Order)), first)
                    : EquityBrief.Core.Filter.OpenTrades.Walk(candidate.Select(one => one.Listing));

                return candidate.Where(one => walked[(one.Listing.Ticker, one.Listing.Night)] is null).Select(one => one.Row);
            }),
    ];

    // The candidates each night evaluated, which is what a candidate's first night and its family
    // are read from.
    public async Task<IReadOnlyList<CandidateNightRow>> CandidateNightsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = CandidateNights;

        var rows = new List<CandidateNightRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(1))
            {
                continue;
            }

            rows.Add(new CandidateNightRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1)));
        }

        return rows;
    }

    // The windows open now, which is what the versions region draws a row for.
    public async Task<IReadOnlyList<OpenVersionRow>> OpenVersionsAsync(string rule)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = OpenVersions;
        command.Parameters.AddWithValue("$rule", rule);

        var rows = new List<OpenVersionRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new OpenVersionRow(
                reader.GetString(0),
                reader.GetString(1),
                RuleVersions.At(reader.GetString(2))));
        }

        return rows;
    }

    // What each open version of a rule labelled the night's names, and how many of those labels
    // are not the one the night itself stored.
    public async Task<IReadOnlyList<VersionLabelRow>> VersionLabelsAsync(string rule, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = VersionLabels;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$session", On(night));

        var rows = new List<VersionLabelRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(2))
            {
                continue;
            }

            rows.Add(new VersionLabelRow(
                reader.GetString(0),
                RuleVersions.At(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return rows;
    }

    // What the night's own rule labelled the same names.
    public async Task<IReadOnlyList<LiveLabelRow>> LiveLabelsAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LiveLabels;
        command.Parameters.AddWithValue("$session", On(night));

        var rows = new List<LiveLabelRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LiveLabelRow(reader.GetString(0), reader.GetInt32(1)));
        }

        return rows;
    }

    // How often a stored label changed from one night to the next and how often the old one came
    // back, over every night the store holds, with the count of those nights.
    public async Task<(LabelReturns Returns, int Nights)> LabelReturnsAsync()
    {
        await using var connection = Open();

        int nights;

        await using (var counted = connection.CreateCommand())
        {
            counted.CommandText = NightsOfLabels;

            nights = Convert.ToInt32(await counted.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        await using var command = connection.CreateCommand();

        command.CommandText = LabelFlips;

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() && !reader.IsDBNull(0)
            ? (new LabelReturns(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)), nights)
            : (new LabelReturns(0, 0, 0, 0), nights);
    }

    // Every block of every window of a rule, as the scorer froze each when it completed. A version
    // of the trend rule only ever takes a setup away, since the label it changes is changed to the
    // one that carries no tranche at all, so a block's two sides were summed from one set of
    // stored outcomes on the night that block completed.
    // see: A trend version is judged by the candidates' test on its difference from the live rule
    // see: A version's record is read from the blocks frozen as each completed
    public async Task<IReadOnlyList<VersionBlockRow>> VersionBlocksAsync(string rule)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = VersionBlocks;
        command.Parameters.AddWithValue("$rule", rule);

        var rows = new List<VersionBlockRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new VersionBlockRow(
                reader.GetString(0),
                RuleVersions.At(reader.GetString(1)),
                new VersionBlock(
                    reader.GetInt32(2),
                    reader.GetDouble(3),
                    reader.GetInt32(4),
                    reader.GetDouble(5),
                    reader.GetInt32(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8),
                    reader.GetDouble(9),
                    reader.GetDouble(10))));
        }

        return rows;
    }

    // The year's highest high and lowest low among the stored bars up to the night,
    // each with the session it was made on, the newer of two equal ones, or none
    // where the name holds no bar.
    public async Task<YearExtremes?> YearExtremesAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = YearForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", On(asOf));

        await using var bars = await command.ExecuteReaderAsync();

        YearExtremes? year = null;

        while (await bars.ReadAsync())
        {
            var on = DateOnly.ParseExact(bars.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var high = Money.FromStorage(bars.GetString(1));
            var low = Money.FromStorage(bars.GetString(2));

            year = year is null
                ? new YearExtremes(ticker, high, on, low, on, 1)
                : year with
                {
                    High = high >= year.High ? high : year.High,
                    HighOn = high >= year.High ? on : year.HighOn,
                    Low = low <= year.Low ? low : year.Low,
                    LowOn = low <= year.Low ? on : year.LowOn,
                    Sessions = year.Sessions + 1,
                };
        }

        return year;
    }

    // Each start of the surface is a run of its own, named in UTC to the tenth of a
    // microsecond, so two starts on one store within one second each write a row.
    public static string StartRunId(DateTimeOffset startedAt) =>
        FormattableString.Invariant($"{Stage}-{startedAt.UtcDateTime:yyyyMMddTHHmmss.fffffffZ}");

    // The operational record of the read surface coming up, which section
    // 15.10's run page reads. Appended rather than updated, because the run log
    // has no updater declared and every component that writes appends to it.
    public async Task RecordStartAsync(string runId, string detail)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync();
    }

    // ---- the request store ----
    //
    // The one table this surface writes, and it writes no research: a request is
    // an ask, and what it leads to is the worker's. The statements sit here
    // rather than behind a writer of their own because the owner of a table is
    // the component whose source carries the statements, and splitting the two
    // leaves SCHEMA naming one thing and the code doing another.
    // see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours

    // A request is written only where the name holds none outstanding. The index
    // refuses the second, and the refusal is read back as the line the page
    // states rather than thrown, because a reader pressing twice has asked a
    // reasonable question.
    const string Ask = @"
        INSERT INTO research_request (ticker, asked_at, asked_from, lane, state, refresh)
        VALUES ($ticker, $asked_at, $asked_from, $lane, 'outstanding', $refresh);
    ";

    // Only a request nobody has started. The state is named in the statement
    // rather than read first, so a drain that claims the row between the screen
    // and the press moves nothing here and the reader is told which state
    // refused them.
    const string Withdraw = @"
        UPDATE research_request
        SET state = 'withdrawn', settled_at = $settled_at, reason = $reason
        WHERE ticker = $ticker AND asked_at = $asked_at AND state = 'outstanding';
    ";

    const string StateOf = @"
        SELECT state FROM research_request WHERE ticker = $ticker AND asked_at = $asked_at;
    ";

    const string Queue = @"
        SELECT ticker, asked_at, asked_from, lane, state, settled_at, run_id, reason
        FROM research_request
        ORDER BY asked_at, ticker;
    ";

    // A press asking for every section to be written again carries `refresh`, which the drain
    // hands the pass as the operator's own ask.
    // see: Nothing expires on a timer
    public async Task<RequestWritten> AskAsync(string ticker, string from, string lane, bool refresh = false)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Ask;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$asked_at", clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$asked_from", from);
        command.Parameters.AddWithValue("$lane", lane);
        command.Parameters.AddWithValue("$refresh", refresh ? 1 : 0);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException failure) when (failure.SqliteErrorCode == 19)
        {
            // Two constraints refuse an ask and they mean different things. The index over the
            // outstanding state refuses a name already waiting. The key of ticker and instant
            // refuses a press inside the same second as an earlier request for the name, which
            // may be one that has since settled or been withdrawn, and then pressing again
            // writes a new one. Where both are broken at once SQLite names either, so a key
            // collision is read as the queue refusing whenever the name has one waiting.
            var waiting = failure.SqliteExtendedErrorCode != PrimaryKeyRefused
                || await StateAsync(connection, ticker, OutstandingFor, ("$state", ResearchRequests.Outstanding)) is not null;

            if (waiting)
            {
                return new RequestWritten(
                    false,
                    $"{ticker} is already in the queue, so nothing was added: one report is asked for at a time.");
            }

            var earlier = await StateAsync(connection, ticker, StateOf, ("$asked_at", command.Parameters["$asked_at"].Value!));

            return new RequestWritten(
                false,
                $"{ticker} was asked for earlier in this same second and that request is {earlier}, so nothing was added: press again and a new request is written.");
        }

        return new RequestWritten(true, refresh ? $"{ticker} is in the queue, to have every section written again." : $"{ticker} is in the queue.");
    }

    // SQLite's extended code for a primary key refusing a row, as against a unique index.
    const int PrimaryKeyRefused = 1555;

    // ---- the quotes ----
    //
    // A name page open in the regular session asks for the delayed quote, this surface writes the ask, and the worker's
    // quote job answers it. An ask inside the interval of the name's newest is not written, so two pages open on one name
    // ask once between them, and the page draws the newest stored quote of the session it is in.
    // see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap

    const string NewestQuoteAsk = "SELECT MAX(asked_at) FROM quote_request WHERE ticker = $ticker;";

    const string AskQuote = "INSERT INTO quote_request (ticker, asked_at) VALUES ($ticker, $asked_at);";

    const string NewestQuoteOf = @"
        SELECT asked_at, quoted_at, price, previous_close, change, change_pct, night, distances
        FROM live_quote
        WHERE ticker = $ticker AND asked_at >= $from AND asked_at < $to
        ORDER BY asked_at DESC
        LIMIT 1;
    ";

    const string QuotesBetween = @"
        SELECT ticker, asked_at, answered_at, quoted_at
        FROM live_quote
        WHERE asked_at >= $from AND asked_at < $to
        ORDER BY asked_at;
    ";

    const string QuoteRunsBetween = @"
        SELECT outcome, COUNT(*)
        FROM run_log
        WHERE stage = $stage AND started_at >= $from AND started_at < $to
        GROUP BY outcome
        ORDER BY outcome;
    ";

    // Writes the ask unless the name's newest ask is inside the interval, and says which.
    public async Task<QuoteAsk> AskQuoteAsync(string ticker)
    {
        var now = clock.UtcNow;

        await using var connection = Open();
        string? newest;

        await using (var reading = connection.CreateCommand())
        {
            reading.CommandText = NewestQuoteAsk;
            reading.Parameters.AddWithValue("$ticker", ticker);
            newest = await reading.ExecuteScalarAsync() as string;
        }

        if (newest is not null
            && DateTimeOffset.TryParseExact(newest, ResearchRequests.Instant, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var last)
            && now - last < TimeSpan.FromMinutes(QuoteLimits.IntervalMinutes))
        {
            return new QuoteAsk(false, last, $"{ticker}'s quote was asked at {newest}, inside the {QuoteLimits.IntervalMinutes} minutes between two asks, so nothing was asked.");
        }

        await using var command = connection.CreateCommand();

        command.CommandText = AskQuote;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$asked_at", now.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException failure) when (failure.SqliteErrorCode == 19)
        {
            return new QuoteAsk(false, now, $"{ticker}'s quote was asked in this same second, so nothing was asked.");
        }

        return new QuoteAsk(true, now, $"{ticker}'s quote is being asked.");
    }

    // The newest quote the job stored for the name between two instants, the session's open and close; none where it
    // stored none.
    public async Task<LiveQuoteView?> NewestQuoteAsync(string ticker, DateTimeOffset from, DateTimeOffset to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestQuoteOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        using var distances = JsonDocument.Parse(reader.GetString(7));

        return new LiveQuoteView(
            Instant(reader.GetString(0)),
            Instant(reader.GetString(1)),
            Money.FromStorage(reader.GetString(2)),
            reader.IsDBNull(3) ? null : Money.FromStorage(reader.GetString(3)),
            reader.IsDBNull(4) ? null : Money.FromStorage(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : DateOnly.ParseExact(reader.GetString(6), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            [.. distances.RootElement.EnumerateArray().Select(band => new QuoteDistance(
                Money.FromStorage(band.GetProperty("low").GetString()!),
                Money.FromStorage(band.GetProperty("high").GetString()!),
                band.GetProperty("role").GetString()!,
                band.GetProperty("days").ValueKind == JsonValueKind.Number ? band.GetProperty("days").GetDouble() : null))]);
    }

    // The quotes asked between two instants, each with how long after its own time it was asked, and the quote job's runs
    // between them by what each came to, which the Run page's row for the quote runs draws against the day's cap.
    public async Task<QuoteRunsView> QuoteRunsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        await using var connection = Open();
        var asked = new List<(string Ticker, DateTimeOffset AskedAt, double DelayMinutes)>();
        var outcomes = new List<(string Outcome, int Runs)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = QuotesBetween;
            command.Parameters.AddWithValue("$from", from.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$to", to.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                asked.Add((reader.GetString(0), Instant(reader.GetString(1)), (Instant(reader.GetString(2)) - Instant(reader.GetString(3))).TotalMinutes));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = QuoteRunsBetween;
            command.Parameters.AddWithValue("$stage", QuoteStage);
            command.Parameters.AddWithValue("$from", from.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$to", to.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                outcomes.Add((reader.GetString(0), reader.GetInt32(1)));
            }
        }

        return new QuoteRunsView(from, to, asked, outcomes, QuoteLimits.DailyCap);
    }

    // The stage the quote job writes its rows under. The worker's own constant cannot be referenced from here, so the
    // word is stated and `read-surface` asserts the two agree.
    public const string QuoteStage = "quote";

    // ---- what the masthead's tiles read ----
    // see: Four tiles under the headline are worked by code from stored figures at the price the page draws

    const string TileQuartersOf = @"
        SELECT period_end, revenue, eps_actual, eps_estimate
        FROM reported_quarter
        WHERE ticker = $ticker
          AND fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = $ticker AND session_date <= $on);
    ";

    const string KeptRateOf = @"
        SELECT forward_rate FROM dividend_reading
        WHERE ticker = $ticker AND fetched_at = (SELECT MAX(fetched_at) FROM dividend_reading WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on);
    ";

    const string FetchedDividendOf = @"
        SELECT (SELECT payload FROM fundamentals_snapshot WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $on ORDER BY fetched_at DESC LIMIT 1),
               (SELECT payload FROM fundamentals WHERE ticker = $ticker AND filing_date <= $on ORDER BY filing_date DESC LIMIT 1);
    ";

    const string IndexOf = @"
        SELECT index_code FROM membership
        WHERE ticker = $ticker AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY CASE index_code WHEN 'GSPC' THEN 0 WHEN 'MID' THEN 1 ELSE 2 END
        LIMIT 1;
    ";

    // The quarters of the newest fetch made on or before the night, as the tiles read them.
    public async Task<IReadOnlyList<EquityBrief.Core.Tiles.TileQuarter>> TileQuartersAsync(string ticker, DateOnly? on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = TileQuartersOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", (on ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var quarters = new List<EquityBrief.Core.Tiles.TileQuarter>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            quarters.Add(new EquityBrief.Core.Tiles.TileQuarter(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? null : Money.FromStorage(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Money.FromStorage(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Money.FromStorage(reader.GetString(3))));
        }

        return quarters;
    }

    // The forward annual rate a share as the dividend the quarters fetch kept states it, and where none is kept as the
    // newest fundamentals fetch's dividend states it, the copy's read over the filing's; none where neither states one.
    public async Task<decimal?> ForwardRateAsync(string ticker, DateOnly? on)
    {
        var day = (on ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var connection = Open();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = KeptRateOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", day);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return reader.IsDBNull(0) ? null : Money.FromStorage(reader.GetString(0));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FetchedDividendOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", day);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                foreach (var column in new[] { 0, 1 })
                {
                    if (reader.IsDBNull(column))
                    {
                        continue;
                    }

                    using var payload = JsonDocument.Parse(reader.GetString(column));

                    if (payload.RootElement.TryGetProperty("dividend", out var part) && part.ValueKind == JsonValueKind.Object)
                    {
                        return part.TryGetProperty("forwardAnnualRate", out var rate) && rate.ValueKind == JsonValueKind.String
                            ? Money.FromStorage(rate.GetString()!)
                            : null;
                    }
                }
            }
        }

        return null;
    }

    // The index that held the name on a session, the S&P 500 first where two did.
    public async Task<string?> IndexOfAsync(string ticker, DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndexOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync() as string;
    }

    static DateTimeOffset Instant(string stamp) =>
        DateTimeOffset.ParseExact(stamp, ResearchRequests.Instant, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    const string OutstandingFor = @"
        SELECT state FROM research_request WHERE ticker = $ticker AND state = $state LIMIT 1;
    ";

    static async Task<string?> StateAsync(SqliteConnection connection, string ticker, string sql, (string Name, object Value) bound)
    {
        await using var reading = connection.CreateCommand();

        reading.CommandText = sql;
        reading.Parameters.AddWithValue("$ticker", ticker);
        reading.Parameters.AddWithValue(bound.Name, bound.Value);

        return await reading.ExecuteScalarAsync() as string;
    }

    public async Task<RequestWritten> WithdrawAsync(string ticker, DateTimeOffset askedAt)
    {
        await using var connection = Open();

        var asked = askedAt.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();

        command.CommandText = Withdraw;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$asked_at", asked);
        command.Parameters.AddWithValue("$settled_at", clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$reason", "taken out of the queue before it was written");

        if (await command.ExecuteNonQueryAsync() == 1)
        {
            return new RequestWritten(true, $"{ticker} was taken out of the queue.");
        }

        // Nothing moved, so the row is in some other state or is not there at
        // all. Which it is, is the answer: a report already being written is not
        // one that has not been generated.
        await using var reading = connection.CreateCommand();

        reading.CommandText = StateOf;
        reading.Parameters.AddWithValue("$ticker", ticker);
        reading.Parameters.AddWithValue("$asked_at", asked);

        var state = await reading.ExecuteScalarAsync() as string;

        return new RequestWritten(
            false,
            state is null
                ? $"nothing was taken out: no request for {ticker} was asked for at that instant."
                : $"nothing was taken out: {ticker}'s request is {state}, and only one nobody has started can be withdrawn.");
    }

    // The newest row of a name's passes that started at or after a request was asked for,
    // read by the order rows were written, whose run names the instant that pass started.
    const string PassSince = @"
        SELECT run_id FROM run_log
        WHERE run_id LIKE $like AND started_at >= $asked_at
        ORDER BY rowid DESC LIMIT 1;
    ";

    public async Task<IReadOnlyList<TimeSpan>> FinishedPassesAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = PassRun.FinishedPasses;

        var passes = new List<TimeSpan>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (PassRun.Took(reader.GetString(0), reader.GetString(1)) is { } took)
            {
                passes.Add(took);
            }
        }

        return passes;
    }

    // When each request being written started its pass, where its pass has written a row.
    public async Task<IReadOnlyDictionary<RequestRow, DateTimeOffset?>> PassStartsAsync(IEnumerable<RequestRow> writing)
    {
        await using var connection = Open();

        var starts = new Dictionary<RequestRow, DateTimeOffset?>();

        foreach (var row in writing)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = PassSince;
            command.Parameters.AddWithValue("$like", PassRun.Like(row.Ticker));
            command.Parameters.AddWithValue("$asked_at", row.AskedAt.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));

            starts[row] = await command.ExecuteScalarAsync() is string run ? PassRun.StartedAt(run) : null;
        }

        return starts;
    }

    public async Task<IReadOnlyList<RequestRow>> QueueAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Queue;

        var rows = new List<RequestRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RequestRow(
                reader.GetString(0),
                RequestedAt(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : RequestedAt(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows;
    }

    // The newest drain that stopped on an error, while no pass has started since it stopped: a pass started after it is
    // a later drain at work, and the stop is no longer what the queue waits on.
    // see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
    const string NewestDrainStop = @"
        SELECT s.started_at, s.ended_at, IFNULL(s.detail, '') FROM run_log s
        WHERE s.run_id LIKE $stops AND s.stage = $stage AND s.outcome = $failed
          AND NOT EXISTS (SELECT 1 FROM run_log p WHERE p.run_id LIKE $passes AND p.started_at > s.ended_at)
        ORDER BY s.ended_at DESC, s.rowid DESC
        LIMIT 1;
    ";

    public async Task<DrainStop?> DrainStoppedAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestDrainStop;
        command.Parameters.AddWithValue("$stops", DrainStops.Prefix + "%");
        command.Parameters.AddWithValue("$stage", DrainStops.Stage);
        command.Parameters.AddWithValue("$failed", DrainStops.Failed);
        command.Parameters.AddWithValue("$passes", PassRun.Prefix + "%");

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new DrainStop(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                reader.GetString(2))
            : null;
    }

    public static DateTimeOffset RequestedAt(string stored) =>
        DateTimeOffset.ParseExact(
            stored,
            ResearchRequests.Instant,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
