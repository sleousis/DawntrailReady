using System.Text;
using DawntrailReady.Core.Materials;

namespace DawntrailReady.Core.Tests.Materials;

/// <summary>
/// Synthetic materials only (built in code). Checks the TexTools-ported reader/writer byte layout.
/// </summary>
public class MtrlTests
{
    private static MtrlTexture Tex(string path, ESamplerId sampler, uint settings = 0x000F8340, ushort flags = 0)
        => new() { TexturePath = path, Flags = flags, Sampler = new TextureSampler { SamplerId = sampler, SamplerSettingsRaw = settings } };

    private static int SamplerCountInHeader(byte[] b)
    {
        var shaderHeader = 16 + (b[12] + b[13] + b[14]) * 4 + BitConverter.ToUInt16(b, 8) + b[15] + BitConverter.ToUInt16(b, 6);
        return BitConverter.ToUInt16(b, shaderHeader + 6);
    }

    private static XivMtrl LegacyMaterial()
    {
        var colorset = new List<Half>();
        for (var i = 0; i < 256; i++) colorset.Add((Half)(i / 16f));
        var dye = new byte[32];
        for (var i = 0; i < dye.Length; i++) dye[i] = (byte)(i * 7);

        return new XivMtrl
        {
            MTRLPath = "chara/equipment/e0001/material/v0001/mt_c0101e0001_top_a.mtrl",
            ShaderPackRaw = "characterlegacy.shpk",
            Textures =
            [
                Tex("chara/equipment/e0001/texture/v01_c0101e0001_top_n.tex", ESamplerId.g_SamplerNormal, flags: 0x8000),
                Tex("chara/equipment/e0001/texture/v01_c0101e0001_top_d.tex", ESamplerId.g_SamplerDiffuse),
                Tex("chara/equipment/e0001/texture/v01_c0101e0001_top_s.tex", ESamplerId.g_SamplerSpecular),
            ],
            UvMapStrings = [new MtrlString { Value = "uv0", Flags = 0 }],
            ColorsetStrings = [new MtrlString { Value = "colorSetMap0", Flags = 0 }],
            AdditionalData = [0x04, 0, 0, 0],
            ColorSetData = colorset,
            ColorSetDyeData = dye,
            MaterialFlags = EMaterialFlags1.HideBackfaces | EMaterialFlags1.EnableTranslucency,
            MaterialFlags2 = EMaterialFlags2.Unknown0004,
            ShaderKeys = [new ShaderKey { KeyId = 0xB616DC5A, Value = 0x5CC605B5 }],
            ShaderConstants =
            [
                new ShaderConstant { ConstantId = 0x29AC0223, Values = [0.5f] },
                new ShaderConstant { ConstantId = 0x2C2A34DD, Values = [1f, 0.25f, -2f] },
            ],
        };
    }

    private static XivMtrl DawntrailMaterial()
    {
        var colorset = new List<Half>();
        for (var i = 0; i < 1024; i++) colorset.Add(BitConverter.UInt16BitsToHalf((ushort)(i * 61)));
        var dye = new byte[128];
        for (var i = 0; i < dye.Length; i++) dye[i] = (byte)(255 - i);

        return new XivMtrl
        {
            MTRLPath = "chara/equipment/e0001/material/v0001/mt_c0101e0001_top_a.mtrl",
            ShaderPackRaw = "character.shpk",
            Textures =
            [
                Tex("chara/equipment/e0001/texture/c0101e0001_top_norm.tex", ESamplerId.g_SamplerNormal),
                Tex("chara/equipment/e0001/texture/c0101e0001_top_mask.tex", ESamplerId.g_SamplerMask),
                Tex("chara/equipment/e0001/texture/c0101e0001_top_id.tex", ESamplerId.g_SamplerIndex),
            ],
            UvMapStrings = [new MtrlString { Value = "uv0" }],
            ColorsetStrings = [new MtrlString { Value = "colorSetMap0" }],
            AdditionalData = [0x34, 0x05, 0, 0],
            ColorSetData = colorset,
            ColorSetDyeData = dye,
            ShaderKeys =
            [
                new ShaderKey { KeyId = 0xB616DC5A, Value = 0x5CC605B5 },
                new ShaderKey { KeyId = 0xF52CCF05, Value = 0xDFE74BAC },
            ],
            ShaderConstants = [new ShaderConstant { ConstantId = 0x38A64362, Values = [0f, 0f, 0f] }],
        };
    }

    [Fact]
    public void LegacyMaterial_RoundTripsByteIdentical()
    {
        var bytes = Mtrl.XivMtrlToUncompressedMtrl(LegacyMaterial());

        var parsed = Mtrl.GetXivMtrl(bytes, "x.mtrl");
        Assert.Equal(544, BitConverter.ToUInt16(bytes, 6));
        Assert.Equal(256, parsed.ColorSetData.Count);
        Assert.Equal(32, parsed.ColorSetDyeData.Length);
        Assert.Equal(544, parsed.ColorSetDataSize);
        Assert.Equal(EShaderPack.CharacterLegacy, parsed.ShaderPack);
        Assert.Equal(3, parsed.Textures.Count);
        Assert.Equal(0x8000, parsed.Textures[0].Flags);
        Assert.Equal(new float[] { 1f, 0.25f, -2f }, parsed.ShaderConstants[1].Values);
        Assert.Equal(EMaterialFlags1.HideBackfaces | EMaterialFlags1.EnableTranslucency, parsed.MaterialFlags);
        Assert.Equal(EMaterialFlags2.Unknown0004, parsed.MaterialFlags2);
        // Dye data present => writer sets bit 0x08 of AdditionalData[0].
        Assert.Equal(0x0C, parsed.AdditionalData[0]);

        Assert.Equal(bytes, Mtrl.XivMtrlToUncompressedMtrl(parsed));
    }

    [Fact]
    public void DawntrailMaterial_RoundTripsByteIdentical()
    {
        var original = DawntrailMaterial();
        var bytes = Mtrl.XivMtrlToUncompressedMtrl(original);

        var parsed = Mtrl.GetXivMtrl(bytes);
        Assert.Equal(2048 + 128, BitConverter.ToUInt16(bytes, 6));
        Assert.Equal(1024, parsed.ColorSetData.Count);
        Assert.Equal(128, parsed.ColorSetDyeData.Length);
        Assert.Equal(original.ColorSetData.Select(BitConverter.HalfToUInt16Bits), parsed.ColorSetData.Select(BitConverter.HalfToUInt16Bits));
        Assert.Equal(original.ColorSetDyeData, parsed.ColorSetDyeData);
        Assert.Equal(EShaderPack.Character, parsed.ShaderPack);

        Assert.Equal(bytes, Mtrl.XivMtrlToUncompressedMtrl(parsed));
    }

    [Fact]
    public void Writer_ProducesTexToolsLayout()
    {
        var mtrl = new XivMtrl
        {
            ShaderPackRaw = "skin.shpk",
            Textures = [Tex("A/B.tex", ESamplerId.g_SamplerNormal, 0x000F8340)],
            UvMapStrings = [new MtrlString { Value = "uv", Flags = 0 }],
            ColorsetStrings = [],
            AdditionalData = [0x08, 1, 2, 3],
            ColorSetData = [],
            ColorSetDyeData = [],
            MaterialFlags = EMaterialFlags1.HideBackfaces,
            ShaderKeys = [new ShaderKey { KeyId = 0x380CAED0, Value = 0x2BDB45F1 }],
            ShaderConstants = [new ShaderConstant { ConstantId = 0x29AC0223, Values = [0.5f] }],
        };

        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(0x301);
        bw.Write((ushort)96);        // file size
        bw.Write((ushort)0);         // colorset size
        bw.Write((ushort)24);        // string block size (21 padded to 4)
        bw.Write((ushort)11);        // shader name offset
        bw.Write((byte)1); bw.Write((byte)1); bw.Write((byte)0); bw.Write((byte)4);
        bw.Write((short)0); bw.Write((short)0);   // texture offset/flags
        bw.Write((short)8); bw.Write((short)0);   // uv map offset/flags
        bw.Write(Encoding.UTF8.GetBytes("a/b.tex\0uv\0skin.shpk\0\0\0\0"));
        bw.Write(new byte[] { 0x00, 1, 2, 3 });   // no dye data => 0x08 cleared
        bw.Write((ushort)4); bw.Write((ushort)1); bw.Write((ushort)1); bw.Write((ushort)1);
        bw.Write((ushort)1); bw.Write((ushort)0); // flags
        bw.Write(0x380CAED0u); bw.Write(0x2BDB45F1u);
        bw.Write(0x29AC0223u); bw.Write((ushort)0); bw.Write((ushort)4);
        bw.Write((uint)ESamplerId.g_SamplerNormal); bw.Write(0x000F8340u); bw.Write((byte)0); bw.Write(new byte[3]);
        bw.Write(0.5f);
        bw.Flush();

        Assert.Equal(ms.ToArray(), Mtrl.XivMtrlToUncompressedMtrl(mtrl));
        Assert.Equal("a/b.tex", mtrl.Textures[0].TexturePath); // writer lower-cases in place, as TexTools
    }

    [Fact]
    public void TwoUvSets_DoubleWritePrimarySamplers()
    {
        var mtrl = DawntrailMaterial();
        mtrl.UvMapStrings = [new MtrlString { Value = "uv0" }, new MtrlString { Value = "uv1" }];
        mtrl.Textures =
        [
            Tex("a_norm.tex", ESamplerId.g_SamplerNormalMap0),
            Tex("a_base.tex", ESamplerId.g_SamplerColorMap0),
            Tex("a_base2.tex", ESamplerId.g_SamplerColorMap1), // secondary already present => ColorMap0 not doubled
        ];

        var bytes = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
        Assert.Equal(4, SamplerCountInHeader(bytes)); // NormalMap0 written twice, ColorMap0 once, ColorMap1 once
        var parsed = Mtrl.GetXivMtrl(bytes);

        Assert.Equal(3, parsed.Textures.Count);
        Assert.Equal(ESamplerId.g_SamplerNormalMap0, parsed.Textures[0].Sampler.SamplerId);
        Assert.Equal(bytes, Mtrl.XivMtrlToUncompressedMtrl(parsed));
    }

    [Fact]
    public void EmptySampler_ReaderMakesPlaceholder_WriterLowercasesItIntoARealTexture()
    {
        // A sampler whose texture index is 255 (no texture) in the file.
        var bytes = Mtrl.XivMtrlToUncompressedMtrl(DawntrailMaterial());
        var shaderHeader = 16 + (bytes[12] + bytes[13] + bytes[14]) * 4 + BitConverter.ToUInt16(bytes, 8) + bytes[15] + BitConverter.ToUInt16(bytes, 6);
        var lastSampler = shaderHeader + 12 + 2 * 8 + 1 * 8 + 2 * 12; // header, 2 keys, 1 constant, 3rd sampler
        bytes[lastSampler + 8] = 255;

        var parsed = Mtrl.GetXivMtrl(bytes);
        Assert.Equal(4, parsed.Textures.Count); // placeholder appended after the 3 real textures
        Assert.Equal(ESamplerId.Unknown, parsed.Textures[2].Sampler.SamplerId); // id.tex lost its sampler, keeps the blank default
        Assert.Equal("_EMPTY_SAMPLER_g_SamplerIndex", parsed.Textures[3].TexturePath);

        // TexTools quirks: the writer lower-cases every TexturePath first, so the case-sensitive
        // StartsWith(EmptySamplerPrefix) checks no longer match and the placeholder is written as a 4th texture;
        // and a texture with the blank default sampler still gets that (0, 0) sampler written.
        var written = Mtrl.XivMtrlToUncompressedMtrl(parsed);
        Assert.Equal("_empty_sampler_g_samplerindex", parsed.Textures[3].TexturePath);
        Assert.Equal(4, written[12]);
        Assert.Equal(4, SamplerCountInHeader(written));
        Assert.Equal(written, Mtrl.XivMtrlToUncompressedMtrl(Mtrl.GetXivMtrl(written)));
    }

    [Fact]
    public void Dx11Path_Uses0x8000Flag()
    {
        var tex = new MtrlTexture { TexturePath = "chara/equipment/e0001/texture/v01_c0101e0001_top_n.tex", Flags = 0x8000 };
        Assert.Equal("chara/equipment/e0001/texture/--v01_c0101e0001_top_n.tex", tex.Dx11Path);
        Assert.Equal(tex.TexturePath, tex.Dx9Path);

        tex.Flags = 0;
        Assert.Equal(tex.TexturePath, tex.Dx11Path);
        Assert.Null(tex.Dx9Path);
    }

    [Theory]
    [InlineData("character.shpk", ESamplerId.g_SamplerNormal, XivTexType.Normal)]
    [InlineData("character.shpk", ESamplerId.g_SamplerMask, XivTexType.Mask)]
    [InlineData("character.shpk", ESamplerId.g_SamplerIndex, XivTexType.Index)]
    [InlineData("characterlegacy.shpk", ESamplerId.g_SamplerSpecular, XivTexType.Specular)]
    [InlineData("characterlegacy.shpk", ESamplerId.g_SamplerDiffuse, XivTexType.Diffuse)]
    [InlineData("bg.shpk", ESamplerId.g_SamplerSpecularMap0, XivTexType.Mask)]
    [InlineData("character.shpk", ESamplerId.g_SamplerSpecularMap0, XivTexType.Specular)]
    [InlineData("character.shpk", ESamplerId.g_SamplerDither, XivTexType.Other)]
    public void ResolveFullUsage_BySampler(string shpk, ESamplerId sampler, XivTexType expected)
    {
        var mtrl = new XivMtrl { ShaderPackRaw = shpk };
        Assert.Equal(expected, mtrl.ResolveFullUsage(Tex("x.tex", sampler)));
    }

    [Fact]
    public void ResolveFullUsage_LegacyCompatibilityMaskIsSpecular()
    {
        var mtrl = new XivMtrl { ShaderPackRaw = "characterlegacy.shpk" };
        var mask = Tex("x_m.tex", ESamplerId.g_SamplerMask);
        mtrl.ShaderKeys.Add(new ShaderKey { KeyId = 0xB616DC5A, Value = 0x600EF9DF });
        Assert.Equal(XivTexType.Specular, mtrl.ResolveFullUsage(mask));

        mtrl.ShaderKeys.Add(new ShaderKey { KeyId = 0xC8BD1DEF, Value = 0xA02F4828 });
        Assert.Equal(XivTexType.Mask, mtrl.ResolveFullUsage(mask));

        Assert.Equal(XivTexType.Other, mtrl.ResolveFullUsage(new MtrlTexture { Sampler = null! }));
    }

    [Fact]
    public void ShaderPackStrings_MapBothWays()
    {
        Assert.Equal(EShaderPack.CharacterLegacy, ShaderHelpers.GetShpkFromString("characterlegacy.shpk"));
        Assert.Equal(EShaderPack.Unknown, ShaderHelpers.GetShpkFromString("nope.shpk"));
        Assert.Equal(EShaderPack.Unknown, ShaderHelpers.GetShpkFromString(null));
        Assert.Equal("hair.shpk", ShaderHelpers.GetEnumDescription(EShaderPack.Hair));

        var mtrl = new XivMtrl { ShaderPack = EShaderPack.Skin };
        Assert.Equal("skin.shpk", mtrl.ShaderPackRaw);
    }

    [Fact]
    public void Sampler_UnknownRawIdAndSettingsBits()
    {
        var s = new TextureSampler { SamplerIdRaw = 0xDEADBEEF, SamplerSettingsRaw = 0 };
        Assert.Equal(ESamplerId.Unknown, s.SamplerId);

        s.UTilingMode = TextureSampler.ETilingMode.Clamp;
        s.VTilingMode = TextureSampler.ETilingMode.Mirror;
        Assert.Equal(0b1001u, s.SamplerSettingsRaw);

        s.LoDBias = -1.0f; // TexTools (.NET Framework x64) wraps the negative float->uint cast
        Assert.Equal(-1.0f, s.LoDBias);
        s.LoDBias = 2.5f;
        Assert.Equal(2.5f, s.LoDBias);
    }

    [Fact]
    public void Reader_TruncatedConstantDataGivesBlankValues()
    {
        var bytes = Mtrl.XivMtrlToUncompressedMtrl(new XivMtrl
        {
            Textures = [],
            AdditionalData = [0, 0, 0, 0],
            ColorSetData = [],
            ColorSetDyeData = [],
            ShaderConstants = [new ShaderConstant { ConstantId = 1, Values = [3f, 4f] }],
        });
        // Declare the constant larger than the data block, as some broken files do.
        var sizeOffset = bytes.Length - 8 - 2;
        BitConverter.GetBytes((short)12).CopyTo(bytes, sizeOffset);

        var parsed = Mtrl.GetXivMtrl(bytes);
        Assert.Equal(new float[] { 0f, 0f, 0f }, parsed.ShaderConstants[0].Values);
    }
}
