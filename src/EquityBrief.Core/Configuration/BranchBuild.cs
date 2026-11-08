namespace EquityBrief.Core.Configuration;

// Whether a server about to start is a branch build on the live store. A branch build is a Debug build, which is what
// every branch's gates and tests build, or a build in a checkout whose HEAD names a branch other than main; the live store
// is the one under the checkout's own data folder. A build in a folder holding no repository, as the night's clean copy
// of main's commit, is judged by its configuration alone.
// see: A branch build is served over a copy of the store, and its server refuses to start on the live store
public static class BranchBuild
{
    public const string Main = "main";

    public const string LiveFolder = "data";

    // The exit code of a server refused, apart from a crash's.
    public const int Refused = 3;

    const string HeadRef = "ref: refs/heads/";

    // The branch a checkout's HEAD names, the commit it names where it is detached, or null where the folder holds no
    // repository.
    public static string? BranchOf(string? checkout)
    {
        var head = checkout is null ? null : Path.Combine(checkout, ".git", "HEAD");

        if (head is null || !File.Exists(head))
        {
            return null;
        }

        var named = File.ReadAllText(head).Trim();

        return named.StartsWith(HeadRef, StringComparison.Ordinal) ? named[HeadRef.Length..] : named;
    }

    // The live store's file under a checkout.
    public static string LiveStore(string checkout) =>
        Path.GetFullPath(Path.Combine(checkout, LiveFolder, StoreLocation.DatabaseFileName));

    // Why the server refuses to start, or null where it may: a branch build whose store is the live store's file.
    public static string? Refusal(string? checkout, string databaseFile, string? configuration, string? branch)
    {
        if (checkout is null || !string.Equals(Path.GetFullPath(databaseFile), LiveStore(checkout), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var debug = string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase);
        var offMain = branch is not null && !string.Equals(branch, Main, StringComparison.Ordinal);

        if (!debug && !offMain)
        {
            return null;
        }

        var which = debug && offMain ? $"a Debug build on the branch {branch}" : debug ? "a Debug build" : $"a build on the branch {branch}";

        return $"Refused to start: {which} would serve the live store at {LiveStore(checkout)}. A branch build is served over a copy of the store made for the read-back: copy the store into a folder of its own and set EquityBrief__DataRoot to that folder. The live store is served by main's Release build alone.";
    }
}
