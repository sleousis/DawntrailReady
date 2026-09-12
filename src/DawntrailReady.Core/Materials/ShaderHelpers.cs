// Port of xivModdingFramework (TexTools, GPL-3.0): Materials/DataContainers/ShaderHelpers.cs
// The enums (ESamplerId, EShaderPack, ESamplerFormats) live in MaterialEnums.cs at namespace level.
using System.ComponentModel;
using System.Reflection;

namespace DawntrailReady.Core.Materials;

public static class ShaderHelpers
{
    // NOT PORTED: ShaderConstantInfo / ShaderKeyInfo, the ShaderConstants / ShaderKeys dictionaries,
    // LoadShaderInfo() and AddCustomNamesAndValues(). They are UI name/default lookups loaded from TexTools'
    // SQLite shader_info.db and are not used when reading, writing or upgrading materials.

    static ShaderHelpers()
    {
        foreach (EShaderPack shpk in Enum.GetValues(typeof(EShaderPack)))
        {
            var fieldInfo = typeof(EShaderPack).GetField(shpk.ToString())!;
            var descriptionAttributes = (DescriptionAttribute[])fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);

            StringToShpk.Add(descriptionAttributes[0].Description, shpk);
        }
    }

    public static bool UsesColorset(this EShaderPack shpk)
    {
        switch (shpk)
        {
            case EShaderPack.Character:
            case EShaderPack.CharacterLegacy:
            case EShaderPack.CharacterGlass:
            case EShaderPack.CharacterInc:
            case EShaderPack.CharacterStockings:
            case EShaderPack.CharacterScroll:
            case EShaderPack.CharacterReflection:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Converts Sampler usage to XixTexType
    /// - Note that this is not 1:1 reversable.
    /// XiVTexType should primarily only be used for naive user display purposes.
    /// </summary>
    /// <param name="samplerId"></param>
    /// <param name="mtrl">Option - Material that is using the texture.  Allows proper resolution for specular maps using mask sampler.</param>
    public static XivTexType SamplerIdToTexUsage(ESamplerId samplerId, XivMtrl? mtrl = null)
    {
        // Compatibility mode shader keys...
        if (mtrl != null && mtrl.ShaderPack == EShaderPack.CharacterLegacy && mtrl.ShaderKeys.Any(x => x.KeyId == 0xB616DC5A && x.Value == 0x600EF9DF))
        {
            if (!mtrl.ShaderKeys.Any(x => x.KeyId == 0xC8BD1DEF && x.Value == 0xA02F4828))
            {
                if (samplerId == ESamplerId.g_SamplerMask)
                {
                    return XivTexType.Specular;
                }
            }
        }
        // At least for furniture, these are unconditionally masks and not specular maps
        if (mtrl != null && (mtrl.ShaderPack == EShaderPack.Bg || mtrl.ShaderPack == EShaderPack.BgProp || mtrl.ShaderPack == EShaderPack.BgColorChange))
        {
            if (samplerId == ESamplerId.g_SamplerSpecularMap0 || samplerId == ESamplerId.g_SamplerSpecularMap1)
            {
                return XivTexType.Mask;
            }
        }

        if (samplerId == ESamplerId.g_SamplerNormal || samplerId == ESamplerId.g_SamplerNormal2 || samplerId == ESamplerId.g_SamplerNormalMap0 || samplerId == ESamplerId.g_SamplerNormalMap1 || samplerId == ESamplerId.g_SamplerTileNormal)
        {
            return XivTexType.Normal;
        }
        else if (samplerId == ESamplerId.g_SamplerMask || samplerId == ESamplerId.g_SamplerWrinklesMask || samplerId == ESamplerId.g_SamplerTileOrb)
        {
            return XivTexType.Mask;
        }
        else if (samplerId == ESamplerId.g_SamplerIndex)
        {
            return XivTexType.Index;
        }
        else if (samplerId == ESamplerId.g_SamplerDiffuse || samplerId == ESamplerId.g_SamplerColorMap0 || samplerId == ESamplerId.g_SamplerColorMap1)
        {
            return XivTexType.Diffuse;
        }
        else if (samplerId == ESamplerId.g_SamplerReflectionArray || samplerId == ESamplerId.g_SamplerSphereMap)
        {
            return XivTexType.Reflection;
        }
        else if (samplerId == ESamplerId.g_SamplerDecal)
        {
            return XivTexType.Decal;
        }
        else if (samplerId == ESamplerId.g_SamplerSpecularMap0 || samplerId == ESamplerId.g_SamplerSpecular || samplerId == ESamplerId.g_SamplerSpecularMap1)
        {
            return XivTexType.Specular;
        }
        return XivTexType.Other;
    }

    private static Dictionary<string, EShaderPack> StringToShpk = new Dictionary<string, EShaderPack>();

    public static EShaderPack GetShpkFromString(string? s)
    {
        if (s == null) { return EShaderPack.Unknown; }
        if (StringToShpk.ContainsKey(s))
        {
            return StringToShpk[s];
        }

        return EShaderPack.Unknown;
    }

    public static T GetValueFromDescription<T>(string description) where T : Enum
    {
        foreach (var field in typeof(T).GetFields())
        {
            if (Attribute.GetCustomAttribute(field,
            typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
            {
                if (attribute.Description == description)
                    return (T)field.GetValue(null)!;
            }
            else
            {
                if (field.Name == description)
                    return (T)field.GetValue(null)!;
            }
        }
        return default(T)!;
    }

    public static string GetEnumDescription(Enum value)
    {
        // As TexTools: an undefined enum value has no field, so this throws NullReferenceException.
        FieldInfo fi = value.GetType().GetField(value.ToString())!;

        DescriptionAttribute[]? attributes = fi.GetCustomAttributes(typeof(DescriptionAttribute), false) as DescriptionAttribute[];

        if (attributes != null && attributes.Any())
        {
            return attributes.First().Description;
        }

        return value.ToString();
    }
}
