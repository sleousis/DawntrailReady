using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Service;
using DawntrailReady.Core.Tests.Upgrade;
using static DawntrailReady.Core.Tests.Upgrade.Kit;

namespace DawntrailReady.Core.Tests.Service;

/// <summary>
/// "Is this mod Dawntrail-compatible?" Only mods holding a file that no longer works in Dawntrail are upgraded;
/// the rest are left alone even where TexTools' upgrade would still rearrange them.
/// </summary>
public class PreDawntrailDetectorTests
{
    private static PreDawntrailReport Inspect((FakeGame Game, ModpackData Data) s) => PreDawntrailDetector.Inspect(s.Data, s.Game);

    [Fact]
    public void A_legacy_material_is_pre_dawntrail() => Assert.Equal(1, Inspect(Scenarios.LegacyGearWithNormal()).LegacyMaterials);

    [Fact]
    public void An_old_hair_material_is_pre_dawntrail() => Assert.Equal(1, Inspect(Scenarios.HairMaterial()).LegacyMaterials);

    [Fact]
    public void A_v5_model_is_pre_dawntrail() => Assert.Equal(1, Inspect(Scenarios.All["old model header"]()).OldModels);

    [Fact]
    public void An_old_skin_texture_path_is_pre_dawntrail() => Assert.True(Inspect(Scenarios.All["skin alias"]()).OldTextures > 0);

    [Fact]
    public void An_old_iris_mask_is_pre_dawntrail() => Assert.True(Inspect(Scenarios.EyeMask()).OldTextures > 0);

    [Fact]
    public void Old_hair_textures_without_their_material_are_pre_dawntrail() => Assert.Equal(2, Inspect(Scenarios.UnclaimedHair()).OldTextures);

    [Fact]
    public void A_dawntrail_mod_is_not() => Assert.False(Inspect(Scenarios.DawntrailOnly()).Any);

    [Fact]
    public void An_eye_mod_that_ships_dawntrail_iris_textures_is_not_even_with_old_masks()
    {
        // The real case that went wrong: a Dawntrail eye mod keeps its old masks for older setups. TexTools' mask
        // conversion would overwrite the mod's own Dawntrail iris texture, so the mod must be left alone.
        var (game, data) = Scenarios.EyeMask();
        data.Options[0].Files![Scenarios.IrisDiffuse] = new MemoryFileSource(SolidTex(4, 4, 9, 9, 9, 255));

        Assert.False(PreDawntrailDetector.Inspect(data, game).Any);
    }

    [Fact]
    public void An_old_iris_mask_the_game_has_no_iris_material_for_is_not()
    {
        // TexTools skips such a mask, so there is nothing to convert.
        var (_, data) = Scenarios.EyeMask();
        Assert.False(PreDawntrailDetector.Inspect(data, new FakeGame()).Any);
    }

    [Fact]
    public void Old_hair_textures_are_not_when_the_mod_already_has_the_dawntrail_paths()
    {
        // TexTools treats these as already converted and copies nothing.
        var (game, data) = Scenarios.UnclaimedHair();
        data.Options[0].Files![Scenarios.VanillaHairNorm] = new MemoryFileSource(SolidTex(4, 4, 1, 1, 1, 1));

        Assert.False(PreDawntrailDetector.Inspect(data, game).Any);
    }

    [Fact]
    public void A_dawntrail_hair_mod_with_highlight_options_is_not()
    {
        // TexTools would staple textures into these options (or refuse the unresolvable one) — but nothing here is old.
        Assert.False(Inspect(Scenarios.HighlightStaple()).Any);
        Assert.False(Inspect(Scenarios.HighlightUnresolvable()).Any);
    }

    [Fact]
    public void An_old_skin_path_read_by_the_mods_own_material_is_not()
    {
        // A Dawntrail skin material of the mod's own that still reads the old texture path: it works as it is.
        const string oldSkin = "chara/human/c1401/obj/tail/t0001/texture/--c1401t0001_etc_d.tex";
        var tail = Material("chara/human/c1401/obj/tail/t0001/material/v0001/mt_c1401t0001_a.mtrl", "skin.shpk", [], [],
            Texture(oldSkin, ESamplerId.g_SamplerDiffuse));
        tail.AdditionalData = [0, 0, 0, 0];
        tail.ColorsetStrings = [];
        var data = Pack(Option("", "Default", (tail.MTRLPath, Bytes(tail)), (oldSkin, SolidTex(4, 4, 1, 2, 3, 4))));

        Assert.False(PreDawntrailDetector.Inspect(data, new FakeGame()).Any);
    }

    [Fact]
    public void Every_scenario_the_upgrade_changes_is_flagged_or_is_a_known_rearrangement()
    {
        // The detector may skip mods where TexTools only rearranges (highlight stapling); it must never skip a mod
        // whose files TexTools actually converts.
        string[] rearrangeOnly = ["highlight staple", "highlight unresolvable", "dawntrail only"];
        foreach (var (name, build) in Scenarios.All)
        {
            if (rearrangeOnly.Contains(name)) continue;
            Assert.True(Inspect(build()).Any, name);
        }
    }
}
