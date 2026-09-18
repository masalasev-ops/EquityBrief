using System.Globalization;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Rules;

// The `version` verb: a ladder rule's live window opened, a version opened, replaced
// or closed with its evidence, a past night backfilled, and the open windows listed.
// Every attempt but a listing is one row on the run log, written by the scorer, a
// refusal of the command line included.
//
// Its own class rather than a local function in the program, so the suite runs
// the verb a person runs.
// see: A ladder rule's version is measured beside that rule's live window, and both count against the bound
public static class VersionVerb
{
    public const string Name = "version";

    public const string RunPrefix = "version-";

    public static IReadOnlyList<VerbForm> Forms { get; } =
    [
        new("--list", [], [], ["--list"]),
        new("--backfill", ["--backfill"], [], []),
        new("--live-window", ["--rule"], [], ["--live-window"]),
        new("--version", ["--rule", "--version", "--parameters"], [], []),
        new("--close", ["--rule", "--close", "--evidence"], [], []),
        new("--replace", ["--rule", "--replace", "--with", "--parameters", "--evidence"], [], []),
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
            await error.WriteLineAsync("version: " + refused);

            return 1;
        }

        var scorer = new RuleVersionScorer(clock, databaseFile);
        var runId = RunIdAt(clock.UtcNow);

        try
        {
            return await FormAsync(args, scorer, clock, runId, output, error);
        }
        catch (SqliteException collided) when (collided.SqliteErrorCode == 19 && collided.Message.Contains("run_log", StringComparison.Ordinal))
        {
            await error.WriteLineAsync(
                "version: another command wrote under the same run id at this instant, and nothing this one asked for was written. Run it again.");

            return 1;
        }
    }

    static async Task<int> FormAsync(
        string[] args,
        RuleVersionScorer scorer,
        IClock clock,
        string runId,
        TextWriter output,
        TextWriter error)
    {
        var (form, refusal) = VerbArguments.FormOf(args, Forms);

        if (form is null)
        {
            return await RefusedAsync(scorer, runId, refusal!, error);
        }

        string Given(string flag) => VerbArguments.Value(args, flag)!;

        IReadOnlyDictionary<string, double> parameters = new Dictionary<string, double>(StringComparer.Ordinal);

        if (form.Needs.Contains("--parameters", StringComparer.Ordinal))
        {
            try
            {
                parameters = VerbArguments.Parameters(Given("--parameters"));
            }
            catch (FormatException unreadable)
            {
                return await RefusedAsync(scorer, runId, unreadable.Message, error);
            }
        }

        return form.Flag switch
        {
            "--list" => await ListAsync(scorer, clock, output),
            "--backfill" => await BackfillAsync(Given("--backfill"), scorer, runId, output, error),
            "--live-window" => await Said(
                await scorer.OpenLiveAsync(Given("--rule"), runId),
                $"opened the live window of '{Given("--rule")}'",
                output,
                error),
            "--version" => await Said(
                await scorer.OpenAsync(Given("--rule"), Given("--version"), parameters, runId),
                $"opened '{Given("--version")}' of '{Given("--rule")}' with {RuleVersions.Write(parameters)}",
                output,
                error),
            "--close" => await Said(
                await scorer.CloseAsync(Given("--rule"), Given("--close"), Given("--evidence"), runId),
                $"closed '{Given("--close")}' of '{Given("--rule")}' with nothing replacing it",
                output,
                error),
            _ => await Said(
                await scorer.ReplaceAsync(Given("--rule"), Given("--replace"), Given("--with"), parameters, Given("--evidence"), runId),
                $"replaced '{Given("--replace")}' of '{Given("--rule")}' with '{Given("--with")}' {RuleVersions.Write(parameters)}",
                output,
                error),
        };
    }

    static async Task<int> ListAsync(RuleVersionScorer scorer, IClock clock, TextWriter output)
    {
        var open = RuleVersions.OpenAt(await scorer.VersionsAsync(), clock.UtcNow);
        var now = RuleVersionScorer.HashesNow();

        await output.WriteLineAsync(FormattableString.Invariant(
            $"version: {open.Count} open window(s) of the {RuleVersions.MostAtOnce} the bound allows at once"));

        foreach (var rule in LadderRules.All)
        {
            await output.WriteLineAsync(FormattableString.Invariant(
                $"  '{rule}' {open.Count(row => string.Equals(row.Rule, rule, StringComparison.Ordinal))} of {RuleVersions.MostFor(rule)}"));
        }

        foreach (var row in open)
        {
            await output.WriteLineAsync(
                $"  '{row.Rule}' '{row.Version}' {row.Parameters}, opened " +
                row.OpenedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

            // What the next night would stop on, read by the reader the night reads it with.
            foreach (var moved in RuleVersions.Drifted([row], now))
            {
                await output.WriteLineAsync("    the next night stops here: " + moved);
            }
        }

        return 0;
    }

    // A past night scored under the windows open now, outside any night's deadline,
    // keeping every score already stored. A score counts only for a session after the
    // New York date its window opened on, so a version opened later is seen against
    // nights it could not have been fitted to without those nights becoming its evidence.
    // see: A version's score counts only for a session after the New York date its window opened on
    static async Task<int> BackfillAsync(
        string named,
        RuleVersionScorer scorer,
        string runId,
        TextWriter output,
        TextWriter error)
    {
        if (!DateOnly.TryParseExact(named, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session))
        {
            return await RefusedAsync(scorer, runId, $"'--backfill {named}' is not a session in the form yyyy-MM-dd.", error);
        }

        if (await scorer.BackfillRefusalAsync(session) is { } refused)
        {
            return await RefusedAsync(scorer, runId, refused, error);
        }

        try
        {
            var scored = await scorer.BackfillAsync(session, runId);

            await output.WriteLineAsync(
                "version: scored " + session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + FormattableString.Invariant(
                    $" under {scored.Versions} open window(s): {scored.RowsWritten} score(s) written, {scored.RowsKept} already stored and kept, ") +
                FormattableString.Invariant(
                    $"over {scored.NamesScored} name(s), {scored.NamesNotComputed} left out with no bands or plan of that session, ") +
                FormattableString.Invariant(
                    $"{scored.NameNightsSkipped} name-night(s) skipped for a band set stored without member sources, {scored.RowsDropped} dropped"));

            return 0;
        }
        catch (InvalidOperationException stopped)
        {
            return await RefusedAsync(scorer, runId, stopped.Message, error);
        }
    }

    static async Task<int> RefusedAsync(RuleVersionScorer scorer, string runId, string refusal, TextWriter error)
    {
        await scorer.RecordRefusalAsync(runId, refusal);
        await error.WriteLineAsync("version: " + refusal);

        return 1;
    }

    static async Task<int> Said(string? refusal, string done, TextWriter output, TextWriter error)
    {
        if (refusal is not null)
        {
            await error.WriteLineAsync("version: " + refusal);

            return 1;
        }

        await output.WriteLineAsync("version: " + done);

        return 0;
    }
}
