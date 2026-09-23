using EquityBrief.Core.Shortlist;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 9.1: the selected name's region names what its link opens, calling it a
// report only where the name holds one, and where the name holds none says so beside the
// control asking for one.
public partial class ReadSurface
{
    // Tonight's page as the route draws it with the given row selected.
    static string TonightWith(IReadOnlyList<ListingCell> rows, string ticker) =>
        new SinglePageApp().TonightRegion(
            new MarkRenderer(), rows[0].SessionDate, 503, rows.Count, null, rows, [], "<div class=\"plan\"></div>", null, selectedTicker: ticker);

    // The selected name's card, from its opening to the next card.
    static string SelectedCard(string page)
    {
        var opens = page.IndexOf("data-card=\"selected\"", StringComparison.Ordinal);

        Assert.True(opens >= 0, "the page draws no selected name's card");

        var next = page.IndexOf("data-card=\"", opens + 1, StringComparison.Ordinal);

        return next < 0 ? page[opens..] : page[opens..next];
    }

    [Fact]
    public void TheSelectedNameCallsWhatItOpensAReportOnlyWhereOneIsWritten()
    {
        var night = new DateOnly(2026, 9, 22);
        ListingCell[] rows =
        [
            new("ZZZA", night, 2, 10, 10m, [ShortlistSeries.AtEntryZone], ResearchedOn: new DateOnly(2026, 9, 20)),
            new("ZZZB", night, 1, 5, 10m, [ShortlistSeries.AtEntryZone]),
        ];

        // A name holding research: its report, by that word and with the day it was written,
        // and no line or control saying it holds none.
        var held = SelectedCard(TonightWith(rows, "ZZZA"));

        Assert.Contains("Open the full report for ZZZA, written 2026-09-20", WordsOf(held), StringComparison.Ordinal);
        Assert.Contains("href=\"#/name/ZZZA\"", held, StringComparison.Ordinal);
        Assert.DoesNotContain("sel-unwritten", held, StringComparison.Ordinal);
        Assert.DoesNotContain("research-control", held, StringComparison.Ordinal);

        // A name holding none: nothing calls what the link opens a report, a line says none
        // is written, and the control asking for one is the control the row carries.
        var none = SelectedCard(TonightWith(rows, "ZZZB"));

        Assert.DoesNotContain("report for ZZZB", WordsOf(none), StringComparison.Ordinal);
        Assert.Contains("No report is written for ZZZB yet.", WordsOf(none), StringComparison.Ordinal);
        Assert.Contains("Open ZZZB's page", WordsOf(none), StringComparison.Ordinal);
        Assert.Contains("href=\"#/name/ZZZB\"", none, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.AskForAReport("ZZZB"), none, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.AskForAReport("ZZZB"), new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn), StringComparison.Ordinal);
    }
}
