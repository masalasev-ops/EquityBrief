namespace EquityBrief.Core.Components;

// What a component may do to a store.
//
// Insert, Update and Delete are the three operations SCHEMA.md declares
// ownership for, so a declaration reconciles against that table without a
// translation between two vocabularies. Read is the fourth because the read and
// write matrix asserts reads as well, and a blank cell there is a claim as much
// as a filled one.
[Flags]
public enum Touch
{
    None = 0,
    Read = 1,
    Insert = 2,
    Update = 4,
    Delete = 8,
}

// One member per table SCHEMA.md describes, the member name being the table name
// in Pascal case. component-access asserts the two sets against each other in
// both directions, so a table added to SCHEMA with no member here fails rather
// than going undeclared.
public enum Store
{
    Membership,
    Bar,
    Indicator,
    Swing,
    VolumeProfile,
    Level,
    Ladder,
    Move,
    Listing,
    ForwardReturn,
    Facts,
    Fundamentals,
    NewsPulse,
    ResearchSection,
    ThemeSection,
    SourceDocument,
    CandidateRegister,
    SeriesState,
    RunLog,
}

// The outside sources of section 5. A feed is not a store and has no column in
// the read and write matrix, so it is declared apart and reconciled against the
// catalogue's Reads cell alone. Saying that here rather than leaving it as an
// absence is the point: a reader looking for feeds in the matrix should find the
// reason they are not there.
public enum Feed
{
    IndexMembership,
    BulkPrice,
    HistoricalPrice,
    SplitsAndDividends,
    News,
    CompanyFinancials,
    FilingsArchive,
    ResearchModel,
    LocalModel,
}

public readonly record struct StoreTouch(Store Store, Touch Touch);

public sealed record ComponentAccess(
    IReadOnlyList<StoreTouch> Stores,
    IReadOnlyList<Feed> Feeds)
{
    // The empty declaration. A component that touches no store says so, which is
    // a claim and not an omission: the mark renderer's eleven blank matrix cells
    // are asserted as much as any filled one.
    public static ComponentAccess Nothing { get; } = new([], []);

    public Touch On(Store store) =>
        Stores
            .Where(entry => entry.Store == store)
            .Aggregate(Touch.None, (all, entry) => all | entry.Touch);
}

// A component of the catalogue in section 7.
//
// The declaration lives in the type because a declaration written beside the
// check that reads it is a second statement of one fact and nothing keeps the
// two together. It is an interface with a static abstract member rather than a
// plain static property, which is what CheckReach uses inside the suite: that
// one is discovered by the literal string "Reach", so a rename silently empties
// the population. A component declaration crosses an assembly boundary and is
// read reflectively from outside, so discovery is keyed on a type instead, and a
// type that opts in cannot then be missing the member.
public interface IComponent
{
    static abstract ComponentAccess Access { get; }
}
