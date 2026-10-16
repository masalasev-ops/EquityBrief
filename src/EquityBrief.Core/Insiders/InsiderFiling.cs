namespace EquityBrief.Core.Insiders;

// One reporting owner of an insider's filing as the filing states them: the CIK padded to ten digits, the name and the
// role, a director, an officer with the title, a ten per cent owner.
public sealed record InsiderOwner(string Cik, string Name, bool Director, bool Officer, string? Title, bool TenPercent);

// One transaction of a filing's table of shares, in the filing's order from 1: the security, the day traded, the code,
// the shares, the price a share where one is stated, whether the shares were acquired or disposed of, the shares held
// after it where the filing states them, and whether the holding is direct, with the nature of an indirect one.
public sealed record InsiderTransaction(
    int Sequence,
    string Security,
    DateOnly Traded,
    string Code,
    decimal Shares,
    decimal? Price,
    bool Acquired,
    decimal? HeldAfter,
    bool Direct,
    string? Nature);

// One Form 4 or 4/A as a reader read it: the accession, the day filed, the form, the issuer's CIK padded to ten digits and
// its trading symbol as stated, for an amendment the day it states its original was filed, the owners in the filing's
// order, whether the filing ticks the Rule 10b5-1 box, none where it carries no box, and its table of shares.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public sealed record InsiderFiling(
    string Accession,
    DateOnly Filed,
    string Form,
    string IssuerCik,
    string? Symbol,
    DateOnly? OriginalFiled,
    IReadOnlyList<InsiderOwner> Owners,
    bool? Planned,
    IReadOnlyList<InsiderTransaction> Transactions)
{
    public const string Original = "4";

    public const string Amendment = "4/A";

    public static IReadOnlyList<string> Forms { get; } = [Original, Amendment];

    public bool Amends => Form == Amendment;

    // The owners' CIKs in the order this reader lists them, joined by commas, which every figure takes as one insider in
    // ordinal order, so a filing made jointly is one insider and the same owners filing again are the same one.
    public string OwnerKey => string.Join(',', Owners.Select(owner => owner.Cik));
}
