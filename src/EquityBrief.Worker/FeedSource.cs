namespace EquityBrief.Worker;

// Where a set of feeds comes from, decided before anything runs.
//
// One reading of the rule rather than one per set. 2.6 wrote it for the night's
// six feeds and 6.1 adds the on-demand ones, which resolve the same way and are
// not nightly: a second copy of five refusal branches is two chances to disagree
// about what a mistyped fixture path does, and what a mistyped path does is the
// whole of the property.
// see: The on-demand feeds are resolved in one place, as the nightly feeds are
//
// Both directions refuse and neither falls back, and that is the property rather
// than a courtesy. A fixture folder that does not exist must not resolve to the
// provider, because a mistyped path would then spend the allowance where a replay
// was meant. A live source with no key must not resolve to a capture, because a
// run that quietly replayed a capture would look exactly like one that ran.
public static class FeedSource
{
    public const string SourceKey = "EquityBrief:Providers:Source";

    public const string FixtureKey = "EquityBrief:Providers:Fixture";

    public const string Live = "live";

    public const string Fixture = "fixture";

    // The live default. A source nobody configured is the provider, because a
    // machine with no setting is the operator's own machine and a capture is the
    // thing asked for by name.
    public static T Resolve<T>(
        string? source,
        string? fixtureFolder,
        Func<string, T> fromFixture,
        Func<T> live,
        string what)
    {
        source = string.IsNullOrWhiteSpace(source) ? Live : source.Trim();

        if (string.Equals(source, Live, StringComparison.OrdinalIgnoreCase))
        {
            // The live builder makes the credentials, which refuse a blank key by
            // name. Nothing here catches that and reaches for a fixture.
            return live();
        }

        if (!string.Equals(source, Fixture, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{SourceKey}' is '{source}', and the two it may be are '{Live}' and " +
                $"'{Fixture}'. A source that is neither is refused rather than guessed at, " +
                $"because either guess is {what} that ran against something the operator did not " +
                "ask for.");
        }

        if (string.IsNullOrWhiteSpace(fixtureFolder))
        {
            throw new InvalidOperationException(
                $"'{SourceKey}' is '{Fixture}' and '{FixtureKey}' names no folder. {Capitalised(what)} " +
                "over a capture has to be told which, and falling back to the provider would spend " +
                "the allowance on a run that asked for a replay.");
        }

        return Directory.Exists(fixtureFolder)
            ? fromFixture(fixtureFolder)
            : throw new DirectoryNotFoundException(
                $"'{FixtureKey}' names '{fixtureFolder}' and no such folder exists. A mistyped path " +
                "is refused rather than resolved to the provider: the failure a fall-back would " +
                $"produce is {what} that reached the network when a replay was meant, and it would " +
                "look like one that ran.");
    }

    // The caller's own word for what it is resolving, at the start of a sentence.
    // The refusals read as prose the operator sees, and "a night over a capture"
    // and "A night over a capture" are the same word in two positions rather than
    // two strings to keep in step.
    static string Capitalised(string what) =>
        what.Length == 0 ? what : char.ToUpperInvariant(what[0]) + what[1..];
}
