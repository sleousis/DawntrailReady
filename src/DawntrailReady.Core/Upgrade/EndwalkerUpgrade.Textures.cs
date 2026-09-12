// Port of TexTools xivModdingFramework/Mods/EndwalkerUpgrade.cs (commit 25b3ae72): CreateIndexFromNormal,
// UpdateEndwalkerHairTextures, UpgradeRemainingTextures, UpgradeMaskTex.
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Textures;

namespace DawntrailReady.Core.Upgrade;

public sealed partial class EndwalkerUpgrade
{
    /// <summary>
    /// Creates the actual index file data from the constituent parts.
    /// Returns the bytes of an uncompressed Tex file, or null data when the normal is not in the file set.
    /// </summary>
    private static async Task<(string indexFilePath, byte[]? data)> CreateIndexFromNormal(string indexPath, string sourceNormalPath, Dictionary<string, FileSource> files)
    {
        var data = ResolveFile(sourceNormalPath, files);
        if (data == null)
        {
            // Can't create an index.
            return (indexPath, null);
        }

        // Read normal file.
        var normalTex = XivTex.FromUncompressedTex(data);

        if (!IOUtil.IsPowerOfTwo(normalTex.Width) || !IOUtil.IsPowerOfTwo(normalTex.Height))
        {
            await Tex.ResizeXivTx(normalTex, IOUtil.RoundToPowerOfTwo(normalTex.Width), IOUtil.RoundToPowerOfTwo(normalTex.Height));
        }
        var normalData = await normalTex.GetRawPixels();

        var indexData = new byte[normalTex.Width * normalTex.Height * 4];
        await TextureHelpers.CreateIndexTexture(normalData, indexData, normalTex.Width, normalTex.Height);

        var format = DefaultTextureFormat == XivTexFormat.A8R8G8B8 ? XivTexFormat.A8R8G8B8 : XivTexFormat.BC5;
        // Create MipMaps (And DDS header that we don't really need)
        indexData = await Tex.ConvertToDDS(indexData, format, true, normalTex.Width, normalTex.Height, true);

        // Convert DDS to uncompressed Tex
        indexData = Tex.DDSToUncompressedTex(indexData);

        return (indexPath, indexData);
    }

    private static async Task UpdateEndwalkerHairTextures(string normalPath, string maskPath, HashSet<string>? _ConvertedTextures, Dictionary<string, FileSource> files)
    {
        _ConvertedTextures ??= new HashSet<string>();
        var oldNormData = ResolveFile(normalPath, files);
        var oldMaskData = ResolveFile(maskPath, files);

        if (oldNormData == null || oldMaskData == null)
        {
            // Shouldn't ever be able to hit this, but if we do, nothing to be done about it.
            throw new FileNotFoundException("Unable to properly resolve existing Hair Normal/Mask texture.");
        }

        // Read normal file.
        var normalTex = XivTex.FromUncompressedTex(oldNormData);
        var maskTex = XivTex.FromUncompressedTex(oldMaskData);

        if (!IOUtil.IsPowerOfTwo(normalTex.Width) || !IOUtil.IsPowerOfTwo(normalTex.Height))
        {
            await Tex.ResizeXivTx(normalTex, IOUtil.RoundToPowerOfTwo(normalTex.Width), IOUtil.RoundToPowerOfTwo(normalTex.Height));
        }
        if (!IOUtil.IsPowerOfTwo(maskTex.Width) || !IOUtil.IsPowerOfTwo(maskTex.Height))
        {
            await Tex.ResizeXivTx(maskTex, IOUtil.RoundToPowerOfTwo(maskTex.Width), IOUtil.RoundToPowerOfTwo(maskTex.Height));
        }

        // Resize to be same size.
        var data = await TextureHelpers.ResizeImages(normalTex, maskTex);

        // Create the final hair pixel data.
        await TextureHelpers.CreateHairMaps(data.TexA, data.TexB, data.Width, data.Height);

        if (!_ConvertedTextures.Contains(normalPath))
        {
            // Normal
            var normalData = await Tex.ConvertToDDS(data.TexA, DefaultTextureFormat, true, data.Width, data.Height, true);
            normalData = Tex.DDSToUncompressedTex(normalData);
            WriteFile(normalData, normalPath, files);
            _ConvertedTextures.Add(normalPath);
        }

        if (!_ConvertedTextures.Contains(maskPath))
        {
            // Mask
            var maskData = await Tex.ConvertToDDS(data.TexB, DefaultTextureFormat, true, data.Width, data.Height, true);
            maskData = Tex.DDSToUncompressedTex(maskData);
            WriteFile(maskData, maskPath, files);
            _ConvertedTextures.Add(maskPath);
        }
    }

    /// <summary>
    /// Scans one option's file collection for the texture pieces the first pass asked for, and creates or
    /// upgrades them where the option contains their source.
    /// </summary>
    public async Task UpgradeRemainingTextures(Dictionary<string, FileSource> files, Dictionary<string, UpgradeInfo> upgrades)
    {
        foreach (var kv in upgrades)
        {
            var upgrade = kv.Value;

            if (upgrade.Usage == EUpgradeTextureUsage.IndexMaps)
            {
                if (files.ContainsKey(upgrade.Files["normal"]))
                {
                    var res = await CreateIndexFromNormal(upgrade.Files["index"], upgrade.Files["normal"], files);
                    if (res.data == null)
                    {
                        // If the normal was not included, just skip it.
                        continue;
                    }
                    WriteFile(res.data, res.indexFilePath, files);
                }
            }
            else if (upgrade.Usage == EUpgradeTextureUsage.HairMaps)
            {
                if (files.ContainsKey(upgrade.Files["normal"])
                    && files.ContainsKey(upgrade.Files["mask"]))
                {
                    await UpdateEndwalkerHairTextures(upgrade.Files["normal"], upgrade.Files["mask"], null, files);
                }
                else if (files.ContainsKey(upgrade.Files["normal"])
                    || files.ContainsKey(upgrade.Files["mask"]))
                {
                    // One but not both.
                    throw new FileNotFoundException("Unable to upgrade Hair Normal/Mask - Normal and Mask do not exist in the same file set/modpack option.\n" + upgrade.Files["normal"] + "\n" + upgrade.Files["mask"]);
                }
            }
            else if (upgrade.Usage == EUpgradeTextureUsage.GearMaskNew)
            {
                if (files.ContainsKey(upgrade.Files["mask_old"]))
                {
                    var data = ResolveFile(upgrade.Files["mask_old"], files);
                    // TexTools passes a null here straight on (no check before UpgradeMaskTex).
                    data = await UpgradeMaskTex(data!);
                    if (data != null)
                    {
                        WriteFile(data, upgrade.Files["mask_new"], files);
                    }
                }
            }
            else if (upgrade.Usage == EUpgradeTextureUsage.GearMaskLegacy)
            {
                if (files.ContainsKey(upgrade.Files["mask_old"]))
                {
                    var data = ResolveFile(upgrade.Files["mask_old"], files);
                    if (data != null)
                    {
                        data = await UpgradeMaskTex(data, true);
                        WriteFile(data, upgrade.Files["mask_new"], files);
                    }
                }
            }
        }
    }

    public static async Task<byte[]> UpgradeMaskTex(byte[] uncompMaskTex, bool legacy = false)
    {
        var tex = XivTex.FromUncompressedTex(uncompMaskTex);

        if (!IOUtil.IsPowerOfTwo(tex.Width) || !IOUtil.IsPowerOfTwo(tex.Height))
        {
            await Tex.ResizeXivTx(tex, IOUtil.RoundToPowerOfTwo(tex.Width), IOUtil.RoundToPowerOfTwo(tex.Height));
        }

        var pixData = await tex.GetRawPixels();

        await TextureHelpers.UpgradeGearMask(pixData, tex.Width, tex.Height, legacy);
        var data = await Tex.ConvertToDDS(pixData, DefaultTextureFormat, true, tex.Width, tex.Height, true);
        data = Tex.DDSToUncompressedTex(data);

        return data;
    }
}
