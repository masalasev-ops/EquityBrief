using System.Globalization;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Rules;

// The `version` verb: a ladder rule's live window opened, a version opened beside
// it, one closed naming what replaced it, and the open windows listed.
//
// Its own class rather than a local function in the program, so the suite runs
// the verb a person runs. Every refusal is the scorer's, which records it on the
// run log; what this adds is reading the command line and saying what happened.
// see: A ladder rule's version is measured beside that rule's live window, and both count against the bound
public static class VersionVerb
{
    public const string Name = "version";

    public static async Task<int> RunAsync(
        string[] args,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        var scorer = new RuleVersionScorer(clock, databaseFile);
        var runId = FormattableString.Invariant($"version-{clock.UtcNow:yyyyMMddTHHmmssZ}");

        if (VerbArguments.Has(args, "--list"))
        {
            var open = RuleVersions.OpenAt(await scorer.VersionsAsync(), clock.UtcNow);
            var now = RuleVersionScorer.HashesNow();

            await output.WriteLineAsync(FormattableString.Invariant(
                $"version: {open.Count} open window(s) of the {RuleVersions.MostAtOnce} the bound allows at once"));

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

        // A past night scored under the windows open now, outside any night's
        // deadline. Each score a window gets for a night before it opened is
        // flagged in sample by the scorer and counts toward nothing, which is what
        // lets a version added later be seen against nights it could not have
        // been fitted to without those nights becoming its evidence.
        if (VerbArguments.Value(args, "--backfill") is { } named)
        {
            if (!DateOnly.TryParseExact(named, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session))
            {
                await error.WriteLineAsync($"version: '--backfill {named}' is not a session in the form yyyy-MM-dd.");

                return 1;
            }

            try
            {
                var scored = await scorer.RunAsync(session, runId);

                await output.WriteLineAsync(
                    "version: scored " + session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                    FormattableString.Invariant($" under {scored.Versions} open window(s), {scored.RowsWritten} score(s) over {scored.NamesScored} name(s)"));

                return 0;
            }
            catch (InvalidOperationException stopped)
            {
                await error.WriteLineAsync("version: " + stopped.Message);

                return 1;
            }
        }

        var rule = VerbArguments.Value(args, "--rule");

        if (string.IsNullOrWhiteSpace(rule))
        {
            await error.WriteLineAsync(
                "version: '--rule' names the ladder rule a window measures, and every form but '--list' needs one. " +
                $"Rules carried: {string.Join(", ", LadderRules.All.Select(one => $"'{one}'"))}.");

            return 1;
        }

        if (VerbArguments.Value(args, "--close") is { } closing)
        {
            var refused = await scorer.CloseAsync(rule, closing, VerbArguments.Value(args, "--replaced-by"), runId);

            return await Said(refused, $"closed '{closing}' of '{rule}'", output, error);
        }

        if (VerbArguments.Has(args, "--live-window"))
        {
            return await Said(await scorer.OpenLiveAsync(rule, runId), $"opened the live window of '{rule}'", output, error);
        }

        var version = VerbArguments.Value(args, "--version");

        if (string.IsNullOrWhiteSpace(version))
        {
            await error.WriteLineAsync(
                "version: give '--live-window' to open a rule's live window, '--version <name> --parameters <name=value,...>' " +
                "to open a version beside it, '--close <name>' to close one, or '--list'.");

            return 1;
        }

        IReadOnlyDictionary<string, double> parameters;

        try
        {
            parameters = VerbArguments.Parameters(VerbArguments.Value(args, "--parameters"));
        }
        catch (FormatException refusal)
        {
            await error.WriteLineAsync("version: " + refusal.Message);

            return 1;
        }

        return await Said(
            await scorer.OpenAsync(rule, version, parameters, runId),
            $"opened '{version}' of '{rule}' with {RuleVersions.Write(parameters)}",
            output,
            error);
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
