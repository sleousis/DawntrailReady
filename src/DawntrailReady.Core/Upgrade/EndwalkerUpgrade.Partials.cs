// Port of TexTools xivModdingFramework/Mods/EndwalkerUpgrade.cs (commit 25b3ae72): the "partial" (texture-only)
// upgrades — HairRegexSet, UpdateUnclaimedHairTextures, UpdateUnclaimedHairAccessory, UpdateEyeMask — plus the
// race lookup it uses from Helpers/IOUtil.cs GetRaceFromPath and General/Enums/XivRace.cs.
using System.Text.RegularExpressions;
using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Textures;
using static DawntrailReady.Core.Materials.ShaderHelpers;

namespace DawntrailReady.Core.Upgrade;

public sealed partial class EndwalkerUpgrade
{
    internal sealed class HairRegexSet
    {
        public required Regex OldTextureRegex;
        public required Regex MaterialRegex;
        public required string MaterialFormat;
    }

    internal static readonly HairRegexSet HairRegexes = new()
    {
        OldTextureRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/texture\\/(?:--)?c([0-9]{4})h([0-9]{4})_hir_([ns])\\.tex"),
        MaterialRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/material\\/v0001\\/mt_c([0-9]{4})h([0-9]{4})_hir_a\\.mtrl"),
        MaterialFormat = "chara/human/c{0}/obj/hair/h{1}/material/v0001/mt_c{0}h{1}_hir_a.mtrl",
    };

    //chara/human/c0801/obj/tail/t0003/material/v0001/mt_c0801t0003_a.mtrl
    internal static readonly HairRegexSet TailRegexes = new()
    {
        OldTextureRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/tail\\/t[0-9]{4}\\/texture\\/(?:--)?c([0-9]{4})t([0-9]{4})_etc_([ns])\\.tex"),
        MaterialRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/tail\\/t[0-9]{4}\\/material\\/v0001\\/mt_c([0-9]{4})t([0-9]{4})_a\\.mtrl"),
        MaterialFormat = "chara/human/c{0}/obj/tail/t{1}/material/v0001/mt_c{0}t{1}_a.mtrl",
    };

    internal static readonly HairRegexSet EarRegexes = new()
    {
        OldTextureRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/zear\\/z[0-9]{4}\\/texture\\/(?:--)?c([0-9]{4})z([0-9]{4})_etc_([ns])\\.tex"),
        MaterialRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/zear\\/z[0-9]{4}\\/material\\/v0001\\/mt_c([0-9]{4})z([0-9]{4})_a\\.mtrl"),
        MaterialFormat = "chara/human/c{0}/obj/zear/z{1}/material/v0001/mt_c{0}z{1}_a.mtrl",
    };

    internal static readonly HairRegexSet AccessoryRegexes = new()
    {
        OldTextureRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/texture\\/(?:--)?c([0-9]{4})h([0-9]{4})_acc_([dns])\\.tex"),
        MaterialRegex = new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair\\/h[0-9]{4}\\/material\\/v0001\\/mt_c([0-9]{4})h([0-9]{4})_acc_b\\.mtrl"),
        MaterialFormat = "chara/human/c{0}/obj/hair/h{1}/material/v0001/mt_c{0}h{1}_acc_b.mtrl",
    };

    internal static readonly Regex EyeMaskPathRegex = new("chara/human/c[0-9]{4}/obj/face/f[0-9]{4}/texture/--c[0-9]{4}f[0-9]{4}_iri_s.tex");

    public async Task UpdateUnclaimedHairTextures(List<string> files, HashSet<string>? _ConvertedTextures, Dictionary<string, FileSource> fileInfos)
    {
        await UpdateUnclaimedHairTextures(HairRegexes, files, _ConvertedTextures, fileInfos);
        await UpdateUnclaimedHairTextures(TailRegexes, files, _ConvertedTextures, fileInfos);
        await UpdateUnclaimedHairTextures(EarRegexes, files, _ConvertedTextures, fileInfos);

        await UpdateUnclaimedHairAccessory(AccessoryRegexes, files, _ConvertedTextures, fileInfos);
    }

    /// <summary>
    /// Copies inbound old hair (tail, ear) textures to SE's new pathing if they were included by themselves,
    /// without their associated default material, and upgrades them.
    /// </summary>
    private async Task UpdateUnclaimedHairTextures(HairRegexSet hairset, List<string> files, HashSet<string>? _ConvertedTextures, Dictionary<string, FileSource> fileInfos)
    {
        var results = new Dictionary<int, Dictionary<int, List<(string Path, XivTexType TexType)>>>();

        var materials = new List<(int Race, int Hair)>();
        List<string> fileList = fileInfos.Keys.ToList();
        foreach (var file in fileList)
        {
            var matMatch = hairset.MaterialRegex.Match(file);
            if (matMatch.Success)
            {
                var rid = Int32.Parse(matMatch.Groups[1].Value);
                var hid = Int32.Parse(matMatch.Groups[2].Value);
                materials.Add((rid, hid));
                continue;
            }

            // Only match textures to those in the main list.
            if (!files.Contains(file)) continue;

            var match = hairset.OldTextureRegex.Match(file);
            if (!match.Success) continue;

            var raceId = Int32.Parse(match.Groups[1].Value);
            var hairId = Int32.Parse(match.Groups[2].Value);
            var tex = match.Groups[3].Value;
            if (!results.ContainsKey(raceId))
            {
                results.Add(raceId, new Dictionary<int, List<(string Path, XivTexType TexType)>>());
            }

            if (!results[raceId].ContainsKey(hairId))
            {
                results[raceId].Add(hairId, new List<(string Path, XivTexType TexType)>());
            }

            var tt = tex == "n" ? XivTexType.Normal : XivTexType.Specular;

            if (results[raceId][hairId].Any(x => x.TexType == tt))
            {
                var prev = results[raceId][hairId].First(x => x.TexType == tt);
                if (prev.Path.Contains("--"))
                {
                    // Dx11 wins out.
                    continue;
                }
                else
                {
                    results[raceId][hairId].RemoveAll(x => x.TexType == tt);
                }
            }
            results[raceId][hairId].Add((file, tt));
        }

        // Winnow list to only entries with both types, that also lack material files.
        var races = results.Keys.ToList();
        foreach (var r in races)
        {
            var hairs = results[r].Keys.ToList();
            foreach (var h in hairs)
            {
                if (results[r][h].Count < 2)
                {
                    results[r].Remove(h);
                }
                else if (materials.Any(x => x.Hair == h && x.Race == r))
                {
                    results[r].Remove(h);
                }
            }

            if (results[r].Count == 0)
            {
                results.Remove(r);
            }
        }

        if (results.Count == 0) return;

        foreach (var rKv in results)
        {
            var race = rKv.Key.ToString("D4");
            foreach (var hKv in rKv.Value)
            {
                var hair = hKv.Key.ToString("D4");
                var matPath = string.Format(hairset.MaterialFormat, race, hair);

                if (!game.FileExists(matPath))
                {
                    // Invalid path or non-existent in DT or some other shenanigans.
                    continue;
                }

                var mtrl = GetVanillaMtrl(matPath)!;

                if (mtrl.ShaderPack != EShaderPack.Hair)
                {
                    // Au Ra or some other shenanigans here.
                    continue;
                }

                var normTex = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerNormal);
                var maskTex = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerMask);

                if (normTex == null || maskTex == null)
                {
                    // Sus...
                    continue;
                }

                // (TexTools resolves the owning item here only for Dat.CopyFile on its transaction route.)

                // Ensure none of the files have already been converted.
                var skip = false;
                foreach (var tex in hKv.Value)
                {
                    var newPath = tex.TexType == XivTexType.Normal ? normTex.Dx11Path : maskTex.Dx11Path;

                    if (fileList.Contains(newPath))
                    {
                        // Already converted.
                        skip = true;
                        continue;
                    }
                }
                if (skip)
                {
                    continue;
                }

                foreach (var tex in hKv.Value)
                {
                    var newPath = tex.TexType == XivTexType.Normal ? normTex.Dx11Path : maskTex.Dx11Path;
                    var data = ResolveFile(tex.Path, fileInfos);
                    WriteFile(data, newPath, fileInfos);

                    files.Add(newPath);
                }

                try
                {
                    await UpdateEndwalkerHairTextures(normTex.Dx11Path, maskTex.Dx11Path, _ConvertedTextures, fileInfos);
                }
                catch (Exception ex)
                {
                    Trace(ex);
                    continue;
                }

                if (hairset == TailRegexes && (mtrl.MaterialFlags & EMaterialFlags1.HideBackfaces) == 0)
                {
                    // Flip backface bit to match old mtrls.
                    mtrl.MaterialFlags |= EMaterialFlags1.HideBackfaces;

                    // Rip constants from standard hair to better match usages.
                    var constantBase = GetVanillaMtrl(_SampleHair);
                    mtrl.ShaderConstants = constantBase!.ShaderConstants;

                    var data = Mtrl.XivMtrlToUncompressedMtrl(mtrl);
                    WriteFile(data, mtrl.MTRLPath, fileInfos);
                }
            }
        }
    }

    private async Task UpdateUnclaimedHairAccessory(HairRegexSet hairset, List<string> files, HashSet<string>? _ConvertedTextures, Dictionary<string, FileSource> fileInfos)
    {
        var results = new Dictionary<int, Dictionary<int, List<(string Path, XivTexType TexType)>>>();

        var materials = new List<(int Race, int Hair)>();
        foreach (var file in files)
        {
            var matMatch = hairset.MaterialRegex.Match(file);
            if (matMatch.Success)
            {
                var rid = Int32.Parse(matMatch.Groups[1].Value);
                var hid = Int32.Parse(matMatch.Groups[2].Value);
                materials.Add((rid, hid));
                continue;
            }

            var match = hairset.OldTextureRegex.Match(file);
            if (!match.Success) continue;

            var raceId = Int32.Parse(match.Groups[1].Value);
            var hairId = Int32.Parse(match.Groups[2].Value);
            var tex = match.Groups[3].Value;
            if (!results.ContainsKey(raceId))
            {
                results.Add(raceId, new Dictionary<int, List<(string Path, XivTexType TexType)>>());
            }

            if (!results[raceId].ContainsKey(hairId))
            {
                results[raceId].Add(hairId, new List<(string Path, XivTexType TexType)>());
            }

            XivTexType tt;
            if (tex == "n")
            {
                tt = XivTexType.Normal;
            }
            else if (tex == "s")
            {
                tt = XivTexType.Specular;
            }
            else if (tex == "d")
            {
                tt = XivTexType.Diffuse;
            }
            else
            {
                continue;
            }

            if (results[raceId][hairId].Any(x => x.TexType == tt))
            {
                var prev = results[raceId][hairId].First(x => x.TexType == tt);
                if (prev.Path.Contains("--"))
                {
                    // Dx11 wins out.
                    continue;
                }
                else
                {
                    results[raceId][hairId].RemoveAll(x => x.TexType == tt);
                }
            }
            results[raceId][hairId].Add((file, tt));
        }

        // Winnow the list to only entries without their associated material.
        var races = results.Keys.ToList();
        foreach (var r in races)
        {
            var hairs = results[r].Keys.ToList();
            foreach (var h in hairs)
            {
                if (materials.Any(x => x.Hair == h && x.Race == r))
                {
                    results[r].Remove(h);
                }
            }

            if (results[r].Count == 0)
            {
                results.Remove(r);
            }
        }

        if (results.Count == 0) return;

        foreach (var rKv in results)
        {
            var race = rKv.Key.ToString("D4");
            foreach (var hKv in rKv.Value)
            {
                var hair = hKv.Key.ToString("D4");
                var matPath = string.Format(hairset.MaterialFormat, race, hair);

                if (!game.FileExists(matPath))
                {
                    // Invalid path or non-existent in DT or some other shenanigans.
                    continue;
                }

                var mtrl = GetVanillaMtrl(matPath)!;

                if (mtrl.ShaderPack != EShaderPack.Character && mtrl.ShaderPack != EShaderPack.CharacterLegacy)
                {
                    // Some kind of shenanigans going on here where this is not a proper accessory.
                    continue;
                }

                var normTex = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerNormal);
                var specTex = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerMask);
                var diffuseTex = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerDiffuse);

                if (normTex == null)
                {
                    // If we couldn't resolve normal, we're in trouble.
                    continue;
                }

                // Ensure none of the files have already been converted.
                var skip = false;
                foreach (var tex in hKv.Value)
                {
                    var newPath = "";
                    if (tex.TexType == XivTexType.Normal)
                    {
                        newPath = normTex.Dx11Path;
                    }
                    else if (tex.TexType == XivTexType.Specular)
                    {
                        if (specTex == null)
                        {
                            skip = true;
                            break;
                        }
                        newPath = specTex.Dx11Path;
                    }
                    else if (tex.TexType == XivTexType.Diffuse)
                    {
                        if (diffuseTex == null)
                        {
                            skip = true;
                            break;
                        }
                        newPath = diffuseTex.Dx11Path;
                    }

                    if (files.Contains(newPath))
                    {
                        // Already converted.
                        skip = true;
                        continue;
                    }
                }

                if (skip)
                {
                    continue;
                }

                foreach (var tex in hKv.Value)
                {
                    var newPath = "";
                    if (tex.TexType == XivTexType.Normal)
                    {
                        newPath = normTex.Dx11Path;
                    }
                    else if (tex.TexType == XivTexType.Specular)
                    {
                        newPath = specTex!.Dx11Path;
                    }
                    else if (tex.TexType == XivTexType.Diffuse)
                    {
                        newPath = diffuseTex!.Dx11Path;
                    }

                    // Copy files to new destinations.
                    var data = ResolveFile(tex.Path, fileInfos);
                    WriteFile(data, newPath, fileInfos);

                    files.Add(newPath);
                }
            }
        }

        await Task.CompletedTask;
    }

    public async Task UpdateEyeMask(string maskPath, HashSet<string>? _ConvertedTextures, Dictionary<string, FileSource> files)
    {
        if (!EyeMaskPathRegex.IsMatch(maskPath))
        {
            return;
        }

        _ConvertedTextures ??= new HashSet<string>();

        if (!Exists(maskPath, files))
        {
            return;
        }

        if (_ConvertedTextures.Contains(maskPath))
        {
            return;
        }
        var newTexPath = maskPath.Replace(".tex", "_diffuse.tex").Replace("--", "");

        var data = ResolveFile(maskPath, files);

        var tex = XivTex.FromUncompressedTex(data!);

        var facex = new Regex("f([0-9]{4})");
        var match = facex.Match(Path.GetFileName(maskPath));
        if (!match.Success)
        {
            return;
        }

        var race = GetRaceCodeFromPath(maskPath);
        var face = Int32.Parse(match.Groups[1].Value);

        var irisFormat = "chara/human/c{0}/obj/face/f{1}/material/mt_c{0}f{1}_iri_a.mtrl";
        var irisPath = string.Format(irisFormat, race, face.ToString("D4"));

        if (!game.FileExists(irisPath))
        {
            // Hmmm...
            return;
        }

        // Update Material
        var baseMaterial = Mtrl.GetXivMtrl(game.ReadFile(irisPath)!, irisPath);

        var mtrlTex = baseMaterial.Textures.FirstOrDefault(x => x.Sampler != null && x.Sampler.SamplerId == ESamplerId.g_SamplerDiffuse);
        newTexPath = mtrlTex!.TexturePath;

        // Convert Mask to Diffuse
        var pixels = await tex.GetRawPixels();

        // ConvertEyeMaskToDiffuse pulls the base game eye files as its baseline.
        var baseDiffuseTex = GetVanillaTex("chara/common/texture/eye/eye01_base.tex");
        var frameTex = GetVanillaTex("chara/common/texture/eye/eye01_mask.tex");
        var updated = await EyeMask.ConvertEyeMaskToDiffuse(pixels, tex.Width, tex.Height, baseDiffuseTex, frameTex);

        await TextureHelpers.SwizzleRB(updated.PixelData, updated.Width, updated.Height);

        // Create MipMaps (And DDS header that we don't really need)
        var maskData = await Tex.ConvertToDDS(updated.PixelData, DefaultTextureFormat, true, updated.Width, updated.Height);

        // Convert DDS to uncompressed Tex
        maskData = Tex.DDSToUncompressedTex(maskData);

        // Write the updated material and texture.
        WriteFile(maskData, newTexPath, files);
        _ConvertedTextures.Add(maskPath);
    }

    private static readonly Regex ExtractRaceRegex = new("c([0-9]{4})");

    /// <summary>Every race code XivRace defines (its Description attributes).</summary>
    private static readonly HashSet<string> XivRaceCodes =
    [
        "0101", "0104", "0201", "0204", "0301", "0304", "0401", "0404", "0501", "0504", "0601", "0604",
        "0701", "0704", "0801", "0804", "0901", "0904", "1001", "1004", "1101", "1104", "1201", "1204",
        "1301", "1304", "1401", "1404", "1501", "1504", "1601", "1604", "1701", "1704", "1801", "1804",
        "9104", "9204", "0000",
    ];

    /// <summary>
    /// IOUtil.GetRaceFromPath(path).GetRaceCode(). An unknown code resolves to XivRace's default member
    /// (All_Races, "0000"), like XivRaces.GetXivRace's FirstOrDefault; monster and ui paths are "0000" too.
    /// </summary>
    internal static string GetRaceCodeFromPath(string? path)
    {
        if (path == null) return "0000";
        if (path.Contains("ui/") || path.Contains(".avfx")) return "0000";
        if (path.Contains("monster")) return "0000";
        var res = ExtractRaceRegex.Match(path);
        if (res.Success && XivRaceCodes.Contains(res.Groups[1].Value)) return res.Groups[1].Value;
        return "0000";
    }
}
