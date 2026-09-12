// Port of xivModdingFramework (TexTools, GPL-3.0): Materials/DataContainers/XivMtrl.cs (class XivMtrl).
// Colorset halves use System.Half in place of SharpDX.Half; raw bits are kept unchanged both ways.

namespace DawntrailReady.Core.Materials;

/// <summary>
/// This class holds the information for an MTRL file
/// </summary>
public class XivMtrl : ICloneable
{
    // NOT PORTED: ItemPathToken/TextureNameToken/CommonPathToken, GetSuffix, GetDataFile, GetTextureRootDirectory,
    // GetVersion, GetVariantString, GetFakeSlot, GetDefaultTexureName, GetUniqueTextureName, GetMaterialIdentifier,
    // GetItemTypeIdentifier, GetRace, GetCommonTextureDirectory, TokenizePath, DetokenizePath and the private
    // Get/SetShaderConstant. They depend on TexTools' item/race/IOUtil/hash helpers and are not used by the
    // material reader, writer or by the Endwalker upgrade.

    /// <summary>
    /// The MTRL file signature
    /// </summary>
    public int Signature { get; set; } = 0x00000301;

    /// <summary>
    /// The size of the ColorSet Data section
    /// </summary>
    /// <remarks>
    /// Can be 0 if there is no ColorSet Data
    /// </remarks>
    public ushort ColorSetDataSize
    {
        get
        {
            var size = ColorSetData.Count * 2;
            size += ColorSetDyeData == null ? 0 : ColorSetDyeData.Length;
            return (ushort)size;
        }
    }

    public List<MtrlString> UvMapStrings { get; set; } = new List<MtrlString>();

    public List<MtrlString> ColorsetStrings { get; set; } = new List<MtrlString>();

    public EShaderPack ShaderPack
    {
        get
        {
            return ShaderHelpers.GetShpkFromString(ShaderPackRaw);
        }
        set
        {
            ShaderPackRaw = ShaderHelpers.GetEnumDescription(value);
        }
    }

    /// <summary>
    /// The name of the shader used by the item
    /// </summary>
    public string ShaderPackRaw { get; set; } = "character.shpk";

    /// <summary>
    /// Unknown value
    /// </summary>
    public byte[] AdditionalData { get; set; } = new byte[4];

    /// <summary>
    /// The list of half floats containing the ColorSet data
    /// </summary>
    public List<Half> ColorSetData { get; set; } = new List<Half>(new Half[1024]);

    /// <summary>
    /// The byte array containing the extra ColorSet data
    /// </summary>
    public byte[] ColorSetDyeData { get; set; } = new byte[128];

    /// <summary>
    /// The size of the additional MTRL Data
    /// </summary>
    public ushort ShaderConstantsDataSize
    {
        get
        {
            var size = 0;
            ShaderConstants.ForEach(x =>
            {
                size += x.Values.Count * 4;
            }
            );

            return (ushort)size;
        }
        set
        {
            //No-Op
            throw new Exception("Attempted to directly set AdditionalDataSize");
        }
    }

    /// <summary>
    /// The number of type 1 data sturctures
    /// </summary>
    public ushort ShaderKeyCount
    {
        get { return (ushort)ShaderKeys.Count; }
        set
        {
            //No-Op
            throw new Exception("Attempted to directly set TextureUsageCount");
        }
    }

    /// <summary>
    /// The number of type 2 data structures
    /// </summary>
    public ushort ShaderConstantsCount
    {
        get { return (ushort)ShaderConstants.Count; }
        set
        {
            //No-Op
            throw new Exception("Attempted to directly set ShaderParameterCount");
        }
    }

    public List<MtrlTexture> Textures = new List<MtrlTexture>();

    public EMaterialFlags1 MaterialFlags { get; set; }

    /// <summary>
    /// Unknown Value
    /// </summary>
    public EMaterialFlags2 MaterialFlags2 { get; set; }

    /// <summary>
    /// The list of Type 1 data structures
    /// </summary>
    public List<ShaderKey> ShaderKeys { get; set; } = new List<ShaderKey>();

    /// <summary>
    /// The list of Type 2 data structures
    /// </summary>
    public List<ShaderConstant> ShaderConstants { get; set; } = new List<ShaderConstant>();

    /// <summary>
    /// The internal MTRL path
    /// </summary>
    public string MTRLPath { get; set; } = null!;

    internal ushort GetRealSamplerCount()
    {
        var total = Textures.Count(x => x.Sampler != null);

        if (UvMapStrings.Count <= 1)
        {
            return (ushort)total;
        }

        foreach (var tex in Textures)
        {
            if (tex.Sampler == null) continue;
            if (tex.Sampler.SamplerId == ESamplerId.g_SamplerColorMap0
                || tex.Sampler.SamplerId == ESamplerId.g_SamplerSpecularMap0
                || tex.Sampler.SamplerId == ESamplerId.g_SamplerNormalMap0)
            {
                ESamplerId secondarySampler;
                switch (tex.Sampler.SamplerId)
                {
                    case ESamplerId.g_SamplerColorMap0:
                        secondarySampler = ESamplerId.g_SamplerColorMap1;
                        break;
                    case ESamplerId.g_SamplerSpecularMap0:
                        secondarySampler = ESamplerId.g_SamplerSpecularMap1;
                        break;
                    case ESamplerId.g_SamplerNormalMap0:
                    default:
                        secondarySampler = ESamplerId.g_SamplerNormalMap1;
                        break;
                }

                if (Textures.Any(x => x.Sampler != null && x.Sampler.SamplerId == secondarySampler))
                {
                    // This already has another copy of this sampler manually added on a different tex.
                    continue;
                }

                // These samplers get double-written when available.
                total++;
            }
        }

        return (ushort)total;
    }

    /// <summary>
    /// Simplified one-line accessor for ease of use.
    /// </summary>
    public MtrlTexture? GetTexture(XivTexType usage, bool clone = false)
    {
        var val = Textures.FirstOrDefault(x => ResolveFullUsage(x) == usage);
        if (val != null && clone)
        {
            val = (MtrlTexture)val.Clone();
        }
        return val;
    }

    /// <summary>
    /// Performs a better usage resolve than just MtrlTexture.Usage, properly accounting for shader keys.
    /// </summary>
    public XivTexType ResolveFullUsage(MtrlTexture tex)
    {
        if (tex.Sampler == null)
        {
            return XivTexType.Other;
        }
        return ShaderHelpers.SamplerIdToTexUsage(tex.Sampler.SamplerId, this);
    }

    public object Clone()
    {
        var clone = (XivMtrl)MemberwiseClone();

        clone.Textures = new List<MtrlTexture>();
        for (int i = 0; i < Textures.Count; i++)
        {
            clone.Textures.Add((MtrlTexture)Textures[i].Clone());
        }

        clone.ShaderConstants = new List<ShaderConstant>();
        for (int i = 0; i < ShaderConstants.Count; i++)
        {
            clone.ShaderConstants.Add((ShaderConstant)ShaderConstants[i].Clone());
        }

        clone.ShaderKeys = new List<ShaderKey>();
        for (int i = 0; i < ShaderKeys.Count; i++)
        {
            clone.ShaderKeys.Add((ShaderKey)ShaderKeys[i].Clone());
        }

        if (AdditionalData != null)
        {
            clone.AdditionalData = (byte[])AdditionalData.Clone();
        }

        if (ColorSetData != null)
        {
            clone.ColorSetData = ColorSetData.ToList();
        }
        if (ColorSetDyeData != null)
        {
            clone.ColorSetDyeData = (byte[])ColorSetDyeData.Clone();
        }

        // As TexTools: UvMapStrings / ColorsetStrings are shared with the original (shallow copy).
        return clone;
    }
}
