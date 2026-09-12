using System.Buffers.Binary;
using System.Numerics;
using DawntrailReady.Core.Models;

namespace DawntrailReady.Core.Tests.Models;

public class EndwalkerModelTests
{
    [Fact]
    public void FastMdlv6Upgrade_OneBoneSet_ChangesExactlyTheExpectedBytes()
    {
        var mdl = new SyntheticV5Mdl();
        var input = mdl.Build();
        var actual = (byte[])input.Clone();

        // v6 bone table: header {offset in dwords = 1, count = 3}, indices, pad short, zero fill to 132 bytes.
        var boneTable = new byte[132];
        new byte[] { 1, 0, 3, 0, 5, 0, 7, 0, 9, 0, 0, 0 }.CopyTo(boneTable, 0);

        Assert.True(EndwalkerModel.FastMdlv6Upgrade(actual));
        Assert.Equal(Expected(mdl, input, boneTable), actual);
    }

    [Fact]
    public void FastMdlv6Upgrade_TwoBoneSetsAndExtraMeshes_WritesDwordOffsetsPerSet()
    {
        var mdl = new SyntheticV5Mdl
        {
            MeshCount = 2,
            BoneCount = 4,
            BoneSets = [[5, 7, 9], [2, 4]],
            Flags2 = (byte)EMeshFlags2.HasExtraMeshes,
            Radius = -3.7f,
        };
        var input = mdl.Build();
        var actual = (byte[])input.Clone();

        // Headers {2, 3} and {3, 2}: set 0 starts 8 bytes after its header, set 1 12 bytes after its own.
        var boneTable = new byte[264];
        new byte[] { 2, 0, 3, 0, 3, 0, 2, 0, 5, 0, 7, 0, 9, 0, 0, 0, 2, 0, 4, 0 }.CopyTo(boneTable, 0);

        Assert.True(EndwalkerModel.FastMdlv6Upgrade(actual));
        Assert.Equal(Expected(mdl, input, boneTable), actual);
    }

    [Fact]
    public void FastMdlv6Upgrade_Version6_ReturnsFalseAndChangesNothing()
    {
        var input = new SyntheticV5Mdl { Version = 0x01000006 }.Build();
        var actual = (byte[])input.Clone();

        Assert.False(EndwalkerModel.FastMdlv6Upgrade(actual));
        Assert.Equal(input, actual);
    }

    public static TheoryData<string> SkippedModels => ["meshCount0", "boneSets0", "bones0"];

    [Theory]
    [MemberData(nameof(SkippedModels))]
    public void FastMdlv6Upgrade_SkippedByTexTools_ReturnsFalseAndChangesNothing(string kind)
    {
        var mdl = new SyntheticV5Mdl();
        switch (kind)
        {
            case "meshCount0": mdl.MeshCount = 0; break;
            case "boneSets0": mdl.BoneSets = []; break;
            case "bones0": mdl.BoneCount = 0; break;
        }
        var input = mdl.Build();
        var actual = (byte[])input.Clone();

        Assert.False(EndwalkerModel.FastMdlv6Upgrade(actual));
        Assert.Equal(input, actual);
    }

    [Fact]
    public void FastMdlv6Upgrade_SecondRunOnUpgradedFile_ChangesNothing()
    {
        var actual = new SyntheticV5Mdl().Build();
        Assert.True(EndwalkerModel.FastMdlv6Upgrade(actual));
        var once = (byte[])actual.Clone();

        Assert.False(EndwalkerModel.FastMdlv6Upgrade(actual));
        Assert.Equal(once, actual);
    }

    [Fact]
    public void FixModelLoD_SetsHeaderLodCountToOne()
    {
        var input = new SyntheticV5Mdl().Build();
        var actual = (byte[])input.Clone();
        var expected = (byte[])input.Clone();
        expected[64] = 1;

        Assert.True(EndwalkerModel.FixModelLoD(actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FixModelLoD_AlreadyOne_ReturnsFalse()
    {
        var input = new SyntheticV5Mdl { HeaderLodCount = 1 }.Build();
        var actual = (byte[])input.Clone();

        Assert.False(EndwalkerModel.FixModelLoD(actual));
        Assert.Equal(input, actual);
    }

    [Fact]
    public void MdlModelData_ReadThenWrite_IsByteIdenticalAnd56Bytes()
    {
        var mdl = new SyntheticV5Mdl { Flags2 = 0xFF };
        var file = mdl.Build();
        var original = file.AsSpan(mdl.ModelDataOffset, 56).ToArray();

        var data = MdlModelData.Read(new BinaryReader(new MemoryStream(original)));
        using var ms = new MemoryStream();
        data.Write(new BinaryWriter(ms));

        Assert.Equal(original, ms.ToArray());
        Assert.Equal(2.5f, data.Radius);
        Assert.Equal(3, data.BoneCount);
        Assert.Equal(1, data.BoneSetCount);
        Assert.Equal(3, data.LoDCount);
        Assert.Equal((EMeshFlags2)0xFF, data.Flags2);
        Assert.Equal((short)0x0AAA, data.BoneSetSize);
        Assert.Equal((short)0x1717, data.Unknown17);
    }

    [Fact]
    public void BoundingBox_WriteThenRead_RoundTrips()
    {
        var box = new List<Vector4> { new(-1.5f, -2f, -3f, 1f), new(4f, 5.25f, 6f, 1f) };
        using var ms = new MemoryStream();
        Mdl.WriteBoundingBox(new BinaryWriter(ms), box);
        Assert.Equal(32, ms.Length);

        ms.Position = 0;
        Assert.Equal(box, Mdl.ReadBoundingBox(new BinaryReader(ms)));
    }

    /// <summary>The input with exactly TexTools' edits applied, computed independently of the port.</summary>
    private static byte[] Expected(SyntheticV5Mdl mdl, byte[] input, byte[] boneTable)
    {
        var e = (byte[])input.Clone();

        // Version 5 -> 6 (low ushort only; the 0x0100 high half stays).
        e[0] = 6;
        e[1] = 0;

        // File header LoD count.
        e[64] = 1;

        // Model header: LoDCount at +22, BoneSetSize at +44.
        e[mdl.ModelDataOffset + 22] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(mdl.ModelDataOffset + 44), (short)(64 * mdl.BoneSets.Count));

        boneTable.CopyTo(e, mdl.BoneSetStart);

        // Every bone box becomes {-r, -r, -r, 1} {r, r, r, 1} with r = Radius / 20.
        var r = mdl.Radius / 20.0f;
        float[] box = [-r, -r, -r, 1f, r, r, r, 1f];
        for (var bone = 0; bone < mdl.BoneCount; bone++)
        {
            for (var i = 0; i < box.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(e.AsSpan(mdl.BoneBoxesStart + (bone * 32) + (i * 4)), box[i]);
            }
        }

        return e;
    }
}
