// Port of TexTools xivModdingFramework/Mods/EndwalkerUpgrade.cs (commit 25b3ae72): the modpack route only
// (every call with a `files` dictionary, `tx == null`). The transaction route that writes into TexTools' own
// game index (Dat.WriteModFile, Dat.CopyFile, Mtrl.ImportMtrl) has no equivalent here and is not ported.
// Split into partial files: this one (shared helpers, materials, models), EndwalkerUpgrade.Textures.cs,
// EndwalkerUpgrade.Partials.cs.
using System.Text.RegularExpressions;
using DawntrailReady.Core.Game;
using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Models;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Textures;
using static DawntrailReady.Core.Materials.ShaderHelpers;

namespace DawntrailReady.Core.Upgrade;

/// <summary>
/// Endwalker to Dawntrail file upgrades, exactly as TexTools performs them on a modpack. Vanilla game files
/// (TexTools' read-only transaction) come from <see cref="IGameData"/>. Exceptions TexTools writes to Trace and
/// swallows are passed to <paramref name="trace"/> and swallowed the same way.
/// </summary>
public sealed partial class EndwalkerUpgrade(IGameData game, Action<string>? trace = null)
{
    /// <summary>Enum representing the type of upgrade a given texture needs to go through.</summary>
    public enum EUpgradeTextureUsage
    {
        IndexMaps,
        GearMaskLegacy,
        GearMaskNew,
        HairMaps,
    };

    public struct UpgradeInfo
    {
        public EUpgradeTextureUsage Usage;
        public Dictionary<string, string> Files;
    }

    private const string _SampleHair = "chara/human/c0801/obj/hair/h0115/material/v0001/mt_c0801h0115_hir_a.mtrl";

    /// <summary>TexTools Eqp.DawntrailTestFile.</summary>
    public const string DawntrailTestFile = "chara/xls/charadb/equipmentdeformerparameter/c1601.eqdp";

    /// <summary>
    /// XivCache.FrameworkSettings.DefaultTextureFormat. The framework default is A8R8G8B8 (XivCache.cs:68);
    /// the TexTools app can change it in its settings, which cannot be mirrored here.
    /// </summary>
    public const XivTexFormat DefaultTextureFormat = XivTexFormat.A8R8G8B8;

    private static readonly Regex FixableMdlsRegex = new("chara\\/.*\\.mdl");
    private static readonly Regex FixableMtrlsRegex = new("chara\\/.*\\.mtrl");

    private readonly IGameData game = game;

    private void Trace(Exception ex) => trace?.Invoke($"{ex.GetType().Name}: {ex.Message}");

    public void AssertIsDawntrail()
    {
        if (!game.FileExists(DawntrailTestFile))
            throw new InvalidDataException("The currently set FFXIV Directory is not a Dawntrail install.");
    }

    /// <summary>
    /// Performs Endwalker => Dawntrail Upgrades on the files of one modpack option.
    /// Returns a collection of file upgrade information to be used for texture upgrades.
    /// NOTE: Does not run texture upgrades in this pass for Materials.
    /// </summary>
    public async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerFiles(Dictionary<string, FileSource> files)
    {
        AssertIsDawntrail();
        var ret = new Dictionary<string, UpgradeInfo>();

        var _ConvertedTextures = new HashSet<string>();
        var filePaths = files.Keys;

        var fixableMdls = filePaths.Where(x => FixableMdlsRegex.Match(x).Success).ToList();
        var fixableMtrls = filePaths.Where(x => FixableMtrlsRegex.Match(x).Success).ToList();

        ret = await UpdateEndwalkerMaterials(fixableMtrls, _ConvertedTextures, files);

        foreach (var path in fixableMdls)
        {
            await UpdateEndwalkerModel(path, files);
        }

        // Texture-only upgrades are ignored in this route, and handled by the TT modpack upgrader afterwards.
        return ret;
    }

    public async Task UpdateEndwalkerModel(string path, Dictionary<string, FileSource> files)
    {
        var uncomp = ResolveFile(path, files);
        if (uncomp == null)
        {
            return;
        }

        var anyChanges = EndwalkerModel.FastMdlv6Upgrade(uncomp);
        if (!anyChanges)
        {
            return;
        }

        WriteFile(uncomp, path, files);
        await Task.CompletedTask;
    }

    private async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerMaterials(List<string> paths, HashSet<string> _ConvertedTextures, Dictionary<string, FileSource> files)
    {
        var ret = new Dictionary<string, UpgradeInfo>();
        var materials = new List<XivMtrl>();
        foreach (var path in paths)
        {
            var file = ResolveFile(path, files);
            if (file == null)
            {
                continue;
            }

            try
            {
                var mtrl = Mtrl.GetXivMtrl(file, path);
                if (!DoesMtrlNeedDawntrailUpdate(mtrl))
                {
                    continue;
                }

                materials.Add(mtrl);
            }
            catch
            {
                continue;
            }
        }

        foreach (var mtrl in materials)
        {
            try
            {
                var missingFiles = await UpdateEndwalkerMaterial(mtrl, true, _ConvertedTextures, files);

                // Merge missing files in.
                foreach (var kv in missingFiles)
                {
                    if (!ret.ContainsKey(kv.Key))
                    {
                        ret.Add(kv.Key, kv.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                // No-Op
                Trace(ex);
            }
        }

        return ret;
    }

    private const uint _OldShaderConstant1 = 0x36080AD0; // == 1
    private const uint _OldShaderConstant2 = 0x992869AB; // == 3 (skin) or 4 (hair)

    public static bool DoesMtrlNeedDawntrailUpdate(XivMtrl mtrl)
    {
        if (mtrl.ColorSetData != null && mtrl.ColorSetData.Count == 256)
        {
            // Any old colorset, regardless of shader needs to be updated.
            return true;
        }

        if (mtrl.ShaderPack == EShaderPack.Hair)
        {
            if (mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant1)
                && mtrl.ShaderConstants.Any(x => x.ConstantId == _OldShaderConstant2))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerMaterial(XivMtrl mtrl, bool createTextures, HashSet<string>? _ConvertedTextures, Dictionary<string, FileSource> files)
    {
        var ret = new Dictionary<string, UpgradeInfo>();

        if (!createTextures)
        {
            // Should we really allow this?
            throw new NotImplementedException();
        }

        if (!DoesMtrlNeedDawntrailUpdate(mtrl))
        {
            return ret;
        }

        return await UpdateEndwalkerMaterialFiles(mtrl, _ConvertedTextures, files);
    }

    /// <summary>Updates an individual material as part of a modpack (TexTools' private overload, files route).</summary>
    private async Task<Dictionary<string, UpgradeInfo>> UpdateEndwalkerMaterialFiles(XivMtrl mtrl, HashSet<string>? _ConvertedTextures, Dictionary<string, FileSource> files)
    {
        var ret = new Dictionary<string, UpgradeInfo>();
        _ConvertedTextures ??= new HashSet<string>();

        if (mtrl.ColorSetDataSize > 0)
        {
            var texInfo = await UpdateEndwalkerColorset(mtrl, files);

            // (files == null only: textures are created here. On the modpack route they are created afterwards
            // by UpgradeRemainingTextures, per option.)

            foreach (var kv in texInfo)
            {
                ret.Add(kv.Key, kv.Value);
            }
        }
        else if (mtrl.ShaderPack == EShaderPack.Hair)
        {
            ret = await UpdateEndwalkerHairMaterial(mtrl, _ConvertedTextures, files);
        }

        return ret;
    }

    /// <summary>
    /// Handles the boilerplate of resolving a file from the option's file list. Returns null when it is not
    /// there or cannot be read. Every Read() is a private copy, so callers may edit the array in place.
    /// </summary>
    private static byte[]? ResolveFile(string path, Dictionary<string, FileSource>? files)
    {
        if (files != null && files.TryGetValue(path, out var source))
        {
            try
            {
                return source.Read();
            }
            catch
            {
                return null;
            }
        }

        // tx is always null on the modpack route.
        return null;
    }

    private static bool Exists(string path, Dictionary<string, FileSource>? files) => files != null && files.ContainsKey(path);

    /// <summary>Replaces or adds the option's entry for this game path with the new bytes.</summary>
    private static void WriteFile(byte[]? uncompData, string path, Dictionary<string, FileSource> files)
    {
        ArgumentNullException.ThrowIfNull(uncompData); // TexTools dereferences uncompData.Length here.
        files[path] = new MemoryFileSource(uncompData);
    }

    /// <summary>TexTools Mtrl.GetXivMtrl(path, forceOriginal: true, rtx): null when the game has no such file.</summary>
    private XivMtrl? GetVanillaMtrl(string path)
    {
        if (!game.FileExists(path))
        {
            return null;
        }
        return Mtrl.GetXivMtrl(game.ReadFile(path)!, path);
    }

    /// <summary>TexTools Tex.GetXivTex(path, forceOriginal: true, rtx).</summary>
    private XivTex GetVanillaTex(string path)
    {
        if (!game.FileExists(path))
        {
            throw new FileNotFoundException($"Could not find offset for {path}");
        }

        XivTex xivTex;
        try
        {
            xivTex = XivTex.FromUncompressedTex(game.ReadFile(path)!);
        }
        catch (Exception)
        {
            throw new Exception($"There was an error reading the file: " + path);
        }

        xivTex.FilePath = path;
        return xivTex;
    }
}
