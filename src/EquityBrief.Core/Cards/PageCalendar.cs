namespace EquityBrief.Core.Cards;

// One dated event of a name's next ninety days: its date, its kind, what it is in words, whether it is declared or, for an
// ex-dividend date the calendar does not declare, estimated, and an ex-dividend date's payment a share where one is read.
public sealed record CalendarItem(DateOnly Date, string Kind, string Words, bool Declared = true, decimal? Amount = null)
{
    public const string Results = "results";
    public const string ExDividend = "ex-dividend";
}

// The ninety days after a night as the name page's calendar draws them: the company's next results as the earnings
// calendar dates them, its next ex-dividend date, declared where the calendar carries one and otherwise estimated as a
// pick's card estimates it, and each FOMC decision and CPI release the committed table dates, with each kind of the table
// that ends inside the days. Worked by one function from stored rows, which the page alone draws.
// see: The calendar draws a name's next ninety days of results, ex-dividend dates and the Fed's and the CPI's dates beside the dated items the research read
// see: A pick's next ex-dividend date is the calendar's where it declares one, and otherwise estimated from the dividend the quarters fetch kept or else from the newest fundamentals fetch and the steps its dividends leave in the bars
public static class PageCalendar
{
    // The days the calendar reads after the night.
    public const int Days = 90;

    public static (IReadOnlyList<CalendarItem> Items, IReadOnlyList<string> PastTheTable) Of(
        DateOnly night,
        IReadOnlyList<(DateOnly Date, string Kind, string? Timing)> dated,
        DividendKept? kept,
        IReadOnlyList<MarketEvent>? table = null)
    {
        var from = night.AddDays(1);
        var through = night.AddDays(Days);
        var items = new List<CalendarItem>();

        if (dated.Where(one => one.Kind == "earnings" && one.Date >= from && one.Date <= through).OrderBy(one => one.Date).FirstOrDefault() is { Date: var reports } next && reports != default)
        {
            items.Add(new CalendarItem(reports, CalendarItem.Results, next.Timing switch
            {
                "before" => "Results, before the open",
                "after" => "Results, after the close",
                _ => "Results, its timing not filed",
            }));
        }

        DateOnly[] declared = [.. dated.Where(one => one.Kind == "ex-dividend").Select(one => one.Date)];

        if (CardHitsReading.Dividend(night, through, declared, kept, null, null) is { } dividend)
        {
            items.Add(new CalendarItem(
                dividend.Date,
                CalendarItem.ExDividend,
                dividend.Declared ? "Ex-dividend, declared" : "Ex-dividend, estimated from its usual interval",
                dividend.Declared,
                dividend.Amount));
        }

        var (events, past) = MarketEvents.Within(from, through, table);

        items.AddRange(events.Select(one => new CalendarItem(one.Date, one.Kind, one.Name)));

        return ([.. items.OrderBy(item => item.Date).ThenBy(item => item.Kind, StringComparer.Ordinal)], past);
    }
}
