namespace EquityBrief.Web.Marks;

// The store's newest copy as the Run page states it: when it was made, its name and the folder it sits in read
// against the surface's own data root, or the folder's name alone where the store could not carry a path to it,
// how many copies the folder keeps, the newest attempt where that attempt made no copy, with why, the newest copy
// started after them that has written no end, whether its start is older than a copy's longest wait and an hour,
// so it was ended before it finished, and the copies the newest copy found gone from the folder that no copy
// removed.
public sealed record StoreCopyView(
    DateTimeOffset? MadeAt,
    string? Copy,
    string? Folder,
    int Kept,
    DateTimeOffset? FailedAt,
    string? Failed,
    DateTimeOffset? StartedAt = null,
    bool Gone = false,
    IReadOnlyList<string>? Missing = null);

// The copies' rows as the surface read them, the newest copy among them or none where no row is recorded.
public sealed record StoreCopyRead(StoreCopyView? Newest);

public sealed partial class MarkRenderer
{
    // The store's newest copy in one line beneath anything to worry about: its time, its folder and the copies
    // kept, the newest attempt that made none with why, a copy started since that has written no end, waiting or
    // copying still or ended before it finished, the copies the newest copy found gone from its folder that no
    // copy removed, or that no copy is recorded yet.
    // see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
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
        var unfinished = copy.StartedAt is { } since
            ? copy.Gone
                ? FormattableString.Invariant($" The copy started at {since.UtcDateTime:HH:mm} UTC on {since.UtcDateTime:yyyy-MM-dd} wrote no end: it was ended before it finished, and made none.")
                : FormattableString.Invariant($" A copy started at {since.UtcDateTime:HH:mm} UTC on {since.UtcDateTime:yyyy-MM-dd} is waiting or copying, and has not written its end.")
            : string.Empty;
        var missing = copy.Missing is { Count: > 0 } gone
            ? FormattableString.Invariant($" {(gone.Count == 1 ? "1 copy the copy before kept is" : gone.Count + " copies the copy before kept are")} gone from the folder, removed by no copy: {Escaped(string.Join(", ", gone))}.")
            : string.Empty;
        var stamp = copy.MadeAt is { } when ? FormattableString.Invariant($"{when.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}") : "none";
        var failing = copy.FailedAt is null ? "false" : "true";
        var started = copy.StartedAt is { } open ? FormattableString.Invariant($"{open.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}") : "none";
        var state = copy.StartedAt is null ? "none" : copy.Gone ? "gone" : "open";

        return FormattableString.Invariant($"<p class=\"store-copy\" data-made=\"{stamp}\" data-kept=\"{copy.Kept}\" data-failed=\"{failing}\" data-started=\"{started}\" data-unfinished=\"{state}\" data-missing=\"{copy.Missing?.Count ?? 0}\">") + made + failed + unfinished + missing + "</p>";
    }
}
