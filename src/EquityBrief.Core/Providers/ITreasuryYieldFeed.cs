using System.Globalization;

namespace EquityBrief.Core.Providers;

// One session's 10-year par yield as the Treasury publishes it, in per cent.
public sealed record TreasuryYield(DateOnly Session, double TenYear);

// The Treasury's daily par yield curve for one calendar year, one request whatever the year holds, free and keyless and
// not the provider's, so it counts against no allowance.
// see: The Treasury's 10-year par yield is read once a night after the close and kept a session a row
public interface ITreasuryYieldFeed
{
    Task<IReadOnlyList<TreasuryYield>> YearAsync(int year, CancellationToken cancellation = default);

    int Requests { get; }
}

// The Treasury's table as it sends it: a header naming each tenor in quotes, the 10-year under "10 Yr", then a session a
// line, its date written month first with slashes. An empty answer is a year with no session published yet, and a
// session whose 10-year cell is empty is left out; an answer whose first line names no date and no 10-year column, or a
// line whose date or yield cannot be read, is refused.
public static class TreasuryYields
{
    public const string TenYearColumn = "10 Yr";

    public static IReadOnlyList<TreasuryYield> Parse(string csv)
    {
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            return [];
        }

        var header = Cells(lines[0]);
        var column = Array.IndexOf(header, TenYearColumn);

        if (header.Length == 0 || header[0] != "Date" || column < 0)
        {
            throw new FormatException(
                "The Treasury's answer opens on no header naming a date and a 10-year column, so it is not the par yield table.");
        }

        var yields = new List<TreasuryYield>();

        foreach (var line in lines.Skip(1))
        {
            var cells = Cells(line);

            if (!DateOnly.TryParseExact(cells[0], "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session))
            {
                throw new FormatException($"A line of the Treasury's table carries no date written month first: '{line}'.");
            }

            if (column >= cells.Length || cells[column].Length == 0)
            {
                continue;
            }

            yields.Add(double.TryParse(cells[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var tenYear)
                ? new TreasuryYield(session, tenYear)
                : throw new FormatException(FormattableString.Invariant($"The Treasury's 10-year for {session:yyyy-MM-dd} reads '{cells[column]}', which is no figure.")));
        }

        return [.. yields.OrderBy(yield => yield.Session)];
    }

    // A line's cells, each with the quotes the header wraps its tenors in taken off. No cell of the table holds a comma.
    static string[] Cells(string line) => [.. line.Split(',').Select(cell => cell.Trim().Trim('"'))];
}
