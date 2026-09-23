using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 5.8: how long tonight's list is, stated above its rows and counted down them,
// and the universe's paging stating its count apart from its page.
//
// Read off the markup the renderer draws, because what these are for is what a reader sees:
// how many rows the list draws of how many fired before reading any of them, which row of
// them a reader is on, and a count and a page that cannot be read as one figure.
public partial class ReadSurface
{
    static ListingCell[] FiredNight(DateOnly night, int count) =>
    [
        .. Enumerable.Range(0, count)
            .Select(at => new ListingCell($"N{at:00}", night, 3 - (at % 3), count - at, 100m, [ShortlistSeries.AtEntryZone])),
    ];

    // The words an element draws, with its tags taken out and its entities read.
    static string WordsOf(string markup) =>
        WebUtility.HtmlDecode(Regex.Replace(markup, "<[^>]+>", " ")).Trim();

    [Fact]
    public void TheListNumbersItsRowsInItsOrderUnderALineStatingHowManyAreDrawnOfHowManyFired()
    {
        // Nights either side of the most drawn and at it, and one of a single name, because
        // what the line says turns on whether the rows leave a name out.
        var night = new DateOnly(2026, 9, 18);
        var marks = new MarkRenderer();

        foreach (var (fired, says) in new[]
        {
            (40, "Showing 20 of the 40 names that fired."),
            (20, "Showing all 20 names that fired."),
            (7, "Showing all 7 names that fired."),
            (1, "Showing the one name that fired."),
        })
        {
            var list = marks.TonightList(FiredNight(night, fired), SinglePageApp.TonightDrawn, []);
            var drawn = Math.Min(fired, SinglePageApp.TonightDrawn);

            // The line stands above the rows it counts, and says where every name is where the
            // rows leave one out.
            var line = Regex.Match(list, "<p class=\"list-count\"[^>]*>.*?</p>", RegexOptions.Singleline);

            Assert.True(line.Success, $"a night of {fired} draws no line stating how many rows the list draws");
            Assert.True(line.Index < list.IndexOf("<table", StringComparison.Ordinal), $"a night of {fired} states how many are drawn below the rows");
            Assert.StartsWith(says, WordsOf(line.Value), StringComparison.Ordinal);
            Assert.Equal(fired > drawn, line.Value.Contains("href=\"#/universe\"", StringComparison.Ordinal));

            // Each drawn row opens with its place in the order the rows are drawn in, counted
            // from one, the figure on the cell being the one its attribute carries.
            var places = Regex
                .Matches(list, "<tr data-ticker=\"[^\"]+\"[^>]*><td class=\"place\" data-place=\"(?<at>\\d+)\">(?<shows>[^<]*)</td>")
                .Select(row => (At: row.Groups["at"].Value, Shows: row.Groups["shows"].Value))
                .ToArray();

            Assert.Equal(Regex.Matches(list, "<tr data-ticker=\"").Count, places.Length);
            Assert.Equal([.. Enumerable.Range(1, drawn).Select(at => at.ToString(CultureInfo.InvariantCulture))], [.. places.Select(place => place.At)]);
            Assert.All(places, place => Assert.Equal(place.At, place.Shows));

            // The place has a head of its own, and the footer's label spans every column before
            // the first reason, so each reason's record still stands under its own column.
            var head = Regex.Match(list, "<thead><tr>(?<cells>.*?)</tr></thead>", RegexOptions.Singleline).Groups["cells"].Value;
            var heads = Regex.Matches(head, "<th(?: class=\"(?<class>[^\"]*)\")?[^>]*>").Select(cell => cell.Groups["class"].Value).ToArray();

            Assert.Equal("place", heads[0]);

            var span = Regex.Match(list, "<tfoot><tr><td colspan=\"(?<span>\\d+)\"");

            Assert.True(span.Success, "the list draws no footer label");
            Assert.Equal(heads.TakeWhile(named => named != "rz").Count(), int.Parse(span.Groups["span"].Value, CultureInfo.InvariantCulture));
        }

        // A night where nothing fired draws no rows to count, and says none fired.
        var none = marks.TonightList([], SinglePageApp.TonightDrawn, []);

        Assert.DoesNotContain("list-count", none, StringComparison.Ordinal);
        Assert.Contains("no name fired a reason tonight", none, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUniversesPagingStatesItsCountApartFromItsPage()
    {
        // The count and the page each stand beside their own word, and no figure in what the
        // paging says follows another across a comma or a space alone, which a reader takes
        // for one number with its thousands set off.
        var marks = new MarkRenderer();

        static string CountAndPage(string nav) =>
            WordsOf(Regex.Match(nav, "<span class=\"page-of\">.*?</span>", RegexOptions.Singleline).Value);

        Assert.Equal("503 names, page 1 of 11", CountAndPage(marks.UniversePaging(503, 1, 50, null, null)));
        Assert.Equal("1 name, page 1 of 1", CountAndPage(marks.UniversePaging(1, 1, 50, null, null)));

        foreach (var (rows, page) in new[] { (503, 1), (503, 11), (1, 1), (0, 1), (1000, 7), (7, 1) })
        {
            var words = WordsOf(marks.UniversePaging(rows, page, UniverseScreen.PageSize, "uptrend", null));

            Assert.DoesNotMatch(@"\d[\s,]+\d", words);
        }

        // The matcher is shown to find the figures it is for.
        Assert.Matches(@"\d[\s,]+\d", "page 1 of 11, 503 name(s)");
        Assert.DoesNotMatch(@"\d[\s,]+\d", "503 names, page 1 of 11");
    }
}
