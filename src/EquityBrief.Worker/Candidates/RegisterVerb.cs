using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Candidates;

// The `register` verb: a candidate condition registered before anything scores it, and a
// retirement. Every attempt is one row on the run log, written by the registrar, a refusal of
// the command line included.
//
// A verb rather than a step, because a registration is a decision a person takes and never
// something a night arrives at: a register that filled itself would be the thing
// pre-registration exists to stop. Its own class rather than a local function in the program,
// so the suite runs the verb a person runs.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public static class RegisterVerb
{
    public const string Name = "register";

    public const string RunPrefix = "register-";

    // The flag that registers phase 10's three at one instant, which takes nothing else: what
    // would be typed after it is written down in the set it names.
    public const string TheThree = "--the-three";

    // The flag that retires phase 10's three and registers the swing family at one instant.
    public const string TheFamily = "--the-family";

    // The flag that registers again, unchanged and at one instant, every standing candidate whose
    // evaluator a code change moved, on the evidence given.
    public const string Moved = "--moved";

    public static IReadOnlyList<VerbForm> Forms { get; } =
    [
        new("--candidate", ["--candidate", "--rule", "--test", "--evaluator"], ["--parameters"], []),
        new("--retire", ["--retire", "--evidence"], [], []),
        new(TheThree, [], [], [TheThree]),
        new(TheFamily, [], [], [TheFamily]),
        new(Moved, ["--evidence"], [], [Moved]),
    ];

    // The run id, to the ten-millionth of a second, so two commands a second apart never share one.
    public static string RunIdAt(DateTimeOffset at) => FormattableString.Invariant($"{RunPrefix}{at:yyyyMMddTHHmmss.fffffffZ}");

    public static async Task<int> RunAsync(
        string[] args,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (VerbStore.Refusal(databaseFile) is { } refused)
        {
            await error.WriteLineAsync("register: " + refused);

            return 1;
        }

        var registrar = new CandidateRegistrar(clock, databaseFile);
        var runId = RunIdAt(clock.UtcNow);

        try
        {
            return await FormAsync(args, registrar, runId, output, error);
        }
        catch (SqliteException collided) when (collided.SqliteErrorCode == 19 && collided.Message.Contains("run_log", StringComparison.Ordinal))
        {
            await error.WriteLineAsync(
                "register: another command wrote under the same run id at this instant, and nothing this one asked for was written. Run it again.");

            return 1;
        }
    }

    static async Task<int> FormAsync(
        string[] args,
        CandidateRegistrar registrar,
        string runId,
        TextWriter output,
        TextWriter error)
    {
        var (form, refusal) = VerbArguments.FormOf(args, Forms);

        if (form is null)
        {
            return await RefusedAsync(registrar, runId, refusal!, error);
        }

        string Given(string flag) => VerbArguments.Value(args, flag)!;

        if (form.Flag == "--retire")
        {
            return await Said(await registrar.RetireAsync(Given("--retire"), Given("--evidence"), runId), output, error);
        }

        if (form.Flag == TheFamily)
        {
            return await Said(await registrar.RegisterTheFamilyAsync(runId), output, error);
        }

        if (form.Flag == Moved)
        {
            return await Said(await registrar.RegisterMovedAgainAsync(Given("--evidence"), runId), output, error);
        }

        if (form.Flag == TheThree)
        {
            return await Said(await registrar.RegisterTogetherAsync(TheThreeCandidates.All, runId), output, error);
        }

        IReadOnlyDictionary<string, double> parameters;

        try
        {
            parameters = VerbArguments.Parameters(VerbArguments.Value(args, "--parameters"));
        }
        catch (FormatException unreadable)
        {
            return await RefusedAsync(registrar, runId, unreadable.Message, error);
        }

        return await Said(
            await registrar.RegisterAsync(Given("--candidate"), Given("--rule"), Given("--test"), Given("--evaluator"), parameters, runId),
            output,
            error);
    }

    static async Task<int> RefusedAsync(CandidateRegistrar registrar, string runId, string refusal, TextWriter error)
    {
        await registrar.RecordRefusalAsync(runId, refusal);
        await error.WriteLineAsync("register: " + refusal);

        return 1;
    }

    static async Task<int> Said(RegistrationOutcome outcome, TextWriter output, TextWriter error)
    {
        if (outcome.Outcome == CandidateRegistrar.Refused)
        {
            await error.WriteLineAsync("register: " + outcome.Detail);

            return 1;
        }

        await output.WriteLineAsync("register: " + outcome.Detail);

        return 0;
    }
}
