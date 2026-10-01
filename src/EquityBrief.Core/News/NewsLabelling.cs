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
}
