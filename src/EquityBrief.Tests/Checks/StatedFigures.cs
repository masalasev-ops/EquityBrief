using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// A figure section 17 states in digits and what holds it: a value the code carries or
// derives, or a kind no constant holds with the check that kind allows.
internal sealed record HeldFigure(string Row, string Stated, decimal? Holds, string Held, Func<bool>? Stands = null);

// A figure the code holds, and every sentence form a document restates it in, counted
// per document in advance.
internal sealed record Restatement(
    string Figure,
    decimal Holds,
    IReadOnlyList<string> Patterns,
    IReadOnlyDictionary<string, int> Statements);

internal static class StatedFigures
{
    static readonly Regex Digits = new(@"(?<![\w.,-])(?:\d{1,3}(?:,\d{3})+|\d+(?:\.\d+)?)(?![\w]|[.,]\d)");

    static readonly Regex Citation = new($@"\((?:{Corpus.Decision}|{Corpus.Obligation}): [^)]*\)");

    static readonly string[] Words =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    static readonly string[] Ordinals =
        ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth"];

    internal static readonly string Number =
        @"(?<n>\d{1,3}(?:,\d{3})+|\d+(?:\.\d+)?|half|" + string.Join("|", Words) + ")";

    internal static readonly string Ordinal = "(?<n>" + string.Join("|", Ordinals.Skip(1)) + ")";

    // The documents a restatement is read in: the specs, the rules files, and the
    // decisions that stand.
    internal static IReadOnlyList<string> Documents => [.. Corpus.SpecsAndRules, "docs/DECISIONS.md"];

    internal static IReadOnlyList<(string Row, string Stated)> InValues(ArchitectureTable limits) =>
    [
        .. limits.Body
            .Where(cells => cells.Count > 1)
            .SelectMany(cells => Digits.Matches(Citation.Replace(cells[1], string.Empty)).Select(match => (cells[0], match.Value))),
    ];

    internal static decimal ValueOf(string stated)
    {
        if (stated.Length > 0 && char.IsDigit(stated[0]))
        {
            return decimal.Parse(stated.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        if (string.Equals(stated, "half", StringComparison.OrdinalIgnoreCase))
        {
            return 0.5m;
        }

        var word = Array.FindIndex(Words, candidate => string.Equals(candidate, stated, StringComparison.OrdinalIgnoreCase));

        if (word >= 0)
        {
            return word;
        }

        var ordinal = Array.FindIndex(Ordinals, candidate => string.Equals(candidate, stated, StringComparison.OrdinalIgnoreCase));

        return ordinal > 0 ? ordinal : throw new FormatException($"'{stated}' is not a figure this reader reads.");
    }

    // The census against what section 17 states, in order, both directions.
    internal static IReadOnlyList<string> Unheld(IReadOnlyList<(string Row, string Stated)> stated, IReadOnlyList<HeldFigure> census)
    {
        var faults = new List<string>();
        var at = 0;

        for (; at < Math.Min(stated.Count, census.Count); at++)
        {
            var (row, figure) = stated[at];
            var held = census[at];

            if (row != held.Row || figure != held.Stated)
            {
                faults.Add($"Figure {at + 1}: section 17 states {figure} in '{row}' where the census holds {held.Stated} in '{held.Row}'.");
                return faults;
            }

            if (held.Holds is { } holds && ValueOf(figure) != holds)
            {
                faults.Add($"'{row}' states {figure}, and {held.Held} holds {holds.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (held.Holds is null && held.Stands is null)
            {
                faults.Add($"'{row}' states {figure} as {held.Held}, with nothing held and nothing checked.");
            }

            if (held.Stands is { } stands && !stands())
            {
                faults.Add($"'{row}' states {figure} as {held.Held}, and that does not stand.");
            }
        }

        faults.AddRange(stated.Skip(at).Select(figure => $"'{figure.Row}' states {figure.Stated}, which no census entry holds."));
        faults.AddRange(census.Skip(at).Select(held => $"The census holds {held.Stated} in '{held.Row}', which section 17 does not state."));

        return faults;
    }

    // A document as a sentence reader sees it. A decision under "Previously decided" is
    // a record of what was held and is not read.
    internal static string Prose(string path, string text)
    {
        if (path.EndsWith(".html", StringComparison.Ordinal))
        {
            text = Regex.Replace(text, "<[^>]+>", " ");
        }

        if (path == "docs/DECISIONS.md" && text.IndexOf("## Previously decided", StringComparison.Ordinal) is var marker && marker >= 0)
        {
            text = text[..marker];
        }

        text = WebUtility.HtmlDecode(text)
            .Replace("**", " ", StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);

        return Regex.Replace(text, @"\s+", " ");
    }

    internal static IReadOnlyList<string> Disagreeing(
        IReadOnlyDictionary<string, string> documents,
        IReadOnlyList<Restatement> restatements)
    {
        var faults = new List<string>();

        foreach (var restatement in restatements)
        {
            faults.AddRange(restatement.Statements.Keys
                .Where(path => !documents.ContainsKey(path))
                .Select(path => $"{restatement.Figure} is counted in {path}, which is not a document this reads."));

            foreach (var (path, text) in documents)
            {
                var found = restatement.Patterns
                    .SelectMany(pattern => Regex.Matches(
                        text,
                        pattern.Replace("{N}", Number, StringComparison.Ordinal).Replace("{O}", Ordinal, StringComparison.Ordinal),
                        RegexOptions.IgnoreCase))
                    .ToArray();

                var counted = restatement.Statements.GetValueOrDefault(path);

                if (found.Length != counted)
                {
                    faults.Add($"{path} restates {restatement.Figure} {found.Length} time(s), and {counted} are counted in advance.");
                }

                faults.AddRange(found
                    .Where(match => ValueOf(match.Groups["n"].Value) != restatement.Holds)
                    .Select(match => $"{path} restates {restatement.Figure} as '{match.Value}', and the code holds {restatement.Holds.ToString(CultureInfo.InvariantCulture)}."));
            }
        }

        return faults;
    }
}
