using DawntrailReady.Core.Game;
using DawntrailReady.Core.Packaging;
using static DawntrailReady.Core.Tests.Packaging.PackagingTestUtil;

namespace DawntrailReady.Core.Tests.Packaging;

public class PenumbraReadTests
{
    private sealed class FakeGame(params string[] files) : IGameData
    {
        public bool FileExists(string gamePath) => files.Contains(gamePath);
        public byte[]? ReadFile(string gamePath) => FileExists(gamePath) ? [1] : null;
    }

    private const string V4Meta = """
        {
        	"FileVersion": 4,
        	"Name": "Test Mod",
        	"DefaultData": {
        		"Files": { "chara/a.mtrl": "def\\a.mtrl", "not/a/game/path.x": "def\\x.x" },
        		"FileSwaps": {},
        		"Manipulations": []
        	},
        	"Groups": [
        		{ "Type": "Single", "Name": "Late", "Page": 1, "Options": [
        			{ "Name": "A", "Files": { "chara/s.tex": "shared\\x.tex" } },
        			{ "Name": "B", "Files": { "chara/b.tex": "late\\b\\chara\\b.tex" } } ] },
        		{ "Type": "Multi", "Name": "Multi", "Options": [
        			{ "Name": "C", "Priority": 1, "Files": { "chara/s.tex": "Shared/X.tex" } } ] },
        		{ "Type": "Imc", "Name": "Imc", "Options": [ { "Name": "X", "AttributeMask": 1 }, { "Name": "Y", "IsDisableSubMod": true } ] },
        		{ "Type": "Combining", "Name": "Comb", "Options": [ { "Name": "P" }, { "Name": "Q" } ],
        		  "Containers": [ { "Files": { "chara/c.tex": "c\\0.tex" } }, {}, {}, {} ] },
        		{ "Type": "Single", "Name": "Empty", "Options": [] }
        	]
        }
        """;

    [Fact]
    public void V4_Folder_Maps_Default_Then_Groups_By_Page()
    {
        using var dir = new TempDir();
        dir.File("meta.json", V4Meta);

        var data = PenumbraModFolder.Read(dir.Path);

        Assert.Equal("Test Mod", data.Name);
        Assert.Equal(
            ["Default/Default", "Multi/C", "Imc/X", "Imc/Y", "Comb/P", "Comb/Q", "Late/A", "Late/B"],
            data.Options.Select(o => $"{o.GroupName}/{o.OptionName}"));
        Assert.Equal("default", data.Options[0].Key);
        Assert.Equal("group:1/option:0", data.Options[1].Key);
        Assert.Equal("group:0/option:1", data.Options[7].Key);

        // Only game paths are imported (PMP.CanImport).
        Assert.Equal(["chara/a.mtrl"], data.Options[0].Files!.Keys);

        // Imc and Combining options have no StandardData in TexTools.
        Assert.All(data.Options.Where(o => o.GroupName is "Imc" or "Comb"), o => Assert.Null(o.Files));
        Assert.Equal(4, data.FileOptions.Count());

        // One source per distinct relative path, whatever the slashes or case; the first spelling read wins
        // (option C, page 0, is read before option A, page 1).
        var a = data.Options.Single(o => o.OptionName == "A").Files!["chara/s.tex"];
        var c = data.Options.Single(o => o.OptionName == "C").Files!["chara/s.tex"];
        Assert.Same(a, c);
        Assert.Equal("Shared/X.tex", ((DiskFileSource)a).RelativePath);
    }

    [Fact]
    public void Empty_Default_Is_Not_An_Option()
    {
        using var dir = new TempDir();
        dir.File("meta.json", """{ "FileVersion": 4, "Name": "M", "DefaultData": { "Files": {} }, "Groups": [ { "Type": "Single", "Name": "G", "Options": [ { "Name": "O" } ] } ] }""");

        var data = PenumbraModFolder.Read(dir.Path);

        var only = Assert.Single(data.Options);
        Assert.Equal("G", only.GroupName);
        Assert.Empty(only.Files!);
    }

    [Fact]
    public void V3_Folder_Reads_Default_Mod_And_Group_Files()
    {
        using var dir = new TempDir();
        dir.File("meta.json", """{ "FileVersion": 3, "Name": "Old" }""");
        dir.File("default_mod.json", [0xEF, 0xBB, 0xBF, .. B("""{ "Version": 0, "Files": { "chara/d.tex": "d.tex" }, "FileSwaps": {}, "Manipulations": [] }""")]);
        dir.File("group_001_first.json", """{ "Name": "First", "Type": "Multi", "Page": 0, "Options": [ { "Name": "F1", "Files": { "chara/f1.tex": "first\\f1\\chara\\f1.tex" } }, { "Name": "F2" } ] }""");
        dir.File("group_002_second.json", """{ "Name": "Second", "Type": "Single", "Options": [ { "Name": "S1", "Files": { "chara/s1.mdl": "second\\chara\\s1.mdl" } } ] }""");
        dir.File("notes.json", """{ "Name": "not a group" }""");

        var data = PenumbraModFolder.Read(dir.Path);

        Assert.Equal(["Default/Default", "First/F1", "First/F2", "Second/S1"], data.Options.Select(o => $"{o.GroupName}/{o.OptionName}"));
        Assert.Equal("group:group_001_first.json/option:1", data.Options[2].Key);
        Assert.Equal(["chara/d.tex"], data.Options[0].Files!.Keys);
        Assert.Equal(["chara/f1.tex"], data.Options[1].Files!.Keys);
        Assert.Empty(data.Options[2].Files!);
        Assert.Equal("second\\chara\\s1.mdl", ((DiskFileSource)data.Options[3].Files!["chara/s1.mdl"]).RelativePath);
    }

    [Fact]
    public void Traversal_Paths_Are_Refused()
    {
        using var dir = new TempDir();
        dir.File("meta.json", """{ "FileVersion": 4, "Name": "M", "DefaultData": { "Files": { "chara/a.tex": "..\\..\\evil.tex" } } }""");

        Assert.Throws<InvalidDataException>(() => PenumbraModFolder.Read(dir.Path));
    }

    [Fact]
    public void FileSwaps_Mirror_TexTools_Only_With_Game_Data()
    {
        using var dir = new TempDir();
        dir.File("meta.json", """
            { "FileVersion": 4, "Name": "M", "DefaultData": {
              "Files": { "chara/a.tex": "a.tex" },
              "FileSwaps": { "chara/dest.tex": "chara\\src.tex", "chara/d2.tex": "chara/missing.tex" } } }
            """);

        var without = PenumbraModFolder.Read(dir.Path);
        Assert.Equal(["chara/a.tex"], without.Options[0].Files!.Keys);

        var with = PenumbraModFolder.Read(dir.Path, new FakeGame("chara/src.tex"));
        var files = with.Options[0].Files!;
        Assert.Equal(["chara/a.tex", "chara/src.tex"], files.Keys);
        var swap = Assert.IsType<PenumbraFileSwapSource>(files["chara/src.tex"]);
        Assert.Throws<ArgumentNullException>(() => swap.Read());
    }
}
