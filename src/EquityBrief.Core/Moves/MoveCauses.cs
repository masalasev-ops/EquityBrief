using EquityBrief.Core.News;

namespace EquityBrief.Core.Moves;

// The sessions a move spans: its first and its last session, the instant the close it is measured from was set and the
// instant its last session closed.
public sealed record MoveWindow(DateOnly First, DateOnly Last, DateTimeOffset From, DateTimeOffset Through);

// A story the news labeller labelled, as a move's cause reads it: when it was published, its headline, its source, its
// link, the way the labeller read it for the company and the kind of story it read it as.
public sealed record CauseStory(DateTimeOffset Published, string Title, string Source, string Link, string Direction, string? Kind);

// A filing the filings refresh stored for the company: the day it was filed, its form and its address at the archive.
public sealed record CauseFiling(DateOnly Filed, string Form, string Link);

// A print the earnings calendar files for the company: the day it was reported, when in the session, and the session the
// earnings rule takes for it.
public sealed record CausePrint(DateOnly ReportDate, string Timing, DateOnly Session);

// What a move's cause says and where it was taken from: research's sentences naming it, the labelled stories published
// inside it with the prints and filings dated inside it beside them, or those prints and filings alone, or none of them.
public sealed record MoveCause(
    string Kind,
    string? Written,
    IReadOnlyList<CauseStory> Stories,
    IReadOnlyList<CausePrint> Prints,
    IReadOnlyList<CauseFiling> Filings);

// A move's cause, taken in a stated order: research's sentences naming it where research has written them; otherwise the
// stories the news labeller labelled, an opinion piece aside, that were published after the close the move is measured
// from and by the close of its last session, with the prints and filings dated inside the move listed beneath them;
// otherwise the prints whose reaction session falls on its sessions and the filings filed on them; otherwise none, said as
// found in neither. Research is first because it is written from documents inside the move and checked against them, the
// labels next because each was read from one article, and the filings last because a filing inside a move is a fact
// about the calendar and not a reading of what moved the price.
// see: A move's cause is research's where written, then the labelled news inside it and then the filings dated inside it
public static class MoveCauses
{
    public const string Research = "research";

    public const string News = "news";

    public const string Filed = "filed";

    public const string Neither = "none";

    public static MoveCause For(
        MoveWindow window,
        string? written,
        IReadOnlyList<CauseStory> stories,
        IReadOnlyList<CausePrint> prints,
        IReadOnlyList<CauseFiling> filings)
    {
        if (written is { Length: > 0 })
        {
            return new MoveCause(Research, written, [], [], []);
        }

        CauseStory[] inside =
        [
            .. stories
                .Where(story => !NewsInstruction.IsOpinion(story.Kind))
                .Where(story => story.Published > window.From && story.Published <= window.Through)
                .OrderByDescending(story => story.Published),
        ];
        CausePrint[] reported = [.. prints.Where(print => print.Session >= window.First && print.Session <= window.Last).OrderBy(print => print.Session)];
        CauseFiling[] filed = [.. filings.Where(filing => filing.Filed >= window.First && filing.Filed <= window.Last).OrderBy(filing => filing.Filed)];

        return inside.Length > 0
            ? new MoveCause(News, null, inside, reported, filed)
            : reported.Length > 0 || filed.Length > 0
                ? new MoveCause(Filed, null, [], reported, filed)
                : new MoveCause(Neither, null, [], [], []);
    }

    // The instants the labels cover: when the first and the last labelled story was published, none where none was.
    public static (DateTimeOffset First, DateTimeOffset Last)? LabelsCover(IReadOnlyList<CauseStory> stories) =>
        stories.Count == 0 ? null : (stories.Min(story => story.Published), stories.Max(story => story.Published));
}
