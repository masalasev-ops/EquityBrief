using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One figure a filing states under one concept for a period: the period's first and last days, the value in dollars,
// the day the filing was made, its form and its accession.
public sealed record ConceptFact(DateOnly Start, DateOnly End, decimal Value, DateOnly Filed, string Form, string Accession);

// Every figure a filer has stated under one concept, each with the day it was filed, in one request.
//
// Asked by the history pull on the operator's command and by no night, through the archive's structured endpoint for
// one concept of one filer, which is free and asks no key, at most ten requests a second as the archive's fair access
// asks. A concept the filer never filed under is the archive saying the document is not there, which is no figure
// and no failure.
// see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
public interface IFiledRevenueFeed
{
    int Requests { get; }

    Task<IReadOnlyList<ConceptFact>> ConceptAsync(string cik, string concept, CancellationToken cancellation = default);
}

// The concept answer, read.
//
// Written against two captured answers, a filer stating revenue under the concept most filers use and a bank stating
// it under a concept of its own. The figures are in dollars under one unit; a figure for an instant rather than a
// period carries no first day and is no revenue, and a quarter a filing states again beside a later one is a figure
// of its own with that filing's date, which is what lets a quarter be read as first filed.
public static class ConceptAnswers
{
    public const string Dollars = "USD";

    public static IReadOnlyList<ConceptFact> Parse(string json, string cik, string concept)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new FormatException(
                $"The archive's answer for {concept} of CIK {cik} is not one object, so its figures cannot be read.");
        }

        if (!root.TryGetProperty("units", out var units) || units.ValueKind is not JsonValueKind.Object
            || !units.TryGetProperty(Dollars, out var dollars))
        {
            return [];
        }

        if (dollars.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                $"The archive's answer for {concept} of CIK {cik} holds its dollar figures in no array, so they cannot be read.");
        }

        var facts = new List<ConceptFact>();

        foreach (var fact in dollars.EnumerateArray())
        {
            if (fact.ValueKind is not JsonValueKind.Object)
            {
                throw new FormatException($"A figure in the archive's answer for {concept} of CIK {cik} is not an object.");
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
                        $"A figure in the archive's answer for {concept} of CIK {cik} carries no period, value, filing day, "
                        + "form or accession it can be read by."));
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
