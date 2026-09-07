using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Harness;

// Section 14's ordered list, read as claims.
//
// The component catalogue names sections 7, 14, 15, 16 and 18 as what the
// harness reports on, and 14 carries a list rather than a table. Reading the
// list is the fix; narrowing the stated scope to the sections that happen to
// use tables would have been the harness deciding what it is responsible for.
internal static class NightlyRunSteps
{
    internal const string Heading = "14. The nightly run, in order";

    internal static IReadOnlyList<string> In(string document)
    {
        var start = document.IndexOf(Heading, StringComparison.Ordinal);
        var end = document.IndexOf("15. The screens", StringComparison.Ordinal);

        if (start < 0 || end < 0 || end < start)
        {
            throw new InvalidOperationException(
                "Section 14 was not found between its own heading and section 15. Reporting no " +
                "nightly steps from a document that could not be parsed is the failure this " +
                "guard exists to prevent.");
        }

        var steps = Regex
            .Matches(document[start..end], @"<li[^>]*>(.*?)</li>", RegexOptions.Singleline)
            .Select(match => Regex.Replace(
                WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty)),
                @"\s+",
                " ").Trim())
            .Where(step => step.Length > 0)
            .ToArray();

        if (steps.Length == 0)
        {
            throw new InvalidOperationException(
                "Section 14 carries no ordered list. It is named as a claim source, so an empty " +
                "read is a parse failure rather than a section with nothing in it.");
        }

        return steps;
    }
}
