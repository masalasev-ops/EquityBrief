using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Report;

// One segment of the clean table: its name, its revenue for the quarter and the same quarter a year earlier with the
// change in per cent, its operating income for both with the change, and its share of the company's revenue in per cent.
public sealed record SegmentRow(
    string Segment,
    decimal Revenue,
    decimal? RevenueYearEarlier,
    double? RevenueChange,
    decimal? Earnings,
    decimal? EarningsYearEarlier,
    double? EarningsChange,
    double Share);

// What the rule made of a filing's segment table: the clean table, the quarter it covers, the company's revenue it was
// summed against and the segments' sum, or where no set of rows sums to it, why, and the raw table is drawn instead.
public sealed record SegmentReading(
    string Report,
    DateOnly? Quarter,
    int Months,
    decimal? Total,
    decimal? Sum,
    IReadOnlyList<SegmentRow> Rows,
    string? Raw)
{
    public bool Clean => Raw is null;
}

// The segment table built from a filing's segment report by a stated rule, worked by one function so the page and its
// file draw the same rows.
//
// The quarter is the table's shortest period at its newest end, and a year earlier the same period ending within a
// week of a year before. A segment is a group whose label, with the parts naming the consolidation's own axis taken
// off, names something, and whose label names no total, elimination, intersegment amount, corporate amount,
// reconciling item, unallocated amount or adjustment; groups the filing repeats under one name are one segment. Its
// revenue is the first figure filed for the quarter under a revenue concept, and its earnings the operating income
// filed for it. The company's total is the table's own consolidated revenue for the quarter, or the quarter's reported
// revenue where the table states none. The segments stand as a table only where their revenue sums to that total within
// what rounding each figure to the table's unit can move it, half a unit a figure; otherwise the raw table is drawn and
// says by how much the rows missed.
// see: The segment table is the segments whose revenue sums to the company's total within rounding, and the raw table where none do
public static class SegmentTable
{
    // The parts of a group's label that name the consolidation's axis rather than a segment, read without case and with
    // the renderer's "[Member]" taken off.
    public static IReadOnlyList<string> AxisParts { get; } =
        ["operating segments", "operating segment", "reportable segments", "reportable segment", "segments", "segment"];

    // The words that mark a group as no segment of its own.
    public static IReadOnlyList<string> NotASegment { get; } =
        ["total", "elimination", "intersegment", "inter-segment", "corporate", "reconcil", "unallocated", "adjustment"];

    public const string OperatingIncomeConcept = "OperatingIncomeLoss";

    // The rule over a table read from its stored form, with the quarter's reported revenue where the table states no total.
    public static SegmentReading Read(SegmentBreakdown table, decimal? reportedRevenue = null)
    {
        var shortest = table.Periods.Count == 0 ? 0 : table.Periods.Min(period => period.Months);
        DateOnly? quarter = table.Periods.Where(period => period.Months == shortest).Select(period => (DateOnly?)period.Ended).Max();

        if (quarter is not { } ended)
        {
            return new SegmentReading(table.Report, null, shortest, null, null, [], "the table states no period");
        }

        var yearEarlier = table.Periods
            .Where(period => period.Months == shortest && Math.Abs(period.Ended.DayNumber - ended.AddYears(-1).DayNumber) <= QuarterFetch.NearDays)
            .Select(period => (DateOnly?)period.Ended)
            .FirstOrDefault();

        bool InQuarter(SegmentFigure figure, DateOnly end) => figure.Period.Months == shortest && figure.Period.Ended == end;

        // A row is money in the table's scale where it states no unit or the table's own currency, as one filer marks
        // every money row; a count or a share states a unit of its own.
        var currency = SecEdgarArchive.Currency(table.Title);

        bool Money(SegmentFigure figure) => figure.Unit is null || string.Equals(figure.Unit, currency, StringComparison.Ordinal);

        decimal? First(IEnumerable<SegmentFigure> figures, Func<SegmentFigure, bool> concept, DateOnly? end) =>
            end is { } on ? figures.FirstOrDefault(figure => Money(figure) && figure.Value is not null && concept(figure) && InQuarter(figure, on))?.Value : null;

        var total = First(table.Consolidated, IsRevenue, ended) ?? reportedRevenue;

        var segments = table.Groups
            .Select(group => (Name: NameOf(group.Label), group.Figures))
            .Where(group => group.Name is not null)
            .GroupBy(group => group.Name!, StringComparer.Ordinal)
            .Select(named => (Name: named.Key, Figures: named.SelectMany(group => group.Figures).ToArray()))
            .Select(segment => (
                segment.Name,
                Revenue: First(segment.Figures, IsRevenue, ended),
                RevenueBefore: First(segment.Figures, IsRevenue, yearEarlier),
                Earnings: First(segment.Figures, IsEarnings, ended),
                EarningsBefore: First(segment.Figures, IsEarnings, yearEarlier)))
            .Where(segment => segment.Revenue is not null)
            .ToArray();

        if (segments.Length == 0)
        {
            return new SegmentReading(table.Report, ended, shortest, total, null, [], "the table states no segment's revenue for the quarter");
        }

        var sum = segments.Sum(segment => segment.Revenue!.Value);

        if (total is not { } whole || whole <= 0m)
        {
            return new SegmentReading(table.Report, ended, shortest, null, sum, [], "neither the table nor the quarter states the company's revenue to sum against");
        }

        var tolerance = Math.Max(table.Scale, 1) * (segments.Length + 1) / 2m;

        if (Math.Abs(sum - whole) > tolerance)
        {
            return new SegmentReading(
                table.Report,
                ended,
                shortest,
                whole,
                sum,
                [],
                "the segments' revenue does not sum to the company's within rounding");
        }

        return new SegmentReading(
            table.Report,
            ended,
            shortest,
            whole,
            sum,
            [
                .. segments.Select(segment => new SegmentRow(
                    segment.Name,
                    segment.Revenue!.Value,
                    segment.RevenueBefore,
                    Percents.FromFraction(QuarterFetch.Grown(segment.Revenue, segment.RevenueBefore)),
                    segment.Earnings,
                    segment.EarningsBefore,
                    Percents.FromFraction(QuarterFetch.Grown(segment.Earnings, segment.EarningsBefore)),
                    Percents.FromFraction(QuarterFetch.Margined(segment.Revenue, whole))!.Value)),
            ],
            null);
    }

    // A group's segment name: its label's parts with those naming the consolidation's axis taken off, joined; none where
    // nothing is left or a part names no segment of its own.
    public static string? NameOf(string label)
    {
        var parts = label.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Replace("[Member]", string.Empty, StringComparison.OrdinalIgnoreCase).Trim())
            .Where(part => part.Length > 0 && !AxisParts.Contains(part.ToLowerInvariant()))
            .ToArray();

        return parts.Length == 0 || parts.Any(part => NotASegment.Any(word => part.Contains(word, StringComparison.OrdinalIgnoreCase)))
            ? null
            : string.Join(", ", parts);
    }

    static bool IsRevenue(SegmentFigure figure) =>
        SecEdgarArchive.SegmentRevenue.Any(revenue => figure.Concept.EndsWith("_" + revenue, StringComparison.Ordinal));

    static bool IsEarnings(SegmentFigure figure) =>
        figure.Concept.EndsWith("_" + OperatingIncomeConcept, StringComparison.Ordinal);

    // A table as the fundamentals payload stores it, read back: its report, title, scale and periods, the consolidated
    // rows and each group's figures, every figure's value already in the table's scale.
    public static SegmentBreakdown FromStored(JsonElement segments)
    {
        static DateOnly Day(JsonElement element) =>
            DateOnly.ParseExact(element.GetString() ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        static IReadOnlyList<SegmentFigure> Figures(JsonElement figures) =>
            [
                .. figures.EnumerateArray().Select(figure => new SegmentFigure(
                    figure.TryGetProperty("concept", out var concept) ? concept.GetString() ?? string.Empty : string.Empty,
                    figure.TryGetProperty("lineItem", out var line) ? line.GetString() ?? string.Empty : string.Empty,
                    figure.TryGetProperty("unit", out var unit) && unit.ValueKind == JsonValueKind.String ? unit.GetString() : null,
                    new ReportPeriod(figure.GetProperty("months").GetInt32(), Day(figure.GetProperty("ended"))),
                    figure.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                        && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var figured)
                            ? figured
                            : null)),
            ];

        return new SegmentBreakdown(
            segments.TryGetProperty("report", out var report) ? report.GetString() ?? string.Empty : string.Empty,
            segments.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
            segments.TryGetProperty("scale", out var scale) && scale.TryGetInt32(out var times) ? times : 1,
            [.. segments.GetProperty("periods").EnumerateArray().Select(period => new ReportPeriod(period.GetProperty("months").GetInt32(), Day(period.GetProperty("ended"))))],
            Figures(segments.GetProperty("consolidated")),
            [
                .. segments.GetProperty("groups").EnumerateArray().Select(group => new SegmentGroup(
                    group.GetProperty("label").GetString() ?? string.Empty,
                    group.TryGetProperty("dimension", out var dimension) && dimension.ValueKind == JsonValueKind.String ? dimension.GetString() : null,
                    Figures(group.GetProperty("figures")))),
            ]);
    }
}
