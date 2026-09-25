using System.Globalization;
using EquityBrief.Core.Filter;

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
    //
    // The levels and the ladder are in it because a night's values are read off
    // the bands and the plan those two write, so a change to either moves what a
    // registered condition would have fired on. The swing reader's and the swing
    // filter's files are in it because the swing family is evaluated through the
    // filter's gates over the readings the reader stores. That is what makes a registration
    // stall rather than drift: an evaluation under a rule the register does not
    // name is evidence about a different condition.
    public static IReadOnlyList<string> EvaluationSources { get; } =
    [
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Core/Indicators/IndicatorSeries.cs",
        "src/EquityBrief.Worker/Indicators/IndicatorEngine.cs",
        "src/EquityBrief.Core/Levels/LevelSeries.cs",
        "src/EquityBrief.Worker/Levels/LevelBuilder.cs",
        "src/EquityBrief.Core/Ladders/LadderSeries.cs",
        "src/EquityBrief.Worker/Ladders/LadderBuilder.cs",
        "src/EquityBrief.Worker/Shortlist/ShortlistBuilder.cs",
        "src/EquityBrief.Core/Candidates/NightValues.cs",
        "src/EquityBrief.Core/Candidates/NightReading.cs",
        "src/EquityBrief.Core/Candidates/ShadowColumn.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluators.cs",
        "src/EquityBrief.Core/Candidates/CandidateEvaluator.cs",
        "src/EquityBrief.Core/Filter/FilterSettings.cs",
        "src/EquityBrief.Core/Filter/SwingGates.cs",
        "src/EquityBrief.Core/Filter/SwingReadings.cs",
        "src/EquityBrief.Worker/Filter/SwingReader.cs",
        "src/EquityBrief.Worker/Filter/ListedTranche.cs",
        "src/EquityBrief.Worker/Filter/SwingFilter.cs",
        "src/EquityBrief.Core/Candidates/FamilyShadow.cs",
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

    // A value the night writes only where the name has the thing it describes,
    // and nothing where there is nothing to describe.
    //
    // Told apart from a value the night could not compute by a companion count
    // the evaluator reads as required: a name whose plan holds no buying zone
    // carries a zone count of nought and no edges, and a night that could not say
    // how many zones it held carries no count either and is refused before this
    // is reached. Without that count this would be the default `Required` exists
    // to refuse, read afterwards as a measurement.
    protected static double? Optional(CandidateNight night, string key) =>
        night.Values.TryGetValue(key, out var value) ? value : null;

    // A figure as a verdict records it, and what it records where the night wrote
    // none. The words rather than a blank, because a value that was never written
    // and a value of nought read the same way in a blank.
    protected static string Figure(double? value) =>
        value is { } figure ? figure.ToString("0.####", CultureInfo.InvariantCulture) : "not stored";
}

// A candidate evaluated in the swing filter's own stage over a member's gate inputs, rather than in the
// listings stage over the night's values: the swing family, whose rules are the filter's gates at other
// settings. The listings stage leaves it to the filter's stage, which is the shadow split by stage.
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
public abstract class GateEvaluator : CandidateEvaluator
{
    // Read nothing off a listings night, which never evaluates one.
    public override IReadOnlyList<string> Reads => [];

    public override CandidateVerdict Evaluate(CandidateNight night, IReadOnlyDictionary<string, double> parameters) =>
        throw new InvalidOperationException(
            $"{Name} is evaluated in the swing filter's stage over a member's gate inputs, and never over a listings night.");

    // The gate's verdict over one member's inputs, and the arrival window's longest reach its parameters ask.
    public abstract CandidateVerdict EvaluateGates(GateInputs inputs, IReadOnlyDictionary<string, double> parameters);

    public abstract int ArrivalSessions(IReadOnlyDictionary<string, double> parameters);
}
