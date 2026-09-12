using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using static DawntrailReady.Core.Tests.Upgrade.Kit;

namespace DawntrailReady.Core.Tests.Upgrade;

/// <summary>
/// Each scenario builds a fresh game and mod every time it is asked, so the same case can be checked and then
/// upgraded independently.
/// </summary>
internal static class Scenarios
{
    public static readonly Dictionary<string, Func<(FakeGame Game, ModpackData Data)>> All = new()
    {
        ["legacy gear with normal"] = LegacyGearWithNormal,
        ["legacy gear without normal"] = () => (new FakeGame(), Pack(Option("", "Default", (GearMtrl, Bytes(LegacyGear())))))
        ,
        ["legacy gear without any normal sampler"] = () => (new FakeGame(), Pack(Option("", "Default",
            (GearMtrl, Bytes(Material(GearMtrl, "character.shpk", LegacyColorset(), LegacyDye(5, 0x13), Texture(GearDiffuse, ESamplerId.g_SamplerDiffuse))))))),
        ["specular compatibility"] = SpecularCompat,
        ["glass"] = Glass,
        ["legacy mask"] = LegacyMask,
        ["hair material"] = HairMaterial,
        ["highlight staple"] = HighlightStaple,
        ["highlight unresolvable"] = HighlightUnresolvable,
        ["skin alias"] = () => (new FakeGame(), Pack(Option("", "Default", ("chara/bibo/midlander_d.tex", SolidTex(4, 4, 1, 2, 3, 4))))),
        ["unclaimed hair"] = UnclaimedHair,
        ["eye mask"] = EyeMask,
        ["old model header"] = () => (new FakeGame(), Pack(Option("", "Default", ("chara/equipment/e0001/model/c0101e0001_top.mdl", [5, 0, 0, 1])))),
        ["dawntrail only"] = DawntrailOnly,
    };

    public static XivMtrl LegacyGear(params MtrlTexture[] extra)
    {
        // Normal sampler with U = Clamp, V = Mirror, so the copied tiling can be seen on the new index sampler.
        var textures = new List<MtrlTexture> { Texture(GearNormal, ESamplerId.g_SamplerNormal, 0x000F8340 | (2 << 2) | 1) };
        textures.AddRange(extra);
        var m = Material(GearMtrl, "character.shpk", LegacyColorset(), LegacyDye(5, 0x13), [.. textures]);
        m.ShaderKeys = [new ShaderKey { KeyId = 0xB616DC5A, Value = 0x600EF9DF }];
        m.ShaderConstants = [new ShaderConstant { ConstantId = 0x29AC0223, Values = [0.5f] }];
        return m;
    }

    public static (FakeGame, ModpackData) LegacyGearWithNormal() => (new FakeGame(), Pack(Option("", "Default",
        (GearMtrl, Bytes(LegacyGear(Texture(GearDiffuse, ESamplerId.g_SamplerDiffuse)))),
        (GearNormal, SolidTex(4, 4, 128, 128, 255, 204)))));

    public static (FakeGame, ModpackData) SpecularCompat()
    {
        var m = LegacyGear(Texture(GearSpec, ESamplerId.g_SamplerSpecular), Texture(GearDiffuse, ESamplerId.g_SamplerDiffuse));
        m.ShaderKeys = [new ShaderKey { KeyId = 0xB616DC5A, Value = 0x11111111 }];
        return (new FakeGame(), Pack(Option("", "Default",
            (GearMtrl, Bytes(m)), (GearNormal, SolidTex(4, 4, 128, 128, 255, 0)), (GearSpec, SolidTex(4, 4, 9, 8, 7, 6)))));
    }

    public static (FakeGame, ModpackData) Glass()
    {
        var game = new FakeGame();
        var sample = Material(GlassSample, "characterglass.shpk", DawntrailColorset(), new byte[128]);
        sample.AdditionalData = [0x34, 0x05, 0, 0];
        sample.ShaderKeys = [new ShaderKey { KeyId = 0x11, Value = 0x22 }];
        sample.ShaderConstants = [new ShaderConstant { ConstantId = 0x33, Values = [1.5f] }];
        game.Files[GlassSample] = Bytes(sample);

        var m = Material(GearMtrl, "characterglass.shpk", LegacyColorset(), LegacyDye(5, 0x13),
            Texture(GearNormal, ESamplerId.g_SamplerNormal), Texture(GearDiffuse, ESamplerId.g_SamplerDiffuse));
        m.MaterialFlags = EMaterialFlags1.HideBackfaces | EMaterialFlags1.Unknown0004 | EMaterialFlags1.Unknown0008;
        return (game, Pack(Option("", "Default", (GearMtrl, Bytes(m)))));
    }

    public static (FakeGame, ModpackData) LegacyMask() => (new FakeGame(), Pack(Option("", "Default",
        (GearMtrl, Bytes(LegacyGear(Texture(GearMask, ESamplerId.g_SamplerMask)))),
        (GearNormal, SolidTex(4, 4, 128, 128, 255, 0)),
        (GearMask, SolidTex(4, 4, 10, 20, 30, 40)))));

    public const string HairMtrl = "chara/human/c0101/obj/hair/h0005/material/v0001/mt_c0101h0005_hir_a.mtrl";
    public const string HairNormal = "chara/human/c0101/obj/hair/h0005/texture/--c0101h0005_hir_n.tex";
    public const string HairMask = "chara/human/c0101/obj/hair/h0005/texture/--c0101h0005_hir_s.tex";

    public static XivMtrl Hair(bool oldConstants)
    {
        var m = Material(HairMtrl, "hair.shpk", [], [],
            Texture(HairNormal, ESamplerId.g_SamplerNormal), Texture(HairMask, ESamplerId.g_SamplerMask));
        m.AdditionalData = [0, 0, 0, 0];
        m.ColorsetStrings = [];
        m.ShaderConstants = oldConstants
            ? [new ShaderConstant { ConstantId = 0x36080AD0, Values = [1f] }, new ShaderConstant { ConstantId = 0x992869AB, Values = [4f] },
               new ShaderConstant { ConstantId = 0x29AC0223, Values = [0.3f] }]
            : [new ShaderConstant { ConstantId = 0x29AC0223, Values = [0.3f] }];
        return m;
    }

    public static FakeGame GameWithHairSample()
    {
        var game = new FakeGame();
        var sample = Material(HairSample, "hair.shpk", [], []);
        sample.AdditionalData = [1, 2, 3, 4];
        sample.ColorsetStrings = [];
        sample.ShaderConstants = [new ShaderConstant { ConstantId = 0x29AC0223, Values = [0.5f] }, new ShaderConstant { ConstantId = 0x44, Values = [2f] }];
        game.Files[HairSample] = Bytes(sample);
        return game;
    }

    public static (FakeGame, ModpackData) HairMaterial() => (GameWithHairSample(), Pack(Option("", "Default",
        (HairMtrl, Bytes(Hair(oldConstants: true))),
        (HairNormal, SolidTex(4, 4, 100, 110, 120, 130)),
        (HairMask, SolidTex(4, 4, 10, 20, 30, 40)))));

    public static (FakeGame, ModpackData) HighlightStaple() => (new FakeGame(), Pack(
        Option("Hair", "Full", (HairMtrl, Bytes(Hair(oldConstants: false))), (HairNormal, SolidTex(4, 4, 1, 1, 1, 1)), (HairMask, SolidTex(4, 4, 2, 2, 2, 2))),
        Option("Highlights", "Normal only", (HairNormal, SolidTex(4, 4, 3, 3, 3, 3)))));

    public static (FakeGame, ModpackData) HighlightUnresolvable() => (new FakeGame(), Pack(
        Option("Hair", "A", (HairMtrl, Bytes(Hair(oldConstants: false))), (HairNormal, SolidTex(4, 4, 1, 1, 1, 1)), (HairMask, SolidTex(4, 4, 2, 2, 2, 2))),
        Option("Hair", "B", (HairMask, SolidTex(4, 4, 5, 5, 5, 5))),
        Option("Highlights", "Normal only", (HairNormal, SolidTex(4, 4, 3, 3, 3, 3)))));

    public const string OldHairN = "chara/human/c0101/obj/hair/h0001/texture/--c0101h0001_hir_n.tex";
    public const string OldHairS = "chara/human/c0101/obj/hair/h0001/texture/--c0101h0001_hir_s.tex";
    public const string VanillaHairMtrl = "chara/human/c0101/obj/hair/h0001/material/v0001/mt_c0101h0001_hir_a.mtrl";
    public const string VanillaHairNorm = "chara/human/c0101/obj/hair/h0001/texture/c0101h0001_hir_norm.tex";
    public const string VanillaHairMask = "chara/human/c0101/obj/hair/h0001/texture/c0101h0001_hir_mask.tex";

    public static (FakeGame, ModpackData) UnclaimedHair()
    {
        var game = new FakeGame();
        var vanilla = Material(VanillaHairMtrl, "hair.shpk", [], [],
            Texture(VanillaHairNorm, ESamplerId.g_SamplerNormal), Texture(VanillaHairMask, ESamplerId.g_SamplerMask));
        vanilla.ColorsetStrings = [];
        game.Files[VanillaHairMtrl] = Bytes(vanilla);
        return (game, Pack(Option("", "Default",
            (OldHairN, SolidTex(4, 4, 100, 110, 120, 130)), (OldHairS, SolidTex(4, 4, 10, 20, 30, 40)))));
    }

    public const string EyeMaskPath = "chara/human/c0101/obj/face/f0001/texture/--c0101f0001_iri_s.tex";
    public const string IrisMtrl = "chara/human/c0101/obj/face/f0001/material/mt_c0101f0001_iri_a.mtrl";
    public const string IrisDiffuse = "chara/human/c0101/obj/face/f0001/texture/c0101f0001_iri_base.tex";

    public static (FakeGame, ModpackData) EyeMask()
    {
        var game = new FakeGame();
        var iris = Material(IrisMtrl, "iris.shpk", [], [], Texture(IrisDiffuse, ESamplerId.g_SamplerDiffuse));
        iris.ColorsetStrings = [];
        game.Files[IrisMtrl] = Bytes(iris);
        game.Files["chara/common/texture/eye/eye01_base.tex"] = SolidTex(64, 64, 200, 100, 50, 255);
        game.Files["chara/common/texture/eye/eye01_mask.tex"] = SolidTex(64, 64, 0, 0, 255, 255);
        return (game, Pack(Option("", "Default", (EyeMaskPath, SolidTex(32, 32, 180, 0, 0, 255)))));
    }

    public static (FakeGame, ModpackData) DawntrailOnly()
    {
        var m = Material(GearMtrl, "characterlegacy.shpk", DawntrailColorset(), new byte[128],
            Texture(GearNormal, ESamplerId.g_SamplerNormal), Texture(GearIndex, ESamplerId.g_SamplerIndex));
        m.AdditionalData = [0x3C, 0x05, 0, 0];
        return (new FakeGame(), Pack(Option("", "Default",
            (GearMtrl, Bytes(m)),
            (GearNormal, SolidTex(4, 4, 128, 128, 255, 255)),
            (GearIndex, SolidTex(4, 4, 4, 255, 0, 255)),
            ("chara/equipment/e0001/model/c0101e0001_top.mdl", [6, 0, 0, 1]),
            ("chara/bibo_mid_base.tex", SolidTex(4, 4, 1, 2, 3, 4)))));
    }
}
