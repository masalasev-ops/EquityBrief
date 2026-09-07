using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

internal sealed record ClockUse(string File, int Line, string Text);

// Finds every read of the machine clock and every expression of local time.
//
// The patterns are assembled from parts so this file never contains the strings
// it bans. An exemption for the checker's own source would otherwise have to be
// written down somewhere and remembered by everyone who later moves the file.
internal static class MachineClock
{
    static readonly string[] Patterns =
    [
        "DateTime" + @"\.\s*(Now|UtcNow|Today)\b",
        "DateTimeOffset" + @"\.\s*(Now|UtcNow)\b",
        "TimeProvider" + @"\.\s*System\b",
        "TimeZoneInfo" + @"\.\s*Local\b",
        "DateTimeKind" + @"\.\s*Local\b",
        @"\.\s*To" + "LocalTime" + @"\s*\(",
    ];

    internal static IReadOnlyList<ClockUse> In(string source, string file = "")
    {
        var uses = new List<ClockUse>();
        var lines = source.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var pattern in Patterns)
            {
                var match = Regex.Match(lines[index], pattern);

                if (match.Success)
                {
                    uses.Add(new ClockUse(file, index + 1, match.Value));
                }
            }
        }

        return uses;
    }
}
