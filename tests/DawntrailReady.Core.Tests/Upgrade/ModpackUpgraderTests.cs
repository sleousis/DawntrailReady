using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Upgrade;
using static DawntrailReady.Core.Tests.Upgrade.Kit;
using static DawntrailReady.Core.Tests.Upgrade.Scenarios;

namespace DawntrailReady.Core.Tests.Upgrade;

public class ModpackUpgraderTests
{
    private static Half H(float f) => (Half)f;

    [Fact]
    public async Task Legacy_gear_with_its_normal_becomes_characterlegacy_with_an_index_map()
    {
        var (game, data) = LegacyGearWithNormal();
        var files = data.Options[0].Files!;

        var result = await RunUpgrade(game, data);

        Assert.True(result.AnyChanges);
        Assert.True(result.AnyContentChanges);
        var m = Parse(files[GearMtrl], GearMtrl);
        Assert.Equal("characterlegacy.shpk", m.ShaderPackRaw);
        Assert.Equal(1024, m.ColorSetData.Count);
        Assert.Equal(new byte[] { 0x3C, 0x05, 0, 0 }, m.AdditionalData);

        // Row 3 of the old table (halves 48..63) moved into row 3 of the new one, spec power and gloss swapped.
        var row = m.ColorSetData.Skip(3 * 32).Take(32).ToArray();
        Assert.Equal([H(48), H(49), H(50), H(55), H(52), H(53), H(54), H(51), H(56), H(57), H(58)], row[..11]);
        Assert.Equal(H(59), row[25]);
        Assert.Equal(H(1), row[26]);
        Assert.Equal([H(60), H(61), H(62), H(63)], row[28..32]);
        // Rows 16..31 are the default row.
        var fresh = m.ColorSetData.Skip(20 * 32).Take(32).ToArray();
        Assert.Equal(EndwalkerUpgrade.GetDefaultColorsetRow(EShaderPack.CharacterLegacy), fresh);

        // Dye: characterlegacy keeps the template number.
        Assert.Equal((5u << 16) | 0x13, BitConverter.ToUInt32(m.ColorSetDyeData, 0));
        Assert.Equal(128, m.ColorSetDyeData.Length);

        // Index sampler appended, tiling copied from the normal's sampler.
        var id = m.Textures.Last();
        Assert.Equal(GearIndex, id.TexturePath);
        Assert.Equal(0x565F8FD8u, id.Sampler.SamplerIdRaw);
        Assert.Equal(0x000F8349u, id.Sampler.SamplerSettingsRaw);

        // The index map: alpha 204 is old row 12 exactly -> row pair 6 (R = 6*17+4), weight fully on the first row.
        var (w, h, px) = await Pixels(files[GearIndex]);
        Assert.Equal((4, 4), (w, h));
        Assert.Equal(new byte[] { 106, 255, 0, 255 }, px[..4]);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public async Task Legacy_gear_without_its_normal_in_the_option_gets_no_index_file()
    {
        var (game, data) = Scenarios.All["legacy gear without normal"]();
        var files = data.Options[0].Files!;

        var result = await RunUpgrade(game, data);

        Assert.True(result.AnyChanges);
        Assert.Contains(Parse(files[GearMtrl], GearMtrl).Textures, t => t.TexturePath == GearIndex);
        Assert.False(files.ContainsKey(GearIndex));
    }

    [Fact]
    public async Task A_material_without_a_normal_is_left_as_it_was_and_the_error_noted()
    {
        var (game, data) = Scenarios.All["legacy gear without any normal sampler"]();
        var before = data.Options[0].Files![GearMtrl];

        var result = await RunUpgrade(game, data);

        Assert.False(result.AnyChanges);
        Assert.Same(before, data.Options[0].Files![GearMtrl]);
        Assert.Single(result.Notes);
    }

    [Fact]
    public async Task Specular_with_diffuse_switches_to_mask_compatibility_without_touching_the_texture()
    {
        var (game, data) = SpecularCompat();
        var files = data.Options[0].Files!;
        var spec = files[GearSpec];

        await RunUpgrade(game, data);

        var m = Parse(files[GearMtrl], GearMtrl);
        Assert.Equal(ESamplerId.g_SamplerMask, m.Textures.Single(t => t.TexturePath == GearSpec).Sampler.SamplerId);
        Assert.Contains(m.ShaderKeys, k => k.KeyId == 0xC8BD1DEF && k.Value == 0x198D11CD);
        Assert.Contains(m.ShaderKeys, k => k.KeyId == 0xB616DC5A && k.Value == 0x600EF9DF);
        Assert.Same(spec, files[GearSpec]);
    }

    [Fact]
    public async Task Glass_copies_the_sample_material_and_offsets_dye_templates_by_1000()
    {
        var (game, data) = Glass();
        var files = data.Options[0].Files!;

        await RunUpgrade(game, data);

        var m = Parse(files[GearMtrl], GearMtrl);
        Assert.Equal("characterglass.shpk", m.ShaderPackRaw);
        Assert.Equal([(0x11u, 0x22u)], m.ShaderKeys.Select(k => (k.KeyId, k.Value)));
        Assert.Equal([1.5f], m.ShaderConstants.Single(c => c.ConstantId == 0x33).Values);
        Assert.Equal(EMaterialFlags1.HideBackfaces, m.MaterialFlags);
        Assert.Equal((1005u << 16) | 0x13, BitConverter.ToUInt32(m.ColorSetDyeData, 0));
        var row = m.ColorSetData.Skip(3 * 32).Take(32).ToArray();
        Assert.Equal([H(48), H(49), H(50), H(1)], row[..4]);                       // [3] stays default for glass
        Assert.Equal([H(0.8100586f), H(0.8100586f), H(0.8100586f), H(0)], row[4..8]); // glass specular; [7] glass default
        Assert.Equal(H(2.5f), row[14]);
        Assert.Equal(H(5), row[27]);
    }

    [Fact]
    public async Task A_legacy_mask_texture_is_remapped_the_legacy_way()
    {
        var (game, data) = LegacyMask();
        var files = data.Options[0].Files!;

        await RunUpgrade(game, data);

        var (_, _, px) = await Pixels(files[GearMask]);
        Assert.Equal(new byte[] { 30, 20, 10, 40 }, px[..4]); // R'=B, G'=G, B'=R, A kept
    }

    [Fact]
    public async Task Hair_material_takes_the_sample_constants_and_its_textures_are_rebuilt()
    {
        var (game, data) = HairMaterial();
        var files = data.Options[0].Files!;

        await RunUpgrade(game, data);

        var m = Parse(files[HairMtrl], HairMtrl);
        Assert.Equal([0x29AC0223u, 0x44u], m.ShaderConstants.Select(c => c.ConstantId));
        Assert.Equal([0.3f], m.ShaderConstants[0].Values);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, m.AdditionalData);

        var (_, _, normal) = await Pixels(files[HairNormal]);
        var (_, _, mask) = await Pixels(files[HairMask]);
        Assert.Equal(new byte[] { 100, 110, 40, 130 }, normal[..4]); // blue = old mask alpha
        Assert.Equal(new byte[] { 20, 245, 49, 10 }, mask[..4]);     // R=old G, G=remap(255-old R), B=49, A=old R
    }

    [Fact]
    public async Task A_highlight_option_missing_the_mask_gets_it_stapled_in()
    {
        var (game, data) = HighlightStaple();

        var result = await RunUpgrade(game, data);

        Assert.True(result.AnyChanges);
        Assert.Same(data.Options[0].Files![HairMask], data.Options[1].Files![HairMask]);
    }

    [Fact]
    public async Task An_unresolvable_highlight_option_fails_the_whole_upgrade_like_TexTools()
    {
        var (game, data) = HighlightUnresolvable();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunUpgrade(game, data));
        Assert.StartsWith("Cannot upgrade modpack - Highlight/Visibility options are unresolveable", ex.Message);
    }

    [Fact]
    public async Task An_old_skin_texture_gets_its_Dawntrail_path_as_an_alias_of_the_same_file()
    {
        var (game, data) = Scenarios.All["skin alias"]();
        var files = data.Options[0].Files!;

        var result = await RunUpgrade(game, data);

        Assert.True(result.AnyContentChanges);
        Assert.Same(files["chara/bibo/midlander_d.tex"], files["chara/bibo_mid_base.tex"]);
    }

    [Fact]
    public async Task Unclaimed_old_hair_textures_move_to_the_vanilla_material_paths_and_are_rebuilt()
    {
        var (game, data) = UnclaimedHair();
        var files = data.Options[0].Files!;

        await RunUpgrade(game, data);

        var (_, _, normal) = await Pixels(files[VanillaHairNorm]);
        var (_, _, mask) = await Pixels(files[VanillaHairMask]);
        Assert.Equal(new byte[] { 100, 110, 40, 130 }, normal[..4]);
        Assert.Equal(new byte[] { 20, 245, 49, 10 }, mask[..4]);
        Assert.True(files.ContainsKey(OldHairN));
    }

    [Fact]
    public async Task An_old_iris_mask_becomes_the_vanilla_iris_diffuse_at_four_times_the_size()
    {
        var (game, data) = EyeMask();
        var files = data.Options[0].Files!;

        await RunUpgrade(game, data);

        var (w, h, _) = await Pixels(files[IrisDiffuse]);
        Assert.Equal((128, 128), (w, h));
    }

    [Fact]
    public async Task A_Dawntrail_mod_is_left_alone()
    {
        var (game, data) = DawntrailOnly();

        var result = await RunUpgrade(game, data);

        Assert.False(result.AnyChanges);
        Assert.False(result.AnyContentChanges);
        Assert.Empty(result.Changes);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public async Task Upgrading_an_upgraded_mod_again_changes_nothing()
    {
        var (game, data) = LegacyGearWithNormal();
        await RunUpgrade(game, data);

        var again = await RunUpgrade(game, data);

        Assert.False(again.AnyChanges);
    }

    [Fact]
    public async Task Without_a_Dawntrail_install_the_upgrade_fails_with_TexTools_message()
    {
        var data = Pack(Option("Body", "Big", (GearNormal, SolidTex(4, 4, 1, 1, 1, 1))));

        var ex = await Assert.ThrowsAsync<Exception>(() => RunUpgrade(new FakeGame(dawntrail: false), data));
        Assert.Equal("An error occurred while updating Group: Body - Option: Big\n\nThe currently set FFXIV Directory is not a Dawntrail install.", ex.Message);
    }

    [Fact]
    public async Task A_broken_old_model_fails_the_whole_upgrade_like_TexTools()
    {
        var (game, data) = Scenarios.All["old model header"]();

        var ex = await Assert.ThrowsAsync<Exception>(() => RunUpgrade(game, data));
        Assert.StartsWith("An error occurred while updating Group:", ex.Message);
    }

    [Fact]
    public async Task Index_maps_are_made_in_every_option_that_holds_the_normal_not_where_the_material_is()
    {
        // Round 1 collects upgrade targets from every option; round 2 builds each target inside any option that
        // holds its source. Options are never merged: the material's option gets no copy of the default's normal.
        var data = Pack(
            Option("", "Default", (GearNormal, SolidTex(4, 4, 1, 1, 1, 204))),
            Option("Style", "A", (GearMtrl, Bytes(LegacyGear()))));

        await RunUpgrade(new FakeGame(), data);

        Assert.True(data.Options[0].Files!.ContainsKey(GearIndex));
        Assert.False(data.Options[1].Files!.ContainsKey(GearIndex));
    }
}
