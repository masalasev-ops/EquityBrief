using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One name's fundamentals, answered from a recorded response.
//
// The parser was written against four captured payloads and not the other way
// round, which is the rule 1.6 and 1.7 set. Six things about this payload would
// have been written wrong from the endpoint's name and SCHEMA's own column note,
// and the note is where two of them would have come from.
//
// The endpoint files no segment table and no management guidance, under any key,
// for any of the four names. SCHEMA's payload note names both, so a parser
// written from it would have looked for a value and found the key missing, which
// reads as this name having none rather than as the provider filing none. They
// arrive from the filings archive instead.
//
// Every statement row carries its own `filing_date`, and it is not the period
// end: the quarter ending 2026-06-30 was filed on 2026-07-31. This table is
// keyed on the filing date, so a reader taking `date` for it would key every row
// weeks early and nothing downstream would question it.
//
// One name files a quarter with no `filing_date` at all. A grain of one row per
// filing date cannot hold such a quarter, so it is counted and skipped rather
// than stored under a date nobody filed.
//
// Money arrives as strings in the three statements, as numbers in `Highlights`,
// and as strings again in the estimate trend while the earnings history sends
// numbers. Four conventions in one payload, so the reader takes either kind.
//
// The row for the next print carries an estimate, a null actual and an
// `epsDifference` of zero. A reader keying the surprise on that field reports
// that a print which has not happened came in exactly as expected.
//
// And the word segment appears in two of the four payloads, in the company
// description, which is prose. So what says whether a part is carried is a walk
// over the payload's key names and never a scan of its text.
public sealed class RecordedFundamentalsFeed(IReadOnlyDictionary<string, string> responses) : IFundamentalsFeed
{
    public const string Prefix = "fundamentals-";

    // The two parts section 4's numbers row asks for that this endpoint may not
    // file. Looked for rather than declared absent, so the day the provider
    // starts sending a segment table the absence stops being reported without
    // anyone editing a list.
    static readonly (string Part, string Key)[] MayBeAbsent =
    [
        ("segments", "segment"),
        ("guidance", "guidance"),
        ("ratings", "AnalystRatings"),
        ("dividend", "SplitsDividends"),
    ];

    public int Requests { get; private set; }

    // The names this feed holds a capture for, in order. Read off what was
    // committed rather than listed anywhere, so a fifth capture is served without
    // a second place naming it.
    public IReadOnlyList<string> Names =>
        [.. responses.Keys.OrderBy(ticker => ticker, StringComparer.Ordinal)];

    public static RecordedFundamentalsFeed FromFolder(string folder) =>
        new(Directory
            .GetFiles(folder, Prefix + "*.json")
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path)[Prefix.Length..],
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase));

    public Task<CompanyFundamentals> FundamentalsAsync(string ticker, CancellationToken cancellation = default)
    {
        // Counted per name, because this feed is per name by design and the run
        // log records the figure the on-demand path costs.
        Requests++;

        if (!responses.TryGetValue(ticker, out var captured))
        {
            throw new InvalidOperationException(
                $"No captured fundamentals for {ticker}. A recorded feed answering an unknown name " +
                "with an empty payload would look exactly like a name the provider files nothing " +
                "for, and the fetcher would record it as fetched.");
        }

        return Task.FromResult(Parse(captured, ticker));
    }

    public static CompanyFundamentals Parse(string response, string ticker)
    {
        using var document = JsonDocument.Parse(response);

        var root = document.RootElement;

        if (!root.TryGetProperty("General", out var general))
        {
            throw new FormatException(
                $"The fundamentals response for {ticker} carries no `General` object. A payload " +
                "without it is a different answer rather than a name the provider knows nothing " +
                "about, and the parts below are read relative to keys this one proves are there.");
        }

        var income = Statement(root, "Income_Statement");
        var sheet = Statement(root, "Balance_Sheet");
        var history = Earnings(root, "History");

        var filed = new List<FiledQuarter>();
        var withNoFilingDate = 0;

        foreach (var quarter in Quarterly(income))
        {
            var periodEnd = Date(Text(quarter.Value, "date"));
            var filingDate = Date(Text(quarter.Value, "filing_date"));

            if (periodEnd is not { } covers)
            {
                continue;
            }

            // The grain is one row per filing date. A quarter the provider files
            // no date for cannot be keyed, and defaulting it to the period end
            // would state that the figures were known weeks before they were.
            if (filingDate is not { } filedOn)
            {
                withNoFilingDate++;

                continue;
            }

            var balance = Quarterly(sheet).FirstOrDefault(row => Text(row.Value, "date") == quarter.Name);
            var print = history.FirstOrDefault(row => row.Name == quarter.Name);

            filed.Add(new FiledQuarter(
                covers,
                filedOn,
                new QuarterFigures(
                    Money(quarter.Value, "totalRevenue"),
                    Money(quarter.Value, "grossProfit"),
                    Money(quarter.Value, "netIncome")),
                new BalanceSheet(
                    Money(balance.Value, "totalAssets"),
                    Money(balance.Value, "totalLiab"),
                    Money(balance.Value, "totalStockholderEquity"),
                    Money(balance.Value, "cash"),
                    Money(balance.Value, "netDebt")),
                Reported(print.Value)));
        }

        return new CompanyFundamentals(
            Text(general, "Code") ?? ticker,
            Text(general, "CurrencyCode") ?? string.Empty,
            Text(general, "CIK") ?? string.Empty,
            [.. filed.OrderByDescending(quarter => quarter.PeriodEnd)],
            Estimated(history),
            Bases(root),
            Valuation(root),
            Market(root),
            Ratings(root),
            [.. MayBeAbsent.Where(part => !CarriesAKeyFor(root, part.Key)).Select(part => part.Part)],
            withNoFilingDate,
            Dividend(root));
    }

    // The next print, which is the row carrying an estimate and no actual. The
    // earliest such period end rather than any of them, because a payload holding
    // two would be holding this quarter's and the one after it, and the section
    // states one.
    //
    // A name whose next print the provider has filed no estimate for has none,
    // which one of the four captured names is: every row it files carries an
    // actual. That absence is what the section marks rather than drawing a row
    // with nothing in it.
    static EstimatedQuarter? Estimated(IReadOnlyList<JsonProperty> history)
    {
        var next = history
            .Where(row => Money(row.Value, "epsActual") is null)
            .Select(row => (Row: row, PeriodEnd: Date(Text(row.Value, "date"))))
            .Where(pair => pair.PeriodEnd is not null)
            .OrderBy(pair => pair.PeriodEnd)
            .Select(pair => (pair.Row, PeriodEnd: pair.PeriodEnd!.Value))
            .FirstOrDefault();

        return next.Row.Value.ValueKind == JsonValueKind.Object
            ? new EstimatedQuarter(
                next.PeriodEnd,
                Date(Text(next.Row.Value, "reportDate")),
                ProviderTiming.Filed(Text(next.Row.Value, "beforeAfterMarket")),
                Money(next.Row.Value, "epsEstimate"))
            : null;
    }

    // What the provider filed about a print that happened. A row with no actual
    // is not one of these, which is what keeps the estimate quarter out of the
    // reported set and the zero difference out of the surprise.
    static ReportedEarnings? Reported(JsonElement print)
    {
        if (print.ValueKind != JsonValueKind.Object || Money(print, "epsActual") is null)
        {
            return null;
        }

        return new ReportedEarnings(
            Date(Text(print, "reportDate")),
            ProviderTiming.Filed(Text(print, "beforeAfterMarket")),
            Money(print, "epsActual"),
            Money(print, "epsEstimate"));
    }

    // The three earnings bases, from the aggregates the payload states as of the
    // fetch. Money per share, so decimal, and the ratio is left to the caller
    // because a ratio needs a close and this payload holds none.
    static EpsBases Bases(JsonElement root)
    {
        if (!root.TryGetProperty("Highlights", out var highlights))
        {
            return new EpsBases(null, null, null);
        }

        return new EpsBases(
            Money(highlights, "DilutedEpsTTM") ?? Money(highlights, "EarningsShare"),
            Money(highlights, "EPSEstimateCurrentYear"),
            Money(highlights, "EPSEstimateNextYear"));
    }

    // The valuation on each earnings basis, copied from the payload's own
    // aggregates. Two rather than the seven the object carries, because the two
    // are the ones section 4's numbers row asks for and a figure nothing reads is
    // one that can be wrong without anyone noticing.
    static ValuationRatios Valuation(JsonElement root)
    {
        if (!root.TryGetProperty("Valuation", out var valuation))
        {
            return new ValuationRatios(null, null);
        }

        return new ValuationRatios(
            Money(valuation, "TrailingPE"),
            Money(valuation, "ForwardPE"));
    }

    // The market's own figure for the whole company, from the aggregates the
    // payload states as of the fetch.
    static MarketValue Market(JsonElement root) =>
        root.TryGetProperty("Highlights", out var highlights)
            ? new MarketValue(Money(highlights, "MarketCapitalization"))
            : new MarketValue(null);

    // What the analysts say of the company, from the one object the payload files it in.
    static AnalystRatings Ratings(JsonElement root) =>
        root.TryGetProperty("AnalystRatings", out var ratings)
            ? new AnalystRatings(
                Money(ratings, "Rating"),
                Money(ratings, "TargetPrice"),
                Count(ratings, "StrongBuy"),
                Count(ratings, "Buy"),
                Count(ratings, "Hold"),
                Count(ratings, "Sell"),
                Count(ratings, "StrongSell"))
            : new AnalystRatings(null, null, null, null, null, null, null);

    // The dividend, from the one object the payload files it in, each value as the provider sends
    // it and none where the object is not filed at all.
    // see: The numbers section shows the dividend the provider files, on the newest filing alone
    static DividendFiled? Dividend(JsonElement root) =>
        root.TryGetProperty("SplitsDividends", out var part) && part.ValueKind == JsonValueKind.Object
            ? new DividendFiled(
                Money(part, "ForwardAnnualDividendRate"),
                Money(part, "ForwardAnnualDividendYield"),
                Money(part, "PayoutRatio"),
                Date(Text(part, "ExDividendDate")),
                Date(Text(part, "DividendDate")))
            : null;

    // A count of analysts, which the payload sends as a whole number.
    static int? Count(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count)
            ? count
            : null;

    // Whether the payload carries a key named for a part, at any depth. A walk
    // over key names rather than a scan of the text, because the word segment
    // appears in two of the four captured payloads inside the company
    // description, and a text scan would read that prose as a segment table.
    static bool CarriesAKeyFor(JsonElement element, string word)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                        || CarriesAKeyFor(property.Value, word))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array:
                foreach (var entry in element.EnumerateArray())
                {
                    if (CarriesAKeyFor(entry, word))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    static JsonElement Statement(JsonElement root, string name) =>
        root.TryGetProperty("Financials", out var financials) && financials.TryGetProperty(name, out var statement)
            ? statement
            : default;

    // The quarterly rows of a statement, in the order the provider sent them.
    // Ordering is applied where the result is used rather than assumed here,
    // because the payload happens to send them newest first and a parser resting
    // on that is resting on a convention nothing states.
    static IReadOnlyList<JsonProperty> Quarterly(JsonElement statement) =>
        statement.ValueKind == JsonValueKind.Object && statement.TryGetProperty("quarterly", out var rows)
            ? [.. rows.EnumerateObject()]
            : [];

    static IReadOnlyList<JsonProperty> Earnings(JsonElement root, string name) =>
        root.TryGetProperty("Earnings", out var earnings) && earnings.TryGetProperty(name, out var rows)
            ? [.. rows.EnumerateObject()]
            : [];

    static string? Text(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Money, and therefore decimal, from either of the two forms this payload
    // sends it in. A statement sends "36267000000.00" and Highlights sends
    // 4849207869440, and a reader keyed on one kind reports the other as absent,
    // which on a balance sheet is a zero a reader would act on.
    static decimal? Money(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : null,
            JsonValueKind.String => decimal.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                    ? parsed
                    : null,
            _ => null,
        };
    }

    static DateOnly? Date(string? stored) =>
        stored is not null
        && DateOnly.TryParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
