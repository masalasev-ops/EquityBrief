using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 5.8: each of the universe table's column headings says what its column holds while the
// pointer is over it or it has the focus, read back off the table word for word against sentences written
// here, with the windows each one names stated as numbers rather than read from the constants that set them.
public partial class ReadSurface
{
    // Each heading as drawn: its name, the words it shows, and what it says of its column.
    static IReadOnlyList<(string Heading, string Shown, string Says)> HeadingsOf(string table) =>
    [
        .. Regex.Matches(
                WebUtility.HtmlDecode(Regex.Match(table, "<table class=\"universe-table\"[^>]*><tr>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value),
                "<th class=\"tipped\" tabindex=\"0\" data-heading=\"([^\"]+)\"><span class=\"th-t\">([^<]+)</span><span class=\"head-tip\" role=\"tooltip\">([^<]+)</span></th>")
            .Select(heading => (heading.Groups[1].Value, heading.Groups[2].Value, heading.Groups[3].Value)),
    ];

    // The twelve sentences, written out: a window the swing reader or the indicator engine moves makes
    // one of these false, and this is where that shows.
    static readonly (string Heading, string Says)[] SaidOfEachColumn =
    [
        ("Name", "The stock's ticker, which opens its own page, with the company beneath it. Hold the pointer over the name to see its year of closes with its nearest support and resistance."),
        ("Sector", "The sector the index files the company under."),
        ("Close", "The stock's closing price on the night the table is drawn for."),
        ("Trend", "The chart's trend in a word. Uptrend: the close is above its 50-day average, the 50-day is above the 200-day, and the latest swing low is above the one before it. Downtrend: the mirror of that. Range: every input is there and neither holds. Not classified: too little history to read one."),
        ("Distance", "How far the close sits from its nearest support below and its nearest resistance above, in typical days' moves, a typical day's move being the stock's average true range over 14 sessions. S is the gap between the close and the top of the support band, R the gap between the close and the bottom of the resistance band, so 0.0 means the close is at that band's edge. In the picture the close is the centre line, support is green to the left and resistance orange to the right, one tick per typical day, and an arrow marks a band more than 4 away."),
        ("Sessions to earnings", "Trading sessions from the night to the company's next dated earnings report on the calendar."),
        ("Strength", "How the stock's returns compare with the rest of the index: the average of where its return over the last 63 sessions and over the last 126 sits among every other member's. 90% means that across the two it beat nine in ten of them."),
        ("Pullback", "How far the close sits below the highest high of the last 20 sessions, in typical days' moves."),
        ("Dry-up", "The median daily volume since that high against the stock's 50-day average volume. Below 1 means trading thinned out on the way down."),
        ("Tightness", "The mean true range, a day's full span of movement, of the last 10 sessions against the last 50. Below 1 means the price has been moving in a narrower range lately."),
        ("Last on the list", "The last evening the stock was on tonight's list, or never."),
        ("Sixty evenings", "One column for each of the last sixty sessions: a solid bar on an evening the stock was on the list and a thin line on one it was not. Before the swing filter's first night an evening's list held every stock any reason fired for, which was most of the index."),
    ];

    [Fact]
    public void EachUniverseHeadingSaysWhatItsColumnHoldsWhileThePointerIsOverIt()
    {
        UniverseCell[] rows = [new("ZZAA", "Energy", 110m, "uptrend", 96m, 114m, 9.33, 2.67, 2.67, Name: "Zed Ay")];

        var table = new MarkRenderer().UniverseTable(rows);
        var headings = HeadingsOf(table);

        // One heading for each column a row draws, each naming its column and saying what it holds in the
        // words written above.
        Assert.Equal(SaidOfEachColumn.Select(column => column.Heading), headings.Select(heading => heading.Heading));
        Assert.Equal(SaidOfEachColumn.Select(column => column.Heading), headings.Select(heading => heading.Shown));
        Assert.Equal(Regex.Matches(Regex.Match(table, "<tr data-ticker=\"ZZAA\".*?</tr>", RegexOptions.Singleline).Value, "<td[ >]").Count, headings.Count);

        foreach (var ((heading, says), drawn) in SaidOfEachColumn.Zip(headings))
        {
            Assert.True(says == drawn.Says, $"The {heading} heading says \"{drawn.Says}\".");
        }

        // Hidden until the pointer is over the heading or it has the focus, in the sentence's own type, and
        // placed beside the heading by the shell.
        Assert.Contains(".head-tip{display:none;position:fixed;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("letter-spacing:normal;text-transform:none;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("th.tipped:hover .head-tip,th.tipped:focus-within .head-tip{display:block}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("cell.querySelector('.peer-pop, .head-tip, .why, .says')", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUniverseScreenDrawsEveryHeadingWithWhatItsColumnHolds()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var screen = await client.GetStringAsync("/screens/universe");

        // The twelve headings, once each on the screen and nowhere else, each saying what its column holds.
        Assert.Equal(SaidOfEachColumn, HeadingsOf(screen).Select(heading => (heading.Heading, heading.Says)));
        Assert.Equal(SaidOfEachColumn.Length, Regex.Matches(screen, "<span class=\"head-tip\" role=\"tooltip\">").Count);
    }
}
