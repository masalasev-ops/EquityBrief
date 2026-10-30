using EquityBrief.Core.Loop;

namespace EquityBrief.Worker.Loop;

// A family's rule as the three engines proposing a change of its hooks read it, winners against losers, the learned score
// and the idea generator: its family and words; the unit its evidence is counted in, the word for one of what it reads and
// the scale its figures are said at; the cap a block of it waits; every listing it reads with its edge, the session its
// path ended on and its readings; its own units over the whole history; its walk with a change's hooks on top of those it
// stands at; how a procedure's units are paired with its own over the test years; the live alarm's reference; and its
// finished trades' paths, which the generator's prompt reads. A swing family reads its listings and its trades in risks,
// and from 21.5 a book family the ledger's setups and its book's months in points.
// see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
public sealed record EngineRule(
    string Family,
    string Current,
    string Unit,
    string Thing,
    double Scale,
    int Cap,
    IReadOnlyList<RuleListing> Listings,
    Func<int, IReadOnlyList<double?>> ReadingsOf,
    IReadOnlyList<(int Entry, double? Edge)> Own,
    Func<IReadOnlyDictionary<string, double>, TreeModel?, IReadOnlyList<(int Entry, double? Edge)>> Walk,
    Func<IReadOnlyList<LoopFold>, IReadOnlyList<DateOnly>, IReadOnlyList<IReadOnlyList<(int Entry, double? Edge)>?>, IReadOnlyList<(int Entry, double? Edge)>, LoopEvidence> Evidence,
    IReadOnlyList<AlarmUnit>? Reference,
    IReadOnlyList<(DateOnly Ended, TradePath Path)> Finished)
{
    // A swing family's rule walk as the engines read it: its listings and its trades after their round trips, in risks.
    public static EngineRule Of(LoopRead read, RuleWalk rule, LoopReadings readings)
    {
        IReadOnlyList<double?> ReadingsOf(int at) => readings.Of(rule.Listings[at].Name, rule.Listings[at].Bar, rule.Listings[at].Session);

        IReadOnlyList<(int Entry, double? Edge)> own = [.. rule.Own.Select(one => (one.Entry, one.Edge))];

        return new EngineRule(
            rule.Family,
            rule.Current,
            LoopProcedures.Risks,
            "listing",
            1,
            rule.Cap,
            rule.Listings,
            ReadingsOf,
            own,
            (change, model) => [.. rule.Hooked(change, ReadingsOf, model).Select(one => (one.Entry, one.Edge))],
            LoopProcedures.Evidence,
            LoopProcedures.Reference(read, own),
            [.. rule.Own.Where(one => one.Exit is not null && one.Trade.Result is not null).Select(one => (read.Calendar[one.Exit!.Value], ExitProcedures.Path(read, rule.Closes, one)))]);
    }
}
