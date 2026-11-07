using System.Text;

namespace EquityBrief.Core.News;

// One story the feed carried more than once: its title read without case, punctuation or spacing, and the day it was
// published, so two copies of one story under several links count once.
// see: A story the feed carried more than once is drawn and counted once, and the page says how many copies it merged
public static class NewsDuplicates
{
    public static string Key(string title, string published)
    {
        var words = new StringBuilder(title.Length);
        var gap = false;

        foreach (var letter in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(letter))
            {
                if (gap && words.Length > 0)
                {
                    words.Append(' ');
                }

                words.Append(letter);
                gap = false;
            }
            else
            {
                gap = true;
            }
        }

        return words.Append('|').Append(published.Length >= 10 ? published[..10] : published).ToString();
    }

    // The stories once each, in the order given: of a story's copies the first the caller prefers, a labelled one where the
    // labeller read one, and otherwise the first.
    public static IReadOnlyList<T> Once<T>(IEnumerable<T> stories, Func<T, string> title, Func<T, string> published, Func<T, bool>? preferred = null) =>
        [.. stories.GroupBy(story => Key(title(story), published(story)), StringComparer.Ordinal).Select(copies => copies.OrderBy(copy => preferred?.Invoke(copy) == true ? 0 : 1).First())];
}
