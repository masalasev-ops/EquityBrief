using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.4 correction: listings written before it are kept as written, the
// routes that draw a session they belong to say what those rows could not do, and the two
// reasons the correction changed count toward their records only off rows the corrected
// rule wrote. Over a store the pipeline populated, with one earlier session written in the
// shape the rows took before the correction, since no replay can write that shape now.
// see: Sessions to a dated event are counted on the exchange calendar and never on stored bars
public partial class ReadSurface
{
    // The six reasons in the shape a row took before the correction: earnings soon fired on a
    // count of 0 with no event date among its values, and breakout on volume carrying no
    // previous close.
    const string ReasonsBeforeTheCorrection =
        "[{\"name\":\"at entry zone\",\"fired\":false,\"values\":{\"close\":\"1\",\"zones\":\"0\"}}," +
        "{\"name\":\"crossed a level\",\"fired\":false,\"values\":{\"close\":\"1\",\"previous close\":\"1\"}}," +
        "{\"name\":\"breakout on volume\",\"fired\":false,\"values\":{\"close\":\"1\",\"resistance bands below the close\":\"0\",\"volume\":\"1\",\"fifty-day average volume\":\"1\"}}," +
        "{\"name\":\"trend state changed\",\"fired\":false,\"values\":{\"trend state\":\"range\",\"previous trend state\":\"range\"}}," +
        "{\"name\":\"unusual volume\",\"fired\":false,\"values\":{\"volume\":\"1\",\"fifty-day average volume\":\"1\",\"multiple\":\"2\"}}," +
        "{\"name\":\"earnings soon\",\"fired\":true,\"values\":{\"sessions to the next dated event\":\"0\",\"horizon\":\"20\"}}]";

    [Fact]
    public async Task ASessionWrittenBeforeTheCorrectionIsSaidSoOnItsTonightRunAndUniverseRoutesAndACorrectedOneIsNot()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        const string before = "2026-09-03";

        // Every member's row for the session before, in the old shape, beside the night's own
        // rows, which the corrected builder wrote.
        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
            $"SELECT ticker, '{before}', '{ReasonsBeforeTheCorrection}', 1, plan_at_listing, shadow_reasons FROM listing WHERE session_date = '{night}';");

        Assert.Equal([new DateOnly(2026, 9, 3)], TonightScreen.WrittenBeforeTheCorrection(await Api(store).ListingsAsync()));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var surfaces = new[]
        {
            await client.GetStringAsync($"/screens/tonight/{before}"),
            await client.GetStringAsync($"/screens/run/{before}"),
            await client.GetStringAsync("/screens/universe"),
        };

        foreach (var surface in surfaces)
        {
            var line = Assert.Single(Blocks(surface, "<p class=\"written-before-correction\"[^>]*>.*?</p>"));

            Assert.Contains($"data-sessions=\"{before}\"", line, StringComparison.Ordinal);
            Assert.Contains(SinglePageApp.WrittenBeforeTheCorrectionText, WebUtility.HtmlDecode(line), StringComparison.Ordinal);
            Assert.DoesNotContain(night, line, StringComparison.Ordinal);
        }

        // A session the corrected rule wrote carries no line on either of its routes.
        Assert.DoesNotContain("written-before-correction", await client.GetStringAsync($"/screens/tonight/{night}"), StringComparison.Ordinal);
        Assert.DoesNotContain("written-before-correction", await client.GetStringAsync($"/screens/run/{night}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionWrittenBeforeTheCorrectionIsSaidSoOnTheNamePageAndItsExportedReportAndACorrectedOneIsNot()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The newest night as the corrected rule wrote it, which is the night the name page reads.
        Assert.DoesNotContain("written-before-correction", await client.GetStringAsync($"/screens/name/{name}"), StringComparison.Ordinal);
        Assert.DoesNotContain("written-before-correction", await client.GetStringAsync(ReportExporter.Route + name), StringComparison.Ordinal);

        // The same night's rows in the shape they took before the correction.
        store.Execute($"UPDATE listing SET reasons = '{ReasonsBeforeTheCorrection}', fired_count = 1 WHERE session_date = '{night}';");

        foreach (var surface in new[] { await client.GetStringAsync($"/screens/name/{name}"), await client.GetStringAsync(ReportExporter.Route + name) })
        {
            var line = Assert.Single(Blocks(surface, "<p class=\"written-before-correction\"[^>]*>.*?</p>"));

            Assert.Contains($"data-sessions=\"{night}\"", line, StringComparison.Ordinal);
            Assert.Contains(SinglePageApp.WrittenBeforeTheCorrectionText, WebUtility.HtmlDecode(line), StringComparison.Ordinal);

            // Inside the name's region, so the file carries it, and above the reasons it is about.
            var region = surface[surface.IndexOf("<section class=\"name\"", StringComparison.Ordinal)..];

            Assert.InRange(
                region.IndexOf("written-before-correction", StringComparison.Ordinal),
                0,
                region.IndexOf("<section class=\"why-it-is-here\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task EachReasonsRecordStatesTheNightsItStandsOnLeavingOutTheSessionsWrittenBeforeTheCorrection()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var api = Api(store);

        // Every session the pipeline wrote is in the corrected shape, and one more is written
        // in the old shape, so the four reasons the correction left alone stand on one night
        // more than the two it changed.
        var corrected = (await api.ListingsAsync()).Select(listing => listing.SessionDate).Distinct().Count();
        const string before = "2026-09-03";

        Assert.InRange(corrected, 1, int.MaxValue);

        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
            $"SELECT ticker, '{before}', '{ReasonsBeforeTheCorrection}', 1, plan_at_listing, shadow_reasons FROM listing WHERE session_date = '{night}';");

        var records = RunScreen.Records(await api.ListingsAsync(), RunScreen.Resolved(await api.ForwardReturnsAsync()));
        string[] changed = [ShortlistSeries.BreakoutOnVolume, ShortlistSeries.EarningsSoon];

        foreach (var record in records)
        {
            Assert.Equal(changed.Contains(record.Reason) ? corrected : corrected + 1, record.Nights);
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{night}"));

        Assert.Contains(
            $"the record below stands on {corrected + 1} night(s) of listings, and breakout on volume and earnings soon on {corrected} of them, {MarkRenderer.FewerNightsText}</p>",
            page,
            StringComparison.Ordinal);

        foreach (var reason in ShortlistSeries.Reasons)
        {
            Assert.Matches(
                $"<tr data-reason=\"{Regex.Escape(reason)}\"[^>]*data-nights=\"{(changed.Contains(reason) ? corrected : corrected + 1)}\"",
                page);
        }
    }

    [Fact]
    public void AReasonsRecordCountsTheTwoCorrectedReasonsOnlyOffRowsTheCorrectedRuleWrote()
    {
        // Two rows for one name, one written before the correction with earnings soon and
        // breakout on volume fired and neither carrying its marker, and one written since
        // with both fired and both carrying theirs. The setup the old row seeded won and the
        // new row's lost, so a record counting the old row reads a win it did not have.
        var earlier = new DateOnly(2026, 9, 3);
        var later = new DateOnly(2026, 9, 4);

        const string beforeBoth =
            "[{\"name\":\"breakout on volume\",\"fired\":true,\"values\":{\"close\":\"1\"}}," +
            "{\"name\":\"earnings soon\",\"fired\":true,\"values\":{\"sessions to the next dated event\":\"0\",\"horizon\":\"20\"}}]";

        const string sinceBoth =
            "[{\"name\":\"breakout on volume\",\"fired\":true,\"values\":{\"close\":\"1\",\"previous close\":\"0.9\"}}," +
            "{\"name\":\"earnings soon\",\"fired\":true,\"values\":{\"next dated event\":\"2026-09-10\",\"sessions to the next dated event\":\"4\",\"horizon\":\"20\"}}]";

        var records = RunScreen.Records(
            [new ListingRow("AAPL", earlier, beforeBoth, 2, "{}"), new ListingRow("AAPL", later, sinceBoth, 2, "{}")],
            [new ResolvedSetup("AAPL", earlier, ForwardReturnSeries.Win), new ResolvedSetup("AAPL", later, ForwardReturnSeries.Loss)]);

        foreach (var reason in new[] { ShortlistSeries.EarningsSoon, ShortlistSeries.BreakoutOnVolume })
        {
            var record = records.Single(one => one.Reason == reason);

            Assert.Equal(1, record.Fired);
            Assert.Equal(0, record.Won);
            Assert.Equal(1, record.Lost);
        }
    }
}
