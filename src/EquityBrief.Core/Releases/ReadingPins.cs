using System.Globalization;

namespace EquityBrief.Core.Releases;

// One reading version as the pin's store holds it: the model, its file by its path under LM Studio's models folder,
// the file's SHA-256, size and last-modified instant, the runtime's name and version the probe's answer stated as the
// version was written, kept as a record and compared with nothing, the instruction's version, the seed, the answer's
// budget and the context the model is loaded at, the model's cutoff, the instant the version was written and the
// instant its history read completed, none until it has.
public sealed record ReadingPin(
    long Id,
    string Model,
    string File,
    string Sha256,
    long Size,
    DateTimeOffset Modified,
    string RuntimeName,
    string RuntimeVersion,
    string PromptVersion,
    int Seed,
    int AnswerTokens,
    int Context,
    DateOnly Cutoff,
    DateTimeOffset WrittenAt,
    DateTimeOffset? HistoryCompletedAt);

// The settings every reading is asked at, the same for every version: temperature zero, one seed, and an answer's
// budget enough for four words in one object.
public static class ReadingSettings
{
    public const double Temperature = 0;

    public const int Seed = 20261018;

    public const int AnswerTokens = 256;

    // How many times a reading is asked once more after an answer that cannot be read.
    public const int AskedAgain = 1;
}

// What a run compares with the newest pin before it reads, how it checks the model's file, and what it compares on
// each answer.
// see: The local run is a process of its own at two slots outside the night and refuses any model file or prompt but its pin, whatever runtime answers
public static class ReadingPins
{
    // The parts a refusal names, in the order a run compares them.
    public const string ModelPart = "the model";
    public const string FilePart = "the model's file";
    public const string PromptPart = "the prompt";
    public const string WindowPart = "the model's window";

    // The two checks of the model's file a run makes, named on its row.
    public const string WholeHash = "hashed whole";
    public const string SizeAndTime = "size and modified time";

    // Whether a run hashes the model's file whole: where its size or its modified instant differs from the pin's, and
    // on a run in an ISO week of UTC no run has hashed it whole in yet. Otherwise the size and the modified instant are
    // compared alone, a file of the same size written at the same instant read as the same file.
    public static bool HashesWhole(ReadingPin pin, long size, DateTimeOffset modified, DateTimeOffset? lastHashedWhole, DateTimeOffset now) =>
        size != pin.Size
        || modified.UtcTicks != pin.Modified.UtcTicks
        || lastHashedWhole is not { } last
        || WeekOf(last) != WeekOf(now);

    public static (int Year, int Week) WeekOf(DateTimeOffset at) =>
        (ISOWeek.GetYear(at.UtcDateTime), ISOWeek.GetWeekOfYear(at.UtcDateTime));

    // The first part a run differs from the pin in before it asks anything, or none: the model its profile names, the
    // file's hash where the run hashed it whole or else its size and modified instant, the instruction's version, and the
    // window its profile loads the model at.
    // see: A reading is taken only at the window its pin names, and the model is loaded again at it wherever it is held at another
    public static string? Differs(ReadingPin pin, string model, string? sha256, long size, DateTimeOffset modified, string promptVersion, int window)
    {
        if (!string.Equals(model, pin.Model, StringComparison.Ordinal))
        {
            return ModelPart;
        }

        var file = sha256 is { } hashed
            ? !string.Equals(hashed, pin.Sha256, StringComparison.Ordinal)
            : size != pin.Size || modified.UtcTicks != pin.Modified.UtcTicks;

        if (file)
        {
            return FilePart;
        }

        if (!string.Equals(promptVersion, pin.PromptVersion, StringComparison.Ordinal))
        {
            return PromptPart;
        }

        return window == pin.Context ? null : WindowPart;
    }

    // The part an answer differs from the pin in, or none: the model that answered, since the runtime answers a request
    // naming a model it does not hold with whatever model it has loaded. The runtime that served the answer is no part,
    // since LM Studio updates it by itself and an update leaves the model's file, which the pin hashes, as it was.
    public static string? AnswerDiffers(ReadingPin pin, string? model) =>
        string.Equals(model, pin.Model, StringComparison.Ordinal) ? null : ModelPart;
}
