using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace EquityBrief.Core.Providers;

// A report's own XBRL instance, read where the archive's facts answer for its filer carries no figure of the report:
// the folder's listing names the instance among the filing's documents, and the instance is read under the
// concepts asked for, each a figure with no dimension for the period or the day its context states, so a line broken
// into segments stands as its total alone, as the facts answer states it. A figure is kept in the shape the facts
// answer gives one, dated by the report's filing day and named by its form and accession.
// see: A report the archive's facts answer carries no figure of is read from the report's own XBRL instance
public static class SecEdgarInstance
{
    public const string Listing = "index.json";

    const string Instance = "http://www.xbrl.org/2003/instance";

    const string Schema = "http://www.w3.org/2001/XMLSchema-instance";

    // The taxonomies' namespaces, each one a year: the accounting standards' and the SEC's cover page's.
    const string Accounting = "http://fasb.org/us-gaap/";

    const string Cover = "http://xbrl.sec.gov/dei/";

    // The documents an instance is not: the linkbases beside it, the schema and the report summary.
    static readonly string[] NotTheInstance = ["_cal.xml", "_def.xml", "_lab.xml", "_pre.xml", "FilingSummary.xml"];

    // The instance among a filing's documents: the one an inline report's own XBRL is extracted into, named for its
    // primary document with "_htm.xml", and where none is, the one XML document that is no linkbase and no summary;
    // none where the filing holds neither.
    public static string? InstanceFile(string listing)
    {
        using var document = JsonDocument.Parse(listing);

        var names = document.RootElement.TryGetProperty("directory", out var directory)
            && directory.TryGetProperty("item", out var items) && items.ValueKind is JsonValueKind.Array
                ? items.EnumerateArray()
                    .Select(item => item.TryGetProperty("name", out var name) && name.ValueKind is JsonValueKind.String ? name.GetString() : null)
                    .OfType<string>()
                    .ToArray()
                : throw new FormatException("The filing's listing holds no directory of items, so its instance cannot be found.");

        var extracted = names.Where(name => name.EndsWith("_htm.xml", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (extracted.Length > 0)
        {
            return extracted.Order(StringComparer.Ordinal).First();
        }

        var instances = names
            .Where(name => name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !NotTheInstance.Any(other => name.EndsWith(other, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return instances.Length == 1 ? instances[0] : null;
    }

    // The figures the instance states under each concept asked for: a figure in dollars for the period its context
    // spans, or for a balance sheet's line or a cover's share count the day its context stands at, each context with no
    // segment and no scenario; a concept the instance never states holds none.
    public static IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> Facts(
        string xml,
        string accession,
        DateOnly filed,
        string form,
        IReadOnlyList<string> concepts)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException unreadable)
        {
            throw new FormatException($"The instance of {accession} is not XML that can be read: {unreadable.Message}");
        }

        var root = document.Root ?? throw new FormatException($"The instance of {accession} holds no element.");
        XNamespace xbrli = Instance;

        // Each context with no dimension, by its id: the span it covers, or the day it stands at as a span of one day.
        var contexts = new Dictionary<string, (DateOnly Start, DateOnly End, bool Day)>(StringComparer.Ordinal);

        foreach (var context in root.Elements(xbrli + "context"))
        {
            if (context.Attribute("id")?.Value is not { } id
                || context.Element(xbrli + "entity")?.Element(xbrli + "segment") is not null
                || context.Element(xbrli + "scenario") is not null
                || context.Element(xbrli + "period") is not { } period)
            {
                continue;
            }

            if (Day(period.Element(xbrli + "instant")) is { } instant)
            {
                contexts[id] = (instant, instant, true);
            }
            else if (Day(period.Element(xbrli + "startDate")) is { } start && Day(period.Element(xbrli + "endDate")) is { } end)
            {
                contexts[id] = (start, end, false);
            }
        }

        // Each unit's single measure by its id, dollars and shares alone read.
        var units = root.Elements(xbrli + "unit")
            .Where(unit => unit.Attribute("id") is not null && unit.Elements(xbrli + "measure").Count() == 1)
            .ToDictionary(unit => unit.Attribute("id")!.Value, unit => unit.Element(xbrli + "measure")!.Value.Trim(), StringComparer.Ordinal);

        var answer = concepts.ToDictionary(concept => concept, _ => new List<ConceptFact>(), StringComparer.Ordinal);
        var cover = ConceptAnswers.EntityCounts;

        foreach (var element in root.Elements())
        {
            var space = element.Name.NamespaceName;
            var concept = element.Name.LocalName;

            if (!answer.TryGetValue(concept, out var figures)
                || !(cover.Contains(concept, StringComparer.Ordinal) ? space.StartsWith(Cover, StringComparison.Ordinal) : space.StartsWith(Accounting, StringComparison.Ordinal))
                || string.Equals(element.Attribute(XNamespace.Get(Schema) + "nil")?.Value, "true", StringComparison.Ordinal)
                || element.Attribute("contextRef")?.Value is not { } contextRef
                || !contexts.TryGetValue(contextRef, out var context)
                || element.Attribute("unitRef")?.Value is not { } unitRef
                || !units.TryGetValue(unitRef, out var measure))
            {
                continue;
            }

            var counted = cover.Contains(concept, StringComparer.Ordinal);
            var onADay = counted || Ledger.FiledFacts.OnADay.Contains(concept, StringComparer.Ordinal);

            // A measure is a name in a namespace, written with its prefix or, in the instance's own namespace, without one.
            if (context.Day != onADay
                || !(counted ? Measures(measure, ConceptAnswers.Shares) : Measures(measure, ConceptAnswers.Dollars))
                || !decimal.TryParse(element.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            // A figure the instance states twice for one period, as the statements and a note may, is kept once.
            if (!figures.Any(figure => figure.Start == context.Start && figure.End == context.End))
            {
                figures.Add(new ConceptFact(context.Start, context.End, value, filed, form, accession));
            }
        }

        return answer.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ConceptFact>)pair.Value, StringComparer.Ordinal);
    }

    static bool Measures(string measure, string unit) =>
        measure == unit || measure.EndsWith(":" + unit, StringComparison.Ordinal);

    static DateOnly? Day(XElement? element) =>
        element is not null && DateOnly.TryParseExact(element.Value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
}
