// Port of xivModdingFramework Mods/EndwalkerUpgrade.cs (TexTools commit 25b3ae72): FixModelLoD (lines 216-247),
// FastMdlv6Upgrade (282-476), and the MemoryStream + BinaryReader + BinaryWriter that UpdateEndwalkerModel
// (258-271) wraps around the uncompressed file.
// FixOldModel (a full re-import through TexTools' TTModel pipeline) is NOT PORTED by design.
using System.Numerics;

namespace DawntrailReady.Core.Models;

/// <summary>
/// TexTools' in-place v5 → v6 .mdl upgrade. The byte[] entry points edit the array through a fixed-size
/// MemoryStream exactly like TexTools: a truncated or malformed file throws (EndOfStreamException, or
/// NotSupportedException when a write would run past the end) and may leave the array partly written.
/// TexTools' UpdateEndwalkerModel lets that exception escape without saving the file, so callers must discard
/// the array when these throw.
/// </summary>
public static class EndwalkerModel
{
    /// <summary>
    /// Upgrades an uncompressed v5 .mdl to v6 in place. Returns false with the array untouched for version != 5,
    /// meshCount == 0, BoneSetCount == 0 or BoneCount == 0.
    /// </summary>
    public static bool FastMdlv6Upgrade(byte[] mdl)
    {
        using var ms = new MemoryStream(mdl);
        using var br = new BinaryReader(ms);
        using var bw = new BinaryWriter(ms);
        return FastMdlv6Upgrade(br, bw);
    }

    /// <summary>
    /// Sets the header LoD count to 1 unless it already is 1. TexTools defines this but never calls it.
    /// </summary>
    public static bool FixModelLoD(byte[] mdl)
    {
        using var ms = new MemoryStream(mdl);
        using var br = new BinaryReader(ms);
        using var bw = new BinaryWriter(ms);
        return FixModelLoD(br, bw);
    }

    public static bool FixModelLoD(BinaryReader br, BinaryWriter bw, long offset = -1)
    {
        if (offset < 0)
        {
            offset = br.BaseStream.Position;
        }

        br.BaseStream.Seek(offset, SeekOrigin.Begin);
        bw.BaseStream.Seek(offset, SeekOrigin.Begin);

        var version = br.ReadUInt16();

        br.BaseStream.Seek(offset + 12, SeekOrigin.Begin);
        var meshCount = br.ReadUInt16();

        br.BaseStream.Seek(offset + 64, SeekOrigin.Begin);

        var lodOffset = br.BaseStream.Position;
        var lods = br.ReadByte();

        if (lods == 1)
        {
            return false;
        }

        // Write LoD count.
        bw.BaseStream.Seek(lodOffset, SeekOrigin.Begin);
        bw.Write((byte)1);

        return true;
    }

    public static bool FastMdlv6Upgrade(BinaryReader br, BinaryWriter bw, long offset = -1)
    {
        if (offset < 0)
        {
            offset = br.BaseStream.Position;
        }

        br.BaseStream.Seek(offset, SeekOrigin.Begin);
        bw.BaseStream.Seek(offset, SeekOrigin.Begin);

        var version = br.ReadUInt16();
        if (version != 5)
        {
            return false;
        }

        br.BaseStream.Seek(offset + 12, SeekOrigin.Begin);
        var meshCount = br.ReadUInt16();

        if (meshCount == 0)
        {
            return false;
        }

        br.BaseStream.Seek(offset + 64, SeekOrigin.Begin);
        var lodOffset = br.BaseStream.Position;
        var lods = br.ReadByte();

        var endOfVertexHeaders = offset + Mdl._MdlHeaderSize + (Mdl._VertexDataHeaderSize * meshCount);

        br.BaseStream.Seek(endOfVertexHeaders + 4, SeekOrigin.Begin);
        var pathBlockSize = br.ReadUInt32();

        br.ReadBytes((int)pathBlockSize);

        // Mesh data block.
        var mdlDataOffset = br.BaseStream.Position;
        var mdlData = MdlModelData.Read(br);

        if (mdlData.BoneSetCount == 0 || mdlData.BoneCount == 0)
        {
            // TexTools: not sure how to update boneless meshes to v6 yet, so don't upgrade for safety.
            return false;
        }

        br.ReadBytes(mdlData.ElementIdCount * 32);

        // LoD Headers
        br.ReadBytes(60 * 3);

        if ((mdlData.Flags2 & EMeshFlags2.HasExtraMeshes) != 0)
        {
            // Extra Mesh Info Block.
            br.ReadBytes(60);
        }

        // Mesh Group headers.
        br.ReadBytes(36 * meshCount);

        // Attribute pointers.
        br.ReadBytes(4 * mdlData.AttributeCount);

        // Mesh Part information.
        br.ReadBytes(16 * mdlData.MeshPartCount);

        // Show Mesh Part Information.
        br.ReadBytes(12 * mdlData.TerrainShadowPartCount);

        // Material Pointers.
        br.ReadBytes(4 * mdlData.MaterialCount);

        // Bone Pointers.
        br.ReadBytes(4 * mdlData.BoneCount);

        var bonesetStart = br.BaseStream.Position;
        var boneSets = new List<(long Offset, ushort BoneCount, byte[] data)>();
        for (int i = 0; i < mdlData.BoneSetCount; i++)
        {
            // Bone List
            var data = br.ReadBytes(64 * 2);

            // Bone List Size.
            var countOffset = br.BaseStream.Position;
            var count = br.ReadUInt32();

            boneSets.Add((countOffset, (ushort)count, data));
        }

        // Write version information.
        bw.BaseStream.Seek(offset, SeekOrigin.Begin);
        bw.Write((ushort)6);

        // Write LoD count.
        bw.BaseStream.Seek(lodOffset, SeekOrigin.Begin);
        bw.Write((byte)1);

        // Write bone set size.
        short boneSetSize = (short)(64 * mdlData.BoneSetCount);
        mdlData.BoneSetSize = (short)boneSetSize;
        mdlData.LoDCount = 1;
        bw.BaseStream.Seek(mdlDataOffset, SeekOrigin.Begin);
        mdlData.Write(bw);

        // Upgrade bone sets to v6 format.
        bw.BaseStream.Seek(bonesetStart, SeekOrigin.Begin);
        List<long> headerOffsets = new List<long>();
        foreach (var bs in boneSets)
        {
            headerOffsets.Add(bw.BaseStream.Position);
            bw.Write((short)0);
            bw.Write((short)bs.BoneCount);
        }

        var idx = 0;
        foreach (var bs in boneSets)
        {
            var headerOffset = headerOffsets[idx];
            var distance = (short)((bw.BaseStream.Position - headerOffset) / 4);

            bw.Write(bs.data, 0, bs.BoneCount * 2);

            if (bs.BoneCount % 2 != 0)
            {
                // Expected Padding
                bw.Write((short)0);
            }

            var pos = bw.BaseStream.Position;
            bw.BaseStream.Seek(headerOffset, SeekOrigin.Begin);
            bw.Write(distance);
            bw.BaseStream.Seek(pos, SeekOrigin.Begin);

            idx++;
        }

        // Net size of old bone sets
        var end = bonesetStart + (((64 * 2) + 4) * mdlData.BoneSetCount);
        while (bw.BaseStream.Position < end)
        {
            // Fill out the remainder of the block with 0s.
            bw.Write((byte)0);
        }

        var endOfBoneSet = bw.BaseStream.Position;

        // Shape Data is next.
        var shpCount = mdlData.ShapeCount;
        var shpParts = mdlData.ShapePartCount;
        var shpIndices = mdlData.ShapeDataCount;

        var endOfShapeHeaders = endOfBoneSet + (shpCount * 16);
        var endOfShapePartHeaders = endOfShapeHeaders + (shpParts * 12);
        var endOfShapeIndices = endOfShapePartHeaders + (shpIndices * 4);

        // Part Bone Sets
        br.BaseStream.Seek(endOfShapeIndices, SeekOrigin.Begin);
        var partBoneSets = br.ReadInt32();
        var endOfPartBones = br.BaseStream.Position + (partBoneSets);

        // Padding
        br.BaseStream.Seek(endOfPartBones, SeekOrigin.Begin);
        var padding = br.ReadByte();
        br.BaseStream.Seek(br.BaseStream.Position + padding, SeekOrigin.Begin);

        // Bounding Boxes
        var baseBox = Mdl.ReadBoundingBox(br);
        var mdlBox = Mdl.ReadBoundingBox(br);
        var waterBox = Mdl.ReadBoundingBox(br);
        var shadowBox = Mdl.ReadBoundingBox(br);

        const float _Divisor = 20.0f;
        var min = -1 * (mdlData.Radius / _Divisor);
        var max = (mdlData.Radius / _Divisor);
        var bb = new List<Vector4>()
        {
            new Vector4(min, min, min, 1.0f),
            new Vector4(max, max, max, 1.0f),
        };

        // Write new bone bounding boxes.
        bw.BaseStream.Seek(br.BaseStream.Position, SeekOrigin.Begin);
        for (int i = 0; i < mdlData.BoneCount; i++)
        {
            Mdl.WriteBoundingBox(bw, bb);
        }

        return true;
    }
}
