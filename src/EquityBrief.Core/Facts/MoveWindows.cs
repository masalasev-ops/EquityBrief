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

    // One episode: stored moves whose spans share a session, which are one run of the price
    // measured from several starts, and the largest of them by its change, which is the move
    // the episode's cause is written for.
    public sealed record Episode(MoveWindow Largest, IReadOnlyList<MoveWindow> Moves);

    public const string PerCent = "per cent";

    // The stored moves as episodes, in the order of their largest moves' ends. Five-session
    // moves ranked by change overlap whenever one run carries several of the largest, so a
    // cause asked for per move would be asked once for each of them. A move whose start the
    // file does not carry is an episode of its own, since nothing can be shown to share a
    // session with it.
    public static IReadOnlyList<Episode> Episodes(IReadOnlyList<Fact> facts)
    {
        var changes = facts
            .GroupBy(fact => fact.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        decimal Change(MoveWindow move) =>
            changes.TryGetValue(move.Name + " " + PerCent, out var value)
            && decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                ? Math.Abs(parsed)
                : 0m;

        var episodes = new List<List<MoveWindow>>();

        foreach (var move in In(facts).OrderBy(move => move.From ?? move.To).ThenBy(move => move.To))
        {
            var last = episodes.Count > 0 ? episodes[^1] : null;

            if (last is not null
                && move.From is { } from
                && last.All(held => held.From is not null)
                && from <= last.Max(held => held.To))
            {
                last.Add(move);
            }
            else
            {
                episodes.Add([move]);
            }
        }

        return
        [
            .. episodes
                .Select(moves => new Episode(
                    moves.OrderByDescending(Change).ThenBy(move => move.To).First(),
                    moves))
                .OrderBy(episode => episode.Largest.To),
        ];
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
