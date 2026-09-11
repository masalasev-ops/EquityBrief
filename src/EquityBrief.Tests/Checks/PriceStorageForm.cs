using EquityBrief.Data.Migrations;

namespace EquityBrief.Tests.Checks;

// price-storage-form. No migration declares a price or money column REAL, and
// every helper crossing between the decimal world and the double one is named
// for the crossing.
//
// The rule has two halves and they fail apart. The storage half can be satisfied
// in code while a REAL column is still written, which is why the first test
// reads the migration text rather than the C# that uses it. The code half can be
// satisfied in the migrations while an expression casts money to a statistic
// inline, which is what the phase 5 sign-off found: `Statistic` sat in
// `EquityBrief.Data`, out of reach of `EquityBrief.Core`, and the two Core
// components that cross this boundary cast inline because they could not call
// it. Nothing read either direction of the code half at all.
public class PriceStorageForm
{
    // Which columns hold money is read from SCHEMA.md, not listed here.
    //
    // The obligation carried out of 0.7: this was a hand maintained array, so a
    // money column added to SCHEMA under a new name was unchecked and nothing
    // said so. The document marks one by writing "decimal" in its Notes cell, so
    // the set moves when the document does.
    internal static IReadOnlyList<string> MoneyColumns() =>
        StoreSchema.DeclaredMoney(Corpus.Read("docs/SCHEMA.md"))
            .Select(entry => entry.Column.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void NoMigrationDeclaresAMoneyColumnReal()
    {
        var money = MoneyColumns();

        // The set SCHEMA declares, stated because a run that derived none would
        // assert nothing at all and pass. Eleven columns across five tables when
        // this floor was set.
        Assert.True(money.Count >= 8, $"SCHEMA declares {money.Count} money columns, expected at least 8.");

        var columns = SchemaMigrations.All
            .SelectMany(migration => ColumnDeclarations.In(migration.Sql)
                .Select(column => (Migration: migration.Name, Column: column)))
            .ToArray();

        // Two scopes. The declarations read is context; the money columns among
        // them carry the property, and that number rises as the tables holding
        // prices are built.
        var declared = columns.Where(entry => money.Contains(entry.Column.Name, StringComparer.Ordinal)).ToArray();

        Assert.True(columns.Length >= 10, $"Read {columns.Length} column declarations, expected at least 10.");
        Assert.True(declared.Length >= 5, $"Read {declared.Length} money columns in migrations, expected at least 5.");

        Assert.DoesNotContain(declared, entry => entry.Column.Type != "TEXT");
    }

    [Fact]
    public void TheMoneyColumnsAreReadFromSchemaRatherThanListedHere()
    {
        var money = MoneyColumns();

        // The five tables SCHEMA marks. bar writes "decimal in code" and the
        // others write it bare, so the marker is the word rather than the phrase.
        Assert.Contains("close", money);
        Assert.Contains("spend", money);
        Assert.Contains("low_edge", money);

        // And a column that holds a statistic is not one of them, which is what
        // stops the reader marking every column in a table carrying a price.
        Assert.DoesNotContain("volume", money);
        Assert.DoesNotContain("observed_at", money);

        // One table is described as a difference from another and has no column
        // table of its own. It is named rather than skipped quietly, so a second
        // one appearing is a failure here instead of a silent exclusion from the
        // money check.
        Assert.Equal("theme_section", Assert.Single(StoreSchema.DescribedByDelta(Corpus.Read("docs/SCHEMA.md"))));

        // The permanent proof that the reader reads the Notes cell and not the
        // column name, over a constructed document.
        var derived = StoreSchema.DeclaredMoney(
            "### t\nGrain: one row per thing.\n\n" +
            "| Column | Type | Notes |\n|---|---|---|\n" +
            "| `a` | TEXT | decimal |\n" +
            "| `b` | REAL | a statistic |\n");

        Assert.Equal("a", Assert.Single(derived).Column.Name);
    }

    [Fact]
    public void TheCheckReportsAMoneyColumnDeclaredReal()
    {
        // The permanent proof that the assertion can fail.
        var columns = ColumnDeclarations.In(
            "CREATE TABLE t (\n  ticker TEXT,\n  spend REAL\n) STRICT;");

        Assert.Contains(new StoreColumn("spend", "REAL"), columns);
        Assert.Contains(new StoreColumn("ticker", "TEXT"), columns);
    }

    // Every method in the shipped source whose signature crosses between the two
    // worlds, being decimal in and double out or the reverse.
    //
    // The signature is what makes this checkable. An inline cast cannot be told
    // from a legitimate one by reading the source, since `(double)` over an int
    // is arithmetic and `(double)` over an object is unboxing, and neither is
    // this boundary. A method that takes money and hands back a statistic is
    // unambiguous, and it is also the thing worth governing: a second crossing
    // helper nobody knows about is how the boundary stops being one place.
    internal static IReadOnlyList<string> CrossingHelpersIn(string source, string file)
    {
        var code = SourceStatements.WithoutComments(source);

        return System.Text.RegularExpressions.Regex
            .Matches(
                code,
                @"(?<returns>\bdecimal\b|\bdouble\b)\??\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<parameters>[^)]*)\)")
            .Where(match =>
            {
                var returns = match.Groups["returns"].Value;
                var parameters = match.Groups["parameters"].Value;
                var opposite = returns == "double" ? "decimal" : "double";

                return System.Text.RegularExpressions.Regex.IsMatch(parameters, @"\b" + opposite + @"\b");
            })
            .Select(match => $"{Path.GetFileName(file)}: {match.Groups["name"].Value}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void EveryCrossingBetweenTheTwoWorldsIsAHelperNamedForIt()
    {
        var shipped = Repository.SourceFiles()
            .Where(file => !file.Contains(
                Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToArray();

        Assert.True(shipped.Length >= 15, $"Read {shipped.Length} shipped source files, expected at least 15.");

        var crossings = shipped
            .SelectMany(file => CrossingHelpersIn(File.ReadAllText(file), file))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Stated as a set rather than a count, so a new one is a decision and
        // not a number that moved.
        //
        // Three are `Statistic`'s own and are the boundary proper. The other
        // three each cross it and each reaches it through one of those three,
        // which is what the rule asks: `PlotValue` is the chart's crossing and
        // says why it is separate, a coordinate being a position on a surface
        // rather than a statistic and never travelling back; `Y` is the local
        // that calls it; and `Distance` turns a gap between two prices into a
        // count of typical days, through `Statistic.FromPrice`. That last one
        // cast inline until the phase 5 sign-off.
        Assert.Equal(
            [
                "MarkRenderer.cs: PlotValue",
                "MarkRenderer.cs: Y",
                "Statistic.cs: FromPrice",
                "Statistic.cs: FromRatio",
                "Statistic.cs: ToPrice",
                "UniverseScreen.cs: Distance",
            ],
            crossings);
    }

    [Fact]
    public void TheCrossingReaderReadsBothDirectionsAndLeavesArithmeticAlone()
    {
        // Permanent proof in both directions. The forward cases are the two
        // shapes the rule governs.
        Assert.Equal(
            ["Probe.cs: FromPrice"],
            CrossingHelpersIn("public static double FromPrice(decimal price) => (double)price;", "Probe.cs"));

        Assert.Equal(
            ["Probe.cs: ToPrice"],
            CrossingHelpersIn("public static decimal ToPrice(double statistic) => (decimal)statistic;", "Probe.cs"));

        // And the reverse, since a reader that flagged every double would make
        // the rule unwritable: arithmetic over a count, a conversion from a
        // volume, and a method that stays inside one world are not crossings.
        Assert.Empty(CrossingHelpersIn("public static double FromVolume(long shares) => shares;", "Probe.cs"));
        Assert.Empty(CrossingHelpersIn("static double Slot(double width, int count) => width / count;", "Probe.cs"));
        Assert.Empty(CrossingHelpersIn("static decimal Round(decimal value, int places) => value;", "Probe.cs"));

        // A comment describing one is not one, which is why the source is
        // stripped first.
        Assert.Empty(CrossingHelpersIn("// static double Sneak(decimal price) => 0;", "Probe.cs"));
    }

    // Every explicit cast to double or to decimal in a file, keyed on the file
    // and the start of its operand.
    //
    // The inline half the helper set above cannot see. A method whose signature
    // stays inside one world can still cast money to a statistic in its body,
    // which is exactly what `MoveSeries`, `ForwardReturnSeries` and
    // `UniverseScreen.Distance` did until the phase 5 sign-off moved them onto
    // the helpers: and putting any of the three back left the whole suite
    // green, because each sits in a method the signature reader either never
    // matches or already permits. A cast's operand type cannot be read from the
    // text, so the casts are stated as a set of sites, and a new one is a
    // decision rather than a number that moved. The operand is keyed on its
    // first token, which is enough to tell `(double)price` from
    // `(double)counted.Count` and a parenthesised expression from either.
    //
    // Two more forms from the phase 5 sign-off, which the reviewer found this
    // reader blind to: a cast to the nullable type, `(double?)`, is the same
    // crossing with a null carried through it, and `Convert.ToDouble(` or
    // `Convert.ToDecimal(` is a cast written as a call. One of the second
    // shipped when the reader was written and was in no stated set.
    internal static IReadOnlyList<string> CastsIn(string source, string file)
    {
        var code = SourceStatements.WithoutComments(source);

        const string Operand = @"(?<operand>\(|[A-Za-z_][A-Za-z0-9_.]*(?:\[[^\]]*\])?)";

        var casts = System.Text.RegularExpressions.Regex
            .Matches(code, @"\((?<to>double|decimal)(?<nullable>\?)?\)\s*" + Operand)
            .Select(match => $"{Path.GetFileName(file)}: ({match.Groups["to"].Value}{match.Groups["nullable"].Value}){match.Groups["operand"].Value}");

        var conversions = System.Text.RegularExpressions.Regex
            .Matches(code, @"\bConvert\.To(?<to>Double|Decimal)\(\s*" + Operand)
            .Select(match => $"{Path.GetFileName(file)}: Convert.To{match.Groups["to"].Value}({match.Groups["operand"].Value}");

        return [.. casts, .. conversions];
    }

    [Fact]
    public void EveryCastBetweenTheTwoWorldsInTheShippedSourceIsAStatedSite()
    {
        var shipped = Repository.SourceFiles()
            .Where(file => !file.Contains(
                Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToArray();

        var casts = shipped
            .SelectMany(file => CastsIn(File.ReadAllText(file), file))
            .OrderBy(site => site, StringComparer.Ordinal)
            .ToArray();

        // Stated as a multiset, so a second cast of an allowed shape in the same
        // file is a change here too. Each is one of three kinds: the crossing
        // helpers themselves, a count or a share divided by a count, which is
        // arithmetic over integers and never money, and a stored statistic read
        // back as the double it is before `Statistic.ToPrice` makes it a price.
        // `PlotValue` is the chart's crossing, which says why it is its own.
        //
        // Two joined at the phase 5 sign-off, when the reader came to see the
        // nullable cast and the conversion call. `(decimal?)null` types a null
        // in the price world and crosses nothing. `Convert.ToDouble(value)` in
        // the shortlist builder reads the fifty-session volume average, a
        // statistic stored as the double it is, back as a double; it was in the
        // shipped source when the reader was written and in no stated set.
        Assert.Equal(
            [
                "ForwardReturnSeries.cs: (double)counted.Count",
                "LadderBuilder.cs: (decimal?)null",
                "LadderBuilder.cs: (double)value",
                "LevelBuilder.cs: (double)value",
                "MarkRenderer.cs: (double)(",
                "MarkRenderer.cs: (double)(",
                "MarkRenderer.cs: (double)(",
                "MarkRenderer.cs: (double)Label",
                "MarkRenderer.cs: (double)band.Shares",
                "MarkRenderer.cs: (double)bar.Volume",
                "MarkRenderer.cs: (double)price",
                "ShortlistBuilder.cs: Convert.ToDouble(value",
                "Statistic.cs: (decimal)statistic",
                "Statistic.cs: (double)price",
                "Statistic.cs: (double)ratio",
                "TrendClassifier.cs: (double)value",
                "VolumeProfileSeries.cs: (double)shares[band]",
            ],
            casts);
    }

    [Fact]
    public void TheCastReaderKeysOnTheOperandAndIgnoresComments()
    {
        // The three inline casts the sign-off moved onto the helpers, each of
        // which has to produce a site the stated set does not hold.
        Assert.Equal(
            ["MoveSeries.cs: (double)("],
            CastsIn("var change = (double)((bars[at].Close - from) / from) * 100;", "MoveSeries.cs"));
        Assert.Equal(
            ["UniverseScreen.cs: (double)("],
            CastsIn("? Math.Abs((double)(price - band)) / typicalMove.Value", "UniverseScreen.cs"));

        // The helper's own form and an indexed operand are read whole.
        Assert.Equal(["Statistic.cs: (double)price"], CastsIn("=> (double)price;", "Statistic.cs"));
        Assert.Equal(["V.cs: (double)shares[band]"], CastsIn("(double)shares[band] / total", "V.cs"));

        // A comment naming a cast is not one.
        Assert.Empty(CastsIn("// (double)price is what this used to do", "Probe.cs"));

        // The two forms the phase 5 sign-off reviewer found this reader blind
        // to, each producing a site the stated set does not hold.
        Assert.Equal(["P.cs: (double?)price"], CastsIn("var d = (double?)price;", "P.cs"));
        Assert.Equal(["P.cs: Convert.ToDouble(price"], CastsIn("var d = Convert.ToDouble(price);", "P.cs"));
        Assert.Equal(["P.cs: Convert.ToDecimal(statistic"], CastsIn("var m = Convert.ToDecimal(statistic);", "P.cs"));

        // And a conversion to an integer is neither world.
        Assert.Empty(CastsIn("var n = Convert.ToInt32(count);", "P.cs"));
    }
}
