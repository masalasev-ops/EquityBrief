using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Facts;

// One stored move as a facts file names it: the session its change was measured
// from and the session it ended on. `From` is absent where the file carries no
// start for the move, which is a move whose first close has left the stored year.
public sealed record MoveWindow(string Name, DateOnly? From, DateOnly To);

// A move's span, read back out of a facts file by the names the assembler writes.
//
// The names are held here rather than beside the assembler, because the claim
// checker in this project reads them and the assembler in the worker writes them,
// and a name spelled in two places is a span that stops being found without
// anything failing.
// see: A cause of a move rests only on a document published inside that move
public static class MoveWindows
{
    public const string Largest = "largest move";
    public const string Ranked = "move";
    public const string Ended = "session";
    public const string MeasuredFrom = "measured from";

    static readonly Regex EndedName = new(
        "^(?<prefix>" + Regex.Escape(Largest) + "|" + Regex.Escape(Ranked) + @" \d+) " + Regex.Escape(Ended) + "$",
        RegexOptions.Compiled);

    public static IReadOnlyList<MoveWindow> In(IReadOnlyList<Fact> facts)
    {
        var values = facts
            .GroupBy(fact => fact.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        var windows = new List<MoveWindow>();

        foreach (var fact in facts)
        {
            var named = EndedName.Match(fact.Name);

            if (!named.Success || Date(fact.Value) is not { } ended)
            {
                continue;
            }

            var prefix = named.Groups["prefix"].Value;

            windows.Add(new MoveWindow(
                prefix,
                values.TryGetValue(prefix + " " + MeasuredFrom, out var from) ? Date(from) : null,
                ended));
        }

        return windows;
    }

    // Whether a document published on a date falls inside a move.
    //
    // Both edges are inside. A publish date carries no time, so a release filed
    // after the close on the session a move was measured from is dated that session
    // and is what moved the next one, which is the fixture's own case: the release
    // filed after the close on 2026-08-18. The same reading lets in a document dated
    // the move's last session and published after its close, which cannot have
    // moved it, and that is the cost of a date without a time, stated rather than
    // guessed around.
    public static bool Holds(MoveWindow move, DateOnly published) =>
        move.From is { } from && published >= from && published <= move.To;

    static DateOnly? Date(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
