using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One figure a filing states under one concept for a period: the period's first and last days, the value in dollars,
// the day the filing was made, its form and its accession.
public sealed record ConceptFact(DateOnly Start, DateOnly End, decimal Value, DateOnly Filed, string Form, string Accession);

// Every figure a filer has stated under each of the concepts asked for, each with the day it was filed, in one request.
//
// Asked by the history pull on the operator's command and by no night, through the archive's structured facts of one
// filer, which is free and asks no key, at most ten requests a second as the archive's fair access asks. The facts
// hold every concept the filer used, and the concepts asked for are read out of them. The archive's endpoint for one
// concept is not asked: it answers an empty set of dollars for some filers whose facts hold the figures, Coca-Cola's
// revenue among them. A filer with no facts is the archive saying the document is not there, which is no figure and
// no failure.
// see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
public interface IFiledRevenueFeed
{
    int Requests { get; }

    Task<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>> RevenueAsync(
        string cik,
        IReadOnlyList<string> concepts,
        CancellationToken cancellation = default);
}

// A filer's figures under a concept, read.
//
// Written against a captured set of a filer's facts holding the two concepts it moved its revenue between, and two
// captured answers for one concept each, a filer stating revenue under the concept most filers use and a bank stating
// it under a concept of its own; a concept sits in a filer's facts in the shape the one concept's answer has. The
// figures are in dollars under one unit; a figure for an instant rather than a period carries no first day and is no
// revenue, and a quarter a filing states again beside a later one is a figure of its own with that filing's date,
// which is what lets a quarter be read as first filed. The archive sends a concept holding no dollars as an empty
// object where the figures would be, which reads as none.
public static class ConceptAnswers
{
    public const string Dollars = "USD";

    // The taxonomy the revenue concepts are filed under.
    public const string Taxonomy = "us-gaap";

    // One concept's answer.
    public static IReadOnlyList<ConceptFact> Parse(string json, string cik, string concept)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.ValueKind is JsonValueKind.Object
            ? Figures(document.RootElement, cik, concept)
            : throw new FormatException($"The archive's answer for {concept} of CIK {cik} is not one object, so its figures cannot be read.");
    }

    // A filer's facts, read for each concept asked for; a concept the filer never used holds no figure.
    public static IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> FromFacts(string json, string cik, IReadOnlyList<string> concepts)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new FormatException($"The archive's facts of CIK {cik} are not one object, so their figures cannot be read.");
        }

        var filed = root.TryGetProperty("facts", out var facts) && facts.ValueKind is JsonValueKind.Object
            && facts.TryGetProperty(Taxonomy, out var taxonomy) && taxonomy.ValueKind is JsonValueKind.Object
                ? taxonomy
                : (JsonElement?)null;

        return concepts.ToDictionary(
            concept => concept,
            concept => filed is { } held && held.TryGetProperty(concept, out var holder) && holder.ValueKind is JsonValueKind.Object
                ? Figures(holder, cik, concept)
                : (IReadOnlyList<ConceptFact>)[],
            StringComparer.Ordinal);
    }

    // The dollar figures one concept holds, under its units.
    static IReadOnlyList<ConceptFact> Figures(JsonElement holder, string cik, string concept)
    {
        if (!holder.TryGetProperty("units", out var units) || units.ValueKind is not JsonValueKind.Object
            || !units.TryGetProperty(Dollars, out var dollars)
            || (dollars.ValueKind is JsonValueKind.Object && !dollars.EnumerateObject().Any()))
        {
            return [];
        }

        if (dollars.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                $"The archive holds the dollar figures for {concept} of CIK {cik} in neither an array nor an empty object, so they cannot be read.");
        }

        var facts = new List<ConceptFact>();

        foreach (var fact in dollars.EnumerateArray())
        {
            if (fact.ValueKind is not JsonValueKind.Object)
            {
                throw new FormatException($"A figure the archive holds for {concept} of CIK {cik} is not an object.");
            }

            if (Text(fact, "start") is null)
            {
                continue;
            }

            facts.Add(
                Date(Text(fact, "start")) is { } start
                && Date(Text(fact, "end")) is { } end
                && fact.TryGetProperty("val", out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetDecimal(out var dollarsFiled)
                && Date(Text(fact, "filed")) is { } filed
                && Text(fact, "form") is { } form
                && Text(fact, "accn") is { } accession
                    ? new ConceptFact(start, end, dollarsFiled, filed, form, accession)
                    : throw new FormatException(
                        $"A figure the archive holds for {concept} of CIK {cik} carries no period, value, filing day, form "
                        + "or accession it can be read by."));
        }

        return facts;
    }

    static string? Text(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
