using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Facts;

// How a segment fact names the period it covers: a quarter by its end alone, and a
// longer period by its months and its end. Held here because the assembler in the
// worker writes the name and the prompt and the claim checker in this project read it.
// see: A segment figure held for a period longer than a quarter is asked for by that period and refused where its sentence names a period of another length
public static class SegmentPeriods
{
    public const int QuarterMonths = 3;

    public const string Prefix = "segment ";

    public static string Name(int months, string ended) =>
        months == QuarterMonths ? ended : $"{months.ToString(CultureInfo.InvariantCulture)} months to {ended}";

    static readonly Regex Longer = new(
        "^" + Regex.Escape(Prefix) + @".+ (?<months>\d+) months to (?<ended>\d{4}-\d{2}-\d{2})$",
        RegexOptions.Compiled);

    // The period a segment fact covers where it is longer than a quarter, or null.
    public static (int Months, string Ended)? LongerThanAQuarter(Fact fact)
    {
        var named = Longer.Match(fact.Name);

        if (!named.Success
            || !int.TryParse(named.Groups["months"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var months)
            || months <= QuarterMonths)
        {
            return null;
        }

        return (months, named.Groups["ended"].Value);
    }

    // The period every segment figure in a facts file covers where it is longer than a
    // quarter, or null where the file carries a quarter or no segment figure.
    public static (int Months, string Ended)? LongerPeriodIn(IReadOnlyList<Fact> facts) =>
        facts.Select(LongerThanAQuarter).FirstOrDefault(period => period is not null);
}
