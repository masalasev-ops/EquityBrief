using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// The history pull.
//
// On the operator's command and never on a night. Every name the index held on any session from a
// date to tonight is asked once for its daily bars over that whole span, and the earnings calendar
// once for each calendar month of it, and what comes back is stored in two tables of its own. The
// span reaches tonight rather than stopping where the store's year begins, because the night drops
// its oldest session every night, and a pull that stopped at the store's first session would leave a
// hole between the two within a week.
//
// Each row carries the run id of the pull that wrote it. No night reads either table, so removing a
// pull whole moves nothing any night, listing, score or page read, which is what lets these rows be
// removed where a stored bar may not be.
//
// Asked for the market series, it pulls the index's and the VIX's daily series instead, one request a
// series, into a table of their own marked and removed the same way, and asked for the sector funds, the
// eleven funds' series into the same table.
//
// Asked for the companies, it pulls each name's filer, GICS classification, delisting and quarterly share
// counts with the day each was filed, one request a name; for the splits, each name's splits, one request
// a name; and for the revenue, every figure each pulled company's filer stated under each revenue concept,
// with the day it was filed, one request a filer for its whole facts, each into a table of its own marked and
// removed the same way.
//
// Asked for a wider index's members, the S&P 400's or the S&P 600's, it stores the members its answer lists today,
// one request an index, marked and removed the same way; every other pull asked for that index reads its names from
// them, survivors alone, since the answer carries no span of membership.
// see: The history pulled before the store's year sits apart from its bars, marked by the pull that wrote it, read by no night and removed whole by that pull
// see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
public sealed class HistoryPull(
    IHistoricalBarFeed bars,
    IEarningsCalendarFeed earnings,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledEarnings, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledSurprise, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledMarketBar, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledCompany, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledShares, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledSplit, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledRevenue, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledMember, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledIncome, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledSnapshot, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.IndexMembership, Feed.HistoricalPrice, Feed.EarningsCalendar, Feed.CompanyFinancials, Feed.SplitsAndDividends, Feed.FilingsArchive, Feed.IdentifierMapping]);

    public const string Stage = "history-pull";
    public const string PurgeStage = "history-purge";

    // The surprise pull's own stage on the run log, since it asks the calendar alone and stores one table.
    public const string SurpriseStage = "history-pull-surprises";

    // The market pull's own stage, since it asks for two series and stores one table.
    public const string MarketStage = "history-pull-market";

    // The series a market pull asks for, in this order: the index itself and the VIX.
    // see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
    public static IReadOnlyList<string> MarketSeries { get; } = ["GSPC", "VIX"];

    // The four pulls of a company's history, each its own stage since each asks one feed and stores one table.
    public const string CompaniesStage = "history-pull-companies";
    public const string SplitsStage = "history-pull-splits";
    public const string SectorFundsStage = "history-pull-sector-funds";
    public const string RevenueStage = "history-pull-revenue";

    // The funds a sector funds pull asks for, in the order of their tickers.
    public static IReadOnlyList<string> SectorFunds { get; } = [.. GicsSectors.Funds.Values.Order(StringComparer.Ordinal)];

    // The index and credit funds' pull, from 15.1: the funds the night asks for beside the sector funds, pulled whole
    // from 2018 for the readings and the sweeps of each index.
    public const string IndexFundsStage = "history-pull-index-funds";

    // The members pull's own stage, since it asks one index's components and stores one table.
    public const string MembersStage = "history-pull-members";

    // The holdings pull's own stage, since it asks a fund's filings and the symbol lists and stores two tables.
    public const string HoldingsStage = "history-pull-holdings";

    // The indices beside the night's own whose members today a members pull stores, the S&P 400 and the S&P 600, and
    // which every other pull reads its names from, in place of the night's membership.
    // see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
    public static IReadOnlyList<string> WiderIndices { get; } = ["MID", "SML"];

    // The requests a second the archive's fair access asks a caller to stay within, which the revenue pull keeps
    // by waiting a tenth of a second after each.
    public const int ArchiveRequestsASecond = 10;

    // The run ids a pull and a purge are written under, which the run page reads as runs by hand.
    public const string RunPrefix = "history-pull-";
    public const string PurgePrefix = "history-purge-";

    // A pull any name went unanswered on.
    public const string Partial = "partial";

    // Every name the index held on at least one session of the span.
    const string NamesHeld = @"
        SELECT DISTINCT ticker FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $through)
          AND (""left"" IS NULL OR ""left"" > $from)
        ORDER BY ticker;
    ";

    // Insert only. A session some earlier pull already holds for a name keeps that pull's row, so
    // a second pull adds what the first did not reach and a purge of either removes its own rows.
    const string InsertBar = @"
        INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull)
        VALUES ($ticker, $session_date, $open, $high, $low, $close, $raw_close, $volume, $pull)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string InsertEarnings = @"
        INSERT INTO pulled_earnings (ticker, event_date, timing, pull)
        VALUES ($ticker, $event_date, $timing, $pull)
        ON CONFLICT (ticker, event_date) DO NOTHING;
    ";

    // The surprises, one row a print the calendar answers over the span: the figures as filed, kept as text,
    // and the provider's surprise as a statistic, null where the answer carries none.
    const string InsertSurprise = @"
        INSERT INTO pulled_surprise (ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, pull)
        VALUES ($ticker, $event_date, $timing, $eps_actual, $eps_estimate, $surprise_percent, $pull)
        ON CONFLICT (ticker, event_date) DO NOTHING;
    ";

    // The index's and the VIX's sessions as the provider sent them, insert only, so a session an earlier
    // pull holds keeps that pull's row.
    const string InsertMarketBar = @"
        INSERT INTO pulled_market_bar (series, session_date, open, high, low, close, pull)
        VALUES ($series, $session_date, $open, $high, $low, $close, $pull)
        ON CONFLICT (series, session_date) DO NOTHING;
    ";

    // A company as the provider files it today, insert only, so a ticker an earlier pull holds keeps that pull's row.
    const string InsertCompany = @"
        INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull)
        VALUES ($ticker, $cik, $sector, $industry_group, $industry, $sub_industry, $delisted_on, $pull)
        ON CONFLICT (ticker) DO NOTHING;
    ";

    // A quarter's share count with the day its balance sheet was filed and the session whose split basis it is on.
    const string InsertShares = @"
        INSERT INTO pulled_shares (ticker, period_end, filing_date, shares, basis_session, pull)
        VALUES ($ticker, $period_end, $filing_date, $shares, $basis_session, $pull)
        ON CONFLICT (ticker, period_end) DO NOTHING;
    ";

    // A quarter's income as its filer filed it, from 15.2, insert only, so a quarter an earlier pull holds keeps that
    // pull's row.
    // see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
    const string InsertIncome = @"
        INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull)
        VALUES ($ticker, $period_end, $filing_date, $net_income, $operating_income, $interest_expense, $pull)
        ON CONFLICT (ticker, period_end) DO NOTHING;
    ";

    const string InsertSplit = @"
        INSERT INTO pulled_split (ticker, ex_date, new_shares, old_shares, pull)
        VALUES ($ticker, $ex_date, $new_shares, $old_shares, $pull)
        ON CONFLICT (ticker, ex_date) DO NOTHING;
    ";

    // One figure one filing stated under one concept, insert only, so a figure an earlier pull holds keeps that pull's row.
    const string InsertRevenue = @"
        INSERT INTO pulled_revenue (cik, concept, period_start, period_end, accession, dollars, filed, form, pull)
        VALUES ($cik, $concept, $period_start, $period_end, $accession, $dollars, $filed, $form, $pull)
        ON CONFLICT (cik, concept, period_start, period_end, accession) DO NOTHING;
    ";

    // A member of a wider index today as its answer lists it, insert only, so a member an earlier pull holds keeps that
    // pull's row.
    const string InsertMember = @"
        INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull)
        VALUES ($index_code, $ticker, $exchange, $name, $sector, $industry, $pull)
        ON CONFLICT (index_code, ticker) DO NOTHING;
    ";

    // The names a pull asks for on a wider index: its members today, as a members pull stored them, and from 15.3 every
    // code its fund's snapshots matched a holding to, so the names it held and let go are pulled beside them.
    // see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
    const string MembersHeld = @"
        SELECT ticker FROM pulled_member WHERE index_code = $index
        UNION
        SELECT ticker FROM pulled_holding WHERE index_code = $index AND ticker IS NOT NULL
        ORDER BY ticker;
    ";

    // A fund's snapshot as filed, insert only, so a quarter an earlier pull holds keeps that pull's row.
    const string InsertSnapshot = @"
        INSERT INTO pulled_snapshot (index_code, period, accession, filed, holdings, equity, pull)
        VALUES ($index_code, $period, $accession, $filed, $holdings, $equity, $pull)
        ON CONFLICT (index_code, period) DO NOTHING;
    ";

    // A holding of common stock with the code it matched and the key it matched by.
    const string InsertHolding = @"
        INSERT INTO pulled_holding (index_code, period, holding, name, cusip, isin, ticker, matched_by, pull, shares, value_usd)
        VALUES ($index_code, $period, $holding, $name, $cusip, $isin, $ticker, $matched_by, $pull, $shares, $value_usd)
        ON CONFLICT (index_code, period, holding) DO NOTHING;
    ";

    // What an earlier pull stored for a quarter's holdings, which a pull reading the quarter again matches again, and
    // whether it stored the shares and the value, which a pull before the columns were did not.
    const string StoredHoldings = @"
        SELECT holding, ticker, matched_by, value_usd IS NOT NULL FROM pulled_holding
        WHERE index_code = $index_code AND period = $period;
    ";

    // A holding an earlier pull stored, matched again under the rule as it stands: its code and its key, and the shares and
    // the value as the filing states them where the row stored none, the holding, its name, its identifiers and the pull
    // that stored it kept as first written.
    // see: A holding matched by name is kept only where its code traded at the quarter's end
    const string MatchAgain = @"
        UPDATE pulled_holding SET ticker = $ticker, matched_by = $matched_by, shares = $shares, value_usd = $value_usd
        WHERE index_code = $index_code AND period = $period AND holding = $holding;
    ";

    // A code's close on its newest pulled session in the days to a snapshot's quarter end, as the provider sent it, and
    // the splits the provider files after that session, which a close sent already divided by them has undone.
    const string PulledCloseNear = @"
        SELECT raw_close, session_date FROM pulled_bar
        WHERE ticker = $ticker AND session_date BETWEEN $from AND $to
        ORDER BY session_date DESC LIMIT 1;
    ";

    const string PulledSplitsAfter = "SELECT new_shares, old_shares FROM pulled_split WHERE ticker = $ticker AND ex_date > $session;";

    // The names every holdings pull stored with the code their ISIN matched, both funds' alike, which read a holding named
    // with no identifier by the name a filing carried beside one.
    const string FiledByIsin = "SELECT DISTINCT name, ticker FROM pulled_holding WHERE matched_by = 'isin' AND ticker IS NOT NULL;";

    // The filers the revenue pull asks for: every CIK the pulled companies carry, and the tickers carrying none.
    const string PulledFilers = "SELECT DISTINCT cik FROM pulled_company WHERE cik IS NOT NULL ORDER BY cik;";
    const string PulledWithoutAFiler = "SELECT ticker FROM pulled_company WHERE cik IS NULL ORDER BY ticker;";

    const string BarCount = "SELECT COUNT(*) FROM pulled_bar;";
    const string EarningsCount = "SELECT COUNT(*) FROM pulled_earnings;";
    const string SurpriseCount = "SELECT COUNT(*) FROM pulled_surprise;";
    const string MarketBarCount = "SELECT COUNT(*) FROM pulled_market_bar;";
    const string CompanyCount = "SELECT COUNT(*) FROM pulled_company;";
    const string SharesCount = "SELECT COUNT(*) FROM pulled_shares;";
    const string IncomeCount = "SELECT COUNT(*) FROM pulled_income;";
    const string SplitCount = "SELECT COUNT(*) FROM pulled_split;";
    const string RevenueCount = "SELECT COUNT(*) FROM pulled_revenue;";
    const string MemberCount = "SELECT COUNT(*) FROM pulled_member WHERE index_code = $index;";

    const string BarsOfPull = "SELECT COUNT(*) FROM pulled_bar WHERE pull = $pull;";
    const string EarningsOfPull = "SELECT COUNT(*) FROM pulled_earnings WHERE pull = $pull;";
    const string SurprisesOfPull = "SELECT COUNT(*) FROM pulled_surprise WHERE pull = $pull;";
    const string MarketBarsOfPull = "SELECT COUNT(*) FROM pulled_market_bar WHERE pull = $pull;";
    const string CompaniesOfPull = "SELECT COUNT(*) FROM pulled_company WHERE pull = $pull;";
    const string SharesOfPull = "SELECT COUNT(*) FROM pulled_shares WHERE pull = $pull;";
    const string SplitsOfPull = "SELECT COUNT(*) FROM pulled_split WHERE pull = $pull;";
    const string RevenueOfPull = "SELECT COUNT(*) FROM pulled_revenue WHERE pull = $pull;";
    const string MembersOfPull = "SELECT COUNT(*) FROM pulled_member WHERE pull = $pull;";
    const string IncomeOfPull = "SELECT COUNT(*) FROM pulled_income WHERE pull = $pull;";
    const string SnapshotsOfPull = "SELECT COUNT(*) FROM pulled_snapshot WHERE pull = $pull;";
    const string HoldingsOfPull = "SELECT COUNT(*) FROM pulled_holding WHERE pull = $pull;";

    // The removal, which takes a pull's rows whole and nothing else.
    const string DeleteBarsOfPull = "DELETE FROM pulled_bar WHERE pull = $pull;";
    const string DeleteEarningsOfPull = "DELETE FROM pulled_earnings WHERE pull = $pull;";
    const string DeleteSurprisesOfPull = "DELETE FROM pulled_surprise WHERE pull = $pull;";
    const string DeleteMarketBarsOfPull = "DELETE FROM pulled_market_bar WHERE pull = $pull;";
    const string DeleteCompaniesOfPull = "DELETE FROM pulled_company WHERE pull = $pull;";
    const string DeleteSharesOfPull = "DELETE FROM pulled_shares WHERE pull = $pull;";
    const string DeleteSplitsOfPull = "DELETE FROM pulled_split WHERE pull = $pull;";
    const string DeleteRevenueOfPull = "DELETE FROM pulled_revenue WHERE pull = $pull;";
    const string DeleteMembersOfPull = "DELETE FROM pulled_member WHERE pull = $pull;";
    const string DeleteIncomeOfPull = "DELETE FROM pulled_income WHERE pull = $pull;";
    const string DeleteSnapshotsOfPull = "DELETE FROM pulled_snapshot WHERE pull = $pull;";
    const string DeleteHoldingsOfPull = "DELETE FROM pulled_holding WHERE pull = $pull;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public static string RunIdAt(string prefix, DateTimeOffset at) => FormattableString.Invariant($"{prefix}{at:yyyyMMddTHHmmss.fffffffZ}");

    // The verb a person runs: `history-pull --from <yyyy-MM-dd>`, the same with `--surprises` for the earnings
    // surprises alone, `--market` for the index's and the VIX's series alone, `--sector-etfs` for the sector funds'
    // series, `--companies` for each name's company and share counts or `--splits` for each name's splits,
    // `history-pull --revenue` for each pulled company's revenue as its filer filed it, `history-pull --members
    // --index <MID or SML>` for a wider index's members today, or `history-pull --purge <pull>`. Every pull but the
    // revenue's and the market series' takes `--index`, a wider index's names being its members today as a members
    // pull stored them, and the bars, surprises, companies and splits pulls take `--names` with codes between commas,
    // asking only those of the index's names, so a pull for the few codes a match added does not ask every name again
    // at the allowance's cost. The feeds are asked for only by a pull, so removing one needs no key and reaches no
    // provider.
    public static async Task<int> RunAsync(
        string[] args,
        Func<NightFeeds> feeds,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error,
        Func<IMarketSeriesFeed>? market = null,
        Func<ICompanyFeed>? companies = null,
        Func<ISplitHistoryFeed>? splits = null,
        Func<IFiledRevenueFeed>? revenue = null,
        Func<IIndexComponentsFeed>? members = null,
        Func<IFundSnapshotFeed>? snapshots = null,
        Func<ISymbolListFeed>? symbols = null,
        Func<IOpenFigiMappingFeed>? mappings = null)
    {
        if (VerbArguments.Value(args, "--purge") is { } pull)
        {
            try
            {
                var purged = await PurgeAsync(clock, databaseFile, pull, RunIdAt(PurgePrefix, clock.UtcNow));

                output.WriteLine(FormattableString.Invariant($"removed the pull {purged.Pull}: {purged.Bars} bar(s), {purged.Earnings} earnings print(s), {purged.Surprises} surprise(s), {purged.MarketBars} market session(s), {purged.Companies} compan(ies), {purged.Shares} share count(s), {purged.Splits} split(s), {purged.Revenue} revenue figure(s), {purged.Members} member(s), {purged.Income} quarter(s) of income, {purged.Snapshots} snapshot(s) and {purged.Holdings} holding(s)"));

                return 0;
            }
            catch (ArgumentException refusal)
            {
                error.WriteLine("history-pull: " + refusal.Message);

                return 1;
            }
        }

        if (VerbArguments.Has(args, "--revenue"))
        {
            return await RevenueAsync(revenue, clock, databaseFile, output, error);
        }

        if (VerbArguments.Has(args, "--members"))
        {
            return await MembersAsync(VerbArguments.Value(args, "--index"), members, clock, databaseFile, output, error);
        }

        if (VerbArguments.Has(args, "--holdings"))
        {
            return await HoldingsAsync(VerbArguments.Value(args, "--index"), snapshots, symbols, mappings, feeds, clock, databaseFile, output, error);
        }

        if (VerbArguments.Value(args, "--from") is not { } given
            || !DateOnly.TryParseExact(given, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
        {
            error.WriteLine("history-pull: name the first session to pull with '--from <yyyy-MM-dd>', or a pull to remove with '--purge <pull>'.");

            return 2;
        }

        if (VerbArguments.Has(args, "--market"))
        {
            return await MarketAsync(from, market, clock, databaseFile, output, error);
        }

        // The codes a pull is narrowed to, none where it asks every name; and the operator's word to go past the stop.
        IReadOnlyCollection<string>? only = VerbArguments.Value(args, "--names") is { } listed
            ? [.. listed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : null;
        var pastTheStop = VerbArguments.Has(args, ProviderStop.PastTheStop);

        if (VerbArguments.Has(args, "--companies"))
        {
            return await CompaniesAsync(from, VerbArguments.Value(args, "--index") ?? "GSPC", companies, clock, databaseFile, output, error, only, pastTheStop);
        }

        if (VerbArguments.Has(args, "--splits"))
        {
            return await SplitsAsync(from, VerbArguments.Value(args, "--index") ?? "GSPC", splits, clock, databaseFile, output, error, only, pastTheStop);
        }

        NightFeeds resolved;

        try
        {
            resolved = feeds();
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);

        if (VerbArguments.Has(args, "--sector-etfs") || VerbArguments.Has(args, "--index-funds"))
        {
            try
            {
                var indexFunds = VerbArguments.Has(args, "--index-funds");
                var funds = indexFunds
                    ? await PullSectorFundsAsync(resolved.Historical, clock, databaseFile, from, runId, funds: MarketSeriesFetcher.IndexAndCreditFunds, stage: IndexFundsStage)
                    : await PullSectorFundsAsync(resolved.Historical, clock, databaseFile, from, runId);

                output.WriteLine("pull " + runId);
                output.WriteLine(indexFunds ? Detail(funds, "index and credit funds", MarketSeriesFetcher.IndexAndCreditFunds.Count) : Detail(funds));

                return funds.Refused.Count == 0 ? 0 : 1;
            }
            catch (ArgumentException refusal)
            {
                error.WriteLine("history-pull: " + refusal.Message);

                return 1;
            }
        }

        try
        {
            var puller = new HistoryPull(resolved.Historical, resolved.Calendar, clock, databaseFile);

            if (VerbArguments.Has(args, "--surprises"))
            {
                var surprises = await puller.PullSurprisesAsync(VerbArguments.Value(args, "--index") ?? "GSPC", from, runId, error.WriteLine, only: only, pastTheStop: pastTheStop);

                output.WriteLine("pull " + runId);
                output.WriteLine(Detail(surprises));

                return 0;
            }

            var outcome = await puller.PullAsync(VerbArguments.Value(args, "--index") ?? "GSPC", from, runId, error.WriteLine, only: only, pastTheStop: pastTheStop);

            output.WriteLine("pull " + runId);
            output.WriteLine(Detail(outcome));

            return 0;
        }
        catch (ArgumentException refusal)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }
    }

    // Pulls every name the index held from a date to tonight, and the earnings prints over the same
    // span. A date on or after tonight's session asks for nothing and is refused before any request, as is a
    // statement of calls past the stop unless the operator said to go past it.
    public async Task<HistoryPullOutcome> PullAsync(
        string indexCode,
        DateOnly from,
        string runId,
        Action<string>? progress = null,
        CancellationToken cancellation = default,
        IReadOnlyCollection<string>? only = null,
        bool pastTheStop = false)
    {
        var startedAt = clock.UtcNow;
        var through = clock.SessionDateAt(startedAt);

        if (from >= through)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"A pull reaches from a date before tonight's session, {through:yyyy-MM-dd}, and {from:yyyy-MM-dd} is not before it."),
                nameof(from));
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var names = await NamesAsync(connection, indexCode, from, through, cancellation, only);
        var requestsBefore = bars.Requests + earnings.Requests;
        var months = MonthsOf(from, through);

        // Stated before the first request: a year a name at the historical weight and the earnings calendar's months.
        // see: Every pull states its provider calls before it runs and stops for the operator at the day's cap
        var stated = (names.Count * ProviderWeights.HistoricalPerTicker) + (months.Count * ProviderWeights.EarningsCalendar);
        var what = FormattableString.Invariant($"{names.Count:N0} name(s) and {ProviderStop.Months(months.Count)} month(s) of the earnings calendar");

        progress?.Invoke(ProviderStop.Stated(what, stated));
        ProviderStop.Hold(what, stated, pastTheStop);

        // Fetched before anything is stored, so the calendar the holes are read against is the whole
        // set rather than whichever names happened to arrive first.
        var fetched = new Dictionary<string, IReadOnlyList<ProviderBar>>(StringComparer.OrdinalIgnoreCase);
        var unanswered = new List<string>();

        foreach (var ticker in names)
        {
            try
            {
                var series = await bars.BarsAsync(ticker, from, through, cancellation);

                if (series.Count == 0)
                {
                    unanswered.Add(FormattableString.Invariant($"{ticker}: the provider sent no session"));
                }
                else
                {
                    fetched[ticker] = series;
                }
            }
            // A series the reader cannot take, a session with no positive close among them, is named as a refusal
            // is, so one departed name the provider sends a broken year for does not stop a pull of eighteen hundred.
            catch (Exception failure) when (failure is ProviderRefusal or FormatException)
            {
                unanswered.Add($"{ticker}: {failure.Message}");
            }

            if ((fetched.Count + unanswered.Count) % 50 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"{fetched.Count + unanswered.Count} of {names.Count} name(s) asked"));
            }
        }

        // A hole is a session the other names traded and one name's series does not hold between its
        // own first and last. It is stored as the provider sent it and named, and a reader of the
        // pulled series is the one that decides what a hole stops.
        var dates = fetched.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyCollection<DateOnly>)[.. entry.Value.Select(bar => bar.SessionDate)],
            StringComparer.OrdinalIgnoreCase);
        var (holes, strays) = HolesIn(dates);

        var held = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prints = new Dictionary<(string Ticker, DateOnly Date), CalendarEvent>();
        var monthsUnanswered = new List<string>();

        foreach (var (first, last) in months)
        {
            try
            {
                foreach (var print in await earnings.EventsAsync(first, last, cancellation))
                {
                    if (held.Contains(print.Ticker))
                    {
                        prints.TryAdd((print.Ticker, print.EventDate), print);
                    }
                }
            }
            catch (ProviderRefusal refusal)
            {
                monthsUnanswered.Add(FormattableString.Invariant($"{first:yyyy-MM}: {refusal.Message}"));
            }
        }

        var barsBefore = await CountAsync(connection, BarCount, null, cancellation);
        var earningsBefore = await CountAsync(connection, EarningsCount, null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var (ticker, series) in fetched.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            foreach (var bar in series)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertBar;
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$session_date", Text(bar.SessionDate));
                Money.Bind(insert, "$open", bar.Open);
                Money.Bind(insert, "$high", bar.High);
                Money.Bind(insert, "$low", bar.Low);
                Money.Bind(insert, "$close", bar.Close);
                Money.Bind(insert, "$raw_close", bar.RawClose);
                insert.Parameters.AddWithValue("$volume", bar.Volume);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        foreach (var print in prints.Values.OrderBy(print => print.Ticker, StringComparer.Ordinal).ThenBy(print => print.EventDate))
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = InsertEarnings;
            insert.Parameters.AddWithValue("$ticker", print.Ticker);
            insert.Parameters.AddWithValue("$event_date", Text(print.EventDate));
            insert.Parameters.AddWithValue("$timing", EquityBrief.Worker.Calendar.CalendarFetcher.Filed(print.Timing));
            insert.Parameters.AddWithValue("$pull", runId);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var barsWritten = await CountAsync(connection, BarCount, null, cancellation) - barsBefore;
        var earningsWritten = await CountAsync(connection, EarningsCount, null, cancellation) - earningsBefore;
        var requests = bars.Requests + earnings.Requests - requestsBefore;

        var outcome = new HistoryPullOutcome(
            from,
            through,
            names.Count,
            fetched.Count,
            unanswered,
            holes,
            barsWritten,
            earningsWritten,
            requests,
            months.Count,
            monthsUnanswered,
            strays);

        await AppendAsync(
            connection,
            runId,
            Stage,
            startedAt,
            clock.UtcNow,
            unanswered.Count == 0 && monthsUnanswered.Count == 0 ? "ok" : Partial,
            barsWritten + earningsWritten,
            requests,
            JsonSerializer.Serialize(new
            {
                from = Text(from),
                through = Text(through),
                names = names.Count,
                stored = fetched.Count,
                bars = barsWritten,
                earnings = earningsWritten,
                months = months.Count,
                unanswered,
                monthsUnanswered,
                holes = holes
                    .GroupBy(gap => gap.Ticker, StringComparer.Ordinal)
                    .Select(group => FormattableString.Invariant($"{group.Key} is missing {group.Count()} session(s), the first {group.Min(gap => gap.SessionDate):yyyy-MM-dd}"))
                    .ToArray(),
                strays = strays
                    .Select(stray => FormattableString.Invariant($"{stray.Day:yyyy-MM-dd} held by {stray.Holders.Count} of the {stray.Spanning} name(s) spanning it: {string.Join(", ", stray.Holders)}"))
                    .ToArray(),
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // Pulls the earnings surprises of every name the index held from a date to tonight: the calendar asked once
    // a calendar month of the span, as the bars' pull asks it, and each print of a held name stored with the
    // figures as filed and the provider's surprise where the answer carries one. A date on or after tonight's
    // session is refused before any request, and a print an earlier pull holds keeps that pull's row.
    // see: The surprises pulled before the store's year sit beside the pulled prints and are read by no night
    public async Task<HistorySurpriseOutcome> PullSurprisesAsync(
        string indexCode,
        DateOnly from,
        string runId,
        Action<string>? progress = null,
        CancellationToken cancellation = default,
        IReadOnlyCollection<string>? only = null,
        bool pastTheStop = false)
    {
        var startedAt = clock.UtcNow;
        var through = clock.SessionDateAt(startedAt);

        if (from >= through)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"A pull reaches from a date before tonight's session, {through:yyyy-MM-dd}, and {from:yyyy-MM-dd} is not before it."),
                nameof(from));
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var names = await NamesAsync(connection, indexCode, from, through, cancellation, only);
        var held = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestsBefore = earnings.Requests;
        var prints = new Dictionary<(string Ticker, DateOnly Date), CalendarEvent>();
        var monthsUnanswered = new List<string>();
        var months = MonthsOf(from, through);
        var asked = 0;

        // Stated before the first request: the earnings calendar's months, whatever the names, since the calendar is
        // asked a month at a time and filtered to them.
        // see: Every pull states its provider calls before it runs and stops for the operator at the day's cap
        var what = FormattableString.Invariant($"{ProviderStop.Months(months.Count)} month(s) of the earnings calendar for {names.Count:N0} name(s)");

        progress?.Invoke(ProviderStop.Stated(what, months.Count * ProviderWeights.EarningsCalendar));
        ProviderStop.Hold(what, months.Count * ProviderWeights.EarningsCalendar, pastTheStop);

        foreach (var (first, last) in months)
        {
            try
            {
                foreach (var print in await earnings.EventsAsync(first, last, cancellation))
                {
                    if (held.Contains(print.Ticker))
                    {
                        prints.TryAdd((print.Ticker, print.EventDate), print);
                    }
                }
            }
            catch (ProviderRefusal refusal)
            {
                monthsUnanswered.Add(FormattableString.Invariant($"{first:yyyy-MM}: {refusal.Message}"));
            }

            if (++asked % 12 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"{asked} of {months.Count} month(s) asked"));
            }
        }

        var before = await CountAsync(connection, SurpriseCount, null, cancellation);
        var withASurprise = 0;

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var print in prints.Values.OrderBy(print => print.Ticker, StringComparer.Ordinal).ThenBy(print => print.EventDate))
        {
            var percent = SurprisePercent(print);

            withASurprise += percent is null ? 0 : 1;

            await using var insert = connection.CreateCommand();
            insert.CommandText = InsertSurprise;
            insert.Parameters.AddWithValue("$ticker", print.Ticker);
            insert.Parameters.AddWithValue("$event_date", Text(print.EventDate));
            insert.Parameters.AddWithValue("$timing", EquityBrief.Worker.Calendar.CalendarFetcher.Filed(print.Timing));
            insert.Parameters.AddWithValue("$eps_actual", (object?)print.Actual ?? DBNull.Value);
            insert.Parameters.AddWithValue("$eps_estimate", (object?)print.Estimate ?? DBNull.Value);
            insert.Parameters.AddWithValue("$surprise_percent", (object?)percent ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pull", runId);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var written = await CountAsync(connection, SurpriseCount, null, cancellation) - before;
        var requests = earnings.Requests - requestsBefore;
        var outcome = new HistorySurpriseOutcome(from, through, names.Count, months.Count, monthsUnanswered, prints.Count, withASurprise, written, requests);

        await AppendAsync(
            connection,
            runId,
            SurpriseStage,
            startedAt,
            clock.UtcNow,
            monthsUnanswered.Count == 0 ? "ok" : Partial,
            written,
            requests,
            JsonSerializer.Serialize(new
            {
                from = Text(from),
                through = Text(through),
                names = names.Count,
                months = months.Count,
                prints = prints.Count,
                withASurprise,
                surprises = written,
                monthsUnanswered,
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // The verb's market branch. A series the provider refused stores nothing and fails the command, so a run
    // that would read it is not started over a series that is not there.
    static async Task<int> MarketAsync(
        DateOnly from,
        Func<IMarketSeriesFeed>? market,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (market is null)
        {
            error.WriteLine("history-pull: this command was handed no market series feed to ask.");

            return 1;
        }

        IMarketSeriesFeed resolved;

        try
        {
            resolved = market();
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);

        try
        {
            var pulled = await PullMarketAsync(resolved, clock, databaseFile, from, runId);

            output.WriteLine("pull " + runId);
            output.WriteLine(Detail(pulled));

            return pulled.Refused.Count == 0 ? 0 : 1;
        }
        catch (ArgumentException refusal)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }
    }

    // Pulls the index's and the VIX's daily series from a date to tonight, one request a series, and stores each
    // session the provider sent marked by the pull. A series the provider refuses, does not answer in time, sends no
    // session for or sends a payload that cannot be read stores nothing and is named, the other is stored, and the
    // pull's row says partial. A date on or after tonight's session is refused before any request.
    // see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
    public static async Task<HistoryMarketOutcome> PullMarketAsync(
        IMarketSeriesFeed market,
        IClock clock,
        string databaseFile,
        DateOnly from,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var through = clock.SessionDateAt(startedAt);

        if (from >= through)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"A pull reaches from a date before tonight's session, {through:yyyy-MM-dd}, and {from:yyyy-MM-dd} is not before it."),
                nameof(from));
        }

        var requestsBefore = market.Requests;
        var answered = new List<(string Series, IReadOnlyList<ProviderBar> Bars)>();
        var refused = new List<string>();

        foreach (var series in MarketSeries)
        {
            try
            {
                var bars = await market.SeriesAsync(series, from, through, cancellation);

                if (bars.Count == 0)
                {
                    refused.Add(FormattableString.Invariant($"{series}: the provider sent no session"));
                }
                else
                {
                    answered.Add((series, bars));
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                refused.Add($"{series}: the provider did not answer in time on any try");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                refused.Add(failure is ProviderRefusal or FormatException
                    ? $"{series}: {failure.Message}"
                    : $"{series}: its answer could not be read: {failure.Message}");
            }
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var before = await CountAsync(connection, MarketBarCount, null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var (series, bars) in answered)
        {
            foreach (var bar in bars)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertMarketBar;
                insert.Parameters.AddWithValue("$series", series);
                insert.Parameters.AddWithValue("$session_date", Text(bar.SessionDate));
                Money.Bind(insert, "$open", bar.Open);
                Money.Bind(insert, "$high", bar.High);
                Money.Bind(insert, "$low", bar.Low);
                Money.Bind(insert, "$close", bar.Close);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        var written = await CountAsync(connection, MarketBarCount, null, cancellation) - before;
        var requests = market.Requests - requestsBefore;
        IReadOnlyList<MarketSeriesStored> stored =
        [
            .. answered.Select(entry => new MarketSeriesStored(entry.Series, entry.Bars.Count, entry.Bars.Min(bar => bar.SessionDate), entry.Bars.Max(bar => bar.SessionDate))),
        ];
        var outcome = new HistoryMarketOutcome(from, through, stored, refused, written, requests);

        await AppendAsync(
            connection,
            runId,
            MarketStage,
            startedAt,
            clock.UtcNow,
            refused.Count == 0 ? "ok" : Partial,
            written,
            requests,
            JsonSerializer.Serialize(new
            {
                from = Text(from),
                through = Text(through),
                series = stored.Select(Described).ToArray(),
                refused,
                stored = written,
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // The verb's companies branch. A name the provider does not serve is named on the pull's row and stores nothing,
    // and the pull goes on, so the command ends well with names unanswered and says which.
    static async Task<int> CompaniesAsync(
        DateOnly from,
        string indexCode,
        Func<ICompanyFeed>? companies,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error,
        IReadOnlyCollection<string>? only = null,
        bool pastTheStop = false)
    {
        if (Resolved(companies, "company", error) is not { } feed)
        {
            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);

        try
        {
            var pulled = await PullCompaniesAsync(feed, clock, databaseFile, indexCode, from, runId, error.WriteLine, only: only, pastTheStop: pastTheStop);

            output.WriteLine("pull " + runId);
            output.WriteLine(Detail(pulled));

            return 0;
        }
        catch (ArgumentException refusal)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }
    }

    // The verb's splits branch, ending well with names unanswered as the companies branch does.
    static async Task<int> SplitsAsync(
        DateOnly from,
        string indexCode,
        Func<ISplitHistoryFeed>? splits,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error,
        IReadOnlyCollection<string>? only = null,
        bool pastTheStop = false)
    {
        if (Resolved(splits, "splits", error) is not { } feed)
        {
            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);

        try
        {
            var pulled = await PullSplitsAsync(feed, clock, databaseFile, indexCode, from, runId, error.WriteLine, only: only, pastTheStop: pastTheStop);

            output.WriteLine("pull " + runId);
            output.WriteLine(Detail(pulled));

            return 0;
        }
        catch (ArgumentException refusal)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }
    }

    // The verb's revenue branch, which asks for the filers the companies pull stored and so needs no date.
    static async Task<int> RevenueAsync(
        Func<IFiledRevenueFeed>? revenue,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (Resolved(revenue, "revenue", error) is not { } feed)
        {
            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);

        try
        {
            var pulled = await PullRevenueAsync(feed, clock, databaseFile, runId, progress: error.WriteLine);

            output.WriteLine("pull " + runId);
            output.WriteLine(Detail(pulled));

            return 0;
        }
        catch (ArgumentException refusal)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }
    }

    // The verb's members branch, which asks one wider index's components and so needs no date. An index outside the wider
    // ones is refused before any request, and an answer that cannot be read or lists nobody ends the command badly with
    // its row saying why.
    static async Task<int> MembersAsync(
        string? indexCode,
        Func<IIndexComponentsFeed>? members,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (indexCode is null || !WiderIndices.Contains(indexCode, StringComparer.Ordinal))
        {
            error.WriteLine($"history-pull: name a wider index to pull the members of with '--index', one of {string.Join(", ", WiderIndices)}; the night keeps the S&P 500's own.");

            return 2;
        }

        if (Resolved(members, "members", error) is not { } feed)
        {
            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);
        var pulled = await PullMembersAsync(feed, clock, databaseFile, indexCode, runId);

        output.WriteLine("pull " + runId);
        output.WriteLine(Detail(pulled));

        return pulled.Refused is null ? 0 : 1;
    }

    // Pulls a wider index's members today, one request: each component the index's answer lists, stored with its
    // exchange, name, sector and industry and marked by the pull. An answer the provider refuses, does not send in time
    // or that cannot be read stores nothing and says why on the pull's row. The members are survivors alone, since the
    // answer carries no span of membership, and the row says so.
    // see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
    public static async Task<HistoryMemberOutcome> PullMembersAsync(
        IIndexComponentsFeed feed,
        IClock clock,
        string databaseFile,
        string indexCode,
        string runId,
        CancellationToken cancellation = default)
    {
        if (!WiderIndices.Contains(indexCode, StringComparer.Ordinal))
        {
            throw new ArgumentException($"A members pull asks for a wider index, one of {string.Join(", ", WiderIndices)}, and {indexCode} is not one.", nameof(indexCode));
        }

        var startedAt = clock.UtcNow;
        var requestsBefore = feed.Requests;
        IReadOnlyList<IndexComponent> listed = [];
        string? refused = null;

        try
        {
            listed = await feed.ComponentsAsync(indexCode, cancellation);
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            refused = failure.Message;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var before = await MembersCountAsync(connection, indexCode, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var member in listed)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = InsertMember;
            insert.Parameters.AddWithValue("$index_code", indexCode);
            insert.Parameters.AddWithValue("$ticker", member.Ticker);
            insert.Parameters.AddWithValue("$exchange", member.Exchange);
            insert.Parameters.AddWithValue("$name", (object?)member.Name ?? DBNull.Value);
            insert.Parameters.AddWithValue("$sector", (object?)member.Sector ?? DBNull.Value);
            insert.Parameters.AddWithValue("$industry", (object?)member.Industry ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pull", runId);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var held = await MembersCountAsync(connection, indexCode, cancellation);
        var outcome = new HistoryMemberOutcome(indexCode, listed.Count, held - before, held, refused, feed.Requests - requestsBefore);

        await AppendAsync(
            connection,
            runId,
            MembersStage,
            startedAt,
            clock.UtcNow,
            refused is null ? "ok" : "refused",
            outcome.Written,
            outcome.Requests,
            Detail(outcome),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    static async Task<int> MembersCountAsync(SqliteConnection connection, string indexCode, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = MemberCount;
        command.Parameters.AddWithValue("$index", indexCode);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static async Task<int> HoldingsAsync(
        string? indexCode,
        Func<IFundSnapshotFeed>? snapshots,
        Func<ISymbolListFeed>? symbols,
        Func<IOpenFigiMappingFeed>? mappings,
        Func<NightFeeds> feeds,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (indexCode is null || !WiderIndices.Contains(indexCode, StringComparer.Ordinal))
        {
            error.WriteLine($"history-pull: name a wider index to pull its fund's holdings for with '--index', one of {string.Join(", ", WiderIndices)}.");

            return 2;
        }

        if (Resolved(snapshots, "fund holdings", error) is not { } filings || Resolved(symbols, "symbol list", error) is not { } listed)
        {
            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);
        var pulled = await PullHoldingsAsync(filings, listed, clock, databaseFile, indexCode, runId, error.WriteLine, prices: feeds().Historical, mappings: mappings?.Invoke());

        output.WriteLine("pull " + runId);
        output.WriteLine(Detail(pulled));

        return pulled.Refused is null && pulled.Unread.Count == 0 ? 0 : 1;
    }

    // Pulls a wider index's fund's quarter-end holdings, the S&P 400's IJH or the S&P 600's IJR: the fund's filings of its
    // holdings as the archive lists them and the two filings before the first public one, the N-Q for the quarter to
    // 2018-12-31 and the annual report for the year to 2019-03-31, each read at the archive's pace, and the provider's
    // listed and delisted symbols, one request a list; each holding of common stock is stored with the provider's code it
    // matched by ISIN, by name, by a code its fund held by ISIN within a year or by none, and each snapshot with its
    // quarter's end and how many holdings it filed. A match by name is kept only where its code traded at the quarter's
    // end, read off a pulled bar in the days to it or, where the store holds none and the provider's prices are handed in,
    // off the provider's own daily prices for those days, asked once a code a quarter. Every code a holding matched is
    // then read over the quarters it matched and kept only where its closes move in step with the fund's values a share,
    // the holding's codes by name read in place of one that does not; a holding still matched to none whose identifier
    // the symbol lists carry under no code is mapped through OpenFIGI, where a mapping feed is handed in, to the tickers
    // it traded under on the US venues, each read under the same checks; and a holding still matched to none at two
    // quarters or more is read against the codes its fund held by ISIN within a year. A quarter an earlier pull stored is
    // matched again, each holding kept as first stored and its code and key changed where the rule as it stands reads
    // another. A filing that cannot be read or is for another series is named and the others stored; a filing list or a
    // symbol list that cannot be read stores nothing and says why.
    // see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
    // see: A holding matched by name is kept only where its code traded at the quarter's end
    // see: The funds' holdings before their first public N-PORT are read from their N-Q of 2018-12-31 and their annual report of 2019-03-31
    // see: A holding's code is kept only where its closes move in step with the fund's values a share, and a match by name is held to the level at each quarter end as well
    // see: A renamed company is matched to a code its fund held by ISIN within a year where its close equals the value a share to the cent at two quarter ends
    // see: A code the fund held by ISIN beside a holding at one quarter end is another holding and never that holding renamed
    // see: A holding of a schedule filed with no identifier carries the code the next coded quarter's holding of the identical name matched, where the closes move in step across them
    // see: A holding the symbol lists carry under no code is mapped to the tickers it traded under through OpenFIGI, each held to the checks a name's code is
    public static async Task<HistoryHoldingOutcome> PullHoldingsAsync(
        IFundSnapshotFeed feed,
        ISymbolListFeed symbols,
        IClock clock,
        string databaseFile,
        string indexCode,
        string runId,
        Action<string>? progress = null,
        CancellationToken cancellation = default,
        IHistoricalBarFeed? prices = null,
        IOpenFigiMappingFeed? mappings = null)
    {
        if (!WiderIndices.Contains(indexCode, StringComparer.Ordinal))
        {
            throw new ArgumentException($"A holdings pull asks for a wider index's fund, one of {string.Join(", ", WiderIndices)}, and {indexCode} is not one.", nameof(indexCode));
        }

        var startedAt = clock.UtcNow;
        var series = FundSnapshots.Series[indexCode];
        var requestsBefore = feed.Requests + symbols.Requests;
        var snapshots = new List<(FundFiling Filing, FundSnapshot Snapshot)>();
        var unread = new List<string>();
        IReadOnlyList<FundFiling> filings = [];
        IReadOnlyList<ListedSymbol> listed = [];
        HoldingMatcher? matcher = null;
        string? refused = null;

        try
        {
            filings = [.. (await feed.FilingsAsync(series, cancellation)).Where(filing => filing.Form == FundSnapshots.Form).OrderBy(filing => filing.Filed)];

            foreach (var filing in filings)
            {
                try
                {
                    var snapshot = await feed.SnapshotAsync(filing.Accession, cancellation);

                    if (snapshot.Series == series)
                    {
                        snapshots.Add((filing, snapshot));
                        progress?.Invoke(FormattableString.Invariant($"{indexCode}: read the holdings as of {snapshot.Period:yyyy-MM-dd}"));
                    }
                    else
                    {
                        unread.Add($"{filing.Accession}: the filing is for {snapshot.Series} and not {series}");
                    }
                }
                catch (Exception failure) when (failure is not OperationCanceledException || !cancellation.IsCancellationRequested)
                {
                    unread.Add($"{filing.Accession}: {failure.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(1.0 / ArchiveRequestsASecond), cancellation);
            }

            // The two filings before the first public N-PORT, each one document of the trust's schedules, read for this
            // index's fund.
            foreach (var schedule in FundSchedules.Filings)
            {
                try
                {
                    var snapshot = FundSchedules.Parse(
                        await feed.DocumentAsync(schedule.Accession, schedule.Document, cancellation),
                        series,
                        FundSchedules.Titles[indexCode],
                        schedule);

                    snapshots.Add((new FundFiling(schedule.Accession, schedule.Filed, schedule.Form), snapshot));
                    progress?.Invoke(FormattableString.Invariant($"{indexCode}: read the holdings as of {snapshot.Period:yyyy-MM-dd} off the {schedule.Form}"));
                }
                catch (Exception failure) when (failure is not OperationCanceledException || !cancellation.IsCancellationRequested)
                {
                    unread.Add($"{schedule.Accession} {schedule.Document}: {failure.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(1.0 / ArchiveRequestsASecond), cancellation);
            }

            listed = [.. await symbols.SymbolsAsync(false, cancellation), .. await symbols.SymbolsAsync(true, cancellation)];
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            refused = failure.Message;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (refused is null)
        {
            // The names the fund's own filings carry beside an ISIN a symbol carries, each with the code it matched, and
            // the ones every holdings pull stored so, both funds' alike, which read a holding named with no identifier, or
            // renamed since, by the name it was held under.
            var byIsinAlone = new HoldingMatcher(listed);
            var filed = snapshots
                .SelectMany(one => one.Snapshot.Equity)
                .Select(holding => (holding.Name, Match: byIsinAlone.Match(holding)))
                .Where(pair => pair.Match.By == HoldingMatcher.ByIsin && pair.Match.Ticker is not null)
                .Select(pair => (pair.Name, pair.Match.Ticker!))
                .ToList();

            await using (var named = connection.CreateCommand())
            {
                named.CommandText = FiledByIsin;

                await using var rows = await named.ExecuteReaderAsync(cancellation);

                while (await rows.ReadAsync(cancellation))
                {
                    filed.Add((rows.GetString(0), rows.GetString(1)));
                }
            }

            matcher = new HoldingMatcher(listed, filed);
        }

        var (stored, equity, byIsin, byName, between, again, asked, widely, byHeld, carried, byMapping, mapped) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var unmatched = new List<string>();
        var unanswered = new List<string>();
        var pricedOut = new List<string>();
        var notInStep = new List<string>();

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        if (refused is null && matcher is not null)
        {
            var closes = new Dictionary<(string Code, DateOnly Quarter), QuarterEndClose?>();
            var askedOf = new HashSet<(string Code, DateOnly Quarter)>();

            // A code's close on its newest pulled session in the days to a quarter's end, with the splits filed after that
            // session undone beside it, or the provider's own where it was asked; none where neither holds one.
            QuarterEndClose? CloseOn(string code, DateOnly quarter)
            {
                if (closes.TryGetValue((code, quarter), out var known))
                {
                    return known;
                }

                QuarterEndClose? close = null;

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = PulledCloseNear;
                    command.Parameters.AddWithValue("$ticker", code);
                    command.Parameters.AddWithValue("$from", quarter.AddDays(-SnapshotSessionDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$to", quarter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                    using var row = command.ExecuteReader();

                    if (row.Read())
                    {
                        var sent = decimal.Parse(row.GetString(0), CultureInfo.InvariantCulture);
                        var session = row.GetString(1);
                        var factor = 1m;

                        using var splits = connection.CreateCommand();
                        splits.CommandText = PulledSplitsAfter;
                        splits.Parameters.AddWithValue("$ticker", code);
                        splits.Parameters.AddWithValue("$session", session);

                        using var split = splits.ExecuteReader();

                        while (split.Read())
                        {
                            factor *= decimal.Parse(split.GetString(0), CultureInfo.InvariantCulture) / decimal.Parse(split.GetString(1), CultureInfo.InvariantCulture);
                        }

                        close = new QuarterEndClose(sent, factor == 1m ? null : sent * factor);
                    }
                }

                closes[(code, quarter)] = close;

                return close;
            }

            // Every code a holding's name could be read as that holds no pulled bar in the days to a quarter's end, asked of
            // the provider once a quarter: its close on the newest session it sent in those days, none where it sent none.
            async Task AskAboutAsync(IEnumerable<string> codes, DateOnly quarter)
            {
                foreach (var code in codes)
                {
                    if (prices is null || CloseOn(code, quarter) is not null || !askedOf.Add((code, quarter)))
                    {
                        continue;
                    }

                    asked++;

                    try
                    {
                        var answered = await prices.BarsAsync(code, quarter.AddDays(-SnapshotSessionDays), quarter, cancellation);

                        closes[(code, quarter)] = answered.Count == 0 ? null : new QuarterEndClose(answered.MaxBy(bar => bar.SessionDate)!.RawClose);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException || !cancellation.IsCancellationRequested)
                    {
                        closes[(code, quarter)] = null;
                        unanswered.Add(FormattableString.Invariant($"{code} on {quarter:yyyy-MM-dd}: {failure.Message}"));
                    }
                }
            }

            // First each quarter's holdings, by its ISIN and then by its name at that quarter's end, as the matcher reads it.
            var quarters = new List<HeldQuarter>();

            foreach (var (filing, snapshot) in snapshots.OrderBy(one => one.Snapshot.Period))
            {
                var holdings = snapshot.Equity
                    .GroupBy(holding => FundSnapshots.IsinOf(holding) ?? holding.Cusip ?? holding.Name, StringComparer.Ordinal)
                    .Select(group => (Key: group.Key, Holding: group.First()))
                    .ToArray();

                await using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = InsertSnapshot;
                    insert.Parameters.AddWithValue("$index_code", indexCode);
                    insert.Parameters.AddWithValue("$period", snapshot.Period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$accession", snapshot.Accession);
                    insert.Parameters.AddWithValue("$filed", filing.Filed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$holdings", snapshot.Holdings.Count);
                    insert.Parameters.AddWithValue("$equity", holdings.Length);
                    insert.Parameters.AddWithValue("$pull", runId);

                    stored += await insert.ExecuteNonQueryAsync(cancellation);
                }

                foreach (var (key, holding) in holdings)
                {
                    await AskAboutAsync(matcher.NameCandidates(holding), snapshot.Period);

                    var match = matcher.Match(holding, code => CloseOn(code, snapshot.Period));

                    // A name nothing matched is read again more widely before it is left matched to none.
                    if (match.Ticker is null && match.By is null)
                    {
                        await AskAboutAsync(matcher.NameCandidates(holding, wider: true), snapshot.Period);

                        match = matcher.Match(holding, code => CloseOn(code, snapshot.Period), wider: true);
                    }

                    quarters.Add(new HeldQuarter(snapshot.Period, key, holding) { Match = match });
                }
            }

            // Whether a code's closes move in step with a holding's values a share over the quarters given.
            bool? InStep(string code, IEnumerable<HeldQuarter> over) =>
                HoldingMatcher.Tracks(
                [
                    .. over
                        .Select(one => (Price: one.Holding.ValuePerShare, Close: CloseOn(code, one.Period)))
                        .Where(pair => pair.Price is not null && pair.Close is not null)
                        .Select(pair => (pair.Price!.Value, pair.Close!)),
                ]);

            // Then each holding over every quarter it was held: each code it matched kept only where its closes move in step
            // with the fund's values a share over the quarters it matched, and matched to none at them where they do not.
            // see: A holding's code is kept only where its closes move in step with the fund's values a share, and a match by name is held to the level at each quarter end as well
            var byHolding = quarters
                .GroupBy(one => one.Key, StringComparer.Ordinal)
                .Select(group => group.OrderBy(one => one.Period).ToArray())
                .ToArray();
            var setAside = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var holding in byHolding)
            {
                foreach (var code in holding.Select(one => one.Match.Ticker).OfType<string>().Distinct(StringComparer.Ordinal).ToArray())
                {
                    var matched = holding.Where(one => one.Match.Ticker == code).ToArray();

                    if (InStep(code, matched) is not false)
                    {
                        continue;
                    }

                    if (!setAside.TryGetValue(holding[0].Key, out var aside))
                    {
                        setAside[holding[0].Key] = aside = new HashSet<string>(StringComparer.Ordinal);
                    }

                    aside.Add(code);
                    notInStep.Add(FormattableString.Invariant($"{code} for {matched[0].Holding.Name}, {matched.Length} quarter(s)"));

                    foreach (var one in matched)
                    {
                        one.Match = new HoldingMatch(null, null, []);
                    }
                }
            }

            // The codes the fund held by ISIN at each quarter end, those not in step set aside, each with the holding it was.
            var heldByIsin = quarters
                .Where(one => one.Match.By == HoldingMatcher.ByIsin && one.Match.Ticker is not null)
                .Select(one => (Quarter: one.Period, Code: one.Match.Ticker!, one.Key))
                .Distinct()
                .ToArray();

            // A code read against a holding's quarters matched to none: asked of the provider at each, and kept at the
            // quarters it holds a close at where it moves in step over them and sits at the level at each of them, as a
            // match by name at one quarter is, since a steady ratio off the level is another security's price moving with
            // the holding's, or where it stands at one ratio at every one of them, which is the provider's own closes
            // divided by a factor.
            // see: A holding's code is kept only where its closes move in step with the fund's values a share, and a match by name is held to the level at each quarter end as well
            async Task KeepWhereItTracksAsync(string code, List<HeldQuarter> none, string by)
            {
                foreach (var one in none)
                {
                    await AskAboutAsync([code], one.Period);
                }

                var priced = none.Where(one => CloseOn(code, one.Period) is not null).ToArray();
                (decimal, QuarterEndClose)[] read =
                [
                    .. priced
                        .Where(one => one.Holding.ValuePerShare is not null)
                        .Select(one => (one.Holding.ValuePerShare!.Value, CloseOn(code, one.Period)!)),
                ];

                if (priced.Length > 0
                    && InStep(code, priced) is true
                    && (read.All(pair => HoldingMatcher.AtTheLevel(pair.Item1, pair.Item2)) || HoldingMatcher.OneRatio(read)))
                {
                    foreach (var one in priced)
                    {
                        one.Match = new HoldingMatch(code, by, [code]);
                    }

                    none.RemoveAll(one => one.Match.Ticker is not null);
                }
            }

            foreach (var holding in byHolding)
            {
                var none = holding.Where(one => one.Match.Ticker is null).ToList();
                var aside = setAside.GetValueOrDefault(holding[0].Key) ?? [];

                // The quarters it was matched to none at read against its codes by name, a code its ISIN matched that was
                // not in step set aside, each kept at the quarters it holds a close at where it moves in step over them; a
                // delisted listing the pulled history holds no close of asked of the provider.
                foreach (var code in none
                    .SelectMany(one => matcher.NameCandidates(one.Holding, wider: true, despiteIsin: true))
                    .Distinct(StringComparer.Ordinal)
                    .Where(code => !aside.Contains(code))
                    .Order(StringComparer.Ordinal)
                    .ToArray())
                {
                    if (none.Count == 0)
                    {
                        break;
                    }

                    await KeepWhereItTracksAsync(code, none, HoldingMatcher.ByName);
                }
            }

            // Then the holdings still matched to none whose identifier the symbol lists carry under no code, mapped through
            // OpenFIGI, ten identifiers a request at the keyless pace, to the tickers each traded under on the US venues,
            // every one read against the holding's quarters under the same checks a name's code is; a code set aside as not
            // in step is not read again.
            // see: A holding the symbol lists carry under no code is mapped to the tickers it traded under through OpenFIGI, each held to the checks a name's code is
            if (mappings is not null)
            {
                var unmapped = byHolding
                    .Where(holding => holding.Any(one => one.Match.Ticker is null) && (FundSnapshots.IsinOf(holding[0].Holding) ?? holding[0].Holding.Cusip) is not null)
                    .Select(holding => (Isin: FundSnapshots.IsinOf(holding[0].Holding) ?? holding[0].Holding.Cusip!, Holding: holding))
                    .GroupBy(pair => pair.Isin, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Select(pair => pair.Holding).ToArray(), StringComparer.Ordinal);
                var isins = unmapped.Keys.Order(StringComparer.Ordinal).ToArray();

                for (var at = 0; at < isins.Length; at += OpenFigiMappings.IdentifiersARequest)
                {
                    if (at > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(60.0 / OpenFigiMappings.RequestsAMinute), cancellation);
                    }

                    var batch = isins[at..Math.Min(at + OpenFigiMappings.IdentifiersARequest, isins.Length)];
                    IReadOnlyList<IdentifierMapping> answered;

                    try
                    {
                        answered = OpenFigiMappings.Parse(await mappings.MapAsync(batch, cancellation), batch);
                    }
                    catch (Exception failure) when (failure is ProviderRefusal or FormatException)
                    {
                        unanswered.Add(FormattableString.Invariant($"OpenFIGI for {string.Join(", ", batch)}: {failure.Message}"));
                        continue;
                    }

                    mapped += batch.Length;

                    foreach (var mapping in answered)
                    {
                        foreach (var holding in unmapped[mapping.Identifier])
                        {
                            var none = holding.Where(one => one.Match.Ticker is null).ToList();
                            var aside = setAside.GetValueOrDefault(holding[0].Key) ?? [];

                            foreach (var code in mapping.Tickers.Where(code => !aside.Contains(code)))
                            {
                                if (none.Count == 0)
                                {
                                    break;
                                }

                                await KeepWhereItTracksAsync(code, none, HoldingMatcher.ByMapping);
                            }
                        }
                    }
                }
            }

            foreach (var holding in byHolding)
            {
                var none = holding.Where(one => one.Match.Ticker is null).ToList();
                var aside = setAside.GetValueOrDefault(holding[0].Key) ?? [];

                // Then a company renamed since: a code the fund held by ISIN within a year of a quarter still matched to none,
                // read off the pulled history alone, kept where its close equals the value a share to the cent at two of
                // those quarter ends or more and it moves in step over every one it holds a close at. A code the fund held
                // by ISIN as another holding at a quarter end this holding was held at is that other holding and not this
                // one renamed, so it is not read.
                // see: A renamed company is matched to a code its fund held by ISIN within a year where its close equals the value a share to the cent at two quarter ends
                // see: A code the fund held by ISIN beside a holding at one quarter end is another holding and never that holding renamed
                if (none.Count < HoldingMatcher.CentQuarters)
                {
                    continue;
                }

                var beside = heldByIsin
                    .Where(held => held.Key != holding[0].Key && holding.Any(one => one.Period == held.Quarter))
                    .Select(held => held.Code)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var code in heldByIsin
                    .Where(held => none.Any(one => Math.Abs(held.Quarter.DayNumber - one.Period.DayNumber) <= HoldingMatcher.HeldWithinDays))
                    .Select(held => held.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Where(code => !aside.Contains(code) && !beside.Contains(code))
                    .Order(StringComparer.Ordinal)
                    .ToArray())
                {
                    var priced = none.Where(one => CloseOn(code, one.Period) is not null).ToArray();

                    if (priced.Count(one => one.Holding.ValuePerShare is { } price && HoldingMatcher.ToTheCent(price, CloseOn(code, one.Period)!)) >= HoldingMatcher.CentQuarters
                        && InStep(code, priced) is true)
                    {
                        foreach (var one in priced)
                        {
                            one.Match = new HoldingMatch(code, HoldingMatcher.ByHeld, [code]);
                        }

                        break;
                    }
                }
            }

            // Then the schedules filed with no identifier, whose holdings are keyed by their names and so never meet the
            // same company's rows keyed by ISIN: a quarter still matched to none carries the code the next coded quarter's
            // holding of the identical name matched, where the closes move in step across the joined quarters and the
            // join itself holds the ratio, the schedule's quarter in step with the first coded quarter alone, since the
            // step the in-step reading allows would otherwise let a schedule's quarter at any ratio join a later company
            // that took the name.
            // see: A holding of a schedule filed with no identifier carries the code the next coded quarter's holding of the identical name matched, where the closes move in step across them
            var coded = quarters
                .Where(one => one.Match.Ticker is not null && (FundSnapshots.IsinOf(one.Holding) ?? one.Holding.Cusip) is not null)
                .GroupBy(one => HoldingMatcher.NameKey(one.Holding.Name), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(one => one.Period).ToArray(), StringComparer.Ordinal);

            foreach (var one in quarters.Where(one => one.Match.Ticker is null && FundSnapshots.IsinOf(one.Holding) is null && one.Holding.Cusip is null))
            {
                if (!coded.TryGetValue(HoldingMatcher.NameKey(one.Holding.Name), out var later)
                    || later.FirstOrDefault(next => next.Period > one.Period) is not { } next)
                {
                    continue;
                }

                // The quarters joined are the next coded quarter's own security's, read by its key across any later rename,
                // as the in-step reading over the coded quarters reads them, and not the name's, which a rename cuts short.
                var code = next.Match.Ticker!;
                var joined = quarters.Where(held => held.Key == next.Key && held.Match.Ticker == code).Prepend(one).OrderBy(held => held.Period).ToArray();

                await AskAboutAsync([code], one.Period);

                if (CloseOn(code, one.Period) is not null && InStep(code, [one, next]) is true && InStep(code, joined) is true)
                {
                    one.Match = new HoldingMatch(code, HoldingMatcher.ByCarried, [code]);
                    carried++;
                }
            }

            // Each holding then stored, or matched again where an earlier pull stored its quarter.
            foreach (var group in quarters.GroupBy(one => one.Period))
            {
                var period = group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var earlier = new Dictionary<string, (string? Ticker, string? By, bool Valued)>(StringComparer.Ordinal);

                await using (var read = connection.CreateCommand())
                {
                    read.CommandText = StoredHoldings;
                    read.Parameters.AddWithValue("$index_code", indexCode);
                    read.Parameters.AddWithValue("$period", period);

                    await using var rows = await read.ExecuteReaderAsync(cancellation);

                    while (await rows.ReadAsync(cancellation))
                    {
                        earlier[rows.GetString(0)] = (rows.IsDBNull(1) ? null : rows.GetString(1), rows.IsDBNull(2) ? null : rows.GetString(2), rows.GetInt64(3) == 1);
                    }
                }

                foreach (var (key, holding, match) in group.Select(one => (one.Key, one.Holding, one.Match)))
                {
                    equity++;
                    byIsin += match.By == HoldingMatcher.ByIsin ? 1 : 0;
                    byName += match.By == HoldingMatcher.ByName ? 1 : 0;
                    byHeld += match.By == HoldingMatcher.ByHeld ? 1 : 0;
                    byMapping += match.By == HoldingMatcher.ByMapping ? 1 : 0;
                    between += match.Between.Count > 1 ? 1 : 0;
                    widely += match.Wider ? 1 : 0;

                    if (match.Ticker is null)
                    {
                        unmatched.Add(FormattableString.Invariant($"{holding.Name} on {group.Key:yyyy-MM-dd}"));

                        if (match.PricedOut)
                        {
                            pricedOut.Add(FormattableString.Invariant($"{holding.Name} on {group.Key:yyyy-MM-dd}"));
                        }
                    }

                    var shares = holding.Shares is { } count ? (object)count.ToString(CultureInfo.InvariantCulture) : DBNull.Value;
                    var value = holding.Value is { } worth ? (object)worth.ToString(CultureInfo.InvariantCulture) : DBNull.Value;

                    if (earlier.TryGetValue(key, out var was))
                    {
                        if (was.Ticker == match.Ticker && was.By == match.By && (was.Valued || holding.Value is null))
                        {
                            continue;
                        }

                        await using var update = connection.CreateCommand();
                        update.CommandText = MatchAgain;
                        update.Parameters.AddWithValue("$index_code", indexCode);
                        update.Parameters.AddWithValue("$period", period);
                        update.Parameters.AddWithValue("$holding", key);
                        update.Parameters.AddWithValue("$ticker", (object?)match.Ticker ?? DBNull.Value);
                        update.Parameters.AddWithValue("$matched_by", (object?)match.By ?? DBNull.Value);
                        update.Parameters.AddWithValue("$shares", shares);
                        update.Parameters.AddWithValue("$value_usd", value);

                        again += await update.ExecuteNonQueryAsync(cancellation);

                        continue;
                    }

                    await using var insert = connection.CreateCommand();
                    insert.CommandText = InsertHolding;
                    insert.Parameters.AddWithValue("$index_code", indexCode);
                    insert.Parameters.AddWithValue("$period", period);
                    insert.Parameters.AddWithValue("$holding", key);
                    insert.Parameters.AddWithValue("$name", holding.Name);
                    insert.Parameters.AddWithValue("$cusip", (object?)holding.Cusip ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$isin", (object?)FundSnapshots.IsinOf(holding) ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$ticker", (object?)match.Ticker ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$matched_by", (object?)match.By ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$pull", runId);
                    insert.Parameters.AddWithValue("$shares", shares);
                    insert.Parameters.AddWithValue("$value_usd", value);

                    await insert.ExecuteNonQueryAsync(cancellation);
                }
            }
        }

        var periods = snapshots.Select(one => one.Snapshot.Period).Order().ToArray();
        var outcome = new HistoryHoldingOutcome(
            indexCode,
            filings.Count + (refused is null ? FundSchedules.Filings.Count : 0),
            refused is null ? snapshots.Count : 0,
            stored,
            refused is null && periods.Length > 0 ? periods[0] : null,
            refused is null && periods.Length > 0 ? periods[^1] : null,
            equity,
            byIsin,
            byName,
            equity - byIsin - byName - byHeld - carried - byMapping,
            between,
            unmatched,
            unread,
            refused,
            feed.Requests + symbols.Requests - requestsBefore + asked + (mappings?.Requests ?? 0),
            again,
            asked,
            unanswered,
            widely,
            pricedOut,
            byHeld,
            notInStep,
            carried,
            byMapping,
            mapped,
            mappings?.Requests ?? 0);

        await AppendAsync(
            connection,
            runId,
            HoldingsStage,
            startedAt,
            clock.UtcNow,
            refused is not null ? "refused" : unread.Count > 0 ? Partial : "ok",
            equity,
            outcome.Requests,
            Detail(outcome),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // A holdings pull in words, as its row and the command state it: the snapshots read and their span, the holdings of
    // common stock and the keys they matched by, those by the wider reading of their names, the holdings an earlier pull
    // stored matched again to another code or to none, the codes asked of the provider whether they traded at a
    // quarter's end, the holdings whose codes traded only at another price, the codes matched to none where their closes
    // did not move in step with the fund's values a share, the holdings matched by none and the filings not read, each
    // named.
    public static string Detail(HistoryHoldingOutcome outcome) =>
        outcome.Refused is { } why
            ? FormattableString.Invariant($"{outcome.Index}: nothing was stored, {why}; {outcome.Requests} request(s)")
            : FormattableString.Invariant($"{outcome.Index}: {outcome.Snapshots} snapshot(s) of {outcome.Filings} filing(s), {outcome.Stored} new, from {outcome.First:yyyy-MM-dd} to {outcome.Last:yyyy-MM-dd}, {outcome.Equity} holding(s) of common stock, {outcome.ByIsin} matched by ISIN, {outcome.ByName} by name alone, {outcome.Widely} of them by its wider reading, {outcome.Held} by a code its fund held by ISIN within a year, {outcome.Carried} carried from the next coded quarter's holding of the identical name, and {outcome.Unmatched} by none, {(outcome.PricedOut ?? []).Count} of them because each code its name reads traded at a close the fund's value a share stood more than {HoldingMatcher.ValueTolerance * 100:0}% from, {(outcome.NotInStep ?? []).Count} code(s) matched to none at a holding's quarters because their closes did not move in step with the fund's values a share, {outcome.Between} standing between codes, {outcome.Again} holding(s) of quarters an earlier pull stored matched again to another code or to none, {outcome.Asked} code(s) asked of the provider whether they traded at a quarter's end; {outcome.Requests} request(s)")
              + (outcome.Mapped == 0 && outcome.MappingRequests == 0 ? string.Empty : FormattableString.Invariant($"; {outcome.Mapped} identifier(s) mapped through OpenFIGI in {outcome.MappingRequests} request(s), {outcome.ByMapping} holding(s) matched by a ticker it answered"))
              + (outcome.Unread.Count == 0 ? string.Empty : "; not read: " + string.Join("; ", outcome.Unread))
              + ((outcome.NotInStep ?? []).Count == 0 ? string.Empty : "; not in step: " + string.Join("; ", outcome.NotInStep!.Take(HoldingsNamed)) + (outcome.NotInStep!.Count > HoldingsNamed ? FormattableString.Invariant($" and {outcome.NotInStep.Count - HoldingsNamed} more") : string.Empty))
              + ((outcome.PricedOut ?? []).Count == 0 ? string.Empty : "; at another price: " + string.Join("; ", outcome.PricedOut!.Take(HoldingsNamed)) + (outcome.PricedOut!.Count > HoldingsNamed ? FormattableString.Invariant($" and {outcome.PricedOut.Count - HoldingsNamed} more") : string.Empty))
              + ((outcome.Unanswered ?? []).Count == 0 ? string.Empty : "; not answered: " + string.Join("; ", outcome.Unanswered!.Take(HoldingsNamed)) + (outcome.Unanswered!.Count > HoldingsNamed ? FormattableString.Invariant($" and {outcome.Unanswered.Count - HoldingsNamed} more") : string.Empty))
              + (outcome.UnmatchedNames.Count == 0 ? string.Empty : "; matched by none: " + string.Join("; ", outcome.UnmatchedNames.Take(HoldingsNamed)) + (outcome.UnmatchedNames.Count > HoldingsNamed ? FormattableString.Invariant($" and {outcome.UnmatchedNames.Count - HoldingsNamed} more") : string.Empty));

    // A holding of a quarter and the code the pull matched it to, which its reading across every quarter it was held may
    // change.
    sealed record HeldQuarter(DateOnly Period, string Key, FiledHolding Holding)
    {
        public required HoldingMatch Match { get; set; }
    }

    // How many of the holdings matched by none a pull's row names, the rest counted.
    public const int HoldingsNamed = 40;

    // How many days before a snapshot's quarter end a code's pulled bar still says it traded then, a quarter's end
    // falling on a weekend or a holiday.
    public const int SnapshotSessionDays = 6;

    // A members pull in words, as its row and the command state it.
    public static string Detail(HistoryMemberOutcome outcome) =>
        outcome.Refused is { } why
            ? FormattableString.Invariant($"{outcome.Index}: nothing was stored, {why}; {outcome.Requests} request(s)")
            : FormattableString.Invariant($"{outcome.Index}: {outcome.Listed} member(s) today, {outcome.Written} new and {outcome.Held} held, survivors alone since the answer carries no span of membership; {outcome.Requests} request(s)");

    // A feed a branch was handed, resolved, or the refusal written and none.
    static T? Resolved<T>(Func<T>? feed, string what, TextWriter error)
        where T : class
    {
        if (feed is null)
        {
            error.WriteLine($"history-pull: this command was handed no {what} feed to ask.");

            return null;
        }

        try
        {
            return feed();
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return null;
        }
    }

    // Pulls the company of every name the index held from a date to tonight, one request a name: its filer, its GICS
    // classification, the day it was delisted, and each quarterly share count carrying the day its balance sheet was
    // filed, on the split basis of tonight's session. A name the provider does not serve, does not answer in time or
    // answers in a form that cannot be read stores nothing and is named, and the others are stored. The pull states
    // the names filing no sector, no count or no CIK, and reads each of the fourteen GICS moved after the close of
    // 2023-03-17 against the sector the provider files for it. A date on or after tonight's session is refused before
    // any request.
    // see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
    public static async Task<HistoryCompanyOutcome> PullCompaniesAsync(
        ICompanyFeed companies,
        IClock clock,
        string databaseFile,
        string indexCode,
        DateOnly from,
        string runId,
        Action<string>? progress = null,
        CancellationToken cancellation = default,
        IReadOnlyCollection<string>? only = null,
        bool pastTheStop = false)
    {
        var startedAt = clock.UtcNow;
        var through = clock.SessionDateAt(startedAt);

        Refuse(from, through);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var names = await NamesAsync(connection, indexCode, from, through, cancellation, only);
        var requestsBefore = companies.Requests;
        var answered = new List<CompanyAnswer>();
        var unanswered = new List<string>();

        // Stated before the first request: one fundamentals request a name, at that endpoint's weight.
        // see: Every pull states its provider calls before it runs and stops for the operator at the day's cap
        var what = FormattableString.Invariant($"{names.Count:N0} name(s) at the fundamentals weight of {ProviderWeights.Fundamentals}");

        progress?.Invoke(ProviderStop.Stated(what, names.Count * ProviderWeights.Fundamentals));
        ProviderStop.Hold(what, names.Count * ProviderWeights.Fundamentals, pastTheStop);

        foreach (var ticker in names)
        {
            try
            {
                answered.Add(await companies.CompanyAsync(ticker, cancellation));
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                unanswered.Add($"{ticker}: the provider did not answer in time on any try");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                unanswered.Add(NotServed(ticker, failure));
            }

            if ((answered.Count + unanswered.Count) % 50 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"{answered.Count + unanswered.Count} of {names.Count} name(s) asked"));
            }
        }

        var companiesBefore = await CountAsync(connection, CompanyCount, null, cancellation);
        var sharesBefore = await CountAsync(connection, SharesCount, null, cancellation);
        var incomeBefore = await CountAsync(connection, IncomeCount, null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var company in answered)
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = InsertCompany;
                insert.Parameters.AddWithValue("$ticker", company.Ticker);
                insert.Parameters.AddWithValue("$cik", (object?)company.Cik ?? DBNull.Value);
                insert.Parameters.AddWithValue("$sector", (object?)company.Sector ?? DBNull.Value);
                insert.Parameters.AddWithValue("$industry_group", (object?)company.IndustryGroup ?? DBNull.Value);
                insert.Parameters.AddWithValue("$industry", (object?)company.Industry ?? DBNull.Value);
                insert.Parameters.AddWithValue("$sub_industry", (object?)company.SubIndustry ?? DBNull.Value);
                insert.Parameters.AddWithValue("$delisted_on", company.DelistedOn is { } delisted ? Text(delisted) : DBNull.Value);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }

            foreach (var count in company.Counts)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertShares;
                insert.Parameters.AddWithValue("$ticker", company.Ticker);
                insert.Parameters.AddWithValue("$period_end", Text(count.PeriodEnd));
                insert.Parameters.AddWithValue("$filing_date", Text(count.FilingDate));
                Money.Bind(insert, "$shares", count.Shares);
                insert.Parameters.AddWithValue("$basis_session", Text(through));
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }

            foreach (var quarter in company.Income)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertIncome;
                insert.Parameters.AddWithValue("$ticker", company.Ticker);
                insert.Parameters.AddWithValue("$period_end", Text(quarter.PeriodEnd));
                insert.Parameters.AddWithValue("$filing_date", Text(quarter.FilingDate));
                BindMoney(insert, "$net_income", quarter.NetIncome);
                BindMoney(insert, "$operating_income", quarter.OperatingIncome);
                BindMoney(insert, "$interest_expense", quarter.InterestExpense);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        var companiesWritten = await CountAsync(connection, CompanyCount, null, cancellation) - companiesBefore;
        var sharesWritten = await CountAsync(connection, SharesCount, null, cancellation) - sharesBefore;
        var incomeWritten = await CountAsync(connection, IncomeCount, null, cancellation) - incomeBefore;
        var requests = companies.Requests - requestsBefore;
        var (movesFiled, movesNot) = MovesAgainst(answered);
        var outcome = new HistoryCompanyOutcome(
            from,
            through,
            names.Count,
            answered.Count,
            unanswered,
            companiesWritten,
            sharesWritten,
            [.. answered.Where(company => company.Sector is null).Select(company => company.Ticker)],
            [.. answered.Where(company => company.Counts.Count == 0).Select(company => company.Ticker)],
            [.. answered.Where(company => company.Cik is null).Select(company => company.Ticker)],
            answered.Sum(company => company.SheetsUncounted),
            movesFiled,
            movesNot,
            requests,
            incomeWritten,
            answered.Sum(company => company.StatementsUndated),
            [.. answered.Where(company => company.Income.Count == 0).Select(company => company.Ticker)]);

        await AppendAsync(
            connection,
            runId,
            CompaniesStage,
            startedAt,
            clock.UtcNow,
            unanswered.Count == 0 ? "ok" : Partial,
            companiesWritten + sharesWritten + incomeWritten,
            requests,
            JsonSerializer.Serialize(new
            {
                from = Text(from),
                through = Text(through),
                names = names.Count,
                answered = answered.Count,
                companies = companiesWritten,
                counts = sharesWritten,
                income = incomeWritten,
                statementsUndated = outcome.StatementsUndated,
                noIncome = outcome.NoIncome,
                unanswered,
                noSector = outcome.NoSector,
                noCount = outcome.NoCount,
                noCik = outcome.NoCik,
                sheetsUncounted = outcome.SheetsUncounted,
                moves = FormattableString.Invariant($"{movesFiled} of {GicsSectors.Moves.Count} filed in the sector they moved to"),
                movesNot,
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // A money figure a filing may state none of, bound as none where it does.
    static void BindMoney(SqliteCommand command, string name, decimal? value)
    {
        if (value is { } money)
        {
            Money.Bind(command, name, money);
        }
        else
        {
            command.Parameters.AddWithValue(name, DBNull.Value);
        }
    }

    // The fourteen moves of 2023-03-17 read against the answers: how many are filed in the sector they moved to under
    // the filer the table names, and each that is not, unanswered among them.
    public static (int Filed, IReadOnlyList<string> Not) MovesAgainst(IReadOnlyList<CompanyAnswer> answered)
    {
        var filed = 0;
        var not = new List<string>();

        foreach (var move in GicsSectors.Moves)
        {
            var company = answered.FirstOrDefault(answer => string.Equals(answer.Ticker, move.Ticker, StringComparison.Ordinal));

            if (company is null)
            {
                not.Add($"{move.Ticker}: not answered");
            }
            else if (!string.Equals(company.Sector, move.To, StringComparison.Ordinal))
            {
                not.Add($"{move.Ticker}: filed in {company.Sector ?? "no sector"}, not {move.To}");
            }
            else if (company.Cik is { } cik && !string.Equals(cik, move.Cik, StringComparison.Ordinal))
            {
                not.Add($"{move.Ticker}: filed under CIK {cik}, not {move.Cik}");
            }
            else
            {
                filed++;
            }
        }

        return (filed, not);
    }

    // Pulls the splits of every name the index held from a date to tonight, one request a name, each split from the
    // date on. A name not served is named and the others stored, as the companies pull does.
    // see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
    public static async Task<HistorySplitOutcome> PullSplitsAsync(
        ISplitHistoryFeed splits,
        IClock clock,
        string databaseFile,
        string indexCode,
        DateOnly from,
        string runId,
        Action<string>? progress = null,
        CancellationToken cancellation = default,
        IReadOnlyCollection<string>? only = null,
        bool pastTheStop = false)
    {
        var startedAt = clock.UtcNow;
        var through = clock.SessionDateAt(startedAt);

        Refuse(from, through);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var names = await NamesAsync(connection, indexCode, from, through, cancellation, only);
        var requestsBefore = splits.Requests;
        var answered = new List<(string Ticker, IReadOnlyList<SplitAnswer> Splits)>();
        var unanswered = new List<string>();

        // Stated before the first request: one request a name at the historical weight.
        // see: Every pull states its provider calls before it runs and stops for the operator at the day's cap
        var what = FormattableString.Invariant($"{names.Count:N0} name(s), one request each");

        progress?.Invoke(ProviderStop.Stated(what, names.Count * ProviderWeights.HistoricalPerTicker));
        ProviderStop.Hold(what, names.Count * ProviderWeights.HistoricalPerTicker, pastTheStop);

        foreach (var ticker in names)
        {
            try
            {
                answered.Add((ticker, [.. (await splits.SplitsAsync(ticker, from, cancellation)).Where(split => split.ExDate >= from && split.ExDate <= through)]));
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                unanswered.Add($"{ticker}: the provider did not answer in time on any try");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                unanswered.Add(NotServed(ticker, failure));
            }

            if ((answered.Count + unanswered.Count) % 50 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"{answered.Count + unanswered.Count} of {names.Count} name(s) asked"));
            }
        }

        var before = await CountAsync(connection, SplitCount, null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var (ticker, held) in answered)
        {
            foreach (var split in held)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertSplit;
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$ex_date", Text(split.ExDate));
                Money.Bind(insert, "$new_shares", split.NewShares);
                Money.Bind(insert, "$old_shares", split.OldShares);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        var written = await CountAsync(connection, SplitCount, null, cancellation) - before;
        var requests = splits.Requests - requestsBefore;
        var outcome = new HistorySplitOutcome(
            from,
            through,
            names.Count,
            answered.Count,
            unanswered,
            answered.Count(entry => entry.Splits.Count > 0),
            written,
            requests,
            answered.Sum(entry => entry.Splits.Count(split => new FiledSplit(split.ExDate, split.NewShares, split.OldShares).Plain)));

        await AppendAsync(
            connection,
            runId,
            SplitsStage,
            startedAt,
            clock.UtcNow,
            unanswered.Count == 0 ? "ok" : Partial,
            written,
            requests,
            JsonSerializer.Serialize(new
            {
                from = Text(from),
                through = Text(through),
                names = names.Count,
                answered = answered.Count,
                withASplit = outcome.WithASplit,
                splits = written,
                plain = outcome.Plain,
                unanswered,
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // Pulls the eleven sector funds' daily series from a date to tonight, one request a fund, into the pulled market
    // series beside the index's and the VIX's, each in the adjusted form a member's bars take. A fund not served stores
    // nothing and is named, the others are stored, and the pull's row says partial. Handed the index and credit funds,
    // it pulls those under a stage of their own.
    // see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
    public static async Task<HistorySectorFundOutcome> PullSectorFundsAsync(
        IHistoricalBarFeed bars,
        IClock clock,
        string databaseFile,
        DateOnly from,
        string runId,
        CancellationToken cancellation = default,
        IReadOnlyList<string>? funds = null,
        string stage = SectorFundsStage)
    {
        var startedAt = clock.UtcNow;
        var through = clock.SessionDateAt(startedAt);

        Refuse(from, through);

        var requestsBefore = bars.Requests;
        var answered = new List<(string Fund, IReadOnlyList<ProviderBar> Bars)>();
        var refused = new List<string>();

        foreach (var fund in funds ?? SectorFunds)
        {
            try
            {
                var series = await bars.BarsAsync(fund, from, through, cancellation);

                if (series.Count == 0)
                {
                    refused.Add(FormattableString.Invariant($"{fund}: the provider sent no session"));
                }
                else
                {
                    answered.Add((fund, series));
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                refused.Add($"{fund}: the provider did not answer in time on any try");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                refused.Add(NotServed(fund, failure));
            }
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var before = await CountAsync(connection, MarketBarCount, null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var (fund, series) in answered)
        {
            foreach (var bar in series)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertMarketBar;
                insert.Parameters.AddWithValue("$series", fund);
                insert.Parameters.AddWithValue("$session_date", Text(bar.SessionDate));
                Money.Bind(insert, "$open", bar.Open);
                Money.Bind(insert, "$high", bar.High);
                Money.Bind(insert, "$low", bar.Low);
                Money.Bind(insert, "$close", bar.Close);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        var written = await CountAsync(connection, MarketBarCount, null, cancellation) - before;
        var requests = bars.Requests - requestsBefore;
        IReadOnlyList<MarketSeriesStored> stored =
        [
            .. answered.Select(entry => new MarketSeriesStored(entry.Fund, entry.Bars.Count, entry.Bars.Min(bar => bar.SessionDate), entry.Bars.Max(bar => bar.SessionDate))),
        ];
        var outcome = new HistorySectorFundOutcome(from, through, stored, refused, written, requests);

        await AppendAsync(
            connection,
            runId,
            stage,
            startedAt,
            clock.UtcNow,
            refused.Count == 0 ? "ok" : Partial,
            written,
            requests,
            JsonSerializer.Serialize(new
            {
                from = Text(from),
                through = Text(through),
                series = stored.Select(Described).ToArray(),
                refused,
                stored = written,
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // Pulls, for every filer the pulled companies carry, every figure it stated under each revenue concept with the
    // day each filing was made, one request a filer for its whole facts, waiting a tenth of a second after each. A
    // concept a filer never filed under is no figure and no failure; a filer not served is named and the rest stored.
    // Refused where no pulled company carries a filer, since the companies pull is what names them.
    // see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
    public static async Task<HistoryRevenueOutcome> PullRevenueAsync(
        IFiledRevenueFeed revenue,
        IClock clock,
        string databaseFile,
        string runId,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        Action<string>? progress = null,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var pause = wait ?? Task.Delay;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var filers = await ColumnAsync(connection, PulledFilers, cancellation);
        var withoutAFiler = await ColumnAsync(connection, PulledWithoutAFiler, cancellation);

        if (filers.Count == 0)
        {
            throw new ArgumentException(
                "No pulled company carries a CIK, so there is no filer to ask. The companies pull names them, and is run before this one.",
                nameof(databaseFile));
        }

        var requestsBefore = revenue.Requests;
        var answered = new List<(string Cik, string Concept, IReadOnlyList<ConceptFact> Facts)>();
        var unanswered = new List<string>();
        var asked = 0;

        foreach (var cik in filers)
        {
            try
            {
                var filed = await revenue.RevenueAsync(cik, FirstFiledRevenue.Concepts, cancellation);

                answered.AddRange(FirstFiledRevenue.Concepts.Select(concept => (cik, concept, filed.TryGetValue(concept, out var facts) ? facts : (IReadOnlyList<ConceptFact>)[])));
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                unanswered.Add($"CIK {cik}: the archive did not answer in time on any try");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                unanswered.Add(NotServed($"CIK {cik}", failure));
            }

            await pause(TimeSpan.FromSeconds(1.0 / ArchiveRequestsASecond), cancellation);

            if (++asked % 50 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"{asked} of {filers.Count} filer(s) asked"));
            }
        }

        var before = await CountAsync(connection, RevenueCount, null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var (cik, concept, facts) in answered)
        {
            foreach (var fact in facts)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertRevenue;
                insert.Parameters.AddWithValue("$cik", cik);
                insert.Parameters.AddWithValue("$concept", concept);
                insert.Parameters.AddWithValue("$period_start", Text(fact.Start));
                insert.Parameters.AddWithValue("$period_end", Text(fact.End));
                insert.Parameters.AddWithValue("$accession", fact.Accession);
                Money.Bind(insert, "$dollars", fact.Value);
                insert.Parameters.AddWithValue("$filed", Text(fact.Filed));
                insert.Parameters.AddWithValue("$form", fact.Form);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        var written = await CountAsync(connection, RevenueCount, null, cancellation) - before;
        var requests = revenue.Requests - requestsBefore;
        IReadOnlyList<RevenueConceptFiled> byConcept =
        [
            .. FirstFiledRevenue.Concepts.Select(concept => new RevenueConceptFiled(
                concept,
                answered.Count(entry => entry.Concept == concept && entry.Facts.Count > 0),
                answered.Where(entry => entry.Concept == concept).Sum(entry => entry.Facts.Count))),
        ];
        var noRevenue = answered
            .GroupBy(entry => entry.Cik, StringComparer.Ordinal)
            .Where(filer => filer.All(entry => entry.Facts.Count == 0))
            .Select(filer => filer.Key)
            .ToArray();
        var outcome = new HistoryRevenueOutcome(filers.Count, withoutAFiler, byConcept, noRevenue, unanswered, written, requests);

        await AppendAsync(
            connection,
            runId,
            RevenueStage,
            startedAt,
            clock.UtcNow,
            unanswered.Count == 0 ? "ok" : Partial,
            written,
            requests,
            JsonSerializer.Serialize(new
            {
                filers = filers.Count,
                withoutAFiler,
                concepts = byConcept.Select(Described).ToArray(),
                noRevenue,
                unanswered,
                figures = written,
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // A name, fund or filer the provider did not serve, with why: a refusal or a payload the reader refused in its own
    // words, anything else as an answer that could not be read.
    static string NotServed(string what, Exception failure) =>
        failure is ProviderRefusal or FormatException
            ? $"{what}: {failure.Message}"
            : $"{what}: its answer could not be read: {failure.Message}";

    static void Refuse(DateOnly from, DateOnly through)
    {
        if (from >= through)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"A pull reaches from a date before tonight's session, {through:yyyy-MM-dd}, and {from:yyyy-MM-dd} is not before it."),
                nameof(from));
        }
    }

    // The provider's surprise in per cent, a statistic, read from the text the feed kept; none where the print
    // carries no estimate, no actual or no surprise, since a surprise against nothing is not one.
    public static double? SurprisePercent(CalendarEvent print) =>
        print.Estimate is { Length: > 0 } && print.Actual is { Length: > 0 } && print.Surprise is { Length: > 0 } surprise
        && double.TryParse(surprise, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? percent
            : null;

    // Removes one pull's rows from the twelve tables and says so on the run log. A pull no row carries is
    // refused and nothing is written, so a mistyped id cannot record a removal that removed nothing.
    public static async Task<HistoryPurgeOutcome> PurgeAsync(
        IClock clock,
        string databaseFile,
        string pull,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var barsHeld = await CountAsync(connection, BarsOfPull, pull, cancellation);
        var earningsHeld = await CountAsync(connection, EarningsOfPull, pull, cancellation);
        var surprisesHeld = await CountAsync(connection, SurprisesOfPull, pull, cancellation);
        var marketHeld = await CountAsync(connection, MarketBarsOfPull, pull, cancellation);
        var companiesHeld = await CountAsync(connection, CompaniesOfPull, pull, cancellation);
        var sharesHeld = await CountAsync(connection, SharesOfPull, pull, cancellation);
        var splitsHeld = await CountAsync(connection, SplitsOfPull, pull, cancellation);
        var revenueHeld = await CountAsync(connection, RevenueOfPull, pull, cancellation);
        var membersHeld = await CountAsync(connection, MembersOfPull, pull, cancellation);
        var incomeHeld = await CountAsync(connection, IncomeOfPull, pull, cancellation);
        var snapshotsHeld = await CountAsync(connection, SnapshotsOfPull, pull, cancellation);
        var holdingsHeld = await CountAsync(connection, HoldingsOfPull, pull, cancellation);

        if (barsHeld + earningsHeld + surprisesHeld + marketHeld + companiesHeld + sharesHeld + splitsHeld + revenueHeld + membersHeld + incomeHeld + snapshotsHeld + holdingsHeld == 0)
        {
            throw new ArgumentException($"No pulled row carries the pull '{pull}', so there is nothing to remove.", nameof(pull));
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var removal in new[]
        {
            DeleteBarsOfPull, DeleteEarningsOfPull, DeleteSurprisesOfPull, DeleteMarketBarsOfPull,
            DeleteCompaniesOfPull, DeleteSharesOfPull, DeleteSplitsOfPull, DeleteRevenueOfPull, DeleteMembersOfPull,
            DeleteIncomeOfPull, DeleteSnapshotsOfPull, DeleteHoldingsOfPull,
        })
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = removal;
            delete.Parameters.AddWithValue("$pull", pull);

            await delete.ExecuteNonQueryAsync(cancellation);
        }

        await AppendAsync(
            connection,
            runId,
            PurgeStage,
            startedAt,
            clock.UtcNow,
            "ok",
            0,
            0,
            JsonSerializer.Serialize(new
            {
                pull,
                bars = barsHeld,
                earnings = earningsHeld,
                surprises = surprisesHeld,
                market = marketHeld,
                companies = companiesHeld,
                counts = sharesHeld,
                splits = splitsHeld,
                revenue = revenueHeld,
                members = membersHeld,
                income = incomeHeld,
                snapshots = snapshotsHeld,
                holdings = holdingsHeld,
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return new HistoryPurgeOutcome(pull, barsHeld, earningsHeld, surprisesHeld, marketHeld, companiesHeld, sharesHeld, splitsHeld, revenueHeld, membersHeld, incomeHeld, snapshotsHeld, holdingsHeld);
    }

    // The windows the earnings calendar is asked for: each calendar month the span touches, cut to
    // the span at both ends.
    public static IReadOnlyList<(DateOnly From, DateOnly To)> MonthsOf(DateOnly from, DateOnly through)
    {
        var months = new List<(DateOnly, DateOnly)>();

        for (var start = new DateOnly(from.Year, from.Month, 1); start <= through; start = start.AddMonths(1))
        {
            var last = start.AddMonths(1).AddDays(-1);

            months.Add((start < from ? from : start, last > through ? through : last));
        }

        return months;
    }

    // The sessions each name's series misses, and the days read as no session at all. A day is a
    // session where at least half the names whose series span it hold it, and a stray where fewer do:
    // the closure table covers the store's own years alone, and read against every date any name holds,
    // a provider's bar on a day the exchange was closed is a session every other name missed.
    public static (IReadOnlyList<Gap> Holes, IReadOnlyList<Stray> Strays) HolesIn(IReadOnlyDictionary<string, IReadOnlyCollection<DateOnly>> series)
    {
        if (!TradingCalendar.CanDetect(series))
        {
            return ([], []);
        }

        var spans = series.Values
            .Where(held => held.Count > 0)
            .Select(held => (First: held.Min(), Last: held.Max()))
            .ToArray();
        var holders = new SortedDictionary<DateOnly, List<string>>();

        foreach (var (ticker, held) in series)
        {
            foreach (var day in held.Distinct())
            {
                if (!holders.TryGetValue(day, out var on))
                {
                    holders[day] = on = [];
                }

                on.Add(ticker);
            }
        }

        var sessions = new List<DateOnly>();
        var strays = new List<Stray>();

        foreach (var (day, on) in holders)
        {
            var spanning = spans.Count(span => span.First <= day && day <= span.Last);

            if (on.Count * 2 >= spanning)
            {
                sessions.Add(day);
            }
            else
            {
                strays.Add(new Stray(day, [.. on.Order(StringComparer.Ordinal)], spanning));
            }
        }

        IReadOnlyList<Gap> holes =
        [
            .. series
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .SelectMany(entry => TradingCalendar.MissingFrom(entry.Value, sessions).Select(session => new Gap(entry.Key, session))),
        ];

        return (holes, strays);
    }

    // What a pull did, as the operator reads it at the prompt.
    public static string Detail(HistoryPullOutcome outcome) =>
        FormattableString.Invariant($"pulled {outcome.From:yyyy-MM-dd} to {outcome.Through:yyyy-MM-dd}: {outcome.Stored} of {outcome.Names} name(s) answered, {outcome.BarsWritten} bar(s) and {outcome.EarningsWritten} earnings print(s) stored, {outcome.Holes.Select(gap => gap.Ticker).Distinct(StringComparer.Ordinal).Count()} name(s) with a missing session, {outcome.Strays.Count} day(s) held by fewer than half the names spanning them, {outcome.Months} calendar month(s) asked, {outcome.Requests} request(s)")
        + string.Concat(outcome.Unanswered.Select(line => Environment.NewLine + "  unanswered: " + line))
        + string.Concat(outcome.MonthsUnanswered.Select(line => Environment.NewLine + "  month unanswered: " + line));

    public static string Detail(HistorySurpriseOutcome outcome) =>
        FormattableString.Invariant($"pulled surprises {outcome.From:yyyy-MM-dd} to {outcome.Through:yyyy-MM-dd}: {outcome.Prints} print(s) of {outcome.Names} name(s) answered over {outcome.Months} calendar month(s), {outcome.WithASurprise} carrying a surprise, {outcome.Written} stored, {outcome.Requests} request(s)")
        + string.Concat(outcome.MonthsUnanswered.Select(line => Environment.NewLine + "  month unanswered: " + line));

    public static string Detail(HistoryMarketOutcome outcome) =>
        FormattableString.Invariant($"pulled the market series {outcome.From:yyyy-MM-dd} to {outcome.Through:yyyy-MM-dd}: {outcome.Stored.Count} of {MarketSeries.Count} series answered, {outcome.Written} session(s) stored, {outcome.Requests} request(s)")
        + string.Concat(outcome.Stored.Select(series => Environment.NewLine + "  " + Described(series)))
        + string.Concat(outcome.Refused.Select(line => Environment.NewLine + "  refused: " + line));

    // One stored series as the pull's row and its printed lines say it: how many sessions, the first and the last.
    static string Described(MarketSeriesStored series) =>
        FormattableString.Invariant($"{series.Series}: {series.Sessions} session(s), {series.First:yyyy-MM-dd} to {series.Last:yyyy-MM-dd}");

    public static string Detail(HistoryCompanyOutcome outcome) =>
        FormattableString.Invariant($"pulled the companies {outcome.From:yyyy-MM-dd} to {outcome.Through:yyyy-MM-dd}: {outcome.Answered} of {outcome.Names} name(s) answered, {outcome.CompaniesWritten} compan(ies) and {outcome.CountsWritten} share count(s) stored, {outcome.NoSector.Count} filing no sector, {outcome.NoCount.Count} filing no count with its date, {outcome.NoCik.Count} filing no CIK, {outcome.SheetsUncounted} balance sheet(s) carrying no count or no filing date, {outcome.IncomeWritten} quarter(s) of income stored, {outcome.StatementsUndated} income statement(s) carrying no filing date, {outcome.NoIncome.Count} filing no income statement with its date, the moves of 2023-03-17: {outcome.MovesFiled} of {GicsSectors.Moves.Count} filed in the sector they moved to, {outcome.Requests} request(s)")
        + string.Concat(outcome.Unanswered.Select(line => Environment.NewLine + "  unanswered: " + line))
        + Listed("no sector", outcome.NoSector)
        + Listed("no count", outcome.NoCount)
        + Listed("no CIK", outcome.NoCik)
        + Listed("no income", outcome.NoIncome)
        + string.Concat(outcome.MovesNot.Select(line => Environment.NewLine + "  move not filed: " + line));

    public static string Detail(HistorySplitOutcome outcome) =>
        FormattableString.Invariant($"pulled the splits {outcome.From:yyyy-MM-dd} to {outcome.Through:yyyy-MM-dd}: {outcome.Answered} of {outcome.Names} name(s) answered, {outcome.WithASplit} with a split, {outcome.Written} split(s) stored, {outcome.Plain} of the splits answered plain and the rest a spin-off's or a merger's adjustment, {outcome.Requests} request(s)")
        + string.Concat(outcome.Unanswered.Select(line => Environment.NewLine + "  unanswered: " + line));

    public static string Detail(HistorySectorFundOutcome outcome) => Detail(outcome, "sector funds", SectorFunds.Count);

    public static string Detail(HistorySectorFundOutcome outcome, string funds, int asked) =>
        FormattableString.Invariant($"pulled the {funds} {outcome.From:yyyy-MM-dd} to {outcome.Through:yyyy-MM-dd}: {outcome.Stored.Count} of {asked} fund(s) answered, {outcome.Written} session(s) stored, {outcome.Requests} request(s)")
        + string.Concat(outcome.Stored.Select(series => Environment.NewLine + "  " + Described(series)))
        + string.Concat(outcome.Refused.Select(line => Environment.NewLine + "  refused: " + line));

    public static string Detail(HistoryRevenueOutcome outcome) =>
        FormattableString.Invariant($"pulled the revenue of {outcome.Filers} filer(s): {outcome.Written} figure(s) stored, {outcome.NoRevenue.Count} filer(s) filing under none of the {FirstFiledRevenue.Concepts.Count} concepts, {outcome.WithoutAFiler.Count} pulled compan(ies) filing no CIK, {outcome.Requests} request(s)")
        + string.Concat(outcome.ByConcept.Select(concept => Environment.NewLine + "  " + Described(concept)))
        + string.Concat(outcome.Unanswered.Select(line => Environment.NewLine + "  unanswered: " + line))
        + Listed("filing under no concept", outcome.NoRevenue)
        + Listed("no CIK", outcome.WithoutAFiler);

    // One concept as the revenue pull's row and its printed lines say it: the filers stating any figure under it and
    // the figures they stated.
    static string Described(RevenueConceptFiled concept) =>
        FormattableString.Invariant($"{concept.Concept}: {concept.Filers} filer(s), {concept.Figures} figure(s)");

    static string Listed(string what, IReadOnlyList<string> names) =>
        names.Count == 0 ? string.Empty : Environment.NewLine + "  " + what + ": " + string.Join(", ", names);

    static async Task<IReadOnlyList<string>> ColumnAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    // The names a pull asks for: every name the index held over the span, or, for a wider index, its members today as a
    // members pull stored them, which holds no span to read the dates against; and where the pull is narrowed to codes,
    // those of them alone, a code the index does not hold asked for by none.
    static async Task<IReadOnlyList<string>> NamesAsync(SqliteConnection connection, string indexCode, DateOnly from, DateOnly through, CancellationToken cancellation, IReadOnlyCollection<string>? only = null)
    {
        var wider = WiderIndices.Contains(indexCode, StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = wider ? MembersHeld : NamesHeld;
        command.Parameters.AddWithValue("$index", indexCode);

        if (!wider)
        {
            command.Parameters.AddWithValue("$from", Text(from));
            command.Parameters.AddWithValue("$through", Text(through));
        }

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            names.Add(reader.GetString(0));
        }

        return only is null ? names : [.. names.Where(name => only.Contains(name, StringComparer.Ordinal))];
    }

    static async Task<int> CountAsync(SqliteConnection connection, string sql, string? pull, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (pull is not null)
        {
            command.Parameters.AddWithValue("$pull", pull);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static async Task AppendAsync(
        SqliteConnection connection,
        string runId,
        string stage,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string outcome,
        int rowsWritten,
        int requests,
        string detail,
        CancellationToken cancellation)
    {
        await using var log = connection.CreateCommand();
        log.CommandText = AppendRun;
        log.Parameters.AddWithValue("$run_id", runId);
        log.Parameters.AddWithValue("$stage", stage);
        log.Parameters.AddWithValue("$started_at", startedAt.ToString("O", CultureInfo.InvariantCulture));
        log.Parameters.AddWithValue("$ended_at", endedAt.ToString("O", CultureInfo.InvariantCulture));
        log.Parameters.AddWithValue("$outcome", outcome);
        log.Parameters.AddWithValue("$rows_written", rowsWritten);
        log.Parameters.AddWithValue("$network_requests", requests);
        log.Parameters.AddWithValue("$detail", detail);

        await log.ExecuteNonQueryAsync(cancellation);
    }

    static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// What one pull did: the span, the names the index held over it and how many answered, each name the
// provider did not answer and why, the sessions the answered names miss against one another, the rows
// stored, the requests made, the calendar months asked and unanswered, and the days too few names hold
// to be read as sessions.
public sealed record HistoryPullOutcome(
    DateOnly From,
    DateOnly Through,
    int Names,
    int Stored,
    IReadOnlyList<string> Unanswered,
    IReadOnlyList<Gap> Holes,
    int BarsWritten,
    int EarningsWritten,
    int Requests,
    int Months,
    IReadOnlyList<string> MonthsUnanswered,
    IReadOnlyList<Stray> Strays);

// A day fewer than half the names whose series span it hold: the names holding it, and how many span it.
public sealed record Stray(DateOnly Day, IReadOnlyList<string> Holders, int Spanning);

// What one surprise pull did: the span, the names held over it, the months asked and unanswered, the prints
// answered for held names and how many carried a surprise, the rows stored and the requests made.
public sealed record HistorySurpriseOutcome(
    DateOnly From,
    DateOnly Through,
    int Names,
    int Months,
    IReadOnlyList<string> MonthsUnanswered,
    int Prints,
    int WithASurprise,
    int Written,
    int Requests);

// What one market pull did: the span, each series stored with its sessions, each series refused and why,
// the rows stored and the requests made.
public sealed record HistoryMarketOutcome(
    DateOnly From,
    DateOnly Through,
    IReadOnlyList<MarketSeriesStored> Stored,
    IReadOnlyList<string> Refused,
    int Written,
    int Requests);

// One series a market pull stored: the sessions the provider sent, the first and the last.
public sealed record MarketSeriesStored(string Series, int Sessions, DateOnly First, DateOnly Last);

// What one companies pull did: the span, the names held over it and how many answered, each name not served and
// why, the companies and counts stored, the names filing no sector, no dated count or no CIK, the balance sheets
// carrying no count or no filing date, how many of the fourteen moves of 2023-03-17 are filed where they moved and
// each that is not, and the requests made.
public sealed record HistoryCompanyOutcome(
    DateOnly From,
    DateOnly Through,
    int Names,
    int Answered,
    IReadOnlyList<string> Unanswered,
    int CompaniesWritten,
    int CountsWritten,
    IReadOnlyList<string> NoSector,
    IReadOnlyList<string> NoCount,
    IReadOnlyList<string> NoCik,
    int SheetsUncounted,
    int MovesFiled,
    IReadOnlyList<string> MovesNot,
    int Requests,
    int IncomeWritten = 0,
    int StatementsUndated = 0,
    IReadOnlyList<string>? NoIncomeFiled = null)
{
    // The names whose answer filed no income statement with its date, which the profit gate reads as no quarter filed.
    public IReadOnlyList<string> NoIncome => NoIncomeFiled ?? [];
}

// What one splits pull did: the span, the names held over it and how many answered, each name not served and why,
// the names with a split in the span, the splits stored, the requests made, and how many of the splits answered are
// plain ones rather than a spin-off's or a merger's adjustment the provider files as a split.
public sealed record HistorySplitOutcome(
    DateOnly From,
    DateOnly Through,
    int Names,
    int Answered,
    IReadOnlyList<string> Unanswered,
    int WithASplit,
    int Written,
    int Requests,
    int Plain);

// What one sector funds pull did: the span, each fund stored with its sessions, each fund refused and why, the rows
// stored and the requests made.
public sealed record HistorySectorFundOutcome(
    DateOnly From,
    DateOnly Through,
    IReadOnlyList<MarketSeriesStored> Stored,
    IReadOnlyList<string> Refused,
    int Written,
    int Requests);

// What one revenue pull did: the filers asked, the pulled companies filing no CIK, each concept's filers and figures,
// the filers filing under none of the concepts, each request not served and why, the figures stored and the requests
// made.
public sealed record HistoryRevenueOutcome(
    int Filers,
    IReadOnlyList<string> WithoutAFiler,
    IReadOnlyList<RevenueConceptFiled> ByConcept,
    IReadOnlyList<string> NoRevenue,
    IReadOnlyList<string> Unanswered,
    int Written,
    int Requests);

// One revenue concept as a revenue pull found it: how many filers stated a figure under it and how many figures.
public sealed record RevenueConceptFiled(string Concept, int Filers, int Figures);

// What one purge removed.
public sealed record HistoryPurgeOutcome(
    string Pull,
    int Bars,
    int Earnings,
    int Surprises = 0,
    int MarketBars = 0,
    int Companies = 0,
    int Shares = 0,
    int Splits = 0,
    int Revenue = 0,
    int Members = 0,
    int Income = 0,
    int Snapshots = 0,
    int Holdings = 0);

// What one holdings pull did: the wider index's fund, the filings listed and the snapshots read with how many were new
// and their span, the holdings of common stock and the keys they matched by, those standing between codes, each holding
// matched by neither and each filing not read, why nothing was stored where a list could not be read, and the requests.
public sealed record HistoryHoldingOutcome(
    string Index,
    int Filings,
    int Snapshots,
    int Stored,
    DateOnly? First,
    DateOnly? Last,
    int Equity,
    int ByIsin,
    int ByName,
    int Unmatched,
    int Between,
    IReadOnlyList<string> UnmatchedNames,
    IReadOnlyList<string> Unread,
    string? Refused,
    int Requests,
    // The holdings of quarters an earlier pull stored that this one matched to another code or to none, the codes it
    // asked the provider whether they traded at a quarter's end, and the asks the provider did not answer.
    int Again = 0,
    int Asked = 0,
    IReadOnlyList<string>? Unanswered = null,
    // The holdings matched by the wider reading of their names, and those left matched to none because every code their
    // names read traded at the quarter's end at a close away from the price the fund valued them at.
    int Widely = 0,
    IReadOnlyList<string>? PricedOut = null,
    // The holdings matched to a code their fund held by ISIN within a year, and each code matched to none at a holding's
    // quarters because its closes did not move in step with the fund's values a share, with the holding and its count.
    int Held = 0,
    IReadOnlyList<string>? NotInStep = null,
    // The holdings of a schedule filed with no identifier that carry the code the next coded quarter's holding of the
    // identical name matched, the closes in step across the joined quarters.
    int Carried = 0,
    // The holdings matched to a ticker OpenFIGI mapped their identifier to, the identifiers it was asked for and the
    // requests that took.
    int ByMapping = 0,
    int Mapped = 0,
    int MappingRequests = 0);

// What one members pull did: the wider index asked, the members its answer listed today, those new to the store and
// those it holds, why nothing was stored where the answer was refused or could not be read, and the requests made.
public sealed record HistoryMemberOutcome(
    string Index,
    int Listed,
    int Written,
    int Held,
    string? Refused,
    int Requests);
