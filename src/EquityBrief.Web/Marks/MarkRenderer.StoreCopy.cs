namespace EquityBrief.Web.Marks;

// The store's newest copy as the Run page states it: when it was made, its name and the folder it sits in read
// against the surface's own data root, or the folder's name alone where the store could not carry a path to it,
// how many copies the folder keeps, and the newest attempt where that attempt made no copy, with why.
public sealed record StoreCopyView(DateTimeOffset? MadeAt, string? Copy, string? Folder, int Kept, DateTimeOffset? FailedAt, string? Failed);

// The copies' rows as the surface read them, the newest copy among them or none where no row is recorded.
public sealed record StoreCopyRead(StoreCopyView? Newest);

public sealed partial class MarkRenderer
{
    // The store's newest copy in one line beneath anything to worry about: its time, its folder and the copies
    // kept, the newest attempt that made none with why, or that no copy is recorded yet.
    // see: The store is copied after every night once the night, its drain and its labeller have finished, and the newest three copies are kept, each opened and read before an older one is removed
    public string StoreCopyLine(StoreCopyView? copy)
    {
        if (copy is null)
        {
            return "<p class=\"store-copy\" data-copies=\"none\">No copy of the store is recorded yet. The night starts one after its labeller, and it waits until the night, its drain and its labeller have finished.</p>";
        }

        var folder = Escaped(copy.Folder ?? "a folder the store cannot name");
        var kept = copy.Kept == 1 ? "1 copy is kept" : FormattableString.Invariant($"{copy.Kept} copies are kept");
        var made = copy.MadeAt is { } at && copy.Copy is { } name
            ? FormattableString.Invariant($"The newest copy of the store was made at {at.UtcDateTime:HH:mm} UTC on {at.UtcDateTime:yyyy-MM-dd}, in {folder} as {Escaped(name)}, and opened and read; {kept}.")
            : "No copy of the store has been made yet.";
        var failed = copy.FailedAt is { } tried && copy.Failed is { } why
            ? FormattableString.Invariant($" The copy tried at {tried.UtcDateTime:HH:mm} UTC on {tried.UtcDateTime:yyyy-MM-dd} was not made: {Escaped(why)}.")
            : string.Empty;
        var stamp = copy.MadeAt is { } when ? FormattableString.Invariant($"{when.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}") : "none";
        var failing = copy.FailedAt is null ? "false" : "true";

        return FormattableString.Invariant($"<p class=\"store-copy\" data-made=\"{stamp}\" data-kept=\"{copy.Kept}\" data-failed=\"{failing}\">") + made + failed + "</p>";
    }
}
