// Port of xivModdingFramework (TexTools, GPL-3.0): Materials/DataContainers/XivMtrl.cs
//   MtrlString, MtrlTexture, ShaderKey, ShaderConstant, TextureSampler.
// TexTools leaves several of these reference members null by default; they are declared non-nullable here
// (as the shared contract asks) but keep the same null defaults and null checks.

namespace DawntrailReady.Core.Materials;

public class MtrlString : ICloneable
{
    public string Value = null!;
    public ushort Flags;

    public object Clone()
    {
        return MemberwiseClone();
    }
}

/// <summary>
/// Class representing the sum of information used as a texture reference in a material.
/// </summary>
public class MtrlTexture : ICloneable
{
    public string TexturePath { get; set; }

    public ushort Flags { get; set; }

    public TextureSampler Sampler { get; set; }

    // NOT PORTED: GetTexData(forceOriginal, tx) - reads through TexTools' transaction/texture loader.

    public MtrlTexture()
    {
        TexturePath = "";
        Sampler = new TextureSampler();
    }

    public string Dx11Path
    {
        get
        {
            var texpath = TexturePath;
            if ((Flags & 0x8000) != 0)
            {
                // .NET Framework's Path.GetDirectoryName throws on "" (ArgumentException); .NET returns null and the
                // Replace below throws NullReferenceException instead. Either way the call fails, as in TexTools.
                var path = Path.GetDirectoryName(TexturePath)!.Replace("\\", "/");
                var file = Path.GetFileName(TexturePath);
                texpath = path + "/--" + file;
            }
            return texpath;
        }
    }

    public string? Dx9Path
    {
        get
        {
            if ((Flags & 0x8000) != 0)
            {
                return TexturePath;
            }
            return null;
        }
    }

    /// <summary>
    /// Retrieves this texture's usage type based on the sampler id.
    /// </summary>
    public XivTexType Usage
    {
        get
        {
            if (Sampler == null)
            {
                return XivTexType.Other;
            }
            return Sampler.TexType;
        }
    }

    public object Clone()
    {
        var clone = (MtrlTexture)MemberwiseClone();
        clone.Sampler = (TextureSampler)Sampler.Clone();
        return clone;
    }
}

/// <summary>
/// This class contains the information for the shader keys.
/// Primarily these enable/disable shader functionality at large.
/// </summary>
public class ShaderKey : ICloneable
{
    public uint KeyId;

    public uint Value;

    // NOT PORTED: GetKeyInfo(EShaderPack) - lookup into the shader_info.db dictionaries (see ShaderHelpers).

    public object Clone()
    {
        return MemberwiseClone();
    }
}

/// <summary>
/// This class contains the information for the shader parameters at the end the MTRL File
/// </summary>
public class ShaderConstant : ICloneable
{
    public uint ConstantId;

    // The actual data (after extraction).
    public List<float> Values = null!;

    // NOT PORTED: GetConstantInfo(EShaderPack) - lookup into the shader_info.db dictionaries (see ShaderHelpers).

    public object Clone()
    {
        var clone = (ShaderConstant)MemberwiseClone();
        clone.Values = Values.ToList();
        return clone;
    }
}

/// <summary>
/// This class contains the information for MTRL texture samplers.
/// These determine how each texture is used.
/// </summary>
public class TextureSampler : ICloneable
{
    /// <summary>
    /// 2-Bit UV Tiling Mode Identifer.
    /// </summary>
    public enum ETilingMode
    {
        Wrap,
        Mirror,
        Clamp,
        Border
    };

    /// <summary>
    /// The core Texture Sampler this object refers to.
    /// </summary>
    public ESamplerId SamplerId
    {
        get
        {
            if (Enum.IsDefined(typeof(ESamplerId), SamplerIdRaw))
            {
                return (ESamplerId)SamplerIdRaw;
            }
            return ESamplerId.Unknown;
        }
        set
        {
            SamplerIdRaw = (uint)value;
        }
    }

    /// <summary>
    /// The rough 'TexTools' style human-readable approximate texture usage of this texture.
    /// Not 1:1 with SamplerID, since multiple sampler IDs can feed into single TexTools categories.
    /// </summary>
    public XivTexType TexType
    {
        get
        {
            return ShaderHelpers.SamplerIdToTexUsage(SamplerId);
        }
    }

    /// <summary>
    /// U Direction UV Tiling Mode.
    /// </summary>
    public ETilingMode UTilingMode
    {
        get
        {
            uint shifted = (SamplerSettingsRaw >> 2);
            uint mode = (shifted & ((uint)0x3));
            return (ETilingMode)mode;
        }
        set
        {
            unchecked
            {
                SamplerSettingsRaw &= ~((uint)3 << 2);
                SamplerSettingsRaw |= ((uint)value << 2);
            }
        }
    }

    /// <summary>
    /// V Direction UV Tiling Mode.
    /// </summary>
    public ETilingMode VTilingMode
    {
        get
        {
            var mode = (((byte)SamplerSettingsRaw)) & 0x3;
            return (ETilingMode)mode;
        }
        set
        {
            unchecked
            {
                SamplerSettingsRaw &= ~((uint)3);
                SamplerSettingsRaw |= ((uint)value);
            }
        }
    }

    /// <summary>
    /// Minimum MipMap to use.
    /// </summary>
    public byte MinimumLoDLevel
    {
        get
        {
            var val = SamplerSettingsRaw >> 20;
            val &= 0xF;
            return (byte)val;
        }
        set
        {
            if (value > 15)
            {
                value = 15;
            }

            unchecked
            {
                SamplerSettingsRaw &= ~((uint)0xF << 20);
                SamplerSettingsRaw |= (((uint)value) << 20);
            }
        }
    }

    /// <summary>
    /// Bits 4-10 of the Sampler Settings
    /// Unknown Usage, but sometimes have actual values.
    /// </summary>
    public byte SamplerSettingsLowUnknown
    {
        get
        {
            var val = ((SamplerSettingsRaw >> 4) & 0x3f);
            return (byte)val;
        }
        set
        {
            unchecked
            {
                // No more than 6 bits.
                value &= 0x3F;
                SamplerSettingsRaw &= ~((uint)0x3f << 4);
                SamplerSettingsRaw |= ((uint)value << 4);
            }
        }
    }

    /// <summary>
    /// Bytes 24-31 of the Sampler Settings
    /// </summary>
    public byte SamplerSettingsHighByte
    {
        get
        {
            var val = SamplerSettingsRaw >> 24;
            return (byte)val;
        }
        set
        {
            unchecked
            {
                SamplerSettingsRaw &= ~((uint)0xFF << 24);
                SamplerSettingsRaw |= ((uint)value << 24);
            }
        }
    }

    /// <summary>
    /// LoD Bias. 10 bit signed value / 64, range -8 to ~+8.
    /// </summary>
    public float LoDBias
    {
        get
        {
            // 10 bit signed int.  We need to bit shift off the sign first.
            uint unsigned = (SamplerSettingsRaw & (0x1ff << 10)) >> 10;

            // Then get the sign
            uint signVal = (SamplerSettingsRaw >> 19) & 1;

            int res = 0;
            if (signVal > 0)
            {
                // If it's negative, we need to interpret it as two's complement.
                res = unchecked((int)(0xFFFFFe00 | unsigned));
            }
            else
            {
                // Positive is fine as is.
                res = (int)unsigned;
            }

            // Then 64 divisor and apply sign.
            var val = res / 64.0f;
            return val;
        }
        set
        {
            // Clamp
            var val = Math.Min(Math.Max(value, -8.0f), 7.984375f);

            // Split into sign and integer body.
            // TexTools writes (uint)(val * 64.0f). On .NET Framework x64 a negative float -> uint cast wraps
            // (e.g. -64f -> 0xFFFFFFC0); .NET 9+ saturates to 0 instead. Going through long reproduces TexTools.
            uint x64 = unchecked((uint)(long)(val * 64.0f));

            // Limit to 9 bits and sign.
            var limited = ((uint)x64 & 0x1FF);
            var shiftedSign = (((uint)x64) >> 31) << 9;
            var combined = limited | shiftedSign;
            unchecked
            {
                SamplerSettingsRaw &= ~((uint)0x3FF << 10);
                SamplerSettingsRaw |= ((uint)combined << 10);
            }
        }
    }

    /// <summary>
    /// CRC Identifier for a Texture Sampler.
    /// </summary>
    public uint SamplerIdRaw;

    // Bitwise field of sampler settings
    // Low 4 bits are V and U tiling mode, 2 each
    //      wrap, mirror, clamp, border in order.
    // 6 Bytes unknown(?)
    // 10 bits LoD Bias
    // 4 Bits for Minimum LoD level
    // 8 Bits Unknown
    public uint SamplerSettingsRaw;

    public object Clone()
    {
        return MemberwiseClone();
    }
}
