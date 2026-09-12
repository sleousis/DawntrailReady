using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Service;
using DawntrailReady.Core.Tests.Upgrade;
using DawntrailReady.Core.Textures;
using DawntrailReady.Core.Upgrade;
using static DawntrailReady.Core.Tests.Upgrade.Kit;

namespace DawntrailReady.Core.Tests.Service;

/// <summary>Mods whose update would create a texture larger than 4096x4096 are left untouched.</summary>
public class TextureSizeGuardTests
{
    [Fact]
    public void An_iris_mask_whose_converted_texture_would_exceed_4096_is_refused_before_anything_is_built()
    {
        // TexTools paints the mask on a canvas four times its size: 1025 px becomes 4100 px.
        var (game, data) = Scenarios.EyeMask();
        data.Options[0].Files![Scenarios.EyeMaskPath] = new MemoryFileSource(SolidTex(1025, 4, 180, 0, 0, 255));

        var ex = Assert.Throws<TextureTooLargeException>(() => TextureSizeGuard.CheckBeforeUpgrade(data, game));
        Assert.Contains("4100x16", ex.Message);
    }

    [Fact]
    public void An_ordinary_iris_mask_passes()
    {
        var (game, data) = Scenarios.EyeMask();
        TextureSizeGuard.CheckBeforeUpgrade(data, game);
    }

    [Fact]
    public void A_large_mask_that_will_not_be_converted_passes()
    {
        // The mod already ships the Dawntrail iris texture, so TexTools' conversion never runs on this mask.
        var (game, data) = Scenarios.EyeMask();
        data.Options[0].Files![Scenarios.EyeMaskPath] = new MemoryFileSource(SolidTex(1025, 4, 180, 0, 0, 255));
        data.Options[0].Files![Scenarios.IrisDiffuse] = new MemoryFileSource(SolidTex(4, 4, 9, 9, 9, 255));

        TextureSizeGuard.CheckBeforeUpgrade(data, game);
    }

    [Theory]
    [InlineData(5000, 4, true)]
    [InlineData(4, 8192, true)]
    [InlineData(4096, 4096, false)]
    public void A_created_texture_larger_than_4096_is_refused_before_it_is_written(int width, int height, bool refused)
    {
        const string path = "chara/equipment/e0001/texture/v01_c0101e0001_top_id.tex";
        var option = Option("", "Default");
        // Only the 80-byte header is read, so no pixels are needed.
        option.Files![path] = new MemoryFileSource(Tex.CreateTexFileHeader(XivTexFormat.A8R8G8B8, width, height, 1));
        var result = new UpgradeResult(true, []) { Changes = [new ChangedFile(option, path, Added: true, ContentChanged: false)] };

        if (refused) Assert.Throws<TextureTooLargeException>(() => TextureSizeGuard.CheckAfterUpgrade(result));
        else TextureSizeGuard.CheckAfterUpgrade(result);
    }
}
