using System.Globalization;

namespace EquityBrief.Core.Candidates;

// What a candidate condition is evaluated over: one name on one night, and the
// values that night computed for it.
//
// A dictionary rather than a typed record of every figure a reason might read,
// because the point of a candidate is that nobody knows yet which figures the
// next one wants, and a typed surface would make every new candidate a change to
// this file. What keeps that from becoming a bag of anything is the other half:
// an evaluator names the keys it needs and refuses a night that does not carry
// them, rather than reading a missing value as a zero and firing on it.
public sealed record CandidateNight(string Ticker, DateOnly Session, IReadOnlyDictionary<string, double> Values);

// What one evaluation produced: whether the condition fired, and the values that
// made it true or false.
//
// The values are carried whichever way it went, for the reason the listings row
// carries them for a live reason: a night where nothing fired is the night that
// tells you how close it came, and a record that kept only the fires cannot say.
public sealed record CandidateVerdict(bool Fired, IReadOnlyDictionary<string, string> Values);

// A registered candidate's evaluator: code the register names, rather than a
// rule written in prose that a later session re-implements from words.
//
// The version is the pin of the evaluator's own source and every source its evaluation runs through,
// and `register-append-only` fails where the pin and the constant disagree.
// see: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public abstract class CandidateEvaluator
{
    // The evaluator's name, as the register's `evaluator` column holds it.
    public abstract string Name { get; }

    // The pin of this evaluator's own source and the evaluation sources, as the register's `evaluator_version` column holds it.
    public abstract string Version { get; }

    // The value keys this evaluator reads. Declared rather than discovered,
    // because a night missing one of them has to refuse instead of evaluating
    // over whatever it does hold.
    public abstract IReadOnlyList<string> Reads { get; }

    // The parameter names this evaluator is registered with, in the order a
    // registration states them. A registration carrying a parameter this does not
    // name, or missing one it does, is refused at the write.
    public abstract IReadOnlyList<string> Parameters { get; }

    public abstract CandidateVerdict Evaluate(CandidateNight night, IReadOnlyDictionary<string, double> parameters);

    // The line that carries a version, which is the one line the pin is taken
    // over the absence of.
    //
    // A hash of the whole file including its own version could never be written
    // down: putting the computed value into the file changes the file and so
    // changes the value. Leaving this line out is what makes the pin settle.
    // Changing anything an evaluator does still moves the hash, which is the
    // property; changing the recorded version alone moves nothing, which is what
    // lets the recorded version be corrected to the value the check demands.
    public const string VersionDeclaration = "public override string Version =>";

    // The sources besides an evaluator's own that decide a value it reads or the verdict it returns, from the repository root.
    public static IReadOnlyList<string> EvaluationSources { get; } =
    [
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Core/Indicators/IndicatorSeries.cs",
        "src/EquityBrief.Worker/Indicators/IndicatorEngine.cs",
        "src/EquityBrief.Worker/Shortlist/ShortlistBuilder.cs",
        "src/EquityBrief.Core/Candidates/ShadowColumn.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluators.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluator.cs",
    ];

    // The pin, over an evaluator's own source first and then the evaluation sources in the order listed.
    public static string Pin(IEnumerable<string> sources) => SourcePin.Of(sources, VersionDeclaration);

    // A registration's parameters, as the register stores them and as an
    // evaluator is handed them. JSON of one flat object of numbers: a candidate
    // whose parameters are not numbers is a candidate whose registration cannot
    // be compared with the next one, and the divisor counts registrations.
    public static string Write(IReadOnlyDictionary<string, double> parameters) =>
        "{" + string.Join(
            ", ",
            parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => FormattableString.Invariant($"\"{pair.Key}\": {pair.Value}"))) + "}";

    public static IReadOnlyDictionary<string, double> Read(string parameters)
    {
        var read = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var part in parameters.Trim('{', '}', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOf(':', StringComparison.Ordinal);

            if (at < 0)
            {
                throw new FormatException(
                    $"'{part.Trim()}' in a registration's parameters is not a name and a value. A " +
                    "registration nobody can read back is a registration the shadow column cannot run.");
            }

            read[part[..at].Trim().Trim('"')] = double.Parse(part[(at + 1)..].Trim(), CultureInfo.InvariantCulture);
        }

        return read;
    }

    // The value an evaluator needs, or a refusal naming what was missing.
    //
    // Never a default. A candidate that read a missing figure as zero would fire
    // on the nights the night could not compute it, which is the population a
    // shadow evaluation is least able to tell apart from a real one afterwards.
    protected static double Required(CandidateNight night, string key) =>
        night.Values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(
                $"{night.Ticker} on {night.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} " +
                $"carries no '{key}', which this evaluator reads. A night missing a value is not a night " +
                "the condition did not fire on.");
}
