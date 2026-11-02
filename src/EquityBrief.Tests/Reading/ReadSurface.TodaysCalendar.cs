using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// The calendar keeps no history, so a page for an earlier night reads it as it stands and says so; and a name's next dated
// event is the first after the night, the night's own being no next.
// see: A page for an earlier night draws the next report and the calendar as the calendar holds them today, and says so
// see: A name's next dated event is the first after the night, and the earnings soon reason alone counts the night's own
public partial class ReadSurface
{
    const string FixtureNight = "2026-09-04";

    static void CalendarOf(TemporaryStore store, params (string Day, string Kind)[] rows) =>
        store.Execute(
            $"DELETE FROM calendar WHERE ticker = '{Name}';"
            + string.Concat(rows.Select(row => $"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{Name}', '{row.Day}', '{row.Kind}', 'before', '{{}}', '2026-10-01T21:00:00Z');")));

    [Fact]
    public async Task APageForAnEarlierNightSaysItsNextReportAndItsCalendarAreAsTheCalendarHoldsThemToday()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        CalendarOf(store, ("2026-09-20", "earnings"), ("2026-09-10", "ex-dividend"));

        var dated = await client.GetStringAsync($"/screens/name/{Name}/{FixtureNight}");
        var tonight = await client.GetStringAsync($"/screens/name/{Name}");

        // The calendar's row is read on the dated page, a day after its night among them, and says whose it is.
        Assert.Contains("<dt>Next report</dt><dd data-reportDate=\"2026-09-20\"><b>2026-09-20</b><small>", dated, StringComparison.Ordinal);
        Assert.Contains($", {NameScreen.CalendarToday}</small></dd>", dated, StringComparison.Ordinal);
        Assert.Contains($"and is expected on 2026-09-20, {NameScreen.CalendarToday}, with a consensus estimate", dated, StringComparison.Ordinal);
        Assert.Contains($"<dt>Next dated event</dt><dd>2026-09-10 <small>{NameScreen.CalendarToday}</small></dd>", dated, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"calendar-today\" data-night=\"{FixtureNight}\">The dates here are {NameScreen.CalendarToday}, which keeps no record of what it held on {FixtureNight}.</p>", dated, StringComparison.Ordinal);

        // Tonight's page reads the same days and says none of it.
        Assert.Contains("<dt>Next report</dt><dd data-reportDate=\"2026-09-20\"><b>2026-09-20</b>", tonight, StringComparison.Ordinal);
        Assert.DoesNotContain(NameScreen.CalendarToday, tonight, StringComparison.Ordinal);
        Assert.DoesNotContain("calendar-today", tonight, StringComparison.Ordinal);

        // A day from the filing's payload and not the calendar says nothing of the kind.
        CalendarOf(store);

        var unlabelled = await client.GetStringAsync($"/screens/name/{Name}/{FixtureNight}");

        Assert.Contains("<dt>Next report</dt><dd data-reportDate=\"2026-10-29\"><b>2026-10-29</b>", unlabelled, StringComparison.Ordinal);
        Assert.DoesNotContain($"2026-10-29, {NameScreen.CalendarToday}", unlabelled, StringComparison.Ordinal);
        Assert.DoesNotContain($", {NameScreen.CalendarToday}</small></dd>", unlabelled, StringComparison.Ordinal);
    }

    // The plan's block around the next report and the line saying the numbers' reported quarter, off the same words.
    [Fact]
    public void ThePlansBlockAndTheNumbersLineSayTheCalendarIsTodaysOnAnEarlierNightAlone()
    {
        var night = new DateOnly(2026, 9, 28);
        CalendarItem next = new(new DateOnly(2026, 11, 2), CalendarItem.Results, "Results", true, null);
        var view = new CalendarView(night, night.AddDays(90), [next], []);

        Assert.Contains("The next report is on 2 Nov 2026, as the calendar dates it.", MarkRenderer.NextReportAroundThePlan(view), StringComparison.Ordinal);
        Assert.Contains($"The next report is on 2 Nov 2026, {NameScreen.CalendarToday}.", MarkRenderer.NextReportAroundThePlan(view with { EarlierNight = true }), StringComparison.Ordinal);
    }

    // The night's own event is no next: an ex-dividend date on the night and a report after it, the fact strip and the
    // chart page each read the first after it. The stored calendar's reader asked from the night itself still answers the
    // night's, which is what asks from the day after.
    [Fact]
    public async Task ANamesNextDatedEventIsTheFirstAfterTheNightOnThePageAndTheChartPage()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        CalendarOf(store, (FixtureNight, "earnings"), ("2026-09-20", "earnings"));

        var page = await client.GetStringAsync($"/screens/name/{Name}");
        var chart = await client.GetStringAsync($"/screens/chart/{Name}");

        Assert.Contains("<dt>Next dated event</dt><dd>2026-09-20</dd>", page, StringComparison.Ordinal);
        Assert.Contains("data-next-event=\"2026-09-20\"", page, StringComparison.Ordinal);
        Assert.Contains("2026-09-20", chart, StringComparison.Ordinal);
        Assert.DoesNotContain($"<dt>Next dated event</dt><dd>{FixtureNight}", page, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 9, 4), (await Api(store).NextEventAsync(Name, new DateOnly(2026, 9, 4)))!.EventDate);
    }

    // The report days keep the calendar's ex-dividend days out, the query being the one the staleness verdict reads.
    [Fact]
    public async Task TheReportDaysOnTheCalendarLeaveItsExDividendDaysOut()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        CalendarOf(store, ("2026-09-20", "earnings"), ("2026-09-10", "ex-dividend"), ("2026-07-30", "earnings"));

        Assert.Equal([new DateOnly(2026, 7, 30), new DateOnly(2026, 9, 20)], await Api(store).ReportDaysOnTheCalendarAsync(Name));
    }
}
