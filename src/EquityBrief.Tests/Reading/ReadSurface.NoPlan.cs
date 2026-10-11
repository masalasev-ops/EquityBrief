using EquityBrief.Api.Reading;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 4.6 correction of 2026-10-11: a name whose ladder placed no tranche is drawn with the reason its ladder
// stored and what would bring a plan, read off that reason and the bands about the price, and a plan's tables draw a line
// where a table would hold no row. PEP's page of 2026-10-11 drew "has no plan to draw, which is what a name with no
// eligible band gets" over a trend reading down, two tables of headings alone and "Buy into these" over nothing. Worked by
// hand over PEP's bands of the night of 2026-10-09 and its close of 125.97.
// see: A name with no plan is drawn with the reason its ladder stored and what would bring a plan, and no empty table
public partial class ReadSurface
{
    static readonly LadderBand[] PepBands =
    [
        new(127.98m, 129.98m, "resistance", 0.8, 3, new DateOnly(2026, 10, 8), null),
        new(132.3165m, 132.3165m, "resistance", 2.5, 1, new DateOnly(2026, 7, 23), null),
        new(134.7734m, 135.6805m, "resistance", 3.5, 5, new DateOnly(2026, 9, 17), null),
    ];

    [Fact]
    public void ANameWithNoPlanSaysWhyAndWhatWouldBringOneByTheReasonItsLadderStored()
    {
        // The trend reading down, and no support band beneath the price: the nearest band above, 127.98 to 129.98, is the
        // one a close above its top edge would put beneath the price.
        Assert.Equal(
            "A plan forms on a night the trend no longer reads down and a support band sits beneath the price. A close above 129.98 would put the nearest band above, 127.98 to 129.98, beneath the price.",
            NameScreen.PlanForms("the trend is down, so no tranche is placed", PepBands, 125.97m));
        Assert.Equal(
            "A plan forms on a night a support band sits beneath the price. A close above 129.98 would put the nearest band above, 127.98 to 129.98, beneath the price.",
            NameScreen.PlanForms("no support band sits below the price", PepBands, 125.97m));

        // A band of one price is named at its price, and above the second band the third is the nearest.
        Assert.EndsWith(
            "A close above 135.68 would put the nearest band above, 134.77 to 135.68, beneath the price.",
            NameScreen.PlanForms("no support band sits below the price", PepBands, 133m),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "A close above 132.32 would put the nearest band above at 132.32, beneath the price.",
            NameScreen.PlanForms("no support band sits below the price", PepBands, 131m),
            StringComparison.Ordinal);

        // No band above the price: the sentence says what forms a plan and names no close.
        Assert.Equal(
            "A plan forms on a night the trend no longer reads down and a support band sits beneath the price.",
            NameScreen.PlanForms("the trend is down, so no tranche is placed", PepBands, 140m));

        // Each other reason a ladder or the night's builder stores.
        Assert.Equal(
            "A plan forms on a night a band beneath the price holds a swing, a touch, a retracement or a volume shelf and not an average alone.",
            NameScreen.PlanForms("no support band below the price has an anchor other than a moving average", PepBands, 125.97m));
        Assert.Equal("A plan forms once the name holds the sessions its averages need.", NameScreen.PlanForms("no 200-day average", PepBands, 125.97m));
        Assert.Equal("A plan forms once the name holds the sessions its averages need.", NameScreen.PlanForms("no 50-day average", PepBands, 125.97m));
        Assert.Equal("A plan forms once the name's chart holds the swings its trend is read from.", NameScreen.PlanForms("fewer than 2 swing lows", PepBands, 125.97m));
        Assert.Equal(
            "A plan forms on the next night its bars arrive.",
            NameScreen.PlanForms("no bar for this session; the last session stored for the name is 2026-10-06", PepBands, 125.97m));
        Assert.Null(NameScreen.PlanForms("a reason no rule writes", PepBands, 125.97m));

        // The line the page draws in the plan's place: the reason as stored, then what would bring a plan.
        var marks = new MarkRenderer();

        Assert.Equal(
            "<p class=\"degraded no-plan\" data-ticker=\"PEP\" data-rows=\"0\" data-reason=\"the trend is down, so no tranche is placed\">"
            + "PEP has no plan to draw: the trend is down, so no tranche is placed. "
            + "A plan forms on a night the trend no longer reads down and a support band sits beneath the price. A close above 129.98 would put the nearest band above, 127.98 to 129.98, beneath the price.</p>",
            marks.PlanColumn("PEP", 125.97m, [], "the trend is down, so no tranche is placed", NameScreen.PlanForms("the trend is down, so no tranche is placed", PepBands, 125.97m)));

        // A plan placing tranches and no exit draws its entries and a line where the exits' table would stand empty.
        var tables = marks.PlanTables("TEST", [new PlanRow(100m, 102m, PlanKind.Tranche, "buy on the zone", true, "the price reaching the zone", 98m)]);

        Assert.Contains("<table class=\"tranche-table\"", tables, StringComparison.Ordinal);
        Assert.DoesNotContain("<table class=\"exit-table\"", tables, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-exits=\"none\">The plan places no exit.</p>", tables, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-entries=\"none\">The plan places no tranche.</p>", marks.PlanTables("TEST", []), StringComparison.Ordinal);
    }
}
