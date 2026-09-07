using System.Text.RegularExpressions;
using EquityBrief.Data.Migrations;

namespace EquityBrief.Tests.Checks;

// bar-append-only and writer-ownership.
//
// Both read what the shipped source and the migrations actually say, because a
// rule about writes that is checked by reading the components' own claims about
// themselves is a rule checked against an opinion.
public class StoreWrites
{
    static IReadOnlyList<string> ShippedSource() =>
        Repository.SourceFiles()
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToArray();

    static IReadOnlyList<string> Statements(string text, string verb, string table) =>
        Regex.Matches(text, $@"\b{verb}\b[^;]*?\b{table}\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => Regex.Replace(match.Value, @"\s+", " ").Trim())
            .ToArray();

    [Fact]
    public void NothingDeletesOrUpdatesABarTable()
    {
        // The scope is the whole shipped source and every migration, and it is
        // stated because the population that carries the property, statements
        // touching a bar table, is currently zero: the bar store arrives at 1.3.
        var sources = ShippedSource();
        var migrations = SchemaMigrations.All;

        Assert.True(sources.Count >= 5, $"Scanned {sources.Count} shipped source files, expected at least 5.");
        Assert.True(migrations.Count >= 1, $"Scanned {migrations.Count} migrations, expected at least 1.");

        var offences = sources
            .SelectMany(file => new[] { "delete", "update" }
                .SelectMany(verb => Statements(File.ReadAllText(file), verb, "bar")
                    .Select(statement => $"{Path.GetFileName(file)}: {statement}")))
            .Concat(migrations
                .SelectMany(migration => new[] { "delete", "update", "drop" }
                    .SelectMany(verb => Statements(migration.Sql, verb, "bar")
                        .Select(statement => $"{migration.Name}: {statement}"))))
            .ToArray();

        Assert.Empty(offences);
    }

    [Fact]
    public void TheCheckReportsADeleteAgainstABarTable()
    {
        // The permanent proof that the reader can find one, written now because
        // there is no bar table yet for it to find.
        Assert.Single(Statements("DELETE FROM bar WHERE ticker = 'AAPL';", "delete", "bar"));
        Assert.Single(Statements("UPDATE bar SET close = '1';", "update", "bar"));
        Assert.Single(Statements("DROP TABLE bar;", "drop", "bar"));
        Assert.Empty(Statements("INSERT INTO bar VALUES ('AAPL');", "delete", "bar"));
    }

    [Fact]
    public void EveryWriterInTheCodeIsDeclaredInSchema()
    {
        var schema = Corpus.Read("docs/SCHEMA.md");
        var declared = Regex.Matches(schema, @"^\| `([a-z_]+)` \| ([^|]*) \| ([^|]*) \| ([^|]*) \|", RegexOptions.Multiline)
            .Select(match => (
                Table: match.Groups[1].Value,
                Owners: string.Join(",", match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value)))
            .ToArray();

        Assert.True(declared.Length >= 15, $"Read {declared.Length} ownership rows, expected at least 15.");

        // Every write in the shipped source, by the file that carries it. A
        // write in a file whose type is not declared for that table is the
        // failure; today there are none, because no component writes rows yet.
        var writes = ShippedSource()
            .SelectMany(file => declared
                .SelectMany(row => new[] { "insert into", "update", "delete from" }
                    .SelectMany(verb => Statements(File.ReadAllText(file), verb.Split(' ')[0], row.Table)
                        .Select(statement => $"{Path.GetFileName(file)} -> {row.Table}: {statement}"))))
            .ToArray();

        Assert.Empty(writes);
    }

    [Fact]
    public void TheDeclaredWritersThatDoNotExistYetAreCounted()
    {
        // The other direction of writer-ownership, which cannot hold until the
        // components are built. Counted rather than asserted, and the count is
        // what the phase report carries as out of scope.
        var schema = Corpus.Read("docs/SCHEMA.md");

        var owners = Regex.Matches(schema, @"^\| `[a-z_]+` \| ([^|]*) \| ([^|]*) \| ([^|]*) \|", RegexOptions.Multiline)
            .SelectMany(match => new[] { match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value })
            .SelectMany(cell => cell.Split(','))
            .Select(name => name.Trim())
            .Where(name => Regex.IsMatch(name, "^[A-Z][A-Za-z]+$"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(owners.Length >= 15, $"Read {owners.Length} declared writers, expected at least 15.");

        var built = owners
            .Where(owner => ShippedSource().Any(file => Path.GetFileNameWithoutExtension(file) == owner))
            .ToArray();

        // Nothing is built yet. The assertion is that the two are consistent,
        // not that either is a particular size.
        Assert.All(built, owner => Assert.Contains(owner, owners, StringComparer.Ordinal));
    }
}
