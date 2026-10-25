using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Sweep;

// The command run after a sweep that records the answer its run states, which a card reads to say its sweep found no
// setting that passed the floors. It reads the run's own answer file and decides nothing itself: a run that states no
// answer, made before runs stated one or the pullback's base, which searches nothing, is refused with nothing written.
// Recording a run again writes its row again. From 20.11 it records beside the answer the figures the run's own figures
// file states for the setting its family would run, read as each sweep writes them, and, where a tester run is named
// with it, the tester's verdict on the family's search from that run's own figures, refusing with nothing written a
// tester run that is not one, covers another index or holds no verdict, or two, for the family.
// see: No family on any index is set aside or hidden by a test result without the operator's word
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
public sealed class SweepAnswers(IClock clock, string databaseFile, string sweepFolder, TextWriter output) : IComponent
{
    public const string Verb = "sweep-answer";

    public const string Stage = "sweep-answer";

    public const string RunPrefix = "sweep-answer-";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.SweepAnswer, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    // The indices a run may name and the families a card draws, which are the answers a card can read.
    static readonly string[] IndexCodes = [EquityBrief.Worker.Indices.IndexFamilies.LargeIndex, "MID", "SML"];

    static readonly string[] Families = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, HeavyweightRule.Name, QualityRule.Name, DipRule.Name];

    // From 22.11 the newest run in the sweep's folder that states an answer for a family on an index, by its folder's name,
    // which is the instant it started; none where no run states one. A remedy written before its run names it this way.
    // see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
    public string? NewestRun(string family, string index)
    {
        if (!Directory.Exists(sweepFolder))
        {
            return null;
        }

        foreach (var folder in Directory.GetDirectories(sweepFolder).Select(Path.GetFileName).OfType<string>().OrderByDescending(name => name, StringComparer.Ordinal))
        {
            var file = Path.Combine(sweepFolder, folder, SweepAnswer.File);

            try
            {
                if (File.Exists(file) && SweepAnswer.Read(File.ReadAllText(file)) is { } stated && stated.Family == family && stated.Index == index)
                {
                    return folder;
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    const string Write = @"
        INSERT INTO sweep_answer (run, index_code, family, design, answer, recorded_at, setting, trades, edge, year_edges, without_largest, settings_read, meeting, luck, tester_run, adjusted, tester_passed, ruled_on, entry_kept)
        VALUES ($run, $index, $family, $design, $answer, $recorded, $setting, $trades, $edge, $year_edges, $without_largest, $settings_read, $meeting, $luck, $tester_run, $adjusted, $tester_passed, $ruled_on, $entry_kept)
        ON CONFLICT (run) DO UPDATE SET
            index_code = excluded.index_code, family = excluded.family, design = excluded.design,
            answer = excluded.answer, recorded_at = excluded.recorded_at, setting = excluded.setting, trades = excluded.trades,
            edge = excluded.edge, year_edges = excluded.year_edges, without_largest = excluded.without_largest,
            settings_read = excluded.settings_read, meeting = excluded.meeting, luck = excluded.luck,
            tester_run = excluded.tester_run, adjusted = excluded.adjusted, tester_passed = excluded.tester_passed,
            ruled_on = excluded.ruled_on, entry_kept = excluded.entry_kept;";

    // A setting the operator ruled from among those a run read, in place of the one the run states: the day of the ruling,
    // the setting by the key the run names it by, and the entry level it is bought on, as the run's second stage words it,
    // where it names one.
    public sealed record RuledSetting(DateOnly On, string Setting, string? Entry);

    // The ruled setting the verb's options name, none where it names no ruling; a ruling with no setting or a day that
    // cannot be read names one the command refuses.
    public static RuledSetting? RuledOf(string? on, string? setting, string? entry) =>
        on is null && setting is null && entry is null
            ? null
            : new RuledSetting(
                DateOnly.TryParseExact(on ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : DateOnly.MinValue,
                setting ?? string.Empty,
                entry);

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);";

    public async Task<int> RecordAsync(string run, CancellationToken cancellation = default, string? tester = null, RuledSetting? ruled = null)
    {
        var refusal = Refusal(run, out var answer);
        AnswerFigures? figures = null;

        if (refusal is null)
        {
            (figures, refusal) = ruled is null ? FiguresOf(run, answer!) : RuledFiguresOf(run, ruled);
        }

        if (refusal is null && tester is not null)
        {
            (figures, refusal) = WithTester(figures, tester, answer!);
        }

        if (refusal is not null)
        {
            output.WriteLine($"{Verb}: {refusal}; nothing was recorded");

            return 2;
        }

        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = Write;
            command.Parameters.AddWithValue("$run", run);
            command.Parameters.AddWithValue("$index", answer!.Index);
            command.Parameters.AddWithValue("$family", answer.Family);
            command.Parameters.AddWithValue("$design", (object?)answer.Design ?? DBNull.Value);
            command.Parameters.AddWithValue("$answer", answer.Word);
            command.Parameters.AddWithValue("$recorded", Instant(started));
            command.Parameters.AddWithValue("$setting", (object?)figures?.Setting ?? DBNull.Value);
            command.Parameters.AddWithValue("$trades", (object?)figures?.Trades ?? DBNull.Value);
            command.Parameters.AddWithValue("$edge", (object?)figures?.Edge ?? DBNull.Value);
            command.Parameters.AddWithValue("$year_edges", figures is null ? DBNull.Value : JsonSerializer.Serialize(figures.YearEdges.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value)));
            command.Parameters.AddWithValue("$without_largest", (object?)figures?.WithoutLargest ?? DBNull.Value);
            command.Parameters.AddWithValue("$settings_read", (object?)figures?.SettingsRead ?? DBNull.Value);
            command.Parameters.AddWithValue("$meeting", (object?)figures?.Meeting ?? DBNull.Value);
            command.Parameters.AddWithValue("$luck", (object?)figures?.Luck ?? DBNull.Value);
            command.Parameters.AddWithValue("$tester_run", (object?)figures?.TesterRun ?? DBNull.Value);
            command.Parameters.AddWithValue("$adjusted", (object?)figures?.Adjusted ?? DBNull.Value);
            command.Parameters.AddWithValue("$tester_passed", figures?.TesterPassed is { } passed ? passed ? 1 : 0 : DBNull.Value);
            command.Parameters.AddWithValue("$ruled_on", figures?.RuledOn is { } on ? on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
            command.Parameters.AddWithValue("$entry_kept", figures?.EntryKept is { } kept ? kept ? 1 : 0 : DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", FormattableString.Invariant($"{RunPrefix}{run}-{started:yyyyMMddTHHmmss.fffffffZ}"));
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", Instant(started));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", "ok");
            command.Parameters.AddWithValue("$rows_written", 1);
            command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { run, index = answer.Index, family = answer.Family, design = answer.Design, answer = answer.Word, setting = figures?.Setting, tester = figures?.TesterRun }));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        output.WriteLine(
            $"recorded run {run}: {answer.Index} {answer.Family}{(answer.Design is { } design ? " design " + design : string.Empty)}, {answer.Word}"
            + (figures is null ? ", its figures stating no setting it would run" : FormattableString.Invariant($", the setting it would run {figures.Setting} at {figures.Trades:N0} trades"))
            + (figures?.TesterRun is { } testerRun ? FormattableString.Invariant($", the tester's run {testerRun} reading its search at an adjusted p of {figures.Adjusted:0.####}, {(figures.TesterPassed == true ? "passed" : "not passed")}") : string.Empty));

        return 0;
    }

    // The figures a run's own figures file states for the setting its family would run, read as each sweep writes them:
    // a breakout's or a drift's search on an index's own history its setting with its figures whole, its settings read
    // and those meeting the floors against what luck alone passes; a pullback's search its setting with its measures and
    // its edge without the five largest, stating no count of settings meeting the floors; a heavyweights design's search
    // its proposal's figures among its settings, with its settings and those meeting the floors against luck. None for a
    // run stating no such setting, every run before 20.11 among them, and a refusal for a file that cannot be read whole.
    (AnswerFigures? Figures, string? Refusal) FiguresOf(string run, SweepAnswer answer)
    {
        var file = Path.Combine(sweepFolder, run, IndexSweepRunner.FiguresFile);

        if (!File.Exists(file))
        {
            return (null, null);
        }

        try
        {
            using var read = JsonDocument.Parse(File.ReadAllText(file));
            var root = read.RootElement;

            static double? Edge(JsonElement edge) => edge.ValueKind == JsonValueKind.Number ? edge.GetDouble() : null;

            static IReadOnlyDictionary<int, double?> Years(JsonElement edges) =>
                edges.EnumerateArray()
                    .Select((edge, place) => (Year: SweepColumns.FirstScored.Year + place, Edge: Edge(edge)))
                    .ToDictionary(pair => pair.Year, pair => pair.Edge);

            static double? Number(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

            // From 22.11 the quality family's history test: its proposal's figures where one met its floors in months, and
            // its starting setting's otherwise, the setting a freeze then fixes, with its settings, those meeting its floors
            // against luck and the run's own tester's verdict.
            // see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
            if (root.TryGetProperty("family", out var named) && named.GetString() == QualityRule.Name && root.TryGetProperty("settings", out var grid))
            {
                var key = root.TryGetProperty("proposal", out var proposed) && proposed.ValueKind == JsonValueKind.String ? proposed.GetString()! : QualityRule.Starting.Key;

                return (QualityFiguresOf(run, root, [.. grid.EnumerateArray()], key), null);
            }

            if (root.TryGetProperty("wouldRun", out var wouldRun) && wouldRun.TryGetProperty("figures", out var figures))
            {
                var settings = root.GetProperty("afterCosts").EnumerateArray().ToArray();

                return (new AnswerFigures(
                    wouldRun.GetProperty("key").GetString()!,
                    figures.GetProperty("Trades").GetInt32(),
                    Number(figures, "Edge"),
                    Years(figures.GetProperty("YearEdge")),
                    Number(figures, "EdgeWithoutLargest"),
                    settings.Length,
                    settings.Count(setting => setting.GetProperty("MeetsFloors").GetBoolean()),
                    Number(root.GetProperty("luck"), "settings"),
                    null,
                    null,
                    null), null);
            }

            if (root.TryGetProperty("wouldRun", out var searched) && searched.TryGetProperty("after", out var after))
            {
                return (new AnswerFigures(
                    searched.GetProperty("key").GetString()!,
                    after.GetProperty("Scored").GetInt32(),
                    Number(after, "Edge"),
                    Years(after.GetProperty("YearEdge")),
                    searched.TryGetProperty("withoutTheLargest", out var trimmed) ? Number(trimmed, "Edge") : null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null), null);
            }

            if (root.TryGetProperty("proposal", out var proposal) && proposal.ValueKind == JsonValueKind.String && root.TryGetProperty("settings", out var book))
            {
                var settings = book.EnumerateArray().ToArray();
                var chosen = settings.Single(setting => setting.GetProperty("Key").GetString() == proposal.GetString()).GetProperty("After");

                return (new AnswerFigures(
                    proposal.GetString()!,
                    chosen.GetProperty("Trades").GetInt32(),
                    Number(chosen, "Edge"),
                    Years(chosen.GetProperty("YearEdge")),
                    Number(chosen, "EdgeWithoutLargest"),
                    settings.Length,
                    settings.Count(setting => setting.GetProperty("After").GetProperty("MeetsFloors").GetBoolean()),
                    HeavyweightSweep.Luck(settings.Length),
                    null,
                    null,
                    null), null);
            }

            return (null, null);
        }
        catch (Exception unread) when (unread is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return (null, $"run {run}'s figures cannot be read whole: {unread.Message}");
        }
    }

    // The figures a run's own figures file states for a setting the operator ruled from among the strongest settings its
    // search read, in place of the one the run states: where an entry is named, that setting read at that entry by the
    // search's second stage, its trades, its edge and its edge each year as the stage states them, with whether the stage
    // kept the entry; otherwise the setting as the first stage read it; and beside either the run's settings read and those
    // meeting the floors against luck. Refused where the ruling names no day or no setting, the run's figures hold no
    // search, the setting is not among its strongest or the entry was not read on it.
    (AnswerFigures? Figures, string? Refusal) RuledFiguresOf(string run, RuledSetting ruled)
    {
        if (ruled.On == DateOnly.MinValue || string.IsNullOrEmpty(ruled.Setting))
        {
            return (null, "a ruled setting names the day of the ruling as yyyy-MM-dd with '--ruled' and the setting by the key its run names it by with '--setting'");
        }

        var file = Path.Combine(sweepFolder, run, IndexSweepRunner.FiguresFile);

        if (!File.Exists(file))
        {
            return (null, $"run {run} holds no figures to read a ruled setting from");
        }

        try
        {
            using var read = JsonDocument.Parse(File.ReadAllText(file));
            var root = read.RootElement;

            static double? Number(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

            static double? Edge(JsonElement edge) => edge.ValueKind == JsonValueKind.Number ? edge.GetDouble() : null;

            static IReadOnlyDictionary<int, double?> Years(JsonElement edges) =>
                edges.EnumerateArray()
                    .Select((edge, place) => (Year: SweepColumns.FirstScored.Year + place, Edge: Edge(edge)))
                    .ToDictionary(pair => pair.Year, pair => pair.Edge);

            // From the answers of 2026-10-25 a quality family's history test: the ruled setting's figures among the settings
            // its grid walked, read as the family's own answer reads its proposal's, beside the run's own tester's verdict;
            // refused where the setting is not one the grid walked or an entry is named, which the family reads none of.
            // see: The S&P 400's and 600's quality rules are frozen at their starting settings on the operator's answer, as the S&P 500's was
            if (root.TryGetProperty("family", out var named) && named.GetString() == QualityRule.Name && root.TryGetProperty("settings", out var grid))
            {
                var walked = grid.EnumerateArray().ToArray();

                if (ruled.Entry is not null)
                {
                    return (null, $"run {run} is a quality history test, which reads no entry, so a ruled setting on it names none");
                }

                if (walked.All(setting => setting.GetProperty("Key").GetString() != ruled.Setting))
                {
                    return (null, $"'{ruled.Setting}' is not a setting run {run}'s history test walked");
                }

                return (QualityFiguresOf(run, root, walked, ruled.Setting) with { RuledOn = ruled.On }, null);
            }

            if (!root.TryGetProperty("strongestTen", out var strongest) || !root.TryGetProperty("afterCosts", out var afterCosts))
            {
                return (null, $"run {run}'s figures hold no search's strongest settings to read a ruled setting from");
            }

            var ten = strongest.EnumerateArray().ToArray();
            var place = Array.FindIndex(ten, one => one.GetProperty("setting").GetString() == ruled.Setting);

            if (place < 0)
            {
                return (null, $"'{ruled.Setting}' is not among the strongest settings run {run}'s search read");
            }

            var settings = afterCosts.EnumerateArray().ToArray();
            var meeting = settings.Count(setting => setting.GetProperty("MeetsFloors").GetBoolean());
            var luck = Number(root.GetProperty("luck"), "settings");

            if (ruled.Entry is null)
            {
                var first = ten[place].GetProperty("after");

                return (new AnswerFigures(ruled.Setting, first.GetProperty("Scored").GetInt32(), Number(first, "Edge"), Years(first.GetProperty("YearEdge")), null, settings.Length, meeting, luck, null, null, null, ruled.On), null);
            }

            var tried = root.GetProperty("tries").EnumerateArray()
                .FirstOrDefault(one => one.GetProperty("Dial").GetString() == EntryDial && one.GetProperty("Level").GetString() == ruled.Entry && one.GetProperty("Setting").GetInt32() == place);

            if (tried.ValueKind != JsonValueKind.Object)
            {
                return (null, $"run {run}'s second stage read no entry '{ruled.Entry}' on '{ruled.Setting}'");
            }

            var with = tried.GetProperty("with");
            var kept = root.GetProperty("crossedLevels").EnumerateArray().Any(level =>
                level.ValueKind == JsonValueKind.String ? level.GetString() == ruled.Entry : level.TryGetProperty("Level", out var words) && words.GetString() == ruled.Entry);

            return (new AnswerFigures($"{ruled.Setting} entering on {ruled.Entry}", with.GetProperty("Scored").GetInt32(), Number(with, "Edge"), Years(with.GetProperty("YearEdge")), null, settings.Length, meeting, luck, null, null, null, ruled.On, kept), null);
        }
        catch (Exception unread) when (unread is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return (null, $"run {run}'s figures cannot be read whole: {unread.Message}");
        }
    }

    // The second stage's dial a drift's entry is read on, as its tries name it.
    const string EntryDial = "entry";

    // A quality history test's figures for one setting of the grid it walked: its holdings, its edge, its edge each year
    // and without its five largest, the settings walked and those meeting its floors against luck, and the run's own
    // tester's verdict where it states one.
    static AnswerFigures QualityFiguresOf(string run, JsonElement root, JsonElement[] settings, string key)
    {
        static double? Number(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

        static double? Edge(JsonElement edge) => edge.ValueKind == JsonValueKind.Number ? edge.GetDouble() : null;

        var chosen = settings.Single(setting => setting.GetProperty("Key").GetString() == key).GetProperty("Figures");
        var tested = root.TryGetProperty("tested", out var verdict) && verdict.ValueKind == JsonValueKind.Object ? verdict : (JsonElement?)null;

        return new AnswerFigures(
            key,
            chosen.GetProperty("Trades").GetInt32(),
            Number(chosen, "Edge"),
            chosen.GetProperty("YearEdge").EnumerateArray()
                .Select((edge, place) => (Year: SweepColumns.FirstScored.Year + place, Edge: Edge(edge)))
                .ToDictionary(pair => pair.Year, pair => pair.Edge),
            Number(chosen, "EdgeWithoutLargest"),
            settings.Length,
            settings.Count(setting => setting.GetProperty("meetsFloors").GetBoolean()),
            Number(root, "luck"),
            tested is null ? null : run,
            tested is { } judged ? Number(judged.GetProperty("verdict"), "Adjusted") : null,
            tested is { } passed ? passed.GetProperty("passed").GetBoolean() : null);
    }

    // The tester's verdict on the family's search from a tester run's own figures: its run, its adjusted p and whether it
    // passed, the entry the run tested for the answer's family, and for the sector heavyweights for its design; refused
    // where the run is not one, is not a tester's run on the answer's index or holds no entry, or two, for the family.
    (AnswerFigures? Figures, string? Refusal) WithTester(AnswerFigures? figures, string tester, SweepAnswer answer)
    {
        if (figures is null)
        {
            return (null, $"run {tester} is named as the tester's, and the answer's run states no setting it would run for the tester's verdict to stand beside");
        }

        if (!SweepFolder.IsRunName(tester))
        {
            return (null, $"'{tester}' is not a run's name, the instant it started as yyyyMMddTHHmmssZ");
        }

        var file = Path.Combine(sweepFolder, tester, IndexSweepRunner.FiguresFile);

        if (!File.Exists(file))
        {
            return (null, $"the sweep's folder holds no figures of a run {tester}");
        }

        try
        {
            using var read = JsonDocument.Parse(File.ReadAllText(file));
            var root = read.RootElement;

            if (root.GetProperty("family").GetString() != IndexSweepRunner.TesterOwn)
            {
                return (null, $"run {tester} is not a tester's run on an index's own history");
            }

            if (root.GetProperty("index").GetString() != answer.Index)
            {
                return (null, $"run {tester} read the index '{root.GetProperty("index").GetString()}', and the answer is the {answer.Index}'s");
            }

            var design = answer.Design switch { "a" => Loop.OwnProcedures.DesignA, "b" => Loop.OwnProcedures.DesignB, _ => null };
            var entries = root.GetProperty("tested").EnumerateArray()
                .Where(entry => entry.GetProperty("Family").GetString() == answer.Family && (design is null || entry.GetProperty("Proposal").GetString() == design))
                .ToArray();

            if (entries.Length != 1)
            {
                return (null, FormattableString.Invariant($"run {tester} holds {entries.Length} verdicts for the {answer.Family}{(design is null ? string.Empty : ", " + design)}, and one is read"));
            }

            var verdict = entries[0].GetProperty("verdict");

            return (figures with
            {
                TesterRun = tester,
                Adjusted = verdict.TryGetProperty("Adjusted", out var adjusted) && adjusted.ValueKind == JsonValueKind.Number ? adjusted.GetDouble() : null,
                TesterPassed = entries[0].GetProperty("passed").GetBoolean(),
            }, null);
        }
        catch (Exception unread) when (unread is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, $"run {tester}'s figures cannot be read whole: {unread.Message}");
        }
    }

    // Why a run cannot be recorded, or none where it can, with the answer it states.
    string? Refusal(string run, out SweepAnswer? answer)
    {
        answer = null;

        if (!SweepFolder.IsRunName(run))
        {
            return $"'{run}' is not a run's name, the instant it started as yyyyMMddTHHmmssZ";
        }

        var folder = Path.Combine(sweepFolder, run);

        if (!Directory.Exists(folder))
        {
            return $"the sweep's folder holds no run {run}";
        }

        var file = Path.Combine(folder, SweepAnswer.File);

        if (!File.Exists(file))
        {
            return $"run {run} states no answer, being a run made before runs stated one or the pullback's base, which searches nothing; run its sweep again from this build";
        }

        try
        {
            answer = SweepAnswer.Read(File.ReadAllText(file));
        }
        catch (JsonException)
        {
            answer = null;
        }

        if (answer is null || string.IsNullOrEmpty(answer.Index) || string.IsNullOrEmpty(answer.Family))
        {
            answer = null;

            return $"run {run}'s answer cannot be read";
        }

        if (!IndexCodes.Contains(answer.Index, StringComparer.Ordinal))
        {
            return $"run {run} names the index '{answer.Index}', which no page reads";
        }

        if (!Families.Contains(answer.Family, StringComparer.Ordinal))
        {
            return $"run {run} names the family '{answer.Family}', which no card draws";
        }

        return null;
    }

    static string Instant(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
