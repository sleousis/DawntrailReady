// Port of xivModdingFramework (TexTools, GPL-3.0):
//   Materials/DataContainers/XivMtrl.cs          - EMaterialFlags1, EMaterialFlags2
//   Materials/DataContainers/ShaderHelpers.cs    - ESamplerId, ESamplerFormats, EShaderPack (nested in ShaderHelpers there)
//   Textures/Enums/XivTexType.cs                 - XivTexType
using System.ComponentModel;

namespace DawntrailReady.Core.Materials;

// Values copied verbatim, including TexTools' duplicated 0x1000 for the top four members.
[Flags]
public enum EMaterialFlags1 : ushort
{
    HideBackfaces = 0x01,
    Unknown0002 = 0x02,
    Unknown0004 = 0x04,
    Unknown0008 = 0x08,
    EnableTranslucency = 0x10,
    Unknown0020 = 0x020,
    Unknown0040 = 0x040,
    Unknown0080 = 0x080,
    Unknown0100 = 0x100,
    Unknown0200 = 0x200,
    Unknown0400 = 0x400,
    Unknown0800 = 0x800,
    Unknown1000 = 0x1000,
    Unknown2000 = 0x1000,
    Unknown4000 = 0x1000,
    Unknown8000 = 0x1000,
};

[Flags]
public enum EMaterialFlags2 : ushort
{
    Unknown0001 = 0x01,
    Unknown0002 = 0x02,
    Unknown0004 = 0x04,
    Unknown0008 = 0x08,
    Unknown0010 = 0x10,
    Unknown0020 = 0x20,
    Unknown0040 = 0x40,
    Unknown0080 = 0x80,
    Unknown0100 = 0x100,
    Unknown0200 = 0x200,
    Unknown0400 = 0x400,
    Unknown0800 = 0x800,
    Unknown1000 = 0x1000,
    Unknown2000 = 0x1000,
    Unknown4000 = 0x1000,
    Unknown8000 = 0x1000,
};

/// <summary>
/// Enum containing the Types of Textures
/// </summary>
public enum XivTexType
{
    Diffuse,
    Specular,
    Normal,
    Mask,
    Reflection,
    Skin,
    ColorSet,
    Index,
    Map,
    Icon,
    Vfx,
    UI,
    Decal,

    // Legacy Unused DX9 Textures
    DX9,

    Other
}

/// <summary>
/// Sampler IDs obtained via dumping RAM stuff.
/// These are the same across all shpks/etc. so we can just use a simple enum here.
/// </summary>
public enum ESamplerId : uint
{
    // This isn't ALL samplers in existence in FFXIV,
    // But it is all the samplers used with Material Textures,
    // So they're the only ones we care about.
    Unknown = 0,
    g_SamplerNormal = 0x0C5EC1F1,
    g_SamplerNormalMap0 = 0xAAB4D9E9,
    g_SamplerNormalMap1 = 0xDDB3E97F,
    g_SamplerNormal2 = 0x0261CDCB,
    g_SamplerSpecular = 0x2B99E025,
    g_SamplerSpecularMap0 = 0x1BBC2F12,
    g_SamplerSpecularMap1 = 0x6CBB1F84,
    g_SamplerDiffuse = 0x115306BE,
    g_SamplerMask = 0x8A4E82B6,
    g_SamplerIndex = 0x565F8FD8,
    g_SamplerOcclusion = 0x32667BD7,
    g_SamplerFlow = 0xA7E197F6,
    g_SamplerDecal = 0x0237CB94,
    g_SamplerDither = 0x9F467267,
    g_SamplerColorMap0 = 0x1E6FEF9C,
    g_SamplerColorMap1 = 0x6968DF0A,
    g_SamplerWrinklesMask = 0xB3F13975,
    g_SamplerReflection = 0x87F6474D,
    g_SamplerReflectionArray = 0xC5C4CB3C,
    g_SamplerTileOrb = 0x800BE99B,
    g_SamplerTileNormal = 0x92F03E53,
    g_SamplerWaveMap = 0xE6321AFC,
    g_SamplerWaveMap1 = 0xE5338C17,
    g_SamplerWaveletMap0 = 0x574E22D6,
    g_SamplerWaveletMap1 = 0x20491240,
    g_SamplerWhitecapMap = 0x95E1F64D,
    g_SamplerEnvMap = 0xF8D7957A,
    g_SamplerTable = 0x2005679F,
    g_SamplerGBuffer = 0xEBBB29BD,
    g_SamplerSphereMap = 0x3334D3CA,
    g_SamplerCatchlight = 0xFEA0F3D2,
    tPerlinNoise2D = 0xC06FEB5B,
    g_Sampler = 0x88408C04,
    g_Sampler0 = 0x213CB439,
    g_Sampler1 = 0x563B84AF,
    g_SkySampler = 0xB4C285EF,
    g_FogWeightLutSampler = 0x6E231669,

};

// Enum representation of the format map data is used as.
public enum ESamplerFormats : ushort
{
    UsesColorset,
    NoColorset,
    Other
};

// Enum representation of the shader names used in mtrl files.
public enum EShaderPack
{
    [Description("UNKNOWN")]
    Unknown,

    [Description("character.shpk")]
    Character,

    [Description("characterlegacy.shpk")]
    CharacterLegacy,

    [Description("characterglass.shpk")]
    CharacterGlass,

    [Description("characterstockings.shpk")]
    CharacterStockings,

    [Description("charactertattoo.shpk")]
    CharacterTattoo,

    [Description("skin.shpk")]
    Skin,

    [Description("hair.shpk")]
    Hair,

    [Description("iris.shpk")]
    Iris,

    [Description("bg.shpk")]
    Bg,

    [Description("bgprop.shpk")]
    BgProp,

    [Description("bgcolorchange.shpk")]
    BgColorChange,

    [Description("bguvscroll.shpk")]
    BgUvScroll,

    [Description("bgcrestchange.shpk")]
    BgCrestChange,

    [Description("characterscroll.shpk")]
    CharacterScroll,

    [Description("characterinc.shpk")]
    CharacterInc,

    [Description("characterocclusion.shpk")]
    CharacterOcclusion,

    [Description("characterreflection.shpk")]
    CharacterReflection,

    [Description("charactertransparency.shpk")]
    CharacterTransparency,

    [Description("water.shpk")]
    Water,

    [Description("river.shpk")]
    River,

    [Description("crystal.shpk")]
    Crystal,

    [Description("lightshaft.shpk")]
    LightShaft,

    [Description("verticalfog.shpk")]
    VerticalFog,

};
