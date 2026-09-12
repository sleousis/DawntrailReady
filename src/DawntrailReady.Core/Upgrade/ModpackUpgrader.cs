// Port of TexTools xivModdingFramework/Mods/ModpackUpgrader.cs (commit 25b3ae72): UpgradeModpack (the data half),
// AnyChanges, ResolveHighlightOptionsAndMashupHair, RepathHairMashups, UpdateSkinPaths. Reading and writing the
// modpack itself (WizardData.FromModpack / WriteModpack) lives in Core/Packaging.
using System.Text.RegularExpressions;
using DawntrailReady.Core.Game;
using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using static DawntrailReady.Core.Materials.ShaderHelpers;
using static DawntrailReady.Core.Upgrade.EndwalkerUpgrade;

namespace DawntrailReady.Core.Upgrade;

/// <summary>What one upgrade did.</summary>
/// <param name="AnyChanges">TexTools' verdict: some option gained, lost or replaced an entry.</param>
/// <param name="Notes">Errors TexTools swallows (it writes them to Trace), in order.</param>
public sealed record UpgradeResult(bool AnyChanges, IReadOnlyList<string> Notes)
{
    /// <summary>Every entry that was added or replaced, per option.</summary>
    public IReadOnlyList<ChangedFile> Changes { get; init; } = [];

    /// <summary>
    /// Stricter than <see cref="AnyChanges"/>: an entry was added, or a replaced entry's bytes differ. TexTools'
    /// mashup-hair repath re-writes materials unconditionally; when that re-write reproduces the same bytes
    /// there is nothing to save.
    /// </summary>
    public bool AnyContentChanges { get; init; }
}

public sealed record ChangedFile(ModOption Option, string GamePath, bool Added, bool ContentChanged);

/// <summary>Full modpack upgrade handler (TexTools ModpackUpgrader), on an already-read <see cref="ModpackData"/>.</summary>
public sealed class ModpackUpgrader(IGameData game)
{
    public async Task<UpgradeResult> UpgradeModpack(ModpackData data, bool includePartials = true, CancellationToken ct = default)
    {
        var notes = new List<string>();
        var upgrade = new EndwalkerUpgrade(game, notes.Add);

        var textureUpgradeTargets = new Dictionary<string, UpgradeInfo>();
        var allTextures = new HashSet<string>();
        var anyChanges = false;

        var originals = new Dictionary<ModOption, Dictionary<string, FileSource>>();

        // Store original data for comparison later.
        foreach (var o in data.FileOptions)
        {
            originals.Add(o, new Dictionary<string, FileSource>(o.Files!, o.Files!.Comparer));
        }

        // Pre-Upgrades - Specifically can hair to see if we can resolve some highlight-related stuff.
        await ResolveHighlightOptionsAndMashupHair(data);

        // First Round Upgrade -
        // This does models and base MTRLS only, and caches their texture information.
        foreach (var o in data.FileOptions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var missing = await upgrade.UpdateEndwalkerFiles(o.Files!);
                foreach (var kv in missing)
                {
                    if (!textureUpgradeTargets.ContainsKey(kv.Key))
                    {
                        textureUpgradeTargets.Add(kv.Key, kv.Value);
                    }
                }

                var textures = o.Files!.Select(x => x.Key).Where(x => x.EndsWith(".tex", StringComparison.Ordinal));
                allTextures.UnionWith(textures);
            }
            catch (Exception ex)
            {
                var mes = "An error occurred while updating Group: " + o.GroupName + " - Option: " + o.OptionName + "\n\n" + ex.Message;
                throw new Exception(mes);
            }
        }

        // Second Round Upgrade - This does textures based on the collated upgrade information from the previous pass
        foreach (var o in data.FileOptions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await upgrade.UpgradeRemainingTextures(o.Files!, textureUpgradeTargets);
            }
            catch (Exception ex)
            {
                var mes = "An error occurred while updating Group: " + o.GroupName + " - Option: " + o.OptionName + "\n\n" + ex.Message;
                throw new Exception(mes);
            }
        }

        if (includePartials)
        {
            // Find all un-referenced textures.
            var unusedTextures = new HashSet<string>(
                allTextures.Where(t =>
                    !textureUpgradeTargets.Any(x =>
                        x.Value.Files.ContainsValue(t)
                    )));

            foreach (var o in data.FileOptions)
            {
                UpdateSkinPaths(o.Files!);
            }

            // Third Round Upgrade - This inspects as-of-yet unupgraded textures for possible jank-upgrades,
            // Which is to say, upgrades where we can infer their usage and pairing, but the base mtrl was not included.
            foreach (var o in data.FileOptions)
            {
                ct.ThrowIfCancellationRequested();
                var contained = unusedTextures.Where(x => o.Files!.ContainsKey(x));
                await upgrade.UpdateUnclaimedHairTextures(contained.ToList(), null, o.Files!);

                foreach (var possibleMask in contained)
                {
                    await upgrade.UpdateEyeMask(possibleMask, null, o.Files!);
                }
            }
        }

        // Evaluate success
        foreach (var o in data.FileOptions)
        {
            if (!anyChanges)
            {
                anyChanges = AnyChanges(originals[o], o.Files!);
            }
            if (anyChanges) break;
        }

        var changes = CollectChanges(originals);
        return new UpgradeResult(anyChanges, notes)
        {
            Changes = changes,
            AnyContentChanges = changes.Any(c => c.Added || c.ContentChanged),
        };
    }

    private static bool AnyChanges(Dictionary<string, FileSource> original, Dictionary<string, FileSource> newFiles)
    {
        if (original.Count != newFiles.Count)
        {
            return true;
        }

        foreach (var kv in original)
        {
            if (!newFiles.TryGetValue(kv.Key, out var n))
            {
                return true;
            }

            if (!ReferenceEquals(kv.Value, n))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Not in TexTools: lists what changed so the caller can save exactly that.</summary>
    private static List<ChangedFile> CollectChanges(Dictionary<ModOption, Dictionary<string, FileSource>> originals)
    {
        var list = new List<ChangedFile>();
        foreach (var (option, original) in originals)
        {
            foreach (var (path, now) in option.Files!)
            {
                if (!original.TryGetValue(path, out var before))
                {
                    list.Add(new ChangedFile(option, path, Added: true, ContentChanged: true));
                }
                else if (!ReferenceEquals(before, now))
                {
                    list.Add(new ChangedFile(option, path, Added: false, ContentChanged: !SameBytes(before, now)));
                }
            }
        }
        return list;
    }

    private static bool SameBytes(FileSource a, FileSource b)
    {
        try
        {
            return a.Read().AsSpan().SequenceEqual(b.Read());
        }
        catch
        {
            return false;
        }
    }

    private async Task ResolveHighlightOptionsAndMashupHair(ModpackData data)
    {
        // This needs to scan the entire set of options first, rip all the mtrls,
        // and rip their Normal/Mask pairs.
        var mData = new List<(string Normal, string Mask)>();
        var hairMaterials = new HashSet<string>();

        foreach (var o in data.FileOptions)
        {
            foreach (var f in o.Files!)
            {
                if (!f.Key.EndsWith(".mtrl", StringComparison.Ordinal)) continue;
                try
                {
                    var raw = f.Value.Read();
                    XivMtrl mtrl;
                    try
                    {
                        mtrl = Mtrl.GetXivMtrl(raw, f.Key);
                    }
                    catch
                    {
                        continue;
                    }

                    if (mtrl.ShaderPack != EShaderPack.Hair) continue;

                    var norm = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerNormal);
                    var mask = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerMask);

                    if (norm == null || mask == null) continue;
                    hairMaterials.Add(f.Key);
                    mData.Add((norm.Dx11Path, mask.Dx11Path));
                }
                catch
                {
                    continue;
                }
            }
        }

        if (mData.Count == 0)
        {
            return;
        }

        // Construct the list of bad options and relevant texture containing options.
        var containers = new Dictionary<string, List<ModOption>>();
        var badOptions = new List<ModOption>();
        foreach (var o in data.FileOptions)
        {
            foreach (var pair in mData)
            {
                var hasMask = o.Files!.ContainsKey(pair.Mask);
                var hasNorm = o.Files.ContainsKey(pair.Normal);

                if (hasNorm)
                {
                    if (!containers.ContainsKey(pair.Normal))
                    {
                        containers.Add(pair.Normal, new List<ModOption>());
                    }
                    containers[pair.Normal].Add(o);
                }
                if (hasMask)
                {
                    if (!containers.ContainsKey(pair.Mask))
                    {
                        containers.Add(pair.Mask, new List<ModOption>());
                    }
                    containers[pair.Mask].Add(o);
                }

                if (hasMask && hasNorm) continue;
                if (!hasMask && !hasNorm) continue;
                badOptions.Add(o);
            }
        }

        if (badOptions.Count == 0)
        {
            if (containers.Count == 0)
            {
                // This is a material-only Mashup hair.
                // We can typically fix these via material repathing.
                RepathHairMashups(data);
            }
            return;
        }

        // Resolve if there is only one container for the missing item or not.
        foreach (var o in badOptions)
        {
            foreach (var pair in mData)
            {
                var hasMask = o.Files!.ContainsKey(pair.Mask);
                var hasNorm = o.Files.ContainsKey(pair.Normal);

                var missingTex = hasMask ? pair.Normal : pair.Mask;

                if (containers[missingTex].Count != 1)
                {
                    throw new InvalidDataException("Cannot upgrade modpack - Highlight/Visibility options are unresolveable either due to missing files or too much complexity.\nTry installing the modpack and creating an updated pack from the desired options.");
                }

                // If there is only one exact source, staple in the copy to this option.
                var file = containers[missingTex][0].Files![missingTex];
                o.Files.Add(missingTex, file);
            }
        }

        await Task.CompletedTask;
    }

    private void RepathHairMashups(ModpackData data)
    {
        RepathHairMashups(data, new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/hair.*\\.mtrl"));
        RepathHairMashups(data, new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/zear.*\\.mtrl"));
        RepathHairMashups(data, new Regex("chara\\/human\\/c[0-9]{4}\\/obj\\/tail.*\\.mtrl"));
    }

    private void RepathHairMashups(ModpackData data, Regex mtrlRegex)
    {
        foreach (var o in data.FileOptions)
        {
            var files = new Dictionary<string, FileSource>(o.Files!, o.Files!.Comparer);
            foreach (var f in files)
            {
                if (!mtrlRegex.IsMatch(f.Key)) continue;

                var m = f.Key;
                var raw = files[m].Read();
                var mtrl = Mtrl.GetXivMtrl(raw, m);

                if (mtrl.ShaderPack != EShaderPack.Hair && mtrl.ShaderPack != EShaderPack.Character)
                {
                    continue;
                }

                var norm = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerNormal);
                var mask = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerMask);
                var diff = mtrl.Textures.FirstOrDefault(x => x.Sampler!.SamplerId == ESamplerId.g_SamplerDiffuse);

                if (norm == null || mask == null) continue;
                var nPath = norm.Dx11Path;
                var mPath = mask.Dx11Path;

                if (!game.FileExists(nPath))
                {
                    var newPath = nPath.Replace("_n.tex", "_norm.tex").Replace("--", "");
                    if (game.FileExists(newPath))
                    {
                        norm.TexturePath = norm.TexturePath.Replace("_n.tex", "_norm.tex").Replace("--", "");
                    }
                }

                if (!game.FileExists(mPath))
                {
                    var newPath = mPath.Replace("_m.tex", "_mask.tex").Replace("--", "");
                    var found = false;
                    if (game.FileExists(newPath) && !found)
                    {
                        mask.TexturePath = mask.TexturePath.Replace("_m.tex", "_mask.tex").Replace("--", "");
                        found = true;
                    }

                    newPath = mPath.Replace("_m.tex", "_mult.tex").Replace("--", "");
                    if (game.FileExists(newPath) && !found)
                    {
                        mask.TexturePath = mask.TexturePath.Replace("_m.tex", "_mult.tex").Replace("--", "");
                        found = true;
                    }

                    newPath = mPath.Replace("_s.tex", "_mask.tex").Replace("--", "");
                    if (game.FileExists(newPath) && !found)
                    {
                        mask.TexturePath = mask.TexturePath.Replace("_s.tex", "_mask.tex").Replace("--", "");
                        found = true;
                    }

                    newPath = mPath.Replace("_s.tex", "_mult.tex").Replace("--", "");
                    if (game.FileExists(newPath) && !found)
                    {
                        mask.TexturePath = mask.TexturePath.Replace("_s.tex", "_mult.tex").Replace("--", "");
                        found = true;
                    }
                }

                if (diff != null && !game.FileExists(diff.Dx11Path))
                {
                    var dPath = diff.Dx11Path;
                    var newPath = dPath.Replace("_d.tex", "_base.tex").Replace("--", "");
                    if (game.FileExists(newPath))
                    {
                        diff.TexturePath = diff.TexturePath.Replace("_d.tex", "_base.tex").Replace("--", "");
                    }
                }

                o.Files[m] = new MemoryFileSource(Mtrl.XivMtrlToUncompressedMtrl(mtrl));
            }
        }
    }

    public static void UpdateSkinPaths(Dictionary<string, FileSource> files)
    {
        var clone = new Dictionary<string, FileSource>(files, files.Comparer);
        foreach (var fkv in clone)
        {
            var file = fkv.Key;
            if (SkinRepathDict.ContainsKey(file))
            {
                var target = SkinRepathDict[file];
                if (files.ContainsKey(target)) continue;

                // Duplicate the pointer.
                files.Add(target, fkv.Value);
            }
        }
    }
}
