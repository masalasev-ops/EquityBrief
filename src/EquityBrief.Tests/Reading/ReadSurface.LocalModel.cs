using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.1: the Run page's local model region, read back off the rendered page over a constructed store. One
// line naming the two slots where the local model has not run; and one row a run, newest first and at most ten, each
// with its kind, its start and end, what it fetched and read, its refused answers by cause, its median and slowest
// seconds a call, the pin it read by its model, its file's hash, the runtime and the prompt, the check of the model's file
// it made and what it came to with why, each from the detail the run's own writer writes; a row whose detail cannot be
// read drawn by its outcome alone, and a row under another stage drawn by none.
// see: The local run is a process of its own at two slots outside the night and refuses any model file or prompt but its pin, whatever runtime answers
public partial class ReadSurface
{
    // The rows 20.1 adds that this check reaches: section 15.10's region.
    internal static readonly string[] LocalModelRows =
    [
        CheckReach.Key("15.10 Run", "The local model's runs"),
    ];

    const string LocalModelCard = "<section class=\"card\" data-card=\"local-model\">.*?</section>";

    [Fact]
    public async Task TheRunPagesLocalModelRegionDrawsEachRunNewestFirstWithThePinItReadAndWhatItCameTo()
    {
        using var store = await FixtureExpectations.WithListings();
        var night = NightIn(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Region() =>
            Regex.Match(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{night}")), LocalModelCard, RegexOptions.Singleline).Value;

        // No run yet: the one line, naming each slot's start in UTC.
        Assert.Contains(
            "<p class=\"local-runs\" data-runs=\"0\">The local model has not run yet. It runs at 12:05 and 21:40 UTC each day, and by hand.</p>",
            await Region(),
            StringComparison.Ordinal);

        var pin = new ReadingPin(
            1,
            "google/gemma-4-26b-a4b-qat",
            "lmstudio-community/gemma-4-26B-A4B-it-QAT-GGUF/gemma-4-26B-A4B-it-QAT-Q4_0.gguf",
            "a586dac572e606b1f2ad0011f7465aa1dffdb0f6516c5b5809fab35857865389",
            14_439_363_264,
            DateTimeOffset.Parse("2026-04-20T10:00:00Z", CultureInfo.InvariantCulture),
            "llama.cpp-win-x86_64-nvidia-cuda12-avx2",
            "2.56.0",
            ReleaseQuestions.Version,
            ReadingSettings.Seed,
            ReadingSettings.AnswerTokens,
            32768,
            new DateOnly(2025, 1, 31),
            DateTimeOffset.Parse("2026-10-13T06:04:12Z", CultureInfo.InvariantCulture),
            null);

        static LocalRunOutcome Ran(string kind, string outcome, string? why = null, int read = 0, IReadOnlyDictionary<string, int>? refused = null, double? median = null, double? slowest = null, ReadingPin? pinned = null, string? check = null, int compared = 0, int identical = 0) =>
            new(kind, outcome, why, 0, read, refused ?? new Dictionary<string, int>(), median, slowest, pinned, check, null, compared, identical);

        // Twelve runs, oldest first, the detail each the run's own writer's: a slot whose runtime was not running and one
        // finding no pin, both past the ten drawn; the pin written and the stored releases read twice under it with three
        // answers refused; six slots finding nothing to read; a slot the pin refused; and a row whose detail cannot be read.
        const string Waiting = "no stored release is waiting for a reading under the pin";
        var rows = new List<(string Stage, string Outcome, string Started, string Ended, string Detail)>
        {
            (LocalSlots.Stage, ReadingRuntimeUnavailable.NotRunning, "2026-10-12T12:05:00Z", "2026-10-12T12:05:01Z", LocalRun.Detail(Ran(LocalRun.EarlySlot, ReadingRuntimeUnavailable.NotRunning, "LM Studio is not running"))),
            (LocalSlots.Stage, LocalRun.NoPin, "2026-10-12T21:40:00Z", "2026-10-12T21:40:01Z", LocalRun.Detail(Ran(LocalRun.LateSlot, LocalRun.NoPin, "no reading version is pinned yet"))),
            (LocalSlots.Stage, LocalRun.Finished, "2026-10-13T06:00:00Z", "2026-10-13T06:04:12Z", LocalRun.Detail(Ran(LocalRun.Pinning, LocalRun.Finished, pinned: pin, check: ReadingPins.WholeHash))),
            (LocalSlots.Stage, LocalRun.Finished, "2026-10-13T06:30:52Z", "2026-10-13T06:35:10Z", LocalRun.Detail(Ran(LocalRun.Twice, LocalRun.Finished, read: 70, refused: new Dictionary<string, int> { ["cut off"] = 2, ["not one object"] = 1 }, median: 3.21, slowest: 8.49, pinned: pin, check: ReadingPins.SizeAndTime, compared: 70, identical: 69))),
        };

        foreach (var day in new[] { 14, 15, 16 })
        {
            rows.Add((LocalSlots.Stage, LocalRun.NothingToRead, $"2026-10-{day}T12:05:00Z", $"2026-10-{day}T12:05:02Z", LocalRun.Detail(Ran(LocalRun.EarlySlot, LocalRun.NothingToRead, Waiting, pinned: pin, check: ReadingPins.SizeAndTime))));
            rows.Add((LocalSlots.Stage, LocalRun.NothingToRead, $"2026-10-{day}T21:40:00Z", $"2026-10-{day}T21:40:02Z", LocalRun.Detail(Ran(LocalRun.LateSlot, LocalRun.NothingToRead, Waiting, pinned: pin, check: ReadingPins.SizeAndTime))));
        }

        rows.Add((LocalSlots.Stage, LocalRun.PinRefused, "2026-10-17T21:40:00Z", "2026-10-17T21:43:31Z", LocalRun.Detail(Ran(LocalRun.LateSlot, LocalRun.PinRefused, "the model's file is not pin 1's", pinned: pin, check: ReadingPins.WholeHash))));
        rows.Add((LocalSlots.Stage, LocalRun.Finished, "2026-10-18T12:05:00Z", "2026-10-18T12:05:09Z", "not a detail the run writes"));

        // And a row under a stage whose name the local run's begins, which the region reads none of.
        rows.Add(("local-runs", LocalRun.Finished, "2026-10-18T13:00:00Z", "2026-10-18T13:00:01Z", LocalRun.Detail(Ran(LocalRun.Twice, LocalRun.Finished, read: 999))));

        foreach (var (row, at) in rows.Select((row, at) => (row, at)))
        {
            store.Execute($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, detail) VALUES ('local-run-constructed-{at:00}', '{row.Stage}', '{row.Started}', '{row.Ended}', '{row.Outcome}', '{row.Detail.Replace("'", "''", StringComparison.Ordinal)}');");
        }

        var region = await Region();
        var drawn = Regex.Matches(region, "<tr data-kind=\"([^\"]*)\" data-outcome=\"([^\"]*)\">(.*?)</tr>", RegexOptions.Singleline)
            .Select(match => (Kind: match.Groups[1].Value, Outcome: match.Groups[2].Value, Cells: Regex.Matches(match.Groups[3].Value, "<td>(.*?)</td>").Select(cell => Regex.Replace(cell.Groups[1].Value, "<[^>]+>", string.Empty)).ToArray()))
            .ToArray();

        // Each row's start and end each held on one line, a date and its time never broken apart on a narrow screen.
        Assert.Equal(10, Regex.Matches(region, "<td><span class=\"nw\">\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}</span> to <span class=\"nw\">\\d{2}:\\d{2} UTC</span></td>").Count);

        // Ten rows, newest first by the order the rows were written whatever the night the page is for, the card stamped as
        // read from the store, the eldest two not drawn and the other stage's row none.
        Assert.Contains("<span class=\"stamp computed\">Computed from the store</span>", region, StringComparison.Ordinal);
        Assert.Contains("<table class=\"local-runs\" data-runs=\"10\">", region, StringComparison.Ordinal);
        Assert.Equal(
            ["a run finished", "late slot pin refused",
             "late slot nothing to read", "early slot nothing to read", "late slot nothing to read", "early slot nothing to read", "late slot nothing to read", "early slot nothing to read",
             "twice finished", "pin finished"],
            drawn.Select(row => $"{row.Kind} {row.Outcome}"));
        Assert.DoesNotContain("LM Studio is not running", region, StringComparison.Ordinal);
        Assert.DoesNotContain("no reading version is pinned yet", region, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>999</td>", region, StringComparison.Ordinal);

        // Each column of a row as the run wrote it: the twice read's three refused answers by cause, its median and slowest
        // call to a tenth of a second, the pin by its model, the first twelve characters of its file's hash, the runtime and
        // the prompt's version, the check of the model's file, and how many of the releases it compared it read alike.
        var pinned = $"pin 1: google/gemma-4-26b-a4b-qat, file a586dac572e6, llama.cpp-win-x86_64-nvidia-cuda12-avx2 2.56.0, prompt {ReleaseQuestions.Version}";

        Assert.Equal(
            ["twice", "2026-10-13 06:30 to 06:35 UTC", "0", "70", "2 cut off, 1 not one object", "3.2 s, slowest 8.5 s", pinned, "size and modified time", "finished: 69 of 70 answered alike when read twice"],
            drawn[8].Cells);
        Assert.Equal(
            ["pin", "2026-10-13 06:00 to 06:04 UTC", "0", "0", "none", "none read", pinned, "hashed whole", "finished"],
            drawn[9].Cells);
        Assert.Equal(
            ["early slot", "2026-10-16 12:05 to 12:05 UTC", "0", "0", "none", "none read", pinned, "size and modified time", "nothing to read: " + Waiting],
            drawn[3].Cells);

        // A run the pin refused names what differed after what it came to.
        Assert.Equal(
            ["late slot", "2026-10-17 21:40 to 21:43 UTC", "0", "0", "none", "none read", pinned, "hashed whole", "pin refused: the model's file is not pin 1's"],
            drawn[1].Cells);

        // A row whose detail cannot be read is drawn by its outcome alone.
        Assert.Equal(
            ["a run", "2026-10-18 12:05 to 12:05 UTC", "0", "0", "none", "none read", "no pin read", "not checked", "finished"],
            drawn[0].Cells);
    }
}
