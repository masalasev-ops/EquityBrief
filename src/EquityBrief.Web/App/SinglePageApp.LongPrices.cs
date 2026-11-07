using System.Text;

namespace EquityBrief.Web.App;

// What the History page draws of the long history's prices: the run that wrote them and when, the codes asked and priced, the
// sessions, the ends read from filings and those marked, the day the S&P 500 reads the provider's dates from and how far its count
// stood from IVV's, the lowest coverage over the spans, the codes priced by nothing, and the ends no filing settles.
public sealed record LongPricesView(
    string? RunId,
    string? EndedAt,
    int Codes,
    int Priced,
    long Sessions,
    int Ends,
    int Marked,
    string? Cutover,
    int ApartAtMost,
    IReadOnlyList<string> Lowest,
    IReadOnlyList<string> Nothing,
    IReadOnlyList<string> Unsettled);

// The History page's prices back to 2004.
// see: The long history's prices are asked once a code from 2004-01-02 and kept apart from the bars, the pull's tables and every night
// see: A stock's history ends by rule on its company's last regular session read from its Form 25 or the 8-K completing its merger
public sealed partial class SinglePageApp
{
    public const string LongPricesLede =
        "Every stock ever a member, priced from 2004 so 2005 reads a year behind it. A company that left ends on its last regular session as its filings give it; where no filing settles it, it ends on its last session with volume and goes to the operator.";

    string LongPricesCard(LongPricesView view)
    {
        if (view.RunId is null)
        {
            return Cards.Computed("Prices", "<p class=\"empty\">The prices back to 2004 have not been written. <code>long-prices --write</code> writes them from a capture.</p>", title: "Prices back to 2004", lede: LongPricesLede, region: "long-prices");
        }

        var body = new StringBuilder();

        body.Append(Invariant($"<p class=\"read\" data-read=\"long-prices\">{view.Priced:N0} of {view.Codes:N0} codes priced, {view.Sessions:N0} sessions. {view.Ends:N0} ends read from filings, {view.Marked:N0} marked where the provider's prices run on past them. The S&P 500 reads the provider's dates from {Escaped(view.Cutover ?? string.Empty)}, its count at most {view.ApartAtMost:N0} from IVV's.</p>"));

        if (view.Lowest.Count > 0)
        {
            body.Append("<p class=\"read\">Lowest coverage over the spans: " + string.Join("; ", view.Lowest.Select(Escaped)) + ".</p>");
        }

        if (view.Nothing.Count > 0)
        {
            body.Append(Invariant($"<details data-list=\"nothing\"><summary>Priced by nothing, left out: {view.Nothing.Count:N0}</summary><p class=\"named\">{string.Join("; ", view.Nothing.Select(Escaped))}</p></details>"));
        }

        body.Append(Invariant($"<details data-list=\"unsettled\"><summary>Ends no filing settles, to the operator: {view.Unsettled.Count:N0}</summary>"));

        if (view.Unsettled.Count > 0)
        {
            body.Append("<p class=\"named\">" + string.Join("; ", view.Unsettled.Select(Escaped)) + "</p>");
        }

        body.Append("</details>");

        return Cards.Computed(
            "Prices",
            body.ToString(),
            title: "Prices back to 2004",
            lede: LongPricesLede,
            stamp: $"<span class=\"run\">{Escaped(view.RunId)}</span>",
            region: "long-prices");
    }
}
