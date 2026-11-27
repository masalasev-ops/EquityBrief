using System.Globalization;
using Wording = EquityBrief.Core.Families.RuleWording.WaitingVariants;

namespace EquityBrief.Web.Marks;

// A background variant entered to wait for a slot: its name, the live rule it moves from, the session its live rule's orders
// are walked after, whether it has registered since, and its record over the orders that ended beside the live rule's.
public sealed record WaitingVariantRow(
    string Candidate,
    string Live,
    DateOnly WalkedFrom,
    bool Registered,
    int Ended,
    int UnderFloor,
    int Filled,
    double? Result,
    int LiveFilled,
    double? LiveResult);

public sealed partial class MarkRenderer
{
    // On the operator's answers of 2026-11-27, item 2, the background variants a full family has no room for, each with its
    // record so far; none where no variant was ever entered.
    // see: A background variant a full family has no room for is walked each night from its live rule's own orders and registers in the first slot that frees
    public static string WaitingVariants(IReadOnlyList<WaitingVariantRow> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var waiting = rows.Count(row => !row.Registered);

        return $"<section class=\"waiting-variants\" data-waiting=\"{waiting.ToString(CultureInfo.InvariantCulture)}\"><h3>{Wording.Heading}</h3>"
            + $"<p>{Wording.Lead}</p><ol>"
            + string.Concat(rows.Select(row =>
                $"<li data-candidate=\"{Escaped(row.Candidate)}\" data-registered=\"{(row.Registered ? "1" : "0")}\"><b>{Escaped(row.Candidate)}</b>"
                + $"<br>{Escaped(Wording.WalkedFrom(row.WalkedFrom))}{(row.Registered ? "; " + Wording.Registered : string.Empty)}"
                + $"<br>{Escaped(Wording.Line(row.Ended, row.UnderFloor, row.Filled, row.Result, row.LiveFilled, row.LiveResult))}</li>"))
            + $"</ol><p class=\"note\">{Wording.Unlike}</p></section>";
    }
}
