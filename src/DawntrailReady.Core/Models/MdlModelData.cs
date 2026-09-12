// Port of xivModdingFramework Models/DataContainers/MdlModelData.cs (TexTools commit 25b3ae72):
// EMeshFlags1/2/3 and MdlModelData with its Read/Write, same field order and widths.
namespace DawntrailReady.Core.Models;

[Flags]
public enum EMeshFlags1 : byte
{
    ShadowDisabled = 0x01,
    LightShadowDisabled = 0x02,
    WavingAnimationDisabled = 0x04,
    LightingReflectionEnabled = 0x08,
    Unknown10 = 0x10,
    RainOcclusionEnabled = 0x20,
    SnowOcclusionEnabled = 0x40,
    DustOcclusionEnabled = 0x80,
}

[Flags]
public enum EMeshFlags2 : byte
{
    HasBonelessParts = 0x01,
    EdgeGeometryEnabled = 0x02,
    ForceLodRangeEnabled = 0x04,
    ShadowMaskEnabled = 0x08,
    HasExtraMeshes = 0x10,
    EnableForceNonResident = 0x20,
    BgUvScrollEnabled = 0x40,
    Unknown80 = 0x80,
}

[Flags]
public enum EMeshFlags3 : byte
{
    Unknown01 = 0x01,
    UseMaterialChange = 0x02,
    UseCrestChange = 0x04,
    Unknown08 = 0x08,
    Unknown10 = 0x10,
    Unknown20 = 0x20,
    Unknown40 = 0x40,
    Unknown80 = 0x80,
}

/// <summary>
/// The 56-byte model header that follows the string block in an uncompressed .mdl.
/// </summary>
public class MdlModelData
{
    public float Radius { get; set; }

    /// <summary>The total number of meshes the model contains, all LoDs included.</summary>
    public short MeshCount { get; set; }

    public short AttributeCount { get; set; }

    public short MeshPartCount { get; set; }

    public short MaterialCount { get; set; }

    public short BoneCount { get; set; }

    /// <summary>The number of bone lists (usually one per LoD).</summary>
    public short BoneSetCount { get; set; }

    public short ShapeCount { get; set; }

    public short ShapePartCount { get; set; }

    public ushort ShapeDataCount { get; set; }

    public byte LoDCount { get; set; }

    public EMeshFlags1 Flags1 { get; set; }

    public ushort ElementIdCount { get; set; }

    public byte TerrainShadowMeshCount { get; set; }

    public EMeshFlags2 Flags2 { get; set; }

    public float ModelClipOutDistance { get; set; }

    public float ShadowClipOutDistance { get; set; }

    /// <summary>Bounding boxes for LoD0 Mesh0 parts for furniture/non-boned items with multiple parts.</summary>
    public ushort FurniturePartBoundingBoxCount { get; set; }

    public short TerrainShadowPartCount { get; set; }

    public EMeshFlags3 Flags3 { get; set; }

    public byte BgChangeMaterialIndex { get; set; }

    public byte BgCrestChangeMaterialIndex { get; set; }

    public byte NeckMorphTableSize { get; set; }

    public short BoneSetSize { get; set; }

    public short Unknown13 { get; set; }

    public short Patch72TableSize { get; set; }

    public short Unknown15 { get; set; }

    public short Unknown16 { get; set; }

    public short Unknown17 { get; set; }

    public static MdlModelData Read(BinaryReader br)
    {
        var modelData = new MdlModelData
        {
            Radius = br.ReadSingle(),

            MeshCount = br.ReadInt16(),
            AttributeCount = br.ReadInt16(),
            MeshPartCount = br.ReadInt16(),
            MaterialCount = br.ReadInt16(),

            BoneCount = br.ReadInt16(),
            BoneSetCount = br.ReadInt16(),

            ShapeCount = br.ReadInt16(),
            ShapePartCount = br.ReadInt16(),
            ShapeDataCount = br.ReadUInt16(),

            LoDCount = br.ReadByte(),

            Flags1 = (EMeshFlags1)br.ReadByte(),

            ElementIdCount = br.ReadUInt16(),
            TerrainShadowMeshCount = br.ReadByte(),

            Flags2 = (EMeshFlags2)br.ReadByte(),

            ModelClipOutDistance = br.ReadSingle(),
            ShadowClipOutDistance = br.ReadSingle(),

            FurniturePartBoundingBoxCount = br.ReadUInt16(),
            TerrainShadowPartCount = br.ReadInt16(),
            Flags3 = (EMeshFlags3)br.ReadByte(),

            BgChangeMaterialIndex = br.ReadByte(),
            BgCrestChangeMaterialIndex = br.ReadByte(),

            NeckMorphTableSize = br.ReadByte(),
            BoneSetSize = br.ReadInt16(),

            Unknown13 = br.ReadInt16(),
            Patch72TableSize = br.ReadInt16(),
            Unknown15 = br.ReadInt16(),
            Unknown16 = br.ReadInt16(),
            Unknown17 = br.ReadInt16()
        };

        return modelData;
    }

    public void Write(BinaryWriter br)
    {
        br.Write(Radius);
        br.Write(MeshCount);
        br.Write(AttributeCount);
        br.Write(MeshPartCount);
        br.Write(MaterialCount);
        br.Write(BoneCount);
        br.Write(BoneSetCount);
        br.Write(ShapeCount);
        br.Write(ShapePartCount);
        br.Write(ShapeDataCount);
        br.Write(LoDCount);
        br.Write((byte)Flags1);
        br.Write(ElementIdCount);
        br.Write(TerrainShadowMeshCount);
        br.Write((byte)Flags2);
        br.Write(ModelClipOutDistance);
        br.Write(ShadowClipOutDistance);
        br.Write(FurniturePartBoundingBoxCount);
        br.Write(TerrainShadowPartCount);
        br.Write((byte)Flags3);
        br.Write(BgChangeMaterialIndex);
        br.Write(BgCrestChangeMaterialIndex);
        br.Write(NeckMorphTableSize);
        br.Write(BoneSetSize);
        br.Write(Unknown13);
        br.Write(Patch72TableSize);
        br.Write(Unknown15);
        br.Write(Unknown16);
        br.Write(Unknown17);
    }
}
