using DawntrailReady.Core.Textures;

namespace DawntrailReady.Core.Tests.Textures;

public class TextureHelpersTests
{
    [Theory]
    // Values checked against a real TexTools upgrade output.
    [InlineData(204, 106, 255)]
    [InlineData(205, 106, 240)]
    [InlineData(221, 106, 0)]
    [InlineData(222, 106, 0)]
    [InlineData(230, 123, 255)]
    [InlineData(102, 55, 255)]
    [InlineData(153, 72, 0)]
    [InlineData(0, 4, 255)]
    [InlineData(255, 123, 0)]
    public async Task CreateIndexTexture_MapsNormalAlphaToRowAndBlend(byte alpha, byte expectedRed, byte expectedGreen)
    {
        var normal = new byte[] { 12, 34, 56, alpha };
        var index = new byte[4];

        await TextureHelpers.CreateIndexTexture(normal, index, 1, 1);

        Assert.Equal(new byte[] { expectedRed, expectedGreen, 0, 255 }, index);
    }

    [Fact]
    public async Task CreateHairMaps_RearrangesMaskAndPutsHighlightInNormalBlue()
    {
        var normal = new byte[] { 1, 2, 3, 4, /* second pixel */ 5, 6, 7, 8, /* third */ 9, 9, 9, 9 };
        var mask = new byte[] { 100, 150, 7, 200, /* second pixel */ 255, 0, 0, 0, /* third */ 0, 0, 0, 0 };

        await TextureHelpers.CreateHairMaps(normal, mask, 3, 1);

        // Normal blue <- mask alpha.
        Assert.Equal(new byte[] { 1, 2, 200, 4, 5, 6, 0, 8, 9, 9, 0, 9 }, normal);
        // R <- G, G <- remap(255 - R, 0..255 -> 10..255), B = 49, A <- R.
        // 255 - 100 = 155 -> 155 / 255 * 245 + 10 = 158.92 -> 159; 0 -> 10; 255 -> 255.
        Assert.Equal(new byte[] { 150, 159, 49, 100, 0, 10, 49, 255, 0, 255, 49, 0 }, mask);
    }

    [Fact]
    public async Task UpgradeGearMask_NewStyle_InvertsGlossAndSwapsSpecAndAo()
    {
        var mask = new byte[] { 10, 20, 30, 40, /* gloss 255 */ 1, 255, 2, 3 };

        await TextureHelpers.UpgradeGearMask(mask, 2, 1);

        Assert.Equal(new byte[] { 30, 235, 10, 40, 2, 1, 1, 3 }, mask);
    }

    [Fact]
    public async Task UpgradeGearMask_Legacy_KeepsGloss()
    {
        var mask = new byte[] { 10, 20, 30, 40, 1, 255, 2, 3 };

        await TextureHelpers.UpgradeGearMask(mask, 2, 1, legacy: true);

        Assert.Equal(new byte[] { 30, 20, 10, 40, 2, 255, 1, 3 }, mask);
    }

    [Fact]
    public async Task SwizzleRB_SwapsRedAndBlue()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        await TextureHelpers.SwizzleRB(data, 1, 2);

        Assert.Equal(new byte[] { 3, 2, 1, 4, 7, 6, 5, 8 }, data);
    }

    [Fact]
    public async Task ExpandChannel_CopiesChannelIntoColourAndOptionallyAlpha()
    {
        var a = new byte[] { 1, 2, 3, 4 };
        var b = new byte[] { 1, 2, 3, 4 };

        await TextureHelpers.ExpandChannel(a, 2, 1, 1);
        await TextureHelpers.ExpandChannel(b, 2, 1, 1, includeAlpha: true);

        Assert.Equal(new byte[] { 3, 3, 3, 4 }, a);
        Assert.Equal(new byte[] { 3, 3, 3, 3 }, b);
    }

    [Fact]
    public async Task MaskImage_CopiesAlphaOnly()
    {
        var baseImage = new byte[] { 1, 2, 3, 4 };

        await TextureHelpers.MaskImage(baseImage, [9, 9, 9, 77], 1, 1);

        Assert.Equal(new byte[] { 1, 2, 3, 77 }, baseImage);
    }

    [Fact]
    public async Task OverlayImagePreserveAlpha_TruncatesBlend()
    {
        var baseImage = new byte[] { 0, 100, 250, 10 };

        await TextureHelpers.OverlayImagePreserveAlpha(baseImage, [200, 0, 0, 128], 1, 1);

        // 200 * 0.50196 = 100.39 -> 100; 100 * 0.49804 = 49.80 -> 49; 250 * 0.49804 = 124.51 -> 124.
        Assert.Equal(new byte[] { 100, 49, 124, 10 }, baseImage);
    }

    [Fact]
    public async Task ResizeImage_SameSizeTexture_ReturnsDecodedPixels()
    {
        var tex = new XivTex { TextureFormat = XivTexFormat.A8R8G8B8, Width = 1, Height = 1, MipMapCount = 1, TexData = [3, 2, 1, 4] };

        var pixels = await TextureHelpers.ResizeImage(tex, 1, 1);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, pixels);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1024, true)]
    [InlineData(0, false)]
    [InlineData(600, false)]
    public void IsPowerOfTwo(int value, bool expected)
    {
        Assert.Equal(expected, IOUtil.IsPowerOfTwo(value));
    }

    [Theory]
    [InlineData(600, 512)]
    [InlineData(900, 1024)]
    [InlineData(768, 512)] // A tie goes to the lower power.
    [InlineData(1000, 1024)]
    [InlineData(3, 2)]
    public void RoundToPowerOfTwo(int value, int expected)
    {
        Assert.Equal(expected, IOUtil.RoundToPowerOfTwo(value));
    }
}
