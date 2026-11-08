using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// What the first-year measure read: each filing-based family's first test year on each index with the years it was read
// from, the drift's with its coverage, the since-gone codes with no surprise by why they left, and the heavyweights'
// agreement by index and year.
public sealed record LongFirstYearsRead(
    IReadOnlyList<(FamilyFirstYear Year, IReadOnlyList<YearShare> Shares)> Families,
    IReadOnlyList<DriftFirstYear> Drifts,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> WhyLeft,
    IReadOnlyList<(string Index, int Year, int Groups, int Agreed, int Compared)> Agreement);

// The first-year measure over the long history, read-only and writing no store row. For each index and year: the members
// on the year's first session, from the long spans, whose filer's facts filed before it, in the long table or the standing
// one, give the fundamentals scorecard a word, which the Fundamentals and the quality families read, and give a share count
// standing within a year, which the heavyweights read; the drift's since-gone members of the year and how many of them a
// surprise covers that year, an older code matched to the calendar's by its suffix taken off, those no surprise covers split
// by why they left; and the heavyweights' agreement, at each fund quarter end, between each industry heading's three largest
// holdings by the fund's value and by the close times the filed share count.
// see: Each filing-based family's first test year is three learning years after the first year 90 in every 100 members read its score
// see: A rule reading a surprise is judged from the first year surprises cover 80% of an index's since-gone members, the years before printed beside it
public sealed class LongFirstYearReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSpan, Touch.Read),
            new StoreTouch(Store.LongFact, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.LongFiling, Touch.Read),
            new StoreTouch(Store.LongPrint, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.LongBar, Touch.Read),
            new StoreTouch(Store.LongMarketBar, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.LongHolding, Touch.Read),
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
        ],
        Feeds: []);

    // How many of each industry heading's holdings the agreement compares.
    public const int Largest = 3;

    // How far a share count may stand before the session and still be read.
    public const int CountWithinDays = 400;

    // How far past the day a code left its index its prices must run for it to be read as dropped rather than ended.
    public const int PricedAfterDays = 30;

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<LongFirstYearsRead> ReadAsync(string databaseFile, Action<string> progress, CancellationToken cancellation = default)
    {
        var builder = StoreConnection.Builder(databaseFile);
        builder.Mode = SqliteOpenMode.ReadOnly;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);

        async Task EachAsync(string sql, Action<SqliteDataReader> row)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                row(reader);
            }
        }

        var sessions = new List<DateOnly>();
        await EachAsync("SELECT session_date FROM long_market_bar WHERE series = 'GSPC' ORDER BY session_date;", reader => sessions.Add(Day(reader.GetString(0))));
        await EachAsync("SELECT DISTINCT session_date FROM bar WHERE session_date > (SELECT MAX(session_date) FROM long_market_bar WHERE series = 'GSPC') ORDER BY session_date;", reader => sessions.Add(Day(reader.GetString(0))));

        var newest = sessions.Count > 0 ? sessions[^1] : DateOnly.MinValue;
        var firstOf = sessions.GroupBy(session => session.Year).ToDictionary(year => year.Key, year => year.Min());

        var spans = new List<(string Index, string Ticker, DateOnly Joined, DateOnly? Left)>();
        await EachAsync("SELECT index_code, ticker, joined, \"left\" FROM long_span;", reader =>
            spans.Add((reader.GetString(0), reader.GetString(1), Day(reader.GetString(2)), reader.IsDBNull(3) ? null : Day(reader.GetString(3)))));

        var filerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        await EachAsync("SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;", reader => filerOf[reader.GetString(0)] = reader.GetString(1).TrimStart('0'));
        await EachAsync("SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;", reader => filerOf[reader.GetString(0)] = reader.GetString(1).TrimStart('0'));
        await EachAsync("SELECT DISTINCT ticker, cik FROM long_filing;", reader => filerOf[reader.GetString(0)] = reader.GetString(1).TrimStart('0'));

        var financial = new HashSet<string>(StringComparer.Ordinal);
        await EachAsync("SELECT DISTINCT ticker FROM membership WHERE sector = 'Financials';", reader => financial.Add(reader.GetString(0)));

        progress("reading the facts");

        var facts = new Dictionary<string, List<FiledFactRow>>(StringComparer.Ordinal);

        foreach (var table in new[] { "long_fact", "filed_fact" })
        {
            await EachAsync($"SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM {table};", reader =>
            {
                var cik = reader.GetString(0).TrimStart('0');

                if (!facts.TryGetValue(cik, out var rows))
                {
                    facts[cik] = rows = [];
                }

                rows.Add(new FiledFactRow(reader.GetString(1), Day(reader.GetString(2)), Day(reader.GetString(3)), Money.FromStorage(reader.GetString(4)), Day(reader.GetString(5)), reader.GetString(6), reader.GetString(7)));
            });
        }

        progress("reading the scores by year");

        var families = new List<(FamilyFirstYear, IReadOnlyList<YearShare>)>();
        var indices = spans.Select(span => span.Index).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        foreach (var index in indices)
        {
            var scored = new List<YearShare>();
            var counted = new List<YearShare>();

            foreach (var (year, session) in firstOf.OrderBy(year => year.Key))
            {
                var members = spans.Where(span => span.Index == index && span.Joined <= session && (span.Left is null || span.Left > session)).Select(span => span.Ticker).Distinct(StringComparer.Ordinal).ToArray();
                var (scores, counts) = (0, 0);

                foreach (var ticker in members)
                {
                    if (!filerOf.TryGetValue(ticker, out var cik) || !facts.TryGetValue(cik, out var rows))
                    {
                        continue;
                    }

                    if (FundamentalScorecard.Read(rows, session, financial.Contains(ticker), PriceHistory.None).Word is not null)
                    {
                        scores++;
                    }

                    if (rows.Any(row => FiledFacts.SharesOutstanding.Contains(row.Concept, StringComparer.Ordinal) && row.Filed < session && row.End >= session.AddDays(-CountWithinDays)))
                    {
                        counts++;
                    }
                }

                scored.Add(new YearShare(year, members.Length, scores));
                counted.Add(new YearShare(year, members.Length, counts));
            }

            families.Add((LongFirstYears.FamilyYear(LongFirstYears.Fundamentals, index, scored), scored));
            families.Add((LongFirstYears.FamilyYear(LongFirstYears.Quality, index, scored), scored));
            families.Add((LongFirstYears.FamilyYear(LongFirstYears.Heavyweights, index, counted), counted));
        }

        progress("reading the surprises");

        var surprised = new HashSet<(string Ticker, int Year)>();
        await EachAsync("SELECT ticker, event_date FROM long_print WHERE read_from = 'provider' AND surprise_percent IS NOT NULL;", reader => surprised.Add((reader.GetString(0), Day(reader.GetString(1)).Year)));
        await EachAsync("SELECT ticker, event_date FROM pulled_surprise WHERE surprise_percent IS NOT NULL;", reader => surprised.Add((reader.GetString(0), Day(reader.GetString(1)).Year)));

        var lastPriced = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        await EachAsync("SELECT ticker, MAX(session_date) FROM long_bar GROUP BY ticker;", reader => lastPriced[reader.GetString(0)] = Day(reader.GetString(1)));

        var failed = new HashSet<string>(StringComparer.Ordinal);
        var ended = new HashSet<string>(StringComparer.Ordinal);
        await EachAsync("SELECT ticker, form, items FROM long_filing WHERE form IN ('8-K', '25', '25-NSE');", reader =>
        {
            var items = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

            if (reader.GetString(1) == "8-K" && items.Contains("1.03", StringComparison.Ordinal))
            {
                failed.Add(reader.GetString(0));
            }
            else if (reader.GetString(1) != "8-K" || items.Contains("2.01", StringComparison.Ordinal))
            {
                ended.Add(reader.GetString(0));
            }
        });

        var drifts = new List<DriftFirstYear>();
        var whyLeft = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);

        foreach (var index in indices)
        {
            var last = spans.Where(span => span.Index == index).GroupBy(span => span.Ticker, StringComparer.Ordinal)
                .ToDictionary(code => code.Key, code => code.Any(span => span.Left is null) ? (DateOnly?)null : code.Max(span => span.Left), StringComparer.Ordinal);
            var gone = last.Where(code => code.Value is { } left && left <= newest).Select(code => code.Key).ToHashSet(StringComparer.Ordinal);
            var coverage = new List<YearShare>();
            var uncovered = new HashSet<string>(StringComparer.Ordinal);

            foreach (var year in firstOf.Keys.Order())
            {
                var (from, through) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
                var held = spans.Where(span => span.Index == index && gone.Contains(span.Ticker) && span.Joined <= through && (span.Left is null || span.Left >= from)).Select(span => span.Ticker).Distinct(StringComparer.Ordinal).ToArray();
                var covered = held.Count(ticker => surprised.Contains((ticker, year)) || surprised.Contains((LongFirstYears.CalendarCode(ticker), year)));

                uncovered.UnionWith(held.Where(ticker => !surprised.Contains((ticker, year)) && !surprised.Contains((LongFirstYears.CalendarCode(ticker), year))));
                coverage.Add(new YearShare(year, held.Length, covered));
            }

            drifts.Add(LongFirstYears.DriftYear(index, coverage));
            whyLeft[index] = uncovered
                .GroupBy(ticker => LongFirstYears.WhyLeft(failed.Contains(ticker), ended.Contains(ticker), lastPriced.TryGetValue(ticker, out var priced) && last[ticker] is { } left && priced > left.AddDays(PricedAfterDays)), StringComparer.Ordinal)
                .ToDictionary(why => why.Key, why => why.Count(), StringComparer.Ordinal);
        }

        progress("reading the heavyweights' agreement");

        var holdings = new List<(string Index, DateOnly Period, string? Industry, string Ticker, decimal Value)>();
        await EachAsync("SELECT index_code, period, industry, ticker, value_usd FROM long_holding WHERE ticker IS NOT NULL AND value_usd IS NOT NULL;", reader =>
            holdings.Add((reader.GetString(0), Day(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), Money.FromStorage(reader.GetString(4)))));

        await using var close = connection.CreateCommand();
        close.CommandText = "SELECT close FROM long_bar WHERE ticker = $ticker AND session_date <= $day ORDER BY session_date DESC LIMIT 1;";
        var closeTicker = close.Parameters.Add("$ticker", SqliteType.Text);
        var closeDay = close.Parameters.Add("$day", SqliteType.Text);

        decimal? Shares(string ticker, DateOnly day) =>
            filerOf.TryGetValue(ticker, out var cik) && facts.TryGetValue(cik, out var rows)
                ? rows.Where(row => FiledFacts.SharesOutstanding.Contains(row.Concept, StringComparer.Ordinal) && row.Filed <= day && row.End >= day.AddDays(-CountWithinDays))
                    .OrderByDescending(row => row.End).Select(row => (decimal?)row.Value).FirstOrDefault()
                : null;

        var agreement = new List<(string, int, int, int, int)>();

        foreach (var group in holdings.GroupBy(holding => (holding.Index, holding.Period.Year)).OrderBy(group => group.Key.Index, StringComparer.Ordinal).ThenBy(group => group.Key.Year))
        {
            var (groups, agreed, compared) = (0, 0, 0);

            foreach (var heading in group.GroupBy(holding => (holding.Period, holding.Industry)))
            {
                var priced = new List<(string Ticker, decimal Value, decimal Cap)>();

                foreach (var holding in heading)
                {
                    closeTicker.Value = holding.Ticker;
                    closeDay.Value = Text(holding.Period);

                    if (await close.ExecuteScalarAsync(cancellation) is string closed && Shares(holding.Ticker, holding.Period) is { } shares)
                    {
                        priced.Add((holding.Ticker, holding.Value, Money.FromStorage(closed) * shares));
                    }
                }

                if (priced.Count < Largest)
                {
                    continue;
                }

                groups++;
                compared += Largest;
                agreed += LongFirstYears.Agreed(priced, Largest);
            }

            agreement.Add((group.Key.Index, group.Key.Year, groups, agreed, compared));
        }

        return new LongFirstYearsRead(families, drifts, whyLeft, agreement);
    }

    // What a person reads of the measure.
    public static IReadOnlyList<string> Lines(LongFirstYearsRead read)
    {
        var lines = new List<string>();

        foreach (var (year, shares) in read.Families)
        {
            lines.Add(FormattableString.Invariant($"{year.Family} on {year.Index}: first scored {year.FirstScored?.ToString(CultureInfo.InvariantCulture) ?? "never"}, first test year {year.FirstTest?.ToString(CultureInfo.InvariantCulture) ?? "none"} against the estimate {year.Estimate}") + (year.Apart ? ", more than a year apart, used as measured" : string.Empty)
                + "; by year " + string.Join(", ", shares.Where(share => share.Members > 0).Select(share => FormattableString.Invariant($"{share.Year} {share.Share:P0}"))));
        }

        foreach (var drift in read.Drifts)
        {
            lines.Add(FormattableString.Invariant($"drift on {drift.Index}: ") + (drift.Reached ? FormattableString.Invariant($"first test year {drift.Year}") : FormattableString.Invariant($"no year's surprises reach {LongFirstYears.SurpriseShare:P0} of its since-gone members, so it is judged from {drift.Year}, every year labelled {LongFirstYears.MostlySurvivors}"))
                + "; coverage by year " + string.Join(", ", drift.Coverage.Where(share => share.Members > 0).Select(share => FormattableString.Invariant($"{share.Year} {share.Read} of {share.Members}"))));
        }

        foreach (var (index, why) in read.WhyLeft)
        {
            lines.Add($"since-gone with no surprise on {index}: " + string.Join(", ", why.OrderByDescending(one => one.Value).Select(one => FormattableString.Invariant($"{one.Key} {one.Value}"))));
        }

        foreach (var (index, year, groups, agreed, compared) in read.Agreement)
        {
            lines.Add(FormattableString.Invariant($"heavyweights' agreement on {index} {year}: {agreed} of {compared} over {groups} heading(s), {(compared == 0 ? 0 : (double)agreed / compared):P1}")
                + (LongFirstYears.GoesToTheOperator(agreed, compared) ? ", which goes to the operator, adopted only on their word" : string.Empty));
        }

        return lines;
    }
}
