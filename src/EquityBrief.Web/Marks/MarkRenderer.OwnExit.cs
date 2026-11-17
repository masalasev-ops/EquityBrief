using System.Text;

namespace EquityBrief.Web.Marks;

// One stock the own exit sold since its walk began: the stock, the close it sold at and why.
public sealed record OwnExitSoldCell(string Ticker, DateOnly On, string Reason);

// A book's own exit beside its live book, a record only: the night its walk began, the stocks it holds tonight, what it sold
// since, and the mean change of its positions since then, the open ones at tonight's close, beside the live book's over its
// own positions on the same nights, each with how many positions it counts.
public sealed record OwnExitView(
    DateOnly Since,
    string Turn,
    IReadOnlyList<string> Held,
    IReadOnlyList<OwnExitSoldCell> Sold,
    double? OwnMean,
    int OwnCounted,
    double? LiveMean,
    int LiveCounted);

// The books' own exit as a comparison record, drawn as a fold on each book's card: it says it is a record that draws no pick
// and cannot go live as it stands, and that it is brought to the operator if it leads after the book's 12 months.
// see: Each book's own exit is walked beside its live book as a record on its card, and registers no version and draws no pick
public sealed partial class MarkRenderer
{
    public const string OwnExitHeading = "Its own exit, a record only";

    public static string OwnExitFold(OwnExitView? view)
    {
        if (view is null)
        {
            return string.Empty;
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<details class=\"fold own-exit\" data-own-held=\"{view.Held.Count}\" data-own-sold=\"{view.Sold.Count}\" data-own-mean=\"{Figure(view.OwnMean)}\" data-live-mean=\"{Figure(view.LiveMean)}\"><summary>{OwnExitHeading}</summary><div class=\"fold-b\">");
        body.Append(Invariant, $"<p class=\"record-said\">The rule as it sold before the rules restart: it sells only {Escaped(view.Turn)}, with no stop and no take-profit. It is walked each night from what the rule stored, draws no pick and cannot go live as it stands; if it leads after its first 12 months live, it is brought to you.</p>");
        body.Append(Invariant, $"<p class=\"record-said own-exit-since\">Since {view.Since:d MMM yyyy} its {view.OwnCounted} position(s) moved {Moved(view.OwnMean)} on average, the open ones at tonight's close, against {Moved(view.LiveMean)} over the live rule's {view.LiveCounted}.</p>");
        body.Append(view.Held.Count == 0
            ? "<p class=\"record-said own-exit-held\">It holds nothing tonight.</p>"
            : FormattableString.Invariant($"<p class=\"record-said own-exit-held\">It holds {view.Held.Count} tonight: {Escaped(string.Join(", ", view.Held))}.</p>"));

        foreach (var sold in view.Sold)
        {
            body.Append(Invariant, $"<p class=\"record-said own-exit-sold\" data-ticker=\"{Escaped(sold.Ticker)}\" data-sold-on=\"{DayOf(sold.On)}\">It sold {Escaped(sold.Ticker)} at the close of {sold.On:d MMM yyyy}: {Escaped(sold.Reason)}.</p>");
        }

        return body.Append("</div></details>").ToString();

        static string Figure(double? change) => change is { } value ? value.ToString("R", Invariant) : "none";

        static string Moved(double? change) => change is { } value
            ? SignedFigure(value * 100, 1, Formatted($"{Unsigned(value * 100, 1):+0.0;-0.0;0.0}%"))
            : "<span class=\"degraded\">not yet measured</span>";
    }
}
