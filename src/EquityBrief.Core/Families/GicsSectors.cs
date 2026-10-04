namespace EquityBrief.Core.Families;

// One member of the index GICS moved to another sector after the close of 2023-03-17: the ticker the index holds it
// under, the company's CIK, and the sectors it left and joined.
public sealed record SectorMove(string Ticker, string Cik, string From, string To);

// A company's GICS sector on a session.
//
// The provider files each company's sector as of its last update and keeps no dated history, so a session reads the
// sector filed today, but for the fourteen members of the index GICS moved after the close of 2023-03-17, which read
// the sector they left on every session to that close. The fourteen are the published list: eight from Information
// Technology to Financials, three from Information Technology to Industrials and three from Consumer Discretionary to
// Consumer Staples. A company moved on its own, for a change of business, carries today's sector on every session,
// which is the residue no dated history removes. The sectors read are the eleven formed after the close of
// 2018-09-21, and a session before them reads none.
// see: A company's sector on a session is the GICS sector the provider files, with the fourteen moves of 2023-03-17 read by date
public static class GicsSectors
{
    // The first session of the eleven sectors, Communication Services formed after the close before it.
    public static readonly DateOnly First = new(2018, 9, 24);

    // The last session the fourteen read the sector they left.
    public static readonly DateOnly MovedAfter = new(2023, 3, 17);

    public const string CommunicationServices = "Communication Services";
    public const string ConsumerDiscretionary = "Consumer Discretionary";
    public const string ConsumerStaples = "Consumer Staples";
    public const string Energy = "Energy";
    public const string Financials = "Financials";
    public const string HealthCare = "Health Care";
    public const string Industrials = "Industrials";
    public const string InformationTechnology = "Information Technology";
    public const string Materials = "Materials";
    public const string RealEstate = "Real Estate";
    public const string Utilities = "Utilities";

    public static IReadOnlyList<string> Eleven { get; } =
    [
        CommunicationServices, ConsumerDiscretionary, ConsumerStaples, Energy, Financials, HealthCare,
        Industrials, InformationTechnology, Materials, RealEstate, Utilities,
    ];

    // The fund holding each sector's members of the index, whose daily series a sector's return may be read from.
    public static IReadOnlyDictionary<string, string> Funds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CommunicationServices] = "XLC",
        [ConsumerDiscretionary] = "XLY",
        [ConsumerStaples] = "XLP",
        [Energy] = "XLE",
        [Financials] = "XLF",
        [HealthCare] = "XLV",
        [Industrials] = "XLI",
        [InformationTechnology] = "XLK",
        [Materials] = "XLB",
        [RealEstate] = "XLRE",
        [Utilities] = "XLU",
    };

    // The fourteen, each under the ticker the index holds it by and the CIK the archive files it under.
    public static IReadOnlyList<SectorMove> Moves { get; } =
    [
        new("V", "0001403161", InformationTechnology, Financials),
        new("MA", "0001141391", InformationTechnology, Financials),
        new("PYPL", "0001633917", InformationTechnology, Financials),
        new("FIS", "0001136893", InformationTechnology, Financials),
        new("FISV", "0000798354", InformationTechnology, Financials),
        new("GPN", "0001123360", InformationTechnology, Financials),
        new("JKHY", "0000779152", InformationTechnology, Financials),
        new("CPAY", "0001175454", InformationTechnology, Financials),
        new("ADP", "0000008670", InformationTechnology, Industrials),
        new("PAYX", "0000723531", InformationTechnology, Industrials),
        new("BR", "0001383312", InformationTechnology, Industrials),
        new("TGT", "0000027419", ConsumerDiscretionary, ConsumerStaples),
        new("DG", "0000029534", ConsumerDiscretionary, ConsumerStaples),
        new("DLTR", "0000935703", ConsumerDiscretionary, ConsumerStaples),
    ];

    // The sector a listing reads on a session: none where the provider files none of the eleven or the session is
    // before them, the sector it left for one of the fourteen on a session to the close it moved after, and the sector
    // filed otherwise.
    public static string? On(string ticker, string? filed, DateOnly session)
    {
        if (session < First || filed is null || !Eleven.Contains(filed, StringComparer.Ordinal))
        {
            return null;
        }

        return session <= MovedAfter && Moves.FirstOrDefault(move => string.Equals(move.Ticker, ticker, StringComparison.Ordinal)) is { } moved
            ? moved.From
            : filed;
    }
}
