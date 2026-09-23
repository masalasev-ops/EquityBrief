using System.Text.RegularExpressions;
using EquityBrief.Core.Components;
using EquityBrief.Tests.Harness;
using DataStore = EquityBrief.Core.Components.Store;

namespace EquityBrief.Tests.Checks;

// component-access.
//
// Section 7 says the name is the key: the harness looks for a class of each
// catalogue name with spaces removed and asserts it reads and writes only what
// the row lists. Section 16 says a blank cell in the read and write matrix is a
// claim as much as a filled one. Section 19.2 asks for both, in both directions.
//
// The declaration lives in the component rather than in a list here, because a
// declaration written beside the check that reads it is a second statement of one
// fact and nothing keeps the two together.
//
// What is assertable at 1.1 and what is only counted is stated per test rather
// than left to be worked out. Most of the document-against-document directions
// hold over the whole catalogue today; the class-against-document ones hold over
// the components that exist, which is one.
public class ComponentAccess
{
    internal static CheckReach Reach => new(
        "component-access",
        ["docs/ARCHITECTURE.html", "docs/SCHEMA.md"],
        [
            // 9.2, the request drain, the worker's half of the request store.
            CheckReach.Key(Scope.CatalogueTable, "Request drain"),
            CheckReach.Key(Scope.MatrixTable, "Request drain"),

            // 8.6, the rule version scorer.
            CheckReach.Key(Scope.CatalogueTable, "Rule version scorer"),
            CheckReach.Key(Scope.MatrixTable, "Rule version scorer"),

            // 8.3, the candidate registrar.
            CheckReach.Key(Scope.CatalogueTable, "Candidate registrar"),
            CheckReach.Key(Scope.MatrixTable, "Candidate registrar"),

            // 6.11, the report exporter, and the harness's own matrix row.
            CheckReach.Key(Scope.CatalogueTable, "Report exporter"),
            CheckReach.Key(Scope.MatrixTable, "Report exporter"),
            CheckReach.Key(Scope.MatrixTable, "Verification harness"),

            // 6.10, the overnight queue.
            CheckReach.Key(Scope.CatalogueTable, "Overnight queue"),
            CheckReach.Key(Scope.MatrixTable, "Overnight queue"),

            // 6.9, the theme research runner.
            CheckReach.Key(Scope.CatalogueTable, "Theme research runner"),
            CheckReach.Key(Scope.MatrixTable, "Theme research runner"),

            // 6.8, the research runner.
            CheckReach.Key(Scope.CatalogueTable, "Research runner"),
            CheckReach.Key(Scope.MatrixTable, "Research runner"),

            // 5.5, the forward returns and the news pulse.
            CheckReach.Key(Scope.CatalogueTable, "Forward return filler"),
            CheckReach.Key(Scope.CatalogueTable, "News pulse counter"),
            CheckReach.Key(Scope.CatalogueTable, "Night close"),
            CheckReach.Key(Scope.MatrixTable, "Forward return filler"),
            CheckReach.Key(Scope.MatrixTable, "News pulse counter"),
            CheckReach.Key(Scope.MatrixTable, "Night close"),

            // 5.4, tonight's list.
            CheckReach.Key(Scope.CatalogueTable, "Shortlist builder"),
            CheckReach.Key(Scope.MatrixTable, "Shortlist builder"),

            // 5.3, the facts file.
            CheckReach.Key(Scope.CatalogueTable, "Facts assembler"),
            CheckReach.Key(Scope.CatalogueTable, "Change detector"),
            CheckReach.Key(Scope.MatrixTable, "Facts assembler"),
            CheckReach.Key(Scope.MatrixTable, "Change detector"),

            // 5.2, the move annotator.
            CheckReach.Key(Scope.CatalogueTable, "Move annotator"),
            CheckReach.Key(Scope.MatrixTable, "Move annotator"),

            CheckReach.Key(Scope.CatalogueTable, "Membership loader"),
            CheckReach.Key(Scope.MatrixTable, "Membership loader"),
            CheckReach.Key(Scope.MatrixTable, "Migration runner"),
            CheckReach.Key(Scope.CatalogueTable, "Backfill"),
            CheckReach.Key(Scope.MatrixTable, "Backfill"),
            CheckReach.Key(Scope.CatalogueTable, "Read API"),
            CheckReach.Key(Scope.MatrixTable, "Read API"),
            CheckReach.Key(Scope.CatalogueTable, "Mark renderer"),
            CheckReach.Key(Scope.MatrixTable, "Mark renderer"),
            CheckReach.Key(Scope.CatalogueTable, "Single page app"),
            CheckReach.Key(Scope.MatrixTable, "Single page app"),
            CheckReach.Key(Scope.CatalogueTable, "Bar fetcher"),
            CheckReach.Key(Scope.MatrixTable, "Bar fetcher"),
            CheckReach.Key(Scope.CatalogueTable, "Indicator engine"),
            CheckReach.Key(Scope.MatrixTable, "Indicator engine"),
            CheckReach.Key(Scope.CatalogueTable, "Corporate action checker"),
            CheckReach.Key(Scope.MatrixTable, "Corporate action checker"),
            CheckReach.Key(Scope.CatalogueTable, "Swing finder"),
            CheckReach.Key(Scope.MatrixTable, "Swing finder"),
            CheckReach.Key(Scope.CatalogueTable, "Volume profile builder"),
            CheckReach.Key(Scope.MatrixTable, "Volume profile builder"),
            CheckReach.Key(Scope.CatalogueTable, "Level builder"),
            CheckReach.Key(Scope.MatrixTable, "Level builder"),
            // 6.1, the fundamentals fetcher: its catalogue row and its matrix row,
            // reconciled against the class's own declaration in both directions.
            CheckReach.Key(Scope.CatalogueTable, "Fundamentals fetcher"),
            CheckReach.Key(Scope.MatrixTable, "Fundamentals fetcher"),
            // 6.7, the spend cap, and the run log's row it makes true.
            CheckReach.Key(Scope.CatalogueTable, "Spend cap"),
            CheckReach.Key(Scope.MatrixTable, "Spend cap"),
            CheckReach.Key(Scope.CatalogueTable, "Run log"),
            // 6.6, the prose writer.
            CheckReach.Key(Scope.CatalogueTable, "Prose writer"),
            CheckReach.Key(Scope.MatrixTable, "Prose writer"),
            // 6.5, the staleness judge.
            CheckReach.Key(Scope.CatalogueTable, "Staleness judge"),
            CheckReach.Key(Scope.MatrixTable, "Staleness judge"),
            // 6.4, the claim checker, whose catalogue row was repaired to name the
            // theme store SCHEMA and the matrix already gave it.
            CheckReach.Key(Scope.CatalogueTable, "Claim checker"),
            CheckReach.Key(Scope.MatrixTable, "Claim checker"),
            CheckReach.Key(Scope.CatalogueTable, "Calendar fetcher"),
            CheckReach.Key(Scope.MatrixTable, "Calendar fetcher"),
            CheckReach.Key(Scope.CatalogueTable, "Trend classifier"),
            CheckReach.Key(Scope.MatrixTable, "Trend classifier"),
            CheckReach.Key(Scope.CatalogueTable, "Ladder builder"),
            CheckReach.Key(Scope.MatrixTable, "Ladder builder"),
        ]);

    static IReadOnlyList<ArchitectureTable> Tables() =>
        ArchitectureTables.In(File.ReadAllText(Repository.Architecture));

    static ArchitectureTable Table(string heading) =>
        Tables().Single(table => table.Heading == heading);

    // Section 7's rows: Component, Layer, Runs, Reads, Writes, What it does.
    internal sealed record CatalogueRow(string Component, string Layer, string Reads, string Writes);

    internal static IReadOnlyList<CatalogueRow> Catalogue() =>
        Table(Scope.CatalogueTable).Body
            .Where(row => row.Count >= 5 && row[0].Length > 0)
            .Select(row => new CatalogueRow(row[0], row[1], row[3], row[4]))
            .ToArray();

    [Fact]
    public void EveryMatrixColumnIsAStoreAndEveryStoreHasAColumn()
    {
        var header = Table(Scope.MatrixTable).Rows[0];
        var columns = header.Skip(1).Where(cell => cell.Length > 0).ToArray();

        Assert.True(columns.Length >= 11, $"Read {columns.Length} matrix columns, expected at least 11.");

        // Forwards: every column maps to at least one store, and StoresIn throws
        // on one that does not, so an added column fails rather than being read
        // as a row touching nothing.
        var mapped = columns.SelectMany(ComponentVocabulary.StoresIn).Distinct().ToArray();

        // Backwards: every store has a column, except the ones declared not to.
        var missing = Enum.GetValues<DataStore>()
            .Except(mapped)
            .Except(ComponentVocabulary.WithoutAColumn)
            .ToArray();

        Assert.Empty(missing);

        // And every store is a table SCHEMA describes, both ways, which is what
        // makes the enum the shared vocabulary rather than a third one.
        var declared = StoreSchema.DeclaredTables(Corpus.Read("docs/SCHEMA.md"));

        Assert.True(declared.Count >= 15, $"Read {declared.Count} tables from SCHEMA, expected at least 15.");

        var names = Enum.GetValues<DataStore>().Select(ComponentVocabulary.TableName).ToArray();

        Assert.Empty(names.Except(declared, StringComparer.Ordinal));
        Assert.Empty(declared.Except(names, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryCatalogueTermResolvesAndEveryLexiconTermIsUsed()
    {
        var catalogue = Catalogue();

        Assert.True(catalogue.Count >= 25, $"Read {catalogue.Count} catalogue rows, expected at least 25.");

        var unresolved = catalogue
            .SelectMany(row => new[] { row.Reads, row.Writes }
                .SelectMany(cell => ComponentVocabulary.Read(cell).Unresolved)
                .Select(term => $"{row.Component}: '{term}'"))
            .ToArray();

        // Nothing is unresolved. Until 4.0 this assertion permitted between one
        // and six unresolved calendar reads and asserted that every unresolved
        // term was one of them, because four components read a store SCHEMA did
        // not declare and nothing wrote, which is contradiction E. 4.0 gave the
        // calendar a table, an owner, a column and a fetcher, so the exemption
        // went with the contradiction rather than outliving it.
        //
        // The calendar is still named here, in the other direction: it now has
        // to resolve, so deleting the table or the alias fails this rather than
        // returning the check to the state it was allowed to be in.
        Assert.Empty(unresolved);
        Assert.Contains(
            catalogue,
            row => row.Reads.Contains("calendar", StringComparison.OrdinalIgnoreCase)
                && ComponentVocabulary.Read(row.Reads).Stores.Contains(DataStore.Calendar));

        // The other direction. A phrase the lexicon carries that no cell uses is
        // a translation nobody keeps current.
        var text = string.Join(" | ", catalogue.SelectMany(row => new[] { row.Reads, row.Writes }));

        var unused = ComponentVocabulary.Lexicon()
            .Where(phrase => !text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(unused);
    }

    [Fact]
    public void EveryDeclaringTypeHasACatalogueRowAndDeclaresWhatItLists()
    {
        var components = ShippedComponents.All();
        var catalogue = Catalogue();

        Assert.True(components.Count >= 1, $"Found {components.Count} declaring components, expected at least 1.");

        var faults = new List<string>();

        foreach (var component in components)
        {
            var row = catalogue.FirstOrDefault(entry => Matches(entry.Component, component.Name));

            if (row is null)
            {
                faults.Add(
                    $"{component.Name} declares a ComponentAccess and section 7 carries no row of " +
                    "that name. A component the catalogue does not list is one the matrix asserts " +
                    "nothing about.");

                continue;
            }

            var reads = ComponentVocabulary.Read(row.Reads);
            var writes = ComponentVocabulary.Read(row.Writes);

            foreach (var touch in component.Access.Stores)
            {
                var wanted = touch.Touch.HasFlag(Touch.Read) ? reads.Stores : [];
                var written = touch.Touch is not Touch.Read and not Touch.None;

                if (touch.Touch.HasFlag(Touch.Read) && !wanted.Contains(touch.Store))
                {
                    faults.Add($"{component.Name} declares it reads {touch.Store} and its Reads cell does not list it.");
                }

                // The run log is the exception the catalogue states once rather
                // than in every row: its own Reads cell says "every component
                // appends", and no component's Writes cell lists it. The matrix
                // fills the column for each of them, and that is where the claim
                // is asserted.
                if (written && touch.Store != DataStore.RunLog && !writes.Stores.Contains(touch.Store))
                {
                    faults.Add($"{component.Name} declares it writes {touch.Store} and its Writes cell does not list it.");
                }
            }

            foreach (var store in writes.Stores.Where(store => store != DataStore.RunLog && component.Access.On(store) is Touch.None or Touch.Read))
            {
                faults.Add($"{row.Component}'s Writes cell lists {store} and the class declares no write to it.");
            }

            foreach (var feed in reads.Feeds.Where(feed => !component.Access.Feeds.Contains(feed)))
            {
                faults.Add($"{row.Component}'s Reads cell lists {feed} and the class does not declare it.");
            }

            foreach (var feed in component.Access.Feeds.Where(feed => !reads.Feeds.Contains(feed)))
            {
                faults.Add($"{component.Name} declares it reads {feed} and its Reads cell does not list it.");
            }
        }

        Assert.Empty(faults);
    }

    [Fact]
    public void EveryDeclarationMatchesItsMatrixRowCellByCellIncludingTheBlanks()
    {
        var matrix = Table(Scope.MatrixTable);
        var header = matrix.Rows[0].Skip(1).Where(cell => cell.Length > 0).ToArray();
        var components = ShippedComponents.All();

        var faults = new List<string>();
        var cells = 0;
        var blanks = 0;

        foreach (var component in components)
        {
            var row = matrix.Body.FirstOrDefault(entry => entry.Count > 0 && Matches(entry[0], component.Name));

            if (row is null)
            {
                faults.Add($"{component.Name} has no row in the read and write matrix.");

                continue;
            }

            for (var index = 0; index < header.Length && index + 1 < row.Count; index++)
            {
                var stores = ComponentVocabulary.StoresIn(header[index]);
                var cell = row[index + 1];
                var claimsRead = cell.Contains('R', StringComparison.Ordinal);
                var claimsWrite = cell.Contains('W', StringComparison.Ordinal);

                var declaredRead = stores.Any(store => component.Access.On(store).HasFlag(Touch.Read));
                var declaredWrite = stores.Any(store => component.Access.On(store) is not (Touch.None or Touch.Read));

                cells++;

                if (!claimsRead && !claimsWrite)
                {
                    blanks++;
                }

                if (claimsRead != declaredRead)
                {
                    faults.Add($"{component.Name}, {header[index]}: the matrix says {(claimsRead ? "R" : "no read")} and the class declares {(declaredRead ? "a read" : "none")}.");
                }

                if (claimsWrite != declaredWrite)
                {
                    faults.Add($"{component.Name}, {header[index]}: the matrix says {(claimsWrite ? "W" : "no write")} and the class declares {(declaredWrite ? "a write" : "none")}.");
                }
            }
        }

        Assert.Empty(faults);

        // Two scopes. The cells compared is the population carrying the property
        // and the blanks among them are what section 16 says are asserted as
        // much as the filled ones, so both are stated.
        Assert.True(cells >= 11, $"Compared {cells} matrix cells against a declaration, expected at least 11.");
        Assert.True(blanks >= 9, $"{blanks} of those cells are blank, expected at least 9.");
    }

    [Fact]
    public void EveryDeclaredWriteMatchesSchemaOwnershipAndTheCodeBehindIt()
    {
        var declared = StoreWrites.Declared();
        var components = ShippedComponents.All();
        var sources = Repository.SourceFiles();

        var faults = new List<string>();
        var checkedWrites = 0;

        foreach (var component in components)
        {
            var file = sources.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path) == component.Name);

            var statements = file is null ? [] : StoreWrites.WritesIn(File.ReadAllText(file));

            foreach (var touch in component.Access.Stores)
            {
                var table = ComponentVocabulary.TableName(touch.Store);

                foreach (var operation in new[] { SourceStatements.Insert, SourceStatements.Update, SourceStatements.Delete })
                {
                    if (!touch.Touch.HasFlag(Enum.Parse<Touch>(operation)))
                    {
                        continue;
                    }

                    checkedWrites++;

                    if (!StoreWrites.AnyComponentMay(table, operation)
                        && !declared.Any(row => row.Table == table && row.Operation == operation && row.Owner == component.Name))
                    {
                        faults.Add($"{component.Name} declares {operation} on {table} and SCHEMA does not give it that.");
                    }

                    if (!statements.Any(write => write.Operation == operation && write.Table.Equals(table, StringComparison.OrdinalIgnoreCase)))
                    {
                        faults.Add($"{component.Name} declares {operation} on {table} and its source carries no statement that does.");
                    }
                }
            }
        }

        Assert.Empty(faults);
        Assert.True(checkedWrites >= 2, $"Checked {checkedWrites} declared writes, expected at least 2.");
    }

    // The direction a declaration cannot give by itself: what a component's own queries read,
    // held to what it declares it reads. A read the code makes and the declaration leaves out
    // passes every assertion above, because each of them starts from the declaration, and the
    // catalogue row and the matrix row are then written from the same declaration.
    //
    // The population is the queries in the file named after the component. A read made through
    // a helper in another file is outside it.
    [Fact]
    public void EveryTableAComponentsOwnQueriesReadIsOneItDeclaresItReads()
    {
        var components = ShippedComponents.All();
        var sources = Repository.SourceFiles();
        var tables = Enum.GetValues<DataStore>()
            .ToDictionary(ComponentVocabulary.TableName, store => store, StringComparer.OrdinalIgnoreCase);

        var faults = new List<string>();
        var files = 0;
        var reads = 0;

        foreach (var component in components)
        {
            var file = sources.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path) == component.Name);

            if (file is null)
            {
                continue;
            }

            files++;

            foreach (var read in SourceStatements.ReadsIn(File.ReadAllText(file)))
            {
                if (!tables.TryGetValue(read.Table, out var store))
                {
                    continue;
                }

                reads++;

                if (!component.Access.On(store).HasFlag(Touch.Read))
                {
                    faults.Add($"{component.Name} reads {read.Table} and declares no read of it, in: {read.Statement}");
                }
            }
        }

        Assert.True(faults.Count == 0, string.Join("\n", faults));

        // Two scopes, stated in advance. The files read are context; the reads found are the
        // population carrying the property, and a reader that found none would pass above.
        Assert.True(files >= 20, $"Read the queries of {files} component file(s), expected at least 20.");
        Assert.True(reads >= 100, $"Found {reads} read(s) of a table in them, expected at least 100.");
    }

    [Fact]
    public void TheReadReaderFindsEachFormAQueryReadsInAndLeavesTheRestAlone()
    {
        // Permanent, over constructed source, so the assertion above is not passing over a reader
        // that finds nothing or finds prose.
        const string Constructed = """
            const string A = @"SELECT a FROM bar b JOIN ladder d ON d.ticker = b.ticker LEFT JOIN level l ON 1;";
            const string B = @"SELECT x FROM listing l, facts f, json_each(l.reasons) c WHERE 1;";
            const string C = @"DELETE FROM swing WHERE ticker IN (SELECT ticker FROM membership);";
            const string D = @"INSERT INTO move (ticker) SELECT ticker FROM indicator;";
            const string E = @"WITH ranked AS (SELECT * FROM calendar) SELECT * FROM ranked;";
            // SELECT nothing FROM run_log
            const string F = "the figure came from news_pulse, as stored";
            const string G = @"DELETE FROM volume_profile WHERE as_of < $oldest;";
            """;

        // Found: a FROM and every JOIN with their aliases, a FROM's comma list, a subquery inside
        // a delete and a select feeding an insert. The common table expression's own name is
        // returned too, and is not a table, which is why the caller keeps only the tables.
        Assert.Equal(
            ["bar", "calendar", "facts", "indicator", "ladder", "level", "listing", "membership", "ranked"],
            SourceStatements.ReadsIn(Constructed).Select(read => read.Table).Distinct().Order(StringComparer.Ordinal));

        // Left alone: the table a delete takes rows from, a function in a FROM list, a comment,
        // and a sentence in a string that selects nothing.
        Assert.DoesNotContain(SourceStatements.ReadsIn(Constructed), read =>
            read.Table is "swing" or "volume_profile" or "json_each" or "run_log" or "news_pulse" or "move");

        // The reader's stated limit: a table listed after a function in one FROM list is not
        // read, because the list is read up to the first entry that is not a table.
        Assert.DoesNotContain(
            SourceStatements.ReadsIn(@"const string H = @""SELECT x FROM listing l, json_each(l.reasons) c, facts f;"";"),
            read => read.Table == "facts");
    }

    [Fact]
    public void EveryComponentThatWritesAppendsToTheRunLogAndNoneThatWritesNothingDoes()
    {
        // The run log's catalogue row, from 6.7: every component that writes appends,
        // read over the declarations in both directions. A component changing a store
        // and leaving no row of its own is a night nobody can read back, and a
        // component declaring a run log touch while its catalogue row says it writes
        // nothing is a row that contradicts its class.
        var components = ShippedComponents.All();
        var catalogue = Catalogue();

        Assert.True(components.Count >= 20, $"Read {components.Count} declaring components, expected at least 20.");

        var faults = new List<string>();
        var silent = new List<string>();

        foreach (var component in components)
        {
            var writes = component.Access.Stores.Any(touch => touch.Store != DataStore.RunLog && touch.Touch is not Touch.Read and not Touch.None);
            var appends = component.Access.On(DataStore.RunLog).HasFlag(Touch.Insert);

            if (writes && !appends)
            {
                faults.Add($"{component.Name} writes a store and declares no row of its own on the run log.");
            }

            if (!writes && !appends)
            {
                silent.Add(component.Name);

                var row = catalogue.Single(entry => Matches(entry.Component, component.Name));

                // Its Writes cell names no store: "none", or, for the exporter, a file the
                // person exporting keeps, which is not a store the run log's rule is about.
                if (ComponentVocabulary.Read(row.Writes) is var cell && (cell.Stores.Any() || cell.Unresolved.Any()))
                {
                    faults.Add($"{component.Name} declares no write and its Writes cell reads '{row.Writes}'.");
                }
            }
        }

        Assert.Empty(faults);

        // The four that write no store, stated in advance: the page, the renderer, the
        // trend classifier, which hands its label to the ladder builder, and from 6.11 the
        // report exporter, whose file is kept wherever the person exporting chooses.
        Assert.Equal(["MarkRenderer", "ReportExporter", "SinglePageApp", "TrendClassifier"], silent.Order(StringComparer.Ordinal));

        // And the row's own words, read off the document, are the words this holds.
        Assert.Equal(
            "every component that writes appends",
            Catalogue().Single(entry => entry.Component == "Run log").Reads);
    }

    [Fact]
    public void TheCatalogueRowsWithNoClassYetAreCounted()
    {
        // The direction that could not hold until the components were built, counted with the
        // narrowing named rather than left to read as coverage. It shrank by one per component
        // until 6.11 built the report exporter, and what is left is named: rows no shipped
        // class stands for, each for a reason of its own.
        var catalogue = Catalogue();
        var built = ShippedComponents.All().Select(component => component.Name).ToArray();

        var absent = catalogue
            .Where(row => !built.Any(name => Matches(row.Component, name)))
            .ToArray();

        Assert.True(absent.Length > 0, $"{absent.Length} catalogue rows have no class, which is context and not a pass.");
        Assert.Equal(catalogue.Count - built.Length, absent.Length);
        Assert.DoesNotContain(absent, row => row.Component == "Report exporter");
        Assert.Contains(absent, row => row.Component == "Verification harness");
    }

    [Fact]
    public void TheVerificationHarnessRowIsBlankAcrossTheMatrixAndTheStoresItOpensAreItsOwn()
    {
        // The harness is a catalogue row no shipped class stands for, so no declaration reads its
        // matrix row. The row is read against the catalogue's own words for what the harness
        // reads and writes, which name no store the matrix carries, and against the stores the
        // suite opens, each a temporary store outside the data root, which is the half of its
        // Reads cell that says it never opens one there.
        var row = Table(Scope.MatrixTable).Body.Single(entry => entry.Count > 1 && entry[0] == "Verification harness");

        Assert.True(row.Count >= 12, $"Read {row.Count - 1} cells in the harness's row, expected at least 11.");
        Assert.All(row.Skip(1), cell => Assert.Equal(string.Empty, cell));

        var catalogue = Catalogue().Single(entry => entry.Component == "Verification harness");

        Assert.Contains("never a store under the data root", catalogue.Reads, StringComparison.Ordinal);
        Assert.Empty(ComponentVocabulary.Read(catalogue.Reads).Stores);
        Assert.Empty(ComponentVocabulary.Read(catalogue.Writes).Stores);

        using var store = new TemporaryStore();

        Assert.StartsWith(Path.GetTempPath(), store.Root, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            Path.GetFullPath(store.Root).StartsWith(Path.GetFullPath(Path.Combine(Repository.Root, "data")), StringComparison.OrdinalIgnoreCase),
            "A store the suite opens sits under the checkout's data root.");
    }

    // Section 7 says a class of this exact name with spaces removed, which
    // literally yields Membershiploader. The comparison is on the space-stripped
    // form without case, and this says so rather than leaving a reader to find
    // out why the catalogue and the class disagree about a capital.
    internal static string Key(string component) =>
        component.Replace(" ", string.Empty, StringComparison.Ordinal);

    // Compared without case. Section 7 writes "Membership loader" and the class
    // is MembershipLoader, so the space-stripped forms differ by one capital and
    // an ordinal comparison finds no row at all.
    internal static bool Matches(string component, string typeName) =>
        string.Equals(Key(component), typeName, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TheCheckRefusesTheFourWaysADeclarationCanDisagree()
    {
        // Permanent proofs over constructed input, so none rests on the corpus
        // happening to be correct today.
        var reading = ComponentVocabulary.Read("membership, bar store");

        Assert.Equal([DataStore.Membership, DataStore.Bar], reading.Stores);
        Assert.Empty(reading.Unresolved);

        // A term nobody can place is reported rather than read as nothing.
        Assert.Single(ComponentVocabulary.Read("the widget store").Unresolved);

        // A feed is not a store, and is kept apart rather than absent.
        var feed = ComponentVocabulary.Read("index membership feed");

        Assert.Empty(feed.Stores);
        Assert.Equal([Feed.IndexMembership], feed.Feeds);

        // An empty declaration is a claim: it says the component touches nothing,
        // which is what the mark renderer's blank cells assert.
        Assert.Empty(Core.Components.ComponentAccess.Nothing.Stores);
        Assert.Equal(Touch.None, Core.Components.ComponentAccess.Nothing.On(DataStore.Bar));

        // And a column the matrix does not carry throws rather than mapping to
        // nothing, which is what stops a renamed column reading as a blank row.
        Assert.Throws<InvalidOperationException>(() => ComponentVocabulary.StoresIn("Widgets"));
    }

    // A count of a row's own parts, as a verdict note would state one: a number
    // standing next to the thing it counts. `8.3` and `section 14` are numbers
    // about something else and pass, which is why this reads the word beside the
    // number rather than the number alone.
    //
    // `one` is left out of the number words on purpose, and the limit is stated:
    // the notes use it as a determiner, as in the one reader or the one cell
    // width the table gives a mark, so a note meaning one cell as a count would
    // pass. Every count it has refused counted in twos and upward.
    //
    // A feed is a part of the row as a store is: the reads cell names both.
    // see: A verdict note states no count of the row's own parts
    static readonly Regex CountsTheRowsParts = new(
        @"\b(?:two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|(?<![\d.])\d+(?![\d.]))\b" +
        @"(?:(?:\s+[A-Za-z']+)?\s+(?:cells?|stores?|columns?|feeds?)\b|\s+(?:reads?|written)\b)",
        RegexOptions.IgnoreCase);

    [Fact]
    public void NoVerdictNoteStatesACountOfTheRowItDescribes()
    {
        // The 8.0 ruling. A count typed into a note is read against nothing: the
        // phase report prints it whatever the row holds, which is how the read
        // API's note counted reads a later checkpoint had already added to and
        // how two notes counted blank cells over rows that had grown. The count
        // lives in the row, where the row is the only thing that can change it.
        var notes = Scope.Notes.Concat(PhaseReport.PlacementNotes).ToArray();

        // The population, stated in advance and as context rather than as the
        // property: how many notes there are is a fact about how much is built.
        // What carries the property is that none of them counts.
        Assert.True(notes.Length >= 300, $"Read {notes.Length} verdict note(s), expected at least 300.");

        var counting = notes.Where(note => CountsTheRowsParts.IsMatch(note)).ToArray();

        Assert.Empty(counting);

        // The matcher, shown to refuse the three shapes the ruling found and to
        // leave a checkpoint, a section, a step and a determiner alone. Without
        // this a matcher that matched nothing would pass the assertion above over
        // every note in the map.
        Assert.All(
            new[] { "eleven cells", "12 read", "all thirteen cells are blank", "the four stores it reads", "the table's nine columns", "the class declares the two feeds it reads" },
            counted => Assert.True(CountsTheRowsParts.IsMatch(counted), $"'{counted}' states a count and was not refused."));

        Assert.All(
            new[] { "repaired at 4.0", "section 14", FormattableString.Invariant($"step {17}"), "8.3", "the one reader", "one call, stores no row", "twenty at most are drawn", "both feeds it reads" },
            allowed => Assert.False(CountsTheRowsParts.IsMatch(allowed), $"'{allowed}' is not a count of a row's parts and was refused."));
    }

    // A worker verb a document names and the worker does not dispatch is a
    // behaviour promised to a person with nothing behind it. The 8.6 correction
    // found one: the catalogue said a rule version's window opens through its
    // own verb, the component's methods existed, and no command line reached them,
    // while every check that read the row was reading its stores. A verb is read
    // however the specs write it, in code markup, backticks or bare, as a verb or
    // as a command, and a dispatch arm however it matches.
    [Fact]
    public void EveryWorkerVerbTheDocumentsNameIsDispatchedAndShownAndEveryDispatchedVerbIsInTheHelp()
    {
        var program = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Program.cs"));
        var dispatched = DispatchedVerbs(program);

        // The population, stated: the six the worker carries from 8.6.
        Assert.True(dispatched.Count >= 6, $"Read {dispatched.Count} dispatched verb(s), expected at least 6.");

        var mentions = new[] { "docs/ARCHITECTURE.html", "docs/SCHEMA.md", "docs/BUILD_PLAN.md" }
            .SelectMany(spec => VerbsNamedIn(Corpus.Read(spec)))
            .ToArray();
        var named = mentions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.True(mentions.Length >= 6, $"Read {mentions.Length} mention(s) of a worker verb in the specs, expected at least 6.");
        Assert.True(named.Length >= 3, $"Read {named.Length} verb(s) the specs name, expected at least 3.");

        // Every verb a component's row says a person or a page runs is one the
        // worker dispatches, and the runbook shows its command line.
        Assert.Empty(named.Except(dispatched, StringComparer.Ordinal));

        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.DoesNotContain(named, verb => !ShownIn(runbook, verb));

        // And the help the worker prints with no verb names every verb it
        // dispatches and says how many there are.
        var help = HelpText(program);

        Assert.DoesNotContain(dispatched, verb => !help.Contains($"'{verb}", StringComparison.Ordinal));
        Assert.Contains($"{CountWord(dispatched.Count)} are built", help, StringComparison.Ordinal);

        // The readers, over constructed text, so none of the assertions above
        // passes by reading nothing.
        const string Constructed = """
            return (args.Length > 0 ? args[0] : string.Empty) switch
            {
                "alpha" => A(),
                "beta" => await B(args),
                _ => NoVerb(),
            };
            static int NoVerb()
            {
                Console.Error.WriteLine("Two are built: 'alpha' does a thing and 'beta' another.");
                return 1;
            }
            """;

        Assert.Equal(["alpha", "beta"], DispatchedVerbs(Constructed));
        Assert.Contains("'beta' another", HelpText(Constructed), StringComparison.Ordinal);
        Assert.Equal(
            ["delta", "epsilon", "eta", "gamma", "iota", "research", "theta", "zeta"],
            VerbsNamedIn("<td>through the worker's <code>gamma</code> verb, the <code>research</code> verb, the worker's <code>delta</code> command, `epsilon` verb, the worker's zeta verb, <code>eta</code> and <code>theta</code> verbs, the command `iota`</td>")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        const string Arms = """
            return (args.Length > 0 ? args[0] : string.Empty) switch
            {
                "alpha" => A(),
                "beta" or "bet" => B(),
                "gamma" when x => C(),
                _ => NoVerb(),
            };
            """;

        Assert.Equal(["alpha", "beta", "bet", "gamma"], DispatchedVerbs(Arms));
        Assert.True(ShownIn("dotnet run --project src/EquityBrief.Worker -- gamma --rule x", "gamma"));
        Assert.False(ShownIn("the gamma verb, described and never shown", "gamma"));
    }

    static IReadOnlyList<string> DispatchedVerbs(string program)
    {
        var table = Regex.Match(program, @"args\[0\] : string\.Empty\) switch\s*\{(.*?)_ => NoVerb\(\)", RegexOptions.Singleline);

        return table.Success
            ? [.. Regex.Matches(table.Groups[1].Value, "\"([a-z-]+)\"\\s*(?==>|or\\b|when\\b)").Select(match => match.Groups[1].Value)]
            : [];
    }

    // Every mention of a worker verb, by the position of its name, so a phrasing two
    // patterns both read is one mention.
    static IReadOnlyList<string> VerbsNamedIn(string document)
    {
        const string Named = @"(?:<code>|`)([a-z][a-z-]*)(?:</code>|`)";

        string[] phrasings =
        [
            @"worker's\s+(?:<code>|`)?([a-z][a-z-]*)(?:</code>|`)?\s+(?:verb|command)s?\b",
            Named + @"\s+(?:and|or)\s+" + Named + @"\s+(?:verb|command)s?\b",
            Named + @"\s+(?:verb|command)\b",
            @"\b(?:verb|command)\s+" + Named,
        ];

        return
        [
            .. phrasings
                .SelectMany(phrasing => Regex.Matches(document, phrasing))
                .SelectMany(match => match.Groups.Cast<Group>().Skip(1).Where(group => group.Success))
                .DistinctBy(group => group.Index)
                .OrderBy(group => group.Index)
                .Select(group => group.Value),
        ];
    }

    static bool ShownIn(string runbook, string verb) =>
        Regex.IsMatch(runbook, @"src/EquityBrief\.Worker -- " + Regex.Escape(verb) + @"\b")
        || runbook.Contains($"`tools/{verb}`", StringComparison.Ordinal);

    static string HelpText(string program) =>
        Regex.Match(program, @"static int NoVerb\(\)\s*\{(.*?)return 1;", RegexOptions.Singleline).Groups[1].Value;

    static string CountWord(int count) =>
        count switch
        {
            2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six", 7 => "Seven", 8 => "Eight", 9 => "Nine", 10 => "Ten",
            _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}
