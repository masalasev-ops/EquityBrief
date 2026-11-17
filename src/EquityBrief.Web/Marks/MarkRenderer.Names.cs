using System.Globalization;

namespace EquityBrief.Web.Marks;

public sealed partial class MarkRenderer
{
    public const string NamesNotRead = "The symbol list has not been read yet, so each member is named as its index's file holds it, in title case where that holds it in capitals.";

    // On the operator's answer of 2026-11-17, item 6, the members of the index the symbol list names none of, each with the
    // name it is drawn by, the name its index's file holds in title case; one line where the list has never been read; none
    // where it names every member.
    // see: A member's company is named as the provider's symbol list spells it, read once a week outside the night
    public static string UnlistedNames(IReadOnlyList<(string Ticker, string? Name)> unlisted, DateOnly? readOn) =>
        readOn is not { } read
            ? $"<section class=\"unlisted-names\" data-unlisted=\"not read\"><h3>Company names</h3><p>{Escaped(NamesNotRead)}</p></section>"
            : unlisted.Count == 0
                ? string.Empty
                : $"<section class=\"unlisted-names\" data-unlisted=\"{unlisted.Count.ToString(CultureInfo.InvariantCulture)}\"><h3>Members the symbol list names none of ({unlisted.Count.ToString(CultureInfo.InvariantCulture)})</h3>"
                    + $"<p>Read on {read.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}. Each is named as its index's file holds it, in title case.</p><ul>"
                    + string.Concat(unlisted.Select(one => $"<li data-ticker=\"{Escaped(one.Ticker)}\">{Escaped(one.Ticker)}: {Escaped(one.Name ?? "no name held")}</li>"))
                    + "</ul></section>";
}
