using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Service;
using DawntrailReady.Core.Tests.Upgrade;
using DawntrailReady.Core.Upgrade;
using static DawntrailReady.Core.Tests.Upgrade.Kit;

namespace DawntrailReady.Core.Tests.Service;

/// <summary>
/// Which mods count as "needs updating": only those TexTools' upgrade really changes. TexTools also re-saves
/// materials it doesn't change (hair mashup repathing), and its writer normalises padding and the dye flag; a mod
/// whose only differences are such re-saves is already Dawntrail-ready and must be left alone.
/// </summary>
public class RealChangeTests
{
    private static bool Real(FakeGame game, ModpackData data)
    {
        var before = DawntrailBackend.Snapshot(data);
        var result = new ModpackUpgrader(game).UpgradeModpack(data).GetAwaiter().GetResult();
        return DawntrailBackend.HasRealChanges(before, result);
    }

    [Fact]
    public void Legacy_gear_needs_updating()
    {
        var (game, data) = Scenarios.LegacyGearWithNormal();
        Assert.True(Real(game, data));
    }

    [Fact]
    public void An_old_hair_material_needs_updating()
    {
        var (game, data) = Scenarios.HairMaterial();
        Assert.True(Real(game, data));
    }

    [Fact]
    public void A_dawntrail_mod_does_not()
    {
        var (game, data) = Scenarios.DawntrailOnly();
        Assert.False(Real(game, data));
    }

    [Fact]
    public async Task A_dawntrail_hair_material_that_is_only_resaved_does_not()
    {
        // Material-only hair: TexTools' mashup repathing rewrites it even though nothing needs changing.
        var bytes = Bytes(Scenarios.Hair(oldConstants: false));
        var data = Pack(Option("", "Default", (Scenarios.HairMtrl, bytes)));

        var before = DawntrailBackend.Snapshot(data);
        var result = await new ModpackUpgrader(new FakeGame()).UpgradeModpack(data);

        Assert.True(result.AnyChanges); // what TexTools itself reports
        Assert.False(DawntrailBackend.HasRealChanges(before, result));
    }

    [Fact]
    public async Task A_resave_that_only_normalises_the_dye_flag_does_not()
    {
        // The same material, but carrying the dye-table bit without a dye table: TexTools' writer clears it, so the
        // re-saved bytes differ from the original while nothing of substance changed.
        var bytes = Bytes(Scenarios.Hair(oldConstants: false));
        SetDyeFlag(bytes);
        var data = Pack(Option("", "Default", (Scenarios.HairMtrl, bytes)));

        var before = DawntrailBackend.Snapshot(data);
        var result = await new ModpackUpgrader(new FakeGame()).UpgradeModpack(data);

        Assert.Contains(result.Changes, c => c.ContentChanged);
        Assert.False(DawntrailBackend.HasRealChanges(before, result));
    }

    /// <summary>Sets bit 0x08 of AdditionalData[0], found from the mtrl header like the game reads it.</summary>
    private static void SetDyeFlag(byte[] mtrl)
    {
        var stringTableSize = BitConverter.ToUInt16(mtrl, 8);
        int textures = mtrl[12], uvSets = mtrl[13], colorSets = mtrl[14], additionalSize = mtrl[15];
        Assert.True(additionalSize >= 1);
        var additional = 16 + 4 * (textures + uvSets + colorSets) + stringTableSize;
        mtrl[additional] |= 0x08;
    }
}
