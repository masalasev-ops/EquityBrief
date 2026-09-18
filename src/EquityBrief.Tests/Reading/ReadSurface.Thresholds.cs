using System.Text.Json;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;

namespace EquityBrief.Tests.Reading;

public partial class ReadSurface
{
    [Fact]
    public void AReasonsRecordCountsOnlyTheRowsWrittenUnderTheThresholdTheCodeCarries()
    {
        // Three rows under the multiple the code carries, two under another, and one
        // stating none; every setup a win, so each row counted is one win.
        var night = new DateOnly(2026, 9, 4);

        string Unusual(string? multiple) =>
            "[{\"name\":\"unusual volume\",\"fired\":true,\"values\":{\"volume\":\"9\"" +
            (multiple is null ? string.Empty : $",\"{ShortlistSeries.MultipleValue}\":\"{multiple}\"") + "}}]";

        var carried = ShortlistSeries.Thresholds.Single(one => one.Reason == ShortlistSeries.UnusualVolume).Carried;
        var another = (double.Parse(carried, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        (string Ticker, string? Multiple)[] written =
        [
            ("AAAA", carried), ("BBBB", carried), ("CCCC", carried),
            ("DDDD", another), ("EEEE", another),
            ("FFFF", null),
        ];

        var records = RunScreen.Records(
            [.. written.Select(row => new ListingRow(row.Ticker, night, Unusual(row.Multiple), 1, "{}"))],
            [.. written.Select(row => new ResolvedSetup(row.Ticker, night, ForwardReturnSeries.Win, 40))]);

        var record = records.Single(one => one.Reason == ShortlistSeries.UnusualVolume);

        Assert.Equal((3, 3), (record.Won, record.Resolved));
        Assert.Equal(3, RunScreen.Tracks(records).Single(one => one.Reason == ShortlistSeries.UnusualVolume).Total);

        // The rule the page reads through, both ways and for a reason with no threshold.
        Assert.False(ShortlistSeries.MeasuredUnderAnotherThreshold(ShortlistSeries.UnusualVolume, _ => carried));
        Assert.True(ShortlistSeries.MeasuredUnderAnotherThreshold(ShortlistSeries.UnusualVolume, _ => another));
        Assert.True(ShortlistSeries.MeasuredUnderAnotherThreshold(ShortlistSeries.UnusualVolume, _ => null));
        Assert.False(ShortlistSeries.MeasuredUnderAnotherThreshold(ShortlistSeries.AtEntryZone, _ => null));
    }

    [Fact]
    public async Task EveryThresholdAReasonCarriesIsWrittenOnItsRowsUnderItsOwnName()
    {
        // Every numeric constant the reasons are evaluated under is a threshold the table names.
        var constants = typeof(ShortlistSeries).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && (field.FieldType == typeof(int) || field.FieldType == typeof(double) || field.FieldType == typeof(decimal)))
            .Select(field => (field.Name, Value: Convert.ToString(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture)!))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            string.Join("; ", constants.Select(pair => $"{pair.Name}={pair.Value}")),
            string.Join("; ", ShortlistSeries.Thresholds.OrderBy(threshold => threshold.Constant, StringComparer.Ordinal).Select(threshold => $"{threshold.Constant}={threshold.Carried}")));

        // And each is the one section 11 states, read from the expectation that works it.
        var expected = Expected("run-page");

        foreach (var threshold in ShortlistSeries.Thresholds)
        {
            var stated = expected.GetProperty("thresholds").GetProperty(threshold.Reason);

            Assert.Equal(
                (threshold.Reason, stated.GetProperty("value").GetString(), stated.GetProperty("carried").GetString()),
                (threshold.Reason, threshold.Value, threshold.Carried));
        }

        using var store = await Checks.FixtureExpectations.WithReturns();

        var api = Api(store);
        var listings = await api.ListingsAsync();

        Assert.True(listings.Count >= 4, $"Read {listings.Count} listing row(s), expected at least 4.");

        foreach (var listing in listings)
        {
            using var reasons = JsonDocument.Parse(listing.Reasons);

            foreach (var threshold in ShortlistSeries.Thresholds)
            {
                var stored = reasons.RootElement.EnumerateArray().Single(one => one.GetProperty("name").GetString() == threshold.Reason)
                    .GetProperty("values").GetProperty(threshold.Value).GetString();

                Assert.Equal((listing.Ticker, threshold.Reason, threshold.Carried), (listing.Ticker, threshold.Reason, stored));
            }
        }

        var returns = await api.ForwardReturnsAsync();

        Assert.Equal(
            expected.GetProperty("counts").GetProperty("fired").GetInt32(),
            RunScreen.Records(listings, RunScreen.Resolved(returns)).Sum(record => record.Fired));

        // The fixture fires neither thresholded reason, so its rows are marked firing unusual
        // volume, some keeping the carried multiple and the rest given another.
        store.Execute(
            "UPDATE listing SET reasons = (SELECT json_group_array(json(CASE json_extract(value, '$.name') " +
            "WHEN 'unusual volume' THEN json_set(value, '$.fired', json('true'), '$.values.multiple', " +
            "CASE WHEN listing.ticker < 'M' THEN json_extract(value, '$.values.multiple') ELSE 'another' END) " +
            "ELSE value END)) FROM json_each(listing.reasons));");

        var rewritten = await api.ListingsAsync();
        var carried = ShortlistSeries.Thresholds.Single(one => one.Reason == ShortlistSeries.UnusualVolume).Carried;

        var multiples = rewritten
            .Select(listing => JsonDocument.Parse(listing.Reasons).RootElement.EnumerateArray()
                .Single(one => one.GetProperty("name").GetString() == ShortlistSeries.UnusualVolume))
            .Where(one => one.GetProperty("fired").GetBoolean())
            .Select(one => one.GetProperty("values").GetProperty(ShortlistSeries.MultipleValue).GetString())
            .ToArray();

        var underCarried = multiples.Count(multiple => multiple == carried);

        Assert.True(
            underCarried >= 1 && multiples.Length - underCarried >= 1,
            $"Marked {underCarried} row(s) firing under the carried multiple and {multiples.Length - underCarried} under another, expected at least one of each.");

        Assert.Equal(
            underCarried,
            RunScreen.Records(rewritten, RunScreen.Resolved(returns)).Single(record => record.Reason == ShortlistSeries.UnusualVolume).Fired);
    }
}
