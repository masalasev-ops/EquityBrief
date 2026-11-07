using System.Text;
using EquityBrief.Core.History;

namespace EquityBrief.Web.App;

// One index's year of the long history's membership: its quarter ends read, the holdings at them, those matched to a code,
// those matched by hand, those left out, and the left-out value by why the companies left against the value held.
public sealed record LongMembershipYear(
    string Index,
    int Year,
    int QuarterEnds,
    int Holdings,
    int Matched,
    int ByHand,
    int LeftOut,
    decimal Value,
    IReadOnlyDictionary<string, decimal> LeftOutValue);

// A company left out by name and how every figure leaving it out names it.
public sealed record LongNamedLeftOut(string Index, string Name, string NamedAs);

// What the History page draws of the long history's membership: the run that wrote it, its years and the companies left out
// by name.
public sealed record LongMembershipView(string? RunId, IReadOnlyList<LongMembershipYear> Years, IReadOnlyList<LongNamedLeftOut> Named);

// The History page's membership back to 2005: each index's members by year off the funds' older schedules, matched, matched
// by hand and left out, the S&P 400's and 600's years to 2008 labelled as lower coverage.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
// see: The S&P 400's and 600's figures for 2004 to 2008 carry the lower coverage label
// see: The holdings the long history leaves out are split by why they left with their share of value each year
public sealed partial class SinglePageApp
{
    public const string LongMembershipLede =
        "Each index's members as its fund filed them at each quarter end from 2004 to 2018, matched to the provider's codes, matched by hand where no rule reached them, and left out where no price was found. Left out is split by why the company left, since companies bought out leaving makes a result read slightly worse and failures leaving makes it read better.";

    static readonly (string Index, string Name)[] LongIndices = [("GSPC", "S&P 500"), ("MID", "S&P 400"), ("SML", "S&P 600")];

    string LongMembershipCard(LongMembershipView view)
    {
        if (view.RunId is null)
        {
            return Cards.Computed("Membership", "<p class=\"empty\">The membership back to 2005 has not been written. <code>long-members --write</code> writes it from the funds' older filings.</p>", title: "Members back to 2005", lede: LongMembershipLede, region: "long-membership");
        }

        var body = new StringBuilder();

        foreach (var (index, named) in LongIndices)
        {
            var years = view.Years.Where(year => year.Index == index).OrderBy(year => year.Year).ToArray();

            if (years.Length == 0)
            {
                continue;
            }

            body.Append(Invariant($"<h3 class=\"long-index\">{Escaped(named)}</h3>"));
            body.Append(Invariant($"<div class=\"tbl-wrap\"><table class=\"history-table\" data-table=\"long-membership\" data-index=\"{index}\"><thead><tr><th>Year</th><th class=\"r\">Quarter ends</th><th class=\"r\">Holdings</th><th class=\"r\">Matched</th><th class=\"r\">By hand</th><th class=\"r\">Left out</th>"));
            body.Append(string.Concat(LongLeaving.Kinds.Select(kind => $"<th class=\"r\">{Escaped(Capital(kind))}</th>")));
            body.Append("</tr></thead><tbody>");

            foreach (var year in years)
            {
                var lower = LongFloors.IsLower(index, new DateOnly(year.Year, 12, 31));

                body.Append(Invariant($"<tr data-year=\"{year.Year}\"{(lower ? " data-lower=\"1\"" : string.Empty)}><td>{year.Year}{(lower ? $" <span class=\"lower\">{Escaped(LongFloors.LowerLabel)}</span>" : string.Empty)}</td>"));
                body.Append(Invariant($"<td class=\"n\">{year.QuarterEnds:N0}</td><td class=\"n\">{year.Holdings:N0}</td><td class=\"n\">{year.Matched:N0}</td><td class=\"n\">{year.ByHand:N0}</td><td class=\"n\">{year.LeftOut:N0}</td>"));
                body.Append(string.Concat(LongLeaving.Kinds.Select(kind => Invariant($"<td class=\"n\">{(year.Value == 0m ? 0m : year.LeftOutValue.GetValueOrDefault(kind) / year.Value):P2}</td>"))));
                body.Append("</tr>");
            }

            body.Append("</tbody></table></div>");
        }

        if (view.Named.Count > 0)
        {
            body.Append("<p class=\"named\">Left out by name: ");
            body.Append(string.Join("; ", view.Named.Select(company => $"{Escaped(company.Name)}, {Escaped(LongIndices.First(pair => pair.Index == company.Index).Name)}, {Escaped(company.NamedAs)}")));
            body.Append(".</p>");
        }

        body.Append("<p class=\"read\">Holdings count each holding at each quarter end of the year. The kinds of left out are shares of the value held at the year's quarter ends.</p>");

        return Cards.Computed(
            "Membership",
            body.ToString(),
            title: "Members back to 2005",
            lede: LongMembershipLede,
            stamp: $"<span class=\"run\">{Escaped(view.RunId)}</span>",
            region: "long-membership");
    }
}
