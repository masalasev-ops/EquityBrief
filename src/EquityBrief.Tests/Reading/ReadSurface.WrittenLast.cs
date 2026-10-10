using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 18.4: the risks' two columns, the calendar and its timeline, and the sources, each over a full input and
// over the input it degrades on, saying what it cannot draw.
// see: The calendar draws a name's next ninety days of results, ex-dividend dates and the Fed's and the CPI's dates beside the dated items the research read
// see: A figure the filing and the provider's fetch state differently beyond rounding is listed under the sources with the filing first, and the page draws the fetch's
public partial class ReadSurface
{
    // The rows 18.4 adds that this check reaches, each as the parts its row states: the headline's two it writes last and
    // dates, the risks' two columns, the calendar, the sources and the calendar's timeline, and the two failures the
    // calendar and the sources state.
    internal static string[] WrittenLastPageRows =>
    [
        CheckReach.Key("15.9 Name", "Headline, written last from the accepted claims with its claim marks taken off"),
        CheckReach.Key("15.9 Name", "Headline, the day it was written beneath it"),
        CheckReach.Key("15.9 Name", "The risks' two columns, beneath the risks"),
        CheckReach.Key("15.9 Name", "The risks' two columns, what would turn it up ruled across its top in the rise hue and what would make it worse in the fall hue"),
        CheckReach.Key("15.9 Name", "The risks' two columns, each opening on the line computed at the price the masthead draws and then the writer's lines"),
        CheckReach.Key("15.9 Name", "The risks' two columns, a claim to a row"),
        CheckReach.Key("15.9 Name", "Calendar, the ninety days after the night as a timeline"),
        CheckReach.Key("15.9 Name", "Calendar, a lane each for results"),
        CheckReach.Key("15.9 Name", "Calendar, the ex-dividend date"),
        CheckReach.Key("15.9 Name", "Calendar, the FOMC and the CPI"),
        CheckReach.Key("15.9 Name", "Calendar, an estimated date drawn hollow"),
        CheckReach.Key("15.9 Name", "Calendar, as a table with each event's days after the night"),
        CheckReach.Key("15.9 Name", "Calendar, a line for each kind whose table ends inside the days"),
        CheckReach.Key("15.9 Name", "Calendar, the dated items the research read beneath"),
        CheckReach.Key("15.9 Name", "Sources, every document the research read with its date and link"),
        CheckReach.Key("15.9 Name", "Sources, the figures checked and not used with the filing's first"),
        CheckReach.Key("15.9 Name", "Sources, the line naming what is computed"),
        CheckReach.Key("15.9 Name", "Sources, in the figures' place a line where every one agrees or no filing's facts are stored"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, The ninety days after the night on one axis counted in days from it"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, a lane each for the company's results"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, its ex-dividend date"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, the FOMC's decisions and the CPI's releases"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, each named at its lane's start"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, each event a dot on its day with its words beneath the pointer"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, an estimated date drawn hollow"),
        CheckReach.Key("15.5 The mark vocabulary", "Calendar timeline, A name with no event in the days says so"),
        CheckReach.Key(Scope.FailureTable, "No filed facts are stored for the latest quarter's lines"),
        CheckReach.Key(Scope.FailureTable, "No dated event falls in the ninety days after the night"),
    ];

    [Fact]
    public void TheCalendarTimelineDrawsEachEventOnItsDayAndSaysSoWhereNoneFallsInTheDays()
    {
        var marks = new MarkRenderer();
        var night = new DateOnly(2026, 10, 9);
        var through = night.AddDays(PageCalendar.Days);

        // A full input: results, an estimated ex-dividend date, the FOMC and the CPI each a dot on its lane at its day, the
        // estimate drawn hollow, each with its words beneath the pointer.
        CalendarItem[] items =
        [
            new(new DateOnly(2026, 10, 28), "fomc", "FOMC decision"),
            new(new DateOnly(2026, 11, 9), CalendarItem.ExDividend, "Ex-dividend, estimated from its usual interval", Declared: false, Amount: 0.27m),
            new(new DateOnly(2026, 11, 18), CalendarItem.Results, "Results, after the close"),
            new(night.AddDays(60), "cpi", "CPI release"),
        ];
        var full = new CalendarView(night, through, items, ["FOMC decision"]);
        var drawn = marks.CalendarTimeline("KEYS", full);

        Assert.Contains("data-mark=\"calendar-timeline\" data-items=\"4\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"ct-dot ct-fomc\" ", drawn, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"ct-dot ct-ex-dividend ct-estimated\" ", drawn, StringComparison.Ordinal);
        Assert.Contains(".ct-ex-dividend.ct-estimated{fill:var(--surface);stroke:var(--s3);stroke-width:2}", EquityBrief.Web.App.Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("data-date=\"2026-11-18\" data-kind=\"results\" data-declared=\"yes\"><title>2026-11-18: Results, after the close</title>", drawn, StringComparison.Ordinal);

        // A lane each for the company's results, its ex-dividend date, the FOMC's decisions and the CPI's releases, top to
        // bottom, each named at its lane's start, and the days counted from the night along the one axis.
        var lanes = Regex.Matches(drawn, "<text class=\"m-cap\" x=\"([^\"]+)\" y=\"([^\"]+)\" text-anchor=\"end\">([^<]+)</text>")
            .Select(lane => (X: lane.Groups[1].Value, Y: double.Parse(lane.Groups[2].Value, CultureInfo.InvariantCulture), Name: lane.Groups[3].Value))
            .ToArray();
        var ticks = Regex.Matches(drawn, "<text class=\"m-tick\" x=\"([^\"]+)\" y=\"[^\"]+\" text-anchor=\"middle\">([^<]+)</text>")
            .Select(tick => (X: tick.Groups[1].Value, Words: tick.Groups[2].Value))
            .ToArray();

        Assert.Equal(["Results", "Ex-dividend", "FOMC", "CPI"], lanes.Select(lane => lane.Name));
        Assert.Single(lanes.Select(lane => lane.X).Distinct());
        Assert.True(lanes.Zip(lanes.Skip(1)).All(pair => pair.First.Y < pair.Second.Y), "the lanes are not drawn top to bottom");
        Assert.Equal(["the night", "+30 days", "+60 days", "+90 days"], ticks.Select(tick => tick.Words));

        // Each event a dot on its day on its own kind's lane: the CPI release sixty days after the night stands on the
        // axis's sixtieth day, the dots run in the order of their days, and each sits on the line its lane's name is set
        // four beneath.
        var dots = Regex.Matches(drawn, "<circle class=\"ct-dot [^\"]+\" cx=\"([^\"]+)\" cy=\"([^\"]+)\" r=\"6\" data-date=\"[^\"]+\" data-kind=\"([^\"]+)\"")
            .Select(dot => (X: dot.Groups[1].Value, Y: double.Parse(dot.Groups[2].Value, CultureInfo.InvariantCulture), Kind: dot.Groups[3].Value))
            .ToArray();
        string[] laneOf = ["results", "ex-dividend", "fomc", "cpi"];

        Assert.Equal(ticks.Single(tick => tick.Words == "+60 days").X, dots.Single(dot => dot.Kind == "cpi").X);
        Assert.Equal(
            dots.Select(dot => double.Parse(dot.X, CultureInfo.InvariantCulture)).Order(),
            dots.Select(dot => double.Parse(dot.X, CultureInfo.InvariantCulture)));
        Assert.All(dots, dot => Assert.Equal(lanes[Array.IndexOf(laneOf, dot.Kind)].Y, dot.Y + 4));

        // The region: the table with each event's days after the night, the estimated payment a share, and the line for the
        // kind whose table ends inside the days.
        var region = marks.TheCalendar("KEYS", full, null, []);

        Assert.Contains("<tr data-date=\"2026-11-09\" data-kind=\"ex-dividend\" data-declared=\"no\"><td>2026-11-09</td><td>Ex-dividend, estimated from its usual interval, ", region, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">31</td>", region, StringComparison.Ordinal);
        Assert.Contains("<p class=\"calendar-past\" data-past=\"FOMC decision\">The table of each FOMC decision ends inside these days, so a later one is not ruled out.</p>", region, StringComparison.Ordinal);

        // The input it degrades on: no event in the days, no timeline and no table, and the line saying so.
        var none = new CalendarView(night, through, [], []);

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"calendar-timeline\" data-items=\"0\">No dated event of KEYS's or the market's falls in the 90 days after 2026-10-09, so no timeline is drawn.</p>",
            marks.CalendarTimeline("KEYS", none));
        Assert.DoesNotContain("<table class=\"calendar-dates\"", marks.TheCalendar("KEYS", none, null, []), StringComparison.Ordinal);
    }

    // see: The risks close on what would turn the stock up and what would make it worse, each column opening on a line the plan computes
    [Fact]
    public void TheRisksComputedLinesNameABandOfOnePriceAtItsPriceAndAWiderBandByItsEdges()
    {
        LadderBand[] bands =
        [
            new(217.78m, 217.78m, "resistance", 1.3, 2, null, null),
            new(240.00m, 245.50m, "resistance", 4.0, 1, null, null),
            new(190.00m, 195.50m, "support", 2.1, 3, null, null),
        ];

        // A band whose two edges are one price is named at it, and the nearest band is the one read.
        var lines = EquityBrief.Api.Reading.NameScreen.RiskLinesAt(bands, 205.15m, invalidation: null);

        Assert.Equal("A close above the nearest resistance band at 217.78, 1.3 typical days above.", lines.Up);
        Assert.Equal("A close below the nearest support band, 190.00 to 195.50, 2.1 typical days below.", lines.Worse);

        // Past the band of one price the next is a wider band, named by its edges; and the plan's invalidation, where one
        // is stored, closes the other column in place of the support band.
        var past = EquityBrief.Api.Reading.NameScreen.RiskLinesAt(bands, 230.00m, invalidation: 184.26m);

        Assert.Equal("A close above the nearest resistance band, 240.00 to 245.50, 4.0 typical days above.", past.Up);
        Assert.Equal("A close below the plan's invalidation at 184.26, where the whole position is sold.", past.Worse);
    }

    [Fact]
    public void TheSourcesListTheFiguresTheFilingAndTheFetchStateDifferentlyAndSayWhereNoneCouldBeChecked()
    {
        var marks = new MarkRenderer();
        var conflict = new FigureConflict("Gross profit", new DateOnly(2026, 7, 31), 1_270_000_000m, "GrossProfit", new DateOnly(2026, 9, 2), 1_272_000_000m);

        // The filing's figure first with its concept and the day it was first filed, then the fetch's, which the page draws.
        var listed = marks.Sources("KEYS", [], [conflict], filedFactsHeld: true);

        Assert.Contains("data-conflicts=\"1\"", listed, StringComparison.Ordinal);
        Assert.Contains("<tr data-line=\"Gross profit\" data-filed=\"1270000000\" data-fetched=\"1272000000\"><td>Gross profit</td><td>2026-07-31</td>", listed, StringComparison.Ordinal);
        Assert.Contains(", first filed 2026-09-02 as GrossProfit</td>", listed, StringComparison.Ordinal);
        Assert.Contains("<td>the fetch's</td></tr>", listed, StringComparison.Ordinal);

        // Every line agreeing within rounding, and no filed facts to check by, each said in its own words, and the line
        // naming what is computed under every one.
        var agreeing = marks.Sources("KEYS", [], [], filedFactsHeld: true);
        var unread = marks.Sources("KEYS", [], [], filedFactsHeld: false);

        Assert.Contains("<p class=\"degraded\" data-conflicts=\"none\">The latest quarter's lines the filing states agree with the provider's fetch within rounding.</p>", agreeing, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-conflicts=\"unread\">No filing's facts are stored for the latest quarter, so its lines are checked against no second source.</p>", unread, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-documents=\"none\">no written section cites a document</p>", unread, StringComparison.Ordinal);
        Assert.All(new[] { listed, agreeing, unread }, region => Assert.Contains("<p class=\"computed-line\">Computed by code from stored figures and sourced from no document: ", region, StringComparison.Ordinal));
    }
}
