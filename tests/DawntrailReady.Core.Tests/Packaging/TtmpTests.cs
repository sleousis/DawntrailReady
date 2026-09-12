using System.Text.Json;
using DawntrailReady.Core.Packaging;
using static DawntrailReady.Core.Tests.Packaging.PackagingTestUtil;

namespace DawntrailReady.Core.Tests.Packaging;

public class TtmpTests
{
    /// <summary>A 4x4 A8R8G8B8 .tex, one mip. <paramref name="mip0Offset"/> 80 is correct; anything else is "broken".</summary>
    internal static byte[] Tex(uint mip0Offset = 80, int trailingZeros = 0)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(0u);                 // attributes
        bw.Write(5200u);              // A8R8G8B8
        bw.Write((ushort)4);
        bw.Write((ushort)4);
        bw.Write((ushort)1);          // depth
        bw.Write((byte)1);            // mip count
        bw.Write((byte)1);            // array size
        bw.Write(0u); bw.Write(0u); bw.Write(0u);
        bw.Write(mip0Offset);
        for (var i = 1; i < 13; i++) bw.Write(0u);
        for (var i = 0; i < 64; i++) bw.Write((byte)(i + 1));
        for (var i = 0; i < trailingZeros; i++) bw.Write((byte)0);
        return ms.ToArray();
    }

    private static object Mod(string path, (long Offset, int Size) e) => new { FullPath = path, ModOffset = e.Offset, ModSize = e.Size, Name = "x", Category = "y", DatFile = "040000" };

    [Fact]
    public void Simple_Ttmp2_Is_One_Default_Option()
    {
        using var dir = new TempDir();
        var (mpd, e) = Mpd(B("A-first"), B("MDL"), B("A-second"), B("META"));
        var mpl = JsonSerializer.Serialize(new
        {
            TTMPVersion = "2.1s", Name = "Simple", Author = "Me", Version = "1.2", Description = "d", Url = "u",
            SimpleModsList = new[] { Mod("chara/a.tex", e[0]), Mod("chara/b.mdl", e[1]), Mod("chara/a.tex", e[2]), Mod("chara/x.meta", e[3]) },
        });
        var path = dir.Combine("simple.ttmp2");
        Zip(path, ("TTMPL.mpl", B(mpl)), ("TTMPD.mpd", mpd));

        using var pack = ModpackFiles.ReadTtmp(path);

        Assert.Equal(EModpackType.TtmpSimple, pack.ModpackType);
        Assert.Equal(UpgradesNeeded.None, pack.UpgradesNeeded);
        Assert.Equal(("Simple", "Me", "1.2", "u"), (pack.Name, pack.Author, pack.Version, pack.Url));
        var opt = Assert.Single(pack.Data.Options);
        Assert.Equal(("Default Group", "Default Option", "default"), (opt.GroupName, opt.OptionName, opt.Key));

        // Duplicate path: last entry's data, first entry's position. .meta goes to manipulations in TexTools.
        Assert.Equal(["chara/a.tex", "chara/b.mdl"], opt.Files!.Keys);
        Assert.Equal("A-second", Contents(opt)["chara/a.tex"]);
        Assert.IsType<TtmpFileSource>(opt.Files["chara/b.mdl"]);
        Assert.Equal("chara/x.meta", Assert.Single(pack.MetaFiles[opt]).GamePath);
        Assert.Single(pack.Warnings);
    }

    [Fact]
    public void Wizard_Ttmp2_Builds_TexTools_Options_And_Fixes_Old_Textures()
    {
        using var dir = new TempDir();
        var (mpd, e) = Mpd(B("M1"), B("M2"), Tex(mip0Offset: 0, trailingZeros: 80), B("not a tex"), B("H"));
        var mpl = JsonSerializer.Serialize(new
        {
            TTMPVersion = "2.0w", Name = "Wiz", Author = "Me", Version = "1.0",
            ModPackPages = new object[]
            {
                new { PageIndex = 0, ModGroups = new object[]
                {
                    new { GroupName = "G1", SelectionType = "Single", OptionList = new object[]
                    {
                        new { Name = "O1", Description = "first", IsChecked = false, ModsJsons = new[] { Mod("chara/m.mtrl", e[0]), Mod("chara/t.tex", e[2]) } },
                        new { Name = "O2", Description = "", IsChecked = false, ModsJsons = new[] { Mod("chara/m.mtrl", e[1]), Mod("chara/bad.tex", e[3]) } },
                    } },
                    new { GroupName = "Empty", SelectionType = "Multi", OptionList = Array.Empty<object>() },
                } },
                new { PageIndex = 1, ModGroups = new object[]
                {
                    new { GroupName = "G2", SelectionType = "Multi", OptionList = new object[]
                    {
                        new { Name = "P1", IsChecked = true, ModsJsons = new[] { Mod("chara/h.mdl", e[4]) } },
                    } },
                } },
            },
        });
        var path = dir.Combine("wiz.ttmp2");
        Zip(path, ("TTMPL.mpl", B(mpl)), ("TTMPD.mpd", mpd));

        using var pack = ModpackFiles.ReadTtmp(path);

        Assert.Equal(EModpackType.TtmpWizard, pack.ModpackType);
        Assert.Equal(UpgradesNeeded.NeedsTexFix, pack.UpgradesNeeded);
        Assert.Equal(["G1/O1", "G1/O2", "G2/P1"], pack.Data.Options.Select(o => $"{o.GroupName}/{o.OptionName}"));
        Assert.Equal("page:1/group:0/option:0", pack.Data.Options[2].Key);

        // Old-pack texture fix: the broken mip offset is rewritten and the 80 trailing zero bytes dropped.
        var fixedTex = pack.Data.Options[0].Files!["chara/t.tex"].Read();
        Assert.Equal(80 + 64, fixedTex.Length);
        Assert.Equal(80u, BitConverter.ToUInt32(fixedTex, 28));
        Assert.Equal(Tex(), fixedTex);

        // A texture the fix cannot read is skipped, as TexTools does.
        Assert.Equal(["chara/m.mtrl"], pack.Data.Options[1].Files!.Keys);
        Assert.Equal("M2", Contents(pack.Data.Options[1])["chara/m.mtrl"]);
        Assert.Empty(pack.Warnings);

        // Single group with nothing checked selects its first option.
        var meta = pack.Meta;
        Assert.Equal(("Single", 0UL, 0), (meta.Groups["G1"].Type, meta.Groups["G1"].DefaultSettings, meta.Groups["G1"].Page));
        Assert.Equal(("Multi", 1UL, 1), (meta.Groups["G2"].Type, meta.Groups["G2"].DefaultSettings, meta.Groups["G2"].Page));
        Assert.Equal("first", meta.Groups["G1"].OptionDescriptions["O1"]);
    }

    [Fact]
    public void Legacy_Ttmp_Reads_Line_Json_And_Leaves_Models_With_A_Warning()
    {
        using var dir = new TempDir();
        var (mpd, e) = Mpd(B("MDL"), Tex());
        var lines = "Version 1.0\n" +
                    JsonSerializer.Serialize(Mod("chara/b.mdl", e[0])) + "\n" +
                    JsonSerializer.Serialize(Mod("chara/t.tex", e[1])) + "\n";
        var path = dir.Combine("Legacy Pack.ttmp");
        Zip(path, ("TTMPL.mpl", B(lines)), ("TTMPD.mpd", mpd));

        using var pack = ModpackFiles.ReadTtmp(path);

        Assert.Equal(EModpackType.TtmpOriginal, pack.ModpackType);
        Assert.Equal(UpgradesNeeded.NeedsTexFix | UpgradesNeeded.NeedsMdlFix, pack.UpgradesNeeded);
        Assert.Equal(("Legacy Pack", "Unknown", "0.1s"), (pack.Name, pack.Author, pack.TTMPVersion));
        var opt = Assert.Single(pack.Data.Options);
        Assert.Equal("MDL", Contents(opt)["chara/b.mdl"]);
        Assert.Equal(Tex(), opt.Files!["chara/t.tex"].Read());
        Assert.Equal("Very old TexTools model: TexTools would also rebuild it through its model importer, which isn't available here; it gets the standard Dawntrail model update only (chara/b.mdl).",
            Assert.Single(pack.Warnings));
    }

    [Fact]
    public void Dispose_Removes_The_Extracted_Mpd()
    {
        using var dir = new TempDir();
        var (mpd, e) = Mpd(B("A"));
        var mpl = JsonSerializer.Serialize(new { TTMPVersion = "2.1s", Name = "S", SimpleModsList = new[] { Mod("chara/a.tex", e[0]) } });
        var path = dir.Combine("s.ttmp2");
        Zip(path, ("TTMPL.mpl", B(mpl)), ("TTMPD.mpd", mpd));

        var pack = ModpackFiles.ReadTtmp(path);
        Assert.True(File.Exists(Path.Combine(pack.ExtractFolder, "TTMPD.mpd")));
        pack.Dispose();
        Assert.False(Directory.Exists(pack.ExtractFolder));
    }

    [Theory]
    [InlineData("1.0", UpgradesNeeded.NeedsTexFix | UpgradesNeeded.NeedsMdlFix)]
    [InlineData("2.0w", UpgradesNeeded.NeedsTexFix)]
    [InlineData("2.1s", UpgradesNeeded.None)]
    [InlineData(null, UpgradesNeeded.NeedsTexFix | UpgradesNeeded.NeedsMdlFix)]
    public void DoesModpackNeedFix_Matches_TexTools(string? version, UpgradesNeeded expected) =>
        Assert.Equal(expected, ModpackFiles.DoesModpackNeedFix(version));
}
