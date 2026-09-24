using System.Text.RegularExpressions;
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

        // No table is described as a difference from another. `theme_section`
        // was, until 6.4 wrote its columns out, because the migration creating it
        // made it a table in the store and `schema-columns` compares every one of
        // those against the file column by column. Asserted empty rather than
        // left unasserted, so a table described that way again is a failure here
        // instead of a silent exclusion from the money check.
        Assert.Empty(StoreSchema.DescribedByDelta(Corpus.Read("docs/SCHEMA.md")));

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
        //
        // `TonightScreen.DayChange` was in this set at 5.8 and is not now, and
        // the reason is the signature rather than the arithmetic. It took two
        // decimals and returned a double, so it crossed in the open; it takes
        // the name's own sessions now and the two closes it subtracts never
        // appear in its signature. It still crosses once, through
        // `Statistic.FromPrice`, which is in this set and is the boundary. A
        // method that reads decimals out of a record and hands the finished
        // ratio to the boundary is not a crossing this reader can see, which is
        // the limit stated in the roster row rather than a property lost here:
        // the cast half of this check is what covers it.
        // `ForwardReturnSeries.ChangeFromEntry` joined the set at 8.1, where a
        // setup's return became the move from the close it was entered at rather
        // than nothing at all. It takes two prices and returns a statistic, so it
        // crosses in the open and is named for what it does; the crossing itself
        // is still `Statistic.FromRatio` one call in.
        // see: A setup is scored from its entry, and a target reached before the entry is never a win
        //
        // `ForwardReturnSeries.BreakEven` joined it at 8.2 and is the same shape:
        // three prices in, the share of the plan's range that sat below the entry
        // out, and `Statistic.FromRatio` one call in. It is the crossing 8.2's own
        // text predicted, which is why this set is stated rather than counted.
        // see: A condition is judged against the break-even its own plan demands
        // `Distances.InTypicalDays` joined the set at the 5.8 correction that stated how far
        // each of a name's bands sits from the close. It takes a price and a band edge and
        // returns a count of typical moves, so it crosses in the open, and the crossing is
        // `Statistic.FromPrice` one call in. It was `UniverseScreen.Distance` before that and
        // crossed in the same way; moving it to one place both screens read is what made it a
        // signature this reader can see, which is the point of stating the set.
        // see: Distances are stated as typical days' moves
        Assert.Equal(
            [
                "Distances.cs: InTypicalDays",
                "ForwardReturnSeries.cs: BreakEven",
                "ForwardReturnSeries.cs: ChangeFromEntry",
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

        // Four more forms, from the phase 5 sign-off's own list and owed at 6.0.
        // A negated or signed operand, `(double)-price`, and a numeric literal,
        // `(decimal)0.1`, both start with a character the operand pattern did not
        // admit, so the cast was not read at all. The framework's own type names,
        // `(Double)` and `(Decimal)`, are the same cast spelled the other way.
        // And `decimal.ToDouble(` is the decimal type's own conversion, which is
        // a cast written as a call exactly as `Convert.ToDouble(` is.
        //
        // None of the four ships today, which is why all four survived: a reader
        // blind to a form passes every use of it, and there were no uses to pass.
        const string Operand = @"(?<operand>[-+]?\(|[-+]?[A-Za-z_][A-Za-z0-9_.]*(?:\[[^\]]*\])?|[-+]?[0-9][0-9_.]*[fFdDmM]?)";

        var casts = System.Text.RegularExpressions.Regex
            .Matches(code, @"\((?<to>double|Double|decimal|Decimal)(?<nullable>\?)?\)\s*" + Operand)
            .Select(match => $"{Path.GetFileName(file)}: ({match.Groups["to"].Value}{match.Groups["nullable"].Value}){match.Groups["operand"].Value}");

        var conversions = System.Text.RegularExpressions.Regex
            .Matches(code, @"\bConvert\.To(?<to>Double|Decimal)\(\s*" + Operand)
            .Select(match => $"{Path.GetFileName(file)}: Convert.To{match.Groups["to"].Value}({match.Groups["operand"].Value}");

        var ownConversions = System.Text.RegularExpressions.Regex
            .Matches(code, @"(?<![\w.])(?<from>decimal|Decimal|double|Double)\.To(?<to>Double|Decimal)\(\s*" + Operand)
            .Select(match =>
                $"{Path.GetFileName(file)}: {match.Groups["from"].Value}.To{match.Groups["to"].Value}({match.Groups["operand"].Value}");

        return [.. casts, .. conversions, .. ownConversions];
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
        // shipped source when the reader was written and in no stated set. The
        // scorer reads the same way twice: the typical move and, from 10.4, each
        // of the two averages a trend version reads, every one of them a
        // statistic stored as the double it is.
        Assert.Equal(
            [
                "CandidateRecord.cs: (double)inside.Length",
                "CandidateRecord.cs: (double)setups.Count",
                "ForwardReturnFiller.cs: (double?)null",
                "ForwardReturnSeries.cs: (double)counted.Count",
                "LadderBuilder.cs: (decimal?)null",
                "LadderBuilder.cs: (double)value",
                "LevelBuilder.cs: (double)value",
                "Looks.cs: (double)(",
                "Looks.cs: (double)At[look]",
                "MarkRenderer.cs: (double)(",
                "MarkRenderer.cs: (double)(",
                "MarkRenderer.cs: (double)(",
                "MarkRenderer.cs: (double)Label",
                "MarkRenderer.cs: (double)band.Shares",
                "MarkRenderer.cs: (double)bar.Volume",
                "MarkRenderer.cs: (double)price",
                "MarkRenderer.cs: (double)track.Total",
                "NullWin.cs: (double)wins",
                "RuleVersionScorer.cs: Convert.ToDouble(value",
                "RuleVersionScorer.cs: Convert.ToDouble(value",
                "ShortlistBuilder.cs: Convert.ToDouble(value",
                "SignFlip.cs: (double)atLeast",
                "Statistic.cs: (decimal)statistic",
                "Statistic.cs: (double)price",
                "Statistic.cs: (double)ratio",
                "TargetShares.cs: (double)count.Fired",
                "TrendClassifier.cs: (double)value",
                "VersionRecord.cs: (double)atLeast",
                "VersionRecord.cs: (double?)null",
                "VersionRecord.cs: (double?)null",
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

    // ---- the four forms the phase 5 sign-off named, owed at 6.0 ----

    [Fact]
    public void TheCastReaderSeesANegatedOperandANumericLiteralTheCapitalisedTypeAndTheDecimalsOwnConversion()
    {
        // Four shapes the reader was blind to, each a crossing between the two
        // worlds written in a way its operand pattern or its type pattern did
        // not reach. None of them ships today, which is why all four survived:
        // a reader that misses a form passes every use of it, and the only uses
        // there were to pass were none.
        Assert.Single(CastsIn("var a = (double)-price;", "Probe.cs"));
        Assert.Single(CastsIn("var b = (decimal)0.1;", "Probe.cs"));
        Assert.Single(CastsIn("var c = (Double)price;", "Probe.cs"));
        Assert.Single(CastsIn("var d = decimal.ToDouble(price);", "Probe.cs"));
    }

    [Fact]
    public void TheWidenedCastReaderStillLeavesWhatIsNotACrossingAlone()
    {
        // The other direction, so the widening is not a matcher that matches
        // everything. A cast to another type, a name that merely contains one of
        // the words, and arithmetic on an int are not crossings.
        Assert.Empty(CastsIn("var a = (int)price;", "Probe.cs"));
        Assert.Empty(CastsIn("var b = doubled + decimals;", "Probe.cs"));
        Assert.Empty(CastsIn("var c = ToDoubleCheck(price);", "Probe.cs"));
    }

    // ---- the third half: what a query does with a price ----

    // Every query the shipped source carries, read from the verbatim strings the
    // SQL is written as. Comments go first, so a sentence about an ordering is
    // not read as one, and a string is a query when it selects from something.
    internal static IReadOnlyList<string> QueriesIn(string source) =>
        Regex.Matches(SourceStatements.WithoutComments(source), "@\"(.*?)\"(?!\")", RegexOptions.Singleline)
            .Select(match => Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim())
            .Where(text => Regex.IsMatch(text, @"\bselect\b", RegexOptions.IgnoreCase)
                && Regex.IsMatch(text, @"\bfrom\b", RegexOptions.IgnoreCase))
            .ToArray();

    // What a query does with a price that the store has to compare or add to
    // answer: ordering on one, taking the least or the greatest or the total of
    // one, or comparing one against anything.
    //
    // A price is stored as text, and the store compares text character by
    // character, so each of these answers by how a number is spelled: a band at
    // 87 sorts above one at 117 and the higher of 95.10 and 106.47 is 95.10. An
    // aggregate that adds them is the other direction, the store reading text as
    // a floating point number, which is the storage form this check's first half
    // refuses. Both are the same rule: money is decimal, and the place it is a
    // decimal is the code.
    internal static IReadOnlyList<string> PriceChoicesIn(string query, IReadOnlyList<string> money)
    {
        var qualified = @"(?:[A-Za-z_][A-Za-z0-9_]*\.)?";

        return money
            .SelectMany(column => new (string Pattern, string Reads)[]
            {
                (@"\border\s+by\s+" + qualified + column + @"\b", "orders by"),
                (@"\b(?:min|max|sum|avg|total)\s*\(\s*" + qualified + column + @"\s*\)", "aggregates"),
                (qualified + column + @"\s*(?:<=|>=|<>|<|>)", "compares"),
            }
                .Where(form => Regex.IsMatch(query, form.Pattern, RegexOptions.IgnoreCase))
                .Select(form => $"{form.Reads} `{column}`"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void NoQueryChoosesAmongStoredPricesByTheTextTheyAreStoredAs()
    {
        var money = MoneyColumns();

        Assert.True(money.Count >= 8, $"SCHEMA declares {money.Count} money columns, expected at least 8.");

        var shipped = Repository.SourceFiles()
            .Where(file => !file.Contains(
                Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToArray();

        var queries = shipped
            .SelectMany(file => QueriesIn(File.ReadAllText(file))
                .Select(query => (File: Path.GetFileName(file), Query: query)))
            .ToArray();

        // The population, stated, because a reader that found no query at all
        // would assert nothing and pass. Ninety-four when this floor was set.
        Assert.True(queries.Length >= 80, $"Read {queries.Length} queries in the shipped source, expected at least 80.");

        var choices = queries
            .SelectMany(entry => PriceChoicesIn(entry.Query, money)
                .Select(choice => $"{entry.File}: {choice} in `{entry.Query}`"))
            .OrderBy(choice => choice, StringComparer.Ordinal)
            .ToArray();

        Assert.True(choices.Length == 0, string.Join("\n", choices));
    }

    [Fact]
    public void TheQueryReaderFindsEachFormAndLeavesWhatIsNotAPriceAlone()
    {
        // The permanent proof that the assertion above can fail, in each of the
        // three forms, and the three the corpus carried when it was written:
        // the level table ordered by an edge, the move's extremes taken as the
        // greatest and the least, and a universe row's nearest band the same.
        string[] money = ["low_edge", "high", "close"];

        Assert.Equal(
            ["orders by `low_edge`"],
            PriceChoicesIn("SELECT low_edge FROM level ORDER BY low_edge;", money));

        Assert.Equal(
            ["aggregates `high`"],
            PriceChoicesIn("SELECT MAX(high) FROM bar WHERE ticker = $ticker;", money));

        Assert.Equal(
            ["compares `close`"],
            PriceChoicesIn("SELECT ticker FROM bar WHERE close > $level;", money));

        // Qualified by its table, which is the shape the universe query used.
        Assert.Equal(
            ["aggregates `low_edge`"],
            PriceChoicesIn("SELECT MIN(v.low_edge) FROM level v;", money));

        // And the other direction. A date and a count are not prices however they
        // are chosen, a column that merely ends in the name of one is not it, and
        // an equality reads one stored value rather than comparing two.
        Assert.Empty(PriceChoicesIn("SELECT x FROM t ORDER BY as_of DESC;", money));
        Assert.Empty(PriceChoicesIn("SELECT MAX(as_of) FROM level;", money));
        Assert.Empty(PriceChoicesIn("SELECT SUM(fired_count) FROM listing;", money));
        Assert.Empty(PriceChoicesIn("SELECT x FROM bar ORDER BY raw_close;", money));
        Assert.Empty(PriceChoicesIn("SELECT x FROM bar WHERE close = $close;", money));

        // The reader that finds them reads queries and not prose: a comment
        // naming an ordering is not one, and a string that selects nothing is
        // not a query.
        Assert.Empty(QueriesIn("// SELECT low_edge FROM level ORDER BY low_edge"));
        Assert.Empty(QueriesIn("const string Note = @\"ordered by low_edge\";"));
        Assert.Single(QueriesIn("const string Q = @\"SELECT low_edge FROM level;\";"));
    }
}
