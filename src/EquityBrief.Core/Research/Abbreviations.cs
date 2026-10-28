using System.Text.RegularExpressions;

namespace EquityBrief.Core.Research;

// The abbreviations results releases write, each with the words it stands for, which a written section spells out the
// first time it uses one: "DEPS" is a release's diluted earnings a share and nothing a reader is told.
// see: A written section is drawn without its bracketed markers, its sources listed by title beneath it and every abbreviation spelled out where first used
public static class Abbreviations
{
    public static IReadOnlyDictionary<string, string> Spelled { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DEPS"] = "diluted earnings a share",
        ["EPS"] = "earnings a share",
        ["EBITDA"] = "earnings before interest, taxes, depreciation and amortization",
        ["EBIT"] = "earnings before interest and taxes",
        ["SG&A"] = "selling, general and administrative expenses",
        ["FCF"] = "free cash flow",
        ["ARR"] = "annual recurring revenue",
        ["NRR"] = "net revenue retention",
        ["GAAP"] = "generally accepted accounting principles",
        ["R&D"] = "research and development",
        ["COGS"] = "cost of goods sold",
        ["CapEx"] = "capital expenditure",
        ["ROIC"] = "return on invested capital",
        ["FFO"] = "funds from operations",
        ["AFFO"] = "adjusted funds from operations",
        ["NII"] = "net interest income",
        ["NIM"] = "net interest margin",
        ["ARPU"] = "average revenue per user",
        ["YoY"] = "year on year",
        ["bps"] = "basis points",
    };

    // The text with each abbreviation spelled out where it is first used, the abbreviation kept in brackets after its
    // words; one already spelled out before its first use, or used inside another word, is left as written.
    public static string SpelledOut(string text)
    {
        foreach (var (abbreviation, words) in Spelled)
        {
            var first = Regex.Match(text, $@"(?<![A-Za-z0-9&]){Regex.Escape(abbreviation)}(?![A-Za-z0-9&])");

            if (!first.Success || text[..first.Index].Contains(words, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            text = text[..first.Index] + words + " (" + abbreviation + ")" + text[(first.Index + first.Length)..];
        }

        return text;
    }
}
