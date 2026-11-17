using System.Globalization;

namespace EquityBrief.Web.Marks;

// A step in a stock's closes the share check reads as no split, a split table holding none within days of it, its step
// the close as traded over the close at the store's scale against the session before's; or, where the step is none, a
// split a table holds whose closes show no step.
public sealed record SplitStepRow(string Ticker, DateOnly Day, decimal? Step);

public sealed partial class MarkRenderer
{
    // On the operator's answer of 2026-11-17, the steps the share check reads as no split and the splits it reads as none,
    // each with its stock and date; none where there are none.
    // see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
    public static string SplitSteps(IReadOnlyList<SplitStepRow> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var unheld = rows.Where(row => row.Step is not null).ToArray();
        var unseen = rows.Where(row => row.Step is null).ToArray();

        static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        return $"<section class=\"split-steps\" data-unheld=\"{unheld.Length.ToString(CultureInfo.InvariantCulture)}\" data-unseen=\"{unseen.Length.ToString(CultureInfo.InvariantCulture)}\"><h3>Splits the share check does not read</h3>"
            + (unheld.Length == 0 ? string.Empty
                : $"<p>A jump in the price adjustment with no split in the split tables is not read as a split ({unheld.Length.ToString(CultureInfo.InvariantCulture)}):</p><ul>"
                    + string.Concat(unheld.Select(row => $"<li data-ticker=\"{Escaped(row.Ticker)}\">{Escaped(row.Ticker)}: {Day(row.Day)}, a step of {row.Step!.Value.ToString("0.###", CultureInfo.InvariantCulture)}</li>"))
                    + "</ul>")
            + (unseen.Length == 0 ? string.Empty
                : $"<p>A split in the split tables with no matching jump in the prices is not read either ({unseen.Length.ToString(CultureInfo.InvariantCulture)}):</p><ul>"
                    + string.Concat(unseen.Select(row => $"<li data-ticker=\"{Escaped(row.Ticker)}\">{Escaped(row.Ticker)}: {Day(row.Day)}</li>"))
                    + "</ul>")
            + "</section>";
    }
}
