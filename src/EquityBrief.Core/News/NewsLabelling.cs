namespace EquityBrief.Core.News;

// The words the news labeller's rows carry, held where the worker that writes them and the read surface that
// draws them both reach: the labeller's run and stage names, a label row's two outcomes, the six stops a run
// names, and the window it reads. The worker's labeller states each as its own, by these.
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
public static class NewsLabelling
{
    // The labeller's own run, and the stage the night records starting it under.
    public const string RunPrefix = "label-news-";
    public const string Stage = "news-labels";
    public const string NightStage = "label-news";

    // The outcomes a label row carries.
    public const string Labelled = "labelled";
    public const string Unreadable = "unreadable";

    // The stops, in the words the row and the pages state.
    public const string Finished = "every name reached";
    public const string MonthLimitReached = "the labeller's month limit";
    public const string TimeLimitPassed = "the time limit";
    public const string PeakWindowOpened = "a peak window";
    public const string CapPaused = "the day or month cap";
    public const string ModelUnreachable = "the model could not be reached";

    // The window read before a night, and the most articles a name is sent for.
    public const int WindowDays = 30;
    public const int ArticlesAName = 20;

    // The nights the labeller's time limit, month limit and the share of answers refused for a digit are
    // settled from, which the Run page counts toward.
    public const int SettlingNights = 20;

    // The first day of the window before a night, as the article rows' published instants are compared to it.
    public static string WindowFrom(DateOnly night) => night.AddDays(-WindowDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string WindowTo(DateOnly night) => night.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // The latest instant a run started at an instant can end: its limit counted over the time outside the profile's peak
    // windows, which the labeller waits through rather than labels in, so a run started inside a window ends its limit
    // after the window, and one whose limit reaches the next window ends its limit after that. A profile naming no
    // windows ends at the start plus the limit.
    // see: The news labeller waits for the end of a peak window rather than stopping at one, and its time limit counts the time it labels
    public static DateTimeOffset EndsBy(DateTimeOffset start, TimeSpan limit, Providers.ResearchPricing? pricing)
    {
        if (pricing is null)
        {
            return start + limit;
        }

        var at = start;
        var left = limit;

        while (true)
        {
            at = pricing.OffPeakFrom(at);

            if (pricing.PeakOpensAfter(at) is not { } opens || opens - at >= left)
            {
                return at + left;
            }

            left -= opens - at;
            at = opens;
        }
    }
}
