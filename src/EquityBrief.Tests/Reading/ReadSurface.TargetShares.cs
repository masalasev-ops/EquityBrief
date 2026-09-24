using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 11.9: the run page states each reason's share of the index on the night it is
// read for, its median over the ordinary nights and its target, the same three for the share
// firing any reason, the ordinary nights counted against sixty and every event session with the
// reason that made it one, each read back off the page against the test's own arithmetic over
// constructed nights.
// see: A reason's threshold is calibrated to a target share of the index over ordinary nights and a night a usually quiet reason floods is left out
public partial class ReadSurface
{
    const int SharesMembers = 20;

    // Five nights over twenty members, each reason firing on a run of members starting at its own
    // place so the share firing any reason is a union and not the largest count. At entry zone
    // fires for more than a quarter of the index every night; unusual volume, usually one member
    // or two, fires for twelve on 2026-09-28; the first night's rows are in the shape they took
    // before the 5.4 corrections, earnings soon firing for eighteen; and the second night's session
    // is before the calendar read each member's own listing alone, so its earnings soon, and with
    // it the share firing any reason, counts toward no share.
    static readonly (string Night, bool Before, int[] Fired)[] SharesNights =
    [
        ("2026-09-01", true, [12, 2, 0, 0, 1, 18]),
        ("2026-09-22", false, [11, 1, 1, 0, 1, 2]),
        ("2026-09-28", false, [13, 3, 0, 1, 12, 2]),
        ("2026-09-29", false, [10, 2, 0, 0, 0, 1]),
        ("2026-09-30", false, [12, 0, 2, 1, 2, 0]),
    ];

    // Whether a night's session is one the calendar read each member's own listing alone for,
    // stated here rather than read off the code.
    static bool OverOwnListing(string night) => string.CompareOrdinal(night, "2026-09-24") >= 0;

    static readonly int[] SharesStart = [0, 5, 10, 15, 3, 8];

    [Fact]
    public async Task EachReasonsShareIsDrawnAgainstItsTargetWithEachEventSessionMarkedAndLeftOut()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var (night, before, fired) in SharesNights)
        {
            for (var member = 0; member < SharesMembers; member++)
            {
                store.Execute(
                    "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES " +
                    $"('T{member.ToString("00", CultureInfo.InvariantCulture)}', '{night}', '{SharesReasons(member, fired, before)}', 0, '{{}}', '{{\"candidates\":[],\"skipped\":[]}}');");
            }
        }

        // The arithmetic, done here and not by the page: a reason counts on the rows that evaluated
        // it under its current rule, which the first night's rows do not for the two reasons the
        // 5.4 corrections changed, and earnings soon counts only on a session the calendar read
        // each member's own listing alone for; a night is an event session where a reason fires
        // for more than a quarter of the index while its median over every night it counted on is
        // below a quarter; and every median the page draws leaves those nights out.
        var reasons = ShortlistSeries.Reasons;
        bool Counts(string night, bool before, string reason) =>
            (!before || (reason != ShortlistSeries.EarningsSoon && reason != ShortlistSeries.BreakoutOnVolume))
            && (reason != ShortlistSeries.EarningsSoon || OverOwnListing(night));

        var shares = reasons.ToDictionary(
            reason => reason,
            reason => SharesNights
                .Where(held => Counts(held.Night, held.Before, reason))
                .Select(held => (held.Night, Share: (double)held.Fired[reasons.ToList().IndexOf(reason)] / SharesMembers))
                .ToArray(),
            StringComparer.Ordinal);

        var medians = shares.ToDictionary(pair => pair.Key, pair => SharesMedian([.. pair.Value.Select(held => held.Share)]), StringComparer.Ordinal);

        var events = reasons
            .SelectMany(reason => shares[reason]
                .Where(held => held.Share > 0.25 && medians[reason] < 0.25)
                .Select(held => (held.Night, Reason: reason, held.Share)))
            .ToArray();

        // Worked out rather than read: only the night unusual volume flooded is an event session,
        // and at entry zone, above a quarter every night, marks none; nor does earnings soon's
        // first night, whose eighteen were written before the correction and count for nothing.
        Assert.Equal([("2026-09-28", ShortlistSeries.UnusualVolume, 0.6)], events);
        Assert.Equal(0.6, medians[ShortlistSeries.AtEntryZone]);

        var left = events.Select(held => held.Night).ToHashSet(StringComparer.Ordinal);

        int Union(int[] fired) => Enumerable.Range(0, SharesMembers).Count(member => SharesFires(member, fired).Any(on => on));

        var anyOrdinary = SharesNights
            .Where(held => !held.Before && OverOwnListing(held.Night) && !left.Contains(held.Night))
            .Select(held => (double)Union(held.Fired) / SharesMembers)
            .ToArray();

        Assert.Equal(2, anyOrdinary.Length);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var (night, before, fired) in SharesNights)
        {
            var page = await client.GetStringAsync($"/screens/run/{night}");
            var region = Assert.Single(Blocks(page, "<section class=\"reason-shares\"[^>]*>.*?</section>"));
            var rows = Regex.Matches(region, "<tr data-reason=\"(?<reason>[^\"]+)\" data-fired=\"(?<fired>[^\"]+)\" data-counted=\"(?<counted>[^\"]+)\" data-share=\"(?<share>[^\"]+)\" data-median=\"(?<median>[^\"]+)\" data-ordinary=\"(?<ordinary>\\d+)\" data-target=\"(?<target>[^\"]+)\">(?<cells>.*?)</tr>");

            // Six reasons in section 11's order and the share firing any reason after them.
            Assert.Equal([.. reasons, TargetShares.AnyReason], rows.Select(row => WebUtility.HtmlDecode(row.Groups["reason"].Value)));

            foreach (var reason in reasons)
            {
                var row = rows.Single(held => WebUtility.HtmlDecode(held.Groups["reason"].Value) == reason);
                var at = reasons.ToList().IndexOf(reason);
                var ordinary = shares[reason].Where(held => !left.Contains(held.Night)).Select(held => held.Share).ToArray();

                if (Counts(night, before, reason))
                {
                    Assert.Equal(fired[at].ToString(CultureInfo.InvariantCulture), row.Groups["fired"].Value);
                    Assert.Equal(SharesMembers.ToString(CultureInfo.InvariantCulture), row.Groups["counted"].Value);
                    Assert.Equal((double)fired[at] / SharesMembers, double.Parse(row.Groups["share"].Value, CultureInfo.InvariantCulture));
                    Assert.Contains($"{fired[at]} of {SharesMembers}, {((double)fired[at] / SharesMembers * 100).ToString("0.0", CultureInfo.InvariantCulture)}%", row.Groups["cells"].Value, StringComparison.Ordinal);
                }
                else
                {
                    // A row written before the 5.4 correction counts for neither reason it changed,
                    // and earnings soon on a session read over other listings' dates for nothing.
                    Assert.Equal(("none", "none", "none"), (row.Groups["fired"].Value, row.Groups["counted"].Value, row.Groups["share"].Value));
                    Assert.Contains("not evaluated under its current rule", row.Groups["cells"].Value, StringComparison.Ordinal);
                }

                Assert.Equal(SharesMedian(ordinary), double.Parse(row.Groups["median"].Value, CultureInfo.InvariantCulture));
                Assert.Equal(ordinary.Length.ToString(CultureInfo.InvariantCulture), row.Groups["ordinary"].Value);
                Assert.Equal(0.02, double.Parse(row.Groups["target"].Value, CultureInfo.InvariantCulture));
                Assert.Contains($"{ordinary.Length} of 60", row.Groups["cells"].Value, StringComparison.Ordinal);
                Assert.Contains("2.0%, proposed", row.Groups["cells"].Value, StringComparison.Ordinal);
            }

            var any = rows.Single(held => held.Groups["reason"].Value == TargetShares.AnyReason);

            if (before || !OverOwnListing(night))
            {
                Assert.Equal("none", any.Groups["share"].Value);
            }
            else
            {
                Assert.Equal((Union(fired).ToString(CultureInfo.InvariantCulture), SharesMembers.ToString(CultureInfo.InvariantCulture)), (any.Groups["fired"].Value, any.Groups["counted"].Value));
                Assert.Equal((double)Union(fired) / SharesMembers, double.Parse(any.Groups["share"].Value, CultureInfo.InvariantCulture));
            }

            Assert.Equal(SharesMedian(anyOrdinary), double.Parse(any.Groups["median"].Value, CultureInfo.InvariantCulture));
            Assert.Equal(anyOrdinary.Length.ToString(CultureInfo.InvariantCulture), any.Groups["ordinary"].Value);
            Assert.Equal(0.06, double.Parse(any.Groups["target"].Value, CultureInfo.InvariantCulture));

            // The night the page is for says whether it is an event session, and which reason made
            // it one, and every event session the store holds is named beneath the table.
            var decoded = WebUtility.HtmlDecode(region);

            Assert.Contains(
                left.Contains(night)
                    ? $"The night of {night} is an event session: unusual volume fired for 60.0% of the index against its median of 5.0%."
                    : $"The night of {night} is an ordinary night:",
                decoded,
                StringComparison.Ordinal);

            Assert.Contains($"data-event=\"{(left.Contains(night) ? "true" : "false")}\"", region, StringComparison.Ordinal);
            Assert.Contains("Event sessions, left out of every median: 2026-09-28, unusual volume fired for 60.0% of the index against its median of 5.0%.", decoded, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AStoreWithNoEventSessionSaysSoAndANightNoListingHoldsDrawsNoShare()
    {
        var shares = TargetShares.For(
        [
            new NightFiring(
                new DateOnly(2026, 9, 2),
                ShortlistSeries.Reasons.ToDictionary(reason => reason, reason => new ReasonCount(4, reason == ShortlistSeries.AtEntryZone ? 4 : 0), StringComparer.Ordinal),
                4,
                4),
        ],
        new DateOnly(2026, 9, 3));

        // At entry zone fires for every member on the one night held, so its median is the whole
        // index and it marks nothing; the night asked for holds no listing and draws no share.
        Assert.Empty(shares.Events);
        Assert.All(shares.Reasons, share => Assert.Null(share.Share));
        Assert.Equal(1.0, Assert.Single(shares.Reasons, share => share.Reason == ShortlistSeries.AtEntryZone).Median);

        var markup = WebUtility.HtmlDecode(new EquityBrief.Web.Marks.MarkRenderer().ReasonShares(shares));

        Assert.Contains("No event session among the nights the store holds.", markup, StringComparison.Ordinal);
        Assert.Contains("not evaluated under its current rule", markup, StringComparison.Ordinal);
    }

    static string SharesReasons(int member, int[] fired, bool before)
    {
        var fires = SharesFires(member, fired);

        return "[" + string.Join(",", ShortlistSeries.Reasons.Select((reason, at) =>
        {
            // Each row states the values the reason's current rule writes, the corrected markers
            // and the thresholds the code carries, except where the row is written before the
            // correction, which carries neither marker.
            var values = reason switch
            {
                ShortlistSeries.EarningsSoon => before
                    ? "{\"sessions to the next dated event\":\"0\",\"horizon\":\"20\"}"
                    : "{\"next dated event\":\"2026-09-30\",\"horizon\":\"20\"}",
                ShortlistSeries.BreakoutOnVolume => before ? "{\"close\":\"1\"}" : "{\"close\":\"1\",\"previous close\":\"1\"}",
                ShortlistSeries.UnusualVolume => "{\"volume\":\"1\",\"multiple\":\"2\"}",
                _ => "{\"close\":\"1\"}",
            };

            return $"{{\"name\":\"{reason}\",\"fired\":{(fires[at] ? "true" : "false")},\"values\":{values}}}";
        })) + "]";
    }

    // Which reasons fire on a member: each on the run of members starting at its own place.
    static bool[] SharesFires(int member, int[] fired) =>
        [.. Enumerable.Range(0, fired.Length).Select(at => (member - SharesStart[at] + SharesMembers) % SharesMembers < fired[at])];

    static double SharesMedian(IReadOnlyList<double> shares)
    {
        var ordered = shares.Order().ToArray();
        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}
