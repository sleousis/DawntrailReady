using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using DawntrailReady.Core.Packaging;
using static DawntrailReady.Core.Tests.Packaging.PackagingTestUtil;

namespace DawntrailReady.Core.Tests.Packaging;

public class PmpTests
{
    private static ModOption Opt(string group, string name, params (string Path, string Data)[] files) => new()
    {
        GroupName = group,
        OptionName = name,
        Files = files.ToDictionary(f => f.Path, f => (FileSource)new MemoryFileSource(B(f.Data)), StringComparer.Ordinal),
    };

    [Fact]
    public void WritePmp_Round_Trips_Option_File_Sets_With_Deduplication()
    {
        using var dir = new TempDir();
        var data = new ModpackData { Name = "Pack" };
        data.Options.Add(Opt("Default", "Default", ("chara/d.tex", "D")));
        data.Options.Add(Opt("Colors", "Red", ("chara/m.mtrl", "RED"), ("chara/common.tex", "C")));
        data.Options.Add(Opt("Colors", "Blue", ("chara/m.mtrl", "BLUE"), ("chara/common.tex", "C")));
        data.Options.Add(Opt("Extras", "Hat", ("chara/h.mdl", "H")));
        var meta = new PmpMeta { Name = "Pack", Author = "Me", Version = "1.2.3" };
        meta.Groups["Extras"] = new PmpGroupMeta { Type = "Multi", DefaultSettings = 1 };
        var pmp = dir.Combine("out.pmp");

        ModpackFiles.WritePmp(data, meta, pmp);

        using (var zip = ZipFile.OpenRead(pmp))
        {
            var names = zip.Entries.Select(x => x.FullName).ToList();
            Assert.Contains("meta.json", names);
            Assert.Contains("default_mod.json", names);
            Assert.Contains("group_001_colors.json", names);
            Assert.Contains("group_002_extras.json", names);
            Assert.Contains("common/1/common.tex", names);
            Assert.Contains("colors/red/chara/m.mtrl", names);
            Assert.Contains("extras/chara/h.mdl", names);
            Assert.Single(names, n => n.EndsWith("common.tex"));

            using var s = zip.GetEntry("group_002_extras.json")!.Open();
            var g = JsonNode.Parse(s)!;
            Assert.Equal(("Multi", 1UL), (g["Type"]!.GetValue<string>(), g["DefaultSettings"]!.GetValue<ulong>()));
            using var m = zip.GetEntry("meta.json")!.Open();
            Assert.Equal(3, JsonNode.Parse(m)!["FileVersion"]!.GetValue<int>());
        }

        var folder = ModpackFiles.ExtractPmp(pmp, dir.Combine("x"));
        var back = PenumbraModFolder.Read(folder);

        Assert.Equal(data.Options.Select(o => $"{o.GroupName}/{o.OptionName}"), back.Options.Select(o => $"{o.GroupName}/{o.OptionName}"));
        for (var i = 0; i < data.Options.Count; i++) Assert.Equal(Contents(data.Options[i]), Contents(back.Options[i]));
        Assert.Equal(@"common\1\common.tex", ((DiskFileSource)back.Options[1].Files!["chara/common.tex"]).RelativePath);
    }

    [Fact]
    public void Ttmp_To_Pmp_Keeps_The_Default_Option_Files()
    {
        using var dir = new TempDir();
        var (mpd, e) = Mpd(B("A"), B("B"));
        var mpl = JsonSerializer.Serialize(new
        {
            TTMPVersion = "2.1s", Name = "S", Author = "Me", Version = "1.0",
            SimpleModsList = new[]
            {
                new { FullPath = "chara/a.tex", ModOffset = e[0].Offset, ModSize = e[0].Size },
                new { FullPath = "chara/b.mdl", ModOffset = e[1].Offset, ModSize = e[1].Size },
            },
        });
        var ttmp = dir.Combine("s.ttmp2");
        Zip(ttmp, ("TTMPL.mpl", B(mpl)), ("TTMPD.mpd", mpd));

        using var pack = ModpackFiles.ReadTtmp(ttmp);
        ModpackFiles.WritePmp(pack.Data, pack.ToPmpMeta(), dir.Combine("s.pmp"));
        var back = PenumbraModFolder.Read(ModpackFiles.ExtractPmp(dir.Combine("s.pmp"), dir.Combine("x")));

        var opt = Assert.Single(back.Options);
        Assert.Equal(("Default", "Default"), (opt.GroupName, opt.OptionName));
        Assert.Equal(Contents(pack.Data.Options[0]), Contents(opt));
    }

    [Fact]
    public void ExtractPmp_Refuses_Zip_Slip_Before_Extracting_Anything()
    {
        using var dir = new TempDir();
        var pmp = dir.Combine("evil.pmp");
        Zip(pmp, ("meta.json", B("{}")), ("../evil.txt", B("x")));

        Assert.Throws<InvalidDataException>(() => ModpackFiles.ExtractPmp(pmp, dir.Combine("out")));

        Assert.False(File.Exists(dir.Combine("evil.txt")));
        Assert.False(File.Exists(dir.Combine("out\\meta.json")));
    }

    [Fact]
    public void ExtractPmp_Uses_A_Lone_Subfolder_Holding_Meta()
    {
        using var dir = new TempDir();
        var pmp = dir.Combine("nested.pmp");
        Zip(pmp, ("Inner/meta.json", B("{}")), ("Inner/a.tex", B("x")));

        var folder = ModpackFiles.ExtractPmp(pmp, dir.Combine("out"));

        Assert.Equal(Path.GetFullPath(dir.Combine("out\\Inner")), folder);
    }

    [Fact]
    public void ZipFolderAsPmp_Round_Trips_A_Folder()
    {
        using var dir = new TempDir();
        dir.File("mod\\meta.json", """{ "FileVersion": 3, "Name": "Z" }""");
        dir.File("mod\\sub\\a.mtrl", "AAA");

        ModpackFiles.ZipFolderAsPmp(dir.Combine("mod"), dir.Combine("z.pmp"));
        var folder = ModpackFiles.ExtractPmp(dir.Combine("z.pmp"), dir.Combine("x"));

        Assert.Equal(B("AAA"), File.ReadAllBytes(Path.Combine(folder, "sub", "a.mtrl")));
        Assert.False(File.Exists(dir.Combine("z.pmp.tmp")));
    }
}
