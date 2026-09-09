namespace EquityBrief.Core.Providers;

// One constituent of an index, as the feed reports it tonight.
//
// Left is null while a name is a member, which is what SCHEMA declares for the
// membership row. A provider that reported only today's members and no dates
// could not fill this at all, and that is deliberate: the whole reason
// membership is fetched rather than maintained is that the feed carries the
// spans (see: The universe is the S&P 500, and membership is fetched, not maintained).
//
// Joined is null when the provider carries no join date, which it does more
// often than the fixture suggested. The live payload holds 822 spans and 145 of
// them have no StartDate, two of those being current members: IR and WAB are in
// tonight's snapshot of 503 and the provider will not say since when. The
// fixture's five constituents all carried one, so the parser threw on the first
// live night rather than on any test.
//
// The alternatives were to drop such a name, which takes two real members out
// of the index and out of every computation downstream, or to write a date
// nobody has, which is the thing this corpus refuses everywhere else. Null is
// what is true.
public sealed record IndexConstituent(string Ticker, DateOnly? Joined, DateOnly? Left);

// The index membership feed of section 5, behind an interface so the nightly
// path and the suite meet the same shape.
//
// It returns the whole index in one call rather than one call per name, which is
// what the zero-per-name rule requires of everything on the nightly path
// (see: The nightly run is arithmetic only).
public interface IIndexMembershipFeed
{
    // How many network requests this feed has made.
    //
    // On the interface rather than on the recorded double, so the live feed has
    // to answer the same question and the cost limit is asserted against
    // something measured on both paths. A caller that wrote the figure as a
    // literal would be recording its own intention: correct today, and still
    // reading one on the night a feed starts paging.
    int Requests { get; }

    Task<IReadOnlyList<IndexConstituent>> ConstituentsAsync(
        string indexCode,
        CancellationToken cancellationToken = default);
}
