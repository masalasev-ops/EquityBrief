namespace EquityBrief.Core.Loop;

// What a proposal is built from in one fold, cut at the fold's end: only what ended before the fold's first session,
// and a read of any session at or past that session refused, so nothing built from the view can have read the year it
// is tested on. The engines read the ledger's setups through it and the searches their trades.
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public sealed class FoldView<T>
{
    readonly Func<T, DateOnly> enteredOf;
    readonly T[] learning;

    public FoldView(LoopFold fold, IEnumerable<T> items, Func<T, DateOnly> entered, Func<T, DateOnly?> ended)
    {
        Fold = fold;
        enteredOf = entered;
        learning = [.. items.Where(item => LoopFolds.Learns(fold, ended(item)))];
    }

    public LoopFold Fold { get; }

    // The first session the view refuses, the fold's own first.
    public DateOnly Cut => Fold.TestFrom;

    // Everything the fold learns on.
    public IReadOnlyList<T> Learning => learning;

    // What was entered over a span of sessions, refused where the span reaches the cut.
    public IReadOnlyList<T> Between(DateOnly from, DateOnly through)
    {
        Refuse(through);

        return [.. learning.Where(item => enteredOf(item) >= from && enteredOf(item) <= through)];
    }

    // What was entered on one session, refused at or past the cut.
    public IReadOnlyList<T> On(DateOnly session) => Between(session, session);

    void Refuse(DateOnly session)
    {
        if (session >= Cut)
        {
            throw new FoldReadRefused(Fold, session);
        }
    }
}

// A read past a fold's end: the session asked for and the fold's first, which the view refuses at and past.
public sealed class FoldReadRefused(LoopFold fold, DateOnly session) : InvalidOperationException(
    FormattableString.Invariant($"A read of {session:yyyy-MM-dd} was refused: the fold testing {fold.Year} is cut at {fold.TestFrom:yyyy-MM-dd}, and nothing a proposal is built from may read that session or a later one."))
{
    public LoopFold Fold { get; } = fold;

    public DateOnly Session { get; } = session;
}
