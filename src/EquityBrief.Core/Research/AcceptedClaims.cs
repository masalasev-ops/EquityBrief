using System.Globalization;
using System.Text.Json.Nodes;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Ladders;

namespace EquityBrief.Core.Research;

// One claim the short version and the headline may rest on: a sentence the checker accepted in a written section, or
// one code writes in words from the night's stored plan and readings, and the region of the page that draws it.
public sealed record Claim(string Text, string Region);

// The claims the short version and the headline are written from, numbered in the order they are handed: every
// sentence of every accepted written section, its marks taken off, in the order the page draws the regions, and the
// sentences code writes in words from the stored plan and the business state, which carry no figure. Stored beside each
// draft, so the checker holds the draft to the claims it was handed and the page links each mark to its region.
// see: The short version and the headline are written last from the accepted claims alone, and code writes each where none is accepted
public static class AcceptedClaims
{
    // The regions of the page as section 4 names them, which a claim's mark links to.
    public const string Quarter = "The latest quarter";
    public const string Management = "What management said";
    public const string Segments = "Segments";
    public const string Moves = "How it got here";
    public const string Plan = "The plan";
    public const string Risks = "The risks, each with what would confirm it";
    public const string TwoCases = "The two cases";
    public const string Calendar = "Calendar";

    // The written sections in the order the page draws them, each with its region.
    static readonly (string Section, string Region)[] Written =
    [
        (ClaimRules.ManagementSection, Management),
        ("What the company sells", Segments),
        ("The segment commentary", Segments),
        (ClaimRules.CauseSection, Moves),
        (RiskFields.Section, Risks),
        (ClaimRules.TwoCasesSection, TwoCases),
        (ClaimRules.CalendarSection, Calendar),
    ];

    // The claims, the business state's first and the plan's after the moves' causes, as the page draws them.
    public static IReadOnlyList<Claim> Of(IReadOnlyList<(string Section, string Prose)> accepted, IReadOnlyList<Fact> facts, string? plan)
    {
        var claims = new List<Claim>();

        if (facts.FirstOrDefault(fact => fact.Name == BusinessState)?.Value is { Length: > 0 } state)
        {
            claims.Add(new Claim("Its reported quarters read as " + state + ", read from sales and operating margin alone.", Quarter));
        }

        foreach (var (section, region) in Written)
        {
            if (region == Risks)
            {
                claims.AddRange(PlanClaims(plan));
            }

            foreach (var prose in accepted.Where(one => one.Section == section).Select(one => one.Prose))
            {
                claims.AddRange(ClaimRules.Rows(prose).Select(ClaimRules.Unmarked).Where(text => text.Length > 0).Select(text => new Claim(text, region)));
            }
        }

        return claims;
    }

    // Whether any claim a written section accepted is among them, since claims code writes alone tell a reader nothing
    // a section did not.
    public static bool AnyWritten(IReadOnlyList<Claim> claims) => claims.Any(claim => claim.Region != Plan && claim.Region != Quarter);

    // The plan in words and no figure: how many tranches it buys in, where it sells and where the trail begins, and how
    // its stops are raised, or that none is stored.
    // see: Each tranche buys an equal share of a full position as the exits divide it, and its reward to risk is read to every traded exit
    public static IReadOnlyList<Claim> PlanClaims(string? plan)
    {
        if (plan is null)
        {
            return [new Claim("No plan is stored for the night, the plan being read for the S&P 500's members alone.", Plan)];
        }

        var (tranches, exits, _) = StoredPlan.Read(plan);
        var traded = exits.Where(exit => exit.Traded).ToArray();
        var claims = new List<Claim>
        {
            new(tranches.Count == 0
                ? "The plan buys nothing on the night, since no support band beneath the price holds a tranche."
                : "The plan buys in " + Count(tranches.Count, "tranche") + " at the support bands beneath the price, each an equal share of a full position.", Plan),
        };

        if (tranches.Count > 0)
        {
            claims.Add(new Claim(
                traded.Length == 0
                    ? "No resistance band above the price is far enough from the entry to sell at."
                    : traded.Any(exit => exit.Trailing)
                        ? traded.Length == 1
                            ? "It holds the whole position to the resistance band above, and from there holds it on a stop trailed under the price rather than selling it at a price."
                            : "It sells an equal share at each traded resistance band above but the top one, and from the top one holds the last share on a stop trailed under the price rather than selling it at a price."
                        : "It sells an equal share at each traded resistance band above, the last at the top one.",
                Plan));
            claims.Add(new Claim("In an uptrend each tranche's stop is raised to the higher of the band beneath it and the last swing low beneath it.", Plan));
        }

        return claims;
    }

    // The claims as the row stores them beside its draft.
    public static string Stored(IReadOnlyList<Claim> claims) =>
        new JsonObject
        {
            ["claims"] = new JsonArray([.. claims.Select(claim => (JsonNode)new JsonObject { ["text"] = claim.Text, ["region"] = claim.Region })]),
        }.ToJsonString();

    // The claims a stored row holds, or none where it holds none or is not their shape.
    public static IReadOnlyList<Claim>? Read(string? parts)
    {
        if (string.IsNullOrWhiteSpace(parts))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(parts) is not JsonObject root || root["claims"] is not JsonArray listed)
            {
                return null;
            }

            var claims = new List<Claim>();

            foreach (var item in listed)
            {
                if (item is not JsonObject claim
                    || claim["text"]?.GetValue<string>() is not { Length: > 0 } text
                    || claim["region"]?.GetValue<string>() is not { Length: > 0 } region)
                {
                    return null;
                }

                claims.Add(new Claim(text, region));
            }

            return claims;
        }
        catch (Exception unreadable) when (unreadable is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // A count of up to ten in words, as the prose rules ask, and in digits beyond.
    static string Count(int count, string noun) =>
        (count <= 10 ? Words[count] : count.ToString(CultureInfo.InvariantCulture)) + " " + noun + (count == 1 ? string.Empty : "s");

    static readonly string[] Words = ["no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

    const string BusinessState = "business state";
}
