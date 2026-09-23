using System.Globalization;
using EquityBrief.Core.Spending;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
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
// what the row says in words, because hue is never the only channel.
public sealed record PlanRow(
    decimal LowEdge,
    decimal HighEdge,
    string Kind,
    string Detail,
    bool Traded);

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
    DateOnly? Researched = null);

// One of a name's biggest moves, as the table is given it. `Cause` is the text of
// the accepted cause section that names this move, and null where no sentence of
// it does: a researched claim, which arrives with the pass that writes it.
public sealed record MoveCell(DateOnly SessionDate, int Sessions, double ChangePct, int Rank, string? Cause = null);

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
public sealed record PricedCalls(int Count, int Passes, decimal Total);

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
    string? NoRewardToRisk = null);

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

// One horizon's result for one evening a name was on the list, as the store holds it: the
// outcome, null while the horizon has not matured, the move from that night's close, and the
// universe base rate the row carries.
public sealed record HorizonResult(string? Outcome, double? ReturnPct, double? BaseRate);

// One evening a name was on the list: the reasons that fired, the stored close that night,
// and what the two session horizons came to.
public sealed record ListingEvening(DateOnly Evening, IReadOnlyList<string> Reasons, decimal? Close, HorizonResult Five, HorizonResult TwentyOne);

// A name's listing history: whether it was on the list on each stored evening of the window,
// oldest first, and the evenings it was, newest first.
public sealed record ListingHistoryCard(IReadOnlyList<bool> Strip, IReadOnlyList<ListingEvening> Evenings);

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
// and the maximum family that number is corrected against.
//
// Two numbers and no names. The region says how hard the correction is and that
// every candidate's own record is withheld until it is promoted, and it carries
// nothing a reader could read a candidate's performance off, because the
// register is only a pre-registration for as long as nobody can see how a
// candidate is doing before deciding whether to keep it.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed record ShadowRegion(int Registered, int Divisor, int Maximum);

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

// The candidates' records, with the lifetime count beside them, the looks a verdict is read at,
// the block length and floor, and the round trip the bars carry.
public sealed record CandidateRegion(
    IReadOnlyList<CandidateRecordRow> Candidates,
    int Registered,
    int Standing,
    int Maximum,
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
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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
public sealed record ChartFrame(double? Scale = null, IReadOnlyList<DateOnly>? Markers = null);

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
            // exit are the band they sit on, which has width.
            if (row.Kind is PlanKind.Stop or PlanKind.Invalidation)
            {
                var heavy = row.Kind == PlanKind.Invalidation;

                svg.Append(Invariant, $"<line class=\"{row.Kind}-rule\" x1=\"{PlanAxis - 10}\" y1=\"{Number(bottom)}\" x2=\"{PlanWidth - 2}\" y2=\"{Number(bottom)}\" ");
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
        // them: it is the one thing the whole figure is read against.
        svg.Append(Invariant, $"<g class=\"price-marker\" data-close=\"{close}\">");
        svg.Append(Invariant, $"<line class=\"m-nowline\" x1=\"0\" y1=\"{Number(middle)}\" x2=\"{PlanWidth - 2}\" y2=\"{Number(middle)}\"/>");
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
    // figure is read beside. Every cell is a stored value, drawn at the places it
    // is read at with the stored edges on its row.
    public string PlanTables(string ticker, IReadOnlyList<PlanRow> rows)
    {
        var tranches = rows.Where(row => row.Kind == PlanKind.Tranche).ToArray();
        var exits = rows.Where(row => row.Kind == PlanKind.Exit).ToArray();

        var html = new StringBuilder();

        html.Append("<div class=\"tbl-wrap\">");
        html.Append(Invariant, $"<table class=\"tranche-table\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{tranches.Length}\">");
        html.Append("<tr><th>Zone</th><th>Condition and stop</th></tr>");

        foreach (var row in tranches)
        {
            html.Append(Invariant, $"<tr data-low-edge=\"{row.LowEdge}\" data-high-edge=\"{row.HighEdge}\"><td class=\"num\">{Zone(row)}</td>");
            html.Append(Invariant, $"<td>{Escaped(row.Detail)}</td></tr>");
        }

        html.Append("</table></div>");

        html.Append("<div class=\"tbl-wrap\">");
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

    const int ReadingHeight = 64;
    const int ReadingGap = 10;

    // The momentum panel. One small axis per reading, each with its neutral rule
    // drawn across it.
    //
    // The rule is the point of the mark rather than decoration. Section 5 says
    // an RSI near 50 is balanced and above 70 is stretched, so a reading drawn
    // without its rule is a line whose height means nothing, and the panel would
    // be four squiggles a reader has to bring their own conventions to.
    //
    // Each reading is scaled on its own axis. A MACD is in the stock's money and
    // an RSI is a score out of a hundred, so one shared scale would flatten
    // whichever of them has the smaller numbers into a straight line.
    public string MomentumPanel(string ticker, IReadOnlyList<MomentumReading> readings)
    {
        if (readings.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-readings=\"0\">" +
                $"{Escaped(ticker)} has no momentum readings stored.</p>";
        }

        var height = (readings.Count * ReadingHeight) + ((readings.Count - 1) * ReadingGap);
        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {height}\" ");
        // At the width of the chart's plot above it rather than of whatever holds it,
        // so a session is at one distance across the two and nothing here is drawn
        // larger than it was made.
        svg.Append(Invariant, $"width=\"{Width}\" height=\"{height}\" role=\"img\" class=\"momentum-panel\" data-ticker=\"{Escaped(ticker)}\" ");
        svg.Append(Invariant, $"data-readings=\"{readings.Count}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, {readings.Count} momentum reading(s)</title>");
        svg.Append(Invariant, $"<desc>Each reading on its own small axis with its neutral rule drawn across it.</desc>");

        for (var index = 0; index < readings.Count; index++)
        {
            var reading = readings[index];
            var top = index * (ReadingHeight + ReadingGap);
            var drawn = reading.Values.Where(value => value is not null).Select(value => value!.Value).ToArray();

            // The axis takes in the neutral rule as well as the values, because
            // a rule outside the scale is a rule drawn off the pane, and a
            // reading that never crossed its rule is exactly the case a reader
            // most wants to see.
            var low = reading.Floor ?? Math.Min(drawn.Length > 0 ? drawn.Min() : reading.Neutral, reading.Neutral);
            var high = reading.Ceiling ?? Math.Max(drawn.Length > 0 ? drawn.Max() : reading.Neutral, reading.Neutral);
            var span = high - low > 0 ? high - low : 1;

            double Y(double value) => top + ReadingHeight - 2 - ((value - low) / span * (ReadingHeight - 16));

            var slot = (double)(Width - (2 * Margin)) / Math.Max(reading.Values.Count, 1);

            svg.Append(Invariant, $"<g class=\"reading\" data-name=\"{Escaped(reading.Name)}\" ");
            svg.Append(Invariant, $"data-neutral=\"{Number(reading.Neutral)}\" data-values=\"{drawn.Length}\">");

            svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"{Margin}\" y=\"{top + 14}\" width=\"{Width - (2 * Margin)}\" height=\"{ReadingHeight - 14}\"/>");

            // A reading on a fixed scale carries the range it usually sits in, which is a
            // reading convention the panel draws and nothing computes with.
            // see: The momentum panel is context a reader weighs, and nothing computes with it
            if (reading.Floor is { } floor && reading.Ceiling is { } ceiling)
            {
                var usualLow = floor + ((ceiling - floor) * 0.3);
                var usualHigh = floor + ((ceiling - floor) * 0.7);

                svg.Append(Invariant, $"<rect class=\"m-neutral\" x=\"{Margin}\" y=\"{Number(Y(usualHigh))}\" width=\"{Width - (2 * Margin)}\" height=\"{Number(Y(usualLow) - Y(usualHigh))}\"/>");
            }

            // The rule first, so the reading is drawn over it.
            svg.Append(Invariant, $"<line class=\"neutral-rule\" x1=\"{Margin}\" y1=\"{Number(Y(reading.Neutral))}\" ");
            svg.Append(Invariant, $"x2=\"{Width - Margin}\" y2=\"{Number(Y(reading.Neutral))}\" ");
            svg.Append(Invariant, $"stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\" stroke-dasharray=\"3 3\"/>");
            svg.Append(Invariant, $"<text x=\"{Margin}\" y=\"{Number(top + 10)}\" fill=\"var(--muted, #6a6a6a)\" font-size=\"10\">");
            svg.Append(Invariant, $"{Escaped(ReadingName(reading.Name))}, neutral at {Number(reading.Neutral)}</text>");

            if (reading.Name == "macd_hist")
            {
                // The gap between the two lines, as bars either side of its rule: above
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
                // reading has no value until its warm-up ends.
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
                        svg.Append(Invariant, $"<path d=\"{run.ToString().Trim()}\" fill=\"none\" ");
                        svg.Append(Invariant, $"stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1.2\"/>");
                        run.Clear();
                    }
                }
            }

            // Sessions with no reading are a dashed box saying how many, never a stretch of
            // pane that reads as a quiet reading.
            // see: Not yet measured is drawn as a dashed outline, never as a pale value
            var gapStart = -1;

            for (var at = 0; at <= reading.Values.Count; at++)
            {
                var missing = at < reading.Values.Count && reading.Values[at] is null;

                if (missing && gapStart < 0)
                {
                    gapStart = at;
                }
                else if (!missing && gapStart >= 0)
                {
                    var from = Margin + (slot * gapStart);
                    var wide = slot * (at - gapStart);

                    svg.Append(Invariant, $"<g class=\"not-computed\" data-sessions=\"{at - gapStart}\"><rect class=\"m-absent\" x=\"{Number(from + 0.6)}\" y=\"{top + 15}\" width=\"{Number(Math.Max(wide - 1.2, 1))}\" height=\"{ReadingHeight - 16}\"/>");

                    if (wide > 190)
                    {
                        svg.Append(Invariant, $"<text class=\"m-absent-s\" x=\"{Number(from + 8)}\" y=\"{top + 14 + ((ReadingHeight - 14) / 2) + 4}\">{at - gapStart} of {reading.Values.Count} sessions: not yet computed</text>");
                    }

                    svg.Append("</g>");
                    gapStart = -1;
                }
            }

            svg.Append("</g>");
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    // A reading's name as a reader says it, with the name the store holds it under.
    static string ReadingName(string name) => name switch
    {
        "rsi14" => "Relative strength over 14 sessions (rsi14)",
        "macd" => "Trend momentum (macd)",
        "macd_signal" => "Its signal line (macd_signal)",
        "macd_hist" => "Momentum against its signal (macd_hist)",
        _ => name,
    };

    // The level summary table. Each band with its members and their dates.
    //
    // A table rather than a mark, and that is section 15.5's own arithmetic: it
    // states seven marks and this is not one of them. It is a region of the name
    // screen, listed in 15.9 beside the chart, and it is written here because
    // the marks and the regions that read them are drawn by the same server.
    public string LevelSummary(
        string ticker,
        IReadOnlyList<SummaryBand> bands,
        IReadOnlyList<AbsentAverage>? absent = null)
    {
        var missing = absent ?? [];

        if (bands.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"0\">" +
                $"{Escaped(ticker)} has no level bands stored.</p>";
        }

        var table = new StringBuilder();

        table.Append("<div class=\"tbl-wrap\">");
        table.Append(Invariant, $"<table class=\"level-summary\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"{bands.Count}\">");
        table.Append("<caption>Level summary, each band with its members and their dates</caption>");
        table.Append("<thead><tr><th>Band</th><th>Role</th><th>Away</th><th>Strength</th><th>Members</th></tr></thead><tbody>");

        foreach (var band in bands)
        {
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
            table.Append(Invariant, $"<td>{band.Strength}</td><td>");

            // The members one disclosure down, under a line saying how many and over which
            // sessions, since a band can rest on dozens of them.
            if (band.Members.Count > 0)
            {
                table.Append(Invariant, $"<details><summary>{band.Members.Count} member(s), {band.Members.Min(member => member.Date):yyyy-MM-dd} to {band.Members.Max(member => member.Date):yyyy-MM-dd}</summary>");
            }

            table.Append("<ul>");

            foreach (var member in band.Members)
            {
                table.Append(Invariant, $"<li class=\"member\" data-kind=\"{Escaped(member.Kind)}\" data-date=\"{member.Date:yyyy-MM-dd}\" data-price=\"{member.Price.ToString(Invariant)}\">");
                table.Append(Invariant, $"{Escaped(member.Kind)} at {Price(member.Price)} on {member.Date:yyyy-MM-dd}</li>");
            }

            table.Append(band.Members.Count > 0 ? "</ul></details></td></tr>" : "</ul></td></tr>");
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

        return table.ToString();
    }

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
                    if (bars[index].SessionDate == markers[mark])
                    {
                        at = index;
                    }
                }

                if (at < 0)
                {
                    continue;
                }

                var y = Math.Max(10, At(axis, PlotValue(bars[at].High)) - 14);

                svg.Append(Invariant, $"<g class=\"move-mark\" data-session=\"{markers[mark]:yyyy-MM-dd}\"><circle class=\"m-mark\" cx=\"{Number(Centre(at))}\" cy=\"{Number(y)}\" r=\"9\"/>");
                svg.Append(Invariant, $"<text class=\"m-mark-t\" x=\"{Number(Centre(at))}\" y=\"{Number(y + 4)}\" text-anchor=\"middle\">{mark + 1}</text></g>");
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

    // A name's listing history, section 15.9's region: the strip over the window, then one row
    // per evening the name was on the list with the reasons that fired, the close that night and
    // what followed five and twenty-one sessions on. Each result stands beside the universe base
    // rate its row carries, and an evening too recent to have matured says so. No rate is formed
    // for the name, because a record is a reason's and is measured across every name it fired on.
    // see: Every forward-return figure is shown against the universe base rate
    // see: A name's listing history states what followed each evening it was listed and forms no rate for the name
    public string ListingHistory(string ticker, ListingHistoryCard history)
    {
        var drawn = new StringBuilder();

        drawn.Append(Invariant, $"<section class=\"listing-history\" data-ticker=\"{Escaped(ticker)}\" data-sessions=\"{history.Strip.Count}\" data-evenings=\"{history.Evenings.Count}\">");
        drawn.Append(Invariant, $"<div class=\"sub\" style=\"margin-top:0\">The last {history.Strip.Count} stored sessions</div>");
        drawn.Append(ListingStrip(ticker, history.Strip));

        if (history.Evenings.Count == 0)
        {
            drawn.Append(Invariant, $"<p class=\"listing-none\">{Escaped(ticker)} was not on the list on any of these sessions.</p></section>");

            return drawn.ToString();
        }

        drawn.Append("<div class=\"tbl-wrap\"><table class=\"listing-evenings\">");
        drawn.Append("<tr><th>Evening</th><th>Why it was listed</th><th class=\"num\">Close that night</th><th>5 sessions on</th><th>21 sessions on</th></tr>");

        foreach (var evening in history.Evenings)
        {
            drawn.Append(Invariant, $"<tr data-evening=\"{evening.Evening:yyyy-MM-dd}\" data-close=\"{(evening.Close is { } stored ? stored.ToString(CultureInfo.InvariantCulture) : "none")}\"{Horizon("5", evening.Five)}{Horizon("21", evening.TwentyOne)}>");
            // The evening is a link to the page for it, which is the one place a reader
            // reaches an earlier night's page from.
            // see: A name's page for an earlier night is what the store held that night
            drawn.Append(Invariant, $"<td><a href=\"#/name/{Escaped(ticker)}/{evening.Evening:yyyy-MM-dd}\">{evening.Evening:yyyy-MM-dd}</a></td>");
            drawn.Append(Invariant, $"<td>{Escaped(string.Join(", ", evening.Reasons))}</td>");
            drawn.Append(Invariant, $"<td class=\"num\">{(evening.Close is { } close ? Figures.Price(close) : "not stored")}</td>");
            drawn.Append(Invariant, $"<td>{Result(evening.Five)}</td><td>{Result(evening.TwentyOne)}</td></tr>");
        }

        drawn.Append("</table></div></section>");

        return drawn.ToString();

        static string Horizon(string window, HorizonResult result) =>
            FormattableString.Invariant(
                $" data-outcome-{window}=\"{result.Outcome ?? "none"}\" data-return-{window}=\"{(result.ReturnPct is { } move ? move.ToString("R", CultureInfo.InvariantCulture) : "none")}\" data-base-rate-{window}=\"{(result.BaseRate is { } rate ? rate.ToString("R", CultureInfo.InvariantCulture) : "none")}\"");

        static string Result(HorizonResult result)
        {
            if (result.Outcome is null)
            {
                return "not yet matured";
            }

            var move = result.ReturnPct is { } change ? change.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%, " : string.Empty;
            var rate = result.BaseRate is { } shared
                ? "base rate " + shared.ToString("0.0", CultureInfo.InvariantCulture) + "%"
                : "base rate not yet measured";

            return Escaped($"{move}a {result.Outcome}; {rate}");
        }
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
    // One row per name that fired, in the order the rows arrive in, at most
    // twenty drawn, each numbered by its place in that order. The true count is
    // the header's headline, because a page that shows twenty every night cannot
    // tell you how busy the night was, and the line above the rows states it again
    // beside how many are drawn, because a list that does not say how long it is
    // cannot tell a reader which of its rows they are on.
    // see: The page shows twenty and states the true count
    public string TonightList(
        IReadOnlyList<ListingCell> rows,
        int drawn,
        IReadOnlyList<ReasonRecord>? records = null)
    {
        var shown = rows.Take(drawn).ToArray();
        var list = new StringBuilder();

        list.Append(Invariant, $"<section class=\"tonight-list\" data-fired=\"{rows.Count}\" data-drawn=\"{shown.Length}\">");

        if (rows.Count == 0)
        {
            list.Append("<p class=\"degraded\" data-fired=\"0\">no name fired a reason tonight</p></section>");

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
        list.Append(rows.Count > shown.Length
            ? Formatted($"Showing {shown.Length} of the {rows.Count} names that fired. <a href=\"#/universe\">See every name on the universe page</a>")
            : rows.Count == 1
                ? "Showing the one name that fired."
                : Formatted($"Showing all {rows.Count} names that fired."));
        list.Append("</p>");

        list.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table\" data-rows=\"{shown.Length}\">");
        list.Append("<thead><tr><th class=\"place\">#</th><th>Name</th><th class=\"r\">Close</th><th class=\"r\">Day</th><th>Trend</th><th class=\"c\">Distance to levels</th><th class=\"r\">Reward to risk</th>");

        foreach (var column in columns)
        {
            list.Append(Invariant, $"<th class=\"rz\"><abbr title=\"{Escaped(column)}\">{Escaped(Head(column))}</abbr></th>");
        }

        list.Append("</tr></thead><tbody>");

        foreach (var (row, place) in shown.Select((row, at) => (row, at + 1)))
        {
            list.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-fired-count=\"{row.FiredCount}\" data-strength=\"{row.Strength}\" ");
            list.Append(Invariant, $"data-day-change=\"{Change(row.DayChangePct)}\" data-trend-state=\"{Escaped(row.TrendState ?? NotClassified)}\">");

            // The row's place in the order the rows are drawn in, counted from one.
            list.Append(Invariant, $"<td class=\"place\" data-place=\"{place}\">{place}</td>");

            // The name, and the name is the link that selects this row. Section
            // 15.7's selected-name region is for whichever row is selected, and
            // a row a reader cannot select is a row the region can never be
            // about. The href carries the night as well as the name, so a
            // selected view of an earlier night is a link like every other view.
            list.Append(Invariant, $"<td class=\"c-nm\"><a class=\"select\" data-selects=\"{Escaped(row.Ticker)}\" ");
            list.Append(Invariant, $"href=\"#/night/{row.SessionDate:yyyy-MM-dd}?name={Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>");
            // What the link opens is a report where one was written and the name's
            // page where none was, so it is drawn with the words of whichever it is:
            // a link calling itself a report for a name holding none is the page
            // promising something it does not have.
            // see: A researched name is one holding an accepted section besides the key under each figure
            var researched = row.ResearchedOn is not null;

            list.Append(Invariant, $" <a class=\"open{(researched ? string.Empty : " unwritten")}\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\" ");
            list.Append(Invariant, $"data-researched=\"{(researched ? "true" : "false")}\"");

            if (row.ResearchedOn is { } on)
            {
                list.Append(Invariant, $" data-researched-on=\"{on:yyyy-MM-dd}\" title=\"open the report, written {on:yyyy-MM-dd}\">report</a>");
            }
            else
            {
                list.Append(" title=\"open the name, whose researched sections are not written\">not written</a>");

                // Asked for from the row, which is where the operator is when they see the
                // name holds none. It writes a request and starts nothing, so a row whose
                // name is already waiting is refused by the store rather than by the page
                // reading the queue first and racing itself.
                // see: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
                list.Append(Invariant, $"<form class=\"research-control ask\" method=\"post\" action=\"/passes/{Uri.EscapeDataString(row.Ticker)}\" ");
                list.Append(Invariant, $"data-kind=\"ask\" data-asks=\"{Escaped(row.Ticker)}\" data-from=\"list\">");
                list.Append("<input type=\"hidden\" name=\"from\" value=\"list\">");
                list.Append("<button type=\"submit\" title=\"put this name in the queue, which the worker drains\">ask for a report</button></form>");
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
            // showing an empty cell.
            list.Append(Invariant, $"<td class=\"trend-state\">{Escaped((row.TrendState ?? NotClassified).Replace('_', ' '))}</td>");

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

            list.Append(ReasonsForRow(row, columns, byReason));
            list.Append("</tr>");
        }

        list.Append("</tbody>");

        // Each reason's record once, at the foot of its own column: a property of the
        // reason across every name it has fired for, and never of a row's name.
        // see: A reason's record is displayed, beside the reason and never beside the name
        if (byReason is not null)
        {
            list.Append("<tfoot><tr><td colspan=\"7\" class=\"rec-lab\">Each reason's record across every name it has fired for. ");
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
            header.Append(Invariant, $"<p class=\"priced-calls\" data-calls=\"{calls.Count}\" data-passes=\"{calls.Passes}\" data-spend=\"{calls.Total}\">");
            header.Append(Invariant, $"paid calls with a recorded cost: {calls.Count} over {calls.Passes} research pass(es), costing {SpendVerdict.Money(calls.Total)} in all</p>");
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
        // see: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
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
            foreach (var (label, prose) in cases)
            {
                drawn.Append(Invariant, $"<div class=\"case\" data-case=\"{Escaped(label)}\"><h4>{Escaped(label)}</h4>");
                drawn.Append("<p class=\"prose\">").Append(Escaped(prose)).Append("</p></div>");
            }
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

    // The section holding the case for a name and the case against it, which the writer is
    // asked for as two paragraphs and which every recorded answer over the fixture and every
    // stored section on the operator's machine has been written as.
    public const string TheTwoCases = "The two cases";

    const string CaseFor = "The bull case";
    const string CaseAgainst = "The bear case";

    // The two cases as two labelled halves, where the prose is the shape the writer was asked
    // for, and nothing where it is not. A reader looking for the case against a name should not
    // have to find where one paragraph stops being the case for it.
    //
    // Read off the prose rather than assumed, and the section is drawn as it was written
    // wherever it is not recognised: what a model wrote and the checker accepted is the
    // section, and a shape this file hoped for is no reason to draw any of it differently.
    // see: A written section is broken into parts only where its own prose says where each part ends
    static IReadOnlyList<(string Label, string Prose)>? TwoCases(string section, IReadOnlyList<string> paragraphs) =>
        string.Equals(section, TheTwoCases, StringComparison.Ordinal)
            && paragraphs.Count == 2
            && paragraphs[0].StartsWith(CaseFor, StringComparison.Ordinal)
            && paragraphs[1].StartsWith(CaseAgainst, StringComparison.Ordinal)
                ? [("The case for", paragraphs[0]), ("The case against", paragraphs[1])]
                : null;

    // The section holding the risks, which the writer is asked for as a risk and then what
    // would confirm that risk, one risk after another.
    public const string TheRisks = "The risks, each with what would confirm it";

    // Where one risk ends and the next begins, as the prose states it: a sentence opening on
    // an ordinal and the word risk.
    static readonly Regex RiskOpens = new(
        @"(?:^|(?<=\.\s))(?:The|A)\s(?:first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth)\srisk\b",
        RegexOptions.CultureInvariant);

    // How a part opens what would confirm the risk it states, in the words the writer is asked
    // for.
    const string ConfirmationOpens = "That risk would be confirmed";

    // The risks as one part each, where the prose says where the parts are, and nothing where
    // it does not. Two shapes are read: a paragraph per risk, which is what a writer that broke
    // them up gives, and a sentence opening on an ordinal risk, which is what one that ran them
    // together gives. Where neither is there the section is drawn as it was written, because a
    // boundary this file guessed at would put one risk's words under another's.
    //
    // The parts are the prose cut and never edited, so joined back up they are the section as it
    // was written, which is the property the surface reads them against. Anything before the
    // first cut opens the first part rather than being dropped, so a section that introduces its
    // risks before stating them keeps the introduction.
    //
    // The order is the order they were written in. Ordering them by how severe each one is would
    // rank them on a judgement no model stated and no code computed.
    // see: A written section is broken into parts only where its own prose says where each part ends
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
        var opens = RiskOpens.Matches(whole).Select(one => one.Index).ToList();

        if (opens.Count < 2)
        {
            return null;
        }

        var edges = new List<int> { 0 };

        edges.AddRange(opens.Skip(1));
        edges.Add(whole.Length);

        return [.. Enumerable.Range(0, edges.Count - 1).Select(at => whole[edges[at]..edges[at + 1]].Trim())];
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

    // Dates and sources, section 15.9's region and section 4's last two sections: the
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
    // it is promoted. No evaluation of a name appears here, and the reason is
    // worth the sentence: the region exists to say how hard the test is, and a
    // region that also said how a candidate was doing would let the decision to
    // keep it be taken on the result, which is what registering in advance is
    // for.
    //
    // The count and the divisor are drawn from two figures computed apart, so a register and a
    // correction that disagree show it on the page rather than agreeing by construction.
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
    // see: The significance threshold is divided by the family size, and the divisor is shown
    public string ShadowCandidates(ShadowRegion shadow)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"shadow-candidates\" data-shadow=\"{shadow.Registered}\" data-divisor=\"{shadow.Divisor}\" data-maximum=\"{shadow.Maximum}\">");

        region.Append(shadow is { Registered: 0, Divisor: 0 }
            ? Formatted($"<p data-shadow=\"none\">no candidate condition is registered as this page is read, so no candidate's threshold is divided; the candidate family may hold at most {shadow.Maximum}</p>")
            : Formatted($"<p data-shadow=\"{shadow.Registered}\">{shadow.Registered} candidate condition(s) registered as this page is read, of at most {shadow.Maximum}, so a candidate's threshold is divided by {shadow.Divisor}</p>"));

        region.Append("<p data-withheld=\"true\">each candidate's own record is withheld until it is promoted, and no evaluation of a name is shown here or anywhere else</p>");

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
    // and a candidate's evaluation of a name reaches no screen until the candidate is promoted.
    // The running figure is drawn as monitoring and the verdict field beside it changes only at a
    // look, which is the whole of the arrangement: a figure read every night and acted on when it
    // looks good is the choice the looks exist to keep out of the decision.
    // see: The nightly running figure is monitoring and never the verdict
    // see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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

        drawn.Append(Invariant, $"<p data-lifetime=\"{region.Registered}\">{region.Registered} candidate condition(s) have ever been registered, of at most {region.Maximum}. ");
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

        drawn.Append("<p data-withheld=\"true\">no evaluation of a name is drawn here or anywhere else, and no record is drawn beside a ticker</p>");
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
        region.Append("<p class=\"degraded\" data-unresolved=\"all\">every name listed tonight is a setup nothing has scored yet, so these are counts and not outcomes; each reason's record over time is on the run page</p>");
        region.Append("</section>");

        return region.ToString();
    }

    // The night header, section 15.7's first region.
    //
    // The fired count is the headline, because it is the market's mood and it is
    // the one number the twenty drawn rows cannot tell you. The quantities phase
    // 6 supplies are absent and say so rather than being drawn as zero, which
    // would read as a night that spent nothing because it did nothing.
    public string NightHeader(DateOnly night, int index, int fired, string? duration, HarnessCounts? harness, NightSpend? spend = null, NightProse? prose = null)
    {
        var header = new StringBuilder();

        header.Append(Invariant, $"<header class=\"night-header\" data-night=\"{night:yyyy-MM-dd}\" ");
        header.Append(Invariant, $"data-index=\"{index}\" data-fired=\"{fired}\"><div class=\"night\">");

        // The fired count as the headline, large, with the index it is out of beneath it.
        header.Append(Invariant, $"<div class=\"headline\" aria-hidden=\"true\"><div class=\"big\">{fired}</div><div class=\"cap\">names fired<span>out of {index} in the index</span></div></div>");
        header.Append("<div class=\"ops\">");
        header.Append(Invariant, $"<p class=\"fired\">{fired} of {index} name(s) fired on {night:yyyy-MM-dd}</p>");
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

    // The watch list, section 15.7's second region: the two or three names shown
    // every evening whether or not a reason fired, above the list rather than
    // inside it.
    //
    // No store holds a watch list, and none is invented here. The region states
    // that rather than being absent, because a region a reader cannot find is
    // indistinguishable from one that is empty.
    public string WatchList(IReadOnlyList<ListingCell> watched)
    {
        var watch = new StringBuilder();

        watch.Append(Invariant, $"<section class=\"watch-list\" data-watched=\"{watched.Count}\">");

        watch.Append(watched.Count == 0
            ? "<p class=\"degraded\" data-watch=\"none\">no watch list is on file, so none is shown</p>"
            : string.Empty);

        foreach (var name in watched)
        {
            watch.Append(Invariant, $"<span class=\"watched\" data-ticker=\"{Escaped(name.Ticker)}\">{Escaped(name.Ticker)}</span>");
        }

        watch.Append("</section>");

        return watch.ToString();
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
        table.Append(Invariant, $"<figure class=\"twelve-months\" data-sessions=\"{year.Count}\">");
        table.Append(LevelChart(ticker, year, [], [], new ChartFrame(Markers: [.. moves.Select(move => move.SessionDate)])));
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
            table.Append("<tr><th>Session</th><th>Over</th><th>Change</th></tr>");
        }
        else
        {
            table.Append(Invariant, $"<table class=\"moves-table\" data-rows=\"{moves.Count}\" data-cause-column=\"written\" ");
            table.Append(Invariant, $"data-cause-as-of=\"{cause.AsOf:yyyy-MM-dd}\" data-cause-model=\"{Escaped(cause.Model)}\">");
            table.Append("<tr><th>Session</th><th>Over</th><th>Change</th><th>Cause</th></tr>");
        }

        foreach (var move in moves)
        {
            table.Append(Invariant, $"<tr data-session-date=\"{move.SessionDate:yyyy-MM-dd}\" data-sessions=\"{move.Sessions}\" ");
            table.Append(Invariant, $"data-change-pct=\"{Number(move.ChangePct)}\" data-rank=\"{move.Rank}\">");
            table.Append(Invariant, $"<td>{move.SessionDate:yyyy-MM-dd}</td>");
            table.Append(Invariant, $"<td>{(move.Sessions == 1 ? "one session" : $"{move.Sessions} sessions")}</td>");
            table.Append(Invariant, $"<td>{Number(move.ChangePct)}%</td>");

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
        const double Span = 4;

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
        table.Append("<tr><th>Name</th><th>Sector</th><th>Close</th><th>Trend</th><th>Distance</th>");
        table.Append("<th>Sessions to earnings</th><th>Last on the list</th><th>Sixty evenings</th></tr>");

        foreach (var row in rows)
        {
            table.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-sector=\"{Escaped(row.Sector)}\" ");
            table.Append(Invariant, $"data-trend-state=\"{Escaped(row.TrendState ?? NotClassified)}\">");

            // The ticker is the way to the name's page, with the company's name beneath it and
            // the day its research was written where it holds any.
            table.Append(Invariant, $"<td class=\"c-nm\"><a class=\"tk\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>");
            table.Append(row.Name is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty);
            table.Append(row.Researched is { } written
                ? $"<span class=\"researched-on\" data-researched=\"{written.ToString("yyyy-MM-dd", Invariant)}\">researched {written.ToString("yyyy-MM-dd", Invariant)}</span></td>"
                : "</td>");
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

    public string Degraded(string ticker, int bars) =>
        $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-sessions=\"{bars}\">" +
        $"{Escaped(ticker)} has {bars} stored session{(bars == 1 ? string.Empty : "s")}, " +
        $"and a chart needs at least {FewestBars}.</p>";

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
