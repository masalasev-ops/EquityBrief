using System.Text.Json.Nodes;

namespace EquityBrief.Core.Facts;

// The parts of a company's figures that are as of the fetch rather than as of a filing, and how a
// reader lays the newest copy of them over the newest filing's row.
//
// A filing's row is written once and never updated, and these parts sit on the newest filing's row
// as they stood when it was fetched. A fetch that finds no new filing stores them again as a copy of
// their own, so a reader asking for the figures as they stand now takes each of these parts from the
// newest copy and every other part from the filing. A part the copy holds as null replaces the
// filing's, since the copy is the newer statement of whether the company files it.
// see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
public static class FundamentalsSnapshot
{
    // The earnings bases a multiple is struck on, the multiples, the market value, the analysts'
    // ratings, the dividend and the next report as the provider has it.
    public static readonly string[] Parts =
        ["epsBases", "valuation", "marketCapitalisation", "ratings", "dividend", "estimated"];

    // The filing's payload with each of the parts taken from the copy, or the filing's payload as it
    // is where there is no copy. The filing's other parts, and the order they are written in, are
    // left as they are.
    public static string Over(string filing, string? snapshot)
    {
        if (snapshot is null)
        {
            return filing;
        }

        var row = JsonNode.Parse(filing)!.AsObject();
        var copy = JsonNode.Parse(snapshot)!.AsObject();

        foreach (var part in Parts)
        {
            row[part] = copy[part]?.DeepClone();
        }

        return row.ToJsonString();
    }
}
