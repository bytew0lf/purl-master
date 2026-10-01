namespace PurlMaster.Tests;

public class CpeTests
{
    [Theory]
    [InlineData("cpe:2.3:a:busybox:busybox:1.36.1:*:*:*:*:*:*:*", "pkg:generic/busybox/busybox@1.36.1?cpe_part=a")]
    [InlineData("cpe:/a:busybox:busybox:1.36.1", "pkg:generic/busybox/busybox@1.36.1?cpe_part=a")]
    [InlineData("cpe:2.3:o:MICROSOFT:WINDOWS:11:-:*:EN-US:*:*:X64:*", "pkg:generic/microsoft/windows@11?cpe_language=en-us&cpe_part=o&cpe_target_hw=x64")]
    [InlineData("cpe:2.3:h:acme:router:-:*:*:*:*:*:*:*", "pkg:generic/acme/router?cpe_part=h")]
    [InlineData("cpe:2.3:*:*:product:*:*:*:*:*:*:*:*", "pkg:generic/product")]
    [InlineData("cpe:2.3:-:-:product:-:-:-:-:-:-:-:-", "pkg:generic/product")]
    [InlineData("cpe:/:vendor:product", "pkg:generic/vendor/product")]
    [InlineData("cpe:/a::product", "pkg:generic/product?cpe_part=a")]
    [InlineData("cpe:/a:ACME:WIDGET:1.0:SP1:enterprise:en-US", "pkg:generic/acme/widget@1.0?cpe_edition=enterprise&cpe_language=en-us&cpe_part=a&cpe_update=sp1")]
    [InlineData("cpe:/a:hp:insight_diagnostics:7.4.0.1570::~~online~win2003~x64~", "pkg:generic/hp/insight_diagnostics@7.4.0.1570?cpe_part=a&cpe_sw_edition=online&cpe_target_hw=x64&cpe_target_sw=win2003")]
    [InlineData("cpe:2.3:a:hp:insight_diagnostics:7.4.0.1570:*:*:*:online:win2003:x64:*", "pkg:generic/hp/insight_diagnostics@7.4.0.1570?cpe_part=a&cpe_sw_edition=online&cpe_target_hw=x64&cpe_target_sw=win2003")]
    [InlineData(@"cpe:2.3:a:foo\\bar:big\$money_2010:1.0:*:*:*:*:*:*:*", "pkg:generic/foo%5Cbar/big%24money_2010@1.0?cpe_part=a")]
    [InlineData("cpe:/a:foo%5cbar:big%24money_2010:1.0", "pkg:generic/foo%5Cbar/big%24money_2010@1.0?cpe_part=a")]
    [InlineData(@"cpe:2.3:a:foo:bar\:mumble:1.0:*:*:*:*:*:*:*", "pkg:generic/foo/bar:mumble@1.0?cpe_part=a")]
    [InlineData("cpe:/a:foo:bar%3Amumble:1.0", "pkg:generic/foo/bar:mumble@1.0?cpe_part=a")]
    [InlineData(@"cpe:2.3:a:acme:tool\@v\?x\#y\%z\&a\=b:8.\*:sp\?:*:*:*:*:*:*", "pkg:generic/acme/tool%40v%3Fx%23y%25z%26a%3Db@8.%2A?cpe_part=a&cpe_update=sp%3F")]
    [InlineData("cpe:/a:acme:tool%40v%3fx%23y%25z%26a%3db:8.%2a:sp%3f", "pkg:generic/acme/tool%40v%3Fx%23y%25z%26a%3Db@8.%2A?cpe_part=a&cpe_update=sp%3F")]
    [InlineData(@"cpe:2.3:a:acme:foo\/bar:1\/2:*:*:*:*:*:*:*", "pkg:generic/acme/foo%2Fbar@1%2F2?cpe_part=a")]
    [InlineData(@"cpe:2.3:a:acme:\*:1.0:*:*:*:*:*:*:*", "pkg:generic/acme/%2A@1.0?cpe_part=a")]
    [InlineData("cpe:/a:acme:%2a:1.0", "pkg:generic/acme/%2A@1.0?cpe_part=a")]
    [InlineData(@"cpe:2.3:a:acme:name:1.0:*:*:*:*:*:*:a\~b", "pkg:generic/acme/name@1.0?cpe_other=a~b&cpe_part=a")]
    [InlineData("cpe:/a:acme:name:1.0::~ed~sw~target~hw~other:en", "pkg:generic/acme/name@1.0?cpe_edition=ed&cpe_language=en&cpe_other=other&cpe_part=a&cpe_sw_edition=sw&cpe_target_hw=hw&cpe_target_sw=target")]
    public void ConvertsSupportedBindings(string cpe, string expected) =>
        Assert.Equal(expected, CpePurlConverter.Convert(cpe));

    [Theory]
    [InlineData("")]
    [InlineData("cpe:2.2:a:acme:product")]
    [InlineData("wfn:[part=\"a\"]")]
    [InlineData("cpe:2.3:a:vendor:product:1.0")]
    [InlineData("cpe:2.3:a:vendor:product:1.0:*:*:*:*:*:*:*:extra")]
    [InlineData("cpe:2.3:a:vendor::1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:x:vendor:product:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:product:1.0:*:*:en_US:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:product:1.0:*:*:eng-US-extra:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:produ ct:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:prodüct:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:product:1.0:*:*:*:*:*:*:bad\\")]
    [InlineData(@"cpe:2.3:a:vendor:prod\uct:1.0:*:*:*:*:*:*:*")]
    [InlineData(@"cpe:2.3:a:vendor:prod\.uct:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:/a:vendor:product:1.0:sp:ed:en:extra")]
    [InlineData("cpe:/x:vendor:product")]
    [InlineData("cpe:/a:vendor:product%")]
    [InlineData("cpe:/a:vendor:product%xy")]
    [InlineData("cpe:/a:vendor:product%07")]
    [InlineData("cpe:/a:vendor:product%20")]
    [InlineData("cpe:/a:vendor:product%ff")]
    [InlineData("cpe:/a:vendor:product%41")]
    [InlineData("cpe:/a:vendor:product:1.0::~too~few")]
    [InlineData("cpe:/a:vendor:product:1.0:*:edition")]
    [InlineData("cpe:2.3:a:vendor:prod*uct:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:**product:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:?*product:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:/a:vendor:prod%02uct")]
    [InlineData("cpe:2.3:a:vendor:??:1.0:*:*:*:*:*:*:*")]
    public void RejectsMalformedOrUnsupportedCpe(string cpe) =>
        Assert.Throws<FormatException>(() => CpeName.Parse(cpe));

    [Theory]
    [InlineData("cpe:2.3:a:vendor:product:8.*:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:*vendor*:product:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:??product??:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:product:1.0:sp?:*:*:*:*:*:*")]
    [InlineData("cpe:/a:vendor:product:8.%02")]
    [InlineData("cpe:/a:vendor:product:1.0:sp%01")]
    [InlineData("cpe:/a:vendor:%01%01product%01%01:1.0")]
    public void DoesNotTurnWildcardPatternsIntoConcreteIdentity(string cpe)
    {
        Assert.Contains(CpeName.Parse(cpe).Values, v => v.Kind == CpeValueKind.Pattern);
        Assert.Contains("wildcard", Assert.Throws<FormatException>(() => CpePurlConverter.Convert(cpe)).Message);
    }

    [Theory]
    [InlineData("cpe:2.3:a:vendor:*:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:2.3:a:vendor:-:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:/a:vendor")]
    [InlineData(@"cpe:2.3:a:foo\/bar:product:1.0:*:*:*:*:*:*:*")]
    [InlineData("cpe:/a:foo%2fbar:product:1.0")]
    public void RejectsUnrepresentableIdentity(string cpe) =>
        Assert.Throws<FormatException>(() => CpePurlConverter.Convert(cpe));

    [Fact]
    public void MappingIsIndependentOfCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("tr-TR");
            Assert.Equal("pkg:generic/ibm/identity@i?cpe_part=a",
                CpePurlConverter.Convert("cpe:2.3:a:IBM:IDENTITY:I:*:*:*:*:*:*:*"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
