using EquityBrief.Api.Passes;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Levels;
using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Components;
using EquityBrief.Core.Research;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using EquityBrief.Data;
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
public sealed record MoveExtremes(string Ticker, DateOnly Ended, int Sessions, decimal High, decimal Low);

// One listing row, as the store holds it.
//
// `Reasons` and `PlanAtListing` arrive as the JSON the builder wrote, because
// the read surface hands back stored values unchanged and parsing one into a
// shape would be deriving.
//
// `BandStrength` is null on a row written before the listing recorded it, which is a row the
// comparison of tonight's orders does not read.
// see: A listing records the band strength the old order read, and the three orders are compared over the nights that recorded it
public sealed record ListingRow(
    string Ticker,
    DateOnly SessionDate,
    string Reasons,
    int FiredCount,
    string PlanAtListing,
    int? BandStrength = null);

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

// One row the overnight queue wrote, as the store holds it: the night its detail names,
// the instant it started, what it came to, and the detail the run page reads the counts
// from.
public sealed record QueueRow(DateOnly Night, DateTimeOffset StartedAt, string Outcome, string Detail);

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
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed record CandidateRow(
    long Id,
    string Candidate,
    string Evaluator,
    string Event,
    string? Retires,
    DateTimeOffset RegisteredAt,
    string Parameters = "{}",
    string? Evidence = null);

// One name-night a candidate fired on, with what its setup came to.
//
// The name is not carried and no surface could draw one from this: what a record is over is a
// count of setups and the sessions they were listed on, and a candidate's evaluation of a name is
// the thing the shadow exists to keep off every screen.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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
public sealed record MoveRow(string Ticker, DateOnly SessionDate, int Sessions, double ChangePct, int Rank);

// One name's close on one session, as the day change is read from.
//
// A close and the session it is the close of, because a day change needs two of
// these and a pair with no dates on it cannot say which is the earlier. The
// subtraction is not here: this hands back the stored column and the projection
// that draws the column works out the change, which is the seam
// `UniverseScreen` already names for the distance.
// see: A screen reads and renders, and computes nothing
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
// see: A screen reads and renders, and computes nothing
public sealed class ReadApi : IComponent
{
    // Reads every store and appends to the run log, which is section 7's row
    // for this component and the R cells plus one W in its matrix row.
    //
    // Every store means every store the matrix has a column for, and from 8.4
    // that includes the candidate register: the run page states how many
    // candidates are registered and the divisor that number sets, and a count
    // drawn on a page is a count something read. It reads the register and
    // never a shadow evaluation of a name, which is the decision as it stands.
    // Series state was the one column the row left blank until the 7.0 ruling,
    // which has the name page and tonight's list say where a name's prices may
    // not reflect a dividend or split.
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Move, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.Fundamentals, Touch.Read),
            new StoreTouch(Store.NewsPulse, Touch.Read),
            new StoreTouch(Store.ResearchSection, Touch.Read),
            new StoreTouch(Store.ThemeSection, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.RuleVersion, Touch.Read),
            new StoreTouch(Store.VersionScore, Touch.Read),
            new StoreTouch(Store.VersionBlock, Touch.Read),
            new StoreTouch(Store.SeriesState, Touch.Read),
            new StoreTouch(Store.ResearchRequest, Touch.Read | Touch.Insert | Touch.Update),
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
    const string PaidCallSpends = @"
        SELECT run_id, spend
        FROM run_log
        WHERE substr(stage, 1, length($prefix)) = $prefix AND spend != $nothing;
    ";

    // The newest accepted version of each of a name's sections.
    const string WrittenSectionsForName = @"
        SELECT r.section, r.version, r.as_of, r.model, r.prose, r.source_ids
        FROM research_section r
        WHERE r.ticker = $ticker
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
        WHERE status = 'fallback' AND as_of = $night
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

    // The bars of the sessions the largest move spans, whose highest high and
    // lowest low the fact strip states.
    //
    // The move row names the session it ended on and how many sessions it spans,
    // and these are exactly those bars. The span is counted in stored sessions
    // rather than in calendar days, because a move over a week that holds a
    // holiday spans four sessions and five days, and the days would reach a bar
    // the move does not cover.
    //
    // The bars are handed back and the two extremes are chosen from them as
    // prices. Choosing which stored value to hand back is selection the read
    // surface permits, as the `MAX(as_of)` the level query uses is, but a price
    // is stored as text and the store compares text by its characters, so the
    // choice is made where the values are decimals.
    // see: A stored price is chosen and ordered by its value and never by the text it is stored as
    const string MoveExtremesForName = @"
        SELECT high, low
        FROM bar
        WHERE ticker = $ticker AND session_date <= $ended
        ORDER BY session_date DESC
        LIMIT $sessions;
    ";

    const string LargestMoveForName = @"
        SELECT session_date, sessions
        FROM move
        WHERE ticker = $ticker AND session_date <= $on
        ORDER BY rank
        LIMIT 1;
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
        SELECT ticker, session_date, sessions, change_pct, rank
        FROM move
        WHERE ticker = $ticker AND session_date <= $on
        ORDER BY rank;
    ";

    // The newest night the listings hold, so the front page resolves to it
    // without a date being asked for.
    const string NewestNight = "SELECT MAX(session_date) FROM listing WHERE session_date <= $on;";

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

    // Every listing for one night, fired and quiet alike, because the page's own
    // header states the true fired count over the whole index and the twenty
    // drawn rows cannot tell you it.
    const string ListingsForNight = @"
        SELECT ticker, session_date, reasons, fired_count, plan_at_listing, band_strength
        FROM listing
        WHERE session_date = $session_date
        ORDER BY ticker;
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
    const string EveryListing = @"
        SELECT ticker, session_date, reasons, fired_count, plan_at_listing, band_strength
        FROM listing
        ORDER BY session_date, ticker;
    ";

    // A name's own listing history, which is what the universe screen's two
    // right-hand columns count and what the listing strip draws.
    const string ListingsForName = @"
        SELECT ticker, session_date, reasons, fired_count, plan_at_listing, band_strength
        FROM listing
        WHERE ticker = $ticker AND session_date <= $on
        ORDER BY session_date DESC
        LIMIT $sessions;
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
    // accepted one a research pass wrote, and the key under each figure is not one: the
    // overnight queue writes it for every listed name each night, so counting it would call
    // most of the index researched.
    // see: A researched name is one holding an accepted section besides the key under each figure
    const string ResearchedOn = @"
               (SELECT MAX(r.as_of) FROM research_section r
                WHERE r.ticker = m.ticker AND r.status = 'accepted' AND r.section <> $computed)";

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
        WHERE r.status = 'accepted' AND r.section <> $computed
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
    // Every row the overnight queue wrote, in the order they were written. The whole log's
    // worth rather than a window, because a night the queue did not run is read against
    // the newest night before it on which it did, however long ago that was. Written order
    // rather than the instant each started, because a night run again for its session stamps
    // its stages from that session's evening, so the row written last can carry the earliest
    // instant, and the row written last is the one that says what the night came to.
    const string QueueRows = @"
        SELECT started_at, outcome, IFNULL(detail, '')
        FROM run_log
        WHERE stage = $stage
        ORDER BY rowid;
    ";

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
        SELECT id, candidate, evaluator, event, retires, registered_at, parameters, evidence
        FROM candidate_register
        ORDER BY id;
    ";

    // Every name-night a candidate fired on, with what the setup listed that night came to, the
    // bar its own plan set and the bar the calibration set for it.
    //
    // The fired ones alone. A candidate's record is over the setups it produced, and a name-night
    // it did not fire on produced none; the quiet rows are what the base rate is over and are read
    // by the query that reads them.
    // see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
    const string CandidateSetups = @"
        SELECT json_extract(c.value, '$.candidate'), l.session_date, f.outcome,
               f.null_win, f.null_win_at_sensitivity, f.break_even, f.return_pct, f.planned_risk, f.on_earnings
        FROM listing l, json_each(COALESCE(l.shadow_reasons, '{}'), '$.candidates') c
        LEFT JOIN forward_return f
            ON f.ticker = l.ticker AND f.session_date = l.session_date AND f.horizon = $horizon
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

    // The overnight queue's rows, each under the night its own detail names, which is the
    // session its arithmetic closed, and under the clock's night for a row whose detail
    // names none.
    public async Task<IReadOnlyList<QueueRow>> QueueRowsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = QueueRows;
        command.Parameters.AddWithValue("$stage", RunScreen.QueueStage);

        var rows = new List<QueueRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var started = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            var detail = reader.GetString(2);

            rows.Add(new QueueRow(RunScreen.QueueNightOf(detail) ?? clock.SessionDateAt(started), started, reader.GetString(1), detail));
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

        // The arithmetic's span, which the wall clock row bounds. The overnight queue's row
        // sits under the same run and runs for up to its own limit after the close, so a
        // span over it would read an hour of queue against a limit of minutes.
        // see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed
        var stages = rows.Where(row => row.RunId == run && row.Stage != RunScreen.QueueStage).ToArray();

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
    // see: A name's page for an earlier night is what the store held that night
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
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return rows;
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
                reader.GetInt32(4)));
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
        command.Parameters.AddWithValue("$computed", ClaimRules.ComputedSection);

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
        command.Parameters.AddWithValue("$computed", ClaimRules.ComputedSection);

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
        command.Parameters.AddWithValue("$computed", ClaimRules.ComputedSection);

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
    // see: A name's page for an earlier night is what the store held that night
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
    public async Task<IReadOnlyList<FilingRow>> FundamentalsAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = FilingsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on", On(asOf));

        var rows = new List<FilingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new FilingRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    // Where each of a name's sections stands, newest version first per section.
    public async Task<IReadOnlyList<SectionStateRow>> SectionStatesAsync(string ticker, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SectionStatesForName;
        command.Parameters.AddWithValue("$ticker", ticker);
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

        var spends = new List<(string, decimal)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            spends.Add((reader.GetString(0), decimal.Parse(reader.GetString(1), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)));
        }

        return spends;
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
                reader.IsDBNull(7) ? null : reader.GetString(7)));
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

        var rows = new List<CandidateSetupRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CandidateSetupRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                !reader.IsDBNull(8) && reader.GetInt64(8) == 1));
        }

        return rows;
    }

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

    // The largest move's own high and low, or none where the name has no move.
    public async Task<MoveExtremes?> MoveExtremesAsync(string ticker, DateOnly? asOf = null)
    {
        await using var connection = Open();

        DateOnly ended;
        int sessions;

        await using (var largest = connection.CreateCommand())
        {
            largest.CommandText = LargestMoveForName;
            largest.Parameters.AddWithValue("$ticker", ticker);
            largest.Parameters.AddWithValue("$on", On(asOf));

            await using var reader = await largest.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            ended = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            sessions = reader.GetInt32(1);
        }

        await using var command = connection.CreateCommand();

        command.CommandText = MoveExtremesForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$ended", ended.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sessions", sessions);

        await using var bars = await command.ExecuteReaderAsync();

        var highs = new List<decimal>();
        var lows = new List<decimal>();

        while (await bars.ReadAsync())
        {
            highs.Add(Money.FromStorage(bars.GetString(0)));
            lows.Add(Money.FromStorage(bars.GetString(1)));
        }

        if (highs.Count == 0)
        {
            return null;
        }

        return new MoveExtremes(ticker, ended, sessions, highs.Max(), lows.Min());
    }

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
    // see: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research

    // A request is written only where the name holds none outstanding. The index
    // refuses the second, and the refusal is read back as the line the page
    // states rather than thrown, because a reader pressing twice has asked a
    // reasonable question.
    const string Ask = @"
        INSERT INTO research_request (ticker, asked_at, asked_from, lane, state)
        VALUES ($ticker, $asked_at, $asked_from, $lane, 'outstanding');
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

    public async Task<RequestWritten> AskAsync(string ticker, string from, string lane)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Ask;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$asked_at", clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$asked_from", from);
        command.Parameters.AddWithValue("$lane", lane);

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

        return new RequestWritten(true, $"{ticker} is in the queue, and the worker writes it when it next drains.");
    }

    // SQLite's extended code for a primary key refusing a row, as against a unique index.
    const int PrimaryKeyRefused = 1555;

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

    public static DateTimeOffset RequestedAt(string stored) =>
        DateTimeOffset.ParseExact(
            stored,
            ResearchRequests.Instant,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
