using System.Text.RegularExpressions;
using EquityBrief.Core.Families;

namespace EquityBrief.Core.History;

// The funds' industry headings read as sectors as they stood: each heading's words matched against a stated list of keys in
// order, the first a heading holds naming its sector in today's eleven; a session before Real Estate's own sector reads it
// under Financials, and one before Communication Services' reads its media under Consumer Discretionary and its internet and
// telecommunications under Information Technology, whose fund held them. After the schedules end a member reads its filed
// GICS sector.
// see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
public static class SectorMap
{
    // The first session Real Estate stood as a sector of its own, and the first of Communication Services, after the close
    // of the Friday before.
    public static DateOnly RealEstateFrom { get; } = new(2016, 9, 19);

    public static DateOnly CommunicationFrom => GicsSectors.First;

    // The last quarter end the funds' schedules hold.
    public static DateOnly SchedulesThrough { get; } = new(2018, 9, 30);

    const string Media = "media";
    const string Online = "online";

    // The keys in the order they are read, each a phrase of whole words in the heading's normalised words, and the sector
    // it names; a phrase standing for a part of Communication Services marks which older sector held it.
    public static IReadOnlyList<(string Key, string Sector, string? Older)> Keys { get; } =
    [
        ("FOOD STAPLES RETAILING", GicsSectors.ConsumerStaples, null),
        ("INTERNET CATALOG RETAIL", GicsSectors.ConsumerDiscretionary, null),
        ("INTERNET DIRECT MARKETING RETAIL", GicsSectors.ConsumerDiscretionary, null),
        ("MORTGAGE REAL ESTATE", GicsSectors.Financials, null),
        ("REAL ESTATE", GicsSectors.RealEstate, null),
        ("REIT", GicsSectors.RealEstate, null),
        ("REITS", GicsSectors.RealEstate, null),
        ("BANKS", GicsSectors.Financials, null),
        ("BANK", GicsSectors.Financials, null),
        ("INSURANCE", GicsSectors.Financials, null),
        ("FINANCIAL", GicsSectors.Financials, null),
        ("CAPITAL MARKETS", GicsSectors.Financials, null),
        ("SAVINGS LOANS", GicsSectors.Financials, null),
        ("THRIFTS", GicsSectors.Financials, null),
        ("CONSUMER FINANCE", GicsSectors.Financials, null),
        ("INVESTMENT COMPANIES", GicsSectors.Financials, null),
        ("HOLDING COMPANIES", GicsSectors.Financials, null),
        ("INTERNET SOFTWARE", GicsSectors.CommunicationServices, Online),
        ("INTERACTIVE MEDIA", GicsSectors.CommunicationServices, Online),
        ("INTERNET", GicsSectors.CommunicationServices, Online),
        ("TELECOMMUNICATION", GicsSectors.CommunicationServices, Online),
        ("TELECOMMUNICATIONS", GicsSectors.CommunicationServices, Online),
        ("MEDIA", GicsSectors.CommunicationServices, Media),
        ("ENTERTAINMENT", GicsSectors.CommunicationServices, Media),
        ("ADVERTISING", GicsSectors.CommunicationServices, Media),
        ("SEMICONDUCTORS", GicsSectors.InformationTechnology, null),
        ("SEMICONDUCTOR", GicsSectors.InformationTechnology, null),
        ("SOFTWARE", GicsSectors.InformationTechnology, null),
        ("COMPUTERS", GicsSectors.InformationTechnology, null),
        ("IT SERVICES", GicsSectors.InformationTechnology, null),
        ("TECHNOLOGY HARDWARE", GicsSectors.InformationTechnology, null),
        ("ELECTRONIC EQUIPMENT", GicsSectors.InformationTechnology, null),
        ("ELECTRONICS", GicsSectors.InformationTechnology, null),
        ("COMMUNICATIONS EQUIPMENT", GicsSectors.InformationTechnology, null),
        ("OFFICE ELECTRONICS", GicsSectors.InformationTechnology, null),
        ("OFFICE BUSINESS EQUIPMENT", GicsSectors.InformationTechnology, null),
        ("PHARMACEUTICALS", GicsSectors.HealthCare, null),
        ("BIOTECHNOLOGY", GicsSectors.HealthCare, null),
        ("HEALTH CARE", GicsSectors.HealthCare, null),
        ("LIFE SCIENCES", GicsSectors.HealthCare, null),
        ("GAS UTILITIES", GicsSectors.Utilities, null),
        ("ELECTRIC UTILITIES", GicsSectors.Utilities, null),
        ("MULTI UTILITIES", GicsSectors.Utilities, null),
        ("WATER", GicsSectors.Utilities, null),
        ("INDEPENDENT POWER", GicsSectors.Utilities, null),
        ("OIL", GicsSectors.Energy, null),
        ("ELECTRIC", GicsSectors.Utilities, null),
        ("GAS", GicsSectors.Utilities, null),
        ("PIPELINES", GicsSectors.Energy, null),
        ("COAL", GicsSectors.Energy, null),
        ("ENERGY", GicsSectors.Energy, null),
        ("METAL FABRICATE", GicsSectors.Industrials, null),
        ("ELECTRICAL", GicsSectors.Industrials, null),
        ("CHEMICALS", GicsSectors.Materials, null),
        ("METALS", GicsSectors.Materials, null),
        ("MINING", GicsSectors.Materials, null),
        ("IRON", GicsSectors.Materials, null),
        ("FOREST", GicsSectors.Materials, null),
        ("PAPER", GicsSectors.Materials, null),
        ("PACKAGING", GicsSectors.Materials, null),
        ("CONTAINERS", GicsSectors.Materials, null),
        ("BUILDING MATERIALS", GicsSectors.Materials, null),
        ("CONSTRUCTION MATERIALS", GicsSectors.Materials, null),
        ("FOOD", GicsSectors.ConsumerStaples, null),
        ("BEVERAGES", GicsSectors.ConsumerStaples, null),
        ("TOBACCO", GicsSectors.ConsumerStaples, null),
        ("HOUSEHOLD PRODUCTS", GicsSectors.ConsumerStaples, null),
        ("PERSONAL PRODUCTS", GicsSectors.ConsumerStaples, null),
        ("COSMETICS", GicsSectors.ConsumerStaples, null),
        ("AGRICULTURE", GicsSectors.ConsumerStaples, null),
        ("RETAIL", GicsSectors.ConsumerDiscretionary, null),
        ("APPAREL", GicsSectors.ConsumerDiscretionary, null),
        ("TEXTILES", GicsSectors.ConsumerDiscretionary, null),
        ("HOTELS", GicsSectors.ConsumerDiscretionary, null),
        ("LEISURE", GicsSectors.ConsumerDiscretionary, null),
        ("LODGING", GicsSectors.ConsumerDiscretionary, null),
        ("HOME BUILDERS", GicsSectors.ConsumerDiscretionary, null),
        ("HOUSEHOLD DURABLES", GicsSectors.ConsumerDiscretionary, null),
        ("HOME FURNISHINGS", GicsSectors.ConsumerDiscretionary, null),
        ("HOUSEWARES", GicsSectors.ConsumerDiscretionary, null),
        ("AUTO", GicsSectors.ConsumerDiscretionary, null),
        ("AUTOMOBILES", GicsSectors.ConsumerDiscretionary, null),
        ("TOYS", GicsSectors.ConsumerDiscretionary, null),
        ("DIVERSIFIED CONSUMER SERVICES", GicsSectors.ConsumerDiscretionary, null),
        ("DISTRIBUTORS", GicsSectors.ConsumerDiscretionary, null),
        ("AEROSPACE", GicsSectors.Industrials, null),
        ("MACHINERY", GicsSectors.Industrials, null),
        ("MACHINE TOOLS", GicsSectors.Industrials, null),
        ("MANUFACTURING", GicsSectors.Industrials, null),
        ("COMMERCIAL SERVICES", GicsSectors.Industrials, null),
        ("PROFESSIONAL SERVICES", GicsSectors.Industrials, null),
        ("TRANSPORTATION", GicsSectors.Industrials, null),
        ("AIRLINES", GicsSectors.Industrials, null),
        ("AIR FREIGHT", GicsSectors.Industrials, null),
        ("ROAD RAIL", GicsSectors.Industrials, null),
        ("MARINE", GicsSectors.Industrials, null),
        ("TRUCKING", GicsSectors.Industrials, null),
        ("SHIPBUILDING", GicsSectors.Industrials, null),
        ("BUILDING PRODUCTS", GicsSectors.Industrials, null),
        ("ENGINEERING", GicsSectors.Industrials, null),
        ("CONSTRUCTION", GicsSectors.Industrials, null),
        ("ENVIRONMENTAL", GicsSectors.Industrials, null),
        ("TRADING COMPANIES", GicsSectors.Industrials, null),
        ("DISTRIBUTION", GicsSectors.Industrials, null),
        ("INDUSTRIAL CONGLOMERATES", GicsSectors.Industrials, null),
        ("OFFICE FURNISHINGS", GicsSectors.Industrials, null),
        ("STORAGE WAREHOUSING", GicsSectors.Industrials, null),
    ];

    // A heading's words, upper case, every run of characters that are not letters read as one space.
    public static string Words(string heading) => Regex.Replace(heading.ToUpperInvariant(), "[^A-Z]+", " ").Trim();

    // The sector a heading names in today's eleven, and the older sector a part of Communication Services was held in; none
    // where no key matches.
    public static (string Sector, string? Older)? Of(string heading)
    {
        var words = " " + Words(heading) + " ";

        foreach (var (key, sector, older) in Keys)
        {
            if (words.Contains(" " + key + " ", StringComparison.Ordinal))
            {
                return (sector, older);
            }
        }

        return null;
    }

    // The sector a heading reads on a session as it stood: Real Estate under Financials before its own, and a part of
    // Communication Services under its older sector before that one's.
    public static string? AsItStood(string heading, DateOnly session) =>
        Of(heading) is not { } read
            ? null
            : read.Sector == GicsSectors.RealEstate && session < RealEstateFrom
                ? GicsSectors.Financials
                : read.Sector == GicsSectors.CommunicationServices && session < CommunicationFrom
                    ? read.Older == Media ? GicsSectors.ConsumerDiscretionary : GicsSectors.InformationTechnology
                    : read.Sector;

    // The fund a sector reads, the eleven's own.
    public static string FundOf(string sector) => GicsSectors.Funds[sector];
}
