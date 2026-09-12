namespace DawntrailReady.Core.Tests.Models;

/// <summary>
/// Builds an uncompressed v5 .mdl laid out the way FastMdlv6Upgrade walks it. Every byte the upgrade must not
/// touch is a non-zero pattern, and stale v5 bone-list entries are 0xAB, so any stray write shows up.
/// The model header is written field by field here (not through MdlModelData) so the test does not trust the port.
/// </summary>
internal sealed class SyntheticV5Mdl
{
    public uint Version = 0x01000005;
    public ushort MeshCount = 1;
    public byte HeaderLodCount = 3;
    public float Radius = 2.5f;
    public short BoneCount = 3;
    public List<ushort[]> BoneSets = [[5, 7, 9]];
    public byte Flags2;
    public ushort ElementIdCount = 1;
    public short ShapeCount = 1;
    public short ShapePartCount = 1;
    public ushort ShapeDataCount = 2;
    public int PartBoneSetBytes = 8;
    public byte Padding = 3;

    public int ModelDataOffset { get; private set; }
    public int BoneSetStart { get; private set; }
    public int BoneBoxesStart { get; private set; }

    private int _seed;

    public byte[] Build()
    {
        _seed = 0;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // File header, 0x44 bytes.
        bw.Write(Version);
        bw.Write((uint)(0x88 * MeshCount)); // vertex declarations size
        bw.Write(0x1000u); // model data size
        bw.Write(MeshCount);
        bw.Write((ushort)1); // material count
        Filler(bw, 48); // vertex/index buffer offsets and sizes
        bw.Write(HeaderLodCount);
        bw.Write((byte)0x5A); // flags
        Filler(bw, 2);

        Filler(bw, 0x88 * MeshCount); // vertex declarations

        // String block: count, byte size, strings.
        bw.Write(2u);
        bw.Write(24u);
        Filler(bw, 24);

        // Model header, 56 bytes.
        ModelDataOffset = (int)ms.Position;
        bw.Write(Radius);
        bw.Write((short)MeshCount);
        bw.Write((short)1); // attributes
        bw.Write((short)1); // mesh parts
        bw.Write((short)1); // materials
        bw.Write(BoneCount);
        bw.Write((short)BoneSets.Count);
        bw.Write(ShapeCount);
        bw.Write(ShapePartCount);
        bw.Write(ShapeDataCount);
        bw.Write((byte)3); // LoD count
        bw.Write((byte)0x01); // Flags1
        bw.Write(ElementIdCount);
        bw.Write((byte)0); // terrain shadow meshes
        bw.Write(Flags2);
        bw.Write(100f); // model clip-out distance
        bw.Write(50f); // shadow clip-out distance
        bw.Write((ushort)0); // furniture part bounding boxes
        bw.Write((short)0); // terrain shadow parts
        bw.Write((byte)0x02); // Flags3
        bw.Write((byte)0x11);
        bw.Write((byte)0x22);
        bw.Write((byte)0);
        bw.Write((short)0x0AAA); // BoneSetSize, overwritten by the upgrade
        bw.Write((short)0x1313);
        bw.Write((short)0x7272);
        bw.Write((short)0x1515);
        bw.Write((short)0x1616);
        bw.Write((short)0x1717);

        Filler(bw, 32 * ElementIdCount);
        Filler(bw, 60 * 3); // LoD headers
        if ((Flags2 & 0x10) != 0)
        {
            Filler(bw, 60); // extra mesh info
        }
        Filler(bw, 36 * MeshCount); // meshes
        Filler(bw, 4 * 1); // attribute pointers
        Filler(bw, 16 * 1); // mesh parts
        Filler(bw, 4 * 1); // material pointers
        Filler(bw, 4 * BoneCount); // bone pointers

        BoneSetStart = (int)ms.Position;
        foreach (var set in BoneSets)
        {
            foreach (var bone in set)
            {
                bw.Write(bone);
            }
            for (var i = set.Length; i < 64; i++)
            {
                bw.Write((ushort)0xABAB);
            }
            bw.Write((uint)set.Length);
        }

        Filler(bw, (16 * ShapeCount) + (12 * ShapePartCount) + (4 * ShapeDataCount));
        bw.Write(PartBoneSetBytes);
        Filler(bw, PartBoneSetBytes);
        bw.Write(Padding);
        Filler(bw, Padding);
        Filler(bw, 4 * 32); // model, model-with-shadow, water, vertical-fog boxes

        BoneBoxesStart = (int)ms.Position;
        Filler(bw, 32 * BoneCount);

        Filler(bw, 64); // geometry
        bw.Flush();
        return ms.ToArray();
    }

    private void Filler(BinaryWriter bw, int count)
    {
        for (var i = 0; i < count; i++)
        {
            bw.Write((byte)((_seed % 251) + 1));
            _seed += 37;
        }
    }
}
