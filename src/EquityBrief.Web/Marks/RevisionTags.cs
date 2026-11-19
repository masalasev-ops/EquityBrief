using System.Globalization;

namespace EquityBrief.Web.Marks;

// The tag every pick on Home, Tonight and Past picks carries under its setup's tag:
// the revision that placed it, "Revision x, running since <date>", or for a pick an earlier revision placed "Revision x, ran
// <date> to <date>", opening that revision on Paper trading. The revisions of each family on the index a page reads are
// handed in for the one request drawing it; a page drawn with none handed in draws no tag.
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public static class RevisionTags
{
    static readonly AsyncLocal<(string Universe, IReadOnlyDictionary<string, IReadOnlyList<RevisionView>> Families)?> Held = new();

    // The revisions every tag drawn in the current request reads, until the returned scope is disposed.
    public static IDisposable Use(string universe, IReadOnlyDictionary<string, IReadOnlyList<RevisionView>> families)
    {
        var before = Held.Value;

        Held.Value = (universe, families);

        return new Scope(() => Held.Value = before);
    }

    // Each family's revisions from its running rule's account.
    public static IReadOnlyDictionary<string, IReadOnlyList<RevisionView>> Of(IEnumerable<PaperAccountView> accounts) =>
        accounts.Where(account => account.Head && account.Revisions.Count > 0).GroupBy(account => account.Family, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Revisions, StringComparer.Ordinal);

    // The revision that placed a pick of a family listed on a night, none where none is handed in or none ran then.
    public static RevisionView? At(string family, DateOnly listed) =>
        Held.Value is { } held && held.Families.TryGetValue(family, out var revisions) ? revisions.LastOrDefault(revision => revision.From <= listed) : null;

    // The tag itself, a link to that revision on Paper trading; nothing where no revision is read. An order an earlier
    // revision placed and still waits on says so: "Placed by revision x, ran <date> to <date>".
    public static string For(string family, DateOnly listed, bool order = false)
    {
        if (Held.Value is not { } held || At(family, listed) is not { } revision)
        {
            return string.Empty;
        }

        var link = new PaperLink(held.Universe, Rule: family) { Pick = revision.Running ? null : PaperLink.RevisionPrefix + revision.Number.ToString(CultureInfo.InvariantCulture) };

        return "<a class=\"vtag\" data-revision=\"" + revision.Number.ToString(CultureInfo.InvariantCulture) + "\" data-family=\"" + System.Net.WebUtility.HtmlEncode(family) + "\" href=\"" + System.Net.WebUtility.HtmlEncode(link.Href) + "\">" + System.Net.WebUtility.HtmlEncode(order && !revision.Running ? PlacedBy(revision) : revision.Tag) + "</a>";
    }

    // When a rule's running revision started, as a record's line says it: "Revision 7 started running on 16 Nov 2026", or
    // the day its rule was registered where no revision is handed in.
    public static string StartedRunning(string family, DateOnly registered) =>
        At(family, DateOnly.MaxValue) is { } revision
            ? revision.Name + " started running on " + RevisionView.Day(revision.From)
            : "It started running on " + RevisionView.Day(registered);

    public static string PlacedBy(RevisionView revision) => "Placed by " + revision.Name.ToLowerInvariant() + ", ran " + RevisionView.Ran(revision.From, revision.Until!.Value);

    sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
