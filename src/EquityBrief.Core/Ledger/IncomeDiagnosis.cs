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
