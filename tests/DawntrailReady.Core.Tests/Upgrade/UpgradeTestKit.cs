using DawntrailReady.Core.Game;
using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Textures;
using DawntrailReady.Core.Upgrade;

namespace DawntrailReady.Core.Tests.Upgrade;

/// <summary>The game's own files, in memory. Always a Dawntrail install unless told otherwise.</summary>
internal sealed class FakeGame : IGameData
{
    public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);

    public FakeGame(bool dawntrail = true)
    {
        if (dawntrail) Files[EndwalkerUpgrade.DawntrailTestFile] = [1];
    }

    public bool FileExists(string gamePath) => Files.ContainsKey(gamePath);
    public byte[]? ReadFile(string gamePath) => Files.TryGetValue(gamePath, out var d) ? d : null;
}

internal static class Kit
{
    public const string GearMtrl = "chara/equipment/e0001/material/v0001/mt_c0101e0001_top_a.mtrl";
    public const string GearNormal = "chara/equipment/e0001/texture/v01_c0101e0001_top_n.tex";
    public const string GearIndex = "chara/equipment/e0001/texture/v01_c0101e0001_top_id.tex";
    public const string GearSpec = "chara/equipment/e0001/texture/v01_c0101e0001_top_s.tex";
    public const string GearDiffuse = "chara/equipment/e0001/texture/v01_c0101e0001_top_d.tex";
    public const string GearMask = "chara/equipment/e0001/texture/v01_c0101e0001_top_m.tex";
    public const string GlassSample = "chara/equipment/e5001/material/v0001/mt_c0101e5001_met_b.mtrl";
    public const string HairSample = "chara/human/c0801/obj/hair/h0115/material/v0001/mt_c0801h0115_hir_a.mtrl";

    public static MtrlTexture Texture(string path, ESamplerId sampler, uint settings = 0x000F8340) =>
        new() { TexturePath = path, Sampler = new TextureSampler { SamplerIdRaw = (uint)sampler, SamplerSettingsRaw = settings } };

    /// <summary>Old 16-row table whose half at index k holds k, so every moved field can be traced.</summary>
    public static List<Half> LegacyColorset() => Enumerable.Range(0, 256).Select(i => (Half)i).ToList();

    public static List<Half> DawntrailColorset() => Enumerable.Range(0, 1024).Select(i => (Half)(i % 64)).ToList();

    /// <summary>Old dye table: 16 ushorts, template in the high 11 bits, flags in the low 5.</summary>
    public static byte[] LegacyDye(int row0Template, int row0Bits)
    {
        var dye = new byte[32];
        BitConverter.GetBytes((ushort)((row0Template << 5) | row0Bits)).CopyTo(dye, 0);
        return dye;
    }

    public static XivMtrl Material(string path, string shpk, List<Half> colorset, byte[] dye, params MtrlTexture[] textures)
    {
        var m = new XivMtrl
        {
            MTRLPath = path,
            ShaderPackRaw = shpk,
            AdditionalData = [0x0C, 0, 0, 0],
            ColorSetData = colorset,
            ColorSetDyeData = dye,
            UvMapStrings = [new MtrlString { Value = "map1" }],
            ColorsetStrings = [new MtrlString { Value = "colorSet1" }],
        };
        m.Textures = textures.ToList();
        return m;
    }

    public static byte[] Bytes(XivMtrl m) => Mtrl.XivMtrlToUncompressedMtrl(m);

    public static XivMtrl Parse(FileSource source, string path) => Mtrl.GetXivMtrl(source.Read(), path);

    /// <summary>An uncompressed A8R8G8B8 .tex (BGRA in memory), one mip, every pixel the same RGBA colour.</summary>
    public static byte[] SolidTex(int w, int h, byte r, byte g, byte b, byte a)
    {
        var data = new byte[w * h * 4];
        for (var o = 0; o < data.Length; o += 4)
        {
            data[o] = b; data[o + 1] = g; data[o + 2] = r; data[o + 3] = a;
        }
        return [.. Tex.CreateTexFileHeader(XivTexFormat.A8R8G8B8, w, h, 1), .. data];
    }

    public static async Task<(int Width, int Height, byte[] Rgba)> Pixels(FileSource source)
    {
        var tex = XivTex.FromUncompressedTex(source.Read());
        return (tex.Width, tex.Height, await tex.GetRawPixels());
    }

    public static ModOption Option(string group, string name, params (string Path, byte[] Data)[] files)
    {
        var dict = new Dictionary<string, FileSource>(StringComparer.Ordinal);
        foreach (var (path, data) in files) dict[path] = new MemoryFileSource(data);
        return new ModOption { GroupName = group, OptionName = name, Key = $"{group}/{name}", Files = dict };
    }

    public static ModpackData Pack(params ModOption[] options)
    {
        var data = new ModpackData { Name = "Test" };
        data.Options.AddRange(options);
        return data;
    }

    public static Task<UpgradeResult> RunUpgrade(FakeGame game, ModpackData data) => new ModpackUpgrader(game).UpgradeModpack(data);
}
