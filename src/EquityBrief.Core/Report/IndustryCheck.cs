using System.Text.RegularExpressions;

namespace EquityBrief.Core.Report;

// Whether a stock's industry group names the business its page's own text describes: the business word the text uses most
// among a stated list, against the words an industry carrying that business names it by. A group naming none of them is
// a mismatch, which the page flags beside the group, the group being the provider's classification and an operator's
// override standing in its place where one is set.
// see: A stock's industry group is the provider's until an override is set and is checked against the page's own text, and the industry cycle reads only sources naming the company or its industry
public static class IndustryCheck
{
    // Each business word a company's description uses, with the words an industry group carrying that business is named by.
    static readonly (string Business, string[] Says, string[] Named)[] Businesses =
    [
        ("software", ["software"], ["Software", "IT Services", "Information Technology Services", "Application", "Systems Software"]),
        ("semiconductors", ["semiconductor", "semiconductors", "chips"], ["Semiconductor"]),
        ("banking", ["bank", "banks", "banking", "deposits", "loans"], ["Bank", "Banks", "Thrifts", "Financ"]),
        ("insurance", ["insurance", "insurer", "underwriting", "premiums"], ["Insurance"]),
        ("medicines", ["pharmaceutical", "pharmaceuticals", "drug", "drugs", "therapies", "biotechnology"], ["Pharmaceutical", "Biotechnology", "Life Sciences", "Drug"]),
        ("medical devices", ["medical device", "medical devices", "surgical", "diagnostic"], ["Health Care Equipment", "Medical", "Diagnostic", "Health Care Supplies", "Life Sciences"]),
        ("oil and gas", ["oil", "natural gas", "crude", "refining", "drilling"], ["Oil", "Gas", "Energy"]),
        ("utilities", ["electricity", "electric utility", "utility", "utilities", "power generation"], ["Utilit", "Electric", "Power"]),
        ("real estate", ["real estate", "properties", "REIT", "leases"], ["REIT", "Real Estate"]),
        ("aerospace and defense", ["aerospace", "defense", "aircraft"], ["Aerospace", "Defense"]),
        ("retail", ["retail", "stores", "retailer"], ["Retail", "Stores", "Distributors"]),
        ("restaurants", ["restaurant", "restaurants"], ["Restaurants", "Hotels"]),
    ];

    // The business the text describes most, none where it names none of the list's words.
    public static string? Business(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Businesses
            .Select(business => (business.Business, Count: business.Says.Sum(word => Regex.Matches(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase).Count)))
            .Where(counted => counted.Count > 0)
            .OrderByDescending(counted => counted.Count)
            .Select(counted => counted.Business)
            .FirstOrDefault();
    }

    // The business the text describes where the industry group names none of the words a group carrying it is named by,
    // and none where the two agree or either is not read.
    public static string? Mismatch(string? industry, string? text) =>
        industry is { Length: > 0 } group && Business(text) is { } business
        && !Businesses.First(entry => entry.Business == business).Named.Any(word => group.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? business
            : null;

    // Whether a source the industry cycle rests on is about the company or its industry: its title or text names the
    // company, or one of the words the industry's own name is made of.
    public static bool Relevant(string title, string text, string? company, string? industry)
    {
        var read = title + " " + text;
        var words = (industry ?? string.Empty)
            .Split([' ', ',', '&', '-', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length >= 4 && !Common.Contains(word))
            .ToArray();

        return (company is { Length: > 0 } && News.StoryMatch.NameKeys(company).Any(key => Regex.IsMatch(read, $@"(?<![A-Za-z0-9]){Regex.Escape(key)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase)))
            || words.Any(word => Regex.IsMatch(read, $@"\b{Regex.Escape(word.TrimEnd('s'))}", RegexOptions.IgnoreCase));
    }

    // The fewest sources the industry cycle is written over.
    public const int FewestSources = 2;

    static readonly HashSet<string> Common = new(["Products", "Services", "Equipment", "Components", "Other", "Industries", "Diversified", "General"], StringComparer.OrdinalIgnoreCase);
}
