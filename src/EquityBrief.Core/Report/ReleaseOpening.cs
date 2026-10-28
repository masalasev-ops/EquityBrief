using System.Text.RegularExpressions;

namespace EquityBrief.Core.Report;

// The opening line of a results release as its text stores it: the exhibit, the day the SEC accepted it, its address and
// the first sentence in which the company, by its name, its ticker or its listing, says what it reported or announced,
// from the company's name to the full stop closing it. The stored text is the exhibit flattened, so a headline in
// capitals runs into its dateline's city with nothing between: the sentence's subject is read after the last full stop,
// bullet, dash, run of capitals or day written with its month's name before its verb, and a present tense, which a
// headline is written in, only where no past tense says it and straight after a dateline's day.
// see: A quarter reported before its filing is stored is labelled as the release's, its awaited and next report lines moving on and its release quoted where it says what it reported
public sealed partial record ReleaseOpening(string Exhibit, DateOnly AcceptedOn, string Url, string Line)
{
    // The text read, how far back from its verb a sentence's subject is looked for, the longest sentence drawn whole and
    // the length a longer one is cut at, and the shortest that is a sentence.
    const int TextRead = 8000;
    const int Reach = 320;
    const int Longest = 600;
    const int CutAt = 400;
    const int Shortest = 30;

    // How near a dateline's day stands before a present tense's subject, and how far back a word making the verb an
    // adjective is read.
    const int DayBefore = 16;
    const int AdjectiveReach = 12;

    // The characters a release's text sets apart with, each written as the pattern's escape: the bullets, the en and em
    // dashes, an opening quotation mark, and the characters that take no space.
    const string Bullets = "\\u2022\\u25AA\\u25CF\\u25E6\\u00B7\\u2023\\u2043\\u25A0\\u25A1\\u25C6\\u25BA\\u27A2\\u2713\\u25CB";
    const string Dashes = "\\u2013\\u2014";
    const string OpeningQuote = "\\u201C";
    const string NoSpace = "\\u200B\\u200C\\u200D\\uFEFF\\u00AD";

    // The abbreviations a full stop ends without ending a sentence: a single capital, a company's form, a month and a
    // state.
    const string Abbreviation = @"(?<!\b(?:[A-Z]|Inc|Co|Corp|Ltd|No|vs|St|Mr|Ms|Mrs|Dr|Jr|Sr|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec|Calif|Ill|Va|Fla|Mass|Conn|Pa|Penn|Wash|Ariz|Colo|Ga|Md|Mich|Minn|Mo|Neb|Nev|Okla|Ore|Tenn|Tex|Wis|Ky|La|Miss|Ala|Ind|Kan|approx))";

    const string Month = "(?:(?:January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\\.?|JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER)";

    // The words of a company's name that are none of its own.
    static readonly HashSet<string> NotTheName = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "corp", "corporation", "company", "co", "companies", "plc", "ltd", "limited", "holdings", "holding",
        "group", "nv", "sa", "se", "ag", "llc", "lp", "trust", "the", "class", "a", "b", "c",
    };

    // The opening line read from a release's text, none where no sentence in it says what the company reported.
    public static ReleaseOpening? Of(string exhibit, DateOnly acceptedOn, string url, string text, string company, string ticker)
    {
        var plain = Spaces().Replace(Invisible().Replace(text, string.Empty), " ");
        plain = plain[..Math.Min(plain.Length, TextRead)];

        var mentions = Mentions(company, ticker);
        var line = Said(plain, mentions, present: false) ?? Said(plain, mentions, present: true);

        return line is null ? null : new ReleaseOpening(exhibit, acceptedOn, url, line);
    }

    // Each way the release names the company, and whether it is the listing, which follows the name it opens on.
    static IReadOnlyList<(Regex Pattern, bool Listing)> Mentions(string company, string ticker)
    {
        var listed = Regex.Escape(ticker.Replace('-', '.'));
        var mentions = new List<(Regex, bool)>
        {
            (new Regex(@"[(\[][^()\[\]]{0,60}?\b(?:NYSE|Nasdaq|NASDAQ|NasdaqGS|Cboe|CBOE)\b[^()\[\]]{0,40}?\b" + listed + @"\b[^()\[\]]{0,40}[)\]]"), true),
        };

        var name = company.Split([' ', ',', '\x00A0'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(word => !NotTheName.Contains(new string(word.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())));

        if (name is { Length: >= 2 })
        {
            mentions.Add((new Regex(@"(?<![A-Za-z0-9])" + Regex.Escape(name) + @"(?![a-z])", RegexOptions.IgnoreCase), false));
        }

        if (ticker.Length >= 2)
        {
            mentions.Add((new Regex(@"(?<![A-Za-z0-9])" + listed + @"(?![A-Za-z0-9])"), false));
        }

        return mentions;
    }

    // The first sentence whose verb, in the tense asked for, has the company as its subject.
    static string? Said(string text, IReadOnlyList<(Regex Pattern, bool Listing)> mentions, bool present)
    {
        foreach (Match verb in Verb().Matches(text))
        {
            if ((verb.Value is "announces" or "reports") != present || Adjective().IsMatch(text[Math.Max(0, verb.Index - AdjectiveReach)..verb.Index]))
            {
                continue;
            }

            var before = text[..verb.Index];
            var from = Math.Max(0, verb.Index - Reach);
            var after = Separator().Matches(before, from).LastOrDefault() is { } cut ? cut.Index + cut.Length : from;

            // A present tense says what a headline says, so it opens a sentence only straight after a dateline's day.
            if (present && !Day().Matches(before[..after], from).Any(day => day.Index + day.Length >= after - DayBefore))
            {
                continue;
            }

            var subject = mentions
                .Select(mention => (Found: mention.Pattern.Match(before, after), mention.Listing))
                .Where(mention => mention.Found.Success)
                .OrderBy(mention => mention.Found.Index)
                .FirstOrDefault();

            if (subject.Found is null)
            {
                continue;
            }

            var start = subject.Listing ? after : subject.Found.Index;
            var end = SentenceEnd().Match(text, verb.Index + verb.Length);
            var line = (end.Success && end.Index + 1 - start <= Longest ? text[start..(end.Index + 1)] : text[start..Math.Min(text.Length, start + CutAt)])
                .Trim(' ', '\x2013', '\x2014', '-', ',');

            // A sentence running on from a headline, its bullets or a run of capitalised words before the verb, is not
            // the release's own.
            if (line.Length < Shortest || RunOn().IsMatch(text[start..verb.Index]))
            {
                continue;
            }

            return line;
        }

        return null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("[" + NoSpace + "]")]
    private static partial Regex Invisible();

    // What a release says it did, the past tense first and the present a headline uses after it.
    [GeneratedRegex(@"\b(?:today announced|today reported|announced today|reported today|today posted|today released|announced|reported|posted|recorded|announces|reports)\b")]
    private static partial Regex Verb();

    // A word that makes the verb an adjective, as "on a reported basis" does.
    [GeneratedRegex(@"\b(?:a|an|the|both|its|their|on|as|of|and|previously)\s+$", RegexOptions.IgnoreCase)]
    private static partial Regex Adjective();

    // What closes the text before a sentence's subject: a full stop that ends a sentence, a run of capitals as a headline
    // or a city with no day is written, a bullet, a dash standing between words, or a day written with its month's name.
    [GeneratedRegex(Abbreviation + @"(?<!\.)\.(?!\.)\s+(?=[A-Z0-9" + OpeningQuote + @"""(])"
        + @"|\b[A-Z][A-Z'&.-]+(?:\s+[A-Z][A-Z'&.-]+)+,?(?:\s+(?:[A-Z]\.){1,3}|\s+[A-Z][a-z]{1,5}\.)?(?=\s)"
        + "|[" + Bullets + "]"
        + @"|\s[" + Dashes + @"]\s?|\s?[" + Dashes + @"]\s|\s-{1,2}\s|-{2,}"
        + @"|\(?\b" + Month + @" \d{1,2}, \d{4}\)?")]
    private static partial Regex Separator();

    // A day written with its month's name, as a dateline's is.
    [GeneratedRegex(@"\b" + Month + @" \d{1,2}, \d{4}")]
    private static partial Regex Day();

    // The full stop that closes a sentence: one standing alone and after no abbreviation, an ellipsis's dots none,
    // followed by a space and a capital, a figure, a quotation, a bracket or a bullet, or ending the text.
    [GeneratedRegex(Abbreviation + @"(?<!\.)\.(?!\.)(?=\s+[A-Z0-9" + OpeningQuote + @"""(" + Bullets + @"]|\s*$)")]
    private static partial Regex SentenceEnd();

    // A headline's bullets, a dash opening a highlight, or seven capitalised words in a row before a verb.
    [GeneratedRegex("[" + Bullets + @"]|\s-[A-Z]|(?:\b[A-Z][a-z]+\s+){7,}")]
    private static partial Regex RunOn();
}
