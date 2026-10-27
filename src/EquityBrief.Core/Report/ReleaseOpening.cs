using System.Text.RegularExpressions;

namespace EquityBrief.Core.Report;

// The opening line of a results release as its text stores it: the exhibit, the day the SEC accepted it, its address and
// the first sentence after its dateline, the city and the day the release is issued from, which is where a release
// says what it reports.
// see: A quarter reported before its filing is stored is labelled as the release's, and every awaited and next report line moves on once it is reported
public sealed partial record ReleaseOpening(string Exhibit, DateOnly AcceptedOn, string Url, string Line)
{
    // The opening line read from a release's text, none where no dateline opens a sentence in it.
    public static ReleaseOpening? Of(string exhibit, DateOnly acceptedOn, string url, string text)
    {
        var plain = Regex.Replace(text, @"\s+", " ");
        var dateline = Dateline().Match(plain);

        if (!dateline.Success)
        {
            return null;
        }

        var rest = plain[dateline.Index..];
        var end = SentenceEnd().Match(rest);
        var line = (end.Success ? rest[..(end.Index + 1)] : rest[..Math.Min(rest.Length, 400)]).Trim();

        return line.Length == 0 ? null : new ReleaseOpening(exhibit, acceptedOn, url, line);
    }

    // A city, an optional state, and a day written with its month's name, as a release's dateline is.
    [GeneratedRegex(@"(?<![A-Za-z])[A-Z][a-z]+\.?(?:[ \-][A-Z][a-z]+){0,3}, (?:[A-Z][a-z]+(?: [A-Z][a-z]+)?, )?(?:January|February|March|April|May|June|July|August|September|October|November|December) \d{1,2}, \d{4}")]
    private static partial Regex Dateline();

    // The first full stop that closes a sentence: one standing alone, an ellipsis's dots none, followed by a space and a
    // capital, or ending the text.
    [GeneratedRegex(@"(?<!\.)\.(?!\.)(?=\s+[A-Z])|\.$")]
    private static partial Regex SentenceEnd();
}
