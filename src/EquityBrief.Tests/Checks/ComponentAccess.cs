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

        // The calendar is the known one and it is named, not swallowed. Four
        // components read a store SCHEMA does not declare and nothing writes,
        // which is contradiction E, settled at 4.0.
        var known = unresolved.Where(term => term.Contains("calendar", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Equal(known.Length, unresolved.Length);
        Assert.True(known.Length is > 0 and <= 6, $"{known.Length} calendar reads, expected between 1 and 6 until 4.0 settles it.");

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

    [Fact]
    public void TheCatalogueRowsWithNoClassYetAreCounted()
    {
        // The direction that cannot hold until the components are built, counted
        // with the narrowing named rather than left to read as coverage. It
        // shrinks by one per component from here.
        var catalogue = Catalogue();
        var built = ShippedComponents.All().Select(component => component.Name).ToArray();

        var absent = catalogue
            .Where(row => !built.Any(name => Matches(row.Component, name)))
            .ToArray();

        Assert.True(absent.Length > 0, $"{absent.Length} catalogue rows have no class, which is context and not a pass.");
        Assert.Equal(catalogue.Count - built.Length, absent.Length);
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
        // which is what the mark renderer's eleven blank cells assert.
        Assert.Empty(Core.Components.ComponentAccess.Nothing.Stores);
        Assert.Equal(Touch.None, Core.Components.ComponentAccess.Nothing.On(DataStore.Bar));

        // And a column the matrix does not carry throws rather than mapping to
        // nothing, which is what stops a renamed column reading as a blank row.
        Assert.Throws<InvalidOperationException>(() => ComponentVocabulary.StoresIn("Widgets"));
    }
}
