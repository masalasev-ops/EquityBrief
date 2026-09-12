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
    int Requests);

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
        Feeds: [Feed.CompanyFinancials]);

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

    readonly IFundamentalsFeed feed;
    readonly IClock clock;
    readonly string databaseFile;

    public FundamentalsFetcher(IFundamentalsFeed feed, IClock clock, string databaseFile)
    {
        this.feed = feed;
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
                await CountAsync(connection, ticker, cancellation), 0, [], 0);

            await RecordAsync(connection, runId, startedAt, nothing, cancellation);

            return nothing;
        }

        var fetched = await feed.FundamentalsAsync(ticker, cancellation).ConfigureAwait(false);
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
                command.Parameters.AddWithValue("$payload", Payload(fetched, quarter, quarter.FilingDate == newest));
                command.Parameters.AddWithValue("$source", Source(fetched));

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
            fetched.PartsNotCarried,
            feed.Requests);

        await RecordAsync(connection, runId, startedAt, outcome, cancellation);

        return outcome;
    }

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
    internal static string Payload(CompanyFundamentals fetched, FiledQuarter quarter, bool newest) =>
        JsonSerializer.Serialize(new
        {
            periodEnd = Stored(quarter.PeriodEnd),
            currency = fetched.Currency,
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
            // Named as absent rather than left out, because a key that is not
            // there reads as this name having none and what is true is that the
            // provider files none for anybody.
            segments = (string?)null,
            guidance = (string?)null,
        });

    // Which provider each part came from, which is what SCHEMA's `source` column
    // is for. The two parts this feed does not file say so by name, so a row can
    // be read later without knowing which endpoint filled it.
    internal static string Source(CompanyFundamentals fetched)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in Parts)
        {
            sources[part] = part == Margin ? Computed
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
        "quarter", "margin", "balanceSheet", "earnings", "epsBases", "valuation",
        "estimated", "segments", "guidance",
    ];

    public const string Margin = "margin";

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

        if (outcome.PartsNotCarried.Count > 0)
        {
            detail += ", not filed by this provider: " + string.Join(", ", outcome.PartsNotCarried);
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
