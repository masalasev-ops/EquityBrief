using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what the run page draws.
//
// It sits beside the other three screen projections and in the same seam. What
// it does is count and pair: the reason records are counts over the listings and
// the forward returns, and the base rate is read from the column the filler
// already wrote beside every return.
// see: A screen reads and renders, and computes nothing
public static class RunScreen
{
    // Section 17's minimum. No verdict of any kind is reported below it, and
    // what is shown instead is the count against the minimum inside a dashed
    // outline.
    // see: An unresolved setup is never a win
    // see: Not yet measured is drawn as a dashed outline, never as a pale value
    public const int MinimumResolvedSetups = 250;

    // One row per reason, in section 11's order, whether or not it has earned a
    // number. A reason absent from the page is a reason nobody can ask about.
    //
    // The resolved count is the setups this reason produced that reached an
    // outcome. No rate is computed here and none is drawn: the share that
    // reached target before stop and the break-even those setups demanded are
    // 8.5's half of this row, and a rate over a handful of cases is a number
    // that reads as evidence and is not.
    public static IReadOnlyList<ReasonRecord> Records(
        IReadOnlyList<ListingRow> listings,
        IReadOnlyList<ResolvedSetup> resolved)
    {
        var byReason = ShortlistSeries.Reasons.ToDictionary(
            reason => reason,
            _ => (Fired: 0, Won: 0, Lost: 0, Unresolved: 0),
            StringComparer.Ordinal);

        var listedOn = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var listing in listings)
        {
            var fired = Fired(listing.Reasons);

            listedOn[Key(listing.Ticker, listing.SessionDate)] = [.. fired];

            foreach (var reason in fired)
            {
                // A stored reason the roster does not carry refuses rather than
                // being counted into whichever row is read first. A retired
                // reason or a renamed one reaches this table before it reaches
                // any code that knows about it, and a record quietly missing a
                // reason's nights is a record nobody can tell from a reason that
                // did not fire.
                if (!byReason.TryGetValue(reason, out var counted))
                {
                    throw new InvalidOperationException(
                        FormattableString.Invariant($"The listing for {listing.Ticker} on {listing.SessionDate:yyyy-MM-dd} carries the ") +
                        $"reason '{reason}', which section 11's list does not hold. A record counted over " +
                        "reasons this build does not know about would be a record about a different set of " +
                        "reasons from the one the page names.");
                }

                byReason[reason] = counted with { Fired = counted.Fired + 1 };
            }
        }

        // A setup belongs to every reason that fired on the night it was listed,
        // which is what a reason's own record is over. A setup counted against
        // one reason alone would be a record about whichever reason happened to
        // be read first.
        foreach (var setup in resolved)
        {
            if (!listedOn.TryGetValue(Key(setup.Ticker, setup.SessionDate), out var reasons))
            {
                continue;
            }

            foreach (var reason in reasons)
            {
                var counted = byReason[reason];

                byReason[reason] = setup.Outcome switch
                {
                    ForwardReturnSeries.Win => counted with { Won = counted.Won + 1 },
                    ForwardReturnSeries.Loss => counted with { Lost = counted.Lost + 1 },

                    // Matured and neither, or not yet matured. Its own state and
                    // never a smaller amount of losing.
                    _ => counted with { Unresolved = counted.Unresolved + 1 },
                };
            }
        }

        return
        [
            .. ShortlistSeries.Reasons.Select(reason =>
            {
                var counted = byReason[reason];

                return new ReasonRecord(
                    reason,
                    counted.Fired,
                    counted.Won,
                    counted.Lost,
                    counted.Unresolved,
                    MinimumResolvedSetups);
            }),
        ];
    }

    // The track the run page draws beside each record, which is 15.5's mark over
    // the counts the record already carries.
    //
    // Below the minimum the win and loss counts are handed over as one resolved
    // segment rather than as two. 15.11 gates the record column on the minimum,
    // and a win-loss split drawn beside a reason with eleven resolved setups is
    // the same figure through a second channel: a reader reads the ratio off the
    // picture, which is the thing the gate exists to prevent. The mark keeps its
    // three states as section 15.5 defines them and draws what it is given.
    // see: The record column stays empty until it has earned a number
    public static IReadOnlyList<ReasonTrackRow> Tracks(IReadOnlyList<ReasonRecord> records) =>
    [
        .. records.Select(record => record.Resolved >= record.Minimum
            ? new ReasonTrackRow(record.Reason, record.Won, record.Lost, record.Unresolved)
            : new ReasonTrackRow(record.Reason, 0, 0, record.Unresolved, record.Resolved)),
    ];

    // Tonight's own track, section 15.7's reason totals. Every name on tonight's
    // list is a setup nothing has scored yet, so each reason's whole count is
    // the unresolved state, and what the mark says is how wide each reason's bar
    // is against the busiest: one thing happening to many names, or many things
    // happening to a few.
    public static IReadOnlyList<ReasonTrackRow> Tracks(IReadOnlyList<ReasonTotal> totals) =>
    [
        .. totals.Select(total => new ReasonTrackRow(total.Reason, 0, 0, total.Names)),
    ];

    // The setups a reason's record counts: the setup horizon's rows that reached
    // an outcome. The five and twenty-one session horizons are the universe's
    // own windows and are what the base rate is over, and they are not what a
    // reason is scored on.
    public static IReadOnlyList<ResolvedSetup> Resolved(IReadOnlyList<ForwardReturnRow> returns) =>
    [
        .. returns
            .Where(row => row.Horizon == ForwardReturnSeries.Setup && row.Outcome is not null)
            .Select(row => new ResolvedSetup(row.Ticker, row.SessionDate, row.Outcome!)),
    ];

    // The universe base rate per window, read off the column the filler wrote
    // beside every return rather than counted here. A window whose rows carry no
    // rate has none, which is a night before the first fill rather than a rate
    // of zero.
    //
    // One line per window that has one, which is the two session horizons. The
    // setup horizon is not among them by rule rather than by absence, and the
    // page states that rather than leaving a gap.
    // see: Every forward-return figure is shown against the universe base rate
    // see: The `setup` horizon has no universe base rate, and the column is null for it
    public static IReadOnlyList<BaseRateLine> BaseRates(IReadOnlyList<ForwardReturnRow> returns) =>
    [
        .. ForwardReturnSeries.Horizons
            .Where(horizon => horizon != ForwardReturnSeries.Setup)
            .Select(horizon => new BaseRateLine(
                horizon,
                returns.FirstOrDefault(row => row.Horizon == horizon && row.BaseRate is not null)?.BaseRate)),
    ];

    // How many nights the record stands on, which is what makes the count
    // against the minimum readable as a distance rather than as a small number.
    // Three operating obligations are read on this page, and each of them is a
    // count of nights or of resolved setups.
    // owes: The six reason thresholds calibrated from the nights they fired on
    public static int Nights(IReadOnlyList<ListingRow> listings) =>
        listings.Select(listing => listing.SessionDate).Distinct().Count();

    // The stages of a night, in the order they ran, with each one's own elapsed
    // time. Per stage rather than in one total, because a night that landed
    // inside its limit by one step doing nothing is legible only if the steps
    // are apart.
    //
    // The subtraction is the same one the night header's duration makes: two
    // stored instants, and nothing derived from anything else.
    public static IReadOnlyList<StageRow> Stages(IReadOnlyList<RunStageRow> log) =>
    [
        .. log
            .OrderBy(row => row.StartedAt)
            .ThenBy(row => row.Stage, StringComparer.Ordinal)
            .Select(row => new StageRow(
                row.Stage,
                row.StartedAt,
                (row.EndedAt - row.StartedAt).TotalSeconds,
                row.RowsWritten,
                row.ModelCalls,
                row.NetworkRequests,
                row.Spend,
                row.Outcome,
                row.Detail)),
    ];

    // What failed, in which component, which is the second half of 15.10's stale
    // and failed region. A stage that did not end with the word its own writer
    // uses for success is one a person has to look at.
    public const string Ok = "ok";

    // A night on a day the exchange did not trade, which did what it should by
    // doing nothing and is not a failure a person has to look at. The worker's
    // own constant cannot be referenced from here, since the read surface holds
    // no reference to the worker, so the word is stated and `nightly-run`
    // asserts the two agree.
    // see: A night on a day the exchange did not trade fetches nothing and exits clean
    public const string NoSession = "no session";

    public static IReadOnlyList<StageRow> Failed(IReadOnlyList<StageRow> stages) =>
    [
        .. stages.Where(stage => !string.Equals(stage.Outcome, Ok, StringComparison.Ordinal)
            && !string.Equals(stage.Outcome, "started", StringComparison.Ordinal)
            && !string.Equals(stage.Outcome, NoSession, StringComparison.Ordinal)
            && !(string.Equals(stage.Stage, QueueStage, StringComparison.Ordinal) && string.Equals(stage.Outcome, QueueAtItsLimit, StringComparison.Ordinal))),
    ];

    // The overnight queue's own stage, and the outcome it writes where it stopped at its
    // limit with names left. Stated here for the reason the no-session word is, the read
    // surface holding no reference to the worker, and `nightly-run` asserts both agree. A
    // queue at its limit did what its limit is for, so it is not a stage that failed; a
    // queue the local model stopped is, and stays on the list.
    public const string QueueStage = "overnight queue";

    public const string QueueAtItsLimit = "limit";

    // The night a queue row's detail names, or none where it names none or is not JSON.
    public static DateOnly? QueueNightOf(string detail)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);

            return document.RootElement.TryGetProperty("night", out var night) && night.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(night.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on)
                    ? on
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The overnight queue for one night, read off the queue's rows and the exchange's
    // calendar: what the newest row for the night came to, with its counts, and every
    // traded session with no row from the one after the newest earlier night the queue ran
    // on, up to and including this night. A store with no row on or before the night says
    // the queue never ran rather than naming every night it holds from before the queue
    // existed.
    // see: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar
    public static QueueNight Queue(IReadOnlyList<QueueRow> rows, DateOnly night, Func<DateOnly, bool> traded)
    {
        var tonight = rows
            .Where(row => row.Night == night)
            .OrderByDescending(row => row.StartedAt)
            .FirstOrDefault();

        var earlier = rows.Where(row => row.Night < night).Select(row => (DateOnly?)row.Night).Max();

        if (tonight is null && earlier is null)
        {
            return new QueueNight(night, null, 0, 0, 0, 0, null, null, [], NeverRan: true);
        }

        var notRun = new List<DateOnly>();

        if (earlier is { } since)
        {
            for (var day = since.AddDays(1); day < night; day = day.AddDays(1))
            {
                if (traded(day))
                {
                    notRun.Add(day);
                }
            }
        }

        if (tonight is null && traded(night))
        {
            notRun.Add(night);
        }

        if (tonight is null)
        {
            return new QueueNight(night, null, 0, 0, 0, 0, null, null, notRun, NeverRan: false);
        }

        using var detail = JsonDocument.Parse(tonight.Detail);
        var root = detail.RootElement;

        int Count(string name) =>
            root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;

        string? Text(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return new QueueNight(
            night,
            tonight.Outcome,
            Count("queued"),
            Count("completed"),
            Count("left"),
            root.TryGetProperty("limitHours", out var hours) && hours.ValueKind == JsonValueKind.Number ? hours.GetDouble() : 0,
            Text("reason"),
            Text("awake"),
            notRun,
            NeverRan: false);
    }

    // The documents a night's passes refused, grouped by the category that
    // refused each.
    //
    // Listed rather than counted, for the reason the stale names are: a page that
    // says four documents were refused and does not say which is a page nobody
    // can act on, and the thing a person acts on is the address. Grouped by
    // category and then ordered by the address inside a group, so four refusals
    // of one kind read as a search returning marketing rather than as four
    // unrelated events.
    //
    // No date is drawn beside a row. One class of refusal is that the document
    // carried no publish date, so a column of dates would be blank for exactly
    // the rows whose reason is the blank.
    public static IReadOnlyList<RefusedDocument> Refused(IReadOnlyList<RefusedDocumentRow> rows) =>
    [
        .. rows
            .OrderBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Url, StringComparer.Ordinal)
            .Select(row => new RefusedDocument(row.Category, row.Title, row.Url)),
    ];

    // The sections that fell back on a night, a name's and a theme's, with the
    // reason each stored, in the order the store handed them over.
    public static IReadOnlyList<LeftOutSection> FellBack(IReadOnlyList<FellBackRow> rows) =>
    [
        .. rows.Select(row => new LeftOutSection(row.Subject, row.Section, row.Reason ?? string.Empty)),
    ];

    // The verdict counts from the last phase report, read out of the report the
    // harness wrote rather than counted here.
    //
    // The text is handed in rather than opened here, so nothing on the read
    // surface reaches the filesystem for it, and a machine with no report says
    // so instead of showing four zeros. Out of scope is carried separately from
    // unexamined for the reason CLAUDE.md states: only one of them is a defect,
    // and a page that summed them would report a build that has not reached a
    // claim as one that failed to check it.
    public static HarnessCounts? Harness(string? report)
    {
        if (report is not { Length: > 0 })
        {
            return null;
        }

        using var document = JsonDocument.Parse(report);

        if (!document.RootElement.TryGetProperty("summary", out var summary))
        {
            return null;
        }

        return new HarnessCounts(
            summary.GetProperty("pass").GetInt32(),
            summary.GetProperty("fail").GetInt32(),
            summary.GetProperty("unexamined").GetInt32(),
            summary.GetProperty("outOfScope").GetInt32());
    }

    static string Key(string ticker, DateOnly sessionDate) =>
        $"{ticker}|{sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    static IReadOnlyList<string> Fired(string reasons)
    {
        using var document = JsonDocument.Parse(reasons);

        return
        [
            .. document.RootElement.EnumerateArray()
                .Where(reason => reason.GetProperty("fired").GetBoolean())
                .Select(reason => reason.GetProperty("name").GetString()!),
        ];
    }

    // The paid calls the log carries a recorded cost for, counted, their passes counted
    // by the run each was made under, and summed, off the rows the read surface handed back.
    public static PricedCalls Priced(IReadOnlyList<(string RunId, decimal Spend)> spends) =>
        new(spends.Count, spends.Select(call => call.RunId).Distinct(StringComparer.Ordinal).Count(), spends.Sum(call => call.Spend));
}

// One resolved setup, as the record counts it.
public sealed record ResolvedSetup(string Ticker, DateOnly SessionDate, string Outcome);
