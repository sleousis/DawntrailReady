// Port of the pieces of xivModdingFramework Models/FileTypes/Mdl.cs (TexTools commit 25b3ae72) that the v6
// upgrade uses: _MdlHeaderSize, _VertexDataHeaderSize, ReadBoundingBox, WriteBoundingBox.
using System.Numerics;

namespace DawntrailReady.Core.Models;

public static class Mdl
{
    public const int _MdlHeaderSize = 0x44; // 68 Decimal
    public const int _VertexDataHeaderSize = 0x88; // 136 Decimal

    public static List<Vector4> ReadBoundingBox(BinaryReader br)
    {
        var ret = new List<Vector4>();

        ret.Add(new Vector4(
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle()
        ));

        ret.Add(new Vector4(
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle()
        ));

        return ret;
    }

    // TexTools indexes SharpDX's Vector4 as bb[i][0..3]; X, Y, Z, W are the same components.
    public static void WriteBoundingBox(BinaryWriter bw, List<Vector4> bb)
    {
        bw.Write(BitConverter.GetBytes(bb[0].X));
        bw.Write(BitConverter.GetBytes(bb[0].Y));
        bw.Write(BitConverter.GetBytes(bb[0].Z));
        bw.Write(BitConverter.GetBytes(bb[0].W));

        bw.Write(BitConverter.GetBytes(bb[1].X));
        bw.Write(BitConverter.GetBytes(bb[1].Y));
        bw.Write(BitConverter.GetBytes(bb[1].Z));
        bw.Write(BitConverter.GetBytes(bb[1].W));
    }
}
