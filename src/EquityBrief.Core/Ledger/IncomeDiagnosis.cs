using EquityBrief.Core.Families;
using EquityBrief.Core.Readings;

namespace EquityBrief.Core.Ledger;

// Why a member's four newest quarters as first filed before a session hold no operating income line a reading built on
// operating income can read, or none.
public enum IncomeCause
{
    Read,
    NoFiler,
    FewerQuarters,
    NoOperatingLine,
    SomeQuarters,
}

// The line a member's operating income is read on: as filed, or one of the fallbacks, or none; from the 24.12 correction of
// the eleven the last fallback, net income plus income tax expense plus interest, placed after none so no stored value moves.
public enum IncomeLine
{
    Operating,
    PretaxPlusInterest,
    RevenueLessCosts,
    None,
    NetPlusTaxPlusInterest,
}

// From 24.12, read-only: the diagnosis of every reading built on operating income. A member's cause over the four newest
// quarters of net income its filer filed before the session: read where each holds an operating income line; no filer
// where no filer is on file; fewer quarters where fewer than four quarters of net income were filed; no operating line
// where the filer filed none in any quarter; some quarters where it filed one for some of the four and not the others. It
// moves nothing any reading reads.
// see: Where operating income is not filed a reading takes income before taxes plus interest expense, else revenue less costs and expenses
public static class IncomeDiagnosis
{
    // The days a quarter's operating income may end from its net income's and still be the same quarter's.
    public const int SameQuarterWithin = 7;

    public static string Words(IncomeCause cause) => cause switch
    {
        IncomeCause.Read => "read",
        IncomeCause.NoFiler => "no filer on file",
        IncomeCause.FewerQuarters => "fewer than four quarters filed",
        IncomeCause.NoOperatingLine => "its filer files no operating income line",
        _ => "operating income filed for some of the four quarters only",
    };

    // The two fallback lines, in the order read: income before taxes plus interest expense, then revenue less costs and
    // expenses.
    public static IReadOnlyList<string> PretaxIncome { get; } =
    [
        "IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest",
        "IncomeLossFromContinuingOperationsBeforeIncomeTaxesMinorityInterestAndIncomeLossFromEquityMethodInvestments",
    ];

    // Income before taxes filed in its domestic and foreign parts, read where no total is filed for the period: the domestic
    // part alone where the filer files no foreign part in any period, and otherwise the two of the same period summed.
    public const string PretaxDomestic = "IncomeLossFromContinuingOperationsBeforeIncomeTaxesDomestic";

    public const string PretaxForeign = "IncomeLossFromContinuingOperationsBeforeIncomeTaxesForeign";

    public static IReadOnlyList<string> CostsAndExpenses { get; } = ["CostsAndExpenses"];

    // From the 24.12 correction of the eleven, on the operator's word of 2026-11-20, the tax the last fallback adds back to net
    // income where a filer tags no income before taxes: net income, as the filed facts keep it, plus this, plus interest.
    public const string IncomeTax = "IncomeTaxExpenseBenefit";

    public static IReadOnlyList<string> FallbackConcepts { get; } = [.. PretaxIncome, PretaxDomestic, PretaxForeign, .. CostsAndExpenses, IncomeTax];

    // The fallback lines a filer's answer stores, one a concept a period as first filed over the spans and from the first
    // period the filed facts keep, so the two tables meet period for period.
    public static IReadOnlyList<FiledFactRow> Stored(IReadOnlyDictionary<string, IReadOnlyList<Providers.ConceptFact>> facts) =>
    [
        .. facts
            .Where(concept => FallbackConcepts.Contains(concept.Key, StringComparer.Ordinal))
            .SelectMany(concept => concept.Value
                .Where(fact => fact.End >= FiledFacts.FirstPeriodEnd && FiledFacts.Kept(fact.Start, fact.End))
                .GroupBy(fact => (fact.Start, fact.End))
                .Select(period => period.OrderBy(fact => fact.Filed).ThenBy(fact => fact.Accession, StringComparer.Ordinal).First())
                .Select(fact => new FiledFactRow(concept.Key, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))),
    ];

    // The kind of company whose filings state no operating income line by the nature of its business, named from its industry
    // as filed: a real estate trust, a bank or an insurer; none otherwise.
    public static string? KindOf(params string?[] industries)
    {
        string[] named = [.. industries.OfType<string>()];

        return named.Any(one => one.Contains("Real Estate Investment Trust", StringComparison.Ordinal) || one.Contains("REIT", StringComparison.Ordinal))
            ? "a real estate trust"
            : named.Any(one => one.Contains("Banks", StringComparison.Ordinal))
            ? "a bank"
            : named.Any(one => one.Contains("Insurance", StringComparison.Ordinal))
            ? "an insurer"
            : null;
    }

    public static string Words(IncomeLine line) => line switch
    {
        IncomeLine.Operating => "operating income as filed",
        IncomeLine.PretaxPlusInterest => "income before taxes plus interest expense",
        IncomeLine.RevenueLessCosts => "revenue less costs and expenses",
        IncomeLine.NetPlusTaxPlusInterest => "net income plus tax plus interest",
        _ => "neither fallback line covers its four quarters",
    };

    // A filing's operating income line built from a fallback for each period it states both parts of, as that filing filed
    // them: income before taxes plus the interest expense of the same period, none filed read as nought, or revenue less
    // costs and expenses of the same period.
    static IReadOnlyList<FiledFactRow> Built(IReadOnlyList<FiledFactRow> facts, IncomeLine line)
    {
        IEnumerable<FiledFactRow> Of(IReadOnlyList<string> concepts) =>
            facts.Where(fact => concepts.Contains(fact.Concept, StringComparer.Ordinal))
                .GroupBy(fact => (fact.Start, fact.End))
                .Select(group => group.OrderBy(fact => IndexIn(concepts, fact.Concept)).ThenBy(fact => fact.Filed).First());

        static int IndexIn(IReadOnlyList<string> concepts, string concept) => concepts.ToList().IndexOf(concept);

        var interest = Of(FiledFacts.InterestExpense).ToDictionary(fact => (fact.Start, fact.End));

        if (line == IncomeLine.PretaxPlusInterest)
        {
            var totals = Of(PretaxIncome).ToList();
            var held = totals.Select(fact => (fact.Start, fact.End)).ToHashSet();
            var foreign = Of([PretaxForeign]).ToDictionary(fact => (fact.Start, fact.End));
            var parts = Of([PretaxDomestic])
                .Where(domestic => !held.Contains((domestic.Start, domestic.End)) && (foreign.Count == 0 || foreign.ContainsKey((domestic.Start, domestic.End))))
                .Select(domestic => domestic with { Value = domestic.Value + (foreign.TryGetValue((domestic.Start, domestic.End), out var abroad) ? abroad.Value : 0m) });

            return [.. totals.Concat(parts).Select(pretax => pretax with
            {
                Concept = FiledFacts.OperatingIncome[0],
                Value = pretax.Value + (interest.TryGetValue((pretax.Start, pretax.End), out var paid) ? Math.Abs(paid.Value) : 0m),
            })];
        }

        if (line == IncomeLine.NetPlusTaxPlusInterest)
        {
            var tax = Of([IncomeTax]).ToDictionary(fact => (fact.Start, fact.End));

            return
            [
                .. Of(FiledFacts.NetIncome)
                    .Where(net => tax.ContainsKey((net.Start, net.End)))
                    .Select(net => net with
                    {
                        Concept = FiledFacts.OperatingIncome[0],
                        Value = net.Value + tax[(net.Start, net.End)].Value + (interest.TryGetValue((net.Start, net.End), out var paid) ? Math.Abs(paid.Value) : 0m),
                    }),
            ];
        }

        var costs = Of(CostsAndExpenses).ToDictionary(fact => (fact.Start, fact.End));

        return
        [
            .. Of(FiledFacts.Revenue)
                .Where(revenue => costs.ContainsKey((revenue.Start, revenue.End)))
                .Select(revenue => revenue with { Concept = FiledFacts.OperatingIncome[0], Value = revenue.Value - costs[(revenue.Start, revenue.End)].Value }),
        ];
    }

    // The line a member's four newest quarters are read on and the facts with it: operating income as filed where it covers
    // them; else, for every quarter alike, income before taxes plus interest expense where that covers them; else revenue less
    // costs and expenses; else, from the 24.12 correction of the eleven, net income plus income tax expense plus interest
    // expense; else none, the facts as they stand.
    public static (IncomeLine Line, IReadOnlyList<FiledFactRow> Facts) WithLine(IReadOnlyList<FiledFactRow> facts, DateOnly session)
    {
        if (CauseOf(facts, session) is not (IncomeCause.NoOperatingLine or IncomeCause.SomeQuarters))
        {
            return (IncomeLine.Operating, facts);
        }

        FiledFactRow[] others = [.. facts.Where(fact => !FiledFacts.OperatingIncome.Contains(fact.Concept, StringComparer.Ordinal))];

        foreach (var line in new[] { IncomeLine.PretaxPlusInterest, IncomeLine.RevenueLessCosts, IncomeLine.NetPlusTaxPlusInterest })
        {
            FiledFactRow[] with = [.. others, .. Built(facts, line)];

            if (CauseOf(with, session) == IncomeCause.Read)
            {
                return (line, with);
            }
        }

        return (IncomeLine.None, facts);
    }

    // Whether the four newest quarters' operating income covers their interest by the cover's floor, none stated read as
    // covered, as the rules' cover reads it; none where fewer than four quarters hold the line.
    public static bool? Covers(IReadOnlyList<FiledFactRow> facts, DateOnly session)
    {
        FiledRevenue[] known = [.. facts.Where(fact => fact.Filed < session).Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))];
        var operating = FirstFiledRevenue.Quarters(known, FiledFacts.OperatingIncome).OrderBy(quarter => quarter.End).TakeLast(MemberReadings.Quarters).ToArray();

        if (operating.Length < MemberReadings.Quarters)
        {
            return null;
        }

        var interest = FirstFiledRevenue.Quarters(known, FiledFacts.InterestExpense);
        var paid = operating.Sum(quarter => Math.Abs(interest.FirstOrDefault(line => Math.Abs(line.End.DayNumber - quarter.End.DayNumber) <= SameQuarterWithin)?.Value ?? 0m));

        return paid == 0m || operating.Sum(quarter => quarter.Value) >= MemberReadings.CoverageFloor * paid;
    }

    public static IncomeCause CauseOf(IReadOnlyList<FiledFactRow>? facts, DateOnly session)
    {
        if (facts is null)
        {
            return IncomeCause.NoFiler;
        }

        FiledRevenue[] known = [.. facts.Where(fact => fact.Filed < session).Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))];
        var net = FirstFiledRevenue.Quarters(known, FiledFacts.NetIncome);

        if (net.Count < MemberReadings.Quarters)
        {
            return IncomeCause.FewerQuarters;
        }

        var operating = FirstFiledRevenue.Quarters(known, FiledFacts.OperatingIncome);

        if (operating.Count == 0)
        {
            return IncomeCause.NoOperatingLine;
        }

        return net.OrderBy(quarter => quarter.End).TakeLast(MemberReadings.Quarters).All(quarter => operating.Any(line => Math.Abs(line.End.DayNumber - quarter.End.DayNumber) <= SameQuarterWithin))
            ? IncomeCause.Read
            : IncomeCause.SomeQuarters;
    }
}
