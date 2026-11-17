using EquityBrief.Core.Cloudflare;
using EquityBrief.Core.Time;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// Home's line on the public copy's uploads, from the operator's word of 2026-11-17, drawn beside the masthead with every part of
// the page before it standing as it was, and none on a Home that carries no record view, as the copy's does.
public partial class ReadSurface
{
    // see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
    [Fact]
    public void HomeDrawsTheCopysNewestUploadBesideItsMastheadAndTheCopysHomeDrawsNone()
    {
        var marks = new MarkRenderer();
        var app = new SinglePageApp();
        var zone = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);
        var record = SiteUpload.FailedAfter(
            SiteUpload.Succeeded(new DateTimeOffset(2026, 11, 12, 5, 58, 0, TimeSpan.Zero), new DateOnly(2026, 11, 11), "https://a.equitybrief.pages.dev"),
            new DateTimeOffset(2026, 11, 17, 20, 48, 0, TimeSpan.Zero),
            "the check found words of the method on 9 pages of the copy, so nothing was sent");
        var plain = app.HomeRegion(marks, Day(), string.Empty);
        var drawn = app.HomeRegion(marks, Day() with { Upload = new SiteUploadView(record, zone) }, string.Empty);

        Assert.DoesNotContain("site-upload", plain, StringComparison.Ordinal);
        Assert.Contains("<p class=\"site-upload degraded\" data-upload=\"failed\">The public copy was last uploaded at 00:58 ET on 12 Nov 2026, with the night of 11 Nov 2026.", drawn, StringComparison.Ordinal);
        Assert.Contains("The newest attempt, at 15:48 ET on 17 Nov 2026, failed: the check found words of the method on 9 pages of the copy, so nothing was sent.</p>", drawn, StringComparison.Ordinal);

        // The line is added after the masthead and nothing else on the page moves or goes.
        var line = drawn[drawn.IndexOf("<p class=\"site-upload", StringComparison.Ordinal)..];

        Assert.Equal(plain, drawn.Replace(line[..(line.IndexOf("</p>", StringComparison.Ordinal) + "</p>".Length)], string.Empty, StringComparison.Ordinal));
        Assert.True(drawn.IndexOf("screen-mast", StringComparison.Ordinal) < drawn.IndexOf("site-upload", StringComparison.Ordinal));
    }
}
