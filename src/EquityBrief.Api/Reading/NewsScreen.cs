using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.News;
using EquityBrief.Core.Research;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the stored articles, their labels and the labeller's own rows to what the pages draw: a
// name's news region, tonight's counts and the Run page's labeller line and its count toward the nights its
// limits are settled from. It reads, counts and orders, and computes nothing a label holds.
// see: A screen reads and renders, and computes nothing
// see: The news labels alone name the model that wrote them
public static class NewsScreen
{
    // What a labeller's run row says in its detail: the night it labelled, the profile and the model, the
    // stop and the counts, the cost and the month's against the limit it ran under, or the line it was
    // refused with. A field the row does not carry reads as absent rather than as nought.
    public sealed record RunDetail(
        string? Session,
        string? Profile,
        string? Model,
        string? Stop,
        int Reached,
        int Names,
        int Labelled,
        IReadOnlyDictionary<string, int> Unreadable,
        int RefusedByAdmissibility,
        decimal Cost,
        decimal MonthCost,
        decimal? MonthLimit,
        string? Refused);

    public static RunDetail Detail(LabellerRunRow run)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(run.Detail) ? "{}" : run.Detail);
            var root = document.RootElement;

            var unreadable = new Dictionary<string, int>(StringComparer.Ordinal);

            if (root.TryGetProperty("unreadable", out var causes) && causes.ValueKind == JsonValueKind.Object)
            {
                foreach (var cause in causes.EnumerateObject())
                {
                    unreadable[cause.Name] = cause.Value.ValueKind == JsonValueKind.Number ? cause.Value.GetInt32() : 0;
                }
            }

            return new RunDetail(
                Text(root, "session"),
                Text(root, "profile"),
                Text(root, "model"),
                Text(root, "stop"),
                Whole(root, "reached"),
                Whole(root, "names"),
                Whole(root, "labelled"),
                unreadable,
                Whole(root, "refusedByAdmissibility"),
                Money(root, "cost") ?? 0m,
                Money(root, "monthCost") ?? 0m,
                Money(root, "monthLimit"),
                Text(root, "refused"));
        }
        catch (JsonException)
        {
            return new RunDetail(null, null, null, null, 0, 0, 0, new Dictionary<string, int>(StringComparer.Ordinal), 0, 0m, 0m, null, null);
        }
    }

    static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static int Whole(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    static decimal? Money(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;

    // The labeller's run for a night, the newest whose detail names the night: a run that labelled and one
    // refused before it asked anything both carry it.
    public static LabellerRunRow? RunFor(IReadOnlyList<LabellerRunRow> runs, DateOnly night)
    {
        var day = night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return runs.FirstOrDefault(run => string.Equals(Detail(run).Session, day, StringComparison.Ordinal));
    }

    const string Refused = "refused";

    // A name's news region for a night: each stored article of the window with the newest label written for
    // it, the counts the tabs and the bar draw, the model that wrote the newest label, and where the name
    // holds no label the one line saying why, read off the labeller's run for the night and the night's list.
    public static NewsView Build(string ticker, DateOnly night, IReadOnlyList<NewsArticleRow> articles, bool listed, LabellerRunRow? run)
    {
        var rows = new List<NewsRow>(articles.Count);

        foreach (var article in articles)
        {
            var state = article.Outcome switch
            {
                NewsLabelling.Labelled => NewsRow.Labelled,
                NewsLabelling.Unreadable => NewsRow.Unreadable,
                _ => string.Equals(article.Admissibility, Admissibility.Accepted, StringComparison.Ordinal) ? NewsRow.Unlabelled : NewsRow.Refused,
            };
            var labelled = state == NewsRow.Labelled;
            var opinion = labelled && string.Equals(article.Kind, NewsInstruction.Opinion, StringComparison.Ordinal);
            var tabs = opinion ? "opinion" : labelled ? "all " + article.Direction : "all";

            rows.Add(new NewsRow(
                article.ArticleId,
                article.Title,
                article.Source,
                article.PublishedAt.Length >= 10 ? article.PublishedAt[..10] : article.PublishedAt,
                article.Link,
                state,
                labelled ? article.Kind : null,
                labelled ? article.Direction : null,
                labelled ? article.Reason : null,
                state == NewsRow.Unreadable ? article.Cause : null,
                state == NewsRow.Refused ? article.Admissibility : null,
                tabs));
        }

        var counted = rows.Where(row => row.State == NewsRow.Labelled && row.Tabs != "opinion").ToArray();
        var newest = articles.FirstOrDefault(article => article.Outcome == NewsLabelling.Labelled && article.Model is not null);
        var (why, code) = rows.Count == 0
            ? ($"No article naming {ticker} was stored in the thirty days before this night.", "none")
            : counted.Length > 0 || rows.Any(row => row.State == NewsRow.Labelled)
            ? (null, null)
            : Why(ticker, listed, run, rows);

        return new NewsView(
            ticker,
            night,
            rows,
            rows.Count(row => row.Tabs.Split(' ').Contains("all", StringComparer.Ordinal)),
            counted.Count(row => row.Direction == "positive"),
            counted.Count(row => row.Direction == "negative"),
            counted.Count(row => row.Direction == "neutral"),
            rows.Count(row => row.Tabs == "opinion"),
            newest?.Model,
            newest?.Profile,
            why,
            code);
    }

    // Why a name holding articles holds no label for the night, in one line: the labeller has not run for
    // the night; it was refused before it asked, its key missing or its model not answering; the name was not
    // on the list, which is all the labeller reads; or the stop that came before this name.
    static (string? Why, string? Code) Why(string ticker, bool listed, LabellerRunRow? run, IReadOnlyList<NewsRow> rows)
    {
        if (run is null)
        {
            return ("The news labeller has not run for this night, so none of these is labelled.", "not-run");
        }

        var detail = Detail(run);

        if (string.Equals(run.Outcome, Refused, StringComparison.Ordinal))
        {
            var line = detail.Refused ?? "no reason recorded";

            return line.Contains("does not hold", StringComparison.Ordinal)
                ? ($"The news job's profile names a key the secrets file does not hold, so nothing was labelled: {line}", "key")
                : ($"The news labeller was refused before it asked anything, so nothing was labelled: {line}", "model");
        }

        if (!listed)
        {
            return ($"{ticker} was not on the list this night, and the labeller reads the names on the list alone.", "not-listed");
        }

        return detail.Stop switch
        {
            NewsLabelling.MonthLimitReached => ("The labeller's month limit was reached before this name, so its articles wait for next month.", "month-limit"),
            NewsLabelling.ModelUnreachable => ("The model could not be reached when the labeller ran, so this name's articles are unlabelled.", "model"),
            NewsLabelling.TimeLimitPassed => ("The labeller's time limit came before this name, so its articles wait for the next night.", "time-limit"),
            NewsLabelling.PeakWindowOpened => ("A peak window of the news profile opened before this name, so its articles wait for the next night.", "peak"),
            NewsLabelling.CapPaused => ("The day or month cap paused the labeller before this name, so its articles wait.", "cap"),
            _ => rows.All(row => row.State is NewsRow.Refused or NewsRow.Unreadable)
                ? ("The labeller reached this name and every article was refused by admissibility or could not be read.", "reached")
                : ("The labeller reached this name and labelled none of its articles.", "reached"),
        };
    }

    // The Run page's line for a night: the run that labelled it, the run refused for it, or none.
    public static LabellerLine? Line(DateOnly night, LabellerRunRow? run)
    {
        if (run is null)
        {
            return null;
        }

        var detail = Detail(run);
        var ran = !string.Equals(run.Outcome, Refused, StringComparison.Ordinal);

        return new LabellerLine(
            night,
            ran,
            detail.Profile,
            detail.Model,
            detail.Cost,
            detail.MonthCost,
            detail.MonthLimit,
            detail.Labelled,
            detail.Unreadable,
            detail.RefusedByAdmissibility,
            detail.Reached,
            detail.Names,
            detail.Stop,
            detail.Refused);
    }

    // The nights the labeller ran, against the twenty its time limit, its month limit and the share of
    // answers refused for a digit are settled from, with what those read so far: the median run in minutes,
    // the month's spend as the newest run states it, and the answers refused for a digit of every answer read.
    // owes: The news labeller's time limit settled from its first twenty nights
    // owes: The news labeller's month limit settled from its first twenty nights
    // owes: The share of the labeller's answers refused for a digit read after twenty nights
    public static TriggerLine Trigger(IReadOnlyList<LabellerRunRow> runs)
    {
        var ran = runs
            .Where(run => !string.Equals(run.Outcome, Refused, StringComparison.Ordinal))
            .Select(run => (Run: run, Detail: Detail(run)))
            .Where(pair => pair.Detail.Session is not null)
            .ToArray();
        var nights = ran.Select(pair => pair.Detail.Session!).Distinct(StringComparer.Ordinal).Count();

        if (nights == 0)
        {
            return new TriggerLine("news labeller", 0, NewsLabelling.SettlingNights, "night(s) the news labeller ran, against the twenty its time limit, its month limit and the share of answers refused for a digit are settled from: no run is recorded yet");
        }

        var minutes = ran
            .Select(pair => (Minutes(pair.Run.StartedAt, pair.Run.EndedAt)))
            .OfType<double>()
            .OrderBy(value => value)
            .ToArray();
        var median = minutes.Length == 0 ? 0 : minutes.Length % 2 == 1 ? minutes[minutes.Length / 2] : (minutes[(minutes.Length / 2) - 1] + minutes[minutes.Length / 2]) / 2;
        var answers = ran.Sum(pair => pair.Detail.Labelled + pair.Detail.Unreadable.Values.Sum());
        var digits = ran.Sum(pair => pair.Detail.Unreadable.GetValueOrDefault(NewsInstruction.DigitInTheReason));
        var month = ran[0].Detail.MonthCost;

        return new TriggerLine(
            "news labeller",
            nights,
            NewsLabelling.SettlingNights,
            FormattableString.Invariant($"night(s) the news labeller ran, against the twenty its time limit, its month limit and the share of answers refused for a digit are settled from: {median:0.#} minute(s) a run at the median, {Core.Spending.SpendVerdict.Money(month)} this month, {digits} of {answers} answer(s) refused for a digit"));
    }

    static double? Minutes(string startedAt, string endedAt) =>
        DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var started)
            && DateTimeOffset.TryParse(endedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ended)
            ? (ended - started).TotalMinutes
            : null;
}
