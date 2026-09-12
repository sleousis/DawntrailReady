// Port of TexTools xivModdingFramework/Mods/EndwalkerUpgrade.cs (commit 25b3ae72): UpdateEndwalkerColorset,
// UpdateEndwalkerHairMaterial, GetDefaultColorsetRow.
using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using static DawntrailReady.Core.Materials.ShaderHelpers;

namespace DawntrailReady.Core.Upgrade;

public sealed partial class EndwalkerUpgrade
{
    /// <summary>
    /// Updates a given Endwalker style Material to a Dawntrail style material, returning the Index Map that should
    /// be created after, and the mask maps that should be upgraded.
    /// </summary>
    private async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerColorset(XivMtrl mtrl, Dictionary<string, FileSource> files)
    {
        var ret = new Dictionary<string, UpgradeInfo>();
        if (mtrl.ColorSetData.Count != 256)
        {
            // This is already upgraded.
            return ret;
        }

        if (mtrl.ShaderPack == EShaderPack.Character)
        {
            mtrl.ShaderPack = EShaderPack.CharacterLegacy;
        }
        else
        {
            // Don't need to change the shaderpack for anything else here.
        }

        foreach (var tex in mtrl.Textures)
        {
            if ((tex.Flags & 0x8000) != 0)
            {
                var path = tex.Dx11Path;
                // DX9 textures are no longer used/supported in Endwalker,
                // And can sometimes cause issues here, so just turn the flag off.
                tex.Flags &= unchecked((ushort)~0x8000);
                tex.TexturePath = path;
            }
        }

        mtrl.AdditionalData = new byte[] { 0x34, 0x05, 0, 0, };
        if (mtrl.ShaderPack == EShaderPack.CharacterGlass)
        {
            var samplePath = "chara/equipment/e5001/material/v0001/mt_c0101e5001_met_b.mtrl";
            var sample = GetVanillaMtrl(samplePath);

            // Fix alpha threshhold for old gear.
            mtrl.ShaderKeys = sample!.ShaderKeys;
            mtrl.ShaderConstants = sample.ShaderConstants;
            mtrl.AdditionalData = sample.AdditionalData;
            mtrl.MaterialFlags &= ~EMaterialFlags1.Unknown0004;
            mtrl.MaterialFlags &= ~EMaterialFlags1.Unknown0008;
        }

        // (TexTools: `if (mtrl.ColorSetData == null) { ImportMtrl; return; }` — unreachable, Count was read above.)

        // Update Colorset
        var newData = new List<Half>(1024);
        for (int i = 0; i < 32; i++)
        {
            // Add empty rows after.
            newData.AddRange(GetDefaultColorsetRow(mtrl.ShaderPack));
        }

        for (int i = 0; i < 16; i++)
        {
            var pixel = i * 16;
            var offset = i * 8 * 4;

            // Diffuse Pixel
            newData[offset + 0] = mtrl.ColorSetData[pixel + 0];
            newData[offset + 1] = mtrl.ColorSetData[pixel + 1];
            newData[offset + 2] = mtrl.ColorSetData[pixel + 2];

            if (mtrl.ShaderPack == EShaderPack.CharacterLegacy)
            {
                newData[offset + 3] = mtrl.ColorSetData[pixel + 7];  // SE flipped Specular Power and Gloss values for some reason.
            }

            pixel += 4;
            offset += 4;

            if (mtrl.ShaderPack == EShaderPack.CharacterGlass)
            {
                newData[offset + 0] = (Half)0.8100586f;
                newData[offset + 1] = (Half)0.8100586f;
                newData[offset + 2] = (Half)0.8100586f;
            }
            else
            {
                // Specular Pixel
                newData[offset + 0] = mtrl.ColorSetData[pixel + 0];
                newData[offset + 1] = mtrl.ColorSetData[pixel + 1];
                newData[offset + 2] = mtrl.ColorSetData[pixel + 2];
            }

            if (mtrl.ShaderPack == EShaderPack.CharacterLegacy)
            {
                newData[offset + 3] = mtrl.ColorSetData[pixel - 1];  // SE flipped Specular Power and Gloss values for some reason.
            }

            pixel += 4;
            offset += 4;

            // Emissive Pixel
            newData[offset + 0] = mtrl.ColorSetData[pixel + 0];
            newData[offset + 1] = mtrl.ColorSetData[pixel + 1];
            newData[offset + 2] = mtrl.ColorSetData[pixel + 2];

            // Skip next 3 pixels
            offset += 4;
            offset += 4;
            offset += 4;
            offset += 4;

            //Unknown + subsurface material id
            newData[offset + 1] = mtrl.ColorSetData[pixel + 3];
            newData[offset + 2] = (Half)1.0f;  //  Subsurface Material Alpha

            pixel += 4;
            offset += 4;

            //Subsurface scaling data.
            newData[offset + 0] = mtrl.ColorSetData[pixel + 0];
            newData[offset + 1] = mtrl.ColorSetData[pixel + 1];
            newData[offset + 2] = mtrl.ColorSetData[pixel + 2];
            newData[offset + 3] = mtrl.ColorSetData[pixel + 3];
        }

        mtrl.ColorSetData = newData;
        if (mtrl.ColorSetDyeData != null && mtrl.ColorSetDyeData.Length > 0)
        {
            // Update Dye information.
            var newDyeData = new byte[128];
            // Update old dye information
            for (int i = 0; i < 16; i++)
            {
                var oldOffset = i * 2;
                var newOffset = i * 4;

                var newDyeBlock = (uint)0;
                var oldDyeBlock = BitConverter.ToUInt16(mtrl.ColorSetDyeData, oldOffset);

                // Old dye bitmask was 5 bits long.
                uint dyeBits = (uint)(oldDyeBlock & 0x1F);
                uint oldTemplate = (uint)(oldDyeBlock >> 5);

                if (mtrl.ShaderPack != EShaderPack.CharacterLegacy)
                {
                    oldTemplate += 1000;
                }

                newDyeBlock |= (oldTemplate << 16);
                newDyeBlock |= dyeBits;

                var newDyeBytes = BitConverter.GetBytes(newDyeBlock);
                Array.Copy(newDyeBytes, 0, newDyeData, newOffset, newDyeBytes.Length);
            }
            mtrl.ColorSetDyeData = newDyeData;
        }

        var usesMaskAsSpec = mtrl.ShaderKeys.Any(x => x.KeyId == 0xC8BD1DEF && (x.Value == 0xA02F4828 || x.Value == 0x198D11CD));

        var normalTex = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
        string idPath;
        string normalPath;

        // TexTools dereferences normalTex here before its null check below: a material without a normal map
        // throws, and UpdateEndwalkerMaterials swallows it (the material is left as it was).
        idPath = normalTex!.Dx11Path.Replace(".tex", "_id.tex");
        if (normalTex.Dx11Path.Contains("_n.tex"))
        {
            idPath = normalTex.Dx11Path.Replace("_n.tex", "_id.tex");
        }

        // Only alter to base game path if this is an original material and the calculated path isn't a base game file.
        if (game.FileExists(mtrl.MTRLPath) && !game.FileExists(idPath))
        {
            var original = GetVanillaMtrl(mtrl.MTRLPath);
            // Material is a default MTRL, steal the index path if it exists.
            var idSamp = original!.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Index);

            if (idSamp != null && !string.IsNullOrWhiteSpace(idSamp.Dx11Path))
            {
                idPath = idSamp.Dx11Path;
            }
        }

        // Create Id Texture reference if we have a normal map.
        if (normalTex != null)
        {
            normalPath = normalTex.Dx11Path;
            var idInfo = new UpgradeInfo()
            {
                Usage = EUpgradeTextureUsage.IndexMaps,
                Files = new Dictionary<string, string>()
                {
                    { "normal", normalPath },
                    { "index", idPath }
                },
            };

            var tex = new MtrlTexture();
            tex.TexturePath = idPath;
            tex.Sampler = new TextureSampler()
            {
                SamplerSettingsRaw = 0x000F8340,
                SamplerIdRaw = 1449103320,
            };

            if (normalTex.Sampler != null)
            {
                tex.Sampler.UTilingMode = normalTex.Sampler.UTilingMode;
                tex.Sampler.VTilingMode = normalTex.Sampler.VTilingMode;
            }

            mtrl.Textures.Add(tex);

            ret.Add(idPath, idInfo);
        }

        if (mtrl.ShaderPack == EShaderPack.CharacterLegacy)
        {
            var maskSamp = mtrl.Textures.FirstOrDefault(x => x.Sampler != null && x.Sampler.SamplerId == ESamplerId.g_SamplerMask);
            if (maskSamp != null)
            {
                if (!usesMaskAsSpec)
                {
                    var maskInfo = new UpgradeInfo()
                    {
                        Usage = EUpgradeTextureUsage.GearMaskLegacy,
                        Files = new Dictionary<string, string>()
                        {
                            { "mask_old", maskSamp.Dx11Path },
                            { "mask_new", maskSamp.Dx11Path }
                        },
                    };
                    maskSamp.TexturePath = maskSamp.Dx11Path;
                    ret.Add(maskSamp.Dx11Path, maskInfo);
                }
            }
        }
        else if (mtrl.ShaderPack == EShaderPack.CharacterGlass)
        {
            if (!usesMaskAsSpec)
            {
                var maskSamp = mtrl.Textures.FirstOrDefault(x => x.Sampler != null && x.Sampler.SamplerId == ESamplerId.g_SamplerMask);
                if (maskSamp != null)
                {
                    var maskInfo = new UpgradeInfo()
                    {
                        Usage = EUpgradeTextureUsage.GearMaskNew,
                        Files = new Dictionary<string, string>()
                        {
                            { "mask_old", maskSamp.Dx11Path },
                            { "mask_new", maskSamp.Dx11Path }
                        },
                    };
                    maskSamp.TexturePath = maskSamp.Dx11Path;
                    ret.Add(maskSamp.Dx11Path, maskInfo);
                }
            }
        }

        // TexTools does not null-check Sampler in these two lookups.
        var specTex = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerSpecular);
        var diffuseTex = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerDiffuse);
        if (specTex != null)
        {
            if (diffuseTex != null)
            {
                // Switch material to compat mode properly if it's not already
                specTex.Sampler.SamplerId = ESamplerId.g_SamplerMask;
                var key = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0xC8BD1DEF);
                if (key == null)
                {
                    key = new ShaderKey()
                    {
                        KeyId = 0xC8BD1DEF,
                        Value = 0x198D11CD
                    };
                    mtrl.ShaderKeys.Add(key);
                }
                else
                {
                    key.Value = 0x198D11CD;
                }

                key = mtrl.ShaderKeys.FirstOrDefault(x => x.KeyId == 0xB616DC5A);
                if (key == null)
                {
                    key = new ShaderKey()
                    {
                        KeyId = 0xB616DC5A,
                        Value = 0x600EF9DF
                    };
                    mtrl.ShaderKeys.Add(key);
                }
                else
                {
                    key.Value = 0x600EF9DF;
                }
            }
        }

        var data = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
        WriteFile(data, mtrl.MTRLPath, files);

        await Task.CompletedTask;
        return ret;
    }

    private async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerHairMaterial(XivMtrl mtrl, HashSet<string> _ConvertedTextures, Dictionary<string, FileSource> files)
    {
        var ret = new Dictionary<string, UpgradeInfo>();
        var normalTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Normal);
        var maskTexSampler = mtrl.Textures.FirstOrDefault(x => mtrl.ResolveFullUsage(x) == XivTexType.Mask);

        if (normalTexSampler == null || maskTexSampler == null)
        {
            // Not resolveable.  Mtrl has some weird stuff going on.
            return ret;
        }

        // Arbitrary base game hair file to use to replace our shader constants.
        var constantBase = GetVanillaMtrl(_SampleHair);
        var originalConsts = mtrl.ShaderConstants;
        mtrl.ShaderConstants = constantBase!.ShaderConstants;
        mtrl.AdditionalData = constantBase.AdditionalData;

        // Copy the alpha threshold over since the functionality there is unchanged.
        var alpha = originalConsts.FirstOrDefault(x => x.ConstantId == 0x29AC0223);
        var alphaDest = mtrl.ShaderConstants.FirstOrDefault(x => x.ConstantId == 0x29AC0223);
        if (alpha != null && alphaDest != null)
        {
            alphaDest.Values = alpha.Values.ToList();
        }

        ret.Add(normalTexSampler.Dx11Path, new UpgradeInfo()
        {
            Usage = EUpgradeTextureUsage.HairMaps,
            Files = new Dictionary<string, string>()
            {
                { "normal", normalTexSampler.Dx11Path },
                { "mask", maskTexSampler.Dx11Path }
            },
        });

        var mtrlData = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
        WriteFile(mtrlData, mtrl.MTRLPath, files);

        await Task.CompletedTask;
        return ret;
    }

    public static Half[] GetDefaultColorsetRow(EShaderPack pack)
    {
        var row = new Half[32];

        // Diffuse pixel base
        for (int i = 0; i < 8; i++)
        {
            row[i] = (Half)1.0f;
        }

        row[11] = (Half)0;
        row[12] = (Half)0;
        row[13] = (Half)0;
        row[14] = (Half)0;

        row[16] = (Half)0;
        row[25] = (Half)0;

        // Tile opacity
        row[6 * 4 + 2] = (Half)1.0f;

        row[7 * 4 + 0] = (Half)16.0f;
        row[7 * 4 + 3] = (Half)16.0f;

        if (pack == EShaderPack.CharacterGlass)
        {
            // Spec alpha
            row[1 * 4 + 3] = (Half)0;

            // Emiss Alpha
            row[2 * 4 + 3] = (Half)1;

            // Fresnel Terms
            row[3 * 4 + 0] = (Half)1;
            row[3 * 4 + 1] = (Half)0;
            row[3 * 4 + 2] = (Half)2.5f;

            // Roughness
            row[4 * 4 + 0] = (Half)0.5f;

            // Wetness...? Some kind of reflection thing.
            row[5 * 4 + 1] = (Half)1f;

            // Submat Unknown.
            row[6 * 4 + 3] = (Half)5;
        }

        return row;
    }
}
