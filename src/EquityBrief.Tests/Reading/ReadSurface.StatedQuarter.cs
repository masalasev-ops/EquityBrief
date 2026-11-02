using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Reading;

// A quarter's end is drawn as the company's filing states it, and quarters go on being matched on the provider's label.
// see: A page draws a quarter's end as the company's filing states it, and matches quarters on the provider's label
public partial class ReadSurface
{
    static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // By hand: AAPL's quarter ended on 2026-06-27 and is labelled 2026-06-30, three days off. A week off is the same quarter
    // and a day past it is not, the nearest of two winning, and a quarter no filing states keeps its label.
    [Fact]
    public void AFilingsEndWithinAWeekOfTheLabelIsTheDateDrawnAndALabelNoFilingStatesStands()
    {
        var label = new DateOnly(2026, 6, 30);

        Assert.Equal(new DateOnly(2026, 6, 27), new StatedQuarters([new DateOnly(2026, 6, 27), new DateOnly(2026, 3, 28)]).Of(label));
        Assert.Equal(new DateOnly(2026, 6, 23), new StatedQuarters([new DateOnly(2026, 6, 23)]).Of(label));
        Assert.Equal(new DateOnly(2026, 7, 7), new StatedQuarters([new DateOnly(2026, 7, 7)]).Of(label));
        Assert.Equal(label, new StatedQuarters([new DateOnly(2026, 6, 22)]).Of(label));
        Assert.Equal(label, new StatedQuarters([new DateOnly(2026, 7, 8)]).Of(label));
        Assert.Equal(new DateOnly(2026, 6, 29), new StatedQuarters([new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 29)]).Of(label));
        Assert.Equal(label, new StatedQuarters([]).Of(label));
        Assert.Null(new StatedQuarters([label]).Of((DateOnly?)null));
    }

    // Every provider label a filing corrects, read off both pages with the attributes set apart: a date a page matches on
    // stays in its attribute, and no word a reader sees carries one.
    [Fact]
    public async Task EachPageDrawsAQuarterAsItsFilingStatesItAndKeepsTheLabelForMatching()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var stated = (await Api(store).FundamentalsAsync(Name, new DateOnly(2026, 9, 4)))
            .Select(filing =>
            {
                using var payload = JsonDocument.Parse(filing.Payload);

                return DateOnly.ParseExact(payload.RootElement.GetProperty("periodEnd").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            })
            .OrderDescending()
            .Take(8)
            .ToArray();

        Assert.Equal(8, stated.Length);

        var labels = stated.Select(day => day.AddDays(3)).ToArray();

        foreach (var label in labels)
        {
            store.Execute(
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue, gross_profit, operating_income, net_income, operating_cash_flow, eps_actual, eps_estimate, eps_trailing, close_after) VALUES "
                + $"('{Name}', '2026-09-04T21:00:00.000Z', '2026-09-04', '{Iso(label)}', '{Iso(label.AddDays(25))}', '1000', '600', '300', '200', '220', '1.10', '1.00', '4.40', '80');");
        }

        var quarters = labels.Take(4).Select((label, back) => FixtureExpectations.Quarter(back) with { PeriodEnd = label }).ToArray();

        FixtureExpectations.StoreReadings(store, FixtureNight, Name, QuarterReadings.Of(quarters, 80m, null));

        var page = await client.GetStringAsync($"/screens/name/{Name}");
        var chart = await client.GetStringAsync($"/screens/chart/{Name}");

        static string Words(string html) => Regex.Replace(html, "\\sdata-[a-z-]+=\"[^\"]*\"", string.Empty);

        foreach (var label in labels)
        {
            Assert.DoesNotContain(Iso(label), Words(page), StringComparison.Ordinal);
            Assert.DoesNotContain(Iso(label), Words(chart), StringComparison.Ordinal);
        }

        Assert.Contains($"The quarter to {Iso(stated[0])}, line by line", page, StringComparison.Ordinal);
        Assert.Contains($"<table class=\"quarter-table\" data-quarter=\"{Iso(labels[0])}\" data-year-earlier=\"{Iso(labels[4])}\">", page, StringComparison.Ordinal);
        Assert.Contains($"Quarter to {Iso(stated[4])}</th>", page, StringComparison.Ordinal);
        Assert.Contains($"Read from the quarter to {Iso(stated[0])}.", page, StringComparison.Ordinal);
        Assert.Contains($"Quarter to {Iso(stated[0])}, earnings a share", page, StringComparison.Ordinal);
        Assert.Contains($"Latest quarter</dt><dd>ended <span class=\"nw\">{Iso(stated[0])}</span>", chart, StringComparison.Ordinal);
    }

    // The proof's dates are the label's, so a title drawing the filing's date moves none of them: a next report naming the
    // label still names the quarter drawn, and a reported line of the label is still that quarter's.
    [Fact]
    public void TheProofMatchesTheQuarterTableLabelWhereTheTitleDrawsTheFilingsDate()
    {
        static string Verdict(string page) => VerdictOf(ReportProof.Evaluate(Name, page), "report-day");

        const string Reported = "<section id=\"quarter\"><div class=\"spine\"><span class=\"dl-k\">Reported</span><b>2026-10-28</b></div><h2>The quarter to 2026-09-27, line by line</h2><table class=\"quarter-table\" data-quarter=\"2026-09-30\" data-year-earlier=\"none\"></table></section>";
        const string NotAnnounced = "<div><dt>Next report</dt><dd data-reportDate=\"reported\" data-estimated=\"2027-01-27\"><b>not announced</b></dd></div>";

        Assert.Equal(ReportProof.Failed, Verdict(Reported + "<div><dt>Next report</dt><dd data-reportDate=\"2026-11-04\"><b>2026-11-04</b><small>consensus 4.69 a share, for the quarter to 2026-09-30</small></dd></div>"));
        Assert.Equal(ReportProof.Failed, Verdict(Reported + NotAnnounced + "<p class=\"estimate\" data-estimated-quarter=\"2026-09-30\" data-reported=\"2026-10-21\">"));
        Assert.Equal(ReportProof.Passed, Verdict(Reported + NotAnnounced + "<p class=\"estimate\" data-estimated-quarter=\"2026-09-30\" data-reported=\"2026-10-28\">"));
    }
}
