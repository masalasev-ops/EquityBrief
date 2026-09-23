namespace EquityBrief.Api.Passes;

// The checkout this build sits in, which a relative data root and the phase report are
// read against.
//
// It was a member of the class that started a pass as a process, and outlived it. Where the
// surface is running is what a relative data root is resolved from, and what the worker's
// build a press starts the drain from is found beside.
// see: The whole system is a checkout and one database file
public static class Checkout
{
    // The solution file at the checkout's root, which is how the checkout is found from
    // wherever the surface's build output sits inside it.
    public const string SolutionFile = "EquityBrief.slnx";

    // The nearest directory at or above the given one holding the solution file, or none
    // where no such directory exists.
    public static string? Of(string from)
    {
        for (var directory = new DirectoryInfo(from); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
