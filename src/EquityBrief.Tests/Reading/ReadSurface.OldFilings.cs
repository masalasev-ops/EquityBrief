using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// A part a row carries that is older than the row's own filing by more than the limit is another filing's: TPL's segment table
// of 2020-09-30 on its filing of 2026-08-05, TT's "2008 OUTLOOK" filed on 2008-04-29.
// see: A part filed more than 200 days before the page's own filing is neither drawn nor handed to a writer, and a fetch takes the filer the SEC names
public partial class ReadSurface
{
    static JsonObject OldTable(string ended) => FiledSegments(
        [new ReportPeriod(3, DateOnly.ParseExact(ended, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))],
        FiledFigure("us-gaap_Revenues", "Revenues", null, new ReportPeriod(3, DateOnly.ParseExact(ended, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "74400000"));

    static JsonObject GuidanceFiledOn(string filedOn) => new()
    {
        ["document"] = "ex99.htm",
        ["filedOn"] = filedOn,
        ["heading"] = "2008 OUTLOOK",
        ["passage"] = "We expect revenue of $7.5 billion.",
        ["located"] = true,
    };

    [Fact]
    public void ThePartsLimitIsTwoHundredDaysAndTheDayItsCountedFromIsTheFilings()
    {
        var filed = new DateOnly(2026, 8, 5);

        Assert.Equal(200, OldFilings.MostDays);
        Assert.False(OldFilings.TooOld(filed.AddDays(-200), filed));
        Assert.True(OldFilings.TooOld(filed.AddDays(-201), filed));
        Assert.False(OldFilings.TooOld(filed.AddDays(5), filed));

        using var table = JsonDocument.Parse(OldTable("2020-09-30").ToJsonString());

        Assert.Equal(new DateOnly(2020, 9, 30), OldFilings.TableEnded(table.RootElement));
        Assert.True(OldFilings.TableTooOld(table.RootElement, filed));

        // A table with its newest period inside the limit stands, whatever older column it carries.
        using var both = JsonDocument.Parse(FiledSegments([new ReportPeriod(3, new DateOnly(2025, 6, 30)), new ReportPeriod(3, new DateOnly(2026, 6, 30))]).ToJsonString());

        Assert.False(OldFilings.TableTooOld(both.RootElement, filed));
    }

    [Fact]
    public async Task APartOlderThanItsFilingIsNotDrawnAndThePageSaysWhy()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        var filings = await Api(store).FundamentalsAsync(Name);
        var filed = filings[0].FilingDate;
        var payload = JsonNode.Parse(filings[0].Payload)!.AsObject();

        payload["segments"] = OldTable("2020-09-30");
        payload["guidance"] = GuidanceFiledOn("2008-04-29");

        FilingRow[] old = [filings[0] with { Payload = payload.ToJsonString() }];

        // The table as filed is not drawn, and the region says why in its place.
        Assert.Null(NameScreen.RawSegments(old));

        var reading = new SegmentReading("R33.htm", new DateOnly(2020, 9, 30), 0, null, null, [], OldFilings.Why("the table stored with this filing ends on", new DateOnly(2020, 9, 30), new DateOnly(2026, 8, 5))) { LeftOut = true };

        Assert.Equal(
            "<p class=\"degraded\" data-segments=\"old\" data-report=\"R33.htm\" data-ended=\"2020-09-30\">The filing's segment table is left out: the table stored with this filing ends on 2020-09-30, more than 200 days before this filing of 2026-08-05, so it is left out until the company next files.</p>",
            new MarkRenderer().SegmentsTable(reading, "USD"));

        // What management said is left out and says why, where its own filing stands drawn.
        var numbers = NameScreen.Numbers(old);

        Assert.DoesNotContain("2008 OUTLOOK", numbers, StringComparison.Ordinal);
        Assert.Contains("data-old-filed=\"2008-04-29\">What management said is left out: the release stored with this filing was filed on 2008-04-29", numbers, StringComparison.Ordinal);

        // At the limit each stands.
        var limit = filed.AddDays(-200).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        payload["segments"] = OldTable(limit);
        payload["guidance"] = GuidanceFiledOn(limit);

        FilingRow[] atTheLimit = [filings[0] with { Payload = payload.ToJsonString() }];

        Assert.Contains("data-segment-figure=\"74400000\"", NameScreen.RawSegments(atTheLimit)!, StringComparison.Ordinal);
        Assert.Contains("2008 OUTLOOK", NameScreen.Numbers(atTheLimit), StringComparison.Ordinal);
    }

    // The page itself, through the view it reads: an old table is the region's notice, and none of its figures is drawn.
    [Fact]
    public async Task ThePageDrawsTheNoticeInPlaceOfATableOlderThanItsFiling()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        store.Execute($"UPDATE fundamentals SET payload = json_set(payload, '$.segments', json('{OldTable("2020-09-30").ToJsonString()}')) WHERE ticker = '{Name}';");

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("data-segments=\"old\" data-report=\"R9.htm\" data-ended=\"2020-09-30\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-segment-figure=\"74400000\"", page, StringComparison.Ordinal);
    }
}
