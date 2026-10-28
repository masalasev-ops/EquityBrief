namespace EquityBrief.Core.News;

// The words the news labeller's rows carry, held where the worker that writes them and the read surface that
// draws them both reach: the labeller's run and stage names, a label row's two outcomes, the stops a run names,
// and the window it reads. The worker's labeller states each as its own, by these.
// see: The news labeller is a process of its own the off-peak run starts, and labels on the local model under a pin of its own
public static class NewsLabelling
{
    // The labeller's own run, and the stage the night records starting it under.
    public const string RunPrefix = "label-news-";
    public const string Stage = "news-labels";
    public const string NightStage = "label-news";

    // The outcomes a label row carries.
    public const string Labelled = "labelled";
    public const string Unreadable = "unreadable";

    // The stops, in the words the row and the pages state. A peak window and the caps stopped only a paid run, and are
    // read on the rows the paid labeller wrote.
    public const string Finished = "every name reached";
    public const string MonthLimitReached = "the labeller's month limit";
    public const string TimeLimitPassed = "the time limit";
    public const string PeakWindowOpened = "a peak window";
    public const string CapPaused = "the day or month cap";
    public const string ModelUnreachable = "the model could not be reached";
    public const string NoPin = "no label pin";
    public const string PinDiffers = "the label pin differs";
    public const string BatchEnded = "the batch's end";
    public const string LockHeld = "the local model's lock";
    public const string NextLocalSlot = "the local run's next slot";

    // The session a batch over every stored story is recorded under, since it reads no night's window.
    public const string EveryStory = "every stored story";

    // The window read before a night.
    public const int WindowDays = 30;

    // The answer's budget a label is asked at on the local model, as a release's reading is: a key, a direction and one
    // sentence come well inside it, and an answer that runs past it is cut off rather than run on.
    public const int AnswerTokens = 256;

    // The nights the labeller's time limit, month limit and the share of answers refused for a digit are
    // settled from, which the Run page counts toward.
    public const int SettlingNights = 20;

    // The first day of the window before a night, as the article rows' published instants are compared to it.
    public static string WindowFrom(DateOnly night) => night.AddDays(-WindowDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string WindowTo(DateOnly night) => night.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // The latest instant a run started at an instant can end: the start plus its limit, which counts its wait for the
    // local model's lock. A paid run, whose profile named peak windows it waited through rather than labelled in, ended
    // its limit counted over the time outside them, so one started inside a window ended its limit after the window.
    // see: The news labeller waits for the local model's lock and stops before the local run's next slot, its time limit counted from its start
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
