using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Quarters;

// One member the step asked for tonight: why, the quarter awaited, what came of it, the quarters it
// stored, the weighted calls it spent, the nights it has been asked on for that quarter tonight included,
// and where it stored nothing the night it is next asked on, null where that is the next night.
public sealed record QuarterAsked(string Ticker, string Reason, DateOnly? Awaited, string Outcome, int Quarters, int Weighted, string? Detail, int Nights = 1, DateOnly? NextAsk = null);

// What one night's quarters step did: the members due, the ones asked, and the ones it left for the
// next night because its own limit or the day's allowance was reached, and the fill still owed.
public sealed record QuartersOutcome(
    int Members,
    int Due,
    IReadOnlyList<QuarterAsked> Asked,
    int LeftAtTheLimit,
    int LeftAtTheAllowance,
    int FillOwed,
    int Requests,
    int Weighted);

// The quarters fetch. After the night's close and before the overnight queue, it asks the provider for
// the reported quarters of the members due: every member once at the start, over two nights; a member
// on the first night after it reports, from the calendar; a member whose new quarter was not yet in
// the answer on each of the five nights after and weekly after that, until the quarter appears or the
// next report date passes; and a member joining the index on its first night.
//
// A per-name request on the night, carved out by name and bounded apart from the arithmetic: by its
// own limit and by the day's allowance, never by the night's deadline, the way the overnight queue is
// bounded, and it grows with the reporting calendar rather than with the index.
// see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted
// see: The nightly run is arithmetic only
public sealed class QuarterFetcher : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read | Touch.Insert),
            new StoreTouch(Store.Company, Touch.Read | Touch.Insert),
            new StoreTouch(Store.QuarterAsk, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.CompanyFinancials, Feed.HistoricalPrice]);

    public const string Stage = "quarters";

    // Why a member was asked, as `quarter_ask` holds it.
    public const string Fill = "fill";

    public const string Joined = "joined";

    public const string Report = "report";

    public const string Waiting = "waiting";

    // What came of an ask, as `quarter_ask` holds it.
    public const string Stored = "stored";

    public const string NotYetPosted = "not yet posted";

    public const string NothingReturned = "nothing returned";

    public const string Refused = "refused";

    // At most this many members are filled a night, after the members reporting and waiting, so the
    // whole index is filled over two nights and no night's step runs long.
    public const int FillPerNight = 260;

    // The step's own limit: it starts no ask once this has passed since it began, and the members
    // left wait for the next night.
    public static TimeSpan Limit { get; } = TimeSpan.FromMinutes(15);

    // A member asked again is asked on the schedule a failed refetch is: on each of these nights after
    // the first ask, then on the first night a week after the last.
    public const int RetryNights = CorporateActionChecker.RetryNights;

    public const int WeeklyRetryDays = CorporateActionChecker.WeeklyRetryDays;

    // What one ask may spend: the fundamentals, and one year of closes a year for three years at the
    // historical weight, which is one request. The prices are asked only where the answer is stored.
    public static int WeightOfAnAsk => ProviderWeights.Fundamentals + ProviderWeights.HistoricalPerTicker;

    const string MembersOn = @"
        SELECT DISTINCT ticker, joined
        FROM membership
        WHERE " + IndexScope.Condition + @"
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    // The quarters each member's newest storing fetch holds, which is what a new quarter is looked
    // for against.
    const string NewestHeld = @"
        SELECT r.ticker, r.period_end, r.report_date
        FROM reported_quarter r
        WHERE r.fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = r.ticker);
    ";

    const string EarlierAsks = @"
        SELECT ticker, session_date, awaited
        FROM quarter_ask
        WHERE session_date <= $session;
    ";

    const string FirstNight = "SELECT MIN(session_date) FROM quarter_ask;";

    // Each member's reports before tonight's session, from the calendar, with the period each covers.
    const string ReportsBefore = @"
        SELECT ticker, event_date, detail
        FROM calendar
        WHERE kind = 'earnings' AND event_date < $session;
    ";

    const string InsertQuarter = @"
        INSERT INTO reported_quarter (
            ticker, fetched_at, session_date, period_end, filing_date, report_date,
            revenue, operating_income, net_income, operating_cash_flow, eps_actual, eps_estimate,
            eps_trailing, sales_growth, sales_growth_before, operating_margin, margin_year_earlier,
            close_after, close_after_session, basis_session, basis_close, shares)
        VALUES (
            $ticker, $fetched_at, $session_date, $period_end, $filing_date, $report_date,
            $revenue, $operating_income, $net_income, $operating_cash_flow, $eps_actual, $eps_estimate,
            $eps_trailing, $sales_growth, $sales_growth_before, $operating_margin, $margin_year_earlier,
            $close_after, $close_after_session, $basis_session, $basis_close, $shares)
        ON CONFLICT (ticker, fetched_at, period_end) DO NOTHING;
    ";

    // The company one storing fetch answered for: its filer and its GICS classification, beside the quarters.
    const string InsertCompany = @"
        INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry)
        VALUES ($ticker, $fetched_at, $cik, $sector, $industry_group, $industry, $sub_industry)
        ON CONFLICT (ticker, fetched_at) DO NOTHING;
    ";

    // The members a fetch has stored a company for; the verb's companies run asks every other member.
    const string WithACompany = "SELECT DISTINCT ticker FROM company;";

    // One ask a member a night: a night run again keeps the ask it made rather than asking twice.
    const string InsertAsk = @"
        INSERT INTO quarter_ask (ticker, session_date, asked_at, reason, awaited, outcome, quarters, weighted, nights, next_ask, detail)
        VALUES ($ticker, $session_date, $asked_at, $reason, $awaited, $outcome, $quarters, $weighted, $nights, $next_ask, $detail)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly IFundamentalsFeed fundamentals;
    readonly IHistoricalBarFeed prices;
    readonly Func<int> weightedSoFar;
    readonly TimeSpan limit;
    readonly IClock clock;
    readonly string databaseFile;

    // `weightedSoFar` is the night's weighted calls so far, read before every ask, so an ask that
    // would take the night past the allowance is not made.
    public QuarterFetcher(
        IFundamentalsFeed fundamentals,
        IHistoricalBarFeed prices,
        Func<int> weightedSoFar,
        IClock clock,
        string databaseFile,
        TimeSpan? limit = null)
    {
        this.fundamentals = fundamentals;
        this.prices = prices;
        this.weightedSoFar = weightedSoFar;
        this.clock = clock;
        this.databaseFile = databaseFile;
        this.limit = limit ?? Limit;
    }

    // The run ids the step writes when a person runs it, which the run page reads as run by hand.
    public const string ByHandPrefix = "quarters-by-hand-";

    // The verb a person runs: `quarters`, the step itself outside the night, over the configured feeds and
    // store, its asks dated by the session the clock falls on. Run again on the same session it asks no
    // member that session already asked and takes the next of the fill, so a fill the night spreads over
    // two nights can be taken in one sitting; each run keeps the step's own limit and the day's allowance.
    // With `--companies` it asks instead every member no fetch has stored a company for, as a fill, storing
    // whatever quarters the answer carries, which gives each member its sector, filer and counts at once.
    public static async Task<int> RunAsync(
        string[] args,
        Func<NightFeeds> feeds,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        NightFeeds resolved;

        try
        {
            resolved = feeds();
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
        {
            error.WriteLine("quarters: " + refusal.Message);

            return 1;
        }

        var outcome = await new QuarterFetcher(resolved.Fundamentals, resolved.Historical, () => resolved.WeightedCalls, clock, databaseFile)
            .RunAsync(
                VerbArguments.Value(args, "--index") ?? "GSPC",
                FormattableString.Invariant($"{ByHandPrefix}{clock.UtcNow:yyyyMMddTHHmmss.fffffffZ}"),
                companies: VerbArguments.Has(args, "--companies"));

        output.WriteLine("quarters: " + Detail(outcome));

        return 0;
    }

    // The wider indices are the S&P 400 and 600 the night reads beside its own, whose members' quarters it asks for on
    // the schedule the index's own are.
    public async Task<QuartersOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default, bool companies = false, IReadOnlyList<string>? wider = null)
    {
        var startedAt = clock.UtcNow;
        var session = clock.SessionDateAt(startedAt);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, session, wider, cancellation);
        var held = await HeldAsync(connection, cancellation);
        var asks = await AsksAsync(connection, session, cancellation);
        var reports = await ReportsAsync(connection, session, cancellation);
        var first = await FirstNightAsync(connection, cancellation) ?? session;
        IReadOnlySet<string> withACompany = companies ? await TickersAsync(connection, WithACompany, cancellation) : new HashSet<string>(StringComparer.Ordinal);

        var due = companies
            ? [.. members
                .Where(member => !withACompany.Contains(member.Ticker) && !(asks.GetValueOrDefault(member.Ticker) ?? []).Any(ask => ask.Session == session))
                .Select(member => new Owed(member.Ticker, Fill, null, null, Company: true))]
            : members
                .Select(member => Due(member.Ticker, member.Joined, held, asks, reports, session, first))
                .OfType<Owed>()
                .ToArray();

        var owedFill = due.Where(owed => owed.Reason == Fill).ToArray();

        var ordered = due
            .Where(owed => owed.Reason != Fill)
            .OrderBy(owed => owed.Ticker, StringComparer.Ordinal)
            .Concat(owedFill.OrderBy(owed => owed.Ticker, StringComparer.Ordinal).Take(companies ? owedFill.Length : FillPerNight))
            .ToArray();

        var asked = new List<QuarterAsked>();
        var (atTheLimit, atTheAllowance) = (0, 0);
        var requestsBefore = fundamentals.Requests + prices.Requests;
        var weightedBefore = Weighted();

        foreach (var owed in ordered)
        {
            if (clock.UtcNow - startedAt >= limit)
            {
                atTheLimit = ordered.Length - asked.Count;
                break;
            }

            if (weightedSoFar() + WeightOfAnAsk > ProviderWeights.DailyAllowance)
            {
                atTheAllowance = ordered.Length - asked.Count;
                break;
            }

            asked.Add(await AskAsync(connection, owed, session, held.GetValueOrDefault(owed.Ticker), cancellation));
        }

        var outcome = new QuartersOutcome(
            members.Count,
            due.Length,
            asked,
            atTheLimit,
            atTheAllowance,
            owedFill.Length - asked.Count(one => one.Reason == Fill),
            fundamentals.Requests + prices.Requests - requestsBefore,
            Weighted() - weightedBefore);

        await RecordAsync(connection, runId, startedAt, outcome, cancellation);

        return outcome;
    }

    int Weighted() =>
        (fundamentals.Requests * ProviderWeights.Fundamentals) + (prices.Requests * ProviderWeights.HistoricalPerTicker);

    // A member owed an ask tonight, why, the quarter awaited where one is, the nights it was asked on for
    // that quarter before tonight, and whether it is asked for its company, whatever quarters it holds.
    sealed record Owed(string Ticker, string Reason, DateOnly? Awaited, DateOnly? ReportedOn, int AskedBefore = 0, bool Company = false);

    // Whether a member is owed an ask tonight.
    //
    // A member holding no quarter and never asked is owed its fill, or, joining after the fill began,
    // its first night's. One holding none and asked before is marked absent and asked on the schedule,
    // and again on the first night after a report since its last ask. One holding quarters is owed the
    // quarter its newest report covers, from the first night after that report until an answer carries
    // it, on the schedule after the first ask; a later report replaces the quarter awaited.
    static Owed? Due(
        string ticker,
        DateOnly? joined,
        IReadOnlyDictionary<string, IReadOnlyList<HeldQuarter>> held,
        IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, DateOnly? Awaited)>> asks,
        IReadOnlyDictionary<string, (DateOnly On, DateOnly? Period)> reports,
        DateOnly session,
        DateOnly first)
    {
        var earlier = asks.GetValueOrDefault(ticker) ?? [];

        // A night run again for its session keeps the ask it made.
        if (earlier.Any(ask => ask.Session == session))
        {
            return null;
        }

        var before = earlier.Where(ask => ask.Session < session).Select(ask => ask.Session).Order().ToArray();
        var quarters = held.GetValueOrDefault(ticker) ?? [];
        var reported = reports.TryGetValue(ticker, out var newest) ? newest : ((DateOnly On, DateOnly? Period)?)null;

        if (quarters.Count == 0)
        {
            if (before.Length == 0)
            {
                return new Owed(ticker, joined is { } member && member > first ? Joined : Fill, null, null);
            }

            if (reported is { } since && since.On >= before[^1])
            {
                return new Owed(ticker, Report, since.Period, since.On);
            }

            return Scheduled(before, session) ? new Owed(ticker, Waiting, null, null, before.Length) : null;
        }

        if (reported is not { } report || Covered(quarters, report))
        {
            return null;
        }

        var askedSince = before.Where(on => on > report.On).ToArray();

        return askedSince.Length == 0
            ? new Owed(ticker, Report, report.Period, report.On)
            : Scheduled(askedSince, session) ? new Owed(ticker, Waiting, report.Period, report.On, askedSince.Length) : null;
    }

    // Whether a report's quarter is among the ones held: its period end within a week of one held, or,
    // where the calendar states no period, a quarter held reported on or after it.
    static bool Covered(IReadOnlyList<HeldQuarter> quarters, (DateOnly On, DateOnly? Period) report) =>
        report.Period is { } period
            ? quarters.Any(quarter => Math.Abs(quarter.PeriodEnd.DayNumber - period.DayNumber) <= QuarterFetch.NearDays)
            : quarters.Any(quarter => quarter.ReportDate is { } on && on >= report.On);

    // The first ask and each of the nights after it up to the retries, then a week after the last.
    static bool Scheduled(IReadOnlyList<DateOnly> asked, DateOnly session) =>
        asked.Count <= RetryNights || session >= asked[^1].AddDays(WeeklyRetryDays);

    async Task<QuarterAsked> AskAsync(SqliteConnection connection, Owed owed, DateOnly session, IReadOnlyList<HeldQuarter>? quarters, CancellationToken cancellation)
    {
        var askedAt = clock.UtcNow;
        var spentBefore = Weighted();
        string outcome;
        var stored = 0;
        string? detail = null;

        try
        {
            var fetched = await fundamentals.FundamentalsAsync(owed.Ticker, cancellation).ConfigureAwait(false);

            if (fetched.Filed.Count == 0)
            {
                outcome = NothingReturned;
            }
            else if (!Carries(fetched, owed, quarters))
            {
                outcome = NotYetPosted;
                detail = FormattableString.Invariant($"the answer's newest quarter ends {fetched.Filed.Max(quarter => quarter.PeriodEnd):yyyy-MM-dd}");
            }
            else
            {
                IReadOnlyList<ProviderBar> closes;

                try
                {
                    closes = await prices.BarsAsync(owed.Ticker, session.AddYears(-QuarterFetch.PriceYears), session, cancellation).ConfigureAwait(false);
                }
                catch (ProviderRefusal refusal)
                {
                    // The quarters stand without their closes: the valuation reads too few, and the
                    // other three readings read what the quarters carry.
                    closes = [];
                    detail = "no closes: " + refusal.Message;
                }

                stored = await StoreAsync(connection, owed.Ticker, askedAt, session, QuarterFetch.From(fetched, closes), fetched, cancellation);
                outcome = Stored;
            }
        }
        catch (ProviderRefusal refusal)
        {
            outcome = Refused;
            detail = refusal.Message;
        }
        catch (FormatException unreadable)
        {
            outcome = Refused;
            detail = unreadable.Message;
        }

        // The nights asked for this quarter tonight included, and where nothing was stored the night it is
        // next asked on: the next night while its asks after the first number no more than the retries, a
        // week after tonight once they do.
        var nights = owed.AskedBefore + 1;
        DateOnly? next = outcome == Stored || nights <= RetryNights ? null : session.AddDays(WeeklyRetryDays);

        var asked = new QuarterAsked(owed.Ticker, owed.Reason, owed.Awaited, outcome, stored, Weighted() - spentBefore, detail, nights, next);

        await using var command = connection.CreateCommand();

        command.CommandText = InsertAsk;
        command.Parameters.AddWithValue("$ticker", asked.Ticker);
        command.Parameters.AddWithValue("$session_date", Stamp(session));
        command.Parameters.AddWithValue("$asked_at", Instant(askedAt));
        command.Parameters.AddWithValue("$reason", asked.Reason);
        command.Parameters.AddWithValue("$awaited", asked.Awaited is { } awaited ? Stamp(awaited) : DBNull.Value);
        command.Parameters.AddWithValue("$outcome", asked.Outcome);
        command.Parameters.AddWithValue("$quarters", asked.Quarters);
        command.Parameters.AddWithValue("$weighted", asked.Weighted);
        command.Parameters.AddWithValue("$nights", asked.Nights);
        command.Parameters.AddWithValue("$next_ask", asked.NextAsk is { } nextOn ? Stamp(nextOn) : DBNull.Value);
        command.Parameters.AddWithValue("$detail", asked.Detail is { } said ? said : DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellation);

        return asked;
    }

    // Whether an answer carries what the ask was for: the quarter awaited, within a week of its end;
    // for a member holding quarters and awaiting no stated period, a quarter newer than the newest held;
    // and for a fill, a joiner or a member marked absent, any quarter at all.
    static bool Carries(CompanyFundamentals fetched, Owed owed, IReadOnlyList<HeldQuarter>? quarters)
    {
        // A member asked for its company is stored whatever quarters the answer carries.
        if (owed.Company)
        {
            return true;
        }

        if (owed.Awaited is { } awaited)
        {
            return fetched.Filed.Any(quarter => Math.Abs(quarter.PeriodEnd.DayNumber - awaited.DayNumber) <= QuarterFetch.NearDays);
        }

        return quarters is not { Count: > 0 } || fetched.Filed.Max(quarter => quarter.PeriodEnd) > quarters.Max(quarter => quarter.PeriodEnd);
    }

    // One fetch's quarters and the company it answered for, written at the instant it was made, all or none.
    static async Task<int> StoreAsync(SqliteConnection connection, string ticker, DateTimeOffset fetchedAt, DateOnly session, QuarterFetch fetch, CompanyFundamentals fetched, CancellationToken cancellation)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var written = 0;

        foreach (var quarter in fetch.Quarters)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = InsertQuarter;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$fetched_at", Instant(fetchedAt));
            command.Parameters.AddWithValue("$session_date", Stamp(session));
            command.Parameters.AddWithValue("$period_end", Stamp(quarter.PeriodEnd));
            command.Parameters.AddWithValue("$filing_date", Date(quarter.FilingDate));
            command.Parameters.AddWithValue("$report_date", Date(quarter.ReportDate));
            command.Parameters.AddWithValue("$revenue", Figure(quarter.Revenue));
            command.Parameters.AddWithValue("$operating_income", Figure(quarter.OperatingIncome));
            command.Parameters.AddWithValue("$net_income", Figure(quarter.NetIncome));
            command.Parameters.AddWithValue("$operating_cash_flow", Figure(quarter.OperatingCashFlow));
            command.Parameters.AddWithValue("$eps_actual", Figure(quarter.EpsActual));
            command.Parameters.AddWithValue("$eps_estimate", Figure(quarter.EpsEstimate));
            command.Parameters.AddWithValue("$eps_trailing", Figure(quarter.EpsTrailing));
            command.Parameters.AddWithValue("$sales_growth", Figure(quarter.SalesGrowth));
            command.Parameters.AddWithValue("$sales_growth_before", Figure(quarter.SalesGrowthBefore));
            command.Parameters.AddWithValue("$operating_margin", Figure(quarter.OperatingMargin));
            command.Parameters.AddWithValue("$margin_year_earlier", Figure(quarter.MarginYearEarlier));
            command.Parameters.AddWithValue("$close_after", Figure(quarter.CloseAfter));
            command.Parameters.AddWithValue("$close_after_session", Date(quarter.CloseAfterSession));
            command.Parameters.AddWithValue("$basis_session", Date(fetch.BasisSession));
            command.Parameters.AddWithValue("$basis_close", Figure(fetch.BasisClose));
            command.Parameters.AddWithValue("$shares", Figure(quarter.Shares));

            written += await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var company = connection.CreateCommand())
        {
            company.Transaction = (SqliteTransaction)transaction;
            company.CommandText = InsertCompany;
            company.Parameters.AddWithValue("$ticker", ticker);
            company.Parameters.AddWithValue("$fetched_at", Instant(fetchedAt));
            company.Parameters.AddWithValue("$cik", Filer(fetched.Cik) is { } cik ? cik : DBNull.Value);
            company.Parameters.AddWithValue("$sector", (object?)fetched.Classification?.Sector ?? DBNull.Value);
            company.Parameters.AddWithValue("$industry_group", (object?)fetched.Classification?.IndustryGroup ?? DBNull.Value);
            company.Parameters.AddWithValue("$industry", (object?)fetched.Classification?.Industry ?? DBNull.Value);
            company.Parameters.AddWithValue("$sub_industry", (object?)fetched.Classification?.SubIndustry ?? DBNull.Value);

            await company.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return written;
    }

    // The CIK padded to the ten digits the archive and the pulled companies carry it in, none where none is filed.
    static string? Filer(string? cik)
    {
        var digits = new string([.. (cik ?? string.Empty).Where(char.IsAsciiDigit)]).TrimStart('0');

        return digits.Length == 0 ? null : digits.PadLeft(10, '0');
    }

    // A quarter held, as the ask reads it: its end and the date it was reported on.
    sealed record HeldQuarter(DateOnly PeriodEnd, DateOnly? ReportDate);

    static async Task<IReadOnlySet<string>> TickersAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        var tickers = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    static async Task<IReadOnlyList<(string Ticker, DateOnly? Joined)>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        IndexScope.Bind(command, indexCode, wider);
        command.Parameters.AddWithValue("$session", Stamp(session));

        var members = new Dictionary<string, DateOnly?>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var joined = reader.IsDBNull(1) ? (DateOnly?)null : Parse(reader.GetString(1));

            // A ticker with two current spans is one member, joined by the earlier.
            members[reader.GetString(0)] = members.TryGetValue(reader.GetString(0), out var known) && known is { } was && (joined is null || was < joined)
                ? was
                : joined;
        }

        return [.. members.Select(pair => (pair.Key, pair.Value)).OrderBy(member => member.Key, StringComparer.Ordinal)];
    }

    static async Task<IReadOnlyDictionary<string, IReadOnlyList<HeldQuarter>>> HeldAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestHeld;

        var held = new Dictionary<string, List<HeldQuarter>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!held.TryGetValue(reader.GetString(0), out var quarters))
            {
                held[reader.GetString(0)] = quarters = [];
            }

            quarters.Add(new HeldQuarter(Parse(reader.GetString(1)), reader.IsDBNull(2) ? null : Parse(reader.GetString(2))));
        }

        return held.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<HeldQuarter>)pair.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, DateOnly? Awaited)>>> AsksAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = EarlierAsks;
        command.Parameters.AddWithValue("$session", Stamp(session));

        var asks = new Dictionary<string, List<(DateOnly, DateOnly?)>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!asks.TryGetValue(reader.GetString(0), out var nights))
            {
                asks[reader.GetString(0)] = nights = [];
            }

            nights.Add((Parse(reader.GetString(1)), reader.IsDBNull(2) ? null : Parse(reader.GetString(2))));
        }

        return asks.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(DateOnly, DateOnly?)>)pair.Value, StringComparer.Ordinal);
    }

    // Each member's newest report before tonight, with the period the calendar says it covers.
    static async Task<IReadOnlyDictionary<string, (DateOnly On, DateOnly? Period)>> ReportsAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReportsBefore;
        command.Parameters.AddWithValue("$session", Stamp(session));

        var reports = new Dictionary<string, (DateOnly On, DateOnly? Period)>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var on = Parse(reader.GetString(1));

            if (reports.TryGetValue(reader.GetString(0), out var newer) && newer.On >= on)
            {
                continue;
            }

            reports[reader.GetString(0)] = (on, PeriodIn(reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return reports;
    }

    // The period a calendar row says a report covers, where its detail states one.
    static DateOnly? PeriodIn(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        using var document = JsonDocument.Parse(detail);

        return document.RootElement.TryGetProperty("periodEnd", out var period)
            && period.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(period.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                ? end
                : null;
    }

    static async Task<DateOnly?> FirstNightAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = FirstNight;

        return await command.ExecuteScalarAsync(cancellation) is string first ? Parse(first) : null;
    }

    async Task RecordAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, QuartersOutcome outcome, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Instant(startedAt));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.Asked.Sum(one => one.Quarters));
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue("$detail", Detail(outcome));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // What the run page reads of the step.
    public static string Detail(QuartersOutcome outcome)
    {
        string Counted(string reason) => outcome.Asked.Count(one => one.Reason == reason).ToString(CultureInfo.InvariantCulture);
        string Came(string what) => outcome.Asked.Count(one => one.Outcome == what).ToString(CultureInfo.InvariantCulture);

        return FormattableString.Invariant($"{outcome.Asked.Count} of {outcome.Due} member(s) due asked: ")
            + $"{Counted(Report)} reporting, {Counted(Waiting)} waiting, {Counted(Joined)} joining, {Counted(Fill)} filled; "
            + $"{Came(Stored)} stored, {Came(NotYetPosted)} not yet posted, {Came(NothingReturned)} returning nothing, {Came(Refused)} refused; "
            + FormattableString.Invariant($"{outcome.Asked.Sum(one => one.Quarters)} quarter row(s), {outcome.Weighted} weighted call(s); ")
            + FormattableString.Invariant($"{outcome.FillOwed} member(s) of the fill still owed")
            + (outcome.LeftAtTheLimit > 0 ? FormattableString.Invariant($"; {outcome.LeftAtTheLimit} left at the step's limit") : string.Empty)
            + (outcome.LeftAtTheAllowance > 0 ? FormattableString.Invariant($"; {outcome.LeftAtTheAllowance} left at the day's allowance") : string.Empty);
    }

    static object Figure(decimal? value) => value is { } present ? Money.ToStorage(present) : DBNull.Value;

    static object Date(DateOnly? value) => value is { } present ? Stamp(present) : DBNull.Value;

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static DateOnly Parse(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
