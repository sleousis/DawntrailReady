using System.Text.RegularExpressions;
using DawntrailReady.Core.Game;
using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Upgrade;

namespace DawntrailReady.Core.Service;

/// <summary>What in a mod was made for the game before Dawntrail (7.0) and no longer works there.</summary>
public sealed record PreDawntrailReport(int LegacyMaterials, int OldModels, int OldTextures, int BrokenHairMaterials)
{
    public bool Any => LegacyMaterials + OldModels + OldTextures + BrokenHairMaterials > 0;
}

/// <summary>
/// The "is this mod Dawntrail-compatible?" check. A mod is upgraded only when it holds at least one file that no
/// longer works in Dawntrail; TexTools' upgrade then runs on it unchanged. Mods without such a file are left alone,
/// even where TexTools' upgrade would still rearrange them (for example stapling textures into hair highlight
/// options, or refusing a complex hair mod outright).
/// Reads only material files (small) and two bytes of each model; textures are judged by their paths.
/// </summary>
public static class PreDawntrailDetector
{
    private static readonly Regex CharaMdl = new("chara\\/.*\\.mdl");
    private static readonly Regex CharaMtrl = new("chara\\/.*\\.mtrl");
    private static readonly Regex[] MashupMaterials =
    [
        new("chara\\/human\\/c[0-9]{4}\\/obj\\/hair.*\\.mtrl"),
        new("chara\\/human\\/c[0-9]{4}\\/obj\\/zear.*\\.mtrl"),
        new("chara\\/human\\/c[0-9]{4}\\/obj\\/tail.*\\.mtrl"),
    ];

    public static PreDawntrailReport Inspect(ModpackData data, IGameData game)
    {
        var legacyMaterials = new HashSet<FileSource>(ReferenceEqualityComparer.Instance);
        var oldModels = new HashSet<FileSource>(ReferenceEqualityComparer.Instance);
        var oldTextures = new HashSet<FileSource>(ReferenceEqualityComparer.Instance);
        var hairPairs = new List<(string Normal, string Mask)>();
        var mashupCandidates = new List<XivMtrl>();
        var irisCache = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var option in data.FileOptions)
        {
            var files = option.Files!;
            var referenced = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (path, source) in files)
            {
                if (path.EndsWith(".mdl", StringComparison.Ordinal))
                {
                    if (CharaMdl.IsMatch(path) && IsModelV5(source)) oldModels.Add(source);
                    continue;
                }
                if (!path.EndsWith(".mtrl", StringComparison.Ordinal)) continue;

                var mtrl = TryReadMaterial(source, path);
                if (mtrl is null) continue;
                if (CharaMtrl.IsMatch(path) && EndwalkerUpgrade.DoesMtrlNeedDawntrailUpdate(mtrl)) legacyMaterials.Add(source);

                foreach (var tex in mtrl.Textures)
                {
                    referenced.Add(tex.TexturePath);
                    if (TryDx11(tex) is { } dx11) referenced.Add(dx11);
                }

                if (mtrl.ShaderPack == EShaderPack.Hair
                    && Sampler(mtrl, ESamplerId.g_SamplerNormal) is { } norm && Sampler(mtrl, ESamplerId.g_SamplerMask) is { } mask
                    && TryDx11(norm) is { } normPath && TryDx11(mask) is { } maskPath)
                    hairPairs.Add((normPath, maskPath));
                if (MashupMaterials.Any(r => r.IsMatch(path))) mashupCandidates.Add(mtrl);
            }

            foreach (var (path, source) in files)
            {
                if (!path.EndsWith(".tex", StringComparison.Ordinal) || referenced.Contains(path)) continue;

                // Old skin texture paths nothing reads any more: TexTools gives them their new path.
                if (EndwalkerUpgrade.SkinRepathDict.TryGetValue(path, out var target) && !files.ContainsKey(target)) oldTextures.Add(source);
                // Old iris masks: Dawntrail irises use a diffuse texture instead. A mod that already ships that diffuse
                // keeps the old mask only for older setups, and is Dawntrail-ready.
                else if (EndwalkerUpgrade.EyeMaskPathRegex.IsMatch(path) && !IrisDiffuseProvided(path, files, game, irisCache)) oldTextures.Add(source);
            }

            CountUnclaimed(EndwalkerUpgrade.HairRegexes, files, referenced, game, needsBoth: true, oldTextures);
            CountUnclaimed(EndwalkerUpgrade.TailRegexes, files, referenced, game, needsBoth: true, oldTextures);
            CountUnclaimed(EndwalkerUpgrade.EarRegexes, files, referenced, game, needsBoth: true, oldTextures);
            CountUnclaimed(EndwalkerUpgrade.AccessoryRegexes, files, referenced, game, needsBoth: false, oldTextures);
        }

        var brokenHair = 0;
        if (hairPairs.Count > 0 && !data.FileOptions.Any(o => hairPairs.Any(p => o.Files!.ContainsKey(p.Normal) || o.Files.ContainsKey(p.Mask))))
            brokenHair = mashupCandidates.Count(m => PointsAtRemovedTextures(m, game));

        return new PreDawntrailReport(legacyMaterials.Count, oldModels.Count, oldTextures.Count, brokenHair);
    }

    /// <summary>
    /// Old-style hair, tail, ear or accessory textures shipped without their material: in Dawntrail the game's own
    /// material reads different paths, so these do nothing until TexTools copies them over.
    /// </summary>
    private static void CountUnclaimed(EndwalkerUpgrade.HairRegexSet set, Dictionary<string, FileSource> files, HashSet<string> referenced,
        IGameData game, bool needsBoth, HashSet<FileSource> into)
    {
        var groups = new Dictionary<(string Race, string Id), List<(string Kind, FileSource Source)>>();
        var withMaterial = new HashSet<(string, string)>();
        foreach (var (path, source) in files)
        {
            var mat = set.MaterialRegex.Match(path);
            if (mat.Success) { withMaterial.Add((mat.Groups[1].Value, mat.Groups[2].Value)); continue; }
            if (referenced.Contains(path)) continue;
            var m = set.OldTextureRegex.Match(path);
            if (!m.Success) continue;
            var key = (m.Groups[1].Value, m.Groups[2].Value);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add((m.Groups[3].Value, source));
        }

        foreach (var (key, list) in groups)
        {
            if (withMaterial.Contains(key)) continue;
            if (needsBoth && list.Select(x => x.Kind == "n").Distinct().Count() < 2) continue;

            // The game's own Dawntrail material these textures would be copied under.
            var matPath = string.Format(set.MaterialFormat, key.Race, key.Id);
            if (!game.FileExists(matPath)) continue;
            XivMtrl? vanilla;
            try { vanilla = Mtrl.GetXivMtrl(game.ReadFile(matPath)!, matPath); }
            catch (Exception) { continue; }
            var packOk = needsBoth
                ? vanilla.ShaderPack == EShaderPack.Hair
                : vanilla.ShaderPack is EShaderPack.Character or EShaderPack.CharacterLegacy;
            if (!packOk) continue;

            // Like TexTools: when the mod already has the Dawntrail paths, these old textures were already converted.
            var targets = new[] { ESamplerId.g_SamplerNormal, ESamplerId.g_SamplerMask, ESamplerId.g_SamplerDiffuse }
                .Select(id => Sampler(vanilla, id)).OfType<MtrlTexture>().Select(TryDx11).OfType<string>();
            if (targets.Any(files.ContainsKey)) continue;

            foreach (var (_, source) in list) into.Add(source);
        }
    }

    /// <summary>
    /// Whether the mod already ships the Dawntrail iris texture that TexTools would make from this old mask: the
    /// diffuse texture of the game's own iris material for that race and face. True as well when TexTools could not
    /// convert the mask at all (no such material, or one without a diffuse).
    /// </summary>
    private static bool IrisDiffuseProvided(string maskPath, Dictionary<string, FileSource> files, IGameData game, Dictionary<string, string?> cache)
    {
        var face = FaceRegex.Match(Path.GetFileName(maskPath));
        if (!face.Success) return true;
        var race = EndwalkerUpgrade.GetRaceCodeFromPath(maskPath);
        var id = face.Groups[1].Value;
        var irisPath = $"chara/human/c{race}/obj/face/f{id}/material/mt_c{race}f{id}_iri_a.mtrl";

        if (!cache.TryGetValue(irisPath, out var diffuse))
        {
            diffuse = null;
            try
            {
                if (game.FileExists(irisPath) && game.ReadFile(irisPath) is { } bytes)
                    diffuse = Sampler(Mtrl.GetXivMtrl(bytes, irisPath), ESamplerId.g_SamplerDiffuse)?.TexturePath;
            }
            catch (Exception)
            {
                diffuse = null;
            }
            cache[irisPath] = diffuse;
        }
        return diffuse is null || files.ContainsKey(diffuse);
    }

    private static readonly Regex FaceRegex = new("f([0-9]{4})");

    /// <summary>A hair, ear or tail material whose textures were removed from the game in Dawntrail, where TexTools repaths them.</summary>
    private static bool PointsAtRemovedTextures(XivMtrl mtrl, IGameData game)
    {
        if (mtrl.ShaderPack != EShaderPack.Hair && mtrl.ShaderPack != EShaderPack.Character) return false;
        if (Sampler(mtrl, ESamplerId.g_SamplerNormal) is not { } norm || Sampler(mtrl, ESamplerId.g_SamplerMask) is not { } mask) return false;
        if (TryDx11(norm) is not { } n || TryDx11(mask) is not { } m) return false;

        if (!game.FileExists(n) && game.FileExists(n.Replace("_n.tex", "_norm.tex").Replace("--", ""))) return true;
        if (!game.FileExists(m) && new[] { ("_m.tex", "_mask.tex"), ("_m.tex", "_mult.tex"), ("_s.tex", "_mask.tex"), ("_s.tex", "_mult.tex") }
                .Any(r => game.FileExists(m.Replace(r.Item1, r.Item2).Replace("--", ""))))
            return true;
        return Sampler(mtrl, ESamplerId.g_SamplerDiffuse) is { } diff && TryDx11(diff) is { } d
            && !game.FileExists(d) && game.FileExists(d.Replace("_d.tex", "_base.tex").Replace("--", ""));
    }

    private static MtrlTexture? Sampler(XivMtrl mtrl, ESamplerId id) =>
        mtrl.Textures.FirstOrDefault(x => x.Sampler != null && x.Sampler.SamplerId == id);

    /// <summary>TexTools' Dx11Path throws on a path without a folder; such a texture simply isn't matched.</summary>
    private static string? TryDx11(MtrlTexture tex)
    {
        try { return tex.Dx11Path; }
        catch (Exception) { return null; }
    }

    private static XivMtrl? TryReadMaterial(FileSource source, string path)
    {
        try { return Mtrl.GetXivMtrl(source.Read(), path); }
        catch (Exception) { return null; }
    }

    private static bool IsModelV5(FileSource source)
    {
        try
        {
            if (source is DiskFileSource disk)
            {
                using var fs = File.OpenRead(disk.FullPath);
                Span<byte> head = stackalloc byte[2];
                return fs.Read(head) == 2 && BitConverter.ToUInt16(head) == 5;
            }
            var data = source.Read();
            return data.Length >= 2 && BitConverter.ToUInt16(data, 0) == 5;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
