using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.EntryCheck;

// A one-off item a results release names: the sentence, the amount a share, whether the sentence ties it to earnings per share, and
// which way it moved them, none where the sentence does not say.
public sealed record OneOffItem(string Sentence, decimal Amount, bool TiedToEarnings, int? Direction);

// The one-off reading of a stored results release, by fixed phrases and code alone, so it can be tested over every stored year. A
// sentence counts where it holds one of the order's phrases and an amount a share beside it, and speaks of the quarter reported.
// see: The entry check's one-off reading reads a stored release by fixed phrases, and says it can miss an item
public static partial class OneOffItems
{
    // A longer run than this between two stops is a table the text flattened, and no sentence.
    public const int LongestSentence = 400;

    // More amounts than this in one sentence are the rows of a table.
    public const int MostAmounts = 4;

    // How far from the phrase an amount a share may stand and still be the phrase's own.
    public const int FarthestFromThePhrase = 90;

    // The largest item a release names, none where no sentence of it counts.
    public static OneOffItem? Of(string text)
    {
        OneOffItem? largest = null;

        foreach (var sentence in Sentences(text))
        {
            if (Read(sentence) is { } item && (largest is null || item.Amount > largest.Amount))
            {
                largest = item;
            }
        }

        return largest;
    }

    public static IReadOnlyList<string> Sentences(string text) =>
        [.. SentenceEnd().Split(text).Select(part => string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).Where(part => part.Length > 0)];

    public static OneOffItem? Read(string sentence)
    {
        if (sentence.Length > LongestSentence || NotThisQuarter().IsMatch(sentence) || !PerShareWords().IsMatch(sentence) || Dollar().Count(sentence) > MostAmounts || TwoPlaces().Count(sentence) > MostAmounts - 1)
        {
            return null;
        }

        var phrases = Phrase().Matches(sentence);

        if (phrases.Count == 0)
        {
            return null;
        }

        // The amount a share nearest a phrase, with no total named between the two: "net income was $9.60 a share" is a total.
        decimal? amount = null;
        var nearest = int.MaxValue;
        Match? owner = null;

        foreach (Match found in Amount().Matches(sentence))
        {
            if (Million().IsMatch(sentence[(found.Index + found.Length)..]) || TotalBefore().IsMatch(sentence[..found.Index]) || TotalAfter().IsMatch(sentence[(found.Index + found.Length)..]))
            {
                continue;
            }

            foreach (Match phrase in phrases)
            {
                var (from, to) = phrase.Index < found.Index ? (phrase.Index + phrase.Length, found.Index) : (found.Index + found.Length, phrase.Index);
                var gap = to - from;

                if (gap <= FarthestFromThePhrase && gap < nearest && !TotalBetween().IsMatch(sentence[from..to]) && decimal.TryParse(found.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var read) && read > 0)
                {
                    (amount, nearest, owner) = (read, gap, phrase);
                }
            }
        }

        return amount is { } worth
            ? new OneOffItem(sentence, worth, EarningsWords().IsMatch(sentence), DirectionOf(owner!.Value, sentence))
            : null;
    }

    // Which way the item moved earnings, from the phrase and failing it from the words of the sentence; none where they do not say.
    static int? DirectionOf(string phrase, string sentence)
    {
        if (Helped().IsMatch(phrase))
        {
            return 1;
        }

        if (Hurt().IsMatch(phrase))
        {
            return -1;
        }

        var helped = Helped().IsMatch(sentence);
        var hurt = Hurt().IsMatch(sentence);

        return helped == hurt ? null : helped ? 1 : -1;
    }

    [GeneratedRegex(@"(?<=[a-z0-9\)%\*][.!?])\s+(?=[A-Z\(\$])|\s[^\x00-\x7F\p{L}\p{N}]+\s+(?=[A-Z\$\(])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"benefit of approximately|charge of approximately|one-time|non-recurring|refunds?\b|\bgains? on\b|\bloss(?:es)? on\b|impairments?\b|restructuring", RegexOptions.IgnoreCase)]
    private static partial Regex Phrase();

    [GeneratedRegex(@"\$\s?(\d*\.\d{2})(?!\d)")]
    private static partial Regex Amount();

    [GeneratedRegex(@"^\s*(?:million|billion|thousand)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Million();

    [GeneratedRegex(@"\bper\s+(?:\w+\s+){0,2}share\b|\ba\s+share\b|\bper-share\b|\bEPS\b|\bearnings\s+per\b", RegexOptions.IgnoreCase)]
    private static partial Regex PerShareWords();

    [GeneratedRegex(@"\bEPS\b|\bearnings\s+per\b|\bper\s+diluted\s+share\b", RegexOptions.IgnoreCase)]
    private static partial Regex EarningsWords();

    [GeneratedRegex(@"\bprior[- ]year\b|\bprior[- ]period\b|\byear[- ]ago\b|\blast year\b|\bsame period\b|\bsame quarter\b|\bguidance\b|\boutlook\b|\bestimates?\b|\bexclud(?:e|es|ed|ing)\b|\bcompare[sd]?\b|\b(?:six|nine|twelve)\s+months\b|\bfull[- ]year\b|\byear ended\b|\bfiscal years?\b|\bpast\s+\w+\s+years\b|\bafter adjusting\b|\bdividend\b|\bforecast|\bexpects?\b|\bexcept\s+(?:per\s+)?share|\bin\s+(?:thousands|millions)\b|\bunaudited\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotThisQuarter();

    // A total the amount closes: earnings a share named just before it, or a dollar total of net income with its "or".
    [GeneratedRegex(@"(?:\b(?:net income|net earnings|net loss|income|earnings|loss)\b(?:(?!benefit|charge|gain|impact|impairment)[^$;]){0,60}\$[\d.,]+\s*(?:million|billion)[^$;]{0,20}\bor\s*\(?\s*|\b(?:loss|earnings)\s+per\s+(?:\w+\s+)?share\b[^$;]{0,50}\b(?:was|were|of|totaled)\s*\(?\s*|\bEPS\*?\s*(?:\(\d+\)\s*)?(?:was|were|of)\s*\(?\s*|\bper\s+(?:\w+\s+)?share\s*(?:\(\d+\)\s*)?|\$\s?\(?[\d.,]+\)?\s*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TotalBefore();

    // An amount named as a stated earnings a share: "the $0.97 EPS".
    [GeneratedRegex(@"^\s*(?:GAAP\s+|adjusted\s+)?EPS\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalAfter();

    [GeneratedRegex(@"\$\s?\.?\d")]
    private static partial Regex Dollar();

    [GeneratedRegex(@"(?<![\d,.])\d*\.\d{2}(?![\d%])")]
    private static partial Regex TwoPlaces();

    [GeneratedRegex(@"\bnet income\b|\bnet earnings\b|\bnet loss\b|\b(?:was|were|totaled)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TotalBetween();

    [GeneratedRegex(@"benefit|refund|\bgain|favou?rable", RegexOptions.IgnoreCase)]
    private static partial Regex Helped();

    [GeneratedRegex(@"charge|\bloss|impairment|restructuring|unfavou?rable", RegexOptions.IgnoreCase)]
    private static partial Regex Hurt();
}
