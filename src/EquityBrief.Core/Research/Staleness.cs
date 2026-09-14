using System.Globalization;

namespace EquityBrief.Core.Research;

// Where a name's research stands.
public enum ResearchState
{
    // No section has been accepted. What the page offers is to write it.
    Missing,

    // Every accepted section still stands. Nothing is spent.
    Stands,

    // At least one accepted section was written before something it should not
    // have been written before.
    Stale,
}

// The four questions, which are the whole of what makes research stale.
// see: Nothing expires on a timer
public enum StalenessTrigger
{
    NewFiling,
    EarningsPassed,
    NewsPulse,
    Refresh,
}

// The newest version of one section, as the judge reads it.
public sealed record SectionStanding(string Section, DateOnly AsOf, string Status);

// One earnings event the calendar holds, with the timing the provider filed.
public sealed record EarningsEvent(DateOnly Date, string Timing);

// One session's article count for a name.
public sealed record PulseSession(DateOnly Session, int Articles);

// The news pulse as the judge read it, with the population the baseline rests on.
//
// `BaselineWindows` is carried beside the baseline for the reason a reading over
// fewer than twelve quarters carries its count: a median over six windows and a
// median over sixty are different figures and a reader has to be able to tell.
public sealed record PulseReading(
    int WindowArticles,
    int WindowSessions,
    decimal? Baseline,
    int BaselineWindows,
    bool Above,
    DateOnly? Since);

// One trigger that made at least one section stale, with the date it is dated by
// and the line a page draws for it.
public sealed record FiredTrigger(StalenessTrigger Trigger, DateOnly Since, string Line);

public sealed record StalenessVerdict(
    DateOnly Night,
    ResearchState State,
    IReadOnlyList<FiredTrigger> Fired,
    IReadOnlyList<string> StaleSections,
    DateOnly? WrittenAsOf,
    PulseReading Pulse)
{
    // The newest version of each section the judgement was made over, whatever its
    // status. From 6.10, for the overnight queue, which asks which sections a pass would
    // write without reading the research store itself: its declared reads are the
    // listings and nothing else, and the judge has already read these.
    public IReadOnlyList<SectionStanding> Sections { get; init; } = [];

    // The one line section 15.9 promises for stale research, naming which of the
    // four fired, and the line for research never written. Held here so the page
    // and the run log say the same words.
    public string Line => State switch
    {
        ResearchState.Missing => Staleness.NotYetWritten,
        ResearchState.Stands => FormattableString.Invariant($"the research stands as written, the oldest section as of {WrittenAsOf:yyyy-MM-dd}"),
        _ => "the research is stale: " + string.Join("; ", Fired.Select(fired => fired.Line)),
    };
}

// Whether a name's stored research still stands, answered from data the store
// already holds and nothing else.
//
// Deciding not to spend must not cost anything, so every input here is something
// the nightly run or an earlier open already computed: the filings the fetcher
// stored, the events the calendar holds, the pulse the counter wrote. No request
// and no model call is made to decide whether a request or a model call is needed.
// see: Deciding not to spend must not cost anything
// see: Research goes stale on an event dated after the section was written, and a news spike is dated by the session it began
//
// Per section rather than per record, because a record is written and dated per
// section: a section written after a filing arrived is not stale because another
// was written before it.
// see: A research record is written and dated per section, not as a whole
public static class Staleness
{
    // Section 17's news trigger, read off its row by the check rather than
    // restated beside it.
    public const int WindowSessions = 5;

    public const int ArticleFloor = 5;

    public const int BaselineMultiple = 3;

    public const int BaselineDays = 90;

    public const string Accepted = "accepted";

    // The status an earnings event carries when the provider filed it before the
    // session opened. The two other timings, after and unstated, are read as a
    // print that may not have happened yet when a section dated that day was
    // written.
    public const string BeforeTheOpen = "before";

    public const string NotYetWritten = "the researched sections have not been written";

    public static StalenessVerdict Judge(
        DateOnly night,
        IReadOnlyList<SectionStanding> newestVersions,
        DateOnly? newestFiling,
        IReadOnlyList<EarningsEvent> earnings,
        IReadOnlyList<PulseSession> pulse,
        bool refresh)
    {
        var reading = Pulse(night, pulse);

        var accepted = newestVersions
            .Where(section => string.Equals(section.Status, Accepted, StringComparison.Ordinal))
            .ToArray();

        // A record is at least one accepted section. A name whose sections all fell
        // back has rows and no research, which the page already draws as sections
        // left out, and for staleness it is missing: there is nothing written to
        // go stale.
        if (accepted.Length == 0)
        {
            return new StalenessVerdict(night, ResearchState.Missing, [], [], null, reading) { Sections = newestVersions };
        }

        var stale = new HashSet<string>(StringComparer.Ordinal);
        var fired = new List<FiredTrigger>();

        void Fire(StalenessTrigger trigger, DateOnly since, string line, Func<SectionStanding, bool> makesStale)
        {
            var sections = accepted.Where(makesStale).Select(section => section.Section).ToArray();

            if (sections.Length == 0)
            {
                return;
            }

            stale.UnionWith(sections);
            fired.Add(new FiredTrigger(trigger, since, line));
        }

        // A filing dated after a section was written.
        if (newestFiling is { } filed && filed <= night)
        {
            Fire(
                StalenessTrigger.NewFiling,
                filed,
                FormattableString.Invariant($"a filing dated {filed:yyyy-MM-dd} arrived after it was written"),
                section => filed > section.AsOf);
        }

        // An earnings date that has passed since a section was written. The newest
        // one that has, because that is the print a reader would ask about.
        var passed = earnings
            .Where(earning => earning.Date <= night)
            .OrderByDescending(earning => earning.Date)
            .FirstOrDefault();

        if (passed is not null)
        {
            Fire(
                StalenessTrigger.EarningsPassed,
                passed.Date,
                FormattableString.Invariant($"the earnings date {passed.Date:yyyy-MM-dd} has passed since it was written"),
                section => PrintedAfter(passed, section.AsOf));
        }

        if (reading is { Above: true, Since: { } began })
        {
            Fire(
                StalenessTrigger.NewsPulse,
                began,
                FormattableString.Invariant($"the news pulse has been above its own baseline since {began:yyyy-MM-dd}, {reading.WindowArticles} articles in {reading.WindowSessions} sessions against a median of {reading.Baseline} over {reading.BaselineWindows} windows"),
                section => began > section.AsOf);
        }

        if (refresh)
        {
            Fire(StalenessTrigger.Refresh, night, "a rewrite was asked for", _ => true);
        }

        return new StalenessVerdict(
            night,
            stale.Count == 0 ? ResearchState.Stands : ResearchState.Stale,
            fired,
            [.. stale.Order(StringComparer.Ordinal)],
            accepted.Min(section => section.AsOf),
            reading)
        {
            Sections = newestVersions,
        };
    }

    // Whether a print happened after a section dated `asOf` was written.
    //
    // A print on a later date did. A print on the same date did too unless the
    // provider filed it before the open: a section dated the day of an after-close
    // print was most likely written before the numbers came out, and the cost of
    // reading it that way is one rewrite where it was not, against a report
    // standing on numbers that had already changed.
    public static bool PrintedAfter(EarningsEvent earning, DateOnly asOf) =>
        earning.Date > asOf
        || (earning.Date == asOf && !string.Equals(earning.Timing, BeforeTheOpen, StringComparison.Ordinal));

    // The news pulse for the sessions held on or before the night.
    //
    // The window is the last five sessions held. The baseline is the median of the
    // five-session sums for every window that ends inside the ninety days before
    // the current window begins, so a spike does not raise the baseline it is
    // measured against. A window is above where it holds at least five articles
    // and at least three times the baseline, and no baseline is no reading: with no
    // earlier window there is nothing for the relative half to be relative to, and
    // the floor alone would fire on every name the market writes about.
    //
    // A spike is dated by the session its unbroken run of windows above began,
    // which is what stops a story breaking over several days firing twice: a
    // section written during the run was written after the spike began, and the
    // windows that stay above because they still hold the story do not make it
    // stale again.
    public static PulseReading Pulse(DateOnly night, IReadOnlyList<PulseSession> pulse)
    {
        var sessions = pulse
            .Where(day => day.Session <= night)
            .OrderBy(day => day.Session)
            .ToArray();

        if (sessions.Length == 0)
        {
            return new PulseReading(0, 0, null, 0, false, null);
        }

        int Sum(int end) =>
            sessions.Skip(Math.Max(0, end - WindowSessions + 1)).Take(Math.Min(WindowSessions, end + 1)).Sum(day => day.Articles);

        var last = sessions.Length - 1;
        var windowStart = sessions[Math.Max(0, last - WindowSessions + 1)].Session;
        var from = windowStart.AddDays(-BaselineDays);

        var baselineSums = Enumerable.Range(WindowSessions - 1, Math.Max(0, sessions.Length - WindowSessions + 1))
            .Where(end => sessions[end].Session < windowStart && sessions[end].Session >= from)
            .Select(Sum)
            .Order()
            .ToArray();

        decimal? baseline = baselineSums.Length == 0 ? null : Median(baselineSums);

        bool AboveAt(int end) =>
            baseline is { } median
            && Sum(end) >= ArticleFloor
            && Sum(end) >= BaselineMultiple * median;

        var window = Sum(last);
        var above = AboveAt(last);

        DateOnly? since = null;

        if (above)
        {
            var start = last;

            while (start - 1 >= 0 && sessions[start - 1].Session >= windowStart.AddDays(-BaselineDays) && AboveAt(start - 1))
            {
                start--;
            }

            since = sessions[start].Session;
        }

        return new PulseReading(window, Math.Min(WindowSessions, sessions.Length), baseline, baselineSums.Length, above, since);
    }

    static decimal Median(IReadOnlyList<int> ordered) =>
        ordered.Count % 2 == 1
            ? ordered[ordered.Count / 2]
            : (ordered[(ordered.Count / 2) - 1] + ordered[ordered.Count / 2]) / 2m;
}
