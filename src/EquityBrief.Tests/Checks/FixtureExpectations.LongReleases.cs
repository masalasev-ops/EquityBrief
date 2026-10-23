using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Data.Migrations;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from the 20.3 correction of 2026-10-22: the release a filing's page names, a release longer than
// the model's window, and a new version carrying what it would ask again unchanged. The exhibit a page names the
// release in its description or its file name, read off BXP's and DLR's captured pages and constructed documents; the
// runtime's refusal past the window read for the request's tokens and the context; a cut worked by hand; a reading cut
// and asked again with where it was cut and the request it sent kept; the check of the stored filings' exhibits over a
// recorded archive; the migration keeping every release and reading; and pin 4's carry over a constructed store, each
// release judged alone and counted by why it is read again.
// see: A results release is the exhibit its filing's index page names as the release, and the lowest exhibit only where none is named so
// see: A release longer than the model's window is read from its opening that fits, with a marker where it was cut
// see: A new reading version carries each reading whose request it would send unchanged and reads the rest
public partial class FixtureExpectations
{
    const string BxpCik = "0001037540";
    const string BxpJuly = "0001656423-25-000043";
    const string DlrCik = "0001297996";
    const string DlrFebruary = "0001104659-26-010887";

    [Fact]
    public void AFilingsReleaseIsTheExhibitItsPageNamesTheReleaseInItsDescriptionOrItsFileNameAndTheLowestWhereNoneIs()
    {
        // BXP's page of 2025-07-29 describes both its exhibits by type alone; its file names name the supplemental data
        // package at sequence 2 and the press release at sequence 3, so the release is the third, where the lowest was
        // the supplement.
        var bxp = SecEdgarArchive.Documents(Captured("filing-page-" + BxpJuly + ".htm"));

        Assert.Equal(["EX-99.1|q22025supplemental.htm", "EX-99.2|q22025pressrelease.htm"], bxp.Where(document => document.Type.StartsWith("EX-99", StringComparison.Ordinal)).Select(document => $"{document.Description}|{document.FileName}"));
        Assert.Equal("q22025supplemental.htm", SecEdgarArchive.Release(bxp)!.FileName);
        Assert.Equal("q22025pressrelease.htm", SecEdgarArchive.NamedRelease(bxp)!.FileName);

        // DLR's page of 2026-02-05 names neither of its two by more than its type, so the lowest stands: its financial
        // supplement, which carries the earnings release inside it, and not the results presentation after it.
        var dlr = SecEdgarArchive.Documents(Captured("filing-page-" + DlrFebruary + ".htm"));

        Assert.Equal("dlr-20260205xex99d1.htm", SecEdgarArchive.NamedRelease(dlr)!.FileName);
        Assert.DoesNotContain(dlr, SecEdgarArchive.NamesTheRelease);

        // Keysight's page names its one exhibit a press release in its file name.
        Assert.Equal(KeysExhibit, SecEdgarArchive.NamedRelease(SecEdgarArchive.Documents(Captured("filing-types-KEYS-8k.htm")))!.FileName);

        // A description naming a press, earnings or news release, whatever its case, is read, and a file name with its
        // words run together, joined by a hyphen or an underscore; a dividend's release and an earnings deck are not.
        FiledDocument Exhibit(int sequence, string description, string file) => new(sequence, "EX-99." + sequence, description, file);

        Assert.True(SecEdgarArchive.NamesTheRelease(Exhibit(1, "EX-99.1 - PRESS RELEASE", "a.htm")));
        Assert.True(SecEdgarArchive.NamesTheRelease(Exhibit(1, "2Q2022 Earnings Release", "a.htm")));
        Assert.True(SecEdgarArchive.NamesTheRelease(Exhibit(1, "News release dated May 1", "a.htm")));
        Assert.True(SecEdgarArchive.NamesTheRelease(Exhibit(1, "EX-99.1", "earningspressrelease2023q2.htm")));
        Assert.True(SecEdgarArchive.NamesTheRelease(Exhibit(1, "EX-99.1", "q3-earnings-release.htm")));
        Assert.True(SecEdgarArchive.NamesTheRelease(Exhibit(1, "EX-99.1", "news_release.htm")));
        Assert.False(SecEdgarArchive.NamesTheRelease(Exhibit(1, "EX-99.3", "ex993-dividendrelease.htm")));
        Assert.False(SecEdgarArchive.NamesTheRelease(Exhibit(1, "2Q2022 EARNINGS DECK", "a2q22earningsslidesvf.htm")));
        Assert.False(SecEdgarArchive.NamesTheRelease(Exhibit(1, "EX-99.1", "ex991_93025er.htm")));

        // Two named take the lower; a transcript named an earnings release is passed over; none named takes the lowest.
        Assert.Equal("b.htm", SecEdgarArchive.NamedRelease([Exhibit(1, "EX-99.1 QFR", "a.htm"), Exhibit(2, "PRESS RELEASE 2Q2020", "b.htm"), Exhibit(3, "SEPARATION PRESS RELEASE", "c.htm")])!.FileName);
        Assert.Equal("b.htm", SecEdgarArchive.NamedRelease([Exhibit(1, "Earnings release call transcript", "a.htm"), Exhibit(2, "Earnings release", "b.htm")])!.FileName);
        Assert.Equal("a.htm", SecEdgarArchive.NamedRelease([Exhibit(1, "EX-99.1", "a.htm"), Exhibit(2, "EX-99.2", "b.htm")])!.FileName);
        Assert.Null(SecEdgarArchive.NamedRelease([new FiledDocument(1, "8-K", "8-K", "k.htm")]));
    }

    [Fact]
    public async Task TheRuntimesRefusalPastTheWindowCarriesTheRequestsTokensAndTheWindow()
    {
        // The captured refusal names the request's 63,604 tokens and the 50,176 the model is loaded at, read off the
        // error the engine passes on inside its own; any other refusal names neither.
        Assert.Equal(((int?)63604, (int?)50176), LmStudioReadingFeed.PastTheContext(Runtime("local-reading-past-context-refused.json")));
        Assert.Equal(((int?)null, (int?)null), LmStudioReadingFeed.PastTheContext("{\"error\":\"Model is unloaded.\"}"));
        Assert.Equal(((int?)null, (int?)null), LmStudioReadingFeed.PastTheContext("{\"error\":{\"type\":\"exceed_context_size_error\"}}"));

        // And the feed's refusal carries them beside the runtime's cause.
        var settings = ReadingProfile(Path.GetTempPath());
        var runtime = new ScriptedRuntime((path, _) => Task.FromResult(Answered(HttpStatusCode.BadRequest, Runtime("local-reading-past-context-refused.json"))));
        var refused = await Assert.ThrowsAsync<ReadingRefused>(() => ReadingFeedOver(runtime, settings).AskAsync(new ReadingRequest(Gemma, ReleaseQuestions.Instruction, "a release", ReadingSettings.Seed, ReadingSettings.AnswerTokens)));

        Assert.Equal((ReleaseAnswers.RuntimeRefused, (int?)63604, (int?)50176), (refused.Cause, refused.PromptTokens, refused.Context));
    }

    [Fact]
    public void AReleasePastTheWindowIsCutToTheShareTheWindowHoldsBackToTheLastLinesEndInItsLastTenth()
    {
        // The marker is 85 characters, set after a blank line, so a cut costs 87 beside the instruction's.
        Assert.Equal(85, ReleaseFit.Marker.Length);

        // A text of 99 lines of 99 characters and a newline, 9,900 characters, sent with an instruction of 100, refused
        // as 5,000 tokens past a window of 4,096 with an answer's budget of 256: the room is 4,096 less 256 and the margin
        // of 1,024, 2,816 tokens, which hold 10,000 times 2,816 over 5,000, 5,632 characters at that density, less 100
        // and 87, 5,445. The last newline at or before the 5,445th character is at index 5,399, inside the last tenth's
        // 545, so the text is cut there, 5,399 characters kept and the marker after them.
        var line = new string('x', 99) + "\n";
        var text = string.Concat(Enumerable.Repeat(line, 99));

        Assert.Equal(9900, text.Length);

        var cut = ReleaseFit.Cut(text, 100, 10000, 5000, 4096, 256);

        Assert.NotNull(cut);
        Assert.Equal(text[..5399] + "\n\n" + ReleaseFit.Marker, cut);
        Assert.Equal(5399, ReleaseFit.Kept(cut!));

        // With no line ending in the last tenth, the cut falls at the 5,445th character itself.
        var unbroken = new string('x', 9900);

        Assert.Equal(unbroken[..5445] + "\n\n" + ReleaseFit.Marker, ReleaseFit.Cut(unbroken, 100, 10000, 5000, 4096, 256));

        // A cut refused again is cut again on its own count, its marker no part of what it keeps: the 5,486 characters
        // sent with the 100 counted as 3,000 tokens hold 5,586 times 2,816 over 3,000, 5,243, less 187, 5,056, back to
        // the newline at index 4,999.
        Assert.Equal(4999, ReleaseFit.Kept(ReleaseFit.Cut(cut!, 100, 100 + cut!.Length, 3000, 4096, 256)!));

        // None where the cut would keep all the text holds, 10,000 times 2,816 over 2,700 being 10,429, less 187 past
        // 9,900; and none where the window holds no room past the answer's budget and the margin.
        Assert.Null(ReleaseFit.Cut(text, 100, 10000, 2700, 4096, 256));
        Assert.Null(ReleaseFit.Cut(text, 100, 10000, 5000, 1280, 256));
    }

    [Fact]
    public async Task AReadingRefusedPastTheWindowIsCutAndAskedAgainKeepingWhereItWasCutAndTheRequestItSent()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 22, 9, 0, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // A release of 1,000 lines of 39 characters and a newline, 40,000 characters no name or date in it, which the
        // anonymiser leaves as it stands; sent with the instruction's 676, 40,676.
        var text = string.Concat(Enumerable.Repeat("revenue grew and margins held steady ok\n", 1000));

        Assert.Equal(676, ReleaseQuestions.Instruction.Length);
        KeepRelease(store, "0001601046-26-000201", KeysCik, Utc(2026, 8, 18, 20, 9, 53), text);

        // The runtime refuses the first request with the captured refusal and answers the second.
        var chats = 0;
        var runtime = new ScriptedRuntime((path, _) => Task.FromResult(path switch
        {
            "/api/v1/models" => Answered(HttpStatusCode.OK, Runtime("local-runtime-models-gemma-loaded.json")),
            "/api/v0/chat/completions" => ++chats == 1
                ? Answered(HttpStatusCode.BadRequest, Runtime("local-reading-past-context-refused.json"))
                : Answered(HttpStatusCode.OK, Runtime("local-reading-KEYS-anonymised.json")),
            _ => Answered(HttpStatusCode.NotFound, "{}"),
        }));

        var read = await new LocalRun(ReadingFeedOver(runtime, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).ReadHistoryAsync(null, TextWriter.Null);

        // Worked by hand: the room is 50,176 less 256 and 1,024, 48,896 tokens; 40,676 times 48,896 over 63,604 is
        // 31,269 characters, less 676 and 87, 30,506; the newline at or before it is at index 30,479, so the second
        // request sends the first 30,479 characters and the marker, 30,566, and the reading keeps 30,479 and that
        // request's hash, the suite's own over the body as it was sent.
        var bodies = Chats(runtime);

        Assert.Equal((LocalRun.Finished, 1, 2), (read.Outcome, read.Read, bodies.Count));
        Assert.Equal(text, UserText(bodies[0]));
        Assert.Equal(text[..30479] + "\n\n" + ReleaseFit.Marker, UserText(bodies[1]));
        Assert.Equal(
            [$"0001601046-26-000201|ex99.htm|read|30479|{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bodies[1])))}|null"],
            Query(store, "SELECT accession, exhibit, outcome, cut_at, request_sha256, carried_from FROM release_reading;"));

        // A runtime refusing every request: cut three times, 30,479, then 23,239 and 17,679 characters kept, each worked
        // the same way, and refused after the third cut, four requests in all, the last cut kept with the refusal.
        KeepRelease(store, "0001601046-26-000202", KeysCik, Utc(2026, 8, 19, 20, 9, 53), text);
        store.Execute("UPDATE reading_pin SET history_completed_at = NULL;");

        var refusing = new ScriptedRuntime((path, _) => Task.FromResult(path switch
        {
            "/api/v1/models" => Answered(HttpStatusCode.OK, Runtime("local-runtime-models-gemma-loaded.json")),
            "/api/v0/chat/completions" => Answered(HttpStatusCode.BadRequest, Runtime("local-reading-past-context-refused.json")),
            _ => Answered(HttpStatusCode.NotFound, "{}"),
        }));

        await new LocalRun(ReadingFeedOver(refusing, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).ReadHistoryAsync(null, TextWriter.Null);

        var cuts = Chats(refusing);

        Assert.Equal([40000, 30566, 23326, 17766], cuts.Select(body => UserText(body).Length));
        Assert.Equal(
            [$"0001601046-26-000202|refused|{ReleaseAnswers.RuntimeRefused}|17679|{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cuts[3])))}"],
            Query(store, "SELECT accession, outcome, cause, cut_at, request_sha256 FROM release_reading WHERE accession = '0001601046-26-000202';"));
    }

    [Fact]
    public async Task TheExhibitsCheckStoresTheReleaseAPageNamesInThePlaceOfTheOneStoredAndChecksEachFilingOnce()
    {
        using var store = ReleaseStore();

        // BXP's July filing stored with its supplement as its release, DLR's with its supplement, Keysight's August with
        // its press release, a filing whose page the archive does not answer, and one a slot stored and checked already.
        KeepExhibit(store, BxpJuly, BxpCik, Utc(2025, 7, 29, 21, 37, 14), "q22025supplemental.htm", "Supplemental Operating and Financial Data for the Quarter Ended June 30, 2025");
        KeepExhibit(store, DlrFebruary, DlrCik, Utc(2026, 2, 5, 21, 8, 7), "dlr-20260205xex99d1.htm", "Financial Supplement Table of Contents");
        KeepExhibit(store, KeysAugust, KeysCik, Utc(2026, 8, 18, 20, 9, 53), KeysExhibit, "Keysight reports its third quarter");
        KeepExhibit(store, "0000789019-26-000301", MicrosoftCik, Utc(2026, 9, 1, 20, 5, 0), "ex99.htm", "Microsoft's release");
        KeepExhibit(store, "0000789019-26-000302", MicrosoftCik, Utc(2026, 9, 2, 20, 5, 0), "ex99.htm", "Microsoft's next release");
        store.Execute("INSERT INTO release_check (accession, exhibit, named, replaced, checked_at, run_id) VALUES ('0000789019-26-000302', 'ex99.htm', 0, 0, '2026-09-02T21:40:00Z', 'late-slot');");

        var pressRelease = "<html><body><p>BXP ANNOUNCES SECOND QUARTER 2025 RESULTS</p></body></html>";
        UnansweredPages Feed() => new(
            RecordedReleaseArchiveFeed.Of(
                pages: new Dictionary<string, string>
                {
                    [BxpJuly] = Captured("filing-page-" + BxpJuly + ".htm"),
                    [DlrFebruary] = Captured("filing-page-" + DlrFebruary + ".htm"),
                    [KeysAugust] = Captured("filing-types-KEYS-8k.htm"),
                },
                exhibits: new Dictionary<string, string> { [BxpJuly] = pressRelease }),
            "0000789019-26-000301");

        var clock = new CallClock(Utc(2026, 10, 22, 14, 0, 0));
        var first = Feed();
        var outcome = await new ReleaseFetch(first, clock, store.DatabaseFile, store.Root, Moving(clock)).ExhibitsAsync(null, "exhibits-first", TextWriter.Null);

        // Oldest first: BXP's page and its press release, DLR's page, Keysight's page, and the page not answered; the one
        // checked already not asked.
        Assert.Equal([BxpJuly, BxpJuly + "/q22025pressrelease.htm", DlrFebruary, KeysAugust, "0000789019-26-000301"], first.Asked);
        Assert.Equal(new ExhibitsChecked(4, 3, 2, 1, 1, 5, null, null), outcome);

        // BXP's press release stored as its filing's release beside its supplement, now no release; DLR's and Keysight's
        // as they stood; each check kept with the exhibit its page took, whether the page named it and whether it
        // replaced the one stored.
        Assert.Equal(
            [$"{DlrFebruary}|dlr-20260205xex99d1.htm|1|{ResultsReleases.HashOf("Financial Supplement Table of Contents")}|constructed",
             $"{KeysAugust}|{KeysExhibit}|1|{ResultsReleases.HashOf("Keysight reports its third quarter")}|constructed",
             $"{BxpJuly}|q22025pressrelease.htm|1|{ResultsReleases.HashOf(SecEdgarArchive.Plain(pressRelease))}|exhibits-first",
             $"{BxpJuly}|q22025supplemental.htm|0|{ResultsReleases.HashOf("Supplemental Operating and Financial Data for the Quarter Ended June 30, 2025")}|constructed"],
            Query(store, $"SELECT accession, exhibit, release, text_sha256, run_id FROM release_document WHERE accession IN ('{BxpJuly}', '{DlrFebruary}', '{KeysAugust}') ORDER BY accession, exhibit;"));
        Assert.Equal(
            [$"{DlrFebruary}|dlr-20260205xex99d1.htm|0|0|exhibits-first", $"{KeysAugust}|{KeysExhibit}|1|0|exhibits-first", $"{BxpJuly}|q22025pressrelease.htm|1|1|exhibits-first"],
            Query(store, "SELECT accession, exhibit, named, replaced, run_id FROM release_check WHERE run_id = 'exhibits-first' ORDER BY accession;"));
        Assert.Equal(
            ["ok|4|5"],
            Query(store, $"SELECT outcome, rows_written, network_requests FROM run_log WHERE stage = '{ReleaseFetch.ExhibitsStage}' AND run_id = 'exhibits-first';"));

        // Run again: the page not answered is asked again and nothing else.
        var second = Feed();
        var again = await new ReleaseFetch(second, clock, store.DatabaseFile, store.Root, Moving(clock)).ExhibitsAsync(null, "exhibits-second", TextWriter.Null);

        Assert.Equal(["0000789019-26-000301"], second.Asked);
        Assert.Equal((1, 0, 1), (again.Unchecked, again.Checked, again.Unread));

        // A reading of the supplement answers for none of BXP's prints; a reading of the press release does.
        store.Execute("INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', '2026-10-19T00:00:00Z', 'constructed');");
        store.Execute($"INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES ('{BxpJuly}', 'q22025supplemental.htm', 1, 'read', NULL, 'not stated', 'none given', 'neutral', 'not stated', 3, 5000, '2026-10-19T00:00:00Z', 'constructed');");

        using var connection = store.Open();

        var supplement = await ReleaseReadings.ForPrintAsync(connection, BxpCik, new DateOnly(2025, 7, 29), new DateOnly(2025, 7, 29), 1, ReleaseCalendar.Exchange, NewYork);

        Assert.Equal((BxpJuly, ReleaseReadings.NotRead), (supplement.Accession, supplement.Why));

        store.Execute($"INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES ('{BxpJuly}', 'q22025pressrelease.htm', 1, 'read', NULL, 'better', 'raised', 'confident', 'stronger', 3, 5000, '2026-10-22T15:00:00Z', 'constructed');");

        var release = await ReleaseReadings.ForPrintAsync(connection, BxpCik, new DateOnly(2025, 7, 29), new DateOnly(2025, 7, 29), 1, ReleaseCalendar.Exchange, NewYork);

        Assert.Equal((BxpJuly, "better", "raised"), (release.Accession, release.Answer?.Results, release.Answer?.Outlook));
    }

    [Fact]
    public void TheMigrationKeepsEveryReleaseAsItsFilingsReleaseAndEveryReadingAsOfTheExhibitItsFilingHeld()
    {
        using var store = new TemporaryStore();

        // Built to the schema before the correction, holding Keysight's August release and its readings under two pins.
        new MigrationRunner([.. SchemaMigrations.All.Where(migration => migration.Version < 104)]).Apply(store.DatabaseFile);

        store.Execute("INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) VALUES "
            + $"('{KeysAugust}', '{KeysCik}', '8-K', '2026-08-18', '2026-08-18T20:09:53Z', '{KeysExhibit}', 'https://www.sec.gov/x.htm', zeroblob(4), 'h', 4, 'history');");
        store.Execute("INSERT INTO release_reading (accession, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES "
            + $"('{KeysAugust}', 1, 'read', NULL, 'better', 'kept', 'neutral', 'steady', 3.5, 5000, '2026-10-18T09:00:00Z', 'history-1'), "
            + $"('{KeysAugust}', 3, 'refused', 'a word not listed', NULL, NULL, NULL, NULL, 4.5, 5200, '2026-10-20T09:00:00Z', 'history-3');");

        MigrationRunner.Standard().Apply(store.DatabaseFile);

        // The release as it stood and its filing's release; each reading as it stood, of the exhibit its filing held,
        // carrying no cut, request or carry; no check yet; and the index a reader takes a filer's releases by.
        Assert.Equal(
            [$"{KeysAugust}|{KeysCik}|8-K|2026-08-18|2026-08-18T20:09:53Z|{KeysExhibit}|https://www.sec.gov/x.htm|h|4|history|1"],
            Query(store, "SELECT accession, cik, form, filed, accepted_at, exhibit, url, text_sha256, text_length, run_id, release FROM release_document;"));
        Assert.Equal(
            [$"{KeysAugust}|{KeysExhibit}|1|read|null|better|kept|neutral|steady|3.5|5000|null|null|null|2026-10-18T09:00:00Z|history-1",
             $"{KeysAugust}|{KeysExhibit}|3|refused|a word not listed|null|null|null|null|4.5|5200|null|null|null|2026-10-20T09:00:00Z|history-3"],
            Query(store, "SELECT accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, cut_at, request_sha256, carried_from, read_at, run_id FROM release_reading ORDER BY pin;"));
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM release_check;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'release_document_by_cik';"));

        // A filing's second exhibit is a row of its own, and the same exhibit twice is refused.
        store.Execute($"INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id, release) VALUES ('{KeysAugust}', '{KeysCik}', '8-K', '2026-08-18', '2026-08-18T20:09:53Z', 'other.htm', 'https://www.sec.gov/o.htm', zeroblob(4), 'o', 4, 'exhibits', 1);");
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => store.Execute($"INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) VALUES ('{KeysAugust}', '{KeysCik}', '8-K', '2026-08-18', '2026-08-18T20:09:53Z', 'other.htm', 'https://www.sec.gov/o.htm', zeroblob(4), 'o', 4, 'exhibits');"));
    }

    [Fact]
    public async Task ANewVersionCarriesEachReadingWhoseRequestItWouldSendUnchangedAndReadsTheRest()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 10, 19, 9, 0, 0), SessionZones.UnitedStates);
        var probe = AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json"));

        // Pin 1 as pin 3 stood: written before the fit's rule, its prompt the instruction's and the anonymiser's alone.
        await new LocalRun(ReadingFeedOver(probe, settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);
        store.Execute($"UPDATE reading_pin SET prompt_version = '{ReleaseQuestions.VersionBeforeTheFit}';");

        // Keysight's and Widget Makers' names written before the readings at 10:00, Microsoft's after them; and Widget
        // Makers' insider stored after them by a run of the filings refresh.
        store.Execute("INSERT INTO release_filer (cik, names, tickers, read_at, run_id) VALUES "
            + $"('{KeysCik}', '[\"Keysight Technologies, Inc.\"]', '[\"KEYS\"]', '2026-10-19T08:00:00Z', 'names'), "
            + "('0000000077', '[\"Widget Makers Inc.\"]', '[\"WGT\"]', '2026-10-19T08:00:00Z', 'names'), "
            + $"('{MicrosoftCik}', '[\"MICROSOFT CORP\"]', '[\"MSFT\"]', '2026-10-20T00:00:00Z', 'late-slot');");
        store.Execute("INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome) VALUES ('night-late', 'filings-refresh', '2026-10-20T00:00:00Z', '2026-10-20T00:10:00Z', 'ok');");
        store.Execute("INSERT INTO insider_trade (cik, ticker, accession, filed, form, amends, original_filed, owners, owner_ciks, director, officer, title, ten_percent, sequence, security, traded, code, shares, price, acquired, held_after, planned, direct, nature, source, run_id) "
            + "VALUES ('0000000077', 'WGT', '0000000077-26-900001', '2026-10-19', '4', NULL, NULL, 'SMITH JOHN A', '0000000099', 0, 1, 'Chief Executive Officer', 0, 1, 'Common Stock', '2026-10-16', 'S', '100', '10', 0, '900', 1, 1, NULL, 'filing', 'night-late');");

        var plain = "Results were better than expected and the outlook was raised.";
        var thanked = "Results were better than expected, and the board thanked Smith for the year.";
        var releases = new (string Accession, string Cik, string Text, string? Outcome, string? Cause)[]
        {
            ("0001601046-26-000101", KeysCik, plain, "read", null),
            ("0001601046-26-000102", KeysCik, plain + " Two.", "refused", ReleaseAnswers.WordNotListed),
            ("0001601046-26-000103", KeysCik, plain + " Three.", "refused", ReleaseAnswers.RuntimeRefused),
            ("0001601046-26-000104", KeysCik, plain + " Four.", "read", null),
            ("0001601046-26-000105", KeysCik, plain + " Five.", null, null),
            ("0000789019-26-000106", MicrosoftCik, plain + " Six.", "read", null),
            ("0000000077-26-000107", "0000000077", thanked, "read", null),
            ("0000000077-26-000108", "0000000077", plain + " Eight.", "read", null),
        };

        foreach (var (accession, cik, text, outcome, cause) in releases)
        {
            KeepRelease(store, accession, cik, Utc(2026, 8, 18, 20, 9, 53), text);

            if (outcome is not null)
            {
                var words = outcome == "read" ? "'better', 'raised', 'confident', 'stronger'" : "NULL, NULL, NULL, NULL";

                store.Execute($"INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES ('{accession}', 'ex99.htm', 1, '{outcome}', {(cause is null ? "NULL" : $"'{cause}'")}, {words}, 3.5, 5000, '2026-10-19T10:00:00Z', 'history-1');");
            }
        }

        // The fourth's release replaced by a check after it was read.
        KeepExhibit(store, "0001601046-26-000104", KeysCik, Utc(2026, 8, 18, 20, 9, 53), "press.htm", plain + " Four, the press release.");
        store.Execute("UPDATE release_document SET release = 0 WHERE accession = '0001601046-26-000104' AND exhibit = 'ex99.htm';");

        // Pin 2 written with the fit's rule, everything else the same.
        var output = new StringWriter();
        var pinned = await new LocalRun(ReadingFeedOver(probe, settings), settings, FixedClock.At(Utc(2026, 10, 22, 16, 0, 0), SessionZones.UnitedStates), store.DatabaseFile, store.Root, NoWait).PinAsync(output);

        // Carried: the first read, the second refused for what its answer said, and the eighth, whose filer's insider
        // stored after it is no name its text holds. Read again: the third the runtime refused, the fourth's replacing
        // exhibit, the seventh whose text names the insider stored after it, the sixth whose filer's names were written
        // again after it, and the fifth pin 1 read nothing of.
        Assert.Equal(new CarryCounts(1, 3, 1, 1, 1, 1, 1), pinned.Carry);
        Assert.Contains(LocalRun.Carried(new CarryCounts(1, 3, 1, 1, 1, 1, 1)), output.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            "carried 3 reading(s) from pin 1; to read again, 2 for a changed document or the runtime's refusal of its length (1 an exhibit pin 1 did not read, 1 refused by the runtime), 2 for a name stored after the reading (1 changing the text it reads, 1 whose filer's names were written again after it), and 1 filing(s) pin 1 read nothing of",
            LocalRun.Carried(new CarryCounts(1, 3, 1, 1, 1, 1, 1))[(LocalRun.Stage.Length + 2)..]);

        // Each carried reading as pin 1 kept it, under pin 2 with the instant pin 1 read it, the version it came from and
        // the hash of the request pin 2 would send, the suite's own over the body the text and the filer make.
        string RequestHash(string text, ReleaseFiler filer) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(LmStudioReadingFeed.Body(new ReadingRequest(Gemma, ReleaseQuestions.Instruction, ReleaseAnonymiser.Anonymise(text, filer), ReadingSettings.Seed, ReadingSettings.AnswerTokens)))));

        var keysight = new ReleaseFiler(["Keysight Technologies, Inc."], ["KEYS"], []);
        var widgets = new ReleaseFiler(["Widget Makers Inc."], ["WGT"], ["SMITH JOHN A"]);

        Assert.Equal(
            [$"0000000077-26-000108|read|null|better|3.5|5000|null|{RequestHash(plain + " Eight.", widgets)}|1|2026-10-19T10:00:00Z",
             $"0001601046-26-000101|read|null|better|3.5|5000|null|{RequestHash(plain, keysight)}|1|2026-10-19T10:00:00Z",
             $"0001601046-26-000102|refused|{ReleaseAnswers.WordNotListed}|null|3.5|5000|null|{RequestHash(plain + " Two.", keysight)}|1|2026-10-19T10:00:00Z"],
            Query(store, "SELECT accession, outcome, cause, results, seconds, prompt_tokens, cut_at, request_sha256, carried_from, read_at FROM release_reading WHERE pin = 2 ORDER BY accession;"));
        Assert.Equal(3L, Scalar(store, $"SELECT COUNT(*) FROM release_reading WHERE pin = 2 AND run_id = (SELECT run_id FROM reading_pin WHERE id = 2);"));

        // The pin's row counts the version and the three carried, and names what it carried.
        var detail = JsonDocument.Parse(Query(store, $"SELECT detail FROM run_log WHERE stage = '{LocalRun.Stage}' AND json_extract(detail, '$.kind') = '{LocalRun.Pinning}' ORDER BY started_at DESC LIMIT 1;").Single()).RootElement.GetProperty("carry");

        Assert.Equal((1, 3, 1, 1, 1, 1, 1), (detail.GetProperty("from").GetInt32(), detail.GetProperty("carried").GetInt32(), detail.GetProperty("replaced").GetInt32(), detail.GetProperty("runtime").GetInt32(), detail.GetProperty("named").GetInt32(), detail.GetProperty("namesWritten").GetInt32(), detail.GetProperty("unread").GetInt32()));
        Assert.Equal(["4"], Query(store, $"SELECT rows_written FROM run_log WHERE stage = '{LocalRun.Stage}' AND json_extract(detail, '$.kind') = '{LocalRun.Pinning}' ORDER BY started_at DESC LIMIT 1;"));

        // The history read under pin 2 asks the five left and none of the three carried.
        var reading = AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-KEYS-anonymised.json"));
        var history = await new LocalRun(ReadingFeedOver(reading, settings), settings, FixedClock.At(Utc(2026, 10, 22, 17, 0, 0), SessionZones.UnitedStates), store.DatabaseFile, store.Root, NoWait).ReadHistoryAsync(null, TextWriter.Null);

        Assert.Equal(5, Chats(reading).Count);
        Assert.Equal(5, history.Read);
        Assert.Equal(
            ["0000000077-26-000107|ex99.htm", "0000789019-26-000106|ex99.htm", "0001601046-26-000103|ex99.htm", "0001601046-26-000104|press.htm", "0001601046-26-000105|ex99.htm"],
            Query(store, "SELECT accession, exhibit FROM release_reading WHERE pin = 2 AND carried_from IS NULL ORDER BY accession;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM reading_pin WHERE id = 2 AND history_completed_at IS NOT NULL;"));
    }

    [Fact]
    public void AVersionCarriesFromTheOneBeforeOnlyWhereTheyDifferInTheFitsRuleAlone()
    {
        var before = Pinned() with { PromptVersion = ReleaseQuestions.VersionBeforeTheFit };
        var pin = Pinned() with { Id = 2, PromptVersion = ReleaseQuestions.Version };

        Assert.True(LocalRun.Carries(before, pin));

        // The size, the modified instant, the cutoff and the instants a pin was written and completed are no part of what
        // a request sends.
        Assert.True(LocalRun.Carries(before, pin with { Size = 2048, Modified = pin.Modified.AddDays(1), Cutoff = new DateOnly(2025, 6, 30), WrittenAt = pin.WrittenAt.AddDays(4) }));

        // Each other part refuses the carry alone.
        Assert.False(LocalRun.Carries(before, pin with { Model = Qwen }));
        Assert.False(LocalRun.Carries(before, pin with { File = "pub/model/n.gguf" }));
        Assert.False(LocalRun.Carries(before, pin with { Sha256 = new string('b', 64) }));
        Assert.False(LocalRun.Carries(before, pin with { RuntimeName = "llama.cpp-cuda" }));
        Assert.False(LocalRun.Carries(before, pin with { RuntimeVersion = "2.62.1" }));
        Assert.False(LocalRun.Carries(before, pin with { Seed = 1 }));
        Assert.False(LocalRun.Carries(before, pin with { AnswerTokens = 512 }));
        Assert.False(LocalRun.Carries(before, pin with { Context = 65536 }));
        Assert.False(LocalRun.Carries(before with { PromptVersion = ReleaseQuestions.Version }, pin));
        Assert.False(LocalRun.Carries(before, pin with { PromptVersion = "000000000000" }));
    }

    // ---- the correction's own helpers ----

    // A filing's exhibit kept as the release fetch keeps one, by its file name, its text compressed beside its hash.
    static void KeepExhibit(TemporaryStore store, string accession, string cik, DateTimeOffset accepted, string exhibit, string text)
    {
        var kept = ResultsReleases.Compressed(text);

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) " +
            "VALUES ($accession, $cik, '8-K', $filed, $accepted, $exhibit, 'https://www.sec.gov/Archives/edgar/data/1/' || $exhibit, $text, $sha, $length, 'constructed');";
        command.Parameters.AddWithValue("$accession", accession);
        command.Parameters.AddWithValue("$cik", cik);
        command.Parameters.AddWithValue("$filed", accepted.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$accepted", accepted.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$exhibit", exhibit);
        command.Parameters.AddWithValue("$text", kept.Gzip);
        command.Parameters.AddWithValue("$sha", kept.Sha256);
        command.Parameters.AddWithValue("$length", kept.Length);
        command.ExecuteNonQuery();
    }

    // The chat requests a scripted runtime was sent, each body as it was sent.
    static List<string> Chats(ScriptedRuntime runtime)
    {
        const string Chat = "POST /api/v0/chat/completions ";

        return [.. runtime.Asked.Where(asked => asked.StartsWith(Chat, StringComparison.Ordinal)).Select(asked => asked[Chat.Length..])];
    }

    // The document a reading's body sends, its user message.
    static string UserText(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;

    // An archive answering as the recording does but for the filings' pages named, which it does not answer.
    sealed class UnansweredPages(RecordedReleaseArchiveFeed inner, params string[] unanswered) : IReleaseArchiveFeed
    {
        readonly List<string> asked = [];

        public int Documents => inner.Documents + asked.Count(accession => unanswered.Contains(accession));

        public IReadOnlyList<string> Asked => [.. asked];

        public Task<string?> CurrentAsync(int start, CancellationToken cancellation = default) => inner.CurrentAsync(start, cancellation);

        public Task<string?> DailyIndexAsync(DateOnly day, CancellationToken cancellation = default) => inner.DailyIndexAsync(day, cancellation);

        public Task<string?> FilingPageAsync(string cik, string accession, CancellationToken cancellation = default)
        {
            asked.Add(accession);

            return unanswered.Contains(accession) ? Task.FromResult<string?>(null) : inner.FilingPageAsync(cik, accession, cancellation);
        }

        public Task<string?> ExhibitAsync(string cik, string accession, string file, CancellationToken cancellation = default)
        {
            asked.Add(accession + "/" + file);

            return inner.ExhibitAsync(cik, accession, file, cancellation);
        }

        public Task<string?> SubmissionsAsync(string cik, CancellationToken cancellation = default) => inner.SubmissionsAsync(cik, cancellation);

        public Task<string?> SubmissionsPageAsync(string name, CancellationToken cancellation = default) => inner.SubmissionsPageAsync(name, cancellation);
    }
}
