namespace EquityBrief.Core.Families;

// One listing of a company on a session: its ticker, the company it belongs to, its value, and the dollars it traded
// over the sessions to the session the rank reads.
public sealed record CompanyListing(string Ticker, string Company, decimal Value, decimal DollarVolume);

// Companies ranked by value, each by one listing.
//
// Two classes of one company are filed under one CIK, each with the count of every class, so a rank of listings would
// put the company among a sector's largest twice. A company is ranked once, by the listing that traded the more dollars
// over the fifty sessions to the session, the ticker settling a tie, and a listing the provider files no CIK for is a
// company of its own.
// see: Companies are ranked by CIK with one listing held, the class that traded the more dollars over fifty sessions
public static class CompanyRank
{
    // The sessions to the session a listing's dollars traded are summed over.
    public const int DollarVolumeSessions = 50;

    // The company a listing belongs to: its CIK where the provider files one, and the listing itself where not.
    public static string CompanyOf(string ticker, string? cik) =>
        cik is { Length: > 0 } filed ? "CIK " + filed : "ticker " + ticker;

    // The dollars a listing traded over the newest fifty of the sessions it is handed, in order, each its unadjusted
    // close times its volume.
    public static decimal DollarVolume(IEnumerable<(decimal RawClose, long Volume)> sessions) =>
        sessions.TakeLast(DollarVolumeSessions).Sum(session => session.RawClose * session.Volume);

    // One listing a company, largest value first, the ticker settling a tie.
    public static IReadOnlyList<CompanyListing> Ranked(IEnumerable<CompanyListing> listings) =>
    [
        .. listings
            .GroupBy(listing => listing.Company, StringComparer.Ordinal)
            .Select(company => company
                .OrderByDescending(listing => listing.DollarVolume)
                .ThenBy(listing => listing.Ticker, StringComparer.Ordinal)
                .First())
            .OrderByDescending(listing => listing.Value)
            .ThenBy(listing => listing.Ticker, StringComparer.Ordinal),
    ];
}
