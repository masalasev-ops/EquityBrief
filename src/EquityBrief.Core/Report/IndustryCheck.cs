using System.Text.RegularExpressions;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Report;

// One line the industry check reads: a segment's name, or a revenue line's words with the grouping the filing states it
// under, and the revenue it carries for the quarter.
public sealed record BusinessLine(string Words, decimal Revenue);

// Whether a stock's industry group names the business the company's own segments and revenue lines report: the business
// whose words the lines carrying most of the quarter's revenue use, against the words an industry carrying that business is
// named by. A group naming none of them is a mismatch, which the page flags beside the group, the group being the
// provider's classification and an operator's override standing in its place where one is set. Words in a description of
// the company are not read, since a description names what a company sells beside and to, where its segments name what it
// is paid for.
// see: A stock's industry group is checked against the company's own segments and revenue lines and not its description
public static class IndustryCheck
{
    // Each business word a company's lines use, with the words an industry group carrying that business is named by. A
    // health insurer is classified among the health care providers, managed care being both, so that group names
    // insurance.
    static readonly (string Business, string[] Says, string[] Named)[] Businesses =
    [
        ("software", ["software"], ["Software", "IT Services", "Information Technology Services", "Application", "Systems Software"]),
        ("semiconductors", ["semiconductor", "semiconductors", "chips"], ["Semiconductor"]),
        ("banking", ["bank", "banks", "banking", "deposits", "loans"], ["Bank", "Banks", "Thrifts", "Financ"]),
        ("insurance", ["insurance", "insurer", "underwriting", "premiums"], ["Insurance", "Health Care Providers"]),
        ("medicines", ["pharmaceutical", "pharmaceuticals", "drug", "drugs", "therapies", "biotechnology"], ["Pharmaceutical", "Biotechnology", "Life Sciences", "Drug"]),
        ("medical devices", ["medical device", "medical devices", "surgical", "diagnostic"], ["Health Care Equipment", "Medical", "Diagnostic", "Health Care Supplies", "Life Sciences"]),
        ("oil and gas", ["oil", "natural gas", "crude", "refining", "drilling"], ["Oil", "Gas", "Energy"]),
        ("utilities", ["electricity", "electric utility", "utility", "utilities", "power generation"], ["Utilit", "Electric", "Power"]),
        ("real estate", ["real estate", "properties", "REIT", "leases"], ["REIT", "Real Estate"]),
        ("aerospace and defense", ["aerospace", "defense", "aircraft"], ["Aerospace", "Defense"]),
        ("retail", ["retail", "stores", "retailer"], ["Retail", "Stores", "Distributors"]),
        ("restaurants", ["restaurant", "restaurants"], ["Restaurants", "Hotels"]),
    ];

    // The share of the quarter's revenue a business's lines must carry for the lines to describe it, more than half.
    public const double MostOfRevenue = 0.5;

    // The words a filing's line names revenue by, which the lines read are those of.
    static readonly Regex RevenueLine = new(@"revenue|sales|premium", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The lines the check reads: a clean segment table's segments with their revenue, the release's where it read one and
    // the filing's otherwise; and where neither stands, the filing's table as filed, each grouping's lines naming revenue
    // for the quarter, the grouping's label beside the line's words. None where the page holds no segment table.
    public static IReadOnlyList<BusinessLine> Lines(SegmentReading? clean, SegmentBreakdown? filed)
    {
        if (clean is { Clean: true, Rows.Count: > 0 } table)
        {
            return [.. table.Rows.Select(row => new BusinessLine(row.Segment, row.Revenue))];
        }

        if (filed is not { Periods.Count: > 0 } breakdown)
        {
            return [];
        }

        var shortest = breakdown.Periods.Min(period => period.Months);
        var ended = breakdown.Periods.Where(period => period.Months == shortest).Max(period => period.Ended);

        return
        [
            .. breakdown.Groups.SelectMany(group => group.Figures
                .Where(figure => figure.Value is not null && figure.Period.Months == shortest && figure.Period.Ended == ended && RevenueLine.IsMatch(figure.LineItem))
                .Select(figure => new BusinessLine(group.Label + " " + figure.LineItem, figure.Value!.Value))),
        ];
    }

    // The business the lines carrying most of the quarter's revenue name and the share they carry, none where no business
    // carries more than half. A line is counted under the first business whose words it uses.
    public static (string Business, double Share)? Business(IReadOnlyList<BusinessLine> lines)
    {
        var total = lines.Sum(line => Math.Abs(line.Revenue));

        if (total <= 0m)
        {
            return null;
        }

        var carried = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (Businesses.FirstOrDefault(business => business.Says.Any(word => Regex.IsMatch(line.Words, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))) is { Business: { } named })
            {
                carried[named] = carried.GetValueOrDefault(named) + Math.Abs(line.Revenue);
            }
        }

        return carried.Count == 0
            ? null
            : carried.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, Share: Prices.Statistic.FromRatio(pair.Value / total))).First() is var most && most.Share > MostOfRevenue
                ? most
                : null;
    }

    // The industry group a business is proposed under where the stock's group names none of it, in the classification's own
    // words, which the operator approves before any override is set; the business itself where the list names no group.
    public static string Proposed(string business) => business switch
    {
        "software" => "Software",
        "semiconductors" => "Semiconductors & Semiconductor Equipment",
        "banking" => "Banks",
        "insurance" => "Insurance",
        "medicines" => "Pharmaceuticals",
        "medical devices" => "Health Care Equipment & Supplies",
        "oil and gas" => "Oil, Gas & Consumable Fuels",
        "utilities" => "Electric Utilities",
        "real estate" => "Real Estate Management & Development",
        "aerospace and defense" => "Aerospace & Defense",
        "retail" => "Specialty Retail",
        "restaurants" => "Hotels, Restaurants & Leisure",
        _ => business,
    };

    // The business the lines describe with its share where the industry group names none of the words a group carrying it
    // is named by, and none where the two agree or either is not read.
    public static (string Business, double Share)? Mismatch(string? industry, IReadOnlyList<BusinessLine> lines) =>
        industry is { Length: > 0 } group && Business(lines) is { } described
        && !Businesses.First(entry => entry.Business == described.Business).Named.Any(word => group.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? described
            : null;

    // Whether a source a written section cites is about the company or its industry: its title or its whole stored text
    // names the company, or one of the words the industry's own name is made of. The whole text is read, so a document
    // naming the company past its opening, a roundup giving it a paragraph of its own, stands; one naming neither anywhere,
    // as another company's news under a ticker it shares on another exchange, does not.
    // see: Every written section's sources and the page's are held to documents naming the company or its industry in their whole text
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
