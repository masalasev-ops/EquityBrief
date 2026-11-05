using System.Text.RegularExpressions;

namespace EquityBrief.Core.Report;

// A segment name or insider title filed as one run of joined capitals, split at its capitals for the reader with no abbreviation
// expanded and the stored value unchanged: "HomeBuildingOps" reads "Home Building Ops" and "IntlVehiclePmts" "Intl Vehicle Pmts". A
// name a company itself writes joined keeps its spelling, and a new joined name is split by default.
// see: A name filed as one run of joined capitals is split at its capitals for the reader, and a company's own joined names keep their spelling
public static partial class JoinedNames
{
    public static string Spaced(string name) =>
        JoinedRun().Replace(name, match => CompanysOwnSpelling.Contains(match.Value) ? match.Value : Capital().Replace(match.Value, " "));

    public static bool IsSplit(string name) => !string.Equals(Spaced(name), name, StringComparison.Ordinal);

    public static IReadOnlySet<string> CompanysOwnSpelling { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AmeriGas", "BioPharm", "BioPharma", "CardioVascular", "CarelonRx", "CenterWell", "CoreSite", "CrossLab", "DoorDash", "DownREIT", "EchoStar", "ExxonMobil", "FootJoy", "GovDeals",
        "HomeGoods", "InPost", "IoT", "LiveWire", "LongHorn", "McDonald", "McLane", "MedSurg", "MedTech", "NextEra", "NorthStar", "OptiFreight", "PayPal", "PepsiCo",
        "PharMerica", "PrimeLending", "ProQuad", "ProTax", "QuickBooks", "RotaTeq", "SaaS", "SendTech", "SmartCover", "SpinCo", "TheFork", "TurboTax",
        "UniFirst", "UnitedHealthcare", "UpLift", "YouTube",
    };

    [GeneratedRegex(@"\b[A-Z][a-z]+(?:[A-Z]+[a-z]*)+\b")]
    private static partial Regex JoinedRun();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex Capital();
}
