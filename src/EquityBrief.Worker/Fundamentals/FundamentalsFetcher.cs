using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Fundamentals;

// `QuartersReturned` is everything the payload held, `WindowTaken` the twelve most
// recent of them this component stores, and `RowsWritten` the ones the store did
// not already have. `Held` is what the name holds afterwards, which is what a
// reading over fewer than twelve carries beside itself.
public sealed record FundamentalsOutcome(
    string Ticker,
    bool Fetched,
    DateOnly? HeldBefore,
    int QuartersReturned,
    int WindowTaken,
    int RowsWritten,
    int AlreadyHeld,
    int Held,
    int QuartersWithNoFilingDate,
    IReadOnlyList<string> PartsNotCarried,
    int Requests,
    int ArchiveDocuments,
    string? SegmentReport,
    bool GuidanceLocated);

// The fundamentals fetcher. One name's quarters and balance sheet, fetched when
// the stored copy predates a filing and not otherwise.
//
// On demand and per name, which is the whole reason it is not a nightly step. The
// endpoint weighs 10 a name, so the index would cost 5,030 weighted calls a night
// and would buy nothing: a name nobody opens needs no numbers section.
// see: The nightly run is arithmetic only
// see: Everything expensive happens when a name is opened
//
// Insert only, never update, which is the ownership SCHEMA declares. A provider
// that restates a quarter files it again under a later filing date, so a
// restatement arrives as a new row and what was known at the time stays readable
// (see: Fundamentals are stored with the filing date they came from).
//
// The decision to fetch is taken from a filing date the caller passes in rather
// than from a store this component does not read. The four questions that decide
// whether a name needs anything are the staleness judge's, and it arrives at 6.5
// with the calendar read that answers the first of them; giving this component a
// calendar read of its own would be two components asking one question, and its
// catalogue row names neither the calendar nor a judge.
public sealed class FundamentalsFetcher : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            // Read as well as Insert: what is already held is what decides whether
            // a filing is new, and a fetcher that wrote every quarter it was
            // handed would rewrite eight rows on every open.
            new StoreTouch(Store.Fundamentals, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.CompanyFinancials, Feed.FilingsArchive]);

    public const string Stage = "fundamentals";

    // Which provider each part of a row came from, which is what SCHEMA's `source`
    // column holds. Named here rather than written at each site, so the file and
    // the store agree on one spelling.
    public const string Provider = "eodhd company fundamentals";

    // What this provider does not file, said as a value rather than left as a
    // missing key. A blank cell reads as a zero, and section 18 already carries
    // the row that says the numbers section marks an absence explicitly.
    public const string NotFiled = "not filed by this provider";

    // The one part of a row this component works out rather than copies, said in
    // the source column so a later reader knows which is which.
    public const string Computed = "computed from this filing";

    // What the growth part's source says: computed on this row from this filing and the
    // earlier ones the provider returned with it.
    public const string ComputedAcrossFilings = "computed from this filing and the earlier ones returned with it";

    // How many reported quarters the numbers section states, which is a display
    // decision and not this component's window.
    public const int ReportedQuarters = 5;

    // How many filings one fetch stores, which is more than the section shows.
    //
    // The provider returns years of quarters in one call, so twelve costs the same
    // request at the same weight as five and the only difference is what is
    // written. Five stored quarters yields one year-over-year comparison, because
    // the first such reading costs five quarters before it produces a value;
    // twelve yields eight, which is enough for a direction to be a direction, and
    // it makes a guide-against-actual record a count worth having rather than four
    // observations. Not more, because a company's business changes over five years
    // more than its numbers do, so the earliest quarters describe a different
    // company and widen every range they sit in, which makes today's multiple read
    // as mid-range when it is not.
    // see: Twelve filings are stored and five are shown
    //
    // Counted in filings rather than over a date range, so a company that missed a
    // filing does not silently get a shorter window than one that did not.
    public const int StoredFilings = 12;

    const string LatestHeld = @"
        SELECT MAX(filing_date) FROM fundamentals WHERE ticker = $ticker;
    ";

    const string HeldFilings = @"
        SELECT filing_date FROM fundamentals WHERE ticker = $ticker;
    ";

    const string HeldCount = @"
        SELECT COUNT(*) FROM fundamentals WHERE ticker = $ticker;
    ";

    // The conflict is ignored rather than replacing the row, which keeps the table
    // insert-only as the ownership row declares and makes a second open idempotent.
    // Replacing would be an update by another name and would need declaring as
    // one; failing would make a name opened twice an error rather than a repeat.
    // This is the shape `news_pulse` already has for the same reason.
    const string Insert = @"
        INSERT INTO fundamentals (ticker, filing_date, fetched_at, payload, source)
        VALUES ($ticker, $filing_date, $fetched_at, $payload, $source)
        ON CONFLICT (ticker, filing_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // Which provider each part of a row came from, for the parts the archive
    // supplies. The company financials endpoint files neither for anybody, which is
    // why they are a second feed and not a column that happened to be empty.
    public const string Archive = "sec edgar filings archive";

    // What the archive could not be asked for, said as a value rather than left as
    // a missing key. It is not the same statement as `NotFiled`: one is a provider
    // that files a part for nobody and the other is a read that did not happen, and
    // a row that could not tell them apart would report a company with no segments.
    public const string NotRead = "the archive was not read";

    readonly IFundamentalsFeed feed;
    readonly IFilingsArchiveFeed? archive;
    readonly IClock clock;
    readonly string databaseFile;

    // The archive is optional because the two feeds fail apart. A name whose
    // filings could not be read still has its quarters, its balance sheet and its
    // valuation, and the numbers section marks the two parts the archive supplies
    // as absent with the reason rather than withholding the whole section. The
    // alternative, refusing the fetch, would lose eight stored figures to recover
    // two (see: Fundamentals are stored with the filing date they came from).
    public FundamentalsFetcher(
        IFundamentalsFeed feed,
        IClock clock,
        string databaseFile,
        IFilingsArchiveFeed? archive = null)
    {
        this.feed = feed;
        this.archive = archive;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // `latestKnownFiling` is the date of the newest filing the caller knows this
    // name has made, which is null where the caller knows of none. A name whose
    // store already holds that filing is not fetched, and neither is a name the
    // caller knows nothing new about and the store already holds something.
    public async Task<FundamentalsOutcome> RunAsync(
        string ticker,
        DateOnly? latestKnownFiling,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var held = await LatestAsync(connection, ticker, cancellation);

        if (!NeedsFetching(held, latestKnownFiling))
        {
            var nothing = new FundamentalsOutcome(
                ticker, false, held, 0, 0, 0, 0,
                await CountAsync(connection, ticker, cancellation), 0, [], 0, 0, null, false);

            await RecordAsync(connection, runId, startedAt, nothing, cancellation);

            return nothing;
        }

        var fetched = await feed.FundamentalsAsync(ticker, cancellation).ConfigureAwait(false);

        // After the fundamentals, because the archive is addressed by the CIK that
        // payload carries, and before the write, because both providers' parts go
        // into one row and this table is never updated.
        var filings = await ArchiveAsync(ticker, fetched.Cik, cancellation).ConfigureAwait(false);

        var alreadyHeld = await FilingsAsync(connection, ticker, cancellation);

        var written = 0;
        var repeats = 0;

        // The twelve most recent filings, counted by filing date. The feed hands
        // over everything the payload holds, which is years of them, and choosing
        // the window here rather than in the parser keeps the parser a reader of
        // what the provider sent.
        var window = fetched.Filed
            .OrderByDescending(quarter => quarter.FilingDate)
            .Take(StoredFilings)
            .ToArray();

        var newest = window.Length > 0 ? window[0].FilingDate : (DateOnly?)null;

        await using (var transaction = await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var quarter in window)
            {
                if (alreadyHeld.Contains(Stored(quarter.FilingDate)))
                {
                    repeats++;

                    continue;
                }

                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Insert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$filing_date", Stored(quarter.FilingDate));
                command.Parameters.AddWithValue("$fetched_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$payload", Payload(fetched, quarter, quarter.FilingDate == newest, filings));
                command.Parameters.AddWithValue("$source", Source(fetched, filings, quarter));

                await command.ExecuteNonQueryAsync(cancellation);

                written++;
            }

            await transaction.CommitAsync(cancellation);
        }

        var outcome = new FundamentalsOutcome(
            ticker,
            true,
            held,
            fetched.Filed.Count,
            window.Length,
            written,
            repeats,
            await CountAsync(connection, ticker, cancellation),
            fetched.QuartersWithNoFilingDate,
            Absent(fetched, filings),
            feed.Requests + (archive?.Requests ?? 0),
            filings is null ? 0 : Documents(),
            filings?.Segments?.Report,
            filings?.Guidance?.Located ?? false);

        await RecordAsync(connection, runId, startedAt, outcome, cancellation);

        return outcome;
    }

    // One read of the archive, or nothing where there is none to read.
    //
    // A failure here is not a failure of the fetch. The archive is a second
    // provider with its own transport rule and its own availability, and the parts
    // it supplies are two of the ten a row carries, so an archive that refused
    // leaves those two named as unread and the other eight stored. Section 18's own
    // row for this says the numbers section shows what the provider has and marks
    // the rest absent, which is a statement about a part and not about a section.
    async Task<ArchiveFilings?> ArchiveAsync(string ticker, string cik, CancellationToken cancellation)
    {
        if (archive is null || string.IsNullOrWhiteSpace(cik))
        {
            return null;
        }

        try
        {
            return await archive.FilingsAsync(ticker, cik, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProviderRefusal)
        {
            // Swallowed here and reported on the row and on the run log, which is
            // the only place a caught refusal may go: a part recorded as unread is
            // visible on the surface the operator reads, and a part recorded as not
            // filed would not be.
            return null;
        }
    }

    // How many documents the archive was asked for, where the feed counts them.
    // The live one does, because the archive's fair-access policy is stated in
    // requests a second; the recorded one answers from a folder and counts none.
    int Documents() => archive is SecEdgarFilingsArchiveFeed live ? live.Documents : 0;

    // Which parts the archive was meant to supply and did not, with the two reasons
    // kept apart. No archive at all is a read that did not happen; an archive that
    // answered and served no segment table is the archive's own absence, which it
    // names itself.
    static IReadOnlyList<string> NotCarried(ArchiveFilings? filings) =>
        filings is null ? ArchiveParts : filings.PartsNotCarried;

    // The parts the archive supplies, which is what the source column answers about
    // when there was no archive to read.
    public static readonly string[] ArchiveParts =
        [SecEdgarArchive.Segments, SecEdgarArchive.RevenueTables, SecEdgarArchive.Guidance, SecEdgarArchive.Facts];

    // Which parts of a row are absent, over both feeds and counted once.
    //
    // The company financials feed reports the segment table and the guidance as
    // parts it does not carry, and from 6.2 that is not an absence: it is the reason
    // a second provider supplies them. So its answer about those two is dropped and
    // the archive's stands, which is the same rule the source column follows. Taking
    // the union instead would name segments twice on a row where neither provider
    // had it, and a reader counting absences would count five where there are three.
    static IReadOnlyList<string> Absent(CompanyFundamentals fetched, ArchiveFilings? filings) =>
    [
        .. fetched.PartsNotCarried.Where(part => !ArchiveParts.Contains(part, StringComparer.Ordinal)),
        .. NotCarried(filings),
    ];

    // Whether the stored copy predates a filing, which is the sentence section
    // 15.12 states and the only question this component asks.
    //
    // Nothing held means fetch, whatever the caller knows: a name with no numbers
    // section has nothing to compare and the first open is what fills it. Something
    // held and no newer filing known means fetch nothing, which is the half that
    // makes an open free (see: Deciding not to spend must not cost anything).
    public static bool NeedsFetching(DateOnly? held, DateOnly? latestKnownFiling) =>
        held is not { } stored || (latestKnownFiling is { } filed && filed > stored);

    // One filing's own figures, as the payload column holds them.
    //
    // Money as text in the invariant form, which is the storage form money takes
    // and the fault the phase 5 sign-off found in two other files: a machine with
    // a comma for a decimal point wrote figures no reader could parse back.
    //
    // The bases and the estimated quarter sit on the newest filing's row alone.
    // They are as of the fetch rather than as of a filing, so writing them onto
    // every historical row would state that the market's view of a 2024 quarter
    // was today's, and leaving them out of every row would put the valuation
    // nowhere. Section 4 asks for the valuation on each earnings basis, and the
    // basis a valuation is stated against is the latest one.
    internal static string Payload(
        CompanyFundamentals fetched,
        FiledQuarter quarter,
        bool newest,
        ArchiveFilings? filings = null) =>
        JsonSerializer.Serialize(new
        {
            periodEnd = Stored(PeriodEnd(quarter, filings) ?? quarter.PeriodEnd),
            currency = fetched.Currency,
            // The company's identifier at the filings archive, on the newest row, from
            // 6.8. The archive is addressed by it and nothing else, and a research pass
            // reads the company's own release from there without asking the company
            // financials endpoint for it a second time.
            cik = newest ? fetched.Cik : null,
            quarter = new
            {
                revenue = Money(quarter.Figures.Revenue),
                grossProfit = Money(quarter.Figures.GrossProfit),
                netIncome = Money(quarter.Figures.NetIncome),
                // The one figure on this row that is computed rather than copied,
                // and computed from the two figures beside it in the same filing.
                // Section 4's numbers row asks for a margin per quarter and the
                // provider files one for the trailing year alone, so the choice is
                // to derive it here or to show the wrong period's.
                //
                // Here rather than in the facts assembler, whose own rule is that
                // nothing in it is derived, and here rather than on the screen,
                // which reads and renders and computes nothing. A margin is a
                // property of the filing, so it belongs on the filing's row.
                // see: A screen reads and renders, and computes nothing
                grossMargin = Money(Margined(quarter.Figures.GrossProfit, quarter.Figures.Revenue)),
                netMargin = Money(Margined(quarter.Figures.NetIncome, quarter.Figures.Revenue)),
            },
            // How this quarter compares with the same quarter a year before and with the one
            // before it, computed here from the filings the provider returned, as the margin is
            // from this filing, and never on the screen or in the assembler. The quarters are
            // the ones ending within a week of a year and of three months before this one's
            // end, so a fiscal calendar ending its quarters on a weekday finds its own; with no
            // such filing returned, or an earlier figure of zero or less, there is no growth
            // rather than a guessed one.
            // see: A quarter's growth is computed on its own row from the filings the provider returned
            growth = Growth(fetched.Filed, quarter) is var (yearEarlier, quarterBefore) && (yearEarlier ?? quarterBefore) is not null ? new
            {
                yearEarlier = yearEarlier is { } year ? Stored(PeriodEnd(year, filings) ?? year.PeriodEnd) : null,
                revenue = Money(Grown(quarter.Figures.Revenue, yearEarlier?.Figures.Revenue)),
                netIncome = Money(Grown(quarter.Figures.NetIncome, yearEarlier?.Figures.NetIncome)),
                epsActual = Money(Grown(quarter.Earnings?.EpsActual, yearEarlier?.Earnings?.EpsActual)),
                quarterBefore = quarterBefore is { } before ? Stored(PeriodEnd(before, filings) ?? before.PeriodEnd) : null,
                revenueOnTheQuarterBefore = Money(Grown(quarter.Figures.Revenue, quarterBefore?.Figures.Revenue)),
            } : null,
            balanceSheet = new
            {
                totalAssets = Money(quarter.Sheet.TotalAssets),
                totalLiabilities = Money(quarter.Sheet.TotalLiabilities),
                equity = Money(quarter.Sheet.Equity),
                cash = Money(quarter.Sheet.Cash),
                netDebt = Money(quarter.Sheet.NetDebt),
            },
            earnings = quarter.Earnings is null ? null : new
            {
                reportDate = quarter.Earnings.ReportDate is { } on ? Stored(on) : null,
                timing = Filed(quarter.Earnings.Timing),
                epsActual = Money(quarter.Earnings.EpsActual),
                epsEstimate = Money(quarter.Earnings.EpsEstimate),
            },
            epsBases = newest ? new
            {
                trailing = Money(fetched.Bases.Trailing),
                currentYear = Money(fetched.Bases.CurrentYear),
                nextYear = Money(fetched.Bases.NextYear),
            } : null,
            // The valuation on each earnings basis, copied from the payload with
            // the basis beside it, so a reader can see which earnings figure a
            // ratio was struck on. A ratio has a price in it and a price moves
            // every session, which is why it sits on the newest row alone and why
            // the row carries the instant it was fetched at.
            valuation = newest ? new
            {
                trailingPe = Money(fetched.Valuation.TrailingPe),
                forwardPe = Money(fetched.Valuation.ForwardPe),
            } : null,
            // What the market says the company is worth, which section 15.9's fact
            // strip states beside the close. As of the fetch for the reason the
            // ratios are, so it sits on the newest filing's row with them.
            marketCapitalisation = newest ? Money(fetched.Market.Capitalisation) : null,
            // The next print as the provider has it, and named for what it is: an
            // analysts' estimate. Section 4 places the guided quarter at the
            // earnings release exhibit, which is management stating what it
            // expects, and this endpoint files no such thing.
            estimated = newest && fetched.Estimated is { } next ? new
            {
                periodEnd = Stored(next.PeriodEnd),
                reportDate = next.ReportDate is { } expected ? Stored(expected) : null,
                timing = Filed(next.Timing),
                epsEstimate = Money(next.EpsEstimate),
            } : null,
            // The two parts the company financials endpoint files for nobody,
            // supplied by the filings archive and on the newest filing's row
            // alone. A segment table is read from one filing's own report page and
            // the guidance from one announcement's exhibit, so writing either onto
            // a historical row would state that the 2024 quarter's segments were
            // this quarter's, and deriving them per filing would cost a request
            // per row for figures nothing reads.
            //
            // Null rather than left out where there is nothing, because a key that
            // is not there reads as this name having none, and the source column
            // beside this one says which of the three reasons it was.
            segments = newest && filings?.Segments is { } breakdown ? Table(breakdown) : null,
            // The filing's other tables of revenue by a grouping, by market, product or region,
            // on the same row for the same reason and in the segment table's own shape.
            // see: The filing's other tables of revenue by a grouping are kept beside its segment table
            revenueTables = newest && filings?.RevenueTables is { Count: > 0 } tables ? tables.Select(Table) : null,
            // Each group's change on the same months a year before, in the segment table and the
            // other revenue tables, computed from the columns each table itself states, beside the
            // tables it is computed from and so on the newest row alone.
            // see: A group's growth in a filing's own tables is computed from the columns the table states
            tableGrowth = newest && filings is not null && TableGrowth(filings) is { Count: > 0 } grown
                ? grown.Select(figure => new
                {
                    report = figure.Report,
                    group = figure.Group,
                    label = figure.Label,
                    lineItem = figure.LineItem,
                    months = figure.Months,
                    ended = figure.Ended,
                    yearEarlier = figure.YearEarlier,
                    value = figure.Value,
                })
                : null,
            // Management's own words, with the exhibit and the date they were filed
            // on, and never a figure taken out of them. A heading locates the
            // passage for five of twelve filers measured, so a passage nobody
            // located is recorded as not located and never as guidance not given.
            // see: Guidance is stored as management's own prose and never parsed into a figure
            guidance = newest && filings?.Guidance is { } guided ? new
            {
                document = guided.Document,
                filedOn = Stored(guided.FiledOn),
                located = guided.Located,
                heading = guided.Heading,
                passage = guided.Passage,
            } : null,
            // The archive's own figures, under the concept each was filed against.
            // Kept rather than mapped to a revenue, because the archive files
            // revenue under several concepts at once and keeps none of them current
            // for every filer (see: Code owns every number).
            facts = newest && filings is { Facts.Count: > 0 } ? filings.Facts.Select(fact => new
            {
                concept = fact.Concept,
                unit = fact.Unit,
                // Null for an instant, which is what a balance-sheet figure is.
                // The archive sends no start date for one at all, so a key holding
                // a zero date would state a span the filer never filed.
                start = fact.Start is { } from ? Stored(from) : null,
                end = Stored(fact.End),
                filed = Stored(fact.Filed),
                frame = fact.Frame,
                accession = fact.Accession,
                value = Money(fact.Value),
            }) : null,
        });

    // One table as the payload holds it: the report it was read from, the title and the
    // scale it states, its period columns, and its groups in the order it states them.
    static object Table(SegmentBreakdown breakdown) => new
    {
        report = breakdown.Report,
        title = breakdown.Title,
        scale = breakdown.Scale,
        periods = breakdown.Periods.Select(period => new
        {
            months = period.Months,
            ended = Stored(period.Ended),
        }),
        consolidated = Lines(breakdown.Consolidated),
        groups = breakdown.Groups.Select(group => new
        {
            label = group.Label,
            dimension = group.Dimension,
            figures = Lines(group.Figures),
        }),
    };

    // One table's rows as the payload holds them. Money as text in the invariant
    // form, which is the storage form money takes, and the unit beside a figure
    // that is not in the table's currency.
    static object Lines(IReadOnlyList<SegmentFigure> figures) =>
        figures.Select(figure => new
        {
            concept = figure.Concept,
            lineItem = figure.LineItem,
            unit = figure.Unit,
            months = figure.Period.Months,
            ended = Stored(figure.Period.Ended),
            value = Money(figure.Value),
        });

    // Which provider each part came from, which is what SCHEMA's `source` column
    // is for. The two parts this feed does not file say so by name, so a row can
    // be read later without knowing which endpoint filled it.
    internal static string Source(CompanyFundamentals fetched, ArchiveFilings? filings = null, FiledQuarter? quarter = null)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var fromArchive = NotCarried(filings);

        foreach (var part in Parts)
        {
            sources[part] = part == Margin ? Computed
                : part == GrowthPart ? ComputedAcrossFilings
                : part == TableGrowthPart ? filings is null ? NotRead : ComputedFromTables
                : part == PeriodEndPart ? quarter is { } covered && PeriodEnd(covered, filings) is not null ? Archive : Provider
                : ArchiveParts.Contains(part, StringComparer.Ordinal)
                    ? fromArchive.Contains(part, StringComparer.Ordinal)
                        ? filings is null ? NotRead : NotFiled
                        : Archive
                : fetched.PartsNotCarried.Contains(part, StringComparer.Ordinal) ? NotFiled
                : Provider;
        }

        return JsonSerializer.Serialize(sources);
    }

    // Every part a row's payload may carry, so the source column answers about
    // each of them rather than about the ones that happen to be filled. A part
    // whose source nobody stated is one a later reader cannot attribute.
    public static readonly string[] Parts =
    [
        "periodEnd", "quarter", "margin", "growth", "balanceSheet", "earnings", "epsBases", "valuation",
        "marketCapitalisation", "estimated", "segments", "revenueTables", "tableGrowth", "guidance", "facts",
    ];

    public const string Margin = "margin";

    public const string GrowthPart = "growth";

    public const string TableGrowthPart = "tableGrowth";

    // What the table growth part's source says: computed from the columns of the archive's own
    // tables, which the archive was read for.
    public const string ComputedFromTables = "computed from the filing's own tables";

    public const string PeriodEndPart = "periodEnd";

    // How far a periodic report's own period end may lie from the provider's label
    // for the quarter and still be that quarter's. The provider labels a quarter by
    // the last day of its month, and a fiscal quarter that ends on a weekday ends
    // within a week of it either side; quarters are three months apart, so a week
    // never reaches the next one.
    public const int PeriodEndDays = 7;

    // The quarter's end as the company's own periodic report states it, or null where
    // the archive was not read or indexes no report ending within a week of the
    // provider's label, which then stands.
    // see: A quarter ends on the date the company's own filing states
    internal static DateOnly? PeriodEnd(FiledQuarter quarter, ArchiveFilings? filings) =>
        filings?.Periodic
            .Where(filing => filing.PeriodEnd is { } ended && Math.Abs(ended.DayNumber - quarter.PeriodEnd.DayNumber) <= PeriodEndDays)
            .OrderBy(filing => Math.Abs(filing.PeriodEnd!.Value.DayNumber - quarter.PeriodEnd.DayNumber))
            .Select(filing => filing.PeriodEnd)
            .FirstOrDefault();

    // Money as the store holds it, which is text in the invariant form, and null
    // as null rather than as a zero: a balance sheet line the provider did not
    // file is not a company holding nothing.
    static string? Money(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    // A margin, which is one money value over another and therefore decimal on
    // both sides and decimal out: nothing crosses into the statistics world, so no
    // crossing helper is needed and none is claimed.
    //
    // A revenue of zero gives no margin rather than a division, and a revenue the
    // provider did not file gives none either. Zero would be a figure a reader
    // acts on and this system does not write one it cannot derive.
    // One figure's change on the same figure a year before, as a table states both. The group is
    // named by its position as well as its label, because two groups of one table can share a
    // label; the rows above the first grouping, the company's own, are position zero.
    internal sealed record GrownFigure(string Report, int Group, string Label, string LineItem, int Months, string Ended, string YearEarlier, string Value);

    // Every figure in the newest period of the segment table and the other revenue tables that
    // the same table states for the same months ending within a week of a year before, and its
    // change: the newest period being the newest quarter the table files, or its newest period
    // where it files no quarter.
    internal static IReadOnlyList<GrownFigure> TableGrowth(ArchiveFilings filings)
    {
        var grown = new List<GrownFigure>();

        foreach (var table in new[] { filings.Segments }.Concat(filings.RevenueTables ?? []).OfType<SegmentBreakdown>())
        {
            var quarters = table.Periods.Where(period => period.Months == QuarterMonths).ToArray();
            var latest = (quarters.Length > 0 ? quarters : table.Periods)
                .OrderBy(period => period.Ended)
                .ThenBy(period => period.Months)
                .LastOrDefault();

            var yearBefore = latest is null ? null : table.Periods
                .Where(period => period.Months == latest.Months
                    && Math.Abs(period.Ended.DayNumber - latest.Ended.AddYears(-1).DayNumber) <= PeriodEndDays)
                .OrderBy(period => Math.Abs(period.Ended.DayNumber - latest.Ended.AddYears(-1).DayNumber))
                .FirstOrDefault();

            if (latest is null || yearBefore is null)
            {
                continue;
            }

            var groups = new List<(int Position, string Label, IReadOnlyList<SegmentFigure> Figures)> { (0, CompanyRows, table.Consolidated) };

            groups.AddRange(table.Groups.Select((group, at) => (at + 1, group.Label, group.Figures)));

            foreach (var (position, label, figures) in groups)
            {
                foreach (var now in figures.Where(figure => figure.Period == latest && figure.Value is not null))
                {
                    var then = figures.FirstOrDefault(figure =>
                        figure.Period == yearBefore
                        && string.Equals(figure.Concept, now.Concept, StringComparison.Ordinal)
                        && string.Equals(figure.LineItem, now.LineItem, StringComparison.Ordinal));

                    if (Grown(now.Value, then?.Value) is { } change)
                    {
                        grown.Add(new GrownFigure(
                            table.Report, position, label, now.LineItem, latest.Months,
                            Stored(latest.Ended), Stored(yearBefore.Ended), Money(change)!));
                    }
                }
            }
        }

        return grown;
    }

    // How a table's rows above its first grouping are named, being the company's own.
    public const string CompanyRows = "total";

    // The months a quarter's column covers, which a table's newest quarter is read by.
    const int QuarterMonths = 3;

    // The quarters a year and three months before one, among the filings the provider
    // returned, each the one ending nearest that date within a week, or none.
    internal static (FiledQuarter? YearEarlier, FiledQuarter? QuarterBefore) Growth(IReadOnlyList<FiledQuarter> filed, FiledQuarter quarter)
    {
        FiledQuarter? Near(DateOnly end) => filed
            .Where(one => Math.Abs(one.PeriodEnd.DayNumber - end.DayNumber) <= PeriodEndDays)
            .OrderBy(one => Math.Abs(one.PeriodEnd.DayNumber - end.DayNumber))
            .FirstOrDefault();

        return (Near(quarter.PeriodEnd.AddYears(-1)), Near(quarter.PeriodEnd.AddMonths(-3)));
    }

    // A figure's change on an earlier one as a fraction of the earlier, rounded as a margin
    // is, and none where either is missing or the earlier is not above zero. Money on both
    // sides and decimal out, as a margin is.
    public static decimal? Grown(decimal? now, decimal? then) =>
        now is { } current && then is { } earlier && earlier > 0m
            ? decimal.Round((current - earlier) / earlier, 6, MidpointRounding.ToEven)
            : null;

    public static decimal? Margined(decimal? part, decimal? whole) =>
        part is { } numerator && whole is { } denominator && denominator != 0m
            ? decimal.Round(numerator / denominator, 6, MidpointRounding.ToEven)
            : null;

    static string Stored(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Filed(EventTiming timing) => timing switch
    {
        EventTiming.Before => "before",
        EventTiming.After => "after",
        _ => "unstated",
    };

    static async Task<DateOnly?> LatestAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = LatestHeld;
        command.Parameters.AddWithValue("$ticker", ticker);

        var found = await command.ExecuteScalarAsync(cancellation);

        return found is string stored
            && DateOnly.TryParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }

    // How many filings this name holds. Carried on every outcome and stated on the
    // run log, because a reading computed over fewer than the window it wants has
    // to say how many it had, the way an indicator row carries its bar count.
    static async Task<int> CountAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = HeldCount;
        command.Parameters.AddWithValue("$ticker", ticker);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static async Task<HashSet<string>> FilingsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = HeldFilings;
        command.Parameters.AddWithValue("$ticker", ticker);

        var filings = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            filings.Add(reader.GetString(0));
        }

        return filings;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        FundamentalsOutcome outcome,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue("$detail", Detail(outcome));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // What the operator reads on the run page about one open. A fetch that made no
    // request says so, because "nothing was written" and "nothing needed writing"
    // are different mornings.
    public static string Detail(FundamentalsOutcome outcome)
    {
        if (!outcome.Fetched)
        {
            return outcome.Ticker
                + ": nothing fetched, the stored copy holds the latest filing known ("
                + (outcome.HeldBefore is { } held ? Stored(held) : "none held")
                + "), " + Count(outcome.Held) + ", 0 request(s)";
        }

        var detail = outcome.Ticker
            + ": " + outcome.QuartersReturned.ToString(CultureInfo.InvariantCulture) + " filing(s) returned, "
            + outcome.WindowTaken.ToString(CultureInfo.InvariantCulture) + " in the window, "
            + outcome.RowsWritten.ToString(CultureInfo.InvariantCulture) + " stored, "
            + outcome.AlreadyHeld.ToString(CultureInfo.InvariantCulture) + " already held, "
            + Count(outcome.Held) + ", "
            + outcome.Requests.ToString(CultureInfo.InvariantCulture) + " request(s)";

        if (outcome.QuartersWithNoFilingDate > 0)
        {
            detail += ", " + outcome.QuartersWithNoFilingDate.ToString(CultureInfo.InvariantCulture)
                + " quarter(s) the provider filed no filing date for";
        }

        if (outcome.ArchiveDocuments > 0)
        {
            detail += ", " + outcome.ArchiveDocuments.ToString(CultureInfo.InvariantCulture)
                + " archive document(s)";
        }

        if (outcome.SegmentReport is { } report)
        {
            detail += ", segments from " + report;
        }

        if (outcome.GuidanceLocated)
        {
            detail += ", guidance located";
        }

        if (outcome.PartsNotCarried.Count > 0)
        {
            detail += ", absent: " + string.Join(", ", outcome.PartsNotCarried);
        }

        return detail;
    }

    // What the name holds, against the window that was wanted. A count short of the
    // window says so rather than being read as a full one, which is how an
    // indicator row carries its bar count and for the same reason: a reading over
    // four quarters and a reading over twelve are different readings.
    static string Count(int held) =>
        held >= StoredFilings
            ? held.ToString(CultureInfo.InvariantCulture) + " filing(s) held"
            : held.ToString(CultureInfo.InvariantCulture) + " filing(s) held of "
                + StoredFilings.ToString(CultureInfo.InvariantCulture) + " wanted";
}
