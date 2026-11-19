using System.Globalization;

namespace EquityBrief.Web.Marks;

// One revision of a rule as the pages draw it: its number, the first session it ran, the last where a later revision followed
// it, none while it runs, and what it changed from the revision before in the words the rule's settings are drawn in, none
// for the first.
// see: A rule changes only through a new revision, a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public sealed record RevisionView(int Number, DateOnly From, DateOnly? Until, string? Changed)
{
    // What a revision changed where its settings differ only in one the page draws no words for.
    public const string CodeOnly = "a setting the page draws no words for";

    public bool Running => Until is null;

    public string Name => Named(Number);

    public static string Named(int number) => "Revision " + number.ToString(CultureInfo.InvariantCulture);

    // "Revision 9, running since 16 Nov 2026", or "Revision 8, ran 30 Oct to 13 Nov 2026" for one no longer running.
    public string Tag => Until is { } until
        ? Name + ", ran " + Ran(From, until)
        : Name + ", running since " + Day(From);

    // The sessions it ran, the year once where both fall in it.
    public static string Ran(DateOnly from, DateOnly until) =>
        from.Year == until.Year
            ? from.ToString("d MMM", CultureInfo.InvariantCulture) + " to " + Day(until)
            : Day(from) + " to " + Day(until);

    public static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
